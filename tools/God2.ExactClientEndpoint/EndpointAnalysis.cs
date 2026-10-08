using System.Buffers.Binary;
using System.Text;
using Iced.Intel;

namespace God2.ExactClientEndpoint;

internal sealed record StringHit(string Query, string Classification, int FileOffset, uint? Rva,
    string[] ImportNames);
internal sealed record ReferenceHit(string Target, int TotalByteMatches, int[] FileOffsets,
    string Classification);
internal sealed record InstructionHit(uint Rva, int FileOffset, string Text, string Kind, string? Import,
    uint? NextRva, string TransferProof);
internal sealed record FlowResult(InstructionHit[] Evidence, int DecodedInstructions, string[] Unknowns,
    int MaximumInstructions, int MaximumTransferDepth, int MaximumStackDepth);

internal static class EndpointAnalysis
{
    internal static readonly string[] Terms =
    ["CommandLineToArgv", "GetCommandLine", "LoginServer.csvZ", "LoginServer", "God2Con.csvZ", "God2Con",
        "inet_addr", "getaddrinfo", "gethostbyname", "connect", "WSAConnect", "GetProcAddress", "LoadLibrary"];

    internal static int[] Find(byte[] bytes, byte[] needle)
    {
        PeImage.Require(needle.Length > 0, "Empty byte search rejected.");
        var offsets = new List<int>();
        for (var i = 0; i <= bytes.Length - needle.Length; i++)
            if (bytes.AsSpan(i, needle.Length).SequenceEqual(needle)) offsets.Add(i);
        return offsets.ToArray();
    }

    internal static StringHit[] Strings(PeImage pe)
    {
        var result = new List<StringHit>();
        foreach (var term in Terms)
        {
            foreach (var encoding in new[] { Encoding.ASCII, Encoding.Unicode })
            {
                foreach (var offset in Find(pe.Bytes, encoding.GetBytes(term)))
                {
                    var rva = pe.RvaForOffset(offset);
                    var imports = pe.Imports.Where(i => i.NameRva == rva && i.Name.StartsWith(term, StringComparison.Ordinal)).ToArray();
                    var enclosing = pe.Imports.Where(i => i.NameRva.HasValue && rva.HasValue &&
                        rva.Value >= i.NameRva.Value && (ulong)rva.Value < (ulong)i.NameRva.Value + (uint)i.Name.Length).ToArray();
                    result.Add(new StringHit(term, imports.Length > 0 ? "VERIFIED_PE_IMPORT_NAME" :
                        enclosing.Length > 0 ? "OBSERVED_IMPORT_SUBSTRING_NOT_ENDPOINT_API" :
                        "OBSERVED_STRING_ONLY_NO_CALL_PROOF", offset, rva,
                        enclosing.Select(i => i.Module + "!" + i.Name).ToArray()));
                }
            }
        }
        return result.ToArray();
    }

    internal static ReferenceHit[] References(PeImage pe, StringHit[] strings)
    {
        var result = new List<ReferenceHit>();
        var targets = pe.Imports.Where(i => Terms.Any(t => i.Name.StartsWith(t, StringComparison.Ordinal)) ||
            i.Module.Contains("ws2", StringComparison.OrdinalIgnoreCase) ||
            i.Module.Contains("wsock", StringComparison.OrdinalIgnoreCase));
        foreach (var import in targets)
            Add(import.Module + "!" + import.Name + " IAT VA", import.IatAddress);
        foreach (var hit in strings.Where(h => h.Rva.HasValue))
            Add(hit.Query + " string VA", pe.ImageBase + hit.Rva!.Value);
        return result.ToArray();

        void Add(string label, uint address)
        {
            var needle = new byte[4]; BinaryPrimitives.WriteUInt32LittleEndian(needle, address);
            var hits = Find(pe.Bytes, needle);
            result.Add(new ReferenceHit(label, hits.Length, hits.Take(64).ToArray(),
                "RAW_ADDRESS_BYTES_ONLY_NOT_INSTRUCTION_OR_DATA_FLOW_PROOF"));
        }
    }

    // A bounded single-path stack model, not an emulator or a linear sweep.
    // Only NOP, immediate PUSH, near direct CALL/JMP and plain 32-bit RET
    // have modeled semantics. Every other instruction stops the trace.
    internal const int InstructionLimit = 64;
    internal const int TransferDepthLimit = 16;
    internal const int StackDepthLimit = 16;
    internal static FlowResult Flow(PeImage pe)
    {
        var rva = pe.EntryPoint;
        var depth = 0;
        var stack = new List<(uint Address, string Origin)>();
        var states = new HashSet<string>(StringComparer.Ordinal);
        var occupied = new Dictionary<uint, uint>();
        var evidence = new List<InstructionHit>();
        var unknowns = new List<string>();
        var formatter = new NasmFormatter();
        while (evidence.Count < InstructionLimit)
        {
            if (!pe.Executable(rva)) { Stop($"Unbacked/non-executable target 0x{rva:X8}"); break; }
            var state = rva + ":" + string.Join(',', stack.Select(x => x.Address + "/" + x.Origin));
            if (!states.Add(state)) { Stop($"Repeated control/stack state at 0x{rva:X8}; cycle stopped"); break; }
            if (occupied.TryGetValue(rva, out var owner) && owner != rva)
            { Stop($"Overlapping instruction boundary 0x{rva:X8}"); break; }
            var decoder = Iced.Intel.Decoder.Create(32, new ByteArrayCodeReader(pe.Code(rva)));
            decoder.IP = pe.ImageBase + rva;
            decoder.Decode(out var instruction);
            if (instruction.IsInvalid || instruction.Length == 0)
            { Stop($"Invalid/truncated instruction at 0x{rva:X8}"); break; }
            if (Enumerable.Range(0, instruction.Length).Any(i =>
                occupied.TryGetValue(rva + (uint)i, out var prior) && prior != rva))
            { Stop($"Overlapping instruction span 0x{rva:X8}"); break; }
            for (uint i = 0; i < instruction.Length; i++) occupied[rva + i] = rva;
            var text = new TextOutput(); formatter.Format(instruction, text);
            var absolute = instruction.Op0Kind == OpKind.Memory && instruction.MemoryBase == Register.None &&
                instruction.MemoryIndex == Register.None ? instruction.MemoryDisplacement64 : (ulong?)null;
            var import = absolute.HasValue ? pe.Imports.SingleOrDefault(i => i.IatAddress == absolute) : null;
            uint? next = null;
            var proof = "UNKNOWN";
            var controlTransfer = false;
            var sequential = (ulong)pe.ImageBase + rva + (uint)instruction.Length;
            if (instruction.Mnemonic == Mnemonic.Nop)
            {
                next = Target(sequential); proof = "NOP fall-through";
            }
            else if (instruction.Mnemonic == Mnemonic.Push &&
                instruction.Op0Kind is OpKind.Immediate32 or OpKind.Immediate8to32)
            {
                if (stack.Count >= StackDepthLimit) Stop("Maximum modeled stack depth reached");
                else
                {
                    var value = instruction.Op0Kind == OpKind.Immediate32 ? instruction.Immediate32 :
                        unchecked((uint)instruction.Immediate8to32);
                    stack.Add((value, $"immediate PUSH at 0x{rva:X8}"));
                    next = Target(sequential); proof = "32-bit immediate PUSH; known stack value";
                }
            }
            else if (instruction.FlowControl == FlowControl.Call && instruction.Op0Kind == OpKind.NearBranch32)
            {
                controlTransfer = true;
                if (stack.Count >= StackDepthLimit) Stop("Maximum modeled stack depth reached");
                else if (sequential > uint.MaxValue) Stop("Return-address overflow");
                else
                {
                    stack.Add(((uint)sequential, $"direct CALL return address at 0x{rva:X8}"));
                    next = Target(instruction.NearBranchTarget); proof = "Direct CALL target; return address pushed";
                }
            }
            else if (instruction.FlowControl == FlowControl.UnconditionalBranch && instruction.Op0Kind == OpKind.NearBranch32)
            {
                controlTransfer = true;
                next = Target(instruction.NearBranchTarget); proof = "Direct JMP target";
            }
            else if (instruction.Code == Code.Retnd)
            {
                controlTransfer = true;
                if (stack.Count == 0) Stop("RET stack target not tracked: initial/unknown stack value");
                else
                {
                    var top = stack[^1]; stack.RemoveAt(stack.Count - 1);
                    next = Target(top.Address); proof = "RET target from " + top.Origin;
                }
            }
            else if (instruction.FlowControl is FlowControl.IndirectCall or FlowControl.IndirectBranch)
                Stop("Indirect control: " + (import is null ? "unresolved target" :
                    "IAT operand " + import.Module + "!" + import.Name + " identified; target/data flow unresolved"));
            else Stop($"Unmodeled instruction at 0x{rva:X8}: stack/register/memory effects or dynamic decoding UNKNOWN");

            evidence.Add(new InstructionHit(rva, pe.Offset(rva), text.Value.ToString(),
                "VERIFIED_STATIC_INSTRUCTION_BYTES_NOT_RUNTIME_EXECUTION", import is null ? null :
                import.Module + "!" + import.Name, next, proof));
            if (unknowns.Count > 0 || next is null) break;
            if (controlTransfer && ++depth > TransferDepthLimit)
            { Stop("Maximum control-transfer depth reached; target not followed"); break; }
            rva = next.Value;
        }
        if (evidence.Count == InstructionLimit && unknowns.Count == 0) Stop("Maximum instruction count reached");
        if (pe.HasDelayImports) Stop("Delay imports unresolved by this minimal parser");
        Stop("No verified endpoint selection/overwrite data-flow chain; endpoint precedence UNKNOWN");
        return new FlowResult(evidence.ToArray(), evidence.Count, unknowns.ToArray(),
            InstructionLimit, TransferDepthLimit, StackDepthLimit);

        void Stop(string reason) => unknowns.Add("UNKNOWN: " + reason);
        uint? Target(ulong address)
        {
            if (address < pe.ImageBase || address - pe.ImageBase > uint.MaxValue ||
                !pe.Executable((uint)(address - pe.ImageBase)))
            { Stop($"Unreliable/unbacked target VA 0x{address:X8}"); return null; }
            return (uint)(address - pe.ImageBase);
        }
    }

    private sealed class TextOutput : FormatterOutput
    {
        internal readonly StringBuilder Value = new();
        public override void Write(string text, FormatterTextKind kind) => Value.Append(text);
    }
}
