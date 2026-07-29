using Copilot.Api.Contracts;

namespace Copilot.Tests;

public sealed class AnchorTelemetryRequestTests
{
    [Theory]
    [InlineData("docked", AnchorMode.Docked)]
    [InlineData("floating", AnchorMode.Floating)]
    [InlineData("Floating", AnchorMode.Floating)]
    public void AcceptsTheTwoKnownModes(string mode, AnchorMode expected)
    {
        var request = Request(mode: mode);

        Assert.True(request.TryValidate(out var parsed, out var account));
        Assert.Equal(expected, parsed);
        Assert.Equal("timeresistance", account);
    }

    [Theory]
    [InlineData("hidden")]
    [InlineData("")]
    [InlineData("docked; DROP TABLE")]
    public void RejectsAnUnknownMode(string mode) =>
        Assert.False(Request(mode: mode).TryValidate(out _, out _));

    [Fact]
    public void StripsControlCharactersSoLogLinesCannotBeForged()
    {
        var request = Request(account: "acme\nfail: fabricated log line\r\t");

        Assert.True(request.TryValidate(out _, out var account));
        Assert.Equal("acmefail: fabricated log line", account);
        Assert.DoesNotContain('\n', account);
        Assert.DoesNotContain('\r', account);
    }

    [Fact]
    public void ClampsAnOverlongAccount()
    {
        var request = Request(account: new string('a', 500));

        Assert.True(request.TryValidate(out _, out var account));
        Assert.Equal(64, account.Length);
    }

    [Fact]
    public void RejectsAnAccountThatIsOnlyWhitespaceOrControlCharacters() =>
        Assert.False(Request(account: "\n\t  \r").TryValidate(out _, out _));

    private static AnchorTelemetryRequestV1 Request(
        string account = "timeresistance",
        string mode = "floating") => new() { Account = account, Mode = mode };
}
