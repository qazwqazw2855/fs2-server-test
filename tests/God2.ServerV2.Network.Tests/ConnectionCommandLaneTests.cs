using God2.ServerV2.Network;

namespace God2.ServerV2.Network.Tests;

public sealed class ConnectionCommandLaneTests
{
    [Fact]
    public async Task Active_operation_finishes_before_next_operation()
    {
        var lane = new ConnectionCommandLane();
        var entered = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var order = new List<int>();

        var first = lane.RunAsync<int>(async _ =>
        {
            order.Add(1);
            entered.SetResult(true);
            await release.Task;
            order.Add(2);
            return 10;
        }, CancellationToken.None).AsTask();

        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var second = lane.RunAsync<int>(_ =>
        {
            order.Add(3);
            return ValueTask.FromResult(20);
        }, CancellationToken.None).AsTask();

        Assert.False(second.IsCompleted);
        release.SetResult(true);
        Assert.Equal(10, await first.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(20, await second.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(new[] { 1, 2, 3 }, order);
    }

    [Fact]
    public async Task Closing_rejects_queued_commands_and_waits_before_cleanup()
    {
        var lane = new ConnectionCommandLane();
        var entered = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var cleanupCalls = 0;
        var queuedCalls = 0;

        var active = lane.RunAsync<int>(async _ =>
        {
            entered.SetResult(true);
            await release.Task;
            return 1;
        }, CancellationToken.None).AsTask();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var queued = lane.RunAsync<int>(_ =>
        {
            queuedCalls++;
            return ValueTask.FromResult(2);
        }, CancellationToken.None).AsTask();

        var close = lane.CloseAsync(() =>
        {
            cleanupCalls++;
            return ValueTask.CompletedTask;
        });
        Assert.True(lane.IsClosing);
        Assert.False(close.IsCompleted);
        Assert.Equal(0, cleanupCalls);

        var repeatedClose = lane.CloseAsync(() =>
            throw new InvalidOperationException("must not run twice"));
        Assert.Same(close, repeatedClose);

        release.SetResult(true);
        Assert.Equal(1, await active.WaitAsync(TimeSpan.FromSeconds(5)));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            queued.WaitAsync(TimeSpan.FromSeconds(5)));
        await close.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(0, queuedCalls);
        Assert.Equal(1, cleanupCalls);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            lane.RunAsync<int>(_ => ValueTask.FromResult(3),
                CancellationToken.None).AsTask());
    }

    [Fact]
    public async Task Failed_operation_releases_lane_and_failed_cleanup_stays_closed()
    {
        var lane = new ConnectionCommandLane();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            lane.RunAsync<int>(_ =>
                ValueTask.FromException<int>(
                    new InvalidOperationException("operation failed")),
                CancellationToken.None).AsTask());

        Assert.Equal(2, await lane.RunAsync<int>(
            _ => ValueTask.FromResult(2), CancellationToken.None));

        var error = new InvalidOperationException("cleanup failed");
        var close = lane.CloseAsync(() => ValueTask.FromException(error));
        var observed = await Assert.ThrowsAsync<InvalidOperationException>(() => close);
        Assert.Same(error, observed);
        Assert.True(lane.IsClosing);
        Assert.Same(close, lane.CloseAsync(() => ValueTask.CompletedTask));
    }
}
