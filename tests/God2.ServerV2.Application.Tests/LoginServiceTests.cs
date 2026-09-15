using God2.ServerV2.Application;
using God2.ServerV2.Session;

namespace God2.ServerV2.Application.Tests;

public sealed class LoginServiceTests
{
    [Fact]
    public async Task Valid_credentials_acquire_session()
    {
        var registry = new SessionRegistry();
        using var connection = new ConnectionSessionContext(101, registry);
        var service = new LoginService(new StubAuthenticator(true));

        var result = await service.AuthenticateAsync(
            "kero",
            "secret".AsMemory(),
            connection,
            CancellationToken.None);

        Assert.Equal(LoginResultCode.Success, result.Code);
        Assert.True(connection.HasSession);
        Assert.Equal(1, registry.Count);
    }

    [Fact]
    public async Task Invalid_credentials_do_not_acquire_session()
    {
        var registry = new SessionRegistry();
        using var connection = new ConnectionSessionContext(101, registry);
        var service = new LoginService(new StubAuthenticator(false));

        var result = await service.AuthenticateAsync(
            "kero",
            "wrong".AsMemory(),
            connection,
            CancellationToken.None);

        Assert.Equal(LoginResultCode.CredentialsRejected, result.Code);
        Assert.False(connection.HasSession);
        Assert.Equal(0, registry.Count);
    }

    [Fact]
    public async Task Empty_password_is_rejected_without_calling_authenticator()
    {
        var registry = new SessionRegistry();
        using var connection = new ConnectionSessionContext(101, registry);
        var authenticator = new StubAuthenticator(true);
        var service = new LoginService(authenticator);

        var result = await service.AuthenticateAsync(
            "kero",
            ReadOnlyMemory<char>.Empty,
            connection,
            CancellationToken.None);

        Assert.Equal(LoginResultCode.CredentialsRejected, result.Code);
        Assert.Equal(0, authenticator.CallCount);
    }

    [Fact]
    public async Task Duplicate_account_returns_existing_connection()
    {
        var registry = new SessionRegistry();
        using var first = new ConnectionSessionContext(101, registry);
        using var second = new ConnectionSessionContext(202, registry);
        var service = new LoginService(new StubAuthenticator(true));

        await service.AuthenticateAsync(
            "kero",
            "secret".AsMemory(),
            first,
            CancellationToken.None);

        var result = await service.AuthenticateAsync(
            "KERO",
            "secret".AsMemory(),
            second,
            CancellationToken.None);

        Assert.Equal(LoginResultCode.DuplicateLogin, result.Code);
        Assert.Equal(101, result.ExistingConnectionId);
        Assert.False(second.HasSession);
    }

    [Fact]
    public async Task Session_is_released_when_authenticated_connection_closes()
    {
        var registry = new SessionRegistry();
        var connection = new ConnectionSessionContext(101, registry);
        var service = new LoginService(new StubAuthenticator(true));

        await service.AuthenticateAsync(
            "kero",
            "secret".AsMemory(),
            connection,
            CancellationToken.None);

        connection.Dispose();

        Assert.Equal(0, registry.Count);
    }

    [Fact]
    public async Task Cancellation_is_forwarded_to_authenticator()
    {
        var registry = new SessionRegistry();
        using var connection = new ConnectionSessionContext(101, registry);
        var service = new LoginService(
            new CancellingAuthenticator());

        await Assert.ThrowsAsync<OperationCanceledException>(
            async () => await service.AuthenticateAsync(
                "kero",
                "secret".AsMemory(),
                connection,
                new CancellationToken(true)));
    }

    private sealed class StubAuthenticator(bool accepted)
        : IAccountAuthenticator
    {
        public int CallCount { get; private set; }

        public ValueTask<bool> ValidateCredentialsAsync(
            string accountName,
            ReadOnlyMemory<char> password,
            CancellationToken cancellationToken)
        {
            CallCount++;
            return ValueTask.FromResult(accepted);
        }
    }

    private sealed class CancellingAuthenticator
        : IAccountAuthenticator
    {
        public ValueTask<bool> ValidateCredentialsAsync(
            string accountName,
            ReadOnlyMemory<char> password,
            CancellationToken cancellationToken) =>
            ValueTask.FromCanceled<bool>(cancellationToken);
    }
}
