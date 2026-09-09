using System.Buffers.Binary;
using System.Security.Cryptography;
using God2.ClassicServer.Runtime;
using God2.GameplayContentRecovery;

namespace God2.OfflineClientReverseEngineering;

public sealed record M2EvidenceSource(
    string Kind,
    string Identity,
    string Sha256,
    long Length,
    string EvidenceStatus);

public sealed record M2ClientFunctionEvidence(
    string Purpose,
    string Rva,
    string WindowSha256,
    string EvidenceStatus);

public sealed record M2ApplicationMessageEvidence(
    int Index,
    int TransportOffset,
    string Opcode,
    int Length,
    string Sha256,
    string Classification);

public sealed record M2NpcResourceIdentity(
    int SourceRow,
    int TypeCode,
    int TypeOrdinal,
    string OriginalName,
    string ResourceKey,
    string TypeLabel,
    string IdentityEvidenceStatus);

public sealed record M2WorldEntityObservation(
    int ApplicationMessageIndex,
    string Opcode,
    uint ObservedEntityHandle,
    int? ResourceType,
    int? ResourceOrdinal,
    M2NpcResourceIdentity? NpcResource,
    uint PackedPosition,
    int X,
    int Y,
    string PositionEvidenceStatus,
    string LifecycleInterpretation,
    string MessageSha256);

public sealed record M2NpcSliceCandidate(
    string OfficialClientBuildId,
    string OfficialClientBuildSha256,
    uint ObservedEntityHandle,
    int MapId,
    int AreaId,
    int X,
    int Y,
    int ResourceType,
    int ResourceOrdinal,
    int NpcCsvSourceRow,
    string OriginalName,
    string ResourceKey,
    string NpcType,
    string InteractionFamily,
    string IdentityEvidenceStatus,
    string CoordinateEvidenceStatus,
    string ServiceEvidenceStatus,
    string ProductionPromotionStatus,
    string EvidenceReference);

public sealed record M2WorldContentEvidence(
    string SchemaVersion,
    string Status,
    IReadOnlyList<M2EvidenceSource> Sources,
    IReadOnlyList<M2ClientFunctionEvidence> ClientFunctions,
    int TransportFrameLength,
    string TransportFrameSha256,
    string TrailingChecksumSha256,
    int ApplicationMessageCount,
    IReadOnlyList<M2ApplicationMessageEvidence> ApplicationMessages,
    IReadOnlyList<M2WorldEntityObservation> WorldEntityObservations,
    IReadOnlyList<M2NpcSliceCandidate> NpcSliceCandidates,
    IReadOnlyList<string> SafetyAssertions,
    IReadOnlyList<string> RemainingBlockers);

public static class M2WorldContentEvidenceAnalyzer
{
    public const string SchemaVersion = "god2-roadmap-m2-world-content-evidence-v1";
    public const string OfficialClientBuildId = "god2-opt-6b127086e0c0";
    public const string OfficialClientSha256 = "6B127086E0C00014DE26137B4EC482801E06E0724C5C05C64561D7F9FF32BD9B";
    public const string RuntimeCodeSha256 = "E997DB114BD71E0BD5046D64A53D7D0C1EE78DD96A8C2D5BF224B638A2D2077A";
    public const string NpcCsvSha256 = "8C190363DEE7CE69EBC6DDE92ACA71101832040F40EC86336AE0A90FE4082127";
    public const int SnapshotBaseRva = 0x1000;
    public const int ImageBase = 0x400000;
    public const int IncomingLengthFunctionRva = 0x166360;
    public const int IncomingLengthJumpTableRva = 0x1664B4;

    private static readonly byte[] IncomingLengthFunctionPrefix =
        Convert.FromHexString("558BEC0FB645083DFE0000000F873B010000FF2485B4645600");

    private static readonly (string Purpose, int Rva, int WindowLength)[] FunctionWindows =
    [
        ("World socket read and opcode dispatch", 0x0008E490, 96),
        ("S2C 0x72 entity create/update dispatch", 0x000901DA, 96),
        ("S2C 0x60 entity position update dispatch", 0x00090590, 96),
        ("0x72 runtime entity construction and packed-position consumer", 0x000F7FD0, 512),
        ("0x60 runtime entity lookup and packed-position consumer", 0x000FB000, 160),
        ("NPC appearance type/ordinal resolver", 0x00082270, 160),
        ("NPC grouped resource lookup adapter", 0x00082A90, 96),
        ("NPC grouped resource table lookup", 0x00031AF0, 96)
    ];

    public static async Task<M2WorldContentEvidence> AnalyzeAsync(
        string clientExecutablePath,
        string runtimeCodePath,
        string npcCsvPath,
        CancellationToken cancellationToken)
    {
        var client = BoundedFile.ReadAllBytes(clientExecutablePath, 8 * 1024 * 1024, "Official Client executable");
        RequireHash(client, OfficialClientSha256, "Official Client executable");

        var runtimeCode = BoundedFile.ReadAllBytes(runtimeCodePath, 16 * 1024 * 1024, "Read-only runtime code snapshot");
        RequireHash(runtimeCode, RuntimeCodeSha256, "Read-only runtime code snapshot");
        VerifyRuntimeConsumers(runtimeCode);

        var npcCsvBytes = BoundedFile.ReadAllBytes(npcCsvPath, 16 * 1024 * 1024, "Official NPC.csvZ");
        RequireHash(npcCsvBytes, NpcCsvSha256, "Official NPC.csvZ");
        var npcCsv = await God2PackedFile.ReadCsvZAsync(npcCsvPath, cancellationToken);
        if (npcCsv.InvalidByteCount != 0)
        {
            throw new InvalidDataException("Official NPC.csvZ did not decode cleanly with the evidence-pinned CP936 decoder.");
        }

        var lengths = RecoverIncomingPacketLengths(runtimeCode);
        var staticFrame = OfficialClientWorldProtocolFrames.GetWorldBootstrapSequence()
            .Single(frame => string.Equals(frame.Purpose, "WorldStaticStateBootstrap", StringComparison.Ordinal));
        var bootstrap = OfficialClientWorldProtocolFrames.BuildWorldBootstrapAfterHandshake();
        var transport = OfficialClientWorldProtocolFrames.DecodeWorldServerPayload(
            bootstrap.AsSpan(staticFrame.Offset, staticFrame.Length));
        var messages = SplitFixedLengthTransportFrame(transport, opcode => lengths[opcode]);
        var resources = BuildNpcResourceIndex(npcCsv);
        var observations = DecodeWorldEntityObservations(messages, resources);

        var npc2643 = observations
            .Where(item => item.Opcode == "0x72" &&
                string.Equals(item.NpcResource?.ResourceKey, "data2/rom/npc/npc2643.rom", StringComparison.OrdinalIgnoreCase))
            .Where(item => !observations.Any(update =>
                update.Opcode == "0x60" &&
                update.ObservedEntityHandle == item.ObservedEntityHandle &&
                update.ApplicationMessageIndex > item.ApplicationMessageIndex))
            .OrderBy(item => item.ApplicationMessageIndex)
            .ToArray();
        if (npc2643.Length != 2 || npc2643.Any(item => item.NpcResource is null))
        {
            throw new InvalidDataException("The build-pinned bootstrap did not contain the two expected stable npc2643 observations.");
        }

        var candidates = npc2643.Select(item => new M2NpcSliceCandidate(
            OfficialClientBuildId,
            OfficialClientSha256,
            item.ObservedEntityHandle,
            MapId: 19,
            AreaId: 4,
            item.X,
            item.Y,
            item.ResourceType!.Value,
            item.ResourceOrdinal!.Value,
            item.NpcResource!.SourceRow,
            item.NpcResource.OriginalName,
            item.NpcResource.ResourceKey,
            item.NpcResource.TypeLabel,
            InteractionFamily: "Merchant",
            IdentityEvidenceStatus: "Verified",
            CoordinateEvidenceStatus: "Verified",
            ServiceEvidenceStatus: "Derived",
            ProductionPromotionStatus: "EligibleAfterMariaDbIdentityAndBoundsGate",
            EvidenceReference: "Reports/RoadMap.M2.RealContentSlice.md"))
            .ToArray();

        return new M2WorldContentEvidence(
            SchemaVersion,
            "PASS",
            [
                Source("OfficialClientExecutable", clientExecutablePath, client),
                Source("RuntimeCodeReadOnlySnapshot", runtimeCodePath, runtimeCode),
                Source("OfficialClientNpcResource", npcCsvPath, npcCsvBytes),
                new M2EvidenceSource(
                    "LegacyCompatibilityBootstrapFrame",
                    $"{OfficialClientWorldProtocolFrames.EvidenceId}:frame-{staticFrame.Index}:{staticFrame.Purpose}",
                    Hash(transport),
                    transport.LongLength,
                    "Verified")
            ],
            FunctionWindows.Select(item => new M2ClientFunctionEvidence(
                item.Purpose,
                $"0x{item.Rva:X8}",
                Hash(SliceAtRva(runtimeCode, item.Rva, item.WindowLength)),
                "Verified")).ToArray(),
            transport.Length,
            Hash(transport),
            Hash(transport.AsSpan(transport.Length - 1, 1)),
            messages.Count,
            messages.Select(message => new M2ApplicationMessageEvidence(
                message.Index,
                message.TransportOffset,
                $"0x{message.Bytes[0]:X2}",
                message.Bytes.Length,
                Hash(message.Bytes),
                message.Bytes[0] switch
                {
                    0x72 => "WorldEntityCreateOrUpdate",
                    0x60 => "WorldEntityPositionUpdate",
                    _ => "UnknownPreservedByHashOnly"
                })).ToArray(),
            observations,
            candidates,
            [
                "Unknown application messages retain only opcode, length, offset and SHA-256 metadata.",
                "Observed entity handles are session-scoped evidence and are not promoted as permanent database primary keys.",
                "NPC resource identity is resolved from the official Client type/ordinal lookup and the exact NPC.csvZ hash.",
                "Coordinates use the exact official Client formula x=(packed>>2)&0x7FFF; y=packed>>17.",
                "No protocol bytes were invented and no network traffic was emitted."
            ],
            [
                "Monster identity/spawn/stat/AI/formation/reward/drop evidence is not closed by this NPC bootstrap frame.",
                "NPC interaction C2S/S2C behavior remains outside M2 and is not inferred from the merchant resource classification."
            ]);
    }

    public static IReadOnlyDictionary<byte, int> RecoverIncomingPacketLengths(ReadOnlySpan<byte> runtimeCode)
    {
        RequireSpan(runtimeCode, IncomingLengthFunctionRva, IncomingLengthFunctionPrefix.Length, "incoming length function");
        if (!SliceAtRva(runtimeCode, IncomingLengthFunctionRva, IncomingLengthFunctionPrefix.Length)
            .SequenceEqual(IncomingLengthFunctionPrefix))
        {
            throw new InvalidDataException("The incoming packet-length function does not match the evidence-pinned Client code.");
        }

        var lengths = new Dictionary<byte, int>();
        for (var opcodeValue = 0; opcodeValue <= 0xFE; opcodeValue++)
        {
            var entry = SliceAtRva(runtimeCode, IncomingLengthJumpTableRva + (opcodeValue * 4), 4);
            var absoluteTarget = BinaryPrimitives.ReadUInt32LittleEndian(entry);
            if (absoluteTarget < ImageBase)
            {
                throw new InvalidDataException($"Opcode 0x{opcodeValue:X2} has an invalid packet-length target.");
            }

            var targetRva = checked((int)(absoluteTarget - ImageBase));
            var code = SliceAtRva(runtimeCode, targetRva, 7);
            int length;
            if (code[0] == 0xB8 && code[5] == 0x5D && code[6] == 0xC3)
            {
                length = BinaryPrimitives.ReadInt32LittleEndian(code[1..5]);
            }
            else if (code[0] == 0x83 && code[1] == 0xC8 && code[2] == 0xFF && code[3] == 0x5D && code[4] == 0xC3)
            {
                length = -1;
            }
            else
            {
                throw new InvalidDataException($"Opcode 0x{opcodeValue:X2} uses an unsupported packet-length return sequence.");
            }

            lengths[(byte)opcodeValue] = length;
        }

        return lengths;
    }

    public static IReadOnlyList<M2TransportMessage> SplitFixedLengthTransportFrame(
        ReadOnlySpan<byte> transport,
        Func<byte, int> resolveLength)
    {
        ArgumentNullException.ThrowIfNull(resolveLength);
        if (transport.Length < 4)
        {
            throw new InvalidDataException("World transport frame is truncated.");
        }

        var declaredLength = BinaryPrimitives.ReadUInt16LittleEndian(transport);
        if (declaredLength != transport.Length)
        {
            throw new InvalidDataException("World transport frame length prefix does not match the decoded frame length.");
        }

        var messages = new List<M2TransportMessage>();
        var offset = 2;
        var payloadEnd = transport.Length - 1;
        while (offset < payloadEnd)
        {
            var opcode = transport[offset];
            var length = resolveLength(opcode);
            if (length == -2)
            {
                if (offset + 2 > payloadEnd)
                {
                    throw new InvalidDataException($"World transport variable-length opcode 0x{opcode:X2} has a truncated length field.");
                }
                length = transport[offset + 1];
                if (length < 2)
                {
                    throw new InvalidDataException($"World transport variable-length opcode 0x{opcode:X2} has an invalid declared length.");
                }
            }
            else if (length <= 0)
            {
                throw new InvalidDataException($"World transport opcode 0x{opcode:X2} has no accepted length contract.");
            }
            if (offset + length > payloadEnd)
            {
                throw new InvalidDataException($"World transport opcode 0x{opcode:X2} is truncated.");
            }

            messages.Add(new M2TransportMessage(messages.Count, offset, transport.Slice(offset, length).ToArray()));
            offset += length;
        }

        if (offset != payloadEnd)
        {
            throw new InvalidDataException("World transport application messages do not terminate at the checksum boundary.");
        }

        return messages;
    }

    public static (int X, int Y) DecodePackedPosition(uint packed) =>
        (checked((int)((packed >> 2) & 0x7FFF)), checked((int)(packed >> 17)));

    public static IReadOnlyList<M2NpcResourceIdentity> BuildNpcResourceIndex(CsvZDocument npcCsv)
    {
        var headerIndex = npcCsv.Rows.ToList().FindIndex(row =>
            row.Fields.Count > 0 && row.Fields[0].StartsWith("索引", StringComparison.Ordinal));
        if (headerIndex < 0)
        {
            throw new InvalidDataException("NPC.csvZ header was not found.");
        }

        var ordinals = new Dictionary<int, int>();
        var resources = new List<M2NpcResourceIdentity>();
        foreach (var row in npcCsv.Rows.Skip(headerIndex + 1))
        {
            if (row.Fields.Count < 10 || !int.TryParse(row.Fields[6], out var typeCode))
            {
                continue;
            }

            var ordinal = ordinals.GetValueOrDefault(typeCode);
            ordinals[typeCode] = ordinal + 1;
            resources.Add(new M2NpcResourceIdentity(
                row.RecordIndex,
                typeCode,
                ordinal,
                row.Fields[8],
                NormalizeResource(row.Fields[1]),
                row.Fields[9],
                "Verified"));
        }

        if (resources.Count == 0 || resources
            .GroupBy(item => (item.TypeCode, item.TypeOrdinal))
            .Any(group => group.Count() != 1))
        {
            throw new InvalidDataException("NPC.csvZ type/ordinal identities are empty or ambiguous.");
        }

        return resources;
    }

    public static IReadOnlyList<M2WorldEntityObservation> DecodeWorldEntityObservations(
        IReadOnlyList<M2TransportMessage> messages,
        IReadOnlyList<M2NpcResourceIdentity> resources)
    {
        var result = new List<M2WorldEntityObservation>();
        foreach (var message in messages.Where(item => item.Bytes[0] is 0x60 or 0x72))
        {
            if (message.Bytes.Length != 21)
            {
                throw new InvalidDataException($"World entity opcode 0x{message.Bytes[0]:X2} has an unexpected record length.");
            }

            var entityHandle = BinaryPrimitives.ReadUInt32LittleEndian(message.Bytes.AsSpan(1, 4));
            var packedPosition = BinaryPrimitives.ReadUInt32LittleEndian(message.Bytes.AsSpan(17, 4));
            var (x, y) = DecodePackedPosition(packedPosition);
            int? resourceType = null;
            int? resourceOrdinal = null;
            M2NpcResourceIdentity? resource = null;
            if (message.Bytes[0] == 0x72)
            {
                resourceType = message.Bytes[6] & 0x1F;
                resourceOrdinal = message.Bytes[5] == 0 ? 0 : message.Bytes[5] - 1;
                var matches = resources.Where(item =>
                    item.TypeCode == resourceType.Value && item.TypeOrdinal == resourceOrdinal.Value).ToArray();
                if (matches.Length != 1)
                {
                    throw new InvalidDataException(
                        $"NPC resource type {resourceType} ordinal {resourceOrdinal} did not resolve uniquely.");
                }
                resource = matches[0];
            }

            result.Add(new M2WorldEntityObservation(
                message.Index,
                $"0x{message.Bytes[0]:X2}",
                entityHandle,
                resourceType,
                resourceOrdinal,
                resource,
                packedPosition,
                x,
                y,
                "Verified",
                message.Bytes[0] == 0x72 ? "CreateOrUpdate" : "PositionUpdate",
                Hash(message.Bytes)));
        }

        return result;
    }

    private static void VerifyRuntimeConsumers(ReadOnlySpan<byte> runtimeCode)
    {
        foreach (var function in FunctionWindows)
        {
            RequireSpan(runtimeCode, function.Rva, function.WindowLength, function.Purpose);
        }

        if (!SliceAtRva(runtimeCode, 0x000901DA, 16).SequenceEqual(
                Convert.FromHexString("0FB74F018D77018BBB209FA60089B5A0")) ||
            !SliceAtRva(runtimeCode, 0x000FB000, 16).SequenceEqual(
                Convert.FromHexString("558BEC538B5D0833C056578BF98B3381")) ||
            !Contains(SliceAtRva(runtimeCode, 0x000FB000, 160),
                Convert.FromHexString("C1E811C1E90281E1FF7F0000")))
        {
            throw new InvalidDataException("The official Client entity/resource/coordinate consumers do not match the pinned runtime code.");
        }
    }

    private static bool Contains(ReadOnlySpan<byte> source, ReadOnlySpan<byte> pattern)
    {
        for (var offset = 0; offset <= source.Length - pattern.Length; offset++)
        {
            if (source.Slice(offset, pattern.Length).SequenceEqual(pattern))
            {
                return true;
            }
        }
        return false;
    }

    private static ReadOnlySpan<byte> SliceAtRva(ReadOnlySpan<byte> runtimeCode, int rva, int length)
    {
        RequireSpan(runtimeCode, rva, length, $"RVA 0x{rva:X8}");
        return runtimeCode.Slice(rva - SnapshotBaseRva, length);
    }

    private static void RequireSpan(ReadOnlySpan<byte> runtimeCode, int rva, int length, string label)
    {
        var offset = (long)rva - SnapshotBaseRva;
        if (rva < SnapshotBaseRva || length < 0 || offset + length > runtimeCode.Length)
        {
            throw new InvalidDataException($"The runtime code snapshot does not contain {label}.");
        }
    }

    private static void RequireHash(ReadOnlySpan<byte> bytes, string expected, string label)
    {
        var actual = Hash(bytes);
        if (!string.Equals(actual, expected, StringComparison.Ordinal))
        {
            throw new InvalidDataException($"{label} SHA-256 does not match the evidence-pinned identity.");
        }
    }

    private static M2EvidenceSource Source(string kind, string path, ReadOnlySpan<byte> bytes) =>
        new(kind, Path.GetFileName(path), Hash(bytes), bytes.Length, "Verified");

    private static string NormalizeResource(string value) =>
        value.Replace('\\', '/').Replace(".ROMZ", ".ROM", StringComparison.OrdinalIgnoreCase).ToLowerInvariant();

    private static string Hash(ReadOnlySpan<byte> bytes) => Convert.ToHexString(SHA256.HashData(bytes));
}

public sealed record M2TransportMessage(int Index, int TransportOffset, byte[] Bytes);
