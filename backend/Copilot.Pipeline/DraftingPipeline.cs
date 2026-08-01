using System.Runtime.CompilerServices;
using Copilot.Domain;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Copilot.Pipeline;

/// <summary>
/// MVP-lite pipeline: drafts from ticket content plus the agent's refinement turns.
/// Knowledge retrieval and the relevance gate slot in ahead of the LLM call once a
/// knowledge base exists.
/// </summary>
public sealed class DraftingPipeline(
    IChatClient chatClient,
    IOptions<DraftingOptions> options,
    ILogger<DraftingPipeline> logger) : IDraftingPipeline
{
    private const string NoCustomerMessage = "This ticket has no customer message to reply to.";

    private readonly DraftingOptions _options = options.Value;

    public async Task<PipelineResult> GenerateDraftAsync(
        TicketContext ticket,
        DraftRequest request,
        CancellationToken cancellationToken)
    {
        if (!HasCustomerMessage(ticket))
        {
            return new PipelineResult.InsufficientKnowledge(NoCustomerMessage);
        }

        var response = await chatClient.GetResponseAsync(
            BuildPrompt(ticket, request),
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

        var updates = chatClient.GetStreamingResponseAsync(
            BuildPrompt(ticket, request),
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
    /// Assembles the prompt and checks it against the ceiling. Exceeding it means an upstream
    /// cap failed, so it is logged loudly rather than trimmed — silently shrinking the prompt
    /// would hide the defect and change the answer at the same time.
    /// </summary>
    private IReadOnlyList<ChatMessage> BuildPrompt(TicketContext ticket, DraftRequest request)
    {
        var messages = DraftPrompt.Build(ticket, request, _options.MaxTranscriptCharacters);
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
