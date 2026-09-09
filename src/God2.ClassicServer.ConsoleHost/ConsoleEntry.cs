using God2.ClassicServer.Application.Contracts;
using God2.ClassicServer.Application.Shutdown;
using God2.ClassicServer.Application.Startup;
using God2.ClassicServer.Infrastructure;
using God2.ClassicServer.Persistence;
using God2.ClassicServer.Persistence.OfficialImports;
using God2.ClassicServer.Protocol;
using God2.ClassicServer.Runtime;
using System.Reflection;
using System.Text.RegularExpressions;

namespace God2.ClassicServer.ConsoleHost;

public static class ConsoleEntry
{
    private const string ProductVersion = "0.1.1-unified-runtime";
    private static readonly Regex CurrentMigrationLine = new(
        "^Migration [0-9]+ Current$",
        RegexOptions.CultureInvariant);

    public static Task<int> RunAsync(string[] args, CancellationToken cancellationToken) =>
        RunAsync(args, new ConsoleEntryContext(Directory.GetCurrentDirectory(), Console.Out), cancellationToken);

    public static Task<int> RunAsync(ConsoleEntryContext context, CancellationToken cancellationToken) =>
        RunAsync([], context, cancellationToken);

    public static async Task<int> RunAsync(string[] args, ConsoleEntryContext context, CancellationToken cancellationToken)
    {
        var startedAtUtc = DateTimeOffset.UtcNow;
        var paths = new AppPathProvider(context.BaseDirectory);
        var configurationResult = new JsonServerConfigurationLoader(paths).Load();
        ILocalizer localizer = configurationResult.Succeeded && configurationResult.Value is not null
            ? new JsonLocalizer(paths, configurationResult.Value.Localization.DefaultLanguage, configurationResult.Value.Localization.FallbackLanguage)
            : new FallbackLocalizer();

        WriteBanner(context.Output, configurationResult.Value, localizer);

        if (!configurationResult.Succeeded || configurationResult.Value is null)
        {
            WriteStage(context.Output, new StartupEvent(1, "Configuration", new(false, configurationResult.Error)));
            return 10;
        }

        if (args.Length > 0 && string.Equals(args[0], "--import-official-data", StringComparison.OrdinalIgnoreCase))
        {
            return await RunOfficialImportAsync(args, context, paths, configurationResult.Value, cancellationToken);
        }

        if (args.Length > 0 && string.Equals(args[0], "--migrate-only", StringComparison.OrdinalIgnoreCase))
        {
            return await RunMigrationOnlyAsync(context, paths, configurationResult.Value, cancellationToken);
        }

        using var runtimeCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var stopFileWatcher = StartStopFileWatcher(args, runtimeCancellation);
        var runtimeToken = runtimeCancellation.Token;

        var accountRepository = new MariaDbAccountRepository(configurationResult.Value.Database);
        var characterRepository = new MariaDbCharacterRepository(configurationResult.Value.Database);
        var characterCreationAuthority = new MariaDbCharacterCreationAuthority(
            configurationResult.Value.Database,
            OfficialServerSelectionWireCodec.ClientBuildId);
        var staticDataCache = new MariaDbStaticDataLoader(configurationResult.Value.Database);
        var promotedGameplayContent = new MariaDbCanonicalGameplayContentRuntime(configurationResult.Value.Database);
        var runtime = UnifiedRuntimeComposition.CreateProduction(
            configurationResult.Value.Server.MaximumPlayers,
            accountRepository,
            characterRepository,
            characterCreationAuthority,
            Environment.GetEnvironmentVariable("GOD2_OFFICIAL_LIVE_MOVEMENT_EVIDENCE_JSONL"));
        IServerLogger logger = configurationResult.Value.Logging.Console
            ? new TextWriterServerLogger(context.Output, configurationResult.Value.Logging.LogLevel)
            : NullServerLogger.Instance;
        var gameplayInventory = new MariaDbGameplayInventoryRuntime(configurationResult.Value.Database);
        var worldContentRepository = new MariaDbWorldContentRepository(staticDataCache);
        var productionMapRuntimes = new ProductionMapRuntimeRegistry(
            worldContentRepository,
            promotedGameplayContent);
        IWorldSessionCoordinator worldSessions = new MariaDbWorldSessionCoordinator(
            staticDataCache,
            promotedGameplayContent,
            productionMapRuntimes,
            new MariaDbOfficialImmortalProjectionSource(configurationResult.Value.Database),
            gameplayInventory,
            gameplayInventory);
        IWorldBootstrapProjector worldProjector = new LegacyCompatibilityWorldBootstrapProjector(
            excludeLegacyWorldEntities: true);
        var serverOwnedMultiplayer = new ServerOwnedMultiplayerRuntime(gameplayInventory,
            playerSocialRepository: new MariaDbPlayerSocialRepository(configurationResult.Value.Database));
        var networkHost = new TcpNetworkHost(
            runtime.PacketFactory,
            runtime.ProtocolConnectionRuntime,
            runtime.SessionAuthority,
            runtime.AuthenticationService,
            runtime.CharacterListQuery,
            new MariaDbPortalTransitionStore(configurationResult.Value.Database),
            worldSessionCoordinator: worldSessions,
            worldBootstrapProjector: worldProjector,
            portalMapIdentitySource: new MariaDbOfficialPortalMapIdentitySource(staticDataCache),
            npcInteractionClosedLoop: OfficialNpcInteractionClosedLoop.Create(worldSessions, promotedGameplayContent),
            inventoryTransactionCoordinator: gameplayInventory,
            petLifecycleCoordinator: gameplayInventory,
            productionMonsterCombatFactory: new MariaDbProductionMonsterCombatServiceFactory(
                configurationResult.Value.Database,
                staticDataCache,
                worldSessions,
                gameplayInventory),
            maximumConnections: configurationResult.Value.Security.ConnectionLimit,
            logger: logger,
            characterRuntimeService: runtime.CharacterRuntimeService,
            serverOwnedMultiplayerRuntime: serverOwnedMultiplayer,
            publicBetaCompatibilityProfile: PublicBetaCompatibilityProfile.Production);
        var staticDataValidator = new MariaDbStaticDataValidator(configurationResult.Value.Database);
        var runtimeCacheBuilder = new CompositeRuntimeCacheBuilder(
            staticDataCache,
            gameplayInventory,
            promotedGameplayContent,
            productionMapRuntimes);
        var pipeline = new StartupPipeline(
            new MariaDbRuntimeConnectionProbe(),
            new ReadOnlySqlMigrationVerifier(configurationResult.Value.Database, paths.DatabaseSchemaDirectory),
            staticDataValidator,
            staticDataCache,
            runtimeCacheBuilder,
            runtime.SessionAuthority,
            networkHost);

        var shutdown = new ShutdownCoordinator(
        [
            runtime.SessionAuthority,
            networkHost,
            runtime.CommandDispatcher
        ]);

        StartupResult result;
        try
        {
            result = await pipeline.RunAsync(configurationResult.Value, e => WriteStage(context.Output, e), runtimeToken);
        }
        catch (OperationCanceledException) when (runtimeToken.IsCancellationRequested)
        {
            await StopAndReportAsync(shutdown, context.Output);
            await StopBackgroundTasksAsync(runtimeCancellation, stopFileWatcher);
            context.Output.WriteLine(localizer.Translate("shutdown.complete"));
            return 0;
        }

        if (!result.IsReady)
        {
            await StopAndReportAsync(shutdown, context.Output);
            await StopBackgroundTasksAsync(runtimeCancellation, stopFileWatcher);
            return result.ExitCode;
        }

        WriteReady(context.Output, configurationResult.Value.Network, runtime, networkHost, startedAtUtc);
        WriteProductionWorldReadyEvidence(context.Output, promotedGameplayContent, productionMapRuntimes);
        var gameplayConsole = StartGameplayConsoleAsync(
            context.Output,
            staticDataValidator,
            staticDataCache,
            runtimeCacheBuilder,
            runtimeToken);
        try
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, runtimeToken);
        }
        catch (OperationCanceledException) when (runtimeToken.IsCancellationRequested)
        {
        }

        await StopAndReportAsync(shutdown, context.Output);
        await StopBackgroundTasksAsync(runtimeCancellation, stopFileWatcher, gameplayConsole);
        context.Output.WriteLine(localizer.Translate("shutdown.complete"));
        return shutdown.Failures.Count == 0 ? 0 : 70;
    }

    private static async Task StartGameplayConsoleAsync(
        TextWriter output,
        IStaticDataValidator validator,
        IStaticDataLoader loader,
        IRuntimeCacheBuilder cacheBuilder,
        CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            string? command;
            try
            {
                command = await Console.In.ReadLineAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            if (command is null)
            {
                return;
            }
            if (!string.Equals(command.Trim(), "reload gameplay", StringComparison.OrdinalIgnoreCase))
            {
                output.WriteLine("無法識別此指令。目前支援：reload gameplay");
                continue;
            }

            output.WriteLine("遊戲資料重載：正在驗證正式資料目錄...");
            var validation = await validator.ValidateAsync(cancellationToken);
            if (!validation.Succeeded)
            {
                output.WriteLine($"遊戲資料重載：失敗（{validation.Error.Code}; {validation.Error.Source}），保留舊資料。");
                continue;
            }

            var loaded = await loader.LoadAsync(cancellationToken);
            if (!loaded.Succeeded || loaded.Value is null)
            {
                output.WriteLine($"遊戲資料重載：失敗（{loaded.Error.Code}; {loaded.Error.Source}），保留舊資料。");
                continue;
            }

            var built = await cacheBuilder.BuildAsync(loaded.Value, cancellationToken);
            output.WriteLine(built.Succeeded
                ? $"遊戲資料重載：成功（{loaded.Value.Sum(value => value.Count)} 筆啟用資料）。"
                : $"遊戲資料重載：失敗（{built.Error.Code}; {built.Error.Source}），未完成部分保留舊資料。");
        }
    }

    private static Task? StartStopFileWatcher(string[] args, CancellationTokenSource cancellation)
    {
        var index = Array.FindIndex(args, arg => string.Equals(arg, "--stop-file", StringComparison.OrdinalIgnoreCase));
        if (index < 0 || index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
        {
            return null;
        }

        var stopFile = Path.GetFullPath(args[index + 1]);
        return WatchStopFileAsync(stopFile, cancellation);
    }

    private static async Task WatchStopFileAsync(
        string stopFile,
        CancellationTokenSource cancellation)
    {
        while (!cancellation.IsCancellationRequested)
        {
            if (File.Exists(stopFile))
            {
                await cancellation.CancelAsync();
                return;
            }

            try
            {
                await Task.Delay(500, cancellation.Token);
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
                return;
            }
        }
    }

    private static async Task StopBackgroundTasksAsync(
        CancellationTokenSource cancellation,
        params Task?[] tasks)
    {
        if (!cancellation.IsCancellationRequested)
        {
            await cancellation.CancelAsync();
        }

        var activeTasks = tasks.Where(task => task is not null).Cast<Task>().ToArray();
        if (activeTasks.Length == 0)
        {
            return;
        }

        try
        {
            await Task.WhenAll(activeTasks);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
    }

    private static void WriteBanner(TextWriter output, Application.Configuration.ServerConfiguration? configuration, ILocalizer localizer)
    {
        output.WriteLine("God2 經典版服務端");
        output.WriteLine($"版本：{ProductVersion}");
        output.WriteLine("作者：RayCat");
        output.WriteLine($"執行環境：{configuration?.Server.Environment ?? "未知"}");
        output.WriteLine($".NET: {Environment.Version}");
        output.WriteLine($"顯示語言：{localizer.Language}");
    }

    private static void WriteStage(TextWriter output, StartupEvent startupEvent)
    {
        var details = startupEvent.Details ?? [];
        var currentMigrationCount = details.Count(detail => CurrentMigrationLine.IsMatch(detail));
        if (currentMigrationCount > 0)
        {
            output.WriteLine($"資料庫遷移：{currentMigrationCount}/{currentMigrationCount} 版本一致");
        }

        foreach (var detail in details.Where(detail => !CurrentMigrationLine.IsMatch(detail)))
        {
            output.WriteLine(TranslateStartupDetail(detail));
        }

        var status = startupEvent.Result.Succeeded ? "成功" : "失敗";
        output.WriteLine($"[{startupEvent.Number}/10] {TranslateStageName(startupEvent.Name)}：{status}");
        if (!startupEvent.Result.Succeeded)
        {
            output.WriteLine($"錯誤代碼：{startupEvent.Result.Error.Code}");
            output.WriteLine($"錯誤來源：{startupEvent.Result.Error.Source}");
        }
    }

    private static string TranslateStageName(string name) => name switch
    {
        "Configuration" => "設定檢查",
        "Logging" => "日誌系統",
        "MariaDB Connection" => "MariaDB 資料庫連線",
        "Schema and Migration" => "資料庫結構與遷移",
        "Static Data Validation" => "靜態資料驗證",
        "Static Data Preload" => "靜態資料預載",
        "Runtime Cache" => "執行期快取",
        "Session Authority" => "連線階段管理",
        "Unified Network Host" => "遊戲網路服務",
        "Game Service Ready" => "遊戲服務就緒",
        _ => name
    };

    private static string TranslateStartupDetail(string detail) => detail switch
    {
        "Database Connected" => "資料庫連線成功",
        "Database Ready" => "資料庫已就緒",
        _ => detail
    };

    private static void WriteReady(
        TextWriter output,
        Application.Configuration.NetworkOptions network,
        UnifiedRuntimeComposition runtime,
        TcpNetworkHost networkHost,
        DateTimeOffset startedAtUtc)
    {
        output.WriteLine("[10/10] 遊戲服務就緒：成功");
        output.WriteLine("服務端已就緒");
        output.WriteLine($"遊戲連線位置：{network.BindIp}:{network.LoginPort}");
        output.WriteLine("登入／世界模式：統一連線");
        output.WriteLine($"網路監聽數量：{networkHost.ListenerCount}");
        output.WriteLine("WorldPort 為舊版欄位，統一連線模式不使用。");
        output.WriteLine($"線上玩家：{runtime.CharacterRuntimeRegistry.ActiveCharactersBySession.Count}");
        output.WriteLine($"有效連線階段：{runtime.SessionStore.ActiveSessions.Count}");
        output.WriteLine($"運行時間：{DateTimeOffset.UtcNow - startedAtUtc:c}");
        var compatibility = networkHost.PublicBetaCompatibilityProfile?.Summary;
        if (compatibility is not null)
        {
            output.WriteLine($"公測相容語意：{compatibility.CatalogAppliedCount}/{compatibility.CatalogTotalCount}（{compatibility.CatalogApplicationPercent:0.##}%）");
            output.WriteLine($"正式線路已驗證：{compatibility.CurrentWireVerifiedCount}/{compatibility.CatalogTotalCount}（{compatibility.CurrentWireActivationPercent:0.##}%）");
            output.WriteLine($"正式版轉接已實裝：{compatibility.CurrentWireCompatibilityAdapterReadyCount}（合計 {compatibility.CurrentWireImplementedCount}/{compatibility.CatalogTotalCount}，{compatibility.CurrentWireImplementationPercent:0.##}%）");
            output.WriteLine($"待正式版本轉接：{compatibility.CurrentWireAdapterRequiredCount}");
        }
    }

    private static void WriteProductionWorldReadyEvidence(
        TextWriter output,
        MariaDbCanonicalGameplayContentRuntime promotedGameplayContent,
        ProductionMapRuntimeRegistry productionMapRuntimes)
    {
        var promoted = promotedGameplayContent.PublishedSnapshot;
        var world = productionMapRuntimes.Status;
        output.WriteLine($"目前內容版本：{promoted.ReleaseId}");
        output.WriteLine($"正式資料指紋：{promoted.FormalCatalogFingerprint}");
        output.WriteLine($"正式資料筆數：{promoted.FormalCatalogRecordCount}");
        output.WriteLine($"執行期快照識別：{world.SnapshotIdentity}");
        output.WriteLine($"地圖目錄／已啟用：{world.CatalogMapCount} / {world.ActiveMapRuntimeCount}");
        output.WriteLine($"已啟用 NPC／怪物出生點：{world.EnabledNpcSpawnCount} / {world.EnabledMonsterSpawnCount}");
        output.WriteLine($"隔離的資料問題：{world.QuarantinedCatalogIssueCount}");
        output.WriteLine($"啟用地圖阻擋問題：{world.BlockingActiveMapIssueCount}");
        output.WriteLine($"等待建立的初始物件：{world.PendingInitialSpawnCount}");
        output.WriteLine($"服務端建置識別：{Assembly.GetExecutingAssembly().ManifestModule.ModuleVersionId:D}");
    }

    private static async Task StopAndReportAsync(ShutdownCoordinator shutdown, TextWriter output)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        try
        {
            await shutdown.StopOnceAsync(timeout.Token).WaitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            output.WriteLine("關機協調：等待逾時 15 秒。");
        }

        foreach (var failure in shutdown.Failures)
        {
            output.WriteLine($"關機協調：元件 {failure.Participant} 發生 {failure.ExceptionType}。");
        }
    }
    private static async Task<int> RunOfficialImportAsync(
        string[] args,
        ConsoleEntryContext context,
        AppPathProvider paths,
        Application.Configuration.ServerConfiguration configuration,
        CancellationToken cancellationToken)
    {
        var importRoot = args.Length > 1
            ? Path.GetFullPath(args[1])
            : paths.Resolve(Path.Combine("db", "imports", "official"));

        var bootstrap = await new MariaDbDatabaseBootstrapper().ProbeAsync(configuration.Database, cancellationToken);
        if (!bootstrap.Succeeded)
        {
            context.Output.WriteLine("Official Import Bootstrap: FAILED");
            context.Output.WriteLine($"Error: {bootstrap.Error.Message}");
            return 20;
        }

        context.Output.WriteLine("Database Connected");
        if (bootstrap.Value?.DatabaseCreated == true)
        {
            context.Output.WriteLine("Database Created");
        }

        var migrations = await new SqlFileMigrationRunner(configuration.Database, paths.DatabaseSchemaDirectory).VerifyAsync(cancellationToken);
        if (!migrations.Succeeded || migrations.Value is null)
        {
            context.Output.WriteLine("Official Import Migration: FAILED");
            context.Output.WriteLine($"Error: {migrations.Error.Message}");
            return 21;
        }

        foreach (var migration in migrations.Value.Migrations)
        {
            context.Output.WriteLine($"Migration {migration.Version} {migration.Status}");
        }

        var import = await new OfficialDataImportService(configuration.Database, OfficialImporters.All)
            .ImportAsync(importRoot, cancellationToken);
        if (!import.Succeeded || import.Value is null)
        {
            context.Output.WriteLine("Official Import: FAILED");
            context.Output.WriteLine($"Error: {import.Error.Message}");
            context.Output.WriteLine($"Source: {import.Error.Source}");
            return 30;
        }

        foreach (var category in import.Value.Categories)
        {
            context.Output.WriteLine($"Official Import {category.Category}: {category.RecordCount} ({category.VerificationStatus})");
        }

        context.Output.WriteLine($"Official Import Count: {import.Value.ImportedRecordCount}");
        context.Output.WriteLine($"Official Runtime Import Count: {import.Value.FormalImportedRecordCount}");
        context.Output.WriteLine($"Official Reference Issues: {import.Value.ReferenceIssueCount}");
        context.Output.WriteLine($"Official Import Batch: {import.Value.ImportBatch}");
        context.Output.WriteLine("Official Import Ready");
        return 0;
    }

    private static async Task<int> RunMigrationOnlyAsync(
        ConsoleEntryContext context,
        AppPathProvider paths,
        Application.Configuration.ServerConfiguration configuration,
        CancellationToken cancellationToken)
    {
        var database = ResolveAdminDatabaseOptions(configuration.Database);
        var bootstrap = await new MariaDbDatabaseBootstrapper().ProbeAsync(database, cancellationToken);
        if (!bootstrap.Succeeded)
        {
            context.Output.WriteLine("Migration Bootstrap: FAILED");
            context.Output.WriteLine($"Error: {bootstrap.Error.Message}");
            return 20;
        }

        var migrations = await new SqlFileMigrationRunner(database, paths.DatabaseSchemaDirectory)
            .VerifyAsync(cancellationToken);
        if (!migrations.Succeeded || migrations.Value is null)
        {
            context.Output.WriteLine("Migration: FAILED");
            context.Output.WriteLine($"Error: {migrations.Error.Message}");
            return 21;
        }

        foreach (var migration in migrations.Value.Migrations)
        {
            context.Output.WriteLine($"Migration {migration.Version} {migration.Status}");
        }

        context.Output.WriteLine("Migration Ready");
        return 0;
    }

    private static Application.Configuration.DatabaseOptions ResolveAdminDatabaseOptions(
        Application.Configuration.DatabaseOptions runtimeOptions)
    {
        var username = Environment.GetEnvironmentVariable("GOD2_DB_ADMIN_USERNAME");
        var password = Environment.GetEnvironmentVariable("GOD2_DB_ADMIN_PASSWORD");
        return string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(password)
            ? runtimeOptions
            : runtimeOptions with
            {
                Username = username,
                Password = password,
                PasswordSource = "EnvironmentVariable",
                PasswordEnvironmentVariable = "GOD2_DB_ADMIN_PASSWORD"
            };
    }
}

public sealed record ConsoleEntryContext(string BaseDirectory, TextWriter Output);

internal sealed class FallbackLocalizer : ILocalizer
{
    public string Language => "en-US";

    public string Translate(string key) => key switch
    {
        "shutdown.complete" => "Shutdown Complete",
        _ => key
    };
}
