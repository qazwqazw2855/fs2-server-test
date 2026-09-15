using God2.ServerV2.Core;

namespace God2.ServerV2.Core.Tests;

public sealed class ConnectionStateMachineTests
{
    [Fact]
    public void Starts_connected()
    {
        var state = new ConnectionStateMachine();

        Assert.Equal(ConnectionStage.Connected, state.Stage);
    }

    [Fact]
    public void Accepts_login_to_world_closed_loop()
    {
        var state = new ConnectionStateMachine();

        state.Transition(ConnectionStage.LoginHandshake);
        state.Transition(ConnectionStage.Login);
        state.Transition(ConnectionStage.CharacterSelect);
        state.Transition(ConnectionStage.WorldHandshake);
        state.Transition(ConnectionStage.InWorld);
        state.Transition(ConnectionStage.Closing);
        state.Transition(ConnectionStage.Closed);

        Assert.Equal(ConnectionStage.Closed, state.Stage);
    }

    [Fact]
    public void Rejects_skipping_login_handshake()
    {
        var state = new ConnectionStateMachine();

        Assert.False(state.TryTransition(ConnectionStage.Login));
        Assert.Equal(ConnectionStage.Connected, state.Stage);
    }

    [Fact]
    public void Rejects_world_entry_before_character_select()
    {
        var state = new ConnectionStateMachine();
        state.Transition(ConnectionStage.LoginHandshake);
        state.Transition(ConnectionStage.Login);

        Assert.False(state.TryTransition(ConnectionStage.InWorld));
        Assert.Equal(ConnectionStage.Login, state.Stage);
    }

    [Fact]
    public void Allows_close_from_incomplete_handshake()
    {
        var state = new ConnectionStateMachine();
        state.Transition(ConnectionStage.LoginHandshake);

        state.Transition(ConnectionStage.Closing);
        state.Transition(ConnectionStage.Closed);

        Assert.Equal(ConnectionStage.Closed, state.Stage);
    }

    [Fact]
    public void Closed_state_is_terminal()
    {
        var state = new ConnectionStateMachine();
        state.Transition(ConnectionStage.Closing);
        state.Transition(ConnectionStage.Closed);

        Assert.False(state.TryTransition(ConnectionStage.LoginHandshake));
        Assert.Equal(ConnectionStage.Closed, state.Stage);
    }

    [Fact]
    public void Repeating_current_stage_is_idempotent()
    {
        var state = new ConnectionStateMachine();

        Assert.True(state.TryTransition(ConnectionStage.Connected));
        Assert.Equal(ConnectionStage.Connected, state.Stage);
    }
}
