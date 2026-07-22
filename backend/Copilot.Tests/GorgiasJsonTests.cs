using System.Text.Json;
using Copilot.Gorgias;

namespace Copilot.Tests;

public sealed class GorgiasJsonTests
{
    [Fact]
    public void DeserializesSnakeCaseTicketPayload()
    {
        // Shape mirrors the real GET /api/tickets/{id} payload (synthetic values).
        const string json = """
            {
              "id": 123,
              "subject": "Re: Return Confirmation",
              "status": "open",
              "channel": "email",
              "language": "de",
              "unknown_field": {"nested": true},
              "customer": {"name": "Jane Doe", "email": "jane@example.com"},
              "messages": [
                {
                  "id": 9,
                  "public": true,
                  "from_agent": false,
                  "stripped_text": "Hallo, wo ist meine Bestellung?",
                  "body_text": "Hallo, wo ist meine Bestellung?\n\nAm 1.1. schrieb support:",
                  "sender": {"name": "Jane Doe", "email": "jane@example.com"},
                  "source": {"type": "email"},
                  "created_datetime": "2026-07-02T11:46:12+00:00",
                  "sent_datetime": "2026-07-02T11:46:01+00:00"
                }
              ]
            }
            """;

        var ticket = JsonSerializer.Deserialize<GorgiasTicketDto>(json, GorgiasJson.Options);

        Assert.NotNull(ticket);
        Assert.Equal(123, ticket.Id);
        Assert.Equal("de", ticket.Language);
        Assert.Equal("Jane Doe", ticket.Customer?.Name);
        var message = Assert.Single(ticket.Messages!);
        Assert.False(message.FromAgent);
        Assert.Equal("Hallo, wo ist meine Bestellung?", message.StrippedText);
        Assert.Equal(DateTimeOffset.Parse("2026-07-02T11:46:01+00:00"), message.SentDatetime);
    }
}
