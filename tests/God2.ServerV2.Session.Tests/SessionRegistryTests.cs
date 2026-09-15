using God2.ServerV2.Session;

namespace God2.ServerV2.Session.Tests;

public sealed class SessionRegistryTests
{
    [Fact]
    public void First_connection_acquires_account()
    {
        var registry = new SessionRegistry();

        var result = registry.TryAcquire("kero", 101);

        Assert.Equal(SessionAcquireStatus.Acquired, result.Status);
        Assert.Equal(101, result.OwnerConnectionId);
        Assert.Equal(1, registry.Count);
    }

    [Fact]
    public void Same_connection_can_retry_idempotently()
    {
        var registry = new SessionRegistry();
        registry.TryAcquire("kero", 101);

        var result = registry.TryAcquire("kero", 101);

        Assert.Equal(
            SessionAcquireStatus.AlreadyOwnedByConnection,
            result.Status);
        Assert.True(result.Succeeded);
        Assert.Equal(1, registry.Count);
    }

    [Fact]
    public void Different_connection_is_rejected_as_duplicate()
    {
        var registry = new SessionRegistry();
        registry.TryAcquire("kero", 101);

        var result = registry.TryAcquire("kero", 202);

        Assert.Equal(SessionAcquireStatus.DuplicateAccount, result.Status);
        Assert.False(result.Succeeded);
        Assert.Equal(101, result.OwnerConnectionId);
    }

    [Fact]
    public void Account_matching_is_case_insensitive_and_trimmed()
    {
        var registry = new SessionRegistry();
        registry.TryAcquire("  Kero ", 101);

        var result = registry.TryAcquire("kero", 202);

        Assert.Equal(SessionAcquireStatus.DuplicateAccount, result.Status);
        Assert.Equal(101, result.OwnerConnectionId);
    }

    [Fact]
    public void Connection_cannot_release_another_connections_session()
    {
        var registry = new SessionRegistry();
        registry.TryAcquire("kero", 101);

        Assert.False(registry.Release("kero", 202));
        Assert.True(registry.TryGetOwner("kero", out var owner));
        Assert.Equal(101, owner);
    }

    [Fact]
    public void Owner_release_allows_clean_relogin()
    {
        var registry = new SessionRegistry();
        registry.TryAcquire("kero", 101);

        Assert.True(registry.Release("kero", 101));

        var result = registry.TryAcquire("kero", 202);

        Assert.Equal(SessionAcquireStatus.Acquired, result.Status);
        Assert.Equal(202, result.OwnerConnectionId);
        Assert.Equal(1, registry.Count);
    }

    [Fact]
    public void Empty_account_is_rejected()
    {
        var registry = new SessionRegistry();

        Assert.Throws<ArgumentException>(
            () => registry.TryAcquire("   ", 101));
    }
}
