using System.Text;
using Copilot.Domain;
using Microsoft.Extensions.AI;

namespace Copilot.Pipeline;

/// <summary>
/// Prompt templates are versioned here in-repo; changes go through the eval harness
/// once it exists, never ad-hoc.
/// </summary>
public static class DraftPrompt
{
    public const string System = """
        You are an experienced customer support agent helping a colleague draft replies.

        Rules:
        - Write in English by default, even when the customer wrote in another language, so
          the agent can review it first. If the agent asks for a specific language
          (e.g. "translate to German"), switch to it and stay there for the rest of the
          conversation.
        - Be polite, concise, and concrete; match the tone of a professional support team.
        - Use only facts present in the conversation. Never invent order details, policies,
          prices, deadlines, or commitments that are not stated there.
        - If information needed to resolve the request is missing, ask the customer for it
          rather than guessing.
        - Output only the reply body: no subject line, no preamble like "Here is the draft",
          no placeholders like [Name] — use the customer's actual name if known — and end
          with a friendly sign-off from the support team.
        - When the agent asks for a change, rewrite the whole reply with that change applied.
          Always return a complete, ready-to-send reply, never a diff or commentary.
        """;

    /// <summary>
    /// Builds the full conversation: ticket transcript, then the agent's refinement turns.
    /// </summary>
    public static IReadOnlyList<ChatMessage> Build(TicketContext ticket, DraftRequest request)
    {
        List<ChatMessage> messages =
        [
            new(ChatRole.System, System),
            new(ChatRole.User, BuildTranscript(ticket)),
        ];

        foreach (var turn in request.Turns)
        {
            var role = turn.Role == DraftTurnRole.Assistant ? ChatRole.Assistant : ChatRole.User;
            messages.Add(new ChatMessage(role, turn.Text));
        }

        if (!string.IsNullOrWhiteSpace(request.Instruction))
        {
            messages.Add(new ChatMessage(ChatRole.User, request.Instruction));
        }

        return messages;
    }

    public static string BuildTranscript(TicketContext ticket)
    {
        var transcript = new StringBuilder();
        transcript.AppendLine($"Ticket subject: {ticket.Subject}");
        transcript.AppendLine($"Customer: {ticket.Customer?.Name ?? "unknown"}");
        transcript.AppendLine();
        transcript.AppendLine("Conversation (oldest first):");

        foreach (var message in ticket.Messages.Where(m => !m.IsInternalNote))
        {
            var speaker = message.FromAgent ? "Support agent" : "Customer";
            transcript.AppendLine($"--- {speaker} ({message.SenderName ?? "unknown"}):");
            transcript.AppendLine(message.Text);
            transcript.AppendLine();
        }

        transcript.AppendLine("Draft the support agent's next reply to the customer.");
        return transcript.ToString();
    }
}
