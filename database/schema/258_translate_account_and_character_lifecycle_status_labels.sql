-- 258_translate_account_and_character_lifecycle_status_labels.sql
-- Purpose: make formal account and character lifecycle statuses readable in Traditional Chinese.

UPDATE `god2_player`.`accounts`
SET `status` = CASE `status`
    WHEN 'Active' THEN '啟用'
    WHEN 'Disabled' THEN '停用'
    WHEN 'Locked' THEN '鎖定'
    ELSE `status`
END
WHERE `status` IN ('Active','Disabled','Locked');

UPDATE `god2_player`.`characters`
SET `status` = CASE `status`
    WHEN 'Active' THEN '啟用'
    WHEN 'Deleted' THEN '已刪除'
    ELSE `status`
END
WHERE `status` IN ('Active','Deleted');
