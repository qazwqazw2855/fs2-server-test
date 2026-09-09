using System.Buffers.Binary;
using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using God2.ClassicServer.Protocol;
using God2.ClassicServer.Runtime;

var repositoryRoot = FindRepositoryRoot(AppContext.BaseDirectory);
var artifactsRoot = Path.Combine(repositoryRoot, "artifacts");
var corpusRoot = Path.Combine(artifactsRoot, "God2_Decrypted_Packet_Corpus");
var zipPath = Path.Combine(artifactsRoot, "God2_Decrypted_Packet_Corpus.zip");
var zipHashPath = zipPath + ".sha256";

EnsureSafeOutput(repositoryRoot, corpusRoot, zipPath);
if (Directory.Exists(corpusRoot))
{
    Directory.Delete(corpusRoot, recursive: true);
}

Directory.CreateDirectory(corpusRoot);
Directory.CreateDirectory(Path.Combine(corpusRoot, "catalog"));
Directory.CreateDirectory(Path.Combine(corpusRoot, "packets"));
Directory.CreateDirectory(Path.Combine(corpusRoot, "unknown"));
Directory.CreateDirectory(Path.Combine(corpusRoot, "reports"));
Directory.CreateDirectory(Path.Combine(corpusRoot, "manifests"));

var categories = new[]
{
    "00_System", "01_Handshake", "02_Login", "03_Character", "04_World", "05_Map",
    "06_Movement", "07_NPC", "08_Monster", "09_Inventory", "10_Item", "11_Equipment",
    "12_Skill", "13_Combat", "14_Battle", "15_Pet", "16_Immortal", "17_Party",
    "18_Guild", "19_Chat", "20_Trade", "21_Merchant", "22_Quest", "23_Social",
    "24_Status", "25_Buff_Debuff", "26_LifeSkill", "27_Crafting", "28_Mail",
    "29_Friend", "30_Team", "31_Event", "32_UI", "33_Heartbeat", "34_Logout",
    "35_Error", "90_Unknown", "91_Unclassified", "92_Internal"
};
foreach (var category in categories)
{
    Directory.CreateDirectory(Path.Combine(corpusRoot, "packets", category));
}

var packets = new List<PacketRecord>();
var evidenceCatalog = new OfficialEvidencePackage20260806Catalog();
var evidenceCatalogErrors = evidenceCatalog.Validate();
if (evidenceCatalogErrors.Count != 0)
{
    throw new InvalidOperationException($"Evidence package catalog validation failed: {string.Join(", ", evidenceCatalogErrors)}");
}
foreach (var family in evidenceCatalog.SnapshotDecodedFamilies())
{
    var direction = Direction(family.Direction);
    var semantic = SemanticFor(direction, family.Opcode, family.FrameLengths);
    packets.Add(new PacketRecord
    {
        Id = $"evidence-package-20260806-{direction.ToLowerInvariant()}-opcode-{family.Opcode:x2}",
        Direction = direction,
        Opcode = family.Opcode,
        OpcodeHex = $"0x{family.Opcode:X2}",
        Name = semantic.Name,
        Category = semantic.Category,
        Subcategory = semantic.Subcategory,
        HeaderSize = 3,
        Length = family.FrameLengths.Count == 1 ? family.FrameLengths[0].ToString(CultureInfo.InvariantCulture) : string.Join('|', family.FrameLengths),
        VariableLength = family.FrameLengths.Count > 1,
        PayloadSize = family.FrameLengths.Count == 1 ? Math.Max(0, family.FrameLengths[0] - 3).ToString(CultureInfo.InvariantCulture) : "Unknown",
        EncryptedOnWire = "true",
        Encryption = direction == "C2S" ? "Captured at PreEncrypt plaintext boundary" : "Captured at PostDecrypt plaintext boundary",
        Compression = "Unknown",
        Parser = semantic.Parser,
        Builder = semantic.Builder,
        Handler = semantic.Handler,
        RuntimeUsage = semantic.RuntimeUsage,
        SourceFile = "src/God2.ClassicServer.Protocol/OfficialEvidencePackage20260806Catalog.cs",
        SourceSymbol = "OfficialEvidencePackage20260806Catalog.DecodedFamilies",
        Evidence = $"PreEncrypt/PostDecrypt aggregate; observations={family.ObservedCount}; uniquePayloads={family.UniquePayloadCount}",
        EvidenceLevel = "Runtime captured aggregate (payload-free)",
        Confidence = semantic.Confidence,
        PlaintextAvailable = false,
        Status = "StaticOnly",
        IsPlaintextGap = true,
        PreviousStatus = "StaticOnly",
        RuntimeTriggered = true,
        CryptoVerified = true,
        ObservedLengths = family.FrameLengths.ToArray(),
        Notes = "Repository policy retained aggregate plaintext structure but not raw payload bytes. No packet.bin is generated.",
        Fields =
        [
            F(0, 2, "Length", "UInt16LE", string.Join('|', family.FrameLengths)),
            F(2, 1, "Opcode", "UInt8", family.Opcode),
            F(3, family.FrameLengths.Count == 1 ? Math.Max(0, family.FrameLengths[0] - 3) : 0, "Unknown_0x03", family.FrameLengths.Count == 1 ? "bytes" : "variable bytes", "Unknown")
        ]
    });
}

foreach (var handlerFamily in evidenceCatalog.SnapshotHandlerFamilies())
{
    packets.Add(new PacketRecord
    {
        Id = $"evidence-package-20260806-handler-decoded-opcode-{handlerFamily.Opcode:x2}",
        Direction = "Unknown",
        Opcode = handlerFamily.Opcode,
        OpcodeHex = $"0x{handlerFamily.Opcode:X2}",
        Name = $"HandlerDecodedPayload_{handlerFamily.Opcode:X2}",
        Category = "90_Unknown",
        Subcategory = "HandlerDecoded payload without proven frame envelope/direction",
        HeaderSize = 0,
        Length = "Unknown",
        VariableLength = false,
        PayloadSize = handlerFamily.PayloadLength.ToString(CultureInfo.InvariantCulture),
        EncryptedOnWire = "Unknown",
        Encryption = "HandlerDecoded boundary proves decoded handler payload, but repository aggregate omits direction and transport envelope.",
        Compression = "Unknown",
        Parser = "Unknown client handler",
        Builder = "Unknown",
        Handler = $"Recovered handler address 0x{handlerFamily.HandlerAddress:X8}",
        RuntimeUsage = "Handler-level structural evidence only",
        SourceFile = "src/God2.ClassicServer.Protocol/OfficialEvidencePackage20260806Catalog.cs",
        SourceSymbol = "OfficialEvidencePackage20260806Catalog.HandlerFamilies",
        Evidence = $"HandlerDecoded aggregate; observations={handlerFamily.ObservedCount}; uniquePayloads={handlerFamily.UniquePayloadCount}",
        EvidenceLevel = "Runtime captured handler aggregate (payload-free)",
        Confidence = "Recovered",
        PlaintextAvailable = false,
        Status = "StaticOnlyHandlerPayload",
        IsPlaintextGap = true,
        PreviousStatus = "StaticOnlyHandlerPayload",
        RuntimeTriggered = true,
        CryptoVerified = false,
        ObservedLengths = [handlerFamily.PayloadLength],
        Notes = "Not promoted to a packet frame: direction, length prefix, checksum, and transport crypto relation are Unknown.",
        Fields = [F(0, handlerFamily.PayloadLength, "HandlerPayload", "bytes", "Unknown")]
    });
}

AddWireOnlyRegistryEvidence(packets);
AddVerifiedSamples(packets, repositoryRoot);
ApplyPlaintextGapRecovery(packets, repositoryRoot);

if (packets.Count != 112 || packets.Count(value => value.IsPlaintextGap) != 83)
{
    throw new InvalidOperationException($"Corpus baseline changed: packets={packets.Count}, gaps={packets.Count(value => value.IsPlaintextGap)}; expected 112/83.");
}

foreach (var packet in packets.OrderBy(value => value.Category).ThenBy(value => value.Direction).ThenBy(value => value.Opcode ?? int.MaxValue).ThenBy(value => value.Name))
{
    WritePacketDirectory(corpusRoot, packet);
}

WriteCatalogs(corpusRoot, packets);
WriteReports(corpusRoot, packets);
WriteRootReadme(corpusRoot, packets);

var manifest = new
{
    schema_version = 1,
    generated_at_utc = DateTimeOffset.UtcNow,
    generator = "tools/God2.PacketCorpusExporter",
    repository = "God2 Classic Server",
    packet_definitions = packets.Count,
    c2s = packets.Count(value => value.Direction == "C2S"),
    s2c = packets.Count(value => value.Direction == "S2C"),
    bidirectional = packets.Count(value => value.Direction == "Bidirectional"),
    unknown_direction = packets.Count(value => value.Direction == "Unknown"),
    plaintext_samples = packets.Count(value => value.PlaintextAvailable),
    static_only = packets.Count(value => !value.PlaintextAvailable),
    plaintext_gap_total = packets.Count(value => value.IsPlaintextGap),
    plaintext_gap_recovered = packets.Count(value => value.IsPlaintextGap && value.Status == "PLAINTEXT_RECOVERED"),
    plaintext_gap_remaining = packets.Count(value => value.IsPlaintextGap && value.Status != "PLAINTEXT_RECOVERED"),
    source_policy = "Only runtime PreEncrypt/PostDecrypt bytes, exact-session ciphertext decryptions, correlated HandlerDecoded records, existing verified server decoded/builder boundaries, or explicitly unencrypted canonical bytes are packet.bin. Unverified wire ciphertext is never copied as plaintext.",
    sanitization = "The semantically verified A5 LoginRequest sample has credential fields replaced in-place with synthetic ASCII. Newly recovered opaque runtime samples remain byte-exact because mutating unknown fields would violate plaintext evidence; their semantics are not guessed.",
    git = "Unavailable: repository root contains no .git directory."
};
WriteJson(Path.Combine(corpusRoot, "manifests", "build_manifest.json"), manifest);
WriteFileHashes(corpusRoot);
ValidateCorpus(corpusRoot, packets);

if (File.Exists(zipPath))
{
    File.Delete(zipPath);
}
ZipFile.CreateFromDirectory(corpusRoot, zipPath, CompressionLevel.Optimal, includeBaseDirectory: true);
var zipHash = Sha256File(zipPath);
File.WriteAllText(zipHashPath, $"{zipHash}  {Path.GetFileName(zipPath)}{Environment.NewLine}", new UTF8Encoding(false));

Console.WriteLine(JsonSerializer.Serialize(new
{
    result = "PASS_WITH_VALIDATED_PLAINTEXT_GAPS",
    total = packets.Count,
    c2s = packets.Count(value => value.Direction == "C2S"),
    s2c = packets.Count(value => value.Direction == "S2C"),
    bidirectional = packets.Count(value => value.Direction == "Bidirectional"),
    unknownDirection = packets.Count(value => value.Direction == "Unknown"),
    plaintextSamples = packets.Count(value => value.PlaintextAvailable),
    staticOnly = packets.Count(value => !value.PlaintextAvailable),
    newlyRecovered = packets.Count(value => value.IsPlaintextGap && value.Status == "PLAINTEXT_RECOVERED"),
    remainingWithoutPlaintext = packets.Count(value => value.IsPlaintextGap && value.Status != "PLAINTEXT_RECOVERED"),
    unknown = packets.Count(value => value.Category == "90_Unknown"),
    output = Path.GetRelativePath(repositoryRoot, zipPath).Replace('\\', '/'),
    zipSha256 = zipHash
}, JsonDefaults.Options));

static void AddWireOnlyRegistryEvidence(List<PacketRecord> packets)
{
    var skip = new HashSet<string>(StringComparer.Ordinal)
    {
        OfficialLoginEvidenceCatalog.LoginRequestKnowledgeId,
        OfficialLoginEvidenceCatalog.LoginSuccessResponseKnowledgeId
    };
    foreach (var knowledge in ProtocolRegistry.Official.Packets)
    {
        if (skip.Contains(knowledge.Id) || knowledge.Id.StartsWith("evidence-package-", StringComparison.Ordinal))
        {
            continue;
        }

        packets.Add(new PacketRecord
        {
            Id = $"wire-evidence-{knowledge.Id}",
            Direction = Direction(knowledge.Direction),
            Opcode = ParseCandidateOpcode(knowledge.OpcodeCandidate),
            OpcodeHex = ParseCandidateOpcode(knowledge.OpcodeCandidate) is { } opcode ? $"0x{opcode:X4}" : "Unknown",
            Name = $"WireEvidence_{SafeName(knowledge.Id)}",
            Category = "90_Unknown",
            Subcategory = knowledge.Family,
            HeaderSize = 2,
            Length = knowledge.Length.ToString(CultureInfo.InvariantCulture),
            VariableLength = false,
            PayloadSize = Math.Max(0, knowledge.Length - 2).ToString(CultureInfo.InvariantCulture),
            EncryptedOnWire = "Unknown",
            Encryption = "Wire capture; payload may be encrypted/obfuscated. OpcodeCandidate is not promoted to plaintext opcode.",
            Compression = "Unknown",
            Parser = "PacketDeserializer.Classify (wire signature only)",
            Builder = "Unknown",
            Handler = "ProtocolRuntime evidence path",
            RuntimeUsage = "Wire classification/evidence only",
            SourceFile = "src/God2.ClassicServer.Protocol/ProtocolFoundation.cs",
            SourceSymbol = "OfficialProtocolKnowledge.CreateDefault",
            Evidence = string.Join("; ", knowledge.EvidenceSources.Select(value => value.Path)),
            EvidenceLevel = "Static wire evidence",
            Confidence = knowledge.Confidence.ToString(),
            PlaintextAvailable = false,
            Status = "WireOnlyCiphertextCandidate",
            IsPlaintextGap = true,
            PreviousStatus = "WireOnlyCiphertextCandidate",
            RuntimeTriggered = true,
            CryptoVerified = false,
            ObservedLengths = [knowledge.Length],
            WireSamples = knowledge.Samples.ToArray(),
            Notes = "Samples in ProtocolRegistry are deliberately not copied: the repository states their encryption/obfuscation is not decoded.",
            Fields =
            [
                F(0, 2, "Length", "UInt16LE", knowledge.Length),
                F(2, Math.Max(0, knowledge.Length - 2), "WireCiphertextOrObfuscatedPayload", "bytes", "Unknown")
            ]
        });
    }
}

static void AddVerifiedSamples(List<PacketRecord> packets, string repositoryRoot)
{
    var loginRequestPath = Directory.GetFiles(
            Path.Combine(repositoryRoot, "src", "God2.ClassicServer.Protocol", "Evidence", "ProtocolEvidenceRecovery", "VerifiedRaw", "Login"),
            "login-request-208-*.bin")
        .OrderBy(value => value, StringComparer.Ordinal)
        .First();
    var encodedLoginRequest = File.ReadAllBytes(loginRequestPath);
    var decodedLoginRequest = OfficialLoginWireTransform.Decode(encodedLoginRequest);
    SanitizeFixedAscii(decodedLoginRequest.AsSpan(3, 24), "TEST_ACCOUNT");
    SanitizeFixedAscii(decodedLoginRequest.AsSpan(27, 24), "SANITIZED_VALUE");
    decodedLoginRequest[^1] = OfficialLoginWireTransform.ComputeChecksum(decodedLoginRequest);
    var sanitizedEncodedLoginRequest = OfficialLoginWireTransform.Encode(decodedLoginRequest);
    AddSample(packets, new PacketRecord
    {
        Id = "runtime-login-request-sanitized",
        Direction = "C2S", Opcode = decodedLoginRequest[2], OpcodeHex = $"0x{decodedLoginRequest[2]:X2}", Name = "LoginRequest",
        Category = "02_Login", Subcategory = "Authentication", HeaderSize = 3, Length = decodedLoginRequest.Length.ToString(CultureInfo.InvariantCulture),
        VariableLength = false, PayloadSize = (decodedLoginRequest.Length - 3).ToString(CultureInfo.InvariantCulture), EncryptedOnWire = "true",
        Encryption = "OfficialLoginWireTransform; packet.bin is Decode(frame)", Compression = "false",
        Parser = "OfficialClientLoginProtocolFrames.TryDecodeSensitiveLoginRequest", Builder = "Sanitized reconstruction from VERIFIED_RAW login request",
        Handler = "TcpNetworkHost.TryHandleOfficialLoginRequestAsync", RuntimeUsage = "Production authentication ingress",
        SourceFile = "src/God2.ClassicServer.Runtime/OfficialClientLoginProtocolFrames.cs", SourceSymbol = "TryDecodeSensitiveLoginRequest",
        Evidence = Path.GetRelativePath(repositoryRoot, loginRequestPath).Replace('\\', '/'), EvidenceLevel = "Runtime parser + VERIFIED_RAW fixture",
        Confidence = "Verified", PlaintextAvailable = true, Status = "SanitizedVerifiedSample", Sample = decodedLoginRequest,
        Fields =
        [
            F(0, 2, "Length", "UInt16LE", decodedLoginRequest.Length), F(2, 1, "Opcode", "UInt8", decodedLoginRequest[2]),
            F(3, 24, "Username", "ASCII NUL-padded", "TEST_ACCOUNT"), F(27, 24, "Password", "ASCII NUL-padded", "SANITIZED_VALUE"),
            F(51, decodedLoginRequest.Length - 52, "Unknown_0x33", "bytes", "Unknown"), F(decodedLoginRequest.Length - 1, 1, "Checksum", "UInt8", decodedLoginRequest[^1])
        ],
        Notes = "Credentials were replaced in-place. Original field widths, NUL padding, frame length, transform, and checksum are preserved."
    });

    var encodedSuccess = OfficialClientLoginProtocolFrames.BuildLoginSuccessServerGroupBootstrap(
        sanitizedEncodedLoginRequest,
        new byte[] { 192, 0, 2, 1 },
        2592);
    var decodedSuccess = OfficialLoginWireTransform.Decode(encodedSuccess);
    AddSample(packets, Sample("runtime-login-success-bootstrap", "S2C", decodedSuccess, "LoginSuccessServerGroupBootstrap", "02_Login", "Success / Character bootstrap",
        "OfficialLoginWireTransform", "OfficialClientLoginProtocolFrames.BuildLoginSuccessServerGroupBootstrap", "TcpNetworkHost.TryHandleOfficialLoginRequestAsync",
        "Builder generated from sanitized verified login request and RFC 5737 TEST-NET endpoint", "Runtime builder", "Verified"));

    var loginFollowUp = OfficialClientLoginProtocolFrames.BuildLoginVersionFollowUp();
    AddSample(packets, Sample("runtime-login-version-followup", "S2C", OfficialLoginWireTransform.Decode(loginFollowUp), "LoginVersionFollowUp", "01_Handshake", "Login version",
        "OfficialLoginWireTransform", "OfficialClientLoginProtocolFrames.BuildLoginVersionFollowUp", "TcpNetworkHost.CompleteOfficialHandshakeAsync",
        "Runtime builder and protocol tests", "Runtime builder", "Verified"));

    var loginFailure = OfficialClientLoginProtocolFrames.BuildLoginFailureResponse(OfficialLoginFailureCode.CredentialsRejected);
    AddSample(packets, Sample("runtime-login-failure", "S2C", OfficialLoginWireTransform.Decode(loginFailure), "LoginFailureCredentialsRejected", "35_Error", "Login failure",
        "OfficialLoginWireTransform", "OfficialClientLoginProtocolFrames.BuildLoginFailureResponse", "TcpNetworkHost.TryHandleOfficialLoginRequestAsync",
        "Static client dispatcher + runtime builder", "Runtime builder", "Verified"));

    AddUnencryptedSample(packets, "runtime-login-server-handshake", "S2C", Convert.FromHexString("1300405FD0401BB55367D34D90DF1D929883DD"),
        "LoginServerHandshake", "01_Handshake", "TcpNetworkHost.CompleteOfficialHandshakeAsync", "Runtime constant");
    AddUnencryptedSample(packets, "runtime-login-client-handshake", "C2S", Convert.FromHexString("1300E10638FA2835845B9FE9528DB9BCDF70BC"),
        "LoginClientHandshake", "01_Handshake", "TcpNetworkHost.CompleteOfficialHandshakeAsync", "Runtime constant");

    var selectionRequestEncoded = OfficialServerSelectionWireCodec.GoldenEncodedRequest.ToArray();
    var selectionRequestDecoded = OfficialLoginWireTransform.Decode(selectionRequestEncoded);
    AddSample(packets, Sample("runtime-server-selection-request", "C2S", selectionRequestDecoded, "SelectServer", "03_Character", "Server selection",
        "OfficialLoginWireTransform", "OfficialServerSelectionWireCodec.SerializeRequest", "TcpNetworkHost.TryHandleOfficialServerSelectionAsync",
        "GoldenEncodedRequest + DecodeRequest", "Runtime/Protocol test", "Verified"));
    var emptySelection = OfficialServerSelectionWireCodec.SerializeResponse(OfficialServerSelectionWireCodec.EmptyCharacterResponseModel());
    if (!emptySelection.Succeeded)
    {
        throw new InvalidOperationException(emptySelection.Error.Message);
    }
    var selectionResponseDecoded = OfficialLoginWireTransform.Decode(emptySelection.Value.Span);
    AddSample(packets, Sample("runtime-character-list-bootstrap", "S2C", selectionResponseDecoded, "CharacterListBootstrapEmpty", "03_Character", "Character list",
        "OfficialLoginWireTransform", "OfficialServerSelectionWireCodec.SerializeResponse", "TcpNetworkHost.TryHandleOfficialServerSelectionAsync",
        "EmptyCharacterResponseModel avoids retaining historical player identifiers", "Runtime/Protocol test", "Verified"));

    AddUnencryptedSample(packets, "runtime-world-server-handshake", "S2C", OfficialClientWorldProtocolFrames.BuildWorldServerHandshake(),
        "WorldServerHandshake", "01_Handshake", "TcpNetworkHost.CompleteOfficialHandshakeAsync", "Runtime builder");
    AddUnencryptedSample(packets, "runtime-world-client-handshake", "C2S", Convert.FromHexString("1300D20A8DD62AEC4D6FF6F3F5D97E5C26B962"),
        "WorldClientHandshake", "01_Handshake", "TcpNetworkHost.CompleteOfficialHandshakeAsync", "Runtime constant");

    var worldStream = OfficialClientWorldProtocolFrames.BuildWorldBootstrapAfterHandshake();
    foreach (var frame in OfficialClientWorldProtocolFrames.GetWorldBootstrapSequence())
    {
        var encoded = worldStream.AsSpan(frame.Offset, frame.Length);
        var decoded = OfficialClientWorldProtocolFrames.DecodeWorldServerPayload(encoded);
        if (frame.Purpose == "PlayerSpawn")
        {
            var sanitizedEncoded = OfficialClientWorldProtocolFrames.BuildCurrentCreateProfilePlayerSpawnFrame128(0x01020304, "TESTCHR");
            decoded = OfficialClientWorldProtocolFrames.DecodeCurrentCreateWorldServerPayload(sanitizedEncoded);
        }
        AddSample(packets, Sample($"runtime-world-bootstrap-{frame.Index:D2}", "S2C", decoded, frame.Purpose, WorldCategory(frame.Purpose), "World bootstrap",
            "Official world S2C transform", "OfficialClientWorldProtocolFrames.BuildWorldBootstrapAfterHandshake", "TcpNetworkHost.CompleteOfficialHandshakeAsync",
            $"GetWorldBootstrapSequence index {frame.Index}", "Runtime builder + headless tests", "Verified"));
    }

    var portalCommand = new OfficialPortalActivateCommand(
        OfficialPortalWireCodec.ClientBuildId,
        OfficialPortalWireCodec.ActivateOpcode,
        Convert.FromHexString("A82734FC9C"),
        "synthetic-export",
        OfficialPortalWireCodec.EvidenceId);
    var portalActivate = OfficialPortalWireCodec.SerializeActivate(portalCommand);
    if (portalActivate.Succeeded && portalActivate.Value is { } portalBytes)
    {
        AddSample(packets, Sample("runtime-portal-activate", "C2S", portalBytes.ToArray(), "PortalActivate", "05_Map", "Portal request",
            "World C2S chained XOR after opcode", "OfficialPortalWireCodec.SerializeActivate", "TcpNetworkHost.TryHandleOfficialPortalAsync",
            OfficialPortalWireCodec.EvidenceId, "Runtime codec + integration tests", "Verified"));
    }
    var portalResult = OfficialPortalWireCodec.SerializeVerifiedClientDestination(19, 4, 28, 34);
    if (portalResult.Succeeded && portalResult.Value is { } portalFrames)
    {
        AddSample(packets, Sample("runtime-portal-prelude", "S2C", portalFrames.PreludeDecodedFrame.ToArray(), "PortalPrelude", "05_Map", "Portal result prelude",
            "World S2C transform", "OfficialPortalWireCodec.SerializeVerifiedClientDestination", "TcpNetworkHost.TryHandleOfficialPortalAsync",
            OfficialPortalWireCodec.EvidenceId, "Runtime codec + integration tests", "Verified"));
        AddSample(packets, Sample("runtime-map-transition", "S2C", portalFrames.MapTransitionDecodedFrame.ToArray(), "MapTransition", "05_Map", "Portal result",
            "World S2C transform", "OfficialPortalWireCodec.SerializeVerifiedClientDestination", "TcpNetworkHost.TryHandleOfficialPortalAsync",
            OfficialPortalWireCodec.EvidenceId, "Runtime codec + integration tests", "Verified"));
    }

    var dialog = OfficialNpcInteractionWireCodec.SerializeDialogOpen(OfficialNpcInteractionWireCodec.ClientBuildId, 1504);
    if (dialog.Succeeded && dialog.Value is { } projection)
    {
        AddSample(packets, Sample("runtime-npc-dialog-result", "S2C", projection.DecodedFrame.ToArray(), "NpcDialogResult", "07_NPC", "Interaction result",
            "World S2C transform", "OfficialNpcInteractionWireCodec.SerializeDialogOpen", "OfficialNpcInteractionClosedLoop.ExecuteFrameAsync",
            OfficialNpcInteractionWireCodec.EvidenceId, "Runtime builder", "Verified"));
    }

    var identity = new OfficialNpcWireIdentity(
        OfficialNpcReplicationWireCodec.ClientBuildId, 1504, 2, 142, 3, 4, 1,
        "83D11BE70AB2E0D6660BC38915E8C3C101FFFC2C67D7A950453E24D79669A84B", "Verified", "exporter",
        OfficialNpcReplicationWireCodec.OpaqueTemplateSha256);
    var npcState = new NpcState(1, 1, "TEST_NPC", 19, new WorldPosition3(17, 8), WorldDirection.South, "Unknown", "Dialog", "Always", null,
        "Idle", 24, true, "{}", "export", identity);
    AddNpcReplicationSample(packets, OfficialNpcReplicationWireCodec.SerializeSpawn(npcState), "runtime-npc-spawn", "NpcSpawn", "Spawn");
    AddNpcReplicationSample(packets, OfficialNpcReplicationWireCodec.SerializePositionUpdate(npcState), "runtime-npc-position", "NpcPositionUpdate", "Position");
    AddNpcReplicationSample(packets, OfficialNpcReplicationWireCodec.SerializeDespawn(npcState), "runtime-npc-despawn", "NpcDespawn", "Despawn");
}

static void AddNpcReplicationSample(List<PacketRecord> packets, RuntimeSerializationResult result, string id, string name, string subcategory)
{
    if (result.Status != OfficialSerializerStatus.Ready)
    {
        return;
    }
    var decoded = OfficialClientWorldProtocolFrames.DecodeWorldServerPayload(result.Frame);
    AddSample(packets, Sample(id, "S2C", decoded, name, "07_NPC", subcategory, "World S2C transform",
        $"OfficialNpcReplicationWireCodec.Serialize{name[3..]}", "RuntimeReplicationEmitter", result.Reason, "Runtime builder", "Verified"));
}

static PacketRecord Sample(string id, string direction, byte[] bytes, string name, string category, string subcategory,
    string encryption, string builder, string handler, string evidence, string evidenceLevel, string confidence)
{
    var opcode = bytes.Length >= 3 ? bytes[2] : (byte?)null;
    var fields = new List<FieldRecord> { F(0, 2, "Length", "UInt16LE", bytes.Length) };
    if (opcode is not null)
    {
        fields.Add(F(2, 1, "Opcode", "UInt8", opcode.Value));
    }
    if (bytes.Length > 3)
    {
        fields.Add(F(3, bytes.Length - 3, "Unknown_0x03", "bytes", "Unknown"));
    }
    return new PacketRecord
    {
        Id = id, Direction = direction, Opcode = opcode, OpcodeHex = opcode is null ? "Unknown" : $"0x{opcode:X2}", Name = name,
        Category = category, Subcategory = subcategory, HeaderSize = opcode is null ? 2 : 3, Length = bytes.Length.ToString(CultureInfo.InvariantCulture),
        VariableLength = false, PayloadSize = Math.Max(0, bytes.Length - (opcode is null ? 2 : 3)).ToString(CultureInfo.InvariantCulture),
        EncryptedOnWire = "true", Encryption = encryption, Compression = "false", Parser = "Canonical decoded frame",
        Builder = builder, Handler = handler, RuntimeUsage = "Production server protocol path", SourceFile = SourceFor(builder), SourceSymbol = builder,
        Evidence = evidence, EvidenceLevel = evidenceLevel, Confidence = confidence, PlaintextAvailable = true, Status = "PlaintextSample",
        Sample = bytes, Fields = fields
    };
}

static void AddUnencryptedSample(List<PacketRecord> packets, string id, string direction, byte[] bytes, string name, string category, string handler, string evidence)
{
    var packet = Sample(id, direction, bytes, name, category, "Handshake", "None (written/read directly)", "Runtime constant", handler, evidence, "Runtime verified", "Verified");
    packet.Opcode = null;
    packet.OpcodeHex = "Unknown";
    packet.HeaderSize = 2;
    packet.PayloadSize = (bytes.Length - 2).ToString(CultureInfo.InvariantCulture);
    packet.EncryptedOnWire = "false";
    packet.Fields = [F(0, 2, "Length", "UInt16LE", bytes.Length), F(2, bytes.Length - 2, "HandshakeMaterial", "bytes", "Build-pinned non-secret protocol material")];
    AddSample(packets, packet);
}

static void AddSample(List<PacketRecord> packets, PacketRecord packet) => packets.Add(packet);

static void ApplyPlaintextGapRecovery(List<PacketRecord> packets, string repositoryRoot)
{
    var gaps = packets.Where(value => value.IsPlaintextGap).ToArray();
    var instrumentedFrames = ReadInstrumentedFrames(repositoryRoot);
    var exactSessionCandidates = ReadLiveValidationPairCandidates(repositoryRoot)
        .Concat(ReadExactSessionTraceCandidates(repositoryRoot))
        .ToArray();
    var referencedCaptureSamples = ReadReferencedCaptureWireSamples(repositoryRoot);

    foreach (var packet in gaps.Where(value => value.PreviousStatus == "StaticOnly" && value.Opcode is not null))
    {
        var trace = exactSessionCandidates.FirstOrDefault(value =>
            value.Direction == packet.Direction &&
            value.Decoded[2] == packet.Opcode &&
            packet.ObservedLengths.Contains(value.Decoded.Length));
        if (trace is not null)
        {
            PromoteExactSessionTrace(packet, trace, repositoryRoot);
            continue;
        }

        var requiredStage = packet.Direction == "C2S" ? "PreEncrypt" : "PostDecrypt";
        var runtime = instrumentedFrames.FirstOrDefault(value =>
            value.CaptureStage == requiredStage &&
            value.Direction == packet.Direction &&
            value.Bytes.Length >= 4 &&
            value.Bytes[2] == packet.Opcode &&
            packet.ObservedLengths.Contains(value.Bytes.Length) &&
            value.FramingValid &&
            value.ChecksumValid);
        if (runtime is not null)
        {
            PromoteInstrumentedFrame(packet, runtime, repositoryRoot);
        }
    }

    foreach (var packet in gaps.Where(value => value.PreviousStatus == "StaticOnlyHandlerPayload" && value.Opcode is not null))
    {
        var handler = instrumentedFrames.FirstOrDefault(value =>
            value.CaptureStage == "HandlerDecoded" &&
            value.Direction == "S2C" &&
            value.Bytes.Length == packet.ObservedLengths.Single() &&
            value.Bytes.Length > 0 &&
            value.Bytes[0] == packet.Opcode);
        if (handler is null)
        {
            continue;
        }

        var parent = instrumentedFrames
            .Where(value =>
                value.CaptureStage == "PostDecrypt" &&
                value.Direction == "S2C" &&
                value.Sequence <= handler.Sequence &&
                handler.Sequence - value.Sequence <= 30 &&
                value.FramingValid &&
                value.ChecksumValid &&
                value.Bytes.AsSpan().IndexOf(handler.Bytes) >= 0)
            .OrderBy(value => handler.Sequence - value.Sequence)
            .FirstOrDefault();
        if (parent is not null)
        {
            PromoteHandlerRecord(packet, handler, parent, repositoryRoot);
        }
    }

    foreach (var packet in gaps.Where(value => value.PreviousStatus == "WireOnlyCiphertextCandidate"))
    {
        var external = referencedCaptureSamples
            .Where(value => ReferencedSampleMatchesPacket(value, packet))
            .ToArray();
        if (external.Length > 0)
        {
            packet.Evidence += $"; completeReferencedWireSamples={external.Length}; " +
                string.Join(", ", external.Select(value => $"{value.SessionId}/{value.CandidateFrameId}").Distinct(StringComparer.Ordinal));
        }
        var samples = packet.WireSamples
            .Concat(external.Select(value => Convert.ToHexString(value.WireBytes)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (TryDecodeKnownWireSample(packet.Direction, samples, out var recovered))
        {
            PromoteKnownWireFixture(packet, recovered);
            var source = external.FirstOrDefault(value => value.WireBytes.AsSpan().SequenceEqual(recovered.Wire));
            if (source is not null)
            {
                packet.Evidence += $"; recoveredSource={source.SourcePath}#{source.CandidateFrameId}; session={source.SessionId}; sourceFrames={source.SourceFrameIds}";
            }
        }
    }

    foreach (var packet in gaps)
    {
        packet.ParserVerified = packet.PreviousStatus != "WireOnlyCiphertextCandidate" &&
            (packet.Parser != "Unknown" || packet.PreviousStatus == "StaticOnlyHandlerPayload" && packet.PlaintextAvailable);
        packet.BuilderVerified = packet.Builder != "Unknown";
        if (packet.PlaintextAvailable)
        {
            packet.Status = "PLAINTEXT_RECOVERED";
            packet.Blocker = "None";
            continue;
        }

        packet.Status = packet.PreviousStatus switch
        {
            "WireOnlyCiphertextCandidate" => "CRYPTO_UNVERIFIED",
            "StaticOnlyHandlerPayload" => "STATIC_STRUCTURE_ONLY",
            "StaticOnly" when packet.Parser != "Unknown" || packet.Builder != "Unknown" => "STATIC_STRUCTURE_ONLY",
            "StaticOnly" => "INCOMPLETE_DEFINITION",
            _ => "UNRELIABLE_OR_INFERRED"
        };
        packet.Blocker = packet.Status switch
        {
            "CRYPTO_UNVERIFIED" when packet.Evidence.Contains("completeReferencedWireSamples=", StringComparison.Ordinal) =>
                "Complete referenced wire frames were retained and tested, but none validate without the exact session key256, initial rolling byte, and marker-correlated protocol phase; the source sessions had packetDecodeProbe=0.",
            "CRYPTO_UNVERIFIED" => packet.WireSamples.Length == 0
                ? "No complete wire frame plus exact session key/initial rolling state is retained; opcode candidate cannot be decrypted."
                : "Available wire sample did not validate under any production login/world transform; exact session key, initial rolling byte, and protocol phase are missing.",
            "STATIC_STRUCTURE_ONLY" => "Parser/builder/handler structure exists, but no recoverable runtime plaintext bytes or exact ciphertext fixture is available.",
            "INCOMPLETE_DEFINITION" => "Only runtime aggregate opcode/length/count metadata remains; raw PreEncrypt/PostDecrypt bytes were not retained in the original evidence package.",
            _ => "Definition remains inferred and lacks a reliable runtime/parser/builder promotion gate.",
        };
    }

    var allowedStatuses = new HashSet<string>(StringComparer.Ordinal)
    {
        "PLAINTEXT_RECOVERED", "RUNTIME_NOT_TRIGGERED", "STATIC_STRUCTURE_ONLY", "CRYPTO_UNVERIFIED",
        "INCOMPLETE_DEFINITION", "UNRELIABLE_OR_INFERRED"
    };
    if (gaps.Length != 83 || gaps.Any(value => !allowedStatuses.Contains(value.Status)))
    {
        throw new InvalidOperationException("The 83-gap validation set was not completely classified.");
    }
}

static IReadOnlyList<ExactSessionTraceCandidate> ReadLiveValidationPairCandidates(string repositoryRoot)
{
    var captureRoot = Path.Combine(repositoryRoot, "Artifacts", "ClientInstrumentation", "ElevatedAutomationHost");
    if (!Directory.Exists(captureRoot))
    {
        return [];
    }

    var candidates = new List<ExactSessionTraceCandidate>();
    foreach (var pairPath in Directory.EnumerateFiles(captureRoot, "live-validation-pairs.jsonl", SearchOption.AllDirectories)
                 .Where(value => string.Equals(Path.GetFileName(Path.GetDirectoryName(value)), "sensitive", StringComparison.OrdinalIgnoreCase))
                 .Order(StringComparer.Ordinal))
    {
        var markers = ReadMarkerTimeline(pairPath);
        ulong sequence = 0;
        foreach (var line in File.ReadLines(pairPath))
        {
            sequence++;
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            if (JsonText(root, "pairStatus") != "Complete" || JsonText(root, "direction") != "ServerToClient")
            {
                continue;
            }

            if (!root.TryGetProperty("timestampUnixMs", out var timestampElement) || !timestampElement.TryGetInt64(out var timestampUnixMs))
            {
                throw new InvalidDataException($"Complete live-validation pair has no timestamp: {pairPath} line {sequence}.");
            }
            var phase = markers.LastOrDefault(value => value.TimestampUnixMs <= timestampUnixMs);
            if (phase is null)
            {
                // A byte pair without a user action/phase boundary is useful diagnostic
                // material, but it cannot satisfy this corpus's phase-evidence gate.
                continue;
            }

            var ciphertext = ParseSpacedHex(JsonText(root, "ciphertext"));
            var plaintext = ParseSpacedHex(JsonText(root, "plaintext"));
            var key = ParseSpacedHex(JsonText(root, "key256"));
            var rollingText = JsonText(root, "rollingBefore");
            if (ciphertext.Length < 4 || ciphertext.Length != plaintext.Length || key.Length != 256 ||
                !rollingText.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ||
                !byte.TryParse(rollingText.AsSpan(2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var rollingBefore))
            {
                throw new InvalidDataException($"Malformed complete live-validation pair: {pairPath} line {sequence}.");
            }
            if (BinaryPrimitives.ReadUInt16LittleEndian(plaintext) != plaintext.Length ||
                plaintext[^1] != OfficialLoginWireTransform.ComputeChecksum(plaintext))
            {
                throw new InvalidDataException($"Invalid plaintext framing/checksum in live-validation pair: {pairPath} line {sequence}.");
            }

            var state = new SessionCipherState(
                rollingBefore,
                key,
                $"live-validation;phase={phase.Marker};initial=0x{rollingBefore:X2};keySha256={Convert.ToHexString(SHA256.HashData(key))}");
            var decoded = DecodeWithSessionCipher(ciphertext, state);
            var reencoded = EncodeWithSessionCipher(plaintext, state);
            if (!decoded.AsSpan().SequenceEqual(plaintext) || !reencoded.AsSpan().SequenceEqual(ciphertext) ||
                !DecodeWithSessionCipher(reencoded, state).AsSpan().SequenceEqual(plaintext))
            {
                throw new InvalidDataException($"Live-validation pair failed exact cipher round-trip: {pairPath} line {sequence}.");
            }

            candidates.Add(new ExactSessionTraceCandidate(
                "S2C",
                sequence,
                plaintext,
                ciphertext,
                pairPath,
                pairPath,
                state.Identity,
                phase.Marker));
        }
    }
    return candidates;
}

static IReadOnlyList<CaptureMarker> ReadMarkerTimeline(string pairPath)
{
    var sensitiveDirectory = Path.GetDirectoryName(pairPath)!;
    var traceRoot = Directory.GetParent(sensitiveDirectory)?.FullName;
    var markerPath = traceRoot is null ? "" : Path.Combine(traceRoot, "markers.jsonl");
    if (!File.Exists(markerPath))
    {
        return [];
    }
    var result = new List<CaptureMarker>();
    foreach (var line in File.ReadLines(markerPath))
    {
        using var document = JsonDocument.Parse(line);
        var root = document.RootElement;
        if (!root.TryGetProperty("markerUnixMs", out var timestampElement) || !timestampElement.TryGetInt64(out var timestampUnixMs))
        {
            continue;
        }
        var marker = JsonText(root, "marker");
        if (marker.StartsWith("MARKER_", StringComparison.Ordinal))
        {
            result.Add(new CaptureMarker(timestampUnixMs, marker));
        }
    }
    return result.OrderBy(value => value.TimestampUnixMs).ToArray();
}

static byte[] ParseSpacedHex(string value)
{
    var compact = value.Replace(" ", string.Empty, StringComparison.Ordinal)
        .Replace("\t", string.Empty, StringComparison.Ordinal);
    if (compact.Length == 0 || compact.Length % 2 != 0 || compact.Any(value => !Uri.IsHexDigit(value)))
    {
        return [];
    }
    return Convert.FromHexString(compact);
}

static IReadOnlyList<InstrumentedFrame> ReadInstrumentedFrames(string repositoryRoot)
{
    var path = Path.Combine(
        repositoryRoot, "Artifacts", "Investigations", "PacketCapture-20260806-145807", "Captures",
        "2026-08-06_14-45-56_CD899288-146F-4508-8696-8E63006C749F", "raw", "frames.jsonl");
    const string expectedSha256 = "705ac6857cc3f15d34e6ff0c5b2b2fa12bab680f17eda97baa6d2c5c020864c0";
    if (!File.Exists(path) || !string.Equals(Sha256File(path), expectedSha256, StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidDataException("Pinned plaintext-stage frame evidence is missing or has changed.");
    }

    var result = new List<InstrumentedFrame>();
    foreach (var line in File.ReadLines(path))
    {
        using var document = JsonDocument.Parse(line);
        var root = document.RootElement;
        var stage = JsonText(root, "CaptureStage");
        if (stage is not ("PreEncrypt" or "PostDecrypt" or "HandlerDecoded"))
        {
            continue;
        }

        var hex = JsonText(root, "PayloadHex");
        if (string.IsNullOrWhiteSpace(hex) || hex.Length % 2 != 0)
        {
            continue;
        }
        var bytes = Convert.FromHexString(hex);
        var direction = JsonText(root, "PacketDirection") switch
        {
            "ClientToServer" => "C2S",
            "ServerToClient" => "S2C",
            _ => "Unknown"
        };
        var framingValid = stage == "HandlerDecoded" ||
            bytes.Length >= 4 && BinaryPrimitives.ReadUInt16LittleEndian(bytes) == bytes.Length;
        var checksumValid = stage == "HandlerDecoded" ||
            framingValid && bytes[^1] == OfficialLoginWireTransform.ComputeChecksum(bytes);
        result.Add(new InstrumentedFrame(
            path,
            ulong.Parse(JsonText(root, "sequence"), CultureInfo.InvariantCulture),
            JsonText(root, "SourceFrameId"),
            direction,
            stage,
            JsonText(root, "EvidenceLevel"),
            bytes,
            framingValid,
            checksumValid && string.Equals(JsonText(root, "ChecksumValid"), "true", StringComparison.OrdinalIgnoreCase)));
    }
    return result.OrderBy(value => value.Sequence).ToArray();
}

static IReadOnlyList<ReferencedCaptureWireSample> ReadReferencedCaptureWireSamples(string repositoryRoot)
{
    var expectedCandidateHashes = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["2026-08-06_08-32-03_86FA350C-1757-4C70-B295-83EEF58EDBE7"] = "2f1e8bc565935755dc26ee364d75a893552200375b3972538bc643bb15ab17a3",
        ["2026-08-06_09-36-00_FD3FE6E1-887D-433A-83A5-FE84EF3962BC"] = "866b2b880002dc0ca819ef30eff1a352a03e2230d739a6653ded2921c0fa0ace",
        ["2026-08-06_10-31-22_36125A66-1515-4051-9B83-3F31B75FF3C9"] = "f902cca11b6338d9646068976800a10b1d66335b4843f1d6818cba296b3d8c99"
    };
    var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
    if (!Directory.Exists(desktop))
    {
        return [];
    }

    var candidatePaths = new Dictionary<string, string>(StringComparer.Ordinal);
    foreach (var (sessionId, expectedHash) in expectedCandidateHashes)
    {
        string? sessionDirectory = null;
        try
        {
            sessionDirectory = Directory.EnumerateDirectories(desktop, sessionId, SearchOption.AllDirectories)
                .FirstOrDefault(value => File.Exists(Path.Combine(value, "raw", "god2-frame-candidates.jsonl")));
        }
        catch (UnauthorizedAccessException)
        {
            // The referenced extracted evidence is optional outside its original workstation.
        }
        if (sessionDirectory is null)
        {
            continue;
        }
        var candidatePath = Path.Combine(sessionDirectory, "raw", "god2-frame-candidates.jsonl");
        if (!string.Equals(Sha256File(candidatePath), expectedHash, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"Referenced capture candidate evidence changed: {sessionId}");
        }
        candidatePaths[sessionId] = candidatePath;
    }

    var requested = new List<(string Semantic, string SessionId, string SourceFrameIds)>();
    foreach (var evidenceName in new[]
             {
                 "CapturedPacketEvidence.20260806.json",
                 "CapturedPacketEvidence.20260806.Batch2.json"
             })
    {
        var evidencePath = Path.Combine(
            repositoryRoot, "src", "God2.ClassicServer.Protocol", "Evidence", "OfficialProtocolCompletion", evidenceName);
        using var document = JsonDocument.Parse(File.ReadAllText(evidencePath));
        foreach (var candidate in document.RootElement.GetProperty("representativeCandidates").EnumerateArray())
        {
            var semantic = JsonText(candidate, "semantic");
            if (semantic is not ("EncryptedLoginOrHandshakeBlobCandidate" or
                "ClientHeartbeatOrKeepAliveCandidate" or
                "ClientMovementOrActionCandidate" or
                "ServerWorldStateDeltaCandidate"))
            {
                continue;
            }
            requested.Add((semantic, JsonText(candidate, "sessionId"), JsonText(candidate, "sampleSourceFrameIds")));
        }
    }

    var result = new List<ReferencedCaptureWireSample>();
    foreach (var session in requested.GroupBy(value => value.SessionId, StringComparer.Ordinal))
    {
        if (!candidatePaths.TryGetValue(session.Key, out var candidatePath))
        {
            continue;
        }
        var requestedBySource = session
            .GroupBy(value => value.SourceFrameIds, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Select(value => value.Semantic).Distinct(StringComparer.Ordinal).ToArray(), StringComparer.Ordinal);
        foreach (var line in File.ReadLines(candidatePath))
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            var sourceFrameIds = JsonText(root, "SourceFrameIds");
            if (!requestedBySource.TryGetValue(sourceFrameIds, out var semantics))
            {
                continue;
            }
            var payloadHex = JsonText(root, "PayloadHex");
            if (string.IsNullOrWhiteSpace(payloadHex) || payloadHex.Length % 2 != 0)
            {
                continue;
            }
            var bytes = Convert.FromHexString(payloadHex);
            if (bytes.Length < 4 || BinaryPrimitives.ReadUInt16LittleEndian(bytes) != bytes.Length)
            {
                continue;
            }
            var direction = JsonText(root, "PacketDirection") switch
            {
                "ClientToServer" => "C2S",
                "ServerToClient" => "S2C",
                _ => "Unknown"
            };
            foreach (var semantic in semantics)
            {
                result.Add(new ReferencedCaptureWireSample(
                    semantic,
                    session.Key,
                    sourceFrameIds,
                    JsonText(root, "CandidateFrameId"),
                    direction,
                    bytes,
                    candidatePath));
            }
        }
    }
    return result;
}

static bool ReferencedSampleMatchesPacket(ReferencedCaptureWireSample sample, PacketRecord packet)
{
    if (sample.Direction != packet.Direction || !packet.ObservedLengths.Contains(sample.WireBytes.Length))
    {
        return false;
    }
    return sample.Semantic switch
    {
        "EncryptedLoginOrHandshakeBlobCandidate" => packet.Id.Contains("login-handshake", StringComparison.Ordinal),
        "ClientHeartbeatOrKeepAliveCandidate" => packet.Id.Contains("client-keepalive", StringComparison.Ordinal),
        "ClientMovementOrActionCandidate" => packet.Id.Contains("client-action", StringComparison.Ordinal),
        "ServerWorldStateDeltaCandidate" => packet.Id.Contains("server-world-delta", StringComparison.Ordinal),
        _ => false
    };
}

static IReadOnlyList<ExactSessionTraceCandidate> ReadExactSessionTraceCandidates(string repositoryRoot)
{
    var captureRoot = Path.Combine(repositoryRoot, "Artifacts", "ClientInstrumentation", "ElevatedAutomationHost");
    if (!Directory.Exists(captureRoot))
    {
        return [];
    }

    var candidates = new List<ExactSessionTraceCandidate>();
    foreach (var tracePath in Directory.EnumerateFiles(captureRoot, "trace.bin", SearchOption.AllDirectories)
                 .Where(value => string.Equals(Path.GetFileName(Path.GetDirectoryName(value)), "sensitive", StringComparison.OrdinalIgnoreCase))
                 .Order(StringComparer.Ordinal))
    {
        if (new FileInfo(tracePath).Length <= 24)
        {
            continue;
        }
        var attemptDirectory = Directory.GetParent(Path.GetDirectoryName(tracePath)!)!.FullName;
        var logPath = Path.Combine(attemptDirectory, "general.log");
        if (!File.Exists(logPath))
        {
            continue;
        }

        try
        {
            var states = ReadCipherStates(logPath);
            if (states.Count == 0)
            {
                continue;
            }
            var records = ReadRestrictedTraceRecords(tracePath);
            var frames = ReassembleRestrictedTrace(records, 1, "C2S")
                .Concat(ReassembleRestrictedTrace(records, 2, "S2C"))
                .ToArray();
            var valid = new List<(ReassembledTraceFrame Frame, SessionCipherState State, byte[] Decoded)>();
            foreach (var frame in frames)
            {
                foreach (var state in states)
                {
                    var decoded = DecodeWithSessionCipher(frame.WireBytes, state);
                    if (decoded[^1] == OfficialLoginWireTransform.ComputeChecksum(decoded))
                    {
                        valid.Add((frame, state, decoded));
                    }
                }
            }
            var stateCounts = valid.GroupBy(value => value.State.Identity).ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
            foreach (var frameGroup in valid.GroupBy(value => (value.Frame.Direction, value.Frame.Sequence, value.Frame.Ordinal)))
            {
                var distinct = frameGroup.GroupBy(value => Convert.ToHexString(value.Decoded), StringComparer.Ordinal).Select(group => group.First()).ToArray();
                if (distinct.Length != 1)
                {
                    continue;
                }
                var selected = distinct[0];
                if (states.Count > 1 && stateCounts[selected.State.Identity] < 2)
                {
                    continue;
                }
                var reencoded = EncodeWithSessionCipher(selected.Decoded, selected.State);
                if (!reencoded.AsSpan().SequenceEqual(selected.Frame.WireBytes) ||
                    !DecodeWithSessionCipher(reencoded, selected.State).AsSpan().SequenceEqual(selected.Decoded))
                {
                    throw new InvalidDataException("Exact-session cipher round-trip failed.");
                }
                candidates.Add(new ExactSessionTraceCandidate(
                    selected.Frame.Direction,
                    selected.Frame.Sequence,
                    selected.Decoded,
                    selected.Frame.WireBytes,
                    tracePath,
                    logPath,
                    selected.State.Identity));
            }
        }
        catch (InvalidDataException)
        {
            // Historical aborted or mixed-stream traces are excluded; only fully reassembled,
            // checksum-valid, uniquely keyed frames enter the corpus.
        }
    }

    return candidates
        .OrderBy(value => value.TracePath, StringComparer.Ordinal)
        .ThenBy(value => value.Sequence)
        .ToArray();
}

static IReadOnlyList<SessionCipherState> ReadCipherStates(string logPath)
{
    var text = File.ReadAllText(logPath);
    var regex = new Regex(
        "packet-decode entry(?: sequence=\\d+)? .*?initialKey=0x(?<initial>[0-9A-Fa-f]{2}).*?key256=\\\"(?<key>(?:[0-9A-Fa-f]{2} ){255}[0-9A-Fa-f]{2})\\\"",
        RegexOptions.CultureInvariant);
    return regex.Matches(text)
        .Select(match =>
        {
            var key = Convert.FromHexString(match.Groups["key"].Value.Replace(" ", string.Empty, StringComparison.Ordinal));
            var initial = byte.Parse(match.Groups["initial"].Value, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);
            return new SessionCipherState(initial, key, $"initial=0x{initial:X2};keySha256={Convert.ToHexString(SHA256.HashData(key))}");
        })
        .DistinctBy(value => value.Identity, StringComparer.Ordinal)
        .ToArray();
}

static IReadOnlyList<RestrictedTraceRecord> ReadRestrictedTraceRecords(string path)
{
    var bytes = File.ReadAllBytes(path);
    if (bytes.Length < 24 || !bytes.AsSpan(0, 7).SequenceEqual("G2TRC01"u8) || BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(8)) != 1)
    {
        throw new InvalidDataException("Invalid restricted trace header.");
    }
    var result = new List<RestrictedTraceRecord>();
    var offset = 24;
    while (offset < bytes.Length)
    {
        const int headerLength = 188;
        if (bytes.Length - offset < headerLength)
        {
            throw new InvalidDataException("Truncated restricted trace record.");
        }
        var header = bytes.AsSpan(offset, headerLength);
        if (BinaryPrimitives.ReadUInt32LittleEndian(header) != 0x31523247 ||
            BinaryPrimitives.ReadUInt16LittleEndian(header[4..]) != 1 ||
            BinaryPrimitives.ReadUInt16LittleEndian(header[6..]) != headerLength)
        {
            throw new InvalidDataException("Invalid restricted trace record identity.");
        }
        var length = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(header[64..]));
        if (length is < 0 or > 4096 || bytes.Length - offset - headerLength < length)
        {
            throw new InvalidDataException("Invalid restricted trace payload length.");
        }
        result.Add(new RestrictedTraceRecord(
            BinaryPrimitives.ReadUInt64LittleEndian(header[8..]),
            BinaryPrimitives.ReadUInt16LittleEndian(header[42..]),
            bytes.AsSpan(offset + headerLength, length).ToArray()));
        offset += headerLength + length;
    }
    return result;
}

static IReadOnlyList<ReassembledTraceFrame> ReassembleRestrictedTrace(
    IReadOnlyList<RestrictedTraceRecord> records,
    ushort direction,
    string directionName)
{
    var pending = new List<byte>();
    var result = new List<ReassembledTraceFrame>();
    ulong sequence = 0;
    var ordinal = 0;
    foreach (var record in records.Where(value => value.Direction == direction).OrderBy(value => value.Sequence))
    {
        if (pending.Count == 0)
        {
            sequence = record.Sequence;
        }
        pending.AddRange(record.Payload);
        while (pending.Count >= 2)
        {
            var declared = pending[0] | pending[1] << 8;
            if (declared is < 4 or > 4096)
            {
                throw new InvalidDataException("Restricted trace contains an invalid frame length.");
            }
            if (pending.Count < declared)
            {
                break;
            }
            result.Add(new ReassembledTraceFrame(directionName, sequence, ordinal++, pending.Take(declared).ToArray()));
            pending.RemoveRange(0, declared);
            if (pending.Count > 0)
            {
                sequence = record.Sequence;
            }
        }
    }
    if (pending.Count != 0)
    {
        throw new InvalidDataException("Restricted trace ends with an incomplete frame.");
    }
    return result;
}

static byte[] DecodeWithSessionCipher(ReadOnlySpan<byte> wire, SessionCipherState state)
{
    var decoded = wire.ToArray();
    var previous = state.InitialPreviousByte;
    for (var index = 2; index < decoded.Length; index++)
    {
        var feedback = unchecked((byte)(previous - 3));
        var adjusted = unchecked((byte)(wire[index] - feedback));
        decoded[index] = (byte)(state.Key[(index - 2) & 0xFF] ^ adjusted);
        previous = decoded[index];
    }
    return decoded;
}

static byte[] EncodeWithSessionCipher(ReadOnlySpan<byte> decoded, SessionCipherState state)
{
    var wire = decoded.ToArray();
    var previous = state.InitialPreviousByte;
    for (var index = 2; index < wire.Length; index++)
    {
        wire[index] = unchecked((byte)((state.Key[(index - 2) & 0xFF] ^ decoded[index]) + previous - 3));
        previous = decoded[index];
    }
    return wire;
}

static void PromoteExactSessionTrace(PacketRecord packet, ExactSessionTraceCandidate candidate, string repositoryRoot)
{
    packet.Sample = candidate.Decoded.ToArray();
    packet.PlaintextAvailable = true;
    packet.CryptoVerified = true;
    packet.RuntimeTriggered = true;
    packet.RoundTripVerified = true;
    packet.KnownCiphertextVerified = true;
    packet.RecoveryOrigin = candidate.TracePath.EndsWith("live-validation-pairs.jsonl", StringComparison.OrdinalIgnoreCase)
        ? "LiveValidation"
        : "OfflineEvidence";
    packet.PlaintextBoundary = packet.Direction == "C2S" ? "Client encode input recovered by exact-session decrypt" : "Client PostDecrypt / server encode input";
    packet.Encryption = $"Exact-session chained transform ({candidate.CipherIdentity}); ciphertext -> decrypt -> plaintext and plaintext -> encrypt -> identical ciphertext verified";
    packet.Compression = "false";
    packet.SourceFile = Path.GetRelativePath(repositoryRoot, candidate.TracePath).Replace('\\', '/');
    packet.SourceSymbol = $"trace sequence {candidate.Sequence}";
    packet.Evidence = $"{packet.Evidence}; exact-session trace={packet.SourceFile}#seq-{candidate.Sequence}; log={Path.GetRelativePath(repositoryRoot, candidate.LogPath).Replace('\\', '/')}; ciphertextSha256={Convert.ToHexString(SHA256.HashData(candidate.WireBytes))}";
    if (candidate.ProtocolPhase != "Unknown")
    {
        packet.Evidence += $"; protocolPhase={candidate.ProtocolPhase}";
    }
    packet.EvidenceLevel = "Known ciphertext + exact-session key-state decode + checksum + exact round-trip";
    packet.Confidence = "Verified";
    packet.Notes = $"Representative plaintext length {candidate.Decoded.Length} recovered from a uniquely matching exact-session cipher state. Other observed family lengths remain indexed by the catalog row.";
    SetFramedFields(packet, candidate.Decoded);
}

static void PromoteInstrumentedFrame(PacketRecord packet, InstrumentedFrame frame, string repositoryRoot)
{
    var roundTripMethod = VerifyProductionRoundTrip(frame.Bytes, frame.Sequence);
    packet.Sample = frame.Bytes.ToArray();
    packet.PlaintextAvailable = true;
    packet.CryptoVerified = true;
    packet.RuntimeTriggered = true;
    packet.RoundTripVerified = true;
    packet.KnownCiphertextVerified = false;
    packet.PlaintextBoundary = frame.CaptureStage;
    packet.Encryption = $"Runtime {frame.CaptureStage} boundary; checksum verified; {roundTripMethod}";
    packet.Compression = "false";
    packet.SourceFile = Path.GetRelativePath(repositoryRoot, frame.SourcePath).Replace('\\', '/');
    packet.SourceSymbol = frame.SourceFrameId;
    packet.Evidence = $"{packet.Evidence}; runtime plaintext={packet.SourceFile}#{frame.SourceFrameId}; sequence={frame.Sequence}; stage={frame.CaptureStage}; evidenceLevel={frame.EvidenceLevel}";
    packet.EvidenceLevel = $"Runtime {frame.CaptureStage} plaintext + checksum + production-transform round-trip";
    packet.Confidence = "Verified";
    packet.Notes = $"Representative runtime plaintext length {frame.Bytes.Length}; instrumentation metadata and independent checksum validation agree. No ciphertext bytes were substituted.";
    SetFramedFields(packet, frame.Bytes);
}

static void PromoteHandlerRecord(PacketRecord packet, InstrumentedFrame handler, InstrumentedFrame parent, string repositoryRoot)
{
    _ = VerifyProductionRoundTrip(parent.Bytes, parent.Sequence);
    packet.Sample = handler.Bytes.ToArray();
    packet.Direction = "S2C";
    packet.Opcode = handler.Bytes[0];
    packet.OpcodeHex = $"0x{handler.Bytes[0]:X2}";
    packet.HeaderSize = 1;
    packet.Length = handler.Bytes.Length.ToString(CultureInfo.InvariantCulture);
    packet.PayloadSize = Math.Max(0, handler.Bytes.Length - 1).ToString(CultureInfo.InvariantCulture);
    packet.VariableLength = false;
    packet.PlaintextAvailable = true;
    packet.CryptoVerified = true;
    packet.RuntimeTriggered = true;
    packet.ParserVerified = true;
    packet.RoundTripVerified = true;
    packet.KnownCiphertextVerified = false;
    packet.PlaintextBoundary = "HandlerDecoded nested application record";
    packet.EncryptedOnWire = "true";
    packet.Encryption = $"Nested record recovered at HandlerDecoded; byte-identical subspan of checksum-valid PostDecrypt parent opcode 0x{parent.Bytes[2]:X2}, length {parent.Bytes.Length}";
    packet.Compression = "false";
    packet.Parser = "Instrumented client handler decoded-record boundary";
    packet.SourceFile = Path.GetRelativePath(repositoryRoot, handler.SourcePath).Replace('\\', '/');
    packet.SourceSymbol = handler.SourceFrameId;
    packet.Evidence = $"{packet.Evidence}; handler={packet.SourceFile}#{handler.SourceFrameId}; handlerSeq={handler.Sequence}; parent={parent.SourceFrameId}; parentSeq={parent.Sequence}; parentOpcode=0x{parent.Bytes[2]:X2}; exactSubspan=true";
    packet.EvidenceLevel = "Runtime HandlerDecoded bytes + PostDecrypt parent containment + parent checksum/round-trip";
    packet.Confidence = "Verified";
    packet.Notes = "packet.bin is a nested handler application record, not a transport frame: opcode is at offset 0 and there is no UInt16 length prefix. Direction and parent-frame relationship are runtime proven.";
    packet.Fields =
    [
        F(0, 1, "Opcode", "UInt8", handler.Bytes[0]),
        F(1, Math.Max(0, handler.Bytes.Length - 1), "HandlerRecordBody", "bytes", "Runtime captured")
    ];
}

static void PromoteKnownWireFixture(PacketRecord packet, KnownWireRecovery recovered)
{
    packet.Sample = recovered.Decoded.ToArray();
    packet.Opcode = recovered.Decoded[2];
    packet.OpcodeHex = $"0x{recovered.Decoded[2]:X2}";
    packet.HeaderSize = 3;
    packet.Length = recovered.Decoded.Length.ToString(CultureInfo.InvariantCulture);
    packet.PayloadSize = Math.Max(0, recovered.Decoded.Length - 3).ToString(CultureInfo.InvariantCulture);
    packet.VariableLength = false;
    packet.PlaintextAvailable = true;
    packet.CryptoVerified = true;
    packet.RuntimeTriggered = true;
    packet.RoundTripVerified = true;
    packet.KnownCiphertextVerified = true;
    packet.PlaintextBoundary = "Decode output / Encode input";
    packet.Encryption = recovered.Transform;
    packet.Compression = "false";
    packet.Evidence = $"{packet.Evidence}; knownCiphertextSha256={Convert.ToHexString(SHA256.HashData(recovered.Wire))}; decodedOpcode=0x{recovered.Decoded[2]:X2}; checksum=true; exactRoundTrip=true";
    packet.EvidenceLevel = "Known ciphertext -> production decode -> checksum-valid plaintext -> exact re-encode";
    packet.Confidence = "Verified";
    packet.Notes = "The historical two-byte wire opcode candidate was ciphertext. packet.bin contains only the decoded UInt8-opcode plaintext frame.";
    SetFramedFields(packet, recovered.Decoded);
}

static string VerifyProductionRoundTrip(ReadOnlySpan<byte> decoded, ulong sequence)
{
    var loginLike = decoded.Length == 208 ||
        decoded[2] == 0xA6 || decoded[2] == 0x07 ||
        sequence < 100 && decoded[2] is 0x01 or 0x02;
    var encoded = loginLike
        ? OfficialLoginWireTransform.Encode(decoded)
        : OfficialClientWorldProtocolFrames.EncodeWorldServerPayload(decoded);
    var roundTrip = loginLike
        ? OfficialLoginWireTransform.Decode(encoded)
        : OfficialClientWorldProtocolFrames.DecodeWorldServerPayload(encoded);
    if (!roundTrip.AsSpan().SequenceEqual(decoded))
    {
        throw new InvalidDataException("Production transform plaintext round-trip failed.");
    }
    return loginLike ? "OfficialLoginWireTransform round-trip" : "official world chained-transform round-trip";
}

static bool TryDecodeKnownWireSample(string direction, IReadOnlyList<string> samples, out KnownWireRecovery recovered)
{
    foreach (var hex in samples)
    {
        if (string.IsNullOrWhiteSpace(hex) || hex.Length % 2 != 0)
        {
            continue;
        }
        var wire = Convert.FromHexString(hex);
        if (wire.Length < 4 || BinaryPrimitives.ReadUInt16LittleEndian(wire) != wire.Length)
        {
            continue;
        }
        var valid = new List<KnownWireRecovery>();
        if (direction == "C2S")
        {
            var c2sWorld = OfficialClientWorldProtocolFrames.DecodeWorldClientFrame(wire);
            if (c2sWorld[^1] == OfficialLoginWireTransform.ComputeChecksum(c2sWorld) &&
                OfficialClientWorldProtocolFrames.EncodeWorldClientFrame(c2sWorld).AsSpan().SequenceEqual(wire))
            {
                valid.Add(new KnownWireRecovery(
                    wire,
                    c2sWorld,
                    "Official C2S world chained-XOR transform; known ciphertext exact round-trip"));
            }
        }
        var exactBuildWorld = OfficialClientWorldProtocolFrames.DecodeWorldServerPayload(wire);
        if (exactBuildWorld[^1] == OfficialLoginWireTransform.ComputeChecksum(exactBuildWorld) &&
            OfficialClientWorldProtocolFrames.EncodeWorldServerPayload(exactBuildWorld).AsSpan().SequenceEqual(wire))
        {
            valid.Add(new KnownWireRecovery(
                wire,
                exactBuildWorld,
                direction == "C2S"
                    ? "Official exact-build world transform used by C2S runtime ingress; known ciphertext exact round-trip"
                    : "Official S2C world chained transform; known ciphertext exact round-trip"));
        }
        var login = OfficialLoginWireTransform.Decode(wire);
        if (login[^1] == OfficialLoginWireTransform.ComputeChecksum(login) &&
            OfficialLoginWireTransform.Encode(login).AsSpan().SequenceEqual(wire))
        {
            valid.Add(new KnownWireRecovery(wire, login, "OfficialLoginWireTransform; known ciphertext exact round-trip"));
        }
        var distinct = valid.GroupBy(value => Convert.ToHexString(value.Decoded), StringComparer.Ordinal).Select(group => group.First()).ToArray();
        if (distinct.Length == 1)
        {
            recovered = distinct[0];
            return true;
        }
    }
    recovered = null!;
    return false;
}

static void SetFramedFields(PacketRecord packet, ReadOnlySpan<byte> bytes)
{
    if (bytes.Length < 4 || BinaryPrimitives.ReadUInt16LittleEndian(bytes) != bytes.Length ||
        bytes[^1] != OfficialLoginWireTransform.ComputeChecksum(bytes))
    {
        throw new InvalidDataException($"Recovered plaintext frame validation failed: {packet.Id}");
    }
    packet.HeaderSize = 3;
    packet.Fields =
    [
        F(0, 2, "Length", "UInt16LE", bytes.Length),
        F(2, 1, "Opcode", "UInt8", bytes[2]),
        F(3, Math.Max(0, bytes.Length - 4), "Payload", "bytes", "Runtime captured / decoded"),
        F(bytes.Length - 1, 1, "Checksum", "UInt8", bytes[^1])
    ];
}

static void WritePacketDirectory(string corpusRoot, PacketRecord packet)
{
    var opcodeName = packet.Opcode is null ? "NA" : packet.Opcode.Value.ToString(packet.Opcode.Value > 0xFF ? "X4" : "X2", CultureInfo.InvariantCulture);
    var directory = Path.Combine(corpusRoot, "packets", packet.Category, packet.Direction, $"{opcodeName}_{SafeName(packet.Name)}");
    if (Directory.Exists(directory))
    {
        directory += $"_{ShortHash(packet.Id)}";
    }
    Directory.CreateDirectory(directory);
    packet.ArtifactPath = Path.GetRelativePath(corpusRoot, directory).Replace('\\', '/');

    var metadata = new
    {
        id = packet.Id,
        direction = packet.Direction,
        opcode = packet.Opcode,
        opcode_hex = packet.OpcodeHex,
        name = packet.Name,
        category = packet.Category,
        subcategory = packet.Subcategory,
        plaintext = packet.PlaintextAvailable,
        encrypted_on_wire = ValueOrString(packet.EncryptedOnWire),
        encryption = packet.Encryption,
        compression = ValueOrString(packet.Compression),
        length = packet.Length,
        header_length = packet.HeaderSize,
        payload_length = packet.PayloadSize,
        variable_length = packet.VariableLength,
        source = new { parser = packet.Parser, builder = packet.Builder, handler = packet.Handler, file = packet.SourceFile, symbol = packet.SourceSymbol },
        evidence = packet.Evidence,
        evidence_level = packet.EvidenceLevel,
        status = packet.Status,
        previous_status = packet.PreviousStatus,
        plaintext_boundary = packet.PlaintextBoundary,
        runtime_triggered = packet.RuntimeTriggered,
        crypto_verified = packet.CryptoVerified,
        parser_verified = packet.ParserVerified,
        builder_verified = packet.BuilderVerified,
        round_trip_verified = packet.RoundTripVerified,
        known_ciphertext_verified = packet.KnownCiphertextVerified,
        blocker = packet.Blocker,
        fields = packet.Fields,
        confidence = packet.Confidence,
        notes = packet.Notes
    };

    if (packet.PlaintextAvailable && packet.Sample is not null)
    {
        File.WriteAllBytes(Path.Combine(directory, "packet.bin"), packet.Sample);
        File.WriteAllText(Path.Combine(directory, "packet.hex"), HexDump(packet.Sample), new UTF8Encoding(false));
        WriteJson(Path.Combine(directory, "packet.json"), metadata);
    }
    else
    {
        WriteJson(Path.Combine(directory, "structure.json"), metadata);
    }
    File.WriteAllText(Path.Combine(directory, "README.md"), PacketReadme(packet), new UTF8Encoding(false));
}

static void WriteCatalogs(string corpusRoot, List<PacketRecord> packets)
{
    var catalog = Path.Combine(corpusRoot, "catalog");
    var ordered = packets.OrderBy(value => value.Direction).ThenBy(value => value.Opcode ?? int.MaxValue).ThenBy(value => value.Name).ToArray();
    var header = "Direction,Opcode,OpcodeHex,Name,Category,Subcategory,Length,VariableLength,EncryptedOnWire,PlaintextAvailable,Parser,Builder,Handler,SourceFile,Evidence,EvidenceLevel,Confidence,PreviousStatus,Status,RuntimeTriggered,CryptoVerified,ParserVerified,BuilderVerified,RoundTripVerified,KnownCiphertextVerified,PlaintextBoundary,Blocker,ArtifactPath";
    var csv = new StringBuilder(header).AppendLine();
    foreach (var packet in ordered)
    {
        csv.AppendLine(Csv(packet.Direction, packet.Opcode?.ToString(CultureInfo.InvariantCulture) ?? "Unknown", packet.OpcodeHex, packet.Name,
            packet.Category, packet.Subcategory, packet.Length, packet.VariableLength.ToString(), packet.EncryptedOnWire, packet.PlaintextAvailable.ToString(),
            packet.Parser, packet.Builder, packet.Handler, packet.SourceFile, packet.Evidence, packet.EvidenceLevel, packet.Confidence,
            packet.PreviousStatus, packet.Status, packet.RuntimeTriggered.ToString(), packet.CryptoVerified.ToString(), packet.ParserVerified.ToString(),
            packet.BuilderVerified.ToString(), packet.RoundTripVerified.ToString(), packet.KnownCiphertextVerified.ToString(), packet.PlaintextBoundary,
            packet.Blocker, packet.ArtifactPath));
    }
    File.WriteAllText(Path.Combine(catalog, "packet_catalog.csv"), csv.ToString(), new UTF8Encoding(false));
    WriteJson(Path.Combine(catalog, "packet_catalog.json"), ordered);

    var md = new StringBuilder("# Packet Catalog\n\n");
    md.AppendLine("| Direction | Opcode | Name | Category | Length | Plaintext | Evidence | Confidence |");
    md.AppendLine("|---|---:|---|---|---:|---|---|---|");
    foreach (var packet in ordered)
    {
        md.AppendLine($"| {packet.Direction} | {packet.OpcodeHex} | {Md(packet.Name)} | {packet.Category} | {packet.Length} | {(packet.PlaintextAvailable ? "Yes" : "No")} | {Md(packet.EvidenceLevel)} | {packet.Confidence} |");
    }
    File.WriteAllText(Path.Combine(catalog, "packet_catalog.md"), md.ToString(), new UTF8Encoding(false));

    var groups = ordered.Where(value => value.Opcode is not null).GroupBy(value => value.Opcode!.Value).OrderBy(value => value.Key).ToArray();
    var matrixCsv = new StringBuilder("Opcode,C2S,S2C,Name,Status").AppendLine();
    var matrixMd = new StringBuilder("# Opcode Matrix\n\n| Opcode | C2S | S2C | Name | Status |\n|---:|---|---|---|---|\n");
    foreach (var group in groups)
    {
        var c2s = group.Where(value => value.Direction == "C2S").Select(value => value.Name).Distinct().ToArray();
        var s2c = group.Where(value => value.Direction == "S2C").Select(value => value.Name).Distinct().ToArray();
        var sameDirectionDuplicate = c2s.Length > 1 || s2c.Length > 1;
        var status = group.Any(value => value.Direction == "Unknown")
            ? "Includes handler-only Unknown direction"
            : sameDirectionDuplicate ? "Multiple definitions/profile specialization" : "OK";
        var names = string.Join(" / ", group.Select(value => value.Name).Distinct());
        matrixCsv.AppendLine(Csv($"0x{group.Key:X2}", string.Join("; ", c2s), string.Join("; ", s2c), names, status));
        matrixMd.AppendLine($"| 0x{group.Key:X2} | {Md(string.Join("; ", c2s))} | {Md(string.Join("; ", s2c))} | {Md(names)} | {status} |");
    }
    File.WriteAllText(Path.Combine(catalog, "opcode_matrix.csv"), matrixCsv.ToString(), new UTF8Encoding(false));
    File.WriteAllText(Path.Combine(catalog, "opcode_matrix.md"), matrixMd.ToString(), new UTF8Encoding(false));
}

static void WriteReports(string corpusRoot, List<PacketRecord> packets)
{
    var reports = Path.Combine(corpusRoot, "reports");
    var unknown = packets.Where(value => value.Category == "90_Unknown").OrderBy(value => value.Direction).ThenBy(value => value.Opcode).ToArray();
    var unknownCsv = new StringBuilder("Direction,OpcodeHex,Name,Length,Evidence,Confidence,Status").AppendLine();
    foreach (var packet in unknown)
    {
        unknownCsv.AppendLine(Csv(packet.Direction, packet.OpcodeHex, packet.Name, packet.Length, packet.Evidence, packet.Confidence, packet.Status));
    }
    File.WriteAllText(Path.Combine(corpusRoot, "unknown", "unknown_packets.csv"), unknownCsv.ToString(), new UTF8Encoding(false));
    var unknownMd = new StringBuilder("# Unknown Packets\n\n未分類項目不會被丟棄，也不會因 opcode candidate 而獲得未證實語意。\n\n");
    unknownMd.AppendLine("| Direction | Opcode | Name | Length | Status | Evidence |");
    unknownMd.AppendLine("|---|---:|---|---:|---|---|");
    foreach (var packet in unknown)
    {
        unknownMd.AppendLine($"| {packet.Direction} | {packet.OpcodeHex} | {Md(packet.Name)} | {packet.Length} | {packet.Status} | {Md(packet.EvidenceLevel)} |");
    }
    File.WriteAllText(Path.Combine(corpusRoot, "unknown", "unknown_packets.md"), unknownMd.ToString(), new UTF8Encoding(false));

    var coverage = $"""
# Protocol Coverage

| Metric | Count |
|---|---:|
| Total packet definitions | {packets.Count} |
| C2S | {packets.Count(value => value.Direction == "C2S")} |
| S2C | {packets.Count(value => value.Direction == "S2C")} |
| Bidirectional | {packets.Count(value => value.Direction == "Bidirectional")} |
| Unknown direction | {packets.Count(value => value.Direction == "Unknown")} |
| Runtime verified / builder-backed | {packets.Count(value => value.PlaintextAvailable && value.EvidenceLevel.Contains("Runtime", StringComparison.OrdinalIgnoreCase))} |
| Headless verified | {packets.Count(value => value.EvidenceLevel.Contains("headless", StringComparison.OrdinalIgnoreCase))} |
| Test verified | {packets.Count(value => value.EvidenceLevel.Contains("test", StringComparison.OrdinalIgnoreCase))} |
| Static only | {packets.Count(value => !value.PlaintextAvailable)} |
| Plaintext samples | {packets.Count(value => value.PlaintextAvailable)} |
| No plaintext sample | {packets.Count(value => !value.PlaintextAvailable)} |
| Known opcode | {packets.Count(value => value.Opcode is not null)} |
| Unknown opcode | {packets.Count(value => value.Opcode is null)} |
| Handled/builder-backed | {packets.Count(value => value.Handler != "Unknown" && value.PlaintextAvailable)} |
| Unhandled/static | {packets.Count(value => value.Handler == "Unknown" || !value.PlaintextAvailable)} |
| Encrypted on wire | {packets.Count(value => value.EncryptedOnWire == "true")} |
| Unencrypted on wire | {packets.Count(value => value.EncryptedOnWire == "false")} |
| Unknown crypto | {packets.Count(value => value.EncryptedOnWire == "Unknown")} |

## Evidence gap

The original evidence package retained only aggregate lengths/opcodes/counts for 64 directional families. Gap recovery now joins those rows against pinned PreEncrypt/PostDecrypt instrumentation and exact-session restricted traces. A row is promoted only after framing, checksum, crypto state, and round-trip gates pass. Historical wire candidates remain structure-only unless production decode yields checksum-valid plaintext and exact ciphertext re-encryption.
""";
    File.WriteAllText(Path.Combine(reports, "coverage.md"), coverage, new UTF8Encoding(false));

    var crypto = """
# Crypto Pipeline / Plaintext Boundary

## Frame envelope

- File: `src/God2.ClassicServer.Protocol/ProtocolRuntime.cs`
- Class: `FrameAccumulator`
- Method: `Append`
- Header: offset `0x00`, `UInt16LE`, includes the whole frame. The two-byte length remains readable on wire.

## C2S receive pipeline

`NetworkStream.ReadAsync` → `FrameAccumulator.Append` → stage-specific transform → decoded codec → handler.

| Stage | File | Class | Method |
|---|---|---|---|
| Socket receive | `src/God2.ClassicServer.Runtime/RuntimeFoundation.cs` | `TcpNetworkHost` | `ReceiveLoopAsync`, `ReadSingleFramedPacketAsync` |
| Login decrypt | `src/God2.ClassicServer.Protocol/OfficialServerSelectionWire.cs` | `OfficialLoginWireTransform` | `Decode` |
| Login decode/handler | `src/God2.ClassicServer.Runtime/OfficialClientLoginProtocolFrames.cs` / `RuntimeFoundation.cs` | `OfficialClientLoginProtocolFrames` / `TcpNetworkHost` | `TryDecodeSensitiveLoginRequest` / `TryHandleOfficialLoginRequestAsync` |
| World C2S partial chained XOR | `src/God2.ClassicServer.Runtime/OfficialClientWorldProtocolFrames.cs` | `OfficialClientWorldProtocolFrames` | `DecodeWorldClientFrame` |
| World exact-build transform used by NPC ingress | same | same | `DecodeWorldServerPayload` (called by `OfficialNpcInteractionClosedLoop.ExecuteFrameAsync`) |
| Portal dispatch | `src/God2.ClassicServer.Runtime/OfficialPortalClosedLoop.cs` | `OfficialPortalClosedLoop` | `ExecuteFrameAsync` |
| Generic fallback | `src/God2.ClassicServer.Protocol/ProtocolRuntime.cs` | `ProtocolRuntime` | `DecodeAndRouteAsync` |

The generic fallback receives wire frames. Therefore its raw two-byte `OpcodeCandidate` is never treated as a plaintext opcode. Six historical fixtures now have `packet.bin` only because production decode produced a checksum-valid UInt8-opcode frame and re-encryption reproduced the exact ciphertext; the remaining wire candidates are `CRYPTO_UNVERIFIED` and have no `packet.bin`.

## S2C send pipeline

`Handler/runtime model` → decoded builder/serializer → stage-specific encode/encrypt → `NetworkStream.WriteAsync`.

| Stage | File | Class | Method |
|---|---|---|---|
| Login plaintext builder | `src/God2.ClassicServer.Runtime/OfficialClientLoginProtocolFrames.cs` | `OfficialClientLoginProtocolFrames` | `BuildLoginVersionFollowUp`, `BuildLoginFailureResponse`, `BuildLoginSuccessServerGroupBootstrap` |
| Login encryption | `src/God2.ClassicServer.Protocol/OfficialServerSelectionWire.cs` | `OfficialLoginWireTransform` | `Encode` |
| World plaintext builders | `src/God2.ClassicServer.Runtime/OfficialClientWorldProtocolFrames.cs` | `OfficialClientWorldProtocolFrames` | `BuildWorldBootstrapAfterHandshake`, `BuildPlayerSpawnFrame128` |
| World encryption | same | same | `EncodeWorldServerPayload` |
| NPC plaintext serializer | `src/God2.ClassicServer.Runtime/OfficialNpcReplicationWireCodec.cs` | `OfficialNpcReplicationWireCodec` | `SerializeSpawn`, `SerializePositionUpdate`, `SerializeDespawn` (decoded local buffer exists before encoding) |
| Runtime send | `src/God2.ClassicServer.Runtime/RuntimeFoundation.cs` | `NetworkStreamRuntimeFrameSender` | `SendAsync` |

## Compression

No production application-frame compression function is present in the traced server pipeline. Payload-free captured families remain `Compression=Unknown`; builder-backed samples are `false`.

## Diagnostic live validation

- `tools/God2.ClientInstrumentation/src/God2ClientTraceProbe.cpp` writes byte-exact S2C decode pairs to the restricted `sensitive/live-validation-pairs.jsonl` file. Each complete row includes ciphertext, plaintext, key256, rolling state before/after, decoder state, length, opcode, timestamp, process identity, and phase marker context.
- C2S is captured at the exact outbound builder/enqueue `PreEncrypt` boundary and correlated with the restricted socket trace from the same session. Authentication opcode `0x19` and the 417-byte login echo remain excluded from plaintext persistence.
- `Automation/Add-PacketCaptureMarker.ps1` appends explicit English phase/action markers without changing production protocol behavior.
- The exporter consumes a complete S2C pair only after UInt16LE framing, UInt8 opcode, checksum, exact decrypt equality, exact ciphertext re-encryption, and decrypt-after-encrypt identity all pass.

## Corpus invariant

Every `packet.bin` is either (1) the result of the server's `Decode` function, (2) a decoded buffer produced immediately before the server's `Encode` function, or (3) a direct unencrypted handshake frame. Ciphertext captures never become `packet.bin`.
""";
    File.WriteAllText(Path.Combine(reports, "crypto_pipeline.md"), crypto, new UTF8Encoding(false));

    var duplicates = packets.Where(value => value.Opcode is not null)
        .GroupBy(value => (value.Direction, value.Opcode))
        .Where(group => group.Count() > 1)
        .OrderBy(group => group.Key.Direction).ThenBy(group => group.Key.Opcode)
        .ToArray();
    var duplicateMd = new StringBuilder("# Duplicate Opcodes\n\nSame opcode across opposite directions is not a collision. Rows below are same-direction multiple definitions, usually an aggregate family plus a source-backed semantic/version specialization.\n\n");
    duplicateMd.AppendLine("| Direction | Opcode | Definitions | Assessment |");
    duplicateMd.AppendLine("|---|---:|---|---|");
    foreach (var group in duplicates)
    {
        duplicateMd.AppendLine($"| {group.Key.Direction} | 0x{group.Key.Opcode:X2} | {Md(string.Join("; ", group.Select(value => $"{value.Name}({value.Length})")))} | Reviewed; distinct length/profile/evidence rows retained |");
    }
    File.WriteAllText(Path.Combine(reports, "duplicate_opcodes.md"), duplicateMd.ToString(), new UTF8Encoding(false));

    var missing = packets.Where(value => !value.PlaintextAvailable || value.Handler == "Unknown").ToArray();
    var missingMd = new StringBuilder("# Missing Handlers / Builders\n\n");
    missingMd.AppendLine("| Direction | Opcode | Name | Handler | Builder | Reason |");
    missingMd.AppendLine("|---|---:|---|---|---|---|");
    foreach (var packet in missing)
    {
        missingMd.AppendLine($"| {packet.Direction} | {packet.OpcodeHex} | {Md(packet.Name)} | {Md(packet.Handler)} | {Md(packet.Builder)} | {Md(packet.Status)} |");
    }
    File.WriteAllText(Path.Combine(reports, "missing_handlers.md"), missingMd.ToString(), new UTF8Encoding(false));

    var evidence = """
# Evidence Levels

優先序與本 corpus 的使用方式：

1. Runtime verified / production builder or parser
2. Integration test
3. Headless test
4. Unit/protocol test
5. Packet fixture (`VERIFIED_RAW`)
6. Builder generated
7. Parser reconstruction
8. Static structure only

`packet.bin` 只允許 1–7 且必須能證明 plaintext boundary。第 8 級以及 wire-only evidence 僅輸出 `structure.json`。

`Recovered` 表示 layout/transform 有證據，但完整語意未通過 promotion gate；`Verified` 不代表所有 unknown fields 已知。
""";
    File.WriteAllText(Path.Combine(reports, "evidence.md"), evidence, new UTF8Encoding(false));
    WritePlaintextGapValidationReport(reports, packets);
    WriteLiveValidationChecklist(reports, packets);
}

static void WriteLiveValidationChecklist(string reportsDirectory, List<PacketRecord> packets)
{
    var targets = packets.Where(IsFinalPlaintextTarget)
        .OrderBy(value => value.Direction)
        .ThenBy(value => value.Opcode ?? int.MaxValue)
        .ThenBy(value => value.Id, StringComparer.Ordinal)
        .ToArray();
    if (targets.Length != 16)
    {
        throw new InvalidOperationException($"Final live-validation target set changed: {targets.Length}; expected 16.");
    }
    var remaining = targets.Where(value => !value.PlaintextAvailable && value.Status != "REJECTED_FALSE_CANDIDATE").ToArray();
    var output = new StringBuilder("# Live Validation Checklist\n\n");
    output.AppendLine("This checklist is only for the final 16 definitions from the 96/112 baseline. Do not replay the 96 completed definitions.");
    output.AppendLine();
    output.AppendLine("## Recorder contract");
    output.AppendLine();
    output.AppendLine("- `tools/God2.ClientInstrumentation/src/God2ClientTraceProbe.cpp` records S2C complete pairs at the exact in-place decode boundary in restricted `sensitive/live-validation-pairs.jsonl`: complete ciphertext, complete plaintext, key256, rolling byte before/after, decoder state after, length, opcode, timestamp, process/session identity, and marker-correlated phase.");
    output.AppendLine("- C2S records complete `PreEncrypt` plaintext in the same restricted capture and retains socket-send ciphertext in `sensitive/trace.bin`. The corpus exporter accepts a sample only when exact-session state decrypts it uniquely and re-encryption reproduces the original ciphertext.");
    output.AppendLine("- Raw validation content is sensitive-local. Authentication opcode `0x19` and the 417-byte login echo remain excluded from plaintext persistence. No production protocol behavior is modified.");
    output.AppendLine();
    output.AppendLine("## Start / marker / stop commands");
    output.AppendLine();
    output.AppendLine("Run these from the repository root in PowerShell:");
    output.AppendLine();
    output.AppendLine("```powershell");
    output.AppendLine(".\\Automation\\Start-PacketCaptureOnly.ps1 -Label plaintext-gap-live");
    output.AppendLine(".\\Automation\\Add-PacketCaptureMarker.ps1 -Marker MARKER_LOGIN_BEGIN");
    output.AppendLine("# ...record the phase-specific markers below...");
    output.AppendLine(".\\Automation\\Stop-PacketCaptureOnly.ps1");
    output.AppendLine("dotnet run --project .\\tools\\God2.PacketCorpusExporter\\God2.PacketCorpusExporter.csproj -c Release");
    output.AppendLine("```");
    output.AppendLine();
    output.AppendLine("## Ordered user actions");
    output.AppendLine();
    output.AppendLine("1. Start `God2_opt.exe`, stay at the login screen, then start the recorder and write `MARKER_LOGIN_BEGIN`.");
    output.AppendLine("2. Log in, enter character selection, select the test character, enter the world, wait 30 seconds, then write `MARKER_LOGIN_COMPLETE`.");
    output.AppendLine("3. Write `MARKER_IDLE_BEGIN`, do nothing for 60 seconds, then write `MARKER_IDLE_END`. This targets C2S length 5 and S2C lengths 14/20 plus idle opcodes.");
    output.AppendLine("4. Move one step, pause, move several steps, pause, stop, and change facing direction. Keep at least five seconds between actions.");
    output.AppendLine("5. Write `MARKER_OPEN_INVENTORY`, open/close character, inventory, equipment, skill, quest, battle, and immortal panels one at a time, then write `MARKER_CLOSE_INVENTORY`.");
    output.AppendLine("6. With expendable test items only: move one item, use one ordinary item, equip and unequip one item, and split a stack if supported. Never consume or risk a rare item.");
    output.AppendLine("7. Write `MARKER_NPC_CLICK`, approach/click/close one safe NPC twice. Open a merchant once and write `MARKER_SHOP_OPEN`; browse only, then close it.");
    output.AppendLine("8. If safe, write `MARKER_BATTLE_BEGIN`, enter one normal turn-based battle, wait for turn start, write `MARKER_NORMAL_ATTACK` immediately before one normal attack, defend once if available, and write `MARKER_SKILL_CAST` immediately before one skill. Wait for enemy action/result, then write `MARKER_BATTLE_END`.");
    output.AppendLine("9. Write `MARKER_LOGOUT`, log out normally to character selection. Write `MARKER_RELOGIN_BEGIN`, log in once more, enter the world, then write `MARKER_RELOGIN_COMPLETE`. This verifies session/rolling-state reset.");
    output.AppendLine("10. Stop the recorder and rerun the exporter command above. Do not manually copy ciphertext into `packet.bin`; the exporter will promote only checksum-valid, marker-correlated, ciphertext-identical round trips.");
    output.AppendLine();
    output.AppendLine("## Remaining target matrix");
    output.AppendLine();
    output.AppendLine("| Packet | Direction | Opcode | Length | CryptoVerified | CurrentStatus | Required trigger / blocker |");
    output.AppendLine("|---|---|---:|---:|---|---|---|");
    foreach (var packet in targets)
    {
        output.AppendLine($"| {Md(packet.Id)} | {packet.Direction} | {packet.OpcodeHex} | {packet.Length} | {YesNo(packet.CryptoVerified)} | {packet.Status} | {Md(packet.Blocker)} |");
    }
    output.AppendLine();
    output.AppendLine($"- Baseline plaintext: 96 / {packets.Count}");
    output.AppendLine($"- Current plaintext: {packets.Count(value => value.PlaintextAvailable)} / {packets.Count}");
    output.AppendLine($"- Remaining live targets: {remaining.Length}");
    File.WriteAllText(Path.Combine(reportsDirectory, "live_validation_checklist.md"), output.ToString(), new UTF8Encoding(false));
}

static void WritePlaintextGapValidationReport(string reportsDirectory, List<PacketRecord> packets)
{
    var gaps = packets.Where(value => value.IsPlaintextGap)
        .OrderBy(value => value.Direction)
        .ThenBy(value => value.Opcode ?? int.MaxValue)
        .ThenBy(value => value.Id, StringComparer.Ordinal)
        .ToArray();
    var recovered = gaps.Count(value => value.Status == "PLAINTEXT_RECOVERED");
    var before = packets.Count(value => !value.IsPlaintextGap && value.PlaintextAvailable);
    var after = packets.Count(value => value.PlaintextAvailable);
    if (gaps.Length != 83 || before != 29 || after != before + recovered)
    {
        throw new InvalidOperationException($"Plaintext gap accounting failed: gaps={gaps.Length}, before={before}, recovered={recovered}, after={after}.");
    }

    var output = new StringBuilder("# Plaintext Gap Validation\n\n");
    output.AppendLine("This report validates only the 83 definitions that previously had `PlaintextSampleAvailable = false`; the original 29 plaintext definitions were not rescanned or reclassified.");
    output.AppendLine();
    output.AppendLine("## Validation invariants");
    output.AppendLine();
    output.AppendLine("- Frame length is UInt16 little-endian at offset 0 and includes the complete frame.");
    output.AppendLine("- Framed plaintext opcode is UInt8 at offset 2. HandlerDecoded nested records have UInt8 opcode at offset 0 and no transport length prefix.");
    output.AppendLine("- Every recovered framed sample has a valid additive checksum and an explicit PreEncrypt, PostDecrypt, exact-session decrypt, or verified decode/encode boundary.");
    output.AppendLine("- Exact-session candidates require a complete wire frame, a uniquely matching captured key/initial rolling byte, checksum validity, and ciphertext-identical re-encryption.");
    output.AppendLine("- Compression is not present in the validated application-frame pipeline. Unverified definitions retain `Compression=Unknown`.");
    output.AppendLine("- Ciphertext, inferred builder bytes, and payload-free aggregate rows are never emitted as `packet.bin`.");
    output.AppendLine();
    output.AppendLine("## Per-packet result");
    output.AppendLine();
    output.AppendLine("| Packet | Direction | Opcode | PreviousStatus | NewStatus | PlaintextRecovered | RuntimeTriggered | CryptoVerified | ParserVerified | BuilderVerified | Boundary / RoundTrip | Evidence | Blocker |");
    output.AppendLine("|---|---|---:|---|---|---|---|---|---|---|---|---|---|");
    foreach (var packet in gaps)
    {
        var validation = packet.PlaintextAvailable
            ? $"{packet.PlaintextBoundary}; roundTrip={packet.RoundTripVerified}; knownCiphertext={packet.KnownCiphertextVerified}"
            : "Unavailable";
        output.AppendLine($"| {Md(packet.Id)} | {packet.Direction} | {packet.OpcodeHex} | {packet.PreviousStatus} | {packet.Status} | {YesNo(packet.PlaintextAvailable)} | {YesNo(packet.RuntimeTriggered)} | {YesNo(packet.CryptoVerified)} | {YesNo(packet.ParserVerified)} | {YesNo(packet.BuilderVerified)} | {Md(validation)} | {Md(packet.Evidence)} | {Md(packet.Blocker)} |");
    }

    output.AppendLine();
    output.AppendLine("## Classification totals");
    output.AppendLine();
    output.AppendLine($"- TOTAL GAP PACKETS: {gaps.Length}");
    foreach (var status in new[]
             {
                 "PLAINTEXT_RECOVERED", "RUNTIME_NOT_TRIGGERED", "STATIC_STRUCTURE_ONLY", "CRYPTO_UNVERIFIED",
                 "INCOMPLETE_DEFINITION", "UNRELIABLE_OR_INFERRED"
             })
    {
        output.AppendLine($"- {status}: {gaps.Count(value => value.Status == status)}");
    }
    output.AppendLine();
    output.AppendLine("## Corpus delta");
    output.AppendLine();
    output.AppendLine($"- BEFORE PLAINTEXT: {before} / {packets.Count}");
    output.AppendLine($"- AFTER PLAINTEXT: {after} / {packets.Count}");
    output.AppendLine($"- NEWLY RECOVERED: {recovered}");
    output.AppendLine($"- REMAINING WITHOUT PLAINTEXT: {gaps.Length - recovered}");
    output.AppendLine($"- CRYPTO VERIFIED: {gaps.Count(value => value.CryptoVerified)}");
    output.AppendLine($"- STATIC ONLY: {gaps.Count(value => value.Status == "STATIC_STRUCTURE_ONLY")}");
    output.AppendLine($"- UNVERIFIED: {gaps.Count(value => value.Status is "CRYPTO_UNVERIFIED" or "INCOMPLETE_DEFINITION" or "UNRELIABLE_OR_INFERRED")}");
    var finalTargets = packets.Where(IsFinalPlaintextTarget).ToArray();
    var finalRecovered = finalTargets.Count(value => value.PlaintextAvailable);
    var liveRecovered = finalTargets.Count(value => value.PlaintextAvailable && value.RecoveryOrigin == "LiveValidation");
    var falseCandidates = finalTargets.Count(value => value.Status == "REJECTED_FALSE_CANDIDATE");
    output.AppendLine();
    output.AppendLine("## Final 16 live/offline validation round");
    output.AppendLine();
    output.AppendLine("- BEFORE: 96 / 112 plaintext");
    output.AppendLine($"- AFTER: {after} / {packets.Count} plaintext");
    output.AppendLine($"- OFFLINE RECOVERED: {finalRecovered - liveRecovered}");
    output.AppendLine($"- LIVE RECOVERED: {liveRecovered}");
    output.AppendLine($"- CRYPTO VERIFIED: {finalTargets.Count(value => value.CryptoVerified)}");
    output.AppendLine($"- FALSE CANDIDATES: {falseCandidates}");
    output.AppendLine($"- REMAINING: {finalTargets.Count(value => !value.PlaintextAvailable && value.Status != "REJECTED_FALSE_CANDIDATE")}");
    output.AppendLine();
    output.AppendLine("The three referenced 2026-08-06 extracted sessions supplied 41 complete representative wire frames across the five wire-only definitions. None passed a production Login/C2S-World/S2C-World checksum plus ciphertext-identical round trip. Their old enhanced logs report `packetDecodeProbe=0`, so exact key256, rolling byte and phase evidence are genuinely absent rather than merely unscanned.");
    File.WriteAllText(Path.Combine(reportsDirectory, "plaintext_gap_validation.md"), output.ToString(), new UTF8Encoding(false));
}

static bool IsFinalPlaintextTarget(PacketRecord packet)
{
    if (packet.Id.StartsWith("wire-evidence-capture-20260806-", StringComparison.Ordinal))
    {
        return packet.Id is
            "wire-evidence-capture-20260806-client-action-c2s-20-candidate" or
            "wire-evidence-capture-20260806-client-keepalive-c2s-5-candidate" or
            "wire-evidence-capture-20260806-login-handshake-c2s-208-candidate" or
            "wire-evidence-capture-20260806-server-world-delta-s2c-14-candidate" or
            "wire-evidence-capture-20260806-server-world-delta-s2c-20-candidate";
    }
    return packet.Id is
        "evidence-package-20260806-c2s-opcode-1d" or
        "evidence-package-20260806-c2s-opcode-66" or
        "evidence-package-20260806-c2s-opcode-68" or
        "evidence-package-20260806-c2s-opcode-ab" or
        "evidence-package-20260806-c2s-opcode-c2" or
        "evidence-package-20260806-s2c-opcode-23" or
        "evidence-package-20260806-s2c-opcode-42" or
        "evidence-package-20260806-s2c-opcode-5c" or
        "evidence-package-20260806-s2c-opcode-77" or
        "evidence-package-20260806-s2c-opcode-ee" or
        "evidence-package-20260806-s2c-opcode-f5";
}

static void WriteRootReadme(string corpusRoot, List<PacketRecord> packets)
{
    var readme = $"""
# God2 Decrypted Packet Corpus

本資料集整理目前 `God2 Classic Server` repository 中可追溯的 Server protocol packet。以繁體中文說明為主，技術名稱保留英文。

## 1. Corpus 說明

- Packet definitions：{packets.Count}
- C2S：{packets.Count(value => value.Direction == "C2S")}
- S2C：{packets.Count(value => value.Direction == "S2C")}
- Unknown direction：{packets.Count(value => value.Direction == "Unknown")}
- Plaintext samples：{packets.Count(value => value.PlaintextAvailable)}
- Static only：{packets.Count(value => !value.PlaintextAvailable)}

`packet.bin` 僅保存 canonical plaintext bytes。沒有可靠 decoded payload 的項目只有 `structure.json`，不會以 ciphertext 或合成 payload 冒充 runtime packet。

## 2. Server Protocol Architecture

C2S：`Socket → UInt16LE frame → stage-specific decrypt/decode → dispatcher/closed-loop → handler`。

S2C：`runtime/handler → decoded builder → stage-specific encode/encrypt → socket`。

詳細方法與檔案見 `reports/crypto_pipeline.md`。

## 3. Packet Header

所有已驗證 application frames 以 offset `0x00` 的 2-byte `UInt16LE` total length 起始。Decoded application opcode 通常位於 offset `0x02`、寬度 1 byte。Handshake 的 offset `0x02` 不是 application opcode，因此標成 `Unknown`。

## 4. Length Encoding

Length 包含 2-byte length prefix 與整個 frame。`FrameAccumulator` 以此切 frame。

## 5. Opcode Encoding

Decoded opcode 是 `UInt8`。舊 `ProtocolRegistry` 的二-byte `OpcodeCandidate` 可能直接來自 wire ciphertext；只有通過 production decode、checksum 與 exact ciphertext round-trip 的條目才會升級，其餘標示 `CRYPTO_UNVERIFIED`。

## 6. String Encoding

目前可驗證 Login username/password 與 Character name 為 fixed-width ASCII、NUL padded。登入 sample 已原位淨化；寬度、terminator、padding 與 checksum 保持不變。其他字串未證明時為 `Unknown`。

## 7. Endianness

Length、已識別的 `UInt16/UInt32` 欄位均依 code 使用 little-endian。Unknown 欄位不推測。

## 8. Encryption Boundary

Login 使用 `OfficialLoginWireTransform`；World S2C 使用 build-pinned world transform；World C2S 依 family 使用 chained XOR 或 exact-build transform。Handshake 由 runtime 直接讀寫。詳見 crypto report。

## 9. Packet Categories

`packets/` 預建 00–35 gameplay/system categories 及 `90_Unknown`、`91_Unclassified`、`92_Internal`。Battle 使用 turn-based 子分類，不套用即時 MMO 假設。

## 10. Packet Index

完整 index：`catalog/packet_catalog.csv`、`.json`、`.md`。Directional opcode 關係：`catalog/opcode_matrix.*`。

## 11. Evidence Levels

見 `reports/evidence.md`。每筆 catalog 與 packet metadata 均記錄 evidence level、source symbol 與 confidence。

## 12. Unknown Packets

未知或只有 aggregate/wire evidence 的項目保留在 `unknown/` 與 `90_Unknown`，不會丟棄或硬命名。

## 13. Coverage

見 `reports/coverage.md`、`duplicate_opcodes.md` 與 `missing_handlers.md`。

## 14. Limitations

- 2026-08-06 evidence package 本身只保存 64 個 directional family 的 aggregate structure；本次僅以另存的 pinned plaintext-stage frame 或 exact-session trace 補齊可交叉驗證的代表 sample。
- 部分 packet 只有 build-pinned parser/serializer，semantic gameplay mutation 仍被 evidence gate 阻擋。
- Generic fallback 在 transform 前看到 wire bytes，不能用其 candidate opcode 宣稱 plaintext。
- 本 workspace 沒有 `.git`，因此無法產生有效 `git status`/`git diff`。
""";
    File.WriteAllText(Path.Combine(corpusRoot, "README.md"), readme, new UTF8Encoding(false));
}

static void WriteFileHashes(string corpusRoot)
{
    var manifestPath = Path.Combine(corpusRoot, "manifests", "files.sha256");
    var files = Directory.GetFiles(corpusRoot, "*", SearchOption.AllDirectories)
        .Where(value => !string.Equals(value, manifestPath, StringComparison.OrdinalIgnoreCase))
        .OrderBy(value => Path.GetRelativePath(corpusRoot, value), StringComparer.Ordinal)
        .ToArray();
    var lines = files.Select(value => $"{Sha256File(value)}  {Path.GetRelativePath(corpusRoot, value).Replace('\\', '/')}");
    File.WriteAllLines(manifestPath, lines, new UTF8Encoding(false));
}

static void ValidateCorpus(string corpusRoot, List<PacketRecord> packets)
{
    if (packets.Select(value => value.Id).Distinct(StringComparer.Ordinal).Count() != packets.Count)
    {
        throw new InvalidOperationException("Duplicate packet ids detected.");
    }
    foreach (var packet in packets)
    {
        var directory = Path.Combine(corpusRoot, packet.ArtifactPath.Replace('/', Path.DirectorySeparatorChar));
        var binary = Path.Combine(directory, "packet.bin");
        if (packet.PlaintextAvailable != File.Exists(binary))
        {
            throw new InvalidOperationException($"Plaintext sample invariant failed: {packet.Id}");
        }
        if (packet.Sample is { Length: >= 2 } sample && packet.HeaderSize != 1 && BinaryPrimitives.ReadUInt16LittleEndian(sample) != sample.Length)
        {
            throw new InvalidOperationException($"Length prefix mismatch: {packet.Id}");
        }
        if (packet.Sample is { Length: >= 1 } handlerSample && packet.HeaderSize == 1 && packet.Opcode != handlerSample[0])
        {
            throw new InvalidOperationException($"Handler record opcode mismatch: {packet.Id}");
        }
        if (packet.IsPlaintextGap && packet.PlaintextAvailable &&
            (!packet.RoundTripVerified || !packet.CryptoVerified || packet.Status != "PLAINTEXT_RECOVERED"))
        {
            throw new InvalidOperationException($"Recovered gap validation invariant failed: {packet.Id}");
        }
    }
    var forbidden = new[] { "BEGIN PRIVATE KEY", "github_pat_", "ghp_", "AKIA", "mariadb://", "password=" };
    foreach (var file in Directory.GetFiles(corpusRoot, "*", SearchOption.AllDirectories).Where(value => !value.EndsWith(".bin", StringComparison.OrdinalIgnoreCase)))
    {
        var text = File.ReadAllText(file);
        var hit = forbidden.FirstOrDefault(value => text.Contains(value, StringComparison.OrdinalIgnoreCase));
        if (hit is not null)
        {
            throw new InvalidOperationException($"Sensitive-data pattern '{hit}' found in {file}.");
        }
    }
    var manifest = Path.Combine(corpusRoot, "manifests", "files.sha256");
    foreach (var line in File.ReadLines(manifest))
    {
        var split = line.Split("  ", 2, StringSplitOptions.None);
        var path = Path.Combine(corpusRoot, split[1].Replace('/', Path.DirectorySeparatorChar));
        if (!string.Equals(split[0], Sha256File(path), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"SHA-256 verification failed: {split[1]}");
        }
    }
}

static string PacketReadme(PacketRecord packet)
{
    var fields = packet.Fields.Count == 0
        ? "| Unknown | Unknown | Unknown | Unknown |\n"
        : string.Join('\n', packet.Fields.Select(value => $"| 0x{value.Offset:X2} | {value.Size} | {value.Type} | {value.Name} |")) + "\n";
    return $"""
# {packet.Name}

- 功能：{packet.Subcategory}
- Direction：{packet.Direction}
- Opcode：{packet.OpcodeHex}
- Length：{packet.Length}
- Header：{packet.HeaderSize} bytes
- Encoding：Length/known numeric fields little-endian；unknown fields 不推測
- Encryption：{packet.Encryption}
- Compression：{packet.Compression}
- Runtime Handler：{packet.Handler}
- Server 行為：{packet.RuntimeUsage}
- Evidence：{packet.Evidence}
- Evidence Level：{packet.EvidenceLevel}
- Confidence：{packet.Confidence}
- PlaintextSampleAvailable：{packet.PlaintextAvailable.ToString().ToLowerInvariant()}
- Previous Status：{packet.PreviousStatus}
- Validation Status：{packet.Status}
- Plaintext Boundary：{packet.PlaintextBoundary}
- Runtime Triggered：{packet.RuntimeTriggered.ToString().ToLowerInvariant()}
- Crypto Verified：{packet.CryptoVerified.ToString().ToLowerInvariant()}
- Round-trip Verified：{packet.RoundTripVerified.ToString().ToLowerInvariant()}
- Known Ciphertext Verified：{packet.KnownCiphertextVerified.ToString().ToLowerInvariant()}
- Blocker：{packet.Blocker}

## Fields

| Offset | Size | Type | Name |
|---:|---:|---|---|
{fields}
## Unknown Fields / Limitations

{(string.IsNullOrWhiteSpace(packet.Notes) ? "未被 parser/builder 明確命名的剩餘 bytes 一律視為 Unknown。" : packet.Notes)}

## Source

- File：`{packet.SourceFile}`
- Symbol：`{packet.SourceSymbol}`
- Parser：`{packet.Parser}`
- Builder：`{packet.Builder}`
""";
}

static SemanticInfo SemanticFor(string direction, byte opcode, IReadOnlyList<int> lengths)
{
    return (direction, opcode) switch
    {
        ("C2S", 0x2E) => new("MovementCommandFamily", "06_Movement", "Movement", "OfficialClientWorldProtocolFrames.TryDecodeWorldMovement", "Unknown", "TcpNetworkHost.TryHandleOfficialWorldMovementAsync", "Runtime family", "Recovered"),
        ("C2S", 0x35) => new("BattleCommandEnvelope", "14_Battle", "Turn Action / semantics unknown", "OfficialBattleCommandWireCodec.DecodeLayout", "Unknown", "BattleProtocolRuntimeAdapter (mutation blocked)", "Evidence-gated", "Recovered"),
        ("C2S", 0xA6) => new("ServerSelectionFamily", "03_Character", "Server selection", "OfficialServerSelectionWireCodec.DecodeRequest", "OfficialServerSelectionWireCodec.SerializeRequest", "TcpNetworkHost.TryHandleOfficialServerSelectionAsync", "Runtime", "Verified"),
        ("C2S", 0x37) => new("NpcDialogOpenFamily", "07_NPC", "Interaction open", "OfficialNpcInteractionWireCodec.DecodeOpen", "Unknown", "OfficialNpcInteractionClosedLoop", "Runtime", "Verified"),
        ("C2S", 0x39) => new("MerchantCloseFamily", "21_Merchant", "Interaction close", "OfficialNpcInteractionWireCodec.DecodeClose", "Unknown", "OfficialNpcInteractionClosedLoop", "Runtime", "Recovered"),
        ("S2C", 0x07) => new("CharacterListBootstrapFamily", "03_Character", "Character list", "OfficialServerSelectionWireCodec.DecodeResponse", "OfficialServerSelectionWireCodec.SerializeResponse", "Client handler", "Runtime", "Verified"),
        ("S2C", 0x1F) when lengths.Contains(128) => new("PlayerSpawnFamily", "04_World", "Player Spawn", "OfficialClientWorldProtocolFrames.ParseCurrentPlayerSpawn", "BuildPlayerSpawnFrame128", "Client handler", "Runtime", "Verified"),
        ("S2C", 0x61) => new("MapTransitionFamily", "05_Map", "Map transition", "OfficialPortalWireCodec.DecodeResult", "OfficialPortalWireCodec.SerializeResult", "Client handler", "Runtime", "Verified"),
        ("S2C", 0x72) => new("EntitySpawnFamily", "07_NPC", "NPC/entity spawn family", "Unknown", "OfficialNpcReplicationWireCodec.SerializeSpawn (24-byte specialization)", "Client handler", "Runtime specialization", "Recovered"),
        ("S2C", 0x7A) => new("NpcDialogResultFamily", "07_NPC", "Dialog result", "Client consumer evidence", "OfficialNpcInteractionWireCodec.SerializeDialogOpen", "Client handler", "Runtime", "Verified"),
        ("S2C", 0xE6) => new("BattleResultFamily", "14_Battle", "Battle Result / Battle End candidate", "Unknown", "Unknown", "Client handler evidence", "Runtime capture aggregate", "Recovered"),
        _ => new($"Unknown_{direction}_{opcode:X2}", "90_Unknown", "Unknown", "Unknown", "Unknown", "Unknown", "Unknown", "Recovered")
    };
}

static string WorldCategory(string purpose) => purpose switch
{
    "PlayerSpawn" => "04_World",
    "WorldMapAndSceneBootstrap" => "05_Map",
    "WorldUiBootstrap" => "32_UI",
    _ => "04_World"
};

static string SourceFor(string builder) => builder switch
{
    var value when value.Contains("OfficialClientLoginProtocolFrames", StringComparison.Ordinal) => "src/God2.ClassicServer.Runtime/OfficialClientLoginProtocolFrames.cs",
    var value when value.Contains("OfficialServerSelectionWireCodec", StringComparison.Ordinal) => "src/God2.ClassicServer.Protocol/OfficialServerSelectionWire.cs",
    var value when value.Contains("OfficialPortalWireCodec", StringComparison.Ordinal) => "src/God2.ClassicServer.Protocol/OfficialPortalWire.cs",
    var value when value.Contains("OfficialNpcInteractionWireCodec", StringComparison.Ordinal) => "src/God2.ClassicServer.Runtime/OfficialNpcInteractionWireCodec.cs",
    var value when value.Contains("OfficialNpcReplicationWireCodec", StringComparison.Ordinal) => "src/God2.ClassicServer.Runtime/OfficialNpcReplicationWireCodec.cs",
    var value when value.Contains("OfficialClientWorldProtocolFrames", StringComparison.Ordinal) => "src/God2.ClassicServer.Runtime/OfficialClientWorldProtocolFrames.cs",
    _ => "src/God2.ClassicServer.Runtime/RuntimeFoundation.cs"
};

static FieldRecord F(int offset, int size, string name, string type, object? value) => new() { Offset = offset, Size = size, Name = name, Type = type, Value = value };
static object ValueOrString(string value) => bool.TryParse(value, out var boolean) ? boolean : value;
static int? ParseCandidateOpcode(string? value) => string.IsNullOrWhiteSpace(value) || !int.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var parsed) ? null : parsed;
static string Direction(PacketDirection direction) => direction switch { PacketDirection.ClientToServer => "C2S", PacketDirection.ServerToClient => "S2C", _ => "Unknown" };
static void SanitizeFixedAscii(Span<byte> field, string value) { field.Clear(); Encoding.ASCII.GetBytes(value, field); }
static string SafeName(string value) { var chars = value.Select(character => char.IsLetterOrDigit(character) ? character : '_').ToArray(); return new string(chars).Trim('_'); }
static string ShortHash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))[..8];
static string YesNo(bool value) => value ? "Yes" : "No";
static string Md(string value) => value.Replace("|", "\\|", StringComparison.Ordinal).Replace("\r", " ", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal);
static string Csv(params string[] values) => string.Join(',', values.Select(value => $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\""));
static string Sha256File(string path) { using var stream = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant(); }
static string JsonText(JsonElement element, string propertyName)
{
    if (!element.TryGetProperty(propertyName, out var property)) return string.Empty;
    return property.ValueKind switch
    {
        JsonValueKind.String => property.GetString() ?? string.Empty,
        JsonValueKind.Number => property.GetRawText(),
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        _ => property.GetRawText()
    };
}
static string HexDump(byte[] bytes) { var output = new StringBuilder(); for (var offset = 0; offset < bytes.Length; offset += 16) { output.Append(offset.ToString("X8", CultureInfo.InvariantCulture)).Append("  "); var count = Math.Min(16, bytes.Length - offset); for (var index = 0; index < count; index++) output.Append(bytes[offset + index].ToString("X2", CultureInfo.InvariantCulture)).Append(index == 7 ? "  " : " "); output.AppendLine(); } return output.ToString(); }
static void WriteJson(string path, object value) => File.WriteAllText(path, JsonSerializer.Serialize(value, JsonDefaults.Options), new UTF8Encoding(false));

static string FindRepositoryRoot(string start)
{
    var directory = new DirectoryInfo(start);
    while (directory is not null)
    {
        if (File.Exists(Path.Combine(directory.FullName, "God2ClassicServer.sln"))) return directory.FullName;
        directory = directory.Parent;
    }
    throw new DirectoryNotFoundException("God2ClassicServer.sln was not found above the exporter binary.");
}

static void EnsureSafeOutput(string repositoryRoot, string corpusRoot, string zipPath)
{
    var expectedRoot = Path.GetFullPath(Path.Combine(repositoryRoot, "artifacts")) + Path.DirectorySeparatorChar;
    if (!Path.GetFullPath(corpusRoot).StartsWith(expectedRoot, StringComparison.OrdinalIgnoreCase) ||
        !Path.GetFullPath(zipPath).StartsWith(expectedRoot, StringComparison.OrdinalIgnoreCase) ||
        Path.GetFileName(corpusRoot) != "God2_Decrypted_Packet_Corpus")
    {
        throw new InvalidOperationException("Exporter output escaped the intended artifacts directory.");
    }
}

sealed class PacketRecord
{
    public string Id { get; set; } = "";
    public string Direction { get; set; } = "Unknown";
    public int? Opcode { get; set; }
    public string OpcodeHex { get; set; } = "Unknown";
    public string Name { get; set; } = "Unknown";
    public string Category { get; set; } = "90_Unknown";
    public string Subcategory { get; set; } = "Unknown";
    public int HeaderSize { get; set; }
    public string PayloadSize { get; set; } = "Unknown";
    public string Length { get; set; } = "Unknown";
    public bool VariableLength { get; set; }
    public string EncryptedOnWire { get; set; } = "Unknown";
    public string Encryption { get; set; } = "Unknown";
    public string Compression { get; set; } = "Unknown";
    public string Parser { get; set; } = "Unknown";
    public string Builder { get; set; } = "Unknown";
    public string Handler { get; set; } = "Unknown";
    public string RuntimeUsage { get; set; } = "Unknown";
    public string SourceFile { get; set; } = "Unknown";
    public string SourceSymbol { get; set; } = "Unknown";
    public string Evidence { get; set; } = "Unknown";
    public string EvidenceLevel { get; set; } = "Static only";
    public string Confidence { get; set; } = "Unknown";
    public bool PlaintextAvailable { get; set; }
    public string Status { get; set; } = "Unknown";
    public bool IsPlaintextGap { get; set; }
    public string PreviousStatus { get; set; } = "NotInGapSet";
    public bool RuntimeTriggered { get; set; }
    public bool CryptoVerified { get; set; }
    public bool ParserVerified { get; set; }
    public bool BuilderVerified { get; set; }
    public bool RoundTripVerified { get; set; }
    public bool KnownCiphertextVerified { get; set; }
    public string PlaintextBoundary { get; set; } = "Unavailable";
    public string Blocker { get; set; } = "Not assessed";
    public string Notes { get; set; } = "";
    public string ArtifactPath { get; set; } = "";
    [JsonIgnore] public string RecoveryOrigin { get; set; } = "";
    [JsonIgnore] public byte[]? Sample { get; set; }
    [JsonIgnore] public int[] ObservedLengths { get; set; } = [];
    [JsonIgnore] public string[] WireSamples { get; set; } = [];
    public List<FieldRecord> Fields { get; set; } = [];
}

sealed class FieldRecord
{
    public int Offset { get; set; }
    public int Size { get; set; }
    public string Name { get; set; } = "Unknown";
    public string Type { get; set; } = "Unknown";
    public object? Value { get; set; }
}

sealed record SemanticInfo(string Name, string Category, string Subcategory, string Parser, string Builder, string Handler, string RuntimeUsage, string Confidence);

sealed record InstrumentedFrame(
    string SourcePath,
    ulong Sequence,
    string SourceFrameId,
    string Direction,
    string CaptureStage,
    string EvidenceLevel,
    byte[] Bytes,
    bool FramingValid,
    bool ChecksumValid);

sealed record SessionCipherState(byte InitialPreviousByte, byte[] Key, string Identity);
sealed record RestrictedTraceRecord(ulong Sequence, ushort Direction, byte[] Payload);
sealed record ReassembledTraceFrame(string Direction, ulong Sequence, int Ordinal, byte[] WireBytes);
sealed record ExactSessionTraceCandidate(
    string Direction,
    ulong Sequence,
    byte[] Decoded,
    byte[] WireBytes,
    string TracePath,
    string LogPath,
    string CipherIdentity,
    string ProtocolPhase = "Unknown");
sealed record CaptureMarker(long TimestampUnixMs, string Marker);
sealed record ReferencedCaptureWireSample(
    string Semantic,
    string SessionId,
    string SourceFrameIds,
    string CandidateFrameId,
    string Direction,
    byte[] WireBytes,
    string SourcePath);
sealed record KnownWireRecovery(byte[] Wire, byte[] Decoded, string Transform);

static class JsonDefaults
{
    public static JsonSerializerOptions Options { get; } = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };
}
