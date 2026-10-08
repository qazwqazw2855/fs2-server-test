using System.Buffers.Binary;
using System.Text;

namespace God2.ExactClientEndpoint;

internal static class OfflineTests
{
    internal static void Run()
    {
        var source = Fixture();
        var pe = new PeImage(source);
        PeImage.Require(pe.Imports.Count == 1 && pe.Imports[0].Name == "CommandLineToArgvW" &&
            pe.Imports[0].Module == "SHELL32.dll" && pe.Imports[0].IatRva == 0x1090,
            "Synthetic import parsing failed.");
        PeImage.Require(EndpointAnalysis.Strings(pe).Any(h => h.Classification == "VERIFIED_PE_IMPORT_NAME"),
            "Import/string distinction failed.");
        PeImage.Require(EndpointAnalysis.Flow(pe).Evidence.Any(h => h.Import == "SHELL32.dll!CommandLineToArgvW"),
            "Synthetic IAT call traversal failed.");
        var stringOnly = Fixture();
        Encoding.ASCII.GetBytes("LoginServer.csvZ\0").CopyTo(stringOnly, 0x300);
        PeImage.Require(EndpointAnalysis.Strings(new PeImage(stringOnly)).Any(h => h.Query == "LoginServer.csvZ" &&
            h.Classification == "OBSERVED_STRING_ONLY_NO_CALL_PROOF"), "Raw string wrongly promoted.");
        var ordinal = Fixture(); Put(ordinal, 0x280, 0x80000001);
        PeImage.Require(new PeImage(ordinal).Imports[0].Name == "ordinal-1", "Ordinal import failed.");
        var fallback = Fixture(); Put(fallback, 0x240, 0); Put(fallback, 0x290, 0x10C0);
        PeImage.Require(new PeImage(fallback).Imports.Count == 1, "FirstThunk fallback failed.");
        var substring = Fixture();
        Encoding.ASCII.GetBytes("CoDisconnectObject\0").CopyTo(substring, 0x2C2);
        PeImage.Require(EndpointAnalysis.Strings(new PeImage(substring)).Any(h => h.Query == "connect" &&
            h.Classification == "OBSERVED_IMPORT_SUBSTRING_NOT_ENDPOINT_API"),
            "Substring incorrectly promoted to socket API.");
        BoundedFlowTests();
        var rejects = 0;
        void Reject(string label, Action action)
        {
            try { action(); }
            catch (InvalidDataException) { rejects++; Console.WriteLine($"PASS reject: {label}"); return; }
            throw new InvalidDataException("Expected safe stop: " + label);
        }
        void Mutation(string label, Action<byte[]> mutation)
        {
            var b = Fixture(); mutation(b); Reject(label, () => _ = new PeImage(b));
        }
        Reject("synthetic bytes cannot bypass production hash lock", () => PeImage.CheckIdentity(source));
        Reject("empty hash input", () => PeImage.CheckIdentity([]));
        Reject("oversized hash input", () => PeImage.CheckIdentity(new byte[PeImage.MaximumBytes + 1]));
        Reject("short header", () => _ = new PeImage(new byte[63]));
        Mutation("MZ", b => b[0] = 0);
        Mutation("PE signature", b => b[0x80] = 0);
        Mutation("PE offset overflow", b => Put(b, 0x3C, uint.MaxValue));
        Mutation("wrong machine", b => b[0x84] = 0);
        Mutation("PE64", b => b[0x99] = 2);
        Mutation("optional header boundary", b => b[0x95] = 0xFF);
        Mutation("directory count", b => Put(b, 0xF4, 17));
        Mutation("section count", b => b[0x86] = 0);
        Mutation("raw file boundary", b => Put(b, 0x18C, 0x7FF));
        Mutation("RVA overflow", b => Put(b, 0x184, 0xFFFFFFF0));
        Mutation("section overlaps header", b => Put(b, 0x18C, 0x100));
        Mutation("import directory zero-fill", b => Put(b, 0x100, 0x1900));
        Mutation("import directory length", b => Put(b, 0x104, 19));
        Mutation("descriptor termination", b => Put(b, 0x104, 20));
        Mutation("name unmapped RVA", b => Put(b, 0x24C, 0x900000));
        Mutation("lookup unmapped RVA", b => Put(b, 0x240, 0x900000));
        Mutation("IAT unmapped RVA", b => Put(b, 0x250, 0x900000));
        Mutation("import name non-ASCII", b => b[0x2C2] = 0xFF);
        Mutation("import name crosses backing boundary", b => Put(b, 0x280, 0x17FE));
        Mutation("ordinal reserved bits", b => Put(b, 0x280, 0xC0000001));
        Mutation("non-executable entry", b => Put(b, 0xA8, 0x1800));
        Mutation("overlapping sections", b => { b[0x86] = 2; b.AsSpan(0x178,40).CopyTo(b.AsSpan(0x1A0,40)); });
        Reject("unbacked RVA", () => pe.Offset(0x1800));
        Reject("unmapped RVA", () => pe.Offset(0x900000));
        Reject("crossing raw boundary", () => pe.Offset(0x17FF, 2));
        Reject("crossing header boundary", () => pe.Offset(0x1FF, 2));
        var indirect = Fixture(); indirect[0x200] = 0xFF; indirect[0x201] = 0xE0;
        PeImage.Require(EndpointAnalysis.Flow(new PeImage(indirect)).Unknowns.Any(x => x.Contains("unresolved")),
            "Unresolved indirect jump must remain UNKNOWN.");
        var loop = Fixture(); loop[0x200] = 0xEB; loop[0x201] = 0xFE;
        PeImage.Require(EndpointAnalysis.Flow(new PeImage(loop)).DecodedInstructions == 1,
            "Traversal loop did not terminate.");
        Console.WriteLine($"PASS: synthetic offline parser/callsite tests; {rejects} safe-stop cases. Synthetic tests are not client evidence.");
    }

    private static void BoundedFlowTests()
    {
        var checks = 0;
        void Check(bool condition, string label)
        { PeImage.Require(condition, label); checks++; Console.WriteLine("PASS flow: " + label); }
        FlowResult Trace(byte[] code)
        {
            var b = Fixture();
            // Long code-budget fixtures have no imports: avoid overwriting the
            // small fixture's descriptor at raw offset 0x240 with NOP/JMP bytes.
            if (code.Length > 64) { Put(b, 0x100, 0); Put(b, 0x104, 0); }
            code.CopyTo(b, 0x200);
            return EndpointAnalysis.Flow(new PeImage(b));
        }
        // push target; call RET stub; RET to known pushed target; unknown initial RET.
        var result = Trace([0x68,0x0C,0x10,0x40,0, 0xE8,1,0,0,0, 0xC3,0xC3,0xC3]);
        Check(result.Evidence.Select(i => i.Rva).SequenceEqual(new uint[] {0x1000,0x1005,0x100B,0x100A,0x100C}),
            "push/call/ret sequence follows proven stack targets in order");
        Check(result.Evidence[2].NextRva == 0x100A && result.Evidence[3].NextRva == 0x100C,
            "call return and pushed return address remain distinct");
        Check(result.Evidence[4].FileOffset == 0x20C && result.Unknowns.Any(x => x.Contains("stack target not tracked")),
            "file offset reported; initial unknown stack stops");
        result = Trace([0xEB,1,0x90,0xC3]);
        Check(result.Evidence.Select(i => i.Rva).SequenceEqual(new uint[] {0x1000,0x1003}), "direct JMP skips bytes");
        result = Trace([0xEB,0xFE]);
        Check(result.DecodedInstructions == 1 && result.Unknowns.Any(x => x.Contains("cycle")), "cycle state stops");
        result = Trace([0xE8,0xFB,0xFF,0xFF,0xFF]);
        Check(result.Unknowns.Any(x => x.Contains("depth")) && result.DecodedInstructions <= 17,
            "recursive CALL stops at modeled stack/control depth");
        result = Trace(Enumerable.Repeat((byte)0x90, 80).ToArray());
        Check(result.DecodedInstructions == 64 && result.Unknowns.Any(x => x.Contains("instruction count")), "instruction budget stops");
        result = Trace(Enumerable.Range(0,40).SelectMany(_ => new byte[] {0xEB,0}).ToArray());
        Check(result.Unknowns.Any(x => x.Contains("control-transfer depth")) && result.DecodedInstructions == 17,
            "transfer depth stops independently of instruction budget");
        result = Trace([0x68,0,0,0,0,0xC3]);
        Check(result.DecodedInstructions == 2 && result.Unknowns.Any(x => x.Contains("Unreliable")), "bad pushed target stops");
        result = Trace([0x50,0xC3]);
        Check(result.DecodedInstructions == 1 && result.Unknowns.Any(x => x.Contains("Unmodeled")), "unknown register PUSH stops");
        result = Trace([0xC2,4,0]);
        Check(result.DecodedInstructions == 1 && result.Unknowns.Any(x => x.Contains("Unmodeled")), "RET cleanup semantics not guessed");
        result = Trace([0xFF,0xE0]);
        Check(result.DecodedInstructions == 1 && result.Unknowns.Any(x => x.Contains("unresolved")), "indirect JMP stops immediately");
        result = Trace([0xFF,0x15,0x90,0x10,0x40,0,0x90]);
        Check(result.DecodedInstructions == 1 && result.Evidence[0].Import is not null &&
            result.Unknowns.Any(x => x.Contains("IAT operand")), "IAT call identified without following unresolved callee");
        result = Trace([0x74,0,0x90]);
        Check(result.DecodedInstructions == 1 && result.Unknowns.Any(x => x.Contains("Unmodeled")), "unknown conditional branch stops");
        result = Trace([0xC6,0x05,0,0x10,0x40,0,0x90]);
        Check(result.DecodedInstructions == 1 && result.Unknowns.Any(x => x.Contains("dynamic decoding")), "memory write/self-modification stops");
        result = Trace([0x68,1,0x10,0x40,0,0xC3]);
        Check(result.DecodedInstructions == 2 && result.Unknowns.Any(x => x.Contains("Overlapping")), "target inside instruction stops");
        var truncated = Fixture(); Put(truncated, 0xA8, 0x17FF); truncated[0x9FF] = 0xE8;
        result = EndpointAnalysis.Flow(new PeImage(truncated));
        Check(result.DecodedInstructions == 0 && result.Unknowns.Any(x => x.Contains("truncated")), "truncated instruction stops");
        Console.WriteLine($"PASS: {checks} bounded control-flow checks; synthetic only.");
    }

    private static void Put(byte[] b, int offset, uint value) =>
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(offset, 4), value);
    private static byte[] Fixture()
    {
        var b = new byte[0xA00]; b[0] = 0x4D; b[1] = 0x5A; Put(b, 0x3C, 0x80);
        Put(b, 0x80, 0x4550); b[0x84] = 0x4C; b[0x85] = 1; b[0x86] = 1;
        b[0x94] = 0xE0; b[0x98] = 0x0B; b[0x99] = 1;
        Put(b, 0xA8, 0x1000); Put(b, 0xB4, 0x400000); Put(b, 0xD4, 0x200);
        Put(b, 0xF4, 16); Put(b, 0x100, 0x1040); Put(b, 0x104, 40);
        Encoding.ASCII.GetBytes(".text").CopyTo(b, 0x178);
        Put(b, 0x180, 0x1000); Put(b, 0x184, 0x1000); Put(b, 0x188, 0x800); Put(b, 0x18C, 0x200);
        Put(b, 0x19C, 0x60000020);
        b[0x200] = 0xFF; b[0x201] = 0x15; Put(b, 0x202, 0x401090); b[0x206] = 0xC3;
        Put(b, 0x240, 0x1080); Put(b, 0x24C, 0x10A0); Put(b, 0x250, 0x1090);
        Put(b, 0x280, 0x10C0); Encoding.ASCII.GetBytes("SHELL32.dll\0").CopyTo(b, 0x2A0);
        Encoding.ASCII.GetBytes("CommandLineToArgvW\0").CopyTo(b, 0x2C2);
        return b;
    }
}
