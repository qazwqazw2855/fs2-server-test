using System.Buffers.Binary;
using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace God2.ClassicServer.Protocol;

public sealed record UnknownActionPatternEvidence(
    string ActionPatternId,
    string StructuralPatternKey,
    byte TriggerOpcode,
    int TriggerLength,
    IReadOnlyList<byte> ResponseOpcodeSequence,
    IReadOnlyList<int> ResponseLengthPattern,
    IReadOnlyList<byte> HandlerOpcodeSequence,
    int Occurrences,
    decimal MedianLatencyMs,
    decimal P95LatencyMs,
    IReadOnlyList<string> RelatedOpcodeWorkItemIds,
    string SemanticStatus,
    bool ProductionEligible);

public sealed record LifecycleFieldCandidateEvidence(
    string CandidateId,
    string OperationCandidate,
    byte Opcode,
    int FrameLength,
    string FieldName,
    int Offset,
    int Length,
    int ObservedFrameCount,
    int DistinctObservedValueCount,
    string EvidenceStatus,
    bool ValuesRedacted,
    bool ProductionEnabled);

public sealed record CharacterLifecycleCandidateDecode(
    string OperationCandidate,
    string? CharacterNameCandidate,
    byte? SlotOrIndexCandidate,
    string OpaqueFieldSha256,
    bool ChecksumVerified,
    string EvidenceStatus,
    bool RuntimeMutationAllowed);

/// <summary>
/// Payload-free server catalog produced from the validated v1.0.4 capture and
/// its deterministic v1.0.5 action-grouping reanalysis. Structural frame and
/// timing relationships are queryable, while every gameplay semantic and
/// runtime mutation remains fail-closed until independently verified.
/// </summary>
public sealed class OfficialEvidencePackage20260807ActionCatalog
{
    public const string EvidenceSourceId = "god2-evidence-package-20260807-112752";
    public const string SessionId = "C6A8BF34-73F6-4859-AAA0-6FE49530CEC9";
    public const string AnalysisRunId = "6A437B10-7AA5-4EDE-9AFA-1451FFDC7A66";
    public const string SourcePackageSha256 = "20F8D5A60119CE9FA4F82C0CB0F546F832E594E7B3DBF5679B2464044F497F27";
    public const string ReanalysisPackageSha256 = "E5E24500DADB5D30F564EF685379DF7A70543E29D669BD3F37233E2E3AEFCF54";
    public const string ClientSha256 = "6B127086E0C00014DE26137B4EC482801E06E0724C5C05C64561D7F9FF32BD9B";
    public const string EvidencePath =
        "src/God2.ClassicServer.Protocol/Evidence/OfficialProtocolCompletion/God2Evidence.20260807.112752.json";

    public const int DecodedMessageCount = 1778;
    public const int HandlerObservationCount = 214;
    public const int NewDecodedFrameFamilyCount = 64;
    public const int NewDecodedFrameObservationCount = 88;
    public const int NewDecodedFrameUniqueCount = 83;
    public const int ActionPatternCount = 116;
    public const int GroupedActionTriggerCount = 655;
    public const int LifecycleFieldCandidateCount = 3;

    private static readonly Lazy<CatalogData> Data = new(Load, true);

    public IReadOnlyList<SupplementalDecodedFrameFamily> SnapshotNewDecodedFrameFamilies() =>
        Data.Value.NewDecodedFrameFamilies;

    public IReadOnlyList<UnknownActionPatternEvidence> SnapshotActionPatterns() =>
        Data.Value.ActionPatterns;

    public IReadOnlyList<LifecycleFieldCandidateEvidence> SnapshotLifecycleFieldCandidates() =>
        Data.Value.LifecycleFieldCandidates;

    public SupplementalDecodedFrameFamily? MatchNewDecodedFrame(
        PacketDirection direction,
        ReadOnlySpan<byte> frame)
    {
        if (direction is not (PacketDirection.ClientToServer or PacketDirection.ServerToClient) ||
            frame.Length < 3 || BinaryPrimitives.ReadUInt16LittleEndian(frame[..2]) != frame.Length)
        {
            return null;
        }

        var opcode = frame[2];
        var frameLength = frame.Length;
        return Data.Value.NewDecodedFrameFamilies.FirstOrDefault(value =>
            value.Direction == direction && value.Opcode == opcode && value.FrameLength == frameLength);
    }

    public IReadOnlyList<UnknownActionPatternEvidence> FindActionPatterns(byte triggerOpcode, int triggerLength) =>
        Array.AsReadOnly(
            Data.Value.ActionPatterns
                .Where(value => value.TriggerOpcode == triggerOpcode && value.TriggerLength == triggerLength)
                .ToArray());

    /// <summary>
    /// Decodes only the two structural lifecycle candidates supported by this
    /// evidence package. The result deliberately carries Candidate status and
    /// RuntimeMutationAllowed=false; it is not the official character codec.
    /// </summary>
    public bool TryDecodeCharacterLifecycleCandidate(
        ReadOnlySpan<byte> frame,
        out CharacterLifecycleCandidateDecode? candidate)
    {
        candidate = null;
        if (frame.Length < 3 || BinaryPrimitives.ReadUInt16LittleEndian(frame[..2]) != frame.Length ||
            OfficialLoginWireTransform.ComputeChecksum(frame) != frame[^1])
        {
            return false;
        }

        if (frame[2] == 0x17 && frame.Length == 48)
        {
            var nameField = frame.Slice(3, 29);
            var terminator = nameField.IndexOf((byte)0);
            if (terminator <= 0 || nameField[..terminator].ContainsAnyExceptInRange((byte)0x20, (byte)0x7E))
            {
                return false;
            }

            candidate = new CharacterLifecycleCandidateDecode(
                "CharacterCreateRequestCandidate",
                Encoding.ASCII.GetString(nameField[..terminator]),
                null,
                Convert.ToHexString(SHA256.HashData(frame.Slice(32, 15))),
                ChecksumVerified: true,
                EvidenceStatus: "Candidate",
                RuntimeMutationAllowed: false);
            return true;
        }

        if (frame[2] == 0x18 && frame.Length == 5)
        {
            candidate = new CharacterLifecycleCandidateDecode(
                "CharacterLifecycleSlotActionCandidate",
                null,
                frame[3],
                Convert.ToHexString(SHA256.HashData(frame.Slice(3, 1))),
                ChecksumVerified: true,
                EvidenceStatus: "Candidate",
                RuntimeMutationAllowed: false);
            return true;
        }

        return false;
    }

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        var data = Data.Value;

        if (data.NewDecodedFrameFamilies.Count != NewDecodedFrameFamilyCount ||
            data.NewDecodedFrameFamilies.Sum(value => value.ObservedCount) != NewDecodedFrameObservationCount ||
            data.NewDecodedFrameFamilies.Sum(value => value.UniqueFrameCount) != NewDecodedFrameUniqueCount)
        {
            errors.Add("evidence_package_20260807_112752.decoded_frame_count_mismatch");
        }

        if (data.ActionPatterns.Count != ActionPatternCount ||
            data.ActionPatterns.Sum(value => value.Occurrences) != GroupedActionTriggerCount ||
            data.ActionPatterns.Any(value => value.SemanticStatus != "Unknown" || value.ProductionEligible))
        {
            errors.Add("evidence_package_20260807_112752.action_pattern_authority_mismatch");
        }

        if (data.LifecycleFieldCandidates.Count != LifecycleFieldCandidateCount ||
            data.LifecycleFieldCandidates.Any(value => value.ProductionEnabled ||
                value.EvidenceStatus is not ("Candidate" or "Unknown")))
        {
            errors.Add("evidence_package_20260807_112752.lifecycle_candidate_authority_mismatch");
        }

        if (data.NewDecodedFrameFamilies.Any(value => value.FrameLength < 3 || value.ObservedCount <= 0 ||
                value.UniqueFrameCount <= 0 || value.UniqueFrameCount > value.ObservedCount) ||
            data.NewDecodedFrameFamilies.GroupBy(value => (value.Direction, value.Opcode, value.FrameLength))
                .Any(group => group.Count() != 1))
        {
            errors.Add("evidence_package_20260807_112752.invalid_structural_family");
        }

        return errors.AsReadOnly();
    }

    private static CatalogData Load()
    {
        var assembly = typeof(OfficialEvidencePackage20260807ActionCatalog).Assembly;
        var resourceName = assembly.GetManifestResourceNames().Single(value =>
            value.EndsWith("God2Evidence.20260807.112752.json", StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded evidence resource is missing: {resourceName}");
        using var document = JsonDocument.Parse(stream);
        var root = document.RootElement;

        var families = root.GetProperty("newDecodedFrameEvidence").GetProperty("families")
            .EnumerateArray()
            .Select(value => new SupplementalDecodedFrameFamily(
                Enum.Parse<PacketDirection>(value.GetProperty("direction").GetString()!, false),
                ParseOpcode(value.GetProperty("opcode").GetString()!),
                value.GetProperty("frameLength").GetInt32(),
                value.GetProperty("observedCount").GetInt32(),
                value.GetProperty("uniqueFrameCount").GetInt32()))
            .ToArray();

        var patterns = root.GetProperty("actionGroupingEvidence").GetProperty("patterns")
            .EnumerateArray()
            .Select(value => new UnknownActionPatternEvidence(
                value.GetProperty("actionPatternId").GetString()!,
                value.GetProperty("structuralPatternKey").GetString()!,
                ParseOpcode(value.GetProperty("triggerOpcode").GetString()!),
                value.GetProperty("triggerLength").GetInt32(),
                Array.AsReadOnly(value.GetProperty("responseOpcodeSequence").EnumerateArray()
                    .Select(item => ParseOpcode(item.GetString()!)).ToArray()),
                Array.AsReadOnly(value.GetProperty("responseLengthPattern").EnumerateArray()
                    .Select(item => item.GetInt32()).ToArray()),
                Array.AsReadOnly(value.GetProperty("handlerOpcodeSequence").EnumerateArray()
                    .Select(item => ParseOpcode(item.GetString()!)).ToArray()),
                value.GetProperty("occurrences").GetInt32(),
                value.GetProperty("medianLatencyMs").GetDecimal(),
                value.GetProperty("p95LatencyMs").GetDecimal(),
                Array.AsReadOnly(value.GetProperty("relatedOpcodeWorkItemIds").EnumerateArray()
                    .Select(item => item.GetString()!).ToArray()),
                value.GetProperty("semanticStatus").GetString()!,
                value.GetProperty("productionEligible").GetBoolean()))
            .ToArray();

        var lifecycle = root.GetProperty("lifecycleFieldCandidates")
            .EnumerateArray()
            .Select(value => new LifecycleFieldCandidateEvidence(
                value.GetProperty("candidateId").GetString()!,
                value.GetProperty("operationCandidate").GetString()!,
                ParseOpcode(value.GetProperty("opcode").GetString()!),
                value.GetProperty("frameLength").GetInt32(),
                value.GetProperty("fieldName").GetString()!,
                value.GetProperty("offset").GetInt32(),
                value.GetProperty("length").GetInt32(),
                value.GetProperty("observedFrameCount").GetInt32(),
                value.GetProperty("distinctObservedValueCount").GetInt32(),
                value.GetProperty("evidenceStatus").GetString()!,
                value.GetProperty("valuesRedacted").GetBoolean(),
                value.GetProperty("productionEnabled").GetBoolean()))
            .ToArray();

        return new CatalogData(
            Array.AsReadOnly(families),
            Array.AsReadOnly(patterns),
            Array.AsReadOnly(lifecycle));
    }

    private static byte ParseOpcode(string value) => Convert.ToByte(value[2..], 16);

    private sealed record CatalogData(
        ReadOnlyCollection<SupplementalDecodedFrameFamily> NewDecodedFrameFamilies,
        ReadOnlyCollection<UnknownActionPatternEvidence> ActionPatterns,
        ReadOnlyCollection<LifecycleFieldCandidateEvidence> LifecycleFieldCandidates);
}
