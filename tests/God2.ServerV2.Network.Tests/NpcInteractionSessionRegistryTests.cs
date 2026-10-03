using God2.ServerV2.Application;
using God2.ServerV2.Network;
using God2.ServerV2.Protocol;

namespace God2.ServerV2.Network.Tests;

public sealed class NpcInteractionSessionRegistryTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 16, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Visible_target_is_owned_by_connection()
    {
        var registry =
            new NpcInteractionSessionRegistry();

        var result = registry.TryOpen(
            101,
            1,
            1675308248,
            5042,
            [Npc(5042, 1310005042)],
            Now);

        Assert.True(result.Succeeded);
        Assert.Equal(1, registry.Count);
        Assert.Equal(101, result.Session!.ConnectionId);
        Assert.Equal(1, result.Session.CharacterId);
        Assert.Equal(5042U, result.Session.ClientEntityHandle);
    }

    [Fact]
    public void Target_from_another_map_is_rejected()
    {
        var registry =
            new NpcInteractionSessionRegistry();

        var result = registry.TryOpen(
            101,
            1,
            999,
            5042,
            [Npc(5042, 1310005042)],
            Now);

        Assert.Equal(
            NpcInteractionOpenStatus.TargetNotVisible,
            result.Status);
        Assert.Equal(0, registry.Count);
    }

    [Fact]
    public void Connection_cannot_open_second_interaction()
    {
        var registry =
            new NpcInteractionSessionRegistry();
        var visible = new[]
        {
            Npc(5042, 1310005042),
            Npc(5096, 1310005096)
        };

        registry.TryOpen(
            101, 1, 1675308248, 5042, visible, Now);

        var duplicate = registry.TryOpen(
            101, 1, 1675308248, 5096, visible, Now);

        Assert.Equal(
            NpcInteractionOpenStatus
                .ConnectionAlreadyInteracting,
            duplicate.Status);
        Assert.Equal(5042U,
            duplicate.Session!.ClientEntityHandle);
        Assert.Equal(1, registry.Count);
    }

    [Fact]
    public void Close_requires_owned_handle()
    {
        var registry =
            new NpcInteractionSessionRegistry();

        registry.TryOpen(
            101,
            1,
            1675308248,
            5042,
            [Npc(5042, 1310005042)],
            Now);

        Assert.Equal(
            NpcInteractionCloseStatus.HandleMismatch,
            registry.TryClose(101, 5096, out _));

        Assert.Equal(
            NpcInteractionCloseStatus.Closed,
            registry.TryClose(
                101,
                5042,
                out var closed));

        Assert.Equal(5042U, closed!.ClientEntityHandle);
        Assert.Equal(0, registry.Count);
    }

    [Fact]
    public void Disconnect_removes_only_owned_interaction()
    {
        var registry =
            new NpcInteractionSessionRegistry();

        registry.TryOpen(
            101,
            1,
            1675308248,
            5042,
            [Npc(5042, 1310005042)],
            Now);

        registry.TryOpen(
            202,
            2,
            1675308248,
            5096,
            [Npc(5096, 1310005096)],
            Now);

        Assert.True(
            registry.Remove(101, out var removed));
        Assert.Equal(5042U, removed!.ClientEntityHandle);
        Assert.Equal(1, registry.Count);
    }

    [Fact]
    public void Reopening_same_target_invalidates_previous_identity()
    {
        var registry = new NpcInteractionSessionRegistry();
        var visible = new[] { Npc(5042, 1310005042) };

        var first = registry.TryOpen(
            101, 1, 1675308248, 5042, visible, Now).Session!;
        Assert.NotEqual(Guid.Empty, first.InteractionId);
        Assert.True(registry.TryGetByConnection(101, out var current));
        Assert.Same(first, current);
        Assert.True(registry.IsCurrent(101, first.InteractionId));

        Assert.Equal(NpcInteractionCloseStatus.Closed,
            registry.TryClose(101, 5042, out _));
        Assert.False(registry.TryGetByConnection(101, out _));
        Assert.False(registry.IsCurrent(101, first.InteractionId));

        // Same connection, target and timestamp must still be a new interaction.
        var second = registry.TryOpen(
            101, 1, 1675308248, 5042, visible, Now).Session!;
        Assert.NotEqual(first.InteractionId, second.InteractionId);
        Assert.False(registry.IsCurrent(101, first.InteractionId));
        Assert.True(registry.IsCurrent(101, second.InteractionId));
    }

    [Fact]
    public void Interaction_identity_is_connection_scoped_and_removed_on_disconnect()
    {
        var registry = new NpcInteractionSessionRegistry();
        var first = registry.TryOpen(
            101, 1, 1675308248, 5042,
            [Npc(5042, 1310005042)], Now).Session!;
        var second = registry.TryOpen(
            202, 2, 1675308248, 5096,
            [Npc(5096, 1310005096)], Now).Session!;

        Assert.False(registry.IsCurrent(202, first.InteractionId));
        Assert.False(registry.IsCurrent(101, second.InteractionId));

        Assert.True(registry.Remove(101, out _));
        Assert.False(registry.IsCurrent(101, first.InteractionId));
        Assert.True(registry.IsCurrent(202, second.InteractionId));
    }

    [Fact]
    public void Failed_close_and_duplicate_open_preserve_current_identity()
    {
        var registry = new NpcInteractionSessionRegistry();
        var visible = new[]
        {
            Npc(5042, 1310005042),
            Npc(5096, 1310005096)
        };
        var first = registry.TryOpen(
            101, 1, 1675308248, 5042, visible, Now).Session!;

        Assert.Equal(NpcInteractionCloseStatus.HandleMismatch,
            registry.TryClose(101, 5096, out _));
        var duplicate = registry.TryOpen(
            101, 1, 1675308248, 5096, visible, Now);

        Assert.Equal(NpcInteractionOpenStatus.ConnectionAlreadyInteracting,
            duplicate.Status);
        Assert.Same(first, duplicate.Session);
        Assert.True(registry.IsCurrent(101, first.InteractionId));
    }

    [Fact]
    public void Merchant_policy_checks_authoritative_identity_and_distance()
    {
        var npc = Npc(5042, 1310005042);
        var interaction = new NpcInteractionSession(
            101, 1, npc.MapId, npc.SpawnId, 5042, Now);
        var player = new MerchantInteractionPlayer(
            1, npc.MapId, 74, 124);
        // Test policy only; this does not promote a formal merchant mapping.
        var binding = new MerchantInteractionBinding(
            99, npc.NpcId, npc.SpawnId, npc.MapId, 5042,
            npc.ClientBuildId!, 2, "test-fixture", true);

        MerchantInteractionStatus Check(
            MerchantInteractionPlayer p,
            NpcSnapshotEntry n,
            MerchantInteractionBinding b) =>
            MerchantInteractionPolicy.Check(
                99, interaction.InteractionId, interaction, p, n, b);

        Assert.Equal(MerchantInteractionStatus.Allowed,
            Check(player, npc, binding));
        Assert.Equal(MerchantInteractionStatus.Allowed,
            Check(player with { PositionX = 76 }, npc, binding));
        Assert.Equal(MerchantInteractionStatus.OutOfRange,
            Check(player with { PositionX = 77 }, npc, binding));
        Assert.Equal(MerchantInteractionStatus.CharacterMismatch,
            Check(player with { CharacterId = 2 }, npc, binding));
        Assert.Equal(MerchantInteractionStatus.MapMismatch,
            Check(player with { MapId = 999 }, npc, binding));
        Assert.Equal(MerchantInteractionStatus.NpcMismatch,
            Check(player, npc with { SpawnId = npc.SpawnId + 1 }, binding));
        Assert.Equal(MerchantInteractionStatus.NpcMismatch,
            Check(player, npc with { NpcId = npc.NpcId + 1 }, binding));
        Assert.Equal(MerchantInteractionStatus.NpcMismatch,
            Check(player, npc with { ClientEntityHandle = 5096 }, binding));
        Assert.Equal(MerchantInteractionStatus.ClientBuildMismatch,
            Check(player, npc with { ClientBuildId = "other-build" }, binding));
        Assert.Equal(MerchantInteractionStatus.PositionUnknown,
            Check(player with { PositionX = null }, npc, binding));
        Assert.Equal(MerchantInteractionStatus.PositionUnknown,
            Check(player, npc with { PositionY = null }, binding));
        Assert.Equal(MerchantInteractionStatus.InteractionConflict,
            MerchantInteractionPolicy.Check(
                99, Guid.NewGuid(), interaction, player, npc, binding));
    }

    [Fact]
    public void Merchant_policy_blocks_unapproved_mapping_and_handles_large_coordinates()
    {
        var npc = Npc(5042, 1310005042);
        var interaction = new NpcInteractionSession(
            101, 1, npc.MapId, npc.SpawnId, 5042, Now);
        var player = new MerchantInteractionPlayer(
            1, npc.MapId, 74, 124);
        var binding = new MerchantInteractionBinding(
            99, npc.NpcId, npc.SpawnId, npc.MapId, 5042,
            npc.ClientBuildId!, 2, "test-fixture", true);

        foreach (var blocked in new[]
        {
            binding with { Enabled = false },
            binding with { MerchantId = 100 },
            binding with { EvidenceReference = "" },
            binding with { ClientBuildId = "" },
            binding with { MaximumDistance = null },
            binding with { MaximumDistance = -1 }
        })
        {
            Assert.Equal(MerchantInteractionStatus.BindingBlocked,
                MerchantInteractionPolicy.Check(
                    99, interaction.InteractionId,
                    interaction, player, npc, blocked));
        }

        Assert.Equal(MerchantInteractionStatus.OutOfRange,
            MerchantInteractionPolicy.Check(
                99, interaction.InteractionId, interaction,
                player with { PositionX = int.MinValue },
                npc with { PositionX = int.MaxValue },
                binding with { MaximumDistance = int.MaxValue }));
    }

    private static NpcSnapshotEntry Npc(
        uint handle,
        long spawnId) =>
        new(
            spawnId,
            spawnId - 10000,
            $"NPC-{handle}",
            1675308248,
            74,
            124,
            OfficialNpcSpawnCodec.ClientBuildId,
            handle,
            0,
            45,
            3,
            4,
            1,
            new string('A', 64),
            OfficialNpcSpawnCodec
                .TypeZeroOpaqueTemplateSha256,
            "Derived",
            "test");
}
