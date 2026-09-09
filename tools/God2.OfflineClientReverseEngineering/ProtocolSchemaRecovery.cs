using System.Text.Json;

namespace God2.OfflineClientReverseEngineering;

public sealed record ProtocolSchemaRecoveryResult(
    int PacketFamilyCount,
    int BattleActionTypeCount,
    int SkillFamilyCount,
    int ResultFamilyCount,
    int DecoderCandidateCount,
    int DecoderVerifiedCount,
    int SerializerCandidateCount,
    int SerializerVerifiedCount,
    int RuntimeMappingCount,
    int ProductionReadyCount,
    int FakeNetworkBytes);

public static class ProtocolSchemaRecovery
{
    private static readonly string[] PacketFamilies =
    [
        "LoginHandshake", "LoginAuthentication", "CharacterLifecycle", "WorldBootstrap",
        "MapNavigation", "PortalTransfer", "Inventory", "Equipment", "Shop", "Crafting",
        "Quest", "Party", "Mount", "Pet", "Progression", "BattleCommand", "BattleResult"
    ];

    private static readonly string[] BattleActionTypes =
    [
        "BasicAttack", "Skill", "Item", "Defend", "Flee", "Formation", "PositionSwap"
    ];

    private static readonly string[] SkillFamilies =
    [
        "BasicAttack", "SingleTargetDamage", "MultiTargetDamage", "SingleTargetHeal", "MultiTargetHeal",
        "Buff", "Debuff", "StatusRemove", "ResourceRestore", "Revive", "Formation", "PositionSwap",
        "Summon", "PetCommand", "Passive", "TriggeredEffect"
    ];

    private static readonly string[] ResultFamilies =
    [
        "CharacterResult", "InventoryResult", "EquipmentResult", "SkillResult", "BattleResult",
        "QuestResult", "PartyResult", "MountPetResult", "ShopCraftingResult", "WorldResult"
    ];

    public static ProtocolSchemaRecoveryResult Write(
        string workspaceRoot,
        DispatchRecoverySnapshot dispatch,
        JsonSerializerOptions options)
    {
        var evidenceRoot = Path.Combine(workspaceRoot, "protocol", "evidence", "current-build");
        var captureRoot = Path.Combine(evidenceRoot, "chinese-labeled-captures");
        Directory.CreateDirectory(evidenceRoot);

        var familySchemas = PacketFamilies.Select(family => new
        {
            FamilyId = family,
            States = StatesFor(family),
            Direction = DirectionFor(family),
            Framing = "UInt16LittleEndian length + decoded opcode + payload + checksum; state-specific dispatch",
            SemanticFields = FieldsFor(family),
            UnknownFieldPolicy = "Preserve opaque bytes with offset; do not infer or discard.",
            DecoderStatus = "EvidenceBlocked",
            SerializerStatus = "SerializerBlockedByEvidence",
            RuntimeMutationAllowed = false,
            Evidence = new[]
            {
                "protocol/evidence/current-build/client-dispatch-registry.json",
                "protocol/evidence/current-build/chinese-labeled-captures/action-transactions.json"
            }
        }).ToArray();
        WriteJson(Path.Combine(evidenceRoot, "protocol-family-schemas.json"), new
        {
            SchemaVersion = "protocol-family-schemas-v1",
            ClientBuildId = dispatch.ClientBuildId,
            PacketFamilies = familySchemas,
            StateSpecificDispatchEntryCount = dispatch.StateSpecificRegistryEntryCount,
            MovementExcluded = true,
            HeartbeatExcluded = true,
            FakeNetworkBytes = 0
        }, options);

        WriteJson(Path.Combine(evidenceRoot, "battle-command-schema.json"), new
        {
            SchemaVersion = "battle-command-schema-v1",
            ClientBuildId = dispatch.ClientBuildId,
            Envelope = new
            {
                Required = new[] { "actorId", "actionType", "battleReference", "round", "commandWindow" },
                Optional = new[] { "skillId", "itemId", "targetIds", "formation", "positionSwap", "flags" },
                Unknown = "offset-indexed byte regions preserved unchanged",
                ActionTypes = BattleActionTypes
            },
            Decoder = new
            {
                State = "Battle.CommandWindowOpen",
                BuildLocked = true,
                LengthValidated = true,
                MutatesRuntime = false,
                Status = "DecoderCandidate",
                Blocker = "Required dynamic official-wire fields remain opaque; semantic model is executable but official-wire mutation remains closed."
            },
            Serializer = new
            {
                Source = "AuthoritativeGameplayResult only",
                RejectCapturedPacketIdentifier = true,
                RejectFixedResponseIdentifier = true,
                ProductionBytesEmitted = false,
                Status = "SerializerBlockedByEvidence"
            }
        }, options);

        WriteJson(Path.Combine(evidenceRoot, "skill-family-schema.json"), new
        {
            SchemaVersion = "skill-family-schema-v1",
            GenericCommand = new[] { "actorId", "skillId", "targetIds", "round", "commandWindow", "flags", "unknownFields" },
            CatalogDriven = true,
            PerSkillHandlerCount = 0,
            Families = SkillFamilies.Select((family, index) => new
            {
                FamilyId = index + 1,
                Name = family,
                RuntimeMapping = "God2.ClassicServer.Runtime.SkillActionRuntime",
                ProtocolHandler = "GeneralizedGameplayCommand.Skill"
            }).ToArray(),
            OfficialWireStatus = "EvidenceBlocked"
        }, options);

        WriteJson(Path.Combine(evidenceRoot, "result-family-schemas.json"), new
        {
            SchemaVersion = "result-family-schemas-v1",
            ResultFamilies = ResultFamilies.Select(family => new
            {
                FamilyId = family,
                CommonFields = new[] { "resultCode", "failureCode", "stateVersion", "eventSequence", "unknownFields" },
                Source = "authoritative runtime state",
                SerializerStatus = "SerializerBlockedByEvidence"
            }).ToArray(),
            BattleSemanticResults = new object[]
            {
                new { Name = "DamageResult", Fields = new[] { "actorId", "targetId", "skillId", "amount", "absorbed", "critical", "targetHpAfter", "eventSequence" } },
                new { Name = "HealingResult", Fields = new[] { "actorId", "targetId", "skillId", "amount", "overheal", "targetHpAfter", "eventSequence" } },
                new { Name = "StatusApply", Fields = new[] { "sourceId", "targetId", "statusId", "stacks", "duration", "expiresAt", "eventSequence" } },
                new { Name = "StatusRemove", Fields = new[] { "sourceId", "targetId", "statusId", "reason", "eventSequence" } },
                new { Name = "Settlement", Fields = new[] { "battleId", "terminationType", "participants", "experience", "items", "currency", "questEvents", "outbox", "stateVersion", "idempotencyKey" } }
            },
            ProductionBytesEmitted = false
        }, options);

        var decoderSource = Path.Combine(captureRoot, "decoder-candidates.json");
        var serializerSource = Path.Combine(captureRoot, "serializer-candidates.json");
        var mappingsSource = Path.Combine(captureRoot, "runtime-mappings.json");
        var gatesSource = Path.Combine(captureRoot, "promotion-gates.json");
        CopyEvidence(decoderSource, Path.Combine(evidenceRoot, "decoder-candidates.json"));
        CopyEvidence(serializerSource, Path.Combine(evidenceRoot, "serializer-candidates.json"));
        CopyEvidence(mappingsSource, Path.Combine(evidenceRoot, "runtime-mappings.json"));
        CopyEvidence(gatesSource, Path.Combine(evidenceRoot, "promotion-gates.json"));

        using var decoders = JsonDocument.Parse(BoundedFile.ReadAllBytes(decoderSource, 64L * 1024 * 1024, "Decoder evidence"));
        using var serializers = JsonDocument.Parse(BoundedFile.ReadAllBytes(serializerSource, 64L * 1024 * 1024, "Serializer evidence"));
        using var mappings = JsonDocument.Parse(BoundedFile.ReadAllBytes(mappingsSource, 64L * 1024 * 1024, "Runtime mapping evidence"));
        using var gates = JsonDocument.Parse(BoundedFile.ReadAllBytes(gatesSource, 64L * 1024 * 1024, "Promotion-gate evidence"));
        var gateRows = gates.RootElement.TryGetProperty("promotionGates", out var gateArray) ? gateArray : default;
        return new ProtocolSchemaRecoveryResult(
            PacketFamilies.Length,
            BattleActionTypes.Length,
            SkillFamilies.Length,
            ResultFamilies.Length,
            decoders.RootElement.GetArrayLength(),
            CountTrue(decoders.RootElement, "decoderVerified"),
            serializers.RootElement.GetArrayLength(),
            CountTrue(serializers.RootElement, "serializerVerified"),
            mappings.RootElement.GetArrayLength(),
            gateRows.ValueKind == JsonValueKind.Array ? CountTrue(gateRows, "productionReady") : 0,
            0);
    }

    private static string[] StatesFor(string family) => family switch
    {
        "LoginHandshake" or "LoginAuthentication" => ["Login"],
        "CharacterLifecycle" => ["CharacterList"],
        "BattleCommand" or "BattleResult" => ["Battle"],
        _ => ["World"]
    };

    private static string DirectionFor(string family) => family switch
    {
        "WorldBootstrap" or "BattleResult" or "Progression" => "ServerToClient",
        "LoginHandshake" => "Bidirectional",
        _ => "Bidirectional"
    };

    private static string[] FieldsFor(string family) => family switch
    {
        "BattleCommand" => ["actorId", "actionType", "skillId", "itemId", "targetIds", "round", "commandWindow", "formation", "flags"],
        "BattleResult" => ["participants", "hp", "mp", "statuses", "round", "events", "reward", "finalization"],
        "Inventory" => ["characterId", "operation", "itemId", "quantity", "slot", "version", "idempotencyKey"],
        "Equipment" => ["characterId", "operation", "itemId", "sourceSlot", "targetSlot", "version", "idempotencyKey"],
        "Quest" => ["characterId", "questId", "operation", "objectiveId", "progress", "version", "idempotencyKey"],
        "MapNavigation" => ["mapId", "position", "destination", "path", "collisionVersion"],
        _ => ["entityId", "operation", "version", "idempotencyKey"]
    };

    private static int CountTrue(JsonElement array, string property)
    {
        var count = 0;
        foreach (var item in array.EnumerateArray())
        {
            if (item.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.True) count++;
        }
        return count;
    }

    private static void CopyEvidence(string source, string destination) => File.Copy(source, destination, overwrite: true);

    private static void WriteJson(string path, object value, JsonSerializerOptions options) =>
        File.WriteAllText(path, JsonSerializer.Serialize(value, options));
}
