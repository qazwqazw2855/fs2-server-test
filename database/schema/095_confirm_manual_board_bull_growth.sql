-- User-confirmed board-bull late-breakthrough allocation: 5 strength, 4 constitution, 1 speed.

UPDATE `god2_game`.`pet_automatic_growth_allocations`
SET `constitution_delta`=4,`strength_delta`=5,`intelligence_delta`=0,`speed_delta`=1,
    `source_value_zh_tw`='5力4體1速',`evidence_status`='UserConfirmed',`enabled`=1,
    `source_reference_zh_tw`='服主手動確認：5力4體1速',
    `admin_note`='服主確認板牛類晚破50至99級每級自動配點。'
WHERE `growth_archetype_id`=18 AND `growth_grade`='LateBreakthrough' AND `minimum_level`=50;
