using System.Collections.ObjectModel;

namespace God2.ClassicServer.Protocol;

public enum PublicBetaCurrentWireDisposition
{
    CurrentWireVerified,
    CurrentWireCompatibilityAdapterReady,
    CurrentWireAdapterRequired
}

public sealed record PublicBetaClientRequestCompatibility(
    string Domain,
    byte Opcode,
    string CanonicalCommand,
    PublicBetaCurrentWireDisposition CurrentWireDisposition,
    string AdaptationReason);

public sealed record PublicBetaServerOutputCompatibility(
    string Domain,
    int CanonicalContractCount,
    int CompatibilityAdapterReadyCount,
    PublicBetaCurrentWireDisposition CurrentWireDisposition,
    string AdaptationReason);

public sealed record PublicBetaCompatibilitySummary(
    int CatalogAppliedCount,
    int CatalogTotalCount,
    int ClientRequestCount,
    int ServerOutputCount,
    int CurrentWireVerifiedCount,
    int CurrentWireCompatibilityAdapterReadyCount,
    int CurrentWireAdapterRequiredCount)
{
    public decimal CatalogApplicationPercent =>
        CatalogTotalCount == 0 ? 0 : CatalogAppliedCount * 100m / CatalogTotalCount;

    public decimal CurrentWireActivationPercent =>
        CatalogTotalCount == 0 ? 0 : CurrentWireVerifiedCount * 100m / CatalogTotalCount;

    public int CurrentWireImplementedCount =>
        CurrentWireVerifiedCount + CurrentWireCompatibilityAdapterReadyCount;

    public decimal CurrentWireImplementationPercent =>
        CatalogTotalCount == 0 ? 0 : CurrentWireImplementedCount * 100m / CatalogTotalCount;
}

/// <summary>
/// Production compatibility intake for the complete 97-entry public-beta catalog.
/// Every public-beta contract is retained as a canonical semantic contract. Current
/// wire activation remains a separate gate: changed layouts are translated by a
/// build-specific adapter and are never decoded with public-beta offsets.
/// </summary>
public sealed class PublicBetaCompatibilityProfile
{
    private readonly ReadOnlyCollection<PublicBetaClientRequestCompatibility> _clientRequests;
    private readonly ReadOnlyCollection<PublicBetaServerOutputCompatibility> _serverOutputs;

    private PublicBetaCompatibilityProfile(
        IEnumerable<PublicBetaClientRequestCompatibility> clientRequests,
        IEnumerable<PublicBetaServerOutputCompatibility> serverOutputs)
    {
        _clientRequests = Array.AsReadOnly(clientRequests.ToArray());
        _serverOutputs = Array.AsReadOnly(serverOutputs.ToArray());

        var applied = _clientRequests.Count + _serverOutputs.Sum(value => value.CanonicalContractCount);
        if (_clientRequests.Count != OfficialPublicBetaCrossVersionEvidence.ClientToServerEntryCount ||
            _serverOutputs.Sum(value => value.CanonicalContractCount) !=
            OfficialPublicBetaCrossVersionEvidence.ServerToClientEntryCount ||
            applied != OfficialPublicBetaCrossVersionEvidence.CatalogEntryCount)
        {
            throw new InvalidOperationException("public_beta.compatibility_catalog_incomplete");
        }

        var verified = _clientRequests.Count(value =>
            value.CurrentWireDisposition == PublicBetaCurrentWireDisposition.CurrentWireVerified);
        var compatibilityReady = _clientRequests.Count(value =>
                value.CurrentWireDisposition == PublicBetaCurrentWireDisposition.CurrentWireCompatibilityAdapterReady) +
            _serverOutputs.Sum(value => value.CompatibilityAdapterReadyCount);
        Summary = new PublicBetaCompatibilitySummary(
            applied,
            OfficialPublicBetaCrossVersionEvidence.CatalogEntryCount,
            _clientRequests.Count,
            _serverOutputs.Sum(value => value.CanonicalContractCount),
            verified,
            compatibilityReady,
            applied - verified - compatibilityReady);
    }

    public static PublicBetaCompatibilityProfile Production { get; } = CreateProduction();

    public IReadOnlyList<PublicBetaClientRequestCompatibility> ClientRequests => _clientRequests;

    public IReadOnlyList<PublicBetaServerOutputCompatibility> ServerOutputs => _serverOutputs;

    public PublicBetaCompatibilitySummary Summary { get; }

    public PublicBetaClientRequestCompatibility? FindClientRequest(string domain, byte opcode) =>
        _clientRequests.FirstOrDefault(value =>
            value.Opcode == opcode && string.Equals(value.Domain, domain, StringComparison.Ordinal));

    private static PublicBetaCompatibilityProfile CreateProduction()
    {
        var clientRequests = OfficialPublicBetaCrossVersionEvidence
            .SnapshotOutboundComparisons()
            .Select(ToCompatibility)
            .ToArray();
        var serverOutputs = OfficialPublicBetaCrossVersionEvidence
            .SnapshotInboundInventory()
            .Select(value => new PublicBetaServerOutputCompatibility(
                value.Domain,
                value.EntryCount,
                CompatibilityAdapterCount(value.Domain),
                CompatibilityAdapterCount(value.Domain) > 0
                    ? PublicBetaCurrentWireDisposition.CurrentWireCompatibilityAdapterReady
                    : PublicBetaCurrentWireDisposition.CurrentWireAdapterRequired,
                string.Equals(value.Domain, "combat", StringComparison.Ordinal)
                    ? "Eight current-build combat writers are implemented; 0x84 battle feedback packets remain hypothesis-only because they are not observed in the current build and are still candidate-only combat UX/event feeds."
                    : string.Equals(value.Domain, "gameplay", StringComparison.Ordinal)
                    ? "Ten gameplay outputs have executable current-build writers: 0x22 player status, 0x24 player vitals, 0x2B player/god progress, 0x2C player derived stats, 0x71 monster world, 0x72 NPC spawn, 0x31 immortal status, 0x32 catalog selector, 0x33 UI flags, and 0x39 immortal vitals; the other gameplay outputs remain adapter-required."
                    : "Canonical server output is retained; serialization must use an independently verified current-build writer."))
            .ToArray();
        return new PublicBetaCompatibilityProfile(clientRequests, serverOutputs);
    }

    private static PublicBetaClientRequestCompatibility ToCompatibility(
        PublicBetaOutboundContractComparison evidence)
    {
        if (string.Equals(evidence.Domain, "account_login", StringComparison.Ordinal) &&
            evidence.Opcode == PublicBetaCompatibilityGameplayRequestWireAdapter.AccountLoginOpcode)
        {
            var accountLoginFeature = evidence.GameFeature == "待補功能對照"
                ? "account-login request"
                : evidence.GameFeature;
            return new PublicBetaClientRequestCompatibility(
                evidence.Domain,
                evidence.Opcode,
                evidence.PublicBetaName,
                PublicBetaCurrentWireDisposition.CurrentWireCompatibilityAdapterReady,
                $"Current-build account-login request opcode 0x04 has fixed frame length and exact opcode; raw compatibility preserves capture-verified structure for {accountLoginFeature}.");
        }

        if (string.Equals(evidence.Domain, "route", StringComparison.Ordinal) &&
            evidence.Opcode == PublicBetaCompatibilityGameplayRequestWireAdapter.RouteOpcode)
        {
            var routeFeature = evidence.GameFeature == "待補功能對照"
                ? "route-query request"
                : evidence.GameFeature;
            return new PublicBetaClientRequestCompatibility(
                evidence.Domain,
                evidence.Opcode,
                evidence.PublicBetaName,
                PublicBetaCurrentWireDisposition.CurrentWireCompatibilityAdapterReady,
                $"Current-build route-query request opcode 0xA9 has fixed frame length and exact opcode; raw compatibility preserves capture-verified structure for {routeFeature}.");
        }

        if (string.Equals(evidence.Domain, "game_login", StringComparison.Ordinal) &&
            evidence.Opcode == PublicBetaCompatibilityGameplayRequestWireAdapter.GameLoginOpcode)
        {
            var gameLoginFeature = evidence.GameFeature == "待補功能對照"
                ? "game-login request"
                : evidence.GameFeature;
            return new PublicBetaClientRequestCompatibility(
                evidence.Domain,
                evidence.Opcode,
                evidence.PublicBetaName,
                PublicBetaCurrentWireDisposition.CurrentWireCompatibilityAdapterReady,
                $"Current-build game-login request opcode 0x00 has fixed frame length and exact opcode; raw compatibility preserves capture-verified structure for {gameLoginFeature}.");
        }

        if (string.Equals(evidence.Domain, "combat", StringComparison.Ordinal) &&
            evidence.Opcode == OfficialBattleCommandWireCodec.CommandOpcode)
        {
            var combatFeature = evidence.GameFeature == "待補功能對照"
                ? "combat action command"
                : evidence.GameFeature;
            return new PublicBetaClientRequestCompatibility(
                evidence.Domain,
                evidence.Opcode,
                evidence.PublicBetaName,
                PublicBetaCurrentWireDisposition.CurrentWireCompatibilityAdapterReady,
                $"The exact current-build 0x35 layout is converted into the stable canonical combat command for {combatFeature}; third target-group and unknown skill operands fail closed.");
        }

        if (string.Equals(evidence.Domain, "mission", StringComparison.Ordinal) &&
            evidence.Opcode is
                PublicBetaCompatibilityGameplayRequestWireAdapter.Interaction24Opcode or
                PublicBetaCompatibilityGameplayRequestWireAdapter.Interaction26Opcode or
                PublicBetaCompatibilityGameplayRequestWireAdapter.Interaction27Opcode)
        {
            return new PublicBetaClientRequestCompatibility(
                evidence.Domain,
                evidence.Opcode,
                evidence.PublicBetaName,
                PublicBetaCurrentWireDisposition.CurrentWireCompatibilityAdapterReady,
                $"The exact current-build producer for {evidence.GameFeature} preserves raw payload; business effects remain server-evidence-gated.");
        }

        if (string.Equals(evidence.Domain, "combination", StringComparison.Ordinal) &&
            evidence.Opcode == PublicBetaCompatibilityGameplayRequestWireAdapter.CombinationStepOpcode)
        {
            var combinationStepFeature = evidence.GameFeature == "待補功能對照"
                ? "combination step request"
                : evidence.GameFeature;
            return new PublicBetaClientRequestCompatibility(
                evidence.Domain,
                evidence.Opcode,
                evidence.PublicBetaName,
                PublicBetaCurrentWireDisposition.CurrentWireCompatibilityAdapterReady,
                $"The exact current-build producer and public-beta contract both encode a raw selector followed by a strictly validated +1/-1 step for {combinationStepFeature}; combination mutation remains server-evidence-gated.");
        }

        if (string.Equals(evidence.Domain, "gameplay", StringComparison.Ordinal) &&
            evidence.Opcode is
                PublicBetaCompatibilityGameplayRequestWireAdapter.GameplayDisconnectOpcode or
                PublicBetaCompatibilityGameplayRequestWireAdapter.AttributeIncrementOpcode)
        {
            var gameplayFeature = evidence.GameFeature == "待補功能對照"
                ? "gameplay request"
                : evidence.GameFeature;
            return new PublicBetaClientRequestCompatibility(
                evidence.Domain,
                evidence.Opcode,
                evidence.PublicBetaName,
                PublicBetaCurrentWireDisposition.CurrentWireCompatibilityAdapterReady,
                $"The current 0x21/2 and 0x0A application records are independently capture-verified and now pass a build-pinned transport adapter for {gameplayFeature}; public-beta semantics are exposed canonically while production mutation remains evidence-gated.");
        }

        if (string.Equals(evidence.Domain, "social", StringComparison.Ordinal) &&
            evidence.Opcode == PublicBetaCompatibilityGameplayRequestWireAdapter.SocialTextEnvelopeOpcode)
        {
            var socialFeature = evidence.GameFeature == "待補功能對照"
                ? "social text envelope"
                : evidence.GameFeature;
            return new PublicBetaClientRequestCompatibility(
                evidence.Domain,
                evidence.Opcode,
                evidence.PublicBetaName,
                PublicBetaCurrentWireDisposition.CurrentWireCompatibilityAdapterReady,
                $"Current-build social text-envelope envelope shares the same variable-length raw transport envelope boundaries as public-beta for {socialFeature}; business semantics remain current-evidence gated.");
        }

        if ((string.Equals(evidence.Domain, "party", StringComparison.Ordinal) &&
             (evidence.Opcode == PublicBetaCompatibilityGameplayRequestWireAdapter.PartySelectionOpcode ||
              evidence.Opcode == PublicBetaCompatibilityGameplayRequestWireAdapter.PartyRequest95Opcode)) ||
            (string.Equals(evidence.Domain, "team", StringComparison.Ordinal) &&
             evidence.Opcode == PublicBetaCompatibilityGameplayRequestWireAdapter.TeamRequestOpcode))
        {
            var partyFeature = evidence.GameFeature == "待補功能對照"
                ? "party-related operation"
                : evidence.GameFeature;
            return new PublicBetaClientRequestCompatibility(
                evidence.Domain,
                evidence.Opcode,
                evidence.PublicBetaName,
                PublicBetaCurrentWireDisposition.CurrentWireCompatibilityAdapterReady,
                $"Exact current-build producers preserve the same raw record boundary as the public-beta contract for {partyFeature}; party/team mutation remains server-evidence-gated.");
        }

        if (string.Equals(evidence.Domain, "combination", StringComparison.Ordinal) &&
            evidence.Opcode == PublicBetaCompatibilityGameplayRequestWireAdapter.CombinationCommitOpcode)
        {
            var combinationCommitFeature = evidence.GameFeature == "待補功能對照"
                ? "combination result commit"
                : evidence.GameFeature;
            return new PublicBetaClientRequestCompatibility(
                evidence.Domain,
                evidence.Opcode,
                evidence.PublicBetaName,
                PublicBetaCurrentWireDisposition.CurrentWireCompatibilityAdapterReady,
                $"Current-build 0xA5 combination commit request is fixed-length raw-transport only for {combinationCommitFeature}; raw compatibility adapter is evidence-safe.");
        }

        if (string.Equals(evidence.Domain, "mail", StringComparison.Ordinal) &&
            evidence.Opcode == PublicBetaCompatibilityGameplayRequestWireAdapter.MailRecordActionOpcode)
        {
            var mailFeature = evidence.GameFeature == "待補功能對照"
                ? "mail operation"
                : evidence.GameFeature;
            return new PublicBetaClientRequestCompatibility(
                evidence.Domain,
                evidence.Opcode,
                evidence.PublicBetaName,
                PublicBetaCurrentWireDisposition.CurrentWireCompatibilityAdapterReady,
                $"Current-build 0xAE mail request has fixed transport shape for {mailFeature}; raw compatibility adapter safely preserves structure.");
        }

        if (string.Equals(evidence.Domain, "party", StringComparison.Ordinal) &&
            (evidence.Opcode == PublicBetaCompatibilityGameplayRequestWireAdapter.PartyRequest8FOpcode ||
             evidence.Opcode == PublicBetaCompatibilityGameplayRequestWireAdapter.PartyRequest90Opcode ||
             evidence.Opcode == PublicBetaCompatibilityGameplayRequestWireAdapter.PartyRequest91Opcode ||
             evidence.Opcode == PublicBetaCompatibilityGameplayRequestWireAdapter.PartyRequest92Opcode ||
             evidence.Opcode == PublicBetaCompatibilityGameplayRequestWireAdapter.PartyRequest94Opcode))
        {
            var partyFeature = evidence.GameFeature == "待補功能對照"
                ? "party-related operation"
                : evidence.GameFeature;
            return new PublicBetaClientRequestCompatibility(
                evidence.Domain,
                evidence.Opcode,
                evidence.PublicBetaName,
                PublicBetaCurrentWireDisposition.CurrentWireCompatibilityAdapterReady,
                $"Current-build party request {LabelPartyOpcode(evidence.Opcode)} has fixed transport shape; raw compatibility adapter preserves opcode + payload bytes for {partyFeature}.");
        }

        if (string.Equals(evidence.Domain, "pet", StringComparison.Ordinal) &&
            (evidence.Opcode == PublicBetaCompatibilityGameplayRequestWireAdapter.PetEggRequestOpcode ||
             evidence.Opcode == PublicBetaCompatibilityGameplayRequestWireAdapter.FPetRequestOpcode))
        {
            var petFeature = evidence.GameFeature == "待補功能對照"
                ? "pet-related operation"
                : evidence.GameFeature;
            return new PublicBetaClientRequestCompatibility(
                evidence.Domain,
                evidence.Opcode,
                evidence.PublicBetaName,
                PublicBetaCurrentWireDisposition.CurrentWireCompatibilityAdapterReady,
                $"Current-build pet-request opcodes have fixed payload layout and may be transported through raw compatibility adapter for {petFeature}.");
        }

        if (string.Equals(evidence.Domain, "pk", StringComparison.Ordinal) &&
            (evidence.Opcode == PublicBetaCompatibilityGameplayRequestWireAdapter.PkCursorTargetOpcode ||
             evidence.Opcode == PublicBetaCompatibilityGameplayRequestWireAdapter.PetPkTargetOpcode))
        {
            var pkFeature = evidence.GameFeature == "待補功能對照"
                ? "PK targeting request"
                : evidence.GameFeature;
            return new PublicBetaClientRequestCompatibility(
                evidence.Domain,
                evidence.Opcode,
                evidence.PublicBetaName,
                PublicBetaCurrentWireDisposition.CurrentWireCompatibilityAdapterReady,
                $"Current-build pet-PK request has fixed transport boundary for {pkFeature}; raw compatibility adapter preserves bytes.");
        }

        if (string.Equals(evidence.Domain, "vendor_cart", StringComparison.Ordinal) &&
            (evidence.Opcode == PublicBetaCompatibilityGameplayRequestWireAdapter.VendorCartActionB2Opcode ||
             evidence.Opcode == PublicBetaCompatibilityGameplayRequestWireAdapter.VendorCartAddRecordOpcode ||
             evidence.Opcode == PublicBetaCompatibilityGameplayRequestWireAdapter.VendorCartPublishOpcode))
        {
            var vendorFeature = evidence.GameFeature == "待補功能對照"
                ? "vendor-cart operation"
                : evidence.GameFeature;
            return new PublicBetaClientRequestCompatibility(
                evidence.Domain,
                evidence.Opcode,
                evidence.PublicBetaName,
                PublicBetaCurrentWireDisposition.CurrentWireCompatibilityAdapterReady,
                $"Current-build vendor-cart publish and cart actions keep stable raw transport boundaries; raw compatibility adapter preserves opcode+payload bytes for {vendorFeature}.");
        }

        if (string.Equals(evidence.Domain, "character", StringComparison.Ordinal) &&
            (evidence.Opcode == PublicBetaCompatibilityGameplayRequestWireAdapter.CharacterDeleteRequestOpcode ||
             evidence.Opcode == PublicBetaCompatibilityGameplayRequestWireAdapter.CharacterSelectRequestOpcode ||
             evidence.Opcode == PublicBetaCompatibilityGameplayRequestWireAdapter.CharacterCreate1bRequestOpcode))
        {
            var characterFeature = evidence.GameFeature == "待補功能對照"
                ? "character lifecycle operation"
                : evidence.GameFeature;
            return new PublicBetaClientRequestCompatibility(
                evidence.Domain,
                evidence.Opcode,
                evidence.PublicBetaName,
                PublicBetaCurrentWireDisposition.CurrentWireCompatibilityAdapterReady,
                $"The current and public-beta character lifecycle records preserve the raw record boundary for {characterFeature}; lifecycle persistence is gated behind evidence-gated runtime mutation.");
        }

        if (evidence.PromotionStatus == PublicBetaPromotionStatus.CurrentBuildVerified)
        {
            var verifiedFeature = evidence.GameFeature == "待補功能對照"
                ? "verified gameplay request"
                : evidence.GameFeature;
            return new PublicBetaClientRequestCompatibility(
                evidence.Domain,
                evidence.Opcode,
                evidence.PublicBetaName,
                PublicBetaCurrentWireDisposition.CurrentWireVerified,
                $"Exact-current evidence owns the decoder for {verifiedFeature}; public-beta evidence only corroborates the canonical meaning.");
        }

        var fallbackFeature = evidence.GameFeature == "待補功能對照"
            ? "unresolved request semantics"
            : evidence.GameFeature;
        var reason = evidence.LengthComparison == PublicBetaLengthComparison.Changed
            ? $"The canonical command is applied for {fallbackFeature}, but the current-build frame requires a field-by-field version adapter because its shape changed."
            : $"The canonical command is applied for {fallbackFeature}, but current field semantics still require an independently verified version adapter.";
        return new PublicBetaClientRequestCompatibility(
            evidence.Domain,
            evidence.Opcode,
            evidence.PublicBetaName,
            PublicBetaCurrentWireDisposition.CurrentWireAdapterRequired,
            reason);
    }

    private static string LabelPartyOpcode(byte opcode) =>
        opcode switch
        {
            PublicBetaCompatibilityGameplayRequestWireAdapter.PartyRequest8FOpcode => "0x8F",
            PublicBetaCompatibilityGameplayRequestWireAdapter.PartyRequest90Opcode => "0x90",
            PublicBetaCompatibilityGameplayRequestWireAdapter.PartyRequest91Opcode => "0x91",
            PublicBetaCompatibilityGameplayRequestWireAdapter.PartyRequest92Opcode => "0x92",
            PublicBetaCompatibilityGameplayRequestWireAdapter.PartyRequest94Opcode => "0x94",
            _ => $"0x{opcode:X2}",
        };

    private static int CompatibilityAdapterCount(string domain) => domain switch
    {
        "combat" => PublicBetaCompatibilityBattleWireAdapter.CurrentServerOutputAdapterReadyCount,
        "gameplay" => PublicBetaCompatibilityGameplayWireAdapter.CurrentServerOutputAdapterReadyCount,
        _ => 0
    };
}

