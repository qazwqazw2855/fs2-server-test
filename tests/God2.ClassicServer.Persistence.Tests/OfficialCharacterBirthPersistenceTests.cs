namespace God2.ClassicServer.Persistence.Tests;

public sealed class OfficialCharacterBirthPersistenceTests
{
    [Fact]
    public void Character_creation_writes_the_verified_birth_profile_to_canonical_columns()
    {
        var source = File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "src",
            "God2.ClassicServer.Persistence",
            "MariaDbRuntimeRepositories.cs"));

        Assert.Contains("OfficialCharacterBirthProfiles.ResolveClientClass(request.Class)", source, StringComparison.Ordinal);
        Assert.Contains("`current_hp`, `max_hp`, `current_mp`, `max_mp`", source, StringComparison.Ordinal);
        Assert.Contains("`constitution_base`, `constitution_bonus`, `strength_base`, `strength_bonus`", source, StringComparison.Ordinal);
        Assert.Contains("`intelligence_base`, `intelligence_bonus`, `speed_base`, `speed_bonus`", source, StringComparison.Ordinal);
        Assert.Contains("`physical_attack_base`, `physical_attack_bonus`, `physical_defense_base`, `physical_defense_bonus`", source, StringComparison.Ordinal);
        Assert.Contains("@maximumHp, @maximumHp, @maximumMp, @maximumMp", source, StringComparison.Ordinal);
        Assert.Contains("command.Parameters.AddWithValue(\"@constitution\", birthProfile.PrimaryAttributes.Constitution)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Character_immortal_repository_loads_every_inheritable_attribute_and_active_flag()
    {
        var source = File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "src",
            "God2.ClassicServer.Persistence",
            "MariaDbCanonicalCatalogRepositories.cs"));

        Assert.Contains("`strength_base`,`strength_bonus`,`constitution_base`,`constitution_bonus`", source, StringComparison.Ordinal);
        Assert.Contains("`metal_base`,`metal_bonus`,`wood_base`,`wood_bonus`", source, StringComparison.Ordinal);
        Assert.Contains("`fire_base`,`fire_bonus`,`earth_base`,`earth_bonus`", source, StringComparison.Ordinal);
        Assert.Contains("`is_active` FROM `god2_player`.`character_immortals`", source, StringComparison.Ordinal);
    }

    private static string RepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "God2ClassicServer.sln")))
        {
            current = current.Parent;
        }

        return current?.FullName ?? throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
