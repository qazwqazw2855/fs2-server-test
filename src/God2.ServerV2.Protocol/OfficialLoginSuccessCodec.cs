namespace God2.ServerV2.Protocol;

public static class OfficialLoginSuccessCodec
{
    public const int FrameLength = 417;
    public const byte Opcode = 0x1D;

    private static readonly byte[] CurrentClientDecodedTemplate =
        Convert.FromHexString(
            "A1011D00000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000091720000322405000900C8000000000000000000000000000000000000000000000000000000000000000000000000000400010700050004010F1FE2210A060100000006050000000606000000060200000006030000000604000000060C000000060B000000060D000000060E000000050104010F1FE2230A060100000006050000000606000000060200000006030000000604000000060C000000060B000000060D000000060E000000050204010F1FE2240A060100000006050000000606000000060200000006030000000604000000060C000000060B000000060D000000060E000000040106000004020100000A01091700131BEA071E000000000000005A");

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

        var decoded = CurrentClientDecodedTemplate.ToArray();
        var loginRequestDecoded =
            OfficialLoginRequestCodec.DecodeCurrentClientLogin(
                encodedLoginRequest);

        if (!OfficialLoginRequestCodec.HasValidCredentialFields(
                loginRequestDecoded))
        {
            Array.Clear(loginRequestDecoded);
            loginRequestDecoded =
                OfficialLoginWireTransform.Decode(encodedLoginRequest);
        }

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
                CurrentClientServerWireTransform.ComputeChecksum(decoded);

            return CurrentClientServerWireTransform.Encode(decoded);
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
