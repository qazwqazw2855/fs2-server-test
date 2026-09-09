START TRANSACTION;

UPDATE `god2_game`.`pet_templates` template_row
SET template_row.`code` = CONCAT('pet_template_', template_row.`pet_template_id`)
WHERE template_row.`code` IS NULL
   OR template_row.`code` = '';

COMMIT;
