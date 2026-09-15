namespace God2.ServerV2.Network;

public sealed class WorldMovementSequenceTracker
{
    private bool _hasAcceptedSequence;
    private byte _lastAcceptedSequence;

    public bool TryAccept(byte sequence)
    {
        if (!_hasAcceptedSequence)
        {
            _hasAcceptedSequence = true;
            _lastAcceptedSequence = sequence;
            return true;
        }

        var expected = unchecked((byte)(_lastAcceptedSequence + 1));
        if (sequence != expected)
        {
            return false;
        }

        _lastAcceptedSequence = sequence;
        return true;
    }

    public bool HasAcceptedSequence => _hasAcceptedSequence;

    public byte LastAcceptedSequence =>
        _hasAcceptedSequence
            ? _lastAcceptedSequence
            : throw new InvalidOperationException(
                "No movement sequence has been accepted.");
}
