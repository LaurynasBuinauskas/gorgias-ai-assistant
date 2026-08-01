using Copilot.Domain;
using Copilot.Knowledge;
using Microsoft.Extensions.Logging;

namespace Copilot.Pipeline;

/// <summary>
/// Everything needed to explain a draft after the fact, keyed by its id.
///
/// The constraint that shapes this: **no ticket or policy text may appear in a log line.**
/// Logs are retained and searchable, and a log that quotes a customer message is a second copy
/// of personal data in a system nobody designed to hold it. So the record is ids, scores,
/// paths and counts — enough to reconstruct exactly which chunks were used, because each id
/// resolves against the index, and nothing more.
/// </summary>
internal static class RetrievalLog
{
    private static readonly string[] s_corpora = ["policy", "template", "internal", "ticket"];

    public static void Retrieved(
        ILogger logger,
        string draftId,
        TicketContext ticket,
        RetrievedContext context)
    {
        if (context.Bypassed)
        {
            logger.LogInformation(
                "Draft {DraftId} ticket {TicketId}: retrieval bypassed (Retrieval:Enabled=false)",
                draftId, ticket.Id);
            return;
        }

        logger.LogInformation(
            "Draft {DraftId} ticket {TicketId}: market {Market} decided by {Signal}; "
            + "policy=[{Policy}] template=[{Template}] ticketExemplars=[{Tickets}] internal=[{Internal}]",
            draftId,
            ticket.Id,
            context.Market.Market,
            context.Market.Signal,
            Describe(context.Policy),
            Describe(context.Templates),
            Describe(context.Tickets),
            Describe(context.Internal));
    }

    public static void Gate(
        ILogger logger,
        string draftId,
        TicketContext ticket,
        RetrievedContext context,
        double threshold,
        bool covered) =>
        logger.LogInformation(
            "Draft {DraftId} ticket {TicketId}: gate {Decision}, best policy score {Score:F3} vs threshold {Threshold:F3}",
            draftId,
            ticket.Id,
            covered ? "passed" : "declined",
            context.BestPolicyScore,
            threshold);

    public static void Prompt(ILogger logger, string draftId, int characters, int estimatedTokens) =>
        logger.LogInformation(
            "Draft {DraftId}: prompt {Characters} characters, ~{EstimatedTokens} tokens",
            draftId, characters, estimatedTokens);

    public static void Usage(ILogger logger, string draftId, long? inputTokens, long? outputTokens) =>
        logger.LogInformation(
            "Draft {DraftId}: {InputTokens} tokens in / {OutputTokens} out",
            draftId, inputTokens, outputTokens);

    public static void Citations(
        ILogger logger,
        string draftId,
        IReadOnlyList<DraftCitation> citations) =>
        logger.LogInformation(
            "Draft {DraftId}: cited {Count} source(s) {Sources}",
            draftId,
            citations.Count,
            citations.Count == 0
                ? "none"
                : string.Join(" ", citations.Select(c => $"{c.Label}={Readable(c.ChunkId)}@{c.Market}")));

    /// <summary>Chunk identity and score per hit — never the chunk's text.</summary>
    private static string Describe(IReadOnlyList<KnowledgeChunk> chunks) =>
        chunks.Count == 0
            ? ""
            : string.Join(" ", chunks.Select(c => $"{Readable(c.Id)}:{c.Score:F3}"));

    /// <summary>
    /// Search keys accept only a restricted alphabet, so ingestion base64url-encodes the
    /// natural key (<c>corpus:sourcePath:ordinal</c>). Decoding it back for the log costs
    /// nothing and is the difference between a line someone can read and forty opaque
    /// characters they have to paste somewhere to understand.
    /// </summary>
    internal static string Readable(string chunkId)
    {
        try
        {
            var padded = chunkId.Replace('-', '+').Replace('_', '/')
                .PadRight(chunkId.Length + ((4 - (chunkId.Length % 4)) % 4), '=');
            var decoded = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(padded));

            // Only accept a decode that actually looks like the natural key. Short ids can be
            // accidentally valid base64 — "policy-1" decodes to bytes — and printing that
            // gibberish instead of the id would make the log worse, not better.
            return s_corpora.Any(c => decoded.StartsWith($"{c}:", StringComparison.Ordinal))
                ? decoded
                : chunkId;
        }
        catch (FormatException)
        {
            // Not an encoded key — a fixture id, or an id scheme that changed. Log it as-is.
            return chunkId;
        }
    }
}
