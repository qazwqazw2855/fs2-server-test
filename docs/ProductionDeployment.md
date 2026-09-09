# God2 Classic Server Production Deployment

本文件描述 M9 可重現的部署前檢查。它不代表目前 RoadMap M9 已完成；`Artifacts/RoadMap/M9/final-summary.json` 仍為 `IN PROGRESS — BLOCKED`。

## 前置條件

- Windows x64 與 .NET SDK 10.0。
- MariaDB 12.x；`mariadb.exe` 與 `mariadb-dump.exe` 可用。
- `config/database.json` 指向本服 MariaDB。密碼可依本機既有 `ConfigValue` 或 DPAPI 流程解析，但不得出現在命令列、Report、Artifact 或 log。
- 官方 Client 必須是 SHA-256 `6B127086E0C00014DE26137B4EC482801E06E0724C5C05C64561D7F9FF32BD9B`。
- Production 不允許 InMemory／TestOnly fallback。

## 部署前 Gate

```powershell
dotnet build God2ClassicServer.sln -warnaserror
dotnet build God2ClassicServer.sln -c Release -warnaserror
dotnet test God2ClassicServer.sln --no-build
dotnet format God2ClassicServer.sln --verify-no-changes --no-restore
```

以既有安全 secret 流程執行環境與 migration 驗證：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Automation\Test-God2AutomationEnvironment.ps1
```

執行 M9 全資料備份／隔離還原驗證：

```powershell
$runId = "m9-backup-{0}" -f ([DateTimeOffset]::UtcNow.ToString("yyyyMMddTHHmmssZ"))
dotnet run --project .\tools\God2.AdvancedHeadlessVerification\God2.AdvancedHeadlessVerification.csproj `
  -c Release --no-restore -- --phase revalidation-prepare --root "$PWD" --run-id $runId

dotnet .\tools\God2.AutomationEnvironmentProbe\bin\Release\net10.0\God2.AutomationEnvironmentProbe.dll `
  --mode m9-production-backup-restore `
  --base-directory "$PWD" `
  --build-identity "$PWD\Artifacts\AdvancedFinalFreezeRevalidation\$runId\build-identity.json" `
  --out "$PWD\Artifacts\RoadMap\M9\backup-restore.json"
```

The probe fails closed unless the supplied source/build identity still matches the running Release assembly. Its transient dump is AES-256 encrypted with an in-memory ephemeral key, is opened without read sharing, and is deleted after validation. Restore acceptance compares database defaults, InnoDB eligibility, table definitions, exact row counts, extended content checksums, views, routines/parameters, events, and triggers.

這個 probe 會使用 child-process environment 傳遞 MariaDB 密碼，建立 `god2_m9_restore_<32 hex>` 隔離 Schema，精確比對全部 base table，再刪除隔離 Schema 與 Temp SQL。它不是 retained backup；正式備份必須存到 workspace 外的加密、限制存取儲存區，並另外執行還原演練。

## 啟動與停止

只使用 Release build 啟動，並先確認 server-ready probe、active content release 與 Migration 048 全部通過。正常停止必須走 stop-file／graceful shutdown，等待 connection tasks、ordered outbox 與 repository cleanup 完成；不可用強制終止冒充 clean shutdown。

## 回滾

1. 停止接受新連線。
2. 等待 session、outbox、journal 與 DB connection drain。
3. 保存失敗部署的必要 metadata 與 hash，不保存 credential 或完整敏感 packet。
4. 從部署前已驗證、加密的 retained backup 還原到隔離 Schema。
5. 比對 migration head、table identity、exact row counts、active content release 與 catalog fingerprint。
6. 僅在 isolated validation 全部通過後切換正式資料庫；任何差異都 fail-closed。

## 目前禁止宣告 Production Closed 的條件

- M2–M8 尚未全部完成。
- Current-source official Client 完整核心循環與 family matrix 尚未通過。
- 7,200 秒 long soak 尚未執行。
- Workspace 外 retained encrypted backup 尚未配置與演練。
