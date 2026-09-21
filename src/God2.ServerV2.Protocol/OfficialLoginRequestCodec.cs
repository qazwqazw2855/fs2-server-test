namespace God2.ServerV2.Protocol;

public sealed class SensitiveLoginRequest : IDisposable
{
    private char[] _password;
    private bool _disposed;

    internal SensitiveLoginRequest(
        string accountName,
        char[] password)
    {
        AccountName = accountName;
        _password = password;
    }

    public string AccountName { get; }

    public ReadOnlyMemory<char> Password
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _password;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Array.Clear(_password);
        _password = [];
        _disposed = true;
    }
}

public static class OfficialLoginRequestCodec
{
    public const int FrameLength = 208;
    public const int AccountOffset = 3;
    public const int AccountLength = 24;
    public const int PasswordOffset = 27;
    public const int PasswordLength = 24;

    public static string Diagnose(ReadOnlySpan<byte> encodedFrame)
    {
        if (encodedFrame.Length != FrameLength)
        {
            return $"length:{encodedFrame.Length}";
        }

        if (encodedFrame[0] != 0xD0 || encodedFrame[1] != 0x00)
        {
            return $"encoded-prefix:{encodedFrame[0]:X2}{encodedFrame[1]:X2}";
        }

        var decoded = DecodeCurrentClientLogin(encodedFrame);

        try
        {
            var accountLength =
                FieldLength(decoded.AsSpan(AccountOffset, AccountLength));

            if (accountLength == 0)
            {
                return "account-field-invalid";
            }

            var passwordLength =
                FieldLength(decoded.AsSpan(PasswordOffset, PasswordLength));

            if (passwordLength == 0)
            {
                return "password-field-invalid";
            }

            return $"ok:accountLength={accountLength},passwordLength={passwordLength}";
        }
        finally
        {
            Array.Clear(decoded);
        }
    }

    public static bool TryDecode(
        ReadOnlySpan<byte> encodedFrame,
        out SensitiveLoginRequest? request)
    {
        request = null;

        if (encodedFrame.Length != FrameLength ||
            encodedFrame[0] != 0xD0 ||
            encodedFrame[1] != 0x00)
        {
            return false;
        }

        var decoded =
            DecodeCurrentClientLogin(encodedFrame);

        if (!HasValidCredentialFields(decoded))
        {
            Array.Clear(decoded);
            decoded = OfficialLoginWireTransform.Decode(encodedFrame);
        }

        try
        {
            // The verified Classic/current-client login path does not require
            // the final byte of the 208-byte LoginRequest to satisfy the
            // generic login checksum formula. Validate the decoded credential
            // fields instead; later protocol families retain their own
            // checksum validation.
            var account = ReadAscii(
                decoded.AsSpan(AccountOffset, AccountLength));

            var password = ReadAsciiCharacters(
                decoded.AsSpan(PasswordOffset, PasswordLength));

            if (account.Length == 0 ||
                password.Length == 0)
            {
                Array.Clear(password);
                return false;
            }

            request = new SensitiveLoginRequest(
                account,
                password);
            return true;
        }
        finally
        {
            Array.Clear(decoded);
        }
    }

    internal static byte[] DecodeCurrentClientLogin(
        ReadOnlySpan<byte> frame)
    {
        ReadOnlySpan<byte> cipherKey =
            Convert.FromHexString(
                "B20DBBA18DA4B99A0C2480B2B88574D2F9CC806862D6CFC6594E48DE5243D87AFD3EAF4E62CB1B66EC3C8AF0951496AC229B2B19417065DB90448F89D04C2F1D1AD12F1E01C2B70A9873301FA5084CD2D60C33F2E2FF591794158567F4A5D61186BAF06B67A4D56755B9E856DEC0F65D9C865F9D4F6DF4A1EC2AF221C536157DC95FBADE9D57BDA8AA767C3D4924DC75FD7179C290A07BA220EA9E5E4BE83489692A541EAAC5B7F31F12B27BED1D45417F364708AB823941B9BC50C78FA17A5FF08288D596D40B713DF553B9D2917BE9AC3B9118AAF975A73E0AD204984A3024E5CE1CA86A6E00498B87269E016AC4960CE9219A95EF76FB373BEABC6ECC9E03");

        var decoded = frame.ToArray();
        var previousPlain = 0xF3;

        for (var index = 2; index < decoded.Length; index++)
        {
            var encrypted =
                (decoded[index] + 3 - previousPlain) & 0xFF;

            var plain =
                cipherKey[(index - 2) & 0xFF] ^ encrypted;

            decoded[index] = (byte)plain;
            previousPlain = plain;
        }

        return decoded;
    }

    internal static bool HasValidCredentialFields(
        ReadOnlySpan<byte> decoded)
    {
        return
            FieldLength(decoded.Slice(AccountOffset, AccountLength)) > 0 &&
            FieldLength(decoded.Slice(PasswordOffset, PasswordLength)) > 0;
    }

    private static string ReadAscii(
        ReadOnlySpan<byte> field)
    {
        var length = FieldLength(field);

        if (length == 0)
        {
            return string.Empty;
        }

        return System.Text.Encoding.ASCII.GetString(
            field[..length]);
    }

    private static char[] ReadAsciiCharacters(
        ReadOnlySpan<byte> field)
    {
        var length = FieldLength(field);

        if (length == 0)
        {
            return [];
        }

        var value = new char[length];

        for (var index = 0; index < length; index++)
        {
            value[index] = (char)field[index];
        }

        return value;
    }

    private static int FieldLength(
        ReadOnlySpan<byte> field)
    {
        var terminator = field.IndexOf((byte)0);
        var value = terminator >= 0
            ? field[..terminator]
            : field;

        if (value.IsEmpty ||
            value.IndexOfAnyExceptInRange(
                (byte)0x21,
                (byte)0x7E) >= 0)
        {
            return 0;
        }

        return value.Length;
    }
}
