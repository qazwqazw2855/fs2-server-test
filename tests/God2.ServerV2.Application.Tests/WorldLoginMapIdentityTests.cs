using God2.ServerV2.Application;

namespace God2.ServerV2.Application.Tests;

public sealed class WorldLoginMapIdentityTests
{
    [Fact]
    public void Admission_requires_current_build_and_in_bounds_position()
    {
        var identity = new WorldLoginMapIdentity(
            1675308248, "god2-opt-6b127086e0c0", 3, 4,
            0, 251, 0, 251);

        Assert.True(identity.MatchesBuildAndContains(
            "god2-opt-6b127086e0c0", 251, 251));
        Assert.False(identity.MatchesBuildAndContains(
            "another-client-build", 196, 139));
        Assert.False(identity.MatchesBuildAndContains(
            "god2-opt-6b127086e0c0", 252, 397));
    }
}
