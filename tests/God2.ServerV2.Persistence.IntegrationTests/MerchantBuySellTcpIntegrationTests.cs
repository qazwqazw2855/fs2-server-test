using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;
using God2.ServerV2.Application;
using God2.ServerV2.Network;
using God2.ServerV2.Persistence;
using MySqlConnector;
using God2.ServerV2.Protocol;
using God2.ServerV2.Session;

namespace God2.ServerV2.Persistence.IntegrationTests;

[Collection("InventoryGrantDatabase")]
public sealed class MerchantBuySellTcpIntegrationTests
{
    [Fact]
    [Trait("Category", "Integration")]
    public async Task TcpBuyThenSellCommitsAndReturnsBothResponses()
    {
        const int scenario = 0;
        var repos = await Repositories.CreateAsync();
        using var lifetime = new CancellationTokenSource();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var output = new StringWriter();
        var previousOutput = Console.Out;
        Console.SetOut(TextWriter.Synchronized(output));

        await using var server = new TcpGameServer(
            TcpServerOptions.Loopback(0),
            new SessionRegistry(),
            new LoginService(repos),
            new CharacterListService(repos),
            npcSnapshotService: new NpcSnapshotService(repos),
            inventoryRepository: repos,
            merchantInteractionBindingProvider:
                new MerchantInteractionBindingProvider(repos),
            merchantCatalogRepository: repos,
            walletRepository: repos,
            merchantPurchaseWriter: repos,
            merchantSaleWriter: repos,
            economyRepository: repos,
            itemIdentityRepository: repos,
            enableMerchantSaleExecution: true);

        Task? running = null;
        try
        {
            running = server.RunAsync(lifetime.Token);
            var port = server.LocalEndpoint.Port;
            Assert.InRange(port, 1, ushort.MaxValue);

            using (var login = new TcpClient())
            {
                await login.ConnectAsync(IPAddress.Loopback, port, timeout.Token);
                var stream = login.GetStream();
                Assert.Equal(
                    OfficialLoginHandshakeProtocol.ServerHandshakeFrame.ToArray(),
                    await ReadFrame(stream, timeout.Token));
                await stream.WriteAsync(
                    OfficialLoginHandshakeProtocol.ExpectedClientHandshakeFrame,
                    timeout.Token);
                Assert.Equal(
                    OfficialLoginHandshakeProtocol.VersionFollowUpFrame.ToArray(),
                    await ReadFrame(stream, timeout.Token));

                var request = LoginRequest();
                try
                {
                    await stream.WriteAsync(request, timeout.Token);
                }
                finally
                {
                    Array.Clear(request);
                }

                Assert.Equal(
                    OfficialLoginSuccessCodec.FrameLength,
                    (await ReadFrame(stream, timeout.Token)).Length);
                await stream.WriteAsync(
                    Convert.FromHexString("06009202CE97"), timeout.Token);
                var characters = await ReadFrame(stream, timeout.Token);
                Assert.NotEmpty(characters);
                Assert.Equal(
                    0, await stream.ReadAsync(new byte[1], timeout.Token));
            }

            using (var world = new TcpClient())
            {
                await world.ConnectAsync(IPAddress.Loopback, port, timeout.Token);
                var stream = world.GetStream();
                Assert.Equal(
                    OfficialWorldHandshakeProtocol.ServerHandshakeFrame.ToArray(),
                    await ReadFrame(stream, timeout.Token));
                await stream.WriteAsync(
                    OfficialWorldHandshakeProtocol.ExpectedClientHandshakeFrame,
                    timeout.Token);
                Assert.Equal(
                    OfficialWorldHandshakeProtocol.FirstFollowUpFrame.ToArray(),
                    await ReadFrame(stream, timeout.Token));

                var lengths = new[]
                {
                    OfficialWorldBootstrapCodec.PlayerSpawnFrameLength,
                    320, 752, 68, 182, 36, 63, 42, 67, 88, 26
                };
                var total = 0;
                foreach (var length in lengths)
                {
                    var frame = await ReadFrame(stream, timeout.Token);
                    Assert.Equal(length, frame.Length);
                    total += frame.Length;
                }
                Assert.Equal(OfficialWorldBootstrapCodec.PayloadLength, total);

                var spawn = await ReadFrame(stream, timeout.Token);
                Assert.True(
                    OfficialNpcSpawnCodec.TryDecodeHandle(spawn, out var handle));
                Assert.Equal(3954U, handle);

                // Synthetic authorization/catalog fixture. It does not enable
                // a formal merchant or establish official transaction evidence.
                await stream.WriteAsync(
                    OfficialNpcInteractionCodec.EncodeOpen(3954), timeout.Token);
                await stream.WriteAsync(
                    OfficialMerchantTransactionCodec.EncodeRequest(
                        3954, 6901, 1,
                        OfficialMerchantTransactionOperation.Buy, 7),
                    timeout.Token);

                // Receive the supported response before logout; rejected,
                // uncertain or unreconciled execution must produce EOF.
                if (scenario == 0)
                {
                    var response = await ReadFrame(stream, timeout.Token);
                    Assert.Equal(57, response.Length);
                    Assert.True(OfficialMerchantPurchaseResultCodec.TryEncode(
                        OfficialMerchantPurchaseResultCodec.ClientBuildId,
                        new(3954, 6901, 1,
                            OfficialMerchantTransactionOperation.Buy, 7),
                        60, out var expected, out _));
                    Assert.Equal(expected, response);

                    // Verify the purchase independently before sending SELL.
                    var purchased = await repos.GetByCharacterAsync(
                        repos.CharacterId, timeout.Token);
                    Assert.NotNull(purchased);
                    var purchasedSlot = Assert.Single(purchased.Slots);
                    Assert.Equal(0, purchasedSlot.SlotIndex);
                    Assert.Equal(253231541, purchasedSlot.ItemId);
                    Assert.Equal(1, purchasedSlot.Quantity);
                    Assert.Equal(1, purchased.Version);
                    Assert.Equal(1, purchased.MutationSequence);

                    await stream.WriteAsync(
                        OfficialMerchantTransactionCodec.EncodeRequest(
                            3954, 6901, 1,
                            OfficialMerchantTransactionOperation.Sell, 4),
                        timeout.Token);
                    var saleResponse = await ReadFrame(stream, timeout.Token);
                    Assert.Equal(25, saleResponse.Length);
                    Assert.True(OfficialMerchantSaleResultCodec.TryEncode(
                        OfficialMerchantSaleResultCodec.ClientBuildId,
                        new(3954, 6901, 1,
                            OfficialMerchantTransactionOperation.Sell, 4),
                        64, out var expectedSale, out _));
                    Assert.Equal(expectedSale, saleResponse);

                    await stream.WriteAsync(
                        Convert.FromHexString("0500AC9D30"), timeout.Token);
                }
                // Other outcomes close without emitting a purchase response.
                Assert.Equal(
                    0, await stream.ReadAsync(new byte[1], timeout.Token));
            }
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException(
                "Merchant TCP fixture failed. Server log:\n" +
                output.ToString(), exception);
        }
        finally
        {
            lifetime.Cancel();
            try
            {
                if (running is not null)
                    await running.WaitAsync(TimeSpan.FromSeconds(5));
            }
            finally
            {
                Console.SetOut(previousOutput);
            }
        }

        var log = output.ToString();
        Assert.Contains(
            "reason=; reconciliation=False; wireResponse=Purchase0x3B", log);
        Assert.Contains(
            "reason=; reconciliation=False; wireResponse=Sale0x41", log);
        Assert.DoesNotContain("uncertain:", log);
        Assert.Equal(1, repos.PurchaseCalls);
        Assert.Equal(1, repos.SaleCalls);
        Assert.NotNull(repos.LastPurchase);
        Assert.NotNull(repos.LastSale);

        var sale = repos.LastSale!;
        Assert.Equal(repos.CharacterId, sale.CharacterId);
        Assert.Equal(repos.MerchantId, sale.MerchantId);
        Assert.Equal(253231541, sale.ItemId);
        Assert.True(sale.ItemInstanceId > 0);
        Assert.Equal(0, sale.SlotIndex);
        Assert.Equal(1, sale.Quantity);
        Assert.Equal(1, sale.ExpectedInventoryVersion);
        Assert.Equal(1, sale.ExpectedMutationSequence);
        Assert.Equal(1, sale.ExpectedWalletVersion);
        Assert.NotEqual(repos.LastPurchase!.TransactionId, sale.TransactionId);

        async Task VerifyFinalState()
        {
            var inventory = await repos.GetByCharacterAsync(
                repos.CharacterId, CancellationToken.None);
            var wallet = await repos.GetGoldByCharacterAsync(
                repos.CharacterId, CancellationToken.None);
            Assert.NotNull(inventory);
            Assert.NotNull(wallet);
            Assert.Empty(inventory.Slots);
            Assert.Equal(2, inventory.Version);
            Assert.Equal(2, inventory.MutationSequence);
            Assert.Equal(64, wallet.Balance);
            Assert.Equal(2, wallet.Version);
            foreach (var operation in new[] { "V2MerchantBuy", "V2MerchantSell" })
            {
                Assert.Equal(1, await repos.ScalarAsync(
                    "SELECT COUNT(*) FROM god2_player.inventory_transaction_idempotency " +
                    "WHERE CharacterId=@character AND OperationType='" + operation + "';"));
            }
        }

        await VerifyFinalState();
        var replay = await repos.SellAsync(sale, CancellationToken.None);
        Assert.Equal(MerchantSaleStatus.Replayed, replay.Status);
        Assert.Equal(sale.TransactionId, replay.TransactionId);
        Assert.Equal(64, replay.BalanceAfter);
        await VerifyFinalState();
        Console.WriteLine(
            "PASS: TCP BUY/SELL responses; wallet 100 -> 60 -> 64; unchanged SELL replay.");
    }

    private static async Task<byte[]> ReadFrame(
        Stream stream, CancellationToken cancellationToken)
    {
        var header = new byte[2];
        await stream.ReadExactlyAsync(header, cancellationToken);
        var length = BinaryPrimitives.ReadUInt16LittleEndian(header);
        Assert.InRange(length, 3, 8192);
        var frame = new byte[length];
        header.CopyTo(frame, 0);
        await stream.ReadExactlyAsync(
            frame.AsMemory(2), cancellationToken);
        return frame;
    }

    private static byte[] LoginRequest()
    {
        var decoded = new byte[OfficialLoginRequestCodec.FrameLength];
        try
        {
            BinaryPrimitives.WriteUInt16LittleEndian(
                decoded, OfficialLoginRequestCodec.FrameLength);
            Encoding.ASCII.GetBytes("tcpfixture").CopyTo(
                decoded, OfficialLoginRequestCodec.AccountOffset);
            Encoding.ASCII.GetBytes("fixture-only").CopyTo(
                decoded, OfficialLoginRequestCodec.PasswordOffset);
            decoded[^1] = OfficialLoginWireTransform.ComputeChecksum(decoded);
            return OfficialLoginWireTransform.Encode(decoded);
        }
        finally
        {
            Array.Clear(decoded);
        }
    }

    private sealed class Repositories :
        IAccountAuthenticator, ICharacterListRepository, INpcSnapshotRepository,
        IMerchantInteractionAuthorityRepository, IMerchantCatalogIdentityRepository,
        ICharacterInventorySnapshotRepository, ICharacterWalletSnapshotRepository,
        IMerchantPurchaseWriter, IMerchantSaleWriter,
        ICharacterEconomySnapshotRepository,
        ICharacterInventoryItemIdentityRepository
    {
        private const long MapId = 1675308248;
        private const long SpawnId = 9001;
        private const long NpcId = 8001;
        private readonly MariaDbAuthenticationOptions _options;
        private readonly MariaDbCharacterInventorySnapshotRepository _inventory;
        private readonly MariaDbCharacterWalletSnapshotRepository _wallet;
        private readonly MariaDbMerchantPurchaseWriter _writer;
        private readonly MariaDbMerchantSaleWriter _saleWriter;
        private readonly MariaDbCharacterEconomySnapshotRepository _economy;
        private readonly MariaDbCharacterInventoryItemIdentityRepository _items;
        public long CharacterId { get; }
        public long AccountId { get; private set; }
        public long MerchantId { get; }
        public int PurchaseCalls { get; private set; }
        public int SaleCalls { get; private set; }
        public int InventoryCalls { get; private set; }
        public int WalletCalls { get; private set; }
        public MerchantPurchaseRequest? LastPurchase { get; private set; }
        public MerchantSaleRequest? LastSale { get; private set; }

        private Repositories(
            MariaDbAuthenticationOptions options, long character, long merchant)
        {
            _options = options;
            CharacterId = character;
            MerchantId = merchant;
            _inventory = new(options);
            _wallet = new(options);
            _writer = new(options, new FixtureBuyGate(character, merchant));
            _saleWriter = new(options, new FixtureSaleGate(character, merchant));
            _economy = new(options);
            _items = new(options);
        }

        public static async Task<Repositories> CreateAsync()
        {
            static string Required(string name) =>
                Environment.GetEnvironmentVariable(name) is { Length: > 0 } value
                    ? value : throw new InvalidOperationException($"Missing {name}");

            Assert.Equal("1", Required("GOD2_RUN_DB_INTEGRATION"));
            Assert.Equal("127.0.0.1", Required("GOD2_DB_HOST"));
            Assert.Equal("3308", Required("GOD2_DB_PORT"));
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
            var repos = new Repositories(options, character, merchant);

            Assert.Equal(1, await repos.ScalarAsync("""
                SELECT COUNT(*) FROM god2_player.characters c
                JOIN god2_game.merchants m ON m.merchant_id=@merchant
                WHERE c.character_id=@character AND c.enabled=0
                  AND c.admin_note='MerchantPurchaseFixture'
                  AND c.name LIKE 'shopfixture_%'
                  AND m.admin_note=c.name;
                """));
            repos.AccountId = await repos.ScalarAsync("""
                SELECT account_id FROM god2_player.characters
                WHERE character_id=@character;
                """);
            var initial = await repos._inventory.GetByCharacterAsync(
                character, CancellationToken.None);
            var wallet = await repos._wallet.GetGoldByCharacterAsync(
                character, CancellationToken.None);
            Assert.NotNull(initial);
            Assert.Empty(initial.Slots);
            Assert.Equal(0, initial.Version);
            Assert.Equal(0, initial.MutationSequence);
            Assert.NotNull(wallet);
            Assert.Equal(100, wallet.Balance);
            Assert.Equal(0, wallet.Version);
            return repos;
        }

        public async Task<long> ScalarAsync(string sql)
        {
            await using var connection =
                new MySqlConnection(_options.BuildConnectionString());
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.Parameters.AddWithValue("@character", CharacterId);
            command.Parameters.AddWithValue("@merchant", MerchantId);
            return Convert.ToInt64(await command.ExecuteScalarAsync());
        }

        public ValueTask<AccountAuthenticationResult> ValidateCredentialsAsync(
            string accountName, ReadOnlyMemory<char> password,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            // Synthetic login adapter for this disabled fixture only.
            Assert.Equal("tcpfixture", accountName);
            Assert.Equal("fixture-only", password.ToString());
            return ValueTask.FromResult(
                AccountAuthenticationResult.Accepted(AccountId));
        }

        public ValueTask<IReadOnlyList<CharacterListEntry>> ListByAccountAsync(
            long accountId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Assert.Equal(AccountId, accountId);
            IReadOnlyList<CharacterListEntry> characters =
            [
                new(CharacterId, AccountId, "Fixture", "Swordsman", "Female",
                    null, 1, "Appearance1", MapId, 74, 124,
                    DateTimeOffset.UtcNow, null)
            ];
            return ValueTask.FromResult(characters);
        }

        public ValueTask<IReadOnlyList<NpcSnapshotEntry>> ListByMapAsync(
            long mapId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Assert.Equal(MapId, mapId);
            IReadOnlyList<NpcSnapshotEntry> npcs =
            [
                new(SpawnId, NpcId, "Fixture NPC", MapId, 74, 124,
                    OfficialNpcSpawnCodec.ClientBuildId, 3954,
                    0, 45, 3, 4, 1,
                    "EE7BEFB421B8788A8B38D4D842837F33B57005052CA23EE9700D8D62C84BB3C3",
                    OfficialNpcSpawnCodec.TypeZeroOpaqueTemplateSha256,
                    "Derived", "synthetic-3954-transport-fixture-not-official-capture")
            ];
            return ValueTask.FromResult(npcs);
        }

        public ValueTask<MerchantInteractionAuthority?> ResolveAsync(
            long spawnId, long mapId, string clientBuildId,
            uint clientEntityHandle, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Assert.Equal(SpawnId, spawnId);
            Assert.Equal(MapId, mapId);
            Assert.Equal(OfficialNpcSpawnCodec.ClientBuildId, clientBuildId);
            Assert.Equal(3954U, clientEntityHandle);
            return ValueTask.FromResult<MerchantInteractionAuthority?>(
                new(MerchantId, NpcId, SpawnId, MapId, 3954,
                    clientBuildId, "synthetic-db-tcp-policy", 2, true));
        }

        public ValueTask<MerchantCatalogIdentity?> ResolvePurchaseAsync(
            long merchantId, string build, int index, long item,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Assert.Equal(MerchantId, merchantId);
            Assert.Equal(OfficialNpcSpawnCodec.ClientBuildId, build);
            Assert.Equal(7, index);
            Assert.Equal(6901, item);
            // Synthetic catalog projection; DB writer validates the real listing.
            return ValueTask.FromResult<MerchantCatalogIdentity?>(
                new(MerchantId, MerchantId, 253231541, 7, 6901));
        }

        public ValueTask<CharacterInventorySnapshot?> GetByCharacterAsync(
            long characterId, CancellationToken cancellationToken)
        {
            Assert.Equal(CharacterId, characterId);
            InventoryCalls++;
            return _inventory.GetByCharacterAsync(characterId, cancellationToken);
        }

        public ValueTask<CharacterWalletSnapshot?> GetGoldByCharacterAsync(
            long characterId, CancellationToken cancellationToken)
        {
            Assert.Equal(CharacterId, characterId);
            WalletCalls++;
            return _wallet.GetGoldByCharacterAsync(characterId, cancellationToken);
        }

        public ValueTask<MerchantPurchaseResult> PurchaseAsync(
            MerchantPurchaseRequest request, CancellationToken cancellationToken)
        {
            PurchaseCalls++;
            LastPurchase = request;
            return _writer.PurchaseAsync(request, cancellationToken);
        }

        ValueTask<CharacterEconomySnapshot>
            ICharacterEconomySnapshotRepository.GetByCharacterAsync(
                long characterId, CancellationToken cancellationToken)
        {
            Assert.Equal(CharacterId, characterId);
            return _economy.GetByCharacterAsync(characterId, cancellationToken);
        }

        public ValueTask<CharacterInventoryItemIdentity?> GetBySlotAsync(
            long characterId, int authoritySlotIndex,
            CancellationToken cancellationToken)
        {
            Assert.Equal(CharacterId, characterId);
            Assert.Equal(0, authoritySlotIndex);
            return _items.GetBySlotAsync(
                characterId, authoritySlotIndex, cancellationToken);
        }

        public ValueTask<MerchantSaleResult> SellAsync(
            MerchantSaleRequest request, CancellationToken cancellationToken)
        {
            SaleCalls++;
            LastSale = request;
            return _saleWriter.SellAsync(request, cancellationToken);
        }
    }

    // Approval applies only to this disabled synthetic test character.
    private sealed class FixtureSaleGate(long character, long merchant)
        : IMerchantSaleEvidenceGate
    {
        public ValueTask<bool> IsApprovedAsync(
            MerchantSaleRequest request, MerchantSaleQuote quote,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(
                request.CharacterId == character &&
                request.MerchantId == merchant &&
                request.ItemId == 253231541 &&
                request.SlotIndex == 0 && request.Quantity == 1 &&
                quote == new MerchantSaleQuote(
                    merchant, 253231541, "Gold", 4, true, true));
        }
    }

    private sealed class FixtureBuyGate(long character, long merchant)
        : IMerchantPurchaseEvidenceGate
    {
        public ValueTask<bool> IsApprovedAsync(
            MerchantPurchaseRequest request, MerchantPurchaseQuote quote,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(
                request.CharacterId == character &&
                request.MerchantId == merchant &&
                quote == new MerchantPurchaseQuote(
                    merchant, 253231541, "Gold", 40, 1, true));
        }
    }
}
