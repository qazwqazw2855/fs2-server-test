namespace God2.ServerV2.Protocol;

public static class CurrentClientServerWireTransform
{
    private static readonly byte[] CipherKey =
        Convert.FromHexString(
            "B20DBBA18DA4B99A0C2480B2B88574D2F9CC806862D6CFC6594E48DE5243D87A" +
            "FD3EAF4E62CB1B66EC3C8AF0951496AC229B2B19417065DB90448F89D04C2F1D" +
            "1AD12F1E01C2B70A9873301FA5084CD2D60C33F2E2FF591794158567F4A5D611" +
            "86BAF06B67A4D56755B9E856DEC0F65D9C865F9D4F6DF4A1EC2AF221C536157D" +
            "C95FBADE9D57BDA8AA767C3D4924DC75FD7179C290A07BA220EA9E5E4BE83489" +
            "692A541EAAC5B7F31F12B27BED1D45417F364708AB823941B9BC50C78FA17A5F" +
            "F08288D596D40B713DF553B9D2917BE9AC3B9118AAF975A73E0AD204984A3024" +
            "E5CE1CA86A6E00498B87269E016AC4960CE9219A95EF76FB373BEABC6ECC9E03");

    private const int InitialPreviousPlain = 0xF3;

    public static byte[] Decode(ReadOnlySpan<byte> frame)
    {
        var decoded = frame.ToArray();
        var previousPlain = InitialPreviousPlain;

        for (var index = 2; index < decoded.Length; index++)
        {
            var encrypted =
                (decoded[index] + 3 - previousPlain) & 0xFF;
            var plain =
                CipherKey[(index - 2) & 0xFF] ^ encrypted;

            decoded[index] = (byte)plain;
            previousPlain = plain;
        }

        return decoded;
    }

    public static byte[] Encode(ReadOnlySpan<byte> decoded)
    {
        var encoded = decoded.ToArray();
        var previousPlain = InitialPreviousPlain;

        for (var index = 2; index < encoded.Length; index++)
        {
            var plain = decoded[index];
            var encrypted =
                ((CipherKey[(index - 2) & 0xFF] ^ plain) +
                 previousPlain - 3) & 0xFF;

            encoded[index] = (byte)encrypted;
            previousPlain = plain;
        }

        return encoded;
    }

    public static byte ComputeChecksum(ReadOnlySpan<byte> decoded)
    {
        if (decoded.Length < 2)
        {
            throw new ArgumentException(
                "A login frame must contain a length header and checksum byte.",
                nameof(decoded));
        }

        var checksum = 0;

        for (var index = 0; index < decoded.Length - 1; index++)
        {
            checksum =
                (checksum + decoded[index] + 0x3C) & 0xFF;
        }

        return (byte)checksum;
    }
}
