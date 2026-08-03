namespace Copilot.Api.Contracts;

/// <summary>v1 shell/panel config: anchor probes and kill switch are served, not shipped.</summary>
public sealed record ConfigResponseV1
{
    public int V { get; init; } = 1;

    public required bool KillSwitch { get; init; }

    public required string MinShellVersion { get; init; }

    public required IReadOnlyList<string> AnchorProbes { get; init; }

    /// <summary>
    /// Whether retrieval is drawing on the resolved-ticket corpus (`Retrieval:TicketTopK`
    /// above zero).
    ///
    /// Served so that turning exemplars on or off is *observable*. It is an app-setting change,
    /// which restarts App Service and takes 70-90 seconds while `az` returns immediately, and
    /// until now there was nothing to poll — the one change in this system that alters what
    /// customer-derived data reaches a draft was also the one you had to take on trust.
    /// Additive: the shell reads named fields and ignores the rest.
    /// </summary>
    public required bool Exemplars { get; init; }
}
