using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json;

namespace God2.RecoveredDatabaseImporter;

public static class RecoveryJsonCanonicalizer
{
    public static byte[] Canonicalize(JsonElement element)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = false }))
        {
            WriteCanonical(writer, element);
        }

        return stream.ToArray();
    }

    public static string Sha256(JsonElement element) =>
        Convert.ToHexString(SHA256.HashData(Canonicalize(element))).ToLowerInvariant();

    public static string Sha256File(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static void WriteCanonical(Utf8JsonWriter writer, JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in element.EnumerateObject().OrderBy(value => value.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    WriteCanonical(writer, property.Value);
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray())
                {
                    WriteCanonical(writer, item);
                }
                writer.WriteEndArray();
                break;
            case JsonValueKind.String:
                writer.WriteStringValue(element.GetString());
                break;
            case JsonValueKind.Number:
                writer.WriteRawValue(CanonicalizeNumber(element.GetRawText()), skipInputValidation: false);
                break;
            case JsonValueKind.True:
                writer.WriteBooleanValue(true);
                break;
            case JsonValueKind.False:
                writer.WriteBooleanValue(false);
                break;
            case JsonValueKind.Null:
                writer.WriteNullValue();
                break;
            default:
                throw new RecoveryImportException("schema.unsupported_json_value", $"Unsupported JSON value kind: {element.ValueKind}.");
        }
    }

    private static string CanonicalizeNumber(string raw)
    {
        var negative = raw[0] == '-';
        var unsigned = negative ? raw[1..] : raw;
        var exponentMarker = unsigned.IndexOfAny('e', 'E');
        var significand = exponentMarker >= 0 ? unsigned[..exponentMarker] : unsigned;
        var exponent = exponentMarker >= 0
            ? BigInteger.Parse(unsigned[(exponentMarker + 1)..], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture)
            : BigInteger.Zero;
        var decimalPoint = significand.IndexOf('.');
        var fractionalDigits = decimalPoint >= 0 ? significand.Length - decimalPoint - 1 : 0;
        var digits = decimalPoint >= 0 ? significand.Remove(decimalPoint, 1) : significand;
        var significant = digits.TrimStart('0');
        if (significant.Length == 0)
        {
            return "0";
        }

        exponent -= fractionalDigits;
        var trailingZeros = significant.Length - significant.TrimEnd('0').Length;
        if (trailingZeros != 0)
        {
            significant = significant[..^trailingZeros];
            exponent += trailingZeros;
        }

        var scientificExponent = exponent + significant.Length - 1;
        var coefficient = significant.Length == 1
            ? significant
            : $"{significant[0]}.{significant[1..]}";
        var sign = negative ? "-" : string.Empty;
        return scientificExponent.IsZero
            ? string.Concat(sign, coefficient)
            : string.Concat(sign, coefficient, "e", scientificExponent.ToString(CultureInfo.InvariantCulture));
    }
}

public static class SemanticOracle
{
    public static SemanticOracleResult Compare(
        JsonElement expected,
        JsonElement actual,
        string sourceEvidence,
        string responsibleSubsystem)
    {
        var expectedHash = RecoveryJsonCanonicalizer.Sha256(expected);
        var actualHash = RecoveryJsonCanonicalizer.Sha256(actual);
        if (string.Equals(expectedHash, actualHash, StringComparison.Ordinal))
        {
            return new SemanticOracleResult(true, expectedHash, actualHash, null);
        }

        var divergence = FindFirstDivergence(expected, actual, "$", out var expectedValue, out var actualValue);
        return new SemanticOracleResult(
            false,
            expectedHash,
            actualHash,
            new OracleCounterexample(
                sourceEvidence,
                expectedValue,
                actualValue,
                responsibleSubsystem,
                divergence));
    }

    private static string FindFirstDivergence(
        JsonElement expected,
        JsonElement actual,
        string path,
        out string expectedValue,
        out string actualValue)
    {
        if (expected.ValueKind != actual.ValueKind)
        {
            expectedValue = expected.GetRawText();
            actualValue = actual.GetRawText();
            return path;
        }

        if (expected.ValueKind == JsonValueKind.Object)
        {
            var left = expected.EnumerateObject().ToDictionary(value => value.Name, value => value.Value, StringComparer.Ordinal);
            var right = actual.EnumerateObject().ToDictionary(value => value.Name, value => value.Value, StringComparer.Ordinal);
            foreach (var key in left.Keys.Union(right.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal))
            {
                if (!left.TryGetValue(key, out var leftValue) || !right.TryGetValue(key, out var rightValue))
                {
                    expectedValue = left.TryGetValue(key, out leftValue) ? leftValue.GetRawText() : "<missing>";
                    actualValue = right.TryGetValue(key, out rightValue) ? rightValue.GetRawText() : "<missing>";
                    return $"{path}.{key}";
                }

                if (!string.Equals(
                        RecoveryJsonCanonicalizer.Sha256(leftValue),
                        RecoveryJsonCanonicalizer.Sha256(rightValue),
                        StringComparison.Ordinal))
                {
                    return FindFirstDivergence(leftValue, rightValue, $"{path}.{key}", out expectedValue, out actualValue);
                }
            }
        }
        else if (expected.ValueKind == JsonValueKind.Array)
        {
            var left = expected.EnumerateArray().ToArray();
            var right = actual.EnumerateArray().ToArray();
            var count = Math.Max(left.Length, right.Length);
            for (var index = 0; index < count; index++)
            {
                if (index >= left.Length || index >= right.Length)
                {
                    expectedValue = index < left.Length ? left[index].GetRawText() : "<missing>";
                    actualValue = index < right.Length ? right[index].GetRawText() : "<missing>";
                    return $"{path}[{index}]";
                }

                if (!string.Equals(
                        RecoveryJsonCanonicalizer.Sha256(left[index]),
                        RecoveryJsonCanonicalizer.Sha256(right[index]),
                        StringComparison.Ordinal))
                {
                    return FindFirstDivergence(left[index], right[index], $"{path}[{index}]", out expectedValue, out actualValue);
                }
            }
        }

        expectedValue = expected.GetRawText();
        actualValue = actual.GetRawText();
        return path;
    }
}
