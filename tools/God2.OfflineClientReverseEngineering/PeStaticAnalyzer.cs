using System.Buffers.Binary;
using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;
using Iced.Intel;

namespace God2.OfflineClientReverseEngineering;

public sealed record PeSection(
    string Name,
    uint VirtualAddress,
    uint VirtualSize,
    uint RawOffset,
    uint RawSize,
    uint Characteristics);

public sealed record PeImport(
    string Module,
    string Name,
    ushort? Ordinal,
    uint IatRva,
    ulong IatAddress);

public sealed record StaticInstruction(
    ulong Address,
    uint Rva,
    int Length,
    string Mnemonic,
    string Text,
    ulong? DirectTarget,
    ulong? AbsoluteMemoryTarget,
    string FlowControl);

public sealed record ImportCallSite(
    string Module,
    string Import,
    string CallAddress,
    string ReturnAddress,
    string CallRva,
    string ReturnRva);

public sealed record StaticAnchor(
    string Name,
    string Rva,
    string Address,
    bool InExecutableSection,
    bool SemanticInstructionsAvailable,
    string Sha256Window,
    IReadOnlyList<string> IncomingCalls,
    IReadOnlyList<string> Instructions);

public sealed record StaticAnalysisSnapshot(
    string SchemaVersion,
    string ClientBuildId,
    string ClientSha256,
    long ClientLength,
    string Architecture,
    string ImageBase,
    string EntryPointRva,
    IReadOnlyList<PeSection> Sections,
    IReadOnlyList<PeImport> Imports,
    IReadOnlyList<ImportCallSite> NetworkCallSites,
    IReadOnlyList<StaticAnchor> Anchors,
    int InstructionCount,
    int DirectCallCount,
    int IndirectCallCount,
    string OnDiskCodeStatus,
    bool OnDiskSemanticDisassemblyAvailable,
    bool ReadOnlyAnalysis);

public sealed class PeStaticAnalyzer
{
    public const string ExpectedClientSha256 = "6B127086E0C00014DE26137B4EC482801E06E0724C5C05C64561D7F9FF32BD9B";

    private static readonly IReadOnlyDictionary<string, uint> KnownAnchors =
        new ReadOnlyDictionary<string, uint>(new Dictionary<string, uint>(StringComparer.Ordinal)
        {
            ["InboundSocketReadLength"] = 0x00078977,
            ["InboundSocketReadOpcode"] = 0x000789B3,
            ["InboundSocketReadPayload"] = 0x00078A1B,
            ["PacketDecode"] = 0x00078D70,
            ["PacketDecodeCallSite"] = 0x00078A48,
            ["CentralInboundDispatch"] = 0x0007C0B0,
            ["VersionParser"] = 0x0007CE90,
            ["VersionParserCallSite"] = 0x0007E7A2,
            ["InboundDispatchCallSite"] = 0x0007E91B,
            ["OutboundSocketWrite"] = 0x0007BB68,
            ["OutboundFinalizeCaller"] = 0x0007DC3E
        });

    private readonly byte[] _image;
    private readonly uint _peOffset;
    private readonly uint _optionalHeaderOffset;
    private readonly ushort _optionalMagic;
    private readonly uint _dataDirectoryOffset;
    private readonly uint _importDirectoryRva;
    private readonly IReadOnlyList<StaticInstruction> _instructions;
    private readonly IReadOnlyDictionary<ulong, StaticInstruction> _instructionByAddress;

    public PeStaticAnalyzer(string path)
    {
        Path = System.IO.Path.GetFullPath(path);
        _image = BoundedFile.ReadAllBytes(Path, 64L * 1024 * 1024, "Client executable");
        if (_image.Length < 0x100 || ReadUInt16(0) != 0x5A4D)
        {
            throw new InvalidDataException("Client binary is not a DOS/PE image.");
        }

        _peOffset = ReadUInt32(0x3C);
        if (ReadUInt32(_peOffset) != 0x00004550)
        {
            throw new InvalidDataException("Client binary does not contain a PE signature.");
        }

        Machine = ReadUInt16(_peOffset + 4);
        var sectionCount = ReadUInt16(_peOffset + 6);
        var optionalSize = ReadUInt16(_peOffset + 20);
        _optionalHeaderOffset = _peOffset + 24;
        _optionalMagic = ReadUInt16(_optionalHeaderOffset);
        if (_optionalMagic != 0x10B)
        {
            throw new InvalidDataException($"Only PE32/x86 is supported; optional-header magic was 0x{_optionalMagic:X4}.");
        }

        EntryPointRva = ReadUInt32(_optionalHeaderOffset + 16);
        ImageBase = ReadUInt32(_optionalHeaderOffset + 28);
        _dataDirectoryOffset = _optionalHeaderOffset + 96;
        _importDirectoryRva = ReadUInt32(_dataDirectoryOffset + 8);

        var sections = new List<PeSection>(sectionCount);
        var sectionOffset = _optionalHeaderOffset + optionalSize;
        for (var index = 0; index < sectionCount; index++)
        {
            var offset = sectionOffset + (uint)(index * 40);
            var name = Encoding.ASCII.GetString(_image, checked((int)offset), 8).TrimEnd('\0');
            sections.Add(new PeSection(
                name,
                ReadUInt32(offset + 12),
                ReadUInt32(offset + 8),
                ReadUInt32(offset + 20),
                ReadUInt32(offset + 16),
                ReadUInt32(offset + 36)));
        }

        Sections = sections.AsReadOnly();
        Imports = ParseImports().AsReadOnly();
        // This build is packed/transformed on disk. PE metadata and raw byte hashes are
        // still valid, but linear disassembly is deliberately suppressed as non-evidence.
        _instructions = Array.Empty<StaticInstruction>();
        _instructionByAddress = _instructions.ToDictionary(item => item.Address);
    }

    public string Path { get; }

    public ushort Machine { get; }

    public uint ImageBase { get; }

    public uint EntryPointRva { get; }

    public IReadOnlyList<PeSection> Sections { get; }

    public IReadOnlyList<PeImport> Imports { get; }

    public IReadOnlyList<StaticInstruction> Instructions => _instructions;

    public StaticAnalysisSnapshot Analyze()
    {
        var sha256 = Convert.ToHexString(SHA256.HashData(_image));
        if (!string.Equals(sha256, ExpectedClientSha256, StringComparison.Ordinal))
        {
            throw new InvalidDataException($"Build lock rejected client SHA-256 {sha256}.");
        }

        var iatImports = Imports.ToDictionary(item => item.IatAddress);
        var networkNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "send", "WSASend", "recv", "WSARecv", "connect", "WSAConnect", "closesocket"
        };
        var networkCalls = new List<ImportCallSite>();
        foreach (var instruction in _instructions.Where(item => item.Mnemonic == "Call" && item.AbsoluteMemoryTarget is not null))
        {
            if (!iatImports.TryGetValue(instruction.AbsoluteMemoryTarget!.Value, out var import) ||
                !networkNames.Contains(import.Name))
            {
                continue;
            }

            networkCalls.Add(new ImportCallSite(
                import.Module,
                import.Name,
                Hex(instruction.Address),
                Hex(instruction.Address + (uint)instruction.Length),
                HexRva(instruction.Rva),
                HexRva(instruction.Rva + (uint)instruction.Length)));
        }

        var incomingCalls = _instructions
            .Where(item => item.Mnemonic == "Call" && item.DirectTarget is not null)
            .GroupBy(item => item.DirectTarget!.Value)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<string>)group.Select(item => HexRva(item.Rva)).OrderBy(item => item, StringComparer.Ordinal).ToArray());

        var anchors = KnownAnchors.Select(pair => BuildAnchor(pair.Key, pair.Value, incomingCalls)).ToArray();
        return new StaticAnalysisSnapshot(
            "offline-client-static-analysis-v2",
            "god2-opt-6b127086e0c0",
            sha256,
            _image.LongLength,
            Machine == 0x14C ? "x86" : $"machine-0x{Machine:X4}",
            Hex(ImageBase),
            HexRva(EntryPointRva),
            Sections,
            Imports,
            networkCalls.AsReadOnly(),
            Array.AsReadOnly(anchors),
            _instructions.Count,
            _instructions.Count(item => item.Mnemonic == "Call" && item.DirectTarget is not null),
            _instructions.Count(item => item.Mnemonic == "Call" && item.AbsoluteMemoryTarget is not null),
            OnDiskCodeStatus: "PackedOrTransformed",
            OnDiskSemanticDisassemblyAvailable: false,
            ReadOnlyAnalysis: true);
    }

    public IReadOnlyList<StaticInstruction> Window(uint rva, int before = 12, int after = 80)
    {
        var address = ImageBase + rva;
        var nearestIndex = -1;
        for (var index = 0; index < _instructions.Count; index++)
        {
            if (_instructions[index].Address >= address)
            {
                nearestIndex = index;
                break;
            }
        }

        if (nearestIndex < 0)
        {
            return Array.Empty<StaticInstruction>();
        }

        var start = Math.Max(0, nearestIndex - before);
        var count = Math.Min(_instructions.Count - start, before + after);
        return _instructions.Skip(start).Take(count).ToArray();
    }

    public byte[] ReadRva(uint rva, int length)
    {
        var offset = RvaToOffset(rva);
        if (offset is null || offset.Value + length > _image.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(rva));
        }

        return _image.AsSpan(offset.Value, length).ToArray();
    }

    private StaticAnchor BuildAnchor(
        string name,
        uint rva,
        IReadOnlyDictionary<ulong, IReadOnlyList<string>> incomingCalls)
    {
        var address = ImageBase + rva;
        var window = Window(rva, 0, 48);
        var bytes = ReadRva(rva, 64);
        return new StaticAnchor(
            name,
            HexRva(rva),
            Hex(address),
            IsExecutableRva(rva),
            SemanticInstructionsAvailable: false,
            Convert.ToHexString(SHA256.HashData(bytes)),
            incomingCalls.TryGetValue(address, out var calls) ? calls : Array.Empty<string>(),
            window.Select(FormatInstruction).ToArray());
    }

    private List<PeImport> ParseImports()
    {
        var imports = new List<PeImport>();
        if (_importDirectoryRva == 0)
        {
            return imports;
        }

        var descriptorOffset = RvaToOffset(_importDirectoryRva) ?? throw new InvalidDataException("Invalid import directory RVA.");
        for (var descriptorIndex = 0; descriptorIndex < 4096; descriptorIndex++)
        {
            var offset = descriptorOffset + descriptorIndex * 20;
            var originalFirstThunk = ReadUInt32((uint)offset);
            var nameRva = ReadUInt32((uint)offset + 12);
            var firstThunk = ReadUInt32((uint)offset + 16);
            if (originalFirstThunk == 0 && nameRva == 0 && firstThunk == 0)
            {
                break;
            }

            var module = ReadAsciiZ(nameRva);
            var lookupRva = originalFirstThunk == 0 ? firstThunk : originalFirstThunk;
            for (var thunkIndex = 0; thunkIndex < 65536; thunkIndex++)
            {
                var thunkRva = lookupRva + (uint)(thunkIndex * 4);
                var thunkOffset = RvaToOffset(thunkRva) ?? throw new InvalidDataException("Invalid import thunk RVA.");
                var value = ReadUInt32((uint)thunkOffset);
                if (value == 0)
                {
                    break;
                }

                string name;
                ushort? ordinal = null;
                if ((value & 0x80000000) != 0)
                {
                    ordinal = (ushort)(value & 0xFFFF);
                    name = $"ordinal-{ordinal.Value}";
                }
                else
                {
                    name = ReadAsciiZ(value + 2);
                }

                var iatRva = firstThunk + (uint)(thunkIndex * 4);
                imports.Add(new PeImport(module, name, ordinal, iatRva, ImageBase + iatRva));
            }
        }

        return imports;
    }

    private List<StaticInstruction> DisassembleExecutableSections()
    {
        var results = new List<StaticInstruction>();
        foreach (var section in Sections.Where(item => (item.Characteristics & 0x20000000) != 0))
        {
            var safeLength = checked((int)Math.Min(section.RawSize, (uint)(_image.Length - section.RawOffset)));
            var bytes = _image.AsSpan(checked((int)section.RawOffset), safeLength).ToArray();
            var decoder = Iced.Intel.Decoder.Create(32, new ByteArrayCodeReader(bytes));
            decoder.IP = ImageBase + section.VirtualAddress;
            var formatter = new NasmFormatter();
            var output = new StringOutput();
            var sectionEnd = ImageBase + section.VirtualAddress + (uint)safeLength;
            while (decoder.IP < sectionEnd)
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
                if (instruction.Op0Kind == OpKind.Memory && instruction.MemoryBase == Register.None && instruction.MemoryIndex == Register.None)
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
        }

        return results;
    }

    private bool IsExecutableRva(uint rva) =>
        Sections.Any(section =>
            (section.Characteristics & 0x20000000) != 0 &&
            rva >= section.VirtualAddress &&
            rva < section.VirtualAddress + Math.Max(section.VirtualSize, section.RawSize));

    private int? RvaToOffset(uint rva)
    {
        foreach (var section in Sections)
        {
            var span = Math.Max(section.VirtualSize, section.RawSize);
            if (rva < section.VirtualAddress || rva >= section.VirtualAddress + span)
            {
                continue;
            }

            var delta = rva - section.VirtualAddress;
            if (delta >= section.RawSize)
            {
                return null;
            }

            return checked((int)(section.RawOffset + delta));
        }

        return rva < _image.Length ? checked((int)rva) : null;
    }

    private string ReadAsciiZ(uint rva)
    {
        var offset = RvaToOffset(rva) ?? throw new InvalidDataException($"Invalid string RVA 0x{rva:X8}.");
        var end = offset;
        while (end < _image.Length && _image[end] != 0 && end - offset < 4096)
        {
            end++;
        }

        return Encoding.ASCII.GetString(_image, offset, end - offset);
    }

    private ushort ReadUInt16(uint offset)
    {
        EnsureRange(offset, 2);
        return BinaryPrimitives.ReadUInt16LittleEndian(_image.AsSpan(checked((int)offset), 2));
    }

    private uint ReadUInt32(uint offset)
    {
        EnsureRange(offset, 4);
        return BinaryPrimitives.ReadUInt32LittleEndian(_image.AsSpan(checked((int)offset), 4));
    }

    private void EnsureRange(uint offset, int length)
    {
        if ((ulong)offset + (uint)length > (ulong)_image.Length)
        {
            throw new InvalidDataException("PE structure points outside the client binary.");
        }
    }

    private static string FormatInstruction(StaticInstruction item) =>
        $"{HexRva(item.Rva)} {item.Text}";

    private static string Hex(ulong value) => $"0x{value:X8}";

    private static string HexRva(uint value) => $"0x{value:X8}";

    private sealed class StringOutput : FormatterOutput
    {
        private readonly StringBuilder _builder = new();

        public override void Write(string text, FormatterTextKind kind) => _builder.Append(text);

        public void Reset() => _builder.Clear();

        public override string ToString() => _builder.ToString();
    }
}
