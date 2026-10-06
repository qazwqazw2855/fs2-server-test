using System.Text.Json;
using God2.ServerV2.Application;
using God2.ServerV2.Network;
using God2.ServerV2.Protocol;
using God2.ServerV2.Persistence;
using MySqlConnector;

namespace God2.ServerV2.Persistence.IntegrationTests;

[Collection("InventoryGrantDatabase")]
public sealed class MerchantCommandIntegrationTests
{
    [Fact]
    [Trait("Category", "Integration")]
    public async Task Current_interaction_dispatches_durable_buy_and_sell()
    {
        static string Required(string name) =>
            Environment.GetEnvironmentVariable(name) is { Length: > 0 } value
                ? value : throw new InvalidOperationException($"Missing {name}");

        Assert.Equal("1", Required("GOD2_RUN_DB_INTEGRATION"));
        var user = Required("GOD2_MERCHANT_COMMAND_FIXTURE_USER");
        Assert.StartsWith("shoptest_", user);
        var character = long.Parse(Required(
            "GOD2_MERCHANT_COMMAND_FIXTURE_CHARACTER_ID"));
        var merchant = long.Parse(Required(
            "GOD2_MERCHANT_COMMAND_FIXTURE_MERCHANT_ID"));
        Assert.True(character > 1);
        var options = new MariaDbAuthenticationOptions(
            Required("GOD2_DB_HOST"), int.Parse(Required("GOD2_DB_PORT")),
            user, Required("GOD2_MERCHANT_COMMAND_FIXTURE_PASSWORD"));

        await using var connection = new MySqlConnection(options.BuildConnectionString());
        await connection.OpenAsync();
        async Task<long> Scalar(string sql)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.Parameters.AddWithValue("@character", character);
            return Convert.ToInt64(await command.ExecuteScalarAsync());
        }

        Assert.Equal(1, await Scalar("""
            SELECT COUNT(*) FROM god2_player.characters
            WHERE character_id=@character AND enabled=0
              AND admin_note='MerchantPurchaseFixture'
              AND name LIKE 'shopfixture_%';
            """));
        var account = await Scalar("""
            SELECT account_id FROM god2_player.characters
            WHERE character_id=@character;
            """);
        var repository = new MariaDbCharacterInventorySnapshotRepository(options);
        var initial = await repository.GetByCharacterAsync(character, CancellationToken.None);
        Assert.NotNull(initial);
        Assert.Empty(initial.Slots);
        Assert.Equal(0, initial.Version);

        // Artificial in-memory world identity; not official NPC/wire approval.
        var now = DateTimeOffset.UtcNow;
        var presences = new WorldPresenceRegistry();
        var interactions = new NpcInteractionSessionRegistry();
        var npcs = new WorldNpcRegistry();
        var npc = new NpcSnapshotEntry(
            9001, 8001, "Command fixture", 100, 74, 124,
            OfficialMerchantPurchaseResultCodec.ClientBuildId, 3954, null, null, null, null, null,
            null, null, "EvidenceBlocked", "command-fixture");
        npcs.PublishMap(100, [npc]);
        Assert.True(presences.TryEnter(new WorldPresence(
            101, "command-fixture", account,
            new CharacterListEntry(
                character, account, "Fixture", null, null, null, 1, null,
                100, 74, 124, now, null), now)).Succeeded);
        var interactionId = interactions.TryOpen(
            101, character, 100, 3954, [npc], now).Session!.InteractionId;
        var binding = new MerchantInteractionBinding(
            merchant, 8001, 9001, 100, 3954,
            OfficialMerchantPurchaseResultCodec.ClientBuildId, 2, "command-fixture", true);
        var resolver = new MerchantInteractionResolver(interactions, presences, npcs);

        var lane = new ConnectionCommandLane();
        MerchantCommandService Service() => new(
            presences, resolver,
            new MariaDbMerchantPurchaseWriter(options, new BuyGate(character, merchant)),
            new MariaDbMerchantSaleWriter(options, new SaleGate(character, merchant)),
            lane);

        // Simulate a frame already holding this connection's lane.
        async Task<MerchantPurchaseCommandResult> PurchaseInFrame(
            long connectionId,
            Guid currentInteractionId,
            MerchantInteractionBinding currentBinding,
            MerchantPurchaseCommand command,
            CancellationToken cancellationToken)
        {
            using var lease = await lane.EnterAsync(cancellationToken);
            return await Service().PurchaseInLeaseAsync(
                lease, connectionId, currentInteractionId,
                currentBinding, command, cancellationToken);
        }

        async Task<MerchantSaleCommandResult> SellInFrame(
            long connectionId,
            Guid currentInteractionId,
            MerchantInteractionBinding currentBinding,
            MerchantSaleCommand command,
            CancellationToken cancellationToken)
        {
            using var lease = await lane.EnterAsync(cancellationToken);
            return await Service().SellInLeaseAsync(
                lease, connectionId, currentInteractionId,
                currentBinding, command, cancellationToken);
        }

        var buy = new MerchantPurchaseCommand(
            Guid.NewGuid(), "command-buy", 253231541, 1,
            initial.InventoryId, 0, 0, 0);

        // Synthetic authority only; this does not approve a formal merchant.
        var selection = new OfficialMerchantTransactionSelection(
            3954, 6901, 1, OfficialMerchantTransactionOperation.Buy, 7);

        async Task<MerchantPurchaseExecutionResult> ExecuteInFrame(
            MerchantCommandService service,
            MerchantPurchaseCommand command)
        {
            using var lease = await lane.EnterAsync(CancellationToken.None);
            var coordinator = new MerchantPurchaseExecutionService(
                lane, presences, resolver, service, repository);
            return await coordinator.ExecuteInLeaseAsync(
                lease, 101, interactionId, binding, selection,
                command, CancellationToken.None);
        }

        var blockedService = new MerchantCommandService(
            presences, resolver,
            new MariaDbMerchantPurchaseWriter(
                options, new BlockedMerchantPurchaseEvidenceGate()),
            new MariaDbMerchantSaleWriter(
                options, new SaleGate(character, merchant)),
            lane);
        var blocked = await ExecuteInFrame(blockedService, buy);
        Assert.Equal(MerchantPurchaseStatus.EvidenceBlocked,
            blocked.TransactionResult!.Status);
        Assert.Null(blocked.EncodedResponse);
        Assert.False(blocked.RequiresReconciliation);
        Assert.Equal(0, await Scalar("""
            SELECT COUNT(*) FROM god2_player.inventory_transaction_idempotency
            WHERE CharacterId=@character;
            """));
        Assert.Equal(100, await Scalar("""
            SELECT Balance FROM god2_player.player_currency_balances
            WHERE CharacterId=@character AND CurrencyType='Gold';
            """));

        Assert.True(presences.TryMove(101, character, 100, 124, out _));
        var rejected = await PurchaseInFrame(
            101, interactionId, binding, buy, CancellationToken.None);
        Assert.Equal(MerchantInteractionStatus.OutOfRange, rejected.InteractionStatus);
        Assert.Null(rejected.TransactionResult);
        Assert.Equal(100, await Scalar("""
            SELECT Balance FROM god2_player.player_currency_balances
            WHERE CharacterId=@character AND CurrencyType='Gold';
            """));
        Assert.Equal(0, await Scalar("""
            SELECT COUNT(*) FROM god2_player.inventory_transaction_idempotency
            WHERE CharacterId=@character;
            """));

        Assert.True(presences.TryMove(101, character, 74, 124, out _));
        var purchased = await ExecuteInFrame(Service(), buy);
        Assert.Equal(MerchantInteractionStatus.Allowed, purchased.InteractionStatus);
        Assert.NotNull(purchased.TransactionResult);
        Assert.Equal(MerchantPurchaseStatus.Purchased, purchased.TransactionResult.Status);
        Assert.Equal(60, purchased.TransactionResult.BalanceAfter);
        Assert.Equal(1, purchased.TransactionResult.InventoryVersionAfter);
        Assert.Equal("", purchased.FailureCode);
        Assert.False(purchased.RequiresReconciliation);
        Assert.True(OfficialMerchantPurchaseResultCodec.TryEncode(
            OfficialMerchantPurchaseResultCodec.ClientBuildId,
            selection, 60, out var expectedResponse, out _));
        Assert.Equal(expectedResponse, purchased.EncodedResponse);
        Assert.NotNull(purchased.EncodedResponse);
        Assert.Equal(57, purchased.EncodedResponse.Length);

        // A second fresh BUY cannot use this empty-inventory response profile.
        var secondBuy = buy with
        {
            TransactionId = Guid.NewGuid(),
            IdempotencyKey = "command-second-buy-blocked",
            ExpectedInventoryVersion = 1,
            ExpectedMutationSequence = 1,
            ExpectedWalletVersion = 1
        };
        var layoutBlocked = await ExecuteInFrame(Service(), secondBuy);
        Assert.Null(layoutBlocked.TransactionResult);
        Assert.Null(layoutBlocked.EncodedResponse);
        Assert.Equal("InventoryLayoutEvidenceBlocked",
            layoutBlocked.FailureCode);
        Assert.Equal(60, await Scalar("""
            SELECT Balance FROM god2_player.player_currency_balances
            WHERE CharacterId=@character AND CurrencyType='Gold';
            """));
        Assert.Equal(1, await Scalar("""
            SELECT COUNT(*) FROM god2_player.inventory_transaction_idempotency
            WHERE CharacterId=@character AND OperationType='V2MerchantBuy';
            """));

        var replay = await PurchaseInFrame(
            101, interactionId, binding, buy, CancellationToken.None);
        Assert.Equal(MerchantPurchaseStatus.Replayed, replay.TransactionResult!.Status);

        var beforeSale = await repository.GetByCharacterAsync(character, CancellationToken.None);
        Assert.NotNull(beforeSale);
        var slot = Assert.Single(beforeSale.Slots);
        Assert.Equal(1, slot.Quantity);
        var instance = await Scalar("""
            SELECT inventory_id FROM god2_player.character_inventory
            WHERE character_id=@character AND enabled=1 AND deleted_at_utc IS NULL;
            """);
        var slotVersion = await Scalar("""
            SELECT slot_version FROM god2_player.character_inventory
            WHERE character_id=@character AND enabled=1 AND deleted_at_utc IS NULL;
            """);
        var sale = new MerchantSaleCommand(
            Guid.NewGuid(), "command-sell", 253231541, instance, slot.SlotIndex, 1,
            beforeSale.InventoryId, beforeSale.Version, beforeSale.MutationSequence,
            slotVersion, 1);

        Assert.Equal(NpcInteractionCloseStatus.Closed,
            interactions.TryClose(101, 3954, out _));
        var closedSale = await SellInFrame(
            101, interactionId, binding, sale, CancellationToken.None);
        Assert.Equal(MerchantInteractionStatus.InteractionConflict,
            closedSale.InteractionStatus);
        Assert.Null(closedSale.TransactionResult);
        var unchanged = await repository.GetByCharacterAsync(character, CancellationToken.None);
        Assert.Equal(JsonSerializer.Serialize(beforeSale), JsonSerializer.Serialize(unchanged));

        var reopenedId = interactions.TryOpen(
            101, character, 100, 3954, [npc], now).Session!.InteractionId;
        Assert.NotEqual(interactionId, reopenedId);
        var oldSale = await SellInFrame(
            101, interactionId, binding, sale, CancellationToken.None);
        Assert.Equal(MerchantInteractionStatus.InteractionConflict, oldSale.InteractionStatus);

        var sold = await SellInFrame(
            101, reopenedId, binding, sale, CancellationToken.None);
        Assert.Equal(MerchantSaleStatus.Sold, sold.TransactionResult!.Status);
        Assert.Equal(64, sold.TransactionResult.BalanceAfter);
        var saleReplay = await SellInFrame(
            101, reopenedId, binding, sale, CancellationToken.None);
        Assert.Equal(MerchantSaleStatus.Replayed, saleReplay.TransactionResult!.Status);

        var final = await repository.GetByCharacterAsync(character, CancellationToken.None);
        Assert.NotNull(final);
        Assert.Empty(final.Slots);
        Assert.Equal(2, final.Version);
        Assert.Equal(2, final.MutationSequence);
        Assert.Equal(64, await Scalar("""
            SELECT Balance FROM god2_player.player_currency_balances
            WHERE CharacterId=@character AND CurrencyType='Gold';
            """));
        Assert.Equal(2, await Scalar("""
            SELECT Version FROM god2_player.player_currency_balances
            WHERE CharacterId=@character AND CurrencyType='Gold';
            """));
        Assert.Equal(2, await Scalar("""
            SELECT COUNT(*) FROM god2_player.inventory_transaction_idempotency
            WHERE CharacterId=@character
              AND OperationType IN ('V2MerchantBuy','V2MerchantSell');
            """));

        // No frame lease remains after the durable operations.
        var cleanupCalls = 0;
        await lane.CloseAsync(() =>
        {
            cleanupCalls++;
            interactions.Remove(101, out _);
            presences.TryLeave(101, out _);
            return ValueTask.CompletedTask;
        }).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, cleanupCalls);
        Assert.False(interactions.TryGetByConnection(101, out _));
        Assert.False(presences.TryGetByConnection(101, out _));
    }

    private sealed class BuyGate(long character, long merchant) : IMerchantPurchaseEvidenceGate
    {
        public ValueTask<bool> IsApprovedAsync(
            MerchantPurchaseRequest request, MerchantPurchaseQuote quote,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(request.CharacterId == character &&
                request.MerchantId == merchant &&
                quote == new MerchantPurchaseQuote(merchant, 253231541, "Gold", 40, 1, true));
    }

    private sealed class SaleGate(long character, long merchant) : IMerchantSaleEvidenceGate
    {
        public ValueTask<bool> IsApprovedAsync(
            MerchantSaleRequest request, MerchantSaleQuote quote,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(request.CharacterId == character &&
                request.MerchantId == merchant &&
                quote == new MerchantSaleQuote(merchant, 253231541, "Gold", 4, true, true));
    }
}
