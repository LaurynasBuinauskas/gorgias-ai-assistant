using System.Text;
using Copilot.Domain;

namespace Copilot.Pipeline;

/// <summary>
/// Prompt templates are versioned here in-repo; changes go through the eval harness
/// once it exists (Stage 3), never ad-hoc.
/// </summary>
public static class DraftPrompt
{
    public const string System = """
        You are an experienced customer support agent drafting a reply to a customer.

        Rules:
        - Write the reply in the same language as the customer's latest message.
        - Be polite, concise, and concrete; match the tone of a professional support team.
        - Use only facts present in the conversation. Never invent order details,
          policies, prices, deadlines, or commitments that are not stated there.
        - If information needed to resolve the request is missing, ask the customer
          for it rather than guessing.
        - Output only the reply body: no subject line, no placeholders like [Name] —
          use the customer's actual name if known, and end with a friendly sign-off
          from the support team.
        """;

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
