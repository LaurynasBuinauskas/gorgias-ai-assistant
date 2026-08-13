namespace Copilot.Api.Uploads;

/// <summary>
/// Settings for client policy uploads. The connection string comes from Key Vault
/// (`storage-connection`) via app settings; everything else has sane defaults here.
/// </summary>
public sealed class PolicyUploadOptions
{
    public const string SectionName = "PolicyUploads";

    public string ConnectionString { get; set; } = "";

    public string DraftsContainer { get; set; } = "knowledge-drafts";

    /// <summary>Generous for a policy document, hostile to anything else.</summary>
    public long MaxFileBytes { get; set; } = 2 * 1024 * 1024;

    /// <summary>
    /// The markets the corpus actually has folders for. A typo'd market would not fail —
    /// it would index a chunk no market filter ever returns, which is worse.
    /// </summary>
    public string[] Markets { get; set; } =
        ["GLOBAL", "AU_NZ", "CA", "DE", "ES", "EU", "FR", "IT", "NL", "PL", "SE", "SG", "UK", "US"];
}
