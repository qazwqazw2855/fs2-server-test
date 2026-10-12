using System;

const string originalHash =
    God2.V2Launcher.Services.V2EndpointService.OriginalLoginServerSha256;

const string v2Hash =
    God2.V2Launcher.Services.V2EndpointService.V2LoginServerSha256;

const string isolatedHost = "127.0.0.1";
const int isolatedPort = 6002;

var passed = 0;

void Check(string name, bool condition)
{
    if (!condition)
        throw new InvalidOperationException($"FAIL: {name}");

    passed++;
    Console.WriteLine($"PASS: {name}");
}

string Decide(string hash)
{
    if (hash.Equals(v2Hash, StringComparison.OrdinalIgnoreCase))
        return "ALREADY_CONFIGURED";

    if (hash.Equals(originalHash, StringComparison.OrdinalIgnoreCase))
        return "REPLACE_WITH_V2";

    return "REJECT";
}

bool IsIsolatedEndpoint(string host, int port)
{
    return host == isolatedHost && port == isolatedPort;
}

Console.WriteLine("===== LAUNCHER HASH DECISION =====");

Check("Original hash requires replacement",
    Decide(originalHash) == "REPLACE_WITH_V2");

Check("V2 hash already configured",
    Decide(v2Hash) == "ALREADY_CONFIGURED");

Check("Unknown hash rejected",
    Decide(new string('0', 64)) == "REJECT");

Check("Hash comparison ignores case",
    Decide(v2Hash.ToLowerInvariant()) == "ALREADY_CONFIGURED");

Console.WriteLine("===== ISOLATION POLICY =====");

Check("Loopback 6002 accepted",
    IsIsolatedEndpoint("127.0.0.1", 6002));

Check("Public AWS endpoint rejected",
    !IsIsolatedEndpoint("52.63.34.162", 6001));

Check("Public AWS host on 6002 rejected",
    !IsIsolatedEndpoint("52.63.34.162", 6002));

Check("Loopback production port rejected",
    !IsIsolatedEndpoint("127.0.0.1", 6001));


Console.WriteLine("===== REAL SERVICE UNKNOWN HASH TEST =====");

var temporaryDirectory = Path.Combine(
    Path.GetTempPath(),
    "god2-launcher-offline-" + Guid.NewGuid().ToString("N"));

Directory.CreateDirectory(temporaryDirectory);

try
{
    var targetFile = Path.Combine(
        temporaryDirectory, "LoginServer.csvZ");

    var originalBytes = System.Text.Encoding.ASCII.GetBytes(
        "INVALID_OFFLINE_TEST_CONFIGURATION");

    File.WriteAllBytes(targetFile, originalBytes);

    var result =
        await God2.V2Launcher.Services.V2EndpointService.ConfigureAsync(
            temporaryDirectory);

    Check("Real service rejects unknown hash",
        !result.Success &&
        result.Status == "LoginServer.csvZ 版本不符");

    Check("Unknown configuration remains unchanged",
        File.ReadAllBytes(targetFile).AsSpan()
            .SequenceEqual(originalBytes));

    Check("No backup created for rejected configuration",
        !File.Exists(Path.Combine(
            temporaryDirectory, "LoginServer.original.bak")));

    Check("No temporary replacement created",
        !File.Exists(targetFile + ".v2.tmp"));
}
finally
{
    Directory.Delete(temporaryDirectory, recursive: true);
}

Console.WriteLine("PASS: 4/4 real service rejection checks");


Console.WriteLine("===== REAL SERVICE ALREADY CONFIGURED TEST =====");

var v2TestDirectory = Path.Combine(
    Path.GetTempPath(),
    "god2-launcher-v2-" + Guid.NewGuid().ToString("N"));

Directory.CreateDirectory(v2TestDirectory);

try
{
    var resourceFile = Path.Combine(
        AppContext.BaseDirectory,
        "Resources",
        "LoginServer.v2.csvZ");

    var expectedBytes = File.ReadAllBytes(resourceFile);

    Check("V2 resource matches official SHA256",
        Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(expectedBytes))
        == v2Hash);

    var targetFile = Path.Combine(
        v2TestDirectory, "LoginServer.csvZ");

    File.WriteAllBytes(targetFile, expectedBytes);

    var result =
        await God2.V2Launcher.Services.V2EndpointService.ConfigureAsync(
            v2TestDirectory);

    Check("Real service accepts existing V2 configuration",
        result.Success && result.AlreadyConfigured);

    Check("Existing V2 configuration remains unchanged",
        File.ReadAllBytes(targetFile).AsSpan()
            .SequenceEqual(expectedBytes));

    Check("Existing V2 configuration creates no backup",
        !File.Exists(Path.Combine(
            v2TestDirectory, "LoginServer.original.bak")));

    Check("Existing V2 configuration creates no temporary replacement",
        !File.Exists(targetFile + ".v2.tmp"));
}
finally
{
    Directory.Delete(v2TestDirectory, recursive: true);
}

Console.WriteLine("PASS: 5/5 real service V2 checks");


Console.WriteLine("===== REAL SERVICE ORIGINAL REPLACEMENT TEST =====");

var originalSample =
    "/srv/god2-reference/client-legacy/God2/LoginServer.csvZ";

var originalSampleBytes = File.ReadAllBytes(originalSample);

Check("Original sample SHA256 matches baseline",
    Convert.ToHexString(
        System.Security.Cryptography.SHA256.HashData(originalSampleBytes))
    == originalHash);

var replacementDirectory = Path.Combine(
    Path.GetTempPath(),
    "god2-launcher-replace-" + Guid.NewGuid().ToString("N"));

Directory.CreateDirectory(replacementDirectory);

try
{
    var targetFile = Path.Combine(
        replacementDirectory, "LoginServer.csvZ");

    var backupFile = Path.Combine(
        replacementDirectory, "LoginServer.original.bak");

    File.WriteAllBytes(targetFile, originalSampleBytes);

    var result =
        await God2.V2Launcher.Services.V2EndpointService.ConfigureAsync(
            replacementDirectory);

    Check("Real service replaces original configuration",
        result.Success && !result.AlreadyConfigured);

    Check("Original configuration backup exists",
        File.Exists(backupFile));

    Check("Original backup content preserved",
        File.ReadAllBytes(backupFile).AsSpan()
            .SequenceEqual(originalSampleBytes));

    Check("Installed configuration matches V2 SHA256",
        Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(
                File.ReadAllBytes(targetFile))) == v2Hash);

    Check("Replacement temporary file removed",
        !File.Exists(targetFile + ".v2.tmp"));
}
finally
{
    Directory.Delete(replacementDirectory, recursive: true);
}

Console.WriteLine("PASS: 6/6 real service replacement checks");

Console.WriteLine($"PASS: {passed}/{passed} offline checks");
Console.WriteLine("No game execution, launcher execution, network or DB operations. Temporary test configuration files only.");
