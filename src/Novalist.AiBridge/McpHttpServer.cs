using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Novalist.AiBridge;

/// <summary>Stateless MCP over Streamable HTTP, bound exclusively to local loopback.</summary>
internal sealed class McpHttpServer : IDisposable
{
    public const string Endpoint = "http://127.0.0.1:8765/mcp/";
    private const int MaxRequestBytes = 64 * 1024;
    private readonly ProjectGateway _project;
    private readonly string _settingsDirectory;
    private readonly HttpListener _listener = new();
    private readonly CancellationTokenSource _stop = new();
    private string _token = "";
    private Task? _loop;
    private bool _disposed;

    public McpHttpServer(ProjectGateway project, string settingsDirectory)
    {
        _project = project;
        _settingsDirectory = settingsDirectory;
        _listener.Prefixes.Add("http://127.0.0.1:8765/");
    }

    public void Start()
    {
        _token = LoadOrCreateToken();
        _listener.Start();
        _loop = ListenAsync();
    }

    private string LoadOrCreateToken()
    {
        Directory.CreateDirectory(_settingsDirectory);
        var path = Path.Combine(_settingsDirectory, "bridge-token.txt");
        if (File.Exists(path))
        {
            var existing = File.ReadAllText(path).Trim();
            if (existing.Length < 32) throw new InvalidOperationException("Existing bridge token is invalid.");
            return existing;
        }
        var key = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var options = new FileStreamOptions
        {
            Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None,
            UnixCreateMode = OperatingSystem.IsLinux() || OperatingSystem.IsMacOS()
                ? UnixFileMode.UserRead | UnixFileMode.UserWrite : null
        };
        using (var stream = new FileStream(path, options))
        using (var writer = new StreamWriter(stream, new UTF8Encoding(false))) writer.Write(key + "\n");
        return key;
    }

    private async Task ListenAsync()
    {
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                HttpListenerContext context;
                try { context = await _listener.GetContextAsync().WaitAsync(_stop.Token).ConfigureAwait(false); }
                catch (HttpListenerException) when (_stop.IsCancellationRequested) { break; }
                catch (OperationCanceledException) { break; }
                _ = Task.Run(() => HandleAsync(context), _stop.Token);
            }
        }
        catch (Exception) when (_stop.IsCancellationRequested) { /* Expected on shutdown. */ }
        catch (Exception) { /* Novalist remains usable if the bridge stops. */ }
    }

    private async Task HandleAsync(HttpListenerContext ctx)
    {
        try
        {
            var request = ctx.Request;
            var path = request.Url?.AbsolutePath ?? "";
            // Defend browser-based access via DNS rebinding to localhost.
            // This also applies to the intentionally unauthenticated health and
            // setup pages, not just the protected MCP endpoint.
            if (!string.Equals(request.Headers["Host"], "127.0.0.1:8765", StringComparison.OrdinalIgnoreCase))
            {
                await RespondAsync(ctx, 403, new { error = "Host denied" });
                return;
            }
            if (path is "/setup" or "/setup/")
            {
                // Old link: the token is no longer available through an HTTP setup page.
                if (request.HttpMethod != "GET")
                {
                    await RespondAsync(ctx, 405, new { error = "Use GET" });
                    return;
                }
                await RespondHtmlAsync(ctx,
                    """
                    <!doctype html><html lang="en"><head><meta charset="utf-8"><title>AI Bridge settings moved</title></head>
                    <body><h1>AI Bridge settings moved</h1>
                    <p>Open Novalist &rarr; World &rarr; From extensions &rarr; AI Bridge Settings.</p>
                    <p>Reveal and copy the connection token there. There is no pairing code or separate browser page.</p>
                    </body></html>
                    """,
                    "default-src 'none'; base-uri 'none'; frame-ancestors 'none'");
                return;
            }
            if (path == "/health" && request.HttpMethod == "GET")
            {
                await RespondAsync(ctx, 200, new { name = "Novalist AI Bridge", status = "running", authenticated = false });
                return;
            }
            if (path is not ("/mcp" or "/mcp/"))
            {
                await RespondAsync(ctx, 404, new { error = "Not found" });
                return;
            }
            // Prevent websites from using browser-originated requests to reach the local MCP bridge.
            var origin = request.Headers["Origin"];
            if (!string.IsNullOrWhiteSpace(origin) && !TrustedOrigin(origin))
            {
                await RespondAsync(ctx, 403, new { error = "Origin denied" });
                return;
            }
            if (!Authenticated(request.Headers["Authorization"]))
            {
                ctx.Response.AddHeader("WWW-Authenticate", "Bearer");
                await RespondAsync(ctx, 401, new { error = "Bearer token required" });
                return;
            }
            if (request.HttpMethod == "DELETE")
            {
                // Stateless transport: no per-client sessions to delete.
                ctx.Response.StatusCode = 204;
                ctx.Response.Close();
                return;
            }
            if (request.HttpMethod != "POST")
            {
                ctx.Response.Headers["Allow"] = "POST, DELETE";
                await RespondAsync(ctx, 405, new { error = "Use POST for Streamable HTTP" });
                return;
            }
            if (request.ContentLength64 > MaxRequestBytes)
            {
                await RespondAsync(ctx, 413, new { error = "Request too large" });
                return;
            }
            using var input = new MemoryStream();
            var chunk = new byte[8192];
            while (true)
            {
                var length = await request.InputStream.ReadAsync(chunk, _stop.Token).ConfigureAwait(false);
                if (length == 0) break;
                if (input.Length + length > MaxRequestBytes)
                {
                    await RespondAsync(ctx, 413, new { error = "Request too large" });
                    return;
                }
                input.Write(chunk, 0, length);
            }
            using var doc = JsonDocument.Parse(input.ToArray());
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                await RespondAsync(ctx, 400, RpcError(null, -32600, "Expected JSON-RPC object"));
                return;
            }
            var root = doc.RootElement;
            var hasId = root.TryGetProperty("id", out var id);
            var idValue = hasId ? JsonSerializer.Deserialize<object>(id.GetRawText()) : null;
            var method = Text(root, "method");
            if (string.IsNullOrWhiteSpace(method))
            {
                await RespondAsync(ctx, 400, RpcError(idValue, -32600, "Missing JSON-RPC method"));
                return;
            }
            // MCP notifications must not generate an RPC response.
            if (!hasId)
            {
                ctx.Response.StatusCode = 202;
                ctx.Response.Close();
                return;
            }
            object response;
            try
            {
                var data = await DispatchAsync(method, root).ConfigureAwait(false);
                response = new { jsonrpc = "2.0", id = idValue, result = data };
            }
            catch (NotSupportedException ex) { response = RpcError(idValue, -32601, ex.Message); }
            catch (ArgumentException ex) { response = RpcError(idValue, -32602, ex.Message); }
            catch (Exception ex) { response = RpcError(idValue, -32603, ex.Message); }
            await RespondAsync(ctx, 200, response);
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { TryClose(ctx); }
        catch (Exception) { TryClose(ctx); }
    }

    private static object RpcError(object? id, int code, string message) => new
    {
        jsonrpc = "2.0", id, error = new { code, message }
    };

    private async Task<object> DispatchAsync(string method, JsonElement root)
    {
        switch (method)
        {
            case "initialize":
                return new
                {
                    protocolVersion = "2025-06-18",
                    capabilities = new { tools = new { listChanged = false } },
                    serverInfo = new { name = "novalist-ai-bridge", version = "0.5.0" },
                    instructions = "Read-only access to the currently open Novalist book and draft. Prefer bounded get_scene_excerpt and get_scene_stats over large get_scene responses. Use search_codex_context and get_codex_context for scene-filtered lore; use find_mentions to cite manuscript evidence. Inactive and export-excluded scenes are withheld."
                };
            case "ping": return new { };
            case "tools/list": return new { tools = Tools() };
            case "tools/call":
            {
                if (!root.TryGetProperty("params", out var parameters))
                    throw new ArgumentException("Missing params");
                var name = Text(parameters, "name");
                var args = parameters.TryGetProperty("arguments", out var a) ? a : default;
                try
                {
                    object result = name switch
                    {
                        "sdk_diagnostics" => _project.SdkDiagnostics(),
                        "book_info" => await _project.BookInfoAsync(),
                        "list_chapters" => await _project.ListChaptersAsync(),
                        "list_scenes" => await _project.ListScenesAsync(PositiveInt(args, "chapter")),
                        "get_scene" => await _project.GetSceneAsync(PositiveInt(args, "chapter"), PositiveInt(args, "scene")),
                        "get_scene_metadata" => await _project.GetSceneMetadataAsync(PositiveInt(args, "chapter"), PositiveInt(args, "scene")),
                        "get_scene_stats" => await _project.GetSceneStatsAsync(PositiveInt(args, "chapter"), PositiveInt(args, "scene")),
                        "get_scene_excerpt" => await _project.GetSceneExcerptAsync(PositiveInt(args, "chapter"), PositiveInt(args, "scene"), OptionalInt(args, "start", 0), OptionalInt(args, "length", 2000), Text(args, "expectedHash")),
                        "get_codex_context" => await _project.GetCodexContextAsync(PositiveInt(args, "chapter"), PositiveInt(args, "scene"), OptionalInt(args, "offset", 0), OptionalInt(args, "limit", 5)),
                        "get_codex_section" => await _project.GetCodexSectionAsync(PositiveInt(args, "chapter"), PositiveInt(args, "scene"), RequiredText(args, "entryId"), RequiredNonnegativeInt(args, "sectionIndex"), OptionalInt(args, "start", 0), OptionalInt(args, "length", 2000), Text(args, "expectedHash")),
                        "search_scenes" => await _project.SearchScenesAsync(RequiredText(args, "query"), OptionalInt(args, "limit", 10)),
                        "find_mentions" => await _project.FindMentionsAsync(RequiredText(args, "query"), OptionalInt(args, "offset", 0), OptionalInt(args, "limit", 10), OptionalInt(args, "chapterStart", 1), OptionalInt(args, "chapterEnd", 0), OptionalBool(args, "wholeWord", false)),
                        "search_codex_context" => await _project.SearchCodexContextAsync(PositiveInt(args, "chapter"), PositiveInt(args, "scene"), RequiredText(args, "query"), OptionalInt(args, "offset", 0), OptionalInt(args, "limit", 10)),
                        _ => throw new ArgumentException("Unknown tool: " + name)
                    };
                    return ToolResult(result, false);
                }
                catch (Exception e) { return ToolResult(new { error = e.Message }, true); }
            }
            default: throw new NotSupportedException("Unknown MCP method: " + method);
        }
    }

    private static object ToolResult(object result, bool isError) => new
    {
        content = new[] { new { type = "text", text = JsonSerializer.Serialize(result) } },
        isError
    };

    private static object[] Tools() =>
    [
        new
        {
            name = "sdk_diagnostics",
            description = "Show read-only SDK method names and parameter types to diagnose compatibility; never includes manuscript content or secrets.",
            inputSchema = new { type = "object", properties = new { }, additionalProperties = false }
        },
        new
        {
            name = "book_info",
            description = "Read metadata for Novalist's currently open book and draft.",
            inputSchema = new { type = "object", properties = new { }, additionalProperties = false }
        },
        new
        {
            name = "list_chapters",
            description = "List chapters in the active draft in manuscript order, with 1-based chapter numbers.",
            inputSchema = new { type = "object", properties = new { }, additionalProperties = false }
        },
        new
        {
            name = "list_scenes",
            description = "List visible scenes in one chapter in active draft order; inactive and excluded scenes are skipped.",
            inputSchema = new
            {
                type = "object", properties = new { chapter = new { type = "integer", minimum = 1 } },
                required = new[] { "chapter" }, additionalProperties = false
            }
        },
        new
        {
            name = "get_scene",
            description = "Read the current saved text of a visible scene, by 1-based chapter and scene number.",
            inputSchema = new
            {
                type = "object",
                properties = new { chapter = new { type = "integer", minimum = 1 }, scene = new { type = "integer", minimum = 1 } },
                required = new[] { "chapter", "scene" }, additionalProperties = false
            }
        },
        new
        {
            name = "get_scene_metadata",
            description = "Get saved scene metadata without loading prose.",
            inputSchema = new { type = "object", properties = new { chapter = new { type = "integer", minimum = 1 }, scene = new { type = "integer", minimum = 1 } }, required = new[] { "chapter", "scene" }, additionalProperties = false }
        },
        new
        {
            name = "get_scene_stats",
            description = "Word count, character count, hash and short beginning/end scene samples.",
            inputSchema = new { type = "object", properties = new { chapter = new { type = "integer", minimum = 1 }, scene = new { type = "integer", minimum = 1 } }, required = new[] { "chapter", "scene" }, additionalProperties = false }
        },
        new
        {
            name = "get_scene_excerpt",
            description = "Read up to 4000 UTF-16 characters of scene text at a specified offset; continue using nextStart and contentHash.",
            inputSchema = new { type = "object", properties = new {
                chapter = new { type = "integer", minimum = 1 }, scene = new { type = "integer", minimum = 1 },
                start = new { type = "integer", minimum = 0 }, length = new { type = "integer", minimum = 1, maximum = 4000 },
                expectedHash = new { type = "string" } }, required = new[] { "chapter", "scene" }, additionalProperties = false }
        },
        new
        {
            name = "get_codex_context",
            description = "List only Novalist-approved, AI-visible Codex context entries for a specific scene.",
            inputSchema = new { type = "object", properties = new {
                chapter = new { type = "integer", minimum = 1 }, scene = new { type = "integer", minimum = 1 },
                offset = new { type = "integer", minimum = 0 }, limit = new { type = "integer", minimum = 1, maximum = 10 } },
                required = new[] { "chapter", "scene" }, additionalProperties = false }
        },
        new
        {
            name = "get_codex_section",
            description = "Page through an allowed AI-visible Codex section in a specific scene context.",
            inputSchema = new { type = "object", properties = new {
                chapter = new { type = "integer", minimum = 1 }, scene = new { type = "integer", minimum = 1 },
                entryId = new { type = "string", minLength = 1 }, sectionIndex = new { type = "integer", minimum = 0 },
                start = new { type = "integer", minimum = 0 }, length = new { type = "integer", minimum = 1, maximum = 4000 },
                expectedHash = new { type = "string" } },
                required = new[] { "chapter", "scene", "entryId", "sectionIndex" }, additionalProperties = false }
        },
        new
        {
            name = "find_mentions",
            description = "Find all literal mentions across visible saved scenes, with source chapter/scene, positions, surrounding excerpts, scene content hash and pagination; useful for continuity evidence.",
            inputSchema = new { type = "object", properties = new {
                query = new { type = "string", minLength = 2, maxLength = 120 },
                offset = new { type = "integer", minimum = 0 },
                limit = new { type = "integer", minimum = 1, maximum = 20 },
                chapterStart = new { type = "integer", minimum = 1 },
                chapterEnd = new { type = "integer", minimum = 0 },
                wholeWord = new { type = "boolean" } },
                required = new[] { "query" }, additionalProperties = false }
        },
        new
        {
            name = "search_codex_context",
            description = "Search only AI-eligible Codex entries and sections for one scene using Novalist's inclusion and withholding policy. Returns matching names, section references and short previews.",
            inputSchema = new { type = "object", properties = new {
                chapter = new { type = "integer", minimum = 1 }, scene = new { type = "integer", minimum = 1 },
                query = new { type = "string", minLength = 2, maxLength = 120 },
                offset = new { type = "integer", minimum = 0 },
                limit = new { type = "integer", minimum = 1, maximum = 20 } },
                required = new[] { "chapter", "scene", "query" }, additionalProperties = false }
        },
        new
        {
            name = "search_scenes",
            description = "Case-insensitive literal search of visible manuscript scenes; returns short cited excerpts and chapter/scene numbers.",
            inputSchema = new
            {
                type = "object",
                properties = new { query = new { type = "string", minLength = 2, maxLength = 120 }, limit = new { type = "integer", minimum = 1, maximum = 25 } },
                required = new[] { "query" }, additionalProperties = false
            }
        }
    ];

    private static string Text(JsonElement obj, string key)
    {
        return obj.ValueKind == JsonValueKind.Object && obj.TryGetProperty(key, out var v)
            && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
    }
    private static string RequiredText(JsonElement obj, string key)
    {
        var text = Text(obj, key);
        if (string.IsNullOrWhiteSpace(text)) throw new ArgumentException("Missing " + key);
        return text;
    }
    private static int RequiredNonnegativeInt(JsonElement obj, string key)
    {
        if (obj.ValueKind != JsonValueKind.Object || !obj.TryGetProperty(key, out var v)
            || v.ValueKind != JsonValueKind.Number || !v.TryGetInt32(out var n) || n < 0)
            throw new ArgumentException("Expected non-negative integer for " + key);
        return n;
    }
    private static int PositiveInt(JsonElement obj, string key)
    {
        if (obj.ValueKind != JsonValueKind.Object || !obj.TryGetProperty(key, out var v)
            || !v.TryGetInt32(out var n) || n < 1)
            throw new ArgumentException("Expected positive integer for " + key);
        return n;
    }
    private static int OptionalInt(JsonElement obj, string key, int fallback)
    {
        return obj.ValueKind == JsonValueKind.Object && obj.TryGetProperty(key, out var v)
            && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n) ? n : fallback;
    }
    private static bool OptionalBool(JsonElement obj, string key, bool fallback)
    {
        return obj.ValueKind == JsonValueKind.Object && obj.TryGetProperty(key, out var v)
            && (v.ValueKind == JsonValueKind.True || v.ValueKind == JsonValueKind.False)
            ? v.GetBoolean() : fallback;
    }
    // Only the Novalist-hosted extension view's SDK message controller can request this;
    // the HTTP endpoints never expose the token.
    internal string GetBearerValue()
    {
        if (_disposed || string.IsNullOrEmpty(_token) || !_listener.IsListening)
            throw new InvalidOperationException("AI Bridge is not running.");
        return "Bearer " + _token;
    }

    private bool Authenticated(string? authorization)
    {
        if (authorization is null || !authorization.StartsWith("Bearer ", StringComparison.Ordinal)) return false;
        var supplied = Encoding.UTF8.GetBytes(authorization[7..]);
        var expected = Encoding.UTF8.GetBytes(_token);
        return supplied.Length == expected.Length && CryptographicOperations.FixedTimeEquals(supplied, expected);
    }
    private static bool TrustedOrigin(string origin)
    {
        return Uri.TryCreate(origin, UriKind.Absolute, out var uri)
            && uri.Scheme is "http" or "https"
            && (uri.Host == "localhost" || uri.Host == "127.0.0.1" || uri.Host == "[::1]" || uri.Host == "::1");
    }
    private static async Task RespondAsync(HttpListenerContext context, int status, object payload)
    {
        var response = context.Response;
        response.StatusCode = status;
        response.ContentType = "application/json; charset=utf-8";
        response.Headers["Cache-Control"] = "no-store";
        response.Headers["X-Content-Type-Options"] = "nosniff";
        response.Headers["Referrer-Policy"] = "no-referrer";
        response.Headers["X-Frame-Options"] = "DENY";
        response.Headers["Cross-Origin-Resource-Policy"] = "same-origin";
        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload);
        response.ContentLength64 = bytes.Length;
        await response.OutputStream.WriteAsync(bytes).ConfigureAwait(false);
        response.Close();
    }
    private static async Task RespondHtmlAsync(HttpListenerContext context, string html, string csp)
    {
        var response = context.Response;
        response.StatusCode = 200;
        response.ContentType = "text/html; charset=utf-8";
        response.Headers["Cache-Control"] = "no-store";
        response.Headers["Content-Security-Policy"] = csp;
        response.Headers["Referrer-Policy"] = "no-referrer";
        response.Headers["X-Content-Type-Options"] = "nosniff";
        response.Headers["X-Frame-Options"] = "DENY";
        response.Headers["Cross-Origin-Resource-Policy"] = "same-origin";
        var bytes = Encoding.UTF8.GetBytes(html);
        response.ContentLength64 = bytes.Length;
        await response.OutputStream.WriteAsync(bytes).ConfigureAwait(false);
        response.Close();
    }

    private static void TryClose(HttpListenerContext context) { try { context.Response.Close(); } catch { } }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _stop.Cancel();
        if (_listener.IsListening) _listener.Stop();
        _listener.Close();
        _stop.Dispose();
    }
}
