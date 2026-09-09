START TRANSACTION;

UPDATE `god2_game`.`merchants`
SET `admin_note` = '商店尚無通過證據閘門的正式庫存，已安全停用。'
WHERE `admin_note` = '商店尚無通過 Gate 的正式庫存，已安全停用。';

UPDATE `god2_game`.`pet_template_skills`
SET `admin_note` = '一般戰寵維基技能階級與官方客戶端完整繁中技能名的精確對應；效果公式仍由技能啟用旗標阻擋。'
WHERE `admin_note` = '一般戰寵Wiki技能階級與官方客戶端完整繁中技能名的精確對應；效果公式仍由skills.enabled阻擋。';

COMMIT;
