namespace God2.ClassicServer.Runtime;

public sealed record OfficialNpcInteractionCoverageEntry(
    ushort ClientEntityHandle,
    string InteractionFamily,
    string OpenRequest,
    string DialogResponse,
    string CompletionStatus,
    string MissingOperations,
    string EvidenceStatus,
    string EvidenceId);

public sealed record OfficialNpcInteractionCoverageGap(
    ushort ClientEntityHandle,
    string ObservedRequest,
    string MissingEvidence,
    string EvidenceStatus,
    string EvidenceId);

public static class OfficialNpcInteractionCoverage
{
    public static IReadOnlyList<OfficialNpcInteractionCoverageEntry> Snapshot()
    {
        var entries = OfficialNpcInteractionWireCodec.SnapshotVerifiedDialogHandles()
            .Select(handle => new OfficialNpcInteractionCoverageEntry(
                handle,
                "Dialog",
                "C2S 0x37/8",
                handle == 3793 ? "S2C 0x7A/32" : "S2C 0x7A/58",
                handle == 3793 ? "CompleteObservedDialogSlice" : "OpenOnly",
                handle == 3793
                    ? "Other selector values and independent 0x86 semantics remain blocked"
                    : "dialog option semantics; 0x86 close semantics",
                "Verified",
                handle == 3793
                    ? OfficialNpcInteractionWireCodec.LiveDialogSelectionEvidenceId
                    : OfficialNpcInteractionWireCodec.EvidenceId))
            .ToList();

        entries.Add(new OfficialNpcInteractionCoverageEntry(
            OfficialMerchantTransactionWireCodec.LiveStageMerchantHandle,
            "Merchant",
            "C2S 0x37/8 + 0x85/10",
            "S2C 0x7A/31 + 0x68/16",
            "CompleteMerchantSlice",
            "None",
            "Verified",
            OfficialMerchantTransactionWireCodec.EvidenceId));

        return Array.AsReadOnly(entries
            .OrderBy(entry => entry.ClientEntityHandle)
            .ToArray());
    }

    public static bool HasVerifiedOpenProfile(ushort clientEntityHandle) =>
        OfficialNpcInteractionWireCodec.HasVerifiedDialogProfile(clientEntityHandle) ||
        OfficialMerchantTransactionWireCodec.HasVerifiedObservedDialogProfile(clientEntityHandle);

    public static IReadOnlyList<OfficialNpcInteractionCoverageGap> SnapshotBlockedGaps() =>
        Array.AsReadOnly<OfficialNpcInteractionCoverageGap>(
        [
            new(3784, "080037C80E0000B9", "No correlated S2C dialog response", "BlockedMissingResponse",
                "LiveRecovery/attempt-759-encode-5848"),
            new(3960, "080037780F00006A", "No correlated S2C dialog response", "BlockedMissingResponse",
                "LiveRecovery/attempt-759-encode-6196"),
            new(5004, "0800378C13000082", "No correlated S2C dialog response", "BlockedMissingResponse",
                "LiveRecovery/attempt-759-encode-6191")
        ]);
}
