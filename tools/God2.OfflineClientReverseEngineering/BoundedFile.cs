namespace God2.OfflineClientReverseEngineering;

internal static class BoundedFile
{
    public static byte[] ReadAllBytes(string path, long maximumBytes, string label)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var file = new FileInfo(path);
        if (!file.Exists)
        {
            throw new FileNotFoundException($"{label} does not exist.", file.FullName);
        }
        if (file.Length < 0 || file.Length > maximumBytes || file.Length > int.MaxValue)
        {
            throw new InvalidDataException($"{label} exceeds the {maximumBytes:N0}-byte input budget.");
        }

        var bytes = new byte[checked((int)file.Length)];
        using var stream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.Read);
        stream.ReadExactly(bytes);
        if (stream.ReadByte() != -1)
        {
            throw new InvalidDataException($"{label} changed while it was being read.");
        }
        return bytes;
    }
}
