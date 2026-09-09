using God2.ClassicServer.Application.Common;
using God2.ClassicServer.Application.Configuration;
using God2.ClassicServer.Application.Contracts;

namespace God2.ClassicServer.Application.Startup;

public sealed class StartupPipeline
{
    private readonly IMariaDbConnectionProbe _connectionProbe;
    private readonly IMigrationRunner _migrationRunner;
    private readonly IStaticDataValidator _staticDataValidator;
    private readonly IStaticDataLoader _staticDataLoader;
    private readonly IRuntimeCacheBuilder _runtimeCacheBuilder;
    private readonly ISessionAuthority _sessionAuthority;
    private readonly INetworkHost _networkHost;

    public StartupPipeline(
        IMariaDbConnectionProbe connectionProbe,
        IMigrationRunner migrationRunner,
        IStaticDataValidator staticDataValidator,
        IStaticDataLoader staticDataLoader,
        IRuntimeCacheBuilder runtimeCacheBuilder,
        ISessionAuthority sessionAuthority,
        INetworkHost networkHost)
    {
        _connectionProbe = connectionProbe;
        _migrationRunner = migrationRunner;
        _staticDataValidator = staticDataValidator;
        _staticDataLoader = staticDataLoader;
        _runtimeCacheBuilder = runtimeCacheBuilder;
        _sessionAuthority = sessionAuthority;
        _networkHost = networkHost;
    }

    public async Task<StartupResult> RunAsync(ServerConfiguration configuration, Action<StartupEvent> report, CancellationToken cancellationToken)
    {
        if (!await StepAsync(1, "Configuration", report, () => ValidateConfiguration(configuration)))
        {
            return StartupResult.Failed(10);
        }

        if (!await StepAsync(2, "Logging", report, () => Task.FromResult(OperationResult.Success)))
        {
            return StartupResult.Failed(11);
        }

        var connectionResult = await _connectionProbe.ProbeAsync(configuration.Database, cancellationToken);
        report(new StartupEvent(3, "MariaDB Connection", ToUnit(connectionResult), connectionResult.Value?.ConsoleLines ?? []));
        if (!connectionResult.Succeeded)
        {
            return StartupResult.Failed(20);
        }

        var migrationResult = await _migrationRunner.VerifyAsync(cancellationToken);
        report(new StartupEvent(4, "Schema and Migration", ToUnit(migrationResult), migrationResult.Value?.ConsoleLines ?? []));
        if (!migrationResult.Succeeded)
        {
            return StartupResult.Failed(21);
        }

        if (!await StepAsync(5, "Static Data Validation", report, () => _staticDataValidator.ValidateAsync(cancellationToken)))
        {
            return StartupResult.Failed(30);
        }

        OperationResult<IReadOnlyList<StaticDataLoadCount>> staticData = OperationResult<IReadOnlyList<StaticDataLoadCount>>.Failure("startup.not_run", "Static data was not loaded.");
        if (!await StepAsync(6, "Static Data Preload", report, async () =>
        {
            staticData = await _staticDataLoader.LoadAsync(cancellationToken);
            return staticData.Succeeded ? OperationResult.Success : OperationResult.Failure(staticData.Error.Code, staticData.Error.Message, staticData.Error.Source);
        }))
        {
            return StartupResult.Failed(31);
        }

        if (!await StepAsync(7, "Runtime Cache", report, () => _runtimeCacheBuilder.BuildAsync(staticData.Value ?? [], cancellationToken)))
        {
            return StartupResult.Failed(40);
        }

        if (!await StepAsync(8, "Session Authority", report, () => _sessionAuthority.InitializeAsync(cancellationToken)))
        {
            await _sessionAuthority.StopAcceptingLoginsAsync(CancellationToken.None);
            return StartupResult.Failed(41);
        }

        if (!await StepAsync(9, "Unified Network Host", report, () => _networkHost.StartAsync(configuration.Network, cancellationToken)))
        {
            await _networkHost.StopAsync(CancellationToken.None);
            await _sessionAuthority.StopAcceptingLoginsAsync(CancellationToken.None);
            return StartupResult.Failed(50);
        }

        report(new StartupEvent(10, "Game Service Ready", OperationResult.Success));
        return StartupResult.Ready(staticData.Value ?? []);
    }

    private static Task<OperationResult> ValidateConfiguration(ServerConfiguration configuration)
    {
        var errors = configuration.Validate();
        if (errors.Count == 0)
        {
            return Task.FromResult(OperationResult.Success);
        }

        var first = errors[0];
        return Task.FromResult(OperationResult.Failure(first.Code, first.Message, first.Source));
    }

    private static async Task<bool> StepAsync(int number, string name, Action<StartupEvent> report, Func<Task<OperationResult>> execute)
    {
        var result = await execute();
        report(new StartupEvent(number, name, result));
        return result.Succeeded;
    }

    private static OperationResult ToUnit<T>(OperationResult<T> result) =>
        result.Succeeded
            ? OperationResult.Success
            : OperationResult.Failure(result.Error.Code, result.Error.Message, result.Error.Source);
}

public sealed record StartupEvent(int Number, string Name, OperationResult Result, IReadOnlyList<string>? Details = null);

public sealed record StartupResult(bool IsReady, int ExitCode, IReadOnlyList<StaticDataLoadCount> StaticData)
{
    public static StartupResult Ready(IReadOnlyList<StaticDataLoadCount> staticData) => new(true, 0, staticData);

    public static StartupResult Failed(int exitCode) => new(false, exitCode, []);
}
