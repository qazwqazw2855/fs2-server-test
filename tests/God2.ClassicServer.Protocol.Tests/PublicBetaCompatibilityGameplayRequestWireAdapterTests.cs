using System.Buffers.Binary;
using God2.ClassicServer.Protocol;

namespace God2.ClassicServer.Protocol.Tests;

public sealed class PublicBetaCompatibilityGameplayRequestWireAdapterTests
{
    [Theory]
    [InlineData((byte)1, OfficialPlayerAttributeCandidate.Intelligence)]
    [InlineData((byte)2, OfficialPlayerAttributeCandidate.Vitality)]
    [InlineData((byte)3, OfficialPlayerAttributeCandidate.Strength)]
    [InlineData((byte)4, OfficialPlayerAttributeCandidate.Speed)]
    public void CurrentAttributeIncrement_AdaptsCaptureVerifiedRecordWithoutEnablingMutation(
        byte rawCode,
        OfficialPlayerAttributeCandidate expected)
    {
        var frame = BuildRawFrame(PublicBetaCompatibilityGameplayRequestWireAdapter.AttributeIncrementOpcode, [rawCode]);

        var result = PublicBetaCompatibilityGameplayRequestWireAdapter.AdaptAttributeIncrement(
            PublicBetaCompatibilityGameplayRequestWireAdapter.ClientBuildId,
            GameplayProtocolState.World,
            frame);

        Assert.True(result.Adapted);
        Assert.Equal(rawCode, result.Command!.RawAttributeCode);
        Assert.Equal(expected, result.Command.PublicBetaCandidate);
        Assert.False(result.Command.RuntimeMutationAllowed);
        Assert.Contains("CurrentConsumerConfirmationRequired", result.Command.EvidenceStatus, StringComparison.Ordinal);

        frame[^1] ^= 1;
        Assert.Equal(
            PublicBetaGameplayRequestAdapterResultCode.InvalidChecksum,
            PublicBetaCompatibilityGameplayRequestWireAdapter.AdaptAttributeIncrement(
                PublicBetaCompatibilityGameplayRequestWireAdapter.ClientBuildId,
                GameplayProtocolState.World,
                frame).Code);
    }

    [Fact]
    public void CurrentAttributeIncrement_FailsClosedForBuildStateLengthAndChecksum()
    {
        var frame = BuildRawFrame(PublicBetaCompatibilityGameplayRequestWireAdapter.AttributeIncrementOpcode, [(byte)1]);
        var wrongChecksum = frame.ToArray();
        wrongChecksum[^1] ^= 1;

        Assert.Equal(
            PublicBetaGameplayRequestAdapterResultCode.BuildMismatch,
            PublicBetaCompatibilityGameplayRequestWireAdapter.AdaptAttributeIncrement(
                "other-build",
                GameplayProtocolState.World,
                frame).Code);
        Assert.Equal(
            PublicBetaGameplayRequestAdapterResultCode.InvalidState,
            PublicBetaCompatibilityGameplayRequestWireAdapter.AdaptAttributeIncrement(
                PublicBetaCompatibilityGameplayRequestWireAdapter.ClientBuildId,
                GameplayProtocolState.Battle,
                frame).Code);
        Assert.Equal(
            PublicBetaGameplayRequestAdapterResultCode.InvalidLength,
            PublicBetaCompatibilityGameplayRequestWireAdapter.AdaptAttributeIncrement(
                PublicBetaCompatibilityGameplayRequestWireAdapter.ClientBuildId,
                GameplayProtocolState.World,
                frame[..^1]).Code);
        Assert.Equal(
            PublicBetaGameplayRequestAdapterResultCode.InvalidChecksum,
            PublicBetaCompatibilityGameplayRequestWireAdapter.AdaptAttributeIncrement(
                PublicBetaCompatibilityGameplayRequestWireAdapter.ClientBuildId,
                GameplayProtocolState.World,
                wrongChecksum).Code);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    public void CurrentAttributeIncrement_RejectsInvalidAttributeCodesAsAttributeCodeFailure(byte rawCode)
    {
        var frame = BuildRawFrame(PublicBetaCompatibilityGameplayRequestWireAdapter.AttributeIncrementOpcode, [rawCode]);

        var result = PublicBetaCompatibilityGameplayRequestWireAdapter.AdaptAttributeIncrement(
            PublicBetaCompatibilityGameplayRequestWireAdapter.ClientBuildId,
            GameplayProtocolState.World,
            frame);

        Assert.False(result.Adapted);
        Assert.Equal(
            PublicBetaGameplayRequestAdapterResultCode.InvalidAttributeCode,
            result.Code);
        Assert.Equal("wire.player.attribute_increment_code_invalid", result.FailureCode);
    }

    [Fact]
    public void CurrentInteraction24_AdaptsTwoRawBytesWithoutInventingBusinessMeaning()
    {
        var frame = BuildFrame(0x12, 0xA5);

        var result = PublicBetaCompatibilityGameplayRequestWireAdapter.AdaptClientCommand(
            PublicBetaCompatibilityGameplayRequestWireAdapter.ClientBuildId,
            GameplayProtocolState.World,
            frame);

        Assert.True(result.Adapted);
        Assert.Equal((byte)0x12, result.Command!.RawValue0);
        Assert.Equal((byte)0xA5, result.Command.RawValue1);
        Assert.False(result.Command.ProductionMutationAllowed);
        Assert.Contains("0x000AEF2B", result.Command.CompatibilityEvidence, StringComparison.Ordinal);
    }

    [Fact]
    public void CurrentInteraction28_AdaptsRawInventoryFrameWithoutInventingBusinessMeaning()
    {
        var frame = BuildInventoryActivationFrame(0x1234, 0x21, 0x01);

        var result = PublicBetaCompatibilityGameplayRequestWireAdapter.AdaptInventoryActivation(
            PublicBetaCompatibilityGameplayRequestWireAdapter.ClientBuildId,
            GameplayProtocolState.World,
            frame);

        Assert.True(result.Adapted);
        Assert.Equal(new byte[] { 0x34, 0x12, 0x21, 0x01 }, result.Command!.RawPayload.ToArray());
        Assert.False(result.Command.ProductionMutationAllowed);
        Assert.Contains(
            "interaction28-u16-u8-u8-corroboration",
            result.Command.CompatibilityEvidence,
            StringComparison.Ordinal);
    }

    [Fact]
    public void CurrentInteraction24_FailsClosedForBuildStateShapeOpcodeAndChecksum()
    {
        var frame = BuildFrame(1, 2);

        Assert.Equal(
            PublicBetaGameplayRequestAdapterResultCode.BuildMismatch,
            Adapt("other-build", GameplayProtocolState.World, frame).Code);
        Assert.Equal(
            PublicBetaGameplayRequestAdapterResultCode.InvalidState,
            Adapt(PublicBetaCompatibilityGameplayRequestWireAdapter.ClientBuildId, GameplayProtocolState.Battle, frame).Code);
        Assert.Equal(
            PublicBetaGameplayRequestAdapterResultCode.InvalidLength,
            Adapt(PublicBetaCompatibilityGameplayRequestWireAdapter.ClientBuildId, GameplayProtocolState.World, frame[..^1]).Code);

        var wrongOpcode = frame.ToArray();
        wrongOpcode[2] = 0x25;
        wrongOpcode[^1] = OfficialLoginWireTransform.ComputeChecksum(wrongOpcode);
        Assert.Equal(
            PublicBetaGameplayRequestAdapterResultCode.InvalidOpcode,
            Adapt(PublicBetaCompatibilityGameplayRequestWireAdapter.ClientBuildId, GameplayProtocolState.World, wrongOpcode).Code);

        var wrongChecksum = frame.ToArray();
        wrongChecksum[^1] ^= 0x01;
        Assert.Equal(
            PublicBetaGameplayRequestAdapterResultCode.InvalidChecksum,
            Adapt(PublicBetaCompatibilityGameplayRequestWireAdapter.ClientBuildId, GameplayProtocolState.World, wrongChecksum).Code);
    }

    [Fact]
    public void CurrentInteraction28_FailsClosedForWrongLengthAndChecksum()
    {
        var frame = BuildInventoryActivationFrame(0x1234, 0x21, 0x01);
        var wrongChecksum = frame.ToArray();
        wrongChecksum[^1] ^= 1;

        Assert.Equal(
            PublicBetaGameplayRequestAdapterResultCode.InvalidLength,
            PublicBetaCompatibilityGameplayRequestWireAdapter.AdaptInventoryActivation(
                PublicBetaCompatibilityGameplayRequestWireAdapter.ClientBuildId,
                GameplayProtocolState.World,
                frame[..^1]).Code);
        Assert.Equal(
            PublicBetaGameplayRequestAdapterResultCode.InvalidChecksum,
            PublicBetaCompatibilityGameplayRequestWireAdapter.AdaptInventoryActivation(
                PublicBetaCompatibilityGameplayRequestWireAdapter.ClientBuildId,
                GameplayProtocolState.World,
                wrongChecksum).Code);
    }

    [Fact]
    public void CurrentInteraction27_AdaptsRawU32WithoutInventingBusinessMeaning()
    {
        var frame = BuildInteraction27Frame(0xA5B61234);

        var result = PublicBetaCompatibilityGameplayRequestWireAdapter.AdaptInteraction27(
            PublicBetaCompatibilityGameplayRequestWireAdapter.ClientBuildId,
            GameplayProtocolState.World,
            frame);

        Assert.True(result.Adapted);
        Assert.Equal(0xA5B61234u, result.Command!.RawValue);
        Assert.False(result.Command.ProductionMutationAllowed);
        Assert.Contains("0x000AEF69", result.Command.CompatibilityEvidence, StringComparison.Ordinal);
    }

    [Fact]
    public void CurrentInteraction27_FailsClosedForWrongStateAndChecksum()
    {
        var frame = BuildInteraction27Frame(42);
        var wrongChecksum = frame.ToArray();
        wrongChecksum[^1] ^= 1;

        Assert.Equal(
            PublicBetaGameplayRequestAdapterResultCode.InvalidState,
            PublicBetaCompatibilityGameplayRequestWireAdapter.AdaptInteraction27(
                PublicBetaCompatibilityGameplayRequestWireAdapter.ClientBuildId,
                GameplayProtocolState.Battle,
                frame).Code);
        Assert.Equal(
            PublicBetaGameplayRequestAdapterResultCode.InvalidChecksum,
            PublicBetaCompatibilityGameplayRequestWireAdapter.AdaptInteraction27(
                PublicBetaCompatibilityGameplayRequestWireAdapter.ClientBuildId,
                GameplayProtocolState.World,
                wrongChecksum).Code);
    }

    [Fact]
    public void CurrentInteraction26_AdaptsExactRawOverlayAndPreservesTail()
    {
        var frame = BuildInteraction26Frame(0x1234, 0x5678, 0x9A, 0xBC);

        var result = PublicBetaCompatibilityGameplayRequestWireAdapter.AdaptInteraction26(
            PublicBetaCompatibilityGameplayRequestWireAdapter.ClientBuildId,
            GameplayProtocolState.World,
            frame);

        Assert.True(result.Adapted);
        Assert.Equal((ushort)0x1234, result.Command!.RawValue0);
        Assert.Equal((ushort)0x5678, result.Command.RawValue2);
        Assert.Equal((byte)0x9A, result.Command.RawValue4);
        Assert.Equal((byte)0xBC, result.Command.RawTail);
        Assert.False(result.Command.ProductionMutationAllowed);
        Assert.Contains("0x000AF795", result.Command.CompatibilityEvidence, StringComparison.Ordinal);
    }

    [Fact]
    public void CurrentInteraction26_FailsClosedForWrongLengthAndChecksum()
    {
        var frame = BuildInteraction26Frame(1, 2, 3, 4);
        var wrongChecksum = frame.ToArray();
        wrongChecksum[^1] ^= 1;

        Assert.Equal(
            PublicBetaGameplayRequestAdapterResultCode.InvalidLength,
            PublicBetaCompatibilityGameplayRequestWireAdapter.AdaptInteraction26(
                PublicBetaCompatibilityGameplayRequestWireAdapter.ClientBuildId,
                GameplayProtocolState.World,
                frame[..^1]).Code);
        Assert.Equal(
            PublicBetaGameplayRequestAdapterResultCode.InvalidChecksum,
            PublicBetaCompatibilityGameplayRequestWireAdapter.AdaptInteraction26(
                PublicBetaCompatibilityGameplayRequestWireAdapter.ClientBuildId,
                GameplayProtocolState.World,
                wrongChecksum).Code);
    }

    [Theory]
    [InlineData((byte)0x17, (sbyte)1)]
    [InlineData((byte)0x2C, (sbyte)-1)]
    public void CurrentCombinationStep_AdaptsSelectorAndStrictPlusMinusOne(byte selector, sbyte step)
    {
        var frame = BuildCombinationStepFrame(selector, step);

        var result = PublicBetaCompatibilityGameplayRequestWireAdapter.AdaptCombinationStep(
            PublicBetaCompatibilityGameplayRequestWireAdapter.ClientBuildId,
            GameplayProtocolState.World,
            frame);

        Assert.True(result.Adapted);
        Assert.Equal(selector, result.Command!.RawSelector);
        Assert.Equal(step, result.Command.Step);
        Assert.False(result.Command.ProductionMutationAllowed);
        Assert.Contains("0x0011469C", result.Command.CompatibilityEvidence, StringComparison.Ordinal);
    }

    [Fact]
    public void CurrentCombinationStep_RejectsZeroStep()
    {
        var frame = BuildCombinationStepFrame(1, 0);

        var result = PublicBetaCompatibilityGameplayRequestWireAdapter.AdaptCombinationStep(
            PublicBetaCompatibilityGameplayRequestWireAdapter.ClientBuildId,
            GameplayProtocolState.World,
            frame);

        Assert.False(result.Adapted);
        Assert.Equal("wire.public_beta_compat.combination_step_value_invalid", result.FailureCode);
    }

    [Fact]
    public void CurrentPartySelection_AndTeamRequest_PreserveExactRawPayloads()
    {
        var party = BuildRawFrame(PublicBetaCompatibilityGameplayRequestWireAdapter.PartySelectionOpcode, [0x34, 0x12, 0xA5, 0xB6]);
        var teamPayload = Enumerable.Range(0, 18).Select(value => checked((byte)value)).ToArray();
        var team = BuildRawFrame(PublicBetaCompatibilityGameplayRequestWireAdapter.TeamRequestOpcode, teamPayload);

        var partyResult = PublicBetaCompatibilityGameplayRequestWireAdapter.AdaptPartySelection(
            PublicBetaCompatibilityGameplayRequestWireAdapter.ClientBuildId, GameplayProtocolState.World, party);
        var teamResult = PublicBetaCompatibilityGameplayRequestWireAdapter.AdaptTeamRequest(
            PublicBetaCompatibilityGameplayRequestWireAdapter.ClientBuildId, GameplayProtocolState.World, team);

        Assert.True(partyResult.Adapted);
        Assert.Equal(new byte[] { 0x34, 0x12, 0xA5, 0xB6 }, partyResult.Command!.RawPayload.ToArray());
        Assert.True(teamResult.Adapted);
        Assert.Equal(teamPayload, teamResult.Command!.RawPayload.ToArray());
        Assert.False(partyResult.Command.ProductionMutationAllowed);
        Assert.False(teamResult.Command.ProductionMutationAllowed);
        Assert.Contains("legacy_party_protocol/93-raw-u32", partyResult.Command.CompatibilityEvidence, StringComparison.Ordinal);
        Assert.Contains("legacy_team_protocol/5a-u16-plus-16-raw-bytes", teamResult.Command.CompatibilityEvidence, StringComparison.Ordinal);
    }

    [Fact]
    public void CurrentPartySelection_FailsClosedForWrongLengthAndChecksum()
    {
        var party = BuildRawFrame(PublicBetaCompatibilityGameplayRequestWireAdapter.PartySelectionOpcode, [0x34, 0x12, 0xA5, 0xB6]);
        var wrongLength = party[..^1];
        var wrongChecksum = party.ToArray();
        wrongChecksum[^1] ^= 0x01;

        Assert.Equal(
            PublicBetaGameplayRequestAdapterResultCode.InvalidLength,
            PublicBetaCompatibilityGameplayRequestWireAdapter.AdaptPartySelection(
                PublicBetaCompatibilityGameplayRequestWireAdapter.ClientBuildId,
                GameplayProtocolState.World,
                wrongLength).Code);
        Assert.Equal(
            PublicBetaGameplayRequestAdapterResultCode.InvalidChecksum,
            PublicBetaCompatibilityGameplayRequestWireAdapter.AdaptPartySelection(
                PublicBetaCompatibilityGameplayRequestWireAdapter.ClientBuildId,
                GameplayProtocolState.World,
                wrongChecksum).Code);
    }

    [Fact]
    public void CurrentPartyRequest92_AdaptsRawPayloadWithoutInventingBusinessMeaning()
    {
        var party92Payload = new byte[] { 0x12, 0x34 };
        var frame = BuildRawFrame(PublicBetaCompatibilityGameplayRequestWireAdapter.PartyRequest92Opcode, party92Payload);

        var result = PublicBetaCompatibilityGameplayRequestWireAdapter.AdaptPartyRequest92(
            PublicBetaCompatibilityGameplayRequestWireAdapter.ClientBuildId,
            GameplayProtocolState.World,
            frame);

        Assert.True(result.Adapted);
        Assert.Equal(party92Payload, result.Command!.RawPayload.ToArray());
        Assert.False(result.Command.ProductionMutationAllowed);
        Assert.Contains(
            OfficialPublicBetaCrossVersionEvidence.ArchiveSha256,
            result.Command.CompatibilityEvidence,
            StringComparison.Ordinal);
        Assert.Contains("legacy_party_protocol/92-current-build-raw", result.Command.CompatibilityEvidence, StringComparison.Ordinal);
    }

    [Fact]
    public void CurrentPartyRequest92_FailsClosedForWrongLengthAndChecksum()
    {
        var party = BuildRawFrame(PublicBetaCompatibilityGameplayRequestWireAdapter.PartyRequest92Opcode, [0x12, 0x34]);
        var wrongLength = party[..^1];
        var wrongChecksum = party.ToArray();
        wrongChecksum[^1] ^= 0x01;

        Assert.Equal(
            PublicBetaGameplayRequestAdapterResultCode.InvalidLength,
            PublicBetaCompatibilityGameplayRequestWireAdapter.AdaptPartyRequest92(
                PublicBetaCompatibilityGameplayRequestWireAdapter.ClientBuildId,
                GameplayProtocolState.World,
                wrongLength).Code);
        Assert.Equal(
            PublicBetaGameplayRequestAdapterResultCode.InvalidChecksum,
            PublicBetaCompatibilityGameplayRequestWireAdapter.AdaptPartyRequest92(
                PublicBetaCompatibilityGameplayRequestWireAdapter.ClientBuildId,
                GameplayProtocolState.World,
                wrongChecksum).Code);
    }

    [Fact]
    public void CurrentPetPkTarget_AdaptsRawPayloadWithoutInventingBusinessMeaning()
    {
        var pkPayload = new byte[] { 0x12, 0x34 };
        var frame = BuildRawFrame(PublicBetaCompatibilityGameplayRequestWireAdapter.PetPkTargetOpcode, pkPayload);

        var result = PublicBetaCompatibilityGameplayRequestWireAdapter.AdaptPetPkTarget(
            PublicBetaCompatibilityGameplayRequestWireAdapter.ClientBuildId,
            GameplayProtocolState.World,
            frame);

        Assert.True(result.Adapted);
        Assert.Equal(pkPayload, result.Command!.RawPayload.ToArray());
        Assert.False(result.Command.ProductionMutationAllowed);
        Assert.Contains(
            OfficialPublicBetaCrossVersionEvidence.ArchiveSha256,
            result.Command.CompatibilityEvidence,
            StringComparison.Ordinal);
        Assert.Contains("legacy_pk_protocol/b8-current-build-raw", result.Command.CompatibilityEvidence, StringComparison.Ordinal);
    }

    [Fact]
    public void CurrentPetPkTarget_FailsClosedForWrongLengthAndChecksum()
    {
        var petTarget = BuildRawFrame(PublicBetaCompatibilityGameplayRequestWireAdapter.PetPkTargetOpcode, [0x12, 0x34]);
        var wrongLength = petTarget[..^1];
        var wrongChecksum = petTarget.ToArray();
        wrongChecksum[^1] ^= 0x01;

        Assert.Equal(
            PublicBetaGameplayRequestAdapterResultCode.InvalidLength,
            PublicBetaCompatibilityGameplayRequestWireAdapter.AdaptPetPkTarget(
                PublicBetaCompatibilityGameplayRequestWireAdapter.ClientBuildId,
                GameplayProtocolState.World,
                wrongLength).Code);
        Assert.Equal(
            PublicBetaGameplayRequestAdapterResultCode.InvalidChecksum,
            PublicBetaCompatibilityGameplayRequestWireAdapter.AdaptPetPkTarget(
                PublicBetaCompatibilityGameplayRequestWireAdapter.ClientBuildId,
                GameplayProtocolState.World,
                wrongChecksum).Code);
    }

    [Fact]
    public void CurrentGameplayDisconnect_AdaptsRawDisconnectPayloadWithoutInventingBusinessMeaning()
    {
        var disconnectPayload = new byte[17]
        {
            0xF8, 0x27, 0xD2, 0x5F, 0x44, 0x4D, 0x98, 0x0A,
            0x11, 0x22, 0x33, 0x44, 0x55, 0x66, 0x77, 0x88, 0x99
        };
        var frame = BuildRawFrame(PublicBetaCompatibilityGameplayRequestWireAdapter.GameplayDisconnectOpcode, disconnectPayload);

        var result = PublicBetaCompatibilityGameplayRequestWireAdapter.AdaptGameplayDisconnect(
            PublicBetaCompatibilityGameplayRequestWireAdapter.ClientBuildId,
            GameplayProtocolState.World,
            frame);

        Assert.True(result.Adapted);
        Assert.Equal(disconnectPayload, result.Command!.RawPayload.ToArray());
        Assert.False(result.Command.ProductionMutationAllowed);
        Assert.Contains(
            OfficialPublicBetaCrossVersionEvidence.ArchiveSha256,
            result.Command.CompatibilityEvidence,
            StringComparison.Ordinal);
    }

    [Fact]
    public void CurrentGameplayDisconnect_FailsClosedForWrongLengthAndChecksum()
    {
        var frame = BuildRawFrame(PublicBetaCompatibilityGameplayRequestWireAdapter.GameplayDisconnectOpcode, new byte[17]);
        var wrongChecksum = frame.ToArray();
        wrongChecksum[^1] ^= 1;

        Assert.Equal(
            PublicBetaGameplayRequestAdapterResultCode.InvalidLength,
            PublicBetaCompatibilityGameplayRequestWireAdapter.AdaptGameplayDisconnect(
                PublicBetaCompatibilityGameplayRequestWireAdapter.ClientBuildId,
                GameplayProtocolState.World,
                frame[..^1]).Code);
        Assert.Equal(
            PublicBetaGameplayRequestAdapterResultCode.InvalidChecksum,
            PublicBetaCompatibilityGameplayRequestWireAdapter.AdaptGameplayDisconnect(
                PublicBetaCompatibilityGameplayRequestWireAdapter.ClientBuildId,
                GameplayProtocolState.World,
                wrongChecksum).Code);
    }

    [Fact]
    public void CurrentPartyRequest95_AdaptsRawPayloadWithoutInventingBusinessMeaning()
    {
        var party95Payload = new byte[] { 0x12, 0x34 };
        var frame = BuildRawFrame(PublicBetaCompatibilityGameplayRequestWireAdapter.PartyRequest95Opcode, party95Payload);

        var result = PublicBetaCompatibilityGameplayRequestWireAdapter.AdaptPartyRequest95(
            PublicBetaCompatibilityGameplayRequestWireAdapter.ClientBuildId,
            GameplayProtocolState.World,
            frame);

        Assert.True(result.Adapted);
        Assert.Equal(party95Payload, result.Command!.RawPayload.ToArray());
        Assert.False(result.Command.ProductionMutationAllowed);
        Assert.Contains(
            OfficialPublicBetaCrossVersionEvidence.ArchiveSha256,
            result.Command.CompatibilityEvidence,
            StringComparison.Ordinal);
        Assert.Contains("legacy_party_protocol/95-u16", result.Command.CompatibilityEvidence, StringComparison.Ordinal);
    }

    [Fact]
    public void CurrentPartyRequest95_FailsClosedForWrongLengthAndChecksum()
    {
        var frame = BuildRawFrame(PublicBetaCompatibilityGameplayRequestWireAdapter.PartyRequest95Opcode, new byte[2]);
        var wrongChecksum = frame.ToArray();
        wrongChecksum[^1] ^= 1;

        Assert.Equal(
            PublicBetaGameplayRequestAdapterResultCode.InvalidLength,
            PublicBetaCompatibilityGameplayRequestWireAdapter.AdaptPartyRequest95(
                PublicBetaCompatibilityGameplayRequestWireAdapter.ClientBuildId,
                GameplayProtocolState.World,
                frame[..^1]).Code);
        Assert.Equal(
            PublicBetaGameplayRequestAdapterResultCode.InvalidChecksum,
            PublicBetaCompatibilityGameplayRequestWireAdapter.AdaptPartyRequest95(
                PublicBetaCompatibilityGameplayRequestWireAdapter.ClientBuildId,
                GameplayProtocolState.World,
                wrongChecksum).Code);
    }

    [Fact]
    public void CurrentVendorCartPublish_AdaptsMinimalRawBoundaryFrameWithoutInventingBusinessMeaning()
    {
        var frame = BuildVendorCartPublishRawFrame();
        var result = PublicBetaCompatibilityGameplayRequestWireAdapter.AdaptVendorCartPublish(
            PublicBetaCompatibilityGameplayRequestWireAdapter.ClientBuildId,
            GameplayProtocolState.World,
            frame);

        Assert.True(result.Adapted);
        Assert.Equal(PublicBetaCompatibilityGameplayRequestWireAdapter.VendorCartPublishOpcode, result.Command!.Opcode);
        Assert.Empty(result.Command.RawPayload.ToArray());
        Assert.False(result.Command.ProductionMutationAllowed);
        Assert.Contains(
            "legacy_vendor_cart_protocol/b0-current-build-raw",
            result.Command.CompatibilityEvidence,
            StringComparison.Ordinal);
        Assert.Contains(
            OfficialPublicBetaCrossVersionEvidence.ArchiveSha256,
            result.Command.CompatibilityEvidence,
            StringComparison.Ordinal);
    }

    [Fact]
    public void CurrentVendorCartPublish_FailsClosedForWrongLengthAndOpcode()
    {
        var frame = BuildVendorCartPublishRawFrame();
        var wrongLength = frame[..^1];
        var wrongOpcode = BuildVendorCartPublishRawFrame(opcode: 0xB1);

        Assert.Equal(
            PublicBetaGameplayRequestAdapterResultCode.InvalidLength,
            PublicBetaCompatibilityGameplayRequestWireAdapter.AdaptVendorCartPublish(
                PublicBetaCompatibilityGameplayRequestWireAdapter.ClientBuildId,
                GameplayProtocolState.World,
                wrongLength).Code);
        Assert.Equal(
            PublicBetaGameplayRequestAdapterResultCode.InvalidOpcode,
            PublicBetaCompatibilityGameplayRequestWireAdapter.AdaptVendorCartPublish(
                PublicBetaCompatibilityGameplayRequestWireAdapter.ClientBuildId,
                GameplayProtocolState.World,
                wrongOpcode).Code);
    }

    [Theory]
    [InlineData(PublicBetaCompatibilityGameplayRequestWireAdapter.AccountLoginOpcode, PublicBetaCompatibilityGameplayRequestWireAdapter.AccountLoginDecodedFrameLength)]
    [InlineData(PublicBetaCompatibilityGameplayRequestWireAdapter.GameLoginOpcode, PublicBetaCompatibilityGameplayRequestWireAdapter.GameLoginDecodedFrameLength)]
    [InlineData(PublicBetaCompatibilityGameplayRequestWireAdapter.RouteOpcode, PublicBetaCompatibilityGameplayRequestWireAdapter.RouteDecodedFrameLength)]
    [InlineData(PublicBetaCompatibilityGameplayRequestWireAdapter.CombinationCommitOpcode, PublicBetaCompatibilityGameplayRequestWireAdapter.CombinationCommitDecodedFrameLength)]
    [InlineData(PublicBetaCompatibilityGameplayRequestWireAdapter.MailRecordActionOpcode, PublicBetaCompatibilityGameplayRequestWireAdapter.MailRecordActionDecodedFrameLength)]
    [InlineData(PublicBetaCompatibilityGameplayRequestWireAdapter.PartyRequest8FOpcode, PublicBetaCompatibilityGameplayRequestWireAdapter.PartyRequest8FDecodedFrameLength)]
    [InlineData(PublicBetaCompatibilityGameplayRequestWireAdapter.PartyRequest90Opcode, PublicBetaCompatibilityGameplayRequestWireAdapter.PartyRequest90DecodedFrameLength)]
    [InlineData(PublicBetaCompatibilityGameplayRequestWireAdapter.PartyRequest91Opcode, PublicBetaCompatibilityGameplayRequestWireAdapter.PartyRequest91DecodedFrameLength)]
    [InlineData(PublicBetaCompatibilityGameplayRequestWireAdapter.PartyRequest92Opcode, PublicBetaCompatibilityGameplayRequestWireAdapter.PartyRequest92DecodedFrameLength)]
    [InlineData(PublicBetaCompatibilityGameplayRequestWireAdapter.PartyRequest94Opcode, PublicBetaCompatibilityGameplayRequestWireAdapter.PartyRequest94DecodedFrameLength)]
    [InlineData(PublicBetaCompatibilityGameplayRequestWireAdapter.PetEggRequestOpcode, PublicBetaCompatibilityGameplayRequestWireAdapter.PetEggRequestDecodedFrameLength)]
    [InlineData(PublicBetaCompatibilityGameplayRequestWireAdapter.FPetRequestOpcode, PublicBetaCompatibilityGameplayRequestWireAdapter.FPetRequestDecodedFrameLength)]
    [InlineData(PublicBetaCompatibilityGameplayRequestWireAdapter.PkCursorTargetOpcode, PublicBetaCompatibilityGameplayRequestWireAdapter.PkCursorTargetDecodedFrameLength)]
    [InlineData(PublicBetaCompatibilityGameplayRequestWireAdapter.VendorCartActionB2Opcode, PublicBetaCompatibilityGameplayRequestWireAdapter.VendorCartActionB2DecodedFrameLength)]
    [InlineData(PublicBetaCompatibilityGameplayRequestWireAdapter.VendorCartAddRecordOpcode, PublicBetaCompatibilityGameplayRequestWireAdapter.VendorCartAddRecordDecodedFrameLength)]
    public void CurrentRawClientGameplayRequestAdapters_AdaptsExactPayloadWithoutInventingBusinessMeaning(
        byte opcode,
        int decodedLength)
    {
        var payload = BuildSequentialPayload(decodedLength - 4);
        var frame = BuildRawFrame(opcode, payload);

        var result = AdaptRawClientRequest(
            opcode,
            PublicBetaCompatibilityGameplayRequestWireAdapter.ClientBuildId,
            GameplayProtocolState.World,
            frame);

        Assert.True(result.Adapted);
        Assert.Equal(opcode, result.Command!.Opcode);
        Assert.Equal(payload, result.Command.RawPayload.ToArray());
        Assert.False(result.Command.ProductionMutationAllowed);
        Assert.Contains(
            OfficialPublicBetaCrossVersionEvidence.ArchiveSha256,
            result.Command.CompatibilityEvidence,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(PublicBetaCompatibilityGameplayRequestWireAdapter.AccountLoginOpcode, PublicBetaCompatibilityGameplayRequestWireAdapter.AccountLoginDecodedFrameLength)]
    [InlineData(PublicBetaCompatibilityGameplayRequestWireAdapter.GameLoginOpcode, PublicBetaCompatibilityGameplayRequestWireAdapter.GameLoginDecodedFrameLength)]
    [InlineData(PublicBetaCompatibilityGameplayRequestWireAdapter.RouteOpcode, PublicBetaCompatibilityGameplayRequestWireAdapter.RouteDecodedFrameLength)]
    [InlineData(PublicBetaCompatibilityGameplayRequestWireAdapter.CombinationCommitOpcode, PublicBetaCompatibilityGameplayRequestWireAdapter.CombinationCommitDecodedFrameLength)]
    [InlineData(PublicBetaCompatibilityGameplayRequestWireAdapter.MailRecordActionOpcode, PublicBetaCompatibilityGameplayRequestWireAdapter.MailRecordActionDecodedFrameLength)]
    [InlineData(PublicBetaCompatibilityGameplayRequestWireAdapter.PartyRequest8FOpcode, PublicBetaCompatibilityGameplayRequestWireAdapter.PartyRequest8FDecodedFrameLength)]
    [InlineData(PublicBetaCompatibilityGameplayRequestWireAdapter.PartyRequest90Opcode, PublicBetaCompatibilityGameplayRequestWireAdapter.PartyRequest90DecodedFrameLength)]
    [InlineData(PublicBetaCompatibilityGameplayRequestWireAdapter.PartyRequest91Opcode, PublicBetaCompatibilityGameplayRequestWireAdapter.PartyRequest91DecodedFrameLength)]
    [InlineData(PublicBetaCompatibilityGameplayRequestWireAdapter.PartyRequest94Opcode, PublicBetaCompatibilityGameplayRequestWireAdapter.PartyRequest94DecodedFrameLength)]
    [InlineData(PublicBetaCompatibilityGameplayRequestWireAdapter.PetEggRequestOpcode, PublicBetaCompatibilityGameplayRequestWireAdapter.PetEggRequestDecodedFrameLength)]
    [InlineData(PublicBetaCompatibilityGameplayRequestWireAdapter.FPetRequestOpcode, PublicBetaCompatibilityGameplayRequestWireAdapter.FPetRequestDecodedFrameLength)]
    [InlineData(PublicBetaCompatibilityGameplayRequestWireAdapter.PkCursorTargetOpcode, PublicBetaCompatibilityGameplayRequestWireAdapter.PkCursorTargetDecodedFrameLength)]
    [InlineData(PublicBetaCompatibilityGameplayRequestWireAdapter.VendorCartActionB2Opcode, PublicBetaCompatibilityGameplayRequestWireAdapter.VendorCartActionB2DecodedFrameLength)]
    [InlineData(PublicBetaCompatibilityGameplayRequestWireAdapter.VendorCartAddRecordOpcode, PublicBetaCompatibilityGameplayRequestWireAdapter.VendorCartAddRecordDecodedFrameLength)]
    public void CurrentRawClientGameplayRequestAdapters_FailsClosedForWrongLengthAndChecksum(
        byte opcode,
        int decodedLength)
    {
        var frame = BuildRawFrame(opcode, BuildSequentialPayload(decodedLength - 4, seed: 0x3C));
        var wrongLength = frame[..^1];
        var wrongChecksum = frame.ToArray();
        wrongChecksum[^1] ^= 0x01;

        Assert.Equal(
            PublicBetaGameplayRequestAdapterResultCode.InvalidLength,
            AdaptRawClientRequest(
                opcode,
                PublicBetaCompatibilityGameplayRequestWireAdapter.ClientBuildId,
                GameplayProtocolState.World,
                wrongLength).Code);
        Assert.Equal(
            PublicBetaGameplayRequestAdapterResultCode.InvalidChecksum,
            AdaptRawClientRequest(
                opcode,
                PublicBetaCompatibilityGameplayRequestWireAdapter.ClientBuildId,
                GameplayProtocolState.World,
                wrongChecksum).Code);
    }

    [Fact]
    public void CurrentSocialTextEnvelope33_AdaptsVariableRawPayloadWithoutInventingBusinessMeaning()
    {
        var payload = BuildSequentialPayload(11, seed: 0x80);
        var result = PublicBetaCompatibilityGameplayRequestWireAdapter.AdaptSocialTextEnvelope(
            PublicBetaCompatibilityGameplayRequestWireAdapter.ClientBuildId,
            GameplayProtocolState.World,
            BuildRawFrame(PublicBetaCompatibilityGameplayRequestWireAdapter.SocialTextEnvelopeOpcode, payload));

        Assert.True(result.Adapted);
        Assert.Equal(PublicBetaCompatibilityGameplayRequestWireAdapter.SocialTextEnvelopeOpcode, result.Command!.Opcode);
        Assert.Equal(payload, result.Command.RawPayload.ToArray());
        Assert.False(result.Command.ProductionMutationAllowed);
        Assert.Contains(
            OfficialPublicBetaCrossVersionEvidence.ArchiveSha256,
            result.Command.CompatibilityEvidence,
            StringComparison.Ordinal);
    }

    [Fact]
    public void CurrentSocialTextEnvelope33_FailsClosedForWrongLengthAndChecksum()
    {
        var payload = BuildSequentialPayload(11, seed: 0x3C);
        var frame = BuildRawFrame(PublicBetaCompatibilityGameplayRequestWireAdapter.SocialTextEnvelopeOpcode, payload);
        var wrongLength = frame[..^1];
        var wrongChecksum = frame.ToArray();
        wrongChecksum[^1] ^= 0x01;

        Assert.Equal(
            PublicBetaGameplayRequestAdapterResultCode.InvalidLength,
            PublicBetaCompatibilityGameplayRequestWireAdapter.AdaptSocialTextEnvelope(
                PublicBetaCompatibilityGameplayRequestWireAdapter.ClientBuildId,
                GameplayProtocolState.World,
                wrongLength).Code);
        Assert.Equal(
            PublicBetaGameplayRequestAdapterResultCode.InvalidChecksum,
            PublicBetaCompatibilityGameplayRequestWireAdapter.AdaptSocialTextEnvelope(
                PublicBetaCompatibilityGameplayRequestWireAdapter.ClientBuildId,
                GameplayProtocolState.World,
                wrongChecksum).Code);
    }

    [Fact]
    public void CurrentCharacterLifecycleRequests_AdaptsRawPayloadsWithoutInventingBusinessMeaning()
    {
        var deletePayload = Enumerable.Range(0, 53).Select(value => checked((byte)(value + 1))).ToArray();
        var selectPayload = Enumerable.Range(0, 37).Select(value => checked((byte)(value + 3))).ToArray();
        var createPayload = Enumerable.Range(0, 44).Select(value => checked((byte)(value + 5))).ToArray();

        var deleteResult = PublicBetaCompatibilityGameplayRequestWireAdapter.AdaptCharacterDeleteRequest(
            PublicBetaCompatibilityGameplayRequestWireAdapter.ClientBuildId,
            GameplayProtocolState.World,
            BuildRawFrame(PublicBetaCompatibilityGameplayRequestWireAdapter.CharacterDeleteRequestOpcode, deletePayload));
        var selectResult = PublicBetaCompatibilityGameplayRequestWireAdapter.AdaptCharacterSelectRequest(
            PublicBetaCompatibilityGameplayRequestWireAdapter.ClientBuildId,
            GameplayProtocolState.World,
            BuildRawFrame(PublicBetaCompatibilityGameplayRequestWireAdapter.CharacterSelectRequestOpcode, selectPayload));
        var createResult = PublicBetaCompatibilityGameplayRequestWireAdapter.AdaptCharacterCreate1bRequest(
            PublicBetaCompatibilityGameplayRequestWireAdapter.ClientBuildId,
            GameplayProtocolState.World,
            BuildRawFrame(PublicBetaCompatibilityGameplayRequestWireAdapter.CharacterCreate1bRequestOpcode, createPayload));

        Assert.True(deleteResult.Adapted);
        Assert.True(selectResult.Adapted);
        Assert.True(createResult.Adapted);
        Assert.Equal(deletePayload, deleteResult.Command!.RawPayload.ToArray());
        Assert.Equal(selectPayload, selectResult.Command!.RawPayload.ToArray());
        Assert.Equal(createPayload, createResult.Command!.RawPayload.ToArray());
        Assert.False(deleteResult.Command!.ProductionMutationAllowed);
        Assert.False(selectResult.Command!.ProductionMutationAllowed);
        Assert.False(createResult.Command!.ProductionMutationAllowed);
        Assert.Contains(
            OfficialPublicBetaCrossVersionEvidence.ArchiveSha256,
            deleteResult.Command.CompatibilityEvidence,
            StringComparison.Ordinal);
    }

    [Fact]
    public void CurrentCharacterLifecycleRequests_FailClosedForWrongLengthAndChecksum()
    {
        var deleteFrame = BuildRawFrame(PublicBetaCompatibilityGameplayRequestWireAdapter.CharacterDeleteRequestOpcode, new byte[53]);
        var wrongChecksum = deleteFrame.ToArray();
        wrongChecksum[^1] ^= 1;

        Assert.Equal(
            PublicBetaGameplayRequestAdapterResultCode.InvalidLength,
            PublicBetaCompatibilityGameplayRequestWireAdapter.AdaptCharacterDeleteRequest(
                PublicBetaCompatibilityGameplayRequestWireAdapter.ClientBuildId,
                GameplayProtocolState.World,
                deleteFrame[..^1]).Code);
        Assert.Equal(
            PublicBetaGameplayRequestAdapterResultCode.InvalidChecksum,
            PublicBetaCompatibilityGameplayRequestWireAdapter.AdaptCharacterDeleteRequest(
                PublicBetaCompatibilityGameplayRequestWireAdapter.ClientBuildId,
                GameplayProtocolState.World,
                wrongChecksum).Code);
    }

    private static PublicBetaGameplayRequestAdapterResult Adapt(
        string build,
        GameplayProtocolState state,
        ReadOnlySpan<byte> frame) =>
        PublicBetaCompatibilityGameplayRequestWireAdapter.AdaptClientCommand(build, state, frame);

    private static PublicBetaRawRequestAdapterResult AdaptRawClientRequest(
        byte opcode,
        string build,
        GameplayProtocolState state,
        ReadOnlySpan<byte> frame) =>
        opcode switch
        {
            PublicBetaCompatibilityGameplayRequestWireAdapter.AccountLoginOpcode => PublicBetaCompatibilityGameplayRequestWireAdapter.AdaptAccountLogin(build, state, frame),
            PublicBetaCompatibilityGameplayRequestWireAdapter.GameLoginOpcode => PublicBetaCompatibilityGameplayRequestWireAdapter.AdaptGameLogin(build, state, frame),
            PublicBetaCompatibilityGameplayRequestWireAdapter.RouteOpcode => PublicBetaCompatibilityGameplayRequestWireAdapter.AdaptRouteQuery(build, state, frame),
            PublicBetaCompatibilityGameplayRequestWireAdapter.CombinationCommitOpcode => PublicBetaCompatibilityGameplayRequestWireAdapter.AdaptCombinationCommit(build, state, frame),
            PublicBetaCompatibilityGameplayRequestWireAdapter.MailRecordActionOpcode => PublicBetaCompatibilityGameplayRequestWireAdapter.AdaptMailRecordAction(build, state, frame),
            PublicBetaCompatibilityGameplayRequestWireAdapter.PartyRequest8FOpcode => PublicBetaCompatibilityGameplayRequestWireAdapter.AdaptPartyRequest8F(build, state, frame),
            PublicBetaCompatibilityGameplayRequestWireAdapter.PartyRequest90Opcode => PublicBetaCompatibilityGameplayRequestWireAdapter.AdaptPartyRequest90(build, state, frame),
            PublicBetaCompatibilityGameplayRequestWireAdapter.PartyRequest91Opcode => PublicBetaCompatibilityGameplayRequestWireAdapter.AdaptPartyRequest91(build, state, frame),
            PublicBetaCompatibilityGameplayRequestWireAdapter.PartyRequest92Opcode => PublicBetaCompatibilityGameplayRequestWireAdapter.AdaptPartyRequest92(build, state, frame),
            PublicBetaCompatibilityGameplayRequestWireAdapter.PartyRequest94Opcode => PublicBetaCompatibilityGameplayRequestWireAdapter.AdaptPartyRequest94(build, state, frame),
            PublicBetaCompatibilityGameplayRequestWireAdapter.PetEggRequestOpcode => PublicBetaCompatibilityGameplayRequestWireAdapter.AdaptPetEggRequestB7(build, state, frame),
            PublicBetaCompatibilityGameplayRequestWireAdapter.FPetRequestOpcode => PublicBetaCompatibilityGameplayRequestWireAdapter.AdaptFPetRequestBB(build, state, frame),
            PublicBetaCompatibilityGameplayRequestWireAdapter.PkCursorTargetOpcode => PublicBetaCompatibilityGameplayRequestWireAdapter.AdaptPkCursorTarget(build, state, frame),
            PublicBetaCompatibilityGameplayRequestWireAdapter.VendorCartActionB2Opcode => PublicBetaCompatibilityGameplayRequestWireAdapter.AdaptVendorCartActionB2(build, state, frame),
            PublicBetaCompatibilityGameplayRequestWireAdapter.VendorCartAddRecordOpcode => PublicBetaCompatibilityGameplayRequestWireAdapter.AdaptVendorCartAddRecord(build, state, frame),
            PublicBetaCompatibilityGameplayRequestWireAdapter.VendorCartPublishOpcode => PublicBetaCompatibilityGameplayRequestWireAdapter.AdaptVendorCartPublish(build, state, frame),
            PublicBetaCompatibilityGameplayRequestWireAdapter.SocialTextEnvelopeOpcode => PublicBetaCompatibilityGameplayRequestWireAdapter.AdaptSocialTextEnvelope(build, state, frame),
            _ => throw new ArgumentOutOfRangeException(nameof(opcode), opcode, "No raw adapter mapping configured for opcode."),
        };

    private static byte[] BuildFrame(byte raw0, byte raw1)
    {
        var frame = new byte[PublicBetaCompatibilityGameplayRequestWireAdapter.DecodedFrameLength];
        BinaryPrimitives.WriteUInt16LittleEndian(frame, checked((ushort)frame.Length));
        frame[2] = PublicBetaCompatibilityGameplayRequestWireAdapter.Interaction24Opcode;
        frame[3] = raw0;
        frame[4] = raw1;
        frame[^1] = OfficialLoginWireTransform.ComputeChecksum(frame);
        return frame;
    }

    private static byte[] BuildInventoryActivationFrame(ushort clientItemId, byte slotIndex, byte quantity)
    {
        var frame = new byte[PublicBetaCompatibilityGameplayRequestWireAdapter.Interaction28DecodedFrameLength];
        BinaryPrimitives.WriteUInt16LittleEndian(frame, checked((ushort)frame.Length));
        frame[2] = PublicBetaCompatibilityGameplayRequestWireAdapter.Interaction28Opcode;
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(3), clientItemId);
        frame[5] = slotIndex;
        frame[6] = quantity;
        frame[^1] = OfficialLoginWireTransform.ComputeChecksum(frame);
        return frame;
    }

    private static byte[] BuildInteraction27Frame(uint rawValue)
    {
        var frame = new byte[PublicBetaCompatibilityGameplayRequestWireAdapter.Interaction27DecodedFrameLength];
        BinaryPrimitives.WriteUInt16LittleEndian(frame, checked((ushort)frame.Length));
        frame[2] = PublicBetaCompatibilityGameplayRequestWireAdapter.Interaction27Opcode;
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(3), rawValue);
        frame[^1] = OfficialLoginWireTransform.ComputeChecksum(frame);
        return frame;
    }

    private static byte[] BuildInteraction26Frame(ushort raw0, ushort raw2, byte raw4, byte rawTail)
    {
        var frame = new byte[PublicBetaCompatibilityGameplayRequestWireAdapter.Interaction26DecodedFrameLength];
        BinaryPrimitives.WriteUInt16LittleEndian(frame, checked((ushort)frame.Length));
        frame[2] = PublicBetaCompatibilityGameplayRequestWireAdapter.Interaction26Opcode;
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(3), raw0);
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(5), raw2);
        frame[7] = raw4;
        frame[8] = rawTail;
        frame[^1] = OfficialLoginWireTransform.ComputeChecksum(frame);
        return frame;
    }

    private static byte[] BuildCombinationStepFrame(byte selector, sbyte step)
    {
        var frame = new byte[PublicBetaCompatibilityGameplayRequestWireAdapter.CombinationStepDecodedFrameLength];
        BinaryPrimitives.WriteUInt16LittleEndian(frame, checked((ushort)frame.Length));
        frame[2] = PublicBetaCompatibilityGameplayRequestWireAdapter.CombinationStepOpcode;
        frame[3] = selector;
        frame[4] = unchecked((byte)step);
        frame[^1] = OfficialLoginWireTransform.ComputeChecksum(frame);
        return frame;
    }

    private static byte[] BuildVendorCartPublishRawFrame(byte opcode = PublicBetaCompatibilityGameplayRequestWireAdapter.VendorCartPublishOpcode)
    {
        var frame = new byte[PublicBetaCompatibilityGameplayRequestWireAdapter.VendorCartPublishDecodedFrameLength];
        BinaryPrimitives.WriteUInt16LittleEndian(frame, checked((ushort)frame.Length));
        frame[2] = opcode;
        return frame;
    }

    private static byte[] BuildSequentialPayload(int length, byte seed = 0x01)
    {
        return Enumerable.Range(0, length)
            .Select(value => unchecked((byte)(seed + value)))
            .ToArray();
    }

    private static byte[] BuildRawFrame(byte opcode, ReadOnlySpan<byte> payload)
    {
        var frame = new byte[payload.Length + 4];
        BinaryPrimitives.WriteUInt16LittleEndian(frame, checked((ushort)frame.Length));
        frame[2] = opcode;
        payload.CopyTo(frame.AsSpan(3));
        frame[^1] = OfficialLoginWireTransform.ComputeChecksum(frame);
        return frame;
    }
}
