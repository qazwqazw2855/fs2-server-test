namespace God2.ServerV2.Application;

public sealed class RejectAllAccountAuthenticator
    : IAccountAuthenticator
{
    public ValueTask<bool> ValidateCredentialsAsync(
        string accountName,
        ReadOnlyMemory<char> password,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(false);
    }
}
