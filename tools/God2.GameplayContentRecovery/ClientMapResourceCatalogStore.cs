using God2.ClassicServer.Application.Configuration;
using MySqlConnector;

namespace God2.GameplayContentRecovery;

public sealed record ClientMapResourceSyncResult(
    bool Succeeded,
    int SourceCount,
    int UpsertedCount,
    int CatalogCount,
    int EnabledCount);

public sealed class ClientMapResourceCatalogStore(DatabaseOptions options)
{
    public async Task<ClientMapResourceSyncResult> SynchronizeAsync(
        IReadOnlyList<ClientMapResourceRecord> resources,
        string clientBuildId,
        string clientExecutableSha256,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(clientBuildId);
        ArgumentException.ThrowIfNullOrWhiteSpace(clientExecutableSha256);
        await using var connection = new MySqlConnection(BuildConnectionString());
        await connection.OpenAsync(cancellationToken);
        var (_, enabledBeforeSync) = await ReadCountsAsync(connection, clientBuildId, cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using (var prepareCommand = connection.CreateCommand())
        {
            prepareCommand.Transaction = transaction;
            prepareCommand.CommandText = """
                ALTER TABLE `god2_research`.`client_map_resource_evidence`
                    ADD COLUMN IF NOT EXISTS `client_executable_sha256` char(64) NULL AFTER `resource_key`,
                    ADD COLUMN IF NOT EXISTS `can_record_index` int NULL AFTER `client_executable_sha256`,
                    ADD COLUMN IF NOT EXISTS `can_record_type` tinyint unsigned NULL AFTER `can_record_index`,
                    ADD COLUMN IF NOT EXISTS `can_relative_path` varchar(512) NULL AFTER `can_record_type`,
                    ADD COLUMN IF NOT EXISTS `can_sha256` char(64) NULL AFTER `can_relative_path`,
                    ADD COLUMN IF NOT EXISTS `mbd_relative_path` varchar(512) NULL AFTER `can_sha256`,
                    ADD COLUMN IF NOT EXISTS `mbd_sha256` char(64) NULL AFTER `mbd_relative_path`,
                    ADD COLUMN IF NOT EXISTS `resource_file_relative_path` varchar(512) NULL AFTER `mbd_sha256`,
                    ADD COLUMN IF NOT EXISTS `resource_file_sha256` char(64) NULL AFTER `resource_file_relative_path`,
                    ADD COLUMN IF NOT EXISTS `navigation_relative_path` varchar(512) NULL AFTER `resource_file_sha256`,
                    ADD COLUMN IF NOT EXISTS `navigation_sha256` char(64) NULL AFTER `navigation_relative_path`;
                """;
            await prepareCommand.ExecuteNonQueryAsync(cancellationToken);
        }
        var upserted = 0;
        foreach (var resource in resources)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO `god2_game`.`client_map_resources`
                    (`resource_key`,`area_code`,`resource_name`,
                     `grid_width`,`grid_height`,`minimum_x`,`maximum_x`,`minimum_y`,`maximum_y`,
                     `client_build_id`,`resource_format`,`navigation_format`,`enabled`)
                VALUES
                    (@key,@area,@name,@width,@height,@minX,@maxX,@minY,@maxY,
                     @build,@resourceFormat,@navigationFormat,0)
                ON DUPLICATE KEY UPDATE
                    `area_code`=VALUES(`area_code`),
                    `resource_name`=VALUES(`resource_name`),
                    `grid_width`=VALUES(`grid_width`),
                    `grid_height`=VALUES(`grid_height`),
                    `minimum_x`=VALUES(`minimum_x`),
                    `maximum_x`=VALUES(`maximum_x`),
                    `minimum_y`=VALUES(`minimum_y`),
                    `maximum_y`=VALUES(`maximum_y`),
                    `resource_format`=VALUES(`resource_format`),
                    `navigation_format`=VALUES(`navigation_format`);

                INSERT INTO `god2_research`.`client_map_resource_evidence`
                    (`resource_key`,`client_executable_sha256`,`can_record_index`,`can_record_type`,
                     `can_relative_path`,`can_sha256`,`mbd_relative_path`,`mbd_sha256`,
                     `resource_file_relative_path`,`resource_file_sha256`,
                     `navigation_relative_path`,`navigation_sha256`,
                     `resource_evidence_status`,`map_identity_evidence_status`,
                     `portal_placement_evidence_status`,`navigation_evidence_status`,`admin_note`)
                VALUES
                    (@key,@executableHash,@recordIndex,@recordType,@canPath,@canHash,
                     @legacyMbdPath,@legacyMbdHash,@resourcePath,@resourceHash,
                     @navigationPath,@navigationHash,'Derived','EvidenceBlocked','EvidenceBlocked',
                     @navigationEvidence,
                     'Official client CAN plus MDT/MBD or HMD navigation inventory; numeric map identity and portal placement remain gated.')
                ON DUPLICATE KEY UPDATE
                    `client_executable_sha256`=VALUES(`client_executable_sha256`),
                    `can_record_index`=VALUES(`can_record_index`),
                    `can_record_type`=VALUES(`can_record_type`),
                    `can_relative_path`=VALUES(`can_relative_path`),
                    `can_sha256`=VALUES(`can_sha256`),
                    `mbd_relative_path`=VALUES(`mbd_relative_path`),
                    `mbd_sha256`=VALUES(`mbd_sha256`),
                    `resource_file_relative_path`=VALUES(`resource_file_relative_path`),
                    `resource_file_sha256`=VALUES(`resource_file_sha256`),
                    `navigation_relative_path`=VALUES(`navigation_relative_path`),
                    `navigation_sha256`=VALUES(`navigation_sha256`),
                    `resource_evidence_status`=VALUES(`resource_evidence_status`),
                    `navigation_evidence_status`=VALUES(`navigation_evidence_status`),
                    `admin_note`=VALUES(`admin_note`),
                    `moved_at_utc`=UTC_TIMESTAMP(6);
                """;
            command.Parameters.AddWithValue("@key", resource.AuthorityKey);
            command.Parameters.AddWithValue("@area", resource.Area);
            command.Parameters.AddWithValue("@name", resource.ResourceName);
            command.Parameters.AddWithValue("@recordIndex", resource.CanRecordIndex);
            command.Parameters.AddWithValue("@recordType", resource.CanRecordType);
            command.Parameters.AddWithValue("@width", (object?)resource.GridWidth ?? DBNull.Value);
            command.Parameters.AddWithValue("@height", (object?)resource.GridHeight ?? DBNull.Value);
            command.Parameters.AddWithValue("@minX", resource.GridWidth is > 0 ? 0 : DBNull.Value);
            command.Parameters.AddWithValue("@maxX", resource.GridWidth is > 0 ? checked(resource.GridWidth.Value * 21 - 1) : DBNull.Value);
            command.Parameters.AddWithValue("@minY", resource.GridHeight is > 0 ? 0 : DBNull.Value);
            command.Parameters.AddWithValue("@maxY", resource.GridHeight is > 0 ? checked(resource.GridHeight.Value * 21 - 1) : DBNull.Value);
            command.Parameters.AddWithValue("@build", clientBuildId);
            command.Parameters.AddWithValue("@executableHash", clientExecutableSha256);
            command.Parameters.AddWithValue("@canPath", resource.CanRelativePath);
            command.Parameters.AddWithValue("@canHash", resource.CanSha256);
            command.Parameters.AddWithValue("@resourceFormat", resource.ResourceFormat);
            command.Parameters.AddWithValue("@resourcePath", (object?)resource.ResourceFileRelativePath ?? DBNull.Value);
            command.Parameters.AddWithValue("@resourceHash", (object?)resource.ResourceFileSha256 ?? DBNull.Value);
            command.Parameters.AddWithValue("@navigationFormat", (object?)resource.NavigationFormat ?? DBNull.Value);
            command.Parameters.AddWithValue("@navigationPath", (object?)resource.NavigationRelativePath ?? DBNull.Value);
            command.Parameters.AddWithValue("@navigationHash", (object?)resource.NavigationSha256 ?? DBNull.Value);
            command.Parameters.AddWithValue("@legacyMbdPath", resource.NavigationFormat == "MBD v1.2" ? (object?)resource.NavigationRelativePath ?? DBNull.Value : DBNull.Value);
            command.Parameters.AddWithValue("@legacyMbdHash", resource.NavigationFormat == "MBD v1.2" ? (object?)resource.NavigationSha256 ?? DBNull.Value : DBNull.Value);
            command.Parameters.AddWithValue("@navigationEvidence", resource.GridWidth is > 0 && resource.GridHeight is > 0 ? "Derived" : "Unknown");
            upserted += await command.ExecuteNonQueryAsync(cancellationToken) > 0 ? 1 : 0;
        }
        await transaction.CommitAsync(cancellationToken);

        var (catalogCount, enabledCount) = await ReadCountsAsync(connection, clientBuildId, cancellationToken);
        return new ClientMapResourceSyncResult(
            catalogCount == resources.Count && enabledCount == enabledBeforeSync,
            resources.Count,
            upserted,
            catalogCount,
            enabledCount);
    }

    private static async Task<(int Catalog, int Enabled)> ReadCountsAsync(
        MySqlConnection connection,
        string clientBuildId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*),COALESCE(SUM(`enabled`),0) FROM `god2_game`.`client_map_resources` WHERE `client_build_id`=@build;";
        command.Parameters.AddWithValue("@build", clientBuildId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        return (reader.GetInt32(0), reader.GetInt32(1));
    }

    private string BuildConnectionString() => new MySqlConnectionStringBuilder
    {
        Server = options.Host,
        Port = checked((uint)options.Port),
        Database = options.DatabaseName,
        UserID = options.Username,
        Password = options.Password,
        CharacterSet = "utf8mb4",
        ConnectionTimeout = checked((uint)Math.Max(1, options.ConnectionTimeoutSeconds)),
        DefaultCommandTimeout = 180,
        Pooling = true,
        SslMode = MySqlSslMode.Preferred
    }.ConnectionString;
}
