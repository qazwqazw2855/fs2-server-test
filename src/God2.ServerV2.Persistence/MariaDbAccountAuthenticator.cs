using God2.ServerV2.Application;
using MySqlConnector;

namespace God2.ServerV2.Persistence;

public sealed record MariaDbAuthenticationOptions(
    string Host,
    int Port,
    string Username,
    string Password)
{
    public string BuildConnectionString()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Host);
        ArgumentException.ThrowIfNullOrWhiteSpace(Username);
        ArgumentException.ThrowIfNullOrWhiteSpace(Password);

        if (Port is < 1 or > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(Port));
        }

        return new MySqlConnectionStringBuilder
        {
            Server = Host,
            Port = checked((uint)Port),
            UserID = Username,
            Password = Password,
            Database = "god2_player",
            ConnectionTimeout = 5,
            DefaultCommandTimeout = 5,
            SslMode = MySqlSslMode.None,
            Pooling = true,
            MinimumPoolSize = 0,
            MaximumPoolSize = 20
        }.ConnectionString;
    }
}

public sealed class MariaDbAccountAuthenticator : IAccountAuthenticator
{
    private readonly string _connectionString;
    private readonly IPasswordHashVerifier _passwordVerifier;

    public MariaDbAccountAuthenticator(
        MariaDbAuthenticationOptions options,
        IPasswordHashVerifier passwordVerifier)
    {
        ArgumentNullException.ThrowIfNull(options);
        _connectionString = options.BuildConnectionString();
        _passwordVerifier = passwordVerifier ??
            throw new ArgumentNullException(nameof(passwordVerifier));
    }

    public async ValueTask<AccountAuthenticationResult> ValidateCredentialsAsync(
        string accountName,
        ReadOnlyMemory<char> password,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accountName);

        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                account_id,
                password_hash,
                status,
                locked_until_utc
            FROM god2_player.accounts
            WHERE username = @username
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("@username", accountName.Trim());

        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);

        if (!await reader.ReadAsync(cancellationToken))
        {
            return AccountAuthenticationResult.Rejected;
        }

        var accountIdOrdinal = reader.GetOrdinal("account_id");
        var passwordHashOrdinal = reader.GetOrdinal("password_hash");
        var statusOrdinal = reader.GetOrdinal("status");
        var lockedUntilUtcOrdinal = reader.GetOrdinal("locked_until_utc");

        var status = reader.GetString(statusOrdinal);
        var enabled =
            string.Equals(status, "啟用", StringComparison.Ordinal) ||
            string.Equals(status, "Active", StringComparison.OrdinalIgnoreCase);

        if (!enabled)
        {
            return AccountAuthenticationResult.Rejected;
        }

        if (!reader.IsDBNull(lockedUntilUtcOrdinal))
        {
            var lockedUntilUtc = reader.GetDateTime(lockedUntilUtcOrdinal);

            if (lockedUntilUtc > DateTime.UtcNow)
            {
                return AccountAuthenticationResult.Rejected;
            }
        }

        var passwordHash = reader.GetString(passwordHashOrdinal);

        return _passwordVerifier.Verify(password, passwordHash)
            ? AccountAuthenticationResult.Accepted(
                reader.GetInt64(accountIdOrdinal))
            : AccountAuthenticationResult.Rejected;
    }
}
