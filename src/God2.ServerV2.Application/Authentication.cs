using God2.ServerV2.Session;

namespace God2.ServerV2.Application;

public interface IAccountAuthenticator
{
    ValueTask<bool> ValidateCredentialsAsync(
        string accountName,
        ReadOnlyMemory<char> password,
        CancellationToken cancellationToken);
}

public enum LoginResultCode
{
    Success,
    CredentialsRejected,
    DuplicateLogin
}

public readonly record struct LoginResult(
    LoginResultCode Code,
    long? ExistingConnectionId = null)
{
    public bool Succeeded => Code == LoginResultCode.Success;
}

public sealed class LoginService
{
    private readonly IAccountAuthenticator _authenticator;

    public LoginService(IAccountAuthenticator authenticator)
    {
        _authenticator = authenticator ??
            throw new ArgumentNullException(nameof(authenticator));
    }

    public async ValueTask<LoginResult> AuthenticateAsync(
        string accountName,
        ReadOnlyMemory<char> password,
        ConnectionSessionContext connection,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accountName);
        ArgumentNullException.ThrowIfNull(connection);

        if (password.IsEmpty)
        {
            return new LoginResult(
                LoginResultCode.CredentialsRejected);
        }

        var accepted = await _authenticator.ValidateCredentialsAsync(
            accountName.Trim(),
            password,
            cancellationToken);

        if (!accepted)
        {
            return new LoginResult(
                LoginResultCode.CredentialsRejected);
        }

        var session = connection.BindAccount(accountName);

        return session.Status switch
        {
            SessionAcquireStatus.Acquired =>
                new LoginResult(LoginResultCode.Success),

            SessionAcquireStatus.AlreadyOwnedByConnection =>
                new LoginResult(LoginResultCode.Success),

            SessionAcquireStatus.DuplicateAccount =>
                new LoginResult(
                    LoginResultCode.DuplicateLogin,
                    session.OwnerConnectionId),

            _ => throw new InvalidOperationException(
                $"Unknown session acquisition status: {session.Status}.")
        };
    }
}
