namespace Copilot.Api.Contracts;

/// <summary>v1 typed "no draft possible" outcome — a first-class state in the panel, not an error.</summary>
public sealed record InsufficientDataResponseV1
{
    public int V { get; init; } = 1;

    public string Status { get; init; } = "insufficient_data";

    public required string Message { get; init; }
}
