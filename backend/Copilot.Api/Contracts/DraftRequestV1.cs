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

    /// <summary>
    /// Validates the request against the configured caps before any of it can reach the
    /// model. Every failure is a 400 — this input is client-controlled and unbounded
    /// otherwise, so silently trimming it would hide exactly the abuse the caps exist for.
    /// </summary>
    public bool TryValidate(DraftLimitsOptions limits, out DraftRequest request, out string error)
    {
        request = DraftRequest.Initial;

        if (Turns.Count > limits.MaxTurns)
        {
            error = $"Too many turns: {Turns.Count} exceeds the limit of {limits.MaxTurns}.";
            return false;
        }

        if (Instruction is { Length: var length } && length > limits.MaxInstructionCharacters)
        {
            error = $"Instruction is too long: {length} characters exceeds {limits.MaxInstructionCharacters}.";
            return false;
        }

        var total = Instruction?.Length ?? 0;
        var turns = new List<DraftTurn>(Turns.Count);

        for (var index = 0; index < Turns.Count; index++)
        {
            var turn = Turns[index];

            if (!DraftTurnV1.TryParseRole(turn.Role, out var role))
            {
                error = $"Turn {index} has an unrecognised role '{turn.Role}'; expected 'assistant' or 'agent'.";
                return false;
            }

            if (turn.Text.Length > limits.MaxTurnCharacters)
            {
                error = $"Turn {index} is too long: {turn.Text.Length} characters exceeds {limits.MaxTurnCharacters}.";
                return false;
            }

            total += turn.Text.Length;
            turns.Add(new DraftTurn(role, turn.Text));
        }

        if (total > limits.MaxTotalCharacters)
        {
            error = $"Request is too large: {total} characters across turns and instruction exceeds {limits.MaxTotalCharacters}.";
            return false;
        }

        request = new DraftRequest { Turns = turns, Instruction = Instruction };
        error = "";
        return true;
    }
}

public sealed record DraftTurnV1
{
    public required string Role { get; init; }

    public required string Text { get; init; }

    /// <summary>
    /// Matched by name only, and unknown roles are rejected rather than defaulted. Treating
    /// anything unrecognised as "agent" let a caller smuggle arbitrary role strings into the
    /// prompt and silently changed what the model was told it was reading.
    /// </summary>
    public static bool TryParseRole(string? role, out DraftTurnRole parsed)
    {
        DraftTurnRole? recognised = role?.Trim().ToLowerInvariant() switch
        {
            "assistant" => DraftTurnRole.Assistant,
            "agent" => DraftTurnRole.Agent,
            _ => null,
        };

        parsed = recognised ?? default;
        return recognised is not null;
    }
}
