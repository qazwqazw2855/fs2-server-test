using System.Globalization;
using System.Text.RegularExpressions;

namespace God2.OfflineClientReverseEngineering;

public sealed record M4OutboundCallsite(
    string CallRva,
    string FunctionStartRva,
    string FunctionEndRva,
    int? Opcode,
    int? PayloadLength,
    string OpcodeArgument,
    string PayloadArgument,
    string LengthArgument,
    IReadOnlyList<string> Context);

public sealed record M4OpcodePayloadGroup(
    int? Opcode,
    int? PayloadLength,
    int CallsiteCount,
    IReadOnlyList<string> CallRvas);

public sealed record M4SelectedFunctionCaller(
    string CallRva,
    string FunctionStartRva,
    IReadOnlyList<string> Context);

public sealed record M4SelectedFunctionCallerGroup(
    string TargetFunctionRva,
    IReadOnlyList<M4SelectedFunctionCaller> Callers);

public sealed record M4SelectedFunctionWindow(
    string Purpose,
    string StartRva,
    int Length,
    IReadOnlyList<string> Instructions);

public sealed record M4NpcInteractionStaticSnapshot(
    string SchemaVersion,
    string RuntimeCodeSha256,
    string OutboundEnqueueRva,
    int DirectCallerCount,
    int ResolvedArgumentCount,
    IReadOnlyList<M4OpcodePayloadGroup> ApplicationPayloadGroups,
    IReadOnlyList<M4OutboundCallsite> SelectedApplicationCallsites,
    IReadOnlyList<M4SelectedFunctionCallerGroup> SelectedFunctionCallers,
    IReadOnlyList<M4SelectedFunctionWindow> SelectedFunctionWindows,
    string NpcSendBranchStatus,
    string TargetHandleSourceStatus,
    string Decision,
    string ExactBlocker,
    IReadOnlyList<string> ProvenFacts,
    IReadOnlyList<string> RuledOut,
    bool ClientWriteAccessUsed,
    bool ClientBinaryModified,
    bool NetworkBytesEmitted,
    bool FakeNetworkBytes);

/// <summary>
/// Read-only, build-pinned reconstruction of calls to the official Client's world-stream
/// enqueue function. The function takes a one-byte opcode, payload pointer and payload
/// length. Application payload length is intentionally kept separate from captured transport
/// frame length: no general conversion between those two lengths has been proven.
/// </summary>
public static partial class M4NpcInteractionStaticRecovery
{
    public const uint OutboundEnqueueRva = 0x0007FC10;

    public static M4NpcInteractionStaticSnapshot Recover(
        RuntimeCodeAnalyzer analyzer,
        string runtimeCodeSha256)
    {
        ArgumentNullException.ThrowIfNull(analyzer);
        ArgumentException.ThrowIfNullOrWhiteSpace(runtimeCodeSha256);

        var directCallers = analyzer.DirectCallers(OutboundEnqueueRva);
        var resolved = RecoverApplicationCallsites(analyzer);
        var groups = resolved
            .GroupBy(callsite => (callsite.Opcode, callsite.PayloadLength))
            .OrderBy(group => group.Key.Opcode ?? int.MaxValue)
            .ThenBy(group => group.Key.PayloadLength ?? int.MaxValue)
            .Select(group => new M4OpcodePayloadGroup(
                group.Key.Opcode,
                group.Key.PayloadLength,
                group.Count(),
                group.Select(callsite => callsite.CallRva).ToArray()))
            .ToArray();
        var selectedCallsites = resolved
            .Where(callsite => callsite.Opcode is 0x37 or 0x38 or 0x39 or 0x86 or 0x8A)
            .ToArray();
        var selectedFunctionCallers = new uint[] { 0x000AEDF0, 0x000B07D0, 0x000B0800, 0x00099A80, 0x000F4D50 }
            .Select(target => new M4SelectedFunctionCallerGroup(
                HexRva(target),
                analyzer.DirectCallers(target)
                    .Select(call => RecoverCaller(analyzer, call))
                    .ToArray()))
            .ToArray();
        var selectedFunctionWindows = new (string Purpose, uint Start, int Length)[]
            {
                ("Captured C2S 0x39 sender", 0x000B07D0, 0x30),
                ("C2S 0x28 four-byte sender candidate", 0x000AEDF0, 0xA0),
                ("Captured C2S 0x38 dialog-selection sender", 0x000B0730, 0xA0),
                ("Captured C2S 0x37 sender", 0x000B0800, 0x70),
                ("C2S 0x37 world-selection caller and preconditions", 0x00089480, 0x160),
                ("Outbound transport cipher and checksum", 0x00078EA0, 0x160),
                ("Inbound transport cipher and checksum", 0x00078D70, 0x130),
                ("S2C 0x7A dispatch", 0x00090670, 0x60),
                ("S2C 0x41 merchant-session handler", 0x0008FBC0, 0x180),
                ("S2C 0x41 state consumer", 0x000C5B40, 0x360),
                ("Result record consumer", 0x000F60E0, 0x250),
                ("Dialog state initializer", 0x000F5F20, 0x1C0),
                ("Dialog close branch", 0x000F4D50, 0x130)
            }
            .Select(window => new M4SelectedFunctionWindow(
                window.Purpose,
                HexRva(window.Start),
                window.Length,
                analyzer.DecodeRange(window.Start, window.Length)
                    .Where(instruction => instruction.Rva < window.Start + window.Length)
                    .Select(instruction => $"{HexRva(instruction.Rva)} {instruction.Text}")
                    .ToArray()))
            .ToArray();
        return new M4NpcInteractionStaticSnapshot(
            "roadmap-m4-npc-interaction-static-recovery-v2",
            runtimeCodeSha256,
            HexRva(OutboundEnqueueRva),
            directCallers.Count,
            resolved.Count,
            groups,
            selectedCallsites,
            selectedFunctionCallers,
            selectedFunctionWindows,
            NpcSendBranchStatus: "ExactBuildStaticContractRecovered",
            TargetHandleSourceStatus: "ExactBuildStaticContractRecovered",
            Decision: "STATIC_RECOVERY_COMPLETE_CAPTURE_CORRELATION_SEPARATE",
            ExactBlocker:
                "Static recovery alone does not assign an observed action. The separate M4 existing-capture evidence " +
                "artifact correlation must pass before any recovered opcode or field is used by Production.",
            ProvenFacts:
            [
                "RVA 0x0007FC10 appends one opcode byte followed by the caller-supplied payload bytes.",
                "RVA 0x000B0800 enqueues C2S 0x37 with a four-byte selected-handle payload.",
                "RVA 0x000B0730 enqueues C2S 0x38 with an eight-byte interaction payload.",
                "RVA 0x000B07D0 enqueues C2S 0x39 with a four-byte interaction-state payload.",
                "RVA 0x000AEE51-0x000AEE7D enqueues C2S 0x28 with a four-byte payload built from one u16 and two u8 function arguments.",
                "RVA 0x00089511-0x0008953F rejects selection when absolute X or Y delta exceeds three.",
                "Every reported candidate is recovered from the read-only unpacked exact-build runtime snapshot."
            ],
            RuledOut:
            [
                "The encrypted/raw transport discriminator 0x7758 is not treated as a plaintext opcode.",
                "Application payload length is not equated with captured transport frame length.",
                "No candidate payload is emitted or replayed; capture correlation is recorded separately."
            ],
            ClientWriteAccessUsed: false,
            ClientBinaryModified: false,
            NetworkBytesEmitted: false,
            FakeNetworkBytes: false);
    }

    public static IReadOnlyList<M4OutboundCallsite> RecoverApplicationCallsites(
        RuntimeCodeAnalyzer analyzer)
    {
        ArgumentNullException.ThrowIfNull(analyzer);
        return analyzer.DirectCallers(OutboundEnqueueRva)
            .Select(call => RecoverCallsite(analyzer, call))
            .Where(callsite => callsite is not null)
            .Cast<M4OutboundCallsite>()
            .OrderBy(callsite => callsite.CallRva, StringComparer.Ordinal)
            .ToArray();
    }

    private static M4SelectedFunctionCaller RecoverCaller(RuntimeCodeAnalyzer analyzer, StaticInstruction call)
    {
        var function = analyzer.FunctionInstructions(call.Rva);
        var functionStart = function.Count == 0 ? call.Rva : function[0].Rva;
        var start = call.Rva >= RuntimeCodeAnalyzer.CodeStartRva + 0x80
            ? call.Rva - 0x80
            : RuntimeCodeAnalyzer.CodeStartRva;
        var context = analyzer.DecodeRange(start, 0xA0)
            .Where(instruction => instruction.Rva <= call.Rva)
            .TakeLast(28)
            .Select(instruction => $"{HexRva(instruction.Rva)} {instruction.Text}")
            .ToArray();
        return new M4SelectedFunctionCaller(HexRva(call.Rva), HexRva(functionStart), context);
    }

    private static M4OutboundCallsite? RecoverCallsite(
        RuntimeCodeAnalyzer analyzer,
        StaticInstruction call)
    {
        var startRva = call.Rva >= RuntimeCodeAnalyzer.CodeStartRva + 0x80
            ? call.Rva - 0x80
            : RuntimeCodeAnalyzer.CodeStartRva;
        var local = analyzer.DecodeRange(startRva, 0x90);
        var callIndex = -1;
        for (var index = 0; index < local.Count; index++)
        {
            if (local[index].Rva == call.Rva)
            {
                callIndex = index;
                break;
            }
        }

        if (callIndex < 0)
        {
            return null;
        }

        var pushes = new List<StaticInstruction>(3);
        for (var index = callIndex - 1; index >= 0 && pushes.Count < 3; index--)
        {
            var instruction = local[index];
            if (instruction.Mnemonic.Equals("Call", StringComparison.OrdinalIgnoreCase) ||
                instruction.Mnemonic.Equals("Ret", StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            if (instruction.Mnemonic.Equals("Push", StringComparison.OrdinalIgnoreCase))
            {
                pushes.Add(instruction);
            }
        }

        if (pushes.Count != 3)
        {
            return null;
        }

        var function = analyzer.FunctionInstructions(call.Rva);
        var functionStart = function.Count == 0 ? call.Rva : function[0].Rva;
        var functionEnd = function.Count == 0
            ? call.Rva + (uint)call.Length
            : function[^1].Rva + (uint)function[^1].Length;
        var opcodePush = pushes[0];
        var payloadPush = pushes[1];
        var lengthPush = pushes[2];
        var opcode = TryParsePushImmediate(opcodePush.Text);
        var payloadLength = TryParsePushImmediate(lengthPush.Text);
        var contextStart = Math.Max(0, callIndex - 20);
        var context = local
            .Skip(contextStart)
            .Take(callIndex - contextStart + 1)
            .Select(instruction => $"{HexRva(instruction.Rva)} {instruction.Text}")
            .ToArray();

        return new M4OutboundCallsite(
            HexRva(call.Rva),
            HexRva(functionStart),
            HexRva(functionEnd),
            opcode,
            payloadLength,
            $"{HexRva(opcodePush.Rva)} {opcodePush.Text}",
            $"{HexRva(payloadPush.Rva)} {payloadPush.Text}",
            $"{HexRva(lengthPush.Rva)} {lengthPush.Text}",
            context);
    }

    public static int? TryParsePushImmediate(string instruction)
    {
        var match = PushImmediateRegex().Match(instruction.Trim());
        if (!match.Success)
        {
            return null;
        }

        var token = match.Groups["value"].Value;
        var style = match.Groups["hex"].Success
            ? NumberStyles.AllowHexSpecifier
            : NumberStyles.Integer;
        return int.TryParse(token, style, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
    }

    private static string HexRva(uint value) => $"0x{value:X8}";

    [GeneratedRegex(
        "^push\\s+(?<value>[0-9A-Fa-f]+)(?<hex>h)?$",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex PushImmediateRegex();
}
