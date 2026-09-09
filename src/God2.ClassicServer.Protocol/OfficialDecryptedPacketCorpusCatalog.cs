using System.Buffers.Binary;
using System.Collections.ObjectModel;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace God2.ClassicServer.Protocol;

public sealed record OfficialDecryptedPacketDefinition(
    string Id,
    PacketDirection Direction,
    byte? Opcode,
    int HeaderSize,
    IReadOnlyList<int> Lengths,
    bool PlaintextAvailable,
    string Status,
    bool CryptoVerified,
    bool ParserVerified,
    bool BuilderVerified,
    string? PlaintextSha256);

public sealed record OfficialDecryptedPacketMatch(
    OfficialDecryptedPacketDefinition Definition,
    bool ExactSample);

/// <summary>
/// Production-readable, immutable index of the verified decrypted packet corpus.
/// It recognizes evidence at a plaintext boundary but never grants unknown packets
/// gameplay semantics or database mutation authority.
/// </summary>
public sealed class OfficialDecryptedPacketCorpusCatalog
{
    public const int ExpectedPacketDefinitions = 112;
    public const int ExpectedPlaintextSamples = 96;
    public const string ResourceSuffix = "DecryptedPacketCorpus.20260807.v2.json";

    private readonly ReadOnlyCollection<OfficialDecryptedPacketDefinition> _definitions;

    public OfficialDecryptedPacketCorpusCatalog()
        : this(LoadSnapshot())
    {
    }

    private OfficialDecryptedPacketCorpusCatalog(CorpusSnapshot snapshot)
    {
        SchemaVersion = snapshot.SchemaVersion;
        Source = snapshot.Source;
        SourceZipSha256 = snapshot.SourceZipSha256;
        _definitions = Array.AsReadOnly(snapshot.Entries.Select(ToDefinition).ToArray());

        var errors = Validate();
        if (errors.Count != 0)
        {
            throw new InvalidDataException($"Embedded decrypted packet corpus is invalid: {string.Join(", ", errors)}");
        }
    }

    public int SchemaVersion { get; }

    public string Source { get; }

    public string SourceZipSha256 { get; }

    public IReadOnlyList<OfficialDecryptedPacketDefinition> Definitions => _definitions;

    public IReadOnlyList<OfficialDecryptedPacketDefinition> Snapshot() => _definitions;

    public OfficialDecryptedPacketMatch? MatchExactPlaintext(
        PacketDirection direction,
        ReadOnlySpan<byte> plaintext)
    {
        var candidates = MatchPlaintextFamily(direction, plaintext);
        if (candidates.Count == 0)
        {
            return null;
        }

        var sha256 = Convert.ToHexString(SHA256.HashData(plaintext));
        var exact = candidates.FirstOrDefault(value =>
            string.Equals(value.PlaintextSha256, sha256, StringComparison.OrdinalIgnoreCase));
        return exact is null ? null : new OfficialDecryptedPacketMatch(exact, ExactSample: true);
    }

    public IReadOnlyList<OfficialDecryptedPacketDefinition> MatchPlaintextFamily(
        PacketDirection direction,
        ReadOnlySpan<byte> plaintext)
    {
        if (direction is not (PacketDirection.ClientToServer or PacketDirection.ServerToClient) ||
            plaintext.IsEmpty)
        {
            return [];
        }

        var result = new List<OfficialDecryptedPacketDefinition>();
        foreach (var definition in _definitions)
        {
            if (definition.PlaintextAvailable &&
                definition.Direction == direction &&
                definition.Lengths.Contains(plaintext.Length) &&
                BoundaryMatches(definition, plaintext))
            {
                result.Add(definition);
            }
        }
        return result.AsReadOnly();
    }

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (SchemaVersion != 2)
        {
            errors.Add("packet_corpus.schema_version_invalid");
        }
        if (!string.Equals(Source, "God2_Decrypted_Packet_Corpus", StringComparison.Ordinal) ||
            !IsSha256(SourceZipSha256))
        {
            errors.Add("packet_corpus.source_identity_invalid");
        }
        if (_definitions.Count != ExpectedPacketDefinitions ||
            _definitions.Count(value => value.PlaintextAvailable) != ExpectedPlaintextSamples)
        {
            errors.Add("packet_corpus.count_mismatch");
        }
        if (_definitions.Select(value => value.Id).Distinct(StringComparer.Ordinal).Count() != _definitions.Count)
        {
            errors.Add("packet_corpus.duplicate_id");
        }
        if (_definitions.Any(value =>
                string.IsNullOrWhiteSpace(value.Id) ||
                value.Direction is not (PacketDirection.ClientToServer or PacketDirection.ServerToClient) ||
                value.HeaderSize is < 0 or > 3 ||
                value.Lengths.Count == 0 ||
                value.Lengths.Any(length => length <= 0 || length > ushort.MaxValue) ||
                value.PlaintextAvailable != IsSha256(value.PlaintextSha256)))
        {
            errors.Add("packet_corpus.definition_invalid");
        }
        return errors.AsReadOnly();
    }

    private static bool BoundaryMatches(
        OfficialDecryptedPacketDefinition definition,
        ReadOnlySpan<byte> plaintext)
    {
        if (definition.HeaderSize >= 2 &&
            (plaintext.Length < 2 || BinaryPrimitives.ReadUInt16LittleEndian(plaintext) != plaintext.Length))
        {
            return false;
        }
        if (definition.Opcode is null)
        {
            return true;
        }
        var opcodeOffset = definition.HeaderSize switch
        {
            0 or 1 => 0,
            _ => 2
        };
        return plaintext.Length > opcodeOffset && plaintext[opcodeOffset] == definition.Opcode.Value;
    }

    private static OfficialDecryptedPacketDefinition ToDefinition(CorpusEntry entry) => new(
        entry.Id,
        entry.Direction switch
        {
            "C2S" => PacketDirection.ClientToServer,
            "S2C" => PacketDirection.ServerToClient,
            _ => PacketDirection.Unknown
        },
        entry.Opcode,
        entry.HeaderSize,
        Array.AsReadOnly(entry.Lengths),
        entry.PlaintextAvailable,
        entry.Status,
        entry.CryptoVerified,
        entry.ParserVerified,
        entry.BuilderVerified,
        entry.PlaintextSha256);

    private static CorpusSnapshot LoadSnapshot()
    {
        var assembly = typeof(OfficialDecryptedPacketCorpusCatalog).Assembly;
        var resourceName = assembly.GetManifestResourceNames()
            .SingleOrDefault(value => value.EndsWith(ResourceSuffix, StringComparison.Ordinal));
        if (resourceName is null)
        {
            throw new InvalidDataException("Embedded decrypted packet corpus resource was not found.");
        }

        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidDataException("Embedded decrypted packet corpus resource could not be opened.");
        return JsonSerializer.Deserialize<CorpusSnapshot>(stream)
            ?? throw new InvalidDataException("Embedded decrypted packet corpus resource could not be parsed.");
    }

    private static bool IsSha256(string? value) =>
        value is { Length: 64 } && value.All(Uri.IsHexDigit);

    private sealed class CorpusSnapshot
    {
        [JsonPropertyName("schema_version")]
        public int SchemaVersion { get; set; }

        [JsonPropertyName("source")]
        public string Source { get; set; } = string.Empty;

        [JsonPropertyName("source_zip_sha256")]
        public string SourceZipSha256 { get; set; } = string.Empty;

        [JsonPropertyName("entries")]
        public CorpusEntry[] Entries { get; set; } = [];
    }

    private sealed class CorpusEntry
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("direction")]
        public string Direction { get; set; } = string.Empty;

        [JsonPropertyName("opcode")]
        public byte? Opcode { get; set; }

        [JsonPropertyName("header_size")]
        public int HeaderSize { get; set; }

        [JsonPropertyName("lengths")]
        public int[] Lengths { get; set; } = [];

        [JsonPropertyName("plaintext_available")]
        public bool PlaintextAvailable { get; set; }

        [JsonPropertyName("status")]
        public string Status { get; set; } = string.Empty;

        [JsonPropertyName("crypto_verified")]
        public bool CryptoVerified { get; set; }

        [JsonPropertyName("parser_verified")]
        public bool ParserVerified { get; set; }

        [JsonPropertyName("builder_verified")]
        public bool BuilderVerified { get; set; }

        [JsonPropertyName("plaintext_sha256")]
        public string? PlaintextSha256 { get; set; }
    }
}
