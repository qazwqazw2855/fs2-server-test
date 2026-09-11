using God2.ClassicServer.Application.Common;
using God2.ClassicServer.Protocol;
using System.Buffers.Binary;
using System.Linq;

namespace God2.ClassicServer.Runtime;

internal static class RuntimeProtocolConnectionRuntimeFactory
{
    public static ProtocolConnectionRuntime Create(
        PacketFactory packetFactory,
        OfficialDecryptedPacketCorpusCatalog? decryptedPacketCorpus = null,
        bool preserveRawEvidence = false,
        IProtocolAuditLog? audit = null,
        IUnknownPacketCaptureSink? unknownPacketCapture = null,
        string? officialLiveMovementEvidenceJsonlPath = null)
    {
        var compatibilityKnowledge = new PacketKnowledge[]
        {
            BuildFallbackCompatibilityPacketKnowledge(
                "Item",
                PublicBetaCompatibilityGameplayRequestWireAdapter.AccountLoginOpcode,
                PublicBetaCompatibilityGameplayRequestWireAdapter.AccountLoginDecodedFrameLength),
            BuildFallbackCompatibilityPacketKnowledge(
                "Item",
                PublicBetaCompatibilityGameplayRequestWireAdapter.GameLoginOpcode,
                PublicBetaCompatibilityGameplayRequestWireAdapter.GameLoginDecodedFrameLength),
            BuildFallbackCompatibilityPacketKnowledge(
                "Item",
                PublicBetaCompatibilityGameplayRequestWireAdapter.RouteOpcode,
                PublicBetaCompatibilityGameplayRequestWireAdapter.RouteDecodedFrameLength),
            BuildFallbackCompatibilityPacketKnowledge(
                "Item",
                PublicBetaCompatibilityGameplayRequestWireAdapter.AttributeIncrementOpcode,
                PublicBetaCompatibilityGameplayRequestWireAdapter.AttributeIncrementDecodedFrameLength),
            BuildFallbackCompatibilityPacketKnowledge(
                "Item",
                PublicBetaCompatibilityGameplayRequestWireAdapter.GameplayDisconnectOpcode,
                PublicBetaCompatibilityGameplayRequestWireAdapter.GameplayDisconnectDecodedFrameLength),
            BuildFallbackCompatibilityPacketKnowledge(
                "Item",
                PublicBetaCompatibilityGameplayRequestWireAdapter.Interaction24Opcode,
                PublicBetaCompatibilityGameplayRequestWireAdapter.DecodedFrameLength),
            BuildFallbackCompatibilityPacketKnowledge(
                "Item",
                PublicBetaCompatibilityGameplayRequestWireAdapter.Interaction26Opcode,
                PublicBetaCompatibilityGameplayRequestWireAdapter.Interaction26DecodedFrameLength),
            BuildFallbackCompatibilityPacketKnowledge(
                "Item",
                PublicBetaCompatibilityGameplayRequestWireAdapter.Interaction27Opcode,
                PublicBetaCompatibilityGameplayRequestWireAdapter.Interaction27DecodedFrameLength),
            BuildFallbackCompatibilityPacketKnowledge(
                "Item",
                PublicBetaCompatibilityGameplayRequestWireAdapter.Interaction28Opcode,
                PublicBetaCompatibilityGameplayRequestWireAdapter.Interaction28DecodedFrameLength),
            BuildFallbackCompatibilityPacketKnowledge(
                "Item",
                PublicBetaCompatibilityGameplayRequestWireAdapter.CombinationStepOpcode,
                PublicBetaCompatibilityGameplayRequestWireAdapter.CombinationStepDecodedFrameLength),
            BuildFallbackCompatibilityPacketKnowledge(
                "Item",
                PublicBetaCompatibilityGameplayRequestWireAdapter.PartySelectionOpcode,
                PublicBetaCompatibilityGameplayRequestWireAdapter.PartySelectionDecodedFrameLength),
            BuildFallbackCompatibilityPacketKnowledge(
                "Item",
                PublicBetaCompatibilityGameplayRequestWireAdapter.PartyRequest95Opcode,
                PublicBetaCompatibilityGameplayRequestWireAdapter.PartyRequest95DecodedFrameLength),
            BuildFallbackCompatibilityPacketKnowledge(
                "Item",
                PublicBetaCompatibilityGameplayRequestWireAdapter.CharacterDeleteRequestOpcode,
                PublicBetaCompatibilityGameplayRequestWireAdapter.CharacterDeleteDecodedFrameLength),
            BuildFallbackCompatibilityPacketKnowledge(
                "Item",
                PublicBetaCompatibilityGameplayRequestWireAdapter.CharacterSelectRequestOpcode,
                PublicBetaCompatibilityGameplayRequestWireAdapter.CharacterSelectDecodedFrameLength),
            BuildFallbackCompatibilityPacketKnowledge(
                "Item",
                PublicBetaCompatibilityGameplayRequestWireAdapter.CharacterCreate1bRequestOpcode,
                PublicBetaCompatibilityGameplayRequestWireAdapter.CharacterCreate1bDecodedFrameLength),
            BuildFallbackCompatibilityPacketKnowledge(
                "Item",
                PublicBetaCompatibilityGameplayRequestWireAdapter.TeamRequestOpcode,
                PublicBetaCompatibilityGameplayRequestWireAdapter.TeamRequestDecodedFrameLength),
            BuildFallbackCompatibilityPacketKnowledge(
                "Item",
                PublicBetaCompatibilityGameplayRequestWireAdapter.CombinationCommitOpcode,
                PublicBetaCompatibilityGameplayRequestWireAdapter.CombinationCommitDecodedFrameLength),
            BuildFallbackCompatibilityPacketKnowledge(
                "Item",
                PublicBetaCompatibilityGameplayRequestWireAdapter.MailRecordActionOpcode,
                PublicBetaCompatibilityGameplayRequestWireAdapter.MailRecordActionDecodedFrameLength),
            BuildFallbackCompatibilityPacketKnowledge(
                "Item",
                PublicBetaCompatibilityGameplayRequestWireAdapter.PartyRequest8FOpcode,
                PublicBetaCompatibilityGameplayRequestWireAdapter.PartyRequest8FDecodedFrameLength),
            BuildFallbackCompatibilityPacketKnowledge(
                "Item",
                PublicBetaCompatibilityGameplayRequestWireAdapter.PartyRequest90Opcode,
                PublicBetaCompatibilityGameplayRequestWireAdapter.PartyRequest90DecodedFrameLength),
            BuildFallbackCompatibilityPacketKnowledge(
                "Item",
                PublicBetaCompatibilityGameplayRequestWireAdapter.PartyRequest91Opcode,
                PublicBetaCompatibilityGameplayRequestWireAdapter.PartyRequest91DecodedFrameLength),
            BuildFallbackCompatibilityPacketKnowledge(
                "Item",
                PublicBetaCompatibilityGameplayRequestWireAdapter.PartyRequest92Opcode,
                PublicBetaCompatibilityGameplayRequestWireAdapter.PartyRequest92DecodedFrameLength),
            BuildFallbackCompatibilityPacketKnowledge(
                "Item",
                PublicBetaCompatibilityGameplayRequestWireAdapter.PartyRequest94Opcode,
                PublicBetaCompatibilityGameplayRequestWireAdapter.PartyRequest94DecodedFrameLength),
            BuildFallbackCompatibilityPacketKnowledge(
                "Item",
                PublicBetaCompatibilityGameplayRequestWireAdapter.PetEggRequestOpcode,
                PublicBetaCompatibilityGameplayRequestWireAdapter.PetEggRequestDecodedFrameLength),
            BuildFallbackCompatibilityPacketKnowledge(
                "Item",
                PublicBetaCompatibilityGameplayRequestWireAdapter.FPetRequestOpcode,
                PublicBetaCompatibilityGameplayRequestWireAdapter.FPetRequestDecodedFrameLength),
            BuildFallbackCompatibilityPacketKnowledge(
                "Item",
                PublicBetaCompatibilityGameplayRequestWireAdapter.PkCursorTargetOpcode,
                PublicBetaCompatibilityGameplayRequestWireAdapter.PkCursorTargetDecodedFrameLength),
            BuildFallbackCompatibilityPacketKnowledge(
                "Item",
                PublicBetaCompatibilityGameplayRequestWireAdapter.PetPkTargetOpcode,
                PublicBetaCompatibilityGameplayRequestWireAdapter.PetPkTargetDecodedFrameLength),
            BuildFallbackCompatibilityPacketKnowledge(
                "Item",
                PublicBetaCompatibilityGameplayRequestWireAdapter.VendorCartActionB2Opcode,
                PublicBetaCompatibilityGameplayRequestWireAdapter.VendorCartActionB2DecodedFrameLength),
            BuildFallbackCompatibilityPacketKnowledge(
                "Item",
                PublicBetaCompatibilityGameplayRequestWireAdapter.VendorCartPublishOpcode,
                PublicBetaCompatibilityGameplayRequestWireAdapter.VendorCartPublishDecodedFrameLength),
            BuildFallbackCompatibilityPacketKnowledge(
                "Item",
                PublicBetaCompatibilityGameplayRequestWireAdapter.VendorCartAddRecordOpcode,
                PublicBetaCompatibilityGameplayRequestWireAdapter.VendorCartAddRecordDecodedFrameLength),
            BuildFallbackCompatibilityPacketKnowledge(
                "Item",
                PublicBetaCompatibilityGameplayRequestWireAdapter.SocialTextEnvelopeOpcode,
                4)
        };

        var protocolKnowledgeBase = new ProtocolKnowledgeBase(
            ProtocolRegistry.Official.KnowledgeBase.Version,
            ProtocolRegistry.Official.Packets.Concat(compatibilityKnowledge).ToArray(),
            ProtocolRegistry.Official.KnowledgeBase.Remaining,
            ProtocolRegistry.Official.KnowledgeBase.VisibilityBoundary);
        var protocolRegistry = new ProtocolRegistry(protocolKnowledgeBase);
        var packetRegistry = new PacketRegistry(protocolRegistry);
        RegisterOfficialGameplayCompatibilityHandlers(packetRegistry);
        var runtimePacketFactory = new PacketFactory(new PacketDeserializer(protocolRegistry));

        var router = new InMemoryPacketRouter(packetRegistry);
        return new ProtocolConnectionRuntime(
            runtimePacketFactory,
            router,
            unknownPacketCapture: unknownPacketCapture,
            audit: audit,
            decryptedPacketCorpus: decryptedPacketCorpus,
            officialLiveMovementEvidenceSink: OfficialLiveMovementEvidenceSinkFactory.CreateJsonlOrNull(
                officialLiveMovementEvidenceJsonlPath),
            preserveRawEvidence: preserveRawEvidence);
    }

    private static void RegisterOfficialGameplayCompatibilityHandlers(PacketRegistry packetRegistry)
    {
        byte[] variablePayloadOpcodes =
        {
            PublicBetaCompatibilityGameplayRequestWireAdapter.AttributeIncrementOpcode,
            PublicBetaCompatibilityGameplayRequestWireAdapter.Interaction24Opcode,
            PublicBetaCompatibilityGameplayRequestWireAdapter.Interaction26Opcode,
            PublicBetaCompatibilityGameplayRequestWireAdapter.Interaction27Opcode,
            PublicBetaCompatibilityGameplayRequestWireAdapter.Interaction28Opcode,
            PublicBetaCompatibilityGameplayRequestWireAdapter.CombinationStepOpcode,
            PublicBetaCompatibilityGameplayRequestWireAdapter.PartySelectionOpcode,
            PublicBetaCompatibilityGameplayRequestWireAdapter.PartyRequest95Opcode,
            PublicBetaCompatibilityGameplayRequestWireAdapter.TeamRequestOpcode,
            PublicBetaCompatibilityGameplayRequestWireAdapter.GameplayDisconnectOpcode,
            PublicBetaCompatibilityGameplayRequestWireAdapter.AccountLoginOpcode,
            PublicBetaCompatibilityGameplayRequestWireAdapter.GameLoginOpcode,
            PublicBetaCompatibilityGameplayRequestWireAdapter.RouteOpcode,
            PublicBetaCompatibilityGameplayRequestWireAdapter.CombinationCommitOpcode,
            PublicBetaCompatibilityGameplayRequestWireAdapter.MailRecordActionOpcode,
            PublicBetaCompatibilityGameplayRequestWireAdapter.PartyRequest8FOpcode,
            PublicBetaCompatibilityGameplayRequestWireAdapter.PartyRequest90Opcode,
            PublicBetaCompatibilityGameplayRequestWireAdapter.PartyRequest91Opcode,
            PublicBetaCompatibilityGameplayRequestWireAdapter.PartyRequest92Opcode,
            PublicBetaCompatibilityGameplayRequestWireAdapter.PartyRequest94Opcode,
            PublicBetaCompatibilityGameplayRequestWireAdapter.PetEggRequestOpcode,
            PublicBetaCompatibilityGameplayRequestWireAdapter.FPetRequestOpcode,
            PublicBetaCompatibilityGameplayRequestWireAdapter.PkCursorTargetOpcode,
            PublicBetaCompatibilityGameplayRequestWireAdapter.PetPkTargetOpcode,
            PublicBetaCompatibilityGameplayRequestWireAdapter.VendorCartActionB2Opcode,
            PublicBetaCompatibilityGameplayRequestWireAdapter.VendorCartPublishOpcode,
            PublicBetaCompatibilityGameplayRequestWireAdapter.VendorCartAddRecordOpcode,
            PublicBetaCompatibilityGameplayRequestWireAdapter.CharacterDeleteRequestOpcode,
            PublicBetaCompatibilityGameplayRequestWireAdapter.CharacterSelectRequestOpcode,
            PublicBetaCompatibilityGameplayRequestWireAdapter.CharacterCreate1bRequestOpcode,
            PublicBetaCompatibilityGameplayRequestWireAdapter.SocialTextEnvelopeOpcode
        };

        foreach (var opcodeBase in variablePayloadOpcodes)
        {
            for (var lowByte = 0; lowByte <= 0xFF; lowByte++)
            {
                var opcode = (ushort)((opcodeBase << 8) | lowByte);
                packetRegistry.Register(new OfficialClientWorldCompatibilityNoOpPacketHandler(opcode));
            }
        }
    }

    private static PacketKnowledge BuildFallbackCompatibilityPacketKnowledge(string family, byte opcode, int decodedLength)
    {
        var opcodeHex = opcode.ToString("X2");
        var fallbackLength = Math.Max(decodedLength, 4);
        var decoded = new byte[fallbackLength];
        BinaryPrimitives.WriteUInt16LittleEndian(decoded, checked((ushort)fallbackLength));
        decoded[2] = opcode;
        decoded[^1] = OfficialLoginWireTransform.ComputeChecksum(decoded);
        var fixedPrefix = Convert.ToHexString(decoded.AsSpan(0, 3));

        return new PacketKnowledge(
            $"runtime-compat-official-c2s-0x{opcodeHex}-fallback",
            family,
            PacketDirection.ClientToServer,
            fallbackLength,
            opcodeHex,
            ProtocolConfidence.Recovered,
            PacketRecoveryStatus.EvidenceOnly,
            Recovered: true,
            Verified: false,
            new[] { new ProtocolEvidenceSource("runtime-compatibility-official", "FormalizationFallback", ProtocolConfidence.Recovered) },
            new[]
            {
                new ProtocolField(
                    "frameLength",
                    0,
                    2,
                    "uint16le",
                    PacketRecoveryStatus.Verified,
                    "Length-prefixed client-to-server frame."),
                new ProtocolField(
                    "payload",
                    2,
                    fallbackLength - 2,
                    "unknown",
                    PacketRecoveryStatus.NeedsRecovery,
                    "Compatibility payload; handler is currently no-op except for the verified gameplay disconnect request.")
            },
            Array.Empty<ProtocolField>(),
            Array.Empty<string>(),
            new[] { fixedPrefix });
    }

    private sealed class OfficialClientWorldCompatibilityNoOpPacketHandler : IPacketHandler
    {
        private readonly ushort _opcode;

        public OfficialClientWorldCompatibilityNoOpPacketHandler(ushort opcode)
        {
            _opcode = opcode;
        }

        public Opcode Opcode => new(_opcode);

        public ProtocolConfidence Confidence => ProtocolConfidence.Verified;

        public Task<OperationResult> HandleAsync(PacketEnvelope packet, CancellationToken cancellationToken)
        {
            var opcodeBase = checked((byte)(_opcode >> 8));
            if (opcodeBase == PublicBetaCompatibilityGameplayRequestWireAdapter.GameplayDisconnectOpcode)
            {
                return Task.FromResult(OperationResult.Failure(
                    "gameplay.disconnect_requested",
                    "The official client requested termination of the active world connection."));
            }

            return Task.FromResult(OperationResult.Success);
        }
    }
}
