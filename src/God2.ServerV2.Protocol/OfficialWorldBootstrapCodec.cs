using System.Buffers.Binary;
using System.Text;

namespace God2.ServerV2.Protocol;

public static class OfficialWorldBootstrapCodec
{
    public const int PayloadLength = 1772;
    public const int PlayerSpawnFrameLength = 128;
    public const int PlayerNameOffset = 7;
    public const int PlayerNameLength = 12;
    public const int PlayerIdOffset = 19;
    public const byte PlayerSpawnOpcode = 0x1F;

    private static readonly byte[] EncodedPayloadTemplate =
        Convert.FromHexString(
            "80008FB939F4B46A74EE3D2AC53720D7CC4F4F6AA5B5542B7319434EFCCC5C0EE6405306F74BF821C3B9D63B773780CFC56F7B867352191F72607AAC" +
            "6D9581203DE3709B771997145B5D8274E30FAFD25D972DA2084F9AF282736A8B3955FE49F8041C4A496C91E197D8F85260CB9781AE61A97DCB98E5DA" +
            "8A118D2BDFB5A5914001AC9EC7C53FA566FB9EBBC53720D7CC4F4F96C863F8287333C6277C4C5703FF595508F749F79E40DA355DCD0FBF3F93A7C22A" +
            "7614990813487A444377C3F939D4C93C6981E9BE5B5D8273E40DAFD35A982CD6ACB9937A265D8B1B3F55FE62FAD7DC843866FDD1A74F8492D07A44F6" +
            "3523F250A798E5BF98F4D62A6FEEEB3914741F2902C052A767226B42681C85F0EA3BEDE1C2BEEFC91B2D391D544C55B3424FBF29732578288B490912" +
            "6669F7E88AE6E138A03045D4DD94A3BC04607058C0167A51D0D02C26AFC4EED433DFEB878FAF0205D9134092F0C66039BCF9A2153EEA3B98B97A3CF8" +
            "93C57D87DA8C0B35D281398FA1A6554A1A4E213B9EAA9D769801C9C3D1C53FA566FB9E62A40358E8F25A4F96C863F828AAF07F86CBF1680EE6405308" +
            "F794D719F78A02493EDE9FDFA4729D444F4CE8F9172E7A444377DA0EF002837B93C53FB184FD9E85FB3720129B4F4F125D01720D9C1E434EF4D45C0E" +
            "E8425308F84AF81DBFB9D63C42E29FDFA47278657314D7C2092E7A444686DA173DE3709B791F9714496F8273DF12AFD375BD2CD6575D4451FD58B415" +
            "3991CA70E4E91284384D52147BBA8A889C44F4A50623371FA798C7DF9AF4D630C432F60D3750349DA3F8335D2DFB944C662B53C02A4A553BCFD326F2" +
            "1FF50AEA4E4F56B35B6AE81D8F26421DDAC01212589BC61B6C4CF3028FC610D4DD88BDBE0474686C88FE6F4CD0D02C26ADC50F3124D3DCCC46F10F06" +
            "EC4540257FCB5B65EBF2A2153EEA3B9864BF64C0A49C7587DA8C0BD8DA8964B340C62D4192D6213B2632789782F3FA9CC7C53FA5481D9EBBC53720D7" +
            "E2794F96F6959A8E6A34434EC9FF3773DB4B56520049F81D4F16C37949DE9FDFA47278657314D7C20952DD505877EE4396BC709B771997145B5D8273" +
            "E4F438A678D20BF87B74FE53FE6A7E153B57FE70E4C3E49E4167FDF679BA3FB99C44E88B0423F250A79D21E5BDF41305BD14C95F63431F2A02DA33A9" +
            "781A9476302B53EFEA3B533BC1BD26F21FF52711524C55790C4FBFF49C1940174176243F8AAE08E7614DE6FFBA3141D4DDB6D1BC04B5BD8FE12546AD" +
            "2828CC23BDC42734AD604430F9AE776DD599567A9C11A98B1F2EBD5937E3914C9103B24DB87BE5EBC3198554D155A5BB840D5764BF50BB50C506ACD9" +
            "D2E21334E614B72D9AD3F9C61B8C8028AE7475F918679974AB3D434EFD2C9CEAE24F53088F9D00C165CA2273A00203BE2F80678784AEE40ACD88A9B7" +
            "5087D2BFDDE37044501997145B5D8288F11DB77CFB982CFD54ED7851FD6A7EE2266506D686D7DC5B1166FDF679BA3F3E8954E032A723F2878098E5BF" +
            "98F4D6CDB31FD7A5C7431FF2DBD933A961FB94811D3B5B5C8F3B53E49ABD26F21FF50A9C24275AB3C7BC21F41B74127AA809465B2DAFE7F821209606" +
            "8F10F674DD924BBE6D865E25D16C8C4EEEB0F1E7B5C43C6A7F8EDDB4CB22D3CA965E0795FD165682B604A2948F2C3B372BB5710D63D5C0AE14902794" +
            "44000E0078D2C7C53FB6CCFB77BBC5282D003C9384CB3FBADB9B9EF9613991248B189AD1127714482AD2494EE7812CE3AFE82569440EB16882FFF65C" +
            "02190078D2C7C53FB600FB77BBC50956D7CC4F405CE63CF828CE0E0078D2C7C53FB6CCFB77BBC5282D003C9384CB3FBADB9B9E19C13991248B189ADD" +
            "0E4318482AD2494EE78184CF53482569440EB1688A07C056B02D003C9384CB3FBADB9B9EF9613991248B189AD5167912482AD2494EE7812CE3AFE825" +
            "69440EB16882FFBE547C2D003C9384CB3FBADB9B9E59013991248B189ADD0E7B10482AD2494EE78184CF13882569440EB1688E0BBE542A2D003C9384" +
            "CB3FBADB9B9ED98139912400189AC91A7B10482AD2494EE78184CF93082569440EB1688603C45A342D003C9384CB3F00DB9B9E79E13991248B189AD1" +
            "127516482AD2494EE78184CF33682569440EB168920FC0567A2D003C9384CB3FBADB9B9EF9613991248B189ACD1E7912482A00494EE7819CBBB7E825" +
            "69440EB16882FFF65C642D003C9384CB3FBADB9B9E79E13991248B189ADD0E4300482AD2494EE7812CE32F682569440EB1688A07C258062D003C9384" +
            "CB3FBADB9B9EF9613991248B189AD5167714482AD2494EE78154F7ABE82569440EB1688603C2580007004DF895246D0E0078D2C7C53FB6CCFB77BBC5" +
            "282D003C9384CB3FBADB9B9E59013991248B189AD1127714482AD2494EE7812CE30F882569440EB1688E0BBE54C62D003C9384CB3FBADB9B9EF96139" +
            "91248B189AC91A0010482AD2494EE7812CE3AFE82569440EB1688603C2580609");

    private static readonly byte[] WorldCipherKey =
        Convert.FromHexString(
            "FD9FCAC842A869FEA1BEC83A23DACF525299CB66FB2B761C4651FFCF5F11E943560BFA4CFB20C2BCD93F41E1A2E2A7757B687617DAC50C317D47467A" +
            "DD1A40E6739E7A1C9A175E608576E710B2D65D9B2FD97EF07B54006D81183C580173E7DADF873B6900F97CBD42BC9F47EB8E0726F553AA9BE8C29BF7" +
            "D92DA912D2042646222C05DC36AC64FE9779332E56F2ED3E563EC4C029F522F80DED554F58B64552C2F79F1C431A5E490C15696CFAEB560AD8039206" +
            "44D7E097A6BF07B89C6F8B28494FD3D32F29B2C72AF80AAADF5F8CB20508DC16434E1CFD2B0D89F5A51841ED3E9B67A603C3A79F788ADD8F0E38D55F" +
            "65B66C982E441D51243EA1AD7B9AAFCC");

    public static byte[] EncodeAfterFirstFollowUp(
        long characterId,
        string characterName,
        string? classCode,
        string? genderCode,
        string? appearanceCode)
    {
        if (characterId is <= 0 or > uint.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(characterId));
        }

        if (!string.Equals(classCode, "Swordsman", StringComparison.Ordinal) ||
            !string.Equals(genderCode, "Female", StringComparison.Ordinal) ||
            !string.Equals(appearanceCode, "Appearance1", StringComparison.Ordinal))
        {
            throw new NotSupportedException(
                "Only the evidence-verified Female Swordsman Appearance1 profile is supported.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(characterName);
        var name = Encoding.ASCII.GetBytes(characterName);

        if (name.Length >= PlayerNameLength ||
            name.Any(value => value is < 0x21 or > 0x7E))
        {
            Array.Clear(name);
            throw new ArgumentException(
                "World character name must contain 1-11 printable ASCII bytes.",
                nameof(characterName));
        }

        var payload = EncodedPayloadTemplate.ToArray();
        var decodedSpawn = DecodeFrame(
            payload.AsSpan(0, PlayerSpawnFrameLength));

        try
        {
            decodedSpawn.AsSpan(
                PlayerNameOffset,
                PlayerNameLength).Clear();

            name.CopyTo(decodedSpawn, PlayerNameOffset);

            BinaryPrimitives.WriteUInt32LittleEndian(
                decodedSpawn.AsSpan(PlayerIdOffset, sizeof(uint)),
                checked((uint)characterId));

            decodedSpawn[^1] =
                OfficialLoginWireTransform.ComputeChecksum(decodedSpawn);

            var encodedSpawn = EncodeFrame(decodedSpawn);

            try
            {
                encodedSpawn.CopyTo(payload, 0);
            }
            finally
            {
                Array.Clear(encodedSpawn);
            }

            return payload;
        }
        finally
        {
            Array.Clear(decodedSpawn);
            Array.Clear(name);
        }
    }

    public static byte[] DecodeFirstPlayerSpawn(
        ReadOnlySpan<byte> payload)
    {
        if (payload.Length < PlayerSpawnFrameLength)
        {
            throw new ArgumentException(
                "World bootstrap payload is truncated.",
                nameof(payload));
        }

        return DecodeFrame(payload[..PlayerSpawnFrameLength]);
    }

    /// <summary>
    /// Appends only destinations admitted by the pinned client map transition evidence.
    /// The caller must resolve the formal map identity and validate its bounds first.
    /// </summary>
    public static byte[] EncodeWithVerifiedLocation(
        long characterId,
        string characterName,
        string? classCode,
        string? genderCode,
        string? appearanceCode,
        string clientBuildId,
        ushort clientMapId,
        byte clientAreaId,
        int x,
        int y)
    {
        var location = OfficialPortalWireCodec.SerializeWorldProjectionDestination(
            clientBuildId, clientMapId, clientAreaId, x, y);
        if (!location.Succeeded || location.Value is null)
        {
            throw new NotSupportedException(
                $"World login location has no verified wire projection: {location.FailureCode}");
        }

        var bootstrap = EncodeAfterFirstFollowUp(
            characterId, characterName, classCode, genderCode, appearanceCode);
        try
        {
            var frames = location.Value.OrderedDecodedFrames
                .Select(frame => EncodeFrame(frame.Span))
                .ToArray();
            try
            {
                var result = new byte[bootstrap.Length + frames.Sum(frame => frame.Length)];
                bootstrap.CopyTo(result, 0);
                var offset = bootstrap.Length;
                foreach (var frame in frames)
                {
                    frame.CopyTo(result, offset);
                    offset += frame.Length;
                }
                return result;
            }
            finally
            {
                foreach (var frame in frames)
                    Array.Clear(frame);
            }
        }
        finally
        {
            Array.Clear(bootstrap);
        }
    }

    public static byte[] DecodeFrame(ReadOnlySpan<byte> frame)
    {
        var decoded = frame.ToArray();
        var previousPlain = 0xB0;

        for (var index = 2; index < decoded.Length; index++)
        {
            var encrypted =
                (decoded[index] + 3 - previousPlain) & 0xFF;
            var plain =
                WorldCipherKey[(index - 2) & 0xFF] ^ encrypted;

            decoded[index] = (byte)plain;
            previousPlain = plain;
        }

        return decoded;
    }

    internal static byte[] EncodeFrame(ReadOnlySpan<byte> decoded)
    {
        var encoded = decoded.ToArray();
        var previousPlain = 0xB0;

        for (var index = 2; index < encoded.Length; index++)
        {
            var plain = decoded[index];
            encoded[index] = (byte)(
                (WorldCipherKey[(index - 2) & 0xFF] ^ plain) +
                previousPlain -
                3);

            previousPlain = plain;
        }

        return encoded;
    }
}
