namespace Copilot.Api.Auth;

/// <summary>API-level settings. The bearer token comes from appsettings.Development.json
/// locally and Key Vault in production.</summary>
public sealed class ApiOptions
{
    public const string SectionName = "Api";

    public string BearerToken { get; set; } = "";
}
