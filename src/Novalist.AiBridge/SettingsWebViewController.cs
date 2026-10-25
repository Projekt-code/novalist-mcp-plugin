using System.Text.Json;
using Novalist.Sdk.Hooks;

namespace Novalist.AiBridge;

/// <summary>
/// Handles only the Novalist-owned AI Bridge Settings webview. Secrets are
/// returned in response to explicit clicks in that trusted extension view,
/// never through an unauthenticated localhost HTTP endpoint or toast.
/// </summary>
internal sealed class SettingsWebViewController : IWebViewController
{
    private readonly Func<string?> _bearerValue;

    public SettingsWebViewController(Func<string?> bearerValue) =>
        _bearerValue = bearerValue;

    // The SDK requires this optional event for asynchronous pushes. We only
    // issue replies to direct user requests, so there is nothing to push.
#pragma warning disable CS0067
    public event Action<string>? MessagePosted;
#pragma warning restore CS0067

    public Task<string?> OnMessageAsync(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object ||
                !doc.RootElement.TryGetProperty("action", out var requested) ||
                requested.ValueKind != JsonValueKind.String)
                return Reply(new { action = "error", message = "Invalid settings request." });

            var action = requested.GetString();
            if (action == "status")
                return Reply(new { action = "status", ready = !string.IsNullOrEmpty(_bearerValue()) });

            if (action is not ("reveal" or "copy"))
                return Reply(new { action = "error", message = "Unknown settings action." });

            // No token is returned automatically when the panel is opened.
            // Both requests are triggered by the panel's actual buttons.
            var bearer = _bearerValue();
            if (string.IsNullOrWhiteSpace(bearer))
                return Reply(new { action = "error", message = "The MCP bridge is not running." });

            return Reply(new { action, value = bearer });
        }
        catch (JsonException)
        {
            return Reply(new { action = "error", message = "Invalid settings request." });
        }
        catch (Exception)
        {
            // Don't leak local file paths, internal exceptions, or token values.
            return Reply(new { action = "error", message = "The MCP bridge is not available." });
        }
    }

    private static Task<string?> Reply(object message) =>
        Task.FromResult<string?>(JsonSerializer.Serialize(message));
}
