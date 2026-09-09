using God2.ClassicServer.Protocol;
using God2.ClassicServer.Application.Common;
using God2.ClassicServer.Application.Configuration;
using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace God2.ClassicServer.Runtime.Tests;

public sealed class OfficialPortalClosedLoopTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Phase_one_world_envelope_round_trips_six_capture_portal_request()
    {
        var decoded = Convert.FromHexString("08000CA82734FC9C");

        var encoded = OfficialClientWorldProtocolFrames.EncodeWorldClientFrame(decoded);
        var roundTrip = OfficialClientWorldProtocolFrames.DecodeWorldClientFrame(encoded);

        Assert.Equal("08000CA42B9CDBA8", Convert.ToHexString(encoded));
        Assert.Equal(decoded, roundTrip);
    }

    [Fact]
    public async Task Official_request_executes_real_portal_runtime_and_serializes_ordered_result()
    {
        var fixture = Fixture.Create();
        var context = fixture.Contexts.Resolve(fixture.Session.SessionId);
        Assert.True(context.Succeeded);
        var command = Command(fixture, context.Value!, sequence: 1);

        var receipt = await fixture.ClosedLoop.ExecuteAsync(command, CancellationToken.None);

        Assert.Equal(OfficialPortalTransactionCode.Committed, receipt.Code);
        Assert.True(receipt.TransactionCommitted);
        Assert.False(receipt.OutboxDispatched);
        Assert.Equal(1, receipt.RuntimeMutationCount);
        Assert.Equal(19, receipt.SourceRuntimeMapId);
        Assert.Equal(3, receipt.TargetRuntimeMapId);
        Assert.Equal(0, receipt.RuntimeVersionBefore);
        Assert.Equal(1, receipt.RuntimeVersionAfter);
        Assert.Equal(2, receipt.OrderedEncodedFrames.Count);
        Assert.Equal(
            "0600BB0000ED",
            Convert.ToHexString(OfficialClientWorldProtocolFrames.DecodeWorldServerPayload(receipt.OrderedEncodedFrames[0].Span)));
        Assert.Equal(
            "0C0061C400040410031601F7",
            Convert.ToHexString(OfficialClientWorldProtocolFrames.DecodeWorldServerPayload(receipt.OrderedEncodedFrames[1].Span)));

        var rebound = fixture.Sessions.Get(fixture.Session.SessionId).Value!;
        Assert.Equal(3, rebound.MapSession.MapId);
        Assert.Equal(new WorldPosition3(196, 139), Assert.Single(fixture.Target.Objects.OfType<PlayerObject>()).State.Position);
        Assert.Empty(fixture.Source.Objects.OfType<PlayerObject>());
        var persisted = await fixture.PortalStore.LoadAsync(fixture.Character.CharacterId, CancellationToken.None);
        Assert.Equal(3, persisted.CurrentMapId);
        Assert.Equal(1, persisted.RuntimeVersion);
        Assert.Contains(fixture.Events.Events, value => value.Kind == WorldInteractionEventKind.PortalTransitionCommitted);
        Assert.Contains(fixture.Audit.Records, value => value.Handler == "Portal" && value.Result == WorldInteractionResultCode.Success);

        AssertOrdered(receipt.OrderedStages,
            "RuntimeCommitted", "AuthoritativeResult", "ResultSerialized", "JournalCommitted", "OutboxAppended", "TransactionCommitted");
        var dispatched = fixture.ClosedLoop.MarkOutboxDispatched(receipt);
        var sent = fixture.ClosedLoop.MarkNetworkSent(dispatched);
        Assert.True(sent.OutboxDispatched);
        Assert.Equal(1, sent.NetworkSendCount);
        AssertOrdered(sent.OrderedStages, "TransactionCommitted", "OutboxDispatched", "NetworkSend");
    }

    [Fact]
    public async Task Encoded_ingress_decodes_to_authoritative_runtime_without_replayed_response_bytes()
    {
        var fixture = Fixture.Create();
        var encoded = Convert.FromHexString("08000CA42B9CDBA8");

        var result = await fixture.ClosedLoop.ExecuteFrameAsync(
            fixture.Session.SessionId,
            7,
            "wire-ingress-7",
            encoded,
            CancellationToken.None);

        Assert.True(result.Recognized);
        Assert.True(result.Succeeded);
        Assert.Equal(1, result.Receipt.RuntimeMutationCount);
        Assert.NotEqual("06006590D86F", Convert.ToHexString(result.Receipt.OrderedEncodedFrames[0].Span));
        Assert.Equal(
            "0600BB0000ED",
            Convert.ToHexString(OfficialClientWorldProtocolFrames.DecodeWorldServerPayload(result.Receipt.OrderedEncodedFrames[0].Span)));
    }

    [Fact]
    public async Task Same_session_sequence_and_payload_commits_runtime_exactly_once()
    {
        var fixture = Fixture.Create();
        var binding = fixture.Contexts.Resolve(fixture.Session.SessionId).Value!;
        var command = Command(fixture, binding, sequence: 11);

        var first = await fixture.ClosedLoop.ExecuteAsync(command);
        var duplicate = await fixture.ClosedLoop.ExecuteAsync(command);

        Assert.Equal(OfficialPortalTransactionCode.Committed, first.Code);
        Assert.Equal(OfficialPortalTransactionCode.DuplicateCommitted, duplicate.Code);
        Assert.Equal(first.TransactionId, duplicate.TransactionId);
        Assert.Equal(first.ResponseSha256, duplicate.ResponseSha256);
        Assert.Equal(1, duplicate.RuntimeMutationCount);
        Assert.Single(fixture.PortalTransitions.Transitions, value => value.Code == WorldInteractionResultCode.Success);
        Assert.Equal(1, fixture.ClosedLoop.Diagnostics().RuntimeMutationCount);
    }

    [Fact]
    public async Task Concurrent_duplicate_is_serialized_and_commits_once()
    {
        var fixture = Fixture.Create();
        var binding = fixture.Contexts.Resolve(fixture.Session.SessionId).Value!;
        var command = Command(fixture, binding, sequence: 12);

        var results = await Task.WhenAll(Enumerable.Range(0, 32)
            .Select(_ => fixture.ClosedLoop.ExecuteAsync(command)));

        Assert.Single(results, value => value.Code == OfficialPortalTransactionCode.Committed);
        Assert.Equal(31, results.Count(value => value.Code == OfficialPortalTransactionCode.DuplicateCommitted));
        Assert.Equal(1, results.Max(value => value.RuntimeMutationCount));
        Assert.Single(fixture.PortalTransitions.Transitions, value => value.Code == WorldInteractionResultCode.Success);
    }

    [Fact]
    public async Task Same_client_request_with_changed_authoritative_binding_is_still_exactly_once()
    {
        var fixture = Fixture.Create();
        var binding = fixture.Contexts.Resolve(fixture.Session.SessionId).Value!;
        var command = Command(fixture, binding, sequence: 13);
        var first = await fixture.ClosedLoop.ExecuteAsync(command);

        var duplicate = await fixture.ClosedLoop.ExecuteAsync(command with
        {
            RuntimeBinding = binding with
            {
                ExpectedPlayerRuntimeVersion = 1,
                SourceRuntimeMapId = 3,
                TargetRuntimeMapId = 19,
                TargetClientMapId = 19,
                TargetPosition = new WorldPosition3(28, 34)
            }
        });

        Assert.Equal(OfficialPortalTransactionCode.Committed, first.Code);
        Assert.Equal(OfficialPortalTransactionCode.DuplicateCommitted, duplicate.Code);
        Assert.Equal(first.TransactionId, duplicate.TransactionId);
        Assert.Equal(first.ResponseSha256, duplicate.ResponseSha256);
        Assert.Equal(1, fixture.ClosedLoop.Diagnostics().RuntimeMutationCount);
    }

    [Fact]
    public async Task Serializer_preflight_blocks_unverified_content_before_runtime_mutation()
    {
        var fixture = Fixture.Create();
        var binding = fixture.Contexts.Resolve(fixture.Session.SessionId).Value! with
        {
            TargetRuntimeMapId = 4,
            TargetClientMapId = 4
        };

        var result = await fixture.ClosedLoop.ExecuteAsync(Command(fixture, binding, sequence: 14));

        Assert.Equal(OfficialPortalTransactionCode.RolledBack, result.Code);
        Assert.Equal("wire.portal.destination_not_verified", result.FailureCode);
        Assert.Empty(fixture.PortalTransitions.Transitions);
        Assert.Single(fixture.Source.Objects.OfType<PlayerObject>());
    }

    [Fact]
    public async Task Failure_before_runtime_rolls_back_without_outbox_or_bytes()
    {
        var fixture = Fixture.Create();
        fixture.ClosedLoop.FailurePoint = OfficialPortalFailurePoint.BeforeRuntime;
        var binding = fixture.Contexts.Resolve(fixture.Session.SessionId).Value!;

        var result = await fixture.ClosedLoop.ExecuteAsync(Command(fixture, binding, sequence: 15));

        Assert.Equal(OfficialPortalTransactionCode.RolledBack, result.Code);
        Assert.False(result.TransactionCommitted);
        Assert.Empty(result.OrderedEncodedFrames);
        Assert.Empty(fixture.PortalTransitions.Transitions);
        Assert.Equal(0, fixture.ClosedLoop.Diagnostics().RuntimeMutationCount);
    }

    [Fact]
    public async Task Commit_response_lost_is_recoverable_from_integrity_checked_receipt()
    {
        var fixture = Fixture.Create();
        fixture.ClosedLoop.FailurePoint = OfficialPortalFailurePoint.CommitResponseLost;
        var binding = fixture.Contexts.Resolve(fixture.Session.SessionId).Value!;

        var lost = await fixture.ClosedLoop.ExecuteAsync(Command(fixture, binding, sequence: 16));
        var snapshot = fixture.ClosedLoop.Snapshot();
        var recovered = new OfficialPortalClosedLoop(fixture.Contexts, fixture.Coordinator);
        recovered.Restore(snapshot);
        var replay = await recovered.ExecuteAsync(Command(fixture, binding, sequence: 16));

        Assert.Equal(OfficialPortalTransactionCode.RecoveryRequired, lost.Code);
        Assert.True(lost.TransactionCommitted);
        Assert.Single(snapshot);
        Assert.Equal(OfficialPortalTransactionCode.DuplicateCommitted, replay.Code);
        Assert.Equal(lost.ResponseSha256, replay.ResponseSha256);
        Assert.Single(fixture.PortalTransitions.Transitions, value => value.Code == WorldInteractionResultCode.Success);
    }

    [Fact]
    public async Task Recovery_receipts_are_bounded_without_changing_integrity_validation()
    {
        var fixture = Fixture.Create();
        var binding = fixture.Contexts.Resolve(fixture.Session.SessionId).Value!;
        var receipt = await fixture.ClosedLoop.ExecuteAsync(Command(fixture, binding, sequence: 31));
        var recovered = new OfficialPortalClosedLoop(
            fixture.Contexts,
            fixture.Coordinator,
            maximumRetainedReceipts: 2);
        var receipts = Enumerable.Range(1, 3)
            .Select(index => receipt with
            {
                TransactionId = Sha256($"transaction-{index}"),
                JournalId = Sha256($"journal-{index}"),
                OutboxId = Sha256($"outbox-{index}"),
                IdempotencyKeyHash = Sha256($"key-{index}"),
                ClientSequence = index
            })
            .ToArray();

        recovered.Restore(receipts);

        Assert.Equal(2, recovered.Diagnostics().ReceiptCount);
        Assert.DoesNotContain(
            recovered.Snapshot(),
            value => value.IdempotencyKeyHash == receipts[0].IdempotencyKeyHash);
    }

    [Fact]
    public async Task Session_cleanup_retains_committed_response_until_network_delivery()
    {
        var fixture = Fixture.Create();
        var binding = fixture.Contexts.Resolve(fixture.Session.SessionId).Value!;
        var committed = await fixture.ClosedLoop.ExecuteAsync(Command(fixture, binding, sequence: 32));

        fixture.ClosedLoop.RemoveSession(fixture.Session.SessionId);

        var retained = Assert.Single(fixture.ClosedLoop.Snapshot());
        Assert.Equal(committed.TransactionId, retained.TransactionId);
        Assert.True(retained.TransactionCommitted);
        Assert.Equal(0, retained.NetworkSendCount);
    }

    [Fact]
    public async Task Malformed_or_unrecognized_ingress_never_reaches_runtime()
    {
        var fixture = Fixture.Create();
        var mutation = Convert.FromHexString("08000CA42B9CDBA9");

        var malformed = await fixture.ClosedLoop.ExecuteFrameAsync(
            fixture.Session.SessionId, 17, "bad", mutation, CancellationToken.None);
        var heartbeat = await fixture.ClosedLoop.ExecuteFrameAsync(
            fixture.Session.SessionId, 18, "heartbeat", Convert.FromHexString("05001B59AD"), CancellationToken.None);

        Assert.True(malformed.Recognized);
        Assert.Equal(OfficialPortalTransactionCode.RolledBack, malformed.Receipt.Code);
        Assert.False(heartbeat.Recognized);
        Assert.Empty(fixture.PortalTransitions.Transitions);
    }

    [Fact]
    public void Context_resolver_fails_closed_when_implicit_portal_target_is_ambiguous()
    {
        var fixture = Fixture.Create();
        var original = Assert.Single(fixture.Source.Objects.OfType<PortalObject>());
        fixture.Source.Objects.Add(original with
        {
            Identity = original.Identity with { RuntimeObjectId = original.Identity.RuntimeObjectId + 1 }
        });

        var result = fixture.Contexts.Resolve(fixture.Session.SessionId);

        Assert.False(result.Succeeded);
        Assert.Equal("wire.portal.target_ambiguous", result.Error.Code);
    }

    [Fact]
    public async Task Outbox_cannot_dispatch_or_send_before_transaction_commit()
    {
        var fixture = Fixture.Create();
        var failure = await fixture.ClosedLoop.ExecuteFrameAsync(
            fixture.Session.SessionId,
            19,
            "invalid",
            Convert.FromHexString("08000CA42B9CDBA9"),
            CancellationToken.None);

        var dispatched = fixture.ClosedLoop.MarkOutboxDispatched(failure.Receipt);
        var sent = fixture.ClosedLoop.MarkNetworkSent(dispatched);

        Assert.Equal("wire.portal.outbox_before_commit", dispatched.FailureCode);
        Assert.Equal("wire.portal.send_before_commit_or_outbox", sent.FailureCode);
        Assert.Equal(0, sent.NetworkSendCount);
    }

    [Fact]
    public async Task Direct_canonical_command_rejects_inconsistent_wire_hashes_before_runtime()
    {
        var fixture = Fixture.Create();
        var binding = fixture.Contexts.Resolve(fixture.Session.SessionId).Value!;
        var command = Command(fixture, binding, sequence: 20) with
        {
            EncodedRequestSha256 = new string('A', 64)
        };

        var result = await fixture.ClosedLoop.ExecuteAsync(command);

        Assert.Equal(OfficialPortalTransactionCode.RolledBack, result.Code);
        Assert.Equal("wire.portal.command_invalid", result.FailureCode);
        Assert.Empty(fixture.PortalTransitions.Transitions);
    }

    [Fact]
    public async Task Forged_receipt_identity_cannot_dispatch_committed_outbox()
    {
        var fixture = Fixture.Create();
        var binding = fixture.Contexts.Resolve(fixture.Session.SessionId).Value!;
        var receipt = await fixture.ClosedLoop.ExecuteAsync(Command(fixture, binding, sequence: 21));

        var forged = fixture.ClosedLoop.MarkOutboxDispatched(receipt with
        {
            TransactionId = new string('A', 64)
        });

        Assert.Equal(OfficialPortalTransactionCode.RolledBack, forged.Code);
        Assert.Equal("wire.portal.outbox_before_commit", forged.FailureCode);
        Assert.Equal(1, fixture.ClosedLoop.Diagnostics().PendingOutboxCount);
    }

    [Fact]
    public async Task Recovery_rejects_tampered_response_frame()
    {
        var fixture = Fixture.Create();
        var binding = fixture.Contexts.Resolve(fixture.Session.SessionId).Value!;
        await fixture.ClosedLoop.ExecuteAsync(Command(fixture, binding, sequence: 22));
        var receipt = Assert.Single(fixture.ClosedLoop.Snapshot());
        var mutableFrames = receipt.OrderedEncodedFrames.Select(value => value.ToArray()).ToArray();
        mutableFrames[1][5] ^= 1;
        var frames = mutableFrames.Select(value => (ReadOnlyMemory<byte>)value).ToArray();
        var recovered = new OfficialPortalClosedLoop(fixture.Contexts, fixture.Coordinator);

        Assert.Throws<InvalidDataException>(() => recovered.Restore([receipt with { OrderedEncodedFrames = frames }]));
    }

    [Fact]
    public async Task Production_portal_seed_loads_persisted_location_and_runtime_version()
    {
        var sessions = new SessionStore();
        var authority = new InMemorySessionAuthority(sessions);
        await authority.InitializeAsync(CancellationToken.None);
        var session = authority.CreateSession("connection-relogin", "127.0.0.1:2592");
        session = authority.Authenticate(session, 1).Value!;
        session = authority.Transition(session, ProtocolStage.CharacterList).Value!;
        session = authority.BindCharacter(session, 10).Value!;
        session = authority.Transition(session, ProtocolStage.WorldEntering).Value!;
        session = authority.Transition(session, ProtocolStage.InWorld).Value!;
        var character = new CharacterSummary(
            10, 1, "PortalHero", "Class1", "Gender1", "LifeSkill1", 1,
            "Default", 3, 196, 139, "Active", Now, Now);
        var persistence = new InMemoryPortalTransitionStore();
        persistence.Seed(new PortalLocationState(
            10,
            3,
            new WorldPosition3(196, 139),
            WorldDirection.Unknown,
            7,
            0x50483201,
            Guid.NewGuid(),
            Now));
        var runtime = new OfficialPortalPhase2RuntimeService(authority, persistence);

        var seeded = await runtime.SeedSessionAsync(session, character, CancellationToken.None);
        var binding = runtime.Resolve(session.SessionId);

        Assert.True(seeded.Succeeded);
        Assert.True(binding.Succeeded);
        Assert.Equal(3, binding.Value!.SourceRuntimeMapId);
        Assert.Equal(19, binding.Value.TargetRuntimeMapId);
        Assert.Equal(7, binding.Value.ExpectedPlayerRuntimeVersion);
    }

    [Fact]
    public async Task Production_portal_seed_accepts_in_bounds_position_that_is_not_on_portal()
    {
        var (authority, session) = await CreateInWorldSessionAsync(10);
        var character = new CharacterSummary(
            10, 1, "WalkingHero", "Class1", "Gender1", "LifeSkill1", 1,
            "Default", 19, 29, 35, "Active", Now, Now);
        var persistence = new InMemoryPortalTransitionStore();
        persistence.Seed(new PortalLocationState(
            10, 19, new WorldPosition3(29, 35), WorldDirection.Unknown, 2, null, null, Now));
        var runtime = new OfficialPortalPhase2RuntimeService(authority, persistence);

        var seeded = await runtime.SeedSessionAsync(session, character, CancellationToken.None);

        Assert.True(seeded.Succeeded);
        Assert.True(runtime.Resolve(session.SessionId).Succeeded);
    }

    [Fact]
    public async Task Production_portal_seed_allows_verified_map_without_outbound_route_but_keeps_portal_fail_closed()
    {
        const int databaseMap7 = 170015007;
        var (authority, session) = await CreateInWorldSessionAsync(10);
        var character = new CharacterSummary(
            10, 1, "MerchantHero", "Class1", "Gender1", "LifeSkill1", 1,
            "Default", databaseMap7, 238, 54, "Active", Now, Now);
        var persistence = new InMemoryPortalTransitionStore();
        persistence.Seed(new PortalLocationState(
            10, databaseMap7, new WorldPosition3(238, 54), WorldDirection.Unknown, 0, null, null, Now));
        var identities = new TestPortalMapIdentitySource(
        [
            new(databaseMap7, 7, 15, "observed/map7-area15", new MapBounds(0, 0, 293, 293),
                "LiveRecovery/Stage3-CGC-S3-portal-map0-area15-to-map7-area15")
        ]);
        var runtime = new OfficialPortalPhase2RuntimeService(authority, persistence, identities);

        var seeded = await runtime.SeedSessionAsync(session, character, CancellationToken.None);
        var portal = runtime.Resolve(session.SessionId);

        Assert.True(seeded.Succeeded);
        Assert.Equal(0, runtime.ActiveRuntimeCount);
        Assert.False(portal.Succeeded);
        Assert.Equal("wire.portal.runtime_session_not_seeded", portal.Error.Code);
    }

    [Fact]
    public async Task Stage_three_one_way_route_seeds_and_commits_without_inventing_a_reverse_route()
    {
        const int sourceMap = 170015000;
        const int targetMap = 170015007;
        var (authority, session) = await CreateInWorldSessionAsync(10);
        var character = new CharacterSummary(
            10, 1, "Stage3Hero", "Class1", "Gender1", "LifeSkill1", 1,
            "Default", sourceMap, 248, 247, "Active", Now, Now);
        var persistence = new InMemoryPortalTransitionStore();
        persistence.Seed(new PortalLocationState(
            10, sourceMap, new WorldPosition3(248, 247), WorldDirection.Unknown, 25, null, null, Now));
        var identities = new TestPortalMapIdentitySource(
        [
            new(sourceMap, 0, 15, "observed/map0-area15", new MapBounds(0, 0, 293, 293),
                "LiveRecovery/Stage3-source-world-identity-sequence-14880"),
            new(targetMap, 7, 15, "observed/map7-area15", new MapBounds(0, 0, 293, 293),
                "LiveRecovery/Stage3-CGC-S3-portal-map0-area15-to-map7-area15")
        ]);
        var runtime = new OfficialPortalPhase2RuntimeService(authority, persistence, identities);
        var closedLoop = new OfficialPortalClosedLoop(runtime, runtime);

        var seeded = await runtime.SeedSessionAsync(session, character, CancellationToken.None);
        var binding = runtime.Resolve(session.SessionId);

        Assert.True(seeded.Succeeded);
        Assert.True(binding.Succeeded);
        Assert.Equal(sourceMap, binding.Value!.SourceRuntimeMapId);
        Assert.Equal(targetMap, binding.Value.TargetRuntimeMapId);
        Assert.Equal(new WorldPosition3(249, 246), binding.Value.SourcePosition);
        Assert.Equal(0, binding.Value.SourceRadius);
        Assert.True(closedLoop.IsMovementTrigger(
            session.SessionId,
            new WorldPosition3(248, 247),
            new WorldPosition3(249, 246)));

        var movement = new WorldMovementPersistenceRequest(
            10,
            sourceMap,
            new WorldPosition3(248, 247),
            new WorldPosition3(249, 246),
            WorldDirection.NorthEast,
            Now);
        Assert.True((await persistence.CommitMovementAsync(movement, CancellationToken.None)).Succeeded);
        Assert.True(closedLoop.SynchronizeMovement(
            session.SessionId,
            movement.ExpectedPosition,
            movement.Position,
            movement.Direction).Succeeded);

        var result = await closedLoop.ExecuteFrameAsync(
            session.SessionId,
            1,
            "stage3-one-way",
            Convert.FromHexString("08000CA42B9CDBA8"),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(targetMap, result.Receipt.TargetRuntimeMapId);
        Assert.Equal(
            "0C0061CF010F0FC000A20051",
            Convert.ToHexString(OfficialClientWorldProtocolFrames.DecodeWorldServerPayload(
                result.Receipt.OrderedEncodedFrames[0].Span)));
        var persisted = await persistence.LoadAsync(10, CancellationToken.None);
        Assert.Equal(targetMap, persisted.CurrentMapId);
        Assert.Equal(new WorldPosition3(48, 81), persisted.RawPosition);
        Assert.Equal(27, persisted.RuntimeVersion);
        Assert.False(OfficialPortalRouteCatalog.Resolve(7, 15).Succeeded);
    }

    [Fact]
    public async Task Production_map3_portal_uses_latest_observed_trigger_position()
    {
        var (authority, session) = await CreateInWorldSessionAsync(10);
        var character = new CharacterSummary(
            10, 1, "PortalHero", "Class1", "Gender1", "LifeSkill1", 1,
            "Default", 3, 252, 397, "Active", Now, Now);
        var persistence = new InMemoryPortalTransitionStore();
        persistence.Seed(new PortalLocationState(
            10, 3, new WorldPosition3(252, 397), WorldDirection.Unknown, 0, null, null, Now));
        var runtime = new OfficialPortalPhase2RuntimeService(authority, persistence);
        var closedLoop = new OfficialPortalClosedLoop(runtime, runtime);
        Assert.True((await runtime.SeedSessionAsync(session, character, CancellationToken.None)).Succeeded);

        var result = await closedLoop.ExecuteFrameAsync(
            session.SessionId,
            1,
            "latest-observed-map3-to-map19",
            Convert.FromHexString("08000CA42B9CDBA8"),
            CancellationToken.None);

        Assert.True(result.Recognized);
        Assert.True(result.Succeeded);
        Assert.Equal(19, result.Receipt.TargetRuntimeMapId);
        var persisted = await persistence.LoadAsync(10, CancellationToken.None);
        Assert.Equal(19, persisted.CurrentMapId);
        Assert.Equal(new WorldPosition3(28, 34), persisted.RawPosition);
    }

    [Fact]
    public async Task Explicit_portal_activation_uses_the_verified_command_after_world_movement()
    {
        var (authority, session) = await CreateInWorldSessionAsync(10);
        var character = new CharacterSummary(
            10, 1, "WalkingHero", "Class1", "Gender1", "LifeSkill1", 1,
            "Default", 3, 252, 397, "Active", Now, Now);
        var persistence = new InMemoryPortalTransitionStore();
        persistence.Seed(new PortalLocationState(
            10, 3, new WorldPosition3(252, 397), WorldDirection.Unknown, 0, null, null, Now));
        var runtime = new OfficialPortalPhase2RuntimeService(authority, persistence);
        var closedLoop = new OfficialPortalClosedLoop(runtime, runtime);
        Assert.True((await runtime.SeedSessionAsync(session, character, CancellationToken.None)).Succeeded);
        var movement = new WorldMovementPersistenceRequest(
            10,
            3,
            new WorldPosition3(252, 397),
            new WorldPosition3(253, 398),
            WorldDirection.SouthEast,
            Now);
        Assert.True((await persistence.CommitMovementAsync(movement, CancellationToken.None)).Succeeded);

        var synchronized = closedLoop.SynchronizeMovement(
            session.SessionId,
            movement.ExpectedPosition,
            movement.Position,
            movement.Direction);
        var binding = runtime.Resolve(session.SessionId);
        var portal = await closedLoop.ExecuteFrameAsync(
            session.SessionId,
            1,
            "movement-away-from-portal",
            Convert.FromHexString("08000CA42B9CDBA8"),
            CancellationToken.None);

        Assert.True(synchronized.Succeeded);
        Assert.True(binding.Succeeded);
        Assert.Equal(1, binding.Value!.ExpectedPlayerRuntimeVersion);
        Assert.Equal(OfficialPortalTransactionCode.Committed, portal.Receipt.Code);
        Assert.True(portal.Receipt.TransactionCommitted);
        var persisted = await persistence.LoadAsync(10, CancellationToken.None);
        Assert.Equal(19, persisted.CurrentMapId);
        Assert.Equal(new WorldPosition3(28, 34), persisted.RawPosition);
        Assert.Equal(2, persisted.RuntimeVersion);
    }

    [Fact]
    public async Task Hongmeng_biyou_portal_uses_a_movement_region_instead_of_one_exact_tile()
    {
        var (authority, session) = await CreateInWorldSessionAsync(10);
        var character = new CharacterSummary(
            10, 1, "RegionHero", "Class1", "Gender1", "LifeSkill1", 1,
            "Default", 0, 284, 452, "Active", Now, Now);
        var persistence = new InMemoryPortalTransitionStore();
        persistence.Seed(new PortalLocationState(
            10, 0, new WorldPosition3(284, 452), WorldDirection.Unknown, 0, null, null, Now));
        var runtime = new OfficialPortalPhase2RuntimeService(authority, persistence);
        var closedLoop = new OfficialPortalClosedLoop(runtime, runtime);

        Assert.True((await runtime.SeedSessionAsync(session, character, CancellationToken.None)).Succeeded);
        Assert.True(closedLoop.IsMovementTrigger(session.SessionId, new WorldPosition3(284, 452)));
        Assert.True(closedLoop.IsMovementTrigger(session.SessionId, new WorldPosition3(290, 452)));
        Assert.False(closedLoop.IsMovementTrigger(session.SessionId, new WorldPosition3(293, 452)));

        var binding = runtime.Resolve(session.SessionId);
        Assert.True(binding.Succeeded);
        Assert.Equal(43, binding.Value!.TargetClientMapId);
        Assert.Equal(8, binding.Value.SourceRadius);
        Assert.Equal("MovementRegion", binding.Value.TriggerType);
    }

    [Fact]
    public async Task Map19_exit_uses_the_formal_client_observed_movement_region()
    {
        var (authority, session) = await CreateInWorldSessionAsync(10);
        var character = new CharacterSummary(
            10, 1, "PathHero", "Class1", "Gender1", "LifeSkill1", 1,
            "Default", 19, 15, 10, "Active", Now, Now);
        var persistence = new InMemoryPortalTransitionStore();
        persistence.Seed(new PortalLocationState(
            10, 19, new WorldPosition3(15, 10), WorldDirection.Unknown, 0, null, null, Now));
        var runtime = new OfficialPortalPhase2RuntimeService(authority, persistence);
        var closedLoop = new OfficialPortalClosedLoop(runtime, runtime);

        Assert.True((await runtime.SeedSessionAsync(session, character, CancellationToken.None)).Succeeded);
        Assert.True(closedLoop.IsMovementTrigger(
            session.SessionId,
            new WorldPosition3(15, 10),
            new WorldPosition3(16, 20)));
        Assert.False(closedLoop.IsMovementTrigger(
            session.SessionId,
            new WorldPosition3(15, 10),
            new WorldPosition3(15, 18)));
        Assert.False(closedLoop.IsMovementTrigger(
            session.SessionId,
            new WorldPosition3(16, 20),
            new WorldPosition3(17, 21)));

        var binding = runtime.Resolve(session.SessionId);
        Assert.True(binding.Succeeded);
        Assert.Equal(new WorldPosition3(16, 20), binding.Value!.SourcePosition);
        Assert.Equal(1, binding.Value.SourceRadius);
        Assert.Equal("MovementRegion", binding.Value.TriggerType);
    }

    [Fact]
    public async Task Production_portal_persists_database_map_id_but_serializes_official_client_map_id()
    {
        const int databaseMap19 = 557790525;
        const int databaseMap3 = 1675308248;
        var (authority, session) = await CreateInWorldSessionAsync(10);
        var character = new CharacterSummary(
            10, 1, "MappedHero", "Class1", "Gender1", "LifeSkill1", 1,
            "Default", databaseMap19, 16, 20, "Active", Now, Now);
        var persistence = new InMemoryPortalTransitionStore();
        persistence.Seed(new PortalLocationState(
            10, databaseMap19, new WorldPosition3(16, 20), WorldDirection.Unknown, 0, null, null, Now));
        var identities = new TestPortalMapIdentitySource(
        [
            new(databaseMap19, 19, 4, "indoor/groceryl.hmd", new MapBounds(0, 0, 209, 629), "map19-evidence"),
            new(databaseMap3, 3, 4, "cityi2/cityi2.mdt", new MapBounds(0, 0, 251, 251), "map3-evidence")
        ]);
        var runtime = new OfficialPortalPhase2RuntimeService(authority, persistence, identities);
        var closedLoop = new OfficialPortalClosedLoop(runtime, runtime);
        Assert.True((await runtime.SeedSessionAsync(session, character, CancellationToken.None)).Succeeded);

        var ingress = await closedLoop.ExecuteFrameAsync(
            session.SessionId,
            1,
            "mapped-portal",
            Convert.FromHexString("08000CA42B9CDBA8"),
            CancellationToken.None);

        Assert.True(ingress.Succeeded);
        Assert.Equal(databaseMap19, ingress.Receipt.SourceRuntimeMapId);
        Assert.Equal(databaseMap3, ingress.Receipt.TargetRuntimeMapId);
        var decodedFrames = ingress.Receipt.OrderedEncodedFrames
            .Select(frame => OfficialClientWorldProtocolFrames.DecodeWorldServerPayload(frame.Span))
            .ToArray();
        var decoded = OfficialPortalWireCodec.DecodeResult(decodedFrames[0], decodedFrames[1]);
        Assert.True(decoded.Succeeded);
        Assert.Equal((ushort)3, decoded.Value!.ClientMapId);
        Assert.Equal((byte)4, decoded.Value.AreaId);
        Assert.Equal(databaseMap3, (await persistence.LoadAsync(10, CancellationToken.None)).CurrentMapId);
    }

    [Fact]
    public async Task Tcp_host_routes_in_world_portal_frame_and_writes_ordered_outbox_frames()
    {
        var port = GetFreeTcpPort();
        var character = new CharacterSummary(
            10, 1, "PortalHero", "Class1", "Gender1", "LifeSkill1", 1,
            "Default", 3, 252, 397, "Active", Now, null);
        var runtime = UnifiedRuntimeComposition.CreateForTesting(
            100,
            new InMemoryAccountRepository(
            [
                new AccountRecord(
                    1,
                    "fixture_account",
                    PasswordVerifier.Hash("xxxxxxxxxx"),
                    AccountStatus.Active,
                    Now,
                    null,
                    0,
                    null,
                    null,
                    "portal-network-account")
            ]),
            new InMemoryCharacterRepository([character]));
        var portalStore = new InMemoryPortalTransitionStore();
        var worldSessions = new WorldSessionCoordinator(
            new InMemoryWorldContentRepository(Fixture.Content()));
        var host = new TcpNetworkHost(
            runtime.PacketFactory,
            runtime.ProtocolConnectionRuntime,
            runtime.SessionAuthority,
            runtime.AuthenticationService,
            runtime.CharacterListQuery,
            portalStore,
            worldSessions,
            new LegacyCompatibilityWorldBootstrapProjector());
        Assert.True((await host.StartAsync(new NetworkOptions("127.0.0.1", port), CancellationToken.None)).Succeeded);

        using var loginClient = new TcpClient();
        await loginClient.ConnectAsync(IPAddress.Loopback, port);
        var loginStream = loginClient.GetStream();
        await ReadExactAsync(loginStream, 19);
        await loginStream.WriteAsync(Convert.FromHexString("1300E10638FA2835845B9FE9528DB9BCDF70BC"));
        await ReadExactAsync(loginStream, 6);
        await loginStream.WriteAsync(BuildSyntheticLoginRequest208());
        await ReadExactAsync(loginStream, 417);
        await loginStream.WriteAsync(Convert.FromHexString("06009202CE97"));
        await ReadExactAsync(loginStream, 78);

        using var worldClient = new TcpClient();
        await worldClient.ConnectAsync(IPAddress.Loopback, port);
        var worldStream = worldClient.GetStream();
        await ReadExactAsync(worldStream, 19);
        await worldStream.WriteAsync(Convert.FromHexString("1300D20A8DD62AEC4D6FF6F3F5D97E5C26B962"));
        var bootstrap = OfficialClientWorldProtocolFrames.BuildWorldBootstrapAfterHandshake(character);
        var worldFollowUp = OfficialClientWorldProtocolFrames.BuildWorldFirstFollowUp();
        Assert.Equal(worldFollowUp, await ReadExactAsync(worldStream, worldFollowUp.Length));
        await worldStream.WriteAsync(BuildWorldEntryStateGateFrame208());
        await ReadExactAsync(worldStream, bootstrap.Length - worldFollowUp.Length);
        await worldStream.WriteAsync(Convert.FromHexString("08000CA42B9CDBA8"));

        var response = await ReadExactAsync(worldStream, 18);
        var expectedPrelude = OfficialClientWorldProtocolFrames.EncodeWorldServerPayload(
            Convert.FromHexString("0600BB880075"));
        var expectedTransition = OfficialClientWorldProtocolFrames.EncodeWorldServerPayload(
            Convert.FromHexString("0C0061C40404047200440087"));
        Assert.Equal(expectedPrelude, response[..6]);
        Assert.Equal(expectedTransition, response[6..]);

        // A retransmit before the observed client follow-up is exactly-once: the
        // committed result is replayed but the runtime is not mutated again.
        await worldStream.WriteAsync(Convert.FromHexString("08000CA42B9CDBA8"));
        Assert.Equal(response, await ReadExactAsync(worldStream, 18));
        await WaitUntilAsync(() => host.OfficialPortalDiagnostics?.NetworkSendCount == 2);
        Assert.Equal(1, host.OfficialPortalDiagnostics!.RuntimeMutationCount);

        var persisted = await portalStore.LoadAsync(character.CharacterId, CancellationToken.None);
        Assert.Equal(19, persisted.CurrentMapId);
        Assert.Equal(new WorldPosition3(28, 34), persisted.RawPosition);
        Assert.Equal(1, persisted.RuntimeVersion);
        Assert.Equal(2, host.OfficialPortalDiagnostics.NetworkSendCount);
        Assert.Equal(1, host.OfficialPortalDiagnostics.RuntimeMutationCount);

        await host.StopAsync(CancellationToken.None);
        Assert.Equal(0, host.OfficialPortalDiagnostics.ReceiptCount);
    }

    private static OfficialPortalCanonicalCommand Command(
        Fixture fixture,
        OfficialPortalRuntimeBinding binding,
        long sequence)
    {
        var encoded = Convert.FromHexString("08000CA42B9CDBA8");
        var decoded = OfficialPortalWireCodec.DecodeActivate(
            OfficialPortalWireCodec.ClientBuildId,
            GameplayProtocolState.World,
            OfficialClientWorldProtocolFrames.DecodeWorldClientFrame(encoded)).Value!;
        return new OfficialPortalCanonicalCommand(
            fixture.Session.SessionId,
            sequence,
            $"portal-{sequence}",
            OfficialPortalWireCodec.ClientBuildId,
            Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(encoded)),
            decoded,
            binding);
    }

    private static async Task<(InMemorySessionAuthority Authority, RuntimeSession Session)> CreateInWorldSessionAsync(
        long characterId)
    {
        var authority = new InMemorySessionAuthority(new SessionStore());
        await authority.InitializeAsync(CancellationToken.None);
        var session = authority.CreateSession($"connection-{characterId}", "127.0.0.1:2592");
        session = authority.Authenticate(session, 1).Value!;
        session = authority.Transition(session, ProtocolStage.CharacterList).Value!;
        session = authority.BindCharacter(session, characterId).Value!;
        session = authority.Transition(session, ProtocolStage.WorldEntering).Value!;
        session = authority.Transition(session, ProtocolStage.InWorld).Value!;
        return (authority, session);
    }

    private sealed class TestPortalMapIdentitySource(
        IReadOnlyList<OfficialPortalMapRuntimeIdentity> identities) : IOfficialPortalMapIdentitySource
    {
        public OperationResult<OfficialPortalMapRuntimeIdentity> ResolveRuntimeMap(int runtimeMapId) =>
            Resolve(identities.Where(value => value.RuntimeMapId == runtimeMapId).ToArray());

        public OperationResult<OfficialPortalMapRuntimeIdentity> ResolveClientMap(ushort clientMapId, byte clientAreaId) =>
            Resolve(identities.Where(value =>
                value.ClientMapId == clientMapId && value.ClientAreaId == clientAreaId).ToArray());

        private static OperationResult<OfficialPortalMapRuntimeIdentity> Resolve(
            IReadOnlyList<OfficialPortalMapRuntimeIdentity> matches) =>
            matches.Count == 1
                ? OperationResult<OfficialPortalMapRuntimeIdentity>.Success(matches[0])
                : OperationResult<OfficialPortalMapRuntimeIdentity>.Failure(
                    "test.map_identity_missing",
                    "Test identity was not unique.");
    }

    private static void AssertOrdered(IReadOnlyList<string> stages, params string[] expected)
    {
        var previous = -1;
        foreach (var stage in expected)
        {
            var index = stages.IndexOf(stage);
            Assert.True(index > previous, $"Stage {stage} was not ordered after index {previous}.");
            previous = index;
        }
    }

    private static int GetFreeTcpPort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private static ReadOnlyMemory<byte> BuildSyntheticLoginRequest208()
    {
        var decoded = new byte[208];
        decoded[0] = 0xD0;
        Encoding.ASCII.GetBytes("fixture_account").CopyTo(decoded, 3);
        Enumerable.Repeat((byte)'x', 10).ToArray().CopyTo(decoded, 27);
        return OfficialClientLoginProtocolFrames.EncodeLoginFrame(decoded);
    }

    private static ReadOnlyMemory<byte> BuildWorldEntryStateGateFrame208()
    {
        var frame = new byte[208];
        BinaryPrimitives.WriteUInt16LittleEndian(frame, (ushort)frame.Length);
        return frame;
    }

    private static string Sha256(string value) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static async Task<byte[]> ReadExactAsync(NetworkStream stream, int length)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var bytes = new byte[length];
        var offset = 0;
        while (offset < length)
        {
            var read = await stream.ReadAsync(bytes.AsMemory(offset), timeout.Token);
            if (read == 0)
            {
                throw new EndOfStreamException();
            }

            offset += read;
        }

        return bytes;
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (!condition())
        {
            await Task.Delay(10, timeout.Token);
        }
    }

    private sealed record Fixture(
        WorldRuntime World,
        MapRuntime Source,
        MapRuntime Target,
        RuntimeSession Session,
        CharacterSummary Character,
        InMemoryWorldInteractionSessionRegistry Sessions,
        InMemoryPortalTransitionStore PortalStore,
        PortalTransitionCoordinator PortalTransitions,
        InMemoryWorldInteractionEventSink Events,
        InMemoryWorldInteractionAuditLedger Audit,
        IWorldInteractionCoordinator Coordinator,
        SingleActivePortalCommandContextResolver Contexts,
        OfficialPortalClosedLoop ClosedLoop)
    {
        public static Fixture Create(string sessionId = "session-portal")
        {
            var content = Content();
            var factory = new MapRuntimeFactory();
            var session = RuntimeSession.Connected("connection-portal", "127.0.0.1:2592", Now) with
            {
                SessionId = sessionId,
                AccountId = 1,
                CharacterId = 10,
                IsAuthenticated = true,
                ProtocolStage = ProtocolStage.InWorld
            };
            var character = new CharacterSummary(
                10, 1, "PortalTester", "Class1", "Gender1", "LifeSkill1", 1,
                "Test", 19, 28, 34, "Active", Now, null);
            var sourceBinding = new WorldSessionBinder(factory).Bind(
                session, character, content, WorldContentMode.MariaDbAuthoritative).Value!;
            var target = factory.Create(content, 3).Value!.MapRuntime;
            var world = new WorldRuntime();
            world.Add(sourceBinding.MapRuntime);
            world.Add(target);
            var sessions = new InMemoryWorldInteractionSessionRegistry();
            sessions.Seed(sourceBinding);
            var store = new InMemoryPortalTransitionStore();
            store.Seed(new PortalLocationState(
                character.CharacterId,
                19,
                new WorldPosition3(28, 34),
                WorldDirection.Unknown,
                0,
                null,
                null,
                Now));
            var events = new InMemoryWorldInteractionEventSink();
            var audit = new InMemoryWorldInteractionAuditLedger();
            var transitions = new PortalTransitionCoordinator(
                world, content, factory, sessions, store, events, audit);
            var handlers = new InteractionHandlerRegistry([new PortalInteractionHandler(transitions)]);
            var coordinator = new WorldInteractionCoordinator(
                new RuntimeInteractionTargetResolver(world, sessions),
                new RuntimeInteractionEligibilityPolicy(new DeterministicTestInteractionRangePolicy(8)),
                new NpcInteractionRouter(handlers, events),
                new InMemoryInteractionIdempotencyStore(),
                new InFlightInteractionCooldownStore(),
                events,
                audit);
            var contexts = new SingleActivePortalCommandContextResolver(world, sessions);
            var closedLoop = new OfficialPortalClosedLoop(contexts, coordinator);
            return new Fixture(
                world,
                sourceBinding.MapRuntime,
                target,
                session,
                character,
                sessions,
                store,
                transitions,
                events,
                audit,
                coordinator,
                contexts,
                closedLoop);
        }

        internal static WorldContentSnapshot Content()
        {
            var portal = new PortalDefinition(
                3001,
                new PortalEndpoint(19, new WorldPosition3(28, 34)),
                new PortalEndpoint(3, new WorldPosition3(196, 139)),
                "ContactArea",
                "None",
                OfficialPortalWireCodec.EvidenceId,
                "Official Phase 2 Portal");
            return new WorldContentSnapshot(
                new Dictionary<int, MapDefinition>
                {
                    [19] = Map(19, [portal]),
                    [3] = Map(3, [])
                },
                []);
        }

        private static MapDefinition Map(int mapId, IReadOnlyList<PortalDefinition> portals) =>
            new(
                mapId,
                mapId,
                $"Map {mapId}",
                "official-phase2",
                new MapBounds(0, 0, 1000, 1000),
                [new SpawnPoint("Default", new WorldPosition3(28, 34), WorldDirection.Unknown, OfficialPortalWireCodec.EvidenceId)],
                [],
                [],
                portals,
                [],
                [],
                new ClientMapIdentity(
                    mapId,
                    checked((ushort)mapId),
                    4,
                    $"official-phase2-map-{mapId}",
                    OfficialPortalWireCodec.ClientBuildId,
                    1m,
                    1m,
                    0m,
                    0m,
                    MapIdentityEvidenceStatus.Verified,
                    MapIdentityEvidenceStatus.Verified,
                    true,
                    OfficialPortalWireCodec.EvidenceId));
    }
}

internal static class OfficialPortalTestListExtensions
{
    public static int IndexOf(this IReadOnlyList<string> values, string value)
    {
        for (var index = 0; index < values.Count; index++)
        {
            if (string.Equals(values[index], value, StringComparison.Ordinal))
            {
                return index;
            }
        }

        return -1;
    }
}
