START TRANSACTION;

UPDATE `god2_game`.`item_sets`
SET `admin_note` = '套裝關係與客戶端顯示的加成數值已驗證；實際啟用條件與屬性套用仍因缺少服務端實證而維持阻擋。'
WHERE `admin_note` = 'Set relationship and exact displayed bonus values are verified; activation and stat mutation remain evidence-blocked.';

UPDATE `god2_game`.`item_set_bonuses`
SET `admin_note` = '目前只驗證客戶端顯示數值；實際啟用條件與屬性變動仍因缺少服務端實證而維持阻擋。'
WHERE `admin_note` = 'Verified exact current-client display value only; activation and stat mutation remain evidence-blocked.';

UPDATE `god2_game`.`merchants`
SET `admin_note` = '已驗證地圖 19 雜貨 NPC 互動設定，可處理 NPC 代號 1504 與 4638。'
WHERE `admin_note` = 'Enabled for the verified Map 19 grocery NPC interaction profile (handles 1504 and 4638).';

UPDATE `god2_game`.`merchants`
SET `admin_note` = '第 4-6 階段實機開啟；買入與賣出交易流程可用。'
WHERE `admin_note` = 'Stage 4-6 live open, buy and sell transaction path.';

UPDATE `god2_game`.`life_skills`
SET `admin_note` = '固定生活技能基礎資料。'
WHERE `admin_note` = '固定生活技能 Seed';

COMMIT;
