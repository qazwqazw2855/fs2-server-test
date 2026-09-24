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
    public void Encodes_verified_live_dialog_3793_spawn()
    {
        var result = OfficialNpcSpawnCodec.Encode(
            new OfficialNpcSpawnEvidence(
                OfficialNpcSpawnCodec.ClientBuildId,
                OfficialNpcSpawnCodec.LiveDialog3793Handle,
                0,
                OfficialNpcSpawnCodec
                    .LiveDialog3793ResourceOrdinal,
                3,
                OfficialNpcSpawnCodec
                    .LiveDialog3793DirectionCode,
                1,
                65,
                61,
                OfficialNpcSpawnCodec
                    .LiveDialog3793SpawnMessageSha256,
                OfficialNpcSpawnCodec
                    .TypeZeroOpaqueTemplateSha256,
                "Verified",
                "LiveRecovery/attempt-759-decode-2579"));

        Assert.True(result.Succeeded, result.Reason);
        Assert.Equal(
            OfficialNpcSpawnCodec.FrameLength,
            result.Frame.Length);

        Assert.True(
            OfficialNpcSpawnCodec.TryDecodeHandle(
                result.Frame,
                out var decodedHandle));
        Assert.Equal(
            OfficialNpcSpawnCodec.LiveDialog3793Handle,
            decodedHandle);
    }

    [Fact]
    public void Archived_3954_spawn_decodes_to_pinned_application_hash()
    {
        // Migration 103 archived this decoded Stage 4 frame. This audit does
        // not enable the V2 database row or expand the production codec.
        var decoded = Convert.FromHexString(
            "180072720F00005260000081000000CF010000C0036C00A1");

        Assert.Equal(OfficialNpcSpawnCodec.FrameLength, decoded.Length);
        Assert.Equal(OfficialNpcSpawnCodec.SpawnOpcode, decoded[2]);
        Assert.Equal(
            3954U,
            System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(
                decoded.AsSpan(3, sizeof(uint))));
        Assert.Equal(
            "F53E8D79A02FB96A528E9ABF334E5D1383018780BA79D62340549FC5AD36885B",
            Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                decoded.AsSpan(2, OfficialNpcSpawnCodec.ApplicationRecordLength))));
    }

    [Fact]
    public void Live_dialog_profile_does_not_generalize_handle()
    {
        var result = OfficialNpcSpawnCodec.Encode(
            new OfficialNpcSpawnEvidence(
                OfficialNpcSpawnCodec.ClientBuildId,
                3794,
                0,
                OfficialNpcSpawnCodec
                    .LiveDialog3793ResourceOrdinal,
                3,
                OfficialNpcSpawnCodec
                    .LiveDialog3793DirectionCode,
                1,
                65,
                61,
                OfficialNpcSpawnCodec
                    .LiveDialog3793SpawnMessageSha256,
                OfficialNpcSpawnCodec
                    .TypeZeroOpaqueTemplateSha256,
                "Verified",
                "test-evidence"));

        Assert.False(result.Succeeded);
        Assert.Equal(
            "DerivedTypeZeroProfileMismatch",
            result.Reason);
        Assert.Empty(result.Frame);
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
