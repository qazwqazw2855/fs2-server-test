using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text;
using God2.OfflineClientReverseEngineering;
using God2.ClassicServer.Runtime;

var workspaceRoot = WorkspaceDiscovery.FindWorkspaceRoot(AppContext.BaseDirectory);
var clientPath = WorkspaceDiscovery.ResolveClientPath(workspaceRoot, args);
var outputRoot = Path.Combine(workspaceRoot, "Artifacts", "OfflineClientReverseEngineering");
Directory.CreateDirectory(outputRoot);
var options = new JsonSerializerOptions
{
    WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
};

if (args.Contains("--scan-runtime-memory-field-range", StringComparer.OrdinalIgnoreCase))
{
    static string RequiredFieldOption(string[] values, string name)
    {
        var index = Array.FindIndex(values, item => string.Equals(item, name, StringComparison.OrdinalIgnoreCase));
        return index >= 0 && index + 1 < values.Length
            ? values[index + 1]
            : throw new ArgumentException($"{name} requires a value.");
    }

    static uint ParseFieldValue(string value) =>
        value.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? Convert.ToUInt32(value[2..], 16)
            : Convert.ToUInt32(value, System.Globalization.CultureInfo.InvariantCulture);

    var fieldRuntimeCodePath = Path.GetFullPath(RequiredFieldOption(args, "--runtime-code"));
    var expectedSha256 = RequiredFieldOption(args, "--runtime-code-sha256").Trim();
    var requestedOutputPath = Path.GetFullPath(RequiredFieldOption(args, "--out"));
    var minimumOffset = ParseFieldValue(RequiredFieldOption(args, "--minimum-offset"));
    var maximumOffset = ParseFieldValue(RequiredFieldOption(args, "--maximum-offset"));
    var workspacePrefix = workspaceRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
    if (!fieldRuntimeCodePath.StartsWith(workspacePrefix, StringComparison.OrdinalIgnoreCase) ||
        !requestedOutputPath.StartsWith(workspacePrefix, StringComparison.OrdinalIgnoreCase))
        throw new InvalidDataException("Runtime field-scan input and output must remain under the workspace root.");

    var runtimeFile = new FileInfo(fieldRuntimeCodePath);
    if (!runtimeFile.Exists || runtimeFile.Length <= 0 || runtimeFile.Length > 16L * 1024 * 1024)
        throw new InvalidDataException("Runtime field-scan input is missing, empty, or exceeds the 16 MiB limit.");
    using (var stream = File.OpenRead(fieldRuntimeCodePath))
    {
        var actualSha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(stream));
        if (!string.Equals(actualSha256, expectedSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Runtime field-scan input SHA-256 does not match the pinned input.");
        expectedSha256 = actualSha256;
    }

    var fieldAnalyzer = new RuntimeCodeAnalyzer(fieldRuntimeCodePath, 0u);
    var references = fieldAnalyzer.LinearMemoryFieldReferences(minimumOffset, maximumOffset);
    var evidence = new
    {
        SchemaVersion = "exact-build-runtime-memory-field-range-candidates-v1",
        SourceArtifact = Path.GetRelativePath(workspaceRoot, fieldRuntimeCodePath).Replace('\\', '/'),
        RuntimeCodeSha256 = expectedSha256,
        MinimumOffset = $"0x{minimumOffset:X3}",
        MaximumOffset = $"0x{maximumOffset:X3}",
        ReferenceCount = references.Count,
        DistinctFieldOffsetCount = references.Select(reference => reference.FieldOffset).Distinct(StringComparer.Ordinal).Count(),
        References = references,
        EvidenceBoundary = "LinearDecodeDiscoveryCandidatesRequireReachableControlFlowAndLiveConsumerProof",
        ClientWriteAccessUsed = false,
        ClientBinaryModified = false,
        NetworkBytesEmitted = false
    };
    Directory.CreateDirectory(Path.GetDirectoryName(requestedOutputPath)!);
    await File.WriteAllTextAsync(requestedOutputPath, JsonSerializer.Serialize(evidence, options), new UTF8Encoding(false));
    Console.WriteLine(JsonSerializer.Serialize(new
    {
        evidence.SchemaVersion,
        evidence.RuntimeCodeSha256,
        evidence.ReferenceCount,
        evidence.DistinctFieldOffsetCount,
        Output = Path.GetRelativePath(workspaceRoot, requestedOutputPath).Replace('\\', '/')
    }, options));
    return;
}

if (args.Contains("--refresh-official-knowledge-only", StringComparer.OrdinalIgnoreCase))
{
    var knowledge = OfficialKnowledgeRecovery.Recover(
        Path.Combine(workspaceRoot, "db", "imports", "official"),
        Path.Combine(workspaceRoot, "Knowledge"),
        options);
    Console.WriteLine(JsonSerializer.Serialize(new
    {
        knowledge.SchemaVersion,
        knowledge.DomainCount,
        knowledge.SourceCategoryCount,
        knowledge.DeclaredRecordCount,
        knowledge.IndexedEntityCount,
        knowledge.CrossReferenceCount
    }, options));
    return;
}

if (args.Contains("--find-runtime-memory-displacement", StringComparer.OrdinalIgnoreCase))
{
    static string RequiredMemoryOption(string[] values, string name)
    {
        var index = Array.FindIndex(values, item => string.Equals(item, name, StringComparison.OrdinalIgnoreCase));
        return index >= 0 && index + 1 < values.Length
            ? values[index + 1]
            : throw new ArgumentException($"{name} requires a value.");
    }

    static uint ParseMemoryValue(string value) =>
        value.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? Convert.ToUInt32(value[2..], 16)
            : Convert.ToUInt32(value, System.Globalization.CultureInfo.InvariantCulture);

    var memoryRuntimeCodePath = Path.GetFullPath(RequiredMemoryOption(args, "--runtime-code"));
    var expectedSha256 = RequiredMemoryOption(args, "--runtime-code-sha256").Trim();
    var requestedOutputPath = Path.GetFullPath(RequiredMemoryOption(args, "--out"));
    var displacement = ParseMemoryValue(RequiredMemoryOption(args, "--displacement"));
    var workspacePrefix = workspaceRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
    if (!memoryRuntimeCodePath.StartsWith(workspacePrefix, StringComparison.OrdinalIgnoreCase) ||
        !requestedOutputPath.StartsWith(workspacePrefix, StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidDataException("Runtime memory-reference input and output must remain under the workspace root.");
    }

    var runtimeFile = new FileInfo(memoryRuntimeCodePath);
    if (!runtimeFile.Exists || runtimeFile.Length <= 0 || runtimeFile.Length > 16L * 1024 * 1024)
    {
        throw new InvalidDataException("Runtime memory-reference input is missing, empty, or exceeds the 16 MiB limit.");
    }
    using (var stream = File.OpenRead(memoryRuntimeCodePath))
    {
        var actualSha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(stream));
        if (!string.Equals(actualSha256, expectedSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Runtime memory-reference input SHA-256 does not match the pinned input.");
        }
        expectedSha256 = actualSha256;
    }

    var memoryAnalyzer = new RuntimeCodeAnalyzer(memoryRuntimeCodePath, 0u);
    var references = memoryAnalyzer.ExhaustiveMemoryDisplacementReferences(displacement);
    var evidence = new
    {
        SchemaVersion = "exact-build-runtime-memory-displacement-evidence-v1",
        SourceArtifact = Path.GetRelativePath(workspaceRoot, memoryRuntimeCodePath).Replace('\\', '/'),
        RuntimeCodeSha256 = expectedSha256,
        Displacement = $"0x{displacement:X8}",
        ReferenceCount = references.Count,
        LinearDecodeAlignedCount = references.Count(reference => reference.LinearDecodeAligned),
        References = references,
        EvidenceBoundary = "ExhaustiveRawDisplacementCandidatesRequireSurroundingControlFlowValidation",
        ClientWriteAccessUsed = false,
        ClientBinaryModified = false,
        NetworkBytesEmitted = false
    };
    Directory.CreateDirectory(Path.GetDirectoryName(requestedOutputPath)!);
    await File.WriteAllTextAsync(
        requestedOutputPath,
        JsonSerializer.Serialize(evidence, options),
        new UTF8Encoding(false));
    Console.WriteLine(JsonSerializer.Serialize(new
    {
        evidence.SchemaVersion,
        evidence.RuntimeCodeSha256,
        evidence.Displacement,
        evidence.ReferenceCount,
        evidence.LinearDecodeAlignedCount,
        Output = Path.GetRelativePath(workspaceRoot, requestedOutputPath).Replace('\\', '/')
    }, options));
    return;
}

if (args.Contains("--recover-outbound-static-custom", StringComparer.OrdinalIgnoreCase))
{
    static string RequiredOutboundOption(string[] values, string name)
    {
        var index = Array.FindIndex(values, item => string.Equals(item, name, StringComparison.OrdinalIgnoreCase));
        return index >= 0 && index + 1 < values.Length
            ? values[index + 1]
            : throw new ArgumentException($"{name} requires a value.");
    }

    static uint ParseOutboundRva(string value) =>
        value.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? Convert.ToUInt32(value[2..], 16)
            : Convert.ToUInt32(value, System.Globalization.CultureInfo.InvariantCulture);

    var outboundRuntimeCodePath = Path.GetFullPath(RequiredOutboundOption(args, "--runtime-code"));
    var expectedSha256 = RequiredOutboundOption(args, "--runtime-code-sha256").Trim();
    var codeStartRva = ParseOutboundRva(RequiredOutboundOption(args, "--code-start-rva"));
    var requestedOutputPath = Path.GetFullPath(RequiredOutboundOption(args, "--out"));
    var workspacePrefix = workspaceRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
    if (!outboundRuntimeCodePath.StartsWith(workspacePrefix, StringComparison.OrdinalIgnoreCase) ||
        !requestedOutputPath.StartsWith(workspacePrefix, StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidDataException("Custom outbound static input and output must remain under the workspace root.");
    }

    var runtimeFile = new FileInfo(outboundRuntimeCodePath);
    if (!runtimeFile.Exists || runtimeFile.Length <= 0 || runtimeFile.Length > 16L * 1024 * 1024)
    {
        throw new InvalidDataException("Custom outbound runtime code is missing, empty, or exceeds the 16 MiB limit.");
    }

    using (var stream = File.OpenRead(outboundRuntimeCodePath))
    {
        var actualSha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(stream));
        if (!string.Equals(actualSha256, expectedSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Custom outbound runtime code SHA-256 does not match the pinned input.");
        }
    }

    var outboundAnalyzer = new RuntimeCodeAnalyzer(outboundRuntimeCodePath, codeStartRva);
    var evidence = M4NpcInteractionStaticRecovery.Recover(
        outboundAnalyzer,
        expectedSha256.ToUpperInvariant());
    var equipmentFrameLengthCandidates = M4NpcInteractionStaticRecovery
        .RecoverApplicationCallsites(outboundAnalyzer)
        .Where(callsite => callsite.PayloadLength is 3 or 4)
        .ToArray();
    Directory.CreateDirectory(Path.GetDirectoryName(requestedOutputPath)!);
    await File.WriteAllTextAsync(
        requestedOutputPath,
        JsonSerializer.Serialize(new
        {
            SchemaVersion = "custom-outbound-static-recovery-v1",
            SourceArtifact = Path.GetRelativePath(workspaceRoot, outboundRuntimeCodePath).Replace('\\', '/'),
            RuntimeCodeSha256 = expectedSha256.ToUpperInvariant(),
            CodeStartRva = $"0x{codeStartRva:X8}",
            StaticSnapshot = evidence,
            EquipmentFrameLengthCandidates = equipmentFrameLengthCandidates,
            NetworkBytesEmitted = false,
            ClientMemoryWritten = false
        }, options),
        new UTF8Encoding(false));
    Console.WriteLine(JsonSerializer.Serialize(new
    {
        SchemaVersion = "custom-outbound-static-recovery-v1",
        SourceArtifact = Path.GetRelativePath(workspaceRoot, outboundRuntimeCodePath).Replace('\\', '/'),
        RuntimeCodeSha256 = expectedSha256.ToUpperInvariant(),
        CodeStartRva = $"0x{codeStartRva:X8}",
        evidence.DirectCallerCount,
        evidence.ResolvedArgumentCount,
        ApplicationPayloadGroupCount = evidence.ApplicationPayloadGroups.Count,
        EquipmentFrameLengthCandidateCount = equipmentFrameLengthCandidates.Length,
        Output = Path.GetRelativePath(workspaceRoot, requestedOutputPath).Replace('\\', '/'),
        NetworkBytesEmitted = false,
        ClientMemoryWritten = false
    }, options));
    return;
}

if (args.Contains("--disassemble-runtime-range", StringComparer.OrdinalIgnoreCase))
{
    static string RequiredDisassemblyOption(string[] values, string name)
    {
        var index = Array.FindIndex(values, item => string.Equals(item, name, StringComparison.OrdinalIgnoreCase));
        return index >= 0 && index + 1 < values.Length
            ? values[index + 1]
            : throw new ArgumentException($"{name} requires a value.");
    }

    static uint ParseDisassemblyRva(string value) =>
        Convert.ToUInt32(value.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? value[2..] : value, 16);

    var disassemblyRuntimeCodePath = Path.GetFullPath(RequiredDisassemblyOption(args, "--runtime-code"));
    var requestedOutputPath = Path.GetFullPath(RequiredDisassemblyOption(args, "--out"));
    var rva = ParseDisassemblyRva(RequiredDisassemblyOption(args, "--rva"));
    var length = int.Parse(
        RequiredDisassemblyOption(args, "--length"),
        System.Globalization.CultureInfo.InvariantCulture);
    var workspacePrefix = workspaceRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
    if (!disassemblyRuntimeCodePath.StartsWith(workspacePrefix, StringComparison.OrdinalIgnoreCase) ||
        !requestedOutputPath.StartsWith(workspacePrefix, StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidDataException("Runtime disassembly input and output must remain under the workspace root.");
    }
    if (length <= 0 || length > 1024 * 1024)
    {
        throw new ArgumentOutOfRangeException(nameof(length), "Runtime disassembly length must be between 1 and 1048576 bytes.");
    }

    var runtimeAnalyzer = new RuntimeCodeAnalyzer(disassemblyRuntimeCodePath, 0u);
    var instructions = runtimeAnalyzer.DecodeRange(rva, length);
    var targetIndex = Array.FindIndex(
        args,
        item => string.Equals(item, "--target-rva", StringComparison.OrdinalIgnoreCase));
    var targetRva = targetIndex >= 0 && targetIndex + 1 < args.Length
        ? ParseDisassemblyRva(args[targetIndex + 1])
        : (uint?)null;
    var directCallSites = targetRva.HasValue
        ? runtimeAnalyzer.RawDirectCallSites(targetRva.Value)
        : Array.Empty<uint>();
    Directory.CreateDirectory(Path.GetDirectoryName(requestedOutputPath)!);
    await File.WriteAllLinesAsync(
        requestedOutputPath,
        directCallSites.Select(callSite => $"CALLER 0x{callSite:X8} -> 0x{targetRva!.Value:X8}")
            .Concat(instructions.Select(instruction => $"0x{instruction.Rva:X8} {instruction.Text}")),
        new UTF8Encoding(false));
    Console.WriteLine(JsonSerializer.Serialize(new
    {
        rva = $"0x{rva:X8}",
        length,
        instructions = instructions.Count,
        targetRva = targetRva.HasValue ? $"0x{targetRva.Value:X8}" : null,
        directCallSites = directCallSites.Select(callSite => $"0x{callSite:X8}").ToArray(),
        output = Path.GetRelativePath(workspaceRoot, requestedOutputPath).Replace('\\', '/'),
        NetworkBytesEmitted = false,
        ClientMemoryWritten = false
    }, options));
    return;
}

if (args.Contains("--analyze-live-battle-capture", StringComparer.OrdinalIgnoreCase))
{
    static string RequiredLiveBattleOption(string[] values, string name)
    {
        var index = Array.FindIndex(values, item => string.Equals(item, name, StringComparison.OrdinalIgnoreCase));
        return index >= 0 && index + 1 < values.Length
            ? values[index + 1]
            : throw new ArgumentException($"{name} requires a value.");
    }

    var captureDirectory = Path.GetFullPath(RequiredLiveBattleOption(args, "--capture"));
    var liveBattleRuntimeCodePath = Path.GetFullPath(RequiredLiveBattleOption(args, "--runtime-code"));
    var liveBattleRuntimeCodeSha256 = RequiredLiveBattleOption(args, "--runtime-code-sha256");
    var requestedOutputPath = Path.GetFullPath(RequiredLiveBattleOption(args, "--out"));
    var workspacePrefix = workspaceRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
    if (!captureDirectory.StartsWith(workspacePrefix, StringComparison.OrdinalIgnoreCase) ||
        !liveBattleRuntimeCodePath.StartsWith(workspacePrefix, StringComparison.OrdinalIgnoreCase) ||
        !requestedOutputPath.StartsWith(workspacePrefix, StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidDataException("M5 live battle inputs and output must remain under the workspace root.");
    }

    var liveBattleSnapshot = M5LiveBattleCaptureAnalyzer.Analyze(
        captureDirectory,
        liveBattleRuntimeCodePath,
        liveBattleRuntimeCodeSha256,
        M5BattleStaticRecovery.ExpectedOfficialClientSha256);
    Directory.CreateDirectory(Path.GetDirectoryName(requestedOutputPath)!);
    await File.WriteAllTextAsync(
        requestedOutputPath,
        JsonSerializer.Serialize(liveBattleSnapshot, options),
        new UTF8Encoding(false));
    Console.WriteLine(JsonSerializer.Serialize(new
    {
        liveBattleSnapshot.Status,
        commands = liveBattleSnapshot.Commands.Count,
        effects = liveBattleSnapshot.EffectCandidates.Count,
        snapshots = liveBattleSnapshot.SnapshotCandidates.Count,
        battleFrames = liveBattleSnapshot.BattleFrames.Count,
        output = Path.GetRelativePath(workspaceRoot, requestedOutputPath).Replace('\\', '/'),
        liveBattleSnapshot.NetworkBytesEmitted,
        liveBattleSnapshot.FakeNetworkBytes
    }, options));
    return;
}

if (args.Contains("--recover-m5-battle-static", StringComparer.OrdinalIgnoreCase))
{
    static string RequiredM5StaticOption(string[] values, string name)
    {
        var index = Array.FindIndex(values, item => string.Equals(item, name, StringComparison.OrdinalIgnoreCase));
        return index >= 0 && index + 1 < values.Length
            ? values[index + 1]
            : throw new ArgumentException($"{name} requires a value.");
    }

    var requestedOutputPath = Path.GetFullPath(RequiredM5StaticOption(args, "--out"));
    var workspacePrefix = workspaceRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
    if (!requestedOutputPath.StartsWith(workspacePrefix, StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidDataException("M5 battle static output must remain under the workspace root.");
    }

    var officialClientSha256 = HashM5File(clientPath);
    var runtimeCode = ResolveVerifiedM5RuntimeCode(workspaceRoot, outputRoot, officialClientSha256);
    var m5Static = M5BattleStaticRecovery.Recover(
        new RuntimeCodeAnalyzer(runtimeCode.CodePath),
        runtimeCode.CodePath,
        runtimeCode.CodeSha256,
        officialClientSha256,
        runtimeCode.CaptureAttested,
        runtimeCode.BuildAssociationStatus);
    Directory.CreateDirectory(Path.GetDirectoryName(requestedOutputPath)!);
    await File.WriteAllTextAsync(
        requestedOutputPath,
        JsonSerializer.Serialize(m5Static, options),
        new UTF8Encoding(false));
    Console.WriteLine(JsonSerializer.Serialize(new
    {
        m5Static.Status,
        pinnedInstructions = m5Static.PinnedInstructions.Count,
        receiveDispatchEntries = m5Static.ReceiveDispatchEntries.Count,
        output = Path.GetRelativePath(workspaceRoot, requestedOutputPath).Replace('\\', '/'),
        m5Static.NetworkBytesEmitted,
        m5Static.FakeNetworkBytes
    }, options));
    return;
}

if (args.Contains("--recover-m5-battle-evidence", StringComparer.OrdinalIgnoreCase))
{
    static string RequiredM5Option(string[] values, string name)
    {
        var index = Array.FindIndex(values, item => string.Equals(item, name, StringComparison.OrdinalIgnoreCase));
        return index >= 0 && index + 1 < values.Length
            ? values[index + 1]
            : throw new ArgumentException($"{name} requires a value.");
    }

    var requestedOutputPath = Path.GetFullPath(RequiredM5Option(args, "--out"));
    var workspacePrefix = workspaceRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
    if (!requestedOutputPath.StartsWith(workspacePrefix, StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidDataException("M5 battle evidence output must remain under the workspace root.");
    }

    var officialClientSha256 = HashM5File(clientPath);
    var m5Snapshot = M5BattleEvidenceRecovery.Analyze(workspaceRoot, officialClientSha256);
    Directory.CreateDirectory(Path.GetDirectoryName(requestedOutputPath)!);
    await File.WriteAllTextAsync(
        requestedOutputPath,
        JsonSerializer.Serialize(m5Snapshot, options),
        new UTF8Encoding(false));
    Console.WriteLine(JsonSerializer.Serialize(new
    {
        m5Snapshot.Status,
        sessionCount = m5Snapshot.Sessions.Count,
        m5Snapshot.ActionWindowCount,
        m5Snapshot.ChecksumFailureCount,
        clientOpcodes = m5Snapshot.ClientOpcodeCounts.Count,
        serverOpcodes = m5Snapshot.ServerOpcodeCounts.Count,
        output = Path.GetRelativePath(workspaceRoot, requestedOutputPath).Replace('\\', '/'),
        m5Snapshot.NetworkBytesEmitted,
        m5Snapshot.FakeNetworkBytes
    }, options));
    return;
}

if (args.Contains("--recover-m3-npc-replication-evidence", StringComparer.OrdinalIgnoreCase))
{
    static string RequiredM3Option(string[] values, string name)
    {
        var index = Array.FindIndex(values, item => string.Equals(item, name, StringComparison.OrdinalIgnoreCase));
        return index >= 0 && index + 1 < values.Length
            ? values[index + 1]
            : throw new ArgumentException($"{name} requires a value.");
    }

    var m3RuntimeCodePath = Path.GetFullPath(RequiredM3Option(args, "--runtime-code"));
    var requestedOutputPath = Path.GetFullPath(RequiredM3Option(args, "--out"));
    var workspacePrefix = workspaceRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
    if (!m3RuntimeCodePath.StartsWith(workspacePrefix, StringComparison.OrdinalIgnoreCase) ||
        !requestedOutputPath.StartsWith(workspacePrefix, StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidDataException("M3 runtime evidence input and output must remain under the workspace root.");
    }

    var evidence = await M3NpcReplicationEvidenceAnalyzer.AnalyzeAsync(
        clientPath,
        m3RuntimeCodePath,
        CancellationToken.None);
    Directory.CreateDirectory(Path.GetDirectoryName(requestedOutputPath)!);
    await File.WriteAllTextAsync(
        requestedOutputPath,
        JsonSerializer.Serialize(evidence, options),
        new UTF8Encoding(false));
    Console.WriteLine(JsonSerializer.Serialize(new
    {
        evidence.Status,
        evidence.SchemaVersion,
        output = Path.GetRelativePath(workspaceRoot, requestedOutputPath).Replace('\\', '/'),
        opcodes = evidence.Opcodes.Count,
        functions = evidence.ClientFunctions.Count,
        evidence.RawUnknownPacketBodiesRetained,
        evidence.NetworkEmission,
        evidence.FakeNetworkBytes
    }, options));
    return;
}

if (args.Contains("--recover-m4-npc-interaction-static", StringComparer.OrdinalIgnoreCase))
{
    static string RequiredM4Option(string[] values, string name)
    {
        var index = Array.FindIndex(values, item => string.Equals(item, name, StringComparison.OrdinalIgnoreCase));
        return index >= 0 && index + 1 < values.Length
            ? values[index + 1]
            : throw new ArgumentException($"{name} requires a value.");
    }

    var m4OutputPath = Path.GetFullPath(RequiredM4Option(args, "--out"));
    var workspacePrefix = workspaceRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
    if (!m4OutputPath.StartsWith(workspacePrefix, StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidDataException("M4 static evidence output must remain under the workspace root.");
    }

    var runtimeCode = ResolveVerifiedRuntimeCode(workspaceRoot, outputRoot);
    var runtimeCodeSha256 = Convert.ToHexString(
        System.Security.Cryptography.SHA256.HashData(await File.ReadAllBytesAsync(runtimeCode.CodePath)));
    var evidence = M4NpcInteractionStaticRecovery.Recover(
        new RuntimeCodeAnalyzer(runtimeCode.CodePath),
        runtimeCodeSha256);
    Directory.CreateDirectory(Path.GetDirectoryName(m4OutputPath)!);
    await File.WriteAllTextAsync(
        m4OutputPath,
        JsonSerializer.Serialize(evidence, options),
        new UTF8Encoding(false));
    Console.WriteLine(JsonSerializer.Serialize(new
    {
        evidence.Decision,
        evidence.DirectCallerCount,
        evidence.ResolvedArgumentCount,
        applicationPayloadGroupCount = evidence.ApplicationPayloadGroups.Count,
        output = Path.GetRelativePath(workspaceRoot, m4OutputPath).Replace('\\', '/'),
        evidence.ClientWriteAccessUsed,
        evidence.NetworkBytesEmitted,
        evidence.FakeNetworkBytes
    }, options));
    return;
}

if (args.Contains("--recover-m4-npc-interaction-evidence", StringComparer.OrdinalIgnoreCase))
{
    static string RequiredM4EvidenceOption(string[] values, string name)
    {
        var index = Array.FindIndex(values, item => string.Equals(item, name, StringComparison.OrdinalIgnoreCase));
        return index >= 0 && index + 1 < values.Length
            ? values[index + 1]
            : throw new ArgumentException($"{name} requires a value.");
    }

    var requestedOutputPath = Path.GetFullPath(RequiredM4EvidenceOption(args, "--out"));
    var workspacePrefix = workspaceRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
    if (!requestedOutputPath.StartsWith(workspacePrefix, StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidDataException("M4 interaction evidence output must remain under the workspace root.");
    }

    var runtimeCode = ResolveVerifiedRuntimeCode(workspaceRoot, outputRoot);
    var evidence = M4NpcInteractionEvidenceRecovery.Analyze(workspaceRoot, runtimeCode.CodePath);
    Directory.CreateDirectory(Path.GetDirectoryName(requestedOutputPath)!);
    await File.WriteAllTextAsync(
        requestedOutputPath,
        JsonSerializer.Serialize(evidence, options),
        new UTF8Encoding(false));
    Console.WriteLine(JsonSerializer.Serialize(new
    {
        evidence.Decision,
        capturedApplicationFrames = evidence.CapturedApplicationFrames.Count,
        dialogResponses = evidence.DialogResponses.Count,
        merchantStateResponses = evidence.MerchantStateResponses.Count,
        gatesPassed = evidence.Gates.Count(gate => gate.Status == "PASS"),
        gatesBlocked = evidence.Gates.Count(gate => gate.Status == "BLOCKED"),
        output = Path.GetRelativePath(workspaceRoot, requestedOutputPath).Replace('\\', '/'),
        evidence.NetworkBytesEmitted,
        evidence.FakeNetworkBytes
    }, options));
    return;
}

if (args.Contains("--recover-m4-transport-state-sites", StringComparer.OrdinalIgnoreCase))
{
    static string RequiredM4TransportOption(string[] values, string name)
    {
        var index = Array.FindIndex(values, item => string.Equals(item, name, StringComparison.OrdinalIgnoreCase));
        return index >= 0 && index + 1 < values.Length
            ? values[index + 1]
            : throw new ArgumentException($"{name} requires a value.");
    }

    var requestedOutputPath = Path.GetFullPath(RequiredM4TransportOption(args, "--out"));
    var workspacePrefix = workspaceRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
    if (!requestedOutputPath.StartsWith(workspacePrefix, StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidDataException("M4 transport-state evidence output must remain under the workspace root.");
    }

    var runtimeCode = ResolveVerifiedRuntimeCode(workspaceRoot, outputRoot);
    var transportAnalyzer = new RuntimeCodeAnalyzer(runtimeCode.CodePath);
    var runtimeCodeSha256 = Convert.ToHexString(
        System.Security.Cryptography.SHA256.HashData(await File.ReadAllBytesAsync(runtimeCode.CodePath)));
    var sites = new[] { "2420h", "2556h" }
        .SelectMany(token => transportAnalyzer.InstructionsContaining(token)
            .Select(site =>
            {
                var function = transportAnalyzer.FunctionInstructions(site.Rva);
                var start = site.Rva >= RuntimeCodeAnalyzer.CodeStartRva + 0x60
                    ? site.Rva - 0x60
                    : RuntimeCodeAnalyzer.CodeStartRva;
                return new
                {
                    token,
                    rva = $"0x{site.Rva:X8}",
                    site.Text,
                    functionStartRva = function.Count == 0 ? null : $"0x{function[0].Rva:X8}",
                    functionEndRva = function.Count == 0 ? null : $"0x{function[^1].Rva + (uint)function[^1].Length:X8}",
                    context = transportAnalyzer.DecodeRange(start, 0xD0)
                        .Where(item => item.Rva >= site.Rva - Math.Min(site.Rva, 0x40u) && item.Rva <= site.Rva + 0x60)
                        .Select(item => $"0x{item.Rva:X8} {item.Text}")
                        .ToArray()
                };
            }))
        .OrderBy(site => site.rva, StringComparer.Ordinal)
        .ToArray();
    var transportSnapshot = new
    {
        schemaVersion = "god2-roadmap-m4-transport-state-sites-v1",
        officialClientBuildId = M2WorldContentEvidenceAnalyzer.OfficialClientBuildId,
        runtimeCodeSha256,
        sites,
        exactSessionKeyRetained = false,
        clientWriteAccessUsed = false,
        networkBytesEmitted = false,
        fakeNetworkBytes = false
    };
    Directory.CreateDirectory(Path.GetDirectoryName(requestedOutputPath)!);
    await File.WriteAllTextAsync(
        requestedOutputPath,
        JsonSerializer.Serialize(transportSnapshot, options),
        new UTF8Encoding(false));
    Console.WriteLine(JsonSerializer.Serialize(new
    {
        transportSnapshot.schemaVersion,
        siteCount = sites.Length,
        output = Path.GetRelativePath(workspaceRoot, requestedOutputPath).Replace('\\', '/'),
        transportSnapshot.networkBytesEmitted,
        transportSnapshot.fakeNetworkBytes
    }, options));
    return;
}

if (args.Contains("--exhaust-m4-existing-captures", StringComparer.OrdinalIgnoreCase))
{
    static string RequiredM4ExhaustionOption(string[] values, string name)
    {
        var index = Array.FindIndex(values, item => string.Equals(item, name, StringComparison.OrdinalIgnoreCase));
        return index >= 0 && index + 1 < values.Length
            ? values[index + 1]
            : throw new ArgumentException($"{name} requires a value.");
    }

    var requestedOutputPath = Path.GetFullPath(RequiredM4ExhaustionOption(args, "--out"));
    var workspacePrefix = workspaceRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
    if (!requestedOutputPath.StartsWith(workspacePrefix, StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidDataException("M4 capture-exhaustion output must remain under the workspace root.");
    }

    var exhaustionSnapshot = M4ExistingCaptureExhaustionRecovery.Analyze(workspaceRoot);
    Directory.CreateDirectory(Path.GetDirectoryName(requestedOutputPath)!);
    await File.WriteAllTextAsync(
        requestedOutputPath,
        JsonSerializer.Serialize(exhaustionSnapshot, options),
        new UTF8Encoding(false));
    Console.WriteLine(JsonSerializer.Serialize(new
    {
        exhaustionSnapshot.Status,
        sessionCount = exhaustionSnapshot.Sessions.Count,
        exhaustionSnapshot.Captured037Count,
        exhaustionSnapshot.Captured038Count,
        exhaustionSnapshot.Captured039Count,
        exhaustionSnapshot.Captured086Count,
        closeCandidateCount = exhaustionSnapshot.CloseCandidates.Count,
        output = Path.GetRelativePath(workspaceRoot, requestedOutputPath).Replace('\\', '/'),
        exhaustionSnapshot.NetworkBytesEmitted,
        exhaustionSnapshot.FakeNetworkBytes
    }, options));
    return;
}

if (args.Contains("--recover-m2-world-content-evidence", StringComparer.OrdinalIgnoreCase))
{
    static string RequiredM2Option(string[] values, string name)
    {
        var index = Array.FindIndex(values, item => string.Equals(item, name, StringComparison.OrdinalIgnoreCase));
        return index >= 0 && index + 1 < values.Length
            ? values[index + 1]
            : throw new ArgumentException($"{name} requires a value.");
    }

    var m2RuntimeCodePath = Path.GetFullPath(RequiredM2Option(args, "--runtime-code"));
    var m2OutputPath = Path.GetFullPath(RequiredM2Option(args, "--out"));
    var workspacePrefix = workspaceRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
    if (!m2RuntimeCodePath.StartsWith(workspacePrefix, StringComparison.OrdinalIgnoreCase) ||
        !m2OutputPath.StartsWith(workspacePrefix, StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidDataException("M2 runtime evidence input and output must remain under the workspace root.");
    }

    var npcCsvPath = Path.Combine(Path.GetDirectoryName(clientPath)!, "Data2", "Patch", "NPC.csvZ");
    var evidence = await M2WorldContentEvidenceAnalyzer.AnalyzeAsync(
        clientPath,
        m2RuntimeCodePath,
        npcCsvPath,
        CancellationToken.None);
    Directory.CreateDirectory(Path.GetDirectoryName(m2OutputPath)!);
    await File.WriteAllTextAsync(m2OutputPath, JsonSerializer.Serialize(evidence, options), new UTF8Encoding(false));
    Console.WriteLine(JsonSerializer.Serialize(new
    {
        evidence.Status,
        evidence.SchemaVersion,
        output = Path.GetRelativePath(workspaceRoot, m2OutputPath).Replace('\\', '/'),
        evidence.ApplicationMessageCount,
        observations = evidence.WorldEntityObservations.Count,
        npcSliceCandidates = evidence.NpcSliceCandidates.Count,
        fullUnknownPacketBodiesRetained = false,
        networkEmission = 0
    }, options));
    return;
}

if (args.Contains("--inspect-world-bootstrap", StringComparer.OrdinalIgnoreCase))
{
    var bootstrap = OfficialClientWorldProtocolFrames.BuildWorldBootstrapAfterHandshake();
    static int[] FindPattern(ReadOnlySpan<byte> source, ReadOnlySpan<byte> pattern)
    {
        var offsets = new List<int>();
        for (var offset = 0; offset <= source.Length - pattern.Length; offset++)
        {
            if (source.Slice(offset, pattern.Length).SequenceEqual(pattern))
            {
                offsets.Add(offset);
            }
        }

        return offsets.ToArray();
    }

    var frames = OfficialClientWorldProtocolFrames.GetWorldBootstrapSequence().Select(frame =>
    {
        var decoded = OfficialClientWorldProtocolFrames.DecodeWorldServerPayload(
            bootstrap.AsSpan(frame.Offset, frame.Length));
        var goldenPlayer = OfficialClientWorldProtocolFrames.ParseCurrentPlayerSpawn();
        Span<byte> characterIdPattern = stackalloc byte[sizeof(uint)];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(
            characterIdPattern,
            goldenPlayer.CharacterIdCandidate);
        return new
        {
            frame.Index,
            frame.Offset,
            frame.Length,
            frame.Purpose,
            decodedOpcode = decoded.Length > 2 ? $"0x{decoded[2]:X2}" : null,
            map19PackedOffsets = FindPattern(decoded, Convert.FromHexString("C404")),
            map3PackedOffsets = FindPattern(decoded, Convert.FromHexString("C400")),
            map19PositionOffsets = FindPattern(decoded, Convert.FromHexString("72004400")),
            map3PositionOffsets = FindPattern(decoded, Convert.FromHexString("10031601")),
            goldenCharacterNameOffsets = FindPattern(decoded, Encoding.ASCII.GetBytes(goldenPlayer.CharacterName)),
            goldenCharacterIdOffsets = FindPattern(decoded, characterIdPattern),
            npc2401UInt16LeOffsets = FindPattern(decoded, Convert.FromHexString("6109")),
            npc2643UInt16LeOffsets = FindPattern(decoded, Convert.FromHexString("530A")),
            npc2721UInt16LeOffsets = FindPattern(decoded, Convert.FromHexString("A10A")),
            npc2401UInt32LeOffsets = FindPattern(decoded, Convert.FromHexString("61090000")),
            npc2643UInt32LeOffsets = FindPattern(decoded, Convert.FromHexString("530A0000")),
            npc2721UInt32LeOffsets = FindPattern(decoded, Convert.FromHexString("A10A0000")),
            npc2401NpcCsvRowOffsets = FindPattern(decoded, Convert.FromHexString("3E01")),
            npc2643NpcCsvRowOffsets = FindPattern(decoded, Convert.FromHexString("ED00")),
            npc2721NpcCsvRowOffsets = FindPattern(decoded, Convert.FromHexString("1400")),
            npc2401NpcCsvLineOffsets = FindPattern(decoded, Convert.FromHexString("4A01")),
            npc2643NpcCsvLineOffsets = FindPattern(decoded, Convert.FromHexString("F900")),
            npc2721NpcCsvLineOffsets = FindPattern(decoded, Convert.FromHexString("2000")),
            decodedSha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(decoded))
        };
    }).ToArray();
    Console.WriteLine(JsonSerializer.Serialize(new
    {
        schemaVersion = "god2-world-bootstrap-inspection-v1",
        evidenceId = OfficialClientWorldProtocolFrames.EvidenceId,
        bootstrapLength = bootstrap.Length,
        frames
    }, options));
    return;
}

var inspectStaticRecordsIndex = Array.FindIndex(args, item =>
    string.Equals(item, "--inspect-static-bootstrap-records", StringComparison.OrdinalIgnoreCase));
if (inspectStaticRecordsIndex >= 0)
{
    if (inspectStaticRecordsIndex + 1 >= args.Length)
    {
        throw new ArgumentException("--inspect-static-bootstrap-records requires a runtime-code snapshot path.");
    }

    var staticRecordRuntimeSnapshot = File.ReadAllBytes(Path.GetFullPath(args[inspectStaticRecordsIndex + 1]));
    var lengths = M2WorldContentEvidenceAnalyzer.RecoverIncomingPacketLengths(staticRecordRuntimeSnapshot);
    var sequence = OfficialClientWorldProtocolFrames.GetWorldBootstrapSequence();
    var staticFrame = sequence.Single(frame => frame.Index == 3);
    var bootstrap = OfficialClientWorldProtocolFrames.BuildWorldBootstrapAfterHandshake();
    var decoded = OfficialClientWorldProtocolFrames.DecodeWorldServerPayload(
        bootstrap.AsSpan(staticFrame.Offset, staticFrame.Length));
    var sceneFrame = sequence.Single(frame => frame.Index == 2);
    var sceneDecoded = OfficialClientWorldProtocolFrames.DecodeWorldServerPayload(
        bootstrap.AsSpan(sceneFrame.Offset, sceneFrame.Length));
    var messages = M2WorldContentEvidenceAnalyzer.SplitFixedLengthTransportFrame(
        decoded,
        opcode => lengths[opcode]);
    var legacyPlayerDecoded = OfficialClientWorldProtocolFrames.DecodeWorldServerPayload(
        OfficialClientWorldProtocolFrames.BuildPlayerSpawnFrame128());
    var currentCreatePlayerDecoded = OfficialClientWorldProtocolFrames.DecodeCurrentCreateWorldServerPayload(
        OfficialClientWorldProtocolFrames.BuildCurrentCreateProfilePlayerSpawnFrame128(1, "probe"));
    Console.WriteLine(JsonSerializer.Serialize(new
    {
        schemaVersion = "god2-static-bootstrap-record-inspection-v1",
        runtimeSnapshotSha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(staticRecordRuntimeSnapshot)),
        sceneDecodedHex = Convert.ToHexString(sceneDecoded),
        legacyPlayerDecodedHex = Convert.ToHexString(legacyPlayerDecoded),
        currentCreatePlayerDecodedHex = Convert.ToHexString(currentCreatePlayerDecoded),
        recordCount = messages.Count,
        records = messages.Select(message => new
        {
            message.Index,
            message.TransportOffset,
            opcode = $"0x{message.Bytes[0]:X2}",
            length = message.Bytes.Length,
            hex = Convert.ToHexString(message.Bytes),
            sha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(message.Bytes))
        })
    }, options));
    return;
}

var analyzeFileIoIndex = Array.FindIndex(args, item => string.Equals(item, "--analyze-fileio-etl", StringComparison.OrdinalIgnoreCase));
if (analyzeFileIoIndex >= 0)
{
    if (analyzeFileIoIndex + 1 >= args.Length)
    {
        throw new ArgumentException("--analyze-fileio-etl requires an ETL path.");
    }

    static string RequiredOption(string[] values, string name)
    {
        var index = Array.FindIndex(values, item => string.Equals(item, name, StringComparison.OrdinalIgnoreCase));
        return index >= 0 && index + 1 < values.Length
            ? values[index + 1]
            : throw new ArgumentException($"{name} requires a value.");
    }

    var etlPath = Path.GetFullPath(args[analyzeFileIoIndex + 1]);
    var requestedClientRoot = Path.GetFullPath(RequiredOption(args, "--client-root"));
    var clientPid = int.Parse(RequiredOption(args, "--client-pid"), System.Globalization.CultureInfo.InvariantCulture);
    var includeClientRootAllPids = args.Contains("--include-client-root-all-pids", StringComparer.OrdinalIgnoreCase);
    var outputPath = Path.GetFullPath(RequiredOption(args, "--out"));
    var workspacePrefix = workspaceRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
    if (!etlPath.StartsWith(workspacePrefix, StringComparison.OrdinalIgnoreCase) ||
        !outputPath.StartsWith(workspacePrefix, StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidDataException("FileIO ETL input and analysis output must remain under the workspace root.");
    }

    var clientExePath = Path.Combine(requestedClientRoot, "God2_opt.exe");
    var clientSha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(await File.ReadAllBytesAsync(clientExePath)));
    if (!string.Equals(clientSha256, "6B127086E0C00014DE26137B4EC482801E06E0724C5C05C64561D7F9FF32BD9B", StringComparison.Ordinal))
    {
        throw new InvalidDataException("The FileIO trace client does not match the evidence-pinned official build.");
    }

    var temporaryCsv = Path.Combine(Path.GetTempPath(), $"god2-fileio-{Guid.NewGuid():N}.csv");
    try
    {
        var tracerpt = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "tracerpt.exe",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        tracerpt.ArgumentList.Add(etlPath);
        tracerpt.ArgumentList.Add("-of");
        tracerpt.ArgumentList.Add("CSV");
        tracerpt.ArgumentList.Add("-o");
        tracerpt.ArgumentList.Add(temporaryCsv);
        tracerpt.ArgumentList.Add("-y");
        using var process = System.Diagnostics.Process.Start(tracerpt)
            ?? throw new InvalidOperationException("Unable to start tracerpt.exe.");
        var standardOutput = await process.StandardOutput.ReadToEndAsync();
        var standardError = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        if (process.ExitCode != 0 || !File.Exists(temporaryCsv))
        {
            throw new InvalidOperationException($"tracerpt.exe failed with exit {process.ExitCode}: {standardError.Trim()} {standardOutput.Trim()}");
        }

        var rootSuffix = requestedClientRoot.Length > 2 && requestedClientRoot[1] == ':'
            ? requestedClientRoot[2..].TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            : throw new InvalidDataException("The official client root must be a drive-qualified Windows path.");
        var observations = new Dictionary<string, FileIoObservation>(StringComparer.OrdinalIgnoreCase);
        using var parser = new Microsoft.VisualBasic.FileIO.TextFieldParser(temporaryCsv, Encoding.UTF8)
        {
            TextFieldType = Microsoft.VisualBasic.FileIO.FieldType.Delimited,
            HasFieldsEnclosedInQuotes = true,
            TrimWhiteSpace = true
        };
        parser.SetDelimiters(",");
        _ = parser.ReadFields();
        while (!parser.EndOfData)
        {
            string[]? fields;
            try
            {
                fields = parser.ReadFields();
            }
            catch (Microsoft.VisualBasic.FileIO.MalformedLineException)
            {
                continue;
            }

            if (fields is null || fields.Length < 20 ||
                !string.Equals(fields[0], "Microsoft-Windows-Kernel-File", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var ntPath = fields.Skip(19).LastOrDefault(value => value.Contains(rootSuffix, StringComparison.OrdinalIgnoreCase));
            if (string.IsNullOrWhiteSpace(ntPath))
            {
                continue;
            }

            var pidText = fields[9].Trim();
            var eventProcessId = pidText.StartsWith("0x", StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(
                    pidText[2..],
                    System.Globalization.NumberStyles.HexNumber,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var parsedHexProcessId)
                        ? parsedHexProcessId
                        : int.TryParse(
                            pidText,
                            System.Globalization.NumberStyles.Integer,
                            System.Globalization.CultureInfo.InvariantCulture,
                            out var parsedDecimalProcessId)
                                ? parsedDecimalProcessId
                                : (int?)null;
            var targetPidMatched = eventProcessId == clientPid;
            if (!includeClientRootAllPids && !targetPidMatched)
            {
                continue;
            }

            var suffixIndex = ntPath.IndexOf(rootSuffix, StringComparison.OrdinalIgnoreCase);
            var relative = ntPath[(suffixIndex + rootSuffix.Length)..]
                .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
            if (string.IsNullOrWhiteSpace(relative) || relative.Contains("..", StringComparison.Ordinal))
            {
                continue;
            }

            DateTimeOffset? timestamp = null;
            if (long.TryParse(fields[16], System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var fileTime))
            {
                try
                {
                    timestamp = DateTimeOffset.FromFileTime(fileTime);
                }
                catch (ArgumentOutOfRangeException)
                {
                }
            }

            var eventId = int.TryParse(fields[2], System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var parsedEventId)
                ? parsedEventId
                : -1;
            if (!observations.TryGetValue(relative, out var observation))
            {
                observation = new FileIoObservation(relative);
                observations.Add(relative, observation);
            }

            observation.Add(eventId, timestamp, eventProcessId, targetPidMatched);
        }

        var files = observations.Values
            .OrderBy(value => value.RelativePath, StringComparer.OrdinalIgnoreCase)
            .Select(value =>
            {
                var physicalPath = Path.GetFullPath(Path.Combine(requestedClientRoot, value.RelativePath));
                var clientPrefix = requestedClientRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                var exists = physicalPath.StartsWith(clientPrefix, StringComparison.OrdinalIgnoreCase) && File.Exists(physicalPath);
                var contentIdentity = exists
                    ? InspectObservedFile(physicalPath)
                    : new ObservedFileContentIdentity(null, null, "NotPresent");
                return new
                {
                    relativePath = value.RelativePath.Replace(Path.DirectorySeparatorChar, '/'),
                    extension = Path.GetExtension(value.RelativePath),
                    eventCount = value.EventCount,
                    eventIds = value.EventIds.OrderBy(item => item).ToArray(),
                    processIds = value.ProcessIds.OrderBy(item => item).ToArray(),
                    targetPidObserved = value.TargetPidObserved,
                    firstObservedAtUtc = value.FirstObservedAtUtc,
                    lastObservedAtUtc = value.LastObservedAtUtc,
                    exists,
                    length = contentIdentity.Length,
                    sha256 = contentIdentity.Sha256,
                    contentHashStatus = contentIdentity.Status
                };
            })
            .ToArray();
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        await File.WriteAllTextAsync(outputPath, JsonSerializer.Serialize(new
        {
            schemaVersion = "god2-map-fileio-analysis-v1",
            sourceEtlSha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(await File.ReadAllBytesAsync(etlPath))),
            officialClientSha256 = clientSha256,
            clientPid,
            includeClientRootAllPids,
            packetCapture = false,
            absolutePathsRetained = false,
            fileCount = files.Length,
            files
        }, options));
        Console.WriteLine($"FileIOClientFiles={files.Length}");
        Console.WriteLine($"Output={Path.GetRelativePath(workspaceRoot, outputPath)}");
        return;

        static ObservedFileContentIdentity InspectObservedFile(string physicalPath)
        {
            try
            {
                using var stream = new FileStream(
                    physicalPath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete);
                return new ObservedFileContentIdentity(
                    stream.Length,
                    Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(stream)),
                    "Readable");
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return new ObservedFileContentIdentity(null, null, "TemporarilyUnavailable");
            }
        }
    }
    finally
    {
        if (File.Exists(temporaryCsv))
        {
            File.Delete(temporaryCsv);
        }
    }
}

static (string CodeArtifact, string CodePath) ResolveVerifiedRuntimeCode(
    string workspaceRoot,
    string outputRoot)
{
    var captureManifestPath = Path.Combine(outputRoot, "runtime-capture-manifest.json");
    using var captureManifest = JsonDocument.Parse(
        BoundedFile.ReadAllBytes(captureManifestPath, 1024 * 1024, "Runtime capture manifest"));
    var codeArtifact = captureManifest.RootElement.GetProperty("CodeArtifact").GetString()
        ?? throw new InvalidDataException("Runtime capture manifest has no CodeArtifact.");
    var expectedSha256 = captureManifest.RootElement.GetProperty("CodeSha256").GetString()
        ?? throw new InvalidDataException("Runtime capture manifest has no CodeSha256.");
    var codePath = Path.GetFullPath(Path.Combine(workspaceRoot, codeArtifact.Replace('/', Path.DirectorySeparatorChar)));
    var workspacePrefix = Path.GetFullPath(workspaceRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
    if (!codePath.StartsWith(workspacePrefix, StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidDataException("Runtime code artifact must remain under the workspace root.");
    }

    var file = new FileInfo(codePath);
    if (!file.Exists || file.Length <= 0 || file.Length > 16L * 1024 * 1024)
    {
        throw new InvalidDataException("Runtime code artifact is missing, empty, or exceeds the 16 MiB limit.");
    }

    using var stream = File.OpenRead(codePath);
    var actualSha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(stream));
    if (!string.Equals(actualSha256, expectedSha256, StringComparison.Ordinal))
    {
        throw new InvalidDataException("Runtime code artifact hash does not match its provenance manifest.");
    }

    return (codeArtifact, codePath);
}

static M5RuntimeEvidenceIdentity ResolveVerifiedM5RuntimeCode(
    string workspaceRoot,
    string outputRoot,
    string officialClientSha256)
{
    var runtimeCode = ResolveVerifiedRuntimeCode(workspaceRoot, outputRoot);
    var manifestPath = Path.Combine(outputRoot, "runtime-capture-manifest.json");
    using var manifest = JsonDocument.Parse(
        BoundedFile.ReadAllBytes(manifestPath, 1024 * 1024, "M5 runtime capture manifest"));
    var root = manifest.RootElement;
    var clientBuildId = root.GetProperty("ClientBuildId").GetString() ?? string.Empty;
    var manifestClientSha256 = root.GetProperty("ClientSha256").GetString() ?? string.Empty;
    var codeSha256 = root.GetProperty("CodeSha256").GetString() ?? string.Empty;
    var captureAttested = root.GetProperty("CaptureAttested").GetBoolean();
    var buildAssociationStatus = root.GetProperty("BuildAssociationStatus").GetString() ?? string.Empty;
    if (!string.Equals(clientBuildId, M5BattleEvidenceRecovery.OfficialClientBuildId, StringComparison.Ordinal) ||
        !string.Equals(manifestClientSha256, officialClientSha256, StringComparison.Ordinal))
    {
        throw new InvalidDataException("M5 runtime capture manifest is associated with a different official Client identity.");
    }

    M5BattleStaticRecovery.ValidateBuildIdentity(codeSha256, officialClientSha256);
    return new M5RuntimeEvidenceIdentity(
        runtimeCode.CodeArtifact,
        runtimeCode.CodePath,
        codeSha256,
        captureAttested,
        buildAssociationStatus);
}

static string HashM5File(string path)
{
    using var stream = File.OpenRead(path);
    var sha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(stream));
    M5BattleStaticRecovery.ValidateBuildIdentity(M5BattleStaticRecovery.ExpectedRuntimeCodeSha256, sha256);
    return sha256;
}

var inspectTokenIndex = Array.FindIndex(args, item => string.Equals(item, "--inspect-token", StringComparison.OrdinalIgnoreCase));
if (inspectTokenIndex >= 0)
{
    if (inspectTokenIndex + 1 >= args.Length)
    {
        throw new ArgumentException("--inspect-token requires a disassembly token.");
    }

    var runtimeCodePathIndex = Array.FindIndex(args, item => string.Equals(item, "--runtime-code", StringComparison.OrdinalIgnoreCase));
    var codeStartRvaIndex = Array.FindIndex(args, item => string.Equals(item, "--code-start-rva", StringComparison.OrdinalIgnoreCase));
    var runtimeCodeSha256Index = Array.FindIndex(args, item => string.Equals(item, "--runtime-code-sha256", StringComparison.OrdinalIgnoreCase));
    RuntimeCodeAnalyzer inspectionAnalyzer;
    string sourceArtifact;
    string codeStartRva;
    if (runtimeCodePathIndex >= 0 || codeStartRvaIndex >= 0 || runtimeCodeSha256Index >= 0)
    {
        if (runtimeCodePathIndex < 0 || runtimeCodePathIndex + 1 >= args.Length ||
            codeStartRvaIndex < 0 || codeStartRvaIndex + 1 >= args.Length ||
            runtimeCodeSha256Index < 0 || runtimeCodeSha256Index + 1 >= args.Length)
        {
            throw new ArgumentException(
                "Custom runtime token inspection requires --runtime-code, --code-start-rva, and --runtime-code-sha256 together.");
        }

        var customPath = Path.GetFullPath(args[runtimeCodePathIndex + 1]);
        var workspacePrefix = workspaceRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!customPath.StartsWith(workspacePrefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Custom runtime code input must remain under the workspace root.");
        }

        var file = new FileInfo(customPath);
        if (!file.Exists || file.Length <= 0 || file.Length > 16L * 1024 * 1024)
        {
            throw new InvalidDataException("Custom runtime code input is missing, empty, or exceeds the 16 MiB safety limit.");
        }

        using var customStream = File.OpenRead(customPath);
        var actualSha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(customStream));
        var expectedSha256 = args[runtimeCodeSha256Index + 1].Trim();
        if (!string.Equals(actualSha256, expectedSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Custom runtime code SHA-256 does not match the attested input.");
        }

        var startToken = args[codeStartRvaIndex + 1];
        var startRva = startToken.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? Convert.ToUInt32(startToken[2..], 16)
            : Convert.ToUInt32(startToken, System.Globalization.CultureInfo.InvariantCulture);
        inspectionAnalyzer = new RuntimeCodeAnalyzer(customPath, startRva);
        sourceArtifact = Path.GetRelativePath(workspaceRoot, customPath).Replace('\\', '/');
        codeStartRva = $"0x{startRva:X8}";
    }
    else
    {
        var runtimeCode = ResolveVerifiedRuntimeCode(workspaceRoot, outputRoot);
        inspectionAnalyzer = new RuntimeCodeAnalyzer(runtimeCode.CodePath);
        sourceArtifact = runtimeCode.CodeArtifact;
        codeStartRva = $"0x{RuntimeCodeAnalyzer.CodeStartRva:X8}";
    }

    var token = args[inspectTokenIndex + 1];
    var limitIndex = Array.FindIndex(args, item => string.Equals(item, "--inspect-limit", StringComparison.OrdinalIgnoreCase));
    var limit = limitIndex >= 0 && limitIndex + 1 < args.Length
        ? int.Parse(args[limitIndex + 1], System.Globalization.CultureInfo.InvariantCulture)
        : 500;
    if (limit is < 1 or > 5000)
    {
        throw new ArgumentOutOfRangeException(nameof(limit), "Inspection limit must be between 1 and 5000.");
    }

    var matches = inspectionAnalyzer
        .InstructionsContaining(token)
        .Take(limit)
        .ToArray();
    Console.WriteLine(JsonSerializer.Serialize(new
    {
        SchemaVersion = "runtime-code-token-inspection-v1",
        SourceArtifact = sourceArtifact,
        CodeStartRva = codeStartRva,
        Token = token,
        MatchCount = matches.Length,
        Truncated = matches.Length == limit,
        Instructions = matches.Select(item => new
        {
            Rva = $"0x{item.Rva:X8}",
            item.Length,
            item.Mnemonic,
            item.Text,
            DirectTarget = item.DirectTarget is null ? null : $"0x{item.DirectTarget.Value:X8}",
            AbsoluteMemoryTarget = item.AbsoluteMemoryTarget is null ? null : $"0x{item.AbsoluteMemoryTarget.Value:X8}",
            item.FlowControl
        })
    }, options));
    return;
}

var inspectRvaIndex = Array.FindIndex(args, item => string.Equals(item, "--inspect-rva", StringComparison.OrdinalIgnoreCase));
if (inspectRvaIndex >= 0)
{
    if (inspectRvaIndex + 1 >= args.Length)
    {
        throw new ArgumentException("--inspect-rva requires a hexadecimal or decimal RVA.");
    }

    var token = args[inspectRvaIndex + 1];
    var rva = token.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
        ? Convert.ToUInt32(token[2..], 16)
        : Convert.ToUInt32(token, System.Globalization.CultureInfo.InvariantCulture);
    var runtimeCodePathIndex = Array.FindIndex(args, item => string.Equals(item, "--runtime-code", StringComparison.OrdinalIgnoreCase));
    var codeStartRvaIndex = Array.FindIndex(args, item => string.Equals(item, "--code-start-rva", StringComparison.OrdinalIgnoreCase));
    var runtimeCodeSha256Index = Array.FindIndex(args, item => string.Equals(item, "--runtime-code-sha256", StringComparison.OrdinalIgnoreCase));
    RuntimeCodeAnalyzer inspectionAnalyzer;
    string sourceArtifact;
    string codeStartRva;
    if (runtimeCodePathIndex >= 0 || codeStartRvaIndex >= 0 || runtimeCodeSha256Index >= 0)
    {
        if (runtimeCodePathIndex < 0 || runtimeCodePathIndex + 1 >= args.Length ||
            codeStartRvaIndex < 0 || codeStartRvaIndex + 1 >= args.Length ||
            runtimeCodeSha256Index < 0 || runtimeCodeSha256Index + 1 >= args.Length)
        {
            throw new ArgumentException(
                "Custom runtime inspection requires --runtime-code, --code-start-rva, and --runtime-code-sha256 together.");
        }

        var customPath = Path.GetFullPath(args[runtimeCodePathIndex + 1]);
        var workspacePrefix = workspaceRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!customPath.StartsWith(workspacePrefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Custom runtime code input must remain under the workspace root.");
        }

        var file = new FileInfo(customPath);
        if (!file.Exists || file.Length <= 0 || file.Length > 16L * 1024 * 1024)
        {
            throw new InvalidDataException("Custom runtime code input is missing, empty, or exceeds the 16 MiB safety limit.");
        }

        using var customStream = File.OpenRead(customPath);
        var actualSha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(customStream));
        var expectedSha256 = args[runtimeCodeSha256Index + 1].Trim();
        if (!string.Equals(actualSha256, expectedSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Custom runtime code SHA-256 does not match the attested input.");
        }

        var startToken = args[codeStartRvaIndex + 1];
        var startRva = startToken.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? Convert.ToUInt32(startToken[2..], 16)
            : Convert.ToUInt32(startToken, System.Globalization.CultureInfo.InvariantCulture);
        inspectionAnalyzer = new RuntimeCodeAnalyzer(customPath, startRva);
        sourceArtifact = Path.GetRelativePath(workspaceRoot, customPath).Replace('\\', '/');
        codeStartRva = $"0x{startRva:X8}";
    }
    else
    {
        var resolvedInspectionRuntimeCode = ResolveVerifiedRuntimeCode(workspaceRoot, outputRoot);
        inspectionAnalyzer = new RuntimeCodeAnalyzer(resolvedInspectionRuntimeCode.CodePath);
        sourceArtifact = resolvedInspectionRuntimeCode.CodeArtifact;
        codeStartRva = $"0x{RuntimeCodeAnalyzer.CodeStartRva:X8}";
    }

    var lengthIndex = Array.FindIndex(args, item => string.Equals(item, "--inspect-length", StringComparison.OrdinalIgnoreCase));
    var instructions = lengthIndex >= 0 && lengthIndex + 1 < args.Length
        ? inspectionAnalyzer.DecodeRange(
            rva,
            int.Parse(args[lengthIndex + 1], System.Globalization.CultureInfo.InvariantCulture))
        : inspectionAnalyzer.FunctionInstructions(rva);
    Console.WriteLine(JsonSerializer.Serialize(new
    {
        SchemaVersion = "runtime-code-inspection-v1",
        SourceArtifact = sourceArtifact,
        CodeStartRva = codeStartRva,
        Rva = $"0x{rva:X8}",
        InstructionCount = instructions.Count,
        Instructions = instructions.Select(item => new
        {
            Rva = $"0x{item.Rva:X8}",
            item.Length,
            item.Mnemonic,
            item.Text,
            DirectTarget = item.DirectTarget is null ? null : $"0x{item.DirectTarget.Value:X8}",
            AbsoluteMemoryTarget = item.AbsoluteMemoryTarget is null ? null : $"0x{item.AbsoluteMemoryTarget.Value:X8}",
            item.FlowControl
        })
    }, options));
    return;
}

if (args.Contains("--login-outbound-recon", StringComparer.OrdinalIgnoreCase))
{
    var captureManifestPath = Path.Combine(outputRoot, "runtime-capture-manifest.json");
    var captureManifest = JsonDocument.Parse(await File.ReadAllTextAsync(captureManifestPath));
    var codeArtifact = captureManifest.RootElement.GetProperty("CodeArtifact").GetString()
        ?? throw new InvalidDataException("Runtime capture manifest has no CodeArtifact.");
    var codeSha256 = captureManifest.RootElement.GetProperty("CodeSha256").GetString()
        ?? throw new InvalidDataException("Runtime capture manifest has no CodeSha256.");
    var codePath = Path.Combine(workspaceRoot, codeArtifact.Replace('/', Path.DirectorySeparatorChar));
    var actualCodeSha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(await File.ReadAllBytesAsync(codePath)));
    if (!string.Equals(actualCodeSha256, codeSha256, StringComparison.Ordinal))
    {
        throw new InvalidDataException("Runtime code artifact hash does not match its provenance manifest.");
    }

    var loginSnapshot = LoginOutboundStaticRecovery.Recover(new RuntimeCodeAnalyzer(codePath), codeSha256);
    var outputIndex = Array.FindIndex(args, item => string.Equals(item, "--out", StringComparison.OrdinalIgnoreCase));
    var requestedOutput = outputIndex >= 0 && outputIndex + 1 < args.Length
        ? args[outputIndex + 1]
        : Path.Combine(workspaceRoot, "Artifacts", "PostRemediationCompatibilityCheckpoint", "login-outbound-static-recon.json");
    var outputPath = Path.GetFullPath(requestedOutput);
    var rootPrefix = workspaceRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
    if (!outputPath.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidDataException("Login outbound reconstruction output must remain under the workspace root.");
    }

    Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
    await File.WriteAllTextAsync(outputPath, JsonSerializer.Serialize(loginSnapshot, options));
    Console.WriteLine($"LoginOutboundCallsites={loginSnapshot.Callsites.Count}");
    Console.WriteLine($"LoginOutboundCandidates={loginSnapshot.CandidateCount}");
    return;
}

var analyzer = new PeStaticAnalyzer(clientPath);
var snapshot = analyzer.Analyze();
var capture = RuntimeCaptureSelection.Resolve(
    workspaceRoot,
    outputRoot,
    snapshot.ClientBuildId,
    snapshot.ClientSha256,
    options);
var runtimeCodePath = capture?.CodePath;
var runtimeSnapshot = runtimeCodePath is not null
    ? new RuntimeCodeAnalyzer(runtimeCodePath).Analyze(workspaceRoot)
    : null;
var lengthTablePath = capture?.LengthTablePath;
DispatchRecoverySnapshot? dispatchSnapshot = null;
if (runtimeCodePath is not null && lengthTablePath is not null)
{
    var runtimeAnalyzer = new RuntimeCodeAnalyzer(runtimeCodePath);
    dispatchSnapshot = new DispatchRegistryRecovery(runtimeCodePath, lengthTablePath)
        .Recover(runtimeAnalyzer.ReadObservations(workspaceRoot));
}
await File.WriteAllTextAsync(
    Path.Combine(outputRoot, "static-analysis-diagnostics.json"),
    JsonSerializer.Serialize(snapshot, options));
if (runtimeSnapshot is not null)
{
    await File.WriteAllTextAsync(
        Path.Combine(outputRoot, "runtime-code-analysis.json"),
        JsonSerializer.Serialize(runtimeSnapshot, options));
}
if (dispatchSnapshot is not null)
{
    var evidenceRoot = Path.Combine(workspaceRoot, "protocol", "evidence", "current-build");
    Directory.CreateDirectory(evidenceRoot);
    await File.WriteAllTextAsync(
        Path.Combine(evidenceRoot, "client-dispatch-registry.json"),
        JsonSerializer.Serialize(dispatchSnapshot, options));
    await File.WriteAllTextAsync(
        Path.Combine(evidenceRoot, "client-handler-clusters.json"),
        JsonSerializer.Serialize(new
        {
            dispatchSnapshot.SchemaVersion,
            dispatchSnapshot.ClientBuildId,
            Clusters = dispatchSnapshot.HandlerClusters,
            dispatchSnapshot.UniqueStateHandlerCount
        }, options));
    await File.WriteAllTextAsync(
        Path.Combine(evidenceRoot, "packet-reader-writer-model.json"),
        JsonSerializer.Serialize(new
        {
            SchemaVersion = "packet-reader-writer-model-v1",
            dispatchSnapshot.ClientBuildId,
            Inbound = new
            {
                FramingReaderRva = "0x0007C0B0",
                DecodeRva = "0x00078D70",
                LengthPrefix = "UInt16LittleEndian",
                Checksum = "sum(payload)+0x3C compared with final byte",
                Encryption = "rolling previous-byte plus build-local key table; conditional",
                Compression = "signed-length flag with RLE control byte 0xC0 and low-six-bit repeat count",
                StateDispatch = dispatchSnapshot.Tables
            },
            Outbound = new
            {
                FrameBuilderRva = "0x00078EA0",
                ContextWriterRva = "0x00078F40",
                OpcodeFrameBuilderRva = "0x0007C2B0",
                OpcodeLengthTableVa = "0x00894090",
                TransportWriteCallerRva = "0x0007BB68",
                Policies = dispatchSnapshot.OutboundLengthPolicies
            },
            Safety = new { ClientWriteAccessUsed = false, ClientBinaryModified = false, ProductionBytesEmitted = false }
        }, options));
}
var officialContentRoot = Path.Combine(workspaceRoot, "db", "imports", "official");
var knowledgeSnapshot = OfficialKnowledgeRecovery.Recover(
    officialContentRoot,
    Path.Combine(workspaceRoot, "Knowledge"),
    options);
var mapRoot = WorkspaceDiscovery.FindOfficialMapRoot(clientPath);
var mapSnapshot = MapContentRecovery.Recover(mapRoot);
await File.WriteAllTextAsync(
    Path.Combine(outputRoot, "map-collision-results.json"),
    JsonSerializer.Serialize(mapSnapshot, options));
var protocolSchemaSnapshot = dispatchSnapshot is null
    ? null
    : ProtocolSchemaRecovery.Write(workspaceRoot, dispatchSnapshot, options);
if (runtimeSnapshot is not null && dispatchSnapshot is not null && protocolSchemaSnapshot is not null)
{
    OfflineReportWriter.Write(
        workspaceRoot,
        snapshot,
        runtimeSnapshot,
        dispatchSnapshot,
        protocolSchemaSnapshot,
        knowledgeSnapshot,
        mapSnapshot,
        capture!.Provenance,
        options);
}

Console.WriteLine($"ClientBuild={snapshot.ClientBuildId}");
Console.WriteLine($"SHA256={snapshot.ClientSha256}");
Console.WriteLine($"Sections={snapshot.Sections.Count}");
Console.WriteLine($"Imports={snapshot.Imports.Count}");
Console.WriteLine($"Instructions={snapshot.InstructionCount}");
Console.WriteLine($"DirectCalls={snapshot.DirectCallCount}");
Console.WriteLine($"IndirectCalls={snapshot.IndirectCallCount}");
Console.WriteLine($"NetworkCallSites={snapshot.NetworkCallSites.Count}");
if (runtimeSnapshot is not null)
{
    Console.WriteLine($"RuntimeCodeBytes={runtimeSnapshot.CodeBytes}");
    Console.WriteLine($"DecodeObservations={runtimeSnapshot.DecodeObservationCount}");
    Console.WriteLine($"ObservedOpcodes={runtimeSnapshot.ObservedOpcodeCount}");
    Console.WriteLine($"DecodeCallerClusters={runtimeSnapshot.DecodeCallerClusterCount}");
    Console.WriteLine($"IndexedJumps={runtimeSnapshot.IndexedJumps.Count}");
    foreach (var cluster in runtimeSnapshot.CallerClusters)
    {
        Console.WriteLine($"CLUSTER caller={cluster.CallerRva} function={cluster.FunctionStartRva}-{cluster.FunctionEndRva} observations={cluster.ObservationCount} opcodes={string.Join(',', cluster.Opcodes)} jumps={string.Join(',', cluster.IndexedJumpSites)}");
    }
}
if (dispatchSnapshot is not null)
{
    Console.WriteLine($"DispatchEntries={dispatchSnapshot.StateSpecificRegistryEntryCount}");
    Console.WriteLine($"StateHandlers={dispatchSnapshot.UniqueStateHandlerCount}");
    Console.WriteLine($"OutboundLengthPolicies={dispatchSnapshot.OutboundLengthPolicies.Count}");
    foreach (var table in dispatchSnapshot.Tables)
    {
        Console.WriteLine($"DISPATCH state={table.State} opcodes={table.OpcodeCount} handlers={table.UniqueHandlerCount} default={table.DefaultOpcodeCount}");
    }
}
Console.WriteLine($"KnowledgeDomains={knowledgeSnapshot.DomainCount}");
Console.WriteLine($"KnowledgeIndexedEntities={knowledgeSnapshot.IndexedEntityCount}");
Console.WriteLine($"MapCandidates={mapSnapshot.CandidateFileCount}");
Console.WriteLine($"MapParsed={mapSnapshot.ParsedMapCount}");
Console.WriteLine($"MapAStarPassed={mapSnapshot.AStarPassedCount}");
if (protocolSchemaSnapshot is not null)
{
    Console.WriteLine($"PacketFamilies={protocolSchemaSnapshot.PacketFamilyCount}");
    Console.WriteLine($"DecoderCandidates={protocolSchemaSnapshot.DecoderCandidateCount}");
    Console.WriteLine($"DecoderVerified={protocolSchemaSnapshot.DecoderVerifiedCount}");
    Console.WriteLine($"SerializerCandidates={protocolSchemaSnapshot.SerializerCandidateCount}");
    Console.WriteLine($"SerializerVerified={protocolSchemaSnapshot.SerializerVerifiedCount}");
}
foreach (var call in snapshot.NetworkCallSites)
{
    Console.WriteLine($"NETWORK {call.Import} call={call.CallRva} return={call.ReturnRva}");
}

Console.WriteLine("OnDiskCodeStatus=PackedOrTransformed; semantic analysis uses read-only unpacked runtime memory.");

internal sealed class FileIoObservation
{
    public FileIoObservation(string relativePath)
    {
        RelativePath = relativePath;
    }

    public string RelativePath { get; }

    public int EventCount { get; private set; }

    public HashSet<int> EventIds { get; } = [];

    public HashSet<int> ProcessIds { get; } = [];

    public bool TargetPidObserved { get; private set; }

    public DateTimeOffset? FirstObservedAtUtc { get; private set; }

    public DateTimeOffset? LastObservedAtUtc { get; private set; }

    public void Add(int eventId, DateTimeOffset? timestamp, int? processId, bool targetPidObserved)
    {
        EventCount++;
        if (eventId >= 0)
        {
            EventIds.Add(eventId);
        }

        if (processId is not null)
        {
            ProcessIds.Add(processId.Value);
        }
        TargetPidObserved |= targetPidObserved;

        if (timestamp is not null && (FirstObservedAtUtc is null || timestamp < FirstObservedAtUtc))
        {
            FirstObservedAtUtc = timestamp;
        }

        if (timestamp is not null && (LastObservedAtUtc is null || timestamp > LastObservedAtUtc))
        {
            LastObservedAtUtc = timestamp;
        }
    }
}

internal sealed record ObservedFileContentIdentity(long? Length, string? Sha256, string Status);

internal sealed record M5RuntimeEvidenceIdentity(
    string CodeArtifact,
    string CodePath,
    string CodeSha256,
    bool CaptureAttested,
    string BuildAssociationStatus);

internal static class WorkspaceDiscovery
{
    public static string FindWorkspaceRoot(string start)
    {
        var directory = new DirectoryInfo(start);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "God2ClassicServer.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("God2ClassicServer.sln was not found above the executable directory.");
    }

    public static string ResolveClientPath(string workspaceRoot, string[] args)
    {
        var argumentIndex = Array.FindIndex(args, item => string.Equals(item, "--client", StringComparison.OrdinalIgnoreCase));
        if (argumentIndex >= 0 && argumentIndex + 1 < args.Length)
        {
            return Validate(args[argumentIndex + 1]);
        }

        var configured = Environment.GetEnvironmentVariable("GOD2_OFFLINE_CLIENT_PATH");
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return Validate(configured);
        }

        var searchRoot = Directory.GetParent(workspaceRoot)?.FullName
            ?? throw new DirectoryNotFoundException("Workspace parent is not available for client discovery.");
        var candidates = Directory.EnumerateFiles(searchRoot, "God2_opt.exe", SearchOption.AllDirectories)
            .Where(path => path.Contains("OfficialClientWorkingCopy", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .ToArray();
        foreach (var candidate in candidates)
        {
            using var stream = File.OpenRead(candidate);
            var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(stream));
            if (string.Equals(hash, PeStaticAnalyzer.ExpectedClientSha256, StringComparison.Ordinal))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException("No build-locked God2_opt.exe was discovered. Pass --client <path>.");
    }

    public static string FindOfficialMapRoot(string clientPath)
    {
        var besideClient = Path.Combine(Path.GetDirectoryName(clientPath)!, "Data2", "map");
        if (Directory.Exists(besideClient))
        {
            return besideClient;
        }

        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        foreach (var executable in Directory.EnumerateFiles(desktop, "God2_opt.exe", SearchOption.AllDirectories))
        {
            var mapRoot = Path.Combine(Path.GetDirectoryName(executable)!, "Data2", "map");
            if (!Directory.Exists(mapRoot)) continue;
            using var stream = File.OpenRead(executable);
            var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(stream));
            if (string.Equals(hash, PeStaticAnalyzer.ExpectedClientSha256, StringComparison.Ordinal))
            {
                return mapRoot;
            }
        }

        throw new DirectoryNotFoundException("No build-locked official Data2/map directory was discovered.");
    }

    private static string Validate(string path)
    {
        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("Configured client executable does not exist.", fullPath);
        }

        return fullPath;
    }
}
