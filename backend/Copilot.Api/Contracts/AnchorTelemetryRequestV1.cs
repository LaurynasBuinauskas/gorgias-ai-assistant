namespace Copilot.Api.Contracts;

public enum AnchorMode
{
    Docked,
    Floating,
}

/// <summary>
/// v1 dock-mode report from the extension shell. This endpoint is unauthenticated — the
/// shell holds no credentials — so everything here is untrusted input and is validated
/// before it reaches a log.
/// </summary>
public sealed record AnchorTelemetryRequestV1
{
    /// <summary>Long enough for any real subdomain, short enough to keep logs bounded.</summary>
    private const int MaxAccountLength = 64;

    public int V { get; init; } = 1;

    public required string Account { get; init; }

    /// <summary>"docked" or "floating".</summary>
    public required string Mode { get; init; }

    public bool TryValidate(out AnchorMode mode, out string account)
    {
        account = Sanitize(Account);

        // Matched by name only. Enum.TryParse would also accept the underlying numbers —
        // "0" and "1" silently becoming modes, "5" a member that does not exist — none of
        // which were ever part of this contract.
        AnchorMode? recognised = Mode.Trim().ToLowerInvariant() switch
        {
            "docked" => AnchorMode.Docked,
            "floating" => AnchorMode.Floating,
            _ => null,
        };

        mode = recognised ?? default;
        return recognised is not null && account.Length > 0;
    }

    /// <summary>
    /// Strips control characters — newlines in particular, which would otherwise let a
    /// caller forge extra lines in a text log — and clamps the length.
    /// </summary>
    private static string Sanitize(string value)
    {
        var kept = value.Where(c => !char.IsControl(c)).Take(MaxAccountLength).ToArray();
        return new string(kept).Trim();
    }
}
