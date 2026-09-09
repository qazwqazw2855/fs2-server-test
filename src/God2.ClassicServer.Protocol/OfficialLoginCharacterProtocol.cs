using God2.ClassicServer.Application.Common;

namespace God2.ClassicServer.Protocol;

public enum OfficialLoginCharacterPacketKind
{
    LoginRequest,
    LoginResponse,
    CharacterListRequest,
    CharacterListResponse,
    CharacterCreateRequest,
    CharacterCreateResponse,
    CharacterDeleteRequest,
    CharacterDeleteResponse,
    CharacterSelectRequest,
    CharacterSelectResponse,
    WorldEntryContext
}

public sealed record OfficialLoginPacket(string Username, string Password, IReadOnlyDictionary<string, byte[]> UnknownFields);

public sealed record OfficialLoginResponsePacket(string ResultCodeEvidence, IReadOnlyDictionary<string, byte[]> UnknownFields);

public sealed record VerifiedOpaqueLoginPacket(string CandidateRole, string KnowledgeId, string FrameSha256);

public sealed record OfficialLoginProtocolCapabilities(
    bool LoginPacketRoleVerified,
    bool LoginCredentialFieldsVerified,
    bool LoginResponseRoleVerified,
    bool LoginRuntimeMutationEnabled);

public sealed record OfficialCharacterListRequestPacket(IReadOnlyDictionary<string, byte[]> UnknownFields);

public sealed record OfficialCharacterListResponsePacket(IReadOnlyList<OfficialCharacterListEntryPacket> Characters, IReadOnlyDictionary<string, byte[]> UnknownFields);

public sealed record OfficialCharacterListEntryPacket(
    long CharacterId,
    string Name,
    string Class,
    string Gender,
    string LifeSkill,
    string Appearance,
    int Level,
    int MapId,
    int PositionX,
    int PositionY,
    string Status,
    IReadOnlyDictionary<string, byte[]> UnknownFields);

public sealed record OfficialCharacterCreateRequestPacket(
    string? Name,
    string? Class,
    string? Gender,
    string? LifeSkill,
    string? Appearance,
    IReadOnlyDictionary<string, byte[]> UnknownFields);

public sealed record OfficialCharacterCreateResponsePacket(string ResultCodeEvidence, IReadOnlyDictionary<string, byte[]> UnknownFields);

public sealed record OfficialCharacterDeleteRequestPacket(long? CharacterId, int? Slot, IReadOnlyDictionary<string, byte[]> UnknownFields);

public sealed record OfficialCharacterDeleteResponsePacket(string ResultCodeEvidence, IReadOnlyDictionary<string, byte[]> UnknownFields);

public sealed record OfficialCharacterSelectRequestPacket(long? CharacterId, int? Slot, IReadOnlyDictionary<string, byte[]> UnknownFields);

public sealed record OfficialCharacterSelectResponsePacket(string ResultCodeEvidence, IReadOnlyDictionary<string, byte[]> UnknownFields);

public sealed record OfficialWorldEntryContextPacket(
    long CharacterId,
    int MapId,
    int PositionX,
    int PositionY,
    IReadOnlyDictionary<string, byte[]> UnknownFields);

public sealed record OfficialProtocolRecoveryGap(
    OfficialLoginCharacterPacketKind PacketKind,
    string RequiredEvidence,
    string CurrentStatus,
    string Blocker);

public sealed record OfficialProtocolRecoveryCoverage(IReadOnlyList<OfficialProtocolRecoveryGap> Gaps)
{
    public bool IsComplete => Gaps.Count == 0;
}

public interface IOfficialLoginCharacterProtocolCodec
{
    OfficialProtocolRecoveryCoverage Coverage { get; }

    OfficialLoginProtocolCapabilities LoginCapabilities { get; }

    OperationResult<VerifiedOpaqueLoginPacket> VerifyOpaqueLoginRequest(PacketEnvelope packet);

    OperationResult<VerifiedOpaqueLoginPacket> VerifyOpaqueLoginSuccessResponse(PacketEnvelope packet);

    OperationResult<OfficialLoginPacket> DeserializeLoginRequest(PacketEnvelope packet);

    OperationResult<ReadOnlyMemory<byte>> SerializeLoginResponse(OfficialLoginResponsePacket response);

    OperationResult<OfficialCharacterListRequestPacket> DeserializeCharacterListRequest(PacketEnvelope packet);

    OperationResult<ReadOnlyMemory<byte>> SerializeCharacterListResponse(OfficialCharacterListResponsePacket response);

    OperationResult<OfficialCharacterCreateRequestPacket> DeserializeCharacterCreateRequest(PacketEnvelope packet);

    OperationResult<ReadOnlyMemory<byte>> SerializeCharacterCreateResponse(OfficialCharacterCreateResponsePacket response);

    OperationResult<OfficialCharacterDeleteRequestPacket> DeserializeCharacterDeleteRequest(PacketEnvelope packet);

    OperationResult<ReadOnlyMemory<byte>> SerializeCharacterDeleteResponse(OfficialCharacterDeleteResponsePacket response);

    OperationResult<OfficialCharacterSelectRequestPacket> DeserializeCharacterSelectRequest(PacketEnvelope packet);

    OperationResult<ReadOnlyMemory<byte>> SerializeCharacterSelectResponse(OfficialCharacterSelectResponsePacket response);

    OperationResult<ReadOnlyMemory<byte>> SerializeWorldEntryContext(OfficialWorldEntryContextPacket context);
}

public sealed class EvidenceGatedOfficialLoginCharacterProtocolCodec : IOfficialLoginCharacterProtocolCodec
{
    private readonly VerifiedPacketRoleMatcher _roleMatcher = new();

    public EvidenceGatedOfficialLoginCharacterProtocolCodec(ProtocolRegistry? registry = null)
    {
        var selectedRegistry = registry ?? ProtocolRegistry.Official;
        LoginCapabilities = OfficialLoginCharacterProtocolEvidence.EvaluateLoginCapabilities(selectedRegistry);
        Coverage = OfficialLoginCharacterProtocolEvidence.Evaluate(selectedRegistry, LoginCapabilities);
    }

    public OfficialProtocolRecoveryCoverage Coverage { get; }

    public OfficialLoginProtocolCapabilities LoginCapabilities { get; }

    public OperationResult<VerifiedOpaqueLoginPacket> VerifyOpaqueLoginRequest(PacketEnvelope packet) =>
        VerifyRole(
            packet,
            OfficialLoginEvidenceCatalog.LoginRequestKnowledgeId,
            "LoginRequest",
            LoginCapabilities.LoginPacketRoleVerified);

    public OperationResult<VerifiedOpaqueLoginPacket> VerifyOpaqueLoginSuccessResponse(PacketEnvelope packet) =>
        VerifyRole(
            packet,
            OfficialLoginEvidenceCatalog.LoginSuccessResponseKnowledgeId,
            "LoginSuccessAndCharacterBootstrap",
            LoginCapabilities.LoginResponseRoleVerified);

    public OperationResult<OfficialLoginPacket> DeserializeLoginRequest(PacketEnvelope packet)
    {
        var role = VerifyOpaqueLoginRequest(packet);
        if (!role.Succeeded)
        {
            return OperationResult<OfficialLoginPacket>.Failure(role.Error.Code, role.Error.Message, role.Error.Source);
        }

        return OperationResult<OfficialLoginPacket>.Failure(
            "protocol.login_credential_fields_required",
            "Official Login Request packet role is verified, but username and password field semantics remain unknown.",
            OfficialLoginEvidenceCatalog.LoginRequestKnowledgeId);
    }

    public OperationResult<ReadOnlyMemory<byte>> SerializeLoginResponse(OfficialLoginResponsePacket response) =>
        OperationResult<ReadOnlyMemory<byte>>.Failure(
            "protocol.login_runtime_mutation_disabled",
            "Official Login success response role is verified, but result-code and credential semantics do not permit response generation.",
            OfficialLoginEvidenceCatalog.LoginSuccessResponseKnowledgeId);

    public OperationResult<OfficialCharacterListRequestPacket> DeserializeCharacterListRequest(PacketEnvelope packet) =>
        Missing<OfficialCharacterListRequestPacket>(OfficialLoginCharacterPacketKind.CharacterListRequest);

    public OperationResult<ReadOnlyMemory<byte>> SerializeCharacterListResponse(OfficialCharacterListResponsePacket response) =>
        MissingBytes(OfficialLoginCharacterPacketKind.CharacterListResponse);

    public OperationResult<OfficialCharacterCreateRequestPacket> DeserializeCharacterCreateRequest(PacketEnvelope packet) =>
        Missing<OfficialCharacterCreateRequestPacket>(OfficialLoginCharacterPacketKind.CharacterCreateRequest);

    public OperationResult<ReadOnlyMemory<byte>> SerializeCharacterCreateResponse(OfficialCharacterCreateResponsePacket response) =>
        MissingBytes(OfficialLoginCharacterPacketKind.CharacterCreateResponse);

    public OperationResult<OfficialCharacterDeleteRequestPacket> DeserializeCharacterDeleteRequest(PacketEnvelope packet) =>
        Missing<OfficialCharacterDeleteRequestPacket>(OfficialLoginCharacterPacketKind.CharacterDeleteRequest);

    public OperationResult<ReadOnlyMemory<byte>> SerializeCharacterDeleteResponse(OfficialCharacterDeleteResponsePacket response) =>
        MissingBytes(OfficialLoginCharacterPacketKind.CharacterDeleteResponse);

    public OperationResult<OfficialCharacterSelectRequestPacket> DeserializeCharacterSelectRequest(PacketEnvelope packet) =>
        Missing<OfficialCharacterSelectRequestPacket>(OfficialLoginCharacterPacketKind.CharacterSelectRequest);

    public OperationResult<ReadOnlyMemory<byte>> SerializeCharacterSelectResponse(OfficialCharacterSelectResponsePacket response) =>
        MissingBytes(OfficialLoginCharacterPacketKind.CharacterSelectResponse);

    public OperationResult<ReadOnlyMemory<byte>> SerializeWorldEntryContext(OfficialWorldEntryContextPacket context) =>
        MissingBytes(OfficialLoginCharacterPacketKind.WorldEntryContext);

    private OperationResult<T> Missing<T>(OfficialLoginCharacterPacketKind kind) =>
        OperationResult<T>.Failure("protocol.recovery_required", Message(kind), kind.ToString());

    private OperationResult<ReadOnlyMemory<byte>> MissingBytes(OfficialLoginCharacterPacketKind kind) =>
        OperationResult<ReadOnlyMemory<byte>>.Failure("protocol.recovery_required", Message(kind), kind.ToString());

    private OperationResult<VerifiedOpaqueLoginPacket> VerifyRole(
        PacketEnvelope packet,
        string knowledgeId,
        string candidateRole,
        bool roleGate)
    {
        if (!roleGate || !_roleMatcher.Matches(packet, knowledgeId))
        {
            return OperationResult<VerifiedOpaqueLoginPacket>.Failure(
                "protocol.login_packet_role_unverified",
                "The frame does not match byte-exact VERIFIED_RAW Login evidence.",
                knowledgeId);
        }

        var frame = new PacketSerializer().Write(packet);
        return OperationResult<VerifiedOpaqueLoginPacket>.Success(
            new VerifiedOpaqueLoginPacket(
                candidateRole,
                knowledgeId,
                PacketEvidenceHash.Sha256Hex(frame.Value.Span)));
    }

    private string Message(OfficialLoginCharacterPacketKind kind)
    {
        var gap = Coverage.Gaps.FirstOrDefault(item => item.PacketKind == kind);
        return gap is null
            ? "Official raw packet evidence is required before this packet can be processed."
            : $"{gap.RequiredEvidence}: {gap.Blocker}";
    }
}

public static class OfficialLoginCharacterProtocolEvidence
{
    private static readonly IReadOnlyDictionary<OfficialLoginCharacterPacketKind, (string Family, PacketDirection Direction, string RequiredEvidence)> Requirements =
        new Dictionary<OfficialLoginCharacterPacketKind, (string Family, PacketDirection Direction, string RequiredEvidence)>
        {
            [OfficialLoginCharacterPacketKind.LoginRequest] = ("Login", PacketDirection.ClientToServer, "Official Login Request raw packet with length, opcode, header, sequence, username, password, unknown bytes, and variable length rules"),
            [OfficialLoginCharacterPacketKind.LoginResponse] = ("Login", PacketDirection.ServerToClient, "Official Login Response raw packet for success and every known failure result"),
            [OfficialLoginCharacterPacketKind.CharacterListRequest] = ("CharacterList", PacketDirection.ClientToServer, "Official CharacterList Request raw packet"),
            [OfficialLoginCharacterPacketKind.CharacterListResponse] = ("CharacterList", PacketDirection.ServerToClient, "Official CharacterList Response raw packet and repeated character entry structure"),
            [OfficialLoginCharacterPacketKind.CharacterCreateRequest] = ("CharacterCreate", PacketDirection.ClientToServer, "Official CharacterCreate raw request packet proving opcode, length, name, class, gender, life skill, appearance/template, slot/sequence/checksum, and unknown bytes"),
            [OfficialLoginCharacterPacketKind.CharacterCreateResponse] = ("CharacterCreate", PacketDirection.ServerToClient, "Official CharacterCreate result raw response packet"),
            [OfficialLoginCharacterPacketKind.CharacterDeleteRequest] = ("CharacterDelete", PacketDirection.ClientToServer, "Official CharacterDelete raw request packet proving opcode, length, character id or slot, confirmation fields, sequence/checksum, and unknown bytes"),
            [OfficialLoginCharacterPacketKind.CharacterDeleteResponse] = ("CharacterDelete", PacketDirection.ServerToClient, "Official CharacterDelete result raw response packet"),
            [OfficialLoginCharacterPacketKind.CharacterSelectRequest] = ("CharacterSelect", PacketDirection.ClientToServer, "Official CharacterSelect Request raw packet proving CharacterId or Slot format"),
            [OfficialLoginCharacterPacketKind.CharacterSelectResponse] = ("CharacterSelect", PacketDirection.ServerToClient, "Official CharacterSelect Response raw packet"),
            [OfficialLoginCharacterPacketKind.WorldEntryContext] = ("WorldEntry", PacketDirection.ServerToClient, "Official World Entry Context raw packet sequence after CharacterSelect")
        };

    public static OfficialLoginProtocolCapabilities EvaluateLoginCapabilities(ProtocolRegistry registry)
    {
        var requestRoleVerified = HasVerifiedPacket(registry, OfficialLoginEvidenceCatalog.LoginRequestKnowledgeId);
        var responseRoleVerified = HasVerifiedPacket(registry, OfficialLoginEvidenceCatalog.LoginSuccessResponseKnowledgeId);
        return new OfficialLoginProtocolCapabilities(
            requestRoleVerified,
            LoginCredentialFieldsVerified: false,
            responseRoleVerified,
            LoginRuntimeMutationEnabled: false);
    }

    public static OfficialProtocolRecoveryCoverage Evaluate(
        ProtocolRegistry registry,
        OfficialLoginProtocolCapabilities? loginCapabilities = null)
    {
        var capabilities = loginCapabilities ?? EvaluateLoginCapabilities(registry);
        var gaps = new List<OfficialProtocolRecoveryGap>();
        foreach (var requirement in Requirements)
        {
            if (requirement.Key == OfficialLoginCharacterPacketKind.LoginRequest)
            {
                gaps.Add(new OfficialProtocolRecoveryGap(
                    requirement.Key,
                    requirement.Value.RequiredEvidence,
                    capabilities.LoginPacketRoleVerified ? "PacketRoleVerified;FieldSemanticsUnknown" : "NotRecovered",
                    capabilities.LoginPacketRoleVerified
                        ? "The 208-byte Login Request role is VERIFIED_RAW; username and password offsets remain unknown."
                        : "No byte-exact verified Login Request exists in ProtocolRegistry."));
                continue;
            }

            if (requirement.Key == OfficialLoginCharacterPacketKind.LoginResponse)
            {
                gaps.Add(new OfficialProtocolRecoveryGap(
                    requirement.Key,
                    requirement.Value.RequiredEvidence,
                    capabilities.LoginResponseRoleVerified ? "PacketRoleVerified;ResultSemanticsUnknown" : "NotRecovered",
                    capabilities.LoginResponseRoleVerified
                        ? "The 417-byte success/character bootstrap role is VERIFIED_RAW; result-code and generation semantics remain unknown."
                        : "No byte-exact verified Login success response exists in ProtocolRegistry."));
                continue;
            }

            var hasVerifiedEvidence = registry
                .FindByFamily(requirement.Value.Family)
                .Any(packet =>
                    packet.Direction == requirement.Value.Direction &&
                    packet.Verified &&
                    packet.Confidence == ProtocolConfidence.Verified &&
                    packet.RecoveryStatus == PacketRecoveryStatus.Verified &&
                    (packet.Samples.Count > 0 || packet.FixedPrefixes.Count > 0));

            if (!hasVerifiedEvidence)
            {
                gaps.Add(new OfficialProtocolRecoveryGap(
                    requirement.Key,
                    requirement.Value.RequiredEvidence,
                    "NotRecovered",
                    "No verified official raw packet exists in ProtocolRegistry for this packet kind."));
            }
        }

        return new OfficialProtocolRecoveryCoverage(gaps);
    }

    private static bool HasVerifiedPacket(ProtocolRegistry registry, string knowledgeId) =>
        registry.TryGetPacket(knowledgeId, out var packet) &&
        packet is not null &&
        packet.Verified &&
        packet.Confidence == ProtocolConfidence.Verified &&
        packet.RecoveryStatus == PacketRecoveryStatus.Verified &&
        packet.EvidenceSources.Any(source =>
            source.Path.EndsWith("ProtocolEvidenceRecovery/source-manifest.json", StringComparison.Ordinal));
}
