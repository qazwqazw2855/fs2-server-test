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
public sealed class InventoryRestoreTcpBlockedIntegrationTests
{
    [Fact]
    [Trait("Category", "Integration")]
    public async Task UnsupportedRestoreLayoutClosesWithoutMutation()
    {
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
            enableRestrictedInventoryBootstrap: true,
            economyRepository: repos);

        Task? running = null;
        try
        {
            running = server.RunAsync(lifetime.Token);
            var port = server.LocalEndpoint.Port;
            Assert.InRange(port, 1, ushort.MaxValue);

            for (var round = 0; round < 1; round++)
            {
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

                // No NPC open or BUY is sent.
                // Unsupported inventory projection must close without a frame.
                // Other outcomes close without emitting a purchase response.
                Assert.Equal(
                    0, await stream.ReadAsync(new byte[1], timeout.Token));
            }
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
            "Inventory bootstrap rejected: reason=InventoryLayoutEvidenceBlocked.", log);
        Assert.DoesNotContain("Inventory bootstrap projection:", log);
        Assert.DoesNotContain("Merchant BUY execution", log);
        Assert.Equal(0, repos.PurchaseCalls);
        Assert.Equal(0, repos.SaleCalls);
        Assert.Null(repos.LastPurchase);
        Assert.Equal(1, repos.EconomyCalls);
        Assert.Equal(0, repos.InventoryCalls);
        Assert.Equal(0, repos.WalletCalls);

        var inventory = await repos.GetByCharacterAsync(
            repos.CharacterId, CancellationToken.None);
        var wallet = await repos.GetGoldByCharacterAsync(
            repos.CharacterId, CancellationToken.None);
        Assert.NotNull(inventory);
        Assert.NotNull(wallet);
        Assert.Empty(inventory!.Slots);
        Assert.Equal(0, inventory.Version);
        Assert.Equal(0, inventory.MutationSequence);
        Assert.Equal(100, wallet!.Balance);
        Assert.Equal(0, wallet.Version);
        Assert.Equal(0, await repos.ScalarAsync("""
            SELECT COUNT(*) FROM god2_player.inventory_transaction_idempotency
            WHERE CharacterId=@character AND OperationType='V2MerchantBuy';
            """));
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
        ICharacterEconomySnapshotRepository
    {
        private const long MapId = 1675308248;
        private const long SpawnId = 9001;
        private const long NpcId = 8001;
        private readonly MariaDbAuthenticationOptions _options;
        private readonly MariaDbCharacterInventorySnapshotRepository _inventory;
        private readonly MariaDbCharacterWalletSnapshotRepository _wallet;
        private readonly MariaDbMerchantPurchaseWriter _writer;
        private readonly MariaDbCharacterEconomySnapshotRepository _economy;
        public long CharacterId { get; }
        public long AccountId { get; private set; }
        public long MerchantId { get; }
        public int PurchaseCalls { get; private set; }
        public int SaleCalls { get; private set; }
        public int InventoryCalls { get; private set; }
        public int WalletCalls { get; private set; }
        public int EconomyCalls { get; private set; }
        public MerchantPurchaseRequest? LastPurchase { get; private set; }

        private Repositories(
            MariaDbAuthenticationOptions options, long character, long merchant)
        {
            _options = options;
            CharacterId = character;
            MerchantId = merchant;
            _inventory = new(options);
            _economy = new(options);
            _wallet = new(options);
            _writer = new(options, new FixtureBuyGate(character, merchant));
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

        async ValueTask<CharacterEconomySnapshot>
            ICharacterEconomySnapshotRepository.GetByCharacterAsync(
                long characterId, CancellationToken cancellationToken)
        {
            Assert.Equal(CharacterId, characterId);
            EconomyCalls++;
            var snapshot = await _economy.GetByCharacterAsync(
                characterId, cancellationToken);
            Assert.NotNull(snapshot.Inventory);
            // Synthetic unsupported read projection; no database mutation.
            return snapshot with
            {
                Inventory = snapshot.Inventory! with
                {
                    Slots = [
                        new CharacterInventorySlot(0, 253231541, 1),
                        new CharacterInventorySlot(1, 253231541, 1)]
                }
            };
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

        public ValueTask<MerchantSaleResult> SellAsync(
            MerchantSaleRequest request, CancellationToken cancellationToken)
        {
            SaleCalls++;
            throw new InvalidOperationException("BUY must not dispatch SELL.");
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
