using System.Text.Json;
using God2.ClassicServer.Application.Common;
using God2.ClassicServer.Application.Configuration;
using God2.ClassicServer.Application.Contracts;

namespace God2.ClassicServer.Infrastructure;

public sealed class AppPathProvider
{
    public AppPathProvider(string baseDirectory)
    {
        BaseDirectory = Path.GetFullPath(baseDirectory);
    }

    public string BaseDirectory { get; }

    public string ConfigDirectory => Resolve("config");

    public string LocalizationDirectory => Resolve("localization");

    public string DatabaseSchemaDirectory => Resolve(Path.Combine("database", "schema"));

    public string Resolve(string relativePath) => Path.GetFullPath(Path.Combine(BaseDirectory, relativePath));
}

public sealed class JsonServerConfigurationLoader : IServerConfigurationLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Disallow,
        AllowTrailingCommas = false
    };

    private readonly AppPathProvider _paths;

    public JsonServerConfigurationLoader(AppPathProvider paths)
    {
        _paths = paths;
    }

    public OperationResult<ServerConfiguration> Load()
    {
        try
        {
            var database = ResolveDatabasePassword(Read<DatabaseOptions>("database.json"));
            var configuration = new ServerConfiguration(
                Read<ServerOptions>("server.json"),
                database,
                Read<NetworkOptions>("network.json"),
                Read<RatesOptions>("rates.json"),
                Read<SecurityOptions>("security.json"),
                Read<PersistenceOptions>("persistence.json"),
                Read<LocalizationOptions>("localization.json"),
                Read<LoggingOptions>("logging.json"));

            return OperationResult<ServerConfiguration>.Success(configuration);
        }
        catch (FileNotFoundException ex)
        {
            var fileName = Path.GetFileName(ex.FileName ?? _paths.ConfigDirectory);
            return OperationResult<ServerConfiguration>.Failure(
                "configuration.missing",
                $"檔名: {fileName}; 欄位: <file>; 目前值: missing; 合法值: 檔案必須存在且為 UTF-8 JSON",
                ex.FileName ?? _paths.ConfigDirectory);
        }
        catch (JsonException ex)
        {
            return OperationResult<ServerConfiguration>.Failure(
                "configuration.invalid_json",
                $"檔名: config; 欄位: {ex.Path ?? "<unknown>"}; 目前值: invalid JSON; 合法值: JSON 型別與格式必須符合設定規格",
                ex.Path ?? _paths.ConfigDirectory);
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException)
        {
            return OperationResult<ServerConfiguration>.Failure("configuration.invalid", ex.Message, _paths.ConfigDirectory);
        }
    }

    private T Read<T>(string fileName)
    {
        var path = Path.Combine(_paths.ConfigDirectory, fileName);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Configuration file is missing: {fileName}", path);
        }

        using var stream = File.OpenRead(path);
        return JsonSerializer.Deserialize<T>(stream, JsonOptions)
            ?? throw new InvalidOperationException($"Configuration file is empty: {fileName}");
    }

    private static DatabaseOptions ResolveDatabasePassword(DatabaseOptions options)
    {
        if (string.Equals(options.PasswordSource, "ConfigValue", StringComparison.OrdinalIgnoreCase))
        {
            return options;
        }

        if (!string.Equals(options.PasswordSource, "EnvironmentVariable", StringComparison.OrdinalIgnoreCase))
        {
            return options with { Password = string.Empty };
        }

        var password = string.IsNullOrWhiteSpace(options.PasswordEnvironmentVariable)
            ? string.Empty
            : Environment.GetEnvironmentVariable(options.PasswordEnvironmentVariable) ?? string.Empty;
        return options with { Password = password };
    }

}

public sealed class JsonLocalizer : ILocalizer
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IReadOnlyDictionary<string, string> _selected;
    private readonly IReadOnlyDictionary<string, string> _fallback;

    public JsonLocalizer(AppPathProvider paths, string language, string fallbackLanguage)
    {
        Language = language;
        _selected = Load(paths.LocalizationDirectory, language);
        _fallback = Load(paths.LocalizationDirectory, fallbackLanguage);
    }

    public string Language { get; }

    public string Translate(string key)
    {
        if (_selected.TryGetValue(key, out var value))
        {
            return value;
        }

        return _fallback.TryGetValue(key, out var fallback) ? fallback : key;
    }

    private static IReadOnlyDictionary<string, string> Load(string directory, string language)
    {
        var path = Path.Combine(directory, $"{language}.json");
        if (!File.Exists(path))
        {
            return new Dictionary<string, string>();
        }

        using var stream = File.OpenRead(path);
        return JsonSerializer.Deserialize<Dictionary<string, string>>(stream, JsonOptions) ?? [];
    }
}
