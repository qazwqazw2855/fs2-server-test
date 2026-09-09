using System.Buffers.Binary;
using God2.ClassicServer.Protocol;

namespace God2.ClassicServer.Runtime.Tests;

public sealed class PacketCorpusGapCryptoTests
{
    public static TheoryData<string, string> VerifiedHistoricalWorldFixtures => new()
    {
        { "05007ACCEC", "0500300025" },
        { "05003D09A5", "05006D0062" },
        { "0A0080BAD7C34DA69488", "0A002E10000E0001FF72" },
        { "0800775884CB3F09", "080037BB060000A4" },
        { "080077B5F3D73FB8", "0800371E12000013" },
        { "0500AC9D30", "05000201F8" }
    };

    [Theory]
    [MemberData(nameof(VerifiedHistoricalWorldFixtures))]
    public void Historical_wire_fixture_decodes_to_checksum_valid_plaintext_and_round_trips(
        string ciphertextHex,
        string expectedPlaintextHex)
    {
        var ciphertext = Convert.FromHexString(ciphertextHex);
        var expectedPlaintext = Convert.FromHexString(expectedPlaintextHex);

        var plaintext = OfficialClientWorldProtocolFrames.DecodeWorldServerPayload(ciphertext);

        Assert.Equal(expectedPlaintext, plaintext);
        Assert.Equal(plaintext.Length, BinaryPrimitives.ReadUInt16LittleEndian(plaintext));
        Assert.Equal(OfficialLoginWireTransform.ComputeChecksum(plaintext), plaintext[^1]);
        Assert.Equal(ciphertext, OfficialClientWorldProtocolFrames.EncodeWorldServerPayload(plaintext));
    }

    [Fact]
    public async Task Runtime_recognizes_exact_decrypted_corpus_sample_without_granting_gameplay_mutation()
    {
        var runtime = new ProtocolConnectionRuntime();
        var plaintext = Convert.FromHexString("05001F0014");

        var result = await runtime.DecodeAndRouteAsync(
            new PacketRuntimeContext(
                "corpus-connection",
                "corpus-session",
                ProtocolStage.InWorld,
                "runtime/protocol-evidence"),
            plaintext,
            CancellationToken.None);

        Assert.Equal(PacketRouteStatus.CapturedEvidence, result.Status);
        Assert.Equal("decrypted-corpus:evidence-package-20260806-c2s-opcode-1f", result.EvidenceSignatureId);
        Assert.Equal(112, runtime.DecryptedPacketCorpus.Definitions.Count);
    }
}
