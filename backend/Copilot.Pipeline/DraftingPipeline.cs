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

        var context = await retriever.RetrieveAsync(ticket, cancellationToken);
        if (!IsCovered(ticket, context))
        {
            return new PipelineResult.InsufficientKnowledge(NotCovered);
        }

        var response = await chatClient.GetResponseAsync(
            BuildPrompt(ticket, request, context, out var citable),
            ChatOptions,
            cancellationToken);

        LogUsage(ticket, response.Usage);

        var splitter = SourceSplitterExtensions.SplitAll(response.Text);
        var body = splitter.Body;
        if (body.Length == 0)
        {
            return new PipelineResult.InsufficientKnowledge("The model returned an empty draft; try again.");
        }

        var citations = splitter.ResolveCitations(citable);
        LogCitations(ticket, citations, context);
        return new PipelineResult.Success(CreateDraft(ticket, body, citations));
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

        var context = await retriever.RetrieveAsync(ticket, cancellationToken);
        if (!IsCovered(ticket, context))
        {
            yield return new DraftChunk.Insufficient(NotCovered);
            yield break;
        }

        var updates = chatClient.GetStreamingResponseAsync(
            BuildPrompt(ticket, request, context, out var citable),
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
        LogCitations(ticket, citations, context);
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
    private bool IsCovered(TicketContext ticket, RetrievedContext context)
    {
        if (context.Bypassed)
        {
            return true;
        }

        var covered = context.BestPolicyScore >= _retrieval.MinimumPolicyScore;

        // Logged on both paths, not just refusals: a threshold nobody can see the inputs to
        // cannot be tuned, and "why did it answer that?" is as important as "why did it decline?"
        logger.LogInformation(
            "Gate {Decision} for ticket {TicketId}: best policy score {Score:F3} vs threshold {Threshold:F3}, "
            + "market {Market} ({Signal}), chunks policy={Policy} template={Template} internal={Internal}, "
            + "top source {TopSource}",
            covered ? "passed" : "declined",
            ticket.Id,
            context.BestPolicyScore,
            _retrieval.MinimumPolicyScore,
            context.Market.Market,
            context.Market.Signal,
            context.Policy.Count,
            context.Templates.Count,
            context.Internal.Count,
            // Source path, never chunk text: logs must stay free of customer and policy content.
            context.Policy.Count == 0 ? "none" : context.Policy[0].SourcePath);

        return covered;
    }

    /// <summary>
    /// Source paths only, never chunk text or draft body — a log line must not become a third
    /// copy of policy or customer content.
    /// </summary>
    private void LogCitations(
        TicketContext ticket,
        IReadOnlyList<DraftCitation> citations,
        RetrievedContext context)
    {
        if (context.Bypassed)
        {
            return;
        }

        logger.LogInformation(
            "Draft for ticket {TicketId} cited {Count} source(s) in market {Market}: {Sources}",
            ticket.Id,
            citations.Count,
            context.Market.Market,
            citations.Count == 0 ? "none" : string.Join(", ", citations.Select(c => c.SourcePath)));
    }

    /// <summary>
    /// Assembles the prompt and checks it against the ceiling. Exceeding it means an upstream
    /// cap failed, so it is logged loudly rather than trimmed — silently shrinking the prompt
    /// would hide the defect and change the answer at the same time.
    /// </summary>
    private IReadOnlyList<ChatMessage> BuildPrompt(
        TicketContext ticket,
        DraftRequest request,
        RetrievedContext context,
        out IReadOnlyDictionary<string, KnowledgeChunk> citable)
    {
        var messages = DraftPrompt.Build(
            ticket, request, context, _options.MaxTranscriptCharacters, out citable);
        var characters = messages.Sum(m => m.Text?.Length ?? 0);

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
        TicketContext ticket,
        string body,
        IReadOnlyList<DraftCitation> citations) => new()
    {
        DraftId = Guid.NewGuid().ToString("N"),
        TicketId = ticket.Id,
        Body = body,
        Language = ticket.Language,
        Citations = citations,
    };

    private void LogUsage(TicketContext ticket, UsageDetails? usage) =>
        logger.LogInformation(
            "Draft generated for ticket {TicketId}: {InputTokens} in / {OutputTokens} out",
            ticket.Id,
            usage?.InputTokenCount,
            usage?.OutputTokenCount);
}
