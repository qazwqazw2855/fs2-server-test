using System.Collections.ObjectModel;

namespace God2.ClassicServer.Protocol;

public enum PublicBetaLengthComparison
{
    ExactMatch,
    BothVariable,
    Changed
}

public enum PublicBetaPromotionStatus
{
    CurrentBuildVerified,
    StructuralCorroborationOnly,
    HypothesisOnly,
    RejectedAsChanged
}

public sealed record PublicBetaOutboundContractComparison(
    string Domain,
    byte Opcode,
    string PublicBetaName,
    string PublicBetaSemanticStatus,
    int? PublicBetaApplicationLength,
    int? CurrentApplicationLength,
    bool PublicBetaVariableLength,
    bool CurrentVariableLength,
    PublicBetaLengthComparison LengthComparison,
    PublicBetaPromotionStatus PromotionStatus,
    string CurrentEvidence,
    string Boundary)
{
    public string GameFeature => (Domain, Opcode) switch
    {
        ("account_login", 0x04) => "帳號登入請求",
        ("route", 0xA9) => "路由與連線節點查詢",
        ("game_login", 0x00) => "遊戲登入請求",
        ("character", 0x17) => "建立角色",
        ("character", 0x18) => "角色欄位與角色目錄查詢",
        ("character", 0x19) => "刪除角色",
        ("character", 0x1A) => "選擇角色",
        ("character", 0x1B) => "建立角色備援請求",
        ("combination", 0xA4) => "合成流程步驟",
        ("combination", 0xA5) => "提交合成結果",
        ("gameplay", 0x0A) => "斷線與登出通知",
        ("gameplay", 0x21) => "屬性加點",
        ("social", 0x33) => "聊天封包外層封裝",
        ("social", 0x2F) => "聊天訊息",
        ("mail", 0xAE) => "信件操作",
        ("combat", 0x35) => "戰鬥行為指令",
        ("mission", 0x24) => "任務與互動請求一",
        ("mission", 0x25) => "任務物品互動請求",
        ("mission", 0x26) => "世界互動請求",
        ("mission", 0x27) => "任務與互動請求二",
        ("mission", 0x28) => "物品使用與功能啟動",
        ("party", 0x8F) => "組隊系統操作",
        ("party", 0x90) => "組隊系統操作",
        ("party", 0x91) => "組隊系統操作",
        ("party", 0x92) => "組隊系統操作",
        ("party", 0x93) => "組隊系統操作",
        ("party", 0x94) => "組隊系統操作",
        ("party", 0x95) => "組隊系統操作",
        ("pet", 0xBB) => "寵物出戰與戰鬥行為",
        ("pet", 0xB7) => "寵物蛋操作",
        ("pk", 0x69) => "PK 目標定位",
        ("pk", 0xB8) => "寵物 PK 目標",
        ("team", 0x5A) => "隊伍請求",
        ("vendor_cart", 0xB0) => "擺攤發布與展示設定",
        ("vendor_cart", 0xB2) => "擺攤操作",
        ("vendor_cart", 0xBD) => "擺攤項目新增",
        _ => "待補功能對照"
    };
}

public sealed record PublicBetaInboundDomainInventory(
    string Domain,
    int EntryCount,
    string PublicBetaSource,
    string CurrentBuildDisposition);

public sealed record PublicBetaInboundContractComparison(
    string Domain,
    byte Opcode,
    string PublicBetaName,
    string GameFeature);

/// <summary>
/// Cross-version intake ledger for the complete public-beta protocol catalog.
/// The public-beta package contributes hypotheses and consumer semantics only.
/// A row is CurrentBuildVerified only when exact-current static or captured
/// evidence independently establishes the same current wire contract.
/// </summary>
public static class OfficialPublicBetaCrossVersionEvidence
{
    public const string ArchivePath = "C:/Users/SeiHo/Desktop/FS2TW-research-evidence-20260815.zip";
    public const string ArchiveSha256 = "75BA3B07058F83581B0E7DC40B8469ABF413ED5CF4271CBDC497D340D907598D";
    public const string CatalogSource = "payload/src/network/legacy_protocol_catalog.cpp";
    public const string CurrentRegistrySource = "protocol/evidence/current-build/client-dispatch-registry.json";
    public const int CatalogEntryCount = 97;
    public const int ClientToServerEntryCount = 36;
    public const int ServerToClientEntryCount = 61;
    public const int ExactSemanticEntryCount = 28;
    public const int PartialSemanticEntryCount = 69;
    public const int MatchingOutboundLengthCount = 20;
    public const int ChangedOutboundLengthCount = 16;

    private static readonly ReadOnlyCollection<PublicBetaOutboundContractComparison> Outbound =
        Array.AsReadOnly<PublicBetaOutboundContractComparison>(
        [
            Changed("account_login", 0x04, "account_login_request", "partial", 169, 205),
            Changed("route", 0xA9, "route_query", "partial", 3, 5),
            Changed("game_login", 0x00, "game_login_request", "partial", 169, 205),

            Verified("character", 0x17, "character_create_17", "partial", 45,
                "God2_opt exact-current 0x17/48 captures; verified 44-byte create payload decoder; " +
                "production Runtime character-create handler and MariaDB lifecycle transaction"),
            Verified("character", 0x18, "character_catalog_selector_request", "partial", 2,
                "God2_opt exact-current C2S 0x18/5 transmitted capture 050092B3C2 from producer caller RVA 0x0003D482; " +
                "production TryDecodeCharacterSlotAction checksum/slot decoder and CharacterSelect Runtime handler"),
            Match("character", 0x19, "character_delete_request", "partial", 54),
            Match("character", 0x1A, "character_select_request", "partial", 38),
            Match("character", 0x1B, "character_create_1b", "partial", 45),

            MatchWithBoundary("combination", 0xA4, "combination_step_request_a4", "exact", 3,
                PublicBetaPromotionStatus.StructuralCorroborationOnly,
                "Current producer emits selector and +/-1 step at RVA 0x0011469C (source 0x001144FB..0x001146A7); semantics stay current-evidence gated."),
            Changed("combination", 0xA5, "combination_commit_request_a5", "exact", 5, 11),
            Match("gameplay", 0x0A, "disconnect_request", "exact", 17),
            MatchWithBoundary("gameplay", 0x21, "attribute_increment_request", "exact", 2,
                PublicBetaPromotionStatus.StructuralCorroborationOnly,
                "Current producer evidence is available and consumed via current dispatcher/consumer boundaries: " +
                OfficialPlayerSnapshotCandidateWireCodec.CurrentStructuralEvidence +
                "; attribute-code semantics remain candidate-only until stepwise 0x21 validation is complete."),
            VariableMatch("social", 0x33, "text_envelope_request_33", "exact"),
            VerifiedVariable("social", 0x2F, "text_message_request_2f", "exact",
                "God2_opt:rva-0x000AFBF0; current C2S capture 0x2F/14"),
            Changed("mail", 0xAE, "mail_record_action_request_ae", "partial", 9, 13),

            MatchWithBoundary("combat", 0x35, "combat_action_request", "partial", 17,
                PublicBetaPromotionStatus.RejectedAsChanged,
                "Current 0x35 keeps the record length but changed field offsets; only the opcode/action prefix is reusable."),
            MatchWithBoundary("mission", 0x24, "interaction_24", "partial", 3,
                PublicBetaPromotionStatus.StructuralCorroborationOnly,
                "Current writer emits raw u8/u8 payload at RVA 0x000AEF2B (source 0x000AEE90..0x000AEF32); semantics remain blocked."),
            Verified("mission", 0x25, "interaction_25", "partial", 5,
                "God2_opt:rva-0x000AEF80..0x000AF7C1; current C2S 0x25/8 captures"),
            MatchWithBoundary("mission", 0x26, "world_interaction_26", "partial", 7,
                PublicBetaPromotionStatus.StructuralCorroborationOnly,
                "Current writer emits raw u16/u16/u8 and trailing byte at RVAs 0x000AF050/0x000AF7A0 (sources 0x000AEFF0..0x000AF062 and 0x000AF6F0..0x000AF7A0)."),
            MatchWithBoundary("mission", 0x27, "interaction_27", "partial", 5,
                PublicBetaPromotionStatus.StructuralCorroborationOnly,
                "Current writer emits raw u32 at RVA 0x000AEF69 (source 0x000AEF50..0x000AEF6F)."),
            Verified("mission", 0x28, "interaction_28", "partial", 5,
                "God2_opt exact-current equipment outbound callsites; current C2S 0x28/8 captures"),

            Changed("party", 0x8F, "party_request_8f", "exact", 3, 25),
            Changed("party", 0x90, "party_request_90", "exact", 2, 5),
            Changed("party", 0x91, "party_request_91", "exact", 25, 5),
            Changed("party", 0x92, "party_request_92", "exact", 25, 2),
            MatchWithBoundary("party", 0x93, "party_request_93", "exact", 5,
                PublicBetaPromotionStatus.StructuralCorroborationOnly,
                "Current writer emits raw u32 payload for opcode 0x93 at RVA 0x000AED64 (source 0x000AED40..0x000AED6A)."),
            Changed("party", 0x94, "party_request_94", "exact", 5, 9),
            MatchWithBoundary("party", 0x95, "party_request_95", "exact", 2,
                PublicBetaPromotionStatus.StructuralCorroborationOnly,
                "Current writer emits raw u16 payload for opcode 0x95 at RVA 0x000B0657 (source 0x000B0610..0x000B066B)."),

            Changed("pet", 0xBB, "fpet_request_bb", "exact", 5, 85),
            Changed("pet", 0xB7, "pet_egg_request_b7", "exact", 2, 5),
            MatchWithBoundary("pk", 0x69, "pk_cursor_target_69", "partial", 45,
                PublicBetaPromotionStatus.HypothesisOnly,
                "Current controlled assistance uses 0x6A/12; current 0x69 target semantics are not observed."),
            Changed("pk", 0xB8, "pet_pk_target_b8", "exact", 5, 2),
            MatchWithBoundary("team", 0x5A, "team_request_5a", "exact", 19,
                PublicBetaPromotionStatus.StructuralCorroborationOnly,
                "Current writer emits u16 + 16 raw bytes for opcode 0x5A using disambiguated RVAs 0x000B0657 and 0x001186A1."),

            VariableChanged("vendor_cart", 0xB0, "vendor_cart_publish_b0", "exact", 3),
            Changed("vendor_cart", 0xB2, "vendor_cart_action_b2", "partial", 13, 5),
            Changed("vendor_cart", 0xBD, "vendor_cart_add_record_bd", "exact", 5, 25)
        ]);

    private static readonly ReadOnlyCollection<PublicBetaInboundContractComparison> Inbound =
        Array.AsReadOnly<PublicBetaInboundContractComparison>(
        [
            new("account_login", 0x01, "account_server_hello", "帳號服務握手與帳號通道"),
            new("account_login", 0x1D, "account_record", "帳號資料與角色目錄回傳"),
            new("account_login", 0x1E, "account_error", "帳號登入錯誤回應"),

            new("route", 0x47, "route_catalog", "路由公告與分流伺服器定位回應"),
            new("route", 0x02, "route_error", "路由查詢錯誤"),

            new("game_login", 0x01, "game_server_hello", "遊戲伺服器握手"),
            new("game_login", 0x1F, "game_login_result", "登入結果與角色進場資料"),
            new("game_login", 0x1E, "game_login_error", "登入流程錯誤"),

            new("character", 0x03, "character_catalog_selector_result", "角色目錄選擇結果"),
            new("character", 0x17, "character_result", "角色操作結果與欄位回傳"),
            new("character", 0x1E, "character_error", "角色錯誤回應"),

            new("combat", 0x1C, "combat_roster_record", "戰鬥名單快照"),
            new("combat", 0x38, "combat_control_boundary_38", "戰鬥控制分隔記錄"),
            new("combat", 0x83, "combat_single_action", "單體戰鬥效果"),
            new("combat", 0x84, "combat_multi_target_action", "多目標戰鬥效果"),
            new("combat", 0x85, "combat_sequence_boundary_85", "戰鬥回合與流程公告"),
            new("combat", 0x86, "combat_control_record", "戰鬥控制紀錄"),
            new("combat", 0x87, "fightgod_spc_record_control_87", "戰鬥特殊控制片段"),
            new("combat", 0x88, "combat_round_snapshot", "戰鬥回合快照"),
            new("combat", 0x89, "combat_settlement", "戰鬥結算摘要"),

            new("mission", 0xDF, "mission_sync", "任務資料同步"),

            new("gameplay", 0x22, "player_status_snapshot", "玩家狀態快照"),
            new("gameplay", 0x24, "player_vitals_snapshot", "玩家生命與法力即時值"),
            new("gameplay", 0x2B, "player_god_progress_snapshot_2b", "玩家與神仙進度"),
            new("gameplay", 0x2C, "player_derived_stats_snapshot", "玩家派生面板數值"),
            new("gameplay", 0x6F, "player_spawn_6f", "玩家出生與角色進入事件"),
            new("gameplay", 0x71, "monster_spawn_71", "怪物出生與刷新"),
            new("gameplay", 0x72, "npc_spawn_72", "NPC 出生與刷新"),
            new("gameplay", 0x30, "god_add_30", "神仙資料新增"),
            new("gameplay", 0x31, "god_status_snapshot_31", "神仙狀態快照"),
            new("gameplay", 0x32, "god_catalog_selector_32", "神仙清單目標選擇"),
            new("gameplay", 0x33, "god_ui_flags_33", "神仙介面旗標"),
            new("gameplay", 0x34, "god_skill_add_34", "神仙技能新增"),
            new("gameplay", 0x35, "god_skill_level_35", "神仙技能等級更新"),
            new("gameplay", 0x39, "god_vitals_snapshot_39", "神仙生命與法力數值快照"),
            new("gameplay", 0xD5, "god_panel_reference_bank_d5", "神仙面板參照欄位"),
            new("gameplay", 0xD6, "god_panel_modifier_bank_d6", "神仙面板加成欄位"),
            new("gameplay", 0xDD, "god_remove_dd", "神仙移除"),

            new("social", 0xDB, "text_channel_selection_db", "聊天頻道設定與系統文字事件"),
            new("social", 0x19, "localized_template_text_19", "在地化文字模板解碼"),
            new("social", 0x20, "stateful_template_text_20", "狀態式文字模板記錄"),

            new("pet", 0xE7, "fpet_add_record_e7", "寵物紀錄新增"),
            new("pet", 0xE8, "fpet_update_record_e8", "寵物紀錄更新"),
            new("pet", 0xE9, "fpet_event_e9", "寵物事件"),
            new("pet", 0xEA, "pet_egg_state_ea", "寵物蛋狀態"),
            new("pet", 0xEB, "pet_egg_event_eb", "寵物蛋事件"),
            new("pet", 0x4A, "mount_state_4a", "坐騎狀態同步"),

            new("tong", 0x98, "tong_snapshot_98", "幫派資料快照"),
            new("tong", 0x8E, "tong_event_8e", "幫派事件"),
            new("tong", 0x8F, "tong_event_8f", "幫派事件"),
            new("tong", 0x90, "tong_event_90", "幫派事件"),
            new("tong", 0x91, "tong_event_91", "幫派事件"),
            new("tong", 0x92, "tong_event_92", "幫派事件"),
            new("tong", 0x93, "tong_event_93", "幫派事件"),
            new("tong", 0x94, "tong_event_94", "幫派事件"),
            new("tong", 0x95, "tong_event_95", "幫派事件"),
            new("tong", 0x96, "tong_event_96", "幫派事件"),
            new("tong", 0x97, "tong_event_97", "幫派事件"),
            new("tong", 0x99, "tong_event_99", "幫派事件"),
            new("tong", 0x9A, "tong_event_9a", "幫派事件"),
            new("tong", 0x9B, "tong_event_9b", "幫派事件")
        ]);

    private static readonly ReadOnlyCollection<PublicBetaInboundDomainInventory> InboundInventory =
        Array.AsReadOnly<PublicBetaInboundDomainInventory>(
        [
            new("account_login", 3, "legacy_account_login", "Current login framing differs; retain as hypothesis."),
            new("route", 2, "legacy_route_query", "Current route request length changed; do not promote response fields by symmetry."),
            new("game_login", 3, "legacy_game_server_login", "Current login request/result lengths changed."),
            new("character", 3, "legacy_character_protocol", "C2S structures match, but current response payload fields remain evidence-gated."),
            new("gameplay", 17, "legacy_actor_spawn_protocol + legacy_combat_protocol + legacy_god_protocol", "Promote per opcode only; current 0x22/0x24 lengths, 0x31/0x39 frozen structures, and the 0x2C local-panel consumer have independent current evidence."),
            new("social", 3, "legacy_text_protocol", "Current general chat uses S2C 0x5C; beta 0x19/0x20/0xDB meanings remain hypotheses."),
            new("combat", 9, "legacy_combat_protocol", "Handled by the separate current battle cross-version ledger."),
            new("mission", 1, "legacy_mission_protocol", "Exact-current 0xDF consumes a three-byte World payload; the public-beta 13-byte subtype/u32/u32 layout is rejected as changed."),
            new("pet", 6, "legacy_pet_protocol", "C2S pet opcodes changed length; no response semantic promotion."),
            new("tong", 14, "legacy_tong_protocol", "Opcode handlers exist in the current client, but current layouts and business meanings are unverified.")
        ]);

    public static IReadOnlyList<PublicBetaOutboundContractComparison> SnapshotOutboundComparisons() => Outbound;

    public static IReadOnlyList<PublicBetaInboundContractComparison> SnapshotInboundContracts() => Inbound;

    public static IReadOnlyList<PublicBetaInboundDomainInventory> SnapshotInboundInventory() => InboundInventory;

    public static PublicBetaOutboundContractComparison? FindOutbound(string domain, byte opcode) =>
        Outbound.FirstOrDefault(value =>
            value.Opcode == opcode && string.Equals(value.Domain, domain, StringComparison.Ordinal));

    public static PublicBetaInboundContractComparison? FindInbound(string domain, byte opcode) =>
        Inbound.FirstOrDefault(value =>
            value.Opcode == opcode && string.Equals(value.Domain, domain, StringComparison.Ordinal));

    private static PublicBetaOutboundContractComparison Match(
        string domain, byte opcode, string name, string semanticStatus, int length) =>
        MatchWithBoundary(domain, opcode, name, semanticStatus, length,
            PublicBetaPromotionStatus.StructuralCorroborationOnly,
            "Opcode and application-record length match; current field semantics still require exact-current evidence.");

    private static PublicBetaOutboundContractComparison Verified(
        string domain, byte opcode, string name, string semanticStatus, int length, string evidence) =>
        new(domain, opcode, name, semanticStatus, length, length, false, false,
            PublicBetaLengthComparison.ExactMatch, PublicBetaPromotionStatus.CurrentBuildVerified,
            evidence, "Current evidence is authoritative; beta evidence is corroboration only.");

    private static PublicBetaOutboundContractComparison MatchWithBoundary(
        string domain, byte opcode, string name, string semanticStatus, int length,
        PublicBetaPromotionStatus promotion, string boundary) =>
        new(domain, opcode, name, semanticStatus, length, length, false, false,
            PublicBetaLengthComparison.ExactMatch, promotion, CurrentRegistrySource, boundary);

    private static PublicBetaOutboundContractComparison Changed(
        string domain, byte opcode, string name, string semanticStatus, int betaLength, int currentLength) =>
        new(domain, opcode, name, semanticStatus, betaLength, currentLength, false, false,
            PublicBetaLengthComparison.Changed, PublicBetaPromotionStatus.RejectedAsChanged,
            CurrentRegistrySource,
            "Current application-record length differs; beta offsets and semantics must not be registered for the current build.");

    private static PublicBetaOutboundContractComparison VariableMatch(
        string domain, byte opcode, string name, string semanticStatus) =>
        new(domain, opcode, name, semanticStatus, null, null, true, true,
            PublicBetaLengthComparison.BothVariable, PublicBetaPromotionStatus.StructuralCorroborationOnly,
            CurrentRegistrySource,
            "Both versions use embedded-length records; field semantics remain current-evidence blocked.");

    private static PublicBetaOutboundContractComparison VerifiedVariable(
        string domain, byte opcode, string name, string semanticStatus, string evidence) =>
        new(domain, opcode, name, semanticStatus, null, null, true, true,
            PublicBetaLengthComparison.BothVariable, PublicBetaPromotionStatus.CurrentBuildVerified,
            evidence, "Current producer and capture are authoritative; beta field layout is corroboration only.");

    private static PublicBetaOutboundContractComparison VariableChanged(
        string domain, byte opcode, string name, string semanticStatus, int currentLength) =>
        new(domain, opcode, name, semanticStatus, null, currentLength, true, false,
            PublicBetaLengthComparison.Changed, PublicBetaPromotionStatus.RejectedAsChanged,
            CurrentRegistrySource,
            "Public beta is variable-length but current is fixed-length; the beta contract is incompatible.");
}



