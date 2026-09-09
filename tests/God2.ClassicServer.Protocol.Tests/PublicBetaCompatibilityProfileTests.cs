using God2.ClassicServer.Protocol;

namespace God2.ClassicServer.Protocol.Tests;

public sealed class PublicBetaCompatibilityProfileTests
{
    [Fact]
    public void Production_profile_applies_all_97_catalog_contracts_without_reusing_changed_offsets()
    {
        var profile = PublicBetaCompatibilityProfile.Production;

        Assert.Equal(97, profile.Summary.CatalogAppliedCount);
        Assert.Equal(97, profile.Summary.CatalogTotalCount);
        Assert.Equal(36, profile.Summary.ClientRequestCount);
        Assert.Equal(61, profile.Summary.ServerOutputCount);
        Assert.Equal(100m, profile.Summary.CatalogApplicationPercent);
        Assert.Equal(5, profile.Summary.CurrentWireVerifiedCount);
        Assert.Equal(49, profile.Summary.CurrentWireCompatibilityAdapterReadyCount);
        Assert.Equal(54, profile.Summary.CurrentWireImplementedCount);
        Assert.Equal(43, profile.Summary.CurrentWireAdapterRequiredCount);
        Assert.Equal(54m * 100m / 97m, profile.Summary.CurrentWireImplementationPercent);

        var changedLogin = profile.FindClientRequest("account_login", 0x04);
        Assert.NotNull(changedLogin);
        Assert.Equal(
            PublicBetaCurrentWireDisposition.CurrentWireCompatibilityAdapterReady,
            changedLogin.CurrentWireDisposition);

        var characterCreate = profile.FindClientRequest("character", 0x17);
        Assert.NotNull(characterCreate);
        Assert.Equal(
            PublicBetaCurrentWireDisposition.CurrentWireVerified,
            characterCreate.CurrentWireDisposition);

        Assert.Equal(
            PublicBetaCurrentWireDisposition.CurrentWireVerified,
            profile.FindClientRequest("character", 0x18)!.CurrentWireDisposition);

        var verifiedChat = profile.FindClientRequest("social", 0x2F);
        Assert.NotNull(verifiedChat);
        Assert.Equal(
            PublicBetaCurrentWireDisposition.CurrentWireVerified,
            verifiedChat.CurrentWireDisposition);

        var socialTextEnvelope = profile.FindClientRequest("social", 0x33);
        Assert.NotNull(socialTextEnvelope);
        Assert.Equal(
            PublicBetaCurrentWireDisposition.CurrentWireCompatibilityAdapterReady,
            socialTextEnvelope.CurrentWireDisposition);

        var pkCursorTarget = profile.FindClientRequest("pk", 0x69);
        Assert.NotNull(pkCursorTarget);
        Assert.Equal(
            PublicBetaCurrentWireDisposition.CurrentWireCompatibilityAdapterReady,
            pkCursorTarget.CurrentWireDisposition);
        var pkTarget = profile.FindClientRequest("pk", 0xB8);
        Assert.NotNull(pkTarget);
        Assert.Equal(
            PublicBetaCurrentWireDisposition.CurrentWireCompatibilityAdapterReady,
            pkTarget.CurrentWireDisposition);

        var combatAction = profile.FindClientRequest("combat", 0x35);
        Assert.NotNull(combatAction);
        Assert.Equal(
            PublicBetaCurrentWireDisposition.CurrentWireCompatibilityAdapterReady,
            combatAction.CurrentWireDisposition);

        var interaction24 = profile.FindClientRequest("mission", 0x24);
        Assert.NotNull(interaction24);
        Assert.Equal(
            PublicBetaCurrentWireDisposition.CurrentWireCompatibilityAdapterReady,
            interaction24.CurrentWireDisposition);

        var interaction27 = profile.FindClientRequest("mission", 0x27);
        Assert.NotNull(interaction27);
        Assert.Equal(
            PublicBetaCurrentWireDisposition.CurrentWireCompatibilityAdapterReady,
            interaction27.CurrentWireDisposition);

        var interaction28 = profile.FindClientRequest("mission", 0x28);
        Assert.NotNull(interaction28);
        Assert.Equal(
            PublicBetaCurrentWireDisposition.CurrentWireVerified,
            interaction28.CurrentWireDisposition);

        var interaction26 = profile.FindClientRequest("mission", 0x26);
        Assert.NotNull(interaction26);
        Assert.Equal(
            PublicBetaCurrentWireDisposition.CurrentWireCompatibilityAdapterReady,
            interaction26.CurrentWireDisposition);

        var combinationStep = profile.FindClientRequest("combination", 0xA4);
        Assert.NotNull(combinationStep);
        Assert.Equal(
            PublicBetaCurrentWireDisposition.CurrentWireCompatibilityAdapterReady,
            combinationStep.CurrentWireDisposition);
        var combinationCommit = profile.FindClientRequest("combination", 0xA5);
        Assert.NotNull(combinationCommit);
        Assert.Equal(
            PublicBetaCurrentWireDisposition.CurrentWireCompatibilityAdapterReady,
            combinationCommit.CurrentWireDisposition);

        Assert.Equal(
            PublicBetaCurrentWireDisposition.CurrentWireCompatibilityAdapterReady,
            profile.FindClientRequest("gameplay", 0x21)!.CurrentWireDisposition);
        Assert.Equal(
            PublicBetaCurrentWireDisposition.CurrentWireCompatibilityAdapterReady,
            profile.FindClientRequest("gameplay", 0x0A)!.CurrentWireDisposition);

        Assert.Equal(
            PublicBetaCurrentWireDisposition.CurrentWireCompatibilityAdapterReady,
            profile.FindClientRequest("party", 0x93)!.CurrentWireDisposition);
        Assert.Equal(
            PublicBetaCurrentWireDisposition.CurrentWireCompatibilityAdapterReady,
            profile.FindClientRequest("team", 0x5A)!.CurrentWireDisposition);
        Assert.Equal(
            PublicBetaCurrentWireDisposition.CurrentWireCompatibilityAdapterReady,
            profile.FindClientRequest("party", 0x95)!.CurrentWireDisposition);
        Assert.Equal(
            PublicBetaCurrentWireDisposition.CurrentWireCompatibilityAdapterReady,
            profile.FindClientRequest("party", 0x8F)!.CurrentWireDisposition);
        Assert.Equal(
            PublicBetaCurrentWireDisposition.CurrentWireCompatibilityAdapterReady,
            profile.FindClientRequest("party", 0x90)!.CurrentWireDisposition);
        Assert.Equal(
            PublicBetaCurrentWireDisposition.CurrentWireCompatibilityAdapterReady,
            profile.FindClientRequest("party", 0x91)!.CurrentWireDisposition);
        Assert.Equal(
            PublicBetaCurrentWireDisposition.CurrentWireCompatibilityAdapterReady,
            profile.FindClientRequest("party", 0x92)!.CurrentWireDisposition);
        Assert.Equal(
            PublicBetaCurrentWireDisposition.CurrentWireCompatibilityAdapterReady,
            profile.FindClientRequest("party", 0x94)!.CurrentWireDisposition);

        Assert.Equal(
            PublicBetaCurrentWireDisposition.CurrentWireCompatibilityAdapterReady,
            profile.FindClientRequest("account_login", 0x04)!.CurrentWireDisposition);
        Assert.Equal(
            PublicBetaCurrentWireDisposition.CurrentWireCompatibilityAdapterReady,
            profile.FindClientRequest("game_login", 0x00)!.CurrentWireDisposition);
        Assert.Equal(
            PublicBetaCurrentWireDisposition.CurrentWireCompatibilityAdapterReady,
            profile.FindClientRequest("route", 0xA9)!.CurrentWireDisposition);
        Assert.Equal(
            PublicBetaCurrentWireDisposition.CurrentWireCompatibilityAdapterReady,
            profile.FindClientRequest("mail", 0xAE)!.CurrentWireDisposition);
        Assert.Equal(
            PublicBetaCurrentWireDisposition.CurrentWireCompatibilityAdapterReady,
            profile.FindClientRequest("pet", 0xB7)!.CurrentWireDisposition);
        Assert.Equal(
            PublicBetaCurrentWireDisposition.CurrentWireCompatibilityAdapterReady,
            profile.FindClientRequest("pet", 0xBB)!.CurrentWireDisposition);
        Assert.Equal(
            PublicBetaCurrentWireDisposition.CurrentWireCompatibilityAdapterReady,
            profile.FindClientRequest("pk", 0x69)!.CurrentWireDisposition);
        Assert.Equal(
            PublicBetaCurrentWireDisposition.CurrentWireCompatibilityAdapterReady,
            profile.FindClientRequest("pk", 0xB8)!.CurrentWireDisposition);
        Assert.Equal(
            PublicBetaCurrentWireDisposition.CurrentWireCompatibilityAdapterReady,
            profile.FindClientRequest("vendor_cart", 0xB2)!.CurrentWireDisposition);
        Assert.Equal(
            PublicBetaCurrentWireDisposition.CurrentWireCompatibilityAdapterReady,
            profile.FindClientRequest("vendor_cart", 0xBD)!.CurrentWireDisposition);
        Assert.Equal(
            PublicBetaCurrentWireDisposition.CurrentWireCompatibilityAdapterReady,
            profile.FindClientRequest("vendor_cart", 0xB0)!.CurrentWireDisposition);

        Assert.Equal(
            PublicBetaCurrentWireDisposition.CurrentWireCompatibilityAdapterReady,
            profile.FindClientRequest("character", 0x19)!.CurrentWireDisposition);
        Assert.Equal(
            PublicBetaCurrentWireDisposition.CurrentWireCompatibilityAdapterReady,
            profile.FindClientRequest("character", 0x1A)!.CurrentWireDisposition);
        Assert.Equal(
            PublicBetaCurrentWireDisposition.CurrentWireCompatibilityAdapterReady,
            profile.FindClientRequest("character", 0x1B)!.CurrentWireDisposition);
    }

    [Fact]
    public void Production_profile_marks_runtime_completion_pending_contracts_as_adapter_ready_not_verified()
    {
        var profile = PublicBetaCompatibilityProfile.Production;

        Assert.Equal(
            PublicBetaCurrentWireDisposition.CurrentWireCompatibilityAdapterReady,
            profile.FindClientRequest("mission", 0x24)!.CurrentWireDisposition);
        Assert.Equal(
            PublicBetaCurrentWireDisposition.CurrentWireCompatibilityAdapterReady,
            profile.FindClientRequest("mission", 0x26)!.CurrentWireDisposition);
        Assert.Equal(
            PublicBetaCurrentWireDisposition.CurrentWireCompatibilityAdapterReady,
            profile.FindClientRequest("mission", 0x27)!.CurrentWireDisposition);
        Assert.Equal(
            PublicBetaCurrentWireDisposition.CurrentWireCompatibilityAdapterReady,
            profile.FindClientRequest("party", 0x93)!.CurrentWireDisposition);
        Assert.Equal(
            PublicBetaCurrentWireDisposition.CurrentWireCompatibilityAdapterReady,
            profile.FindClientRequest("party", 0x95)!.CurrentWireDisposition);
        Assert.NotEqual(
            PublicBetaCurrentWireDisposition.CurrentWireVerified,
            profile.FindClientRequest("mission", 0x24)!.CurrentWireDisposition);
        Assert.NotEqual(
            PublicBetaCurrentWireDisposition.CurrentWireVerified,
            profile.FindClientRequest("mission", 0x26)!.CurrentWireDisposition);
        Assert.NotEqual(
            PublicBetaCurrentWireDisposition.CurrentWireVerified,
            profile.FindClientRequest("mission", 0x27)!.CurrentWireDisposition);
        Assert.NotEqual(
            PublicBetaCurrentWireDisposition.CurrentWireVerified,
            profile.FindClientRequest("party", 0x93)!.CurrentWireDisposition);
        Assert.NotEqual(
            PublicBetaCurrentWireDisposition.CurrentWireVerified,
            profile.FindClientRequest("party", 0x95)!.CurrentWireDisposition);
    }

    [Fact]
    public void Production_profile_party_batch_requests_explain_party_system_features()
    {
        var profile = PublicBetaCompatibilityProfile.Production;

        foreach (var opcode in new byte[] { 0x8F, 0x90, 0x91, 0x92, 0x94 })
        {
            var request = profile.FindClientRequest("party", opcode);
            var evidence = OfficialPublicBetaCrossVersionEvidence.FindOutbound("party", opcode);
            Assert.NotNull(request);
            Assert.NotNull(evidence);
            Assert.Equal(
                PublicBetaCurrentWireDisposition.CurrentWireCompatibilityAdapterReady,
                request!.CurrentWireDisposition);
            Assert.Contains(evidence!.GameFeature, request.AdaptationReason, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void Production_profile_market_and_pet_batch_requests_explain_gameplay_features()
    {
        var profile = PublicBetaCompatibilityProfile.Production;
        foreach (var (domain, opcode) in
            new (string Domain, byte Opcode)[] { ("pet", 0xBB), ("pet", 0xB7), ("vendor_cart", 0xB0), ("vendor_cart", 0xB2), ("vendor_cart", 0xBD) })
        {
            var request = profile.FindClientRequest(domain, opcode);
            var evidence = OfficialPublicBetaCrossVersionEvidence.FindOutbound(domain, opcode);
            Assert.NotNull(request);
            Assert.NotNull(evidence);
            Assert.Equal(
                PublicBetaCurrentWireDisposition.CurrentWireCompatibilityAdapterReady,
                request!.CurrentWireDisposition);
            Assert.Contains(evidence!.GameFeature, request.AdaptationReason, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void Production_profile_core_play_requests_explain_gameplay_features()
    {
        var profile = PublicBetaCompatibilityProfile.Production;
        foreach (var (domain, opcode) in
            new (string Domain, byte Opcode)[] { ("account_login", 0x04), ("route", 0xA9), ("game_login", 0x00), ("combat", 0x35), ("social", 0x33) })
        {
            var request = profile.FindClientRequest(domain, opcode);
            var evidence = OfficialPublicBetaCrossVersionEvidence.FindOutbound(domain, opcode);
            Assert.NotNull(request);
            Assert.NotNull(evidence);
            Assert.Equal(
                PublicBetaCurrentWireDisposition.CurrentWireCompatibilityAdapterReady,
                request!.CurrentWireDisposition);
            Assert.Contains(evidence!.GameFeature, request.AdaptationReason, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void Production_profile_character_and_pk_batch_requests_explain_gameplay_features()
    {
        var profile = PublicBetaCompatibilityProfile.Production;
        foreach (var (domain, opcode) in
            new (string Domain, byte Opcode)[] { ("character", 0x19), ("character", 0x1A), ("character", 0x1B), ("pk", 0x69), ("pk", 0xB8) })
        {
            var request = profile.FindClientRequest(domain, opcode);
            var evidence = OfficialPublicBetaCrossVersionEvidence.FindOutbound(domain, opcode);
            Assert.NotNull(request);
            Assert.NotNull(evidence);
            Assert.Equal(
                PublicBetaCurrentWireDisposition.CurrentWireCompatibilityAdapterReady,
                request!.CurrentWireDisposition);
            Assert.Contains(evidence!.GameFeature, request.AdaptationReason, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void Production_profile_combo_mail_verified_requests_explain_gameplay_features()
    {
        var profile = PublicBetaCompatibilityProfile.Production;
        foreach (var (domain, opcode) in
            new (string Domain, byte Opcode)[]
            {
                ("combination", 0xA4),
                ("combination", 0xA5),
                ("mail", 0xAE),
                ("social", 0x2F),
                ("character", 0x17)
            })
        {
            var request = profile.FindClientRequest(domain, opcode);
            var evidence = OfficialPublicBetaCrossVersionEvidence.FindOutbound(domain, opcode);
            Assert.NotNull(request);
            Assert.NotNull(evidence);
            Assert.Equal(
                evidence!.PromotionStatus == PublicBetaPromotionStatus.CurrentBuildVerified
                    ? PublicBetaCurrentWireDisposition.CurrentWireVerified
                    : PublicBetaCurrentWireDisposition.CurrentWireCompatibilityAdapterReady,
                request!.CurrentWireDisposition);
            Assert.Contains(evidence.GameFeature, request.AdaptationReason, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void Production_profile_core_gameplay_requests_explain_gameplay_features()
    {
        var profile = PublicBetaCompatibilityProfile.Production;
        foreach (var (domain, opcode) in
            new (string Domain, byte Opcode)[] { ("gameplay", 0x21), ("gameplay", 0x0A) })
        {
            var request = profile.FindClientRequest(domain, opcode);
            var evidence = OfficialPublicBetaCrossVersionEvidence.FindOutbound(domain, opcode);
            Assert.NotNull(request);
            Assert.NotNull(evidence);
            Assert.Equal(
                PublicBetaCurrentWireDisposition.CurrentWireCompatibilityAdapterReady,
                request!.CurrentWireDisposition);
            Assert.Contains(evidence.GameFeature, request.AdaptationReason, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void Production_profile_maps_runtime_completion_pending_items_to_gameplay_features()
    {
        var profile = PublicBetaCompatibilityProfile.Production;
        var pendingItems = OfficialPublicBetaCrossVersionEvidence.SnapshotOutboundComparisons()
            .Where(value => value.PromotionStatus == PublicBetaPromotionStatus.StructuralCorroborationOnly &&
                            ((value.Domain == "mission" && (value.Opcode == 0x24 || value.Opcode == 0x26 || value.Opcode == 0x27)) ||
                             (value.Domain == "party" && (value.Opcode == 0x93 || value.Opcode == 0x95))))
            .ToArray();

        foreach (var item in pendingItems)
        {
            var request = profile.FindClientRequest(item.Domain, item.Opcode);
            Assert.NotNull(request);
            Assert.Equal(
                PublicBetaCurrentWireDisposition.CurrentWireCompatibilityAdapterReady,
                request.CurrentWireDisposition);
            Assert.False(string.IsNullOrWhiteSpace(item.GameFeature));
            Assert.Contains(item.GameFeature, request.AdaptationReason, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void Production_profile_maps_pk_requests_to_single_transport_boundary_branch()
    {
        var profile = PublicBetaCompatibilityProfile.Production;

        var pkCursorTarget = profile.FindClientRequest("pk", 0x69);
        var pkTarget = profile.FindClientRequest("pk", 0xB8);
        Assert.NotNull(pkCursorTarget);
        Assert.NotNull(pkTarget);

        Assert.Equal(
            PublicBetaCurrentWireDisposition.CurrentWireCompatibilityAdapterReady,
            pkCursorTarget.CurrentWireDisposition);
        Assert.Equal(
            PublicBetaCurrentWireDisposition.CurrentWireCompatibilityAdapterReady,
            pkTarget.CurrentWireDisposition);
        Assert.Contains(
            "Current-build pet-PK request has fixed transport boundary",
            pkCursorTarget.AdaptationReason,
            StringComparison.Ordinal);
        Assert.Contains(
            "raw compatibility adapter preserves bytes",
            pkTarget.AdaptationReason,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Production_profile_has_complete_one_to_one_client_request_mapping_to_evidence()
    {
        var profile = PublicBetaCompatibilityProfile.Production;
        var evidence = OfficialPublicBetaCrossVersionEvidence.SnapshotOutboundComparisons();

        Assert.Equal(36, profile.ClientRequests.Count);
        Assert.Equal(36, evidence.Count());

        var profilePairs = profile.ClientRequests
            .Select(value => $"{value.Domain}|{value.Opcode:X2}")
            .ToList();

        Assert.Equal(profilePairs.Count, profilePairs.Distinct().Count());

        foreach (var expected in evidence)
        {
            var mapped = Assert.Single(
                profile.ClientRequests,
                value => string.Equals(value.Domain, expected.Domain, StringComparison.Ordinal) &&
                         value.Opcode == expected.Opcode);
            Assert.Equal(expected.PublicBetaName, mapped.CanonicalCommand);
            Assert.False(string.IsNullOrWhiteSpace(mapped.AdaptationReason));
            Assert.False(string.IsNullOrWhiteSpace(mapped.CanonicalCommand));
        }
    }

    [Fact]
    public void Production_profile_preserves_every_server_output_domain_count()
    {
        var profile = PublicBetaCompatibilityProfile.Production;
        var expected = OfficialPublicBetaCrossVersionEvidence.SnapshotInboundInventory();

        Assert.Equal(expected.Count, profile.ServerOutputs.Count);
        foreach (var domain in expected)
        {
            var actual = Assert.Single(profile.ServerOutputs, value => value.Domain == domain.Domain);
            Assert.Equal(domain.EntryCount, actual.CanonicalContractCount);
            if (domain.Domain == "combat")
            {
                Assert.Equal(8, actual.CompatibilityAdapterReadyCount);
                Assert.Equal(
                    PublicBetaCurrentWireDisposition.CurrentWireCompatibilityAdapterReady,
                    actual.CurrentWireDisposition);
            }
            else if (domain.Domain == "gameplay")
            {
                Assert.Equal(10, actual.CompatibilityAdapterReadyCount);
                Assert.Equal(
                    PublicBetaCurrentWireDisposition.CurrentWireCompatibilityAdapterReady,
                    actual.CurrentWireDisposition);
            }
            else
            {
                Assert.Equal(0, actual.CompatibilityAdapterReadyCount);
                Assert.Equal(
                    PublicBetaCurrentWireDisposition.CurrentWireAdapterRequired,
                    actual.CurrentWireDisposition);
            }
        }
    }

    [Fact]
    public void Gameplay_output_catalog_exposes_local_player_derived_stats_serializer()
    {
        var gameplay = Assert.Single(
            PublicBetaCompatibilityProfile.Production.ServerOutputs,
            value => value.Domain == "gameplay");
        Assert.Equal(10, gameplay.CompatibilityAdapterReadyCount);
        Assert.Contains(
            PublicBetaCompatibilityGameplayWireAdapter.CurrentServerOutputs,
            descriptor => descriptor.CanonicalContract == "player_derived_stats_snapshot");
    }

    [Fact]
    public void Production_profile_describes_each_unconfirmed_gameplay_server_output_by_game_feature()
    {
        var combat = Assert.Single(
            PublicBetaCompatibilityProfile.Production.ServerOutputs,
            value => value.Domain == "combat");
        var gameplay = Assert.Single(
            PublicBetaCompatibilityProfile.Production.ServerOutputs,
            value => value.Domain == "gameplay");

        Assert.Contains("battle feedback", combat.AdaptationReason, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("0x84", combat.AdaptationReason, StringComparison.Ordinal);
        Assert.Contains("player derived stats", gameplay.AdaptationReason, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("monster world", gameplay.AdaptationReason, StringComparison.OrdinalIgnoreCase);
    }
}
