using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using God2.ClassicServer.Persistence;
using God2.ClassicServer.Protocol;
using God2.ClassicServer.Runtime;

namespace God2.RecoveredDatabaseImporter;

public static class OfflineRecoveryVerifier
{
    public static async Task<OfflineRecoveryVerification> VerifyAsync(
        string repositoryRoot,
        string stagingRoot,
        string migrationCandidate,
        IReadOnlyList<string> solutionProjects,
        CancellationToken cancellationToken)
    {
        var compiled = CompiledServerContractScanner.Scan(solutionProjects);
        var migration = OfflineMigrationModelVerifier.Verify(repositoryRoot, migrationCandidate);
        var replay = await DeterministicSemanticReplayRunner.RunAsync(stagingRoot, cancellationToken);
        var status = compiled.Contracts.All(value => value.Status == "COMPILED_CONTRACT_VERIFIED") &&
                     migration.CandidateAdditiveOnly &&
                     migration.CandidateAppliedToModel &&
                     migration.CandidateSecondApplyNoOp &&
                     replay.FixtureStatus == "REPLAY_FIXTURE_EXECUTED" &&
                     replay.CaseCount != 0 &&
                     replay.CounterexampleCount == 0
            ? "OFFLINE_VERIFICATION_COMPLETE"
            : "EVIDENCE_BLOCKED";
        return new OfflineRecoveryVerification(
            "god2-offline-recovery-verification-v1",
            status,
            compiled,
            migration,
            replay,
            ProductionDatabaseConnectionAttempted: false,
            ProductionDatabaseMutationAttempted: false,
            FakeNetworkByteCount: 0);
    }
}

public static class CompiledServerContractScanner
{
    private sealed record Contract(string Domain, string[] Symbols);

    private static readonly Contract[] Contracts =
    [
        new("Protocol", [
            "God2.ClassicServer.Protocol.PacketDeserializer::Read",
            "God2.ClassicServer.Protocol.PacketSerializer::Write",
            "God2.ClassicServer.Protocol.ProtocolRegistry"]),
        new("Monster", [
            "God2.ClassicServer.Runtime.DataDrivenMonsterAiPolicy::Decide",
            "God2.ClassicServer.Persistence.MariaDbMonsterCatalogRepository::LoadEnabledAsync"]),
        new("Npc", [
            "God2.ClassicServer.Runtime.NpcInteractionRouter",
            "God2.ClassicServer.Persistence.MariaDbNpcCatalogRepository::LoadEnabledAsync"]),
        new("Map", [
            "God2.ClassicServer.Runtime.NavigationGrid::FindPath",
            "God2.ClassicServer.Persistence.MariaDbMapCatalogRepository::LoadEnabledAsync"]),
        new("Portal", [
            "God2.ClassicServer.Runtime.PortalTransitionCoordinator",
            "God2.ClassicServer.Persistence.MariaDbPortalCatalogRepository::LoadEnabledAsync"]),
        new("Quest", [
            "God2.ClassicServer.Runtime.QuestObject",
            "God2.ClassicServer.Persistence.MariaDbQuestCatalogRepository::LoadEnabledAsync"]),
        new("Item", [
            "God2.ClassicServer.Runtime.ItemDefinitionCatalog::Resolve",
            "God2.ClassicServer.Persistence.MariaDbItemCatalogRepository::LoadEnabledAsync"]),
        new("Skill", [
            "God2.ClassicServer.Runtime.SkillActionResolver::ResolveAsync",
            "God2.ClassicServer.Persistence.MariaDbSkillCatalogRepository::LoadEnabledAsync"]),
        new("Drop", [
            "God2.ClassicServer.Persistence.MariaDbMonsterDropRepository::LoadEnabledAsync"]),
        new("Formula", [
            "God2.ClassicServer.Runtime.BattleParticipantStatsFactory"]),
        new("StateMachine", [
            "God2.ClassicServer.Runtime.BattleActorStateMachine"]),
        new("ObjectResolver", [
            "God2.ClassicServer.Runtime.RuntimeObjectRegistry::Get"]),
        new("Mutation", [
            "God2.ClassicServer.Runtime.InventoryAuthorityPolicy::ValidateShape",
            "God2.ClassicServer.Persistence.MariaDbCombatMutationStore"])
    ];

    public static CompiledServerContractInventory Scan(IReadOnlyList<string> solutionProjects)
    {
        var requiredProjects = new[]
        {
            "src/God2.ClassicServer.Runtime/God2.ClassicServer.Runtime.csproj",
            "src/God2.ClassicServer.Protocol/God2.ClassicServer.Protocol.csproj",
            "src/God2.ClassicServer.Persistence/God2.ClassicServer.Persistence.csproj"
        };
        var projectSet = solutionProjects.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var projectGraphPresent = requiredProjects.All(projectSet.Contains);
        var assemblies = new[]
        {
            typeof(BattleReplayRunner).Assembly,
            typeof(PacketDeserializer).Assembly,
            typeof(SqlMigrationFile).Assembly
        }.Distinct().OrderBy(value => value.GetName().Name, StringComparer.Ordinal).ToArray();
        var typeMap = assemblies.SelectMany(SafeGetTypes)
            .Where(value => value.FullName is not null)
            .GroupBy(value => value.FullName!, StringComparer.Ordinal)
            .ToDictionary(value => value.Key, value => value.First(), StringComparer.Ordinal);
        var assemblyEvidence = assemblies.Select(AssemblyEvidence).ToArray();
        var checks = Contracts.Select(contract =>
        {
            var verified = projectGraphPresent
                ? contract.Symbols.Where(symbol => VerifySymbol(typeMap, symbol)).ToArray()
                : [];
            var missing = contract.Symbols.Except(verified, StringComparer.Ordinal).ToArray();
            return new CompiledContractCheck(
                contract.Domain,
                missing.Length == 0 ? "COMPILED_CONTRACT_VERIFIED" : "COMPILED_CONTRACT_MISSING",
                contract.Symbols,
                verified,
                missing,
                assemblyEvidence);
        }).ToArray();
        return new CompiledServerContractInventory(
            "LOADED_COMPILED_ASSEMBLY_REFLECTION_READ_ONLY",
            BuildOutputExecutedByImporter: false,
            assemblies.Length,
            checks);
    }

    private static bool VerifySymbol(IReadOnlyDictionary<string, Type> types, string symbol)
    {
        var separator = symbol.IndexOf("::", StringComparison.Ordinal);
        var typeName = separator < 0 ? symbol : symbol[..separator];
        if (!types.TryGetValue(typeName, out var type))
        {
            return false;
        }
        if (separator < 0)
        {
            return true;
        }
        var memberName = symbol[(separator + 2)..];
        const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static;
        return type.GetMember(memberName, flags).Length != 0;
    }

    private static IEnumerable<Type> SafeGetTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException exception)
        {
            return exception.Types.OfType<Type>();
        }
    }

    private static string AssemblyEvidence(Assembly assembly)
    {
        var location = assembly.Location;
        using var stream = File.OpenRead(location);
        var hash = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        return $"assembly:{assembly.GetName().Name};mvid={assembly.ManifestModule.ModuleVersionId:D};sha256={hash}";
    }
}

public static class OfflineMigrationModelVerifier
{
    public static MigrationModelVerification Verify(string repositoryRoot, string candidateSql)
    {
        var migrations = SqlMigrationFile.Discover(Path.Combine(repositoryRoot, "database", "schema"));
        var model = new OfflineSqlSchemaModel();
        var findings = new List<string>();
        var statementCount = 0;
        foreach (var migration in migrations)
        {
            var result = model.Apply(File.ReadAllText(migration.Path, Encoding.UTF8));
            statementCount += result.ParsedStatementCount;
            findings.AddRange(result.Findings.Select(value => $"{migration.Name}: {value}"));
        }

        var beforeCandidate = model.CanonicalHash();
        var forbidden = Regex.IsMatch(
            candidateSql,
            @"(?:^|;)\s*(?:--[^\r\n]*(?:\r?\n|$)\s*)*(INSERT|REPLACE|UPDATE|DELETE|DROP|TRUNCATE|RENAME|CALL|LOAD\s+DATA)\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
            TimeSpan.FromSeconds(1));
        var first = model.Apply(candidateSql);
        var afterFirst = model.CanonicalHash();
        var second = model.Apply(candidateSql);
        var afterSecond = model.CanonicalHash();
        findings.AddRange(first.Findings.Select(value => $"candidate: {value}"));
        findings.AddRange(second.Findings.Select(value => $"candidate-second-apply: {value}"));
        if (forbidden)
        {
            findings.Add("candidate: a production-data or destructive SQL verb is forbidden");
        }
        if (string.Equals(beforeCandidate, afterFirst, StringComparison.Ordinal))
        {
            findings.Add("candidate: no schema change was modeled");
        }

        return new MigrationModelVerification(
            "DETERMINISTIC_SQL_MIGRATION_MODEL_REPLAY_READ_ONLY",
            ProductionConnectionAttempted: false,
            ProductionMutationAttempted: false,
            migrations.Count,
            statementCount,
            model.TableCount,
            model.ColumnCount,
            afterFirst,
            CandidateAdditiveOnly: !forbidden && first.DestructiveStatementCount == 0,
            CandidateAppliedToModel: first.AddedTables.Count != 0 && first.Findings.Count == 0,
            CandidateSecondApplyNoOp: string.Equals(afterFirst, afterSecond, StringComparison.Ordinal) &&
                                        second.AddedTables.Count == 0 && second.AddedColumns.Count == 0,
            first.AddedTables,
            findings);
    }
}

internal sealed class OfflineSqlSchemaModel
{
    private readonly Dictionary<string, HashSet<string>> _tables = new(StringComparer.OrdinalIgnoreCase);

    public int TableCount => _tables.Count;

    public int ColumnCount => _tables.Values.Sum(value => value.Count);

    public IReadOnlyDictionary<string, IReadOnlyList<string>> Columns => _tables
        .OrderBy(value => value.Key, StringComparer.OrdinalIgnoreCase)
        .ToDictionary(
            value => value.Key,
            value => (IReadOnlyList<string>)value.Value.Order(StringComparer.OrdinalIgnoreCase).ToArray(),
            StringComparer.OrdinalIgnoreCase);

    public SqlModelApplyResult Apply(string sql)
    {
        var addedTables = new List<string>();
        var addedColumns = new List<string>();
        var findings = new List<string>();
        var destructive = 0;
        var statements = SplitStatements(sql);
        foreach (var statement in statements)
        {
            var normalized = StripComments(statement).Trim();
            if (normalized.Length == 0)
            {
                continue;
            }
            if (TryApplyRenameTable(normalized))
            {
                destructive++;
                continue;
            }
            if (Regex.IsMatch(normalized, @"^(DROP|TRUNCATE|RENAME)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            {
                destructive++;
                continue;
            }
            if (TryApplyCreateTable(normalized, addedTables, addedColumns) ||
                TryApplyAlterTable(normalized, addedColumns))
            {
                continue;
            }
        }

        foreach (Match reference in Regex.Matches(
                     StripComments(sql),
                     @"\bREFERENCES\s+(?:(?:`[^`]+`|[A-Za-z0-9_]+)\s*\.\s*)?(?:`(?<table>[^`]+)`|(?<table>[A-Za-z0-9_]+))",
                     RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
        {
            var table = reference.Groups["table"].Value;
            if (!_tables.ContainsKey(table))
            {
                findings.Add($"foreign key references an unknown table: {table}");
            }
        }
        return new SqlModelApplyResult(statements.Count, destructive, addedTables, addedColumns, findings);
    }

    public string CanonicalHash()
    {
        var canonical = string.Join(
            "\n",
            _tables.OrderBy(value => value.Key, StringComparer.OrdinalIgnoreCase)
                .Select(value => $"{value.Key.ToLowerInvariant()}:{string.Join(',', value.Value.Order(StringComparer.OrdinalIgnoreCase).Select(column => column.ToLowerInvariant()))}"));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }

    private bool TryApplyCreateTable(string statement, List<string> addedTables, List<string> addedColumns)
    {
        var match = Regex.Match(
            statement,
            @"\bCREATE\s+TABLE\s+(?:IF\s+NOT\s+EXISTS\s+)?(?:(?:`[^`]+`|[A-Za-z0-9_]+)\s*\.\s*)?(?:`(?<table>[^`]+)`|(?<table>[A-Za-z0-9_]+))\s*\(",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (!match.Success)
        {
            return false;
        }
        var tableName = match.Groups["table"].Value;
        if (!_tables.TryGetValue(tableName, out var columns))
        {
            columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            _tables.Add(tableName, columns);
            addedTables.Add(tableName);
        }
        var opening = statement.IndexOf('(', match.Index + match.Length - 1);
        var closing = statement.LastIndexOf(')');
        if (opening < 0 || closing <= opening)
        {
            return true;
        }
        foreach (var part in SplitTopLevel(statement[(opening + 1)..closing]))
        {
            var column = Regex.Match(part, @"^\s*`(?<column>[^`]+)`\s+", RegexOptions.CultureInvariant);
            if (column.Success && columns.Add(column.Groups["column"].Value))
            {
                addedColumns.Add($"{tableName}.{column.Groups["column"].Value}");
            }
        }
        return true;
    }

    private bool TryApplyAlterTable(string statement, List<string> addedColumns)
    {
        var match = Regex.Match(
            statement,
            @"\bALTER\s+TABLE\s+(?:(?:`[^`]+`|[A-Za-z0-9_]+)\s*\.\s*)?(?:`(?<table>[^`]+)`|(?<table>[A-Za-z0-9_]+))\s+(?<body>[\s\S]+)$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (!match.Success)
        {
            return false;
        }
        var tableName = match.Groups["table"].Value;
        if (!_tables.TryGetValue(tableName, out var columns))
        {
            columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            _tables.Add(tableName, columns);
        }
        foreach (var part in SplitTopLevel(match.Groups["body"].Value))
        {
            var column = Regex.Match(
                part,
                @"^\s*ADD\s+(?:COLUMN\s+)?(?:IF\s+NOT\s+EXISTS\s+)?`(?<column>[^`]+)`\s+",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (column.Success && columns.Add(column.Groups["column"].Value))
            {
                addedColumns.Add($"{tableName}.{column.Groups["column"].Value}");
            }
        }
        return true;
    }

    private bool TryApplyRenameTable(string statement)
    {
        var match = Regex.Match(
            statement,
            @"^RENAME\s+TABLE\s+(?:(?:`[^`]+`|[A-Za-z0-9_]+)\s*\.\s*)?(?:`(?<source>[^`]+)`|(?<source>[A-Za-z0-9_]+))\s+TO\s+(?:(?:`[^`]+`|[A-Za-z0-9_]+)\s*\.\s*)?(?:`(?<target>[^`]+)`|(?<target>[A-Za-z0-9_]+))$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (!match.Success)
        {
            return false;
        }

        var source = match.Groups["source"].Value;
        var target = match.Groups["target"].Value;
        if (_tables.Remove(source, out var columns))
        {
            _tables[target] = columns;
        }
        return true;
    }

    private static List<string> SplitStatements(string sql) => SplitDelimited(sql, ';');

    private static List<string> SplitTopLevel(string sql) => SplitDelimited(sql, ',', honorParentheses: true);

    private static List<string> SplitDelimited(string text, char delimiter, bool honorParentheses = false)
    {
        var result = new List<string>();
        var start = 0;
        var depth = 0;
        var quote = '\0';
        for (var index = 0; index < text.Length; index++)
        {
            var current = text[index];
            if (quote != '\0')
            {
                if (current == quote && (index == 0 || text[index - 1] != '\\'))
                {
                    quote = '\0';
                }
                continue;
            }
            if (current is '\'' or '"' or '`')
            {
                quote = current;
                continue;
            }
            if (honorParentheses)
            {
                if (current == '(') depth++;
                if (current == ')') depth--;
            }
            if (current == delimiter && (!honorParentheses || depth == 0))
            {
                result.Add(text[start..index]);
                start = index + 1;
            }
        }
        if (start < text.Length)
        {
            result.Add(text[start..]);
        }
        return result;
    }

    private static string StripComments(string sql)
    {
        var blockless = Regex.Replace(sql, @"/\*[\s\S]*?\*/", string.Empty, RegexOptions.CultureInvariant);
        return Regex.Replace(blockless, @"(?m)^[ \t]*(?:--|#).*$", string.Empty, RegexOptions.CultureInvariant);
    }
}

internal sealed record SqlModelApplyResult(
    int ParsedStatementCount,
    int DestructiveStatementCount,
    IReadOnlyList<string> AddedTables,
    IReadOnlyList<string> AddedColumns,
    IReadOnlyList<string> Findings);

public static class DeterministicSemanticReplayRunner
{
    private const string ReplayRelativePath = "replay/semantic-replay/cases.jsonl";

    public static async Task<SemanticReplayVerification> RunAsync(string stagingRoot, CancellationToken cancellationToken)
    {
        var path = Path.Combine(stagingRoot, ReplayRelativePath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(path))
        {
            return new SemanticReplayVerification(
                "DETERMINISTIC_OFFLINE_JSON_STATE_TWIN",
                "EVIDENCE_BLOCKED_REPLAY_FIXTURE_UNAVAILABLE",
                0,
                0,
                0,
                []);
        }
        var results = new List<SemanticReplayCaseResult>();
        var lineNumber = 0;
        foreach (var line in await File.ReadAllLinesAsync(path, cancellationToken))
        {
            lineNumber++;
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }
            var source = $"{ReplayRelativePath}:{lineNumber}";
            try
            {
                using var document = JsonDocument.Parse(line);
                var root = document.RootElement;
                var caseId = RequiredString(root, "caseId");
                var domain = RequiredString(root, "domain");
                var initial = root.GetProperty("initialState");
                var expected = root.GetProperty("expectedState");
                var packetHash = OptionalSemanticHash(root, "plaintextPacket");
                var eventHash = OptionalSemanticHash(root, "semanticEvent");
                var state = JsonNode.Parse(initial.GetRawText()) ?? throw new JsonException("initialState cannot be null");
                var mutationCount = 0;
                foreach (var mutation in root.GetProperty("mutations").EnumerateArray())
                {
                    ApplyMutation(state, mutation);
                    mutationCount++;
                }
                using var actualState = JsonDocument.Parse(state.ToJsonString());
                SemanticOracleResult oracle;
                if (root.TryGetProperty("expectedResponse", out var expectedResponse))
                {
                    if (!root.TryGetProperty("actualResponse", out var actualResponse))
                    {
                        throw new JsonException("actualResponse is required when expectedResponse is present");
                    }
                    var expectedEnvelope = JsonSerializer.SerializeToElement(new
                    {
                        state = expected,
                        response = expectedResponse
                    });
                    var actualEnvelope = JsonSerializer.SerializeToElement(new
                    {
                        state = actualState.RootElement,
                        response = actualResponse
                    });
                    oracle = SemanticOracle.Compare(expectedEnvelope, actualEnvelope, source, $"{domain}.OfflineStateTwin");
                }
                else
                {
                    oracle = SemanticOracle.Compare(expected, actualState.RootElement, source, $"{domain}.OfflineStateTwin");
                }
                results.Add(new SemanticReplayCaseResult(
                    caseId,
                    domain,
                    oracle.Equivalent ? "SEMANTICALLY_EQUIVALENT" : "COUNTEREXAMPLE",
                    mutationCount,
                    oracle,
                    source,
                    packetHash,
                    eventHash,
                    null));
            }
            catch (Exception exception) when (exception is JsonException or InvalidOperationException or FormatException or OverflowException)
            {
                results.Add(new SemanticReplayCaseResult(
                    $"invalid-line-{lineNumber}",
                    "Unknown",
                    "INVALID_REPLAY_FIXTURE",
                    0,
                    null,
                    source,
                    null,
                    null,
                    exception.Message));
            }
        }
        return new SemanticReplayVerification(
            "DETERMINISTIC_OFFLINE_JSON_STATE_TWIN",
            results.Count == 0 ? "EVIDENCE_BLOCKED_REPLAY_FIXTURE_EMPTY" : "REPLAY_FIXTURE_EXECUTED",
            results.Count,
            results.Count(value => value.Status == "SEMANTICALLY_EQUIVALENT"),
            results.Count(value => value.Status != "SEMANTICALLY_EQUIVALENT"),
            results);
    }

    private static void ApplyMutation(JsonNode state, JsonElement mutation)
    {
        var operation = RequiredString(mutation, "operation");
        var path = RequiredString(mutation, "path");
        var segments = path.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length == 0)
        {
            throw new JsonException("mutation path is empty");
        }
        JsonNode current = state;
        for (var index = 0; index < segments.Length - 1; index++)
        {
            current = current[segments[index]] ?? throw new JsonException($"mutation path was not found: {path}");
        }
        if (current is not JsonObject parent)
        {
            throw new JsonException($"mutation parent is not an object: {path}");
        }
        var leaf = segments[^1];
        switch (operation)
        {
            case "Set":
                parent[leaf] = JsonNode.Parse(mutation.GetProperty("value").GetRawText());
                break;
            case "Increment":
                var existing = parent[leaf]?.GetValue<decimal>() ?? throw new JsonException($"increment target was not found: {path}");
                var delta = mutation.GetProperty("value").GetDecimal();
                parent[leaf] = existing + delta;
                break;
            case "Remove":
                if (!parent.Remove(leaf))
                {
                    throw new JsonException($"remove target was not found: {path}");
                }
                break;
            default:
                throw new JsonException($"unsupported mutation operation: {operation}");
        }
    }

    private static string RequiredString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(property.GetString()))
        {
            throw new JsonException($"{propertyName} is required");
        }
        return property.GetString()!;
    }

    private static string? OptionalSemanticHash(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value))
        {
            return null;
        }
        if (value.ValueKind is not (JsonValueKind.Object or JsonValueKind.Array))
        {
            throw new JsonException($"{propertyName} must be an object or array");
        }
        return RecoveryJsonCanonicalizer.Sha256(value);
    }
}
