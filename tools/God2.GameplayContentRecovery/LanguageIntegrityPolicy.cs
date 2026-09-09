namespace God2.GameplayContentRecovery;

public static class LanguageIntegrityPolicy
{
    private static readonly string[] DisplayMarkers =
    [
        "name", "title", "description", "text", "body", "message", "note", "label",
        "display", "cache", "objective", "reward", "reason", "diagnostic", "comment",
        "detail", "zh_tw"
    ];

    private static readonly string[] NonDisplayMarkers =
    [
        "raw", "source", "path", "uri", "url", "hash", "code", "key",
        "status", "schema", "authority", "evidence", "version", "json", "payload",
        "metadata", "sql", "regex"
    ];

    private static readonly string[] SensitiveMarkers =
    [
        "password", "credential", "secret", "login_token", "session_key", "authentication"
    ];

    private static readonly string[] MojibakeFragments =
    [
        "缍撳", "鐗堟", "鍕欑", "璩囨", "閫ｇ", "鏈嶅", "鍩疯", "瑷",
        "闂滄", "鐭", "寤虹", "鍦板", "鏁稿", "妯″", "瀹屾", "鎴愬"
    ];

    public static bool IsDisplayTextColumn(string columnName)
    {
        var normalized = columnName.ToLowerInvariant();
        return !NonDisplayMarkers.Any(normalized.Contains) && DisplayMarkers.Any(normalized.Contains);
    }

    public static bool IsSensitiveColumn(string columnName)
    {
        var normalized = columnName.ToLowerInvariant();
        return SensitiveMarkers.Any(normalized.Contains);
    }

    public static string? DetectMojibake(string value)
    {
        if (value.Contains('\ufffd'))
        {
            return "UnicodeReplacementCharacter";
        }

        if (value.Any(character => character is '\0' or >= '\u0001' and <= '\u0008' or '\u000b' or '\u000c' or >= '\u000e' and <= '\u001f'))
        {
            return "UnexpectedControlCharacter";
        }

        return MojibakeFragments.Any(value.Contains) ? "KnownUtf8MojibakeSequence" : null;
    }

    public static string SafeSample(string value)
    {
        var normalized = value.Replace("\r", "\\r", StringComparison.Ordinal).Replace("\n", "\\n", StringComparison.Ordinal);
        return normalized.Length <= 160 ? normalized : normalized[..160] + "…";
    }
}
