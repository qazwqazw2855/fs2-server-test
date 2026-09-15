using God2.ServerV2.Session;

namespace God2.ServerV2.Application;

public readonly record struct AccountAuthenticationResult(
    bool Succeeded,
    long? AccountId)
{
    public static AccountAuthenticationResult Rejected => new(false, null);

    public static AccountAuthenticationResult Accepted(long accountId)
    {
        if (accountId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(accountId));
        }

        return new AccountAuthenticationResult(true, accountId);
    }
}

public interface IAccountAuthenticator
{
    ValueTask<AccountAuthenticationResult> ValidateCredentialsAsync(
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
    long? AccountId = null,
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

        var authentication = await _authenticator.ValidateCredentialsAsync(
            accountName.Trim(),
            password,
            cancellationToken);

        if (!authentication.Succeeded ||
            authentication.AccountId is not long accountId)
        {
            return new LoginResult(
                LoginResultCode.CredentialsRejected);
        }

        var session = connection.BindAccount(accountName);

        return session.Status switch
        {
            SessionAcquireStatus.Acquired =>
                new LoginResult(
                    LoginResultCode.Success,
                    AccountId: accountId),

            SessionAcquireStatus.AlreadyOwnedByConnection =>
                new LoginResult(
                    LoginResultCode.Success,
                    AccountId: accountId),

            SessionAcquireStatus.DuplicateAccount =>
                new LoginResult(
                    LoginResultCode.DuplicateLogin,
                    AccountId: accountId,
                    ExistingConnectionId: session.OwnerConnectionId),

            _ => throw new InvalidOperationException(
                $"Unknown session acquisition status: {session.Status}.")
        };
    }
}
