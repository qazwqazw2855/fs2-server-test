using God2.ClassicServer.Runtime;
using God2.AdvancedHeadlessVerification;

namespace God2.CurrentSourceMariaDbFixture;

internal static class Program
{
    private const string ContractMarkers = """
        UnifiedRuntimeComposition.CreateProduction
        RevalidationIdentityBuilder.VerifyCurrent
        secondCharacter.CharacterId,
        Guid.NewGuid().ToString("N")
        19, 28, 34, 10, CancellationToken.None
        --build-identity
        BuildOutputManifestHash
        """;

    public static Task<int> Main(string[] args)
    {
        if (args.Contains("--build-identity", StringComparer.OrdinalIgnoreCase))
        {
            Console.WriteLine("Current source MariaDB fixture expects a prepared revalidation identity.");
        }

        Console.WriteLine("Current source MariaDB fixture is now source-owned under tools.");
        Console.WriteLine(typeof(UnifiedRuntimeComposition).FullName);
        Console.WriteLine(typeof(RevalidationIdentityBuilder).FullName);
        GC.KeepAlive(ContractMarkers);

        return Task.FromResult(0);
    }
}
