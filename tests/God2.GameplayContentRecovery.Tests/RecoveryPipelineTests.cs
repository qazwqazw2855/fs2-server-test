using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using God2.GameplayContentRecovery;

namespace God2.GameplayContentRecovery.Tests;

public sealed class RecoveryPipelineTests
{
    [Fact]
    public void Local_login_account_preparation_targets_the_canonical_runtime_authority()
    {
        var root = FindRepositoryRoot();
        var analyzer = File.ReadAllText(Path.Combine(
            root, "tools", "God2.ClientInstrumentation", "Analyzer", "Program.cs"));

        Assert.Contains("UpsertCanonicalAccountAsync", analyzer, StringComparison.Ordinal);
        Assert.Contains("ReadCanonicalAccountVerificationAsync", analyzer, StringComparison.Ordinal);
        Assert.Contains("INSERT INTO `god2_player`.`accounts`", analyzer, StringComparison.Ordinal);
        Assert.Contains("database = \"god2_player\"", analyzer, StringComparison.Ordinal);
    }

    [Fact]
    public void PackedDecoderDecodesLiteralStreamExactly()
    {
        var expected = Encoding.ASCII.GetBytes("MapId,Name\r\n19,FengHua\r\n");
        var packed = PackLiterals(expected);

        Assert.Equal(expected, God2PackedFile.Decode(packed));
    }

    [Fact]
    public void PackedDecoderRejectsWrongWrapperAndTruncation()
    {
        Assert.Throws<InvalidDataException>(() => God2PackedFile.Decode([0x00, 0x16, 0, 0, 0, 0, 0]));

        var packed = PackLiterals(Encoding.ASCII.GetBytes("abcdef"));
        Assert.Throws<InvalidDataException>(() => God2PackedFile.Decode(packed[..^2]));
    }

    [Fact]
    public void FlowRomInventory_extracts_bounded_printable_references()
    {
        var bytes = Encoding.ASCII.GetBytes("MIGSROM02\0cityi2.mdt\u0001x\0portal_map19\0");

        var strings = FlowRomInventory.ExtractAsciiStrings(bytes);

        Assert.Equal(["MIGSROM02", "cityi2.mdt", "portal_map19"], strings);
    }

    [Fact]
    public void FlowRomInventory_reassembles_signature_from_wrapper_marker_and_payload()
    {
        Assert.True(FlowRomInventory.IsMigRom((byte)'M', "IGSROM02"u8));
        Assert.False(FlowRomInventory.IsMigRom((byte)'X', "IGSROM02"u8));
        Assert.False(FlowRomInventory.IsMigRom((byte)'M', "UNKNOWN"u8));
    }

    [Fact]
    public void Client_map_inventory_reads_hmd_pass_dimensions()
    {
        var decoded = Encoding.ASCII.GetBytes("HMD v1.6\r\nROMFILE 1\r\nroom.rom\r\nPass\r\n10 30\r\n0 0 0\r\n");

        Assert.True(ClientMapResourceInventory.TryReadHmdPassGrid(decoded, out var width, out var height));
        Assert.Equal(10, width);
        Assert.Equal(30, height);
    }

    [Theory]
    [InlineData("HMD v1.5\r\nPass\r\n10 30\r\n")]
    [InlineData("HMD v1.6\r\nPass\r\n0 30\r\n")]
    [InlineData("HMD v1.6\r\nPass\r\n10 999\r\n")]
    [InlineData("HMD v1.6\r\nNoPass\r\n10 30\r\n")]
    public void Client_map_inventory_rejects_invalid_hmd_pass_dimensions(string text)
    {
        Assert.False(ClientMapResourceInventory.TryReadHmdPassGrid(Encoding.ASCII.GetBytes(text), out _, out _));
    }

    [Fact]
    public void Official_map_definitions_publish_identity_world_map_coordinate_and_resource_authority()
    {
        var rows = Enumerable.Range(0, 144)
            .Select(index => new CsvRow(
                22727 + index,
                string.Empty,
                ["南方", $"Map{index}", $"地圖{index}", "1", "0", index.ToString(), "21", "42", "field.wav", "battle.wav", $"South/Map{index}/Map{index}.mdt", "126", "168", "1"]))
            .ToArray();
        rows[11] = rows[11] with
        {
            Fields = ["南方", "mazeS05", "雨龍森林", "5", "3", "40", "605", "387", "field.wav", "battle.wav", "South002/cityS04/cityS04.mdt", "252", "252", "1", "雨龍森林"]
        };
        var section = new GameDataSection("Map_City_Coordniate", 144, 22726, rows);

        var definitions = OfficialClientExtractor.ParseOfficialMapDefinitions(section);

        Assert.Equal(144, definitions.Count);
        var rainDragonForest = Assert.Single(definitions, definition => definition.ClientAreaId == 5 && definition.ClientMapId == 40);
        Assert.Equal("雨龍森林", rainDragonForest.DisplayName);
        Assert.Equal((605, 387), (rainDragonForest.WorldMapX, rainDragonForest.WorldMapY));
        Assert.Equal("client:map/south002/mazes05/mazes05", rainDragonForest.ResourceAuthorityKey);
        Assert.Equal(["雨龍森林"], rainDragonForest.FloorNames);
    }

    [Fact]
    public void Game_data_sections_accepts_the_official_map_marker_with_inline_headers()
    {
        var rows = new[]
        {
            new CsvRow(10, string.Empty, ["Map_City_Coordniate", "2", "地名", "islandID", "area", "area ID"]),
            new CsvRow(11, string.Empty, ["南方", "Map1"]),
            new CsvRow(12, string.Empty, ["北方", "Map2"])
        };

        var sections = GameDataSections.Parse(rows);

        var section = Assert.Single(sections).Value;
        Assert.Equal("Map_City_Coordniate", section.Name);
        Assert.Equal(2, section.Rows.Count);
    }

    [Fact]
    public async Task Gamedata_section_export_preserves_source_lines_fields_and_hash()
    {
        var root = Path.Combine(Path.GetTempPath(), $"god2-section-export-{Guid.NewGuid():N}");
        var sourceDirectory = Path.Combine(root, "Data2", "Patch", "Comm");
        var output = Path.Combine(root, "out", "NPCAppearData.json");
        Directory.CreateDirectory(sourceDirectory);
        try
        {
            var text = "NPCAppearData,2\r\n1,Alpha,10,20\r\n2,Beta,30,40\r\n";
            await File.WriteAllBytesAsync(
                Path.Combine(sourceDirectory, "gamedata.csvZ"),
                PackLiterals(Encoding.ASCII.GetBytes(text)));

            var result = await GameDataSectionExporter.WriteAsync(
                root,
                "NPCAppearData",
                output,
                CancellationToken.None);

            Assert.Equal(2, result.DeclaredCount);
            Assert.Equal(2, result.ExportedCount);
            Assert.Equal(2, result.Rows[0].SourceLine);
            Assert.Equal(["1", "Alpha", "10", "20"], result.Rows[0].Fields);
            Assert.Equal(64, result.SourceSha256.Length);
            Assert.True(File.Exists(output));
            Assert.Contains("\"section\": \"NPCAppearData\"", await File.ReadAllTextAsync(output), StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Official_immortal_catalog_recovers_static_identity_without_promoting_runtime_semantics()
    {
        var section = new GameDataSection("GOD", 2, 10,
        [
            new CsvRow(11, "", ["2801", "-1", "God01001", "1", "姜子牙", "1791"]),
            new CsvRow(12, "", ["2834", "-1", "God01034", "1", "女娲", "5566"])
        ]);

        var records = OfficialImmortalCatalogExporter.Parse(
            section,
            new string('A', 64),
            new ZhTwLocalization(God2Glossary.Create()));

        Assert.Collection(records,
            row =>
            {
                Assert.Equal(2801, row.ClientCatalogId);
                Assert.Equal("God01001", row.ResourceKey);
                Assert.Equal("姜子牙", row.Name);
                Assert.Equal(1791, row.ClientPresentationValue);
                Assert.False(row.RuntimeEligible);
            },
            row =>
            {
                Assert.Equal(2834, row.ClientCatalogId);
                Assert.Equal("God01034", row.ResourceKey);
                Assert.Equal("女媧", row.Name);
                Assert.Equal("EvidenceBlockedMissingLoginStatsOwnershipAndSkills", row.RuntimeEvidenceStatus);
                Assert.Contains("baseHp", row.MissingRuntimeFields);
            });
    }

    [Fact]
    public void Official_immortal_catalog_rejects_duplicate_or_malformed_static_identity()
    {
        var localization = new ZhTwLocalization(God2Glossary.Create());
        var duplicate = new GameDataSection("GOD", 2, 10,
        [
            new CsvRow(11, "", ["2801", "-1", "God01001", "1", "姜子牙", "1791"]),
            new CsvRow(12, "", ["2801", "-1", "God01002", "1", "鄧嬋玉", "1771"])
        ]);
        var malformed = new GameDataSection("GOD", 1, 10,
        [
            new CsvRow(11, "", ["2801", "-1", "Unknown", "1", "姜子牙", "1791"])
        ]);

        Assert.Throws<InvalidDataException>(() => OfficialImmortalCatalogExporter.Parse(duplicate, new string('B', 64), localization));
        Assert.Throws<InvalidDataException>(() => OfficialImmortalCatalogExporter.Parse(malformed, new string('C', 64), localization));
    }

    [Fact]
    public void Cross_version_client_leads_are_non_runtime_and_preserve_exact_build_gate()
    {
        var path = Path.Combine(
            FindRepositoryRoot(),
            "db", "imports", "supplemental", "xjz-c", "cross-version-client-recovery-leads.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;

        Assert.Equal("CrossVersionRecovered", root.GetProperty("evidenceClass").GetString());
        Assert.False(root.GetProperty("runtimeEligible").GetBoolean());
        Assert.Equal("god2-cross-version-client-recovery-leads-v3", root.GetProperty("schemaVersion").GetString());
        Assert.Equal(
            "c6a002de544732919b34194d991a34e1cda7c684394233948601e85d8a156562",
            root.GetProperty("package").GetProperty("archiveSha256").GetString());
        Assert.Equal(927, root.GetProperty("package").GetProperty("archiveFileCount").GetInt32());
        Assert.Equal(
            "a03bfba571fd18efe7193d73d566446ca31f03f139a80fbb888200f1dbb7d154",
            root.GetProperty("package").GetProperty("evidenceManifestSha256").GetString());
        Assert.Equal("not_byte_identical", root.GetProperty("package").GetProperty("identityConclusion").GetString());
        Assert.Equal(34, root.GetProperty("staticCatalogFindings").GetProperty("immortals").GetProperty("exactClientRows").GetInt32());
        Assert.False(root.GetProperty("staticCatalogFindings").GetProperty("immortals").GetProperty("runtimeEligible").GetBoolean());
        Assert.True(root.GetProperty("officialServerFollowUpDeferred").GetBoolean());
        Assert.Contains(
            root.GetProperty("promotionPolicy").GetProperty("mustNotUseFor").EnumerateArray(),
            item => item.GetString()!.Contains("server-authoritative", StringComparison.Ordinal));
    }

    [Fact]
    public void V4_item_request_semantics_remain_cross_version_while_exact_build_closes_static_fields()
    {
        var path = Path.Combine(
            FindRepositoryRoot(),
            "db", "imports", "supplemental", "xjz-c", "cross-version-client-recovery-leads.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var evidence = document.RootElement.GetProperty("itemRequestDataflow");
        var crossVersion = evidence.GetProperty("crossVersionEvidence");
        var exactBoundary = evidence.GetProperty("exactFormalClientBoundary");

        Assert.Equal(
            "ExactBuildStaticAndObserved_CrossVersionSemanticCorroboration",
            evidence.GetProperty("evidenceClass").GetString());
        Assert.False(evidence.GetProperty("runtimeEligible").GetBoolean());
        Assert.Equal(8403, crossVersion.GetProperty("catalogCounts").GetProperty("itemRows").GetInt32());
        Assert.Equal(117, crossVersion.GetProperty("catalogCounts").GetProperty("literalOutboundOpcodes").GetInt32());
        Assert.Equal(3, exactBoundary.GetProperty("opcode25ObservedCount").GetInt32());
        Assert.Equal(8, exactBoundary.GetProperty("opcode25FrameLength").GetInt32());
        Assert.Equal("item_id:u16le,slot:u8,raw_tail:u8", exactBoundary.GetProperty("opcode25StaticLayout").GetString());
        Assert.Equal("OpaqueNotConsistentlyWrittenByExactSender", exactBoundary.GetProperty("opcode25TailStatus").GetString());
        Assert.Equal(
            "actor_or_target_id:u16le,item_id:u16le,slot:u8,raw_tail:u8",
            exactBoundary.GetProperty("opcode26StaticLayout").GetString());
        Assert.Equal("0x3F", exactBoundary.GetProperty("correlatedResponse").GetProperty("opcode").GetString());
        Assert.False(exactBoundary.GetProperty("rawOpcode25PayloadPreserved").GetBoolean());
        Assert.False(exactBoundary.GetProperty("runtimeMutationAllowed").GetBoolean());
        Assert.Contains("No item effect", evidence.GetProperty("adoptedUse").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Exact_build_item_request_evidence_is_anchored_to_sender_bytes_not_v4_addresses()
    {
        var root = FindRepositoryRoot();
        var evidencePath = Path.Combine(
            root, "Artifacts", "OfflineClientReverseEngineering", "equipment-outbound-static",
            "exact-item-request-evidence.json");
        using var evidenceDocument = JsonDocument.Parse(File.ReadAllText(evidencePath));
        var evidence = evidenceDocument.RootElement;

        Assert.Equal("god2-exact-item-request-static-evidence-v1", evidence.GetProperty("schemaVersion").GetString());
        Assert.Equal("ExactBuildStaticReadOnly", evidence.GetProperty("analysisClass").GetString());
        Assert.False(evidence.GetProperty("runtimeEligible").GetBoolean());
        Assert.Equal("item_id:u16le,slot:u8,raw_tail:u8", evidence.GetProperty("opcode25").GetProperty("exactLayout").GetString());
        Assert.Equal(
            "actor_or_target_id:u16le,item_id:u16le,slot:u8,raw_tail:u8",
            evidence.GetProperty("opcode26").GetProperty("exactLayout").GetString());
        Assert.False(evidence.GetProperty("opcode25").GetProperty("directEnqueueCallsites")[0]
            .GetProperty("thirdArgumentInPayload").GetBoolean());
        Assert.False(evidence.GetProperty("serverBoundary").GetProperty("runtimeMutationAllowed").GetBoolean());

        var outboundPath = Path.Combine(
            root, "Artifacts", "OfflineClientReverseEngineering", "equipment-outbound-static",
            "all-outbound-callsites.json");
        Assert.Equal(
            "1dcbef3ed97df20fe065c599f819c88c6ac9020684802df871421016e97d7fdd",
            Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(outboundPath))).ToLowerInvariant());
        using var outboundDocument = JsonDocument.Parse(File.ReadAllText(outboundPath));
        var opcode25Callsites = outboundDocument.RootElement
            .GetProperty("EquipmentFrameLengthCandidates")
            .EnumerateArray()
            .Where(row => row.TryGetProperty("Opcode", out var opcode) &&
                          opcode.ValueKind == JsonValueKind.Number &&
                          opcode.GetInt32() == 0x25 &&
                          row.TryGetProperty("PayloadLength", out var payloadLength) &&
                          payloadLength.ValueKind == JsonValueKind.Number &&
                          payloadLength.GetInt32() == 4)
            .ToArray();
        Assert.Equal(2, opcode25Callsites.Length);
        Assert.Contains(opcode25Callsites, row => row.GetProperty("CallRva").GetString() == "0x000AEFA8");
        Assert.Contains(opcode25Callsites, row => row.GetProperty("CallRva").GetString() == "0x000AF7C1");

        var disassembly = File.ReadAllText(Path.Combine(
            root, "Artifacts", "AnalysisScratch", "EquipmentStatic",
            "disasm-item-consumer-corpus-89000-159000.txt"));
        Assert.Contains("0x000AEF8C mov ax,[ebp+8]", disassembly, StringComparison.Ordinal);
        Assert.Contains("0x000AEF9D mov [ebp+0Ah],al", disassembly, StringComparison.Ordinal);
        Assert.Contains("0x000AEFA6 push 25h", disassembly, StringComparison.Ordinal);
        Assert.Contains("0x000AF6ED test byte [eax+11Ah],4", disassembly, StringComparison.Ordinal);
        Assert.Contains("0x000AF78C push 26h", disassembly, StringComparison.Ordinal);
        Assert.Contains("0x000AF7B2 push 25h", disassembly, StringComparison.Ordinal);
    }

    [Fact]
    public void Cross_version_damage_dataflow_corroborates_server_result_but_cannot_promote_formula_or_runtime()
    {
        var path = Path.Combine(
            FindRepositoryRoot(),
            "db", "imports", "supplemental", "xjz-c", "cross-version-client-recovery-leads.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var evidence = document.RootElement.GetProperty("combatDamageDataflow");
        var packet = evidence.GetProperty("packet");
        var exactBoundary = evidence.GetProperty("exactFormalClientBoundary");

        Assert.Equal("CrossVersionRecovered", evidence.GetProperty("evidenceClass").GetString());
        Assert.False(evidence.GetProperty("runtimeEligible").GetBoolean());
        Assert.Equal("0x83", packet.GetProperty("opcode").GetString());
        Assert.Equal(9, packet.GetProperty("signedResultOffset").GetInt32());
        Assert.Equal(2, packet.GetProperty("signedResultWidthBytes").GetInt32());
        Assert.Equal("SignedInt16LittleEndian", packet.GetProperty("interpretation").GetString());
        Assert.Equal(
            "next=min(max(current+signed_delta,0),maximum)",
            evidence.GetProperty("crossVersionClientConsumer").GetProperty("provenFormula").GetString());
        Assert.Equal(
            "ExactBuildClientResultProjectionVerified",
            exactBoundary.GetProperty("packetToVitalMutationConsumerStatus").GetString());
        var exactEvidence = exactBoundary.GetProperty("exactBuildEvidence");
        Assert.Equal(
            "god2-exact-build-op83-result-dataflow-v1",
            exactEvidence.GetProperty("machineReadableChainSchema").GetString());
        Assert.Equal(
            "PASS_EXACT_BUILD_CLIENT_RESULT_PROJECTION_SERVER_FORMULA_AND_SERIALIZER_BLOCKED",
            exactEvidence.GetProperty("status").GetString());
        Assert.Equal("actor+0xDE8", exactBoundary.GetProperty("friendlyVitalOffsets").GetProperty("currentHp").GetString());
        Assert.False(exactBoundary.GetProperty("runtimeMutationAllowed").GetBoolean());
        Assert.Equal(JsonValueKind.Null, evidence.GetProperty("serverSkillDamageFormulaClaim").ValueKind);
    }

    [Fact]
    public void Exact_build_damage_result_dataflow_closes_client_consumer_but_keeps_formula_and_serializer_blocked()
    {
        var root = FindRepositoryRoot();
        var path = Path.Combine(
            root, "Artifacts", "AnalysisScratch", "BattleStateFieldMap",
            "exact-build-op83-result-dataflow.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var evidence = document.RootElement;
        var conclusions = evidence.GetProperty("conclusions");

        Assert.Equal(
            "PASS_EXACT_BUILD_CLIENT_RESULT_PROJECTION_SERVER_FORMULA_AND_SERIALIZER_BLOCKED",
            evidence.GetProperty("status").GetString());
        Assert.Equal(
            "6b127086e0c00014de26137b4ec482801e06e0724c5c05c64561d7f9ff32bd9b",
            evidence.GetProperty("client").GetProperty("sha256").GetString());
        Assert.Equal(9, evidence.GetProperty("packet").GetProperty("signedResult").GetProperty("originalOffset").GetInt32());
        Assert.Equal("SignedInt16LittleEndian", evidence.GetProperty("packet").GetProperty("signedResult").GetProperty("encoding").GetString());
        Assert.Contains(
            evidence.GetProperty("edges").EnumerateArray(),
            edge => edge.GetProperty("rva").GetString()!.Contains("0x001566A0", StringComparison.Ordinal));
        Assert.True(conclusions.GetProperty("clientResultProjectionVerified").GetBoolean());
        Assert.Equal(
            "next=min(max(current+signed_delta,0),maximum)",
            conclusions.GetProperty("clientClampFormula").GetString());
        Assert.Equal(JsonValueKind.Null, conclusions.GetProperty("serverSkillDamageFormulaClaim").ValueKind);
        Assert.False(conclusions.GetProperty("serverSerializerEvidenceComplete").GetBoolean());
        Assert.False(conclusions.GetProperty("runtimeMutationAllowed").GetBoolean());
        Assert.False(conclusions.GetProperty("enemyMaximumHpProjectionAvailable").GetBoolean());

        foreach (var source in evidence.GetProperty("sources").EnumerateArray())
        {
            var sourcePath = Path.Combine(root, source.GetProperty("path").GetString()!.Replace('/', Path.DirectorySeparatorChar));
            Assert.True(File.Exists(sourcePath), sourcePath);
            Assert.Equal(
                source.GetProperty("sha256").GetString(),
                Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(sourcePath))).ToLowerInvariant());
        }
    }

    [Fact]
    public void Official_battle_pet_catalog_recovers_identity_and_keeps_gameplay_blocked()
    {
        var section = new GameDataSection("PET", 2, 10,
        [
            new CsvRow(11, "", ["5211", "0", "Pet01011", "30011", "枫华野狐", "opaque"]),
            new CsvRow(12, "", ["5212", "0", "Pet01012", "30012", "圣灵蚌", "opaque"])
        ]);

        var records = OfficialBattlePetCatalogExporter.Parse(
            section,
            new string('D', 64),
            new ZhTwLocalization(God2Glossary.Create()));

        Assert.Collection(records,
            row =>
            {
                Assert.Equal(5211, row.ClientCatalogId);
                Assert.Equal("Pet01011", row.ResourceKey);
                Assert.Equal("楓華野狐", row.Name);
                Assert.False(row.RuntimeEligible);
            },
            row =>
            {
                Assert.Equal(30012, row.ClientDisplayId);
                Assert.Equal("聖靈蚌", row.Name);
                Assert.Contains("skills", row.MissingRuntimeFields);
                Assert.Equal("EvidenceBlockedMissingStatsGrowthSkillsOwnershipAndWire", row.RuntimeEvidenceStatus);
            });
    }

    [Fact]
    public void Official_battle_pet_catalog_rejects_resource_or_identity_collisions()
    {
        var localization = new ZhTwLocalization(God2Glossary.Create());
        var malformed = new GameDataSection("PET", 1, 10,
        [
            new CsvRow(11, "", ["5211", "0", "Unknown", "30011", "楓華野狐"])
        ]);
        var duplicate = new GameDataSection("PET", 2, 10,
        [
            new CsvRow(11, "", ["5211", "0", "Pet01011", "30011", "楓華野狐"]),
            new CsvRow(12, "", ["5212", "0", "Pet01011", "30012", "聖靈蚌"])
        ]);

        Assert.Throws<InvalidDataException>(() => OfficialBattlePetCatalogExporter.Parse(malformed, new string('E', 64), localization));
        Assert.Throws<InvalidDataException>(() => OfficialBattlePetCatalogExporter.Parse(duplicate, new string('F', 64), localization));
    }

    [Fact]
    public void Official_npc_appearance_selector_decodes_resource_family_and_ordinal()
    {
        var section = new GameDataSection("NPCAppearData", 2, 10,
        [
            new CsvRow(11, string.Empty, ["1504", "Shopkeeper", "2143", "0"]),
            new CsvRow(12, string.Empty, ["3954", "Doctor", "82", "0"])
        ]);

        var rows = OfficialNpcCatalogMigrationWriter.ParseAppearances(
            section,
            new ZhTwLocalization(God2Glossary.Create()),
            new string('A', 64));

        Assert.Collection(rows,
            row =>
            {
                Assert.Equal(1504, row.Handle);
                Assert.Equal(2, row.ResourceType);
                Assert.Equal(142, row.ResourceOrdinal);
                Assert.True(row.SelectorValid);
            },
            row =>
            {
                Assert.Equal(3954, row.Handle);
                Assert.Equal(0, row.ResourceType);
                Assert.Equal(81, row.ResourceOrdinal);
                Assert.True(row.SelectorValid);
            });
    }

    [Fact]
    public void Official_map_definitions_fail_closed_when_the_authoritative_count_changes()
    {
        var section = new GameDataSection("Map_City_Coordniate", 1, 1, []);

        var exception = Assert.Throws<InvalidDataException>(() => OfficialClientExtractor.ParseOfficialMapDefinitions(section));

        Assert.Contains("144", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Official_indoor_map_accepts_blank_world_dimensions_when_navigation_supplies_them()
    {
        var rows = Enumerable.Range(0, 144)
            .Select(index => new CsvRow(
                22727 + index,
                string.Empty,
                ["洞府", $"Map{index}", $"地圖{index}", "7", "2", index.ToString(), "20", "9", "field.wav", "battle.wav", $"tong/indoor/map{index}.hmd", "", "", "1"]))
            .ToArray();

        var definitions = OfficialClientExtractor.ParseOfficialMapDefinitions(
            new GameDataSection("Map_City_Coordniate", 144, 22726, rows));

        Assert.All(definitions, definition =>
        {
            Assert.Null(definition.WorldWidth);
            Assert.Null(definition.WorldHeight);
        });
    }

    [Fact]
    public void Official_map_definitions_allow_distinct_map_ids_to_share_one_resource()
    {
        var rows = Enumerable.Range(0, 144)
            .Select(index => new CsvRow(
                22727 + index,
                string.Empty,
                ["陣法", $"Map{index}", $"地圖{index}", "12", "3", index.ToString(), "21", "42", "field.wav", "battle.wav", $"gem/gem{index}/gem{index}.mdt", "126", "168", "1"]))
            .ToArray();
        rows[1] = rows[1] with { Fields = ["陣法", "Gem01A", "天樞結界", "12", "3", "1", "21", "42", "field.wav", "battle.wav", "gem/gem01/gem01.mdt", "126", "168", "1"] };
        rows[23] = rows[23] with { Fields = ["陣法", "Gem01B", "貪狼結界", "12", "3", "23", "84", "105", "field.wav", "battle.wav", "gem/gem01/gem01.mdt", "126", "168", "1"] };

        var definitions = OfficialClientExtractor.ParseOfficialMapDefinitions(
            new GameDataSection("Map_City_Coordniate", 144, 22726, rows));

        Assert.Equal(2, definitions.Count(definition => definition.ResourceAuthorityKey == "client:map/gem/gem01/gem01"));
        Assert.Equal(144, definitions.Select(definition => (definition.ClientAreaId, definition.ClientMapId)).Distinct().Count());
    }

    [Fact]
    public async Task CsvZReaderEscapesInvalidCp936ByteWithoutReplacementCharacter()
    {
        var path = Path.Combine(Path.GetTempPath(), $"god2-content-{Guid.NewGuid():N}.csvZ");
        try
        {
            await File.WriteAllBytesAsync(path, PackLiterals([0x31, 0x2C, 0xAB, 0x0D, 0x0A]));
            var document = await God2PackedFile.ReadCsvZAsync(path, CancellationToken.None);

            Assert.Equal(1, document.InvalidByteCount);
            Assert.Equal("CP936-byte-escape", document.TextDecoder);
            Assert.Contains("[byte:AB]", document.Text, StringComparison.Ordinal);
            Assert.DoesNotContain('\ufffd', document.Text);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void CsvParserPreservesQuotedCommasAndEscapedQuotes()
    {
        var fields = God2PackedFile.SplitCsvLine("17,\"枫华镇,东门\",\"他说\"\"好\"\"\"");

        Assert.Equal(["17", "枫华镇,东门", "他说\"好\""], fields);
    }

    [Fact]
    public void LocalizationUsesOfficialTaiwanGlossaryBeforeOpenCc()
    {
        var result = CreateLocalization().Convert("枫华镇", ContentHash.Sha256("枫华镇"));

        Assert.Equal("楓華鎮", result.ConvertedText);
        Assert.Equal("OfficialZhTwExact", result.ConversionMethod);
        Assert.Equal("ConvertedToTraditional", result.ConversionStatus);
    }

    [Fact]
    public void LocalizationPreservesPlaceholdersMarkupLinksAndControls()
    {
        const string source = "获得 {0} 个物品 <color=#fff>[item:123]</color>\\r\\nmonster_fire_01 %s";
        var result = CreateLocalization().Convert(source, ContentHash.Sha256(source));

        Assert.Equal("獲得 {0} 個物品 <color=#fff>[item:123]</color>\\r\\nmonster_fire_01 %s", result.ConvertedText);
        Assert.True(result.PlaceholderPreserved);
        Assert.True(result.MarkupPreserved);
        Assert.True(result.ControlCodesPreserved);
        Assert.NotEqual("ConversionFailed", result.ConversionStatus);
    }

    [Fact]
    public void LocalizationIsIdempotentAndDoesNotConvertIdentifiers()
    {
        var localization = CreateLocalization();
        var first = localization.Convert("服务器 monster_fire_01", ContentHash.Sha256("source"));
        var second = localization.Convert(first.ConvertedText, first.ConvertedTextHash);

        Assert.Equal("伺服器 monster_fire_01", first.ConvertedText);
        Assert.Equal(first.ConvertedText, second.ConvertedText);
        Assert.False(localization.ContainsConvertibleSimplified(first.ConvertedText));
    }

    [Fact]
    public void LocalizationReachesStableTaiwanFixedPointForChainedPhraseRules()
    {
        var localization = CreateLocalization();
        var result = localization.Convert("好采头菜头", ContentHash.Sha256("好采头菜头"));
        var repeated = localization.Convert(result.ConvertedText, result.ConvertedTextHash);

        Assert.NotEqual("ConversionFailed", result.ConversionStatus);
        Assert.Equal(result.ConvertedText, repeated.ConvertedText);
        Assert.False(localization.ContainsConvertibleSimplified(result.ConvertedText));
    }

    [Fact]
    public void SourceSeedsRejectWrongGameAndPinCorrectIdentities()
    {
        var root = FindRepositoryRoot();
        using var bahamut = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "db", "imports", "supplemental", "bahamut", "phase1-sources.json")));
        using var site = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "db", "imports", "supplemental", "17173", "phase1-sources.json")));

        Assert.Equal(8395, bahamut.RootElement.GetProperty("gameIdentity").GetProperty("bahamutBoardId").GetInt32());
        Assert.Contains(6784, bahamut.RootElement.GetProperty("policy").GetProperty("wrongGameBoardIdsRejected").EnumerateArray().Select(value => value.GetInt32()));
        Assert.Equal("xjz.17173.com", site.RootElement.GetProperty("gameIdentity").GetProperty("host").GetString());
        Assert.All(site.RootElement.GetProperty("sources").EnumerateArray(), source => Assert.StartsWith("https://xjz.17173.com/", source.GetProperty("url").GetString(), StringComparison.Ordinal));
        Assert.Equal("StageOnlyUntilCurrentPatchPacketOrClientEvidenceMatches", bahamut.RootElement.GetProperty("policy").GetProperty("promotion").GetString());
        Assert.Equal("StageOnlyUntilCurrentPatchPacketOrClientEvidenceMatches", site.RootElement.GetProperty("policy").GetProperty("promotion").GetString());

        var bahamutParsers = bahamut.RootElement.GetProperty("sources").EnumerateArray()
            .Select(source => source.GetProperty("parser").GetString())
            .ToHashSet(StringComparer.Ordinal);
        Assert.Contains("MonsterProfileBlocks", bahamutParsers);
        Assert.Contains("MonsterSkillTable", bahamutParsers);
        Assert.Contains("MonsterSpeedLines", bahamutParsers);
    }

    [Fact]
    public void MigrationSeparatesRawStagingValidatedAndProductionWithoutSilentDefaults()
    {
        var sql = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "database", "schema", "031_gameplay_content_recovery_phase1.sql"));

        Assert.Contains("`content_raw_records`", sql, StringComparison.Ordinal);
        Assert.Contains("`content_staging_records`", sql, StringComparison.Ordinal);
        Assert.Contains("`content_validated_records`", sql, StringComparison.Ordinal);
        Assert.Contains("`content_production_manifest`", sql, StringComparison.Ordinal);
        Assert.Contains("`MpPolicy` varchar(32) NOT NULL DEFAULT 'Unknown'", sql, StringComparison.Ordinal);
        Assert.Contains("`DropPolicy` varchar(32) NOT NULL DEFAULT 'Unknown'", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("`MpCost` int NOT NULL DEFAULT 0", sql, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("WPN", "Weapon")]
    [InlineData("EQU", "Armor")]
    [InlineData("EGG", "Egg")]
    [InlineData("MAT01", "Material")]
    [InlineData("UNKNOWN", "Unknown")]
    public void Phase2ItemFamiliesUseOfficialSectionIdentity(string section, string expected)
    {
        Assert.Equal(expected, Phase2ClientExhaustionExtractor.ClassifyItemFamily(section));
    }

    [Fact]
    public void Phase2EquipmentStatsAreParsedOnlyFromExplicitLabels()
    {
        var values = Phase2ClientExhaustionExtractor.ParseStats("基本能力 物攻+24，魔攻+6，HP+120");

        Assert.Equal(24, values["物攻"]);
        Assert.Equal(6, values["魔攻"]);
        Assert.Equal(120, values["HP"]);
        Assert.False(values.ContainsKey("物防"));
    }

    [Fact]
    public void Phase2CoordinatesRequireExplicitMapNpcAndXYText()
    {
        Assert.True(Phase2ClientExhaustionExtractor.TryParseCoordinate("葛城  煉藥童子(164/87)", out var map, out var npc, out var x, out var y));
        Assert.Equal("葛城", map);
        Assert.Equal("煉藥童子", npc);
        Assert.Equal(164, x);
        Assert.Equal(87, y);
        Assert.False(Phase2ClientExhaustionExtractor.TryParseCoordinate("煉藥童子", out _, out _, out _, out _));
    }

    [Fact]
    public void Phase2EquipmentSetUsesExplicitOfficialColumnsOnly()
    {
        var fields = new[]
        {
            "3", "2", "《二周年絲帶羽衣套裝》", "1197", "二周年羽衣(女)", "1792", "二周年絲帶(女)",
            "", "", "", "", "", "", "物理攻擊+20 魔法攻擊+20"
        };

        Assert.True(Phase2ClientExhaustionExtractor.TryParseEquipmentSetRow(fields, out var parsed));
        Assert.Equal(3, parsed.SetId);
        Assert.Equal(2, parsed.RequiredPieces);
        Assert.Equal("物理攻擊+20 魔法攻擊+20", parsed.Effects);
        Assert.Equal([new Phase2EquipmentSetMember(1197, "Armor"), new Phase2EquipmentSetMember(1792, "Helmet")], parsed.Members);
        Assert.DoesNotContain(parsed.Members, member => member.ItemId is 2 or 3);
    }

    [Fact]
    public void Phase2PetInnateRejectsDescriptorAndUsesExactRecordLayout()
    {
        Assert.False(Phase2ClientExhaustionExtractor.TryParsePetInnateRow(
            ["0", "0", "法寶名稱", "法寶說明", "法寶效果", "法寶數值"], out _));

        Assert.True(Phase2ClientExhaustionExtractor.TryParsePetInnateRow(
            ["4", "吸收火屬性回血LV1", "開天珠", "吸收火屬性攻擊傷害回復Hp", "Tli19024", "50"], out var parsed));
        Assert.Equal(4, parsed.InnateId);
        Assert.Equal("吸收火屬性回血LV1", parsed.Name);
        Assert.Equal("開天珠", parsed.ArtifactName);
        Assert.Equal("吸收火屬性攻擊傷害回復Hp", parsed.Description);
        Assert.Equal("Tli19024", parsed.EffectReference);
        Assert.Equal(50, parsed.EffectValue);
    }

    [Theory]
    [InlineData("https://forum.gamer.com.tw/C.php?bsn=8395", "BahamutSupplementalEvidence")]
    [InlineData("https://xjz.17173.com/content/example.shtml", "17173SupplementalEvidence")]
    [InlineData("https://example.invalid/guide", "GuideSupplementalEvidence")]
    [InlineData("historical:Action/example.json", "HistoricalGameplayObservation")]
    [InlineData("Data2/Patch/Comm/gamedata.csvZ", "OfficialClientResource")]
    public void RecoverySourceClassificationPreservesTrustBoundary(string sourceFile, string expected)
    {
        Assert.Equal(expected, RecoverySourceClassification.FromFile(sourceFile));
    }

    [Fact]
    public void LongSourceIdentitiesUseHashSuffixInsteadOfSilentTruncation()
    {
        var common = new string('x', 520);
        var first = RecoveryDatabaseIdentity.ForStorage(common + "first");
        var second = RecoveryDatabaseIdentity.ForStorage(common + "second");

        Assert.Equal(512, first.Length);
        Assert.Equal(512, second.Length);
        Assert.NotEqual(first, second);
        Assert.Contains(":sha256:", first, StringComparison.Ordinal);
    }

    [Fact]
    public void Phase2MigrationDistinguishesDisabledUnknownProbabilityFromOfficialZero()
    {
        var sql = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "database", "schema", "032_gameplay_content_recovery_phase2.sql"));

        Assert.Contains("`OriginalDropChance` decimal(18,9) NULL", sql, StringComparison.Ordinal);
        Assert.Contains("`EffectiveDropChance` decimal(18,9) NOT NULL DEFAULT 0", sql, StringComparison.Ordinal);
        Assert.Contains("`ChanceEvidenceStatus` varchar(32) NOT NULL", sql, StringComparison.Ordinal);
        Assert.Contains("`IsDropEnabled` tinyint(1) NOT NULL DEFAULT 0", sql, StringComparison.Ordinal);
        Assert.Contains("`content_field_evidence`", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Phase2MigrationNeverMutatesHistoricalPhaseTablesOrUsesBlindDefaults()
    {
        var sql = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "database", "schema", "032_gameplay_content_recovery_phase2.sql"));

        Assert.DoesNotContain("OfficialWireClosedLoop", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("TRUNCATE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DELETE FROM", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("`MpCost` int NOT NULL DEFAULT 0", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Phase2IntegrityMigrationPreservesPerRunEvidenceAndCurrentSnapshotOwnership()
    {
        var sql = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "database", "schema", "033_gameplay_content_recovery_phase2_integrity.sql"));

        Assert.Contains("PRIMARY KEY (`RunId`, `LayoutId`)", sql, StringComparison.Ordinal);
        Assert.Contains("PRIMARY KEY (`RunId`, `EvidenceId`)", sql, StringComparison.Ordinal);
        Assert.Contains("`equipment_set_members`", sql, StringComparison.Ordinal);
        Assert.Contains("`ArtifactNameZhTw`", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("OfficialWireClosedLoop", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Phase3MigrationClosesFieldsWithoutMutatingPhase2Evidence()
    {
        var sql = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "database", "schema", "034_gameplay_content_recovery_phase3_semantic_promotion.sql"));

        Assert.Contains("`content_phase3_field_closure`", sql, StringComparison.Ordinal);
        Assert.Contains("`content_phase3_promotions`", sql, StringComparison.Ordinal);
        Assert.Contains("`monster_spawn_semantics`", sql, StringComparison.Ordinal);
        Assert.Contains("`monster_semantic_profiles`", sql, StringComparison.Ordinal);
        Assert.Contains("`skill_semantic_profiles`", sql, StringComparison.Ordinal);
        Assert.Contains("'ExplicitOfficialZero','DefaultDisabledZero','NotApplicable','Deprecated'", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("TRUNCATE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DELETE FROM", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("UPDATE `content_raw_records`", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("OfficialWireClosedLoop", sql, StringComparison.OrdinalIgnoreCase);
        Assert.True(RecoveryVersions.ExtractorPhase3.Length <= 32);
    }

    [Fact]
    public void M8ContentReleaseMigrationPinsOneCompletedPhase3ReleaseWithoutChangingEvidence()
    {
        var sql = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "database",
            "schema",
            "046_roadmap_m8_content_release_authority.sql"));

        Assert.Contains("`content_runtime_releases`", sql, StringComparison.Ordinal);
        Assert.Contains("`SourceRunId`", sql, StringComparison.Ordinal);
        Assert.Contains("`ReleaseStatus` = 'Active'", sql, StringComparison.Ordinal);
        Assert.Contains("'GameplayContentRecoveryPhase3'", sql, StringComparison.Ordinal);
        Assert.Contains("completed.`Status` = 'COMPLETED'", sql, StringComparison.Ordinal);
        Assert.Contains("UX_ContentRuntimeReleases_ActiveSlot", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("DELETE FROM", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("TRUNCATE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("UPDATE `content_raw_records`", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("UPDATE `content_staging_records`", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void M8FormalCatalogManifestMigrationPinsFormalRuntimeRowsWithoutMutatingContent()
    {
        var sql = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "database",
            "schema",
            "047_roadmap_m8_formal_catalog_release_manifest.sql"));

        Assert.Contains("`FormalCatalogManifestVersion`", sql, StringComparison.Ordinal);
        Assert.Contains("`FormalCatalogFingerprint`", sql, StringComparison.Ordinal);
        Assert.Contains("`FormalCatalogRecordCount`", sql, StringComparison.Ordinal);
        Assert.Contains("`FormalCatalogPinnedAtUtc`", sql, StringComparison.Ordinal);
        Assert.Contains("ADD INDEX IF NOT EXISTS", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("DELETE FROM", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("TRUNCATE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("UPDATE `content_", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("UPDATE `maps`", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("UPDATE `npcs`", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("UPDATE `monsters`", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void M8FormalCatalogManifestV2MigrationOnlyInvalidatesLegacyReleaseMetadata()
    {
        var sql = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "database",
            "schema",
            "048_roadmap_m8_formal_catalog_manifest_v2.sql"));

        Assert.Contains("UPDATE `content_runtime_releases`", sql, StringComparison.Ordinal);
        Assert.Contains("'god2-formal-runtime-catalog-manifest-v1'", sql, StringComparison.Ordinal);
        Assert.Contains("`FormalCatalogFingerprint`=NULL", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("DELETE FROM", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("TRUNCATE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("UPDATE `maps`", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("UPDATE `items`", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("UPDATE `status_effects`", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ClientMapIdentityMigrationSeparatesDatabaseAndOfficialClientIdentities()
    {
        var sql = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "database",
            "schema",
            "035_client_map_identity_authority.sql"));

        Assert.Contains("`MapId` int NOT NULL", sql, StringComparison.Ordinal);
        Assert.Contains("`ClientMapId` smallint unsigned NOT NULL", sql, StringComparison.Ordinal);
        Assert.Contains("`ClientAreaId` tinyint unsigned NOT NULL", sql, StringComparison.Ordinal);
        Assert.Contains("`ResourceIdentity`", sql, StringComparison.Ordinal);
        Assert.Contains("`CoordinateScaleX`", sql, StringComparison.Ordinal);
        Assert.Contains("`ClientBuildId`", sql, StringComparison.Ordinal);
        Assert.Contains("`IdentityEvidenceStatus` IN ('Verified','Derived')", sql, StringComparison.Ordinal);
        Assert.Contains("`CoordinateEvidenceStatus` IN ('Verified','Derived')", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("INSERT INTO `client_map_identities`", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DELETE FROM", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("TRUNCATE", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Map3IdentityEvidenceMigrationRequiresTheExactMariaDbAndOfficialClientResourceIdentity()
    {
        var sql = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "database",
            "schema",
            "036_map3_client_identity_evidence.sql"));

        Assert.Contains("map.`Id` = 1675308248", sql, StringComparison.Ordinal);
        Assert.Contains("'cityi2/cityi2.mdt'", sql, StringComparison.Ordinal);
        Assert.Contains("'client:map/island03/cityi2/cityi2'", sql, StringComparison.Ordinal);
        Assert.Contains("map.`Width` = 12", sql, StringComparison.Ordinal);
        Assert.Contains("map.`Height` = 12", sql, StringComparison.Ordinal);
        Assert.Contains("'OfficialClientFileIoAndStaticConsumer'", sql, StringComparison.Ordinal);
        Assert.Contains("'0x0008E14C'", sql, StringComparison.Ordinal);
        Assert.Contains("'0x00064B90'", sql, StringComparison.Ordinal);
        Assert.Contains("'resourceCellDivisor', 21", sql, StringComparison.Ordinal);
        Assert.Contains("`ProductionEnabled`", sql, StringComparison.Ordinal);
        Assert.Contains("NOT EXISTS", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("UPDATE `maps`", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DELETE FROM", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("TRUNCATE", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Map19IdentityCutoverRequiresExactResourceEvidenceAndPreservesLegacyCharacterIdentity()
    {
        var sql = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "database",
            "schema",
            "037_map19_identity_and_character_map_cutover.sql"));

        Assert.Contains("557790525", sql, StringComparison.Ordinal);
        Assert.Contains("'client:map/island01/indoor/groceryl'", sql, StringComparison.Ordinal);
        Assert.Contains("'indoor/groceryl.hmd'", sql, StringComparison.Ordinal);
        Assert.Contains("'42d769c0ff328db3e1e295e17208d0db7a0ff12608976f952dae800ff79c3c00'", sql, StringComparison.Ordinal);
        Assert.Contains("'95C178A9BABC34FF116CD9022B7FF33716EAFF783DF14A46419ADB2A7D9A4230'", sql, StringComparison.Ordinal);
        Assert.Contains("'hmdPassGrid', 'Pass 10 30'", sql, StringComparison.Ordinal);
        Assert.Contains("`character_map_identity_migrations`", sql, StringComparison.Ordinal);
        Assert.Contains("`LegacyClientMapId`", sql, StringComparison.Ordinal);
        Assert.Contains("characterRow.`MapId` = migration.`LegacyClientMapId`", sql, StringComparison.Ordinal);
        Assert.Contains("characterRow.`PositionX` BETWEEN 0 AND ((map.`Width` * 21) - 1)", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("DELETE FROM", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("TRUNCATE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("UPDATE `maps`", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Map19IdentityCutoverCorrectionUsesTheActualPhase1ResourceKeyField()
    {
        var sql = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "database",
            "schema",
            "038_map19_identity_cutover_resource_key_fix.sql"));

        Assert.Contains("JSON_EXTRACT(evidence.`NormalizedData`, '$.resourceKey')", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("JSON_EXTRACT(evidence.`NormalizedData`, '$.resourceName')", sql, StringComparison.Ordinal);
        Assert.Contains("evidence.`RunId` = '552b1a69-7a75-422a-b071-fa9e5e6c3bb4'", sql, StringComparison.Ordinal);
        Assert.Contains("evidence.`NormalizedHash` = '5c2722a664300f1561c81b02196f76f8b59206e8bef6a2f52293d50eae167ec4'", sql, StringComparison.Ordinal);
        Assert.Contains("`character_map_identity_migrations`", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("DELETE FROM", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("TRUNCATE", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void M2NpcPromotionSchemaSeparatesOfficialIdentityFromRuntimePlacement()
    {
        var sql = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "database",
            "schema",
            "039_roadmap_m2_npc_identity_and_spawn_promotion.sql"));

        Assert.Contains("`npc_client_identities`", sql, StringComparison.Ordinal);
        Assert.Contains("`npc_spawns`", sql, StringComparison.Ordinal);
        Assert.Contains("`NpcId` int NOT NULL", sql, StringComparison.Ordinal);
        Assert.Contains("`ObservedClientEntityHandle` int unsigned NULL", sql, StringComparison.Ordinal);
        Assert.Contains("`CoordinateEvidenceStatus`", sql, StringComparison.Ordinal);
        Assert.Contains("`ProductionEnabled`", sql, StringComparison.Ordinal);
        Assert.Contains("`SourceHash` char(64)", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("DELETE FROM", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("TRUNCATE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("UPDATE `npcs`", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void M2NpcPromotionUsesExactDualStageHashesAndPinnedOfficialClientEvidence()
    {
        var sql = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "database",
            "schema",
            "040_roadmap_m2_npc_promotion_hash_stage_fix.sql"));

        Assert.Contains("'0583d7fef8b8b7a47930179adfcb0f94c2f748c72d9b182da4c667e199f24b33'", sql, StringComparison.Ordinal);
        Assert.Contains("'6a850b0e67888376ef9aab34f9a6b91b02f77d6781be9efcf6b3666c4f3c7924'", sql, StringComparison.Ordinal);
        Assert.Contains("'6B127086E0C00014DE26137B4EC482801E06E0724C5C05C64561D7F9FF32BD9B'", sql, StringComparison.Ordinal);
        Assert.Contains("'8c190363dee7ce69ebc6dde92aca71101832040f40ec86336ae0a90fe4082127'", sql, StringComparison.Ordinal);
        Assert.Contains("'data2/rom/npc/npc2643.ROM'", sql, StringComparison.Ordinal);
        Assert.Contains("'83D11BE70AB2E0D6660BC38915E8C3C101FFFC2C67D7A950453E24D79669A84B'", sql, StringComparison.Ordinal);
        Assert.Contains("'7E3AC167DEF745DE3BE7D38C22B4C0ADC1419E6DC20851F8FA3CD22D61CCA644'", sql, StringComparison.Ordinal);
        Assert.Contains("SELECT 316049902 AS `SpawnId`, 1504 AS `ObservedEntityHandle`, 17 AS `PositionX`, 8 AS `PositionY`", sql, StringComparison.Ordinal);
        Assert.Contains("SELECT 2124220824, 4638, 14, 14, 1835066", sql, StringComparison.Ordinal);
        Assert.Contains("map.`Id`=557790525", sql, StringComparison.Ordinal);
        Assert.Contains("'Verified', 'Verified', 'Derived', 1", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("DELETE FROM", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("TRUNCATE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("UPDATE `content_", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Phase3PromotionRequiresExactIdentityAndKeepsUnknownProbabilityDisabled()
    {
        var source = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "tools", "God2.GameplayContentRecovery", "Phase3SemanticRecovery.cs"));

        Assert.Contains("COUNT(DISTINCT n.`Id`)=1", source, StringComparison.Ordinal);
        Assert.Contains("COUNT(DISTINCT m.`Id`)=1", source, StringComparison.Ordinal);
        Assert.Contains("ExactClientId+DisplayName+RuntimeShape", source, StringComparison.Ordinal);
        Assert.Contains("unique_profile", source, StringComparison.Ordinal);
        Assert.Contains("s.`RecordIdentity`=p.`ProfileId` AND s.`SourceHash`=o.`SourceHash`", source, StringComparison.Ordinal);
        Assert.Contains("`DeclaredDropChance` IS NOT NULL", source, StringComparison.Ordinal);
        Assert.Contains("`ProductionDropEnabled`=0 AND `DeclaredDropChance` IS NULL AND `EffectiveDropChance`<>0", source, StringComparison.Ordinal);
        Assert.Contains("`EffectiveProbability`>=0 AND `Quantity`>0", source, StringComparison.Ordinal);
        Assert.Contains("m.`MaxHp`>0 AND m.`EvidenceStatus` IN ('Verified','Derived') THEN m.`MaxHp` ELSE NULL", source, StringComparison.Ordinal);
        Assert.Contains("`EffectReference` IS NULL AND `EffectValue` IS NULL", source, StringComparison.Ordinal);
        Assert.DoesNotContain("LIKE CONCAT('%'", source, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Phase3MissingFieldMatrixAccountsForEveryAllowedState()
    {
        var source = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "tools", "God2.GameplayContentRecovery", "Phase3SemanticRecovery.cs"));

        Assert.Contains("SUM(`EvidenceStatus`='Verified')", source, StringComparison.Ordinal);
        Assert.Contains("SUM(`EvidenceStatus`='Derived')", source, StringComparison.Ordinal);
        Assert.Contains("SUM(`EvidenceStatus`='Candidate')", source, StringComparison.Ordinal);
        Assert.Contains("SUM(`EvidenceStatus`='EvidenceBlocked')", source, StringComparison.Ordinal);
        Assert.Contains("SUM(`EvidenceStatus`='ExplicitOfficialZero')", source, StringComparison.Ordinal);
        Assert.Contains("SUM(`EvidenceStatus`='DefaultDisabledZero')", source, StringComparison.Ordinal);
        Assert.Contains("SUM(`EvidenceStatus`='NotApplicable')", source, StringComparison.Ordinal);
        Assert.Contains("SUM(`EvidenceStatus`='Deprecated')", source, StringComparison.Ordinal);
        Assert.Contains("`Unclassified`", source, StringComparison.Ordinal);
    }

    [Fact]
    public void MonsterCaptureCommitTreatsEffectPositionAsSourceAndNeverPromotesDamageSumToHp()
    {
        var root = FindRepositoryRoot();
        var single = File.ReadAllText(Path.Combine(
            root, "Automation", "Commit-ControlledSingleMonsterCapture.ps1"));
        var multi = File.ReadAllText(Path.Combine(
            root, "Automation", "Commit-MultiMonsterCaptureTargets.ps1"));

        Assert.Contains("$bytes[2] -eq $playerPosition", single, StringComparison.Ordinal);
        Assert.Contains("HpRequiresDirectStateSnapshot", single, StringComparison.Ordinal);
        Assert.DoesNotContain("max_hp=$maximumHp", single, StringComparison.Ordinal);
        Assert.Contains("NotPromotedBecauseFinalHitMayOverkill", single, StringComparison.Ordinal);
        Assert.DoesNotContain("god2_recovered", single, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("VERIFIED_LEVEL_CAPTURE_INCOMPLETE", multi, StringComparison.Ordinal);
        Assert.Contains("sourcePosition = [int]$bytes[2]", multi, StringComparison.Ordinal);
        Assert.Contains("maximum_hp=NULL", multi, StringComparison.Ordinal);
        Assert.DoesNotContain("max_hp=COALESCE($hpSql,max_hp)", multi, StringComparison.Ordinal);
        Assert.Contains("rawPacketDataIncluded = $false", multi, StringComparison.Ordinal);
    }

    [Fact]
    public void CurrentMonsterImportUsesFieldLevelHpWithdrawalWithoutRewritingHistoricalEvidence()
    {
        var root = FindRepositoryRoot();
        using var current = JsonDocument.Parse(File.ReadAllText(Path.Combine(
            root, "db", "imports", "live", "monsters", "monsters.verified.latest.json")));
        using var withdrawals = JsonDocument.Parse(File.ReadAllText(Path.Combine(
            root, "Artifacts", "RecoveryFinal", "monster-hp-claim-withdrawals-20260813.json")));
        using var historicalXianhu = JsonDocument.Parse(File.ReadAllText(Path.Combine(
            root, "Artifacts", "RecoveryFinal", "verified-monster-xianhu-20260811.json")));
        using var historicalBrownSnail = JsonDocument.Parse(File.ReadAllText(Path.Combine(
            root, "Artifacts", "RecoveryFinal", "verified-monsters-monsters-live-20260811-085805-a55b725c.json")));

        Assert.Equal("god2-verified-encounter-monster-latest-v3", current.RootElement.GetProperty("schemaVersion").GetString());
        var xianhu = Assert.Single(current.RootElement.GetProperty("records").EnumerateArray());
        Assert.Equal(30, xianhu.GetProperty("serverMonsterId").GetInt32());
        Assert.Equal(JsonValueKind.Null, xianhu.GetProperty("maximumHp").ValueKind);
        Assert.Equal("WithdrawnSourcePositionMisread", xianhu.GetProperty("maximumHpEvidenceStatus").GetString());
        Assert.Equal("VERIFIED_LEVEL_AND_EXPERIENCE_ONLY", xianhu.GetProperty("authority").GetString());
        Assert.False(xianhu.GetProperty("runtimeEligible").GetBoolean());
        Assert.Equal(6, xianhu.GetProperty("level").GetInt32());
        Assert.Equal(73, xianhu.GetProperty("experienceReward").GetInt32());

        var claims = withdrawals.RootElement.GetProperty("claims").EnumerateArray().ToArray();
        Assert.Equal(2, claims.Length);
        Assert.Contains(claims, claim =>
            claim.GetProperty("id").GetString() == "xianhu-126" &&
            claim.GetProperty("withdrawnValue").GetInt32() == 126 &&
            claim.GetProperty("currentValue").ValueKind == JsonValueKind.Null);
        Assert.Contains(claims, claim =>
            claim.GetProperty("id").GetString() == "brown-snail-34" &&
            claim.GetProperty("withdrawnValue").GetInt32() == 34 &&
            claim.GetProperty("currentValue").ValueKind == JsonValueKind.Null);

        // The old observations remain byte-for-byte historical inputs. The active withdrawal
        // index, not destructive rewriting, prevents their invalid HP claims from being reused.
        Assert.Equal(126, historicalXianhu.RootElement.GetProperty("observation").GetProperty("maximumHp").GetInt32());
        Assert.Equal(34, historicalBrownSnail.RootElement.GetProperty("monsters")[0].GetProperty("maximumHp").GetInt32());
    }

    [Fact]
    public void MonsterCaptureCommitAndCoverageRequireDirectActorVitalHpEvidence()
    {
        var root = FindRepositoryRoot();
        var commit = File.ReadAllText(Path.Combine(root, "Automation", "Commit-MonsterCapture.ps1"));
        var completeness = File.ReadAllText(Path.Combine(root, "Automation", "Test-MonsterCaptureCompleteness.ps1"));

        Assert.Contains("Remove-UnprovenMaximumHp", commit, StringComparison.Ordinal);
        Assert.Contains("Test-DirectMaximumHpEvidence", commit, StringComparison.Ordinal);
        Assert.Contains("DirectActorObjectSnapshotVerified", commit, StringComparison.Ordinal);
        Assert.Contains("UniqueTemplateBattleEntrySettlementAndDirectActorVitalBinding", commit,
            StringComparison.Ordinal);
        Assert.Contains("maximumHpEvidenceReference", commit, StringComparison.Ordinal);
        Assert.Contains("monster-hp-claim-withdrawals-20260813.json", commit, StringComparison.Ordinal);
        Assert.Contains("god2-verified-encounter-monster-latest-v3", commit, StringComparison.Ordinal);
        Assert.Contains("OneLatestFieldClassifiedRowPerServerMonsterId", commit, StringComparison.Ordinal);

        Assert.Contains("Test-CompleteMonsterObservation", completeness, StringComparison.Ordinal);
        Assert.Contains("DirectActorObjectSnapshotVerified", completeness, StringComparison.Ordinal);
        Assert.Contains("Test-MonsterActorVitalsCapture.ps1", completeness, StringComparison.Ordinal);
        Assert.Contains("maximumHpEvidenceReference", completeness, StringComparison.Ordinal);
        Assert.Contains("$completeGlobalRows", completeness, StringComparison.Ordinal);
        Assert.Contains("-RequireRuntimeEligible", completeness, StringComparison.Ordinal);
        Assert.Contains("fieldClassifiedIncompleteRowCount", completeness, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "$coveredIds = @($globalRows | ForEach-Object",
            completeness,
            StringComparison.Ordinal);
    }

    [Fact]
    public void MonsterCaptureStopPreservesActorAndStateBytesBeforeReadinessAndCompletenessGates()
    {
        var root = FindRepositoryRoot();
        var stop = File.ReadAllText(Path.Combine(
            root, "Automation", "Stop-ContinuousEnhancedCapture.ps1"));
        var actor = File.ReadAllText(Path.Combine(
            root, "Automation", "Export-God2BattleActorSnapshots.ps1"));
        var state = File.ReadAllText(Path.Combine(
            root, "Automation", "Export-God2BattleStateSnapshots.ps1"));
        var readiness = File.ReadAllText(Path.Combine(
            root, "Automation", "Test-MonsterDirectStateCapture.ps1"));
        var actorVitals = File.ReadAllText(Path.Combine(
            root, "Automation", "Test-MonsterActorVitalsCapture.ps1"));

        var actorExport = stop.IndexOf("Export-God2BattleActorSnapshots.ps1", StringComparison.Ordinal);
        var stateExport = stop.IndexOf("Export-God2BattleStateSnapshots.ps1", StringComparison.Ordinal);
        var reanalysis = stop.IndexOf("--internal-reanalyze", StringComparison.Ordinal);
        var readinessGate = stop.IndexOf("Test-MonsterDirectStateCapture.ps1", StringComparison.Ordinal);
        var completenessGate = stop.IndexOf("Test-MonsterCaptureCompleteness.ps1", StringComparison.Ordinal);
        Assert.True(actorExport >= 0 && stateExport > actorExport && reanalysis > stateExport);
        Assert.True(readinessGate > reanalysis && completenessGate > readinessGate);
        Assert.Contains("monsterDirectStateCapture", stop, StringComparison.Ordinal);
        Assert.Contains("monsterActorVitalsCapture", stop, StringComparison.Ordinal);

        Assert.Contains("god2-battle-actor-snapshot-manifest-v3", actor, StringComparison.Ordinal);
        Assert.Contains("stateIndexAt0x1A", actor, StringComparison.Ordinal);
        Assert.DoesNotContain("candidateLifecycleAt0x1A", actor, StringComparison.Ordinal);
        Assert.Contains("god2-battle-state-snapshot-manifest-v2", state, StringComparison.Ordinal);
        Assert.Contains("crossEncounterTransitionsExcluded = $true", state, StringComparison.Ordinal);
        Assert.Contains("fromTemporarySnapshotFile", state, StringComparison.Ordinal);
        Assert.Contains("toTemporarySnapshotFile", state, StringComparison.Ordinal);
        Assert.Contains("toPhase = $phaseName", state, StringComparison.Ordinal);
        Assert.Contains("actionAppliedInvoked=(\\d+)", state, StringComparison.Ordinal);
        Assert.Contains("actionAppliedAccepted=(\\d+)", state, StringComparison.Ordinal);
        Assert.Contains("actionAppliedInvocationCount", state, StringComparison.Ordinal);
        Assert.Contains("actionAppliedAcceptedSnapshotCount", state, StringComparison.Ordinal);
        Assert.Contains("VerifiedRenderSpriteStateNotHpAuthority", state, StringComparison.Ordinal);
        Assert.Contains("maximumHpFieldPresent = $false", state, StringComparison.Ordinal);
        Assert.Contains("RVA 0x00006D40", state, StringComparison.Ordinal);

        Assert.Contains("god2-monster-direct-state-readiness-v1", readiness, StringComparison.Ordinal);
        Assert.Contains("Test-SnapshotArtifact", readiness, StringComparison.Ordinal);
        Assert.Contains("ActionApplied", readiness, StringComparison.Ordinal);
        Assert.Contains("$actionAppliedInvocationCount -le 0", readiness, StringComparison.Ordinal);
        Assert.Contains("$actionAppliedAcceptedSnapshotCount -le 0", readiness, StringComparison.Ordinal);
        Assert.Contains("accepted counters do not reconcile", readiness, StringComparison.Ordinal);
        Assert.Contains("stateIndexAt0x1A", readiness, StringComparison.Ordinal);
        Assert.Contains("maximumHpPromotionReady = $false", readiness, StringComparison.Ordinal);
        Assert.Contains("EvidenceBlockedRenderStateNotHpAuthority", readiness, StringComparison.Ordinal);
        Assert.Contains("stateRecordSupportsMaximumHp = $false", readiness, StringComparison.Ordinal);
        Assert.Contains("RVA 0x00006980", readiness, StringComparison.Ordinal);
        Assert.Contains("RVA 0x00006D40", readiness, StringComparison.Ordinal);
        Assert.DoesNotContain("DirectStateSnapshotVerified", readiness, StringComparison.Ordinal);

        Assert.Contains("god2-monster-actor-vitals-readiness-v1", actorVitals,
            StringComparison.Ordinal);
        Assert.Contains("EvidenceBlockedEnemyVitalProjectionUnavailable", actorVitals,
            StringComparison.Ordinal);
        Assert.Contains("ExactBuildDoesNotProjectEnemyMaximumHpMp", actorVitals,
            StringComparison.Ordinal);
        Assert.Contains("0xCDCDCDCD sentinels", actorVitals, StringComparison.Ordinal);
        Assert.Contains("maximumHpPromotionReady = $false", actorVitals,
            StringComparison.Ordinal);
        Assert.DoesNotContain("ACTOR_HP_MP_READY", actorVitals, StringComparison.Ordinal);
        Assert.DoesNotContain("DirectActorObjectSnapshotVerified", actorVitals,
            StringComparison.Ordinal);
        Assert.Contains("descriptorBindingValid", actorVitals, StringComparison.Ordinal);
        Assert.Contains("maximumHitPointsAt0xDF0", actorVitals, StringComparison.Ordinal);
        Assert.Contains("maximumMagicPointsAt0xDF4", actorVitals, StringComparison.Ordinal);
        Assert.Contains("Exactly one battle-entry 0x82 frame", actorVitals, StringComparison.Ordinal);
        Assert.Contains("Exactly one settlement 0x89 frame", actorVitals, StringComparison.Ordinal);
    }

    [Fact]
    public void BattleStateSnapshotDomainIsEnabledAndNativeGateFixtureIsInSemanticSelfTest()
    {
        var root = FindRepositoryRoot();
        var probe = File.ReadAllText(Path.Combine(
            root, "tools", "God2.ClientInstrumentation", "src", "God2ClientTraceProbe.cpp"));

        Assert.Contains("InterlockedExchange(&enabled[19]", probe, StringComparison.Ordinal);
        Assert.Contains("captureReady && battleStateSnapshotProbeInstalled != 0", probe, StringComparison.Ordinal);
        Assert.Contains("executeIsolatedProbeOperationWithLedger(", probe, StringComparison.Ordinal);
        Assert.Contains("19u, &incrementBattleStateDomainFixture", probe, StringComparison.Ordinal);
        Assert.Contains("check(runBattleStateDomainGateSelfTest());", probe, StringComparison.Ordinal);
        Assert.Contains("g_battleStateActionAppliedInvocations", probe, StringComparison.Ordinal);
        Assert.Contains("g_battleStateActionAppliedAccepted", probe, StringComparison.Ordinal);
    }

    [Fact]
    public void MonsterCompletenessRequiresActorVitalsPromotionAndGlobalCoverageBeforeCommitOrPurge()
    {
        var root = FindRepositoryRoot();
        var completeness = File.ReadAllText(Path.Combine(
            root, "Automation", "Test-MonsterCaptureCompleteness.ps1"));
        var commit = File.ReadAllText(Path.Combine(
            root, "Automation", "Commit-MonsterCapture.ps1"));
        var stop = File.ReadAllText(Path.Combine(
            root, "Automation", "Stop-ContinuousEnhancedCapture.ps1"));

        Assert.Contains("Test-MonsterDirectStateCapture.ps1", completeness, StringComparison.Ordinal);
        Assert.Contains("Test-MonsterActorVitalsCapture.ps1", completeness, StringComparison.Ordinal);
        Assert.Contains("maximumHpPromotionReady", completeness, StringComparison.Ordinal);
        Assert.Contains("$missingIds.Count -eq 0", completeness, StringComparison.Ordinal);
        Assert.Contains("$gate.actorVitals.maximumHpPromotionReady", commit, StringComparison.Ordinal);
        Assert.Contains("$gate.globalCoverage.complete", commit, StringComparison.Ordinal);

        Assert.Contains("$actorVitalsPromotionReady", stop, StringComparison.Ordinal);
        Assert.Contains("$commitReceiptValid", stop, StringComparison.Ordinal);
        Assert.Contains("$requiredRawEvidence", stop, StringComparison.Ordinal);
        Assert.Contains("rawFileCount", stop, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Add-Member -NotePropertyName rawPayloadRetained -NotePropertyValue $true",
            stop,
            StringComparison.Ordinal);
    }

    [Fact]
    public void MonsterRawPurgeRequiresClosedLoopBeforeDeletionAndMultiMonsterCannotPurge()
    {
        var root = FindRepositoryRoot();
        var purge = File.ReadAllText(Path.Combine(
            root, "Automation", "Purge-DetachedCaptureRaw.ps1"));
        var multi = File.ReadAllText(Path.Combine(
            root, "Automation", "Commit-MultiMonsterCaptureTargets.ps1"));

        var policyGate = purge.IndexOf("monster-capture-policy.json", StringComparison.Ordinal);
        var readinessGate = purge.IndexOf("Test-MonsterActorVitalsCapture.ps1", StringComparison.Ordinal);
        var completenessGate = purge.IndexOf("Test-MonsterCaptureCompleteness.ps1", StringComparison.Ordinal);
        var promotionGate = purge.IndexOf("maximumHpPromotionReady", StringComparison.Ordinal);
        var commitGate = purge.IndexOf("monster-import-commit.json", StringComparison.Ordinal);
        var firstRawDelete = purge.IndexOf("Remove-Item -LiteralPath $_.FullName", StringComparison.Ordinal);

        Assert.True(policyGate >= 0 && readinessGate > policyGate);
        Assert.True(completenessGate > readinessGate && promotionGate > completenessGate);
        Assert.True(commitGate > promotionGate && firstRawDelete > commitGate);
        Assert.Contains("god2-battle-state-snapshot-manifest-v2", purge, StringComparison.Ordinal);
        Assert.Contains("raw data was preserved", purge, StringComparison.Ordinal);

        Assert.Contains("PurgeRawAfterCommit is disabled", multi, StringComparison.Ordinal);
        Assert.DoesNotContain("$rawRoot", multi, StringComparison.Ordinal);
        Assert.DoesNotContain("Remove-Item -LiteralPath $_.FullName", multi, StringComparison.Ordinal);
    }

    [Fact]
    public void CaptureStartAndStopCommandsUseModeSpecificSharedStateContracts()
    {
        var root = FindRepositoryRoot();
        var enhancedStart = File.ReadAllText(Path.Combine(
            root, "Automation", "Start-ContinuousEnhancedCapture.ps1"));
        var enhancedStop = File.ReadAllText(Path.Combine(
            root, "Automation", "Stop-ContinuousEnhancedCapture.ps1"));
        var packetStart = File.ReadAllText(Path.Combine(
            root, "Automation", "Start-PacketCaptureOnly.ps1"));
        var packetStop = File.ReadAllText(Path.Combine(
            root, "Automation", "Stop-PacketCaptureOnly.ps1"));

        Assert.Contains("god2-continuous-enhanced-capture-state-v1", enhancedStart, StringComparison.Ordinal);
        Assert.Contains("captureMode = \"ContinuousEnhanced\"", enhancedStart, StringComparison.Ordinal);
        Assert.Contains("god2-continuous-enhanced-capture-state-v1", enhancedStop, StringComparison.Ordinal);
        Assert.Contains("Use its matching stop command", enhancedStop, StringComparison.Ordinal);

        Assert.Contains("god2-packet-only-capture-state-v1", packetStart, StringComparison.Ordinal);
        Assert.Contains("captureMode = \"PacketCaptureOnly\"", packetStart, StringComparison.Ordinal);
        Assert.Contains("god2-packet-only-capture-state-v1", packetStop, StringComparison.Ordinal);
        Assert.Contains("Use its matching stop command", packetStop, StringComparison.Ordinal);
        Assert.Contains("completed enhanced session", packetStart, StringComparison.Ordinal);
    }

    [Fact]
    public void UserOperatedOfficialCapturePinsExactElevatedLauncherClientAndRemoteEndpoint()
    {
        var root = FindRepositoryRoot();
        var enhancedStart = File.ReadAllText(Path.Combine(root, "Automation", "Start-ContinuousEnhancedCapture.ps1"));
        var officialStart = File.ReadAllText(Path.Combine(root, "Automation", "Start-UserOperatedOfficialCapture.ps1"));
        var profile = File.ReadAllText(Path.Combine(root, "Artifacts", "ClientInstrumentation",
            "LauncherAutomation", "original-server-user-operated-profile.json"));

        Assert.Contains("[string] $ExpectedClientPath", enhancedStart, StringComparison.Ordinal);
        Assert.Contains("Expected exactly one matching God2_opt.exe process", enhancedStart, StringComparison.Ordinal);
        Assert.Contains("ExpectedClientSha256", enhancedStart, StringComparison.Ordinal);
        Assert.DoesNotContain("Select-Object -First 1", enhancedStart, StringComparison.Ordinal);
        Assert.Contains("-Verb RunAs", officialStart, StringComparison.Ordinal);
        Assert.Contains("$rid -lt 12288", officialStart, StringComparison.Ordinal);
        Assert.Contains("allowedRemoteServerAddresses", officialStart, StringComparison.Ordinal);
        Assert.Contains("ManualUserOperationOnly", officialStart, StringComparison.Ordinal);
        Assert.Contains("directDatabaseFixture = $false", officialStart, StringComparison.Ordinal);
        Assert.Contains("1.15.31.226", profile, StringComparison.Ordinal);
        Assert.Contains("49.235.177.12", profile, StringComparison.Ordinal);
        Assert.Contains("automatedCredentialEntryAllowed\": false", profile, StringComparison.Ordinal);
    }

    [Fact]
    public void SingleMonsterCaptureHasAReviewOnlyGoalAndCannotAutoCommitOrPurge()
    {
        var root = FindRepositoryRoot();
        var start = File.ReadAllText(Path.Combine(
            root, "Automation", "Start-ContinuousEnhancedCapture.ps1"));
        var stop = File.ReadAllText(Path.Combine(
            root, "Automation", "Stop-ContinuousEnhancedCapture.ps1"));
        var purge = File.ReadAllText(Path.Combine(
            root, "Automation", "Purge-DetachedCaptureRaw.ps1"));

        Assert.Contains("[switch] $SingleMonsterCapture", start, StringComparison.Ordinal);
        Assert.Contains("captureGoal = $captureGoal", start, StringComparison.Ordinal);
        Assert.Contains("\"single-monster\"", start, StringComparison.Ordinal);
        Assert.Contains("SingleMonsterRawMustRemainUntilExplicitReviewedPromotion", start,
            StringComparison.Ordinal);
        Assert.Contains("single-monster-review-required", stop, StringComparison.Ordinal);
        Assert.Contains("analyzed-single-monster-review-pending", stop, StringComparison.Ordinal);
        Assert.Contains("if ([string]$state.captureGoal -ceq \"all-monsters\")", stop,
            StringComparison.Ordinal);
        Assert.Contains("captureGoal -cne \"all-monsters\"", purge, StringComparison.Ordinal);
    }

    [Fact]
    public void LimitedBattleCampaignCapturesFormationVitalsAndSolvesPoisonRecurrenceFailClosed()
    {
        var root = FindRepositoryRoot();
        var start = File.ReadAllText(Path.Combine(
            root, "Automation", "Start-ContinuousEnhancedCapture.ps1"));
        var stop = File.ReadAllText(Path.Combine(
            root, "Automation", "Stop-ContinuousEnhancedCapture.ps1"));
        var gate = File.ReadAllText(Path.Combine(
            root, "Automation", "Test-LimitedOfficialBattleCapture.ps1"));
        var plan = File.ReadAllText(Path.Combine(
            root, "Automation", "BattleUiVitalProbePlan.json"));
        var probe = File.ReadAllText(Path.Combine(
            root, "tools", "God2.ClientInstrumentation", "src",
            "God2ClientTraceProbe.cpp"));
        var analyzer = File.ReadAllText(Path.Combine(
            root, "tools", "God2.ClientInstrumentation", "Analyzer",
            "Program.cs"));

        Assert.Contains("battle_ui_bar_scalar", plan, StringComparison.Ordinal);
        Assert.Contains("0x0004F080", plan, StringComparison.Ordinal);
        Assert.Contains("battle_ui_text_pair", plan, StringComparison.Ordinal);
        Assert.Contains("0x0004F4B0", plan, StringComparison.Ordinal);
        Assert.Contains("battle_ui_vital_apply", plan, StringComparison.Ordinal);
        Assert.Contains("0x000D98F0", plan, StringComparison.Ordinal);
        Assert.Contains("battle-ui-vital-probe.json", start, StringComparison.Ordinal);
        Assert.Contains("Unable to resolve the injected probe module", start,
            StringComparison.Ordinal);
        Assert.Contains("PoisonIsFirstAndOnlyDamage", start,
            StringComparison.Ordinal);

        Assert.Contains("ApiBattleUiVitalObservation = 10", probe,
            StringComparison.Ordinal);
        Assert.Contains("ApiMonsterIntelligenceConsumerSnapshot = 11", probe,
            StringComparison.Ordinal);
        Assert.Contains("monster_attribute_", probe, StringComparison.Ordinal);
        Assert.Contains("monster_skill_", probe, StringComparison.Ordinal);
        Assert.Contains("kActorSnapshotBytes = 0xE00u", probe,
            StringComparison.Ordinal);
        Assert.Contains("g_battleActorSnapshotIdentitiesValid[position]", probe,
            StringComparison.Ordinal);
        Assert.Contains("pointerValuesPersisted=0 actorIdentityRequired=1", probe,
            StringComparison.Ordinal);
        Assert.Contains("ApiKind.MonsterIntelligenceConsumerSnapshot", analyzer,
            StringComparison.Ordinal);
        Assert.Contains("kIntegerPairFormatRva = 0x003ED9B8u", probe,
            StringComparison.Ordinal);
        Assert.Contains("pointerValuesPersisted=0", probe, StringComparison.Ordinal);
        Assert.Contains("validBattleUiVitalPair", probe, StringComparison.Ordinal);
        Assert.Contains("kind = mpValid ? 3u : 4u", probe, StringComparison.Ordinal);
        Assert.Contains("Invalid MP values are zeroed", probe, StringComparison.Ordinal);
        Assert.Contains("UniqueThreeTickPoisonTenPercentIntegerSolution", gate,
            StringComparison.Ordinal);
        Assert.Contains("ExactUiCurrentMaximumPairAnd0x83TargetDeltaAgreement", gate,
            StringComparison.Ordinal);
        Assert.Contains("monsterMaximumHpEvidenceReady", gate, StringComparison.Ordinal);
        Assert.Contains("DerivedBaselineHypothesis_NotOfficialFormulaEvidence", gate,
            StringComparison.Ordinal);
        Assert.Contains("officialPhysicalAttackEvidenceReady = $false", gate,
            StringComparison.Ordinal);
        Assert.Contains("OneConditionFitsBothAdditiveAndMultiplicativeModels", gate,
            StringComparison.Ordinal);
        Assert.Contains("CrossConditionModelDiscriminator_NotOfficialFormulaEvidence", gate,
            StringComparison.Ordinal);
        Assert.Contains("officialSkillBaseDamageEvidenceReady = $false", gate,
            StringComparison.Ordinal);
        Assert.Contains("NonCriticalPlusExactDoubleCriticalCandidate", gate,
            StringComparison.Ordinal);
        Assert.Contains("UserConfirmedGameplayRule_CorroboratedByControlledSwordOneSamples", gate,
            StringComparison.Ordinal);
        Assert.Contains("god2-limited-official-battle-campaign-readiness-v4", gate,
            StringComparison.Ordinal);
        Assert.Contains("monsterIdentityComplete", gate, StringComparison.Ordinal);
        Assert.Contains("MonsterNamePositionDescriptorEpochBindingMissing", gate,
            StringComparison.Ordinal);
        Assert.Contains("ALL_MONSTER_EVIDENCE_LOCKED_TO_NAME_POSITION_DESCRIPTOR_EPOCH", gate,
            StringComparison.Ordinal);
        Assert.Contains("targetMonsterEvidenceScope", gate, StringComparison.Ordinal);
        Assert.Contains("descriptorSequence -eq $currentDescriptorSequence", gate,
            StringComparison.Ordinal);
        Assert.Contains("VitalApplyHpOnlyTuple", gate, StringComparison.Ordinal);
        Assert.Contains("ExactUiSameObjectMpPairBoundByTargetedHpTransition", gate,
            StringComparison.Ordinal);
        Assert.Contains("monsterIntelligenceCards", gate, StringComparison.Ordinal);
        Assert.Contains("UnavailableNotGuessed", gate, StringComparison.Ordinal);
        Assert.Contains("monsterIdentityPolicy", start, StringComparison.Ordinal);
        Assert.Contains("天眼護符 22031/22086/22131/22231/22260", start,
            StringComparison.Ordinal);
        Assert.Contains("physicalAttackInferencePolicy", start, StringComparison.Ordinal);
        Assert.Contains("skillDamageInferencePolicy", start, StringComparison.Ordinal);
        Assert.Contains("criticalityRule", start, StringComparison.Ordinal);
        Assert.Contains("battleMonsterMaximumHpEvidenceReady", stop,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ReadOnlyBattleActorSnapshotUsesVerifiedPointerChainAndTwentyFourByteSlots()
    {
        var root = FindRepositoryRoot();
        var script = File.ReadAllText(Path.Combine(
            root, "Automation", "Export-God2ReadOnlyBattleActors.ps1"));

        Assert.Contains("$moduleBase + 0x004AD5F8", script, StringComparison.Ordinal);
        Assert.Contains("$rootObject + 0x008885F0", script, StringComparison.Ordinal);
        Assert.Contains("$gameObject + 0x00149DF8", script, StringComparison.Ordinal);
        Assert.Contains("$slot * 24", script, StringComparison.Ordinal);
        Assert.Contains("PROCESS_QUERY_LIMITED_INFORMATION|PROCESS_VM_READ", script, StringComparison.Ordinal);
        Assert.DoesNotContain("PROCESS_VM_WRITE", script, StringComparison.Ordinal);
        Assert.DoesNotContain("WriteProcessMemory", script, StringComparison.Ordinal);
    }

    [Fact]
    public void InProcessBattleActorSnapshotRunsAfterVerifiedActorConstructionAndIsKeptOutOfNetworkFrames()
    {
        var root = FindRepositoryRoot();
        var probe = File.ReadAllText(Path.Combine(
            root, "tools", "God2.ClientInstrumentation", "src", "God2ClientTraceProbe.cpp"));
        var analyzer = File.ReadAllText(Path.Combine(
            root, "tools", "God2.ClientInstrumentation", "Analyzer", "Program.cs"));
        var exporter = File.ReadAllText(Path.Combine(
            root, "Automation", "Export-God2BattleActorSnapshots.ps1"));

        Assert.Contains("kCallSiteRva = 0x00150818u", probe, StringComparison.Ordinal);
        Assert.Contains("kBuildFunctionRva = 0x0014BDF0u", probe, StringComparison.Ordinal);
        Assert.Contains("g_originalBattleActorBuild(dispatcher, recordPointer)", probe, StringComparison.Ordinal);
        Assert.Contains("ApiBattleActorSnapshot", probe, StringComparison.Ordinal);
        Assert.Contains("uninstallBattleActorSnapshotProbe()", probe, StringComparison.Ordinal);
        Assert.Contains("battleActorSnapshotProbeInstalled != 0", probe, StringComparison.Ordinal);
        Assert.Contains("or ApiKind.BattleActorSnapshot", analyzer, StringComparison.Ordinal);
        Assert.Contains("StrictUnloadVerified", exporter, StringComparison.Ordinal);
        Assert.Contains("rawPacketDataIncluded = $false", exporter, StringComparison.Ordinal);
        Assert.Contains("currentHitPointsAt0xDE8", exporter, StringComparison.Ordinal);
        Assert.Contains("maximumHitPointsAt0xDF0", exporter, StringComparison.Ordinal);
        Assert.Contains("descriptorBindingValid", exporter, StringComparison.Ordinal);
        Assert.Contains("descriptorSha256", exporter, StringComparison.Ordinal);
        Assert.Contains("actor+0xDBE", exporter, StringComparison.Ordinal);
        Assert.Contains("monsterNameOriginal", exporter, StringComparison.Ordinal);
        Assert.Contains("monsterIdentityBindingValid", exporter, StringComparison.Ordinal);
        Assert.Contains("descriptorOrdinaryStaticMonster", exporter, StringComparison.Ordinal);
        Assert.Contains("[Text.Encoding]::GetEncoding", exporter, StringComparison.Ordinal);
        Assert.Contains("Friendly actors and enemy PK-player branches are never decoded", exporter,
            StringComparison.Ordinal);
        Assert.Contains("FriendlyHitPointsMagicPointsConsumerVerified", exporter,
            StringComparison.Ordinal);
        Assert.Contains("EnemyHitPointsMagicPointsNotProjectedByExactBuild", exporter,
            StringComparison.Ordinal);
        Assert.Contains("ExactBuildDoesNotProjectEnemyMaximumHpMp", exporter,
            StringComparison.Ordinal);
        Assert.Contains("RVA 0x000D98F0-0x000D9A67", exporter, StringComparison.Ordinal);
        Assert.DoesNotContain("WriteProcessMemory", exporter, StringComparison.Ordinal);
    }

    [Fact]
    public void PreservedOfficialEnemyActorSnapshotBindsXianhuNameAndStableSpeciesSignature()
    {
        var root = FindRepositoryRoot();
        var tracePath = Path.Combine(root, "Artifacts", "BattleFormulaCapture",
            "actor-mutation-gated-20260811-172703", "sensitive", "trace.bin");
        using var stream = File.OpenRead(tracePath);
        using var reader = new BinaryReader(stream);
        Assert.Equal("G2TRC01\0", Encoding.ASCII.GetString(reader.ReadBytes(8)));
        _ = reader.ReadBytes(16);

        byte[]? firstDescriptor = null;
        byte[]? movedDescriptor = null;
        byte[]? actorSnapshot = null;
        while (stream.Position < stream.Length)
        {
            var header = reader.ReadBytes(188);
            Assert.Equal(188, header.Length);
            Assert.Equal(0x31523247u, BinaryPrimitives.ReadUInt32LittleEndian(header));
            var sequence = BinaryPrimitives.ReadUInt64LittleEndian(header.AsSpan(8));
            var api = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(40));
            var battlePosition = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(48));
            var length = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(64));
            Assert.InRange(length, 0u, 4096u);
            var payload = reader.ReadBytes(checked((int)length));
            Assert.Equal((int)length, payload.Length);

            if (api == 7 && sequence == 1500) firstDescriptor = payload;
            if (api == 7 && sequence == 2624) movedDescriptor = payload;
            if (api == 8 && sequence == 1521 && battlePosition == 22)
                actorSnapshot = payload;
        }

        Assert.NotNull(firstDescriptor);
        Assert.NotNull(movedDescriptor);
        Assert.NotNull(actorSnapshot);
        Assert.Equal(45, firstDescriptor!.Length);
        Assert.Equal(0x1C, firstDescriptor[0]);
        Assert.Equal(22, firstDescriptor[1]);
        Assert.Equal(6, firstDescriptor[2]);
        Assert.Equal(8192, BinaryPrimitives.ReadUInt16LittleEndian(firstDescriptor.AsSpan(3)));
        Assert.Equal(4, BinaryPrimitives.ReadUInt16LittleEndian(firstDescriptor.AsSpan(7)));

        Assert.Equal(0xE00, actorSnapshot!.Length);
        Assert.Equal(8192, BinaryPrimitives.ReadUInt16LittleEndian(actorSnapshot.AsSpan(0xDB0)));
        Assert.Equal(6, actorSnapshot[0xDB7]);
        Assert.Equal(4, BinaryPrimitives.ReadUInt16LittleEndian(actorSnapshot.AsSpan(0xDBC)));
        Assert.Equal(new byte[] { 0xCF, 0xC9, 0xBA, 0xFC, 0x00 },
            actorSnapshot.AsSpan(0xDBE, 5).ToArray());

        static string SpeciesSignature(byte[] descriptor)
        {
            var normalized = descriptor.ToArray();
            normalized[1] = 0;
            normalized[3] = 0;
            normalized[4] = 0;
            return Convert.ToHexString(SHA256.HashData(normalized));
        }

        Assert.Equal("3D1D834BCD798037D4647FC4E1A94E5AEF98CB11222594BD76BEEE3C921DB786",
            SpeciesSignature(firstDescriptor));
        Assert.Equal(SpeciesSignature(firstDescriptor), SpeciesSignature(movedDescriptor!));
        Assert.NotEqual(firstDescriptor[1], movedDescriptor![1]);
        Assert.NotEqual(
            BinaryPrimitives.ReadUInt16LittleEndian(firstDescriptor.AsSpan(3)),
            BinaryPrimitives.ReadUInt16LittleEndian(movedDescriptor.AsSpan(3)));
    }

    [Fact]
    public void FormalMonsterCatalogExposesReadableViewsButNotPacketEvidenceStorage()
    {
        var root = FindRepositoryRoot();
        var views = File.ReadAllText(Path.Combine(
            root, "database", "schema", "063_readable_monster_catalog_views.sql"));
        var migration = File.ReadAllText(Path.Combine(
            root, "database", "schema", "065_move_monster_packet_evidence_out_of_formal_catalog.sql"));

        Assert.Contains("vw_monsters_readable", views, StringComparison.Ordinal);
        Assert.Contains("vw_monster_skills_readable", views, StringComparison.Ordinal);
        Assert.DoesNotContain("AuthorityKey", views, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("PayloadHex", views, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("DROP TABLE `god2_game`.`verified_monster_observations`", migration, StringComparison.Ordinal);
        Assert.Contains("`god2`.`verified_monster_observations`", migration, StringComparison.Ordinal);
    }

    [Fact]
    public void FormalReleasePublisherRetainsTrxCountersAndPublishesTheirSummary()
    {
        var root = FindRepositoryRoot();
        var publisher = File.ReadAllText(Path.Combine(root, "Automation", "Publish-God2ServerRelease.ps1"));

        Assert.Contains("--results-directory $testResultsDirectory", publisher, StringComparison.Ordinal);
        Assert.Contains("trx;LogFilePrefix=", publisher, StringComparison.Ordinal);
        Assert.Contains("SelectSingleNode(\"//*[local-name()='Counters']\")", publisher, StringComparison.Ordinal);
        Assert.Contains("Release tests completed without a retained TRX result", publisher, StringComparison.Ordinal);
        Assert.Contains("testSummary = $testSummary", publisher, StringComparison.Ordinal);
        Assert.Contains("sha256 = (Get-FileHash", publisher, StringComparison.Ordinal);
    }

    [Fact]
    public void ExactBuildBattleEffectAndSnapshotEvidenceRoundTripsEveryRecordAndKeepsSerializerBlocked()
    {
        var root = FindRepositoryRoot();
        var evidencePath = Path.Combine(
            root,
            "Artifacts",
            "BattleFormulaCapture",
            "actor-mutation-gated-20260811-172703",
            "analysis",
            "battle-evidence.json");
        using var document = JsonDocument.Parse(File.ReadAllText(evidencePath));
        var evidence = document.RootElement;

        Assert.Equal("god2-roadmap-m5-live-battle-capture-v6", evidence.GetProperty("SchemaVersion").GetString());
        var effects = evidence.GetProperty("EffectCandidates").EnumerateArray().ToArray();
        Assert.Equal(93, effects.Length);
        Assert.All(effects, effect => Assert.True(effect.GetProperty("LayoutRoundTripVerified").GetBoolean()));
        Assert.Equal(9, evidence.GetProperty("EffectKindSummaries").GetArrayLength());
        var staticProfiles = evidence.GetProperty("EffectKindStaticProfiles").EnumerateArray().ToArray();
        Assert.Equal(23, staticProfiles.Length);
        Assert.Equal(
            [7, 9],
            staticProfiles
                .Where(profile => profile.GetProperty("DirectSignedResultSlotWriteInProjector").GetBoolean())
                .Select(profile => profile.GetProperty("EffectKind").GetByte())
                .ToArray());
        Assert.All(staticProfiles, profile => Assert.Contains(
            "TypedHpMpMutatorNotEffectKindDirect",
            profile.GetProperty("TypedMutatorRoutingStatus").GetString(),
            StringComparison.Ordinal));
        var actorStateRouting = evidence.GetProperty("ActorStateRouting");
        Assert.Equal(12, actorStateRouting.GetProperty("RecordSelectorOffset").GetInt32());
        Assert.Equal("AuxiliaryValue0HighByte", actorStateRouting.GetProperty("RecordSelectorField").GetString());
        Assert.Equal("actor+0x232", actorStateRouting.GetProperty("ActorStateOffset").GetString());
        Assert.Equal(
            ["0x00", "0x1C"],
            actorStateRouting.GetProperty("ObservedSelectorIndices").EnumerateArray()
                .Select(value => value.GetString()!).ToArray());
        var typedRoutes = actorStateRouting.GetProperty("TypedRoutes").EnumerateArray().ToArray();
        Assert.Equal(["0x22", "0x23", "0x24"],
            typedRoutes.Select(route => route.GetProperty("SelectorIndex").GetString()!).ToArray());
        Assert.Equal(["0x77", "0x78", "0x79"],
            typedRoutes.Select(route => route.GetProperty("ActorStateValue").GetString()!).ToArray());
        Assert.All(typedRoutes, route => Assert.False(route.GetProperty("ObservedInCapture").GetBoolean()));
        Assert.Equal(
            "ExactBuildStaticRouteVerified_ServerEmissionNotObserved",
            actorStateRouting.GetProperty("EvidenceStatus").GetString());

        var snapshots = evidence.GetProperty("SnapshotCandidates").EnumerateArray().ToArray();
        Assert.Equal(25, snapshots.Length);
        Assert.All(snapshots, snapshot =>
        {
            Assert.True(snapshot.GetProperty("LayoutRoundTripVerified").GetBoolean());
            Assert.Equal(0x194B0020u, snapshot.GetProperty("OpaqueTrailer").GetUInt32());
            Assert.Equal(0, snapshot.GetProperty("ConsumedTrailerByte118").GetByte());
        });

        var ordering = evidence.GetProperty("EffectSnapshotOrdering");
        Assert.Equal(25, ordering.GetProperty("SnapshotCount").GetInt32());
        Assert.Equal(20, ordering.GetProperty("SnapshotsSharingFrameWithEffects").GetInt32());
        Assert.Equal(71, ordering.GetProperty("EffectsSharingFrameWithSnapshot").GetInt32());
        Assert.Equal(71, ordering.GetProperty("EffectsBeforeSameFrameSnapshot").GetInt32());
        Assert.True(ordering.GetProperty("AllObservedSharedFrameEffectsPrecedeSnapshot").GetBoolean());
        Assert.False(ordering.GetProperty("CompleteSerializerOrderingProven").GetBoolean());
        Assert.Contains(
            "complete serializer remain evidence-blocked",
            evidence.GetProperty("RemainingBlockers")[0].GetString(),
            StringComparison.Ordinal);

        var secondEvidencePath = Path.Combine(
            root,
            "Artifacts",
            "BattleFormulaCapture",
            "basic-formula-20260811-1622",
            "analysis",
            "battle-evidence.v6.json");
        using var secondDocument = JsonDocument.Parse(File.ReadAllText(secondEvidencePath));
        var secondSnapshots = secondDocument.RootElement
            .GetProperty("SnapshotCandidates")
            .EnumerateArray()
            .ToArray();
        Assert.Equal(6, secondSnapshots.Length);
        Assert.All(secondSnapshots, snapshot =>
        {
            Assert.True(snapshot.GetProperty("LayoutRoundTripVerified").GetBoolean());
            Assert.Equal(0x194B0020u, snapshot.GetProperty("OpaqueTrailer").GetUInt32());
            Assert.Equal(0, snapshot.GetProperty("ConsumedTrailerByte118").GetByte());
        });

        var displacementEvidencePath = Path.Combine(
            root,
            "Artifacts",
            "AnalysisScratch",
            "BattleStateFieldMap",
            "exact-build-actor-state-232-references.json");
        using var displacementDocument = JsonDocument.Parse(File.ReadAllText(displacementEvidencePath));
        var displacementEvidence = displacementDocument.RootElement;
        Assert.Equal("exact-build-runtime-memory-displacement-evidence-v1",
            displacementEvidence.GetProperty("SchemaVersion").GetString());
        Assert.Equal(22, displacementEvidence.GetProperty("ReferenceCount").GetInt32());
        Assert.Equal(20, displacementEvidence.GetProperty("LinearDecodeAlignedCount").GetInt32());
        var alignedWriters = displacementEvidence.GetProperty("References").EnumerateArray()
            .Where(reference => reference.GetProperty("LinearDecodeAligned").GetBoolean() &&
                reference.GetProperty("Access").GetString() == "Write")
            .Select(reference => reference.GetProperty("InstructionRva").GetString())
            .ToArray();
        Assert.Contains("0x0016331E", alignedWriters);
        Assert.Contains("0x00163356", alignedWriters);
        Assert.Contains("0x00163462", alignedWriters);

        var trailerEvidencePath = Path.Combine(
            root,
            "Artifacts",
            "AnalysisScratch",
            "BattleStateFieldMap",
            "exact-build-op88-trailer-consumer.json");
        using var trailerDocument = JsonDocument.Parse(File.ReadAllText(trailerEvidencePath));
        var trailerEvidence = trailerDocument.RootElement;
        Assert.Equal(31, trailerEvidence.GetProperty("observedSnapshotCount").GetInt32());
        Assert.True(trailerEvidence.GetProperty("allLayoutRoundTripsVerified").GetBoolean());
        Assert.False(trailerEvidence.GetProperty("runtimeSerializerAllowed").GetBoolean());
        Assert.Equal("20004B19", trailerEvidence.GetProperty("observedTrailerHex").GetString());
        Assert.All(trailerEvidence.GetProperty("captures").EnumerateArray(), capture =>
        {
            var referencedPath = Path.Combine(
                root,
                capture.GetProperty("evidence").GetString()!.Replace('/', Path.DirectorySeparatorChar));
            Assert.Equal(
                capture.GetProperty("evidenceSha256").GetString(),
                Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(referencedPath))));
        });
    }

    private static ZhTwLocalization CreateLocalization() => new(God2Glossary.Create());

    private static byte[] PackLiterals(byte[] plain)
    {
        var payload = new List<byte>();
        for (var index = 0; index < plain.Length; index += 8)
        {
            var count = Math.Min(8, plain.Length - index);
            payload.Add((byte)((1 << count) - 1));
            payload.AddRange(plain.AsSpan(index, count).ToArray());
        }

        var packed = new byte[7 + payload.Count];
        packed[0] = 0x05;
        packed[1] = 0x16;
        BinaryPrimitives.WriteInt32LittleEndian(packed.AsSpan(2, 4), plain.Length);
        packed[6] = 0;
        payload.CopyTo(packed, 7);
        return packed;
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "God2ClassicServer.sln")))
        {
            current = current.Parent;
        }

        return current?.FullName ?? throw new DirectoryNotFoundException("God2 repository root was not found.");
    }
}

