using Copilot.Domain;

namespace Copilot.Gorgias;

public static class GorgiasTicketMapper
{
    public static TicketContext ToTicketContext(GorgiasTicketDto ticket)
    {
        var customer = ticket.Customer ?? ticket.Requester;

        var messages = (ticket.Messages ?? [])
            .Select(ToTicketMessage)
            .Where(m => m.Text.Length > 0)
            .OrderBy(m => m.SentAt ?? DateTimeOffset.MaxValue)
            .ToList();

        return new TicketContext
        {
            Id = ticket.Id,
            Subject = ticket.Subject,
            Status = ticket.Status ?? "unknown",
            Channel = ticket.Channel,
            Language = ticket.Language,
            Customer = customer is null ? null : new TicketCustomer(customer.Name, customer.Email),
            Messages = messages,
        };
    }

    private static TicketMessage ToTicketMessage(GorgiasMessageDto message) => new()
    {
        Id = message.Id,
        FromAgent = message.FromAgent,
        IsInternalNote = !message.Public || message.Source?.Type == "internal-note",
        // stripped_text is the clean latest content; body_text includes the quoted thread.
        Text = message.StrippedText is { Length: > 0 } stripped ? stripped : message.BodyText?.Trim() ?? "",
        SenderName = message.Sender?.Name,
        SentAt = message.SentDatetime ?? message.CreatedDatetime,
    };
}
