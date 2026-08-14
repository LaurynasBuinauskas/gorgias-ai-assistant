namespace Copilot.Api.Contracts;

/// <summary>Ask for staged uploads to become live policy.</summary>
public sealed record PublishRequestV1
{
    public int V { get; init; } = 1;

    public IReadOnlyList<string> Blobs { get; init; } = [];

    public string PublishedBy { get; init; } = "";
}

/// <summary>Ask for the state before the newest publish to be restored, gated like any publish.</summary>
public sealed record RollbackRequestV1
{
    public int V { get; init; } = 1;

    public string PublishedBy { get; init; } = "";
}
