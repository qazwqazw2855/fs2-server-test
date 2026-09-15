namespace God2.ServerV2.Protocol;

public static class OfficialLoginSuccessCodec
{
    public const int FrameLength = 417;
    public const byte Opcode = 0x1D;

    private static readonly byte[] EncodedTemplate =
        Convert.FromHexString(
            "A101098813A55DB88F7F19A7D71E4A8F9580DAA86A56B04465AD4D20A9DD18CC71C40E52993CB039B14DE02D6958137CC5D7037715078FDEFBC284356EF14EB45E48FBAD190C4F304DF18C640F8723707BFDB7E81DE0306DEFFE880664CF720D053C6796DA59B5126B4E49AAC7BE5C2825887D62498B9DDB8A88E01BD189439E44A8AA3BA4CCEBC5538F9E675BA5C78C0BA0DE4D63B0DE0E118914DB7DC6C94701414A5649C6FB3C5612DB0391E36B85746C601231F6F9F0CF28ADAD2F2F0D09A98AD15E72C3E442A15831E6322150F5AE0F991E780BFC6CFF9AF3BEDC701BB35F4FCCAB0B96B8D6A2367F780B0E81D2F3BFF6C80B88DD6710E22B26C330EC34D33D485CCD4319BF5064D2469BDC1A949B84DEA27ED76F6D75B44E3F274CD9B007A5E41E9F3CB037B94FE02D675B167CC5D9057B150791F007C2843B7DFC4EB4644908AD19065F3E4DF18767158C2778B803D1F424E1306DF1078D0664D17613053C699EDC59B510705149AAC9C86028258A8F6E498BA3D89588E015DC96439E3EA8B83BA4C8F0CC598FA2695AAAC78212A6EB5B4AD10AFD188914DB7DC6C950AB");

    public static byte[] EncodeServerGroupBootstrap(
        ReadOnlySpan<byte> encodedLoginRequest,
        ReadOnlySpan<byte> serverListIpv4Address,
        ushort serverListPort)
    {
        if (encodedLoginRequest.Length != OfficialLoginRequestCodec.FrameLength)
        {
            throw new ArgumentException(
                "Login success bootstrap requires the original 208-byte login request.",
                nameof(encodedLoginRequest));
        }

        if (serverListIpv4Address.Length != 4)
        {
            throw new ArgumentException(
                "The advertised server-list address must be IPv4.",
                nameof(serverListIpv4Address));
        }

        var decoded = OfficialLoginWireTransform.Decode(EncodedTemplate);
        var loginRequestDecoded =
            OfficialLoginWireTransform.Decode(encodedLoginRequest);

        try
        {
            CopyLoginEchoField(
                decoded,
                loginRequestDecoded,
                OfficialLoginRequestCodec.AccountOffset,
                OfficialLoginRequestCodec.AccountLength);
            CopyLoginEchoField(
                decoded,
                loginRequestDecoded,
                OfficialLoginRequestCodec.PasswordOffset,
                OfficialLoginRequestCodec.PasswordLength);

            PatchServerListEndpoints(
                decoded,
                serverListIpv4Address,
                serverListPort);

            decoded[^1] =
                OfficialLoginWireTransform.ComputeChecksum(decoded);

            return OfficialLoginWireTransform.Encode(decoded);
        }
        finally
        {
            Array.Clear(loginRequestDecoded);
            Array.Clear(decoded);
        }
    }

    private static void CopyLoginEchoField(
        byte[] target,
        byte[] source,
        int offset,
        int length)
    {
        Array.Clear(target, offset, length);
        Buffer.BlockCopy(source, offset, target, offset, length);
    }

    private static void PatchServerListEndpoints(
        byte[] decoded,
        ReadOnlySpan<byte> address,
        ushort port)
    {
        foreach (var offset in new[] { 215, 274, 333 })
        {
            address.CopyTo(decoded.AsSpan(offset, 4));
            decoded[offset + 4] = (byte)(port & 0xFF);
            decoded[offset + 5] = (byte)(port >> 8);
        }
    }
}
