namespace God2.ClassicServer.Protocol;

public interface IOfficialLiveMovementEvidenceSink
{
    ValueTask RecordAsync(
        OfficialLiveMovementEvidenceRecord record,
        CancellationToken cancellationToken = default);
}

public sealed class NullOfficialLiveMovementEvidenceSink : IOfficialLiveMovementEvidenceSink
{
    public static NullOfficialLiveMovementEvidenceSink Instance { get; } = new();

    private NullOfficialLiveMovementEvidenceSink()
    {
    }

    public ValueTask RecordAsync(
        OfficialLiveMovementEvidenceRecord record,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.CompletedTask;
    }
}

public sealed class InMemoryOfficialLiveMovementEvidenceSink : IOfficialLiveMovementEvidenceSink
{
    private readonly object _gate = new();
    private readonly List<OfficialLiveMovementEvidenceRecord> _records = [];

    public IReadOnlyList<OfficialLiveMovementEvidenceRecord> Records
    {
        get
        {
            lock (_gate)
            {
                return _records.ToArray();
            }
        }
    }

    public ValueTask RecordAsync(
        OfficialLiveMovementEvidenceRecord record,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            _records.Add(record);
        }

        return ValueTask.CompletedTask;
    }
}
