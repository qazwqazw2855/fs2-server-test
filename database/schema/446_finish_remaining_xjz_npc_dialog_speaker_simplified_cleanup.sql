UPDATE god2_research.xjz_npc_dialog_candidates
SET speaker_or_branch_zh_tw = REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(
                               REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(
                               REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(
                               REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(
                               speaker_or_branch_zh_tw,
                               '镇', '鎮'),
                               '感觉', '感覺'),
                               '头', '頭'),
                               '询', '詢'),
                               '将', '將'),
                               '东西', '東西'),
                               '调查', '調查'),
                               '踪迹', '蹤跡'),
                               '踪', '蹤'),
                               '迹', '跡'),
                               '虽然', '雖然'),
                               '并非', '並非'),
                               '却', '卻'),
                               '视', '視'),
                               '还是', '還是'),
                               '出马', '出馬'),
                               '至于', '至於'),
                               '应该', '應該'),
                               '该', '該'),
                               '这样', '這樣'),
                               '这', '這'),
                               '告诉', '告訴'),
                               '我们', '我們'),
                               '因缘际', '因緣際'),
                               '启动', '啟動'),
                               '迷宫', '迷宮'),
                               '楼', '樓'),
                               '潜入', '潛入'),
                               '让', '讓'),
                               '离開', '離開'),
                               '麻烦', '麻煩'),
                               '宠', '寵'),
                               '强化', '強化'),
                               '黄', '黃'),
                               '飞', '飛'),
                               '试煉', '試煉'),
                               '战宠', '戰寵'),
                               '破顶', '破頂'),
                               '驯宠', '馴寵'),
                               '达人', '達人')
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
   OR speaker_or_branch_zh_tw LIKE '%还%'
   OR speaker_or_branch_zh_tw LIKE '%说%'
   OR speaker_or_branch_zh_tw LIKE '%货%'
   OR speaker_or_branch_zh_tw LIKE '%镇%'
   OR speaker_or_branch_zh_tw LIKE '%觉%'
   OR speaker_or_branch_zh_tw LIKE '%头%'
   OR speaker_or_branch_zh_tw LIKE '%询%'
   OR speaker_or_branch_zh_tw LIKE '%将%'
   OR speaker_or_branch_zh_tw LIKE '%东%'
   OR speaker_or_branch_zh_tw LIKE '%调%'
   OR speaker_or_branch_zh_tw LIKE '%踪%'
   OR speaker_or_branch_zh_tw LIKE '%迹%'
   OR speaker_or_branch_zh_tw LIKE '%虽%'
   OR speaker_or_branch_zh_tw LIKE '%并%'
   OR speaker_or_branch_zh_tw LIKE '%却%'
   OR speaker_or_branch_zh_tw LIKE '%视%'
   OR speaker_or_branch_zh_tw LIKE '%马%'
   OR speaker_or_branch_zh_tw LIKE '%于%'
   OR speaker_or_branch_zh_tw LIKE '%该%'
   OR speaker_or_branch_zh_tw LIKE '%这%'
   OR speaker_or_branch_zh_tw LIKE '%诉%'
   OR speaker_or_branch_zh_tw LIKE '%们%'
   OR speaker_or_branch_zh_tw LIKE '%缘%'
   OR speaker_or_branch_zh_tw LIKE '%动%'
   OR speaker_or_branch_zh_tw LIKE '%宫%'
   OR speaker_or_branch_zh_tw LIKE '%楼%'
   OR speaker_or_branch_zh_tw LIKE '%潜%'
   OR speaker_or_branch_zh_tw LIKE '%让%'
   OR speaker_or_branch_zh_tw LIKE '%离%'
   OR speaker_or_branch_zh_tw LIKE '%烦%'
   OR speaker_or_branch_zh_tw LIKE '%强%'
   OR speaker_or_branch_zh_tw LIKE '%黄%'
   OR speaker_or_branch_zh_tw LIKE '%顶%'
   OR speaker_or_branch_zh_tw LIKE '%驯%'
   OR speaker_or_branch_zh_tw LIKE '%达%';
