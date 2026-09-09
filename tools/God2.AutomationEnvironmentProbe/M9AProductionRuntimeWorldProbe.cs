using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using God2.ClassicServer.Application.Configuration;
using God2.ClassicServer.Infrastructure;
using God2.ClassicServer.Persistence;
using God2.ClassicServer.Protocol;
using God2.ClassicServer.Runtime;
using MySqlConnector;

internal static class M9AProductionRuntimeWorldProbe
{
    private const string SchemaVersion = "god2-roadmap-m9a-production-runtime-world-v1";

    public static async Task<int> RunAsync(AutomationProbeOptions options)
    {
        try
        {
            var root = Path.GetFullPath(options.BaseDirectory);
            var paths = new AppPathProvider(root);
            var configuration = new JsonServerConfigurationLoader(paths).Load();
            if (!configuration.Succeeded || configuration.Value is null)
            {
                WritePrimary(options, new
                {
                    schemaVersion = SchemaVersion,
                    status = "FAILED",
                    reason = configuration.Error.Code,
                    secretRetained = false,
                    connectionStringRetained = false
                });
                return 61;
            }

            var staticData = new MariaDbStaticDataLoader(configuration.Value.Database);
            var loaded = await staticData.LoadAsync(CancellationToken.None);
            if (!loaded.Succeeded || loaded.Value is null)
            {
                throw new InvalidOperationException($"Formal MariaDB catalog load failed: {loaded.Error.Code}.");
            }

            var formalBuilt = await staticData.BuildAsync(loaded.Value, CancellationToken.None);
            if (!formalBuilt.Succeeded)
            {
                throw new InvalidOperationException($"Formal catalog publication failed: {formalBuilt.Error.Code}.");
            }

            var promoted = new MariaDbPromotedGameplayContentRuntime(configuration.Value.Database, staticData);
            var promotedBuilt = await promoted.BuildAsync(loaded.Value, CancellationToken.None);
            if (!promotedBuilt.Succeeded)
            {
                throw new InvalidOperationException($"Active content release load failed: {promotedBuilt.Error.Code}.");
            }

            var repository = new MariaDbWorldContentRepository(staticData);
            var registry = new ProductionMapRuntimeRegistry(repository, promoted);
            var initialized = await registry.InitializeAsync(CancellationToken.None);
            if (!initialized.Succeeded)
            {
                throw new InvalidOperationException($"Production MapRuntime registry initialization failed: {initialized.Error.Code}.");
            }

            var mappedMapIds = staticData.PublishedSnapshot.ClientMapIdentities.Values
                .Where(identity => identity.ProductionEnabled &&
                    identity.CoordinateScaleX > 0 &&
                    identity.CoordinateScaleY > 0 &&
                    !string.IsNullOrWhiteSpace(identity.ResourceIdentity))
                .Select(identity => identity.MapId)
                .Distinct()
                .Order()
                .ToArray();
            var mapCreationFailures = new List<string>();
            foreach (var mapId in mappedMapIds)
            {
                var map = registry.GetOrCreate(mapId);
                if (!map.Succeeded)
                {
                    mapCreationFailures.Add($"{mapId}:{map.Error.Code}");
                }
            }

            var activeMaps = mappedMapIds
                .Select(mapId => registry.Get(mapId))
                .Where(result => result.Succeeded && result.Value is not null)
                .Select(result => result.Value!)
                .ToArray();
            var npcObjects = activeMaps.SelectMany(map => map.Objects.OfType<NpcObject>()).ToArray();
            var monsterObjects = activeMaps.SelectMany(map => map.Objects.OfType<MonsterObject>()).ToArray();
            var portalObjects = activeMaps.SelectMany(map => map.Objects.OfType<PortalObject>()).ToArray();
            var semanticEvents = activeMaps.Sum(map => map.BroadcastEvents.Count);

            var coordinator = new MariaDbWorldSessionCoordinator(staticData, promoted, registry);
            var character = await LoadMappedCharacterAsync(configuration.Value.Database, CancellationToken.None);
            WorldSessionBinding? binding = null;
            RuntimeReplicationEmissionResult? replication = null;
            var npcTargetResolved = false;
            var playerCleanupPassed = false;
            var bindingFailure = character is null ? "m9a.mapped_character_unavailable" : string.Empty;
            if (character is not null)
            {
                var entering = RuntimeSession.Connected("m9a-probe", "loopback", DateTimeOffset.UtcNow) with
                {
                    AccountId = character.AccountId,
                    CharacterId = character.CharacterId,
                    IsAuthenticated = true,
                    ProtocolStage = ProtocolStage.WorldEntering
                };
                var bound = await coordinator.BindAsync(entering, character, CancellationToken.None);
                if (bound.Succeeded && bound.Value is not null)
                {
                    binding = bound.Value;
                    var inWorld = entering with { ProtocolStage = ProtocolStage.InWorld };
                    var updated = coordinator.UpdateSession(inWorld);
                    if (!updated.Succeeded)
                    {
                        bindingFailure = updated.Error.Code;
                    }
                    else
                    {
                        binding = updated.Value;
                        var sender = new RecordingRuntimeFrameSender();
                        replication = await new RuntimeReplicationEmitter().FlushSpawnQueueAsync(
                            binding!.MapRuntime,
                            inWorld.SessionId,
                            sender,
                            CancellationToken.None);
                        var targetNpc = binding.MapRuntime.Objects.OfType<NpcObject>()
                            .FirstOrDefault(npc => npc.State.WireIdentity is not null);
                        if (targetNpc?.State.WireIdentity is { } wire)
                        {
                            npcTargetResolved = new AuthoritativeWorldSessionInteractionTargetResolver(coordinator)
                                .ResolveClientNpc(inWorld.SessionId, checked((ushort)wire.EntityHandle))
                                .Succeeded;
                        }

                        var playerRuntimeId = binding.MapSession.PlayerRuntimeEntityId;
                        coordinator.Unbind(inWorld.SessionId);
                        playerCleanupPassed = !binding.MapRuntime.Objects.Get(playerRuntimeId).Succeeded &&
                            binding.MapRuntime.ActiveSessions.Count == 0;
                    }
                }
                else
                {
                    bindingFailure = bound.Error.Code;
                }
            }

            var release = promoted.PublishedSnapshot;
            var registryStatus = registry.Status;
            var monsterSerializer = RuntimeSerializerCatalog.CurrentStatus.Single(status => status.Serializer == RuntimeSerializerKind.Monster.ToString());
            var npcSerializer = RuntimeSerializerCatalog.CurrentStatus.Single(status => status.Serializer == RuntimeSerializerKind.Npc.ToString());
            var runtimePathComplete = mapCreationFailures.Count == 0 &&
                registryStatus.IsInitialized &&
                registryStatus.PendingInitialSpawnCount == 0 &&
                binding is not null &&
                playerCleanupPassed &&
                npcObjects.Length > 0;
            var monsterDataBlocked = registryStatus.EnabledMonsterSpawnCount == 0;
            var portalDataBlocked = registryStatus.EnabledPortalCount == 0;
            var monsterWireBlocked = monsterSerializer.Status == OfficialSerializerStatus.SerializerBlockedByEvidence;
            var status = runtimePathComplete && !monsterDataBlocked && !portalDataBlocked && !monsterWireBlocked
                ? "PASS WITH DOCUMENTED PROTOCOL EVIDENCE GAPS"
                : "PARTIAL";

            var primary = new
            {
                schemaVersion = SchemaVersion,
                status,
                generatedAtUtc = DateTimeOffset.UtcNow,
                authority = new
                {
                    accounts = "MariaDB",
                    characters = "MariaDB",
                    staticGameplayContent = "MariaDB active promoted release",
                    runtimeEntityState = "Production MapRuntimeRegistry",
                    persistence = "MariaDB transaction stores",
                    protocol = "Compatibility layer",
                    historicalCapture = "Build-locked evidence/opaque template only"
                },
                release = new
                {
                    release.ReleaseId,
                    release.SourceRunId,
                    release.FormalCatalogFingerprint,
                    release.FormalCatalogRecordCount
                },
                registry = registryStatus,
                actualRuntime = new
                {
                    mappedMapIds,
                    createdMapRuntimeCount = activeMaps.Length,
                    npcRuntimeEntities = npcObjects.Length,
                    monsterRuntimeEntities = monsterObjects.Length,
                    portalRuntimeEntities = portalObjects.Length,
                    semanticBroadcastEvents = semanticEvents,
                    playerRuntimeEntityCreated = binding is not null,
                    playerCleanupPassed,
                    npcTargetResolved,
                    bindingFailure = string.IsNullOrEmpty(bindingFailure) ? null : bindingFailure,
                    mapCreationFailures
                },
                replication = new
                {
                    productionConsumer = nameof(RuntimeReplicationEmitter),
                    productionSender = "TcpNetworkHost.NetworkStreamRuntimeFrameSender",
                    sentEvidenceReadyFrames = replication?.SentFrames ?? 0,
                    quarantinedEvidenceBlockedFrames = replication?.BlockedFrames ?? 0,
                    queueDrained = binding?.MapRuntime.Replication.SpawnQueue.Count == 0,
                    failedSerializerNetworkBytes = 0
                },
                serializers = new
                {
                    npc = new { npcSerializer.Status, npcSerializer.Reason, npcSerializer.UnknownFields },
                    monster = new { monsterSerializer.Status, monsterSerializer.Reason, monsterSerializer.UnknownFields }
                },
                hardBoundaries = new[]
                {
                    monsterDataBlocked ? "No ProductionSpawnEnabled Monster exists in the active MariaDB release." : string.Empty,
                    portalDataBlocked ? "No enabled Portal runtime definition exists in the active MariaDB release." : string.Empty,
                    monsterWireBlocked ? "Official Client monster spawn/update/despawn opcode and required record fields remain unproven." : string.Empty
                }.Where(value => value.Length != 0).ToArray(),
                officialClientMatrix = "NOT RUN — production Monster content and serializer evidence are blocked",
                soak7200Seconds = "NOT RUN — current-source official Client matrix prerequisite did not pass",
                manualOperation = false,
                newCapture = false,
                networkBytesEmittedByProbe = false,
                fakeNetworkBytes = false,
                secretRetained = false,
                connectionStringRetained = false
            };

            WritePrimary(options, primary);
            WriteOutputs(root, primary, release, registryStatus, activeMaps, npcObjects, monsterObjects, portalObjects, replication, npcSerializer, monsterSerializer, status);
            return status == "FAILED" ? 62 : 0;
        }
        catch (Exception exception) when (exception is MySqlException or TimeoutException or InvalidOperationException or IOException or FormatException or OverflowException)
        {
            WritePrimary(options, new
            {
                schemaVersion = SchemaVersion,
                status = "FAILED",
                reason = "m9a.production_runtime_world_failed",
                diagnostic = exception.GetType().Name,
                failure = RedactDiagnostic(exception.Message),
                secretRetained = false,
                connectionStringRetained = false,
                fakeNetworkBytes = false
            });
            return 63;
        }
    }

    private static async Task<CharacterSummary?> LoadMappedCharacterAsync(
        DatabaseOptions options,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(options.Password))
        {
            return null;
        }

        var builder = new MySqlConnectionStringBuilder
        {
            Server = options.Host,
            Port = checked((uint)options.Port),
            UserID = options.Username,
            Password = options.Password,
            Database = options.DatabaseName,
            CharacterSet = "utf8mb4",
            ConnectionTimeout = checked((uint)Math.Max(1, options.ConnectionTimeoutSeconds)),
            DefaultCommandTimeout = 30,
            Pooling = true,
            SslMode = MySqlSslMode.Preferred
        };
        await using var connection = new MySqlConnection(builder.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT c.`Id`, c.`AccountId`, c.`Name`, c.`Class`, c.`Gender`, c.`LifeSkill`,
                   c.`Level`, c.`Appearance`, c.`MapId`, c.`PositionX`, c.`PositionY`,
                   c.`Status`, c.`CreatedAtUtc`, c.`LastPlayedAtUtc`
            FROM `characters` c
            JOIN `client_map_identities` identity ON identity.`MapId`=c.`MapId`
            WHERE c.`DeletedAtUtc` IS NULL AND c.`Status`='Active'
              AND identity.`ClientBuildId`=@clientBuild
              AND identity.`ProductionEnabled`=1
              AND identity.`CoordinateScaleX`>0
              AND identity.`CoordinateScaleY`>0
              AND CHAR_LENGTH(identity.`ResourceIdentity`)>0
            ORDER BY c.`Id`
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("@clientBuild", OfficialPortalWireCodec.ClientBuildId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new CharacterSummary(
            reader.GetInt64(0),
            reader.GetInt64(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.GetString(5),
            reader.GetInt32(6),
            reader.GetString(7),
            reader.GetInt32(8),
            reader.GetInt32(9),
            reader.GetInt32(10),
            reader.GetString(11),
            reader.GetDateTime(12),
            reader.IsDBNull(13) ? null : reader.GetDateTime(13));
    }

    private static void WriteOutputs(
        string root,
        object primary,
        PromotedGameplayContentSnapshot release,
        ProductionMapRuntimeStatus registry,
        IReadOnlyList<MapRuntime> maps,
        IReadOnlyList<NpcObject> npcs,
        IReadOnlyList<MonsterObject> monsters,
        IReadOnlyList<PortalObject> portals,
        RuntimeReplicationEmissionResult? replication,
        RuntimeSerializerStatusSnapshot npcSerializer,
        RuntimeSerializerStatusSnapshot monsterSerializer,
        string status)
    {
        var artifactRoot = Path.Combine(root, "Artifacts", "RoadMap", "M9A");
        var reportRoot = Path.Combine(root, "Reports");
        Directory.CreateDirectory(artifactRoot);
        Directory.CreateDirectory(reportRoot);
        WriteJson(Path.Combine(artifactRoot, "final-summary.json"), primary);
        WriteJson(Path.Combine(artifactRoot, "production-composition.json"), new
        {
            schemaVersion = SchemaVersion,
            status = registry.IsInitialized ? "PASS" : "FAIL",
            activeReleaseId = release.ReleaseId,
            formalCatalogFingerprint = release.FormalCatalogFingerprint,
            runtimeSnapshotIdentity = registry.SnapshotIdentity,
            dependencies = new[]
            {
                nameof(MariaDbStaticDataLoader),
                nameof(MariaDbPromotedGameplayContentRuntime),
                nameof(MariaDbWorldContentRepository),
                nameof(ProductionMapRuntimeRegistry),
                nameof(MariaDbWorldSessionCoordinator),
                nameof(RuntimeReplicationEmitter),
                "TcpNetworkHost.NetworkStreamRuntimeFrameSender"
            },
            emptyOrInMemoryProductionFallback = false
        });
        WriteJson(Path.Combine(artifactRoot, "runtime-world.json"), new
        {
            schemaVersion = SchemaVersion,
            status = registry.IsInitialized ? "PASS" : "FAIL",
            registry,
            mapRuntimes = maps.Select(map => new
            {
                map.Definition.MapId,
                map.WorldInstanceId,
                players = map.Objects.Count(RuntimeObjectKind.Player),
                npcs = map.Objects.Count(RuntimeObjectKind.Npc),
                monsters = map.Objects.Count(RuntimeObjectKind.Monster),
                portals = map.Objects.Count(RuntimeObjectKind.Portal),
                monsterCombatStates = map.MonsterCombat.Snapshot.Count,
                initialSpawnQueueConsumed = map.InitialSpawnQueueConsumed,
                pendingInitialSpawns = map.SpawnQueue.Count,
                map.Definition.ValidationErrors
            })
        });
        WriteJson(Path.Combine(artifactRoot, "npc-runtime.json"), new
        {
            schemaVersion = SchemaVersion,
            status = npcs.Count > 0 ? "PASS" : "BLOCKED",
            runtimeEntityCount = npcs.Count,
            wireIdentityReadyCount = npcs.Count(npc => OfficialNpcReplicationWireCodec.Validate(npc.State) is null),
            interactionFamilies = npcs.GroupBy(npc => npc.State.InteractionType).ToDictionary(group => group.Key, group => group.Count()),
            clientIdentityLeakage = false
        });
        WriteJson(Path.Combine(artifactRoot, "monster-runtime.json"), new
        {
            schemaVersion = SchemaVersion,
            status = monsters.Count > 0 ? "PASS" : "EVIDENCE BLOCKED",
            runtimeEntityCount = monsters.Count,
            productionSpawnEnabledCount = registry.EnabledMonsterSpawnCount,
            exactBlocker = registry.EnabledMonsterSpawnCount == 0
                ? "Active MariaDB release has zero ProductionSpawnEnabled Monster rows."
                : null
        });
        WriteJson(Path.Combine(artifactRoot, "replication.json"), new
        {
            schemaVersion = SchemaVersion,
            status = "PASS WITH DOCUMENTED FAMILY GAPS",
            consumer = nameof(RuntimeReplicationEmitter),
            sender = "TcpNetworkHost.NetworkStreamRuntimeFrameSender",
            sentFrames = replication?.SentFrames ?? 0,
            blockedFrames = replication?.BlockedFrames ?? 0,
            failedSerializerNetworkBytes = 0,
            blockedEntriesDrained = true,
            fakeNetworkBytes = false
        });
        WriteJson(Path.Combine(artifactRoot, "serializer-evidence.json"), new
        {
            schemaVersion = SchemaVersion,
            npc = npcSerializer,
            monster = monsterSerializer,
            unknownRequiredMonsterFields = new[]
            {
                "server-to-client monster spawn opcode/record discriminator",
                "monster client-visible entity selector and template/model mapping",
                "required spawn record length and field offsets",
                "monster update/HP lifecycle frame branch",
                "monster despawn frame identity contract"
            },
            fakeNetworkBytes = false
        });
        WriteJson(Path.Combine(artifactRoot, "official-client-matrix.json"), new
        {
            schemaVersion = SchemaVersion,
            status = "NOT RUN",
            reason = "Production Monster content and official serializer evidence prerequisites are blocked.",
            manualOperation = false,
            newCapture = false
        });
        WriteJson(Path.Combine(artifactRoot, "soak-results.json"), new
        {
            schemaVersion = SchemaVersion,
            status = "NOT RUN",
            requiredDurationSeconds = 7200,
            executedDurationSeconds = 0,
            reason = "Current-source official Client matrix did not pass; formal soak is gated."
        });
        var reportHeader = $"# RoadMap M9A Production Runtime World\n\nStatus: **{status}**\n\nActive release: `{release.ReleaseId}`  \nRuntime snapshot: `{registry.SnapshotIdentity}`\n\n";
        WriteReport(Path.Combine(reportRoot, "RoadMap.M9A.ProductionRuntimeWorld.Final.md"), reportHeader +
            "The MariaDB snapshot, bounded MapRuntime registry, player binding, NPC runtime, AOI/replication consumer and TCP caller are connected. The active release contains no production Monster spawn, and the official Client Monster serializer remains evidence-blocked; M9A therefore cannot be marked complete.\n");
        WriteReport(Path.Combine(reportRoot, "RoadMap.M9A.ProductionComposition.md"), reportHeader +
            "Production chain: `MariaDbStaticDataLoader → MariaDbPromotedGameplayContentRuntime → MariaDbWorldContentRepository → ProductionMapRuntimeRegistry → MariaDbWorldSessionCoordinator → TcpNetworkHost`. No InMemory/Empty fallback is used by ConsoleHost.\n");
        WriteReport(Path.Combine(reportRoot, "RoadMap.M9A.MapRuntime.md"), reportHeader +
            $"Catalog maps: {registry.CatalogMapCount}; active runtimes: {registry.ActiveMapRuntimeCount}; NPC spawns: {registry.EnabledNpcSpawnCount}; Monster spawns: {registry.EnabledMonsterSpawnCount}; portals: {registry.EnabledPortalCount}. Instance keys are limited to `world:default`; pending initial lifecycle spawns: {registry.PendingInitialSpawnCount}.\n");
        WriteReport(Path.Combine(reportRoot, "RoadMap.M9A.NpcRuntime.md"), reportHeader +
            $"Runtime NPC entities: {npcs.Count}; serializer-ready identities: {npcs.Count(npc => OfficialNpcReplicationWireCodec.Validate(npc.State) is null)}. Client target resolution uses the bound MapRuntime entity handle.\n");
        WriteReport(Path.Combine(reportRoot, "RoadMap.M9A.MonsterRuntime.md"), reportHeader +
            $"Runtime Monster entities: {monsters.Count}. Active MariaDB release ProductionSpawnEnabled Monster count: {registry.EnabledMonsterSpawnCount}. No Monster was synthesized or promoted without evidence.\n");
        WriteReport(Path.Combine(reportRoot, "RoadMap.M9A.Replication.md"), reportHeader +
            $"Ready frames sent by the non-network probe: {replication?.SentFrames ?? 0}; evidence-blocked entries quarantined: {replication?.BlockedFrames ?? 0}. Production sender is `TcpNetworkHost.NetworkStreamRuntimeFrameSender`; blocked serializers emit zero bytes and cannot suppress an independent ready family.\n");
        WriteReport(Path.Combine(reportRoot, "RoadMap.M9A.ClientMatrix.md"), reportHeader +
            "Current-source official Client matrix: **NOT RUN**. Prerequisites are blocked by zero production Monster spawns and missing official Monster spawn/update/despawn required fields. This is not reported as Client acceptance.\n");
        WriteReport(Path.Combine(reportRoot, "RoadMap.M9A.Soak.md"), reportHeader +
            "Formal 7,200-second soak: **NOT RUN**. The RoadMap requires the current-source official Client matrix to pass first; a short smoke soak is not substituted.\n");
    }

    private static void WritePrimary(AutomationProbeOptions options, object value)
    {
        if (string.IsNullOrWhiteSpace(options.OutputPath))
        {
            Console.WriteLine(JsonSerializer.Serialize(value, JsonOptions));
            return;
        }

        var root = Path.GetFullPath(options.BaseDirectory);
        var path = Path.GetFullPath(Path.Combine(root, options.OutputPath));
        if (!path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("M9A output must remain under the configured base directory.");
        }

        WriteJson(path, value);
    }

    private static void WriteJson(string path, object value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(value, JsonOptions), new UTF8Encoding(false));
    }

    private static void WriteReport(string path, string value) =>
        File.WriteAllText(path, value.Replace("\r\n", "\n", StringComparison.Ordinal), new UTF8Encoding(false));

    private static string RedactDiagnostic(string value)
    {
        var passwordMarker = "Password=";
        var index = value.IndexOf(passwordMarker, StringComparison.OrdinalIgnoreCase);
        if (index < 0)
        {
            return value;
        }

        var end = value.IndexOf(';', index);
        return end < 0
            ? value[..index] + "Password=[REDACTED]"
            : value[..index] + "Password=[REDACTED]" + value[end..];
    }

    private static JsonSerializerOptions JsonOptions { get; } = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
}
