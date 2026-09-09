using System.Buffers.Binary;
using System.Collections.ObjectModel;
using God2.ClassicServer.Protocol;

namespace God2.ClassicServer.Runtime;

public enum OfficialProtocolFamilyInferenceStatus
{
    Candidate,
    Correlated,
    Verified
}

public sealed record OfficialFamilyFieldBinding(
    string Name,
    int Offset,
    int Width);

public sealed record OfficialFamilyByteConstraint(
    int Offset,
    byte Value);

public sealed record OfficialFamilyFrameRule(
    string Role,
    PacketDirection Direction,
    byte Opcode,
    int FrameLength,
    IReadOnlyList<OfficialFamilyFieldBinding> Bindings,
    IReadOnlyList<OfficialFamilyByteConstraint>? Constraints = null);

public sealed record OfficialProtocolFamilySignature(
    string Family,
    string Operation,
    IReadOnlyList<OfficialFamilyFrameRule> Rules,
    int MinimumCorrelatedFields,
    int MaximumSequenceSpan,
    bool EvidenceVerified,
    string EvidenceId);

public sealed record OfficialFamilyObservedFrame(
    long Sequence,
    PacketDirection Direction,
    ReadOnlyMemory<byte> DecodedFrame);

public sealed record OfficialProtocolFamilyInference(
    string Family,
    string Operation,
    OfficialProtocolFamilyInferenceStatus Status,
    IReadOnlyList<long> MatchedSequences,
    IReadOnlyDictionary<string, ulong> CorrelatedFields,
    int CorrelatedFieldCount,
    string EvidenceId);

/// <summary>
/// Matches ordered packet-family signatures and proves shared identity fields
/// across requests and responses. Signatures are data; the matcher has no
/// operation-specific branches.
/// </summary>
public sealed class OfficialProtocolFamilyInferenceEngine
{
    private readonly IReadOnlyList<OfficialProtocolFamilySignature> _signatures;

    public OfficialProtocolFamilyInferenceEngine(IEnumerable<OfficialProtocolFamilySignature> signatures)
    {
        ArgumentNullException.ThrowIfNull(signatures);
        _signatures = Array.AsReadOnly(signatures.ToArray());
        if (_signatures.Count == 0 || _signatures.Any(signature =>
                string.IsNullOrWhiteSpace(signature.Family) ||
                string.IsNullOrWhiteSpace(signature.Operation) ||
                signature.Rules.Count == 0 ||
                signature.MinimumCorrelatedFields < 0 ||
                signature.MaximumSequenceSpan < 0))
        {
            throw new ArgumentException("At least one valid family signature is required.", nameof(signatures));
        }
    }

    public static OfficialProtocolFamilyInferenceEngine CreateVerifiedCurrentBuild() =>
        new(CurrentBuildSignatures());

    public IReadOnlyList<OfficialProtocolFamilyInference> Infer(
        IEnumerable<OfficialFamilyObservedFrame> observations)
    {
        ArgumentNullException.ThrowIfNull(observations);
        var frames = observations
            .Where(IsValidFrame)
            .OrderBy(frame => frame.Sequence)
            .ToArray();
        var results = new List<OfficialProtocolFamilyInference>();

        foreach (var signature in _signatures)
        {
            if (TryMatch(signature, frames, out var result))
            {
                results.Add(result);
            }
        }

        return Array.AsReadOnly(results
            .OrderBy(result => result.MatchedSequences[0])
            .ThenBy(result => result.Family, StringComparer.Ordinal)
            .ThenBy(result => result.Operation, StringComparer.Ordinal)
            .ToArray());
    }

    private static bool TryMatch(
        OfficialProtocolFamilySignature signature,
        IReadOnlyList<OfficialFamilyObservedFrame> frames,
        out OfficialProtocolFamilyInference result)
    {
        result = null!;
        for (var start = 0; start < frames.Count; start++)
        {
            var values = new Dictionary<string, ulong>(StringComparer.Ordinal);
            var occurrences = new Dictionary<string, int>(StringComparer.Ordinal);
            var sequences = new List<long>();
            var cursor = start;
            var failed = false;

            foreach (var rule in signature.Rules)
            {
                var found = false;
                while (cursor < frames.Count)
                {
                    var frame = frames[cursor++];
                    if (sequences.Count > 0 && frame.Sequence - sequences[0] > signature.MaximumSequenceSpan)
                    {
                        failed = true;
                        break;
                    }

                    if (!Matches(rule, frame, values, occurrences))
                    {
                        continue;
                    }

                    sequences.Add(frame.Sequence);
                    found = true;
                    break;
                }

                if (!found)
                {
                    failed = true;
                    break;
                }
            }

            if (failed)
            {
                continue;
            }

            var correlatedCount = occurrences.Count(entry => entry.Value > 1);
            if (correlatedCount < signature.MinimumCorrelatedFields)
            {
                continue;
            }

            result = new OfficialProtocolFamilyInference(
                signature.Family,
                signature.Operation,
                signature.EvidenceVerified
                    ? OfficialProtocolFamilyInferenceStatus.Verified
                    : correlatedCount > 0
                        ? OfficialProtocolFamilyInferenceStatus.Correlated
                        : OfficialProtocolFamilyInferenceStatus.Candidate,
                sequences.AsReadOnly(),
                new ReadOnlyDictionary<string, ulong>(values),
                correlatedCount,
                signature.EvidenceId);
            return true;
        }

        return false;
    }

    private static bool Matches(
        OfficialFamilyFrameRule rule,
        OfficialFamilyObservedFrame observation,
        IDictionary<string, ulong> values,
        IDictionary<string, int> occurrences)
    {
        var frame = observation.DecodedFrame.Span;
        if (observation.Direction != rule.Direction ||
            frame.Length != rule.FrameLength ||
            frame[2] != rule.Opcode)
        {
            return false;
        }

        foreach (var constraint in rule.Constraints ?? Array.Empty<OfficialFamilyByteConstraint>())
        {
            if (constraint.Offset < 0 ||
                constraint.Offset >= frame.Length ||
                frame[constraint.Offset] != constraint.Value)
            {
                return false;
            }
        }

        var extracted = new List<(string Name, ulong Value)>();
        foreach (var binding in rule.Bindings)
        {
            if (!TryRead(frame, binding.Offset, binding.Width, out var value) ||
                values.TryGetValue(binding.Name, out var existing) && existing != value)
            {
                return false;
            }

            extracted.Add((binding.Name, value));
        }

        foreach (var field in extracted)
        {
            values[field.Name] = field.Value;
            occurrences[field.Name] = occurrences.TryGetValue(field.Name, out var count) ? count + 1 : 1;
        }

        return true;
    }

    private static bool TryRead(ReadOnlySpan<byte> frame, int offset, int width, out ulong value)
    {
        value = 0;
        if (offset < 0 || offset + width > frame.Length)
        {
            return false;
        }

        value = width switch
        {
            1 => frame[offset],
            2 => BinaryPrimitives.ReadUInt16LittleEndian(frame[offset..]),
            4 => BinaryPrimitives.ReadUInt32LittleEndian(frame[offset..]),
            8 => BinaryPrimitives.ReadUInt64LittleEndian(frame[offset..]),
            _ => 0
        };
        return width is 1 or 2 or 4 or 8;
    }

    private static bool IsValidFrame(OfficialFamilyObservedFrame observation)
    {
        var frame = observation.DecodedFrame.Span;
        return observation.Sequence >= 0 &&
            observation.Direction is PacketDirection.ClientToServer or PacketDirection.ServerToClient &&
            frame.Length >= 4 &&
            BinaryPrimitives.ReadUInt16LittleEndian(frame) == frame.Length &&
            OfficialLoginWireTransform.ComputeChecksum(frame) == frame[^1];
    }

    private static IReadOnlyList<OfficialProtocolFamilySignature> CurrentBuildSignatures()
    {
        const string evidence = OfficialMerchantTransactionWireCodec.EvidenceId;
        return Array.AsReadOnly<OfficialProtocolFamilySignature>(
        [
            new(
                "NpcDialog",
                "Open",
                [
                    Rule("OpenRequest", PacketDirection.ClientToServer, 0x37, 8, Field("EntityHandle", 3, 2)),
                    Rule("DialogResponse", PacketDirection.ServerToClient, 0x7A, 32, Field("EntityHandle", 7, 2))
                ],
                MinimumCorrelatedFields: 1,
                MaximumSequenceSpan: 8,
                EvidenceVerified: true,
                OfficialNpcInteractionWireCodec.LiveDialogEvidenceId),
            new(
                "Merchant",
                "Open",
                [
                    Rule("EntitySpawn", PacketDirection.ServerToClient, 0x72, 24, Field("EntityHandle", 3, 2)),
                    Rule("Selection", PacketDirection.ClientToServer, 0x85, 10, Field("EntityHandle", 3, 2)),
                    Rule("Dialog", PacketDirection.ServerToClient, 0x7A, 31, Field("EntityHandle", 7, 2)),
                    Rule("ShopOpen", PacketDirection.ServerToClient, 0x68, 16, Field("EntityHandle", 3, 2))
                ],
                MinimumCorrelatedFields: 1,
                MaximumSequenceSpan: 64,
                EvidenceVerified: true,
                evidence),
            new(
                "Merchant",
                "Buy",
                [
                    Rule(
                        "BuyRequest",
                        PacketDirection.ClientToServer,
                        OfficialMerchantTransactionWireCodec.TransactionOpcode,
                        OfficialMerchantTransactionWireCodec.TransactionFrameLength,
                        [Field("EntityHandle", 3, 2), Field("ObjectToken", 5, 2)],
                        new OfficialFamilyByteConstraint(8, (byte)OfficialMerchantOperation.Buy)),
                    Rule(
                        "BuyResult",
                        PacketDirection.ServerToClient,
                        OfficialMerchantTransactionWireCodec.PurchaseResultOpcode,
                        OfficialMerchantTransactionWireCodec.PurchaseResultFrameLength,
                        [Field("ObjectToken", 7, 4), Field("EntityHandle", 45, 4)])
                ],
                MinimumCorrelatedFields: 2,
                MaximumSequenceSpan: 32,
                EvidenceVerified: true,
                evidence),
            new(
                "Merchant",
                "Sell",
                [
                    Rule(
                        "SellRequest",
                        PacketDirection.ClientToServer,
                        OfficialMerchantTransactionWireCodec.TransactionOpcode,
                        OfficialMerchantTransactionWireCodec.TransactionFrameLength,
                        [
                            Field("EntityHandle", 3, 2),
                            Field("ObjectToken", 5, 2),
                            Field("SlotToken", 9, 2)
                        ],
                        new OfficialFamilyByteConstraint(8, (byte)OfficialMerchantOperation.Sell)),
                    Rule(
                        "SellResult",
                        PacketDirection.ServerToClient,
                        OfficialMerchantTransactionWireCodec.SaleResultOpcode,
                        OfficialMerchantTransactionWireCodec.SaleResultFrameLength,
                        [Field("EntityHandle", 13, 4), Field("SlotToken", 22, 2)])
                ],
                MinimumCorrelatedFields: 2,
                MaximumSequenceSpan: 32,
                EvidenceVerified: true,
                evidence)
        ]);
    }

    private static OfficialFamilyFieldBinding Field(string name, int offset, int width) =>
        new(name, offset, width);

    private static OfficialFamilyFrameRule Rule(
        string role,
        PacketDirection direction,
        byte opcode,
        int length,
        OfficialFamilyFieldBinding binding) =>
        Rule(role, direction, opcode, length, [binding], null);

    private static OfficialFamilyFrameRule Rule(
        string role,
        PacketDirection direction,
        byte opcode,
        int length,
        IReadOnlyList<OfficialFamilyFieldBinding> bindings,
        params OfficialFamilyByteConstraint[]? constraints) =>
        new(role, direction, opcode, length, bindings, constraints);
}
