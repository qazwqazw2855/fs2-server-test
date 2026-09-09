using God2.ClassicServer.Runtime;

namespace God2.ClassicServer.Runtime.Tests;

public sealed class WorldMovementPersistenceTests
{
    public static TheoryData<WorldPosition3, WorldDirection> EightDirectionDestinations => new()
    {
        { new WorldPosition3(17, 11), WorldDirection.North },
        { new WorldPosition3(17, 13), WorldDirection.South },
        { new WorldPosition3(18, 12), WorldDirection.East },
        { new WorldPosition3(16, 12), WorldDirection.West },
        { new WorldPosition3(18, 11), WorldDirection.NorthEast },
        { new WorldPosition3(16, 11), WorldDirection.NorthWest },
        { new WorldPosition3(18, 13), WorldDirection.SouthEast },
        { new WorldPosition3(16, 13), WorldDirection.SouthWest }
    };

    [Theory]
    [MemberData(nameof(EightDirectionDestinations))]
    public async Task Movement_commit_persists_every_supported_direction(
        WorldPosition3 destination,
        WorldDirection direction)
    {
        var store = CreateStore();

        var result = await store.CommitMovementAsync(
            new WorldMovementPersistenceRequest(
                10,
                19,
                new WorldPosition3(17, 12),
                destination,
                direction,
                DateTimeOffset.UtcNow),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        var persisted = await store.LoadAsync(10, CancellationToken.None);
        Assert.Equal(destination, persisted.RawPosition);
        Assert.Equal(direction, persisted.Direction);
        Assert.Equal(8, persisted.RuntimeVersion);
    }

    [Fact]
    public async Task Movement_commit_advances_position_direction_and_runtime_version_atomically()
    {
        var store = CreateStore();

        var result = await store.CommitMovementAsync(
            new WorldMovementPersistenceRequest(
                10,
                19,
                new WorldPosition3(17, 12),
                new WorldPosition3(17, 11),
                WorldDirection.North,
                DateTimeOffset.UtcNow),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        var persisted = await store.LoadAsync(10, CancellationToken.None);
        Assert.Equal(new WorldPosition3(17, 11), persisted.RawPosition);
        Assert.Equal(WorldDirection.North, persisted.Direction);
        Assert.Equal(8, persisted.RuntimeVersion);
    }

    [Fact]
    public async Task Movement_commit_rejects_stale_expected_position_without_partial_mutation()
    {
        var store = CreateStore();
        var request = new WorldMovementPersistenceRequest(
            10,
            19,
            new WorldPosition3(17, 12),
            new WorldPosition3(17, 11),
            WorldDirection.North,
            DateTimeOffset.UtcNow);
        Assert.True((await store.CommitMovementAsync(request, CancellationToken.None)).Succeeded);

        var replay = await store.CommitMovementAsync(request, CancellationToken.None);

        Assert.False(replay.Succeeded);
        Assert.Equal("movement.position_conflict", replay.Error.Code);
        var persisted = await store.LoadAsync(10, CancellationToken.None);
        Assert.Equal(new WorldPosition3(17, 11), persisted.RawPosition);
        Assert.Equal(8, persisted.RuntimeVersion);
    }

    [Fact]
    public async Task Movement_persistence_failure_leaves_authoritative_location_unchanged()
    {
        var store = CreateStore();
        store.FailureInjection.Point = WorldInteractionFailurePoint.PersistenceFailureBeforeRuntimeMutation;

        var result = await store.CommitMovementAsync(
            new WorldMovementPersistenceRequest(
                10,
                19,
                new WorldPosition3(17, 12),
                new WorldPosition3(17, 11),
                WorldDirection.North,
                DateTimeOffset.UtcNow),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("movement.persistence_failure", result.Error.Code);
        var persisted = await store.LoadAsync(10, CancellationToken.None);
        Assert.Equal(new WorldPosition3(17, 12), persisted.RawPosition);
        Assert.Equal(WorldDirection.Unknown, persisted.Direction);
        Assert.Equal(7, persisted.RuntimeVersion);
    }

    private static InMemoryPortalTransitionStore CreateStore()
    {
        var store = new InMemoryPortalTransitionStore();
        store.Seed(new PortalLocationState(
            10,
            19,
            new WorldPosition3(17, 12),
            WorldDirection.Unknown,
            7,
            null,
            null,
            DateTimeOffset.UtcNow));
        return store;
    }
}
