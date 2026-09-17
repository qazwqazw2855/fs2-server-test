using God2.ServerV2.Protocol;

namespace God2.ServerV2.Protocol.Tests;

public sealed class OfficialNpcDialogCodecTests
{
    [Fact]
    public void Encodes_exact_live_dialog_3793_response()
    {
        var result =
            OfficialNpcDialogCodec.EncodeOpenResponse(
                OfficialNpcDialogCodec.ClientBuildId,
                OfficialNpcDialogCodec.LiveDialogHandle);

        Assert.True(result.Succeeded, result.Reason);
        Assert.Equal(
            OfficialNpcDialogCodec.ExactOpenResponseLength,
            result.Frame.Length);
        Assert.Equal(
            "ExactEvidenceHashMatched",
            result.Reason);

        Assert.True(
            OfficialNpcDialogCodec
                .TryDecodeExactOpenResponse(
                    result.Frame,
                    out var handle));
        Assert.Equal(
            OfficialNpcDialogCodec.LiveDialogHandle,
            handle);
    }

    [Fact]
    public void Unknown_handle_is_evidence_blocked()
    {
        var result =
            OfficialNpcDialogCodec.EncodeOpenResponse(
                OfficialNpcDialogCodec.ClientBuildId,
                5042);

        Assert.False(result.Succeeded);
        Assert.Equal(
            "DialogProfileEvidenceBlocked",
            result.Reason);
        Assert.Empty(result.Frame);
    }

    [Fact]
    public void Client_build_mismatch_is_blocked()
    {
        var result =
            OfficialNpcDialogCodec.EncodeOpenResponse(
                "unsupported-build",
                OfficialNpcDialogCodec.LiveDialogHandle);

        Assert.False(result.Succeeded);
        Assert.Equal(
            "ClientBuildMismatch",
            result.Reason);
        Assert.Empty(result.Frame);
    }

    [Fact]
    public void Altered_encoded_response_is_rejected()
    {
        var result =
            OfficialNpcDialogCodec.EncodeOpenResponse(
                OfficialNpcDialogCodec.ClientBuildId,
                OfficialNpcDialogCodec.LiveDialogHandle);

        Assert.True(result.Succeeded);

        result.Frame[10] ^= 1;

        Assert.False(
            OfficialNpcDialogCodec
                .TryDecodeExactOpenResponse(
                    result.Frame,
                    out _));
    }
}
