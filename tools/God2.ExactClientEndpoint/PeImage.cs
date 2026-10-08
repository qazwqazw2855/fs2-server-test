using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace God2.ExactClientEndpoint;

internal sealed record Section(string Name, uint Rva, uint VirtualSize, uint Raw, uint Size, uint Flags);
internal sealed record Import(string Module, string Name, uint? NameRva, uint IatRva, uint IatAddress);

// Adapted from PeStaticAnalyzer's PE32 header/section/import parsing, without its
// old build anchors. All RVA reads are bounded to backed section/header bytes;
// zero-fill and ambiguous mappings are rejected rather than treated as file offsets.
internal sealed class PeImage
{
    internal const int MaximumBytes = 64 * 1024 * 1024;
    internal const string ExactSha256 = "6F2639A0A7AD25053D0364108147173EB68BD04F57E6942491F42633F40052BC";
    internal readonly byte[] Bytes;
    internal readonly uint ImageBase, EntryPoint, HeadersSize;
    internal readonly List<Section> Sections = [];
    internal readonly List<Import> Imports = [];
    internal readonly bool HasDelayImports;

    internal static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidDataException(message);
    }

    internal static void CheckIdentity(byte[] bytes)
    {
        Require(bytes.Length is > 0 and <= MaximumBytes, "Input size rejected.");
        Require(Convert.ToHexString(SHA256.HashData(bytes)) == ExactSha256,
            "Exact-current SHA256 lock rejected input; no analysis performed.");
    }

    // Production calls CheckIdentity before constructing this parser. Synthetic
    // tests construct PeImage directly; they are not promoted as client evidence.
    internal PeImage(byte[] bytes)
    {
        Require(bytes.Length is >= 64 and <= MaximumBytes, "Invalid PE input size.");
        Bytes = bytes;
        Require(U16(0) == 0x5A4D, "Missing MZ signature.");
        var pe = U32(0x3C);
        Require(U32(pe) == 0x4550, "Missing PE signature.");
        Require(U16((ulong)pe + 4) == 0x14C, "Only x86 PE32 supported.");
        var count = U16((ulong)pe + 6);
        Require(count is > 0 and <= 96, "Invalid section count.");
        var optionalSize = U16((ulong)pe + 20);
        Require(optionalSize >= 112, "Truncated optional header.");
        var optional = (ulong)pe + 24;
        Range(optional, optionalSize);
        Require(U16(optional) == 0x10B, "Only PE32 optional header supported.");
        EntryPoint = U32(optional + 16);
        ImageBase = U32(optional + 28);
        HeadersSize = U32(optional + 60);
        var directories = U32(optional + 92);
        Require(directories is >= 2 and <= 16 && 96UL + directories * 8UL <= optionalSize,
            "Invalid data directory count.");
        var table = optional + optionalSize;
        Range(table, count * 40UL);
        Require(HeadersSize >= table + count * 40UL && HeadersSize <= bytes.Length,
            "Invalid backed header length.");
        for (var i = 0; i < count; i++)
        {
            var o = table + (ulong)i * 40;
            var name = Encoding.ASCII.GetString(bytes, (int)o, 8).TrimEnd('\0');
            var s = new Section(name, U32(o + 12), U32(o + 8), U32(o + 20), U32(o + 16), U32(o + 36));
            var span = Math.Max(s.VirtualSize, s.Size);
            Require(span > 0 && s.Rva >= HeadersSize && (ulong)s.Rva + span <= uint.MaxValue &&
                (ulong)ImageBase + s.Rva + span <= uint.MaxValue, "Invalid section RVA extent.");
            if (s.Size > 0)
            {
                Require(s.Raw >= HeadersSize, "Section raw data overlaps headers.");
                Range(s.Raw, s.Size);
            }
            foreach (var prior in Sections)
            {
                Require(!Overlap(s.Rva, span, prior.Rva, Math.Max(prior.VirtualSize, prior.Size)),
                    "Ambiguous overlapping RVA sections.");
                Require(s.Size == 0 || prior.Size == 0 || !Overlap(s.Raw, s.Size, prior.Raw, prior.Size),
                    "Overlapping raw sections.");
            }
            Sections.Add(s);
        }
        var importRva = U32(optional + 104);
        var importSize = U32(optional + 108);
        Require((importRva == 0) == (importSize == 0), "Incomplete import directory.");
        if (importRva != 0) ParseImports(importRva, importSize);
        HasDelayImports = directories > 13 && (U32(optional + 96 + 13 * 8) != 0 || U32(optional + 100 + 13 * 8) != 0);
        Require(Executable(EntryPoint), "Entry point is not backed executable code.");
    }

    private static bool Overlap(uint a, uint n, uint b, uint m) =>
        (ulong)a < (ulong)b + m && (ulong)b < (ulong)a + n;

    private void Range(ulong offset, ulong length) =>
        Require(offset <= (ulong)Bytes.Length && length <= (ulong)Bytes.Length - offset,
            "PE structure exceeds file boundary.");
    private ushort U16(ulong o) { Range(o, 2); return BinaryPrimitives.ReadUInt16LittleEndian(Bytes.AsSpan((int)o, 2)); }
    private uint U32(ulong o) { Range(o, 4); return BinaryPrimitives.ReadUInt32LittleEndian(Bytes.AsSpan((int)o, 4)); }

    internal int Offset(uint rva, uint length = 1)
    {
        if (rva < HeadersSize)
        {
            Require((ulong)rva + length <= HeadersSize, "Read crosses header boundary.");
            return (int)rva;
        }
        var matches = Sections.Where(s => rva >= s.Rva &&
            (ulong)rva < (ulong)s.Rva + Math.Max(s.VirtualSize, s.Size)).ToArray();
        Require(matches.Length == 1, "Unmapped or ambiguous RVA.");
        var section = matches[0];
        var delta = rva - section.Rva;
        Require((ulong)delta + length <= section.Size, "RVA points into unbacked/zero-fill bytes.");
        Range((ulong)section.Raw + delta, length);
        return (int)(section.Raw + delta);
    }

    internal uint? RvaForOffset(int offset)
    {
        if (offset < HeadersSize) return (uint)offset;
        foreach (var s in Sections)
            if ((uint)offset >= s.Raw && (ulong)offset < (ulong)s.Raw + s.Size)
                return s.Rva + (uint)offset - s.Raw;
        return null;
    }

    internal bool Executable(uint rva) => Sections.Any(s => (s.Flags & 0x20000000) != 0 &&
        rva >= s.Rva && (ulong)rva < (ulong)s.Rva + s.Size);

    internal byte[] Code(uint rva)
    {
        Require(Executable(rva), "Non-executable or unbacked code target.");
        var s = Sections.Single(s => rva >= s.Rva && (ulong)rva < (ulong)s.Rva + s.Size);
        return Bytes.AsSpan(Offset(rva), (int)Math.Min(15u, s.Size - (rva - s.Rva))).ToArray();
    }

    private uint At(uint rva) => U32((uint)Offset(rva, 4));
    private static uint Add(uint rva, uint delta)
    {
        Require((ulong)rva + delta <= uint.MaxValue, "RVA arithmetic overflow.");
        return rva + delta;
    }
    private string AsciiZ(uint rva)
    {
        var text = new List<byte>();
        for (uint i = 0; i < 4096; i++)
        {
            var b = Bytes[Offset(Add(rva, i))];
            if (b == 0) return Encoding.ASCII.GetString(text.ToArray());
            Require(b is >= 0x20 and <= 0x7E, "Invalid import ASCII string.");
            text.Add(b);
        }
        throw new InvalidDataException("Unterminated import string.");
    }

    private void ParseImports(uint rva, uint size)
    {
        Require(size is >= 20 and <= 81920, "Import directory size rejected.");
        Offset(rva, size);
        var terminated = false;
        for (uint d = 0; d + 20 <= size; d += 20)
        {
            var descriptor = Add(rva, d);
            var fields = Enumerable.Range(0, 5).Select(i => At(Add(descriptor, (uint)i * 4))).ToArray();
            if (fields.All(f => f == 0)) { terminated = true; break; }
            Require(fields[3] != 0 && fields[4] != 0, "Malformed import descriptor.");
            var module = AsciiZ(fields[3]);
            var lookup = fields[0] == 0 ? fields[4] : fields[0];
            var thunkTerminated = false;
            for (uint i = 0; i < 65536; i++)
            {
                var value = At(Add(lookup, i * 4));
                var iat = Add(fields[4], i * 4);
                Offset(iat, 4);
                if (value == 0) { thunkTerminated = true; break; }
                Require(Imports.Count < 65536, "Import budget exceeded.");
                var ordinal = (value & 0x80000000) != 0;
                if (ordinal) Require((value & 0x7FFF0000) == 0, "Malformed ordinal thunk.");
                else Offset(value, 2);
                var nameRva = ordinal ? (uint?)null : Add(value, 2);
                var name = ordinal ? $"ordinal-{value & 0xFFFF}" : AsciiZ(nameRva!.Value);
                Require((ulong)ImageBase + iat <= uint.MaxValue, "IAT VA overflow.");
                Require(!Imports.Any(x => x.IatRva == iat), "Duplicate IAT slot.");
                Imports.Add(new Import(module, name, nameRva, iat, ImageBase + iat));
            }
            Require(thunkTerminated, "Import thunk table did not terminate.");
        }
        Require(terminated, "Import descriptors did not terminate within directory.");
    }
}
