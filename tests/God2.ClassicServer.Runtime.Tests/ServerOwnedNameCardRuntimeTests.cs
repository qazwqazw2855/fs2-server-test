using God2.ClassicServer.Runtime;

namespace God2.ClassicServer.Runtime.Tests;

public sealed class ServerOwnedNameCardRuntimeTests
{
    [Fact]
    public async Task Two_online_players_can_exchange_name_cards_and_list_the_relationship_from_both_sides()
    {
        var repository = new InMemoryPlayerSocialRepository();
        var runtime = Runtime(repository);
        var invitationId = Guid.NewGuid();

        var requested = await runtime.RequestAsync(new NameCardRequestCommand("request-1", invitationId, 1, 2));
        var accepted = await runtime.RespondAsync(new NameCardResponseCommand("accept-1", invitationId, 2, true));
        var first = await runtime.ListNameCardsAsync(1);
        var second = await runtime.ListNameCardsAsync(2);

        Assert.Equal(SocialOperationResultCode.Success, requested.ResultCode);
        Assert.Equal(SocialInvitationState.Pending, requested.Invitation!.State);
        Assert.Equal(SocialOperationResultCode.Success, accepted.ResultCode);
        Assert.Equal(SocialInvitationState.Accepted, accepted.Invitation!.State);
        Assert.Equal(2, Assert.Single(first).RelatedCharacterId);
        Assert.Equal(1, Assert.Single(second).RelatedCharacterId);
    }

    [Fact]
    public async Task Rejected_exchange_does_not_create_a_relationship()
    {
        var runtime = Runtime(new InMemoryPlayerSocialRepository());
        var invitationId = Guid.NewGuid();
        await runtime.RequestAsync(new NameCardRequestCommand("request", invitationId, 1, 2));

        var rejected = await runtime.RespondAsync(new NameCardResponseCommand("reject", invitationId, 2, false));

        Assert.Equal(SocialInvitationState.Rejected, rejected.Invitation!.State);
        Assert.Empty(await runtime.ListNameCardsAsync(1));
    }

    [Fact]
    public async Task Blacklist_blocks_new_exchange_and_ends_an_existing_name_card_relationship()
    {
        var repository = new InMemoryPlayerSocialRepository();
        var runtime = Runtime(repository);
        var invitationId = Guid.NewGuid();
        await runtime.RequestAsync(new NameCardRequestCommand("request", invitationId, 1, 2));
        await runtime.RespondAsync(new NameCardResponseCommand("accept", invitationId, 2, true));

        var blocked = await runtime.SetBlockAsync(new SocialBlockCommand("block", 2, 1, true));
        var retried = await runtime.RequestAsync(new NameCardRequestCommand("request-after-block", Guid.NewGuid(), 1, 2));

        Assert.True(blocked.Mutated);
        Assert.True(await repository.IsBlockedAsync(2, 1, CancellationToken.None));
        Assert.Empty(await runtime.ListNameCardsAsync(1));
        Assert.Equal(SocialOperationResultCode.BlockedByTarget, retried.ResultCode);
    }

    [Fact]
    public async Task Idempotency_replays_the_same_command_and_rejects_a_changed_payload()
    {
        var runtime = Runtime(new InMemoryPlayerSocialRepository());
        var invitationId = Guid.NewGuid();
        var command = new NameCardRequestCommand("same-key", invitationId, 1, 2);

        var first = await runtime.RequestAsync(command);
        var duplicate = await runtime.RequestAsync(command);
        var conflict = await runtime.RequestAsync(command with { InvitationId = Guid.NewGuid() });

        Assert.Equal(SocialOperationResultCode.Success, first.ResultCode);
        Assert.Equal(SocialOperationResultCode.DuplicateCompleted, duplicate.ResultCode);
        Assert.False(duplicate.Mutated);
        Assert.Equal(SocialOperationResultCode.ReplayConflict, conflict.ResultCode);
    }

    [Fact]
    public async Task Shared_repository_preserves_name_cards_across_runtime_relogin()
    {
        var repository = new InMemoryPlayerSocialRepository();
        var firstSession = Runtime(repository);
        var invitationId = Guid.NewGuid();
        await firstSession.RequestAsync(new NameCardRequestCommand("request", invitationId, 1, 2));
        await firstSession.RespondAsync(new NameCardResponseCommand("accept", invitationId, 2, true));

        var reloggedSession = Runtime(repository);
        var restored = await reloggedSession.ListNameCardsAsync(1);

        Assert.Equal(2, Assert.Single(restored).RelatedCharacterId);
    }

    [Fact]
    public async Task Concurrent_crossed_requests_leave_only_one_pending_invitation()
    {
        var runtime = Runtime(new InMemoryPlayerSocialRepository());

        var results = await Task.WhenAll(
            Task.Run(() => runtime.RequestAsync(new NameCardRequestCommand("a", Guid.NewGuid(), 1, 2))),
            Task.Run(() => runtime.RequestAsync(new NameCardRequestCommand("b", Guid.NewGuid(), 2, 1))));

        Assert.Single(results, value => value.ResultCode == SocialOperationResultCode.Success);
        Assert.Single(results, value => value.ResultCode == SocialOperationResultCode.InvitationPending);
    }

    [Fact]
    public async Task Offline_target_and_unauthorized_response_fail_closed()
    {
        var repository = new InMemoryPlayerSocialRepository();
        var runtime = Runtime(repository);
        runtime.RegisterOrUpdate(new SocialParticipant(2, "Second", false));
        var invitationId = Guid.NewGuid();

        var offline = await runtime.RequestAsync(new NameCardRequestCommand("offline", invitationId, 1, 2));
        runtime.RegisterOrUpdate(new SocialParticipant(2, "Second", true));
        await runtime.RequestAsync(new NameCardRequestCommand("request", invitationId, 1, 2));
        var unauthorized = await runtime.RespondAsync(new NameCardResponseCommand("wrong", invitationId, 1, true));

        Assert.Equal(SocialOperationResultCode.TargetOffline, offline.ResultCode);
        Assert.Equal(SocialOperationResultCode.NotRecipient, unauthorized.ResultCode);
    }

    private static ServerOwnedNameCardRuntime Runtime(InMemoryPlayerSocialRepository repository)
    {
        var runtime = new ServerOwnedNameCardRuntime(
            repository,
            () => DateTimeOffset.Parse("2026-08-14T02:00:00Z", System.Globalization.CultureInfo.InvariantCulture));
        runtime.RegisterOrUpdate(new SocialParticipant(1, "First", true));
        runtime.RegisterOrUpdate(new SocialParticipant(2, "Second", true));
        return runtime;
    }
}
