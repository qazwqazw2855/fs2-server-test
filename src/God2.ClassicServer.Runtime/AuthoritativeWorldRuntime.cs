using System.Collections.Concurrent;
using God2.ClassicServer.Application.Common;
using God2.ClassicServer.Protocol;

namespace God2.ClassicServer.Runtime;

public interface IWorldSessionCoordinator
{
    WorldContentAuthorityKind AuthorityKind { get; }

    int ActiveBindingCount { get; }

    Task<OperationResult<WorldSessionBinding>> BindAsync(
        RuntimeSession session,
        CharacterSummary character,
        CancellationToken cancellationToken);

    OperationResult<WorldSessionBinding> GetBinding(string sessionId);

    OperationResult<WorldSessionBinding> UpdateSession(RuntimeSession session);

    bool Unbind(string sessionId);
}

public interface IWorldSessionTransitionCoordinator
{
    Task<OperationResult<WorldSessionBinding>> RebindAsync(
        RuntimeSession session,
        CharacterSummary character,
        CancellationToken cancellationToken);
}

public interface ICharacterImmortalProjectionSource
{
    Task<OperationResult<IReadOnlyList<OfficialOwnedImmortalWireState>>> LoadAsync(
        long characterId,
        CancellationToken cancellationToken);
}

public interface ICharacterGoldProjectionSource
{
    Task<OperationResult<long>> LoadGoldAsync(
        long characterId,
        CancellationToken cancellationToken);
}

public interface ICharacterInventoryProjectionSource
{
    Task<OperationResult<PlayerInventorySnapshot>> LoadInventoryAsync(
        long characterId,
        CancellationToken cancellationToken);
}

public sealed class EmptyCharacterGoldProjectionSource : ICharacterGoldProjectionSource
{
    public Task<OperationResult<long>> LoadGoldAsync(
        long characterId,
        CancellationToken cancellationToken) =>
        Task.FromResult(OperationResult<long>.Success(0));
}

public sealed class EmptyCharacterInventoryProjectionSource : ICharacterInventoryProjectionSource
{
    public Task<OperationResult<PlayerInventorySnapshot>> LoadInventoryAsync(
        long characterId,
        CancellationToken cancellationToken) =>
        Task.FromResult(OperationResult<PlayerInventorySnapshot>.Success(
            new PlayerInventorySnapshot(Guid.Empty, characterId, 32, 0, 0, "Clean", [])));
}

public sealed class EmptyCharacterImmortalProjectionSource : ICharacterImmortalProjectionSource
{
    public Task<OperationResult<IReadOnlyList<OfficialOwnedImmortalWireState>>> LoadAsync(
        long characterId,
        CancellationToken cancellationToken) =>
        Task.FromResult(OperationResult<IReadOnlyList<OfficialOwnedImmortalWireState>>.Success([]));
}

public sealed class WorldSessionCoordinator : IWorldSessionCoordinator, IWorldSessionTransitionCoordinator
{
    private readonly IWorldContentRepository _repository;
    private readonly IProductionMapRuntimeRegistry _mapRegistry;
    private readonly WorldSessionBinder _binder;
    private readonly ICharacterImmortalProjectionSource _immortalProjectionSource;
    private readonly ICharacterGoldProjectionSource _goldProjectionSource;
    private readonly ICharacterInventoryProjectionSource _inventoryProjectionSource;
    private readonly ConcurrentDictionary<string, WorldSessionBinding> _bindings = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<long, string> _characterOwners = [];

    public WorldSessionCoordinator(
        IWorldContentRepository repository,
        MapRuntimeFactory? factory = null,
        WorldSessionBinder? binder = null,
        IProductionMapRuntimeRegistry? mapRegistry = null,
        ICharacterImmortalProjectionSource? immortalProjectionSource = null,
        ICharacterGoldProjectionSource? goldProjectionSource = null,
        ICharacterInventoryProjectionSource? inventoryProjectionSource = null)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        var effectiveFactory = factory ?? new MapRuntimeFactory();
        _mapRegistry = mapRegistry ?? new ProductionMapRuntimeRegistry(repository, factory: effectiveFactory);
        _binder = binder ?? new WorldSessionBinder(effectiveFactory);
        _immortalProjectionSource = immortalProjectionSource ?? new EmptyCharacterImmortalProjectionSource();
        _goldProjectionSource = goldProjectionSource ?? new EmptyCharacterGoldProjectionSource();
        _inventoryProjectionSource = inventoryProjectionSource ?? new EmptyCharacterInventoryProjectionSource();
    }

    public WorldContentAuthorityKind AuthorityKind => _repository.AuthorityKind;

    public int ActiveBindingCount => _bindings.Count;

    public async Task<OperationResult<WorldSessionBinding>> BindAsync(
        RuntimeSession session,
        CharacterSummary character,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(character);
        if (!session.IsAuthenticated || session.AccountId is null || session.CharacterId != character.CharacterId ||
            session.AccountId != character.AccountId || session.ProtocolStage != ProtocolStage.WorldEntering)
        {
            return OperationResult<WorldSessionBinding>.Failure(
                "world_authority.session_invalid",
                "Authoritative world binding requires the matching authenticated WorldEntering session.",
                session.SessionId);
        }

        var initialized = await _mapRegistry.InitializeAsync(cancellationToken);
        if (!initialized.Succeeded)
        {
            return OperationResult<WorldSessionBinding>.Failure(
                initialized.Error.Code,
                initialized.Error.Message,
                initialized.Error.Source);
        }

        var resolvedMap = _mapRegistry.GetOrCreate(character.MapId);
        if (!resolvedMap.Succeeded || resolvedMap.Value is null)
        {
            return OperationResult<WorldSessionBinding>.Failure(
                resolvedMap.Error.Code,
                resolvedMap.Error.Message,
                resolvedMap.Error.Source);
        }

        var mapRuntime = resolvedMap.Value;
        var validationErrors = mapRuntime.Definition.ValidationErrors;

        if (validationErrors.Count != 0)
        {
            return OperationResult<WorldSessionBinding>.Failure(
                "world_authority.map_validation_failed",
                "The authoritative map contains quarantined validation issues and cannot accept a production player session.",
                character.MapId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        var immortals = await _immortalProjectionSource.LoadAsync(character.CharacterId, cancellationToken);
        if (!immortals.Succeeded || immortals.Value is null)
        {
            return OperationResult<WorldSessionBinding>.Failure(
                immortals.Error.Code,
                immortals.Error.Message,
                immortals.Error.Source);
        }

        var immortalValidation = OfficialImmortalReplicationWireCodec.Validate(immortals.Value);
        if (immortalValidation is not null)
        {
            return OperationResult<WorldSessionBinding>.Failure(
                "world_authority.immortal_projection_invalid",
                immortalValidation,
                character.CharacterId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        var gold = await _goldProjectionSource.LoadGoldAsync(character.CharacterId, cancellationToken);
        if (!gold.Succeeded || gold.Value is < 0 or > uint.MaxValue)
        {
            return OperationResult<WorldSessionBinding>.Failure(
                gold.Succeeded ? "world_authority.gold_projection_invalid" : gold.Error.Code,
                gold.Succeeded
                    ? "The authoritative Gold balance cannot be represented by the official Client wallet field."
                    : gold.Error.Message,
                gold.Succeeded
                    ? character.CharacterId.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    : gold.Error.Source);
        }

        var inventory = await _inventoryProjectionSource.LoadInventoryAsync(
            character.CharacterId,
            cancellationToken);
        if (!inventory.Succeeded || inventory.Value is null)
        {
            return OperationResult<WorldSessionBinding>.Failure(
                inventory.Error.Code,
                inventory.Error.Message,
                inventory.Error.Source);
        }

        var inventoryValidation = OfficialInventoryBootstrapWireCodec.Validate(inventory.Value);
        if (inventoryValidation is not null)
        {
            return OperationResult<WorldSessionBinding>.Failure(
                inventoryValidation,
                "The authoritative inventory cannot be represented by the recovered official Client bootstrap projection.",
                character.CharacterId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        if (!_characterOwners.TryAdd(character.CharacterId, session.SessionId))
        {
            return OperationResult<WorldSessionBinding>.Failure(
                "world_authority.character_already_bound",
                "The authoritative character is already attached to a world session.",
                character.CharacterId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        var bound = _binder.BindToRuntime(
            session,
            character,
            mapRuntime,
            WorldContentMode.MariaDbAuthoritative,
            validationErrors);
        if (!bound.Succeeded || bound.Value is null)
        {
            _characterOwners.TryRemove(
                new KeyValuePair<long, string>(character.CharacterId, session.SessionId));
            return bound;
        }


        var completedBinding = bound.Value with
        {
            OwnedImmortals = immortals.Value,
            InitialGoldBalance = gold.Value,
            InitialInventory = inventory.Value
        };

        if (!_bindings.TryAdd(session.SessionId, completedBinding))
        {
            RemoveFromMap(completedBinding);
            _characterOwners.TryRemove(
                new KeyValuePair<long, string>(character.CharacterId, session.SessionId));
            return OperationResult<WorldSessionBinding>.Failure(
                "world_authority.session_already_bound",
                "The authoritative world session already has an active binding.",
                session.SessionId);
        }

        return OperationResult<WorldSessionBinding>.Success(completedBinding);
    }

    public OperationResult<WorldSessionBinding> GetBinding(string sessionId) =>
        _bindings.TryGetValue(sessionId, out var binding)
            ? OperationResult<WorldSessionBinding>.Success(binding)
            : OperationResult<WorldSessionBinding>.Failure(
                "world_authority.session_not_bound",
                "The session is not attached to an authoritative MapRuntime.",
                sessionId);

    public OperationResult<WorldSessionBinding> UpdateSession(RuntimeSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        while (_bindings.TryGetValue(session.SessionId, out var binding))
        {
            if (binding.MapSession.CharacterId != session.CharacterId || binding.Session.AccountId != session.AccountId)
            {
                return OperationResult<WorldSessionBinding>.Failure(
                    "world_authority.session_identity_changed",
                    "The authoritative session identity changed after world binding.",
                    session.SessionId);
            }

            var updated = binding with { Session = session };
            if (_bindings.TryUpdate(session.SessionId, updated, binding))
            {
                return OperationResult<WorldSessionBinding>.Success(updated);
            }
        }

        return OperationResult<WorldSessionBinding>.Failure(
            "world_authority.session_not_bound",
            "The session is not attached to an authoritative MapRuntime.",
            session.SessionId);
    }

    public async Task<OperationResult<WorldSessionBinding>> RebindAsync(
        RuntimeSession session,
        CharacterSummary character,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(character);
        if (!_bindings.TryGetValue(session.SessionId, out var current) ||
            session.ProtocolStage != ProtocolStage.InWorld ||
            !session.IsAuthenticated ||
            session.CharacterId != character.CharacterId ||
            session.AccountId != character.AccountId ||
            current.MapSession.CharacterId != character.CharacterId)
        {
            return OperationResult<WorldSessionBinding>.Failure(
                "world_authority.rebind_identity_invalid",
                "Authoritative world rebinding requires the matching authenticated InWorld session.",
                session.SessionId);
        }

        if (!_characterOwners.TryGetValue(character.CharacterId, out var ownerSessionId) ||
            !string.Equals(ownerSessionId, session.SessionId, StringComparison.Ordinal))
        {
            return OperationResult<WorldSessionBinding>.Failure(
                "world_authority.character_owner_changed",
                "The authoritative character owner changed before the map transition.",
                character.CharacterId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        if (current.MapSession.MapId == character.MapId)
        {
            return OperationResult<WorldSessionBinding>.Failure(
                "world_authority.rebind_map_unchanged",
                "Authoritative world rebinding requires a different destination map.",
                character.MapId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        var initialized = await _mapRegistry.InitializeAsync(cancellationToken);
        if (!initialized.Succeeded)
        {
            return OperationResult<WorldSessionBinding>.Failure(
                initialized.Error.Code,
                initialized.Error.Message,
                initialized.Error.Source);
        }

        var resolvedMap = _mapRegistry.GetOrCreate(character.MapId);
        if (!resolvedMap.Succeeded || resolvedMap.Value is null)
        {
            return OperationResult<WorldSessionBinding>.Failure(
                resolvedMap.Error.Code,
                resolvedMap.Error.Message,
                resolvedMap.Error.Source);
        }

        var targetMap = resolvedMap.Value;

        if (targetMap.Definition.ValidationErrors.Count != 0)
        {
            return OperationResult<WorldSessionBinding>.Failure(
                "world_authority.map_validation_failed",
                "The portal destination contains quarantined validation issues.",
                character.MapId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        var rebound = _binder.BindToRuntime(
            session,
            character,
            targetMap,
            WorldContentMode.MariaDbAuthoritative,
            targetMap.Definition.ValidationErrors);
        if (!rebound.Succeeded || rebound.Value is null)
        {
            return rebound;
        }


        var completedRebinding = rebound.Value with
        {
            OwnedImmortals = current.OwnedImmortals,
            InitialGoldBalance = current.InitialGoldBalance,
            InitialInventory = current.InitialInventory
        };

        if (!_bindings.TryUpdate(session.SessionId, completedRebinding, current))
        {
            RemoveFromMap(completedRebinding);
            return OperationResult<WorldSessionBinding>.Failure(
                "world_authority.rebind_conflict",
                "The authoritative world binding changed during portal rebinding.",
                session.SessionId);
        }

        RemoveFromMap(current);
        return OperationResult<WorldSessionBinding>.Success(completedRebinding);
    }

    public bool Unbind(string sessionId)
    {
        if (!_bindings.TryRemove(sessionId, out var binding))
        {
            return false;
        }

        RemoveFromMap(binding);
        _characterOwners.TryRemove(
            new KeyValuePair<long, string>(binding.MapSession.CharacterId, binding.MapSession.SessionId));
        return true;
    }

    private static void RemoveFromMap(WorldSessionBinding binding)
    {
        var runtime = binding.MapRuntime;
        lock (runtime.ActiveSessions)
        {
            runtime.ActiveSessions.RemoveAll(session =>
                string.Equals(session.SessionId, binding.MapSession.SessionId, StringComparison.Ordinal));
        }

        runtime.Replication.ClearSession(binding.MapSession.SessionId);
        runtime.EntityRegistry.Remove(binding.MapSession.PlayerRuntimeEntityId);
        runtime.Objects.Remove(binding.MapSession.PlayerRuntimeEntityId);
        runtime.SpawnQueue.RemoveWhere(entity => entity.RuntimeEntityId == binding.MapSession.PlayerRuntimeEntityId);
        runtime.UpdateQueue.RemoveWhere(entity => entity.RuntimeEntityId == binding.MapSession.PlayerRuntimeEntityId);
        runtime.DespawnQueue.RemoveWhere(entity => entity.RuntimeEntityId == binding.MapSession.PlayerRuntimeEntityId);
        runtime.BroadcastEvents.RemoveWhere(entry => entry.RuntimeEntityId == binding.MapSession.PlayerRuntimeEntityId);
    }
}

public sealed record WorldBootstrapProjection(
    byte[] Bytes,
    string Projector,
    string EvidenceId,
    int RuntimeMapId,
    long PlayerRuntimeEntityId,
    ushort ClientMapId,
    byte ClientAreaId,
    ushort ClientX,
    ushort ClientY,
    bool LegacyWorldEntitiesExcluded = false,
    IReadOnlyList<long>? EmbeddedInitialNpcRuntimeObjectIds = null);

public interface IWorldBootstrapProjector
{
    OperationResult<WorldBootstrapProjection> Project(WorldSessionBinding binding);
}

public sealed class LegacyCompatibilityWorldBootstrapProjector : IWorldBootstrapProjector
{
    private readonly ClientMapIdentityResolver _mapIdentityResolver;
    private readonly bool _excludeLegacyWorldEntities;

    public LegacyCompatibilityWorldBootstrapProjector(
        ClientMapIdentityResolver? mapIdentityResolver = null,
        bool excludeLegacyWorldEntities = false)
    {
        _mapIdentityResolver = mapIdentityResolver ?? new ClientMapIdentityResolver();
        _excludeLegacyWorldEntities = excludeLegacyWorldEntities;
    }

    public OperationResult<WorldBootstrapProjection> Project(WorldSessionBinding binding)
    {
        ArgumentNullException.ThrowIfNull(binding);
        if (binding.MapSession.ContentMode != WorldContentMode.MariaDbAuthoritative)
        {
            return OperationResult<WorldBootstrapProjection>.Failure(
                "world_projection.authority_mode_invalid",
                "Legacy compatibility framing may only project an authoritative MariaDB world binding.",
                binding.MapSession.ContentMode.ToString());
        }

        var playerResult = binding.MapRuntime.Objects.Get(binding.MapSession.PlayerRuntimeEntityId);
        if (!playerResult.Succeeded || playerResult.Value is not PlayerObject player)
        {
            return OperationResult<WorldBootstrapProjection>.Failure(
                "world_projection.player_not_bound",
                "The authoritative player runtime object is unavailable for projection.",
                binding.Session.SessionId);
        }

        if (player.State.CharacterId != binding.Character.CharacterId ||
            player.State.MapId != binding.MapSession.MapId ||
            player.State.Position.X != binding.Character.PositionX ||
            player.State.Position.Y != binding.Character.PositionY)
        {
            return OperationResult<WorldBootstrapProjection>.Failure(
                "world_projection.player_state_mismatch",
                "The runtime player state does not match the authoritative character snapshot.",
                binding.Session.SessionId);
        }

        var clientMap = _mapIdentityResolver.Resolve(
            binding.MapRuntime.Definition,
            player.State.Position,
            OfficialPortalWireCodec.ClientBuildId);
        if (!clientMap.Succeeded || clientMap.Value is null)
        {
            return OperationResult<WorldBootstrapProjection>.Failure(
                clientMap.Error.Code,
                clientMap.Error.Message,
                clientMap.Error.Source);
        }

        var authoritativeCharacter = binding.Character with
        {
            Name = player.State.Name,
            Class = player.State.Class,
            Gender = player.State.Gender,
            Level = player.State.Level,
            Appearance = player.State.Appearance,
            MapId = clientMap.Value.ClientMapId,
            PositionX = clientMap.Value.ClientX,
            PositionY = clientMap.Value.ClientY
        };

        try
        {
            var embeddedNpcs = _excludeLegacyWorldEntities
                ? binding.MapRuntime.Objects
                    .OfType<NpcObject>()
                    .Where(npc => OfficialNpcReplicationWireCodec.Validate(npc.State) is null)
                    .OrderBy(npc => npc.State.WireIdentity!.EntityHandle)
                    .ToArray()
                : [];
            var bytes = _excludeLegacyWorldEntities
                ? OfficialClientWorldProtocolFrames.BuildMariaDbAuthoritativeWorldBootstrapAfterHandshake(
                    authoritativeCharacter,
                    clientMap.Value.ClientAreaId,
                    embeddedNpcs.Select(npc => npc.State).ToArray(),
                    binding.OwnedImmortals ?? [])
                : OfficialClientWorldProtocolFrames.BuildWorldBootstrapAfterHandshake(
                    authoritativeCharacter,
                    clientMap.Value.ClientAreaId);
            if (_excludeLegacyWorldEntities)
            {
                var walletFrame = OfficialClientWorldProtocolFrames.BuildGoldBalanceFrame(
                    checked((uint)binding.InitialGoldBalance));
                var bootstrapWithWallet = new byte[bytes.Length + walletFrame.Length];
                bytes.CopyTo(bootstrapWithWallet, 0);
                walletFrame.CopyTo(bootstrapWithWallet, bytes.Length);
                Array.Clear(bytes);
                Array.Clear(walletFrame);
                bytes = bootstrapWithWallet;

                var inventoryProjection = OfficialInventoryBootstrapWireCodec.Serialize(
                    OfficialInventoryBootstrapWireCodec.ClientBuildId,
                    binding.InitialInventory ?? new PlayerInventorySnapshot(
                        Guid.Empty,
                        binding.Character.CharacterId,
                        32,
                        0,
                        0,
                        "Clean",
                        []),
                    checked((uint)binding.InitialGoldBalance));
                if (!inventoryProjection.Succeeded)
                {
                    return OperationResult<WorldBootstrapProjection>.Failure(
                        inventoryProjection.FailureCode,
                        "The authoritative inventory bootstrap projection is not supported by exact-build evidence.",
                        binding.Character.CharacterId.ToString(System.Globalization.CultureInfo.InvariantCulture));
                }

                foreach (var decodedFrame in inventoryProjection.DecodedFrames)
                {
                    var encodedFrame = OfficialClientWorldProtocolFrames.EncodeWorldServerPayload(decodedFrame.Span);
                    var bootstrapWithInventory = new byte[bytes.Length + encodedFrame.Length];
                    bytes.CopyTo(bootstrapWithInventory, 0);
                    encodedFrame.CopyTo(bootstrapWithInventory, bytes.Length);
                    Array.Clear(bytes);
                    Array.Clear(encodedFrame);
                    bytes = bootstrapWithInventory;
                }
            }

            return OperationResult<WorldBootstrapProjection>.Success(new WorldBootstrapProjection(
                bytes,
                _excludeLegacyWorldEntities
                    ? "MariaDbAuthoritativeWorldBootstrapProjector"
                    : nameof(LegacyCompatibilityWorldBootstrapProjector),
                OfficialClientWorldProtocolFrames.EvidenceId,
                binding.MapSession.MapId,
                binding.MapSession.PlayerRuntimeEntityId,
                clientMap.Value.ClientMapId,
                clientMap.Value.ClientAreaId,
                clientMap.Value.ClientX,
                clientMap.Value.ClientY,
                _excludeLegacyWorldEntities,
                embeddedNpcs.Select(npc => npc.Identity.RuntimeObjectId).ToArray()));
        }
        catch (Exception exception) when (exception is ArgumentException or OverflowException or InvalidOperationException)
        {
            return OperationResult<WorldBootstrapProjection>.Failure(
                "world_projection.character_not_serializable",
                "The authoritative runtime player cannot be represented by the recovered compatibility projector.",
                exception.GetType().Name);
        }
    }
}
