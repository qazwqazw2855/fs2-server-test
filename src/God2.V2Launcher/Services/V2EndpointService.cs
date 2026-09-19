using System.IO;
using System.Security.Cryptography;

namespace God2.V2Launcher.Services;

public sealed record V2EndpointResult(
    bool Success,
    bool AlreadyConfigured,
    string Status);

public static class V2EndpointService
{
    public const string OriginalLoginServerSha256 =
        "C5AA8124DB417D13548182DF83F6D44EF74EA6292747AB1997E9B14557525170";

    public const string V2LoginServerSha256 =
        "AC25B908EBC02C280AE8BDB713990486B06CA57258B2035202FB386BC64ABB85";

    public static async Task<V2EndpointResult> ConfigureAsync(
        string launcherDirectory,
        CancellationToken cancellationToken = default)
    {
        var targetPath = Path.Combine(
            launcherDirectory,
            "LoginServer.csvZ");

        var backupPath = Path.Combine(
            launcherDirectory,
            "LoginServer.original.bak");

        var resourcePath = Path.Combine(
            AppContext.BaseDirectory,
            "Resources",
            "LoginServer.v2.csvZ");

        if (!File.Exists(resourcePath))
        {
            return new V2EndpointResult(
                false,
                false,
                "找不到 V2 Server 設定檔");
        }

        var resourceHash = await ComputeSha256Async(
            resourcePath,
            cancellationToken);

        if (!resourceHash.Equals(
                V2LoginServerSha256,
                StringComparison.OrdinalIgnoreCase))
        {
            return new V2EndpointResult(
                false,
                false,
                "V2 Server 設定檔驗證失敗");
        }

        if (!File.Exists(targetPath))
        {
            return new V2EndpointResult(
                false,
                false,
                "找不到 LoginServer.csvZ");
        }

        var currentHash = await ComputeSha256Async(
            targetPath,
            cancellationToken);

        if (currentHash.Equals(
                V2LoginServerSha256,
                StringComparison.OrdinalIgnoreCase))
        {
            return new V2EndpointResult(
                true,
                true,
                "V2 Server 已設定");
        }

        if (!currentHash.Equals(
                OriginalLoginServerSha256,
                StringComparison.OrdinalIgnoreCase))
        {
            return new V2EndpointResult(
                false,
                false,
                "LoginServer.csvZ 版本不符");
        }

        if (!File.Exists(backupPath))
        {
            File.Copy(
                targetPath,
                backupPath,
                overwrite: false);
        }

        var tempPath = targetPath + ".v2.tmp";

        File.Copy(
            resourcePath,
            tempPath,
            overwrite: true);

        File.Move(
            tempPath,
            targetPath,
            overwrite: true);

        var installedHash = await ComputeSha256Async(
            targetPath,
            cancellationToken);

        if (!installedHash.Equals(
                V2LoginServerSha256,
                StringComparison.OrdinalIgnoreCase))
        {
            return new V2EndpointResult(
                false,
                false,
                "V2 Server 設定套用後驗證失敗");
        }

        return new V2EndpointResult(
            true,
            false,
            "V2 Server 設定完成");
    }

    private static async Task<string> ComputeSha256Async(
        string path,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            128 * 1024,
            useAsync: true);

        using var sha256 = SHA256.Create();

        var hash = await sha256.ComputeHashAsync(
            stream,
            cancellationToken);

        return Convert.ToHexString(hash);
    }
}
