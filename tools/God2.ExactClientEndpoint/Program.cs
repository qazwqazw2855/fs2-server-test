using System.Text.Json;
using God2.ExactClientEndpoint;
using God2.OfflineClientReverseEngineering;

const string client = "/srv/god2-reference/client-current-6f2639/God2_opt.exe";
try
{
    if (args.Length == 1 && args[0] == "--self-test")
    {
        OfflineTests.Run();
        return 0;
    }
    PeImage.Require(args.Length == 0, "Only no arguments or --self-test are supported.");
    var bytes = BoundedFile.ReadAllBytes(client, PeImage.MaximumBytes, "Exact-current client");
    PeImage.CheckIdentity(bytes); // Must succeed before parsing/search/disassembly.
    var pe = new PeImage(bytes);
    var strings = EndpointAnalysis.Strings(pe);
    var flow = EndpointAnalysis.Flow(pe);
    Console.WriteLine(JsonSerializer.Serialize(new
    {
        Schema = "ExactClientEndpointStatic/1", ClientPath = client,
        ClientSha256 = PeImage.ExactSha256,
        Authority = "EXACT_CURRENT_FILE_STATIC_ONLY_NOT_TW_LIVE_SERVER_EVIDENCE",
        ReadOnly = true, ClientExecuted = false, NetworkOperations = false,
        OldBuildAnchorsUsed = false, ReportFilesWritten = false,
        pe.ImageBase, pe.EntryPoint, pe.Sections, pe.Imports,
        StringHits = strings, RawReferenceCandidates = EndpointAnalysis.References(pe, strings),
        StaticEntryTraversal = flow,
        EndpointPrecedence = "UNKNOWN",
        Limits = "PE imports and raw strings are structural evidence. Decoded entry paths are static candidates only. No runtime memory, unpacking, dynamic API resolution or endpoint data-flow proof. asdf 6001 semantics UNKNOWN."
    }, new JsonSerializerOptions { WriteIndented = true, IncludeFields = true }));
    return 0;
}
catch (Exception e) when (e is InvalidDataException or IOException or ArgumentException or OverflowException or InvalidOperationException)
{
    Console.WriteLine(JsonSerializer.Serialize(new { Status = "SAFE_STOP", Reason = e.Message,
        EndpointPrecedence = "UNKNOWN", ReportFilesWritten = false }));
    return 1;
}
