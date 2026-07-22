using Copilot.Gorgias;

namespace Copilot.Tests;

public sealed class GorgiasTicketMapperTests
{
    [Fact]
    public void PrefersStrippedTextOverBodyText()
    {
        var ticket = Ticket(Message(id: 1, strippedText: "clean text", bodyText: "clean text\n> quoted history"));

        var context = GorgiasTicketMapper.ToTicketContext(ticket);

        Assert.Equal("clean text", Assert.Single(context.Messages).Text);
    }

    [Fact]
    public void FallsBackToBodyTextWhenStrippedTextMissing()
    {
        var ticket = Ticket(Message(id: 1, strippedText: null, bodyText: "note body"));

        var context = GorgiasTicketMapper.ToTicketContext(ticket);

        Assert.Equal("note body", Assert.Single(context.Messages).Text);
    }

    [Fact]
    public void FlagsInternalNotes()
    {
        var ticket = Ticket(
            Message(id: 1, isPublic: false, sourceType: "internal-note"),
            Message(id: 2, isPublic: true, sourceType: "email"));

        var context = GorgiasTicketMapper.ToTicketContext(ticket);

        Assert.True(context.Messages.Single(m => m.Id == 1).IsInternalNote);
        Assert.False(context.Messages.Single(m => m.Id == 2).IsInternalNote);
    }

    [Fact]
    public void OrdersMessagesOldestFirst()
    {
        var ticket = Ticket(
            Message(id: 2, sentAt: DateTimeOffset.Parse("2026-07-02T10:00:00Z")),
            Message(id: 1, sentAt: DateTimeOffset.Parse("2026-07-01T10:00:00Z")));

        var context = GorgiasTicketMapper.ToTicketContext(ticket);

        Assert.Equal([1L, 2L], context.Messages.Select(m => m.Id));
    }

    [Fact]
    public void DropsMessagesWithoutText()
    {
        var ticket = Ticket(
            Message(id: 1, strippedText: null, bodyText: null),
            Message(id: 2, strippedText: "kept"));

        var context = GorgiasTicketMapper.ToTicketContext(ticket);

        Assert.Equal(2, Assert.Single(context.Messages).Id);
    }

    [Fact]
    public void FallsBackToRequesterWhenCustomerMissing()
    {
        var ticket = new GorgiasTicketDto(
            Id: 42,
            Subject: "s",
            Status: "open",
            Channel: "email",
            Language: "de",
            Customer: null,
            Requester: new GorgiasUserDto("Jane Doe", "jane@example.com"),
            Messages: []);

        var context = GorgiasTicketMapper.ToTicketContext(ticket);

        Assert.Equal("Jane Doe", context.Customer?.Name);
    }

    private static GorgiasTicketDto Ticket(params GorgiasMessageDto[] messages) => new(
        Id: 42,
        Subject: "Order question",
        Status: "open",
        Channel: "email",
        Language: "de",
        Customer: new GorgiasUserDto("Jane Doe", "jane@example.com"),
        Requester: null,
        Messages: messages);

    private static GorgiasMessageDto Message(
        long id,
        bool isPublic = true,
        bool fromAgent = false,
        string? strippedText = "hello",
        string? bodyText = "hello",
        string? sourceType = "email",
        DateTimeOffset? sentAt = null) => new(
        Id: id,
        Public: isPublic,
        FromAgent: fromAgent,
        StrippedText: strippedText,
        BodyText: bodyText,
        Sender: new GorgiasUserDto("Sender", "sender@example.com"),
        Source: new GorgiasMessageSourceDto(sourceType),
        CreatedDatetime: sentAt,
        SentDatetime: sentAt);
}
