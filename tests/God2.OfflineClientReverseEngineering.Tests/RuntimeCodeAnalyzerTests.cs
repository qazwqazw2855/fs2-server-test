using God2.OfflineClientReverseEngineering;

namespace God2.OfflineClientReverseEngineering.Tests;

public sealed class RuntimeCodeAnalyzerTests
{
    [Fact]
    public void ExhaustiveMemoryDisplacementReferences_FindsReadsAndWritesButRejectsImmediateDecoys()
    {
        var bytes = Enumerable.Repeat((byte)0xCC, 0x80).ToArray();
        Convert.FromHexString("C6863202000077").CopyTo(bytes, 0x20); // mov byte [esi+232h],77h
        Convert.FromHexString("8A8332020000").CopyTo(bytes, 0x40);   // mov al,[ebx+232h]
        Convert.FromHexString("6832020000").CopyTo(bytes, 0x60);     // push 232h (not memory)
        var path = Path.Combine(Path.GetTempPath(), $"god2-runtime-displacement-{Guid.NewGuid():N}.bin");
        File.WriteAllBytes(path, bytes);
        try
        {
            var analyzer = new RuntimeCodeAnalyzer(path, 0u);

            var references = analyzer.ExhaustiveMemoryDisplacementReferences(0x232u);

            Assert.Equal(2, references.Count);
            var write = Assert.Single(references, reference => reference.InstructionRva == "0x00000020");
            Assert.Equal("Write", write.Access);
            Assert.Equal("ESI", write.BaseRegister);
            Assert.True(write.LinearDecodeAligned);
            var read = Assert.Single(references, reference => reference.InstructionRva == "0x00000040");
            Assert.Equal("Read", read.Access);
            Assert.Equal("EBX", read.BaseRegister);
            Assert.True(read.LinearDecodeAligned);
            Assert.DoesNotContain(references, reference => reference.InstructionRva == "0x00000060");
        }
        finally
        {
            File.Delete(path);
        }
    }
}
