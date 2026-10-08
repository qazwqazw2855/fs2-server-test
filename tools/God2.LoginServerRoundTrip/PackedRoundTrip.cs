using System.Buffers.Binary;
using System.Text;
using God2.GameplayContentRecovery;

namespace God2.LoginServerRoundTrip;

internal static class PackedRoundTrip
{
    internal const int MaximumPayload = 64 * 1024;

    internal static byte[] Decode(byte[] packed, int packedLength, int payloadLength, byte marker)
    {
        Require(payloadLength is > 0 and <= MaximumPayload, "Invalid payload length.");
        Require(packed.Length == packedLength && packed.Length >= 8 &&
            packed.Length <= 7 + MaximumPayload + (MaximumPayload + 7) / 8,
            "Unexpected packed length.");
        Require(packed[0] == 0x05 && packed[1] == 0x16 && packed[6] == marker,
            "Unexpected header or marker.");
        Require(BinaryPrimitives.ReadInt32LittleEndian(packed.AsSpan(2, 4)) == payloadLength,
            "Unexpected declared payload length.");
        return God2PackedFile.Decode(packed);
    }

    // RecoveryPipelineTests.PackLiterals, with the source marker preserved.
    internal static byte[] Encode(byte[] plain, byte marker)
    {
        Require(plain.Length is > 0 and <= MaximumPayload, "Invalid payload length.");
        var payload = new List<byte>();
        for (var index = 0; index < plain.Length; index += 8)
        {
            var count = Math.Min(8, plain.Length - index);
            payload.Add((byte)((1 << count) - 1));
            payload.AddRange(plain.AsSpan(index, count).ToArray());
        }
        var packed = new byte[7 + payload.Count];
        packed[0] = 0x05;
        packed[1] = 0x16;
        BinaryPrimitives.WriteInt32LittleEndian(packed.AsSpan(2, 4), plain.Length);
        packed[6] = marker;
        payload.CopyTo(packed, 7);
        return packed;
    }

    internal static void Verify(byte[] expected, byte[] encoded, byte marker)
    {
        var actual = Decode(encoded, 7 + expected.Length + (expected.Length + 7) / 8,
            expected.Length, marker);
        Require(actual.AsSpan().SequenceEqual(expected), "Output round-trip mismatch.");
    }

    internal static byte[] Patch(byte[] source, string oldIp, int oldPort,
        string newIp, int newPort, int expectedCount)
    {
        Require(source.Length is > 0 and <= MaximumPayload, "Invalid payload length.");
        Require(ValidIp(oldIp) && ValidIp(newIp) && oldPort is >= 1 and <= 65535 &&
            newPort is >= 1 and <= 65535 && expectedCount is > 0 and <= 64,
            "Invalid endpoint or pattern count.");
        // Include the observed comma and CRLF boundaries. Preserve opaque 'asdf'.
        var from = Encoding.ASCII.GetBytes($",{oldIp} asdf {oldPort}\r\n");
        var to = Encoding.ASCII.GetBytes($",{newIp} asdf {newPort}\r\n");
        var offsets = new List<int>();
        for (var index = 0; index <= source.Length - from.Length; index++)
            if (source.AsSpan(index, from.Length).SequenceEqual(from))
            {
                offsets.Add(index);
                index += from.Length - 1;
            }
        Require(offsets.Count == expectedCount, "Unexpected endpoint pattern count.");
        var outputLength = checked(source.Length + expectedCount * (to.Length - from.Length));
        Require(outputLength is > 0 and <= MaximumPayload, "Invalid patched length.");
        var output = new byte[outputLength];
        var inputOffset = 0;
        var outputOffset = 0;
        foreach (var offset in offsets)
        {
            var unchangedLength = offset - inputOffset;
            source.AsSpan(inputOffset, unchangedLength).CopyTo(output.AsSpan(outputOffset));
            outputOffset += unchangedLength;
            to.CopyTo(output, outputOffset);
            outputOffset += to.Length;
            inputOffset = offset + from.Length;
        }
        source.AsSpan(inputOffset).CopyTo(output.AsSpan(outputOffset));
        // Independently check every unchanged segment and each exact replacement.
        inputOffset = outputOffset = 0;
        foreach (var offset in offsets)
        {
            var unchangedLength = offset - inputOffset;
            Require(source.AsSpan(inputOffset, unchangedLength).SequenceEqual(
                output.AsSpan(outputOffset, unchangedLength)), "Unexpected collateral change.");
            outputOffset += unchangedLength;
            Require(output.AsSpan(outputOffset, to.Length).SequenceEqual(to),
                "Replacement mismatch.");
            outputOffset += to.Length;
            inputOffset = offset + from.Length;
        }
        Require(source.AsSpan(inputOffset).SequenceEqual(output.AsSpan(outputOffset)),
            "Unexpected trailing change.");
        Console.WriteLine($"ASCII patch verified: occurrences={offsets.Count}; patternLength={from.Length}; replacementLength={to.Length}; payloadLength={source.Length}->{output.Length}; offsets={string.Join(',', offsets)}");
        return output;
    }

    private static bool ValidIp(string ip)
    {
        var parts = ip.Split('.');
        return parts.Length == 4 && parts.All(part => part.Length is >= 1 and <= 3 &&
            part.All(c => c is >= '0' and <= '9') &&
            (part.Length == 1 || part[0] != '0') && byte.TryParse(part, out _));
    }

    internal static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidDataException(message);
    }
}
