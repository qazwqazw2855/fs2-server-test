using System.Net;
using System.Net.Sockets;
using God2.ClassicServer.Application.Common;

namespace God2.ClassicServer.Application.Configuration;

public sealed record ServerConfiguration(
    ServerOptions Server,
    DatabaseOptions Database,
    NetworkOptions Network,
    RatesOptions Rates,
    SecurityOptions Security,
    PersistenceOptions Persistence,
    LocalizationOptions Localization,
    LoggingOptions Logging)
{
    public IReadOnlyList<OperationError> Validate()
    {
        List<OperationError> errors = [];

        Require(Server.Name, "server.json", "name", errors);
        Require(Server.Environment, "server.json", "environment", errors);
        Range(Server.MaximumPlayers, 1, 100000, "server.json", "maximumPlayers", errors);
        Choice(
            Server.BattleEngineMode,
            ["LegacyPrimary", "ActorShadow", "ActorPrimary", "ActorPrimaryWithLegacyFallbackDisabled"],
            "server.json",
            "battleEngineMode",
            errors);

        Require(Database.Host, "database.json", "host", errors);
        Require(Database.DatabaseName, "database.json", "databaseName", errors);
        Require(Database.Username, "database.json", "username", errors);
        Require(Database.Password, "database.json", "password", errors);
        Require(Database.PasswordEnvironmentVariable, "database.json", "passwordEnvironmentVariable", errors);
        Choice(
            Database.PasswordSource,
            ["EnvironmentVariable", "ConfigValue"],
            "database.json",
            "passwordSource",
            errors);
        Range(Database.Port, 1, 65535, "database.json", "port", errors);
        Range(Database.ConnectionTimeoutSeconds, 1, 60, "database.json", "connectionTimeoutSeconds", errors);

        Require(Network.BindIp, "network.json", "bindIp", errors);
        if (!string.IsNullOrWhiteSpace(Network.AdvertisedIp) &&
            (!IPAddress.TryParse(Network.AdvertisedIp, out var advertisedIp) ||
             advertisedIp.AddressFamily != AddressFamily.InterNetwork ||
             IPAddress.Any.Equals(advertisedIp)))
        {
            errors.Add(Error(
                "configuration.range",
                "network.json",
                "advertisedIp",
                Network.AdvertisedIp,
                "a concrete IPv4 address"));
        }
        Range(Network.LoginPort, 1, 65535, "network.json", "loginPort", errors);
        if (Network.WorldPort != 0)
        {
            Range(Network.WorldPort, 1, 65535, "network.json", "worldPort", errors);
        }
        Range(Network.MaxFrameSize, 64, 65535, "network.json", "maxFrameSize", errors);

        Positive(Rates.ExperienceRate, "rates.json", "experienceRate", errors);
        Positive(Rates.DropRate, "rates.json", "dropRate", errors);

        RequiredTrue(Security.ReplayProtectionRequired, "security.json", "replayProtectionRequired", errors);
        RequiredTrue(Security.PacketValidationRequired, "security.json", "packetValidationRequired", errors);
        RequiredTrue(Security.AuthenticationValidationRequired, "security.json", "authenticationValidationRequired", errors);
        Range(Security.ConnectionLimit, 1, 100000, "security.json", "connectionLimit", errors);

        Range(Persistence.AutosaveIntervalSeconds, 10, 86400, "persistence.json", "autosaveIntervalSeconds", errors);
        Range(Persistence.BackupIntervalMinutes, 0, 10080, "persistence.json", "backupIntervalMinutes", errors);

        Require(Localization.DefaultLanguage, "localization.json", "defaultLanguage", errors);
        Require(Localization.FallbackLanguage, "localization.json", "fallbackLanguage", errors);

        Require(Logging.LogLevel, "logging.json", "logLevel", errors);
        Choice(
            Logging.LogLevel,
            ["Trace", "Debug", "Information", "Warning", "Error", "Critical"],
            "logging.json",
            "logLevel",
            errors);
        if (Logging.File)
        {
            errors.Add(Error(
                "configuration.unsupported",
                "logging.json",
                "file",
                Logging.File,
                "目前僅支援 false；正式 Runtime 僅輸出單一 Console"));
        }

        return errors;
    }

    private static void Require(string? value, string fileName, string field, ICollection<OperationError> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors.Add(Error("configuration.required", fileName, field, value ?? "<missing>", "非空白文字"));
        }
    }

    private static void Range(int value, int minimum, int maximum, string fileName, string field, ICollection<OperationError> errors)
    {
        if (value < minimum || value > maximum)
        {
            errors.Add(Error("configuration.range", fileName, field, value, $"{minimum} 到 {maximum}"));
        }
    }

    private static void Positive(decimal value, string fileName, string field, ICollection<OperationError> errors)
    {
        if (value <= 0)
        {
            errors.Add(Error("configuration.positive", fileName, field, value, "大於 0 的小數"));
        }
    }

    private static void RequiredTrue(bool value, string fileName, string field, ICollection<OperationError> errors)
    {
        if (!value)
        {
            errors.Add(Error("configuration.required_true", fileName, field, value, "必須為 true"));
        }
    }

    private static void Choice(
        string value,
        IReadOnlyList<string> allowed,
        string fileName,
        string field,
        ICollection<OperationError> errors)
    {
        if (!allowed.Contains(value, StringComparer.OrdinalIgnoreCase))
        {
            errors.Add(Error(
                "configuration.choice",
                fileName,
                field,
                value,
                string.Join("、", allowed)));
        }
    }

    private static OperationError Error(string code, string fileName, string field, object currentValue, string legalValue) =>
        new(
            code,
            $"檔名: {fileName}; 欄位: {field}; 目前值: {currentValue}; 合法值: {legalValue}",
            $"{fileName}:{field}");
}

public sealed record ServerOptions(
    string Name,
    string Environment,
    int MaximumPlayers,
    string BattleEngineMode = "LegacyPrimary");

public sealed record DatabaseOptions(
    string Host,
    int Port,
    string DatabaseName,
    string Username,
    string Password,
    int ConnectionTimeoutSeconds,
    string PasswordSource = "EnvironmentVariable",
    string PasswordEnvironmentVariable = "GOD2_DB_PASSWORD");

public sealed record NetworkOptions(
    string BindIp,
    int LoginPort,
    int WorldPort = 0,
    int MaxFrameSize = 4096,
    string AdvertisedIp = "");

public sealed record RatesOptions(decimal ExperienceRate, decimal DropRate);

public sealed record SecurityOptions(
    bool ReplayProtectionRequired,
    bool PacketValidationRequired,
    bool AuthenticationValidationRequired,
    int ConnectionLimit);

public sealed record PersistenceOptions(int AutosaveIntervalSeconds, int BackupIntervalMinutes);

public sealed record LocalizationOptions(string DefaultLanguage, string FallbackLanguage);

public sealed record LoggingOptions(string LogLevel, bool Console, bool File);
