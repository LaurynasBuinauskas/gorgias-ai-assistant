using Copilot.Domain;

namespace Copilot.Api.Contracts;

/// <summary>
/// v1 drafting request. The panel replays the conversation on every call so the backend
/// stays stateless. Append-only: never change shipped fields in place.
/// </summary>
public sealed record DraftRequestV1
{
    public int V { get; init; } = 1;

    /// <summary>Prior turns, oldest first. "assistant" = a draft, "agent" = an instruction.</summary>
    public IReadOnlyList<DraftTurnV1> Turns { get; init; } = [];

    public string? Instruction { get; init; }

    public DraftRequest ToDomain() => new()
    {
        Turns = [.. Turns.Select(t => t.ToDomain())],
        Instruction = Instruction,
    };
}

public sealed record DraftTurnV1
{
    public required string Role { get; init; }

    public required string Text { get; init; }

    public DraftTurn ToDomain() => new(
        string.Equals(Role, "assistant", StringComparison.OrdinalIgnoreCase)
            ? DraftTurnRole.Assistant
            : DraftTurnRole.Agent,
        Text);
}
