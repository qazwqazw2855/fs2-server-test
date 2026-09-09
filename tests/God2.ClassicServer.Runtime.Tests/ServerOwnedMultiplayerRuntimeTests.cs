using God2.ClassicServer.Runtime;
using God2.ClassicServer.Protocol;

namespace God2.ClassicServer.Runtime.Tests;

public sealed class ServerOwnedMultiplayerRuntimeTests
{
    [Fact]
    public void Party_directory_tracks_membership_across_invite_accept_leave_and_disband()
    {
        var directory = new ServerOwnedPartyDirectory();
        var partyId = Guid.NewGuid();
        var leader = Guid.NewGuid();
        var member = Guid.NewGuid();

        var created = directory.CreateParty(partyId, new PartyMemberState(leader, 7, 10, 20, true, 1));
        var invited = directory.Execute(partyId, new PartyCommand("invite", PartyCommandType.Invite, leader, member));
        var accepted = directory.Execute(partyId, new PartyCommand("accept", PartyCommandType.Accept, member));

        Assert.True(created.Succeeded);
        Assert.True(invited.Succeeded);
        Assert.True(accepted.Succeeded);
        Assert.Equal(partyId, directory.ResolvePartyId(leader));
        Assert.Equal(partyId, directory.ResolvePartyId(member));

        var left = directory.Execute(partyId, new PartyCommand("leave", PartyCommandType.Leave, member));
        var disbanded = directory.Execute(partyId, new PartyCommand("disband", PartyCommandType.Disband, leader));

        Assert.True(left.Succeeded);
        Assert.Null(directory.ResolvePartyId(member));
        Assert.True(disbanded.Succeeded);
        Assert.Null(directory.ResolvePartyId(leader));
        Assert.False(directory.FindByCharacter(leader).Succeeded);
    }

    [Fact]
    public void Party_directory_rejects_cross_party_acceptance()
    {
        var directory = new ServerOwnedPartyDirectory();
        var firstParty = Guid.NewGuid();
        var secondParty = Guid.NewGuid();
        var firstLeader = Guid.NewGuid();
        var secondLeader = Guid.NewGuid();
        var member = Guid.NewGuid();
        Assert.True(directory.CreateParty(firstParty, new PartyMemberState(firstLeader, 1, 0, 0, true, 1)).Succeeded);
        Assert.True(directory.CreateParty(secondParty, new PartyMemberState(secondLeader, 1, 0, 0, true, 1)).Succeeded);
        Assert.True(directory.Execute(firstParty, new PartyCommand("invite-a", PartyCommandType.Invite, firstLeader, member)).Succeeded);
        Assert.True(directory.Execute(firstParty, new PartyCommand("accept-a", PartyCommandType.Accept, member)).Succeeded);
        Assert.True(directory.Execute(secondParty, new PartyCommand("invite-b", PartyCommandType.Invite, secondLeader, member)).Succeeded);

        var result = directory.Execute(secondParty, new PartyCommand("accept-b", PartyCommandType.Accept, member));

        Assert.False(result.Succeeded);
        Assert.Equal("party.already_joined", result.Error.Code);
        Assert.Equal(firstParty, directory.ResolvePartyId(member));
    }

    [Fact]
    public void Chat_routes_world_party_and_whisper_without_cross_party_leakage()
    {
        var parties = new ServerOwnedPartyDirectory();
        var now = DateTimeOffset.Parse("2026-08-14T00:00:00Z", System.Globalization.CultureInfo.InvariantCulture);
        var chat = new ServerOwnedChatRuntime(parties.ResolvePartyId, utcNow: () => now);
        var partyId = Guid.NewGuid();
        var leader = Guid.NewGuid();
        var member = Guid.NewGuid();
        var outsider = Guid.NewGuid();
        Assert.True(parties.CreateParty(partyId, new PartyMemberState(leader, 1, 0, 0, true, 1)).Succeeded);
        Assert.True(parties.Execute(partyId, new PartyCommand("invite", PartyCommandType.Invite, leader, member)).Succeeded);
        Assert.True(parties.Execute(partyId, new PartyCommand("accept", PartyCommandType.Accept, member)).Succeeded);
        chat.RegisterOrUpdate(new ServerChatParticipant(leader, "Leader", true));
        chat.RegisterOrUpdate(new ServerChatParticipant(member, "Member", true));
        chat.RegisterOrUpdate(new ServerChatParticipant(outsider, "Outsider", true));
        var world = chat.Send(new ServerChatCommand("world", leader, ServerChatChannel.World, "hello"));
        now = now.AddSeconds(1);
        var party = chat.Send(new ServerChatCommand("party", leader, ServerChatChannel.Party, "team"));
        now = now.AddSeconds(1);
        var whisper = chat.Send(new ServerChatCommand("whisper", member, ServerChatChannel.Whisper, "secret", outsider));

        Assert.Equal(3, world.Deliveries.Count);
        Assert.Equal(new[] { leader, member }.Order().ToArray(), party.Deliveries.Select(value => value.RecipientId).Order().ToArray());
        Assert.DoesNotContain(party.Message, chat.Inbox(outsider));
        Assert.Equal(new[] { member, outsider }.Order().ToArray(), whisper.Deliveries.Select(value => value.RecipientId).Order().ToArray());
    }

    [Fact]
    public void Chat_is_idempotent_and_rate_limited()
    {
        var sender = Guid.NewGuid();
        var now = DateTimeOffset.Parse("2026-08-14T00:00:00Z", System.Globalization.CultureInfo.InvariantCulture);
        var chat = new ServerOwnedChatRuntime(_ => null, burstLimit: 2, burstWindow: TimeSpan.FromSeconds(10), utcNow: () => now);
        chat.RegisterOrUpdate(new ServerChatParticipant(sender, "Sender", true));
        var command = new ServerChatCommand("one", sender, ServerChatChannel.World, "first");

        var first = chat.Send(command);
        var duplicate = chat.Send(command);
        now = now.AddSeconds(1);
        var second = chat.Send(new ServerChatCommand("two", sender, ServerChatChannel.World, "second"));
        now = now.AddSeconds(1);
        var limited = chat.Send(new ServerChatCommand("three", sender, ServerChatChannel.World, "third"));

        Assert.Equal(ServerChatResultCode.Success, first.ResultCode);
        Assert.Equal(ServerChatResultCode.DuplicateCompleted, duplicate.ResultCode);
        Assert.Equal(ServerChatResultCode.Success, second.ResultCode);
        Assert.Equal(ServerChatResultCode.RateLimited, limited.ResultCode);
        Assert.Equal(2, chat.Inbox(sender).Count);
    }

    [Fact]
    public void Chat_retention_is_bounded_and_unregister_releases_participant_state()
    {
        var sender = Guid.NewGuid();
        var chat = new ServerOwnedChatRuntime(
            _ => null,
            burstLimit: 100,
            maximumInboxMessagesPerParticipant: 2,
            maximumCompletedCommands: 2);
        chat.RegisterOrUpdate(new ServerChatParticipant(sender, "Sender", true));

        chat.Send(new ServerChatCommand("one", sender, ServerChatChannel.World, "first"));
        chat.Send(new ServerChatCommand("two", sender, ServerChatChannel.World, "second"));
        chat.Send(new ServerChatCommand("three", sender, ServerChatChannel.World, "third"));

        Assert.Equal(2, chat.Inbox(sender).Count);
        Assert.Equal(new[] { "second", "third" }, chat.Inbox(sender).Select(value => value.Text));
        Assert.Equal(2, chat.CompletedCommandCount);
        Assert.Equal(1, chat.ParticipantCount);

        chat.Unregister(sender);

        Assert.Empty(chat.Inbox(sender));
        Assert.Equal(0, chat.ParticipantCount);
    }

    [Fact]
    public void Pk_requires_consent_then_records_authoritative_winner()
    {
        var runtime = new ServerOwnedPkRuntime();
        var challenger = Guid.NewGuid();
        var target = Guid.NewGuid();
        var duelId = Guid.NewGuid();
        runtime.RegisterOrUpdate(new ServerPkParticipant(challenger, 7, true, true));
        runtime.RegisterOrUpdate(new ServerPkParticipant(target, 7, true, true));

        var requested = runtime.Execute(new ServerPkCommand("request", ServerPkCommandType.Request, duelId, challenger, target));
        var accepted = runtime.Execute(new ServerPkCommand("accept", ServerPkCommandType.Accept, duelId, target));
        var completed = runtime.Execute(new ServerPkCommand("defeat", ServerPkCommandType.ReportDefeat, duelId, challenger, target));

        Assert.Equal(ServerPkDuelState.Requested, requested.Duel!.State);
        Assert.Equal(ServerPkDuelState.Active, accepted.Duel!.State);
        Assert.Equal(ServerPkDuelState.Completed, completed.Duel!.State);
        Assert.Equal(challenger, completed.Duel.WinnerId);
    }

    [Fact]
    public void Pk_rejects_safe_maps_cross_map_and_overlapping_duels()
    {
        var runtime = new ServerOwnedPkRuntime([1]);
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var third = Guid.NewGuid();
        runtime.RegisterOrUpdate(new ServerPkParticipant(first, 1, true, true));
        runtime.RegisterOrUpdate(new ServerPkParticipant(second, 1, true, true));
        runtime.RegisterOrUpdate(new ServerPkParticipant(third, 2, true, true));
        var safe = runtime.Execute(new ServerPkCommand("safe", ServerPkCommandType.Request, Guid.NewGuid(), first, second));

        runtime.RegisterOrUpdate(new ServerPkParticipant(first, 2, true, true));
        var activeRequest = runtime.Execute(new ServerPkCommand("active", ServerPkCommandType.Request, Guid.NewGuid(), first, third));
        var overlap = runtime.Execute(new ServerPkCommand("overlap", ServerPkCommandType.Request, Guid.NewGuid(), second, first));

        Assert.Equal(ServerPkResultCode.SafeMap, safe.ResultCode);
        Assert.Equal(ServerPkResultCode.Success, activeRequest.ResultCode);
        Assert.Equal(ServerPkResultCode.DifferentMap, overlap.ResultCode);
    }

    [Fact]
    public void Multiplayer_composition_reuses_the_production_pet_authority()
    {
        var pets = new StubPetLifecycleCoordinator();

        var runtime = new ServerOwnedMultiplayerRuntime(pets);

        Assert.Same(pets, runtime.PetLifecycle);
        Assert.NotNull(runtime.Parties);
        Assert.NotNull(runtime.Chat);
        Assert.NotNull(runtime.Pk);
        Assert.NotNull(runtime.NameCards);
    }

    [Fact]
    public void Network_host_exposes_the_server_owned_multiplayer_command_boundary()
    {
        var pets = new StubPetLifecycleCoordinator();
        var multiplayer = new ServerOwnedMultiplayerRuntime(pets);
        var host = new TcpNetworkHost(
            new PacketFactory(new PacketDeserializer(ProtocolRegistry.Official)),
            petLifecycleCoordinator: pets,
            serverOwnedMultiplayerRuntime: multiplayer);

        Assert.Same(multiplayer, host.ServerOwnedMultiplayerRuntime);
        Assert.Same(pets, host.PetLifecycleCoordinator);
        Assert.Same(pets, host.ServerOwnedMultiplayerRuntime!.PetLifecycle);
        Assert.NotNull(host.OfficialWorldChatClosedLoop);
    }

    private sealed class StubPetLifecycleCoordinator : IPetLifecycleCoordinator
    {
        public Task<PetAcquisitionTransactionResult> AcquirePetAsync(PetAcquisitionTransactionRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(new PetAcquisitionTransactionResult(request.TransactionId, false, 0, request.PetTemplateId, 0, null, null, 0, 0, null, "test"));

        public Task<PetLevelUpTransactionResult> ApplyPetLevelUpAsync(PetLevelUpTransactionRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(new PetLevelUpTransactionResult(request.TransactionId, false, request.PetInstanceId, 0, 0, 0, null, null, 0, 0, "test"));

        public Task<PetFourthSkillTransactionResult> TeachPetFourthSkillAsync(PetFourthSkillTransactionRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(new PetFourthSkillTransactionResult(request.TransactionId, false, request.PetInstanceId, 0, string.Empty, 0, 0, "test"));

        public Task<PetDeathTransactionResult> ApplyPetDeathAsync(PetDeathTransactionRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(new PetDeathTransactionResult(request.TransactionId, false, request.PetInstanceId, null, null, "test"));
    }
}
