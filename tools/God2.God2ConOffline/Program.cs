using System.Security.Cryptography;
using System.Buffers.Binary;
using God2.GameplayContentRecovery;

const string path =
    "/srv/god2-reference/client-legacy/God2/God2Con.csvZ";

if (args.Length == 1 && args[0] == "--self-test")
{
    SelfTests.Run();
    return;
}

if (args.Length != 0)
    throw new InvalidDataException("No arguments accepted.");

using var stream = new FileStream(
    path, FileMode.Open, FileAccess.Read, FileShare.Read);

if (stream.Length != God2ConVerifier.PackedLength)
    throw new InvalidDataException("Source length mismatch.");

var packed = new byte[God2ConVerifier.PackedLength];
stream.ReadExactly(packed);

var decoded = God2ConVerifier.Verify(packed);

var text = System.Text.Encoding.GetEncoding(
    936,
    System.Text.EncoderFallback.ExceptionFallback,
    System.Text.DecoderFallback.ExceptionFallback
).GetString(decoded);

var lines = text.Split('\n')
    .Select(line => line.TrimEnd('\r'))
    .ToList();

if (lines.Count > 0 && lines[^1].Length == 0)
    lines.RemoveAt(lines.Count - 1);

God2ConVerifier.Require(
    lines.Count == 42,
    "Unexpected God2Con line count.");

Console.WriteLine("===== GOD2CON OFFLINE EVIDENCE =====");
Console.WriteLine($"SourceSHA256: {God2ConVerifier.SourceHash}");
Console.WriteLine($"DecodedSHA256: {God2ConVerifier.DecodedHash}");
Console.WriteLine($"DecodedLength: {decoded.Length}");
Console.WriteLine("EndpointPrecedence: UNKNOWN");
Console.WriteLine("ClientExecuted: False");
Console.WriteLine("NetworkOperations: False");

for (var i = 0; i < lines.Count; i++)
    Console.WriteLine($"{i + 1:00}: {lines[i]}");

Console.WriteLine("===== COMPLETE =====");
