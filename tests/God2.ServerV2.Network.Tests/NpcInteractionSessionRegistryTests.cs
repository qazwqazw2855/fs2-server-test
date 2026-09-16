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
