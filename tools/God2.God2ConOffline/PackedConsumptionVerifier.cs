using System.Buffers.Binary;

internal static class PackedConsumptionVerifier
{
    internal static void Verify(byte[] packed)
    {
        God2ConVerifier.Require(
            packed.Length == God2ConVerifier.PackedLength,
            "Packed length mismatch.");

        God2ConVerifier.Require(
            packed[0] == 0x05 &&
            packed[1] == 0x16 &&
            packed[6] == 0x5A,
            "Packed header mismatch.");

        var expected = BinaryPrimitives.ReadInt32LittleEndian(
            packed.AsSpan(2, 4));

        God2ConVerifier.Require(
            expected == God2ConVerifier.DecodedLength,
            "Declared decoded length mismatch.");

        var sourceIndex = 7;
        var decodedCount = 0;
        var flags = 0;

        while (sourceIndex < packed.Length &&
               decodedCount < expected)
        {
            flags >>= 1;

            if ((flags & 0x100) == 0)
            {
                flags = packed[sourceIndex++] | 0xFF00;
            }

            if ((flags & 1) != 0)
            {
                God2ConVerifier.Require(
                    sourceIndex < packed.Length,
                    "Truncated literal.");

                sourceIndex++;
                decodedCount++;
            }
            else
            {
                God2ConVerifier.Require(
                    sourceIndex + 1 < packed.Length,
                    "Truncated back-reference.");

                var second = packed[sourceIndex + 1];
                sourceIndex += 2;

                var count = (second & 0x0F) + 3;

                God2ConVerifier.Require(
                    count <= expected - decodedCount,
                    "Back-reference exceeds decoded length.");

                decodedCount += count;
            }
        }

        God2ConVerifier.Require(
            decodedCount == expected,
            "Decoded count mismatch.");

        God2ConVerifier.Require(
            sourceIndex == packed.Length,
            "Trailing packed bytes.");
    }
}
