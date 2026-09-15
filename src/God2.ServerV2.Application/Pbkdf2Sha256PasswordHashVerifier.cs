using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace God2.ServerV2.Application;

public interface IPasswordHashVerifier
{
    bool Verify(ReadOnlyMemory<char> password, string encodedHash);
}

public sealed class Pbkdf2Sha256PasswordHashVerifier : IPasswordHashVerifier
{
    private const string Scheme = "pbkdf2-sha256";
    private const int MinimumIterations = 10_000;
    private const int MaximumIterations = 10_000_000;
    private const int ExpectedSaltLength = 16;
    private const int ExpectedHashLength = 32;

    public bool Verify(ReadOnlyMemory<char> password, string encodedHash)
    {
        if (password.IsEmpty || string.IsNullOrWhiteSpace(encodedHash))
        {
            return false;
        }

        var parts = encodedHash.Split('$', StringSplitOptions.None);

        if (parts.Length != 4 ||
            !string.Equals(parts[0], Scheme, StringComparison.Ordinal))
        {
            return false;
        }

        if (!int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var iterations) ||
            iterations is < MinimumIterations or > MaximumIterations)
        {
            return false;
        }

        byte[] salt;
        byte[] expectedHash;

        try
        {
            salt = Convert.FromBase64String(parts[2]);
            expectedHash = Convert.FromBase64String(parts[3]);
        }
        catch (FormatException)
        {
            return false;
        }

        if (salt.Length != ExpectedSaltLength || expectedHash.Length != ExpectedHashLength)
        {
            Array.Clear(salt);
            Array.Clear(expectedHash);
            return false;
        }

        var passwordBytes = new byte[
            Encoding.UTF8.GetByteCount(password.Span)];
        Encoding.UTF8.GetBytes(
            password.Span,
            passwordBytes);
        var actualHash = new byte[ExpectedHashLength];

        try
        {
            Rfc2898DeriveBytes.Pbkdf2(
                passwordBytes,
                salt,
                actualHash,
                iterations,
                HashAlgorithmName.SHA256);

            return CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
        }
        finally
        {
            Array.Clear(passwordBytes);
            Array.Clear(actualHash);
            Array.Clear(salt);
            Array.Clear(expectedHash);
        }
    }
}
