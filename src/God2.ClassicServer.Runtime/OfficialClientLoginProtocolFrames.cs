using System.Globalization;
using God2.ClassicServer.Protocol;

namespace God2.ClassicServer.Runtime;

public enum OfficialLoginFailureCode : byte
{
    AccountNotRegistered = 0,
    DuplicateLogin = 1,
    AccountStopped = 2,
    CredentialsRejected = 3,
    NetworkDisconnected = 4,
    IllegalCharacter = 5,
    VersionMismatch = 6,
    EntitlementUnavailable = 7,
    ServerFull = 9
}

public static class OfficialClientLoginProtocolFrames
{
    public const int CharacterCreationBootstrapLength = 128;
    public const byte CharacterCreationBootstrapOpcode = 0x1F;
    public const string TargetClientBuild = "God2_opt official client build using stage-specific login version words 0x0022 and 0x00F0";
    public const ushort InitialLoginVersionWord = 0x0022;
    public const ushort CharacterCreationLoginVersionWord = 0x00F0;
    public const byte LoginVersionStatus = 0x01;
    public const byte InitialLoginVersionChecksum = 0x55;
    public const byte CharacterCreationLoginVersionChecksum = 0x23;

    private static readonly byte[] LoginSuccessServerGroupBootstrapTemplate =
        Convert.FromHexString(
            "A101098813A55DB88F7F19A7D71E4A8F9580DAA86A56B04465AD4D20A9DD18CC71C40E52993CB039B14DE02D6958137CC5D7037715078FDEFBC284356EF14EB45E48FBAD190C4F304DF18C640F8723707BFDB7E81DE0306DEFFE880664CF720D053C6796DA59B5126B4E49AAC7BE5C2825887D62498B9DDB8A88E01BD189439E44A8AA3BA4CCEBC5538F9E675BA5C78C0BA0DE4D63B0DE0E118914DB7DC6C94701414A5649C6FB3C5612DB0391E36B85746C601231F6F9F0CF28ADAD2F2F0D09A98AD15E72C3E442A15831E6322150F5AE0F991E780BFC6CFF9AF3BEDC701BB35F4FCCAB0B96B8D6A2367F780B0E81D2F3BFF6C80B88DD6710E22B26C330EC34D33D485CCD4319BF5064D2469BDC1A949B84DEA27ED76F6D75B44E3F274CD9B007A5E41E9F3CB037B94FE02D675B167CC5D9057B150791F007C2843B7DFC4EB4644908AD19065F3E4DF18767158C2778B803D1F424E1306DF1078D0664D17613053C699EDC59B510705149AAC9C86028258A8F6E498BA3D89588E015DC96439E3EA8B83BA4C8F0CC598FA2695AAAC78212A6EB5B4AD10AFD188914DB7DC6C950AB");

    // Exact decoded S2C frame observed immediately before the current client
    // emitted its verified 48-byte character-create request. These fields seed
    // the client's creation-version state; an empty 0x07 character-list frame
    // opens the screen but leaves that state unset and triggers message 184.
    private static readonly byte[] CharacterCreationBootstrapDecodedTemplate =
        Convert.FromHexString(
            "80001F02F0CFAA4732410000000000000000000100000000000000000000000000000002020000040000000138E302323134313030323037320000E8CEB00900000000000000000000000001020000030001007CF191772A5B7676000000003F5B767650C7C1D3E8CEB009F0ECA51EE8CEB009240000001FF5E87190FBAF2BA2");

    public static byte[] BuildLoginVersionFollowUp() =>
        BuildLoginVersionFollowUp(InitialLoginVersionWord, InitialLoginVersionChecksum);

    public static byte[] BuildCharacterCreationLoginVersionFollowUp() =>
        BuildLoginVersionFollowUp(CharacterCreationLoginVersionWord, CharacterCreationLoginVersionChecksum);

    private static byte[] BuildLoginVersionFollowUp(ushort versionWord, byte checksum)
    {
        byte[] decoded =
        [
            0x06,
            0x00,
            LoginVersionStatus,
            (byte)(versionWord & 0xFF),
            (byte)(versionWord >> 8),
            checksum
        ];
        return EncodeLoginFrame(decoded);
    }

    public static byte[] BuildCharacterCreationBootstrap() =>
        EncodeLoginFrame(CharacterCreationBootstrapDecodedTemplate);

    public static byte[] BuildLoginFailureResponse(OfficialLoginFailureCode failureCode)
    {
        if (!Enum.IsDefined(failureCode))
        {
            throw new ArgumentOutOfRangeException(
                nameof(failureCode),
                failureCode,
                "The official login failure result must be defined by the pinned client branch.");
        }

        // Exact-build static client evidence:
        // - login dispatcher RVA 0x0007E8F0 routes decoded opcode 0x1E to RVA 0x0007ECEC;
        // - that branch reads the first payload byte and RVA 0x0007EE20 maps value 3 to
        //   SystemMessage 189 (account/password rejected) from Data2/Patch/Comm/message.csvZ;
        // - the branch reads no additional payload fields. The common login envelope therefore
        //   contains length, opcode, one result byte, and the verified login checksum.
        byte[] decoded = [0x05, 0x00, 0x1E, (byte)failureCode, 0x00];
        decoded[^1] = ComputeLoginChecksum(decoded);
        var encoded = EncodeLoginFrame(decoded);
        Array.Clear(decoded);
        return encoded;
    }

    public static byte[] BuildLoginSuccessServerGroupBootstrap(ReadOnlyMemory<byte> loginRequestFrame, ReadOnlySpan<byte> serverListIpAddress, ushort serverListPort)
    {
        if (loginRequestFrame.Length != 208)
        {
            throw new ArgumentException("Official login success bootstrap requires the decoded echo source from a 208-byte LoginRequest.", nameof(loginRequestFrame));
        }

        var decoded = DecodeLoginFrame(LoginSuccessServerGroupBootstrapTemplate);
        var loginRequestDecoded = DecodeLoginFrame(loginRequestFrame.Span);
        CopyLoginEchoField(decoded, loginRequestDecoded, 3, 24);
        CopyLoginEchoField(decoded, loginRequestDecoded, 27, 24);
        Array.Clear(loginRequestDecoded);

        PatchServerListEndpoints(decoded, serverListIpAddress, serverListPort);
        decoded[^1] = ComputeLoginChecksum(decoded);
        var encoded = EncodeLoginFrame(decoded);
        Array.Clear(decoded);
        return encoded;
    }

    public static bool TryDecodeLoginRequest(
        ReadOnlySpan<byte> loginRequestFrame,
        out string username,
        out string password)
    {
        username = string.Empty;
        password = string.Empty;
        if (!TryDecodeSensitiveLoginRequest(loginRequestFrame, out username, out var passwordCharacters))
        {
            return false;
        }

        try
        {
            password = new string(passwordCharacters);
            return true;
        }
        finally
        {
            Array.Clear(passwordCharacters);
        }
    }

    public static bool TryDecodeSensitiveLoginRequest(
        ReadOnlySpan<byte> loginRequestFrame,
        out string username,
        out char[] password)
    {
        username = string.Empty;
        password = [];
        if (loginRequestFrame.Length != 208)
        {
            return false;
        }

        var decoded = DecodeLoginFrame(loginRequestFrame);
        try
        {
            if (decoded.Length != 208 || decoded[0] != 0xD0 || decoded[1] != 0x00)
            {
                return false;
            }

            username = ReadNullTerminatedAscii(decoded.AsSpan(3, 24));
            password = ReadNullTerminatedAsciiCharacters(decoded.AsSpan(27, 24));
            if (username.Length > 0 && password.Length > 0)
            {
                return true;
            }

            Array.Clear(password);
            password = [];
            username = string.Empty;
            return false;
        }
        finally
        {
            Array.Clear(decoded);
        }
    }

    public static byte[] BuildServerSelectionCharacterListBootstrap()
    {
        var result = OfficialServerSelectionWireCodec.SerializeResponse(
            OfficialServerSelectionWireCodec.GoldenResponseModel());
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(result.Error.Message);
        }

        return result.Value.ToArray();
    }

    public static byte[] DecodeLoginFrame(ReadOnlySpan<byte> frame) => OfficialLoginWireTransform.Decode(frame);

    public static string Hex(byte[] bytes) => Convert.ToHexString(bytes).ToLowerInvariant();

    public static string FormatDecodedWord(ushort value) => $"0x{value.ToString("X4", CultureInfo.InvariantCulture)}";

    public static byte[] EncodeLoginFrame(ReadOnlySpan<byte> decoded) => OfficialLoginWireTransform.Encode(decoded);

    private static void CopyLoginEchoField(byte[] targetDecoded, byte[] sourceDecoded, int offset, int length)
    {
        Array.Clear(targetDecoded, offset, length);
        Buffer.BlockCopy(sourceDecoded, offset, targetDecoded, offset, length);
    }

    private static string ReadNullTerminatedAscii(ReadOnlySpan<byte> field)
    {
        var terminator = field.IndexOf((byte)0);
        var value = terminator >= 0 ? field[..terminator] : field;
        if (value.IsEmpty || value.IndexOfAnyExceptInRange((byte)0x21, (byte)0x7E) >= 0)
        {
            return string.Empty;
        }

        return System.Text.Encoding.ASCII.GetString(value);
    }

    private static char[] ReadNullTerminatedAsciiCharacters(ReadOnlySpan<byte> field)
    {
        var terminator = field.IndexOf((byte)0);
        var value = terminator >= 0 ? field[..terminator] : field;
        if (value.IsEmpty || value.IndexOfAnyExceptInRange((byte)0x21, (byte)0x7E) >= 0)
        {
            return [];
        }

        var characters = new char[value.Length];
        for (var index = 0; index < value.Length; index++)
        {
            characters[index] = (char)value[index];
        }

        return characters;
    }

    private static void PatchServerListEndpoints(byte[] decoded, ReadOnlySpan<byte> ipAddress, ushort port)
    {
        if (ipAddress.Length != 4)
        {
            throw new ArgumentException("IPv4 endpoint patch requires exactly four address bytes.", nameof(ipAddress));
        }

        foreach (var offset in new[] { 215, 274, 333 })
        {
            decoded[offset] = ipAddress[0];
            decoded[offset + 1] = ipAddress[1];
            decoded[offset + 2] = ipAddress[2];
            decoded[offset + 3] = ipAddress[3];
            decoded[offset + 4] = (byte)(port & 0xFF);
            decoded[offset + 5] = (byte)(port >> 8);
        }
    }

    private static byte ComputeLoginChecksum(byte[] decoded) => OfficialLoginWireTransform.ComputeChecksum(decoded);
}
