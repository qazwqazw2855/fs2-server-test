using System.Buffers.Binary;
using System.Security.Cryptography;
using God2.ClassicServer.Protocol;

namespace God2.ClassicServer.Runtime;

public static class OfficialNpcReplicationWireCodec
{
    public const string ClientBuildId = "god2-opt-6b127086e0c0";
    public const string ClientSha256 = "6B127086E0C00014DE26137B4EC482801E06E0724C5C05C64561D7F9FF32BD9B";
    public const string OpaqueTemplateSha256 = "C0CBF5CDFAC3669D695E66D9C789AAA027360488024C1C5E4323311364A419C0";
    public const string LiveStageMerchantOpaqueTemplateSha256 = "C1D92D6C8E358E6E894389F60CB522CBB67429566A58C2B66DEBC872FB9E7838";
    public const byte SpawnOpcode = 0x72;
    public const byte PositionUpdateOpcode = 0x60;
    public const byte DespawnOpcode = 0x75;

    private static readonly byte[] PreservedOpaqueTemplate = Convert.FromHexString("EA680FC2034A01");
    private static readonly byte[] PreservedIgnoredRegions = Convert.FromHexString("0000EA680FC2034A01");

    private static readonly IReadOnlyDictionary<uint, VerifiedSpawnProfile> VerifiedSpawnProfiles =
        new Dictionary<uint, VerifiedSpawnProfile>
        {
            [1504] = new(
                2,
                142,
                3,
                4,
                1,
                2,
                "EA680FC2034A01",
                OpaqueTemplateSha256,
                true,
                "83D11BE70AB2E0D6660BC38915E8C3C101FFFC2C67D7A950453E24D79669A84B"),
            [4638] = new(
                2,
                142,
                2,
                5,
                1,
                2,
                "EA680FC2034A01",
                OpaqueTemplateSha256,
                true,
                "7E3AC167DEF745DE3BE7D38C22B4C0ADC1419E6DC20851F8FA3CD22D61CCA644"),
            [3954] = new(
                0,
                81,
                3,
                4,
                1,
                0,
                "000000CF010000",
                LiveStageMerchantOpaqueTemplateSha256,
                false,
                "F53E8D79A02FB96A528E9ABF334E5D1383018780BA79D62340549FC5AD36885B"),
            [3793] = new(
                0,
                87,
                3,
                5,
                1,
                0,
                "000000CF010000",
                LiveStageMerchantOpaqueTemplateSha256,
                false,
                "FAEB2E6FEC5D9B23F9A06143085535443473A8D63A8C0165BA43CF9586F8FFFF")
        };

    public static RuntimeSerializationResult SerializeSpawn(NpcState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var validation = Validate(state);
        if (validation is not null)
        {
            return RuntimeSerializationResult.Blocked(RuntimeSerializerKind.Npc, validation);
        }

        var identity = state.WireIdentity!;
        _ = TryResolveSpawnProfile(identity, out var profile);
        var adapted = PublicBetaCompatibilityGameplayWireAdapter.EncodeNpcSpawn(
            ClientBuildId,
            GameplayProtocolState.World,
            new OfficialNpcSpawnLayout(
                identity.EntityHandle,
                identity.ResourceOrdinal,
                identity.ResourceType,
                identity.SelectorHighBits,
                RawReservedWord: 0,
                identity.DirectionCode,
                identity.StateCode,
                profile.PreservedOpaqueTemplate,
                checked((ushort)state.Position.X),
                checked((ushort)state.Position.Y),
                profile.PositionMode));
        if (!adapted.LayoutDecoded || adapted.Value is null)
        {
            return RuntimeSerializationResult.Blocked(
                RuntimeSerializerKind.Npc,
                $"NPC spawn compatibility adapter rejected the runtime projection: {adapted.FailureCode}");
        }

        var applicationRecord = adapted.Value;
        var decoded = new byte[applicationRecord.Length + 3];
        BinaryPrimitives.WriteUInt16LittleEndian(decoded, checked((ushort)decoded.Length));
        applicationRecord.CopyTo(decoded, 2);
        decoded[^1] = OfficialLoginWireTransform.ComputeChecksum(decoded);

        if (string.Equals(identity.EvidenceStatus, "Derived", StringComparison.Ordinal) ||
            IsCapturedPosition(identity.EntityHandle, state.Position))
        {
            var actualHash = Convert.ToHexString(SHA256.HashData(decoded.AsSpan(2, 21)));
            if (!string.Equals(actualHash, identity.SpawnMessageSha256, StringComparison.OrdinalIgnoreCase))
            {
                Array.Clear(decoded);
                return RuntimeSerializationResult.Blocked(
                    RuntimeSerializerKind.Npc,
                    "NPC spawn bytes no longer match the build-pinned application-message hash at the captured position.");
            }
        }

        var encoded = OfficialClientWorldProtocolFrames.EncodeWorldServerPayload(decoded);
        Array.Clear(applicationRecord);
        Array.Clear(decoded);
        return RuntimeSerializationResult.Success(
            RuntimeSerializerKind.Npc,
            encoded,
            "Official S2C 0x72 NPC spawn projected from MariaDB-backed runtime state; identity/selector/opaque bytes are build-pinned and only the proven packed position is dynamic.");
    }

    public static RuntimeSerializationResult SerializePositionUpdate(NpcState state, ushort interpolationMilliseconds = 1000)
    {
        ArgumentNullException.ThrowIfNull(state);
        var validation = Validate(state);
        if (validation is not null)
        {
            return RuntimeSerializationResult.Blocked(RuntimeSerializerKind.Npc, validation);
        }

        if (interpolationMilliseconds == 0)
        {
            return RuntimeSerializationResult.Blocked(
                RuntimeSerializerKind.Npc,
                "NPC position update requires a non-zero interpolation interval proven by the Client 0x60 consumer.");
        }

        var identity = state.WireIdentity!;
        if (!TryResolveVerifiedProfile(identity, out var profile) || !profile.PositionUpdateSupported)
        {
            return RuntimeSerializationResult.Blocked(
                RuntimeSerializerKind.Npc,
                "NPC position update is blocked because this capture-backed profile has no observed update template.");
        }

        var decoded = CreateEntityFrame(24, PositionUpdateOpcode, identity.EntityHandle);
        decoded[7] = 0x15;
        decoded[8] = 0xA0;
        BinaryPrimitives.WriteUInt16LittleEndian(decoded.AsSpan(9, sizeof(ushort)), interpolationMilliseconds);
        decoded[11] = 0x02;
        PreservedOpaqueTemplate.CopyTo(decoded, 12);
        BinaryPrimitives.WriteUInt32LittleEndian(decoded.AsSpan(19, sizeof(uint)), PackPosition(state.Position));
        decoded[^1] = OfficialLoginWireTransform.ComputeChecksum(decoded);

        var encoded = OfficialClientWorldProtocolFrames.EncodeWorldServerPayload(decoded);
        Array.Clear(decoded);
        return RuntimeSerializationResult.Success(
            RuntimeSerializerKind.Npc,
            encoded,
            "Official S2C 0x60 entity-position consumer is statically verified for handle, interpolation, state and packed coordinates; non-dynamic bytes preserve the build-pinned observed template.");
    }

    public static RuntimeSerializationResult SerializeDespawn(NpcState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var validation = Validate(state);
        if (validation is not null)
        {
            return RuntimeSerializationResult.Blocked(RuntimeSerializerKind.Npc, validation);
        }

        if (!TryResolveVerifiedProfile(state.WireIdentity!, out var profile) || !profile.PositionUpdateSupported)
        {
            return RuntimeSerializationResult.Blocked(
                RuntimeSerializerKind.Npc,
                "NPC despawn is blocked because this capture-backed profile has no observed lifecycle frame.");
        }

        var decoded = CreateEntityFrame(8, DespawnOpcode, state.WireIdentity!.EntityHandle);
        decoded[^1] = OfficialLoginWireTransform.ComputeChecksum(decoded);
        var encoded = OfficialClientWorldProtocolFrames.EncodeWorldServerPayload(decoded);
        Array.Clear(decoded);
        return RuntimeSerializationResult.Success(
            RuntimeSerializerKind.Npc,
            encoded,
            "Official S2C 0x75 fixed-length entity removal branch is statically verified and keyed by the MariaDB-backed official entity handle.");
    }

    public static string? Validate(NpcState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (!state.Enabled)
        {
            return "Disabled NPC content cannot be replicated.";
        }

        if (state.WireIdentity is not { } identity)
        {
            return "The server-to-client NPC serializer is blocked by evidence because no official Client entity selector is available.";
        }

        if (!string.Equals(identity.ClientBuildId, ClientBuildId, StringComparison.Ordinal))
        {
            return "NPC wire identity is not pinned to the supported official Client build.";
        }

        if (identity.EvidenceStatus is not ("Verified" or "Derived"))
        {
            return "NPC wire identity is neither Verified nor Derived.";
        }

        if (!TryResolveSpawnProfile(identity, out var profile))
        {
            return "NPC wire identity does not match a build-pinned, uniquely cross-matched official spawn profile.";
        }

        if (identity.EntityHandle is 0 or > ushort.MaxValue)
        {
            return "NPC official entity handle is outside the verified Client lookup range.";
        }

        if (state.Position.X is < 0 or > 0x7FFF || state.Position.Y is < 0 or > 0x7FFF)
        {
            return "NPC coordinates are outside the verified 15-bit packed coordinate range.";
        }

        Span<byte> ignoredRegions = stackalloc byte[9];
        profile.PreservedOpaqueTemplate.CopyTo(ignoredRegions[2..]);
        if (!string.Equals(
                Convert.ToHexString(SHA256.HashData(ignoredRegions)),
                profile.OpaqueTemplateSha256,
                StringComparison.Ordinal) ||
            !string.Equals(identity.OpaqueTemplateSha256, profile.OpaqueTemplateSha256, StringComparison.OrdinalIgnoreCase))
        {
            return "NPC build-pinned opaque template integrity check failed.";
        }

        return null;
    }

    private static bool TryResolveSpawnProfile(OfficialNpcWireIdentity identity, out SpawnProfile profile)
    {
        if (string.Equals(identity.EvidenceStatus, "Verified", StringComparison.Ordinal))
        {
            return TryResolveVerifiedProfile(identity, out profile);
        }

        profile = identity.ResourceType switch
        {
            0 => new SpawnProfile(0, identity.ResourceOrdinal, 3, 4, 1, 0, "000000CF010000", LiveStageMerchantOpaqueTemplateSha256, false, null),
            2 => new SpawnProfile(2, identity.ResourceOrdinal, 3, 4, 1, 2, "EA680FC2034A01", OpaqueTemplateSha256, false, null),
            _ => null!
        };

        return profile is not null &&
            identity.SelectorHighBits == profile.SelectorHighBits &&
            identity.DirectionCode == profile.DirectionCode &&
            identity.StateCode == profile.StateCode;
    }

    private static bool TryResolveVerifiedProfile(OfficialNpcWireIdentity identity, out SpawnProfile profile)
    {
        if (VerifiedSpawnProfiles.TryGetValue(identity.EntityHandle, out var verified) &&
            identity.ResourceType == verified.ResourceType &&
            identity.ResourceOrdinal == verified.ResourceOrdinal &&
            identity.SelectorHighBits == verified.SelectorHighBits &&
            identity.DirectionCode == verified.DirectionCode &&
            identity.StateCode == verified.StateCode &&
            string.Equals(identity.SpawnMessageSha256, verified.ApplicationMessageSha256, StringComparison.OrdinalIgnoreCase))
        {
            profile = verified;
            return true;
        }

        profile = null!;
        return false;
    }

    private static byte[] CreateEntityFrame(int length, byte opcode, uint entityHandle)
    {
        var decoded = new byte[length];
        BinaryPrimitives.WriteUInt16LittleEndian(decoded, checked((ushort)length));
        decoded[2] = opcode;
        BinaryPrimitives.WriteUInt32LittleEndian(decoded.AsSpan(3, sizeof(uint)), entityHandle);
        return decoded;
    }

    private static uint PackPosition(WorldPosition3 position, byte positionMode = 2) =>
        checked(((uint)position.Y << 17) | ((uint)position.X << 2) | positionMode);

    private static bool IsCapturedPosition(uint entityHandle, WorldPosition3 position) =>
        entityHandle switch
        {
            1504 => position.X == 17 && position.Y == 8,
            4638 => position.X == 14 && position.Y == 14,
            3954 => position.X == 240 && position.Y == 54,
            3793 => position.X == 65 && position.Y == 61,
            _ => false
        };

    private record SpawnProfile(
        byte ResourceType,
        byte ResourceOrdinal,
        byte SelectorHighBits,
        byte DirectionCode,
        byte StateCode,
        byte PositionMode,
        string PreservedOpaqueHex,
        string OpaqueTemplateSha256,
        bool PositionUpdateSupported,
        string? ApplicationMessageSha256)
    {
        public byte[] PreservedOpaqueTemplate => Convert.FromHexString(PreservedOpaqueHex);
    }

    private sealed record VerifiedSpawnProfile(
        byte ResourceType,
        byte ResourceOrdinal,
        byte SelectorHighBits,
        byte DirectionCode,
        byte StateCode,
        byte PositionMode,
        string PreservedOpaqueHex,
        string OpaqueTemplateSha256,
        bool PositionUpdateSupported,
        string ApplicationMessageSha256)
        : SpawnProfile(
            ResourceType,
            ResourceOrdinal,
            SelectorHighBits,
            DirectionCode,
            StateCode,
            PositionMode,
            PreservedOpaqueHex,
            OpaqueTemplateSha256,
            PositionUpdateSupported,
            ApplicationMessageSha256);
}
