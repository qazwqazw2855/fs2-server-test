using God2.ServerV2.Persistence;
using God2.ServerV2.Protocol;

namespace God2.ServerV2.Persistence.IntegrationTests;

public sealed class MariaDbNpcSnapshotRepositoryTests
{
    [Fact]
    [Trait("Category", "Integration")]
    public async Task KunlunMap_HasExpectedNpcSnapshot()
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable(
                    "GOD2_RUN_DB_INTEGRATION"),
                "1",
                StringComparison.Ordinal))
        {
            return;
        }

        var repository = new MariaDbNpcSnapshotRepository(
            new MariaDbAuthenticationOptions(
                Required("GOD2_DB_HOST"),
                int.Parse(Required("GOD2_DB_PORT")),
                Required("GOD2_DB_USER"),
                Required("GOD2_DB_PASSWORD")));

        var entries = await repository.ListByMapAsync(
            1675308248,
            CancellationToken.None);

        Assert.Equal(2, entries.Count);

        Assert.Collection(
            entries,
            first =>
            {
                Assert.Equal(1310005042, first.SpawnId);
                Assert.Equal("大仙童", first.Name);
                Assert.Equal((uint)5042, first.ClientEntityHandle);
                Assert.Equal(74, first.PositionX);
                Assert.Equal(124, first.PositionY);
                Assert.Equal((byte)0, first.ResourceType);
                Assert.Equal((byte)45, first.ResourceOrdinal);
                Assert.Equal("Derived", first.WireEvidenceStatus);
                Assert.Equal(
                    "3F25673AE985BF8F4818F2EE1254AB019B0406B8BD1C38C44F701DD5BAE27834",
                    first.SpawnMessageSha256);
            },
            second =>
            {
                Assert.Equal(1310005096, second.SpawnId);
                Assert.Equal("法寶仙童", second.Name);
                Assert.Equal((uint)5096, second.ClientEntityHandle);
                Assert.Equal(73, second.PositionX);
                Assert.Equal(121, second.PositionY);
                Assert.Equal((byte)0, second.ResourceType);
                Assert.Equal((byte)45, second.ResourceOrdinal);
                Assert.Equal("Derived", second.WireEvidenceStatus);
                Assert.Equal(
                    "78230A74DC17C388FE1A6FFDF6EBBD284A05C3E77E20719BE1921941903CC9B7",
                    second.SpawnMessageSha256);
            });
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task NineHeavensIceHouse_LoadsVerifiedDialogNpcSpawn()
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable(
                    "GOD2_RUN_DB_INTEGRATION"),
                "1",
                StringComparison.Ordinal))
        {
            return;
        }

        var repository = new MariaDbNpcSnapshotRepository(
            new MariaDbAuthenticationOptions(
                Required("GOD2_DB_HOST"),
                int.Parse(Required("GOD2_DB_PORT")),
                Required("GOD2_DB_USER"),
                Required("GOD2_DB_PASSWORD")));

        var entries = await repository.ListByMapAsync(
            170015007,
            CancellationToken.None);

        var entry = Assert.Single(
            entries,
            candidate =>
                candidate.SpawnId == 170153793 &&
                candidate.ClientEntityHandle == 3793);

        Assert.Equal(170015087, entry.NpcId);
        Assert.Equal("實測對話 NPC 3793", entry.Name);
        Assert.Equal(170015007, entry.MapId);
        Assert.Equal(65, entry.PositionX);
        Assert.Equal(61, entry.PositionY);
        Assert.Equal((byte)0, entry.ResourceType);
        Assert.Equal((byte)87, entry.ResourceOrdinal);
        Assert.Equal((byte)3, entry.SelectorHighBits);
        Assert.Equal((byte)5, entry.DirectionCode);
        Assert.Equal((byte)1, entry.StateCode);
        Assert.Equal(
            OfficialNpcSpawnCodec.ClientBuildId,
            entry.ClientBuildId);
        Assert.Equal(
            OfficialNpcSpawnCodec
                .LiveDialog3793SpawnMessageSha256,
            entry.SpawnMessageSha256);
        Assert.Equal(
            OfficialNpcSpawnCodec
                .TypeZeroOpaqueTemplateSha256,
            entry.OpaqueTemplateSha256);
        Assert.Equal(
            "Verified",
            entry.WireEvidenceStatus);
        Assert.Contains(
            "LiveRecovery/attempt-759-decode-2579",
            entry.WireEvidenceReference,
            StringComparison.Ordinal);

        var encoded =
            OfficialNpcSpawnCodec.Encode(
                new OfficialNpcSpawnEvidence(
                    entry.ClientBuildId,
                    entry.ClientEntityHandle,
                    entry.ResourceType,
                    entry.ResourceOrdinal,
                    entry.SelectorHighBits,
                    entry.DirectionCode,
                    entry.StateCode,
                    entry.PositionX,
                    entry.PositionY,
                    entry.SpawnMessageSha256,
                    entry.OpaqueTemplateSha256,
                    entry.WireEvidenceStatus,
                    entry.WireEvidenceReference));

        Assert.True(encoded.Succeeded, encoded.Reason);
        Assert.Equal(
            OfficialNpcSpawnCodec.FrameLength,
            encoded.Frame.Length);

        Assert.True(
            OfficialNpcSpawnCodec.TryDecodeHandle(
                encoded.Frame,
                out var decodedHandle));
        Assert.Equal(
            OfficialNpcSpawnCodec.LiveDialog3793Handle,
            decodedHandle);
    }

    private static string Required(string name) =>
        Environment.GetEnvironmentVariable(name)
            is { Length: > 0 } value
            ? value
            : throw new InvalidOperationException(
                $"Required environment variable is missing: {name}");
}
