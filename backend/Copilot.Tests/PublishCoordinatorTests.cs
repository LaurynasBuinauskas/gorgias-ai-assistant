using System.Text.Json;
using Copilot.Api.Uploads;
using Microsoft.Extensions.Logging.Abstractions;

namespace Copilot.Tests;

/// <summary>
/// The rules between "someone clicked publish" and "the workflow runs": only staged blobs,
/// attributed, one at a time, fail closed without a token, and rollback restores the state
/// before the newest ledger — pure git when there is nothing earlier.
/// </summary>
public sealed class PublishCoordinatorTests
{
    private readonly FakeDrafts _drafts = new();
    private readonly FakeState _state = new();
    private readonly FakeTrigger _trigger = new();

    private PublishCoordinator Coordinator() =>
        new(_drafts, _state, _trigger, NullLogger<PublishCoordinator>.Instance);

    [Fact]
    public async Task PublishesAStagedBlobAndRecordsTheQueuedState()
    {
        _drafts.Add("DE/returns/one.md");

        var decision = await Coordinator().StartPublishAsync(
            ["DE/returns/one.md"], "Rasa", CancellationToken.None);

        Assert.NotNull(decision.PublishId);
        var call = Assert.Single(_trigger.Calls);
        Assert.Equal((decision.PublishId, "publish", "Rasa"), (call.PublishId, call.Mode, call.PublishedBy));
        Assert.Equal(["DE/returns/one.md"], call.Blobs);
        Assert.Equal(decision.PublishId, _state.Inflight);
        Assert.Equal("queued", _state.Statuses[decision.PublishId!].Step);
    }

    [Fact]
    public async Task RefusesBlobsThatAreNotStaged()
    {
        _drafts.Add("DE/returns/one.md");

        var decision = await Coordinator().StartPublishAsync(
            ["DE/returns/one.md", "US/warranty/ghost.md"], "Rasa", CancellationToken.None);

        Assert.Null(decision.PublishId);
        Assert.Contains("ghost", decision.Refusal);
        Assert.Empty(_trigger.Calls);
    }

    [Fact]
    public async Task RefusesWithoutAttributionOrSelection()
    {
        _drafts.Add("DE/returns/one.md");

        Assert.Null((await Coordinator().StartPublishAsync(
            [], "Rasa", CancellationToken.None)).PublishId);
        Assert.Null((await Coordinator().StartPublishAsync(
            ["DE/returns/one.md"], "  ", CancellationToken.None)).PublishId);
        Assert.Empty(_trigger.Calls);
    }

    [Fact]
    public async Task FailsClosedWhenTheWorkflowTokenIsMissing()
    {
        _drafts.Add("DE/returns/one.md");
        _trigger.Configured = false;

        var decision = await Coordinator().StartPublishAsync(
            ["DE/returns/one.md"], "Rasa", CancellationToken.None);

        Assert.Null(decision.PublishId);
        Assert.Contains("not configured", decision.Refusal);
        Assert.Empty(_trigger.Calls);
    }

    [Fact]
    public async Task RefusesASecondPublishWhileOneRuns()
    {
        _drafts.Add("DE/returns/one.md");
        var coordinator = Coordinator();
        var first = await coordinator.StartPublishAsync(
            ["DE/returns/one.md"], "Rasa", CancellationToken.None);

        var second = await coordinator.StartPublishAsync(
            ["DE/returns/one.md"], "Rasa", CancellationToken.None);

        Assert.Null(second.PublishId);
        Assert.Contains(first.PublishId!, second.Refusal);
        Assert.Single(_trigger.Calls);
    }

    [Fact]
    public async Task AllowsANewPublishOnceTheLastIsTerminal()
    {
        _drafts.Add("DE/returns/one.md");
        var coordinator = Coordinator();
        var first = await coordinator.StartPublishAsync(
            ["DE/returns/one.md"], "Rasa", CancellationToken.None);
        _state.Statuses[first.PublishId!] = _state.Statuses[first.PublishId!]
            with { Step = "published", State = "succeeded" };

        var second = await coordinator.StartPublishAsync(
            ["DE/returns/one.md"], "Rasa", CancellationToken.None);

        Assert.NotNull(second.PublishId);
        Assert.Equal(2, _trigger.Calls.Count);
    }

    [Fact]
    public async Task AFailedDispatchNeverTakesTheLock()
    {
        // The first live run wedged on exactly this: dispatch failed after the lock was
        // taken, leaving a queued publish nothing would ever finish.
        _drafts.Add("DE/returns/one.md");
        _trigger.ThrowOnTrigger = true;
        var coordinator = Coordinator();

        var failed = await coordinator.StartPublishAsync(
            ["DE/returns/one.md"], "Rasa", CancellationToken.None);

        Assert.Null(failed.PublishId);
        Assert.Contains("could not be handed to the workflow", failed.Refusal);
        Assert.Null(_state.Inflight);
        Assert.Contains(_state.Statuses.Values,
            s => s is { Step: "trigger-failed", State: "failed" });

        _trigger.ThrowOnTrigger = false;
        var retry = await coordinator.StartPublishAsync(
            ["DE/returns/one.md"], "Rasa", CancellationToken.None);
        Assert.NotNull(retry.PublishId);
    }

    [Fact]
    public async Task AStaleQueuedLockIsIgnored()
    {
        // A dispatched run replaces "queued" within a minute; ten minutes of queued means
        // it never started and must not wedge publishing forever.
        _drafts.Add("DE/returns/one.md");
        _state.Statuses["ghost"] = new PublishStatus
        {
            PublishId = "ghost", Step = "queued", State = "running",
            UpdatedAt = DateTimeOffset.UtcNow.AddMinutes(-11),
        };
        await _state.WriteInflightAsync("ghost", CancellationToken.None);

        var decision = await Coordinator().StartPublishAsync(
            ["DE/returns/one.md"], "Rasa", CancellationToken.None);

        Assert.NotNull(decision.PublishId);
    }

    [Fact]
    public async Task RollbackPublishesThePreviousLedgersBlobs()
    {
        _state.Ledgers.Add(Ledger("older", ["DE/returns/old.md"], new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero)));
        _state.Ledgers.Add(Ledger("newest", ["DE/returns/new.md"], new DateTimeOffset(2026, 8, 14, 0, 0, 0, TimeSpan.Zero)));

        var decision = await Coordinator().StartRollbackAsync("Rasa", CancellationToken.None);

        var call = Assert.Single(_trigger.Calls);
        Assert.Equal("rollback", call.Mode);
        Assert.Equal(["DE/returns/old.md"], call.Blobs);
        Assert.NotNull(decision.PublishId);
    }

    [Fact]
    public async Task RollbackOfTheFirstPublishRestoresPureGit()
    {
        _state.Ledgers.Add(Ledger("only", ["DE/returns/new.md"], DateTimeOffset.UtcNow));

        await Coordinator().StartRollbackAsync("Rasa", CancellationToken.None);

        Assert.Empty(Assert.Single(_trigger.Calls).Blobs);
    }

    [Fact]
    public async Task RollbackWithNoHistoryIsRefused()
    {
        var decision = await Coordinator().StartRollbackAsync("Rasa", CancellationToken.None);

        Assert.Null(decision.PublishId);
        Assert.Empty(_trigger.Calls);
    }

    private static PublishLedger Ledger(string id, string[] blobs, DateTimeOffset at) => new()
    {
        PublishId = id,
        Mode = "publish",
        PublishedBy = "Rasa",
        PublishedAt = at,
        Blobs = blobs,
        SnapshotIndex = $"knowledge-stage-{id}",
    };

    private sealed class FakeDrafts : IPolicyDraftStore
    {
        private readonly List<PolicyDraft> _drafts = [];

        public void Add(string blobName) => _drafts.Add(new PolicyDraft
        {
            BlobName = blobName,
            FileName = Path.GetFileName(blobName),
            Market = "DE",
            Topic = "returns",
            UploadedBy = "Rasa",
            UploadedAt = DateTimeOffset.UtcNow,
            SizeBytes = 100,
        });

        public Task<PolicyDraft> SaveAsync(PolicyDraft draft, Stream content, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<PolicyDraft>> ListAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<PolicyDraft>>(_drafts);
    }

    private sealed class FakeState : IPublishStateStore
    {
        public Dictionary<string, PublishStatus> Statuses { get; } = [];

        public List<PublishLedger> Ledgers { get; } = [];

        public string? Inflight { get; private set; }

        public Task<PublishStatus?> ReadStatusAsync(string publishId, CancellationToken ct) =>
            Task.FromResult(Statuses.GetValueOrDefault(publishId));

        public Task<JsonElement?> ReadValidationReportAsync(string publishId, CancellationToken ct) =>
            Task.FromResult<JsonElement?>(null);

        public Task WriteQueuedStatusAsync(string publishId, CancellationToken ct)
        {
            Statuses[publishId] = new PublishStatus
            {
                PublishId = publishId, Step = "queued", State = "running",
                UpdatedAt = DateTimeOffset.UtcNow,
            };
            return Task.CompletedTask;
        }

        public Task WriteTriggerFailedStatusAsync(string publishId, CancellationToken ct)
        {
            Statuses[publishId] = new PublishStatus
            {
                PublishId = publishId, Step = "trigger-failed", State = "failed",
                UpdatedAt = DateTimeOffset.UtcNow,
            };
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<PublishLedger>> ListLedgersAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<PublishLedger>>(
                [.. Ledgers.OrderByDescending(l => l.PublishedAt)]);

        public Task<string?> ReadInflightAsync(CancellationToken ct) => Task.FromResult(Inflight);

        public Task WriteInflightAsync(string publishId, CancellationToken ct)
        {
            Inflight = publishId;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeTrigger : IPublishTrigger
    {
        public sealed record Call(string PublishId, IReadOnlyList<string> Blobs, string PublishedBy, string Mode);

        public bool Configured { get; set; } = true;

        public bool ThrowOnTrigger { get; set; }

        public List<Call> Calls { get; } = [];

        public bool IsConfigured => Configured;

        public Task TriggerAsync(string publishId, IReadOnlyList<string> blobs, string publishedBy,
            string mode, CancellationToken ct)
        {
            if (ThrowOnTrigger)
            {
                throw new InvalidOperationException("GitHub refused the workflow dispatch: 403");
            }

            Calls.Add(new Call(publishId, blobs, publishedBy, mode));
            return Task.CompletedTask;
        }
    }
}
