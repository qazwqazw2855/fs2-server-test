using God2.ClassicServer.Application.Common;
using God2.ClassicServer.Protocol;

namespace God2.ClassicServer.Runtime;

public sealed record OfficialProtocolRuntimeResponse(
    CharacterOperationResultCode Code,
    RuntimeSession Session,
    ReadOnlyMemory<byte> ResponseFrame,
    IReadOnlyList<string> RecoveryGaps)
{
    public bool Succeeded => Code == CharacterOperationResultCode.Success;
}

public sealed class OfficialLoginCharacterProtocolRuntime
{
    private readonly IOfficialLoginCharacterProtocolCodec _codec;
    private readonly AuthenticationService _authentication;
    private readonly CharacterListQuery _characterList;
    private readonly CharacterRuntimeService _characters;

    public OfficialLoginCharacterProtocolRuntime(
        IOfficialLoginCharacterProtocolCodec codec,
        AuthenticationService authentication,
        CharacterListQuery characterList,
        CharacterRuntimeService characters)
    {
        _codec = codec;
        _authentication = authentication;
        _characterList = characterList;
        _characters = characters;
    }

    public OfficialProtocolRecoveryCoverage Coverage => _codec.Coverage;

    public OfficialLoginProtocolCapabilities LoginCapabilities => _codec.LoginCapabilities;

    public async Task<OfficialProtocolRuntimeResponse> HandleLoginAsync(RuntimeSession session, PacketEnvelope packet, CancellationToken cancellationToken)
    {
        var request = _codec.DeserializeLoginRequest(packet);
        if (!request.Succeeded || request.Value is null)
        {
            return RecoveryRequired(session, request.Error);
        }

        if (!LoginCapabilities.LoginRuntimeMutationEnabled)
        {
            return RecoveryRequired(session, new OperationError(
                "protocol.login_runtime_mutation_disabled",
                "Login credential and result semantics must be completely recovered before session or account mutation.",
                OfficialLoginCharacterPacketKind.LoginResponse.ToString()));
        }

        var responses = new Dictionary<LoginResultCode, ReadOnlyMemory<byte>>();
        foreach (var code in Enum.GetValues<LoginResultCode>())
        {
            var serialized = _codec.SerializeLoginResponse(new OfficialLoginResponsePacket(
                code.ToString(),
                new Dictionary<string, byte[]>()));
            if (!serialized.Succeeded)
            {
                return RecoveryRequired(session, serialized.Error);
            }

            responses[code] = serialized.Value;
        }

        var login = await _authentication.LoginAsync(
            new LoginRequest(request.Value.Username, request.Value.Password, PacketEvidenceHash.Sha256Hex(packet.Payload.Span)),
            session,
            _characterList,
            cancellationToken);

        return new OfficialProtocolRuntimeResponse(CharacterOperationResultCode.Success, login.Session, responses[login.Code], []);
    }

    public async Task<OfficialProtocolRuntimeResponse> HandleCharacterListAsync(RuntimeSession session, PacketEnvelope packet, CancellationToken cancellationToken)
    {
        var request = _codec.DeserializeCharacterListRequest(packet);
        if (!request.Succeeded)
        {
            return RecoveryRequired(session, request.Error);
        }

        var characters = await _characterList.ListAsync(session, cancellationToken);
        var entries = characters
            .Select(character => new OfficialCharacterListEntryPacket(
                character.CharacterId,
                character.Name,
                character.Class,
                character.Gender,
                character.LifeSkill,
                character.Appearance,
                character.Level,
                character.MapId,
                character.PositionX,
                character.PositionY,
                character.Status,
                new Dictionary<string, byte[]>()))
            .ToArray();

        var response = _codec.SerializeCharacterListResponse(new OfficialCharacterListResponsePacket(entries, new Dictionary<string, byte[]>()));
        if (!response.Succeeded)
        {
            return RecoveryRequired(session, response.Error);
        }

        return new OfficialProtocolRuntimeResponse(CharacterOperationResultCode.Success, session, response.Value, []);
    }

    public async Task<OfficialProtocolRuntimeResponse> HandleCharacterCreateAsync(RuntimeSession session, PacketEnvelope packet, CancellationToken cancellationToken)
    {
        var request = _codec.DeserializeCharacterCreateRequest(packet);
        if (!request.Succeeded || request.Value is null)
        {
            return RecoveryRequired(session, request.Error);
        }

        if (request.Value.Name is null ||
            request.Value.Class is null ||
            request.Value.Gender is null ||
            request.Value.LifeSkill is null ||
            request.Value.Appearance is null)
        {
            return RecoveryRequired(session, new OperationError(
                "protocol.recovery_required",
                "Official CharacterCreate raw packet must prove name, class, gender, life skill, and appearance/template fields before runtime mutation.",
                OfficialLoginCharacterPacketKind.CharacterCreateRequest.ToString()));
        }

        var response = _codec.SerializeCharacterCreateResponse(new OfficialCharacterCreateResponsePacket(
            CharacterOperationResultCode.Success.ToString(),
            new Dictionary<string, byte[]>()));
        if (!response.Succeeded)
        {
            return RecoveryRequired(session, response.Error);
        }

        var created = await _characters.CreateAsync(
            session,
            new CharacterCreateRequest(
                request.Value.Name,
                request.Value.Class,
                request.Value.Gender,
                request.Value.LifeSkill,
                request.Value.Appearance,
                PacketEvidenceHash.Sha256Hex(packet.Payload.Span)),
            cancellationToken);

        if (!created.Succeeded || created.Character is null)
        {
            return new OfficialProtocolRuntimeResponse(created.Code, created.Session, ReadOnlyMemory<byte>.Empty, []);
        }

        return new OfficialProtocolRuntimeResponse(CharacterOperationResultCode.Success, created.Session, response.Value, []);
    }

    public async Task<OfficialProtocolRuntimeResponse> HandleCharacterDeleteAsync(RuntimeSession session, PacketEnvelope packet, CancellationToken cancellationToken)
    {
        var request = _codec.DeserializeCharacterDeleteRequest(packet);
        if (!request.Succeeded || request.Value is null)
        {
            return RecoveryRequired(session, request.Error);
        }

        if (request.Value.CharacterId is null)
        {
            return RecoveryRequired(session, new OperationError(
                "protocol.recovery_required",
                "Official CharacterDelete raw packet must prove whether the request carries CharacterId or Slot.",
                OfficialLoginCharacterPacketKind.CharacterDeleteRequest.ToString()));
        }

        var response = _codec.SerializeCharacterDeleteResponse(new OfficialCharacterDeleteResponsePacket(
            CharacterOperationResultCode.Success.ToString(),
            new Dictionary<string, byte[]>()));
        if (!response.Succeeded)
        {
            return RecoveryRequired(session, response.Error);
        }

        var deleted = await _characters.DeleteAsync(
            session,
            new CharacterDeleteRequest(request.Value.CharacterId.Value, PacketEvidenceHash.Sha256Hex(packet.Payload.Span)),
            cancellationToken);

        if (!deleted.Succeeded || deleted.Character is null)
        {
            return new OfficialProtocolRuntimeResponse(deleted.Code, deleted.Session, ReadOnlyMemory<byte>.Empty, []);
        }

        return new OfficialProtocolRuntimeResponse(CharacterOperationResultCode.Success, deleted.Session, response.Value, []);
    }

    public async Task<OfficialProtocolRuntimeResponse> HandleCharacterSelectAsync(RuntimeSession session, PacketEnvelope packet, CancellationToken cancellationToken)
    {
        var request = _codec.DeserializeCharacterSelectRequest(packet);
        if (!request.Succeeded || request.Value is null)
        {
            return RecoveryRequired(session, request.Error);
        }

        if (request.Value.CharacterId is null)
        {
            return RecoveryRequired(session, new OperationError(
                "protocol.recovery_required",
                "Official CharacterSelect raw packet must prove whether the request carries CharacterId or Slot.",
                OfficialLoginCharacterPacketKind.CharacterSelectRequest.ToString()));
        }

        var character = (await _characterList.ListAsync(session, cancellationToken))
            .SingleOrDefault(value => value.CharacterId == request.Value.CharacterId.Value);
        OperationResult<ReadOnlyMemory<byte>>? selectResponse = null;
        OperationResult<ReadOnlyMemory<byte>>? worldEntry = null;
        if (character is not null)
        {
            selectResponse = _codec.SerializeCharacterSelectResponse(new OfficialCharacterSelectResponsePacket(
                CharacterOperationResultCode.Success.ToString(),
                new Dictionary<string, byte[]>()));
            if (!selectResponse.Succeeded)
            {
                return RecoveryRequired(session, selectResponse.Error);
            }

            worldEntry = _codec.SerializeWorldEntryContext(new OfficialWorldEntryContextPacket(
                character.CharacterId,
                character.MapId,
                character.PositionX,
                character.PositionY,
                new Dictionary<string, byte[]>()));
            if (!worldEntry.Succeeded)
            {
                return RecoveryRequired(session, worldEntry.Error);
            }
        }

        var selected = await _characters.SelectAsync(
            session,
            new CharacterSelectRequest(request.Value.CharacterId.Value, PacketEvidenceHash.Sha256Hex(packet.Payload.Span)),
            cancellationToken);

        if (!selected.Succeeded || selected.Character is null)
        {
            return new OfficialProtocolRuntimeResponse(selected.Code, selected.Session, ReadOnlyMemory<byte>.Empty, []);
        }

        return new OfficialProtocolRuntimeResponse(
            CharacterOperationResultCode.Success,
            selected.Session,
            Concat(selectResponse!.Value, worldEntry!.Value),
            []);
    }

    private OfficialProtocolRuntimeResponse RecoveryRequired(RuntimeSession session, OperationError error)
    {
        var gaps = _codec.Coverage.Gaps
            .Select(gap => $"{gap.PacketKind}: {gap.RequiredEvidence}")
            .ToArray();
        return new OfficialProtocolRuntimeResponse(CharacterOperationResultCode.NeedsProtocolRecovery, session, ReadOnlyMemory<byte>.Empty, gaps.Length == 0 ? [error.Message] : gaps);
    }

    private static ReadOnlyMemory<byte> Concat(ReadOnlyMemory<byte> first, ReadOnlyMemory<byte> second)
    {
        var buffer = new byte[first.Length + second.Length];
        first.Span.CopyTo(buffer);
        second.Span.CopyTo(buffer.AsSpan(first.Length));
        return buffer;
    }
}
