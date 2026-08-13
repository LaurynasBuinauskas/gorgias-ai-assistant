namespace Copilot.Api.Auth;

/// <summary>API-level settings. The bearer token comes from appsettings.Development.json
/// locally and Key Vault in production.</summary>
public sealed class ApiOptions
{
    public const string SectionName = "Api";

    public string BearerToken { get; set; } = "";

    /// <summary>
    /// Separate token for <c>/v1/admin</c> routes — policy uploads must not be reachable
    /// with the agents' drafting token. Placeholder until per-person identity lands; while
    /// unset, every admin route answers 401, so the surface fails closed.
    /// </summary>
    public string AdminToken { get; set; } = "";
}
