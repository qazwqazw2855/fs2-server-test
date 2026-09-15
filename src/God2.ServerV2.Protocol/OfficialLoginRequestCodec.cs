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
            OfficialLoginWireTransform.Decode(encodedFrame);

        try
        {
            if (decoded[^1] !=
                OfficialLoginWireTransform.ComputeChecksum(decoded))
            {
                return false;
            }

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
