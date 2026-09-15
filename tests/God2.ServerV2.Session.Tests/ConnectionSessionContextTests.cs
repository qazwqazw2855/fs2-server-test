using God2.ServerV2.Session;

namespace God2.ServerV2.Session.Tests;

public sealed class ConnectionSessionContextTests
{
    [Fact]
    public void Dispose_releases_owned_account()
    {
        var registry = new SessionRegistry();
        var context = new ConnectionSessionContext(101, registry);
        context.BindAccount("kero");

        context.Dispose();

        Assert.Equal(0, registry.Count);
        Assert.False(context.HasSession);
    }

    [Fact]
    public void Dispose_is_idempotent()
    {
        var registry = new SessionRegistry();
        var context = new ConnectionSessionContext(101, registry);
        context.BindAccount("kero");

        context.Dispose();
        context.Dispose();

        Assert.Equal(0, registry.Count);
    }

    [Fact]
    public void Duplicate_connection_does_not_release_owner()
    {
        var registry = new SessionRegistry();
        using var owner = new ConnectionSessionContext(101, registry);
        var duplicate = new ConnectionSessionContext(202, registry);

        owner.BindAccount("kero");
        var result = duplicate.BindAccount("kero");
        duplicate.Dispose();

        Assert.Equal(SessionAcquireStatus.DuplicateAccount, result.Status);
        Assert.True(registry.TryGetOwner("kero", out var ownerId));
        Assert.Equal(101, ownerId);
    }

    [Fact]
    public void Owner_can_relogin_after_previous_context_closes()
    {
        var registry = new SessionRegistry();
        var first = new ConnectionSessionContext(101, registry);
        first.BindAccount("kero");
        first.Dispose();

        using var second = new ConnectionSessionContext(202, registry);
        var result = second.BindAccount("kero");

        Assert.Equal(SessionAcquireStatus.Acquired, result.Status);
        Assert.Equal(202, result.OwnerConnectionId);
    }

    [Fact]
    public void Connection_cannot_switch_to_another_account()
    {
        var registry = new SessionRegistry();
        using var context = new ConnectionSessionContext(101, registry);
        context.BindAccount("kero");

        Assert.Throws<InvalidOperationException>(
            () => context.BindAccount("another"));
    }

    [Fact]
    public void Disposed_context_rejects_new_session()
    {
        var registry = new SessionRegistry();
        var context = new ConnectionSessionContext(101, registry);
        context.Dispose();

        Assert.Throws<ObjectDisposedException>(
            () => context.BindAccount("kero"));
    }

    [Fact]
    public void Empty_context_can_close_without_registry_changes()
    {
        var registry = new SessionRegistry();
        var context = new ConnectionSessionContext(101, registry);

        context.Dispose();

        Assert.Equal(0, registry.Count);
    }
}
