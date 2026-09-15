using System.Security.Cryptography;
using System.Text;
using God2.ServerV2.Application;

namespace God2.ServerV2.Application.Tests;

public sealed class Pbkdf2Sha256PasswordHashVerifierTests
{
    private const int Iterations = 100_000;

    [Fact]
    public void Accepts_matching_password()
    {
        var verifier = new Pbkdf2Sha256PasswordHashVerifier();
        var encoded = CreateHash(
            "secret",
            Convert.FromHexString("00112233445566778899AABBCCDDEEFF"));

        Assert.True(verifier.Verify("secret".AsMemory(), encoded));
    }

    [Fact]
    public void Rejects_wrong_password()
    {
        var verifier = new Pbkdf2Sha256PasswordHashVerifier();
        var encoded = CreateHash(
            "secret",
            Convert.FromHexString("00112233445566778899AABBCCDDEEFF"));

        Assert.False(verifier.Verify("wrong".AsMemory(), encoded));
    }

    [Theory]
    [InlineData("")]
    [InlineData("plain-text")]
    [InlineData("pbkdf2-sha256$abc$salt$hash")]
    [InlineData("pbkdf2-sha256$9999$AA==$AA==")]
    [InlineData("bcrypt$100000$AA==$AA==")]
    public void Rejects_malformed_or_unsupported_hash(string encoded)
    {
        var verifier = new Pbkdf2Sha256PasswordHashVerifier();

        Assert.False(verifier.Verify("secret".AsMemory(), encoded));
    }

    [Fact]
    public void Supports_unicode_password_bytes()
    {
        var verifier = new Pbkdf2Sha256PasswordHashVerifier();
        var encoded = CreateHash(
            "密碼測試",
            Convert.FromHexString("FFEEDDCCBBAA99887766554433221100"));

        Assert.True(verifier.Verify("密碼測試".AsMemory(), encoded));
    }

    private static string CreateHash(string password, byte[] salt)
    {
        var passwordBytes = Encoding.UTF8.GetBytes(password);
        var hash = new byte[32];

        try
        {
            Rfc2898DeriveBytes.Pbkdf2(
                passwordBytes,
                salt,
                hash,
                Iterations,
                HashAlgorithmName.SHA256);

            return
                $"pbkdf2-sha256$100000$" +
                $"{Convert.ToBase64String(salt)}$" +
                $"{Convert.ToBase64String(hash)}";
        }
        finally
        {
            Array.Clear(passwordBytes);
            Array.Clear(hash);
            Array.Clear(salt);
        }
    }
}
