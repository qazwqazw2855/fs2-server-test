using God2.ClassicServer.Application.Common;
using God2.ClassicServer.Protocol;
using God2.ClassicServer.Runtime;

namespace God2.ClassicServer.Persistence;

public sealed class MariaDbOfficialPortalMapIdentitySource : IOfficialPortalMapIdentitySource
{
    private readonly MariaDbStaticDataLoader _loader;

    public MariaDbOfficialPortalMapIdentitySource(MariaDbStaticDataLoader loader)
    {
        _loader = loader ?? throw new ArgumentNullException(nameof(loader));
    }

    public OperationResult<OfficialPortalMapRuntimeIdentity> ResolveRuntimeMap(int runtimeMapId) =>
        Resolve(identity => identity.MapId == runtimeMapId, runtimeMapId.ToString(System.Globalization.CultureInfo.InvariantCulture));

    public OperationResult<OfficialPortalMapRuntimeIdentity> ResolveClientMap(ushort clientMapId, byte clientAreaId) =>
        Resolve(
            identity => identity.ClientMapId == clientMapId && identity.ClientAreaId == clientAreaId,
            $"{clientMapId}:{clientAreaId}");

    private OperationResult<OfficialPortalMapRuntimeIdentity> Resolve(
        Func<ClientMapIdentityStaticData, bool> predicate,
        string requestedIdentity)
    {
        var snapshot = _loader.PublishedSnapshot;
        var matches = snapshot.ClientMapIdentities.Values.Where(predicate).ToArray();
        if (matches.Length != 1)
        {
            return OperationResult<OfficialPortalMapRuntimeIdentity>.Failure(
                matches.Length == 0 ? "wire.portal.map_identity_missing" : "wire.portal.map_identity_ambiguous",
                "The MariaDB runtime catalog did not contain exactly one promoted map identity for the portal slice.",
                requestedIdentity);
        }

        var identity = matches[0];
        if (!identity.ProductionEnabled ||
            !IsRuntimePromotedStatus(identity.IdentityEvidenceStatus) ||
            !IsRuntimePromotedStatus(identity.CoordinateEvidenceStatus) ||
            !snapshot.Maps.TryGetValue(identity.MapId, out var map) ||
            !OfficialClientWorldCoordinateGrid.TryCreateBounds(map.Width, map.Height, out var bounds) ||
            !SameResourceIdentity(identity.ResourceIdentity, map.ResourceIdentity))
        {
            return OperationResult<OfficialPortalMapRuntimeIdentity>.Failure(
                "wire.portal.map_identity_not_promoted",
                "The MariaDB map identity did not pass the production, coordinate, resource, or bounds gate.",
                requestedIdentity);
        }

        return OperationResult<OfficialPortalMapRuntimeIdentity>.Success(new OfficialPortalMapRuntimeIdentity(
            identity.MapId,
            identity.ClientMapId,
            identity.ClientAreaId,
            identity.ResourceIdentity,
            bounds,
            identity.EvidenceReference));
    }

    private static bool SameResourceIdentity(string expected, string? actual) =>
        !string.IsNullOrWhiteSpace(actual) &&
        string.Equals(
            expected.Replace('\\', '/').TrimStart('/'),
            actual.Replace('\\', '/').TrimStart('/'),
            StringComparison.OrdinalIgnoreCase);

    private static bool IsRuntimePromotedStatus(string value) =>
        value is "Verified" or "Derived" ||
        value.StartsWith("canonical-", StringComparison.Ordinal) ||
        value.StartsWith("formal-runtime-", StringComparison.Ordinal);
}

public sealed class MariaDbWorldContentRepository : IWorldContentRepository
{
    private readonly MariaDbStaticDataLoader _loader;

    public MariaDbWorldContentRepository(MariaDbStaticDataLoader loader)
    {
        _loader = loader;
    }

    public WorldContentAuthorityKind AuthorityKind => WorldContentAuthorityKind.MariaDb;

    public Task<WorldContentSnapshot> LoadAsync(CancellationToken cancellationToken)
    {
        var snapshot = _loader.PublishedSnapshot;
        return Task.FromResult(Build(snapshot));
    }

    public static WorldContentSnapshot Build(FormalRuntimeStaticSnapshot snapshot)
    {
        var validation = new List<string>();
        var maps = new Dictionary<int, MapDefinition>();
        var mapIds = snapshot.Maps.Keys.ToHashSet();
        var merchantIds = snapshot.Merchants.Keys.ToHashSet();
        var npcMapper = new NpcContentMapper();
        var monsterMapper = new MonsterContentMapper();
        var portalMapper = new PortalContentMapper();
        var merchantMapper = new MerchantContentMapper();
        var validator = new RuntimeContentValidator();

        var promotedNpcTemplateIds = snapshot.NpcSpawns.Values
            .Select(spawn => spawn.NpcId)
            .ToHashSet();
        var legacyNpcDefinitions = snapshot.Npcs.Values
            .Where(npc => !promotedNpcTemplateIds.Contains(npc.Id))
            .Select(npc => npcMapper.Map(new NpcDatabaseRecord(
                npc.Id,
                npc.Code,
                npc.Name,
                npc.MapId,
                npc.PositionX,
                npc.PositionY,
                WorldDirection.Unknown,
                "Unknown",
                "Unknown",
                snapshot.Merchants.Values.FirstOrDefault(merchant => merchant.NpcId == npc.Id)?.Id,
                Enabled: npc.MapId is not null && npc.PositionX is not null && npc.PositionY is not null,
                RawMetadata: $"{{\"Code\":\"{EscapeJson(npc.Code)}\"}}",
                Source: $"MariaDB:npcs.Id={npc.Id}",
                ContentVersion: "mariadb-runtime-content-v1")))
            .ToArray();
        var promotedNpcDefinitions = snapshot.NpcSpawns.Values
            .Select(spawn => npcMapper.Map(new NpcDatabaseRecord(
                spawn.NpcId,
                spawn.Code,
                spawn.Name,
                spawn.MapId,
                spawn.PositionX,
                spawn.PositionY,
                Direction(spawn.Direction),
                spawn.ResourceKey,
                spawn.InteractionFamily,
                snapshot.Merchants.Values.FirstOrDefault(merchant => merchant.NpcId == spawn.NpcId)?.Id,
                Enabled: spawn.ProductionEnabled &&
                    IsRuntimePromotedStatus(spawn.IdentityEvidenceStatus) &&
                    IsRuntimePromotedStatus(spawn.CoordinateEvidenceStatus),
                RawMetadata:
                    $"{{\"NpcSpawnId\":{spawn.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)}," +
                    $"\"ObservedClientEntityHandle\":{spawn.ObservedClientEntityHandle?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "null"}," +
                    $"\"ClientBuildId\":\"{EscapeJson(spawn.ClientBuildId)}\",\"ResourceKey\":\"{EscapeJson(spawn.ResourceKey)}\"," +
                    $"\"ServiceEvidenceStatus\":\"{EscapeJson(spawn.ServiceEvidenceStatus)}\"}}",
                Source: $"MariaDB:npc_spawns.Id={spawn.Id}->npcs.Id={spawn.NpcId};{spawn.EvidenceReference}",
                ContentVersion: "mariadb-roadmap-m2-content-v1",
                PlacementId: spawn.Id,
                SpawnCondition: spawn.SpawnCondition,
                WireIdentity: NpcWireIdentity(spawn))))
            .ToArray();
        var npcDefinitions = legacyNpcDefinitions.Concat(promotedNpcDefinitions).ToArray();
        var validatedNpcs = validator.ValidateNpcs(npcDefinitions, mapIds, merchantIds);

        var monsterMappingIssues = new List<ContentValidationIssue>();
        var monsterDefinitions = new List<MonsterSpawnDefinition>();
        foreach (var spawn in snapshot.Spawns.Values.Where(spawn => spawn.ProductionEnabled))
        {
            var monster = snapshot.Monsters.GetValueOrDefault(spawn.MonsterId);
            if (monster is null)
            {
                monsterMappingIssues.Add(new ContentValidationIssue(
                    "content.missing_monster_reference",
                    RuntimeObjectKind.Monster,
                    spawn.MonsterId,
                    spawn.MapId,
                    ContentValidationStatus.Quarantined,
                    "Production monster spawn references a missing monster template.",
                    $"MariaDB:spawns.Id={spawn.Id}"));
                continue;
            }

            var mapped = monsterMapper.TryMap(new MonsterSpawnDatabaseRecord(
                checked((int)(spawn.Id & 0x7FFFFFFF)),
                monster.Id,
                monster.Code,
                monster.Name,
                spawn.MapId,
                spawn.PositionX,
                spawn.PositionY,
                WorldDirection.Unknown,
                TimeSpan.FromSeconds(spawn.RespawnSeconds),
                spawn.SpawnRadius ?? -1,
                spawn.SpawnCount ?? 0,
                Enabled: spawn.ProductionEnabled && IsRuntimePromotedStatus(spawn.EvidenceStatus),
                RawMetadata: $"{{\"SpawnId\":{spawn.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)},\"MonsterCode\":\"{EscapeJson(monster.Code)}\",\"EvidenceStatus\":\"{EscapeJson(spawn.EvidenceStatus)}\"}}",
                Source: $"MariaDB:spawns.Id={spawn.Id}->monsters.Id={monster.Id}",
                ContentVersion: "mariadb-runtime-content-v2",
                Level: monster.Level,
                MaximumHp: monster.MaxHp,
                AttackPower: monster.Attack,
                Defense: monster.Defense,
                MaximumMp: monster.MaxMp,
                MagicAttackPower: monster.MagicAttack,
                MagicDefense: monster.MagicDefense,
                Metal: monster.Metal,
                Wood: monster.Wood,
                Water: monster.Water,
                Fire: monster.Fire,
                Earth: monster.Earth));
            if (mapped.Succeeded && mapped.Value is not null)
            {
                monsterDefinitions.Add(mapped.Value);
            }
            else
            {
                monsterMappingIssues.Add(new ContentValidationIssue(
                    mapped.Error.Code,
                    RuntimeObjectKind.Monster,
                    monster.Id,
                    spawn.MapId,
                    ContentValidationStatus.Quarantined,
                    mapped.Error.Message,
                    mapped.Error.Source));
            }
        }

        var baseValidatedMonsters = validator.ValidateMonsters(monsterDefinitions, mapIds);
        var validatedMonsters = baseValidatedMonsters with
        {
            Issues = baseValidatedMonsters.Issues.Concat(monsterMappingIssues).ToArray()
        };

        var portalDefinitions = snapshot.Portals.Values
            .Select(portal => portalMapper.Map(new PortalDatabaseRecord(
                portal.Id,
                portal.Name,
                portal.SourceMapId,
                portal.SourceX,
                portal.SourceY,
                portal.TargetMapId,
                portal.TargetX,
                portal.TargetY,
                portal.SourceRadius > 0 ? "MovementRegion" : "ExplicitActivation",
                "Unknown",
                Enabled: portal.SourceMapId is not null &&
                    portal.SourceX is not null &&
                    portal.SourceY is not null &&
                    portal.TargetMapId is not null &&
                    portal.TargetX is not null &&
                    portal.TargetY is not null,
                RawMetadata: $"{{\"Name\":\"{EscapeJson(portal.Name)}\"}}",
                Source: $"MariaDB:portals.Id={portal.Id}",
                ContentVersion: "mariadb-runtime-content-v1",
                SourceRadius: portal.SourceRadius)))
            .ToArray();
        var validatedPortals = validator.ValidatePortals(portalDefinitions, mapIds);

        var merchantDefinitions = snapshot.Merchants.Values
            .Select(merchant => merchantMapper.Map(new MerchantDatabaseRecord(
                merchant.Id,
                merchant.NpcId,
                merchant.Name,
                "Unknown",
                "Unknown",
                snapshot.MerchantItems.Values
                    .Where(item => item.MerchantId == merchant.Id)
                    .Select(item => new MerchantItemDefinition(
                        item.ItemId,
                        item.Price,
                        $"{{\"MerchantId\":{item.MerchantId.ToString(System.Globalization.CultureInfo.InvariantCulture)}}}",
                        item.PurchasingPrice))
                    .ToArray(),
                Enabled: merchant.NpcId is not null,
                RawMetadata: $"{{\"Name\":\"{EscapeJson(merchant.Name)}\"}}",
                Source: $"MariaDB:merchants.Id={merchant.Id}",
                ContentVersion: "mariadb-runtime-content-v1")))
            .ToArray();
        var validatedMerchants = validator.ValidateMerchants(
            merchantDefinitions,
            validatedNpcs.Valid.Select(npc => npc.TemplateId).ToHashSet());

        foreach (var map in snapshot.Maps.Values)
        {
            snapshot.ClientMapIdentities.TryGetValue(
                $"{map.Id}\u001f{OfficialPortalWireCodec.ClientBuildId}",
                out var clientMapRecord);
            var clientIdentity = clientMapRecord is null ? null : MapClientIdentity(clientMapRecord);
            var npcPlacements = validatedNpcs.Valid
                .Where(npc => npc.MapId == map.Id)
                .ToArray();

            var monsterSpawns = validatedMonsters.Valid
                .Where(spawn => spawn.MapId == map.Id)
                .ToArray();

            var portals = validatedPortals.Valid
                .Where(portal => portal.Source.MapId == map.Id)
                .ToArray();

            var merchantMappings = validatedMerchants.Valid
                .Where(merchant => npcPlacements.Any(npc => npc.TemplateId == merchant.NpcTemplateId))
                .ToArray();

            var mapValidation = IssuesForMap(map.Id, validatedNpcs.Issues, validatedMonsters.Issues, validatedPortals.Issues)
                .ToList();
            if (!OfficialClientWorldCoordinateGrid.TryCreateBounds(map.Width, map.Height, out var mapBounds))
            {
                mapValidation.Add(
                    $"Official Client coordinate bounds are not proven: maps.Id={map.Id}; " +
                    $"gridWidth={map.Width?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "NULL"}; " +
                    $"gridHeight={map.Height?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "NULL"}; " +
                    $"evidence={OfficialClientWorldCoordinateGrid.ConsumerEvidence}.");
            }

            if (clientIdentity is { ProductionEnabled: true } &&
                !SameResourceIdentity(clientIdentity.ResourceIdentity, map.ResourceIdentity))
            {
                mapValidation.Add(
                    $"Client map identity resource mismatch: maps.Id={map.Id}; " +
                    $"maps.ResourceIdentity={map.ResourceIdentity ?? "NULL"}; " +
                    $"identity.ResourceIdentity={clientIdentity.ResourceIdentity}.");
            }

            var definition = new MapDefinition(
                map.Id,
                null,
                map.Name,
                map.SourceIdentity ?? map.Code,
                mapBounds,
                [],
                npcPlacements,
                monsterSpawns,
                portals,
                merchantMappings,
                mapValidation,
                clientIdentity);
            maps[map.Id] = definition;
        }

        validation.AddRange(FormatIssues(validatedNpcs.Issues));
        validation.AddRange(FormatIssues(validatedMonsters.Issues));
        validation.AddRange(FormatIssues(validatedPortals.Issues));
        validation.AddRange(FormatIssues(validatedMerchants.Issues));
        foreach (var monster in snapshot.Monsters.Values.Where(monster =>
            !snapshot.Spawns.Values.Any(spawn => spawn.MonsterId == monster.Id)))
        {
            validation.Add($"Monster {monster.Id} is template-only: no validated spawn row exists.");
        }

        return new WorldContentSnapshot(maps, validation);
    }

    private static OfficialNpcWireIdentity? NpcWireIdentity(NpcSpawnStaticData spawn)
    {
        if (!IsRuntimePromotedStatus(spawn.WireEvidenceStatus) ||
            spawn.ObservedClientEntityHandle is not { } entityHandle ||
            spawn.OfficialResourceType is not { } resourceType ||
            spawn.OfficialResourceOrdinal is not { } resourceOrdinal ||
            spawn.OfficialSelectorHighBits is not { } selectorHighBits ||
            spawn.OfficialDirectionCode is not { } directionCode ||
            spawn.OfficialStateCode is not { } stateCode ||
            !IsSha256(spawn.ApplicationMessageSha256) ||
            !IsSha256(spawn.OpaqueTemplateSha256) ||
            entityHandle is 0 or > ushort.MaxValue ||
            resourceType is < 0 or > 31 ||
            resourceOrdinal is < 0 or > 254 ||
            selectorHighBits is < 0 or > 7 ||
            directionCode is < 0 or > 7 ||
            stateCode is < 0 or > 31)
        {
            return null;
        }

        return new OfficialNpcWireIdentity(
            spawn.ClientBuildId,
            entityHandle,
            checked((byte)resourceType),
            checked((byte)resourceOrdinal),
            checked((byte)selectorHighBits),
            checked((byte)directionCode),
            checked((byte)stateCode),
            spawn.ApplicationMessageSha256!,
            spawn.WireEvidenceStatus,
            spawn.EvidenceReference,
            spawn.OpaqueTemplateSha256!);
    }

    private static IEnumerable<string> IssuesForMap(
        int mapId,
        params IReadOnlyList<ContentValidationIssue>[] issueSets) =>
        issueSets.SelectMany(issues => issues)
            .Where(issue => issue.MapId == mapId)
            .Select(FormatIssue);

    private static IEnumerable<string> FormatIssues(IEnumerable<ContentValidationIssue> issues) =>
        issues.Select(FormatIssue);

    private static string FormatIssue(ContentValidationIssue issue) =>
        $"{issue.RuntimeType} {issue.TemplateId}: {issue.Code}; {issue.Message} Source={issue.Source}";

    private static string EscapeJson(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);

    private static bool IsSha256(string? value) =>
        value is { Length: 64 } && value.All(Uri.IsHexDigit);

    private static bool SameResourceIdentity(string expected, string? actual) =>
        !string.IsNullOrWhiteSpace(actual) &&
        string.Equals(
            expected.Replace('\\', '/').TrimStart('/'),
            actual.Replace('\\', '/').TrimStart('/'),
            StringComparison.OrdinalIgnoreCase);

    private static WorldDirection Direction(int? value) =>
        value is not null && Enum.IsDefined(typeof(WorldDirection), value.Value)
            ? (WorldDirection)value.Value
            : WorldDirection.Unknown;

    private static ClientMapIdentity MapClientIdentity(ClientMapIdentityStaticData row) => new(
        row.MapId,
        row.ClientMapId,
        row.ClientAreaId,
        row.ResourceIdentity,
        row.ClientBuildId,
        row.CoordinateScaleX,
        row.CoordinateScaleY,
        row.CoordinateOffsetX,
        row.CoordinateOffsetY,
        ParseEvidenceStatus(row.IdentityEvidenceStatus),
        ParseEvidenceStatus(row.CoordinateEvidenceStatus),
        row.ProductionEnabled,
        row.EvidenceReference);

    private static MapIdentityEvidenceStatus ParseEvidenceStatus(string value) =>
        IsRuntimePromotedStatus(value)
            ? MapIdentityEvidenceStatus.Derived
            : Enum.TryParse<MapIdentityEvidenceStatus>(value, ignoreCase: false, out var status)
                ? status
                : MapIdentityEvidenceStatus.EvidenceBlocked;

    private static bool IsRuntimePromotedStatus(string value) =>
        value is "Verified" or "Derived" ||
        value.StartsWith("canonical-", StringComparison.Ordinal) ||
        value.StartsWith("formal-runtime-", StringComparison.Ordinal);
}

public sealed class MariaDbWorldSessionCoordinator : IWorldSessionCoordinator, IWorldSessionTransitionCoordinator
{
    private readonly WorldSessionCoordinator _inner;
    private readonly IProductionGameplayContentAuthority _contentAuthority;

    public MariaDbWorldSessionCoordinator(
        MariaDbStaticDataLoader loader,
        IProductionGameplayContentAuthority contentAuthority,
        IProductionMapRuntimeRegistry? mapRegistry = null,
        ICharacterImmortalProjectionSource? immortalProjectionSource = null,
        ICharacterGoldProjectionSource? goldProjectionSource = null,
        ICharacterInventoryProjectionSource? inventoryProjectionSource = null)
    {
        ArgumentNullException.ThrowIfNull(loader);
        _contentAuthority = contentAuthority ?? throw new ArgumentNullException(nameof(contentAuthority));
        var repository = new MariaDbWorldContentRepository(loader);
        _inner = new WorldSessionCoordinator(
            repository,
            mapRegistry: mapRegistry ?? new ProductionMapRuntimeRegistry(repository, contentAuthority),
            immortalProjectionSource: immortalProjectionSource,
            goldProjectionSource: goldProjectionSource,
            inventoryProjectionSource: inventoryProjectionSource);
        if (_inner.AuthorityKind != WorldContentAuthorityKind.MariaDb)
        {
            throw new InvalidOperationException("Production world sessions require a MariaDB world-content authority.");
        }
    }

    public WorldContentAuthorityKind AuthorityKind => _inner.AuthorityKind;

    public int ActiveBindingCount => _inner.ActiveBindingCount;

    public async Task<OperationResult<WorldSessionBinding>> BindAsync(
        RuntimeSession session,
        CharacterSummary character,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(character);
        var ready = RequireContentRelease();
        return ready.Succeeded
            ? await _inner.BindAsync(session, character, cancellationToken)
            : OperationResult<WorldSessionBinding>.Failure(ready.Error.Code, ready.Error.Message, ready.Error.Source);
    }

    public OperationResult<WorldSessionBinding> GetBinding(string sessionId) =>
        _inner.GetBinding(sessionId);

    public OperationResult<WorldSessionBinding> UpdateSession(RuntimeSession session) =>
        _inner.UpdateSession(session);

    public async Task<OperationResult<WorldSessionBinding>> RebindAsync(
        RuntimeSession session,
        CharacterSummary character,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(character);
        var ready = RequireContentRelease();
        return ready.Succeeded
            ? await _inner.RebindAsync(session, character, cancellationToken)
            : OperationResult<WorldSessionBinding>.Failure(ready.Error.Code, ready.Error.Message, ready.Error.Source);
    }

    public bool Unbind(string sessionId) => _inner.Unbind(sessionId);

    private OperationResult RequireContentRelease()
    {
        var ready = _contentAuthority.RequireReady();
        return ready.Succeeded
            ? ready
            : OperationResult.Failure(ready.Error.Code, ready.Error.Message, nameof(MariaDbWorldSessionCoordinator));
    }
}
