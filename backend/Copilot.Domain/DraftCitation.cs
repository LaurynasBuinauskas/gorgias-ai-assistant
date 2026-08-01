namespace Copilot.Domain;

/// <summary>
/// One source the draft says it relied on, resolved back to the chunk it labelled.
///
/// The label is what the model emits (<c>P1</c>); everything else is resolved by the pipeline,
/// so a citation cannot name a source that was never retrieved. That is what lets an eval
/// assert grounding mechanically and an agent check the policy behind a claim.
/// </summary>
/// <param name="Label">Prompt label, e.g. <c>P1</c>.</param>
/// <param name="ChunkId">Index document id.</param>
/// <param name="SourcePath">Repository path of the file behind the chunk.</param>
/// <param name="Market">Market the chunk belongs to — how a wrong-market citation is caught.</param>
public sealed record DraftCitation(string Label, string ChunkId, string SourcePath, string Market);
