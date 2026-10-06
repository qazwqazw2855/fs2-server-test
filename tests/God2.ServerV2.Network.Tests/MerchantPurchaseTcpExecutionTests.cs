using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;
using God2.ServerV2.Application;
using God2.ServerV2.Protocol;
using God2.ServerV2.Session;

namespace God2.ServerV2.Network.Tests;

[Collection("Merchant TCP")]
public sealed class MerchantPurchaseTcpExecutionTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public async Task TcpExecutionHandlesCommitAndUncertainty(int scenario)
    {
        const bool walletMissing = false;
        var repos = new Repositories(walletMissing) { Scenario = scenario };
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
            walletRepository: repos,
            merchantPurchaseWriter: repos,
            merchantSaleWriter: repos);

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
                        25000, out var expected, out _));
                    Assert.Equal(expected, response);
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
        Assert.Equal(1, repos.PurchaseCalls);
        Assert.Equal(0, repos.SaleCalls);
        Assert.NotNull(repos.LastPurchase);
        Assert.Equal(7, repos.LastPurchase!.CharacterId);
        Assert.Equal(99, repos.LastPurchase.MerchantId);
        Assert.Equal(253231541, repos.LastPurchase.ItemId);
        Assert.Equal(3, repos.LastPurchase.ExpectedInventoryVersion);
        Assert.Equal(4, repos.LastPurchase.ExpectedMutationSequence);
        Assert.Equal(5, repos.LastPurchase.ExpectedWalletVersion);
        Assert.Equal(1, repos.CatalogCalls);
        Assert.Equal(scenario is 0 or 1 ? 4 : 3, repos.InventoryCalls);
        Assert.Equal(1, repos.WalletCalls);
        Assert.DoesNotContain("Merchant BUY preparation:", log);

        if (scenario == 3)
        {
            Assert.Contains("Merchant BUY execution uncertain:", log);
            Assert.Contains("automaticRetry=None; closing connection.", log);
        }
        else
        {
            Assert.Contains("Merchant BUY execution:", log);
            Assert.Contains(scenario switch
            {
                0 => "reason=; reconciliation=False; wireResponse=Purchase0x3B",
                1 => "reason=CommittedInventoryMismatch; reconciliation=True; wireResponse=None",
                2 => "reason=EvidenceBlocked; reconciliation=False; wireResponse=None",
                4 => "reason=ReplayRequiresReconciliation; reconciliation=True; wireResponse=None",
                _ => throw new InvalidOperationException()
            }, log);
        }

        if (scenario is 0 or 1)
        {
            Assert.Equal(4L, repos.Inventory.Version);
            Assert.Equal(5L, repos.Inventory.MutationSequence);
            Assert.Single(repos.Inventory.Slots);
            Assert.Equal(25000L, repos.Wallet!.Balance);
            Assert.Equal(6L, repos.Wallet.Version);
        }
        else
        {
            Assert.Equal(3L, repos.Inventory.Version);
            Assert.Empty(repos.Inventory.Slots);
            Assert.Equal(50000L, repos.Wallet!.Balance);
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
        ICharacterWalletSnapshotRepository,
        IMerchantPurchaseWriter,
        IMerchantSaleWriter
    {
        private const long MapId = 1675308248;
        private const long SpawnId = 9001;
        private const long NpcId = 8001;

        public int Scenario { get; init; }
        public MerchantPurchaseRequest? LastPurchase { get; private set; }
        public int PurchaseCalls { get; private set; }
        public int SaleCalls { get; private set; }
        public int CatalogCalls { get; private set; }
        public int InventoryCalls { get; private set; }
        public int WalletCalls { get; private set; }
        public (long, string, int, long) CatalogLookup { get; private set; }
        public long InventoryCharacter { get; private set; }
        public long WalletCharacter { get; private set; }
        public CharacterInventorySnapshot Inventory { get; private set; } =
            new(Guid.NewGuid(), 7, 8, 3, 4, "Clean", []);
        public CharacterWalletSnapshot? Wallet { get; private set; }

        public Repositories(bool walletMissing)
        {
            Wallet = walletMissing ? null : new(7, "Gold", 50000, 5);
        }

        public ValueTask<MerchantPurchaseResult> PurchaseAsync(
            MerchantPurchaseRequest request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            PurchaseCalls++;
            LastPurchase = request;
            if (Scenario == 3)
                throw new IOException("Synthetic uncertain writer result");

            var status = Scenario switch
            {
                2 => MerchantPurchaseStatus.EvidenceBlocked,
                4 => MerchantPurchaseStatus.Replayed,
                _ => MerchantPurchaseStatus.Purchased
            };
            if (status == MerchantPurchaseStatus.Purchased)
            {
                Inventory = Inventory with
                {
                    Version = 4,
                    MutationSequence = 5,
                    Slots = [new CharacterInventorySlot(
                        Scenario == 1 ? 1 : 0, 253231541, 1)]
                };
                Wallet = new(7, "Gold", 25000, 6);
            }
            return ValueTask.FromResult(new MerchantPurchaseResult(
                status, request.TransactionId, 50000, 25000, 6, 3, 4));
        }

        public ValueTask<MerchantSaleResult> SellAsync(
            MerchantSaleRequest request, CancellationToken cancellationToken)
        {
            SaleCalls++;
            throw new InvalidOperationException("BUY must not invoke sale writer.");
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
            // Distance 2 is a fixture policy, not a production evidence claim.
            return ValueTask.FromResult<MerchantInteractionAuthority?>(
                new(99, NpcId, SpawnId, MapId, 3954,
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
