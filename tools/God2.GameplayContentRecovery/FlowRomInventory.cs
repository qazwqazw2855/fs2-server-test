using System.Security.Cryptography;
using System.Text;

namespace God2.GameplayContentRecovery;

public sealed record FlowRomInventoryEntry(
    string RelativePath,
    string SourceSha256,
    long PackedBytes,
    long DecodedBytes,
    string Format,
    byte WrapperMarker,
    string PayloadPrefixHex,
    int AsciiStringCount,
    IReadOnlyList<string> CandidateReferences);

public sealed record FlowRomInventoryResult(
    bool Succeeded,
    string ClientRoot,
    int CandidateFileCount,
    int ValidMigRomCount,
    IReadOnlyList<FlowRomInventoryEntry> Files,
    IReadOnlyList<string> Gaps);

public static class FlowRomInventory
{
    private const string MigRomSignature = "MIGSROM02";

    public static async Task<FlowRomInventoryResult> BuildAsync(
        string clientRoot,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(clientRoot);
        var root = Path.GetFullPath(clientRoot);
        if (!Directory.Exists(root))
        {
            throw new DirectoryNotFoundException($"Official Client root is missing: {root}");
        }

        var candidates = Directory.EnumerateFiles(root, "*.ROMZ", SearchOption.AllDirectories)
            .Where(path => string.Equals(Path.GetFileName(Path.GetDirectoryName(path)), "Flow", StringComparison.OrdinalIgnoreCase))
            .Where(path => Path.GetFileNameWithoutExtension(path).Contains("map", StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var entries = new List<FlowRomInventoryEntry>(candidates.Length);
        foreach (var path in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var packed = await File.ReadAllBytesAsync(path, cancellationToken);
            var decoded = God2PackedFile.Decode(packed);
            var format = IsMigRom(packed[6], decoded)
                ? MigRomSignature
                : "Unknown";
            var strings = ExtractAsciiStrings(decoded);
            var references = strings
                .Where(IsCandidateReference)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                .Take(500)
                .ToArray();
            entries.Add(new FlowRomInventoryEntry(
                Path.GetRelativePath(root, path).Replace('\\', '/'),
                Convert.ToHexString(SHA256.HashData(packed)).ToLowerInvariant(),
                packed.LongLength,
                decoded.LongLength,
                format,
                packed[6],
                Convert.ToHexString(decoded.AsSpan(0, Math.Min(64, decoded.Length))),
                strings.Count,
                Array.AsReadOnly(references)));
        }

        var validCount = entries.Count(entry => entry.Format == MigRomSignature);
        var gaps = new List<string>();
        if (entries.Count == 0)
        {
            gaps.Add("NoMapFlowRomFound");
        }
        if (validCount != entries.Count)
        {
            gaps.Add($"UnknownFlowRomFormat={entries.Count - validCount}");
        }
        gaps.Add("GraphicsRomInventoryOnlyNoPortalSemanticsProven");

        return new FlowRomInventoryResult(
            entries.Count > 0 && validCount == entries.Count,
            root,
            entries.Count,
            validCount,
            entries.AsReadOnly(),
            gaps.AsReadOnly());
    }

    internal static IReadOnlyList<string> ExtractAsciiStrings(ReadOnlySpan<byte> bytes)
    {
        var result = new List<string>();
        var builder = new StringBuilder();
        foreach (var value in bytes)
        {
            if (value is >= 0x20 and <= 0x7e)
            {
                builder.Append((char)value);
                continue;
            }

            Flush(builder, result);
        }
        Flush(builder, result);
        return result.AsReadOnly();
    }

    internal static bool IsMigRom(byte wrapperMarker, ReadOnlySpan<byte> decoded) =>
        wrapperMarker == (byte)'M' && decoded.StartsWith("IGSROM02"u8);

    private static void Flush(StringBuilder builder, ICollection<string> result)
    {
        if (builder.Length >= 4)
        {
            result.Add(builder.ToString());
        }
        builder.Clear();
    }

    private static bool IsCandidateReference(string value) =>
        value.Contains("map", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("portal", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("warp", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("trans", StringComparison.OrdinalIgnoreCase) ||
        value.Contains(".mdt", StringComparison.OrdinalIgnoreCase) ||
        value.Contains(".hmd", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("city", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("island", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("north", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("south", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("tong", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("array", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("gem", StringComparison.OrdinalIgnoreCase);
}
