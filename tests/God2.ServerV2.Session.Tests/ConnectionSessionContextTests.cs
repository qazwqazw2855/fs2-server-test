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

    [Fact]
    public void World_connection_can_take_ownership_from_login_connection()
    {
        var registry = new SessionRegistry();
        var login = new ConnectionSessionContext(101, registry);
        var world = new ConnectionSessionContext(202, registry);

        login.BindAccount("kero");

        Assert.True(world.TransferOrAcquireFrom("kero", 101));

        Assert.True(registry.TryGetOwner("kero", out var owner));
        Assert.Equal(202, owner);
        Assert.True(world.HasSession);

        // Login context is no longer the registry owner.
        // Disposing it must not release the world session.
        login.Dispose();

        Assert.True(registry.TryGetOwner("kero", out owner));
        Assert.Equal(202, owner);

        world.Dispose();

        Assert.Equal(0, registry.Count);
    }

    [Fact]
    public void Transfer_rejects_wrong_login_connection_without_claiming_session()
    {
        var registry = new SessionRegistry();
        using var login = new ConnectionSessionContext(101, registry);
        using var world = new ConnectionSessionContext(202, registry);

        login.BindAccount("kero");

        Assert.False(world.TransferOrAcquireFrom("kero", 999));

        Assert.False(world.HasSession);
        Assert.True(registry.TryGetOwner("kero", out var owner));
        Assert.Equal(101, owner);
    }


    [Fact]
    public void World_connection_can_acquire_after_login_connection_already_closed()
    {
        var registry = new SessionRegistry();
        var login = new ConnectionSessionContext(101, registry);

        login.BindAccount("kero");
        login.Dispose();

        using var world = new ConnectionSessionContext(202, registry);

        Assert.True(world.TransferOrAcquireFrom("kero", 101));

        Assert.True(world.HasSession);
        Assert.True(registry.TryGetOwner("kero", out var owner));
        Assert.Equal(202, owner);
    }

    [Fact]
    public void World_connection_cannot_steal_session_from_third_connection()
    {
        var registry = new SessionRegistry();

        var login = new ConnectionSessionContext(101, registry);
        login.BindAccount("kero");
        login.Dispose();

        using var replacement = new ConnectionSessionContext(303, registry);
        var replacementResult = replacement.BindAccount("kero");

        Assert.True(replacementResult.Succeeded);

        using var world = new ConnectionSessionContext(202, registry);

        Assert.False(world.TransferOrAcquireFrom("kero", 101));

        Assert.False(world.HasSession);
        Assert.True(registry.TryGetOwner("kero", out var owner));
        Assert.Equal(303, owner);
    }

}
