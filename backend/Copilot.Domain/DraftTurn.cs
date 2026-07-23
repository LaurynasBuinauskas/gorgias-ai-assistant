namespace Copilot.Domain;

public enum DraftTurnRole
{
    /// <summary>A draft the assistant produced.</summary>
    Assistant,

    /// <summary>An instruction the agent gave ("make it friendlier", "translate to English").</summary>
    Agent,
}

public sealed record DraftTurn(DraftTurnRole Role, string Text);

/// <summary>
/// One drafting request. The panel owns the conversation and replays it here, so the
/// backend stays stateless and nothing is persisted server-side.
/// </summary>
public sealed record DraftRequest
{
    public static readonly DraftRequest Initial = new();

    public IReadOnlyList<DraftTurn> Turns { get; init; } = [];

    /// <summary>The new instruction, if the agent is refining rather than starting fresh.</summary>
    public string? Instruction { get; init; }
}
