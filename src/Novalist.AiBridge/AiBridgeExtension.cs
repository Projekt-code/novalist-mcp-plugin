using Novalist.Sdk;
using Novalist.Sdk.Services;
using Novalist.Sdk.Hooks;

namespace Novalist.AiBridge;

/// <summary>Runs a loopback-only MCP endpoint for the currently open Novalist project.</summary>
public sealed class AiBridgeExtension : IExtension, IWebViewContributor
{
    private McpHttpServer? _server;

    public string Id => "com.whscott.novalist-ai-bridge";
    public string DisplayName => "Novalist AI Bridge";
    public string Description => "Read-only manuscript tools for local MCP clients.";
    public string Version => "0.5.0";
    public string Author => "W.H. Scott";

    public void Initialize(IHostServices host)
    {
        try
        {
            _server = new McpHttpServer(new ProjectGateway(host), host.GetExtensionSettingsPath(Id));
            _server.Start();
            host.ShowNotification("Novalist AI Bridge: local MCP endpoint started on port 8765.");
        }
        catch (Exception e)
        {
            _server?.Dispose();
            _server = null;
            // Fail closed: no external access if configuration or binding fails.
            host.ShowNotification($"Novalist AI Bridge could not start: {e.Message}");
        }
    }

    // The host only creates this controller for our own view.
    public IWebViewController? CreateController(string viewKey) =>
        viewKey == "com.whscott.novalist-ai-bridge.settings"
            ? new SettingsWebViewController(() => _server?.GetBearerValue())
            : null;

    public void Shutdown()
    {
        _server?.Dispose();
        _server = null;
    }
}
