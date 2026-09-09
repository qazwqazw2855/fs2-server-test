using System.Buffers.Binary;
using Iced.Intel;

namespace God2.OfflineClientReverseEngineering;

public sealed record DispatchRegistryEntry(
    string State,
    string Direction,
    string Opcode,
    int? TableIndex,
    string HandlerRva,
    bool UsesDefaultHandler,
    int ObservationCount,
    IReadOnlyList<string> ObservationSessions,
    string Evidence);

public sealed record DispatchTableSummary(
    string State,
    string DispatchKind,
    string FunctionRva,
    string JumpRva,
    string TableRva,
    string? IndexTableRva,
    int OpcodeCount,
    int UniqueHandlerCount,
    string DefaultHandlerRva,
    int DefaultOpcodeCount);

public sealed record PacketLengthPolicy(
    string Opcode,
    string Kind,
    int RawPolicy,
    int? FixedFrameLength,
    string Evidence);

public sealed record HandlerCluster(
    string State,
    string HandlerRva,
    IReadOnlyList<string> Opcodes,
    int ObservationCount,
    IReadOnlyList<string> ObservationSessions);

public sealed record DispatchRecoverySnapshot(
    string SchemaVersion,
    string ClientBuildId,
    IReadOnlyList<DispatchTableSummary> Tables,
    IReadOnlyList<DispatchRegistryEntry> Entries,
    IReadOnlyList<HandlerCluster> HandlerClusters,
    IReadOnlyList<PacketLengthPolicy> OutboundLengthPolicies,
    int StateSpecificRegistryEntryCount,
    int UniqueStateHandlerCount,
    int ObservedStateOpcodeCount,
    int FixedLengthPolicyCount,
    int VariableLengthPolicyCount,
    bool MovementModified,
    bool HeartbeatModified,
    bool ClientWriteAccessUsed);

public sealed class DispatchRegistryRecovery
{
    private const uint CodeStartRva = 0x00001000;
    private const uint ImageBase = 0x00400000;
    private const int LengthTableWindowOffset = 0x100;
    private readonly byte[] _code;
    private readonly byte[] _lengthTable;
    private readonly uint _codeEndRva;

    public DispatchRegistryRecovery(string codePath, string lengthTablePath)
    {
        _code = BoundedFile.ReadAllBytes(codePath, 16L * 1024 * 1024, "Runtime code snapshot");
        _lengthTable = BoundedFile.ReadAllBytes(lengthTablePath, 1024 * 1024, "Runtime length-table snapshot");
        _codeEndRva = checked(CodeStartRva + (uint)_code.Length);
        if (_code.Length < 0x147700 || _lengthTable.Length < LengthTableWindowOffset + (256 * sizeof(int)))
        {
            throw new InvalidDataException("The read-only runtime snapshots do not contain the required dispatch tables.");
        }
    }

    public DispatchRecoverySnapshot Recover(IReadOnlyList<DecodeObservation> observations)
    {
        var entries = new List<DispatchRegistryEntry>();
        var summaries = new List<DispatchTableSummary>();

        RecoverIndexed(
            state: "Login",
            functionRva: 0x0007E8F0,
            jumpRva: 0x0007E95B,
            minimumOpcode: 0x04,
            opcodeCount: 27,
            indexTableRva: 0x0007EDF8,
            targetTableRva: 0x0007EDDC,
            targetCount: 7,
            observedCallerRvas: new uint[] { 0x0007E940, 0x0007E8F0 },
            observations,
            entries,
            summaries);

        RecoverDirect(
            state: "World",
            functionRva: 0x0008E490,
            jumpRva: 0x0008E536,
            minimumOpcode: 0x00,
            opcodeCount: 254,
            targetTableRva: 0x000911A8,
            defaultHandlerRva: 0x0008F281,
            observedCallerRvas: new uint[] { 0x0008E4F1 },
            observations,
            entries,
            summaries);

        RecoverIndexed(
            state: "Battle",
            functionRva: 0x00146E00,
            jumpRva: 0x00146E80,
            minimumOpcode: 0x00,
            opcodeCount: 252,
            indexTableRva: 0x001475A8,
            targetTableRva: 0x001474AC,
            targetCount: 63,
            observedCallerRvas: new uint[] { 0x00146E40 },
            observations,
            entries,
            summaries);

        var policies = new List<PacketLengthPolicy>();
        for (var opcode = 0; opcode < 256; opcode++)
        {
            var raw = BinaryPrimitives.ReadInt32LittleEndian(_lengthTable.AsSpan(LengthTableWindowOffset + (opcode * 4), 4));
            if (raw == 0)
            {
                continue;
            }
            if (raw < -2 || raw == -1 || raw > ushort.MaxValue || raw == 1)
            {
                throw new InvalidDataException($"Outbound length policy for opcode {HexByte(opcode)} is outside the supported frame domain: {raw}.");
            }

            policies.Add(new PacketLengthPolicy(
                HexByte(opcode),
                raw == -2 ? "Variable" : raw > 0 ? "Fixed" : "Reserved",
                raw,
                raw > 0 ? raw : null,
                "Read-only runtime table VA 0x00894090 (within the captured 0x00893F90 context window); no client memory writes."));
        }

        var clusters = entries
            .GroupBy(item => (item.State, item.HandlerRva))
            .OrderBy(group => group.Key.State, StringComparer.Ordinal)
            .ThenBy(group => group.Key.HandlerRva, StringComparer.Ordinal)
            .Select(group => new HandlerCluster(
                group.Key.State,
                group.Key.HandlerRva,
                group.Select(item => item.Opcode).OrderBy(item => item, StringComparer.Ordinal).ToArray(),
                group.Sum(item => item.ObservationCount),
                group.SelectMany(item => item.ObservationSessions).Distinct(StringComparer.Ordinal).OrderBy(item => item, StringComparer.Ordinal).ToArray()))
            .ToArray();

        return new DispatchRecoverySnapshot(
            "offline-client-dispatch-registry-v1",
            "god2-opt-6b127086e0c0",
            summaries,
            entries,
            clusters,
            policies,
            entries.Count,
            clusters.Length,
            entries.Count(item => item.ObservationCount > 0),
            policies.Count(item => item.Kind == "Fixed"),
            policies.Count(item => item.Kind == "Variable"),
            MovementModified: false,
            HeartbeatModified: false,
            ClientWriteAccessUsed: false);
    }

    private void RecoverDirect(
        string state,
        uint functionRva,
        uint jumpRva,
        int minimumOpcode,
        int opcodeCount,
        uint targetTableRva,
        uint defaultHandlerRva,
        IReadOnlyCollection<uint> observedCallerRvas,
        IReadOnlyList<DecodeObservation> observations,
        List<DispatchRegistryEntry> entries,
        List<DispatchTableSummary> summaries)
    {
        ValidateRvaRange(functionRva, 1, $"{state} dispatcher function");
        ValidateInstruction(functionRva);
        ValidateJumpInstruction(jumpRva, targetTableRva, state);
        ValidateRvaRange(targetTableRva, checked(opcodeCount * sizeof(uint)), $"{state} direct target table");
        var targets = Enumerable.Range(0, opcodeCount)
            .Select(index => ToRva(ReadUInt32(targetTableRva + checked((uint)(index * 4)))))
            .ToArray();
        AddEntries(state, minimumOpcode, targets, null, defaultHandlerRva, observedCallerRvas, observations, entries, "Direct x86 jump table recovered from read-only runtime memory.");
        AddSummary(state, "DirectJumpTable", functionRva, jumpRva, targetTableRva, null, targets, defaultHandlerRva, summaries);
    }

    private void RecoverIndexed(
        string state,
        uint functionRva,
        uint jumpRva,
        int minimumOpcode,
        int opcodeCount,
        uint indexTableRva,
        uint targetTableRva,
        int targetCount,
        IReadOnlyCollection<uint> observedCallerRvas,
        IReadOnlyList<DecodeObservation> observations,
        List<DispatchRegistryEntry> entries,
        List<DispatchTableSummary> summaries)
    {
        ValidateRvaRange(functionRva, 1, $"{state} dispatcher function");
        ValidateInstruction(functionRva);
        ValidateJumpInstruction(jumpRva, targetTableRva, state);
        ValidateRvaRange(indexTableRva, opcodeCount, $"{state} compact index table");
        ValidateRvaRange(targetTableRva, checked(targetCount * sizeof(uint)), $"{state} compact target table");
        var targetHandlers = Enumerable.Range(0, targetCount)
            .Select(index => ToRva(ReadUInt32(targetTableRva + checked((uint)(index * 4)))))
            .ToArray();
        var indexes = Enumerable.Range(0, opcodeCount)
            .Select(index => (int)ReadByte(indexTableRva + checked((uint)index)))
            .ToArray();
        if (indexes.Any(index => index < 0 || index >= targetHandlers.Length))
        {
            throw new InvalidDataException($"{state} dispatcher index exceeds its compact target table.");
        }

        var targets = indexes.Select(index => targetHandlers[index]).ToArray();
        var defaultHandler = targets.GroupBy(item => item).OrderByDescending(group => group.Count()).ThenBy(group => group.Key).First().Key;
        AddEntries(state, minimumOpcode, targets, indexes, defaultHandler, observedCallerRvas, observations, entries, "Indexed x86 jump table recovered from read-only runtime memory.");
        AddSummary(state, "IndexedJumpTable", functionRva, jumpRva, targetTableRva, indexTableRva, targets, defaultHandler, summaries);
    }

    private static void AddEntries(
        string state,
        int minimumOpcode,
        IReadOnlyList<uint> targets,
        IReadOnlyList<int>? indexes,
        uint defaultHandlerRva,
        IReadOnlyCollection<uint> observedCallerRvas,
        IReadOnlyList<DecodeObservation> observations,
        List<DispatchRegistryEntry> destination,
        string evidence)
    {
        for (var index = 0; index < targets.Count; index++)
        {
            var opcode = checked((byte)(minimumOpcode + index));
            var matching = observations
                .Where(item => item.Opcode == opcode && observedCallerRvas.Contains(item.CallerRva))
                .ToArray();
            destination.Add(new DispatchRegistryEntry(
                state,
                "ServerToClient",
                HexByte(opcode),
                indexes?[index],
                HexRva(targets[index]),
                targets[index] == defaultHandlerRva,
                matching.Length,
                matching.Select(item => item.Session).Distinct(StringComparer.Ordinal).OrderBy(item => item, StringComparer.Ordinal).ToArray(),
                evidence));
        }
    }

    private static void AddSummary(
        string state,
        string kind,
        uint functionRva,
        uint jumpRva,
        uint targetTableRva,
        uint? indexTableRva,
        IReadOnlyList<uint> targets,
        uint defaultHandler,
        List<DispatchTableSummary> destination)
    {
        destination.Add(new DispatchTableSummary(
            state,
            kind,
            HexRva(functionRva),
            HexRva(jumpRva),
            HexRva(targetTableRva),
            indexTableRva is null ? null : HexRva(indexTableRva.Value),
            targets.Count,
            targets.Distinct().Count(),
            HexRva(defaultHandler),
            targets.Count(item => item == defaultHandler)));
    }

    private byte ReadByte(uint rva)
    {
        ValidateRvaRange(rva, 1, "runtime byte");
        var offset = checked((int)(rva - CodeStartRva));
        return _code[offset];
    }

    private uint ReadUInt32(uint rva)
    {
        ValidateRvaRange(rva, sizeof(uint), "runtime UInt32");
        var offset = checked((int)(rva - CodeStartRva));
        return BinaryPrimitives.ReadUInt32LittleEndian(_code.AsSpan(offset, 4));
    }

    private uint ToRva(uint virtualAddress)
    {
        if (virtualAddress < ImageBase)
        {
            throw new InvalidDataException($"Jump target 0x{virtualAddress:X8} is below the image base.");
        }

        var rva = virtualAddress - ImageBase;
        ValidateRvaRange(rva, 1, "jump target");
        ValidateInstruction(rva);
        return rva;
    }

    private void ValidateJumpInstruction(uint jumpRva, uint targetTableRva, string state)
    {
        ValidateRvaRange(jumpRva, 7, $"{state} jump instruction");
        var offset = checked((int)(jumpRva - CodeStartRva));
        var instruction = _code.AsSpan(offset, 7);
        var tableVa = BinaryPrimitives.ReadUInt32LittleEndian(instruction[3..]);
        if (instruction[0] != 0xFF || instruction[1] != 0x24 ||
            (instruction[2] & 0xC7) != 0x85 ||
            tableVa != checked(ImageBase + targetTableRva))
        {
            throw new InvalidDataException($"{state} dispatcher jump evidence does not reference the expected target table.");
        }
    }

    private void ValidateInstruction(uint rva)
    {
        var offset = checked((int)(rva - CodeStartRva));
        var length = Math.Min(15, _code.Length - offset);
        var decoder = Decoder.Create(32, new ByteArrayCodeReader(_code.AsSpan(offset, length).ToArray()));
        decoder.IP = checked(ImageBase + rva);
        decoder.Decode(out var instruction);
        if (instruction.Code == Code.INVALID || instruction.Length == 0)
        {
            throw new InvalidDataException($"Jump target {HexRva(rva)} does not begin with a valid x86 instruction.");
        }
    }

    private void ValidateRvaRange(uint rva, int length, string label)
    {
        if (length < 0 || rva < CodeStartRva || (ulong)rva + (uint)length > _codeEndRva)
        {
            throw new InvalidDataException($"{label} range {HexRva(rva)} + {length} falls outside the read-only runtime code snapshot.");
        }
    }

    private static string HexRva(uint value) => $"0x{value:X8}";
    private static string HexByte(int value) => $"0x{value:X2}";
}
