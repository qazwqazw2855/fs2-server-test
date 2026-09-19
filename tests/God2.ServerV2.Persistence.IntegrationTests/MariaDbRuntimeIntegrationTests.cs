using God2.ServerV2.Application;
using God2.ServerV2.Persistence;
using MySqlConnector;

namespace God2.ServerV2.Persistence.IntegrationTests;

public sealed class MariaDbRuntimeIntegrationTests
{
    [Fact]
    [Trait("Category", "Integration")]
    public async Task God2TestAccount_AcceptsCorrectPassword_AndRejectsWrongPassword()
    {
        if (!ShouldRun() ||
            Environment.GetEnvironmentVariable("GOD2_TEST_PASSWORD")
                is not { Length: > 0 } password)
        {
            return;
        }
        var authenticator = new MariaDbAccountAuthenticator(
            CreateOptions(),
            new Pbkdf2Sha256PasswordHashVerifier());

        var accepted = await authenticator.ValidateCredentialsAsync(
            "god2test",
            password.AsMemory(),
            CancellationToken.None);

        var rejected = await authenticator.ValidateCredentialsAsync(
            "god2test",
            "definitely-wrong-password".AsMemory(),
            CancellationToken.None);

        Assert.Equal(AccountAuthenticationResult.Accepted(1), accepted);
        Assert.Equal(AccountAuthenticationResult.Rejected, rejected);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task AccountOne_HasExpectedTestCharacter()
    {
        if (!ShouldRun())
        {
            return;
        }

        var repository = new MariaDbCharacterListRepository(CreateOptions());

        var characters = await repository.ListByAccountAsync(
            1,
            CancellationToken.None);

        var character = Assert.Single(characters);

        Assert.Equal(1, character.CharacterId);
        Assert.Equal(1, character.AccountId);
        Assert.Equal("test001", character.Name);
        Assert.Equal("Swordsman", character.ClassCode);
        Assert.Equal("Female", character.GenderCode);
        Assert.Equal(1, character.Level);
        Assert.Contains(character.MapId, new long?[] { 170015000, 170015007 });
        Assert.True(character.RuntimeVersion > 0);
        Assert.Equal(32, character.ConcurrencyToken.Length);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task StaleCharacterPositionWrite_IsRejectedWithoutMutation()
    {
        if (!ShouldRun())
        {
            return;
        }

        var options = CreateOptions();
        var repository = new MariaDbCharacterListRepository(options);
        var before = Assert.Single(
            await repository.ListByAccountAsync(
                1,
                CancellationToken.None));

        var writer = new MariaDbCharacterPositionWriter(options);
        var result = await writer.TryUpdateAsync(
            new CharacterPositionWriteRequest(
                before.CharacterId,
                16,
                14,
                before.RuntimeVersion + 100,
                before.ConcurrencyToken),
            CancellationToken.None);

        Assert.False(result.Updated);

        var after = Assert.Single(
            await repository.ListByAccountAsync(
                1,
                CancellationToken.None));

        Assert.Equal(before.PositionX, after.PositionX);
        Assert.Equal(before.PositionY, after.PositionY);
        Assert.Equal(before.RuntimeVersion, after.RuntimeVersion);
        Assert.Equal(before.ConcurrencyToken, after.ConcurrencyToken);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task StaleCharacterMapTransitionWrite_IsRejectedWithoutMutation()
    {
        if (!ShouldRun())
        {
            return;
        }

        var options = CreateOptions();
        var repository =
            new MariaDbCharacterListRepository(options);

        var before = Assert.Single(
            await repository.ListByAccountAsync(
                1,
                CancellationToken.None));

        Assert.NotNull(before.MapId);

        var writer =
            new MariaDbCharacterMapTransitionWriter(options);

        var result = await writer.TryUpdateAsync(
            new CharacterMapTransitionWriteRequest(
                before.CharacterId,
                before.MapId!.Value,
                16,
                15,
                before.RuntimeVersion + 100,
                before.ConcurrencyToken),
            CancellationToken.None);

        Assert.False(result.Updated);

        var after = Assert.Single(
            await repository.ListByAccountAsync(
                1,
                CancellationToken.None));

        Assert.Equal(before.MapId, after.MapId);
        Assert.Equal(before.PositionX, after.PositionX);
        Assert.Equal(before.PositionY, after.PositionY);
        Assert.Equal(
            before.RuntimeVersion,
            after.RuntimeVersion);
        Assert.Equal(
            before.ConcurrencyToken,
            after.ConcurrencyToken);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task CurrentCharacterMapTransitionWrite_SucceedsAndRollsBack()
    {
        if (!ShouldRun())
        {
            return;
        }

        var options = CreateOptions();
        var repository =
            new MariaDbCharacterListRepository(options);

        var before = Assert.Single(
            await repository.ListByAccountAsync(
                1,
                CancellationToken.None));

        Assert.NotNull(before.MapId);

        await using var connection =
            new MySqlConnection(
                options.BuildConnectionString());

        await connection.OpenAsync(
            CancellationToken.None);

        long destinationMapId;

        await using (var mapCommand =
            connection.CreateCommand())
        {
            mapCommand.CommandText = """
                SELECT map_id
                FROM god2_game.maps
                WHERE map_id <> @currentMapId
                  AND enabled = 1
                ORDER BY map_id
                LIMIT 1;
                """;

            mapCommand.Parameters.AddWithValue(
                "@currentMapId",
                before.MapId!.Value);

            var scalar =
                await mapCommand.ExecuteScalarAsync(
                    CancellationToken.None);

            Assert.NotNull(scalar);
            destinationMapId =
                Convert.ToInt64(scalar);
        }

        await using var transaction =
            await connection.BeginTransactionAsync(
                CancellationToken.None);

        const int destinationX = 65;
        const int destinationY = 64;

        try
        {
            var writer =
                new MariaDbCharacterMapTransitionWriter(
                    options);

            var result =
                await writer.TryUpdateAsync(
                    new CharacterMapTransitionWriteRequest(
                        before.CharacterId,
                        destinationMapId,
                        destinationX,
                        destinationY,
                        before.RuntimeVersion,
                        before.ConcurrencyToken),
                    connection,
                    transaction,
                    CancellationToken.None);

            Assert.True(result.Updated);
            Assert.Equal(
                before.RuntimeVersion + 1,
                result.RuntimeVersion);

            Assert.Equal(
                32,
                result.ConcurrencyToken.Length);

            Assert.NotEqual(
                before.ConcurrencyToken,
                result.ConcurrencyToken);

            await using var command =
                connection.CreateCommand();

            command.Transaction = transaction;

            command.CommandText = """
                SELECT map_id,
                       position_x,
                       position_y,
                       runtime_version,
                       concurrency_token
                FROM god2_player.characters
                WHERE character_id = @characterId;
                """;

            command.Parameters.AddWithValue(
                "@characterId",
                before.CharacterId);

            await using var reader =
                await command.ExecuteReaderAsync(
                    CancellationToken.None);

            Assert.True(
                await reader.ReadAsync(
                    CancellationToken.None));

            Assert.Equal(
                destinationMapId,
                reader.GetInt64(0));

            Assert.Equal(
                destinationX,
                reader.GetInt32(1));

            Assert.Equal(
                destinationY,
                reader.GetInt32(2));

            Assert.Equal(
                result.RuntimeVersion,
                reader.GetInt64(3));

            Assert.Equal(
                result.ConcurrencyToken,
                reader.GetString(4));

            Assert.False(
                await reader.ReadAsync(
                    CancellationToken.None));
        }
        finally
        {
            await transaction.RollbackAsync(
                CancellationToken.None);
        }

        var after = Assert.Single(
            await repository.ListByAccountAsync(
                1,
                CancellationToken.None));

        Assert.Equal(before.MapId, after.MapId);
        Assert.Equal(
            before.PositionX,
            after.PositionX);
        Assert.Equal(
            before.PositionY,
            after.PositionY);
        Assert.Equal(
            before.RuntimeVersion,
            after.RuntimeVersion);
        Assert.Equal(
            before.ConcurrencyToken,
            after.ConcurrencyToken);
    }


    [Fact]
    [Trait("Category", "Integration")]
    public async Task CurrentCharacterPositionWrite_SucceedsAndRollsBack()
    {
        if (!ShouldRun())
        {
            return;
        }

        var options = CreateOptions();
        var repository = new MariaDbCharacterListRepository(options);
        var before = Assert.Single(
            await repository.ListByAccountAsync(
                1,
                CancellationToken.None));

        const int testPositionX = 16;
        const int testPositionY = 15;

        await using var connection =
            new MySqlConnection(options.BuildConnectionString());
        await connection.OpenAsync(CancellationToken.None);
        await using var transaction =
            await connection.BeginTransactionAsync(CancellationToken.None);

        try
        {
            var writer = new MariaDbCharacterPositionWriter(options);
            var result = await writer.TryUpdateAsync(
                new CharacterPositionWriteRequest(
                    before.CharacterId,
                    testPositionX,
                    testPositionY,
                    before.RuntimeVersion,
                    before.ConcurrencyToken),
                connection,
                transaction,
                CancellationToken.None);

            Assert.True(result.Updated);
            Assert.Equal(before.RuntimeVersion + 1, result.RuntimeVersion);
            Assert.Equal(32, result.ConcurrencyToken.Length);
            Assert.NotEqual(
                before.ConcurrencyToken,
                result.ConcurrencyToken);

            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                SELECT position_x,
                       position_y,
                       runtime_version,
                       concurrency_token
                FROM god2_player.characters
                WHERE character_id = @characterId;
                """;
            command.Parameters.AddWithValue(
                "@characterId",
                before.CharacterId);

            await using var reader =
                await command.ExecuteReaderAsync(CancellationToken.None);
            Assert.True(
                await reader.ReadAsync(CancellationToken.None));
            Assert.Equal(testPositionX, reader.GetInt32(0));
            Assert.Equal(testPositionY, reader.GetInt32(1));
            Assert.Equal(result.RuntimeVersion, reader.GetInt64(2));
            Assert.Equal(result.ConcurrencyToken, reader.GetString(3));
            Assert.False(
                await reader.ReadAsync(CancellationToken.None));
        }
        finally
        {
            await transaction.RollbackAsync(CancellationToken.None);
        }

        var after = Assert.Single(
            await repository.ListByAccountAsync(
                1,
                CancellationToken.None));

        Assert.Equal(before.PositionX, after.PositionX);
        Assert.Equal(before.PositionY, after.PositionY);
        Assert.Equal(before.RuntimeVersion, after.RuntimeVersion);
        Assert.Equal(before.ConcurrencyToken, after.ConcurrencyToken);
    }


    [Fact]
    public async Task VerifiedPortalRoute_IsLoadedFromDatabase()
    {
        if (!ShouldRun())
        {
            return;
        }

        var repository =
            new MariaDbPortalRouteRepository(CreateOptions());

        var routes = await repository.ListBySourceMapAsync(
            170015000,
            CancellationToken.None);

        var route = Assert.Single(
            routes,
            entry => entry.PortalId == 170015007);

        Assert.Equal(170015000, route.SourceMapId);
        Assert.Equal(249, route.SourceX);
        Assert.Equal(246, route.SourceY);
        Assert.Equal(0, route.SourceRadius);

        Assert.Equal(
            "god2-opt-6b127086e0c0",
            route.SourceClientBuildId);
        Assert.Equal((ushort)0, route.SourceClientMapId);
        Assert.Equal((byte)15, route.SourceClientAreaId);

        Assert.Equal(170015007, route.DestinationMapId);
        Assert.Equal(48, route.DestinationX);
        Assert.Equal(81, route.DestinationY);

        Assert.Equal(
            "god2-opt-6b127086e0c0",
            route.DestinationClientBuildId);
        Assert.Equal((ushort)7, route.DestinationClientMapId);
        Assert.Equal((byte)15, route.DestinationClientAreaId);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task CharacterOne_InventorySnapshot_IsReadOnlyAndComplete()
    {
        if (!ShouldRun())
            return;

        var repository =
            new MariaDbCharacterInventorySnapshotRepository(CreateOptions());

        var snapshot = await repository.GetByCharacterAsync(
            1, CancellationToken.None);

        Assert.NotNull(snapshot);
        Assert.Equal(1, snapshot.CharacterId);
        Assert.Equal(32, snapshot.Capacity);
        Assert.Equal(1, snapshot.Version);
        Assert.Equal(1, snapshot.MutationSequence);
        Assert.Equal("Clean", snapshot.DirtyState);

        var slot = Assert.Single(snapshot.Slots);
        Assert.Equal(0, slot.SlotIndex);
        Assert.Equal(253231541, slot.ItemId);
        Assert.Equal(1, slot.Quantity);

        Assert.Null(await repository.GetByCharacterAsync(
            long.MaxValue, CancellationToken.None));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task MeatItem_NullMaximumStack_IsSingle()
    {
        if (!ShouldRun())
            return;

        var repository =
            new MariaDbItemStackRuleRepository(CreateOptions());

        var rule = await repository.GetByItemIdAsync(
            253231541, CancellationToken.None);

        Assert.NotNull(rule);
        Assert.Equal("肉塊", rule.Name);
        Assert.Null(rule.ConfiguredMaximumStack);
        Assert.Equal(1, rule.EffectiveMaximumStack);
        Assert.False(rule.IsStackable);

        Assert.Null(await repository.GetByItemIdAsync(
            long.MaxValue, CancellationToken.None));
    }

    private static bool ShouldRun() =>
        string.Equals(
            Environment.GetEnvironmentVariable("GOD2_RUN_DB_INTEGRATION"),
            "1",
            StringComparison.Ordinal);

    private static MariaDbAuthenticationOptions CreateOptions() =>
        new(
            Required("GOD2_DB_HOST"),
            int.Parse(Required("GOD2_DB_PORT")),
            Required("GOD2_DB_USER"),
            Required("GOD2_DB_PASSWORD"));

    private static string Required(string name) =>
        Environment.GetEnvironmentVariable(name) is { Length: > 0 } value
            ? value
            : throw new InvalidOperationException(
                $"Required environment variable is missing: {name}");
}
