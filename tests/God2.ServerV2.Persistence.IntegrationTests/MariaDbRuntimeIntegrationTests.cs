using God2.ServerV2.Application;
using God2.ServerV2.Persistence;

namespace God2.ServerV2.Persistence.IntegrationTests;

public sealed class MariaDbRuntimeIntegrationTests
{
    [Fact]
    [Trait("Category", "Integration")]
    public async Task God2TestAccount_AcceptsCorrectPassword_AndRejectsWrongPassword()
    {
        if (!ShouldRun() ||
            Environment.GetEnvironmentVariable("GOD2_TEST_PASSWORD")
                is not { Length: > 0 } password)
        {
            return;
        }
        var authenticator = new MariaDbAccountAuthenticator(
            CreateOptions(),
            new Pbkdf2Sha256PasswordHashVerifier());

        var accepted = await authenticator.ValidateCredentialsAsync(
            "god2test",
            password.AsMemory(),
            CancellationToken.None);

        var rejected = await authenticator.ValidateCredentialsAsync(
            "god2test",
            "definitely-wrong-password".AsMemory(),
            CancellationToken.None);

        Assert.Equal(AccountAuthenticationResult.Accepted(1), accepted);
        Assert.Equal(AccountAuthenticationResult.Rejected, rejected);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task AccountOne_HasExpectedTestCharacter()
    {
        if (!ShouldRun())
        {
            return;
        }

        var repository = new MariaDbCharacterListRepository(CreateOptions());

        var characters = await repository.ListByAccountAsync(
            1,
            CancellationToken.None);

        var character = Assert.Single(characters);

        Assert.Equal(1, character.CharacterId);
        Assert.Equal(1, character.AccountId);
        Assert.Equal("test001", character.Name);
        Assert.Equal("Swordsman", character.ClassCode);
        Assert.Equal("Female", character.GenderCode);
        Assert.Equal(1, character.Level);
        Assert.Equal(1675308248, character.MapId);
        Assert.True(character.RuntimeVersion > 0);
        Assert.Equal(32, character.ConcurrencyToken.Length);
    }

    private static bool ShouldRun() =>
        string.Equals(
            Environment.GetEnvironmentVariable("GOD2_RUN_DB_INTEGRATION"),
            "1",
            StringComparison.Ordinal);

    private static MariaDbAuthenticationOptions CreateOptions() =>
        new(
            Required("GOD2_DB_HOST"),
            int.Parse(Required("GOD2_DB_PORT")),
            Required("GOD2_DB_USER"),
            Required("GOD2_DB_PASSWORD"));

    private static string Required(string name) =>
        Environment.GetEnvironmentVariable(name) is { Length: > 0 } value
            ? value
            : throw new InvalidOperationException(
                $"Required environment variable is missing: {name}");
}
