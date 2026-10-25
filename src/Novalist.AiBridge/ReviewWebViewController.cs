using System.Text.Json;
using Novalist.Sdk.Hooks;

namespace Novalist.AiBridge;

internal sealed class ReviewWebViewController : IWebViewController
{
    private readonly ProjectGateway _gateway;
    public ReviewWebViewController(ProjectGateway gateway) => _gateway = gateway;
#pragma warning disable CS0067
    public event Action<string>? MessagePosted;
#pragma warning restore CS0067

    public async Task<string?> OnMessageAsync(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("action", out var a) || a.ValueKind != JsonValueKind.String)
                throw new ArgumentException("Invalid review request.");
            var action = a.GetString();
            object result = action switch
            {
                "list" => await _gateway.PendingEditorialAsync(),
                "approve" => await _gateway.ApproveEditorialAsync(RequiredId(root)),
                "reject" => await _gateway.RejectEditorialAsync(RequiredId(root)),
                _ => throw new ArgumentException("Unsupported review action.")
            };
            return JsonSerializer.Serialize(new { action, success = true, result });
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { success = false, error = ex.Message });
        }
    }
    private static string RequiredId(JsonElement root)
    {
        if (!root.TryGetProperty("id", out var id) || id.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(id.GetString()) || id.GetString()!.Length > 64)
            throw new ArgumentException("A valid proposal ID is required.");
        return id.GetString()!;
    }
}
