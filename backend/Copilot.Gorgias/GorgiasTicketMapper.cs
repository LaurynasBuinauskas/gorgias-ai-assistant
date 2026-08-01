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
            MarketSignals = CollectMarketSignals(ticket),
            Messages = messages,
        };
    }

    /// <summary>
    /// Gathers the three market signals. The heavy integrations blob is otherwise discarded,
    /// so only the order URLs are lifted out of it — the rest stays unread.
    /// </summary>
    private static MarketSignals CollectMarketSignals(GorgiasTicketDto ticket)
    {
        var orderUrls = new[] { ticket.Customer, ticket.Requester }
            .Where(user => user is not null)
            .SelectMany(user => user!.Integrations?.Values ?? [])
            .SelectMany(integration => integration.Orders ?? [])
            .SelectMany(order => new[] { order.OrderStatusUrl, order.ReferringSite })
            .OfType<string>()
            .Where(url => url.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var messages = ticket.Messages ?? [];

        var chatPages = messages
            .Select(message => message.Meta?.CurrentPage)
            .OfType<string>()
            .Where(page => page.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Inbound only: where we replied from says nothing about which shop the customer used.
        var inboxes = messages
            .Where(message => !message.FromAgent)
            .SelectMany(message => message.Source?.To ?? [])
            .Select(address => address.Address)
            .OfType<string>()
            .Where(address => address.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new MarketSignals
        {
            OrderUrls = orderUrls,
            ChatPages = chatPages,
            SupportInboxes = inboxes,
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
