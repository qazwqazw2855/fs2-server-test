using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;
using God2.ServerV2.Application;
using God2.ServerV2.Protocol;
using God2.ServerV2.Session;

namespace God2.ServerV2.Network.Tests;

[CollectionDefinition("Merchant TCP", DisableParallelization = true)]
public sealed class MerchantTcpCollection
{
}

[Collection("Merchant TCP")]
public sealed class MerchantPurchaseTcpTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BuyPreparationUsesRepositoriesAndEmitsNoTransactionResponse(
        bool walletMissing)
    {
        var repos = new Repositories(walletMissing);
        using var lifetime = new CancellationTokenSource();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
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
            walletRepository: repos);

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
                Assert.Equal(5042U, handle);

                // Synthetic authorization/catalog fixture. It does not enable
                // a formal merchant or establish official transaction evidence.
                await stream.WriteAsync(
                    OfficialNpcInteractionCodec.EncodeOpen(5042), timeout.Token);
                await stream.WriteAsync(
                    OfficialMerchantTransactionCodec.EncodeRequest(
                        5042, 6901, 1,
                        OfficialMerchantTransactionOperation.Buy, 7),
                    timeout.Token);

                // Ordered on the same stream: logout is processed after BUY.
                // EOF also proves no response was emitted before logout.
                await stream.WriteAsync(
                    Convert.FromHexString("0500AC9D30"), timeout.Token);
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
        var expected = walletMissing
            ? "interaction=Allowed; prepared=False; reason=WalletMissing;"
            : "interaction=Allowed; prepared=True; reason=;";
        Assert.Contains("Merchant BUY preparation: " + expected, log);
        Assert.Contains(
            "transactionExecution=BlockedNotWired; wireResponse=None", log);
        Assert.DoesNotContain("Merchant transaction rejected:", log);
        Assert.Equal(1, repos.CatalogCalls);
        Assert.Equal((99L, OfficialNpcSpawnCodec.ClientBuildId, 7, 6901L),
            repos.CatalogLookup);
        Assert.Equal(2, repos.InventoryCalls); // World entry and BUY preparation.
        Assert.Equal(1, repos.WalletCalls);
        Assert.Equal(7L, repos.InventoryCharacter);
        Assert.Equal(7L, repos.WalletCharacter);
        Assert.Equal(3L, repos.Inventory.Version);
        Assert.Equal(4L, repos.Inventory.MutationSequence);
        Assert.Empty(repos.Inventory.Slots);
        if (!walletMissing)
        {
            Assert.NotNull(repos.Wallet);
            Assert.Equal(100L, repos.Wallet!.Balance);
            Assert.Equal(5L, repos.Wallet.Version);
        }
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
        IAccountAuthenticator,
        ICharacterListRepository,
        INpcSnapshotRepository,
        IMerchantInteractionAuthorityRepository,
        IMerchantCatalogIdentityRepository,
        ICharacterInventorySnapshotRepository,
        ICharacterWalletSnapshotRepository
    {
        private const long MapId = 1675308248;
        private const long SpawnId = 9001;
        private const long NpcId = 8001;

        public int CatalogCalls { get; private set; }
        public int InventoryCalls { get; private set; }
        public int WalletCalls { get; private set; }
        public (long, string, int, long) CatalogLookup { get; private set; }
        public long InventoryCharacter { get; private set; }
        public long WalletCharacter { get; private set; }
        public CharacterInventorySnapshot Inventory { get; } =
            new(Guid.NewGuid(), 7, 8, 3, 4, "Clean", []);
        public CharacterWalletSnapshot? Wallet { get; }

        public Repositories(bool walletMissing)
        {
            Wallet = walletMissing ? null : new(7, "Gold", 100, 5);
        }

        public ValueTask<AccountAuthenticationResult> ValidateCredentialsAsync(
            string accountName, ReadOnlyMemory<char> password,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Assert.Equal("tcpfixture", accountName);
            Assert.Equal("fixture-only", password.ToString());
            return ValueTask.FromResult(AccountAuthenticationResult.Accepted(1));
        }

        public ValueTask<IReadOnlyList<CharacterListEntry>> ListByAccountAsync(
            long accountId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Assert.Equal(1L, accountId);
            IReadOnlyList<CharacterListEntry> characters =
            [
                new(7, 1, "Fixture", "Swordsman", "Female", null, 1, "Appearance1",
                    MapId, 74, 124, DateTimeOffset.UtcNow, null)
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
                    OfficialNpcSpawnCodec.ClientBuildId, 5042,
                    0, 45, 3, 4, 1,
                    "3F25673AE985BF8F4818F2EE1254AB019B0406B8BD1C38C44F701DD5BAE27834",
                    OfficialNpcSpawnCodec.TypeZeroOpaqueTemplateSha256,
                    "Derived", "test-fixture")
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
            Assert.Equal(5042U, clientEntityHandle);
            // Distance 2 is a fixture policy, not a production evidence claim.
            return ValueTask.FromResult<MerchantInteractionAuthority?>(
                new(99, NpcId, SpawnId, MapId, 5042,
                    clientBuildId, "synthetic-test-policy", 2, true));
        }

        public ValueTask<MerchantCatalogIdentity?> ResolvePurchaseAsync(
            long merchantId, string build, int index, long item,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CatalogCalls++;
            CatalogLookup = (merchantId, build, index, item);
            return ValueTask.FromResult<MerchantCatalogIdentity?>(
                new(500, 99, 253231541, 7, 6901));
        }

        public ValueTask<CharacterInventorySnapshot?> GetByCharacterAsync(
            long characterId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            InventoryCalls++;
            InventoryCharacter = characterId;
            return ValueTask.FromResult<CharacterInventorySnapshot?>(Inventory);
        }

        public ValueTask<CharacterWalletSnapshot?> GetGoldByCharacterAsync(
            long characterId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            WalletCalls++;
            WalletCharacter = characterId;
            return ValueTask.FromResult(Wallet);
        }
    }
}
