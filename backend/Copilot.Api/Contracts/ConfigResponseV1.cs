namespace Copilot.Api.Contracts;

/// <summary>v1 shell/panel config: anchor probes and kill switch are served, not shipped.</summary>
public sealed record ConfigResponseV1
{
    public int V { get; init; } = 1;

    public required bool KillSwitch { get; init; }

    public required string MinShellVersion { get; init; }

    public required IReadOnlyList<string> AnchorProbes { get; init; }
}
