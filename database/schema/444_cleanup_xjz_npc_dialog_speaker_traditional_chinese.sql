UPDATE god2_research.xjz_npc_dialog_candidates
SET speaker_or_branch_zh_tw = REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(
                               REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(
                               REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(
                               REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(
                               speaker_or_branch_zh_tw,
                               '城镇', '城鎮'),
                               '传', '傳'),
                               '运', '運'),
                               '阵', '陣'),
                               '谜', '謎'),
                               '圣', '聖'),
                               '主线', '主線'),
                               '剧情', '劇情'),
                               '鹤', '鶴'),
                               '语', '語'),
                               '鳌', '鰲'),
                               '岛', '島'),
                               '绝', '絕'),
                               '游', '遊'),
                               '枫', '楓'),
                               '师', '師'),
                               '爱', '愛'),
                               '变化', '變化'),
                               '资格', '資格'),
                               '条件', '條件'),
                               '基础', '基礎'),
                               '职业', '職業'),
                               '磨练', '磨練'),
                               '预言', '預言'),
                               '来', '來'),
                               '心愿', '心願'),
                               '护卫', '護衛'),
                               '圣獸', '聖獸'),
                               '獸', '獸'),
                               '传家寶', '傳家寶'),
                               '活动', '活動'),
                               '维纳斯', '維納斯'),
                               '初访', '初訪'),
                               '密谈', '密談'),
                               '委托', '委託'),
                               '药', '藥'),
                               '护', '護'),
                               '宝', '寶'),
                               '兽', '獸'),
                               '资', '資')
WHERE speaker_or_branch_zh_tw IS NOT NULL;

CREATE OR REPLACE VIEW god2_research.vw_xjz_npc_dialog_speaker_cleanup_zh_tw AS
SELECT
    COUNT(*) AS remaining_suspected_simplified_speaker_rows,
    'NPC 對話與任務文字' AS game_function_zh_tw,
    'speaker_or_branch_zh_tw 已套用繁中清理；text_zh_tw 原本已是繁中候選文字。' AS cleanup_status_zh_tw,
    UTC_TIMESTAMP() AS generated_at_utc
FROM god2_research.xjz_npc_dialog_candidates
WHERE speaker_or_branch_zh_tw LIKE '%传%'
   OR speaker_or_branch_zh_tw LIKE '%转%'
   OR speaker_or_branch_zh_tw LIKE '%华%'
   OR speaker_or_branch_zh_tw LIKE '%龙%'
   OR speaker_or_branch_zh_tw LIKE '%气%'
   OR speaker_or_branch_zh_tw LIKE '%宠%'
   OR speaker_or_branch_zh_tw LIKE '%学%'
   OR speaker_or_branch_zh_tw LIKE '%习%'
   OR speaker_or_branch_zh_tw LIKE '%双%'
   OR speaker_or_branch_zh_tw LIKE '%无%'
   OR speaker_or_branch_zh_tw LIKE '%级%'
   OR speaker_or_branch_zh_tw LIKE '%药%'
   OR speaker_or_branch_zh_tw LIKE '%装%'
   OR speaker_or_branch_zh_tw LIKE '%发%'
   OR speaker_or_branch_zh_tw LIKE '%击%'
   OR speaker_or_branch_zh_tw LIKE '%术%'
   OR speaker_or_branch_zh_tw LIKE '%圣%'
   OR speaker_or_branch_zh_tw LIKE '%势%'
   OR speaker_or_branch_zh_tw LIKE '%炼%'
   OR speaker_or_branch_zh_tw LIKE '%宝%'
   OR speaker_or_branch_zh_tw LIKE '%飞%'
   OR speaker_or_branch_zh_tw LIKE '%门%'
   OR speaker_or_branch_zh_tw LIKE '%队%'
   OR speaker_or_branch_zh_tw LIKE '%阵%'
   OR speaker_or_branch_zh_tw LIKE '%类%'
   OR speaker_or_branch_zh_tw LIKE '%数%'
   OR speaker_or_branch_zh_tw LIKE '%复%'
   OR speaker_or_branch_zh_tw LIKE '%态%'
   OR speaker_or_branch_zh_tw LIKE '%证%'
   OR speaker_or_branch_zh_tw LIKE '%据%'
   OR speaker_or_branch_zh_tw LIKE '%补%'
   OR speaker_or_branch_zh_tw LIKE '%齐%'
   OR speaker_or_branch_zh_tw LIKE '%触%'
   OR speaker_or_branch_zh_tw LIKE '%优%'
   OR speaker_or_branch_zh_tw LIKE '%实%'
   OR speaker_or_branch_zh_tw LIKE '%图%'
   OR speaker_or_branch_zh_tw LIKE '%观%'
   OR speaker_or_branch_zh_tw LIKE '%师%'
   OR speaker_or_branch_zh_tw LIKE '%开%'
   OR speaker_or_branch_zh_tw LIKE '%关%'
   OR speaker_or_branch_zh_tw LIKE '%运%'
   OR speaker_or_branch_zh_tw LIKE '%维%'
   OR speaker_or_branch_zh_tw LIKE '%启%'
   OR speaker_or_branch_zh_tw LIKE '%归%'
   OR speaker_or_branch_zh_tw LIKE '%线%'
   OR speaker_or_branch_zh_tw LIKE '%须%'
   OR speaker_or_branch_zh_tw LIKE '%简%'
   OR speaker_or_branch_zh_tw LIKE '%还%';
