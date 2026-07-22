using Copilot.Domain;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace Copilot.Pipeline;

/// <summary>
/// MVP-lite pipeline: drafts from ticket content alone. Knowledge retrieval and the
/// relevance gate slot in ahead of the LLM call once a knowledge base exists (Stage 2/3).
/// </summary>
public sealed class DraftingPipeline(IChatClient chatClient, ILogger<DraftingPipeline> logger) : IDraftingPipeline
{
    public async Task<PipelineResult> GenerateDraftAsync(TicketContext ticket, CancellationToken cancellationToken)
    {
        var latestCustomerMessage = ticket.Messages.LastOrDefault(m => m is { FromAgent: false, IsInternalNote: false });
        if (latestCustomerMessage is null)
        {
            return new PipelineResult.InsufficientKnowledge(
                "This ticket has no customer message to reply to.");
        }

        ChatMessage[] messages =
        [
            new(ChatRole.System, DraftPrompt.System),
            new(ChatRole.User, DraftPrompt.BuildTranscript(ticket)),
        ];

        var response = await chatClient.GetResponseAsync(messages, cancellationToken: cancellationToken);

        logger.LogInformation(
            "Draft generated for ticket {TicketId}: {InputTokens} in / {OutputTokens} out",
            ticket.Id,
            response.Usage?.InputTokenCount,
            response.Usage?.OutputTokenCount);

        var body = response.Text.Trim();
        if (body.Length == 0)
        {
            return new PipelineResult.InsufficientKnowledge(
                "The model returned an empty draft; try again.");
        }

        return new PipelineResult.Success(new Draft
        {
            DraftId = Guid.NewGuid().ToString("N"),
            TicketId = ticket.Id,
            Body = body,
            Language = ticket.Language,
        });
    }
}
