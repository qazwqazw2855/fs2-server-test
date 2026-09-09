using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Iced.Intel;

namespace God2.OfflineClientReverseEngineering;

public sealed record DecodeObservation(
    string Session,
    byte Opcode,
    uint CallerRva,
    int DecodedLength,
    string SourceFile);

public sealed record DecodeCallerCluster(
    string CallerRva,
    int ObservationCount,
    IReadOnlyList<string> Opcodes,
    IReadOnlyList<string> Sessions,
    string FunctionStartRva,
    string FunctionEndRva,
    IReadOnlyList<string> IndexedJumpSites);

public sealed record IndexedJumpEvidence(
    string JumpRva,
    string JumpTableAddress,
    string IndexRegister,
    int Scale,
    IReadOnlyList<string> Context);

public sealed record RuntimeCodeSnapshot(
    string SchemaVersion,
    string CodeSha256,
    long CodeBytes,
    string CodeStartRva,
    int DecodeObservationCount,
    int ObservedOpcodeCount,
    int DecodeCallerClusterCount,
    IReadOnlyList<DecodeCallerCluster> CallerClusters,
    IReadOnlyList<IndexedJumpEvidence> IndexedJumps,
    IReadOnlyList<string> ValidatedCallSites,
    bool ClientWriteAccessUsed,
    bool ClientBinaryModified,
    bool CredentialsEntered,
    bool GameplayPerformed);

public sealed record RuntimeMemoryDisplacementReference(
    string InstructionRva,
    string RawDisplacementRva,
    string InstructionBytes,
    string Instruction,
    string BaseRegister,
    string IndexRegister,
    int IndexScale,
    int DisplacementSize,
    string Access,
    bool LinearDecodeAligned);

public sealed record RuntimeMemoryFieldReference(
    string FieldOffset,
    string InstructionRva,
    string InstructionBytes,
    string Instruction,
    string BaseRegister,
    string IndexRegister,
    int IndexScale,
    int DisplacementSize,
    string Access);

public sealed partial class RuntimeCodeAnalyzer
{
    public const uint ImageBase = 0x00400000;
    public const uint CodeStartRva = 0x00001000;

    private readonly byte[] _code;
    private readonly uint _codeStartRva;
    private readonly IReadOnlyList<StaticInstruction> _instructions;

    public RuntimeCodeAnalyzer(string codePath)
        : this(codePath, CodeStartRva)
    {
    }

    public RuntimeCodeAnalyzer(string codePath, uint codeStartRva)
    {
        _code = BoundedFile.ReadAllBytes(codePath, 16L * 1024 * 1024, "Runtime code snapshot");
        _codeStartRva = codeStartRva;
        _instructions = Disassemble(_codeStartRva, _code).AsReadOnly();
    }

    public RuntimeCodeSnapshot Analyze(string workspaceRoot)
    {
        ValidateKnownCall(0x00078A48, 0x00078D70);
        ValidateKnownCall(0x0007E7A2, 0x0007CE90);
        ValidateKnownCall(0x0007E91B, 0x0007C0B0);

        var observations = ReadDecodeObservations(workspaceRoot);
        var indexedJumps = FindIndexedJumps();
        var clusters = observations
            .GroupBy(item => item.CallerRva)
            .OrderBy(group => group.Key)
            .Select(group =>
            {
                var start = FindFunctionStart(group.Key);
                var end = FindFunctionEnd(start);
                var jumps = indexedJumps
                    .Where(item => ParseRva(item.JumpRva) >= start && ParseRva(item.JumpRva) < end)
                    .Select(item => item.JumpRva)
                    .ToArray();
                return new DecodeCallerCluster(
                    HexRva(group.Key),
                    group.Count(),
                    group.Select(item => HexByte(item.Opcode)).Distinct().OrderBy(item => item, StringComparer.Ordinal).ToArray(),
                    group.Select(item => item.Session).Distinct().OrderBy(item => item, StringComparer.Ordinal).ToArray(),
                    HexRva(start),
                    HexRva(end),
                    jumps);
            })
            .ToArray();

        return new RuntimeCodeSnapshot(
            "offline-client-runtime-code-v1",
            Convert.ToHexString(SHA256.HashData(_code)),
            _code.LongLength,
            HexRva(CodeStartRva),
            observations.Count,
            observations.Select(item => item.Opcode).Distinct().Count(),
            clusters.Length,
            Array.AsReadOnly(clusters),
            indexedJumps,
            new[]
            {
                "0x00078A48 -> 0x00078D70 PacketDecode",
                "0x0007E7A2 -> 0x0007CE90 VersionParser",
                "0x0007E91B -> 0x0007C0B0 CentralInboundReader"
            },
            ClientWriteAccessUsed: false,
            ClientBinaryModified: false,
            CredentialsEntered: false,
            GameplayPerformed: false);
    }

    public IReadOnlyList<StaticInstruction> FunctionInstructions(uint anyRva)
    {
        var start = FindFunctionStart(anyRva);
        var end = FindFunctionEnd(start);
        return DecodeRange(start, checked((int)(end - start)));
    }

    public IReadOnlyList<StaticInstruction> DirectCallers(uint targetRva) =>
        _instructions
            .Where(item =>
                item.Mnemonic == "Call" &&
                item.DirectTarget == ImageBase + targetRva)
            .ToArray();

    public IReadOnlyList<StaticInstruction> InstructionsContaining(string token) =>
        _instructions
            .Where(item => item.Text.Contains(token, StringComparison.OrdinalIgnoreCase))
            .ToArray();

    public IReadOnlyList<StaticInstruction> InstructionsWithImmediateToken(string token) =>
        _instructions
            .Where(item => Regex.IsMatch(
                item.Text.TrimEnd(),
                $@"(?:^|[,\s]){Regex.Escape(token)}$",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            .ToArray();

    public IReadOnlyList<DecodeObservation> ReadObservations(string workspaceRoot) =>
        ReadDecodeObservations(workspaceRoot).AsReadOnly();

    public IReadOnlyList<StaticInstruction> DecodeRange(uint rva, int length)
    {
        if (rva < _codeStartRva)
        {
            return Array.Empty<StaticInstruction>();
        }

        var unsignedOffset = rva - _codeStartRva;
        if (unsignedOffset > int.MaxValue)
        {
            return Array.Empty<StaticInstruction>();
        }

        var offset = (int)unsignedOffset;
        if (offset >= _code.Length)
        {
            return Array.Empty<StaticInstruction>();
        }

        var safeLength = Math.Min(length, _code.Length - offset);
        return Disassemble(rva, _code.AsSpan(offset, safeLength).ToArray());
    }

    public IReadOnlyList<uint> RawDirectCallSites(uint targetRva)
    {
        var results = new List<uint>();
        for (var offset = 0; offset <= _code.Length - 5; offset++)
        {
            if (_code[offset] != 0xE8)
            {
                continue;
            }

            var displacement = BitConverter.ToInt32(_code, offset + 1);
            var resolvedTarget = (long)offset + 5L + displacement;
            if (resolvedTarget == targetRva)
            {
                results.Add((uint)offset);
            }
        }

        return results.AsReadOnly();
    }

    /// <summary>
    /// Finds every instruction candidate whose encoded four-byte displacement is the requested value.
    /// The scan begins from raw little-endian occurrences and decodes every possible x86 instruction
    /// start in the preceding fifteen-byte window, so data islands cannot silently desynchronize the
    /// ordinary linear decoder. Callers must still use <see cref="RuntimeMemoryDisplacementReference.LinearDecodeAligned"/>
    /// and surrounding control flow to distinguish reachable code from coincidental byte sequences.
    /// </summary>
    public IReadOnlyList<RuntimeMemoryDisplacementReference> ExhaustiveMemoryDisplacementReferences(uint displacement)
    {
        var raw = BitConverter.GetBytes(displacement);
        var references = new Dictionary<uint, RuntimeMemoryDisplacementReference>();
        var infoFactory = new InstructionInfoFactory();
        for (var rawOffset = 0; rawOffset <= _code.Length - raw.Length; rawOffset++)
        {
            if (!_code.AsSpan(rawOffset, raw.Length).SequenceEqual(raw))
            {
                continue;
            }

            var minimumStart = Math.Max(0, rawOffset - 14);
            for (var candidateOffset = minimumStart; candidateOffset <= rawOffset; candidateOffset++)
            {
                var available = Math.Min(15, _code.Length - candidateOffset);
                var decoder = Iced.Intel.Decoder.Create(
                    32,
                    new ByteArrayCodeReader(_code.AsSpan(candidateOffset, available).ToArray()));
                var candidateRva = checked(_codeStartRva + (uint)candidateOffset);
                decoder.IP = ImageBase + candidateRva;
                decoder.Decode(out var instruction);
                if (instruction.Length == 0 || instruction.Code == Code.INVALID)
                {
                    continue;
                }

                var rawAbsoluteOffset = checked(_codeStartRva + (uint)rawOffset);
                var instructionEndRva = checked(candidateRva + (uint)instruction.Length);
                if (rawAbsoluteOffset < candidateRva || rawAbsoluteOffset + 4u > instructionEndRva ||
                    instruction.MemoryDisplSize != 4 || instruction.MemoryDisplacement64 != displacement ||
                    !Enumerable.Range(0, instruction.OpCount).Any(index => instruction.GetOpKind(index) == OpKind.Memory))
                {
                    continue;
                }

                var accesses = infoFactory.GetInfo(in instruction)
                    .GetUsedMemory()
                    .Where(memory => memory.Displacement == displacement)
                    .Select(memory => memory.Access.ToString())
                    .Distinct(StringComparer.Ordinal)
                    .Order(StringComparer.Ordinal)
                    .ToArray();
                var formatter = new NasmFormatter();
                var output = new AnalyzerStringOutput();
                formatter.Format(instruction, output);
                var instructionBytes = _code.AsSpan(candidateOffset, instruction.Length).ToArray();
                var linear = _instructions.FirstOrDefault(item => item.Rva == candidateRva);
                references[candidateRva] = new RuntimeMemoryDisplacementReference(
                    $"0x{candidateRva:X8}",
                    $"0x{rawAbsoluteOffset:X8}",
                    Convert.ToHexString(instructionBytes),
                    output.ToString(),
                    instruction.MemoryBase.ToString(),
                    instruction.MemoryIndex.ToString(),
                    instruction.MemoryIndexScale,
                    instruction.MemoryDisplSize,
                    accesses.Length == 0 ? "AddressCalculationOnlyOrUnavailable" : string.Join('|', accesses),
                    linear is not null && linear.Length == instruction.Length &&
                        string.Equals(linear.Text, output.ToString(), StringComparison.Ordinal));
            }
        }

        return references.Values
            .OrderBy(reference => reference.InstructionRva, StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>
    /// Scans the exact linear runtime image for x86 memory operands whose positive
    /// base-relative displacement falls in an actor-field range. Unlike the
    /// exhaustive four-byte search, this also finds compact disp8 encodings such
    /// as [reg+24h]. Results are discovery candidates: reachable control flow and
    /// a live consumer observation are still required before assigning semantics.
    /// </summary>
    public IReadOnlyList<RuntimeMemoryFieldReference> LinearMemoryFieldReferences(
        uint minimumOffset, uint maximumOffset)
    {
        if (minimumOffset > maximumOffset)
            throw new ArgumentOutOfRangeException(nameof(minimumOffset));

        var results = new List<RuntimeMemoryFieldReference>();
        var decoder = Iced.Intel.Decoder.Create(32, new ByteArrayCodeReader(_code));
        decoder.IP = ImageBase + _codeStartRva;
        var end = decoder.IP + (uint)_code.Length;
        var formatter = new NasmFormatter();
        var output = new AnalyzerStringOutput();
        var infoFactory = new InstructionInfoFactory();
        while (decoder.IP < end)
        {
            var instructionOffset = checked((int)(decoder.IP - ImageBase - _codeStartRva));
            decoder.Decode(out var instruction);
            if (instruction.Length == 0) break;
            if (instruction.Code == Code.INVALID || instruction.MemoryBase == Register.None ||
                instruction.MemoryDisplSize == 0 ||
                !Enumerable.Range(0, instruction.OpCount)
                    .Any(index => instruction.GetOpKind(index) == OpKind.Memory))
                continue;

            var displacement = instruction.MemoryDisplacement64;
            if (displacement < minimumOffset || displacement > maximumOffset)
                continue;

            var accesses = infoFactory.GetInfo(in instruction)
                .GetUsedMemory()
                .Where(memory => memory.Displacement == displacement)
                .Select(memory => memory.Access.ToString())
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToArray();
            output.Reset();
            formatter.Format(instruction, output);
            results.Add(new RuntimeMemoryFieldReference(
                $"0x{displacement:X3}",
                $"0x{instruction.IP - ImageBase:X8}",
                Convert.ToHexString(_code.AsSpan(instructionOffset, instruction.Length)),
                output.ToString(),
                instruction.MemoryBase.ToString(),
                instruction.MemoryIndex.ToString(),
                instruction.MemoryIndexScale,
                instruction.MemoryDisplSize,
                accesses.Length == 0 ? "AddressCalculationOnlyOrUnavailable" :
                    string.Join('|', accesses)));
        }

        return results
            .OrderBy(reference => reference.FieldOffset, StringComparer.Ordinal)
            .ThenBy(reference => reference.InstructionRva, StringComparer.Ordinal)
            .ToArray();
    }

    private IReadOnlyList<IndexedJumpEvidence> FindIndexedJumps()
    {
        var results = new List<IndexedJumpEvidence>();
        for (var index = 0; index < _instructions.Count; index++)
        {
            var instruction = _instructions[index];
            if (instruction.Mnemonic != "Jmp" ||
                instruction.AbsoluteMemoryTarget is null ||
                !instruction.Text.Contains('*', StringComparison.Ordinal))
            {
                continue;
            }

            var context = _instructions
                .Skip(Math.Max(0, index - 12))
                .Take(Math.Min(16, _instructions.Count - Math.Max(0, index - 12)))
                .Select(item => $"{HexRva(item.Rva)} {item.Text}")
                .ToArray();
            results.Add(new IndexedJumpEvidence(
                HexRva(instruction.Rva),
                $"0x{instruction.AbsoluteMemoryTarget.Value:X8}",
                "indexed",
                4,
                context));
        }

        return results.AsReadOnly();
    }

    private List<DecodeObservation> ReadDecodeObservations(string workspaceRoot)
    {
        var root = Path.Combine(workspaceRoot, "Artifacts", "ClientInstrumentation", "ElevatedAutomationHost");
        var observations = new List<DecodeObservation>();
        if (!Directory.Exists(root))
        {
            return observations;
        }

        foreach (var file in Directory.EnumerateFiles(root, "general.log", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(root, file);
            var session = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)[0];
            foreach (var line in File.ReadLines(file))
            {
                var match = DecodeExitRegex().Match(line);
                if (!match.Success)
                {
                    continue;
                }

                observations.Add(new DecodeObservation(
                    session,
                    Convert.ToByte(match.Groups["opcode"].Value, 16),
                    Convert.ToUInt32(match.Groups["caller"].Value, 16),
                    int.Parse(match.Groups["length"].Value, System.Globalization.CultureInfo.InvariantCulture),
                    Path.GetRelativePath(workspaceRoot, file).Replace('\\', '/')));
            }
        }

        return observations;
    }

    private uint FindFunctionStart(uint rva)
    {
        if (rva < CodeStartRva || rva >= CodeStartRva + _code.Length)
        {
            return rva;
        }

        var offset = checked((int)(rva - CodeStartRva));
        var minimum = Math.Max(0, offset - 0x20000);
        for (var index = offset; index >= minimum + 3; index--)
        {
            var standardPrologue = _code[index] == 0x55 && _code[index + 1] == 0x8B && _code[index + 2] == 0xEC;
            var hotPatchPrologue = index + 4 < _code.Length &&
                _code[index] == 0x8B && _code[index + 1] == 0xFF &&
                _code[index + 2] == 0x55 && _code[index + 3] == 0x8B && _code[index + 4] == 0xEC;
            if (!standardPrologue && !hotPatchPrologue)
            {
                continue;
            }

            var paddingStart = Math.Max(0, index - 16);
            var hasBoundary = index == 0 || _code.AsSpan(paddingStart, index - paddingStart).Contains((byte)0xCC);
            if (hasBoundary)
            {
                return CodeStartRva + (uint)index;
            }
        }

        return rva;
    }

    private uint FindFunctionEnd(uint start)
    {
        if (start < CodeStartRva || start >= CodeStartRva + _code.Length)
        {
            return start;
        }

        var offset = checked((int)(start - CodeStartRva));
        var maximum = Math.Min(_code.Length - 4, offset + 0x40000);
        for (var index = offset + 3; index < maximum; index++)
        {
            if (_code[index] != 0xCC || _code[index + 1] != 0xCC || _code[index + 2] != 0xCC)
            {
                continue;
            }

            return CodeStartRva + (uint)index;
        }

        return CodeStartRva + (uint)maximum;
    }

    private void ValidateKnownCall(uint callRva, uint expectedTargetRva)
    {
        var offset = checked((int)(callRva - CodeStartRva));
        if (_code[offset] != 0xE8)
        {
            throw new InvalidDataException($"Runtime callsite {HexRva(callRva)} is not a direct call.");
        }

        var displacement = BitConverter.ToInt32(_code, offset + 1);
        var actualTarget = checked((uint)(callRva + 5 + displacement));
        if (actualTarget != expectedTargetRva)
        {
            throw new InvalidDataException(
                $"Runtime callsite {HexRva(callRva)} targets {HexRva(actualTarget)}, expected {HexRva(expectedTargetRva)}.");
        }
    }

    private static List<StaticInstruction> Disassemble(uint startRva, byte[] bytes)
    {
        var results = new List<StaticInstruction>();
        var decoder = Iced.Intel.Decoder.Create(32, new ByteArrayCodeReader(bytes));
        decoder.IP = ImageBase + startRva;
        var formatter = new NasmFormatter();
        var output = new AnalyzerStringOutput();
        var end = ImageBase + startRva + (uint)bytes.Length;
        while (decoder.IP < end)
        {
            decoder.Decode(out var instruction);
            if (instruction.Length == 0)
            {
                break;
            }

            output.Reset();
            formatter.Format(instruction, output);
            ulong? directTarget = instruction.FlowControl is FlowControl.Call or FlowControl.UnconditionalBranch or FlowControl.ConditionalBranch
                ? instruction.NearBranchTarget
                : null;
            ulong? absoluteMemoryTarget = null;
            if (instruction.Op0Kind == OpKind.Memory && instruction.MemoryBase == Register.None)
            {
                absoluteMemoryTarget = instruction.MemoryDisplacement64;
            }

            results.Add(new StaticInstruction(
                instruction.IP,
                checked((uint)(instruction.IP - ImageBase)),
                instruction.Length,
                instruction.Mnemonic.ToString(),
                output.ToString(),
                directTarget,
                absoluteMemoryTarget,
                instruction.FlowControl.ToString()));
        }

        return results;
    }

    private static uint ParseRva(string rva) => Convert.ToUInt32(rva[2..], 16);

    private static string HexRva(uint value) => $"0x{value:X8}";

    private static string HexByte(byte value) => $"0x{value:X2}";

    [GeneratedRegex(
        "packet-decode exit(?: sequence=\\d+)? result=(?<length>\\d+) decodedOpcode=0x(?<opcode>[0-9A-Fa-f]{2}).*?stack=\\\"god2_opt\\.exe\\+0x00078A4D;god2_opt\\.exe\\+0x(?<caller>[0-9A-Fa-f]{8})",
        RegexOptions.CultureInvariant)]
    private static partial Regex DecodeExitRegex();

    private sealed class AnalyzerStringOutput : FormatterOutput
    {
        private readonly System.Text.StringBuilder _builder = new();

        public override void Write(string text, FormatterTextKind kind) => _builder.Append(text);

        public void Reset() => _builder.Clear();

        public override string ToString() => _builder.ToString();
    }
}
