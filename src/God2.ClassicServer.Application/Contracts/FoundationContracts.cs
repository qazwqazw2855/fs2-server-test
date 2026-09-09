using God2.ClassicServer.Application.Common;
using God2.ClassicServer.Application.Configuration;

namespace God2.ClassicServer.Application.Contracts;

public interface IServerConfigurationLoader
{
    OperationResult<ServerConfiguration> Load();
}

public interface ILocalizer
{
    string Language { get; }

    string Translate(string key);
}

public interface IMariaDbConnectionProbe
{
    Task<OperationResult<DatabaseConnectionReport>> ProbeAsync(DatabaseOptions options, CancellationToken cancellationToken);
}

public interface IMigrationRunner
{
    Task<OperationResult<MigrationReport>> VerifyAsync(CancellationToken cancellationToken);
}

public interface IStaticDataValidator
{
    Task<OperationResult> ValidateAsync(CancellationToken cancellationToken);
}

public interface IStaticDataLoader
{
    Task<OperationResult<IReadOnlyList<StaticDataLoadCount>>> LoadAsync(CancellationToken cancellationToken);
}

public interface IRuntimeCacheBuilder
{
    Task<OperationResult> BuildAsync(IReadOnlyList<StaticDataLoadCount> staticData, CancellationToken cancellationToken);
}

public interface ISessionAuthority
{
    Task<OperationResult> InitializeAsync(CancellationToken cancellationToken);

    Task StopAcceptingLoginsAsync(CancellationToken cancellationToken);
}

public interface INetworkHost
{
    bool IsStarted { get; }

    Task<OperationResult> StartAsync(NetworkOptions options, CancellationToken cancellationToken);

    Task StopAsync(CancellationToken cancellationToken);
}

public interface IShutdownParticipant
{
    string Name { get; }

    Task StopAsync(CancellationToken cancellationToken);
}

public sealed record StaticDataLoadCount(string DataType, int Count);

public sealed record DatabaseConnectionReport(bool DatabaseCreated)
{
    public IReadOnlyList<string> ConsoleLines =>
        DatabaseCreated
            ? ["Database Connected", "Database Created"]
            : ["Database Connected"];
}

public sealed record MigrationReport(IReadOnlyList<MigrationExecution> Migrations)
{
    public IReadOnlyList<string> ConsoleLines =>
        Migrations
            .Select(migration => $"Migration {migration.Version} {migration.Status}")
            .Append("Database Ready")
            .ToArray();
}

public sealed record MigrationExecution(string Version, string Name, MigrationStatus Status);

public enum MigrationStatus
{
    Applied,
    Current
}
