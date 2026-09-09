using System.Text.Json;
using System.Text.Json.Serialization;

namespace God2.ClientInstrumentation.Analyzer;

internal sealed record TestAccount(
    string Key,
    string Account,
    string Password,
    string Class);

internal static class TestAccountProvider
{
    private static readonly (string Key, string Class)[] RequiredAccounts =
    [
        ("OfficialA", "Swordsman"),
        ("OfficialB", "Taoist"),
        ("OfficialC", "Pharmacist"),
        ("OfficialD", "Warlock")
    ];

    public static IReadOnlyList<TestAccount> Load(string repoRoot)
    {
        var path = Path.Combine(repoRoot, "config", "test-accounts.local.json");
        if (!File.Exists(path))
        {
            throw new InvalidOperationException("Test account config is missing: config/test-accounts.local.json");
        }

        using var stream = File.OpenRead(path);
        var config = JsonSerializer.Deserialize<Dictionary<string, TestAccountConfig>>(stream, JsonOptions.Strict);
        if (config is null)
        {
            throw new InvalidOperationException("Test account config is invalid.");
        }

        var accounts = new List<TestAccount>(RequiredAccounts.Length);
        var uniqueAccounts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var required in RequiredAccounts)
        {
            if (!config.TryGetValue(required.Key, out var entry) || entry is null)
            {
                throw new InvalidOperationException($"Test account entry is missing: {required.Key}");
            }

            if (string.IsNullOrWhiteSpace(entry.Account))
            {
                throw new InvalidOperationException($"Test account value is empty: {required.Key}.Account");
            }

            if (string.IsNullOrEmpty(entry.Password))
            {
                throw new InvalidOperationException($"Test account value is empty: {required.Key}.Password");
            }

            if (!string.Equals(entry.Class, required.Class, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Test account class mismatch: {required.Key}");
            }

            if (!uniqueAccounts.Add(entry.Account))
            {
                throw new InvalidOperationException("Test account values must be unique.");
            }

            accounts.Add(new TestAccount(required.Key, entry.Account, entry.Password, entry.Class));
        }

        return accounts;
    }

    private sealed record TestAccountConfig
    {
        [JsonPropertyName("Account")]
        public string Account { get; init; } = "";

        [JsonPropertyName("Password")]
        public string Password { get; init; } = "";

        [JsonPropertyName("Class")]
        public string Class { get; init; } = "";
    }

    private static class JsonOptions
    {
        public static readonly JsonSerializerOptions Strict = new()
        {
            AllowTrailingCommas = false,
            ReadCommentHandling = JsonCommentHandling.Disallow,
            PropertyNameCaseInsensitive = false
        };
    }
}
