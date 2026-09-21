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
        "6F2639A0A7AD25053D0364108147173EB68BD04F57E6942491F42633F40052BC";

    public static async Task<ClientIntegrityResult> VerifyAsync(
        string launcherDirectory,
        CancellationToken cancellationToken = default)
    {
        var clientPath = Path.Combine(launcherDirectory, "God2_opt.exe");

        if (!File.Exists(clientPath))
        {
            return new ClientIntegrityResult(
                false,
                false,
                clientPath,
                null,
                "找不到 God2_opt.exe");
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
