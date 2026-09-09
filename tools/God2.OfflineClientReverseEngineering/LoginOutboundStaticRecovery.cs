namespace God2.OfflineClientReverseEngineering;

public sealed record LoginOutboundCallsite(
    string Target,
    string TargetRva,
    string CallRva,
    string FunctionStartRva,
    string FunctionEndRva,
    bool ContainsFrameLength208,
    bool ContainsOpcodeD0,
    IReadOnlyList<string> Context);

public sealed record LoginOutboundTargetFunction(
    string Target,
    string TargetRva,
    IReadOnlyList<string> Instructions);

public sealed record LoginOutboundConstantSite(
    string Token,
    string InstructionRva,
    IReadOnlyList<string> Context);

public sealed record LoginOutboundStaticSnapshot(
    string SchemaVersion,
    string RuntimeCodeSha256,
    IReadOnlyList<LoginOutboundCallsite> Callsites,
    IReadOnlyList<LoginOutboundTargetFunction> TargetFunctions,
    IReadOnlyList<LoginOutboundConstantSite> ConstantSites,
    int CandidateCount,
    int NumericCoincidenceCount,
    string Decision,
    string ExactBlocker,
    IReadOnlyList<string> RuledOut,
    bool ClientWriteAccessUsed,
    bool ClientBinaryModified,
    bool NetworkBytesEmitted);

public static class LoginOutboundStaticRecovery
{
    private static readonly (string Name, uint Rva)[] Targets =
    [
        ("FrameBuilder", 0x00078EA0),
        ("ContextWriter", 0x00078F40),
        ("OpcodeFrameBuilder", 0x0007C2B0),
        ("TransportWriteFunction", 0x0007BB30),
        ("OutboundEnqueueBytes", 0x0007FC10),
        ("OutboundEnqueueDword", 0x0007FD80),
        ("OutboundFlush", 0x0007DC00)
    ];

    public static LoginOutboundStaticSnapshot Recover(RuntimeCodeAnalyzer analyzer, string runtimeCodeSha256)
    {
        var callsites = new List<LoginOutboundCallsite>();
        foreach (var target in Targets)
        {
            foreach (var call in analyzer.DirectCallers(target.Rva))
            {
                var function = analyzer.FunctionInstructions(call.Rva);
                if (function.Count == 0)
                {
                    continue;
                }

                var start = function[0].Rva;
                var end = function[^1].Rva + (uint)function[^1].Length;
                var callIndex = -1;
                for (var index = 0; index < function.Count; index++)
                {
                    if (function[index].Rva == call.Rva)
                    {
                        callIndex = index;
                        break;
                    }
                }

                var localStart = call.Rva >= RuntimeCodeAnalyzer.CodeStartRva + 0x100
                    ? call.Rva - 0x100
                    : RuntimeCodeAnalyzer.CodeStartRva;
                var local = analyzer.DecodeRange(localStart, 0x180);
                callIndex = -1;
                for (var index = 0; index < local.Count; index++)
                {
                    if (local[index].Rva == call.Rva)
                    {
                        callIndex = index;
                        break;
                    }
                }
                var context = local
                    .Skip(Math.Max(0, callIndex - 32))
                    .Take(Math.Min(64, local.Count - Math.Max(0, callIndex - 32)))
                    .Select(item => $"0x{item.Rva:X8} {item.Text}")
                    .ToArray();
                var containsFrameLength208 = context.Any(item =>
                    item.EndsWith("push 0D0h", StringComparison.OrdinalIgnoreCase) ||
                    item.Contains(",0D0h", StringComparison.OrdinalIgnoreCase));
                var containsPayloadLength206 = context.Any(item =>
                    item.EndsWith("push 0CEh", StringComparison.OrdinalIgnoreCase) ||
                    item.Contains(",0CEh", StringComparison.OrdinalIgnoreCase));
                callsites.Add(new LoginOutboundCallsite(
                    target.Name,
                    $"0x{target.Rva:X8}",
                    $"0x{call.Rva:X8}",
                    $"0x{start:X8}",
                    $"0x{end:X8}",
                    containsFrameLength208 || containsPayloadLength206,
                    containsFrameLength208,
                    context));
            }
        }

        var targetFunctions = Targets
            .Select(target => new LoginOutboundTargetFunction(
                target.Name,
                $"0x{target.Rva:X8}",
                analyzer.DecodeRange(target.Rva, 0x240)
                    .TakeWhile(item => item.Rva < target.Rva + 0x240)
                    .Select(item => $"0x{item.Rva:X8} {item.Text}")
                    .ToArray()))
            .ToArray();

        var constantSites = new[] { "0D0h", "0CEh" }
            .SelectMany(token => analyzer.InstructionsWithImmediateToken(token)
                .Select(site =>
                {
                    var start = site.Rva >= RuntimeCodeAnalyzer.CodeStartRva + 0x60
                        ? site.Rva - 0x60
                        : RuntimeCodeAnalyzer.CodeStartRva;
                    var context = analyzer.DecodeRange(start, 0xD0)
                        .Where(item => item.Rva >= site.Rva - Math.Min(site.Rva, 0x40u) && item.Rva <= site.Rva + 0x50)
                        .Select(item => $"0x{item.Rva:X8} {item.Text}")
                        .ToArray();
                    return new LoginOutboundConstantSite(token, $"0x{site.Rva:X8}", context);
                }))
            .OrderBy(item => item.InstructionRva, StringComparer.Ordinal)
            .ToArray();

        var numericCoincidenceCount = callsites.Count(item => item.ContainsFrameLength208 || item.ContainsOpcodeD0);
        return new LoginOutboundStaticSnapshot(
            "login-outbound-static-recovery-v3",
            runtimeCodeSha256,
            callsites.OrderBy(item => item.CallRva, StringComparer.Ordinal).ToArray(),
            targetFunctions,
            constantSites,
            CandidateCount: 0,
            NumericCoincidenceCount: numericCoincidenceCount,
            Decision: "EvidenceBlocked",
            ExactBlocker: "The exact-build static snapshot does not bind account/password UI state to semantic offsets or a reversible credential transform in the 208-byte Login request.",
            RuledOut:
            [
                "RVA 0x0007FC10 is a one-byte opcode plus payload world-stream enqueue function; its 0xCE/0xD0 operands are opcode values, not the 208-byte Login envelope length.",
                "RVA 0x0007DC00 is a generic outbound-buffer flush and provides no Login field semantics.",
                "The 0xD0 and 0xCE numeric constants have multiple unrelated call, allocation, index, and opcode meanings; numeric equality is not protocol-semantic evidence.",
                "No direct caller of the previously catalogued frame builders identifies the Login submit branch or credential field placement."
            ],
            ClientWriteAccessUsed: false,
            ClientBinaryModified: false,
            NetworkBytesEmitted: false);
    }
}
