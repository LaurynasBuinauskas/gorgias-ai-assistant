namespace Copilot.Api.Endpoints;

/// <summary>
/// What the extension shell is told on every page load.
///
/// These are served rather than shipped precisely so they can change without an extension
/// release — the shell is deployed to browsers and updating it is slow, so anything that might
/// need to change in a hurry lives here instead.
/// </summary>
public sealed class ShellConfigOptions
{
    public const string SectionName = "Shell";

    /// <summary>
    /// Rollback lever 1, and the fastest one: when true the shell mounts no panel at all, on
    /// every agent's browser, within one page load. Set through an app setting so pulling it
    /// needs no build and no deploy — a lever that required a deploy would be useless at the
    /// moment it was needed.
    ///
    /// The shell defaults this to false if the API is unreachable, so a network blip cannot
    /// silently disable the assistant. The corollary is that it only works while the API is up;
    /// if the API itself is the problem, use a later lever.
    /// </summary>
    public bool KillSwitch { get; set; }

    /// <summary>Shells older than this are asked to update rather than run.</summary>
    public string MinShellVersion { get; set; } = "0.1.0";

    /// <summary>Config-served DOM selectors, so a Gorgias markup change needs no release.</summary>
    public IReadOnlyList<string> AnchorProbes { get; set; } = [];
}
