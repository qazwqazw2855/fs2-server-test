using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;
using God2.ClassicServer.Protocol;

namespace God2.ClassicServer.Runtime;

public enum OfficialServerSelectionTransactionCode
{
    Committed,
    DuplicateCommitted,
    ReplayConflict,
    RolledBack,
    RecoveryRequired
}

public enum OfficialServerSelectionFailurePoint
{
    None,
    BeforeCommit,
    CommitResponseLost
}

public sealed record OfficialServerSelectionCanonicalCommand(
    string SessionId,
    long ClientSequence,
    string CorrelationId,
    byte SelectedServerId,
    string ClientBuildId,
    string EncodedRequestSha256);

public sealed record OfficialServerSelectionReceipt(
    string TransactionId,
    string JournalId,
    string OutboxId,
    string SafeSessionId,
    string IdempotencyKeyHash,
    long ClientSequence,
    string CorrelationId,
    string PayloadSha256,
    string ResponseSha256,
    string ResponseHex,
    byte SelectedServerId,
    OfficialServerSelectionTransactionCode Code,
    bool TransactionCommitted,
    bool OutboxDispatched,
    int RuntimeMutationCount,
    int NetworkSendCount,
    string FailureCode,
    IReadOnlyList<string> OrderedStages);

public sealed record OfficialServerSelectionDiagnostics(
    int ReceiptCount,
    int CommittedCount,
    int PendingOutboxCount,
    int RuntimeMutationCount,
    int NetworkSendCount);

public sealed class OfficialServerSelectionClosedLoop
{
    private readonly object _gate = new();
    private readonly Dictionary<string, OfficialServerSelectionReceipt> _receipts = new(StringComparer.Ordinal);

    public OfficialServerSelectionFailurePoint FailurePoint { get; set; }

    public OfficialServerSelectionReceipt Commit(
        OfficialServerSelectionCanonicalCommand command,
        OfficialCharacterListBootstrapWireModel responseModel)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(responseModel);
        if (string.IsNullOrWhiteSpace(command.SessionId) ||
            command.ClientSequence != 1 ||
            string.IsNullOrWhiteSpace(command.CorrelationId) ||
            !string.Equals(command.ClientBuildId, OfficialServerSelectionWireCodec.ClientBuildId, StringComparison.Ordinal) ||
            command.SelectedServerId != OfficialServerSelectionWireCodec.SupportedServerId ||
            !IsSha256(command.EncodedRequestSha256))
        {
            return Failure(command, OfficialServerSelectionTransactionCode.RolledBack, "wire.server_selection.command_invalid");
        }

        var serialized = OfficialServerSelectionWireCodec.SerializeResponse(responseModel);
        if (!serialized.Succeeded)
        {
            return Failure(command, OfficialServerSelectionTransactionCode.RolledBack, serialized.Error.Code);
        }

        var responseHex = Convert.ToHexString(serialized.Value.Span);
        var responseHash = Sha256Hex(serialized.Value.Span);
        var payloadHash = Sha256Hex(
            $"{command.ClientBuildId}|{command.ClientSequence}|{command.SelectedServerId}|{command.EncodedRequestSha256}");
        var key = Sha256Hex($"{command.SessionId}|{command.ClientSequence}");

        lock (_gate)
        {
            if (_receipts.TryGetValue(key, out var existing))
            {
                return existing.PayloadSha256 == payloadHash
                    ? Freeze(existing with { Code = OfficialServerSelectionTransactionCode.DuplicateCommitted })
                    : Freeze(existing with
                    {
                        Code = OfficialServerSelectionTransactionCode.ReplayConflict,
                        FailureCode = "wire.server_selection.sequence_payload_conflict"
                    });
            }

            if (FailurePoint == OfficialServerSelectionFailurePoint.BeforeCommit)
            {
                return Failure(command, OfficialServerSelectionTransactionCode.RolledBack, "wire.server_selection.before_commit_injected");
            }

            var transactionId = Sha256Hex($"transaction|{key}|{payloadHash}");
            var journalId = Sha256Hex($"journal|{transactionId}|1");
            var outboxId = Sha256Hex($"outbox|{transactionId}|1");
            var receipt = new OfficialServerSelectionReceipt(
                transactionId,
                journalId,
                outboxId,
                SafeId(command.SessionId),
                key,
                command.ClientSequence,
                command.CorrelationId,
                payloadHash,
                responseHash,
                responseHex,
                command.SelectedServerId,
                OfficialServerSelectionTransactionCode.Committed,
                TransactionCommitted: true,
                OutboxDispatched: false,
                RuntimeMutationCount: 0,
                NetworkSendCount: 0,
                string.Empty,
                FreezeStages(
                [
                    "ClientRequest",
                    "ServerFrameDecoded",
                    "FamilyDecoded",
                    "CanonicalCommand",
                    "RuntimeCommandPrepared",
                    "SerializerPrepared",
                    "JournalCommitted",
                    "OutboxAppended",
                    "TransactionCommitted"
                ]));
            _receipts[key] = receipt;

            return FailurePoint == OfficialServerSelectionFailurePoint.CommitResponseLost
                ? Freeze(receipt with
                {
                    Code = OfficialServerSelectionTransactionCode.RecoveryRequired,
                    FailureCode = "wire.server_selection.commit_response_lost"
                })
                : Freeze(receipt);
        }
    }

    public OfficialServerSelectionReceipt DispatchOutbox(
        OfficialServerSelectionReceipt receipt,
        Action<byte> runtimeMutation)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        ArgumentNullException.ThrowIfNull(runtimeMutation);
        lock (_gate)
        {
            var key = FindKey(receipt);
            if (key is null || !_receipts.TryGetValue(key, out var committed))
            {
                return receipt with
                {
                    Code = OfficialServerSelectionTransactionCode.RolledBack,
                    FailureCode = "wire.server_selection.receipt_not_found"
                };
            }

            if (committed.OutboxDispatched)
            {
                return Freeze(committed with { Code = OfficialServerSelectionTransactionCode.DuplicateCommitted });
            }

            runtimeMutation(committed.SelectedServerId);
            var updated = committed with
            {
                OutboxDispatched = true,
                RuntimeMutationCount = 1,
                OrderedStages = FreezeStages(committed.OrderedStages.Append("RuntimeMutationApplied").Append("OutboxDispatched"))
            };
            _receipts[key] = updated;
            return Freeze(updated);
        }
    }

    public OfficialServerSelectionReceipt MarkNetworkSent(OfficialServerSelectionReceipt receipt)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        lock (_gate)
        {
            var key = FindKey(receipt);
            if (key is null || !_receipts.TryGetValue(key, out var committed) || !committed.OutboxDispatched)
            {
                return receipt with
                {
                    Code = OfficialServerSelectionTransactionCode.RolledBack,
                    FailureCode = "wire.server_selection.send_before_commit_or_outbox"
                };
            }

            var updated = committed with
            {
                NetworkSendCount = checked(committed.NetworkSendCount + 1),
                OrderedStages = committed.NetworkSendCount == 0
                    ? FreezeStages(committed.OrderedStages.Append("NetworkSend"))
                    : committed.OrderedStages
            };
            _receipts[key] = updated;
            return Freeze(updated);
        }
    }

    public IReadOnlyList<OfficialServerSelectionReceipt> Snapshot()
    {
        lock (_gate)
        {
            return Array.AsReadOnly(_receipts.Values
                .OrderBy(value => value.TransactionId, StringComparer.Ordinal)
                .Select(Freeze)
                .ToArray());
        }
    }

    public void Restore(IEnumerable<OfficialServerSelectionReceipt> receipts)
    {
        ArgumentNullException.ThrowIfNull(receipts);
        lock (_gate)
        {
            foreach (var receipt in receipts)
            {
                if (!receipt.TransactionCommitted ||
                    !IsSha256(receipt.TransactionId) ||
                    !IsSha256(receipt.IdempotencyKeyHash) ||
                    !IsSha256(receipt.PayloadSha256) ||
                    !IsSha256(receipt.ResponseSha256) ||
                    Sha256Hex(Convert.FromHexString(receipt.ResponseHex)) != receipt.ResponseSha256)
                {
                    throw new InvalidDataException("Closed-loop recovery receipt failed integrity validation.");
                }

                _receipts[receipt.IdempotencyKeyHash] = Freeze(receipt);
            }
        }
    }

    public OfficialServerSelectionDiagnostics Diagnostics()
    {
        lock (_gate)
        {
            return new OfficialServerSelectionDiagnostics(
                _receipts.Count,
                _receipts.Values.Count(value => value.TransactionCommitted),
                _receipts.Values.Count(value => !value.OutboxDispatched),
                _receipts.Values.Sum(value => value.RuntimeMutationCount),
                _receipts.Values.Sum(value => value.NetworkSendCount));
        }
    }

    public bool RemoveSession(string sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return false;
        }

        var key = Sha256Hex($"{sessionId}|1");
        lock (_gate)
        {
            return _receipts.Remove(key);
        }
    }

    private string? FindKey(OfficialServerSelectionReceipt receipt) =>
        _receipts.FirstOrDefault(pair =>
            pair.Value.TransactionId == receipt.TransactionId &&
            pair.Value.PayloadSha256 == receipt.PayloadSha256).Key;

    private static OfficialServerSelectionReceipt Failure(
        OfficialServerSelectionCanonicalCommand command,
        OfficialServerSelectionTransactionCode code,
        string failureCode) =>
        new(
            string.Empty,
            string.Empty,
            string.Empty,
            SafeId(command.SessionId ?? string.Empty),
            string.Empty,
            command.ClientSequence,
            command.CorrelationId ?? string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            command.SelectedServerId,
            code,
            TransactionCommitted: false,
            OutboxDispatched: false,
            RuntimeMutationCount: 0,
            NetworkSendCount: 0,
            failureCode,
            FreezeStages(["Rejected"]));

    private static OfficialServerSelectionReceipt Freeze(OfficialServerSelectionReceipt receipt) =>
        receipt with { OrderedStages = FreezeStages(receipt.OrderedStages) };

    private static IReadOnlyList<string> FreezeStages(IEnumerable<string> stages) =>
        new ReadOnlyCollection<string>(stages.ToArray());

    private static bool IsSha256(string value) =>
        value.Length == 64 && value.All(character => Uri.IsHexDigit(character));

    private static string SafeId(string value) => Sha256Hex(value)[..16];

    private static string Sha256Hex(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static string Sha256Hex(ReadOnlySpan<byte> value) =>
        Convert.ToHexString(SHA256.HashData(value));
}
