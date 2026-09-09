using System.Buffers.Binary;
using System.Security.Cryptography;
using God2.ClassicServer.Application.Common;
using God2.ClassicServer.Protocol;

namespace God2.ClassicServer.Runtime.Tests;

public sealed class RoadMapM4NpcInteractionTests
{
    [Fact]
    public void Live_dialog_handle_3793_replays_the_exact_open_response()
    {
        var projection = OfficialNpcInteractionWireCodec.SerializeDialogOpen(
            OfficialNpcInteractionWireCodec.ClientBuildId,
            3793);

        Assert.True(projection.Succeeded);
        Assert.Equal(
            "20007A1B000100D10EF8280780000000000000000000000000000000005D16F3",
            Convert.ToHexString(projection.Value!.DecodedFrame.Span));
        Assert.Equal(OfficialNpcInteractionWireCodec.LiveDialogEvidenceId, projection.Value.EvidenceId);
    }

    [Fact]
    public async Task Live_dialog_handle_3793_resolves_from_world_state_and_emits_the_exact_response()
    {
        var fixture = Fixture.Create(new WorldPosition3(65, 64), 3793);
        var loop = OfficialNpcInteractionClosedLoop.CreateForTesting(fixture.Sessions);

        var receipt = await loop.ExecuteFrameAsync(
            fixture.Session.SessionId,
            1,
            "live-dialog-3793-open",
            EncodedOpenFrame(3793),
            CancellationToken.None);

        Assert.True(
            receipt.Code == OfficialNpcInteractionTransactionCode.DialogOpened,
            $"Dialog open was rejected: {receipt.FailureCode}");
        Assert.True(receipt.RuntimeValidated);
        Assert.False(receipt.NetworkBytesEmitted);
        Assert.False(receipt.EncodedResponse.IsEmpty);
        Assert.Equal(
            "20007A1B000100D10EF8280780000000000000000000000000000000005D16F3",
            Convert.ToHexString(OfficialClientWorldProtocolFrames.DecodeWorldServerPayload(receipt.EncodedResponse.Span)));
    }

    [Theory]
    [InlineData("0A0085D10E07000012A3", 0x12)]
    [InlineData("0A0085D10E07000011A2", 0x11)]
    public void Live_dialog_selection_codec_accepts_both_observed_variable_client_values(
        string frameHex,
        byte expectedOpaqueClientValue)
    {
        var result = OfficialNpcInteractionWireCodec.DecodeDialogSelection(
            OfficialNpcInteractionWireCodec.ClientBuildId,
            GameplayProtocolState.World,
            Convert.FromHexString(frameHex));

        Assert.True(result.Succeeded);
        Assert.Equal(3793, result.Value!.ClientEntityHandle);
        Assert.Equal(7, result.Value.Selector);
        Assert.Equal(0, result.Value.PreservedState);
        Assert.Equal(expectedOpaqueClientValue, result.Value.OpaqueClientValue);
        Assert.Equal(OfficialNpcInteractionWireCodec.LiveDialogSelectionEvidenceId, result.Value.EvidenceId);
    }

    [Fact]
    public void Live_dialog_selection_codec_decodes_the_formal_compound_transport_without_assigning_086_semantics()
    {
        var encodedTransport = Convert.FromHexString("0F0028C9A047D23B3ABEB1C2C528F4");
        var decodedTransport = OfficialClientWorldProtocolFrames.DecodeWorldServerPayload(encodedTransport);

        Assert.Equal("0F0086D900821185D10E07000011C5", Convert.ToHexString(decodedTransport));
        var result = OfficialNpcInteractionWireCodec.DecodeDialogSelection(
            OfficialNpcInteractionWireCodec.ClientBuildId,
            GameplayProtocolState.World,
            decodedTransport);

        Assert.True(result.Succeeded);
        Assert.Equal(3793, result.Value!.ClientEntityHandle);
        Assert.Equal(7, result.Value.Selector);
        Assert.Equal(0x11, result.Value.OpaqueClientValue);
        Assert.Equal("wire.npc_interaction.dialog_close_evidence_blocked",
            OfficialNpcInteractionWireCodec.DecodeClose(
                OfficialNpcInteractionWireCodec.ClientBuildId,
                GameplayProtocolState.World,
                Convert.FromHexString("080086D90082119E")).FailureCode);
    }

    [Fact]
    public void Live_dialog_selection_codec_rejects_unobserved_identity_selector_state_and_bad_checksum()
    {
        static string Failure(byte[] frame) => OfficialNpcInteractionWireCodec.DecodeDialogSelection(
            OfficialNpcInteractionWireCodec.ClientBuildId,
            GameplayProtocolState.World,
            frame).FailureCode;

        var wrongHandle = DialogSelectionFrame(3792, 7, 0x11);
        var wrongSelector = DialogSelectionFrame(3793, 6, 0x11);
        var wrongState = DialogSelectionFrame(3793, 7, 0x11);
        BinaryPrimitives.WriteUInt16LittleEndian(wrongState.AsSpan(6), 1);
        wrongState[^1] = OfficialLoginWireTransform.ComputeChecksum(wrongState);
        var badChecksum = DialogSelectionFrame(3793, 7, 0x11);
        badChecksum[^1] ^= 1;

        Assert.Equal("wire.npc_interaction.dialog_selection_evidence_blocked", Failure(wrongHandle));
        Assert.Equal("wire.npc_interaction.dialog_selection_evidence_blocked", Failure(wrongSelector));
        Assert.Equal("wire.npc_interaction.dialog_selection_evidence_blocked", Failure(wrongState));
        Assert.Equal("wire.npc_interaction.dialog_selection_integrity_invalid", Failure(badChecksum));
    }

    [Fact]
    public async Task Live_dialog_selection_requires_active_authority_closes_session_and_emits_no_response()
    {
        var fixture = Fixture.Create(new WorldPosition3(65, 64), 3793);
        var loop = OfficialNpcInteractionClosedLoop.CreateForTesting(fixture.Sessions);
        var encodedSelection = OfficialClientWorldProtocolFrames.EncodeWorldServerPayload(
            Convert.FromHexString("0A0085D10E07000011A2"));

        Assert.False(loop.RecognizesEncodedDialogSelection(fixture.Session.SessionId, encodedSelection));
        var opened = await loop.ExecuteFrameAsync(
            fixture.Session.SessionId, 1, "live-dialog-open", EncodedOpenFrame(3793), CancellationToken.None);
        Assert.True(loop.RecognizesEncodedDialogSelection(fixture.Session.SessionId, encodedSelection));
        var selected = await loop.ExecuteFrameAsync(
            fixture.Session.SessionId, 2, "live-dialog-select", encodedSelection, CancellationToken.None);
        var replay = await loop.ExecuteFrameAsync(
            fixture.Session.SessionId, 3, "live-dialog-select-replay", encodedSelection, CancellationToken.None);

        Assert.Equal(OfficialNpcInteractionTransactionCode.DialogOpened, opened.Code);
        Assert.Equal(OfficialNpcInteractionTransactionCode.DialogOptionSelected, selected.Code);
        Assert.Equal("Dialog", selected.Handler);
        Assert.True(selected.RuntimeValidated);
        Assert.True(selected.EncodedResponse.IsEmpty);
        Assert.Equal("interaction.dialog_selection_without_active_state", replay.FailureCode);
        Assert.False(loop.RecognizesEncodedDialogSelection(fixture.Session.SessionId, encodedSelection));
    }

    [Fact]
    public async Task Live_compound_dialog_transport_routes_the_embedded_selection_and_closes_active_dialog()
    {
        var fixture = Fixture.Create(new WorldPosition3(65, 64), 3793);
        var loop = OfficialNpcInteractionClosedLoop.CreateForTesting(fixture.Sessions);
        var compoundTransport = Convert.FromHexString("0F0028C9A047D23B3ABEB1C2C528F4");

        await loop.ExecuteFrameAsync(
            fixture.Session.SessionId, 1, "compound-dialog-open", EncodedOpenFrame(3793), CancellationToken.None);
        Assert.True(loop.RecognizesEncodedDialogSelection(fixture.Session.SessionId, compoundTransport));
        var selected = await loop.ExecuteFrameAsync(
            fixture.Session.SessionId, 2, "compound-dialog-select", compoundTransport, CancellationToken.None);

        Assert.Equal(OfficialNpcInteractionTransactionCode.DialogOptionSelected, selected.Code);
        Assert.Equal("Dialog", selected.Handler);
        Assert.True(selected.RuntimeValidated);
        Assert.True(selected.EncodedResponse.IsEmpty);
        Assert.False(loop.RecognizesEncodedDialogSelection(fixture.Session.SessionId, compoundTransport));
    }

    [Fact]
    public async Task Dialog_selection_recognizer_does_not_intercept_active_merchant_selection()
    {
        var fixture = Fixture.Create(new WorldPosition3(17, 11), 3954);
        var loop = OfficialNpcInteractionClosedLoop.CreateForTesting(fixture.Sessions);
        await loop.ExecuteFrameAsync(
            fixture.Session.SessionId, 1, "live-merchant-open", EncodedOpenFrame(3954), CancellationToken.None);
        var merchantSelection = OfficialClientWorldProtocolFrames.EncodeWorldServerPayload(
            Convert.FromHexString("0A0085720F000000EE1A"));

        Assert.False(loop.RecognizesEncodedDialogSelection(fixture.Session.SessionId, merchantSelection));
    }

    [Fact]
    public void Dialog_open_codec_decodes_exact_request_shape_and_rejects_integrity_mutations()
    {
        var frame = OpenFrame(1504);

        var decoded = OfficialNpcInteractionWireCodec.DecodeOpen(
            OfficialNpcInteractionWireCodec.ClientBuildId,
            GameplayProtocolState.World,
            frame);
        var malformed = frame.ToArray();
        malformed[5] = 1;
        malformed[^1] = OfficialLoginWireTransform.ComputeChecksum(malformed);
        var malformedResult = OfficialNpcInteractionWireCodec.DecodeOpen(
            OfficialNpcInteractionWireCodec.ClientBuildId,
            GameplayProtocolState.World,
            malformed);
        var wrongState = OfficialNpcInteractionWireCodec.DecodeOpen(
            OfficialNpcInteractionWireCodec.ClientBuildId,
            GameplayProtocolState.Battle,
            frame);
        var truncated = OfficialNpcInteractionWireCodec.DecodeOpen(
            OfficialNpcInteractionWireCodec.ClientBuildId,
            GameplayProtocolState.World,
            frame.AsSpan(0, 7));

        Assert.True(decoded.Succeeded);
        Assert.Equal(1504, decoded.Value!.ClientEntityHandle);
        Assert.Equal("wire.npc_interaction.open_integrity_invalid", malformedResult.FailureCode);
        Assert.Equal("wire.npc_interaction.state_invalid", wrongState.FailureCode);
        Assert.Equal("wire.npc_interaction.open_length_invalid", truncated.FailureCode);
    }

    [Fact]
    public void Close_codec_decodes_capture_proven_merchant_close_and_blocks_quest_086_conflation()
    {
        var frame = CloseFrame(OfficialNpcInteractionWireCodec.MerchantCloseOpcode, 1504);

        var decoded = OfficialNpcInteractionWireCodec.DecodeClose(
            OfficialNpcInteractionWireCodec.ClientBuildId,
            GameplayProtocolState.World,
            frame);
        var nonzeroState = frame.ToArray();
        BinaryPrimitives.WriteUInt16LittleEndian(nonzeroState.AsSpan(5), 1);
        nonzeroState[^1] = OfficialLoginWireTransform.ComputeChecksum(nonzeroState);

        Assert.True(decoded.Succeeded);
        Assert.Equal(OfficialNpcInteractionCloseKind.Merchant, decoded.Value!.Kind);
        Assert.Equal("wire.npc_interaction.close_fields_invalid",
            OfficialNpcInteractionWireCodec.DecodeClose(
                OfficialNpcInteractionWireCodec.ClientBuildId,
                GameplayProtocolState.World,
                nonzeroState).FailureCode);
        Assert.Equal("wire.npc_interaction.dialog_close_evidence_blocked",
            OfficialNpcInteractionWireCodec.DecodeClose(
                OfficialNpcInteractionWireCodec.ClientBuildId,
                GameplayProtocolState.World,
                CloseFrame(OfficialNpcInteractionWireCodec.DialogOrQuestCloseOpcode, 83)).FailureCode);
        Assert.False(OfficialNpcInteractionClosedLoop.RecognizesEncodedInteraction(
            EncodedCloseFrame(OfficialNpcInteractionWireCodec.DialogOrQuestCloseOpcode, 83)));
    }

    [Fact]
    public void Dialog_open_serializer_reproduces_complete_hash_pinned_0x7a_records()
    {
        var serialized = OfficialNpcInteractionWireCodec.SerializeDialogOpen(
            OfficialNpcInteractionWireCodec.ClientBuildId,
            1504);

        Assert.True(serialized.Succeeded);
        var frame = serialized.Value!.DecodedFrame.ToArray();
        Assert.Equal(58, frame.Length);
        Assert.Equal(OfficialNpcInteractionWireCodec.DialogApplicationSha256,
            Convert.ToHexString(SHA256.HashData(frame.AsSpan(2, frame.Length - 3))));
        Assert.Equal(frame[^1], OfficialLoginWireTransform.ComputeChecksum(frame));
        Assert.Equal(0x7A, frame[2]);
        Assert.Equal(27, frame[3]);
        Assert.Equal(1504, BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(7)));
        Assert.Equal(1416, BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(9)));
        Assert.Equal(0x7A, frame[29]);
        Assert.Equal(28, frame[30]);
        Assert.Equal(1504, BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(34)));
        Assert.Equal(144, BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(36)));

        var encoded = OfficialClientWorldProtocolFrames.EncodeWorldServerPayload(frame);
        Assert.Equal(frame, OfficialClientWorldProtocolFrames.DecodeWorldServerPayload(encoded));
        Assert.Equal("wire.npc_interaction.dialog_profile_evidence_blocked",
            OfficialNpcInteractionWireCodec.SerializeDialogOpen(
                OfficialNpcInteractionWireCodec.ClientBuildId,
                4638).FailureCode);
    }

    [Fact]
    public async Task Production_merchant_slice_resolves_authoritative_handle_validates_runtime_and_serializes()
    {
        var fixture = Fixture.Create(new WorldPosition3(17, 11));
        var loop = OfficialNpcInteractionClosedLoop.CreateForTesting(fixture.Sessions);
        var encodedRequest = EncodedOpenFrame(1504);

        Assert.NotEqual(OfficialNpcInteractionWireCodec.OpenOpcode, encodedRequest[2]);
        Assert.True(OfficialNpcInteractionClosedLoop.RecognizesEncodedOpen(encodedRequest));

        var receipt = await loop.ExecuteFrameAsync(
            fixture.Session.SessionId,
            1,
            "m4-success",
            encodedRequest,
            CancellationToken.None);

        Assert.Equal(OfficialNpcInteractionTransactionCode.MerchantOpened, receipt.Code);
        Assert.True(receipt.RuntimeValidated);
        Assert.Equal(fixture.Npc.Identity.RuntimeObjectId, receipt.TargetRuntimeEntityId);
        Assert.Equal(19, receipt.RuntimeMapId);
        Assert.Equal("Merchant", receipt.Handler);
        Assert.Equal(OfficialNpcInteractionWireCodec.DialogApplicationSha256, receipt.ResponseApplicationSha256);
        var response = OfficialClientWorldProtocolFrames.DecodeWorldServerPayload(receipt.EncodedResponse.Span);
        Assert.Equal(0x7A, response[2]);
    }

    [Fact]
    public async Task Production_dialog_slice_rejects_out_of_range_unknown_and_malformed_without_bytes()
    {
        var fixture = Fixture.Create(new WorldPosition3(28, 34));
        var loop = OfficialNpcInteractionClosedLoop.CreateForTesting(fixture.Sessions);

        var outOfRange = await loop.ExecuteFrameAsync(
            fixture.Session.SessionId, 1, "m4-range", EncodedOpenFrame(1504), CancellationToken.None);
        var unsupported = await loop.ExecuteFrameAsync(
            fixture.Session.SessionId, 2, "m4-profile", EncodedOpenFrame(4638), CancellationToken.None);
        var malformedFrame = OpenFrame(1504);
        malformedFrame[^1] ^= 1;
        var malformed = await loop.ExecuteFrameAsync(
            fixture.Session.SessionId, 3, "m4-malformed", OfficialClientWorldProtocolFrames.EncodeWorldServerPayload(malformedFrame), CancellationToken.None);
        fixture.Sessions.UpdateSession(fixture.Session with { ProtocolStage = ProtocolStage.Closing });
        var wrongRuntimeState = await loop.ExecuteFrameAsync(
            fixture.Session.SessionId, 4, "m4-state", EncodedOpenFrame(1504), CancellationToken.None);

        Assert.Equal("interaction.out_of_range", outOfRange.FailureCode);
        Assert.Equal("wire.npc_interaction.dialog_profile_evidence_blocked", unsupported.FailureCode);
        Assert.Equal("wire.npc_interaction.open_integrity_invalid", malformed.FailureCode);
        Assert.Equal("interaction.ownership_mismatch", wrongRuntimeState.FailureCode);
        Assert.True(outOfRange.EncodedResponse.IsEmpty);
        Assert.True(unsupported.EncodedResponse.IsEmpty);
        Assert.True(malformed.EncodedResponse.IsEmpty);
        Assert.True(wrongRuntimeState.EncodedResponse.IsEmpty);
    }

    [Fact]
    public async Task Same_session_and_payload_is_idempotent_across_ingress_ordinals()
    {
        var fixture = Fixture.Create(new WorldPosition3(17, 11));
        var loop = OfficialNpcInteractionClosedLoop.CreateForTesting(fixture.Sessions);

        var first = await loop.ExecuteFrameAsync(
            fixture.Session.SessionId, 9, "m4-first", EncodedOpenFrame(1504), CancellationToken.None);
        var replay = await loop.ExecuteFrameAsync(
            fixture.Session.SessionId, 10, "m4-replay", EncodedOpenFrame(1504), CancellationToken.None);

        Assert.Equal(OfficialNpcInteractionTransactionCode.MerchantOpened, first.Code);
        Assert.Equal(OfficialNpcInteractionTransactionCode.DuplicateMerchantOpened, replay.Code);
        Assert.Equal(first.EncodedResponse.ToArray(), replay.EncodedResponse.ToArray());
    }

    [Fact]
    public async Task Merchant_close_requires_matching_active_authority_and_emits_no_response()
    {
        var fixture = Fixture.Create(new WorldPosition3(17, 11));
        var loop = OfficialNpcInteractionClosedLoop.CreateForTesting(fixture.Sessions);
        var opened = await loop.ExecuteFrameAsync(
            fixture.Session.SessionId, 1, "m4-open", EncodedOpenFrame(1504), CancellationToken.None);
        var wrongFamily = await loop.ExecuteFrameAsync(
            fixture.Session.SessionId, 2, "m4-wrong-close",
            EncodedCloseFrame(OfficialNpcInteractionWireCodec.DialogOrQuestCloseOpcode, 1504),
            CancellationToken.None);
        var closed = await loop.ExecuteFrameAsync(
            fixture.Session.SessionId, 3, "m4-close",
            EncodedCloseFrame(OfficialNpcInteractionWireCodec.MerchantCloseOpcode, 1504),
            CancellationToken.None);
        var replay = await loop.ExecuteFrameAsync(
            fixture.Session.SessionId, 4, "m4-close-replay",
            EncodedCloseFrame(OfficialNpcInteractionWireCodec.MerchantCloseOpcode, 1504),
            CancellationToken.None);

        Assert.True(
            opened.Code == OfficialNpcInteractionTransactionCode.MerchantOpened,
            $"Merchant open was rejected: {opened.FailureCode}");
        Assert.Equal("wire.npc_interaction.dialog_close_evidence_blocked", wrongFamily.FailureCode);
        Assert.Equal(OfficialNpcInteractionTransactionCode.InteractionClosed, closed.Code);
        Assert.Equal("Merchant", closed.Handler);
        Assert.True(closed.RuntimeValidated);
        Assert.True(closed.EncodedResponse.IsEmpty);
        Assert.Equal("interaction.close_without_active_state", replay.FailureCode);
    }

    [Fact]
    public async Task Unsynchronized_movement_blocks_new_open_until_authority_is_resynchronized()
    {
        var fixture = Fixture.Create(new WorldPosition3(17, 11));
        var loop = OfficialNpcInteractionClosedLoop.CreateForTesting(fixture.Sessions);
        loop.MarkPositionUnsynchronized(fixture.Session.SessionId);

        var blocked = await loop.ExecuteFrameAsync(
            fixture.Session.SessionId, 1, "m4-unsynchronized", EncodedOpenFrame(1504), CancellationToken.None);
        loop.MarkPositionSynchronized(fixture.Session.SessionId);
        var allowed = await loop.ExecuteFrameAsync(
            fixture.Session.SessionId, 2, "m4-resynchronized", EncodedOpenFrame(1504), CancellationToken.None);

        Assert.Equal("interaction.position_unsynchronized", blocked.FailureCode);
        Assert.True(blocked.EncodedResponse.IsEmpty);
        Assert.Equal(OfficialNpcInteractionTransactionCode.MerchantOpened, allowed.Code);
    }

    [Fact]
    public async Task Cancelled_open_does_not_leave_an_active_interaction_state()
    {
        var fixture = Fixture.Create(new WorldPosition3(17, 11));
        var loop = OfficialNpcInteractionClosedLoop.CreateForTesting(fixture.Sessions);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => loop.ExecuteFrameAsync(
            fixture.Session.SessionId,
            1,
            "m4-cancelled",
            EncodedOpenFrame(1504),
            cancellation.Token));

        var retry = await loop.ExecuteFrameAsync(
            fixture.Session.SessionId,
            2,
            "m4-retry-after-cancellation",
            EncodedOpenFrame(1504),
            CancellationToken.None);

        Assert.Equal(OfficialNpcInteractionTransactionCode.MerchantOpened, retry.Code);
        Assert.True(retry.RuntimeValidated);
    }

    [Fact]
    public async Task Production_open_fails_closed_when_the_active_gameplay_content_release_is_not_ready()
    {
        var fixture = Fixture.Create(new WorldPosition3(17, 11));
        var loop = OfficialNpcInteractionClosedLoop.Create(fixture.Sessions, new BlockedGameplayContentAuthority());

        var receipt = await loop.ExecuteFrameAsync(
            fixture.Session.SessionId,
            1,
            "m8-release-not-ready",
            EncodedOpenFrame(1504),
            CancellationToken.None);

        Assert.Equal(OfficialNpcInteractionTransactionCode.Rejected, receipt.Code);
        Assert.Equal("content_release.runtime_not_ready", receipt.FailureCode);
        Assert.True(receipt.EncodedResponse.IsEmpty);
        Assert.False(receipt.NetworkBytesEmitted);
    }

    [Fact]
    public async Task Live_stage_merchant_handle_opens_shop_and_commits_purchase_through_inventory_contract()
    {
        const ushort liveHandle = 3954;
        var fixture = Fixture.Create(new WorldPosition3(17, 11), liveHandle);
        var interactions = OfficialNpcInteractionClosedLoop.CreateForTesting(fixture.Sessions);
        var inventory = new StubInventoryTransactionCoordinator(
            new InventoryTransactionResult(
                InventoryTransactionResultCode.Success,
                Guid.Parse("de4dcb09-cff5-4277-9dc4-d1fb41a2726c"),
                "live-stage-buy",
                4,
                5,
                50000,
                25000));
        var merchant = new OfficialMerchantTransactionClosedLoop(interactions, inventory);

        var opened = await interactions.ExecuteFrameAsync(
            fixture.Session.SessionId,
            1,
            "live-stage-open",
            EncodedOpenFrame(liveHandle),
            CancellationToken.None);
        var selection = await merchant.ExecuteFrameAsync(
            fixture.Session.SessionId,
            1,
            OfficialClientWorldProtocolFrames.EncodeWorldServerPayload(
                Convert.FromHexString("0A0085720F000000EE1A")),
            CancellationToken.None);
        var purchase = await merchant.ExecuteFrameAsync(
            fixture.Session.SessionId,
            2,
            OfficialClientWorldProtocolFrames.EncodeWorldServerPayload(
                Convert.FromHexString("0C0038720FF51A0101070071")),
            CancellationToken.None);

        Assert.True(
            opened.Code == OfficialNpcInteractionTransactionCode.MerchantOpened,
            $"Merchant open was rejected: {opened.FailureCode}");
        Assert.Equal(
            "1F007A1C000400720F900000000000000000000000000000000000000000D2",
            Convert.ToHexString(OfficialClientWorldProtocolFrames.DecodeWorldServerPayload(opened.EncodedResponse.Span)));
        Assert.Equal(OfficialMerchantTransactionCode.ShopOpened, selection.Code);
        Assert.Equal(
            "100068720FA000000000006400000081",
            Convert.ToHexString(OfficialClientWorldProtocolFrames.DecodeWorldServerPayload(selection.EncodedResponse.Span)));
        Assert.Equal(OfficialMerchantTransactionCode.PurchaseCommitted, purchase.Code);
        Assert.Equal(
            "39003B01040000F51A00000000000000000000000000000000000000000000000000000000000027A861000069720F0000010041000007000B",
            Convert.ToHexString(OfficialClientWorldProtocolFrames.DecodeWorldServerPayload(purchase.EncodedResponse.Span)));
        Assert.NotNull(inventory.CapturedRequest);
        Assert.Equal(InventoryOperationType.MerchantBuy, inventory.CapturedRequest!.OperationType);
        Assert.Equal(4001, inventory.CapturedRequest.MerchantTemplateId);
        Assert.Equal(253231541, Assert.Single(inventory.CapturedRequest.RequestedMutations).ItemTemplateId);
    }

    [Fact]
    public async Task Live_stage_merchant_blocks_a_second_purchase_before_inventory_mutation()
    {
        var fixture = Fixture.Create(new WorldPosition3(17, 11), OfficialMerchantTransactionWireCodec.LiveStageMerchantHandle);
        var interactions = OfficialNpcInteractionClosedLoop.CreateForTesting(fixture.Sessions);
        var inventory = new StubInventoryTransactionCoordinator(
            SuccessfulMerchantResult(),
            InventorySnapshot(VerifiedMeatSlot()));
        var merchant = new OfficialMerchantTransactionClosedLoop(interactions, inventory);
        await interactions.ExecuteFrameAsync(
            fixture.Session.SessionId, 1, "live-stage-open", EncodedOpenFrame(3954), CancellationToken.None);

        var purchase = await merchant.ExecuteFrameAsync(
            fixture.Session.SessionId,
            2,
            OfficialClientWorldProtocolFrames.EncodeWorldServerPayload(
                Convert.FromHexString("0C0038720FF51A0101070071")),
            CancellationToken.None);

        Assert.Equal(OfficialMerchantTransactionCode.Rejected, purchase.Code);
        Assert.Equal("wire.merchant.buy_inventory_state_evidence_blocked", purchase.FailureCode);
        Assert.Equal(0, inventory.ExecuteCallCount);
        Assert.Null(inventory.CapturedRequest);
    }

    [Theory]
    [InlineData(false, 1)]
    [InlineData(true, 2)]
    public async Task Live_stage_merchant_blocks_sale_from_unverified_inventory_state(
        bool includeMeat,
        int quantity)
    {
        var fixture = Fixture.Create(new WorldPosition3(17, 11), OfficialMerchantTransactionWireCodec.LiveStageMerchantHandle);
        var interactions = OfficialNpcInteractionClosedLoop.CreateForTesting(fixture.Sessions);
        var slots = includeMeat ? new[] { VerifiedMeatSlot(quantity) } : [];
        var inventory = new StubInventoryTransactionCoordinator(
            SuccessfulMerchantResult(),
            InventorySnapshot(slots));
        var merchant = new OfficialMerchantTransactionClosedLoop(interactions, inventory);
        await interactions.ExecuteFrameAsync(
            fixture.Session.SessionId, 1, "live-stage-open", EncodedOpenFrame(3954), CancellationToken.None);

        var sale = await merchant.ExecuteFrameAsync(
            fixture.Session.SessionId,
            2,
            OfficialClientWorldProtocolFrames.EncodeWorldServerPayload(
                Convert.FromHexString("0C0038720FF51A010204006F")),
            CancellationToken.None);

        Assert.Equal(OfficialMerchantTransactionCode.Rejected, sale.Code);
        Assert.Equal("wire.merchant.sell_inventory_state_evidence_blocked", sale.FailureCode);
        Assert.Equal(0, inventory.ExecuteCallCount);
        Assert.Null(inventory.CapturedRequest);
    }

    [Fact]
    public async Task Live_stage_merchant_allows_sale_from_the_verified_single_meat_state()
    {
        var fixture = Fixture.Create(new WorldPosition3(17, 11), OfficialMerchantTransactionWireCodec.LiveStageMerchantHandle);
        var interactions = OfficialNpcInteractionClosedLoop.CreateForTesting(fixture.Sessions);
        var inventory = new StubInventoryTransactionCoordinator(
            SuccessfulMerchantResult(),
            InventorySnapshot(VerifiedMeatSlot()));
        var merchant = new OfficialMerchantTransactionClosedLoop(interactions, inventory);
        await interactions.ExecuteFrameAsync(
            fixture.Session.SessionId, 1, "live-stage-open", EncodedOpenFrame(3954), CancellationToken.None);

        var sale = await merchant.ExecuteFrameAsync(
            fixture.Session.SessionId,
            2,
            OfficialClientWorldProtocolFrames.EncodeWorldServerPayload(
                Convert.FromHexString("0C0038720FF51A010204006F")),
            CancellationToken.None);

        Assert.Equal(OfficialMerchantTransactionCode.SaleCommitted, sale.Code);
        Assert.Equal(1, inventory.ExecuteCallCount);
        Assert.Equal(4, inventory.CapturedRequest!.ExpectedInventoryVersion);
        var mutation = Assert.Single(inventory.CapturedRequest.RequestedMutations);
        Assert.Equal(OfficialInventoryBootstrapWireCodec.VerifiedAuthoritySlot, mutation.SourceSlotIndex);
        Assert.Equal(1, mutation.Quantity);
    }

    private static InventoryTransactionResult SuccessfulMerchantResult() =>
        new(
            InventoryTransactionResultCode.Success,
            Guid.Parse("de4dcb09-cff5-4277-9dc4-d1fb41a2726c"),
            "live-stage-transaction",
            4,
            5,
            50000,
            25000);

    private static PlayerInventorySnapshot InventorySnapshot(params InventorySlot[] slots) =>
        new(Guid.Parse("9e3423a9-0fa2-4757-93e6-e47812a0b994"), 20, 32, 4, 4, "Clean", slots);

    private static InventorySlot VerifiedMeatSlot(int quantity = 1) =>
        new(
            OfficialInventoryBootstrapWireCodec.VerifiedAuthoritySlot,
            1,
            1,
            0,
            OfficialMerchantTransactionWireCodec.LiveStageCanonicalItemId,
            quantity,
            "Unbound",
            "{}",
            DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch,
            1);

    private static byte[] OpenFrame(ushort handle)
    {
        var frame = new byte[OfficialNpcInteractionWireCodec.OpenFrameLength];
        BinaryPrimitives.WriteUInt16LittleEndian(frame, checked((ushort)frame.Length));
        frame[2] = OfficialNpcInteractionWireCodec.OpenOpcode;
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(3), handle);
        frame[^1] = OfficialLoginWireTransform.ComputeChecksum(frame);
        return frame;
    }

    private static byte[] EncodedOpenFrame(ushort handle) =>
        OfficialClientWorldProtocolFrames.EncodeWorldServerPayload(OpenFrame(handle));

    private static byte[] CloseFrame(byte opcode, ushort handle)
    {
        var frame = new byte[OfficialNpcInteractionWireCodec.CloseFrameLength];
        BinaryPrimitives.WriteUInt16LittleEndian(frame, checked((ushort)frame.Length));
        frame[2] = opcode;
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(3), handle);
        frame[^1] = OfficialLoginWireTransform.ComputeChecksum(frame);
        return frame;
    }

    private static byte[] EncodedCloseFrame(byte opcode, ushort handle) =>
        OfficialClientWorldProtocolFrames.EncodeWorldServerPayload(CloseFrame(opcode, handle));

    private static byte[] DialogSelectionFrame(ushort handle, byte selector, byte opaqueClientValue)
    {
        var frame = new byte[OfficialNpcInteractionWireCodec.DialogSelectionFrameLength];
        BinaryPrimitives.WriteUInt16LittleEndian(frame, checked((ushort)frame.Length));
        frame[2] = OfficialNpcInteractionWireCodec.DialogSelectionOpcode;
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(3), handle);
        frame[5] = selector;
        frame[8] = opaqueClientValue;
        frame[^1] = OfficialLoginWireTransform.ComputeChecksum(frame);
        return frame;
    }

    private sealed record Fixture(
        StubWorldSessionCoordinator Sessions,
        RuntimeSession Session,
        NpcObject Npc)
    {
        public static Fixture Create(WorldPosition3 playerPosition, ushort clientEntityHandle = 1504)
        {
            var liveStageMerchant = clientEntityHandle == 3954;
            var liveDialog = clientEntityHandle == 3793;
            var npcPosition = liveDialog ? new WorldPosition3(65, 61) : new WorldPosition3(17, 8);
            var identity = new OfficialNpcWireIdentity(
                OfficialNpcReplicationWireCodec.ClientBuildId,
                clientEntityHandle,
                liveStageMerchant || liveDialog ? (byte)0 : (byte)2,
                liveStageMerchant ? (byte)81 : liveDialog ? (byte)87 : (byte)142,
                3,
                liveDialog ? (byte)5 : (byte)4,
                1,
                liveStageMerchant
                    ? "F53E8D79A02FB96A528E9ABF334E5D1383018780BA79D62340549FC5AD36885B"
                    : liveDialog
                        ? "FAEB2E6FEC5D9B23F9A06143085535443473A8D63A8C0165BA43CF9586F8FFFF"
                    : "83D11BE70AB2E0D6660BC38915E8C3C101FFFC2C67D7A950453E24D79669A84B",
                "Verified",
                "M4 exact-build evidence",
                liveStageMerchant || liveDialog
                    ? OfficialNpcReplicationWireCodec.LiveStageMerchantOpaqueTemplateSha256
                    : OfficialNpcReplicationWireCodec.OpaqueTemplateSha256);
            var placement = new NpcPlacementRecord(
                316049902,
                1075128734,
                19,
                npcPosition,
                WorldDirection.Unknown,
                "Always",
                liveDialog ? null : 4001,
                "M4 test",
                1,
                "Verified",
                "雜貨老闆",
                "data2/rom/npc/npc2643.ROM",
                liveDialog ? "Dialog" : "Merchant",
                true,
                ContentVersion: "m4-test-v1",
                WireIdentity: identity);
            var map = new MapDefinition(
                19,
                19,
                "M4 Map 19",
                "m4-test",
                new MapBounds(0, 0, 209, 629),
                [new SpawnPoint("Player", playerPosition, WorldDirection.Unknown, "M4")],
                [placement],
                [],
                [],
                liveDialog ? [] : [new MerchantMapping(
                    4001,
                    1075128734,
                    316049902,
                    "M4 verified Merchant binding",
                    "雜貨老闆",
                    "M4",
                    "Currency",
                    [],
                    true)],
                [],
                new ClientMapIdentity(
                    19, 19, 4, "m4-map-19", OfficialNpcInteractionWireCodec.ClientBuildId,
                    1m, 1m, 0m, 0m,
                    MapIdentityEvidenceStatus.Verified,
                    MapIdentityEvidenceStatus.Verified,
                    true,
                    "M4 test"));
            var runtime = new MapRuntimeFactory().Create(
                new WorldContentSnapshot(new Dictionary<int, MapDefinition> { [19] = map }, []),
                19).Value!.MapRuntime;
            var session = RuntimeSession.Connected("m4-connection", "127.0.0.1:1000", DateTimeOffset.UnixEpoch) with
            {
                SessionId = "m4-session",
                AccountId = 10,
                CharacterId = 20,
                IsAuthenticated = true,
                ProtocolStage = ProtocolStage.InWorld
            };
            var character = new CharacterSummary(
                20, 10, "M4Player", "Class1", "Gender1", "LifeSkill1", 1, "Default",
                19, playerPosition.X, playerPosition.Y, "Active", DateTimeOffset.UnixEpoch, null);
            var binding = new WorldSessionBinder(new MapRuntimeFactory()).BindToRuntime(
                session,
                character,
                runtime,
                WorldContentMode.MariaDbAuthoritative).Value!;
            var sessions = new StubWorldSessionCoordinator(binding);
            return new Fixture(sessions, session, Assert.Single(runtime.Objects.OfType<NpcObject>()));
        }
    }

    private sealed class StubInventoryTransactionCoordinator : IInventoryTransactionCoordinator
    {
        private readonly InventoryTransactionResult _result;
        private readonly PlayerInventorySnapshot _snapshot;

        public StubInventoryTransactionCoordinator(
            InventoryTransactionResult result,
            PlayerInventorySnapshot? snapshot = null)
        {
            _result = result;
            _snapshot = snapshot ?? InventorySnapshot();
        }

        public InventoryTransactionRequest? CapturedRequest { get; private set; }

        public int ExecuteCallCount { get; private set; }

        public IReadOnlyList<InventoryTransactionRuntimeEvent> Events => [];

        public Task<OperationResult<long>> GetCurrentVersionAsync(
            long characterId,
            CancellationToken cancellationToken) =>
            Task.FromResult(OperationResult<long>.Success(4));

        public Task<OperationResult<PlayerInventorySnapshot>> LoadInventoryAsync(
            long characterId,
            CancellationToken cancellationToken) =>
            Task.FromResult(OperationResult<PlayerInventorySnapshot>.Success(_snapshot));

        public Task<InventoryTransactionResult> ExecuteAsync(
            InventoryTransactionRequest request,
            CancellationToken cancellationToken)
        {
            ExecuteCallCount++;
            CapturedRequest = request;
            return Task.FromResult(_result);
        }

        public InventoryInspectorSnapshot CaptureInspector(InventoryInspectorQuery query) =>
            new(DateTimeOffset.UnixEpoch, [], [], [], []);
    }

    private sealed class StubWorldSessionCoordinator : IWorldSessionCoordinator
    {
        private WorldSessionBinding _binding;

        public StubWorldSessionCoordinator(WorldSessionBinding binding) => _binding = binding;

        public WorldContentAuthorityKind AuthorityKind => WorldContentAuthorityKind.MariaDb;
        public int ActiveBindingCount => 1;

        public Task<OperationResult<WorldSessionBinding>> BindAsync(
            RuntimeSession session,
            CharacterSummary character,
            CancellationToken cancellationToken) =>
            Task.FromResult(OperationResult<WorldSessionBinding>.Failure("test.not_supported", "Already bound."));

        public OperationResult<WorldSessionBinding> GetBinding(string sessionId) =>
            string.Equals(sessionId, _binding.Session.SessionId, StringComparison.Ordinal)
                ? OperationResult<WorldSessionBinding>.Success(_binding)
                : OperationResult<WorldSessionBinding>.Failure("world_authority.session_not_bound", "Not bound.");

        public OperationResult<WorldSessionBinding> UpdateSession(RuntimeSession session)
        {
            _binding = _binding with { Session = session };
            return OperationResult<WorldSessionBinding>.Success(_binding);
        }

        public bool Unbind(string sessionId) => false;
    }

    private sealed class BlockedGameplayContentAuthority : IProductionGameplayContentAuthority
    {
        public string ActiveReleaseId => string.Empty;

        public bool IsReady => false;

        public OperationResult RequireReady() => OperationResult.Failure(
            "content_release.runtime_not_ready",
            "No active gameplay content release is ready.");

        public OperationResult<ProductionQuestContent> ResolveQuest(int questId) =>
            OperationResult<ProductionQuestContent>.Failure("content_release.runtime_not_ready", "Not ready.");

        public OperationResult<ProductionEquipmentSetContent> ResolveEquipmentSet(int setId) =>
            OperationResult<ProductionEquipmentSetContent>.Failure("content_release.runtime_not_ready", "Not ready.");

        public OperationResult<ProductionPetInnateContent> ResolvePetInnate(int innateId) =>
            OperationResult<ProductionPetInnateContent>.Failure("content_release.runtime_not_ready", "Not ready.");
    }
}
