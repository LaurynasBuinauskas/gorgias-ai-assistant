using Copilot.Domain;
using Copilot.Pipeline;

namespace Copilot.Tests;

/// <summary>
/// The transcript is the half of the prompt the client does not control — it arrives from
/// Gorgias and a long thread is unbounded — so it needs its own ceiling.
/// </summary>
public sealed class DraftPromptTrimmingTests
{
    [Fact]
    public void KeepsAnUntrimmedTranscriptIntact()
    {
        var ticket = Ticket(Message("First question"), Message("Second question"));

        var transcript = DraftPrompt.BuildTranscript(ticket, maxCharacters: 10_000);

        Assert.Contains("First question", transcript);
        Assert.Contains("Second question", transcript);
        Assert.DoesNotContain("Earlier messages omitted", transcript);
    }

    [Fact]
    public void DropsOldestMessagesAndSaysSoWhenOverTheCeiling()
    {
        var ticket = Ticket(
            Message(new string('o', 900)),
            Message(new string('m', 900)),
            Message("the newest question"));

        var transcript = DraftPrompt.BuildTranscript(ticket, maxCharacters: 1_000);

        Assert.Contains("the newest question", transcript);
        Assert.DoesNotContain(new string('o', 900), transcript);
        Assert.Contains("Earlier messages omitted", transcript);
    }

    [Fact]
    public void AlwaysKeepsTheNewestMessageEvenIfItAloneExceedsTheCeiling()
    {
        // Trimming to nothing would leave no question to answer, which is worse than a long prompt.
        var ticket = Ticket(Message(new string('x', 5_000)));

        var transcript = DraftPrompt.BuildTranscript(ticket, maxCharacters: 100);

        Assert.Contains(new string('x', 5_000), transcript);
    }

    [Fact]
    public void TranscriptStaysNearTheCeilingForAThreadOfManyMessages()
    {
        var ticket = Ticket([.. Enumerable.Range(0, 200).Select(i => Message($"Message {i} " + new string('y', 500)))]);

        var transcript = DraftPrompt.BuildTranscript(ticket, maxCharacters: 5_000);

        // Header, omission notice and trailer add a little; the body must not run away.
        Assert.True(transcript.Length < 6_000, $"Transcript was {transcript.Length} characters.");
        Assert.Contains("Message 199", transcript);
        Assert.DoesNotContain("Message 0 ", transcript);
    }

    private static TicketContext Ticket(params TicketMessage[] messages) => new()
    {
        Id = 42,
        Subject = "Order question",
        Status = "open",
        Customer = new TicketCustomer("Jane Doe", "jane@example.com"),
        Messages = messages,
    };

    private static TicketMessage Message(string text) => new()
    {
        Id = 1,
        FromAgent = false,
        IsInternalNote = false,
        SenderName = "Jane Doe",
        Text = text,
    };
}
