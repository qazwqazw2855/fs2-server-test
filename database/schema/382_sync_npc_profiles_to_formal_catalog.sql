INSERT INTO `god2_game`.`npcs`
    (`npc_id`,`code`,`name_zh_tw`,`npc_type`,`resource_key`,`interaction_family`,`enabled`)
SELECT
    npc_row.`Id`,
    LEFT(NULLIF(npc_row.`Code`, ''), 100),
    LEFT(COALESCE(NULLIF(npc_row.`NameZhTw`, ''), NULLIF(npc_row.`Name`, ''), CONCAT('Npc ', npc_row.`Id`)), 150),
    LEFT(COALESCE(NULLIF(npc_row.`NpcType`, ''), '一般'), 50),
    identity_row.`ResourceKey`,
    npc_row.`InteractionFamily`,
    npc_row.`ProductionSpawnEnabled`
FROM `god2`.`npcs` npc_row
LEFT JOIN
(
    SELECT
        `NpcId`,
        MAX(NULLIF(`ResourceKey`, '')) AS `ResourceKey`
    FROM `god2`.`npc_client_identities`
    GROUP BY `NpcId`
) identity_row
  ON identity_row.`NpcId` = npc_row.`Id`
ON DUPLICATE KEY UPDATE
    `code`=VALUES(`code`),
    `name_zh_tw`=VALUES(`name_zh_tw`),
    `npc_type`=VALUES(`npc_type`),
    `resource_key`=COALESCE(VALUES(`resource_key`), `resource_key`),
    `interaction_family`=COALESCE(VALUES(`interaction_family`), `interaction_family`),
    `enabled`=CASE WHEN `god2_game`.`npcs`.`enabled` = 1 OR VALUES(`enabled`) = 1 THEN 1 ELSE 0 END,
    `updated_at_utc`=UTC_TIMESTAMP(6);
