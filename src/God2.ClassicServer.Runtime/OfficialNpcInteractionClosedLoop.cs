using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using God2.ClassicServer.Application.Common;
using God2.ClassicServer.Protocol;

namespace God2.ClassicServer.Runtime;

public enum OfficialNpcInteractionTransactionCode
{
    DialogOpened,
    DuplicateDialogOpened,
    MerchantOpened,
    DuplicateMerchantOpened,
    InteractionClosed,
    DialogOptionSelected,
    Rejected
}

public sealed record OfficialNpcInteractionReceipt(
    bool Recognized,
    OfficialNpcInteractionTransactionCode Code,
    string FailureCode,
    string SessionSafeId,
    long IngressOrdinal,
    ushort ClientEntityHandle,
    long? TargetRuntimeEntityId,
    int? TargetTemplateId,
    int? RuntimeMapId,
    string RequestSha256,
    string ResponseApplicationSha256,
    ReadOnlyMemory<byte> EncodedResponse,
    string Handler,
    bool RuntimeValidated,
    bool NetworkBytesEmitted);

public sealed record OfficialMerchantActiveContext(
    string SessionId,
    long AccountId,
    long CharacterId,
    ushort ClientEntityHandle,
    long RuntimeEntityId,
    int NpcTemplateId,
    int MerchantTemplateId,
    int RuntimeMapId);

/// <summary>
/// Production M4 NPC interaction path. It decodes the build-pinned C2S family, resolves the
/// official handle against the current authoritative MapRuntime, runs the common interaction
/// coordinator, then projects the exact S2C dialog/Merchant-service profile. Close is a
/// session-scoped state transition and emits no fabricated response. Inventory mutation is
/// deliberately outside this M4 slice.
/// </summary>
public sealed class OfficialNpcInteractionClosedLoop
{
    private readonly AuthoritativeWorldSessionInteractionTargetResolver _contexts;
    private readonly IWorldInteractionCoordinator _runtime;
    private readonly IProductionGameplayContentAuthority _contentAuthority;
    private readonly ConcurrentDictionary<string, ActiveInteraction> _activeInteractions = [];
    private readonly ConcurrentDictionary<string, byte> _positionUnsynchronizedSessions = [];

    public OfficialNpcInteractionClosedLoop(
        AuthoritativeWorldSessionInteractionTargetResolver contexts,
        IWorldInteractionCoordinator runtime,
        IProductionGameplayContentAuthority contentAuthority)
    {
        _contexts = contexts ?? throw new ArgumentNullException(nameof(contexts));
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _contentAuthority = contentAuthority ?? throw new ArgumentNullException(nameof(contentAuthority));
    }

    public static OfficialNpcInteractionClosedLoop Create(
        IWorldSessionCoordinator sessions,
        IProductionGameplayContentAuthority contentAuthority)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        ArgumentNullException.ThrowIfNull(contentAuthority);
        if (sessions.AuthorityKind != WorldContentAuthorityKind.MariaDb)
        {
            throw new InvalidOperationException("Production NPC interaction requires MariaDB world authority.");
        }

        var contexts = new AuthoritativeWorldSessionInteractionTargetResolver(sessions);
        var events = new InMemoryWorldInteractionEventSink();
        var audit = new InMemoryWorldInteractionAuditLedger();
        var handlers = new InteractionHandlerRegistry(
        [
            new DialogInteractionHandler(),
            new MerchantServiceOpenInteractionHandler(events)
        ]);
        var runtime = new WorldInteractionCoordinator(
            contexts,
            new RuntimeInteractionEligibilityPolicy(new OfficialClientNpcInteractionRangePolicy()),
            new NpcInteractionRouter(handlers, events),
            new InMemoryInteractionIdempotencyStore(),
            new InFlightInteractionCooldownStore(),
            events,
            audit);
        return new OfficialNpcInteractionClosedLoop(contexts, runtime, contentAuthority);
    }

    public static OfficialNpcInteractionClosedLoop CreateForTesting(IWorldSessionCoordinator sessions) =>
        Create(sessions, ReadyTestGameplayContentAuthority.Instance);

    public static bool RecognizesEncodedOpen(ReadOnlySpan<byte> encodedFrame)
    {
        if (encodedFrame.Length != OfficialNpcInteractionWireCodec.OpenFrameLength ||
            encodedFrame[0] != OfficialNpcInteractionWireCodec.OpenFrameLength ||
            encodedFrame[1] != 0)
        {
            return false;
        }

        var decoded = OfficialClientWorldProtocolFrames.DecodeWorldServerPayload(encodedFrame);
        try
        {
            return decoded[2] == OfficialNpcInteractionWireCodec.OpenOpcode;
        }
        finally
        {
            Array.Clear(decoded);
        }
    }

    public static bool RecognizesEncodedInteraction(ReadOnlySpan<byte> encodedFrame)
    {
        if (encodedFrame.Length != OfficialNpcInteractionWireCodec.OpenFrameLength ||
            encodedFrame[0] != OfficialNpcInteractionWireCodec.OpenFrameLength ||
            encodedFrame[1] != 0)
        {
            return false;
        }

        var decoded = OfficialClientWorldProtocolFrames.DecodeWorldServerPayload(encodedFrame);
        try
        {
            return decoded[2] is OfficialNpcInteractionWireCodec.OpenOpcode or
                OfficialNpcInteractionWireCodec.MerchantCloseOpcode;
        }
        finally
        {
            Array.Clear(decoded);
        }
    }

    public bool RecognizesEncodedDialogSelection(string sessionId, ReadOnlySpan<byte> encodedFrame)
    {
        if (string.IsNullOrWhiteSpace(sessionId) ||
            (encodedFrame.Length != OfficialNpcInteractionWireCodec.DialogSelectionFrameLength &&
             encodedFrame.Length != OfficialNpcInteractionWireCodec.CompoundDialogSelectionFrameLength) ||
            encodedFrame[0] != encodedFrame.Length ||
            encodedFrame[1] != 0 ||
            !_activeInteractions.TryGetValue(sessionId, out var active) ||
            !string.Equals(active.Handler, "Dialog", StringComparison.Ordinal))
        {
            return false;
        }

        var decoded = OfficialClientWorldProtocolFrames.DecodeWorldServerPayload(encodedFrame);
        try
        {
            return decoded[2] == OfficialNpcInteractionWireCodec.DialogSelectionOpcode ||
                (decoded.Length == OfficialNpcInteractionWireCodec.CompoundDialogSelectionFrameLength &&
                 decoded[2] == OfficialNpcInteractionWireCodec.DialogOrQuestCloseOpcode &&
                 decoded[7] == OfficialNpcInteractionWireCodec.DialogSelectionOpcode);
        }
        finally
        {
            Array.Clear(decoded);
        }
    }

    public void MarkPositionUnsynchronized(string sessionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        _positionUnsynchronizedSessions[sessionId] = 0;
    }

    public void MarkPositionSynchronized(string sessionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        _positionUnsynchronizedSessions.TryRemove(sessionId, out _);
    }

    public void RemoveSession(string sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return;
        }

        _activeInteractions.TryRemove(sessionId, out _);
        _positionUnsynchronizedSessions.TryRemove(sessionId, out _);
    }

    public async Task<OfficialNpcInteractionReceipt> ExecuteFrameAsync(
        string sessionId,
        long ingressOrdinal,
        string correlationId,
        ReadOnlyMemory<byte> encodedFrame,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(correlationId);
        if (ingressOrdinal < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(ingressOrdinal));
        }

        var requestHash = Convert.ToHexString(SHA256.HashData(encodedFrame.Span));
        if (encodedFrame.Length < 3)
        {
            return Failure(
                sessionId,
                ingressOrdinal,
                requestHash,
                "wire.npc_interaction.open_length_invalid");
        }

        // The current exact-build NPC capture enters the common 0x78D70/0x78F40
        // session cipher, not the legacy Portal XOR envelope. The fixed production
        // world handshake establishes the same build-pinned world cipher profile used
        // for S2C replication, so this direction must use that reversible transform too.
        var decodedFrame = OfficialClientWorldProtocolFrames.DecodeWorldServerPayload(encodedFrame.Span);
        try
        {
            requestHash = Convert.ToHexString(SHA256.HashData(decodedFrame));
            var compoundDialogSelection =
                decodedFrame.Length == OfficialNpcInteractionWireCodec.CompoundDialogSelectionFrameLength &&
                decodedFrame[2] == OfficialNpcInteractionWireCodec.DialogOrQuestCloseOpcode &&
                decodedFrame[7] == OfficialNpcInteractionWireCodec.DialogSelectionOpcode;
            if (compoundDialogSelection)
            {
                return ExecuteDialogSelection(sessionId, ingressOrdinal, requestHash, decodedFrame);
            }

            if (decodedFrame[2] == OfficialNpcInteractionWireCodec.DialogOrQuestCloseOpcode)
            {
                return Failure(
                    sessionId,
                    ingressOrdinal,
                    requestHash,
                    "wire.npc_interaction.dialog_close_evidence_blocked");
            }

            if (decodedFrame[2] == OfficialNpcInteractionWireCodec.MerchantCloseOpcode)
            {
                return ExecuteClose(sessionId, ingressOrdinal, requestHash, decodedFrame);
            }

            if (decodedFrame[2] == OfficialNpcInteractionWireCodec.DialogSelectionOpcode)
            {
                return ExecuteDialogSelection(sessionId, ingressOrdinal, requestHash, decodedFrame);
            }

            var contentReady = _contentAuthority.RequireReady();
            if (!contentReady.Succeeded)
            {
                return Failure(
                    sessionId,
                    ingressOrdinal,
                    requestHash,
                    contentReady.Error.Code);
            }

            if (_positionUnsynchronizedSessions.ContainsKey(sessionId))
            {
                return Failure(
                    sessionId,
                    ingressOrdinal,
                    requestHash,
                    "interaction.position_unsynchronized");
            }

            var decoded = OfficialNpcInteractionWireCodec.DecodeOpen(
                OfficialNpcInteractionWireCodec.ClientBuildId,
                GameplayProtocolState.World,
                decodedFrame);
            if (!decoded.Succeeded || decoded.Value is null)
            {
                return Failure(sessionId, ingressOrdinal, requestHash, decoded.FailureCode);
            }

            var wire = decoded.Value;
            var dialogPreflight = OfficialNpcInteractionWireCodec.SerializeDialogOpen(
                OfficialNpcInteractionWireCodec.ClientBuildId,
                wire.ClientEntityHandle);

            var context = _contexts.ResolveClientNpc(sessionId, wire.ClientEntityHandle);
            if (!context.Succeeded || context.Value is null)
            {
                return Failure(
                    sessionId,
                    ingressOrdinal,
                    requestHash,
                    dialogPreflight.Succeeded
                        ? context.Error.Code
                        : dialogPreflight.FailureCode,
                    wire.ClientEntityHandle);
            }

            var resolved = context.Value;
            var expectedHandler = resolved.Npc.State.MerchantId is null ? "Dialog" : "Merchant";
            ReadOnlyMemory<byte> preflightFrame;
            string preflightHash;
            if (dialogPreflight.Succeeded && dialogPreflight.Value is not null)
            {
                preflightFrame = dialogPreflight.Value.DecodedFrame;
                preflightHash = dialogPreflight.Value.ApplicationSha256;
            }
            else if (string.Equals(expectedHandler, "Merchant", StringComparison.Ordinal))
            {
                var merchantPreflight = OfficialMerchantTransactionWireCodec.SerializeObservedDialogOpen(
                    OfficialMerchantTransactionWireCodec.ClientBuildId,
                    wire.ClientEntityHandle);
                if (!merchantPreflight.Succeeded || merchantPreflight.Value is null)
                {
                    return Failure(
                        sessionId,
                        ingressOrdinal,
                        requestHash,
                        merchantPreflight.FailureCode,
                        wire.ClientEntityHandle);
                }

                preflightFrame = merchantPreflight.Value.DecodedFrame;
                preflightHash = merchantPreflight.Value.DecodedFrameSha256;
            }
            else
            {
                return Failure(
                    sessionId,
                    ingressOrdinal,
                    requestHash,
                    dialogPreflight.FailureCode,
                    wire.ClientEntityHandle);
            }
            var proposedActive = new ActiveInteraction(
                wire.ClientEntityHandle,
                resolved.Npc.Identity.RuntimeObjectId,
                resolved.Npc.Identity.TemplateId,
                resolved.Binding.MapSession.MapId,
                expectedHandler,
                requestHash);
            var activeAdded = _activeInteractions.TryAdd(sessionId, proposedActive);
            if (!activeAdded &&
                (!_activeInteractions.TryGetValue(sessionId, out var currentActive) ||
                 currentActive.ClientEntityHandle != proposedActive.ClientEntityHandle ||
                 !string.Equals(currentActive.OpenRequestSha256, requestHash, StringComparison.Ordinal)))
            {
                return Failure(
                    sessionId,
                    ingressOrdinal,
                    requestHash,
                    "interaction.already_open",
                    wire.ClientEntityHandle,
                    resolved.Npc.Identity.RuntimeObjectId,
                    resolved.Npc.Identity.TemplateId,
                    resolved.Binding.MapSession.MapId,
                    expectedHandler);
            }

            // C2S 0x37 carries no sequence field. A session plus exact decoded request hash is
            // therefore the strongest evidence-backed replay identity. Repeated opens are
            // read-only and safely return the same projection; they do not duplicate mutation.
            var idempotencyKey = $"official-npc-dialog:{SafeId(sessionId)}:{requestHash}";
            var command = new WorldInteractionRequest(
                DeterministicGuid($"{idempotencyKey}|{requestHash}"),
                idempotencyKey,
                sessionId,
                resolved.Binding.Character.CharacterId,
                resolved.Player.Identity.RuntimeObjectId,
                resolved.Npc.Identity.RuntimeObjectId,
                resolved.Npc.Identity.TemplateId,
                expectedHandler == "Merchant"
                    ? RequestedInteractionType.Merchant
                    : RequestedInteractionType.Dialog,
                0,
                ingressOrdinal,
                "OfficialNpcInteractionM4",
                DateTimeOffset.UtcNow,
                correlationId);
            WorldInteractionResult result;
            try
            {
                result = await _runtime.ExecuteAsync(command, cancellationToken);
            }
            catch
            {
                RemoveProposedActive(sessionId, proposedActive, activeAdded);
                throw;
            }

            if (!result.Succeeded || !string.Equals(result.Handler, expectedHandler, StringComparison.Ordinal))
            {
                RemoveProposedActive(sessionId, proposedActive, activeAdded);

                return Failure(
                    sessionId,
                    ingressOrdinal,
                    requestHash,
                    string.IsNullOrWhiteSpace(result.FailureCode)
                        ? "wire.npc_interaction.runtime_rejected"
                        : result.FailureCode,
                    wire.ClientEntityHandle,
                    resolved.Npc.Identity.RuntimeObjectId,
                    resolved.Npc.Identity.TemplateId,
                    resolved.Binding.MapSession.MapId,
                    result.Handler);
            }

            ReadOnlyMemory<byte> projectedFrame;
            string projectionHash;
            if (dialogPreflight.Succeeded && dialogPreflight.Value is not null)
            {
                var projection = OfficialNpcInteractionWireCodec.SerializeDialogOpen(
                    OfficialNpcInteractionWireCodec.ClientBuildId,
                    wire.ClientEntityHandle);
                if (!projection.Succeeded || projection.Value is null)
                {
                    RemoveProposedActive(sessionId, proposedActive, activeAdded);
                    return Failure(sessionId, ingressOrdinal, requestHash,
                        "wire.npc_interaction.serializer_preflight_diverged", wire.ClientEntityHandle);
                }

                projectedFrame = projection.Value.DecodedFrame;
                projectionHash = projection.Value.ApplicationSha256;
            }
            else
            {
                var projection = OfficialMerchantTransactionWireCodec.SerializeObservedDialogOpen(
                    OfficialMerchantTransactionWireCodec.ClientBuildId,
                    wire.ClientEntityHandle);
                if (!projection.Succeeded || projection.Value is null)
                {
                    RemoveProposedActive(sessionId, proposedActive, activeAdded);
                    return Failure(sessionId, ingressOrdinal, requestHash,
                        "wire.npc_interaction.serializer_preflight_diverged", wire.ClientEntityHandle);
                }

                projectedFrame = projection.Value.DecodedFrame;
                projectionHash = projection.Value.DecodedFrameSha256;
            }

            if (!projectedFrame.Span.SequenceEqual(preflightFrame.Span) ||
                !string.Equals(projectionHash, preflightHash, StringComparison.Ordinal))
            {
                RemoveProposedActive(sessionId, proposedActive, activeAdded);
                return Failure(
                    sessionId,
                    ingressOrdinal,
                    requestHash,
                    "wire.npc_interaction.serializer_preflight_diverged",
                    wire.ClientEntityHandle,
                    resolved.Npc.Identity.RuntimeObjectId,
                    resolved.Npc.Identity.TemplateId,
                    resolved.Binding.MapSession.MapId,
                    result.Handler);
            }

            var encoded = OfficialClientWorldProtocolFrames.EncodeWorldServerPayload(projectedFrame.Span);
            return new OfficialNpcInteractionReceipt(
                true,
                expectedHandler == "Merchant"
                    ? result.IsDuplicate
                        ? OfficialNpcInteractionTransactionCode.DuplicateMerchantOpened
                        : OfficialNpcInteractionTransactionCode.MerchantOpened
                    : result.IsDuplicate
                        ? OfficialNpcInteractionTransactionCode.DuplicateDialogOpened
                        : OfficialNpcInteractionTransactionCode.DialogOpened,
                string.Empty,
                SafeId(sessionId),
                ingressOrdinal,
                wire.ClientEntityHandle,
                resolved.Npc.Identity.RuntimeObjectId,
                resolved.Npc.Identity.TemplateId,
                resolved.Binding.MapSession.MapId,
                requestHash,
                projectionHash,
                encoded,
                result.Handler,
                RuntimeValidated: true,
                NetworkBytesEmitted: false);
        }
        finally
        {
            Array.Clear(decodedFrame);
        }
    }

    public OperationResult<OfficialMerchantActiveContext> ResolveActiveMerchant(
        string sessionId,
        ushort clientEntityHandle)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        if (!_activeInteractions.TryGetValue(sessionId, out var active) ||
            active.ClientEntityHandle != clientEntityHandle ||
            !string.Equals(active.Handler, "Merchant", StringComparison.Ordinal))
        {
            return OperationResult<OfficialMerchantActiveContext>.Failure(
                "interaction.merchant_not_active",
                "The session has no matching active Merchant interaction.");
        }

        var current = _contexts.ResolveClientNpc(sessionId, clientEntityHandle);
        if (!current.Succeeded || current.Value is null)
        {
            return OperationResult<OfficialMerchantActiveContext>.Failure(
                current.Error.Code,
                current.Error.Message,
                current.Error.Source);
        }

        var resolved = current.Value;
        if (resolved.Npc.Identity.RuntimeObjectId != active.RuntimeEntityId ||
            resolved.Npc.Identity.TemplateId != active.TemplateId ||
            resolved.Binding.MapSession.MapId != active.MapId ||
            resolved.Npc.State.MerchantId is not int merchantTemplateId ||
            resolved.Binding.Session.AccountId is not > 0)
        {
            return OperationResult<OfficialMerchantActiveContext>.Failure(
                "interaction.merchant_authority_changed",
                "The active Merchant no longer matches authoritative world or session state.");
        }

        return OperationResult<OfficialMerchantActiveContext>.Success(
            new OfficialMerchantActiveContext(
                sessionId,
                resolved.Binding.Session.AccountId.Value,
                resolved.Binding.Character.CharacterId,
                clientEntityHandle,
                resolved.Npc.Identity.RuntimeObjectId,
                resolved.Npc.Identity.TemplateId,
                merchantTemplateId,
                resolved.Binding.MapSession.MapId));
    }

    private sealed class ReadyTestGameplayContentAuthority : IProductionGameplayContentAuthority
    {
        public static ReadyTestGameplayContentAuthority Instance { get; } = new();

        public string ActiveReleaseId => "test-only";

        public bool IsReady => true;

        public OperationResult RequireReady() => OperationResult.Success;

        public OperationResult<ProductionQuestContent> ResolveQuest(int questId) =>
            OperationResult<ProductionQuestContent>.Failure("test.content.not_configured", "No test Quest content was configured.");

        public OperationResult<ProductionEquipmentSetContent> ResolveEquipmentSet(int setId) =>
            OperationResult<ProductionEquipmentSetContent>.Failure("test.content.not_configured", "No test Equipment content was configured.");

        public OperationResult<ProductionPetInnateContent> ResolvePetInnate(int innateId) =>
            OperationResult<ProductionPetInnateContent>.Failure("test.content.not_configured", "No test Pet content was configured.");
    }

    private OfficialNpcInteractionReceipt ExecuteClose(
        string sessionId,
        long ingressOrdinal,
        string requestHash,
        ReadOnlySpan<byte> decodedFrame)
    {
        var decoded = OfficialNpcInteractionWireCodec.DecodeClose(
            OfficialNpcInteractionWireCodec.ClientBuildId,
            GameplayProtocolState.World,
            decodedFrame);
        if (!decoded.Succeeded || decoded.Value is null)
        {
            return Failure(sessionId, ingressOrdinal, requestHash, decoded.FailureCode);
        }

        var wire = decoded.Value;
        if (!_activeInteractions.TryGetValue(sessionId, out var active))
        {
            return Failure(
                sessionId,
                ingressOrdinal,
                requestHash,
                "interaction.close_without_active_state",
                wire.ClientEntityHandle);
        }

        var expectedKind = string.Equals(active.Handler, "Merchant", StringComparison.Ordinal)
            ? OfficialNpcInteractionCloseKind.Merchant
            : OfficialNpcInteractionCloseKind.DialogOrQuest;
        if (active.ClientEntityHandle != wire.ClientEntityHandle || expectedKind != wire.Kind)
        {
            return Failure(
                sessionId,
                ingressOrdinal,
                requestHash,
                "interaction.close_state_mismatch",
                wire.ClientEntityHandle,
                active.RuntimeEntityId,
                active.TemplateId,
                active.MapId,
                active.Handler);
        }

        var current = _contexts.ResolveClientNpc(sessionId, wire.ClientEntityHandle);
        if (!current.Succeeded || current.Value is null ||
            current.Value.Npc.Identity.RuntimeObjectId != active.RuntimeEntityId ||
            current.Value.Npc.Identity.TemplateId != active.TemplateId ||
            current.Value.Binding.MapSession.MapId != active.MapId)
        {
            return Failure(
                sessionId,
                ingressOrdinal,
                requestHash,
                current.Succeeded ? "interaction.close_authority_changed" : current.Error.Code,
                wire.ClientEntityHandle,
                active.RuntimeEntityId,
                active.TemplateId,
                active.MapId,
                active.Handler);
        }

        if (!((ICollection<KeyValuePair<string, ActiveInteraction>>)_activeInteractions)
            .Remove(new KeyValuePair<string, ActiveInteraction>(sessionId, active)))
        {
            return Failure(
                sessionId,
                ingressOrdinal,
                requestHash,
                "interaction.close_race_rejected",
                wire.ClientEntityHandle,
                active.RuntimeEntityId,
                active.TemplateId,
                active.MapId,
                active.Handler);
        }

        return new OfficialNpcInteractionReceipt(
            true,
            OfficialNpcInteractionTransactionCode.InteractionClosed,
            string.Empty,
            SafeId(sessionId),
            ingressOrdinal,
            wire.ClientEntityHandle,
            active.RuntimeEntityId,
            active.TemplateId,
            active.MapId,
            requestHash,
            string.Empty,
            ReadOnlyMemory<byte>.Empty,
            active.Handler,
            RuntimeValidated: true,
            NetworkBytesEmitted: false);
    }

    private OfficialNpcInteractionReceipt ExecuteDialogSelection(
        string sessionId,
        long ingressOrdinal,
        string requestHash,
        ReadOnlySpan<byte> decodedFrame)
    {
        var decoded = OfficialNpcInteractionWireCodec.DecodeDialogSelection(
            OfficialNpcInteractionWireCodec.ClientBuildId,
            GameplayProtocolState.World,
            decodedFrame);
        if (!decoded.Succeeded || decoded.Value is null)
        {
            return Failure(sessionId, ingressOrdinal, requestHash, decoded.FailureCode);
        }

        var wire = decoded.Value;
        if (!_activeInteractions.TryGetValue(sessionId, out var active))
        {
            return Failure(
                sessionId,
                ingressOrdinal,
                requestHash,
                "interaction.dialog_selection_without_active_state",
                wire.ClientEntityHandle);
        }

        if (active.ClientEntityHandle != wire.ClientEntityHandle ||
            !string.Equals(active.Handler, "Dialog", StringComparison.Ordinal))
        {
            return Failure(
                sessionId,
                ingressOrdinal,
                requestHash,
                "interaction.dialog_selection_state_mismatch",
                wire.ClientEntityHandle,
                active.RuntimeEntityId,
                active.TemplateId,
                active.MapId,
                active.Handler);
        }

        var current = _contexts.ResolveClientNpc(sessionId, wire.ClientEntityHandle);
        if (!current.Succeeded || current.Value is null ||
            current.Value.Npc.Identity.RuntimeObjectId != active.RuntimeEntityId ||
            current.Value.Npc.Identity.TemplateId != active.TemplateId ||
            current.Value.Binding.MapSession.MapId != active.MapId)
        {
            return Failure(
                sessionId,
                ingressOrdinal,
                requestHash,
                current.Succeeded ? "interaction.dialog_selection_authority_changed" : current.Error.Code,
                wire.ClientEntityHandle,
                active.RuntimeEntityId,
                active.TemplateId,
                active.MapId,
                active.Handler);
        }

        if (!((ICollection<KeyValuePair<string, ActiveInteraction>>)_activeInteractions)
            .Remove(new KeyValuePair<string, ActiveInteraction>(sessionId, active)))
        {
            return Failure(
                sessionId,
                ingressOrdinal,
                requestHash,
                "interaction.dialog_selection_race_rejected",
                wire.ClientEntityHandle,
                active.RuntimeEntityId,
                active.TemplateId,
                active.MapId,
                active.Handler);
        }

        return new OfficialNpcInteractionReceipt(
            true,
            OfficialNpcInteractionTransactionCode.DialogOptionSelected,
            string.Empty,
            SafeId(sessionId),
            ingressOrdinal,
            wire.ClientEntityHandle,
            active.RuntimeEntityId,
            active.TemplateId,
            active.MapId,
            requestHash,
            string.Empty,
            ReadOnlyMemory<byte>.Empty,
            active.Handler,
            RuntimeValidated: true,
            NetworkBytesEmitted: false);
    }

    private static OfficialNpcInteractionReceipt Failure(
        string sessionId,
        long ingressOrdinal,
        string requestHash,
        string failureCode,
        ushort clientEntityHandle = 0,
        long? runtimeEntityId = null,
        int? templateId = null,
        int? mapId = null,
        string handler = "") =>
        new(
            true,
            OfficialNpcInteractionTransactionCode.Rejected,
            failureCode,
            SafeId(sessionId),
            ingressOrdinal,
            clientEntityHandle,
            runtimeEntityId,
            templateId,
            mapId,
            requestHash,
            string.Empty,
            ReadOnlyMemory<byte>.Empty,
            handler,
            RuntimeValidated: false,
            NetworkBytesEmitted: false);

    private static Guid DeterministicGuid(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return new Guid(bytes.AsSpan(0, 16));
    }

    private static string SafeId(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private void RemoveProposedActive(
        string sessionId,
        ActiveInteraction proposedActive,
        bool activeAdded)
    {
        if (activeAdded)
        {
            ((ICollection<KeyValuePair<string, ActiveInteraction>>)_activeInteractions)
                .Remove(new KeyValuePair<string, ActiveInteraction>(sessionId, proposedActive));
        }
    }

    private sealed record ActiveInteraction(
        ushort ClientEntityHandle,
        long RuntimeEntityId,
        int TemplateId,
        int MapId,
        string Handler,
        string OpenRequestSha256);
}
