ALTER TABLE `content_client_table_layouts`
    DROP PRIMARY KEY,
    ADD PRIMARY KEY (`RunId`, `LayoutId`);

ALTER TABLE `content_field_evidence`
    DROP PRIMARY KEY,
    ADD PRIMARY KEY (`RunId`, `EvidenceId`);

ALTER TABLE `equipment_set_members`
    ADD COLUMN IF NOT EXISTS `RunId` char(36) NULL AFTER `ItemId`;

UPDATE `equipment_set_members` m
INNER JOIN `equipment_set_definitions` d ON d.`SetId`=m.`SetId`
SET m.`RunId`=d.`RunId`
WHERE m.`RunId` IS NULL;

ALTER TABLE `equipment_set_members`
    MODIFY COLUMN `RunId` char(36) NOT NULL,
    ADD KEY `IX_EquipmentSetMembers_Run` (`RunId`),
    ADD CONSTRAINT `FK_EquipmentSetMembers_Run`
        FOREIGN KEY (`RunId`) REFERENCES `content_recovery_runs` (`RunId`);

ALTER TABLE `pet_innate_definitions`
    ADD COLUMN IF NOT EXISTS `ArtifactNameZhTw` varchar(256) NULL AFTER `NameZhTw`;
