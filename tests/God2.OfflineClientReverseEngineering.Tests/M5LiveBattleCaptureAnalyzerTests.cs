using God2.OfflineClientReverseEngineering;

namespace God2.OfflineClientReverseEngineering.Tests;

public sealed class M5LiveBattleCaptureAnalyzerTests
{
    [Fact]
    public void ResolveRecordLength_ReadsOfficialMovEaxLengthStub()
    {
        var code = new byte[M5LiveBattleCaptureAnalyzer.RecordLengthJumpTableRva + 256 * 4 + 64];
        const byte opcode = 0x83;
        var targetRva = (uint)(code.Length - 16);
        var absoluteTarget = M5LiveBattleCaptureAnalyzer.ImageBase + targetRva;
        BitConverter.GetBytes(absoluteTarget).CopyTo(
            code,
            M5LiveBattleCaptureAnalyzer.RecordLengthJumpTableRva + opcode * 4);
        code[targetRva] = 0xB8;
        BitConverter.GetBytes(15).CopyTo(code, targetRva + 1);

        var resolved = M5LiveBattleCaptureAnalyzer.ResolveRecordLength(code, opcode);

        Assert.NotNull(resolved);
        Assert.Equal(15, resolved.Value.Length);
        Assert.Equal(targetRva, resolved.Value.TargetRva);
    }

    [Fact]
    public void ParseEffect83_UsesVerifiedEffectSourceTargetAndSignedResultFields()
    {
        var record = Convert.FromHexString("830101010000000400DBFF001C0000");

        var effect = M5LiveBattleCaptureAnalyzer.ParseEffect83(709, 4, record);

        Assert.Equal((byte)1, effect.EffectKind);
        Assert.Equal((byte)1, effect.SourceBattlePosition);
        Assert.Equal((byte)0, effect.SourceSide);
        Assert.Equal((byte)1, effect.SourceSlot);
        Assert.Equal((byte)1, effect.PlaybackGate);
        Assert.Equal((ushort)0, effect.FriendlyTargetMask);
        Assert.Equal((ushort)0x0400, effect.EnemyTargetMask);
        Assert.Equal((byte)0, effect.ReservedByte8);
        Assert.Equal([24], effect.TargetBattlePositions);
        Assert.Equal((short)-37, effect.SignedResultCandidate);
        Assert.Equal((ushort)0x1C00, effect.AuxiliaryValue0);
        Assert.Equal((ushort)0, effect.AuxiliaryValue1);
        Assert.True(effect.LayoutRoundTripVerified);
    }

    [Fact]
    public void ParseEffect83_RejectsUnverifiedLength()
    {
        Assert.Throws<InvalidDataException>(() =>
            M5LiveBattleCaptureAnalyzer.ParseEffect83(1, 2, new byte[14]));
    }

    [Fact]
    public void ResolveEffectKindDispatchProfile_ReadsBothExactIndexedJumpTables()
    {
        var code = new byte[M5LiveBattleCaptureAnalyzer.EffectActionQueueIndexTableRva + 32];
        const byte effectKind = 7;
        code[M5LiveBattleCaptureAnalyzer.EffectProjectorIndexTableRva + effectKind - 1] = 2;
        code[M5LiveBattleCaptureAnalyzer.EffectActionQueueIndexTableRva + effectKind - 1] = 3;
        BitConverter.GetBytes(M5LiveBattleCaptureAnalyzer.ImageBase + 0x1234u).CopyTo(
            code,
            M5LiveBattleCaptureAnalyzer.EffectProjectorJumpTableRva + 2 * sizeof(uint));
        BitConverter.GetBytes(M5LiveBattleCaptureAnalyzer.ImageBase + 0x5678u).CopyTo(
            code,
            M5LiveBattleCaptureAnalyzer.EffectActionQueueJumpTableRva + 3 * sizeof(uint));

        var profile = M5LiveBattleCaptureAnalyzer.ResolveEffectKindDispatchProfile(code, effectKind);

        Assert.Equal("0x00001234", profile.ProjectorTargetRva);
        Assert.Equal("0x00005678", profile.ActionQueueTargetRva);
        Assert.True(profile.DirectSignedResultSlotWriteInProjector);
        Assert.Contains("IntermediateActorStateMachineRequired", profile.TypedMutatorRoutingStatus,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ResolveActorStateRoutingEvidence_BindsAuxiliaryHighByteToTypedHpMpRoutesWithoutClaimingObservation()
    {
        var code = new byte[M5LiveBattleCaptureAnalyzer.ActorStateSelectorTableRva + 0x43];
        Convert.FromHexString("8BCF500FB6460B500FB6460A50FF7510FF750CE85EB60000")
            .CopyTo(code, 0x001556AA);
        Convert.FromHexString("8A45143C43730E0FB6C08A80104F7F00884510")
            .CopyTo(code, 0x00160D6B);
        Convert.FromHexString("8A4514752F888632020000C6813202000000")
            .CopyTo(code, 0x00163319);
        Convert.FromHexString("8A450B3C77746B3C7874553C79742F")
            .CopyTo(code, 0x0015F16D);
        code[M5LiveBattleCaptureAnalyzer.ActorStateSelectorTableRva + 0x22] = 0x77;
        code[M5LiveBattleCaptureAnalyzer.ActorStateSelectorTableRva + 0x23] = 0x78;
        code[M5LiveBattleCaptureAnalyzer.ActorStateSelectorTableRva + 0x24] = 0x79;
        var observedEffect = M5LiveBattleCaptureAnalyzer.ParseEffect83(
            709,
            4,
            Convert.FromHexString("830101010000000400DBFF001C0000"));

        var evidence = M5LiveBattleCaptureAnalyzer.ResolveActorStateRoutingEvidence(code, [observedEffect]);

        Assert.Equal(12, evidence.RecordSelectorOffset);
        Assert.Equal("AuxiliaryValue0HighByte", evidence.RecordSelectorField);
        Assert.Equal(["0x1C"], evidence.ObservedSelectorIndices);
        Assert.Equal(["0x22", "0x23", "0x24"], evidence.TypedRoutes.Select(route => route.SelectorIndex));
        Assert.Equal(["0x77", "0x78", "0x79"], evidence.TypedRoutes.Select(route => route.ActorStateValue));
        Assert.All(evidence.TypedRoutes, route => Assert.False(route.ObservedInCapture));
        Assert.Equal("ExactBuildStaticRouteVerified_ServerEmissionNotObserved", evidence.EvidenceStatus);
    }

    [Fact]
    public void OfficialSnapshot88DecoderExposesVerifiedCurrentVitalPairsWithoutNamingHpOrMp()
    {
        var record = Convert.FromHexString(
            "880000000000000000000000002C0200008E000000000000000000000000000000" +
            "0000000000000000000000000000000000000000070100003F0000000000000000" +
            "000000000000000000000000000000000000000000000000000000000000000000" +
            "00000000000000000000000000000000000020004B19");

        var decoded = God2.ClassicServer.Protocol.OfficialBattleVitalSnapshotWireCodec.DecodeLayout(
            God2.ClassicServer.Protocol.OfficialBattleVitalSnapshotWireCodec.ClientBuildId,
            God2.ClassicServer.Protocol.GameplayProtocolState.Battle,
            record);

        Assert.True(decoded.LayoutDecoded);
        Assert.NotNull(decoded.Value);
        Assert.Equal(556, decoded.Value.Positions[1].CurrentHitPoints);
        Assert.Equal(142, decoded.Value.Positions[1].CurrentMagicPoints);
        Assert.Equal((byte)0, decoded.Value.ConsumedTrailerByte118);
        Assert.Equal("HitPointsMagicPointsConsumerVerified_TrailerByte118ConsumerVerifiedMeaningBlocked",
            decoded.Value.SemanticEvidenceStatus);
    }
}
