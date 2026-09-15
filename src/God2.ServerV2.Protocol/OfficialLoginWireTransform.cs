namespace God2.ServerV2.Protocol;

public static class OfficialLoginWireTransform
{
    private static readonly byte[] CipherKey =
        Convert.FromHexString(
            "405FD0401BB55367D34D90DF1D929883DDAB6D59B34768B050422A49D3AE0AA8E11D9C3FB33CB450E3306C5B167FC8DA067A180A92E1FEC5873871F451B7614BFEB01C0F523350F48F67128A26737E00BAEB20E33370F2018B0967D27510083F6A99DD5CB8156E514CADCAC15F2B288B80654C8EA0DE8D8BE31ED48C46A147ABAD3EA7CFEEC85692A16A5EA8CA8F0EA3E15066B3E111148C17DE80C9CC5366B74D09410FFB3650DD160694E66E88776F631534F9FCF3D22BB0B03232100CAC8DD46175C6E745A45B34E9352453FCAD17901A7E09FB6A0E9135FEB46F19B56252C9AD0999BBDF9933827B080982D5F6C4F0C80E8BE6600FE52E2FCC27EF37D031");

    public static byte[] Decode(ReadOnlySpan<byte> frame)
    {
        var decoded = frame.ToArray();
        var previousPlain = 0xAF;

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
        var previousPlain = 0xAF;

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

    public static byte ComputeChecksum(
        ReadOnlySpan<byte> decoded)
    {
        if (decoded.Length < 2)
        {
            throw new ArgumentException(
                "A login frame must contain a length header and checksum byte.",
                nameof(decoded));
        }

        var checksum = 0;

        for (var index = 0;
             index < decoded.Length - 1;
             index++)
        {
            checksum =
                (checksum + decoded[index] + 0x3C) & 0xFF;
        }

        return (byte)checksum;
    }
}
