namespace Copilot.Ai;

/// <summary>
/// Model access settings. Models are pinned to dated snapshots — changing one is a
/// deliberate, evaluated config change, never an implicit upgrade.
/// </summary>
public sealed class AiOptions
{
    public const string SectionName = "OpenAi";

    /// <summary>From user-secrets (dev) or Key Vault (prod), never appsettings.json.</summary>
    public string ApiKey { get; set; } = "";

    public string DraftingModel { get; set; } = "";

    /// <summary>
    /// Changing this forces a full reindex — every stored vector was produced by it — so it
    /// is a deliberate, versioned decision rather than an upgrade.
    /// </summary>
    public string EmbeddingModel { get; set; } = "";

    /// <summary>Must match the index's vector field, or every query fails at search time.</summary>
    public int EmbeddingDimensions { get; set; } = 1536;
}
