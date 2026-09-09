using System.Threading.Channels;
using God2.ClassicServer.Application.Contracts;

namespace God2.ClassicServer.Runtime;

public interface IRuntimeCommandDispatcher
{
    ValueTask EnqueueAsync(
        Func<CancellationToken, ValueTask> command,
        CancellationToken cancellationToken);
}

public sealed class RuntimeCommandDispatcher : IRuntimeCommandDispatcher, IShutdownParticipant
{
    private sealed record RuntimeCommand(
        Func<CancellationToken, ValueTask> Execute,
        TaskCompletionSource Completion);

    private readonly Channel<RuntimeCommand> _commands;
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _processor;
    private int _stopping;

    public RuntimeCommandDispatcher(int capacity = 4_096)
    {
        _commands = Channel.CreateBounded<RuntimeCommand>(new BoundedChannelOptions(Math.Max(1, capacity))
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false
        });
        _processor = ProcessAsync();
    }

    public string Name => "Runtime Command Dispatcher";

    public bool IsStopping => Volatile.Read(ref _stopping) == 1;

    public async ValueTask EnqueueAsync(
        Func<CancellationToken, ValueTask> command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (IsStopping)
        {
            throw new InvalidOperationException("Runtime command dispatcher is stopping.");
        }

        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await _commands.Writer.WriteAsync(new RuntimeCommand(command, completion), cancellationToken);
        await completion.Task.WaitAsync(cancellationToken);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref _stopping, 1) == 0)
        {
            _commands.Writer.TryComplete();
        }

        try
        {
            await _processor.WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            _stop.Cancel();
            throw;
        }
    }

    private async Task ProcessAsync()
    {
        try
        {
            await foreach (var command in _commands.Reader.ReadAllAsync(_stop.Token))
            {
                try
                {
                    await command.Execute(_stop.Token);
                    command.Completion.TrySetResult();
                }
                catch (OperationCanceledException) when (_stop.IsCancellationRequested)
                {
                    command.Completion.TrySetCanceled(_stop.Token);
                }
                catch (Exception exception)
                {
                    command.Completion.TrySetException(exception);
                }
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested)
        {
        }
        finally
        {
            while (_commands.Reader.TryRead(out var pending))
            {
                pending.Completion.TrySetCanceled(_stop.Token);
            }
        }
    }
}
