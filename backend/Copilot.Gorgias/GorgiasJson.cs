using System.Text.Json;

namespace Copilot.Gorgias;

public static class GorgiasJson
{
    /// <summary>Gorgias payloads are snake_case; unknown fields (the huge integrations blob) are skipped.</summary>
    public static JsonSerializerOptions Options { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
    };
}
