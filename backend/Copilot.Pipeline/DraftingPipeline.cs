using System.Runtime.CompilerServices;
using Copilot.Domain;
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
            BuildPrompt(ticket, request, context),
            ChatOptions,
            cancellationToken);

        LogUsage(ticket, response.Usage);

        var body = response.Text.Trim();
        return body.Length == 0
            ? new PipelineResult.InsufficientKnowledge("The model returned an empty draft; try again.")
            : new PipelineResult.Success(CreateDraft(ticket, body));
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
            BuildPrompt(ticket, request, context),
            ChatOptions,
            cancellationToken);

        await foreach (var update in updates.WithCancellation(cancellationToken))
        {
            if (update.Text is { Length: > 0 } text)
            {
                yield return new DraftChunk.Delta(text);
            }
        }

        logger.LogInformation("Streamed draft for ticket {TicketId}", ticket.Id);
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
    /// Assembles the prompt and checks it against the ceiling. Exceeding it means an upstream
    /// cap failed, so it is logged loudly rather than trimmed — silently shrinking the prompt
    /// would hide the defect and change the answer at the same time.
    /// </summary>
    private IReadOnlyList<ChatMessage> BuildPrompt(
        TicketContext ticket,
        DraftRequest request,
        RetrievedContext context)
    {
        var messages = DraftPrompt.Build(ticket, request, context, _options.MaxTranscriptCharacters);
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

    private static Draft CreateDraft(TicketContext ticket, string body) => new()
    {
        DraftId = Guid.NewGuid().ToString("N"),
        TicketId = ticket.Id,
        Body = body,
        Language = ticket.Language,
    };

    private void LogUsage(TicketContext ticket, UsageDetails? usage) =>
        logger.LogInformation(
            "Draft generated for ticket {TicketId}: {InputTokens} in / {OutputTokens} out",
            ticket.Id,
            usage?.InputTokenCount,
            usage?.OutputTokenCount);
}
