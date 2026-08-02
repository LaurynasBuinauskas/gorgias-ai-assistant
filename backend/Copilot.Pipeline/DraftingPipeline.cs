using System.Runtime.CompilerServices;
using Copilot.Domain;
using Copilot.Knowledge;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Copilot.Pipeline;

/// <summary>
/// Drafts from ticket content grounded in retrieved policy, templates and internal guidance.
/// Retrieval and the relevance gate run ahead of the model, so a question the corpus does not
/// cover costs nothing and produces an honest decline rather than an invented answer.
/// </summary>
public sealed class DraftingPipeline(
    IChatClient chatClient,
    KnowledgeRetriever retriever,
    IOptions<DraftingOptions> options,
    IOptions<RetrievalOptions> retrievalOptions,
    ILogger<DraftingPipeline> logger) : IDraftingPipeline
{
    private const string NoCustomerMessage = "This ticket has no customer message to reply to.";

    private const string NotCovered =
        "The policy documents do not cover this question, so I can't draft a reliable reply. " +
        "Please answer from your own knowledge or escalate.";

    private readonly DraftingOptions _options = options.Value;
    private readonly RetrievalOptions _retrieval = retrievalOptions.Value;

    public async Task<PipelineResult> GenerateDraftAsync(
        TicketContext ticket,
        DraftRequest request,
        CancellationToken cancellationToken)
    {
        if (!HasCustomerMessage(ticket))
        {
            return new PipelineResult.InsufficientKnowledge(NoCustomerMessage);
        }

        var draftId = NewDraftId();
        var context = await retriever.RetrieveAsync(ticket, cancellationToken);
        RetrievalLog.Retrieved(logger, draftId, ticket, context);

        if (!IsCovered(draftId, ticket, context))
        {
            return new PipelineResult.InsufficientKnowledge(NotCovered);
        }

        var response = await chatClient.GetResponseAsync(
            BuildPrompt(draftId, ticket, request, context, out var citable),
            ChatOptions,
            cancellationToken);

        RetrievalLog.Usage(logger, draftId, response.Usage?.InputTokenCount, response.Usage?.OutputTokenCount);

        var splitter = SourceSplitterExtensions.SplitAll(response.Text);
        var body = splitter.Body;
        if (body.Length == 0)
        {
            return new PipelineResult.InsufficientKnowledge("The model returned an empty draft; try again.");
        }

        var citations = splitter.ResolveCitations(citable);
        RetrievalLog.Citations(logger, draftId, citations);
        return new PipelineResult.Success(CreateDraft(draftId, ticket, body, citations));
    }

    public async IAsyncEnumerable<DraftChunk> StreamDraftAsync(
        TicketContext ticket,
        DraftRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (!HasCustomerMessage(ticket))
        {
            yield return new DraftChunk.Insufficient(NoCustomerMessage);
            yield break;
        }

        var draftId = NewDraftId();
        yield return new DraftChunk.Started(draftId);

        var context = await retriever.RetrieveAsync(ticket, cancellationToken);
        RetrievalLog.Retrieved(logger, draftId, ticket, context);

        if (!IsCovered(draftId, ticket, context))
        {
            yield return new DraftChunk.Insufficient(NotCovered);
            yield break;
        }

        var updates = chatClient.GetStreamingResponseAsync(
            BuildPrompt(draftId, ticket, request, context, out var citable),
            ChatOptions,
            cancellationToken);

        // The source list is held back rather than streamed: the agent copies what they see,
        // and labels are for review, not for the customer.
        var splitter = new SourceSplitter();
        await foreach (var update in updates.WithCancellation(cancellationToken))
        {
            if (update.Text is { Length: > 0 } text && splitter.Push(text) is { Length: > 0 } ready)
            {
                yield return new DraftChunk.Delta(ready);
            }
        }

        if (splitter.Complete() is { Length: > 0 } tail)
        {
            yield return new DraftChunk.Delta(tail);
        }

        var citations = splitter.ResolveCitations(citable);
        RetrievalLog.Citations(logger, draftId, citations);
        yield return new DraftChunk.Sources(citations);
    }

    private ChatOptions ChatOptions => new() { MaxOutputTokens = _options.MaxOutputTokens };

    /// <summary>
    /// The relevance gate. Declining is a feature: below the threshold the corpus does not
    /// support an answer, and improvising one is the failure mode this whole design exists to
    /// prevent. Returning here costs no tokens, because the model has not been called yet.
    ///
    /// When retrieval is bypassed (rollback lever 2) the gate does not apply — that mode is a
    /// deliberate revert to today's ungrounded behaviour, not an outage.
    /// </summary>
    private bool IsCovered(string draftId, TicketContext ticket, RetrievedContext context)
    {
        if (context.Bypassed)
        {
            return true;
        }

        // A gate that cannot score must stand down, not decline everything. Unranked results
        // score on a different scale — around 0.03 against a threshold calibrated near 2 — so
        // applying it here would refuse every draft while the service looked healthy.
        if (context.RankingUnavailable)
        {
            logger.LogWarning(
                "Draft {DraftId} ticket {TicketId}: relevance gate skipped, semantic ranking "
                + "unavailable. Coverage rests on the prompt rule alone until quota is restored",
                draftId, ticket.Id);
            return true;
        }

        var covered = context.BestPolicyScore >= _retrieval.MinimumPolicyScore;
        RetrievalLog.Gate(logger, draftId, ticket, context, _retrieval.MinimumPolicyScore, covered);
        return covered;
    }

    private static string NewDraftId() => Guid.NewGuid().ToString("N");

    /// <summary>
    /// Assembles the prompt and checks it against the ceiling. Exceeding it means an upstream
    /// cap failed, so it is logged loudly rather than trimmed — silently shrinking the prompt
    /// would hide the defect and change the answer at the same time.
    /// </summary>
    private IReadOnlyList<ChatMessage> BuildPrompt(
        string draftId,
        TicketContext ticket,
        DraftRequest request,
        RetrievedContext context,
        out IReadOnlyDictionary<string, KnowledgeChunk> citable)
    {
        var messages = DraftPrompt.Build(
            ticket, request, context, _options.MaxTranscriptCharacters, out citable);
        var characters = messages.Sum(m => m.Text?.Length ?? 0);

        // Roughly four characters per token; enough to spot a prompt growing without bound.
        RetrievalLog.Prompt(logger, draftId, characters, characters / 4);

        if (characters > _options.MaxPromptCharacters)
        {
            logger.LogError(
                "Prompt for ticket {TicketId} is {Characters} characters, over the {Ceiling} ceiling — an input cap is not holding",
                ticket.Id,
                characters,
                _options.MaxPromptCharacters);
        }

        return messages;
    }

    private static bool HasCustomerMessage(TicketContext ticket) =>
        ticket.Messages.Any(m => m is { FromAgent: false, IsInternalNote: false });

    private static Draft CreateDraft(
        string draftId,
        TicketContext ticket,
        string body,
        IReadOnlyList<DraftCitation> citations) => new()
    {
        DraftId = draftId,
        TicketId = ticket.Id,
        Body = body,
        Language = ticket.Language,
        Citations = citations,
    };
}
