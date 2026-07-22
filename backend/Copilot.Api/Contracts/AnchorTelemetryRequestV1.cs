namespace Copilot.Api.Contracts;

/// <summary>v1 dock-mode report from the extension shell.</summary>
public sealed record AnchorTelemetryRequestV1
{
    public int V { get; init; } = 1;

    public required string Account { get; init; }

    /// <summary>"docked" or "floating".</summary>
    public required string Mode { get; init; }
}
