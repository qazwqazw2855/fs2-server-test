using System.Buffers.Binary;
using System.Collections.ObjectModel;

namespace God2.ClassicServer.Protocol;

public sealed record PublicBetaGameplayOutputAdapterDescriptor(
    string CanonicalContract,
    byte PublicBetaOpcode,
    byte CurrentOpcode,
    int CurrentApplicationRecordLength,
    string CurrentWriter,
    string Evidence);

public sealed record OfficialImmortalStatusLayout(
    byte Identity,
    byte Level,
    ushort Metal,
    ushort Wood,
    ushort Water,
    ushort Fire,
    ushort Earth,
    uint RawReservedValue12,
    short Vitality,
    short Strength,
    short Intelligence,
    short Speed,
    ushort ExperienceBasisPoints,
    bool StatusFlag,
    ushort RawReservedValue26);

public sealed record OfficialImmortalVitalsLayout(
    uint CurrentHitPoints,
    uint CurrentMagicPoints,
    uint MaximumHitPoints,
    uint MaximumMagicPoints);

public sealed record OfficialPlayerGodProgressLayout(
    uint PlayerCurrentHitPoints,
    uint GodCurrentHitPoints,
    uint PlayerCurrentMagicPoints,
    uint GodCurrentMagicPoints,
    ushort PlayerExperienceBasisPoints,
    ushort GodExperienceBasisPoints);

public sealed record OfficialPlayerVitalsLayout(
    uint CurrentHitPoints,
    uint CurrentMagicPoints,
    uint MaximumHitPoints,
    uint MaximumMagicPoints);

public sealed record OfficialPlayerStatusLayout(
    sbyte Level,
    byte ReservedAfterLevel,
    ushort UnspentAttributePoints,
    ushort Constitution,
    ushort Strength,
    ushort Intelligence,
    ushort Speed,
    ushort ExperienceBasisPoints,
    ushort ReservedBeforeMaximumVitals,
    uint MaximumHitPoints,
    uint MaximumMagicPoints);

public sealed record OfficialNpcSpawnLayout(
    uint EntityHandle,
    byte ResourceOrdinal,
    byte ResourceType,
    byte SelectorHighBits,
    ushort RawReservedWord,
    byte DirectionCode,
    byte StateCode,
    ReadOnlyMemory<byte> RawOpaqueBytes,
    ushort MapX,
    ushort MapY,
    byte PositionMode);

/// <summary>
/// Executable current-build writers for public-beta gameplay output contracts
/// whose canonical meanings are corroborated by the old consumers. Every writer
/// validates the exact supported build and state; unresolved fields stay explicit
/// raw values and are never synthesized from public-beta offsets.
/// </summary>
public static class PublicBetaCompatibilityGameplayWireAdapter
{
    public const byte PlayerGodProgressOpcode = 0x2B;
    public const byte PlayerVitalsOpcode = 0x24;
    public const byte PlayerStatusOpcode = 0x22;
    public const byte ImmortalStatusOpcode = 0x31;
    public const byte ImmortalCatalogSelectorOpcode = 0x32;
    public const byte ImmortalUiFlagsOpcode = 0x33;
    public const byte ImmortalVitalsOpcode = 0x39;
    public const byte NpcSpawnOpcode = 0x72;
    public const int ImmortalStatusRecordLength = 29;
    public const int ImmortalCatalogSelectorRecordLength = 2;
    public const int ImmortalUiFlagsRecordLength = 2;
    public const int ImmortalVitalsRecordLength = 17;
    public const int PlayerGodProgressRecordLength = 21;
    public const int PlayerVitalsRecordLength = 17;
    public const int PlayerStatusRecordLength = 25;
    public const int NpcSpawnRecordLength = 21;
    public const int NpcOpaqueLength = 7;
    public const int CurrentServerOutputAdapterReadyCount = 10;

    private static readonly ReadOnlyCollection<PublicBetaGameplayOutputAdapterDescriptor> Outputs =
        Array.AsReadOnly<PublicBetaGameplayOutputAdapterDescriptor>(
        [
            new(
                "player_status_snapshot_22",
                PlayerStatusOpcode,
                PlayerStatusOpcode,
                PlayerStatusRecordLength,
                nameof(PublicBetaCompatibilityGameplayWireAdapter) + ".EncodePlayerStatus",
                "CurrentBuild-world-bootstrap-offset-118-0x22/25+world-dispatch-rva-0x0008FA65+" +
                "battle-dispatch-rva-0x00146F6F+shared-consumer-rva-0x00117DE0;" +
                "FS2TW-public-beta-player-status-0x22/25"),
            new(
                "player_vitals_snapshot_24",
                PlayerVitalsOpcode,
                PlayerVitalsOpcode,
                PlayerVitalsRecordLength,
                nameof(PublicBetaCompatibilityGameplayWireAdapter) + ".EncodePlayerVitals",
                "CurrentBuild-world-bootstrap-offset-84-0x24/17+dispatcher-rva-0x0008FAB8+" +
                "consumer-rva-0x001177C0-player-object-fields-0x5C/0x64/0x60/0x68;" +
                "FS2TW-public-beta-player-vitals-0x24/17"),
            new(
                "player_god_progress_2b",
                PlayerGodProgressOpcode,
                PlayerGodProgressOpcode,
                PlayerGodProgressRecordLength,
                nameof(PublicBetaCompatibilityGameplayWireAdapter) + ".EncodePlayerGodProgress",
                "CurrentBuild-consumer-rva-0x00118510+consumer-rva-0x000D48A0+" +
                "world-bootstrap-offset-2-0x2B/21;FS2TW-public-beta-player-god-progress-0x2B/21"),
            new(
                "player_derived_stats_snapshot",
                OfficialPlayerDerivedStatsWireCodec.Opcode,
                OfficialPlayerDerivedStatsWireCodec.Opcode,
                OfficialPlayerDerivedStatsWireCodec.ApplicationRecordLength,
                nameof(OfficialPlayerDerivedStatsWireCodec) + ".EncodeCanonicalLayout",
                OfficialPlayerDerivedStatsWireCodec.CurrentConsumerEvidence + ";" +
                OfficialPlayerDerivedStatsWireCodec.PublicBetaSemanticEvidence),
            new(
                "monster_spawn_71",
                OfficialMonsterWorldWireCodec.Opcode,
                OfficialMonsterWorldWireCodec.Opcode,
                OfficialMonsterWorldWireCodec.RecordLength,
                nameof(OfficialMonsterWorldWireCodec) + ".EncodeLayout",
                OfficialMonsterWorldWireCodec.FieldLayoutEvidence),
            new(
                "npc_spawn_72",
                NpcSpawnOpcode,
                NpcSpawnOpcode,
                NpcSpawnRecordLength,
                nameof(PublicBetaCompatibilityGameplayWireAdapter) + ".EncodeNpcSpawn",
                "CurrentBuild-live-0x72/21+consumer-rva-0x000900E3;" +
                "FS2TW-public-beta-SC_DRAW_NPC-0x72/21"),
            new(
                "god_status_snapshot_31",
                ImmortalStatusOpcode,
                ImmortalStatusOpcode,
                ImmortalStatusRecordLength,
                nameof(PublicBetaCompatibilityGameplayWireAdapter) + ".EncodeImmortalStatus",
                "CurrentBuild-login-0x31/29+runtime-projection;" +
                "FS2TW-public-beta-god-status-0x31/29"),
            new(
                "god_catalog_selector_32",
                ImmortalCatalogSelectorOpcode,
                ImmortalCatalogSelectorOpcode,
                ImmortalCatalogSelectorRecordLength,
                nameof(PublicBetaCompatibilityGameplayWireAdapter) + ".EncodeImmortalCatalogSelector",
                "CurrentBuild-world-bootstrap-offset-172-0x32/2+runtime-projection;" +
                "FS2TW-public-beta-god-catalog-selector-0x32/2"),
            new(
                "god_ui_flags_33",
                ImmortalUiFlagsOpcode,
                ImmortalUiFlagsOpcode,
                ImmortalUiFlagsRecordLength,
                nameof(PublicBetaCompatibilityGameplayWireAdapter) + ".EncodeImmortalUiFlags",
                "CurrentBuild-world-bootstrap-offset-174-0x33/2+runtime-projection;" +
                "FS2TW-public-beta-god-ui-flags-0x33/2"),
            new(
                "god_vitals_snapshot_39",
                ImmortalVitalsOpcode,
                ImmortalVitalsOpcode,
                ImmortalVitalsRecordLength,
                nameof(PublicBetaCompatibilityGameplayWireAdapter) + ".EncodeImmortalVitals",
                "CurrentBuild-login-0x39/17+runtime-projection;" +
                "FS2TW-public-beta-god-vitals-0x39/17")
        ]);

    public static IReadOnlyList<PublicBetaGameplayOutputAdapterDescriptor> CurrentServerOutputs => Outputs;

    public static OfficialPlayerSnapshotWireResult<byte[]> EncodeImmortalStatus(
        string clientBuildId,
        GameplayProtocolState state,
        OfficialImmortalStatusLayout value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var boundary = Validate(clientBuildId, state, GameplayProtocolState.World);
        if (boundary is not null)
        {
            return boundary;
        }

        if (value.ExperienceBasisPoints > 0x7FFF)
        {
            return Blocked("wire.public_beta_compat.immortal_experience_out_of_range");
        }

        var record = new byte[ImmortalStatusRecordLength];
        record[0] = ImmortalStatusOpcode;
        record[1] = value.Identity;
        record[2] = value.Level;
        WriteU16(record, 3, value.Metal);
        WriteU16(record, 5, value.Wood);
        WriteU16(record, 7, value.Water);
        WriteU16(record, 9, value.Fire);
        WriteU16(record, 11, value.Earth);
        WriteU32(record, 13, value.RawReservedValue12);
        WriteU16(record, 17, unchecked((ushort)value.Vitality));
        WriteU16(record, 19, unchecked((ushort)value.Strength));
        WriteU16(record, 21, unchecked((ushort)value.Intelligence));
        WriteU16(record, 23, unchecked((ushort)value.Speed));
        WriteU16(record, 25, checked((ushort)(value.ExperienceBasisPoints |
            (value.StatusFlag ? 0x8000 : 0))));
        WriteU16(record, 27, value.RawReservedValue26);
        return OfficialPlayerSnapshotWireResult<byte[]>.Success(record);
    }

    public static OfficialPlayerSnapshotWireResult<byte[]> EncodePlayerGodProgress(
        string clientBuildId,
        GameplayProtocolState state,
        OfficialPlayerGodProgressLayout value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var boundary = Validate(clientBuildId, state, GameplayProtocolState.World);
        if (boundary is not null)
        {
            return boundary;
        }

        if (value.PlayerCurrentHitPoints > int.MaxValue || value.GodCurrentHitPoints > int.MaxValue ||
            value.PlayerCurrentMagicPoints > int.MaxValue || value.GodCurrentMagicPoints > int.MaxValue ||
            value.PlayerExperienceBasisPoints > 10_000 || value.GodExperienceBasisPoints > 10_000)
        {
            return Blocked("wire.public_beta_compat.player_god_experience_out_of_range");
        }

        var record = new byte[PlayerGodProgressRecordLength];
        record[0] = PlayerGodProgressOpcode;
        WriteU32(record, 1, value.PlayerCurrentHitPoints);
        WriteU32(record, 5, value.GodCurrentHitPoints);
        WriteU32(record, 9, value.PlayerCurrentMagicPoints);
        WriteU32(record, 13, value.GodCurrentMagicPoints);
        WriteU16(record, 17, value.PlayerExperienceBasisPoints);
        WriteU16(record, 19, value.GodExperienceBasisPoints);
        return OfficialPlayerSnapshotWireResult<byte[]>.Success(record);
    }

    public static OfficialPlayerSnapshotWireResult<byte[]> EncodePlayerVitals(
        string clientBuildId,
        GameplayProtocolState state,
        OfficialPlayerVitalsLayout value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var boundary = Validate(clientBuildId, state, GameplayProtocolState.World);
        if (boundary is not null)
        {
            return boundary;
        }

        if (value.MaximumHitPoints == 0 || value.MaximumMagicPoints == 0 ||
            value.CurrentHitPoints > value.MaximumHitPoints ||
            value.CurrentMagicPoints > value.MaximumMagicPoints ||
            value.CurrentHitPoints > int.MaxValue || value.CurrentMagicPoints > int.MaxValue ||
            value.MaximumHitPoints > int.MaxValue || value.MaximumMagicPoints > int.MaxValue)
        {
            return Blocked("wire.public_beta_compat.player_vitals_out_of_range");
        }

        var record = new byte[PlayerVitalsRecordLength];
        record[0] = PlayerVitalsOpcode;
        WriteU32(record, 1, value.CurrentHitPoints);
        WriteU32(record, 5, value.CurrentMagicPoints);
        WriteU32(record, 9, value.MaximumHitPoints);
        WriteU32(record, 13, value.MaximumMagicPoints);
        return OfficialPlayerSnapshotWireResult<byte[]>.Success(record);
    }

    public static OfficialPlayerSnapshotWireResult<byte[]> EncodePlayerStatus(
        string clientBuildId,
        GameplayProtocolState state,
        OfficialPlayerStatusLayout value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var boundary = Validate(clientBuildId, state, GameplayProtocolState.World);
        if (boundary is not null)
        {
            return boundary;
        }

        if (value.Level <= 0 || value.ExperienceBasisPoints > 10_000 ||
            value.MaximumHitPoints == 0 || value.MaximumMagicPoints == 0 ||
            value.MaximumHitPoints > int.MaxValue || value.MaximumMagicPoints > int.MaxValue)
        {
            return Blocked("wire.public_beta_compat.player_status_out_of_range");
        }

        var record = new byte[PlayerStatusRecordLength];
        record[0] = PlayerStatusOpcode;
        record[1] = unchecked((byte)value.Level);
        record[2] = value.ReservedAfterLevel;
        WriteU16(record, 3, value.UnspentAttributePoints);
        WriteU16(record, 5, value.Constitution);
        WriteU16(record, 7, value.Strength);
        WriteU16(record, 9, value.Intelligence);
        WriteU16(record, 11, value.Speed);
        WriteU16(record, 13, value.ExperienceBasisPoints);
        WriteU16(record, 15, value.ReservedBeforeMaximumVitals);
        WriteU32(record, 17, value.MaximumHitPoints);
        WriteU32(record, 21, value.MaximumMagicPoints);
        return OfficialPlayerSnapshotWireResult<byte[]>.Success(record);
    }

    public static OfficialPlayerSnapshotWireResult<byte[]> EncodeImmortalVitals(
        string clientBuildId,
        GameplayProtocolState state,
        OfficialImmortalVitalsLayout value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var boundary = Validate(clientBuildId, state, GameplayProtocolState.World);
        if (boundary is not null)
        {
            return boundary;
        }

        var record = new byte[ImmortalVitalsRecordLength];
        record[0] = ImmortalVitalsOpcode;
        WriteU32(record, 1, value.CurrentHitPoints);
        WriteU32(record, 5, value.CurrentMagicPoints);
        WriteU32(record, 9, value.MaximumHitPoints);
        WriteU32(record, 13, value.MaximumMagicPoints);
        return OfficialPlayerSnapshotWireResult<byte[]>.Success(record);
    }

    public static OfficialPlayerSnapshotWireResult<byte[]> EncodeImmortalCatalogSelector(
        string clientBuildId,
        GameplayProtocolState state,
        byte selector)
    {
        var boundary = Validate(clientBuildId, state, GameplayProtocolState.World);
        return boundary ?? OfficialPlayerSnapshotWireResult<byte[]>.Success(
            [ImmortalCatalogSelectorOpcode, selector]);
    }

    public static OfficialPlayerSnapshotWireResult<byte[]> EncodeImmortalUiFlags(
        string clientBuildId,
        GameplayProtocolState state,
        byte flags)
    {
        var boundary = Validate(clientBuildId, state, GameplayProtocolState.World);
        return boundary ?? OfficialPlayerSnapshotWireResult<byte[]>.Success(
            [ImmortalUiFlagsOpcode, flags]);
    }

    public static OfficialPlayerSnapshotWireResult<byte[]> EncodeNpcSpawn(
        string clientBuildId,
        GameplayProtocolState state,
        OfficialNpcSpawnLayout value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var boundary = Validate(clientBuildId, state, GameplayProtocolState.World);
        if (boundary is not null)
        {
            return boundary;
        }

        if (value.EntityHandle == 0 || value.ResourceOrdinal == byte.MaxValue ||
            value.ResourceType > 0x1F || value.SelectorHighBits > 0x07 ||
            value.DirectionCode > 0x07 || value.StateCode > 0x1F ||
            value.RawOpaqueBytes.Length != NpcOpaqueLength ||
            value.MapX > 0x7FFF || value.MapY > 0x7FFF || value.PositionMode > 0x03)
        {
            return Blocked("wire.public_beta_compat.npc_spawn_value_out_of_range");
        }

        var record = new byte[NpcSpawnRecordLength];
        record[0] = NpcSpawnOpcode;
        WriteU32(record, 1, value.EntityHandle);
        record[5] = checked((byte)(value.ResourceOrdinal + 1));
        record[6] = checked((byte)((value.SelectorHighBits << 5) | value.ResourceType));
        WriteU16(record, 7, value.RawReservedWord);
        record[9] = checked((byte)((value.DirectionCode << 5) | value.StateCode));
        value.RawOpaqueBytes.Span.CopyTo(record.AsSpan(10, NpcOpaqueLength));
        WriteU32(record, 17,
            checked(((uint)value.MapY << 17) | ((uint)value.MapX << 2) | value.PositionMode));
        return OfficialPlayerSnapshotWireResult<byte[]>.Success(record);
    }

    private static OfficialPlayerSnapshotWireResult<byte[]>? Validate(
        string clientBuildId,
        GameplayProtocolState state,
        GameplayProtocolState requiredState)
    {
        if (!string.Equals(clientBuildId, OfficialBattleCommandWireCodec.ClientBuildId, StringComparison.Ordinal))
        {
            return OfficialPlayerSnapshotWireResult<byte[]>.Failure(
                OfficialPlayerSnapshotWireResultCode.BuildMismatch,
                "wire.public_beta_compat.gameplay_build_mismatch");
        }

        return state != requiredState
            ? OfficialPlayerSnapshotWireResult<byte[]>.Failure(
                OfficialPlayerSnapshotWireResultCode.InvalidState,
                "wire.public_beta_compat.gameplay_state_invalid")
            : null;
    }

    private static OfficialPlayerSnapshotWireResult<byte[]> Blocked(string failureCode) =>
        OfficialPlayerSnapshotWireResult<byte[]>.Failure(
            OfficialPlayerSnapshotWireResultCode.SemanticEvidenceBlocked,
            failureCode);

    private static void WriteU16(Span<byte> bytes, int offset, ushort value) =>
        BinaryPrimitives.WriteUInt16LittleEndian(bytes[offset..], value);

    private static void WriteU32(Span<byte> bytes, int offset, uint value) =>
        BinaryPrimitives.WriteUInt32LittleEndian(bytes[offset..], value);
}
