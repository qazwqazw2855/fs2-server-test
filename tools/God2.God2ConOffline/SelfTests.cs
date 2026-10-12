using System.Security.Cryptography;

internal static class SelfTests
{
    internal static void Run()
    {
        const string path =
            "/srv/god2-reference/client-legacy/God2/God2Con.csvZ";

        var original = File.ReadAllBytes(path);
        var decoded = God2ConVerifier.Verify(original);

        PackedConsumptionVerifier.Verify(original);
        Console.WriteLine("PASS: packed consumption 904/904");

        God2ConVerifier.Require(
            decoded.Length == God2ConVerifier.DecodedLength,
            "Valid fixture decoded length mismatch.");

        Console.WriteLine("PASS: pinned God2Con fixture");

        var rejected = 0;

        void Reject(string name, byte[] candidate)
        {
            try
            {
                God2ConVerifier.Verify(candidate);
            }
            catch (InvalidDataException)
            {
                rejected++;
                Console.WriteLine($"PASS reject: {name}");
                return;
            }

            throw new InvalidDataException(
                $"Unexpected acceptance: {name}");
        }

        Reject("empty input", []);
        Reject("short input", original[..^1]);
        Reject("extra input", [..original, 0]);

        foreach (var index in new[] { 0, 1, 2, 3, 4, 5, 6, 7, 8 })
        {
            var changed = original.ToArray();
            changed[index] ^= 1;
            Reject($"modified byte {index}", changed);
        }

        var middle = original.ToArray();
        middle[middle.Length / 2] ^= 1;
        Reject("modified compressed payload", middle);

        var end = original.ToArray();
        end[^1] ^= 1;
        Reject("modified final byte", end);

        God2ConVerifier.Require(
            Convert.ToHexString(SHA256.HashData(original)) ==
                God2ConVerifier.SourceHash,
            "Original fixture changed.");

        Console.WriteLine(
            $"PASS: {rejected} fail-closed cases");

        var formatRejected = 0;

        byte[] MakeSynthetic()
        {
            var data = new byte[God2ConVerifier.PackedLength];
            data[0] = 0x05;
            data[1] = 0x16;
            data[2] = 0x14;
            data[3] = 0x05;
            data[6] = 0x5A;
            return data;
        }

        void RejectExact(
            string name,
            byte[] candidate,
            string expectedMessage)
        {
            try
            {
                PackedConsumptionVerifier.Verify(candidate);
            }
            catch (InvalidDataException ex)
                when (ex.Message == expectedMessage)
            {
                formatRejected++;
                Console.WriteLine($"PASS format reject: {name}");
                return;
            }

            throw new InvalidDataException(
                $"Expected format rejection: {name} / {expectedMessage}");
        }

        // All literals: 904 bytes cannot produce 1300 bytes.
        var insufficientOutput = MakeSynthetic();
        Array.Fill(insufficientOutput, (byte)0xFF, 7, 897);
        RejectExact(
            "insufficient decoded output",
            insufficientOutput,
            "Decoded count mismatch.");

        // First token is a back-reference of 18 bytes.
        // Repeated references exceed the declared output length.
        var outputOverrun = MakeSynthetic();

        // Zero flags select back-references.
        // Each token consumes two bytes and emits 18 bytes.
        // 73 tokens would emit 1314 bytes, exceeding 1300.
        for (var i = 8; i < outputOverrun.Length; i += 17)
        {
            for (var token = 0; token < 8; token++)
            {
                var secondIndex = i + token * 2 + 1;
                if (secondIndex < outputOverrun.Length)
                    outputOverrun[secondIndex] = 0x0F;
            }
        }

        RejectExact(
            "back-reference output overrun",
            outputOverrun,
            "Back-reference exceeds decoded length.");

        // Invalid declared decoded length.
        var invalidLength = MakeSynthetic();
        invalidLength[2] = 0xFF;
        invalidLength[3] = 0xFF;
        invalidLength[4] = 0x7F;
        invalidLength[5] = 0x7F;
        RejectExact(
            "invalid declared length",
            invalidLength,
            "Declared decoded length mismatch.");

        // Synthetic stream: 72 references of 18 bytes
        // followed by one reference of 4 bytes = 1300.
        // The remaining packed bytes must be rejected.
        var trailingData = MakeSynthetic();
        var cursor = 7;

        for (var group = 0; group < 9; group++)
        {
            trailingData[cursor++] = 0x00;

            for (var token = 0; token < 8; token++)
            {
                trailingData[cursor++] = 0x00;
                trailingData[cursor++] = 0x0F;
            }
        }

        trailingData[cursor++] = 0x00;
        trailingData[cursor++] = 0x00;
        trailingData[cursor++] = 0x01;

        God2ConVerifier.Require(
            cursor < trailingData.Length,
            "Synthetic fixture has no trailing bytes.");

        RejectExact(
            "trailing packed bytes fixture",
            trailingData,
            "Trailing packed bytes.");

        // 99 complete groups: 99 * (1 flag + 8 literals)
        // = 891 bytes. Header is 7 bytes, leaving 6.
        var truncatedLiteral = MakeSynthetic();
        var literalCursor = 7;

        for (var group = 0; group < 99; group++)
        {
            truncatedLiteral[literalCursor++] = 0xFF;

            for (var token = 0; token < 8; token++)
                truncatedLiteral[literalCursor++] = 0x41;
        }

        God2ConVerifier.Require(
            literalCursor == 898,
            "Unexpected literal fixture offset.");

        // One flag followed by only five literal bytes.
        truncatedLiteral[literalCursor++] = 0xFF;

        for (var token = 0; token < 5; token++)
            truncatedLiteral[literalCursor++] = 0x41;

        God2ConVerifier.Require(
            literalCursor == truncatedLiteral.Length,
            "Literal fixture length mismatch.");

        // The parser exits when sourceIndex reaches the
        // packed length, so this reports decoded count mismatch.
        RejectExact(
            "incomplete literal output fixture",
            truncatedLiteral,
            "Decoded count mismatch.");

        // 98 literal groups: 98 * 9 = 882 bytes.
        // One mixed group: flag + 3 literals + 5 references
        // = 1 + 3 + 10 = 14 bytes.
        // Total payload before final flag = 896 bytes.
        var alignedLiteral = MakeSynthetic();
        var alignedCursor = 7;

        for (var group = 0; group < 98; group++)
        {
            alignedLiteral[alignedCursor++] = 0xFF;

            for (var token = 0; token < 8; token++)
                alignedLiteral[alignedCursor++] = 0x41;
        }

        alignedLiteral[alignedCursor++] = 0x07;

        for (var token = 0; token < 3; token++)
            alignedLiteral[alignedCursor++] = 0x41;

        for (var token = 0; token < 5; token++)
        {
            alignedLiteral[alignedCursor++] = 0x00;
            alignedLiteral[alignedCursor++] = 0x00;
        }

        God2ConVerifier.Require(
            alignedCursor == 903,
            "Aligned literal fixture offset mismatch.");

        alignedLiteral[alignedCursor++] = 0x01;

        God2ConVerifier.Require(
            alignedCursor == alignedLiteral.Length,
            "Aligned literal fixture length mismatch.");

        RejectExact(
            "aligned truncated literal token",
            alignedLiteral,
            "Truncated literal.");

        // 98 literal groups consume 882 packed bytes.
        // One mixed group consumes 13 bytes:
        // 1 flag + 4 literals + 4 references.
        // Header 7 + 882 + 13 = 902.
        var alignedReference = MakeSynthetic();
        var referenceCursor = 7;

        for (var group = 0; group < 98; group++)
        {
            alignedReference[referenceCursor++] = 0xFF;

            for (var token = 0; token < 8; token++)
                alignedReference[referenceCursor++] = 0x41;
        }

        alignedReference[referenceCursor++] = 0x0F;

        for (var token = 0; token < 4; token++)
            alignedReference[referenceCursor++] = 0x41;

        for (var token = 0; token < 4; token++)
        {
            alignedReference[referenceCursor++] = 0x00;
            alignedReference[referenceCursor++] = 0x00;
        }

        God2ConVerifier.Require(
            referenceCursor == 902,
            "Aligned reference fixture offset mismatch.");

        // A zero flag selects a back-reference.
        // Only one source byte remains, but two are required.
        alignedReference[referenceCursor++] = 0x00;
        alignedReference[referenceCursor++] = 0x00;

        God2ConVerifier.Require(
            referenceCursor == alignedReference.Length,
            "Aligned reference fixture length mismatch.");

        RejectExact(
            "aligned truncated back-reference token",
            alignedReference,
            "Truncated back-reference.");

        Console.WriteLine(
            $"PASS: {formatRejected} format rejection cases");
    }
}
