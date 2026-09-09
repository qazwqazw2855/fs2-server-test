using System.Collections.ObjectModel;

namespace God2.ClassicServer.Protocol;

public enum PublicBetaDataEvidenceKind
{
    ExactFormatContract,
    CrossVersionFieldCandidate,
    PresentationOnly,
    HistoricalConfigurationOnly
}

public sealed record PublicBetaDataEvidenceDomain(
    string DomainId,
    string FeatureArea,
    IReadOnlyList<string> PublicBetaSources,
    int? SourceFileCount,
    int? SourceRecordCount,
    string ProvenContract,
    PublicBetaDataEvidenceKind EvidenceKind,
    string CurrentServerUse,
    bool CanGuideCurrentExtractor,
    bool DirectGameplayImportAllowed,
    string ImportBoundary);

/// <summary>
/// Formal inventory of non-network data evidence in the public-beta package.
/// The archive contains parsers, tests, counts and hashes, but not the original
/// decoded rows. It can strengthen current extractors and schema hypotheses;
/// it cannot by itself populate production gameplay tables.
/// </summary>
public static class OfficialPublicBetaDataEvidence
{
    public const string ArchiveSha256 = OfficialPublicBetaCrossVersionEvidence.ArchiveSha256;
    public const string PrimaryDocument = "payload/docs/game-data-formats.md";
    public const int SuppliedCsvZFileCount = 50;
    public const int SuppliedCsvZDecodedByteCount = 4_229_682;
    public const int SuppliedCsvZRowCount = 41_510;
    public const int SuppliedCsvZFieldCount = 384_162;
    public const int SectionedGameDataSectionCount = 57;
    public const int SectionedGameDataRecordCount = 12_958;
    public const int DomainCount = 16;

    private static readonly ReadOnlyCollection<PublicBetaDataEvidenceDomain> Domains =
        Array.AsReadOnly<PublicBetaDataEvidenceDomain>(
        [
            D("csvz", "packed game-data transport",
                ["docs/game-data-formats.md#shared-csvz-layer", "src/game_data/flat_table.cpp"],
                50, 41_510,
                "05 16 wrapper; byte-preserved CRLF CSV; exact quoting, doubled quotes, NUL rejection and row consumption.",
                PublicBetaDataEvidenceKind.ExactFormatContract,
                "Cross-check God2PackedFile and quote-aware current-client extraction tests."),

            D("sound_index", "sound/resource lookup",
                ["src/game_data/sound_index.cpp", "Data2/Snd/commsnd.csvZ"],
                1, 1_024,
                "Fixed zero-based sequential slots; 852 populated and 172 intentional empty slots.",
                PublicBetaDataEvidenceKind.PresentationOnly,
                "Guide current sound-index extraction; never invent missing WAV files."),

            D("npc_text", "NPC and quest text",
                ["src/game_data/indexed_text_table.cpp", "src/game_data/npc_text_catalog.cpp"],
                12, 11_417,
                "Mission text IDs 1..9711 and normal text IDs 1..1706 are separate spaces; markup stays literal.",
                PublicBetaDataEvidenceKind.ExactFormatContract,
                "Add separate current-client text namespaces and preserve line/paragraph/color markup."),

            D("npc_presentation", "NPC identity, appearance and map presence",
                ["src/game_data/npc_catalog.cpp", "src/game_data/npc_map_presence.cpp", "src/game_data/map_coordinate_catalog.cpp"],
                5, 2_477,
                "NPC, portrait, sparse appearance, list and map-coordinate key spaces are lossless and may contain unresolved links.",
                PublicBetaDataEvidenceKind.CrossVersionFieldCandidate,
                "Guide NPC source-row joins without unsupported uniqueness or foreign-key assumptions."),

            D("dialog_mission_text", "dialogs and mission descriptions",
                ["src/game_data/dialog_tables.cpp", "MultiMessage01/02.csvZ", "Mission_data.csvZ"],
                3, 7_296,
                "1625 conversations/4154 choices and 248 missions/1269 steps use independent nested key spaces.",
                PublicBetaDataEvidenceKind.ExactFormatContract,
                "Guide current dialog/quest extractor; never join equal integers across independent tables."),

            D("activity_mission_requirements", "quest item requirements",
                ["src/game_data/mission_requirements.cpp", "ActMis*.csvZ", "GameData/MAT01"],
                2, 200,
                "Four item/quantity pairs per mission; empty pairs are exactly 0,0; 34 beta item IDs resolve in MAT01.",
                PublicBetaDataEvidenceKind.CrossVersionFieldCandidate,
                "Provide a current-extractor schema candidate only; completion, consumption and rewards remain server evidence gaps."),

            D("sectioned_gamedata", "items, maps, gods, pets, skills and shared catalogs",
                ["src/game_data/sectioned_game_data.cpp", "Data2/Patch/Comm/gamedata.csvZ"],
                1, 12_958,
                "57 declared sections, including one zero-count section; section metadata and every raw row are retained.",
                PublicBetaDataEvidenceKind.ExactFormatContract,
                "Cross-check current section parser while keeping version-specific row counts separate."),

            D("consumables", "item authoring and request routing",
                ["src/game_data/consumable_item_catalog.cpp", "GameData/MED02"],
                1, 31,
                "MED02 authoring families and presentation effect IDs; client request branch evidence, not effect arithmetic.",
                PublicBetaDataEvidenceKind.CrossVersionFieldCandidate,
                "Guide current item-use experiments and preserve presentation fields as non-authoritative."),

            D("monster_presentation", "monster identity and visuals",
                ["src/game_data/monster_catalog.cpp", "eny.csvZ", "FightEny.csvZ", "GameData/EnyName"],
                3, 2_146,
                "Visual definitions and hierarchical EnyName identities contain no proven combat stats or growth coefficients.",
                PublicBetaDataEvidenceKind.PresentationOnly,
                "Strengthen monster identity joins; explicitly forbid stat derivation from presentation rows."),

            D("skill_book_effect", "skill books and presentation effects",
                ["src/game_data/skill_book_catalog.cpp", "src/game_data/skill_effect_catalog.cpp", "GameData/SKB", "SpgEft.csvZ"],
                2, 518,
                "259 skill-book rows join 259/259 to presentation effects; usage/range text is not cost, damage or targeting authority.",
                PublicBetaDataEvidenceKind.CrossVersionFieldCandidate,
                "Guide exact-current identity/effect cross-links while keeping formulas blocked."),

            D("god_menu", "immortal level-one menu baselines",
                ["src/game_data/god_menu_catalog.cpp", "Data/Patch/God.csvZ"],
                1, 10,
                "Ten protocol IDs with level-one four-ability baselines and five element labels.",
                PublicBetaDataEvidenceKind.CrossVersionFieldCandidate,
                "Corroborate identities/field order only; do not infer growth, HP/MP or derived stats."),

            D("messages_flat_tables", "localized messages and miscellaneous client tables",
                ["src/game_data/message_sections.cpp", "src/game_data/flat_table.cpp"],
                27, 7_747,
                "25 flat tables plus five counted message sections; annotations and prologues remain lossless.",
                PublicBetaDataEvidenceKind.ExactFormatContract,
                "Guide current localization/string-table extraction without collapsing annotation columns."),

            D("server_groups", "launcher/server selection configuration",
                ["src/platform/server_config.cpp", "Data3/config*.iniZ"],
                2, 34,
                "Seven display groups and 34 historical endpoints across 19 unique addresses.",
                PublicBetaDataEvidenceKind.HistoricalConfigurationOnly,
                "Parser regression evidence only; never contact or publish beta endpoints."),

            D("fight_common", "combat presentation resource manifests",
                ["src/game_data/fight_common_table.cpp", "FightComm.csvZ", "FightComm3.csvZ"],
                2, 20,
                "Two ten-row versioned ROM manifests with index-stable replacements.",
                PublicBetaDataEvidenceKind.PresentationOnly,
                "Guide resource diffing; does not prove combat state or input policy."),

            D("combat_special", "skill presentation/control metadata",
                ["src/game_data/combat_special_table.cpp", "Data2/Fight/Spg/cbspec2.bin"],
                1, 343,
                "Four banks, 343 active records, 681 zero slots; selected fields have proven client display/control consumers.",
                PublicBetaDataEvidenceKind.CrossVersionFieldCandidate,
                "Guide current skill metadata recovery; server MP, damage, probability and status formulas remain blocked."),

            D("map_resource_formats", "map collision, layout and model resources",
                ["src/map/can_file.cpp", "src/map/mbd_file.cpp", "src/map/mdt_file.cpp", "src/map/mmb_bitmap.cpp", "src/map/rtb_file.cpp", "src/map/model_text_file.cpp"],
                6, null,
                "Lossless parsers and malformed-input boundaries for CAN/MBD/MDT/MMB/RTB/model text resources.",
                PublicBetaDataEvidenceKind.ExactFormatContract,
                "Cross-check current map/collision tooling; map IDs, portals and server triggers still require current evidence.")
        ]);

    public static IReadOnlyList<PublicBetaDataEvidenceDomain> SnapshotDomains() => Domains;

    public static PublicBetaDataEvidenceDomain? Find(string domainId) =>
        Domains.FirstOrDefault(value => string.Equals(value.DomainId, domainId, StringComparison.Ordinal));

    private static PublicBetaDataEvidenceDomain D(
        string domainId,
        string featureArea,
        IReadOnlyList<string> sources,
        int? sourceFileCount,
        int? sourceRecordCount,
        string contract,
        PublicBetaDataEvidenceKind kind,
        string currentUse) =>
        new(domainId, featureArea, sources, sourceFileCount, sourceRecordCount,
            contract, kind, currentUse, CanGuideCurrentExtractor: true,
            DirectGameplayImportAllowed: false,
            "The archive does not contain the original decoded source rows; current-build files or captures must independently supply importable values.");
}
