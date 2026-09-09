using System.Security.Cryptography;
using God2.ClassicServer.Protocol;

namespace God2.ClassicServer.Runtime.Tests;

public sealed class OfficialServerSelectionClosedLoopTests
{
    [Fact]
    public void Full_closed_loop_commits_projects_serializes_and_records_send_order()
    {
        var request = OfficialServerSelectionWireCodec.DecodeRequest(
            OfficialServerSelectionWireCodec.GoldenEncodedRequest.Span,
            ProtocolStage.CharacterList,
            OfficialServerSelectionWireCodec.ClientBuildId);
        Assert.True(request.Succeeded);
        var loop = new OfficialServerSelectionClosedLoop();

        var receipt = loop.Commit(Command("session-1", RequestHash()), OfficialServerSelectionWireCodec.GoldenResponseModel());
        var mutations = 0;
        receipt = loop.DispatchOutbox(receipt, serverId =>
        {
            Assert.Equal(request.Value!.SelectedServerId, serverId);
            mutations++;
        });
        receipt = loop.MarkNetworkSent(receipt);

        Assert.Equal(OfficialServerSelectionTransactionCode.Committed, receipt.Code);
        Assert.True(receipt.TransactionCommitted);
        Assert.True(receipt.OutboxDispatched);
        Assert.Equal(1, mutations);
        Assert.Equal(1, receipt.RuntimeMutationCount);
        Assert.Equal(1, receipt.NetworkSendCount);
        Assert.Equal(OfficialServerSelectionWireCodec.GoldenEncodedResponse.ToArray(), Convert.FromHexString(receipt.ResponseHex));
        Assert.True(Index(receipt, "TransactionCommitted") < Index(receipt, "RuntimeMutationApplied"));
        Assert.True(Index(receipt, "OutboxDispatched") < Index(receipt, "NetworkSend"));
    }

    [Fact]
    public void Duplicate_same_sequence_and_payload_mutates_runtime_once_and_replays_response()
    {
        var loop = new OfficialServerSelectionClosedLoop();
        var command = Command("session-duplicate", RequestHash());
        var first = loop.Commit(command, OfficialServerSelectionWireCodec.GoldenResponseModel());
        var mutationCount = 0;
        first = loop.DispatchOutbox(first, _ => mutationCount++);

        var duplicate = loop.Commit(command, OfficialServerSelectionWireCodec.GoldenResponseModel());
        duplicate = loop.DispatchOutbox(duplicate, _ => mutationCount++);

        Assert.Equal(OfficialServerSelectionTransactionCode.DuplicateCommitted, duplicate.Code);
        Assert.Equal(1, mutationCount);
        Assert.Equal(first.ResponseSha256, duplicate.ResponseSha256);
        Assert.Equal(first.TransactionId, duplicate.TransactionId);
    }

    [Fact]
    public void Same_sequence_with_different_payload_is_a_replay_conflict()
    {
        var loop = new OfficialServerSelectionClosedLoop();
        var first = loop.Commit(Command("session-conflict", RequestHash()), OfficialServerSelectionWireCodec.GoldenResponseModel());
        var conflict = loop.Commit(Command("session-conflict", new string('A', 64)), OfficialServerSelectionWireCodec.GoldenResponseModel());

        Assert.True(first.TransactionCommitted);
        Assert.Equal(OfficialServerSelectionTransactionCode.ReplayConflict, conflict.Code);
        Assert.Equal("wire.server_selection.sequence_payload_conflict", conflict.FailureCode);
        Assert.Equal(0, loop.Diagnostics().RuntimeMutationCount);
    }

    [Fact]
    public async Task Commit_race_has_one_receipt_and_one_runtime_mutation()
    {
        var loop = new OfficialServerSelectionClosedLoop();
        var command = Command("session-race", RequestHash());
        var receipts = await Task.WhenAll(Enumerable.Range(0, 32).Select(_ => Task.Run(() =>
            loop.Commit(command, OfficialServerSelectionWireCodec.GoldenResponseModel()))));
        var mutationCount = 0;
        foreach (var receipt in receipts)
        {
            loop.DispatchOutbox(receipt, _ => mutationCount++);
        }

        Assert.Single(loop.Snapshot());
        Assert.Equal(1, mutationCount);
        Assert.Single(receipts, receipt => receipt.Code == OfficialServerSelectionTransactionCode.Committed);
        Assert.Equal(31, receipts.Count(receipt => receipt.Code == OfficialServerSelectionTransactionCode.DuplicateCommitted));
    }

    [Fact]
    public void Failure_before_commit_rolls_back_without_journal_outbox_or_mutation()
    {
        var loop = new OfficialServerSelectionClosedLoop { FailurePoint = OfficialServerSelectionFailurePoint.BeforeCommit };

        var receipt = loop.Commit(Command("session-rollback", RequestHash()), OfficialServerSelectionWireCodec.GoldenResponseModel());

        Assert.Equal(OfficialServerSelectionTransactionCode.RolledBack, receipt.Code);
        Assert.False(receipt.TransactionCommitted);
        Assert.Empty(loop.Snapshot());
        Assert.Equal(0, loop.Diagnostics().PendingOutboxCount);
    }

    [Fact]
    public void Lost_commit_response_is_recovered_by_idempotent_replay()
    {
        var loop = new OfficialServerSelectionClosedLoop { FailurePoint = OfficialServerSelectionFailurePoint.CommitResponseLost };
        var command = Command("session-lost", RequestHash());

        var lost = loop.Commit(command, OfficialServerSelectionWireCodec.GoldenResponseModel());
        loop.FailurePoint = OfficialServerSelectionFailurePoint.None;
        var replay = loop.Commit(command, OfficialServerSelectionWireCodec.GoldenResponseModel());

        Assert.Equal(OfficialServerSelectionTransactionCode.RecoveryRequired, lost.Code);
        Assert.True(lost.TransactionCommitted);
        Assert.Equal(OfficialServerSelectionTransactionCode.DuplicateCommitted, replay.Code);
        Assert.Equal(lost.TransactionId, replay.TransactionId);
        Assert.Equal(lost.ResponseSha256, replay.ResponseSha256);
    }

    [Fact]
    public void Snapshot_restore_replays_pending_outbox_without_second_commit()
    {
        var first = new OfficialServerSelectionClosedLoop();
        var command = Command("session-recovery", RequestHash());
        var pending = first.Commit(command, OfficialServerSelectionWireCodec.GoldenResponseModel());
        var recovered = new OfficialServerSelectionClosedLoop();
        recovered.Restore(first.Snapshot());
        var mutations = 0;

        var dispatched = recovered.DispatchOutbox(pending, _ => mutations++);
        var duplicate = recovered.Commit(command, OfficialServerSelectionWireCodec.GoldenResponseModel());

        Assert.True(dispatched.OutboxDispatched);
        Assert.Equal(1, mutations);
        Assert.Equal(OfficialServerSelectionTransactionCode.DuplicateCommitted, duplicate.Code);
        Assert.Equal(1, recovered.Diagnostics().CommittedCount);
        Assert.Equal(0, recovered.Diagnostics().PendingOutboxCount);
    }

    [Fact]
    public void Receipt_tampering_is_rejected_during_recovery()
    {
        var first = new OfficialServerSelectionClosedLoop();
        var receipt = first.Commit(Command("session-tamper", RequestHash()), OfficialServerSelectionWireCodec.GoldenResponseModel());
        var tampered = receipt with { ResponseHex = "00" };

        Assert.Throws<InvalidDataException>(() => new OfficialServerSelectionClosedLoop().Restore([tampered]));
    }

    [Fact]
    public void Network_send_before_outbox_dispatch_is_rejected()
    {
        var loop = new OfficialServerSelectionClosedLoop();
        var committed = loop.Commit(Command("session-order", RequestHash()), OfficialServerSelectionWireCodec.GoldenResponseModel());

        var rejected = loop.MarkNetworkSent(committed);

        Assert.Equal(OfficialServerSelectionTransactionCode.RolledBack, rejected.Code);
        Assert.Equal("wire.server_selection.send_before_commit_or_outbox", rejected.FailureCode);
        Assert.Equal(0, loop.Diagnostics().NetworkSendCount);
    }

    [Fact]
    public void Invalid_client_sequence_never_commits()
    {
        var loop = new OfficialServerSelectionClosedLoop();
        var command = Command("session-sequence", RequestHash()) with { ClientSequence = 2 };

        var result = loop.Commit(command, OfficialServerSelectionWireCodec.GoldenResponseModel());

        Assert.Equal(OfficialServerSelectionTransactionCode.RolledBack, result.Code);
        Assert.Empty(loop.Snapshot());
    }

    [Fact]
    public void Session_cleanup_removes_committed_receipt_and_is_idempotent()
    {
        var loop = new OfficialServerSelectionClosedLoop();
        var command = Command("session-cleanup", RequestHash());
        _ = loop.Commit(command, OfficialServerSelectionWireCodec.GoldenResponseModel());

        Assert.True(loop.RemoveSession(command.SessionId));
        Assert.False(loop.RemoveSession(command.SessionId));
        Assert.Equal(0, loop.Diagnostics().ReceiptCount);
        Assert.Equal(0, loop.Diagnostics().PendingOutboxCount);
    }

    private static int Index(OfficialServerSelectionReceipt receipt, string stage) =>
        receipt.OrderedStages.ToList().IndexOf(stage);

    private static string RequestHash() =>
        Convert.ToHexString(SHA256.HashData(OfficialServerSelectionWireCodec.GoldenEncodedRequest.Span));

    private static OfficialServerSelectionCanonicalCommand Command(string sessionId, string requestHash) =>
        new(
            sessionId,
            ClientSequence: 1,
            CorrelationId: Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(sessionId))),
            OfficialServerSelectionWireCodec.SupportedServerId,
            OfficialServerSelectionWireCodec.ClientBuildId,
            requestHash);
}
