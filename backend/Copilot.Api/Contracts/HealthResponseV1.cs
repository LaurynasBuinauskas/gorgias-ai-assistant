namespace Copilot.Api.Contracts;

/// <summary>
/// v1 liveness response. Public by design and deliberately free of ticket data, secrets or
/// configuration — the version is a build identifier, not an environment detail.
/// </summary>
public sealed record HealthResponseV1
{
    public int V { get; init; } = 1;

    public string Status { get; init; } = "healthy";

    /// <summary>Informational assembly version, e.g. `1.0.0+&lt;commit sha&gt;`.</summary>
    public required string Version { get; init; }
}
