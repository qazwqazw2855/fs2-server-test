using God2.ClassicServer.Application.Common;
using God2.ClassicServer.Protocol;

namespace God2.ClassicServer.Runtime;

public sealed record OfficialOwnedImmortalWireState(
    long ImmortalInstanceId,
    byte ResourceId,
    byte Level,
    long CurrentHp,
    long MaximumHp,
    long CurrentMp,
    long MaximumMp,
    ushort Strength,
    ushort Constitution,
    ushort Intelligence,
    ushort Speed,
    ushort Metal,
    ushort Wood,
    ushort Water,
    ushort Fire,
    ushort Earth,
    bool IsActive);

public static class OfficialImmortalReplicationWireCodec
{
    public const string ClientBuildId = "god2-opt-6b127086e0c0";
    public const string ClientSha256 = "6B127086E0C00014DE26137B4EC482801E06E0724C5C05C64561D7F9FF32BD9B";
    public const string RuntimeImageSha256 = "EBC80CB5C86B8A91EA0CA3B3E5112ABD879493C7DDFE22134B9CA1B5F3F791AB";
    public const byte UpdateOpcode = 0x31;
    public const byte VitalUpdateOpcode = 0x39;
    public const int FrozenLoginApplicationRecordLength = 29;
    public const int FrozenVitalApplicationRecordLength = 17;
    public const byte VerifiedLoginWujiResourceId = 17;
    public const int MaximumVerifiedLoginImmortals = 1;
    private static readonly byte[] FrozenLoginWujiApplicationRecord =
        Convert.FromHexString("31110200001600000000000000040100001B0029000D00060083090000");
    private static readonly byte[] FrozenVitalApplicationRecord =
        Convert.FromHexString("391C020000CF0000001C020000CF000000");

    public static OperationResult<byte[]> BuildAuthoritativeLoginApplicationRecord(
        IReadOnlyList<OfficialOwnedImmortalWireState> immortals)
    {
        ArgumentNullException.ThrowIfNull(immortals);
        var validation = Validate(immortals);
        if (validation is not null)
        {
            return OperationResult<byte[]>.Failure(
                "immortal_projection.invalid_state",
                validation,
                nameof(OfficialImmortalReplicationWireCodec));
        }

        if (immortals.Count == 0)
        {
            return OperationResult<byte[]>.Success([]);
        }

        var immortal = immortals[0];
        var encoded = PublicBetaCompatibilityGameplayWireAdapter.EncodeImmortalStatus(
            ClientBuildId,
            GameplayProtocolState.World,
            new OfficialImmortalStatusLayout(
                immortal.ResourceId,
                immortal.Level,
                immortal.Metal,
                immortal.Wood,
                immortal.Water,
                immortal.Fire,
                immortal.Earth,
                RawReservedValue12: 0x00000104,
                Vitality: checked((short)immortal.Strength),
                Strength: checked((short)immortal.Constitution),
                Intelligence: checked((short)immortal.Intelligence),
                Speed: checked((short)immortal.Speed),
                ExperienceBasisPoints: 0,
                StatusFlag: false,
                RawReservedValue26: 0));
        return encoded.LayoutDecoded && encoded.Value is not null
            ? OperationResult<byte[]>.Success(encoded.Value)
            : OperationResult<byte[]>.Failure(
                "immortal_projection.wire_adapter_blocked",
                encoded.FailureCode,
                nameof(PublicBetaCompatibilityGameplayWireAdapter));
    }

    public static OperationResult<byte[]> BuildAuthoritativeVitalApplicationRecord(
        IReadOnlyList<OfficialOwnedImmortalWireState> immortals)
    {
        ArgumentNullException.ThrowIfNull(immortals);
        var validation = Validate(immortals);
        if (validation is not null)
        {
            return OperationResult<byte[]>.Failure(
                "immortal_projection.invalid_state",
                validation,
                nameof(OfficialImmortalReplicationWireCodec));
        }

        if (immortals.Count == 0)
        {
            return OperationResult<byte[]>.Success([]);
        }

        var immortal = immortals[0];
        var encoded = PublicBetaCompatibilityGameplayWireAdapter.EncodeImmortalVitals(
            ClientBuildId,
            GameplayProtocolState.World,
            new OfficialImmortalVitalsLayout(
                checked((uint)immortal.CurrentHp),
                checked((uint)immortal.CurrentMp),
                checked((uint)immortal.MaximumHp),
                checked((uint)immortal.MaximumMp)));
        return encoded.LayoutDecoded && encoded.Value is not null
            ? OperationResult<byte[]>.Success(encoded.Value)
            : OperationResult<byte[]>.Failure(
                "immortal_projection.wire_adapter_blocked",
                encoded.FailureCode,
                nameof(PublicBetaCompatibilityGameplayWireAdapter));
    }

    public static bool IsFrozenLoginApplicationRecord(ReadOnlySpan<byte> record) =>
        record.SequenceEqual(FrozenLoginWujiApplicationRecord);

    public static bool IsFrozenVitalApplicationRecord(ReadOnlySpan<byte> record) =>
        record.SequenceEqual(FrozenVitalApplicationRecord);

    public static string? Validate(IReadOnlyList<OfficialOwnedImmortalWireState> immortals)
    {
        ArgumentNullException.ThrowIfNull(immortals);
        if (immortals.Count > MaximumVerifiedLoginImmortals)
        {
            return "The exact-build login bootstrap has verified support for one built-in Wuji record only.";
        }

        if (immortals.Count == 1 && !immortals[0].IsActive)
        {
            return "The single verified login Wuji record must be active.";
        }

        var instanceIds = new HashSet<long>();
        foreach (var immortal in immortals)
        {
            if (immortal.ImmortalInstanceId <= 0 || !instanceIds.Add(immortal.ImmortalInstanceId))
            {
                return "Owned immortal instance identities must be positive and unique.";
            }

            // The frozen exact-build 0x2B bootstrap application record begins
            // `31 11 02 ...` and renders the built-in Wuji. FightEny.csvZ identity
            // 95 belongs to a different resource domain and cannot be used here.
            if (immortal.ResourceId != VerifiedLoginWujiResourceId)
            {
                return $"Only exact-build Wuji login resource {VerifiedLoginWujiResourceId} is verified.";
            }

            if (immortal.Level == 0)
            {
                return "Owned immortal level must be in the official one-byte range 1..255.";
            }

            if (immortal.Strength > short.MaxValue ||
                immortal.Constitution > short.MaxValue ||
                immortal.Intelligence > short.MaxValue ||
                immortal.Speed > short.MaxValue)
            {
                return "Owned immortal base abilities must fit the exact signed 16-bit opcode 0x31 fields.";
            }

            if (immortal.MaximumHp <= 0 || immortal.MaximumHp > uint.MaxValue ||
                immortal.CurrentHp < 0 || immortal.CurrentHp > immortal.MaximumHp)
            {
                return "Owned immortal HP must fit the exact opcode 0x39 uint32 fields and current HP must be within maximum HP.";
            }

            if (immortal.MaximumMp < 0 || immortal.MaximumMp > ushort.MaxValue ||
                immortal.CurrentMp < 0 || immortal.CurrentMp > immortal.MaximumMp)
            {
                return "Owned immortal MP must fit the exact opcode 0x39 uint16 fields and current MP must be within maximum MP.";
            }
        }

        return null;
    }
}
