namespace Copilot.Gorgias;

/// <summary>
/// Private-app credentials for the Gorgias REST API. Values come from user-secrets in
/// development and Key Vault in production — never from appsettings.json.
/// </summary>
public sealed class GorgiasOptions
{
    public const string SectionName = "Gorgias";

    public string Subdomain { get; set; } = "";

    /// <summary>Email of the Gorgias user the API key belongs to (Basic auth username).</summary>
    public string Email { get; set; } = "";

    public string ApiKey { get; set; } = "";
}
