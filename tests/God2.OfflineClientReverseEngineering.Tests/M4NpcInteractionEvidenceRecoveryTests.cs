using System.Buffers.Binary;
using God2.OfflineClientReverseEngineering;

namespace God2.OfflineClientReverseEngineering.Tests;

public sealed class M4NpcInteractionEvidenceRecoveryTests
{
    [Fact]
    public void Exact_transport_inverse_recovers_application_and_validates_checksum()
    {
        var key = Enumerable.Range(0, 256).Select(index => unchecked((byte)(index * 17 + 11))).ToArray();
        var encoded = Encode([0x37, 0xE0, 0x05, 0x00, 0x00], key, 0x10);

        var decoded = M4NpcInteractionEvidenceRecovery.DecodeTransportFrameForEvidence(encoded, key, 0x10);

        Assert.Equal(8, decoded.DeclaredLength);
        Assert.Equal(0x37, decoded.ApplicationOpcode);
        Assert.Equal(new byte[] { 0xE0, 0x05, 0x00, 0x00 }, decoded.ApplicationPayload);
        Assert.True(decoded.ChecksumValid);
    }

    [Fact]
    public void Exact_transport_inverse_exposes_ciphertext_mutation_as_checksum_failure()
    {
        var key = Enumerable.Range(0, 256).Select(index => unchecked((byte)(index * 17 + 11))).ToArray();
        var encoded = Encode([0x37, 0xE0, 0x05, 0x00, 0x00], key, 0x10);
        encoded[5] ^= 0x01;

        var decoded = M4NpcInteractionEvidenceRecovery.DecodeTransportFrameForEvidence(encoded, key, 0x10);

        Assert.False(decoded.ChecksumValid);
    }

    [Fact]
    public void Exact_transport_inverse_repeats_the_client_key_index_for_long_frames()
    {
        var key = Enumerable.Range(0, 256).Select(index => unchecked((byte)(index * 17 + 11))).ToArray();
        var application = Enumerable.Range(0, 600).Select(index => unchecked((byte)(index * 29 + 7))).ToArray();
        application[0] = 0x7A;
        var encoded = Encode(application, key, 0x10);

        var decoded = M4NpcInteractionEvidenceRecovery.DecodeTransportFrameForEvidence(encoded, key, 0x10);

        Assert.Equal(application.Length + 3, decoded.DeclaredLength);
        Assert.Equal(0x7A, decoded.ApplicationOpcode);
        Assert.Equal(application.AsSpan(1).ToArray(), decoded.ApplicationPayload);
        Assert.True(decoded.ChecksumValid);
    }

    [Theory]
    [InlineData("070037E00500001B")]
    [InlineData("030000")]
    public void Exact_transport_inverse_rejects_invalid_frame_boundaries(string hex)
    {
        var key = new byte[256];

        Assert.Throws<InvalidDataException>(() =>
            M4NpcInteractionEvidenceRecovery.DecodeTransportFrameForEvidence(
                Convert.FromHexString(hex),
                key,
                0x10));
    }

    [Theory]
    [InlineData(true, 136L, "VerifiedRepeatedTimingIdentityAndApplicationHash")]
    [InlineData(true, 135L, "Unpaired")]
    [InlineData(false, 136L, "Unpaired")]
    [InlineData(true, null, "Unpaired")]
    public void Dialog_pairing_requires_the_exact_recovered_latency(
        bool priorInteractionFound,
        long? latencyMilliseconds,
        string expected)
    {
        Assert.Equal(
            expected,
            M4NpcInteractionEvidenceRecovery.ClassifyDialogPairingForEvidence(
                priorInteractionFound,
                latencyMilliseconds));
    }

    private static byte[] Encode(ReadOnlySpan<byte> application, ReadOnlySpan<byte> key, byte initialPrevious)
    {
        var decoded = new byte[application.Length + 3];
        BinaryPrimitives.WriteUInt16LittleEndian(decoded, checked((ushort)decoded.Length));
        application.CopyTo(decoded.AsSpan(2));
        byte checksum = 0;
        for (var index = 0; index < decoded.Length - 1; index++)
        {
            checksum = unchecked((byte)(checksum + unchecked((byte)(decoded[index] + 0x3C))));
        }

        decoded[^1] = checksum;
        var encoded = decoded.ToArray();
        var previous = initialPrevious;
        for (var index = 2; index < encoded.Length; index++)
        {
            encoded[index] = unchecked((byte)((key[(index - 2) & 0xFF] ^ decoded[index]) + unchecked((byte)(previous - 3))));
            previous = decoded[index];
        }

        return encoded;
    }
}
