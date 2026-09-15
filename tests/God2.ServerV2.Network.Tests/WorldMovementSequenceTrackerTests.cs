using God2.ServerV2.Network;

namespace God2.ServerV2.Network.Tests;

public sealed class WorldMovementSequenceTrackerTests
{
    [Fact]
    public void FirstSequence_IsAccepted()
    {
        var tracker = new WorldMovementSequenceTracker();

        Assert.True(tracker.TryAccept(17));
        Assert.True(tracker.HasAcceptedSequence);
        Assert.Equal(17, tracker.LastAcceptedSequence);
    }

    [Fact]
    public void ConsecutiveSequences_AreAccepted()
    {
        var tracker = new WorldMovementSequenceTracker();

        Assert.True(tracker.TryAccept(1));
        Assert.True(tracker.TryAccept(2));
        Assert.True(tracker.TryAccept(3));
        Assert.Equal(3, tracker.LastAcceptedSequence);
    }

    [Fact]
    public void DuplicateSequence_IsRejectedWithoutAdvancing()
    {
        var tracker = new WorldMovementSequenceTracker();

        Assert.True(tracker.TryAccept(1));
        Assert.False(tracker.TryAccept(1));
        Assert.Equal(1, tracker.LastAcceptedSequence);
        Assert.True(tracker.TryAccept(2));
    }

    [Fact]
    public void BackwardOrSkippedSequence_IsRejected()
    {
        var tracker = new WorldMovementSequenceTracker();

        Assert.True(tracker.TryAccept(10));
        Assert.False(tracker.TryAccept(9));
        Assert.False(tracker.TryAccept(12));
        Assert.Equal(10, tracker.LastAcceptedSequence);
        Assert.True(tracker.TryAccept(11));
    }

    [Fact]
    public void SequenceWrapsFrom255ToZero()
    {
        var tracker = new WorldMovementSequenceTracker();

        Assert.True(tracker.TryAccept(byte.MaxValue));
        Assert.True(tracker.TryAccept(0));
        Assert.Equal(0, tracker.LastAcceptedSequence);
    }

    [Fact]
    public void LastSequenceBeforeFirstAcceptance_Throws()
    {
        var tracker = new WorldMovementSequenceTracker();

        Assert.Throws<InvalidOperationException>(
            () => _ = tracker.LastAcceptedSequence);
    }
}
