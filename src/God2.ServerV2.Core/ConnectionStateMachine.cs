namespace God2.ServerV2.Core;

public sealed class ConnectionStateMachine
{
    private int _stage = (int)ConnectionStage.Connected;

    public ConnectionStage Stage =>
        (ConnectionStage)Volatile.Read(ref _stage);

    public bool TryTransition(ConnectionStage next)
    {
        while (true)
        {
            var currentValue = Volatile.Read(ref _stage);
            var current = (ConnectionStage)currentValue;

            if (current == next)
            {
                return true;
            }

            if (!IsAllowed(current, next))
            {
                return false;
            }

            if (Interlocked.CompareExchange(
                    ref _stage,
                    (int)next,
                    currentValue) == currentValue)
            {
                return true;
            }
        }
    }

    public void Transition(ConnectionStage next)
    {
        if (!TryTransition(next))
        {
            throw new InvalidOperationException(
                $"Invalid connection-stage transition: {Stage} -> {next}.");
        }
    }

    private static bool IsAllowed(
        ConnectionStage current,
        ConnectionStage next) =>
        (current, next) switch
        {
            (ConnectionStage.Connected, ConnectionStage.LoginHandshake) => true,
            (ConnectionStage.Connected, ConnectionStage.WorldHandshake) => true,
            (ConnectionStage.LoginHandshake, ConnectionStage.Login) => true,
            (ConnectionStage.Login, ConnectionStage.CharacterSelect) => true,
            (ConnectionStage.CharacterSelect, ConnectionStage.WorldHandshake) => true,
            (ConnectionStage.WorldHandshake, ConnectionStage.InWorld) => true,

            (ConnectionStage.Connected, ConnectionStage.Closing) => true,
            (ConnectionStage.LoginHandshake, ConnectionStage.Closing) => true,
            (ConnectionStage.Login, ConnectionStage.Closing) => true,
            (ConnectionStage.CharacterSelect, ConnectionStage.Closing) => true,
            (ConnectionStage.WorldHandshake, ConnectionStage.Closing) => true,
            (ConnectionStage.InWorld, ConnectionStage.Closing) => true,

            (ConnectionStage.Closing, ConnectionStage.Closed) => true,
            _ => false
        };
}
