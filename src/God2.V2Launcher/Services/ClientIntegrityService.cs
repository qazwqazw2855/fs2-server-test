using System.IO;
using System.Security.Cryptography;

namespace God2.V2Launcher.Services;

public sealed record ClientIntegrityResult(
    bool Exists,
    bool IsReferenceClient,
    string ClientPath,
    string? Sha256,
    string Status);

public static class ClientIntegrityService
{
    public const string ReferenceSha256 =
        "FB72296CAB5950B74D8F7C98155145F7C5F58749C3011ADB0540325B60749DD1";

    public static async Task<ClientIntegrityResult> VerifyAsync(
        string launcherDirectory,
        CancellationToken cancellationToken = default)
    {
        var clientPath = Path.Combine(launcherDirectory, "God2.exe");

        if (!File.Exists(clientPath))
        {
            return new ClientIntegrityResult(
                false,
                false,
                clientPath,
                null,
                "找不到 God2.exe");
        }

        await using var stream = new FileStream(
            clientPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            1024 * 128,
            useAsync: true);

        using var sha256 = SHA256.Create();

        var hash = await sha256.ComputeHashAsync(
            stream,
            cancellationToken);

        var hashText = Convert.ToHexString(hash);

        if (!hashText.Equals(
                ReferenceSha256,
                StringComparison.OrdinalIgnoreCase))
        {
            return new ClientIntegrityResult(
                true,
                false,
                clientPath,
                hashText,
                "遊戲版本不符");
        }

        return new ClientIntegrityResult(
            true,
            true,
            clientPath,
            hashText,
            "原版 Client 驗證完成");
    }
}
