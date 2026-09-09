-- Converts remaining simplified-Chinese skill presentation labels to Traditional Chinese.
-- Game function: skill animation / presentation category readability only.

UPDATE god2.skill_content_profiles
SET
    AnimationKey = REPLACE(
        REPLACE(
        REPLACE(
        REPLACE(
        REPLACE(
        REPLACE(
        REPLACE(
        REPLACE(AnimationKey,
            '档案标识符', '檔案標識符'),
            '一般动作', '一般動作'),
            '召换动作', '召喚動作'),
            '室内动作', '室內動作'),
            '其他骑乘动作', '其他騎乘動作'),
            '马类骑乘动作', '馬類騎乘動作'),
            '动作', '動作'),
            '骑乘', '騎乘'),
    PresentationKey = REPLACE(
        REPLACE(
        REPLACE(
        REPLACE(
        REPLACE(
        REPLACE(
        REPLACE(
        REPLACE(PresentationKey,
            '档案标识符', '檔案標識符'),
            '一般动作', '一般動作'),
            '召换动作', '召喚動作'),
            '室内动作', '室內動作'),
            '其他骑乘动作', '其他騎乘動作'),
            '马类骑乘动作', '馬類騎乘動作'),
            '动作', '動作'),
            '骑乘', '騎乘')
WHERE AnimationKey REGEXP '档案标识符|一般动作|召换动作|室内动作|其他骑乘动作|马类骑乘动作|动作|骑乘'
   OR PresentationKey REGEXP '档案标识符|一般动作|召换动作|室内动作|其他骑乘动作|马类骑乘动作|动作|骑乘';
