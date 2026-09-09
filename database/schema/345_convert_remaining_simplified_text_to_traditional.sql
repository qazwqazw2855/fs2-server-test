UPDATE god2.quest_content_profiles
SET
    NameZhTw = REPLACE(NameZhTw, '小杰', '小傑'),
    RewardTextZhTw = REPLACE(RewardTextZhTw, '小杰', '小傑')
WHERE NameZhTw LIKE '%小杰%' OR RewardTextZhTw LIKE '%小杰%';

UPDATE god2.skill_content_profiles
SET
    AnimationKey = REPLACE(AnimationKey, '档案编号', '檔案編號'),
    PresentationKey = REPLACE(
        REPLACE(
            REPLACE(PresentationKey, '鸟类骑乘动作', '鳥類騎乘動作'),
            '兽类骑乘动作', '獸類騎乘動作'),
        '战斗动作', '戰鬥動作')
WHERE AnimationKey LIKE '%档案编号%'
   OR PresentationKey LIKE '%鸟类骑乘动作%'
   OR PresentationKey LIKE '%兽类骑乘动作%'
   OR PresentationKey LIKE '%战斗动作%';

UPDATE god2_game_meta.admin_change_audit
SET
    old_value = REPLACE(
        REPLACE(
        REPLACE(
        REPLACE(
        REPLACE(
        REPLACE(
        REPLACE(
        REPLACE(
        REPLACE(
        REPLACE(COALESCE(old_value, ''),
            '碧游宫', '碧遊宮'),
            '九尾国', '九尾國'),
            '绝影流风', '絕影流風'),
            '老手回归奖励', '老手回歸獎勵'),
            '活动结束后', '活動結束後'),
            '无法', '無法'),
            '发布', '發布'),
            '土行孙', '土行孫'),
            '档案编号', '檔案編號'),
            '战斗动作', '戰鬥動作'),
    new_value = REPLACE(
        REPLACE(
        REPLACE(
        REPLACE(
        REPLACE(
        REPLACE(
        REPLACE(
        REPLACE(
        REPLACE(
        REPLACE(COALESCE(new_value, ''),
            '碧游宫', '碧遊宮'),
            '九尾国', '九尾國'),
            '绝影流风', '絕影流風'),
            '老手回归奖励', '老手回歸獎勵'),
            '活动结束后', '活動結束後'),
            '无法', '無法'),
            '发布', '發布'),
            '土行孙', '土行孫'),
            '档案编号', '檔案編號'),
            '战斗动作', '戰鬥動作')
WHERE old_value LIKE '%碧游宫%'
   OR old_value LIKE '%九尾国%'
   OR old_value LIKE '%绝影流风%'
   OR old_value LIKE '%老手回归奖励%'
   OR old_value LIKE '%活动结束后%'
   OR old_value LIKE '%无法%'
   OR old_value LIKE '%发布%'
   OR old_value LIKE '%土行孙%'
   OR old_value LIKE '%档案编号%'
   OR old_value LIKE '%战斗动作%'
   OR new_value LIKE '%碧游宫%'
   OR new_value LIKE '%九尾国%'
   OR new_value LIKE '%绝影流风%'
   OR new_value LIKE '%老手回归奖励%'
   OR new_value LIKE '%活动结束后%'
   OR new_value LIKE '%无法%'
   OR new_value LIKE '%发布%'
   OR new_value LIKE '%土行孙%'
   OR new_value LIKE '%档案编号%'
   OR new_value LIKE '%战斗动作%';
