namespace God2.ClassicServer.Protocol;

public sealed record OfficialLiveMovementEvidenceContext(
    string RunId,
    string MarkerName,
    long ObservedAtUnixMs,
    string SourceFrameId,
    GameplayProtocolState State = GameplayProtocolState.World,
    string? PairingKey = null);

public sealed record OfficialLiveMovementEvidenceRecorderResult(
    int RecordCount,
    bool Parsed,
    string Status,
    string FailureCode);

public sealed class OfficialLiveMovementEvidenceRecorder
{
    private readonly IOfficialLiveMovementEvidenceSink _sink;

    public OfficialLiveMovementEvidenceRecorder(IOfficialLiveMovementEvidenceSink sink)
    {
        _sink = sink ?? NullOfficialLiveMovementEvidenceSink.Instance;
    }

    public async ValueTask<OfficialLiveMovementEvidenceRecorderResult> RecordDecodedFrameAsync(
        PacketDirection direction,
        ReadOnlyMemory<byte> decodedFrame,
        OfficialLiveMovementEvidenceContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        if (decodedFrame.Length < 3)
        {
            await _sink.RecordAsync(
                OfficialLiveMovementEvidenceRecordFactory.CreateParserFailure(
                    direction,
                    0,
                    decodedFrame.Span,
                    context.RunId,
                    context.MarkerName,
                    context.ObservedAtUnixMs,
                    context.SourceFrameId,
                    "DecodedFrameTooShort;EvidenceBlocked"),
                cancellationToken);
            return new OfficialLiveMovementEvidenceRecorderResult(1, Parsed: false, "EvidenceBlocked", "wire.live.frame_too_short");
        }

        return direction switch
        {
            PacketDirection.ClientToServer => await RecordClientFrameAsync(decodedFrame, context, cancellationToken),
            PacketDirection.ServerToClient => await RecordServerFrameAsync(decodedFrame, context, cancellationToken),
            _ => await RecordUnknownDirectionAsync(direction, decodedFrame, context, cancellationToken)
        };
    }

    private async ValueTask<OfficialLiveMovementEvidenceRecorderResult> RecordClientFrameAsync(
        ReadOnlyMemory<byte> decodedFrame,
        OfficialLiveMovementEvidenceContext context,
        CancellationToken cancellationToken)
    {
        var opcode = decodedFrame.Span[2];
        if (opcode == OfficialLiveMovementWorldBatchWireCodec.ClientMovementOpcode)
        {
            var movement = OfficialLiveMovementWorldBatchWireCodec.DecodeClientMovementLayout(decodedFrame.Span);
            if (movement.LayoutDecoded && movement.Value is not null)
            {
                await _sink.RecordAsync(
                    OfficialLiveMovementWorldBatchWireCodec.CreateClientMovementEvidenceRecord(
                        movement.Value,
                        context.RunId,
                        context.MarkerName,
                        context.ObservedAtUnixMs,
                        context.SourceFrameId,
                        context.PairingKey),
                    cancellationToken);
                return new OfficialLiveMovementEvidenceRecorderResult(1, Parsed: true, "Recorded", string.Empty);
            }

            return await RecordClientFailureAsync(opcode, decodedFrame, context, movement.FailureCode, cancellationToken);
        }

        var sideband = OfficialLiveMovementWorldBatchWireCodec.DecodeClientMovementSidebandLayout(decodedFrame.Span);
        if (sideband.LayoutDecoded && sideband.Value is not null)
        {
            await _sink.RecordAsync(
                OfficialLiveMovementWorldBatchWireCodec.CreateClientSidebandEvidenceRecord(
                    sideband.Value,
                    context.RunId,
                    context.MarkerName,
                    context.ObservedAtUnixMs,
                    context.SourceFrameId,
                    context.PairingKey),
                cancellationToken);
            return new OfficialLiveMovementEvidenceRecorderResult(1, Parsed: true, "Recorded", string.Empty);
        }

        return await RecordClientFailureAsync(opcode, decodedFrame, context, sideband.FailureCode, cancellationToken);
    }

    private async ValueTask<OfficialLiveMovementEvidenceRecorderResult> RecordServerFrameAsync(
        ReadOnlyMemory<byte> decodedFrame,
        OfficialLiveMovementEvidenceContext context,
        CancellationToken cancellationToken)
    {
        var batch = OfficialLiveMovementWorldBatchWireCodec.DecodeServerWorldApplicationBatchLayout(decodedFrame.Span);
        if (batch.LayoutDecoded && batch.Value is not null)
        {
            var records = OfficialLiveMovementWorldBatchWireCodec.CreateServerChildEvidenceRecords(
                batch.Value,
                context.RunId,
                context.MarkerName,
                context.ObservedAtUnixMs,
                context.SourceFrameId);
            foreach (var record in records)
            {
                await _sink.RecordAsync(record, cancellationToken);
            }

            return new OfficialLiveMovementEvidenceRecorderResult(records.Count, Parsed: true, "Recorded", string.Empty);
        }

        await _sink.RecordAsync(
            OfficialLiveMovementEvidenceRecordFactory.CreateParserFailure(
                PacketDirection.ServerToClient,
                decodedFrame.Span[2],
                decodedFrame.Span,
                context.RunId,
                context.MarkerName,
                context.ObservedAtUnixMs,
                context.SourceFrameId,
                batch.FailureCode),
            cancellationToken);
        return new OfficialLiveMovementEvidenceRecorderResult(1, Parsed: false, "EvidenceBlocked", batch.FailureCode);
    }

    private async ValueTask<OfficialLiveMovementEvidenceRecorderResult> RecordClientFailureAsync(
        byte opcode,
        ReadOnlyMemory<byte> decodedFrame,
        OfficialLiveMovementEvidenceContext context,
        string failureCode,
        CancellationToken cancellationToken)
    {
        await _sink.RecordAsync(
            OfficialLiveMovementEvidenceRecordFactory.CreateParserFailure(
                PacketDirection.ClientToServer,
                opcode,
                decodedFrame.Span,
                context.RunId,
                context.MarkerName,
                context.ObservedAtUnixMs,
                context.SourceFrameId,
                failureCode),
            cancellationToken);
        return new OfficialLiveMovementEvidenceRecorderResult(1, Parsed: false, "EvidenceBlocked", failureCode);
    }

    private async ValueTask<OfficialLiveMovementEvidenceRecorderResult> RecordUnknownDirectionAsync(
        PacketDirection direction,
        ReadOnlyMemory<byte> decodedFrame,
        OfficialLiveMovementEvidenceContext context,
        CancellationToken cancellationToken)
    {
        await _sink.RecordAsync(
            OfficialLiveMovementEvidenceRecordFactory.CreateParserFailure(
                direction,
                decodedFrame.Span[2],
                decodedFrame.Span,
                context.RunId,
                context.MarkerName,
                context.ObservedAtUnixMs,
                context.SourceFrameId,
                "DirectionUnknown;EvidenceBlocked"),
            cancellationToken);
        return new OfficialLiveMovementEvidenceRecorderResult(1, Parsed: false, "EvidenceBlocked", "wire.live.direction_unknown");
    }
}
