using God2.ServerV2.Protocol;

namespace God2.ServerV2.Protocol.Tests;

public sealed class OfficialNpcSpawnCodecTests
{
    [Theory]
    [InlineData(
        5042U,
        74,
        124,
        "3F25673AE985BF8F4818F2EE1254AB019B0406B8BD1C38C44F701DD5BAE27834",
        "18003C9C88D83F83345B9E3C463720129A504FAEEF9CF095")]
    [InlineData(
        5096U,
        73,
        121,
        "78230A74DC17C388FE1A6FFDF6EBBD284A05C3E77E20719BE1921941903CC9B7",
        "18003CE6BED83F83345B9E3C463720129A504FBAEB92EAF1")]
    public void Encodes_pinned_map_three_spawn(
        uint handle,
        int positionX,
        int positionY,
        string applicationHash,
        string expectedEncodedHex)
    {
        var result = OfficialNpcSpawnCodec.Encode(
            Evidence(
                handle,
                positionX,
                positionY,
                applicationHash));

        Assert.True(result.Succeeded, result.Reason);
        Assert.Equal(
            expectedEncodedHex,
            Convert.ToHexString(result.Frame));

        Assert.True(
            OfficialNpcSpawnCodec.TryDecodeHandle(
                result.Frame,
                out var decodedHandle));
        Assert.Equal(handle, decodedHandle);
    }

    [Fact]
    public void Altered_application_hash_is_blocked()
    {
        var result = OfficialNpcSpawnCodec.Encode(
            Evidence(
                5042,
                74,
                124,
                new string('A', 64)));

        Assert.False(result.Succeeded);
        Assert.Equal(
            "ApplicationRecordHashMismatch",
            result.Reason);
        Assert.Empty(result.Frame);
    }

    [Fact]
    public void Unsupported_profile_is_blocked()
    {
        var evidence = Evidence(
            5042,
            74,
            124,
            "3F25673AE985BF8F4818F2EE1254AB019B0406B8BD1C38C44F701DD5BAE27834")
            with
            {
                ResourceType = 5
            };

        var result =
            OfficialNpcSpawnCodec.Encode(evidence);

        Assert.False(result.Succeeded);
        Assert.Equal(
            "DerivedTypeZeroProfileMismatch",
            result.Reason);
        Assert.Empty(result.Frame);
    }

    private static OfficialNpcSpawnEvidence Evidence(
        uint handle,
        int positionX,
        int positionY,
        string applicationHash) =>
        new(
            OfficialNpcSpawnCodec.ClientBuildId,
            handle,
            0,
            45,
            3,
            4,
            1,
            positionX,
            positionY,
            applicationHash,
            OfficialNpcSpawnCodec
                .TypeZeroOpaqueTemplateSha256,
            "Derived",
            "test-evidence");
}
