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
}
