using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace God2.GameplayContentRecovery;

public sealed record ClientMapResourceRecord(
    string AuthorityKey,
    string Area,
    int CanRecordIndex,
    byte CanRecordType,
    string ResourceName,
    string ResourceFormat,
    int? GridWidth,
    int? GridHeight,
    string CanRelativePath,
    string CanSha256,
    string? ResourceFileRelativePath,
    string? ResourceFileSha256,
    string? NavigationFormat,
    string? NavigationRelativePath,
    string? NavigationSha256);

public sealed record ClientMapResourceInventoryResult(
    bool Succeeded,
    string ClientRoot,
    int CanFileCount,
    int ResourceCount,
    int IndoorMapResourceCount,
    int DimensionCompleteCount,
    IReadOnlyList<ClientMapResourceRecord> Resources,
    IReadOnlyList<string> Gaps);

public static class ClientMapResourceInventory
{
    private const string CanSignature = "CAN v1.0";
    private const string MbdSignature = "MBD v1.2";
    private const string HmdSignature = "HMD v1.6";

    public static async Task<ClientMapResourceInventoryResult> BuildAsync(
        string clientRoot,
        CancellationToken cancellationToken)
    {
        var root = Path.GetFullPath(clientRoot);
        if (!Directory.Exists(root))
        {
            throw new DirectoryNotFoundException($"Official Client root is missing: {root}");
        }

        var canFiles = Directory.EnumerateFiles(root, "*.Can", SearchOption.AllDirectories)
            .Where(path => path.Contains($"{Path.DirectorySeparatorChar}Data2{Path.DirectorySeparatorChar}map{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var resources = new Dictionary<string, ClientMapResourceRecord>(StringComparer.OrdinalIgnoreCase);
        foreach (var canPath in canFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var canBytes = await File.ReadAllBytesAsync(canPath, cancellationToken);
            if (canBytes.Length < 12 || Encoding.ASCII.GetString(canBytes, 0, 8) != CanSignature)
            {
                throw new InvalidDataException($"Invalid CAN signature: {canPath}");
            }
            var count = BinaryPrimitives.ReadInt32LittleEndian(canBytes.AsSpan(8, 4));
            if (count < 0 || 12L + count * 64L > canBytes.LongLength)
            {
                throw new InvalidDataException($"Invalid CAN record count: {canPath}");
            }

            var canHash = Convert.ToHexString(SHA256.HashData(canBytes)).ToLowerInvariant();
            var area = Path.GetFileNameWithoutExtension(canPath).ToLowerInvariant();
            for (var index = 0; index < count; index++)
            {
                var offset = 12 + index * 64;
                var nameBytes = canBytes.AsSpan(offset + 1, 63);
                var terminator = nameBytes.IndexOf((byte)0);
                var resourceName = Encoding.ASCII.GetString(terminator < 0 ? nameBytes : nameBytes[..terminator])
                    .Replace('\\', '/')
                    .ToLowerInvariant();
                if (string.IsNullOrWhiteSpace(resourceName))
                {
                    continue;
                }

                var resourceStem = resourceName
                    .Replace(".mdt", string.Empty, StringComparison.OrdinalIgnoreCase)
                    .Replace(".hmd", string.Empty, StringComparison.OrdinalIgnoreCase);
                var authorityKey = $"client:map/{area}/{resourceStem}";
                var resource = await ResolveResourceAsync(root, canPath, resourceName, resourceStem, cancellationToken);
                resources.TryAdd(authorityKey, new ClientMapResourceRecord(
                    authorityKey,
                    area,
                    index,
                    canBytes[offset],
                    resourceName,
                    resource.Format,
                    resource.Width,
                    resource.Height,
                    Relative(root, canPath),
                    canHash,
                    resource.ResourcePath is null ? null : Relative(root, resource.ResourcePath),
                    resource.ResourceSha256,
                    resource.NavigationFormat,
                    resource.NavigationPath is null ? null : Relative(root, resource.NavigationPath),
                    resource.NavigationSha256));
            }
        }

        var rows = resources.Values.OrderBy(value => value.AuthorityKey, StringComparer.Ordinal).ToArray();
        var indoorMapResources = rows.Count(value => value.ResourceFormat == "HMD");
        var dimensionComplete = rows.Count(value => value.GridWidth is > 0 && value.GridHeight is > 0);
        var gaps = new List<string>();
        if (rows.Length != 303)
        {
            gaps.Add($"ClientMapResourceCount={rows.Length}/303");
        }
        if (dimensionComplete != rows.Length)
        {
            gaps.Add($"MissingMapDimensions={rows.Length - dimensionComplete}");
        }
        gaps.Add("NumericClientMapIdentityBindingPending");
        gaps.Add("PortalPlacementBindingPending");
        return new ClientMapResourceInventoryResult(
            rows.Length == 303,
            root,
            canFiles.Length,
            rows.Length,
            indoorMapResources,
            dimensionComplete,
            Array.AsReadOnly(rows),
            gaps.AsReadOnly());
    }

    internal static bool TryReadHmdPassGrid(ReadOnlySpan<byte> decoded, out int width, out int height)
    {
        width = 0;
        height = 0;
        if (decoded.Length < HmdSignature.Length ||
            !decoded[..HmdSignature.Length].SequenceEqual(Encoding.ASCII.GetBytes(HmdSignature)))
        {
            return false;
        }

        var lines = Encoding.ASCII.GetString(decoded).Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        for (var index = 0; index + 1 < lines.Length; index++)
        {
            if (!string.Equals(lines[index].Trim(), "Pass", StringComparison.Ordinal))
            {
                continue;
            }

            var dimensions = lines[index + 1].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (dimensions.Length == 2 &&
                int.TryParse(dimensions[0], out width) &&
                int.TryParse(dimensions[1], out height) &&
                width is > 0 and <= 512 &&
                height is > 0 and <= 512)
            {
                return true;
            }
        }

        width = 0;
        height = 0;
        return false;
    }

    private static async Task<(string Format, string? ResourcePath, string? ResourceSha256, string? NavigationFormat, string? NavigationPath, string? NavigationSha256, int? Width, int? Height)> ResolveResourceAsync(
        string root,
        string canPath,
        string resourceName,
        string resourceStem,
        CancellationToken cancellationToken)
    {
        var format = resourceName.EndsWith(".hmd", StringComparison.OrdinalIgnoreCase) ? "HMD" : "MDT";
        var canDirectory = Path.GetDirectoryName(canPath) ?? root;
        var unpackedResource = Path.Combine(canDirectory, resourceName.Replace('/', Path.DirectorySeparatorChar));
        var resourcePath = new[] { unpackedResource, $"{unpackedResource}Z" }.FirstOrDefault(File.Exists);
        string? resourceHash = null;
        byte[]? decodedResource = null;
        if (resourcePath is not null)
        {
            var source = await File.ReadAllBytesAsync(resourcePath, cancellationToken);
            resourceHash = Convert.ToHexString(SHA256.HashData(source)).ToLowerInvariant();
            decodedResource = resourcePath.EndsWith('Z') ? God2PackedFile.Decode(source) : source;
        }

        if (format == "HMD")
        {
            var width = 0;
            var height = 0;
            var hasGrid = decodedResource is not null && TryReadHmdPassGrid(decodedResource, out width, out height);
            return (format, resourcePath, resourceHash, hasGrid ? HmdSignature : null, resourcePath, resourceHash,
                hasGrid ? width : null, hasGrid ? height : null);
        }

        var mbd = await ResolveMbdAsync(root, canPath, resourceStem, cancellationToken);
        return (format, resourcePath, resourceHash, mbd.Path is null ? null : MbdSignature, mbd.Path, mbd.Sha256, mbd.Width, mbd.Height);
    }

    private static async Task<(string? Path, string? Sha256, int? Width, int? Height)> ResolveMbdAsync(
        string root,
        string canPath,
        string resourceStem,
        CancellationToken cancellationToken)
    {
        var leaf = Path.GetFileName(resourceStem);
        var relativeDirectory = Path.GetDirectoryName(resourceStem.Replace('/', Path.DirectorySeparatorChar));
        var canDirectory = Path.GetDirectoryName(canPath) ?? root;
        var unpacked = Path.Combine(canDirectory, relativeDirectory ?? string.Empty, $"{leaf}.mbd");
        foreach (var candidate in new[] { unpacked, $"{unpacked}Z" })
        {
            if (!File.Exists(candidate))
            {
                continue;
            }
            var source = await File.ReadAllBytesAsync(candidate, cancellationToken);
            var decoded = candidate.EndsWith('Z') ? God2PackedFile.Decode(source) : source;
            if (decoded.Length < 16 || Encoding.ASCII.GetString(decoded, 0, 8) != MbdSignature)
            {
                continue;
            }
            return (
                candidate,
                Convert.ToHexString(SHA256.HashData(source)).ToLowerInvariant(),
                BinaryPrimitives.ReadInt32LittleEndian(decoded.AsSpan(8, 4)),
                BinaryPrimitives.ReadInt32LittleEndian(decoded.AsSpan(12, 4)));
        }
        return (null, null, null, null);
    }

    private static string Relative(string root, string path) =>
        Path.GetRelativePath(root, path).Replace('\\', '/');
}
