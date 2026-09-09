using God2.ClassicServer.Application.Contracts;

namespace God2.ClassicServer.Application.Shutdown;

public sealed class ShutdownCoordinator
{
    private readonly object _gate = new();
    private readonly IReadOnlyList<IShutdownParticipant> _participants;
    private readonly List<ShutdownFailure> _failures = [];
    private TaskCompletionSource? _completion;
    private int _executionCount;

    public ShutdownCoordinator(IEnumerable<IShutdownParticipant> participants)
    {
        _participants = participants.ToArray();
    }

    public int ExecutionCount => Volatile.Read(ref _executionCount);

    public IReadOnlyList<ShutdownFailure> Failures
    {
        get
        {
            lock (_gate)
            {
                return _failures.ToArray();
            }
        }
    }

    public async Task<bool> StopOnceAsync(CancellationToken cancellationToken)
    {
        Task completionTask;
        TaskCompletionSource? owner = null;
        lock (_gate)
        {
            if (_completion is null)
            {
                owner = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                _completion = owner;
                Interlocked.Increment(ref _executionCount);
            }

            completionTask = _completion.Task;
        }

        if (owner is null)
        {
            await completionTask.WaitAsync(cancellationToken);
            return false;
        }

        foreach (var participant in _participants)
        {
            try
            {
                await participant.StopAsync(cancellationToken);
            }
            catch (Exception exception)
            {
                lock (_gate)
                {
                    _failures.Add(new ShutdownFailure(
                        participant.Name,
                        exception.GetType().Name,
                        exception.Message));
                }
            }
        }

        owner.TrySetResult();
        return true;
    }
}

public sealed record ShutdownFailure(string Participant, string ExceptionType, string Message);
