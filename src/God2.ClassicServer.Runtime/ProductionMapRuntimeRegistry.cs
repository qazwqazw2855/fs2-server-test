using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using God2.ClassicServer.Application.Common;
using God2.ClassicServer.Application.Contracts;

namespace God2.ClassicServer.Runtime;

public sealed record MapRuntimeKey(int MapId, string InstanceId);

public sealed record ProductionMapRuntimeStatus(
    bool IsInitialized,
    string SnapshotIdentity,
    int CatalogMapCount,
    int ActiveMapRuntimeCount,
    int EnabledNpcSpawnCount,
    int EnabledMonsterSpawnCount,
    int EnabledPortalCount,
    int ValidationIssueCount,
    WorldContentAuthorityKind AuthorityKind,
    int QuarantinedCatalogIssueCount,
    int BlockingActiveMapIssueCount,
    int PendingInitialSpawnCount);

public interface IProductionMapRuntimeRegistry
{
    const string DefaultInstanceId = "world:default";

    ProductionMapRuntimeStatus Status { get; }

    Task<OperationResult> InitializeAsync(CancellationToken cancellationToken);

    OperationResult<MapRuntime> GetOrCreate(int mapId, string instanceId = DefaultInstanceId);

    OperationResult<MapRuntime> Get(int mapId, string instanceId = DefaultInstanceId);
}

/// <summary>
/// Owns the finite set of production map instances backed by one immutable MariaDB snapshot.
/// Only the default world instance is currently evidence-backed, which makes the key space
/// bounded by the published map catalog and prevents arbitrary instance-id growth.
/// </summary>
public sealed class ProductionMapRuntimeRegistry : IProductionMapRuntimeRegistry, IRuntimeCacheBuilder
{
    private readonly IWorldContentRepository _repository;
    private readonly IProductionGameplayContentAuthority? _contentAuthority;
    private readonly MapRuntimeFactory _factory;
    private readonly SemaphoreSlim _initializationGate = new(1, 1);
    private readonly ConcurrentDictionary<MapRuntimeKey, Lazy<OperationResult<MapRuntimeCreationResult>>> _maps = [];
    private WorldContentSnapshot? _snapshot;
    private string _snapshotIdentity = string.Empty;

    public ProductionMapRuntimeRegistry(
        IWorldContentRepository repository,
        IProductionGameplayContentAuthority? contentAuthority = null,
        MapRuntimeFactory? factory = null)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _contentAuthority = contentAuthority;
        _factory = factory ?? new MapRuntimeFactory();
        if (_repository.AuthorityKind != WorldContentAuthorityKind.MariaDb && contentAuthority is not null)
        {
            throw new InvalidOperationException("Production MapRuntime registry requires MariaDB content authority.");
        }
    }

    public ProductionMapRuntimeStatus Status
    {
        get
        {
            var snapshot = Volatile.Read(ref _snapshot);
            return snapshot is null
                ? new ProductionMapRuntimeStatus(
                    false,
                    string.Empty,
                    0,
                    _maps.Count,
                    0,
                    0,
                    0,
                    0,
                    _repository.AuthorityKind,
                    0,
                    0,
                    0)
                : new ProductionMapRuntimeStatus(
                    true,
                    Volatile.Read(ref _snapshotIdentity),
                    snapshot.Maps.Count,
                    _maps.Count,
                    snapshot.Maps.Values.Sum(map => map.NpcPlacements.Count(placement => placement.Enabled)),
                    snapshot.Maps.Values.Sum(map => map.MonsterSpawns.Count(spawn => spawn.Enabled)),
                    snapshot.Maps.Values.Sum(map => map.Portals.Count(portal => portal.Enabled)),
                    snapshot.ValidationErrors.Count + snapshot.Maps.Values.Sum(map => map.ValidationErrors.Count),
                    _repository.AuthorityKind,
                    snapshot.ValidationErrors.Count,
                    _maps.Values
                        .Where(value => value.IsValueCreated && value.Value.Succeeded && value.Value.Value is not null)
                        .Sum(value => value.Value.Value!.ValidationErrors.Count),
                    _maps.Values
                        .Where(value => value.IsValueCreated && value.Value.Succeeded && value.Value.Value is not null)
                        .Sum(value => value.Value.Value!.MapRuntime.SpawnQueue.Count));
        }
    }

    public async Task<OperationResult> BuildAsync(
        IReadOnlyList<StaticDataLoadCount> staticData,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(staticData);
        return await InitializeAsync(cancellationToken);
    }

    public async Task<OperationResult> InitializeAsync(CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref _snapshot) is not null)
        {
            return OperationResult.Success;
        }

        await _initializationGate.WaitAsync(cancellationToken);
        try
        {
            if (Volatile.Read(ref _snapshot) is not null)
            {
                return OperationResult.Success;
            }

            if (_repository.AuthorityKind != WorldContentAuthorityKind.MariaDb && _contentAuthority is not null)
            {
                return OperationResult.Failure(
                    "world_registry.authority_invalid",
                    "Production MapRuntime initialization requires a MariaDB world-content repository.",
                    _repository.AuthorityKind.ToString());
            }

            if (_contentAuthority is not null)
            {
                var release = _contentAuthority.RequireReady();
                if (!release.Succeeded)
                {
                    return OperationResult.Failure(release.Error.Code, release.Error.Message, release.Error.Source);
                }
            }

            WorldContentSnapshot snapshot;
            try
            {
                snapshot = await _repository.LoadAsync(cancellationToken);
            }
            catch (Exception exception) when (exception is InvalidOperationException or IOException or TimeoutException)
            {
                return OperationResult.Failure(
                    "world_registry.snapshot_load_failed",
                    "The MariaDB world-content snapshot could not be initialized.",
                    exception.GetType().Name);
            }

            if (snapshot.Maps.Count == 0)
            {
                return OperationResult.Failure(
                    "world_registry.snapshot_empty",
                    "The MariaDB world-content snapshot contains no maps.",
                    nameof(ProductionMapRuntimeRegistry));
            }

            snapshot = FreezeSnapshot(snapshot);
            var duplicateOrInvalid = ValidateSnapshot(snapshot);
            if (duplicateOrInvalid is not null)
            {
                return OperationResult.Failure(
                    "world_registry.snapshot_identity_invalid",
                    duplicateOrInvalid,
                    nameof(ProductionMapRuntimeRegistry));
            }

            var identity = ComputeSnapshotIdentity(snapshot);
            Volatile.Write(ref _snapshotIdentity, identity);
            Volatile.Write(ref _snapshot, snapshot);
            return OperationResult.Success;
        }
        finally
        {
            _initializationGate.Release();
        }
    }

    public OperationResult<MapRuntime> GetOrCreate(
        int mapId,
        string instanceId = IProductionMapRuntimeRegistry.DefaultInstanceId)
    {
        var snapshot = Volatile.Read(ref _snapshot);
        if (snapshot is null)
        {
            return OperationResult<MapRuntime>.Failure(
                "world_registry.not_initialized",
                "The production MapRuntime registry has not published its MariaDB snapshot.",
                mapId.ToString(CultureInfo.InvariantCulture));
        }

        if (!string.Equals(instanceId, IProductionMapRuntimeRegistry.DefaultInstanceId, StringComparison.Ordinal))
        {
            return OperationResult<MapRuntime>.Failure(
                "world_registry.instance_not_promoted",
                "Only the build-evidenced default world instance may be created in production.",
                instanceId);
        }

        if (!snapshot.Maps.ContainsKey(mapId))
        {
            return OperationResult<MapRuntime>.Failure(
                "world_content.map_not_found",
                "The requested map is absent from the published MariaDB snapshot.",
                mapId.ToString(CultureInfo.InvariantCulture));
        }

        var key = new MapRuntimeKey(mapId, instanceId);
        var lazy = _maps.GetOrAdd(
            key,
            _ => new Lazy<OperationResult<MapRuntimeCreationResult>>(
                () => CreateAndActivate(snapshot, mapId, instanceId),
                LazyThreadSafetyMode.ExecutionAndPublication));
        var created = lazy.Value;
        if (!created.Succeeded || created.Value is null)
        {
            _maps.TryRemove(new KeyValuePair<MapRuntimeKey, Lazy<OperationResult<MapRuntimeCreationResult>>>(key, lazy));
            return OperationResult<MapRuntime>.Failure(created.Error.Code, created.Error.Message, created.Error.Source);
        }

        return OperationResult<MapRuntime>.Success(created.Value.MapRuntime);
    }

    public OperationResult<MapRuntime> Get(
        int mapId,
        string instanceId = IProductionMapRuntimeRegistry.DefaultInstanceId)
    {
        var key = new MapRuntimeKey(mapId, instanceId);
        if (!_maps.TryGetValue(key, out var lazy) || !lazy.IsValueCreated)
        {
            return OperationResult<MapRuntime>.Failure(
                "world_registry.map_not_loaded",
                "The requested production MapRuntime has not been created.",
                $"{mapId.ToString(CultureInfo.InvariantCulture)}:{instanceId}");
        }

        var result = lazy.Value;
        return result.Succeeded && result.Value is not null
            ? OperationResult<MapRuntime>.Success(result.Value.MapRuntime)
            : OperationResult<MapRuntime>.Failure(result.Error.Code, result.Error.Message, result.Error.Source);
    }

    private static string? ValidateSnapshot(WorldContentSnapshot snapshot)
    {
        foreach (var pair in snapshot.Maps)
        {
            var map = pair.Value;
            if (map.MapId <= 0)
            {
                return "A published map has an invalid non-positive identity.";
            }

            if (pair.Key != map.MapId)
            {
                return $"Published map key {pair.Key.ToString(CultureInfo.InvariantCulture)} does not match MapId {map.MapId.ToString(CultureInfo.InvariantCulture)}.";
            }

            if (map.NpcPlacements.Select(placement => placement.PlacementId).Distinct().Count() != map.NpcPlacements.Count)
            {
                return $"Map {map.MapId.ToString(CultureInfo.InvariantCulture)} contains duplicate NPC SpawnId values.";
            }

            if (map.MonsterSpawns.Select(spawn => spawn.SpawnId).Distinct().Count() != map.MonsterSpawns.Count)
            {
                return $"Map {map.MapId.ToString(CultureInfo.InvariantCulture)} contains duplicate Monster SpawnId values.";
            }

            if (map.Portals.Select(portal => portal.PortalId).Distinct().Count() != map.Portals.Count)
            {
                return $"Map {map.MapId.ToString(CultureInfo.InvariantCulture)} contains duplicate PortalId values.";
            }
        }

        if (snapshot.Maps.Values.SelectMany(map => map.NpcPlacements)
            .Select(placement => placement.PlacementId).Distinct().Count() !=
            snapshot.Maps.Values.Sum(map => map.NpcPlacements.Count))
        {
            return "The published snapshot contains a duplicate global NPC SpawnId.";
        }

        if (snapshot.Maps.Values.SelectMany(map => map.MonsterSpawns)
            .Select(spawn => spawn.SpawnId).Distinct().Count() !=
            snapshot.Maps.Values.Sum(map => map.MonsterSpawns.Count))
        {
            return "The published snapshot contains a duplicate global Monster SpawnId.";
        }

        if (snapshot.Maps.Values.SelectMany(map => map.Portals)
            .Select(portal => portal.PortalId).Distinct().Count() !=
            snapshot.Maps.Values.Sum(map => map.Portals.Count))
        {
            return "The published snapshot contains a duplicate global PortalId.";
        }

        return null;
    }

    private static WorldContentSnapshot FreezeSnapshot(WorldContentSnapshot source)
    {
        var maps = source.Maps.ToDictionary(
            pair => pair.Key,
            pair => pair.Value with
            {
                SpawnPoints = Array.AsReadOnly(pair.Value.SpawnPoints.ToArray()),
                NpcPlacements = Array.AsReadOnly(pair.Value.NpcPlacements.ToArray()),
                MonsterSpawns = Array.AsReadOnly(pair.Value.MonsterSpawns.ToArray()),
                Portals = Array.AsReadOnly(pair.Value.Portals.ToArray()),
                MerchantMappings = Array.AsReadOnly(pair.Value.MerchantMappings
                    .Select(merchant => merchant with
                    {
                        Items = Array.AsReadOnly(merchant.RuntimeItems.ToArray())
                    })
                    .ToArray()),
                ValidationErrors = Array.AsReadOnly(pair.Value.ValidationErrors.ToArray())
            });
        return new WorldContentSnapshot(
            new ReadOnlyDictionary<int, MapDefinition>(maps),
            Array.AsReadOnly(source.ValidationErrors.ToArray()));
    }

    private OperationResult<MapRuntimeCreationResult> CreateAndActivate(
        WorldContentSnapshot snapshot,
        int mapId,
        string instanceId)
    {
        var created = _factory.Create(snapshot, mapId, instanceId);
        if (!created.Succeeded || created.Value is null)
        {
            return created;
        }

        var activated = created.Value.MapRuntime.ConsumeInitialSpawnQueue();
        if (!activated.Succeeded)
        {
            return OperationResult<MapRuntimeCreationResult>.Failure(
                activated.Error.Code,
                activated.Error.Message,
                activated.Error.Source);
        }

        return created;
    }

    private static string ComputeSnapshotIdentity(WorldContentSnapshot snapshot)
    {
        var canonical = string.Join(
            '\n',
            snapshot.Maps.Values
                .OrderBy(map => map.MapId)
                .Select(map => string.Join(
                    '|',
                    map.MapId.ToString(CultureInfo.InvariantCulture),
                    map.RegionId ?? string.Empty,
                    map.Bounds.MinX.ToString(CultureInfo.InvariantCulture),
                    map.Bounds.MinY.ToString(CultureInfo.InvariantCulture),
                    map.Bounds.MaxX.ToString(CultureInfo.InvariantCulture),
                    map.Bounds.MaxY.ToString(CultureInfo.InvariantCulture),
                    map.ClientIdentity?.ClientBuildId ?? string.Empty,
                    map.ClientIdentity?.ClientMapId.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                    string.Join(',', map.NpcPlacements.Where(value => value.Enabled).Select(value => value.PlacementId).Order()),
                    string.Join(',', map.MonsterSpawns.Where(value => value.Enabled).Select(value => value.SpawnId).Order()),
                    string.Join(',', map.Portals.Where(value => value.Enabled).Select(value => value.PortalId).Order()))));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }
}
