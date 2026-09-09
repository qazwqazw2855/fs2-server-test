using System.Buffers.Binary;
using System.Text;

namespace God2.GameplayContentRecovery;

public sealed record CsvRow(int RecordIndex, string RawLine, IReadOnlyList<string> Fields);

public sealed record CsvZDocument(
    string Path,
    string SourceHash,
    long SourceSize,
    int ExpectedDecodedLength,
    byte Marker,
    string TextDecoder,
    int InvalidByteCount,
    IReadOnlyList<CsvRow> Rows,
    string Text);

public static class God2PackedFile
{
    static God2PackedFile()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public static byte[] Decode(ReadOnlySpan<byte> source)
    {
        if (source.Length < 7 || source[0] != 0x05 || source[1] != 0x16)
        {
            throw new InvalidDataException("The file does not use the God2 05 16 packed wrapper.");
        }

        var expectedLength = BinaryPrimitives.ReadInt32LittleEndian(source[2..6]);
        if (expectedLength < 0 || expectedLength > 512 * 1024 * 1024)
        {
            throw new InvalidDataException($"Invalid decoded length: {expectedLength}.");
        }

        var destination = new byte[expectedLength];
        var window = new byte[4096];
        Array.Fill(window, (byte)0x20);
        var windowPosition = 4078;
        var sourceIndex = 7;
        var destinationIndex = 0;
        var flags = 0;

        while (sourceIndex < source.Length && destinationIndex < expectedLength)
        {
            flags >>= 1;
            if ((flags & 0x100) == 0)
            {
                flags = source[sourceIndex++] | 0xff00;
            }

            if ((flags & 1) != 0)
            {
                if (sourceIndex >= source.Length)
                {
                    break;
                }

                var value = source[sourceIndex++];
                destination[destinationIndex++] = value;
                window[windowPosition] = value;
                windowPosition = (windowPosition + 1) & 4095;
                continue;
            }

            if (sourceIndex + 1 >= source.Length)
            {
                break;
            }

            var first = source[sourceIndex++];
            var second = source[sourceIndex++];
            var position = first | ((second & 0xf0) << 4);
            var lastIndex = (second & 0x0f) + 2;
            for (var index = 0; index <= lastIndex && destinationIndex < expectedLength; index++)
            {
                var value = window[(position + index) & 4095];
                destination[destinationIndex++] = value;
                window[windowPosition] = value;
                windowPosition = (windowPosition + 1) & 4095;
            }
        }

        if (destinationIndex != expectedLength)
        {
            throw new InvalidDataException($"Packed stream ended at {destinationIndex} bytes; expected {expectedLength}.");
        }

        return destination;
    }

    public static async Task<CsvZDocument> ReadCsvZAsync(string path, CancellationToken cancellationToken)
    {
        var source = await File.ReadAllBytesAsync(path, cancellationToken);
        var decoded = Decode(source);
        var text = DecodeCp936WithByteEscapes(decoded, out var invalidByteCount);
        var rows = ParseRows(text);
        var sourceHash = await ContentHash.Sha256FileAsync(path, cancellationToken);
        return new CsvZDocument(
            path,
            sourceHash,
            source.LongLength,
            decoded.Length,
            source[6],
            invalidByteCount == 0 ? "CP936-strict" : "CP936-byte-escape",
            invalidByteCount,
            rows,
            text);
    }

    private static string DecodeCp936WithByteEscapes(byte[] bytes, out int invalidByteCount)
    {
        var strict936 = Encoding.GetEncoding(936, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
        var builder = new StringBuilder(bytes.Length);
        invalidByteCount = 0;
        for (var index = 0; index < bytes.Length;)
        {
            if (bytes[index] < 0x80)
            {
                builder.Append((char)bytes[index++]);
                continue;
            }

            try
            {
                var oneByte = strict936.GetString(bytes, index, 1);
                builder.Append(oneByte);
                index++;
                continue;
            }
            catch (DecoderFallbackException)
            {
                // Most CP936 CJK characters require two bytes; test the pair below.
            }

            if (index + 1 < bytes.Length)
            {
                try
                {
                    var twoBytes = strict936.GetString(bytes, index, 2);
                    builder.Append(twoBytes);
                    index += 2;
                    continue;
                }
                catch (DecoderFallbackException)
                {
                    // Preserve the exact offending byte as an explicit ASCII evidence token.
                }
            }

            builder.Append("[byte:").Append(bytes[index].ToString("X2", System.Globalization.CultureInfo.InvariantCulture)).Append(']');
            invalidByteCount++;
            index++;
        }

        return builder.ToString();
    }

    public static IReadOnlyList<CsvRow> ParseRows(string text)
    {
        var rows = new List<CsvRow>();
        using var reader = new StringReader(text);
        string? line;
        var lineNumber = 0;
        while ((line = reader.ReadLine()) is not null)
        {
            lineNumber++;
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            rows.Add(new CsvRow(lineNumber, line, SplitCsvLine(line)));
        }

        return rows;
    }

    public static IReadOnlyList<string> SplitCsvLine(string line)
    {
        var result = new List<string>();
        var builder = new StringBuilder();
        var inQuotes = false;
        for (var index = 0; index < line.Length; index++)
        {
            var character = line[index];
            if (character == '"')
            {
                if (inQuotes && index + 1 < line.Length && line[index + 1] == '"')
                {
                    builder.Append('"');
                    index++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }
            }
            else if (character == ',' && !inQuotes)
            {
                result.Add(builder.ToString());
                builder.Clear();
            }
            else
            {
                builder.Append(character);
            }
        }

        if (inQuotes)
        {
            throw new InvalidDataException("CSV row contains an unterminated quoted field.");
        }

        result.Add(builder.ToString());
        return result;
    }
}

public sealed record GameDataSection(string Name, int DeclaredCount, int MarkerRecordIndex, IReadOnlyList<CsvRow> Rows);

public static class GameDataSections
{
    public static IReadOnlyDictionary<string, GameDataSection> Parse(IReadOnlyList<CsvRow> rows)
    {
        var result = new Dictionary<string, GameDataSection>(StringComparer.Ordinal);
        for (var index = 0; index < rows.Count; index++)
        {
            var fields = rows[index].Fields;
            var isMapHeaderMarker = fields.Count >= 2 &&
                                    string.Equals(fields[0], "Map_City_Coordniate", StringComparison.Ordinal);
            if ((!isMapHeaderMarker && fields.Count != 2) ||
                !IsIdentifier(fields[0]) ||
                !int.TryParse(fields[1], out var count) ||
                count < 0)
            {
                continue;
            }

            var sectionRows = rows.Skip(index + 1).Take(count).ToArray();
            if (sectionRows.Length != count)
            {
                throw new InvalidDataException($"Section {fields[0]} declares {count} rows but only {sectionRows.Length} remain.");
            }

            result[fields[0]] = new GameDataSection(fields[0], count, rows[index].RecordIndex, sectionRows);
        }

        return result;
    }

    private static bool IsIdentifier(string value)
    {
        if (string.IsNullOrEmpty(value) || !(char.IsLetter(value[0]) || value[0] == '_'))
        {
            return false;
        }

        return value.Skip(1).All(character => char.IsLetterOrDigit(character) || character == '_');
    }
}
