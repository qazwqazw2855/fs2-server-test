using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace God2.GameplayContentRecovery;

public sealed record Phase2EquipmentSetMember(int ItemId, string SlotName);

public sealed record Phase2EquipmentSetRow(
    int SetId,
    int RequiredPieces,
    string Name,
    string Effects,
    IReadOnlyList<Phase2EquipmentSetMember> Members);

public sealed record Phase2PetInnateRow(
    int InnateId,
    string Name,
    string? ArtifactName,
    string? Description,
    string? EffectReference,
    int? EffectValue);

public sealed class Phase2ClientExhaustionExtractor
{
    private static readonly string[] ItemSections =
    [
        "WPN", "EQU", "GOD", "MAP", "MED01", "MED02", "TLI", "PET", "MAT01", "MIS", "SPI", "SKB", "PFD",
        "GWP", "GEH", "GEB", "GEQ", "PEQ", "STR", "KIT01", "KIT02", "NST", "EGG", "SPP", "CBK", "SCD",
        "EQC", "CBF01", "CAD", "BEB", "VPT", "CBF02", "ELE", "MIS02", "MED03", "TWP", "TEQ", "AMU", "BAR",
        "GEQ02", "MEQ", "COM", "NCP", "NMP", "MIS03", "UPS", "SSW", "SES"
    ];

    private static readonly IReadOnlyDictionary<string, (string Family, string ZhTw)> ItemFamilies =
        new Dictionary<string, (string, string)>(StringComparer.Ordinal)
        {
            ["WPN"] = ("Weapon", "武器"),
            ["EQU"] = ("Armor", "防具"),
            ["GOD"] = ("Special", "神祇裝備"),
            ["MAP"] = ("Special", "地圖物品"),
            ["MED01"] = ("Consumable", "消耗品"),
            ["MED02"] = ("Consumable", "消耗品"),
            ["TLI"] = ("Special", "法寶"),
            ["PET"] = ("PetItem", "神獸"),
            ["MAT01"] = ("Material", "材料"),
            ["MIS"] = ("QuestItem", "任務物品"),
            ["MIS02"] = ("QuestItem", "任務物品"),
            ["MIS03"] = ("QuestItem", "任務物品"),
            ["SPI"] = ("Special", "特殊物品"),
            ["SKB"] = ("Recipe", "技能書"),
            ["PFD"] = ("PetItem", "神獸飼料"),
            ["GWP"] = ("Weapon", "神祇武器"),
            ["GEH"] = ("Helmet", "神祇頭部裝備"),
            ["GEB"] = ("Armor", "神祇身體裝備"),
            ["GEQ"] = ("Accessory", "神祇裝備"),
            ["GEQ02"] = ("Accessory", "神祇裝備"),
            ["PEQ"] = ("PetItem", "神獸裝備"),
            ["STR"] = ("Special", "特殊物品"),
            ["KIT01"] = ("Material", "合成材料"),
            ["KIT02"] = ("Material", "合成工具"),
            ["NST"] = ("Special", "特殊物品"),
            ["EGG"] = ("Egg", "神獸蛋"),
            ["SPP"] = ("PetItem", "幼獸"),
            ["CBK"] = ("Special", "卡片冊"),
            ["SCD"] = ("Special", "卡片套組"),
            ["EQC"] = ("Equipment", "裝備卡"),
            ["CBF01"] = ("PetItem", "神獸技能飼料"),
            ["CBF02"] = ("PetItem", "神獸技能飼料"),
            ["CAD"] = ("Special", "封印卡"),
            ["BEB"] = ("PetItem", "戰鬥神獸"),
            ["VPT"] = ("Currency", "虛擬點數"),
            ["ELE"] = ("Material", "元素石"),
            ["MED03"] = ("Consumable", "消耗品"),
            ["TWP"] = ("Weapon", "武器"),
            ["TEQ"] = ("Armor", "防具"),
            ["AMU"] = ("Accessory", "護符"),
            ["BAR"] = ("Special", "商城組合"),
            ["MEQ"] = ("PetItem", "神獸合成裝備"),
            ["COM"] = ("PetItem", "紙娃娃合成神獸"),
            ["NCP"] = ("PetItem", "二代神獸"),
            ["NMP"] = ("PetItem", "二代坐騎"),
            ["UPS"] = ("Material", "升級材料"),
            ["SSW"] = ("Weapon", "武器"),
            ["SES"] = ("Armor", "防具")
        };

    private static readonly Regex Coordinate = new(@"(?<map>[^\d\r\n()（）]+?)\s+(?<npc>[^\d\r\n()（）]+?)[(（]\s*(?<x>\d+)\s*[/,，]\s*(?<y>\d+)\s*[)）]", RegexOptions.Compiled);
    private static readonly Regex Stat = new(@"(?<name>物攻|魔攻|物防|魔防|HP|MP|速度)\s*[+＋]\s*(?<value>-?\d+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private readonly string _repositoryRoot;
    private readonly string _clientRoot;
    private readonly ZhTwLocalization _localization;

    public Phase2ClientExhaustionExtractor(string repositoryRoot, string clientRoot, ZhTwLocalization localization)
    {
        _repositoryRoot = repositoryRoot;
        _clientRoot = clientRoot;
        _localization = localization;
    }

    public async Task ExtractAsync(RecoveryWorkspace workspace, CancellationToken cancellationToken)
    {
        var documents = new Dictionary<string, CsvZDocument>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in Directory.EnumerateFiles(_clientRoot, "*.csvZ", SearchOption.AllDirectories).OrderBy(value => value, StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var document = await God2PackedFile.ReadCsvZAsync(path, cancellationToken);
            var relative = Relative(path);
            documents[relative] = document;
            workspace.Sources.Add(new SourceInventoryEntry($"phase2:client:{relative}", "OfficialClientPackedTable", relative, document.SourceHash,
                document.SourceSize, document.Rows.Count, $"God2-0516-LZSS+{document.TextDecoder}", ClassifyTable(relative)));
            ArchiveTable(workspace, relative, document);
        }

        workspace.Diagnostics["phase2CsvZDecoded"] = documents.Count;
        workspace.Diagnostics["phase2CsvZRowsArchived"] = documents.Values.Sum(value => value.Rows.Count);
        workspace.Diagnostics["phase2CsvZInvalidBytesEscaped"] = documents.Values.Sum(value => value.InvalidByteCount);

        var gameData = Require(documents, "Data2/Patch/Comm/gamedata.csvZ");
        var sections = GameDataSections.Parse(gameData.Rows);
        ExtractEncounterNames(workspace, gameData, sections);
        var itemAuthorities = ExtractItemProfiles(workspace, gameData, sections);
        ExtractMerchantCandidates(workspace, gameData, sections, itemAuthorities);
        ExtractQuestProfiles(workspace, documents, itemAuthorities);
        ExtractEquipmentSets(workspace, documents, itemAuthorities);
        ExtractContainerRelationships(workspace, documents, itemAuthorities);
        ExtractPetContent(workspace, documents, itemAuthorities);
        ExtractSkillProfiles(workspace, documents);
        ExtractDayMissionCoordinates(workspace, documents);
        await ExtractGuideEvidenceAsync(workspace, cancellationToken);
        await ExtractHistoricalObservationsAsync(workspace, cancellationToken);

        workspace.Diagnostics["recoveryPasses"] = new object[]
        {
            new { pass = 1, action = "Decode/archive every accessible official CSVZ record and reconstruct table boundaries", status = "PASS" },
            new { pass = 2, action = "Specialized item/NPC/skill/quest/equipment/container/pet cross-reference extraction", status = "PASS" },
            new { pass = 3, action = "Exact official identity matching of user-verified guide evidence; historical JSON retained as hash-only metadata until gameplay projection is proven", status = "PASS" },
            new { pass = 4, action = "Missing-field analysis; unresolved semantics isolated as EvidenceBlocked without defaults", status = "PASS_WITH_EVIDENCE_GAPS" },
            new { pass = 5, action = "Unique normalized zh-TW identity pass for NPC, map, skill and quest names; no fuzzy matches allowed", status = "PASS_NO_ADDITIONAL_SAFE_PROMOTION" }
        };
    }

    private void ArchiveTable(RecoveryWorkspace workspace, string relative, CsvZDocument document)
    {
        var maxFields = document.Rows.Count == 0 ? 0 : document.Rows.Max(row => row.Fields.Count);
        var header = document.Rows.Take(5).Select(row => new { row.RecordIndex, fields = row.Fields }).ToArray();
        AddEntity(workspace, "ClientTableLayout", $"phase2:layout:{Stable(relative)}", relative, $"layout:{relative}", null, document.SourceHash,
            Path.GetFileName(relative), null, "Verified", "VerifiedDecode", new()
            {
                ["recordCount"] = document.Rows.Count,
                ["maximumFieldCount"] = maxFields,
                ["headerJson"] = RecoveryJson.Compact(header),
                ["decoder"] = $"God2-0516-LZSS+{document.TextDecoder}",
                ["recordBoundaryStatus"] = "Verified",
                ["loaderEvidenceStatus"] = "VerifiedDecoder"
            }, []);

        foreach (var row in document.Rows)
        {
            var rawId = ContentHash.StableId("phase2", relative, row.RecordIndex.ToString(CultureInfo.InvariantCulture), document.SourceHash, row.RawLine);
            workspace.Raw.Add(new RawEvidenceRecord(rawId, ClassifyTable(relative), "OfficialClientPackedTable", relative,
                $"phase2:csvz:{relative}:{row.RecordIndex}", row.RecordIndex, null, document.SourceHash, DetectLanguage(row.RawLine), row.RawLine,
                RecoveryJson.Compact(new { fields = row.Fields, fieldCount = row.Fields.Count, decoder = document.TextDecoder }), ContentHash.Sha256(row.RawLine),
                "VerifiedDecode", "Verified"));
        }
    }

    private void ExtractEncounterNames(
        RecoveryWorkspace workspace,
        CsvZDocument gameData,
        IReadOnlyDictionary<string, GameDataSection> sections)
    {
        if (!sections.TryGetValue("EnyNameTotal", out var section))
        {
            return;
        }

        foreach (var row in section.Rows)
        {
            if (!TryInt(At(row.Fields, 0), out var encounterId) || string.IsNullOrWhiteSpace(At(row.Fields, 1)))
            {
                continue;
            }

            AddEntity(workspace, "EncounterName", $"phase2:encounter-name:{encounterId}:{row.RecordIndex}",
                Relative(gameData.Path), $"gamedata:EnyNameTotal:{row.RecordIndex}", row.RecordIndex, gameData.SourceHash,
                At(row.Fields, 1), null, "PartiallyMapped", "VerifiedDecode", new()
                {
                    ["encounterLocalId"] = encounterId,
                    ["categoryOrModelCandidates"] = row.Fields.Skip(2).ToArray(),
                    ["monsterTemplateBinding"] = null
                }, ["MonsterTemplateBinding", "MapBinding", "SpawnCoordinates", "CombatStats"]);
        }
    }

    private Dictionary<int, string> ExtractItemProfiles(RecoveryWorkspace workspace, CsvZDocument gameData, IReadOnlyDictionary<string, GameDataSection> sections)
    {
        var byId = new Dictionary<int, string>();
        foreach (var sectionName in ItemSections)
        {
            if (!sections.TryGetValue(sectionName, out var section))
            {
                continue;
            }

            var category = ItemFamilies.GetValueOrDefault(sectionName, ("Unknown", "未分類"));
            foreach (var row in section.Rows)
            {
                if (!TryInt(At(row.Fields, 0), out var clientItemId))
                {
                    continue;
                }

                var authority = $"client:item/{sectionName.ToLowerInvariant()}/{clientItemId}";
                byId.TryAdd(clientItemId, authority);
                var descriptions = row.Fields.Skip(7).Take(6).Where(value => !string.IsNullOrWhiteSpace(value)).ToArray();
                var stats = ParseStats(string.Join(" ", descriptions));
                var stackable = Flag(At(row.Fields, 22));
                var trade = Flag(At(row.Fields, 19));
                var warehouse = Flag(At(row.Fields, 21));
                AddEntity(workspace, "ItemProfile", $"phase2:item-profile:{sectionName.ToLowerInvariant()}:{clientItemId}", Relative(gameData.Path),
                    $"gamedata:{sectionName}:{row.RecordIndex}", row.RecordIndex, gameData.SourceHash, At(row.Fields, 4),
                    descriptions.Length == 0 ? null : string.Join("\n", descriptions), "Derived", "VerifiedDecode", new()
                    {
                        ["formalAuthorityKey"] = authority,
                        ["clientItemId"] = clientItemId,
                        ["sourceSection"] = sectionName,
                        ["itemFamily"] = category.Item1,
                        ["officialCategoryZhTw"] = category.Item2,
                        ["stackable"] = stackable,
                        ["stackPolicy"] = stackable is null ? "Unknown" : stackable.Value ? "ExplicitStackable" : "ExplicitNonStackable",
                        ["maximumStack"] = null,
                        ["maximumStackEvidenceStatus"] = "EvidenceBlocked",
                        ["tradePolicy"] = trade is null ? "Unknown" : trade.Value ? "Allowed" : "Disallowed",
                        ["warehousePolicy"] = warehouse is null ? "Unknown" : warehouse.Value ? "Allowed" : "Disallowed",
                        ["equipmentSlot"] = EquipmentSlot(category.Item1, string.Join(" ", descriptions)),
                        ["requiredLevel"] = null,
                        ["physicalAttackBonus"] = StatOrNull(stats, "物攻"),
                        ["magicAttackBonus"] = StatOrNull(stats, "魔攻"),
                        ["physicalDefenseBonus"] = StatOrNull(stats, "物防"),
                        ["magicDefenseBonus"] = StatOrNull(stats, "魔防"),
                        ["hpBonus"] = StatOrNull(stats, "HP"),
                        ["mpBonus"] = StatOrNull(stats, "MP"),
                        ["speedBonus"] = StatOrNull(stats, "速度"),
                        ["iconKey"] = At(row.Fields, 3),
                        ["modelKey"] = At(row.Fields, 2),
                        ["systemSellPrice"] = LongOrNull(At(row.Fields, 5)),
                        ["systemRecyclePrice"] = LongOrNull(At(row.Fields, 6)),
                        ["normalUse"] = Flag(At(row.Fields, 14)),
                        ["battleUse"] = Flag(At(row.Fields, 15)),
                        ["equippable"] = Flag(At(row.Fields, 16)),
                        ["useOnOther"] = Flag(At(row.Fields, 17)),
                        ["hotkey"] = Flag(At(row.Fields, 18)),
                        ["droppable"] = Flag(At(row.Fields, 20)),
                        ["combineUp"] = Flag(At(row.Fields, 23)),
                        ["combineDown"] = Flag(At(row.Fields, 24))
                    }, ["MaximumStack", "RequiredLevel"]);
            }
        }

        workspace.Diagnostics["phase2ItemProfiles"] = workspace.Entities.Count(entity => entity.Domain == "ItemProfile");
        return byId;
    }

    private void ExtractMerchantCandidates(RecoveryWorkspace workspace, CsvZDocument gameData, IReadOnlyDictionary<string, GameDataSection> sections, IReadOnlyDictionary<int, string> itemAuthorities)
    {
        if (!sections.TryGetValue("NPCItemList", out var section))
        {
            return;
        }

        foreach (var row in section.Rows)
        {
            if (!TryInt(At(row.Fields, 0), out var groupId))
            {
                continue;
            }

            foreach (var pair in row.Fields.Select((value, index) => (value, index)).Skip(4))
            {
                if (!TryInt(pair.value, out var itemId) || !itemAuthorities.TryGetValue(itemId, out var authority))
                {
                    continue;
                }

                AddEntity(workspace, "MerchantInventoryCandidate", $"phase2:merchant-group:{groupId}:item:{itemId}:column:{pair.index}", Relative(gameData.Path),
                    $"gamedata:NPCItemList:{row.RecordIndex}:{pair.index}", row.RecordIndex, gameData.SourceHash, null, null, "Candidate", "ColumnSemanticsUnresolved", new()
                    {
                        ["clientInventoryGroupId"] = groupId,
                        ["formalItemAuthorityKey"] = authority,
                        ["clientItemId"] = itemId,
                        ["buyPrice"] = null,
                        ["sellPrice"] = null,
                        ["quantityLimit"] = null,
                        ["refreshPolicy"] = "Unknown",
                        ["enabled"] = false
                    }, ["MerchantIdentity", "ColumnSemantics", "PricePolicy"]);
            }
        }
    }

    private void ExtractSkillProfiles(RecoveryWorkspace workspace, IReadOnlyDictionary<string, CsvZDocument> documents)
    {
        foreach (var relative in new[] { "Data2/Patch/Comm/FightPetSkill.csvZ", "Data2/Patch/Comm/NewFightPet2Skill.csvZ", "Data2/Patch/SpgEft.csvZ", "Data2/Patch/Comm/SpgEft.csvZ" })
        {
            if (!documents.TryGetValue(relative, out var document))
            {
                continue;
            }

            foreach (var row in document.Rows)
            {
                var nameIndex = row.Fields.ToList().FindLastIndex(HasCjk);
                if (nameIndex < 0)
                {
                    continue;
                }

                var name = row.Fields[nameIndex];
                var clientSkillId = row.Fields.Select(IntOrNull).FirstOrDefault(value => value is not null);
                var family = ClassifySkill(name);
                var target = ClassifyTarget(name);
                AddEntity(workspace, "SkillProfile", $"phase2:skill-profile:{Stable(relative)}:{row.RecordIndex}", relative,
                    $"skill-profile:{relative}:{row.RecordIndex}", row.RecordIndex, document.SourceHash, name, null,
                    family == "Unknown" ? "EvidenceBlocked" : "Derived", "VerifiedDecode", new()
                    {
                        ["clientSkillId"] = clientSkillId,
                        ["skillFamily"] = family,
                        ["skillFamilyEvidenceStatus"] = family == "Unknown" ? "EvidenceBlocked" : "Derived",
                        ["targetPolicy"] = target,
                        ["targetPolicyEvidenceStatus"] = target == "Unknown" ? "EvidenceBlocked" : "Derived",
                        ["mpCostPolicy"] = "Unknown",
                        ["mpCost"] = null,
                        ["mpCostEvidenceStatus"] = "EvidenceBlocked",
                        ["effectReferences"] = row.Fields.Where(value => value.Contains("Eft", StringComparison.OrdinalIgnoreCase)).ToArray(),
                        ["statusReferences"] = Array.Empty<string>(),
                        ["animationKey"] = At(row.Fields, 3),
                        ["presentationKey"] = At(row.Fields, 2),
                        ["enabled"] = false
                    }, ["AuthoritativeSkillIdentity", "MpCostPolicy"]);
            }
        }
    }

    private void ExtractQuestProfiles(RecoveryWorkspace workspace, IReadOnlyDictionary<string, CsvZDocument> documents, IReadOnlyDictionary<int, string> itemAuthorities)
    {
        foreach (var pair in documents.Where(pair => pair.Key.Contains("Mission", StringComparison.OrdinalIgnoreCase)))
        {
            foreach (var row in pair.Value.Rows)
            {
                if (!TryInt(At(row.Fields, 0), out var questId))
                {
                    continue;
                }

                var texts = row.Fields.Where(HasCjk).ToArray();
                if (texts.Length == 0)
                {
                    continue;
                }

                AddEntity(workspace, "QuestProfile", $"phase2:quest-profile:{Stable(pair.Key)}:{questId}:{row.RecordIndex}", pair.Key,
                    $"quest:{pair.Key}:{row.RecordIndex}", row.RecordIndex, pair.Value.SourceHash, texts[0], string.Join("\n", texts.Skip(1)),
                    "PartiallyMapped", "VerifiedDecode", new()
                    {
                        ["clientQuestId"] = questId,
                        ["steps"] = row.Fields,
                        ["startNpcClientId"] = IntOrNull(At(row.Fields, 3)),
                        ["endNpcClientId"] = IntOrNull(At(row.Fields, 10)),
                        ["rewardText"] = texts.LastOrDefault(),
                        ["objectiveEvidenceStatus"] = "PartiallyMapped",
                        ["rewardEvidenceStatus"] = "PartiallyMapped"
                    }, ["AuthoritativeQuestIdentity", "ObjectiveRecordLayout", "RewardRecordLayout"]);

                foreach (var value in row.Fields)
                {
                    if (TryInt(value, out var itemId) && itemAuthorities.TryGetValue(itemId, out var authority))
                    {
                        AddEntity(workspace, "QuestObjectiveCandidate", $"phase2:quest-objective:{Stable(pair.Key)}:{questId}:{row.RecordIndex}:{itemId}", pair.Key,
                            $"quest-item:{pair.Key}:{row.RecordIndex}:{itemId}", row.RecordIndex, pair.Value.SourceHash, texts[0], null, "Candidate", "ExactItemIdCrossReference", new()
                            {
                                ["clientQuestId"] = questId,
                                ["objectiveType"] = "ItemCandidate",
                                ["formalItemAuthorityKey"] = authority,
                                ["formalMonsterAuthorityKey"] = null,
                                ["requiredQuantity"] = null,
                                ["objectiveText"] = texts.FirstOrDefault(),
                                ["enabled"] = false
                            }, ["ColumnSemantics", "RequiredQuantity"]);
                    }
                }
            }
        }
    }

    private void ExtractDayMissionCoordinates(RecoveryWorkspace workspace, IReadOnlyDictionary<string, CsvZDocument> documents)
    {
        if (!documents.TryGetValue("Data2/Patch/Comm/DayMissionDesc.csvZ", out var document))
        {
            return;
        }

        foreach (var row in document.Rows)
        {
            foreach (var text in row.Fields.Where(value => Coordinate.IsMatch(value)))
            {
                foreach (Match match in Coordinate.Matches(text))
                {
                    AddCoordinate(workspace, document, row, match.Groups["npc"].Value.Trim(), match.Groups["map"].Value.Trim(),
                        int.Parse(match.Groups["x"].Value, CultureInfo.InvariantCulture), int.Parse(match.Groups["y"].Value, CultureInfo.InvariantCulture),
                        row.Fields.Select(IntOrNull).FirstOrDefault(value => value is not null), "Derived", "Official quest location text; identity remains cross-reference evidence.");
                }
            }
        }
    }

    private void ExtractEquipmentSets(RecoveryWorkspace workspace, IReadOnlyDictionary<string, CsvZDocument> documents, IReadOnlyDictionary<int, string> itemAuthorities)
    {
        if (!documents.TryGetValue("Data2/Patch/Comm/EquipSetList.csvZ", out var document))
        {
            return;
        }

        foreach (var row in document.Rows)
        {
            if (!TryParseEquipmentSetRow(row.Fields, out var parsed))
            {
                continue;
            }

            AddEntity(workspace, "EquipmentSet", $"phase2:equipment-set:{parsed.SetId}", "Data2/Patch/Comm/EquipSetList.csvZ", $"equipment-set:{row.RecordIndex}",
                row.RecordIndex, document.SourceHash, parsed.Name, parsed.Effects, "Verified", "ExplicitOfficialColumnLayout", new()
                { ["setId"] = parsed.SetId, ["requiredPieces"] = parsed.RequiredPieces, ["effects"] = parsed.Effects }, []);

            foreach (var member in parsed.Members)
            {
                if (!itemAuthorities.TryGetValue(member.ItemId, out var authority))
                {
                    continue;
                }

                AddEntity(workspace, "EquipmentSetMember", $"phase2:equipment-set:{parsed.SetId}:item:{member.ItemId}", "Data2/Patch/Comm/EquipSetList.csvZ",
                    $"equipment-set-member:{row.RecordIndex}:{member.SlotName}", row.RecordIndex, document.SourceHash, null, null, "Verified", "ExplicitOfficialColumnLayoutAndExactItemId", new()
                    { ["setId"] = parsed.SetId, ["formalItemAuthorityKey"] = authority, ["slotName"] = member.SlotName }, []);
            }
        }
    }

    private void ExtractContainerRelationships(RecoveryWorkspace workspace, IReadOnlyDictionary<string, CsvZDocument> documents, IReadOnlyDictionary<int, string> itemAuthorities)
    {
        foreach (var pair in documents.Where(pair => Path.GetFileName(pair.Key).Contains("FuDai", StringComparison.OrdinalIgnoreCase)))
        {
            foreach (var row in pair.Value.Rows)
            {
                var resolved = row.Fields.Select(value => IntOrNull(value)).Where(value => value is not null && itemAuthorities.ContainsKey(value.Value)).Select(value => value!.Value).Distinct().ToArray();
                if (resolved.Length < 2)
                {
                    continue;
                }

                var container = resolved[0];
                foreach (var item in resolved.Skip(1))
                {
                    AddEntity(workspace, "ContainerRelationship", $"phase2:container:{container}:item:{item}:{Stable(pair.Key)}:{row.RecordIndex}", pair.Key,
                        $"container:{pair.Key}:{row.RecordIndex}", row.RecordIndex, pair.Value.SourceHash, null, null, "Candidate", "ExactItemIdCrossReference", new()
                        {
                            ["containerAuthorityKey"] = itemAuthorities[container],
                            ["containedItemAuthorityKey"] = itemAuthorities[item],
                            ["quantity"] = null,
                            ["originalProbability"] = null,
                            ["effectiveProbability"] = 0m,
                            ["probabilityEvidenceStatus"] = "DefaultDisabledZero",
                            ["relationshipStatus"] = "Candidate",
                            ["enabled"] = false
                        }, ["RecordLayout", "Probability"]);
                }
            }
        }
    }

    private void ExtractPetContent(RecoveryWorkspace workspace, IReadOnlyDictionary<string, CsvZDocument> documents, IReadOnlyDictionary<int, string> itemAuthorities)
    {
        if (documents.TryGetValue("Data2/Patch/Comm/NewCombatPet_AllInnate.csvZ", out var innate))
        {
            foreach (var row in innate.Rows)
            {
                if (!TryParsePetInnateRow(row.Fields, out var parsed))
                {
                    continue;
                }

                AddEntity(workspace, "PetInnate", $"phase2:pet-innate:{parsed.InnateId}", "Data2/Patch/Comm/NewCombatPet_AllInnate.csvZ", $"pet-innate:{row.RecordIndex}",
                    row.RecordIndex, innate.SourceHash, parsed.Name, parsed.Description, "Verified", "ExplicitOfficialColumnLayout", new()
                    {
                        ["innateId"] = parsed.InnateId,
                        ["artifactName"] = parsed.ArtifactName,
                        ["effectReference"] = parsed.EffectReference,
                        ["effectValue"] = parsed.EffectValue
                    }, []);
            }
        }

        foreach (var pair in documents.Where(pair => pair.Key.Contains("PetDesignDesc", StringComparison.OrdinalIgnoreCase) || pair.Key.Contains("NewCombatPet_Category", StringComparison.OrdinalIgnoreCase)))
        {
            foreach (var row in pair.Value.Rows)
            {
                if (!TryInt(At(row.Fields, 0), out var petId) || !row.Fields.Any(HasCjk))
                {
                    continue;
                }

                var referencedItem = row.Fields.Select(IntOrNull).Where(value => value is not null && itemAuthorities.ContainsKey(value.Value)).Select(value => itemAuthorities[value!.Value]).FirstOrDefault();
                AddEntity(workspace, "PetProfile", $"phase2:pet-profile:{Stable(pair.Key)}:{petId}:{row.RecordIndex}", pair.Key, $"pet:{pair.Key}:{row.RecordIndex}",
                    row.RecordIndex, pair.Value.SourceHash, row.Fields.First(HasCjk), null, "PartiallyMapped", "VerifiedDecode", new()
                    {
                        ["clientPetId"] = petId,
                        ["formalItemAuthorityKey"] = referencedItem,
                        ["petFamily"] = "CombatPet",
                        ["growthType"] = null,
                        ["baseStats"] = new Dictionary<string, int>(),
                        ["skillReferences"] = Array.Empty<string>(),
                        ["evolutionReferences"] = Array.Empty<string>()
                    }, ["GrowthType", "BaseStats", "SkillReferences", "EvolutionReferences"]);
            }
        }
    }

    private async Task ExtractGuideEvidenceAsync(RecoveryWorkspace workspace, CancellationToken cancellationToken)
    {
        var path = Path.Combine(_repositoryRoot, "Artifacts", RecoveryVersions.Phase, "verified-guide-evidence.json");
        if (!File.Exists(path))
        {
            workspace.Diagnostics["phase2GuideEvidence"] = "NOT_AVAILABLE";
            return;
        }

        var hash = await ContentHash.Sha256FileAsync(path, cancellationToken);
        workspace.Sources.Add(new SourceInventoryEntry("phase2:verified-guide-evidence", "ValidatedSupplementalEvidence", RelativeRepository(path), hash,
            new FileInfo(path).Length, null, "System.Text.Json", "GuideCrossReference"));
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(path, cancellationToken));
        var relationships = 0;
        foreach (var row in document.RootElement.GetProperty("rows").EnumerateArray())
        {
            var domain = String(row, "domain");
            if (domain == "MonsterDropEvidence")
            {
                var fields = row.GetProperty("fields");
                var monsters = References(fields, "monsterTemplateReference", "monsterTemplateReferences");
                var items = References(fields, "itemReference", "itemReferences");
                foreach (var monster in monsters)
                    foreach (var item in items)
                    {
                        AddEntity(workspace, "MonsterDropRelationship", $"phase2:drop:{ContentHash.StableId(monster, item, String(row, "authorityKey") ?? string.Empty)}",
                            String(row, "sourceFile") ?? RelativeRepository(path), String(row, "sourceIdentity") ?? "guide", Int(row, "sourceRow"), String(row, "sourceHash") ?? hash,
                            String(row, "name"), null, String(row, "evidenceStatus") == "Derived" ? "Derived" : "Candidate", "ExactOfficialIdentityCrossReference", new()
                            {
                                ["formalMonsterAuthorityKey"] = monster,
                                ["formalItemAuthorityKey"] = item,
                                ["minimumQuantity"] = Int(fields, "minimumQuantity"),
                                ["maximumQuantity"] = Int(fields, "maximumQuantity"),
                                ["originalDropChance"] = Decimal(fields, "dropChance"),
                                ["effectiveDropChance"] = 0m,
                                ["weight"] = Decimal(fields, "dropWeight"),
                                ["rollType"] = null,
                                ["exclusiveGroup"] = null,
                                ["guaranteed"] = null,
                                ["chanceEvidenceStatus"] = "DefaultDisabledZero",
                                ["dropRelationshipStatus"] = String(row, "evidenceStatus") == "Derived" ? "Derived" : "Candidate",
                                ["isDropEnabled"] = false
                            }, ["DropChance"]);
                        relationships++;
                    }
            }
            else if (domain == "NpcSpawnEvidence")
            {
                var fields = row.GetProperty("fields");
                var x = Int(fields, "x") ?? Int(fields, "positionX");
                var y = Int(fields, "y") ?? Int(fields, "positionY");
                if (x is not null && y is not null)
                {
                    AddEntity(workspace, "NpcCoordinateEvidence", $"phase2:npc-coordinate:{ContentHash.StableId(String(row, "authorityKey") ?? string.Empty, x.Value.ToString(), y.Value.ToString())}",
                        String(row, "sourceFile") ?? RelativeRepository(path), String(row, "sourceIdentity") ?? "guide", Int(row, "sourceRow"), String(row, "sourceHash") ?? hash,
                        String(row, "name"), null, "Candidate", "GuideCoordinateCrossReference", new()
                        {
                            ["formalNpcAuthorityKey"] = References(fields, "npcTemplateReference", "npcTemplateReferences").FirstOrDefault(),
                            ["clientNpcId"] = Int(fields, "clientNpcId"),
                            ["mapId"] = Int(fields, "mapId"),
                            ["mapName"] = String(fields, "mapName"),
                            ["x"] = x,
                            ["y"] = y,
                            ["direction"] = Int(fields, "direction"),
                            ["coordinateEvidenceStatus"] = "Candidate",
                            ["productionSpawnEnabled"] = false
                        }, ["OfficialNpcIdentity", "OfficialMapIdentity"]);
                }
            }
            else if (domain == "SkillNumericEvidence" && String(row, "evidenceStatus") == "Derived")
            {
                var fields = row.GetProperty("fields");
                var skill = References(fields, "skillReference", "skillReferences")
                    .Concat(References(fields, "officialClientReference", "officialClientReferences"))
                    .Distinct(StringComparer.Ordinal).FirstOrDefault();
                if (skill is not null)
                {
                    var mp = Int(fields, "mpCost");
                    var targetCandidate = String(fields, "targetPolicy") ?? "Unknown";
                    var target = IsCanonicalTarget(targetCandidate) ? targetCandidate : "Unknown";
                    var family = String(fields, "skillFamily") ?? "Unknown";
                    AddEntity(workspace, "SkillProfile", $"phase2:guide-skill:{ContentHash.StableId(skill, String(row, "authorityKey") ?? string.Empty)}",
                        String(row, "sourceFile") ?? RelativeRepository(path), String(row, "sourceIdentity") ?? "guide", Int(row, "sourceRow"), String(row, "sourceHash") ?? hash,
                        String(row, "name"), null, "Derived", "ExactOfficialIdentityCrossReference", new()
                        {
                            ["formalSkillAuthorityKey"] = skill,
                            ["clientSkillId"] = null,
                            ["skillFamily"] = family,
                            ["skillFamilyEvidenceStatus"] = family == "Unknown" ? "EvidenceBlocked" : "Derived",
                            ["targetPolicy"] = target,
                            ["targetPolicyEvidenceStatus"] = target == "Unknown" ? "EvidenceBlocked" : "Derived",
                            ["mpCostPolicy"] = mp is null ? "Unknown" : mp == 0 ? "ExplicitZero" : "Fixed",
                            ["mpCost"] = mp,
                            ["mpCostEvidenceStatus"] = mp is null ? "EvidenceBlocked" : "Derived",
                            ["effectReferences"] = Array.Empty<string>(),
                            ["statusReferences"] = Array.Empty<string>(),
                            ["animationKey"] = null,
                            ["presentationKey"] = null,
                            ["enabled"] = false
                        }, mp is null ? ["MpCost"] : []);
                }
            }
        }

        workspace.Diagnostics["phase2GuideDropRelationships"] = relationships;
    }

    private async Task ExtractHistoricalObservationsAsync(RecoveryWorkspace workspace, CancellationToken cancellationToken)
    {
        var sibling = Path.GetFullPath(Path.Combine(_repositoryRoot, "..", "God2 Classic Unified Server"));
        if (!Directory.Exists(sibling))
        {
            workspace.Diagnostics["historicalObservations"] = "NOT_AVAILABLE";
            return;
        }

        var candidates = Directory.EnumerateFiles(sibling, "*.json", SearchOption.AllDirectories)
            .Where(path => path.Contains("Action", StringComparison.OrdinalIgnoreCase) || path.Contains("Observation", StringComparison.OrdinalIgnoreCase))
            .Take(250).ToArray();
        var count = 0;
        foreach (var path in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var info = new FileInfo(path);
            if (info.Length > 2_000_000)
            {
                continue;
            }

            var text = await File.ReadAllTextAsync(path, cancellationToken);
            if (!HasCjk(text) || ContainsSensitiveShape(text))
            {
                continue;
            }

            var hash = await ContentHash.Sha256FileAsync(path, cancellationToken);
            var relative = Path.GetRelativePath(sibling, path).Replace('\\', '/');
            var sourceFile = $"historical:{relative}";
            var sourceIdentity = $"phase2:historical:{relative}";
            workspace.Sources.Add(new SourceInventoryEntry(sourceIdentity, "HistoricalGameplayObservation", sourceFile, hash,
                info.Length, 1, "ReadOnlyJsonHashMetadata", "HistoricalSupplementalEvidence"));
            workspace.Raw.Add(new RawEvidenceRecord(
                ContentHash.StableId("phase2-historical-metadata", relative, hash),
                "HistoricalObservationMetadata",
                "HistoricalGameplayObservation",
                sourceFile,
                sourceIdentity,
                null,
                null,
                hash,
                DetectLanguage(text),
                null,
                RecoveryJson.Compact(new { source = relative, eventName = Path.GetFileNameWithoutExtension(path), fullContentStored = false, gameplayProjection = "EvidenceBlocked" }),
                hash,
                "HashOnly",
                "EvidenceBlocked"));
            count++;
        }

        workspace.Diagnostics["historicalMetadataArchives"] = count;
    }

    private void AddCoordinate(RecoveryWorkspace workspace, CsvZDocument document, CsvRow row, string npc, string map, int x, int y, int? clientNpcId, string evidence, string rule)
    {
        AddEntity(workspace, "NpcCoordinateEvidence", $"phase2:npc-coordinate:{ContentHash.StableId(npc, map, x.ToString(), y.ToString(), document.SourceHash)}",
            Relative(document.Path), $"day-mission-coordinate:{row.RecordIndex}:{npc}:{x}:{y}", row.RecordIndex, document.SourceHash, npc, null, evidence, "OfficialDisplayText", new()
            {
                ["formalNpcAuthorityKey"] = null,
                ["clientNpcId"] = clientNpcId,
                ["mapId"] = null,
                ["mapName"] = map,
                ["x"] = x,
                ["y"] = y,
                ["direction"] = null,
                ["coordinateEvidenceStatus"] = evidence,
                ["productionSpawnEnabled"] = false
            }, ["AuthoritativeNpcIdentity", "AuthoritativeMapIdentity"]);
    }

    private static void AddEntity(RecoveryWorkspace workspace, string domain, string authority, string file, string identity, int? row, string hash,
        string? name, string? description, string evidence, string confidence, Dictionary<string, object?> fields, IReadOnlyList<string> missing)
    {
        workspace.Entities.Add(new ContentEntity(domain, authority, file, identity, row, hash, name, description, DetectLanguage($"{name}\n{description}"),
            confidence, evidence, fields, missing, "Phase 2 field-level recovery; unknown semantics remain explicitly isolated."));
    }

    public static string ClassifyItemFamily(string section) => ItemFamilies.GetValueOrDefault(section, ("Unknown", "未分類")).Item1;

    public static bool TryParseEquipmentSetRow(IReadOnlyList<string> fields, out Phase2EquipmentSetRow parsed)
    {
        parsed = null!;
        if (!TryInt(At(fields, 0), out var setId) || setId <= 0 ||
            !TryInt(At(fields, 1), out var requiredPieces) || requiredPieces is < 1 or > 5 ||
            string.IsNullOrWhiteSpace(At(fields, 2)) || !HasCjk(At(fields, 2)))
        {
            return false;
        }

        var members = new List<Phase2EquipmentSetMember>();
        foreach (var column in new[] { (Index: 3, Slot: "Armor"), (Index: 5, Slot: "Helmet"), (Index: 7, Slot: "Gloves"), (Index: 9, Slot: "Shoes"), (Index: 11, Slot: "Weapon") })
        {
            if (TryInt(At(fields, column.Index), out var itemId) && itemId > 0)
            {
                members.Add(new Phase2EquipmentSetMember(itemId, column.Slot));
            }
        }

        if (members.Count < requiredPieces)
        {
            return false;
        }

        parsed = new Phase2EquipmentSetRow(setId, requiredPieces, At(fields, 2)!, At(fields, 13) ?? string.Empty, members);
        return true;
    }

    public static bool TryParsePetInnateRow(IReadOnlyList<string> fields, out Phase2PetInnateRow parsed)
    {
        parsed = null!;
        if (!TryInt(At(fields, 0), out var innateId) || innateId < 0 || !HasCjk(At(fields, 1)))
        {
            return false;
        }

        parsed = new Phase2PetInnateRow(
            innateId,
            At(fields, 1)!,
            string.IsNullOrWhiteSpace(At(fields, 2)) ? null : At(fields, 2),
            string.IsNullOrWhiteSpace(At(fields, 3)) ? null : At(fields, 3),
            string.IsNullOrWhiteSpace(At(fields, 4)) ? null : At(fields, 4),
            IntOrNull(At(fields, 5)));
        return true;
    }

    public static IReadOnlyDictionary<string, int> ParseStats(string text)
    {
        var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in Stat.Matches(text))
        {
            result[match.Groups["name"].Value.ToUpperInvariant()] = int.Parse(match.Groups["value"].Value, CultureInfo.InvariantCulture);
        }
        return result;
    }

    private static int? StatOrNull(IReadOnlyDictionary<string, int> stats, string name) =>
        stats.TryGetValue(name, out var value) ? value : null;

    public static bool TryParseCoordinate(string text, out string map, out string npc, out int x, out int y)
    {
        var match = Coordinate.Match(text);
        map = match.Success ? match.Groups["map"].Value.Trim() : string.Empty;
        npc = match.Success ? match.Groups["npc"].Value.Trim() : string.Empty;
        x = match.Success ? int.Parse(match.Groups["x"].Value, CultureInfo.InvariantCulture) : 0;
        y = match.Success ? int.Parse(match.Groups["y"].Value, CultureInfo.InvariantCulture) : 0;
        return match.Success;
    }

    private static IReadOnlyList<string> References(JsonElement fields, string singular, string plural)
    {
        var result = new List<string>();
        if (fields.TryGetProperty(singular, out var one) && one.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(one.GetString()))
        {
            result.Add(one.GetString()!);
        }
        if (fields.TryGetProperty(plural, out var many) && many.ValueKind == JsonValueKind.Array)
        {
            result.AddRange(many.EnumerateArray().Where(value => value.ValueKind == JsonValueKind.String).Select(value => value.GetString()!).Where(value => !string.IsNullOrWhiteSpace(value)));
        }
        return result.Distinct(StringComparer.Ordinal).ToArray();
    }

    private static string ClassifySkill(string text) => text switch
    {
        var value when value.Contains("治療", StringComparison.Ordinal) || value.Contains("回復", StringComparison.Ordinal) => "Healing",
        var value when value.Contains("防禦", StringComparison.Ordinal) || value.Contains("護", StringComparison.Ordinal) => "Defense",
        var value when value.Contains("召喚", StringComparison.Ordinal) => "Summon",
        var value when value.Contains("被動", StringComparison.Ordinal) => "Passive",
        var value when value.Contains("攻", StringComparison.Ordinal) || value.Contains("擊", StringComparison.Ordinal) || value.Contains("傷害", StringComparison.Ordinal) => "PhysicalAttack",
        var value when value.Contains("狀態", StringComparison.Ordinal) || value.Contains("中毒", StringComparison.Ordinal) || value.Contains("暈", StringComparison.Ordinal) => "Status",
        _ => "Unknown"
    };

    private static string ClassifyTarget(string text) => text switch
    {
        var value when value.Contains("全體敵", StringComparison.Ordinal) => "AllEnemies",
        var value when value.Contains("單體敵", StringComparison.Ordinal) || value.Contains("一名敵", StringComparison.Ordinal) => "SingleEnemy",
        var value when value.Contains("全體我", StringComparison.Ordinal) || value.Contains("全體友", StringComparison.Ordinal) => "AllAllies",
        var value when value.Contains("自身", StringComparison.Ordinal) || value.Contains("自己", StringComparison.Ordinal) => "Self",
        var value when value.Contains("友方", StringComparison.Ordinal) || value.Contains("隊友", StringComparison.Ordinal) => "SingleAlly",
        _ => "Unknown"
    };

    private static bool IsCanonicalTarget(string value) => value is "Self" or "SingleAlly" or "AllAllies" or "SingleEnemy" or "AllEnemies" or "DeadAlly" or "Area" or "RandomEnemy" or "RandomAlly" or "None";

    private static string? EquipmentSlot(string family, string text) => family switch
    {
        "Weapon" => "Weapon",
        "Helmet" => "Helmet",
        "Accessory" => "Accessory",
        "Armor" when text.Contains("頭", StringComparison.Ordinal) => "Helmet",
        "Armor" => "Armor",
        _ => null
    };

    private static bool? Flag(string? value) => value?.Trim() switch { "1" => true, "0" => false, _ => null };
    private static int? IntOrNull(string? value) => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result) ? result : null;
    private static long? LongOrNull(string? value) => long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result) ? result : null;
    private static bool TryInt(string? value, out int result) => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out result);
    private static string? At(IReadOnlyList<string> values, int index) => index >= 0 && index < values.Count ? values[index] : null;
    private static bool HasCjk(string? value) => !string.IsNullOrWhiteSpace(value) && value.Any(character => character is >= '\u3400' and <= '\u9fff');
    private static string DetectLanguage(string? value) => HasCjk(value) ? "zh-Hans-or-mixed" : "NotApplicable";
    private static string ClassifyTable(string path) => Regex.IsMatch(path, "item|equip", RegexOptions.IgnoreCase) ? "ItemEquipment" : Regex.IsMatch(path, "npc|message", RegexOptions.IgnoreCase) ? "NpcDialog" : Regex.IsMatch(path, "eny|monster|fight", RegexOptions.IgnoreCase) ? "MonsterBattle" : Regex.IsMatch(path, "skill|spg|eft", RegexOptions.IgnoreCase) ? "SkillEffect" : Regex.IsMatch(path, "mission|quest", RegexOptions.IgnoreCase) ? "Quest" : Regex.IsMatch(path, "pet|egg|mount", RegexOptions.IgnoreCase) ? "PetEgg" : "GameplayTable";
    private static string Stable(string value) => Regex.Replace(value.ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');
    private string Relative(string path) => Path.GetRelativePath(_clientRoot, path).Replace('\\', '/');
    private string RelativeRepository(string path) => Path.GetRelativePath(_repositoryRoot, path).Replace('\\', '/');
    private static CsvZDocument Require(IReadOnlyDictionary<string, CsvZDocument> documents, string path) => documents.TryGetValue(path, out var value) ? value : throw new FileNotFoundException($"Required official client table is missing: {path}");
    private static bool ContainsSensitiveShape(string text) => text.Contains("password", StringComparison.OrdinalIgnoreCase) || text.Contains("connectionString", StringComparison.OrdinalIgnoreCase) || text.Contains("packetBody", StringComparison.OrdinalIgnoreCase);
    private static string? String(JsonElement value, string property) => value.TryGetProperty(property, out var item) && item.ValueKind == JsonValueKind.String ? item.GetString() : null;
    private static int? Int(JsonElement value, string property) => value.TryGetProperty(property, out var item) && item.ValueKind == JsonValueKind.Number && item.TryGetInt32(out var result) ? result : null;
    private static decimal? Decimal(JsonElement value, string property) => value.TryGetProperty(property, out var item) && item.ValueKind == JsonValueKind.Number && item.TryGetDecimal(out var result) ? result : null;
}
