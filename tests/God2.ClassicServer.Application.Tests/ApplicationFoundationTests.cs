using God2.ClassicServer.Application.Common;
using God2.ClassicServer.Application.Configuration;
using God2.ClassicServer.Application.Contracts;
using God2.ClassicServer.Application.Shutdown;
using God2.ClassicServer.Application.Startup;

namespace God2.ClassicServer.Application.Tests;

public sealed class ApplicationFoundationTests
{
    [Fact]
    public async Task Startup_pipeline_starts_network_only_after_all_previous_steps_succeed()
    {
        var network = new RecordingNetworkHost(OperationResult.Success);
        var pipeline = new StartupPipeline(
            new SuccessConnectionProbe(),
            new SuccessMigrationRunner(),
            new SuccessStaticDataValidator(),
            new SuccessStaticDataLoader(),
            new SuccessRuntimeCacheBuilder(),
            new SuccessSessionAuthority(),
            network);
        var events = new List<StartupEvent>();

        var result = await pipeline.RunAsync(TestConfiguration(), events.Add, CancellationToken.None);

        Assert.True(result.IsReady);
        Assert.True(network.IsStarted);
        Assert.Contains(events, startupEvent => startupEvent.Name == "Unified Network Host");
        Assert.Contains(events, startupEvent => startupEvent.Details?.Contains("Database Connected") == true);
        Assert.Contains(events, startupEvent => startupEvent.Details?.Contains("Migration 001 Applied") == true);
        Assert.Contains(events, startupEvent => startupEvent.Details?.Contains("Database Ready") == true);
    }

    [Fact]
    public async Task Static_data_validation_failure_does_not_start_listener()
    {
        var network = new RecordingNetworkHost(OperationResult.Success);
        var pipeline = new StartupPipeline(
            new SuccessConnectionProbe(),
            new SuccessMigrationRunner(),
            new FailingStaticDataValidator(),
            new SuccessStaticDataLoader(),
            new SuccessRuntimeCacheBuilder(),
            new SuccessSessionAuthority(),
            network);

        var result = await pipeline.RunAsync(TestConfiguration(), _ => { }, CancellationToken.None);

        Assert.False(result.IsReady);
        Assert.False(network.IsStarted);
    }

    [Fact]
    public async Task Migration_failure_does_not_start_listener_or_ready()
    {
        var network = new RecordingNetworkHost(OperationResult.Success);
        var pipeline = new StartupPipeline(
            new SuccessConnectionProbe(),
            new FailingMigrationRunner(),
            new SuccessStaticDataValidator(),
            new SuccessStaticDataLoader(),
            new SuccessRuntimeCacheBuilder(),
            new SuccessSessionAuthority(),
            network);

        var result = await pipeline.RunAsync(TestConfiguration(), _ => { }, CancellationToken.None);

        Assert.False(result.IsReady);
        Assert.False(network.IsStarted);
        Assert.Equal(21, result.ExitCode);
    }

    [Fact]
    public async Task Network_start_failure_rolls_back_session_authority_and_network()
    {
        var network = new RecordingNetworkHost(OperationResult.Failure("network.bind_failed", "bind failed"));
        var authority = new RecordingSessionAuthority();
        var pipeline = new StartupPipeline(
            new SuccessConnectionProbe(),
            new SuccessMigrationRunner(),
            new SuccessStaticDataValidator(),
            new SuccessStaticDataLoader(),
            new SuccessRuntimeCacheBuilder(),
            authority,
            network);

        var result = await pipeline.RunAsync(TestConfiguration(), _ => { }, CancellationToken.None);

        Assert.False(result.IsReady);
        Assert.Equal(50, result.ExitCode);
        Assert.False(network.IsStarted);
        Assert.Equal(1, network.StopCount);
        Assert.Equal(1, authority.StopCount);
        Assert.False(authority.IsAccepting);
    }

    [Fact]
    public async Task Composite_runtime_cache_requires_every_production_cache_to_build()
    {
        var first = new RecordingRuntimeCacheBuilder(OperationResult.Success);
        var second = new RecordingRuntimeCacheBuilder(OperationResult.Failure("inventory.production_initialization_failed", "blocked"));
        var skipped = new RecordingRuntimeCacheBuilder(OperationResult.Success);
        var composite = new CompositeRuntimeCacheBuilder(first, second, skipped);

        var result = await composite.BuildAsync([new StaticDataLoadCount("Item", 1)], CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("inventory.production_initialization_failed", result.Error.Code);
        Assert.Equal(1, first.CallCount);
        Assert.Equal(1, second.CallCount);
        Assert.Equal(0, skipped.CallCount);
    }

    [Fact]
    public async Task Shutdown_coordinator_runs_only_once()
    {
        var participant = new RecordingShutdownParticipant();
        var coordinator = new ShutdownCoordinator([participant]);

        var first = await coordinator.StopOnceAsync(CancellationToken.None);
        var second = await coordinator.StopOnceAsync(CancellationToken.None);

        Assert.True(first);
        Assert.False(second);
        Assert.Equal(1, participant.StopCount);
    }

    [Fact]
    public async Task Concurrent_shutdown_callers_share_completion_and_participant_runs_once()
    {
        var participant = new BlockingShutdownParticipant();
        var coordinator = new ShutdownCoordinator([participant]);

        var first = coordinator.StopOnceAsync(CancellationToken.None);
        await participant.Started.Task;
        var second = coordinator.StopOnceAsync(CancellationToken.None);
        Assert.False(second.IsCompleted);

        participant.Release.TrySetResult();
        var results = await Task.WhenAll(first, second);

        Assert.Contains(true, results);
        Assert.Contains(false, results);
        Assert.Equal(1, participant.StopCount);
        Assert.Equal(1, coordinator.ExecutionCount);
    }

    [Fact]
    public async Task Shutdown_continues_after_participant_failure_and_records_safe_failure()
    {
        var completed = new RecordingShutdownParticipant();
        var coordinator = new ShutdownCoordinator([new ThrowingShutdownParticipant(), completed]);

        var first = await coordinator.StopOnceAsync(CancellationToken.None);

        Assert.True(first);
        Assert.Equal(1, completed.StopCount);
        var failure = Assert.Single(coordinator.Failures);
        Assert.Equal("Throwing", failure.Participant);
        Assert.Equal(nameof(InvalidOperationException), failure.ExceptionType);
    }

    [Fact]
    public void Configuration_validation_reports_file_field_current_value_and_legal_value()
    {
        var configuration = TestConfiguration() with
        {
            Database = TestConfiguration().Database with { Port = 0 }
        };

        var error = Assert.Single(configuration.Validate());

        Assert.Contains("檔名: database.json", error.Message);
        Assert.Contains("欄位: port", error.Message);
        Assert.Contains("目前值: 0", error.Message);
        Assert.Contains("合法值: 1 到 65535", error.Message);
    }

    private static ServerConfiguration TestConfiguration() =>
        new(
            new ServerOptions("God2 Classic Server", "Test", 10),
            new DatabaseOptions("127.0.0.1", 3306, "god2", "god2", "test-password", 1),
            new NetworkOptions("127.0.0.1", 6000, 6001),
            new RatesOptions(1, 1),
            new SecurityOptions(true, true, true, 10),
            new PersistenceOptions(30, 60),
            new LocalizationOptions("en-US", "en-US"),
            new LoggingOptions("Information", true, false));

    private sealed class SuccessConnectionProbe : IMariaDbConnectionProbe
    {
        public Task<OperationResult<DatabaseConnectionReport>> ProbeAsync(DatabaseOptions options, CancellationToken cancellationToken) =>
            Task.FromResult(OperationResult<DatabaseConnectionReport>.Success(new DatabaseConnectionReport(DatabaseCreated: false)));
    }

    private sealed class SuccessMigrationRunner : IMigrationRunner
    {
        public Task<OperationResult<MigrationReport>> VerifyAsync(CancellationToken cancellationToken) =>
            Task.FromResult(OperationResult<MigrationReport>.Success(new MigrationReport([new MigrationExecution("001", "001_accounts.sql", MigrationStatus.Applied)])));
    }

    private sealed class FailingMigrationRunner : IMigrationRunner
    {
        public Task<OperationResult<MigrationReport>> VerifyAsync(CancellationToken cancellationToken) =>
            Task.FromResult(OperationResult<MigrationReport>.Failure("migration.failed", "Migration failed.", "001_accounts.sql"));
    }

    private sealed class SuccessStaticDataValidator : IStaticDataValidator
    {
        public Task<OperationResult> ValidateAsync(CancellationToken cancellationToken) => Task.FromResult(OperationResult.Success);
    }

    private sealed class FailingStaticDataValidator : IStaticDataValidator
    {
        public Task<OperationResult> ValidateAsync(CancellationToken cancellationToken) =>
            Task.FromResult(OperationResult.Failure("static.invalid", "Static data failed.", "Map"));
    }

    private sealed class SuccessStaticDataLoader : IStaticDataLoader
    {
        public Task<OperationResult<IReadOnlyList<StaticDataLoadCount>>> LoadAsync(CancellationToken cancellationToken) =>
            Task.FromResult(OperationResult<IReadOnlyList<StaticDataLoadCount>>.Success([new StaticDataLoadCount("Map", 1)]));
    }

    private sealed class SuccessRuntimeCacheBuilder : IRuntimeCacheBuilder
    {
        public Task<OperationResult> BuildAsync(IReadOnlyList<StaticDataLoadCount> staticData, CancellationToken cancellationToken) =>
            Task.FromResult(OperationResult.Success);
    }

    private sealed class RecordingRuntimeCacheBuilder : IRuntimeCacheBuilder
    {
        private readonly OperationResult _result;

        public RecordingRuntimeCacheBuilder(OperationResult result)
        {
            _result = result;
        }

        public int CallCount { get; private set; }

        public Task<OperationResult> BuildAsync(IReadOnlyList<StaticDataLoadCount> staticData, CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(_result);
        }
    }

    private sealed class SuccessSessionAuthority : ISessionAuthority
    {
        public Task<OperationResult> InitializeAsync(CancellationToken cancellationToken) => Task.FromResult(OperationResult.Success);

        public Task StopAcceptingLoginsAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class RecordingNetworkHost : INetworkHost
    {
        private readonly OperationResult _result;

        public RecordingNetworkHost(OperationResult result)
        {
            _result = result;
        }

        public bool IsStarted { get; private set; }

        public int StopCount { get; private set; }

        public Task<OperationResult> StartAsync(NetworkOptions options, CancellationToken cancellationToken)
        {
            IsStarted = _result.Succeeded;
            return Task.FromResult(_result);
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            StopCount++;
            IsStarted = false;
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingShutdownParticipant : IShutdownParticipant
    {
        public string Name => "Test";

        public int StopCount { get; private set; }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            StopCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class BlockingShutdownParticipant : IShutdownParticipant
    {
        private int _stopCount;

        public string Name => "Blocking";

        public int StopCount => Volatile.Read(ref _stopCount);

        public TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Release { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _stopCount);
            Started.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
        }
    }

    private sealed class ThrowingShutdownParticipant : IShutdownParticipant
    {
        public string Name => "Throwing";

        public Task StopAsync(CancellationToken cancellationToken) =>
            throw new InvalidOperationException("shutdown failed");
    }

    private sealed class RecordingSessionAuthority : ISessionAuthority
    {
        public bool IsAccepting { get; private set; }

        public int StopCount { get; private set; }

        public Task<OperationResult> InitializeAsync(CancellationToken cancellationToken)
        {
            IsAccepting = true;
            return Task.FromResult(OperationResult.Success);
        }

        public Task StopAcceptingLoginsAsync(CancellationToken cancellationToken)
        {
            StopCount++;
            IsAccepting = false;
            return Task.CompletedTask;
        }
    }
}
