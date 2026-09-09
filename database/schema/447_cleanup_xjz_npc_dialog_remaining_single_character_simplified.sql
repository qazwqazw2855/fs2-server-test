UPDATE god2_research.xjz_npc_dialog_candidates
SET speaker_or_branch_zh_tw = REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(
                               speaker_or_branch_zh_tw,
                               '战', '戰'),
                               '驯', '馴'),
                               '问', '問'),
                               '军', '軍'),
                               '样', '樣'),
                               '还', '還')
WHERE speaker_or_branch_zh_tw IS NOT NULL;
