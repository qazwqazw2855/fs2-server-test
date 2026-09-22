-- Repair client map resource provenance omitted by the forged canonical seed.
-- This migration restores only evidence-backed static/staging data.
-- It never updates god2_game.maps, god2_game.portals or player state.

CREATE TEMPORARY TABLE `god2_client_resource_repair_guard` (
    `ready` tinyint NOT NULL,
    CONSTRAINT `ck_god2_client_resource_repair_guard`
        CHECK (`ready` = 1)
);

INSERT INTO `god2_client_resource_repair_guard` (`ready`)
SELECT IF(
    (
        SELECT COUNT(*)
        FROM `god2`.`__schemaversion`
        WHERE (`Version`='114'
               AND `Name`='114_publish_official_map_catalog.sql'
               AND `Checksum`='bf6f02191dd2313f95c366eb96cd81135fe5b783d7167502131fefd01826881e')
           OR (`Version`='115'
               AND `Name`='115_publish_official_portal_resource_links.sql'
               AND `Checksum`='1a9f11e5d219877e3f01589695262c05e78b361c0c1023343f7ab4c04e6c436a')
           OR (`Version`='179'
               AND `Name`='179_split_client_resource_link_evidence.sql'
               AND `Checksum`='635e7f4810560c48da7f68652af5bde0d07cf9f634269674ae998647c500e2b9')
           OR (`Version`='226'
               AND `Name`='226_archive_client_map_resource_file_evidence.sql'
               AND `Checksum`='8c7a418ab422b513c68e2652788958a775c8697454af8088d30e268cbbacfcf7')
    ) = 4
    AND (
        (
            (SELECT COUNT(*) FROM `god2_game`.`client_map_resources`) = 0
            AND (SELECT COUNT(*) FROM `god2_game`.`client_map_resource_identities`) = 0
            AND (SELECT COUNT(*) FROM `god2_game`.`portal_resource_links`) = 0
        )
        OR
        (
            (SELECT COUNT(*) FROM `god2_game`.`client_map_resources`) = 158
            AND (SELECT COUNT(*) FROM `god2_game`.`client_map_resource_identities`) = 144
            AND (SELECT COUNT(*) FROM `god2_game`.`portal_resource_links`) = 65
        )
    ),
    1,
    0
);

DROP TEMPORARY TABLE `god2_client_resource_repair_guard`;

CREATE DATABASE IF NOT EXISTS `god2_research`
    CHARACTER SET utf8mb4
    COLLATE utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `god2_research`.`client_map_resource_evidence` (
    `resource_key` varchar(512) NOT NULL,
    `client_executable_sha256` char(64) NULL,
    `can_record_index` int NULL,
    `can_record_type` tinyint unsigned NULL,
    `can_relative_path` varchar(512) NULL,
    `can_sha256` char(64) NULL,
    `mbd_relative_path` varchar(512) NULL,
    `mbd_sha256` char(64) NULL,
    `resource_file_relative_path` varchar(512) NULL,
    `resource_file_sha256` char(64) NULL,
    `navigation_relative_path` varchar(512) NULL,
    `navigation_sha256` char(64) NULL,
    `resource_evidence_status` varchar(30) NOT NULL,
    `map_identity_evidence_status` varchar(30) NOT NULL,
    `portal_placement_evidence_status` varchar(30) NOT NULL,
    `navigation_evidence_status` varchar(30) NULL,
    `admin_note` varchar(500) NULL,
    `moved_at_utc` datetime(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
    PRIMARY KEY (`resource_key`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `god2_research`.`client_map_resource_identity_evidence` (
    `map_identity_id` bigint NOT NULL,
    `source_row` int NOT NULL,
    `source_sha256` char(64) NOT NULL,
    `identity_evidence_status` varchar(30) NOT NULL,
    `coordinate_evidence_status` varchar(30) NOT NULL,
    `moved_at_utc` datetime(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
    PRIMARY KEY (`map_identity_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `god2_research`.`portal_resource_link_evidence` (
    `portal_link_id` varchar(191) NOT NULL,
    `can_relative_path` varchar(512) NOT NULL,
    `can_record_index` int NOT NULL,
    `can_record_type` tinyint unsigned NOT NULL,
    `source_sha256` char(64) NOT NULL,
    `identity_evidence_status` varchar(30) NOT NULL,
    `source_coordinate_evidence_status` varchar(30) NOT NULL,
    `destination_coordinate_evidence_status` varchar(30) NOT NULL,
    `trigger_evidence_status` varchar(30) NOT NULL,
    `moved_at_utc` datetime(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
    PRIMARY KEY (`portal_link_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TEMPORARY TABLE `god2_map_identity_stage` (
    `map_identity_id` bigint NOT NULL,
    `client_build_id` varchar(64) NOT NULL,
    `client_area_id` int NOT NULL,
    `client_map_id` int NOT NULL,
    `map_id` bigint NOT NULL,
    `resource_key` varchar(512) NOT NULL,
    `world_map_x` int NOT NULL,
    `world_map_y` int NOT NULL,
    `source_row` int NOT NULL,
    `source_sha256` char(64) NOT NULL,
    `identity_evidence_status` varchar(30) NOT NULL,
    `coordinate_evidence_status` varchar(30) NOT NULL,
    `source_enabled` tinyint NOT NULL,
    PRIMARY KEY (`map_identity_id`)
) ENGINE=InnoDB;

INSERT INTO `god2_map_identity_stage`
VALUES
    (1200010000,'god2-opt-6b127086e0c0',1,0,1200010000,'client:map/south001/south001',777,945,22727,'c3bc7ebe8b2e7ea346c376dee25b3b07f90e82208d14f8e26f5feded18bc1e1f','Verified','Derived',1),
    (1200010012,'god2-opt-6b127086e0c0',1,12,1200010012,'client:map/south001/citys01/citys01',230,211,22728,'c3bc7ebe8b2e7ea346c376dee25b3b07f90e82208d14f8e26f5feded18bc1e1f','Verified','Derived',1),
    (1200010081,'god2-opt-6b127086e0c0',1,81,1200010081,'client:map/south001/citys03/citys03',277,685,22729,'c3bc7ebe8b2e7ea346c376dee25b3b07f90e82208d14f8e26f5feded18bc1e1f','Verified','Derived',1),
    (1200010071,'god2-opt-6b127086e0c0',1,71,1200010071,'client:map/south001/citys05/citys05',570,477,22730,'c3bc7ebe8b2e7ea346c376dee25b3b07f90e82208d14f8e26f5feded18bc1e1f','Verified','Derived',1),
    (1200010093,'god2-opt-6b127086e0c0',1,93,1200010093,'client:map/south001/citys10/citys10',230,231,22731,'c3bc7ebe8b2e7ea346c376dee25b3b07f90e82208d14f8e26f5feded18bc1e1f','Verified','Derived',1),
    (1200010080,'god2-opt-6b127086e0c0',1,80,1200010080,'client:map/south001/mazes03/mazes03',723,619,22732,'c3bc7ebe8b2e7ea346c376dee25b3b07f90e82208d14f8e26f5feded18bc1e1f','Verified','Derived',1),
    (1200010056,'god2-opt-6b127086e0c0',1,56,1200010056,'client:map/south001/roads01/roads01',604,215,22733,'c3bc7ebe8b2e7ea346c376dee25b3b07f90e82208d14f8e26f5feded18bc1e1f','Verified','Derived',1),
    (1200010064,'god2-opt-6b127086e0c0',1,64,1200010064,'client:map/south001/roads02/roads02',628,401,22734,'c3bc7ebe8b2e7ea346c376dee25b3b07f90e82208d14f8e26f5feded18bc1e1f','Verified','Derived',1),
    (1200010079,'god2-opt-6b127086e0c0',1,79,1200010079,'client:map/south001/roads03/roads03',728,610,22735,'c3bc7ebe8b2e7ea346c376dee25b3b07f90e82208d14f8e26f5feded18bc1e1f','Verified','Derived',1),
    (1200050000,'god2-opt-6b127086e0c0',5,0,1200050000,'client:map/south002/south002',903,609,22736,'c3bc7ebe8b2e7ea346c376dee25b3b07f90e82208d14f8e26f5feded18bc1e1f','Verified','Derived',1),
    (1200050027,'god2-opt-6b127086e0c0',5,27,1200050027,'client:map/south002/citys02/citys02',695,185,22737,'c3bc7ebe8b2e7ea346c376dee25b3b07f90e82208d14f8e26f5feded18bc1e1f','Verified','Derived',1),
    (1200050040,'god2-opt-6b127086e0c0',5,40,1200050040,'client:map/south002/mazes05/mazes05',605,387,22738,'c3bc7ebe8b2e7ea346c376dee25b3b07f90e82208d14f8e26f5feded18bc1e1f','Verified','Derived',1),
    (1200050051,'god2-opt-6b127086e0c0',5,51,1200050051,'client:map/south002/citys09/citys09',284,331,22739,'c3bc7ebe8b2e7ea346c376dee25b3b07f90e82208d14f8e26f5feded18bc1e1f','Verified','Derived',1),
    (1200050003,'god2-opt-6b127086e0c0',5,3,1200050003,'client:map/south002/4tree/4tree',690,125,22740,'c3bc7ebe8b2e7ea346c376dee25b3b07f90e82208d14f8e26f5feded18bc1e1f','Verified','Derived',1),
    (1200050002,'god2-opt-6b127086e0c0',5,2,1200050002,'client:map/south002/roads04/roads04',669,65,22741,'c3bc7ebe8b2e7ea346c376dee25b3b07f90e82208d14f8e26f5feded18bc1e1f','Verified','Derived',1),
    (1200050001,'god2-opt-6b127086e0c0',5,1,1200050001,'client:map/south002/roads05/roads05',472,76,22742,'c3bc7ebe8b2e7ea346c376dee25b3b07f90e82208d14f8e26f5feded18bc1e1f','Verified','Derived',1),
    (1200050028,'god2-opt-6b127086e0c0',5,28,1200050028,'client:map/south002/roads06/roads06',288,202,22743,'c3bc7ebe8b2e7ea346c376dee25b3b07f90e82208d14f8e26f5feded18bc1e1f','Verified','Derived',1),
    (1200050044,'god2-opt-6b127086e0c0',5,44,1200050044,'client:map/south002/mazes04/mazes04',769,116,22744,'c3bc7ebe8b2e7ea346c376dee25b3b07f90e82208d14f8e26f5feded18bc1e1f','Verified','Derived',1),
    (1200060000,'god2-opt-6b127086e0c0',6,0,1200060000,'client:map/south003/south003',630,882,22745,'c3bc7ebe8b2e7ea346c376dee25b3b07f90e82208d14f8e26f5feded18bc1e1f','Verified','Derived',1),
    (1200060003,'god2-opt-6b127086e0c0',6,3,1200060003,'client:map/south003/citys07/citys07',337,118,22746,'c3bc7ebe8b2e7ea346c376dee25b3b07f90e82208d14f8e26f5feded18bc1e1f','Verified','Derived',1),
    (1200060019,'god2-opt-6b127086e0c0',6,19,1200060019,'client:map/south003/citys08/citys08',437,758,22747,'c3bc7ebe8b2e7ea346c376dee25b3b07f90e82208d14f8e26f5feded18bc1e1f','Verified','Derived',1),
    (1200060002,'god2-opt-6b127086e0c0',6,2,1200060002,'client:map/south003/mazes01/mazes01',515,104,22748,'c3bc7ebe8b2e7ea346c376dee25b3b07f90e82208d14f8e26f5feded18bc1e1f','Verified','Derived',1),
    (1200060012,'god2-opt-6b127086e0c0',6,12,1200060012,'client:map/south003/mazes02/mazes02',383,207,22749,'c3bc7ebe8b2e7ea346c376dee25b3b07f90e82208d14f8e26f5feded18bc1e1f','Verified','Derived',1),
    (1200060026,'god2-opt-6b127086e0c0',6,26,1200060026,'client:map/south003/roads07/roads07',394,819,22750,'c3bc7ebe8b2e7ea346c376dee25b3b07f90e82208d14f8e26f5feded18bc1e1f','Verified','Derived',1),
    (1200080000,'god2-opt-6b127086e0c0',8,0,1200080000,'client:map/array/array',105,105,22751,'c3bc7ebe8b2e7ea346c376dee25b3b07f90e82208d14f8e26f5feded18bc1e1f','Verified','Derived',1),
    (1200080001,'god2-opt-6b127086e0c0',8,1,1200080001,'client:map/array/sroom/sroom',0,0,22752,'c3bc7ebe8b2e7ea346c376dee25b3b07f90e82208d14f8e26f5feded18bc1e1f','Verified','Derived',1),
    (1200080009,'god2-opt-6b127086e0c0',8,9,1200080009,'client:map/array/sroom02/sroom02',0,0,22753,'c3bc7ebe8b2e7ea346c376dee25b3b07f90e82208d14f8e26f5feded18bc1e1f','Verified','Derived',1),
    (1200080016,'god2-opt-6b127086e0c0',8,16,1200080016,'client:map/array/sroom03/sroom03',0,0,22754,'c3bc7ebe8b2e7ea346c376dee25b3b07f90e82208d14f8e26f5feded18bc1e1f','Verified','Derived',1),
    (1200080017,'god2-opt-6b127086e0c0',8,17,1200080017,'client:map/array/sroom04/sroom04',0,0,22755,'c3bc7ebe8b2e7ea346c376dee25b3b07f90e82208d14f8e26f5feded18bc1e1f','Verified','Derived',1),
    (1200080018,'god2-opt-6b127086e0c0',8,18,1200080018,'client:map/array/sroom05/sroom05',0,0,22756,'c3bc7ebe8b2e7ea346c376dee25b3b07f90e82208d14f8e26f5feded18bc1e1f','Verified','Derived',1),
    (1200080019,'god2-opt-6b127086e0c0',8,19,1200080019,'client:map/array/sroom06/sroom06',0,0,22757,'c3bc7ebe8b2e7ea346c376dee25b3b07f90e82208d14f8e26f5feded18bc1e1f','Verified','Derived',1),
    (1200080005,'god2-opt-6b127086e0c0',8,5,1200080005,'client:map/array/array01/array01',128,136,22758,'c3bc7ebe8b2e7ea346c376dee25b3b07f90e82208d14f8e26f5feded18bc1e1f','Verified','Derived',1),
    (1200080008,'god2-opt-6b127086e0c0',8,8,1200080008,'client:map/array/array02/array02',31,25,22759,'c3bc7ebe8b2e7ea346c376dee25b3b07f90e82208d14f8e26f5feded18bc1e1f','Verified','Derived',1),
    (1200080007,'god2-opt-6b127086e0c0',8,7,1200080007,'client:map/array/array03/array03',106,110,22760,'c3bc7ebe8b2e7ea346c376dee25b3b07f90e82208d14f8e26f5feded18bc1e1f','Verified','Derived',1),
    (1200080004,'god2-opt-6b127086e0c0',8,4,1200080004,'client:map/array/array04/array04',62,140,22761,'c3bc7ebe8b2e7ea346c376dee25b3b07f90e82208d14f8e26f5feded18bc1e1f','Verified','Derived',1),
    (1200080006,'god2-opt-6b127086e0c0',8,6,1200080006,'client:map/array/array05/array05',91,252,22762,'c3bc7ebe8b2e7ea346c376dee25b3b07f90e82208d14f8e26f5feded18bc1e1f','Verified','Derived',1),
    (1200080002,'god2-opt-6b127086e0c0',8,2,1200080002,'client:map/array/array06/array06',204,183,22763,'c3bc7ebe8b2e7ea346c376dee25b3b07f90e82208d14f8e26f5feded18bc1e1f','Verified','Derived',1),
    (1200080003,'god2-opt-6b127086e0c0',8,3,1200080003,'client:map/array/array07/array07',149,112,22764,'c3bc7ebe8b2e7ea346c376dee25b3b07f90e82208d14f8e26f5feded18bc1e1f','Verified','Derived',1),
    (1200080010,'god2-opt-6b127086e0c0',8,10,1200080010,'client:map/array/array08/array08',109,149,22765,'c3bc7ebe8b2e7ea346c376dee25b3b07f90e82208d14f8e26f5feded18bc1e1f','Verified','Derived',1),
    (1200080011,'god2-opt-6b127086e0c0',8,11,1200080011,'client:map/array/array09/array09',92,112,22766,'c3bc7ebe8b2e7ea346c376dee25b3b07f90e82208d14f8e26f5feded18bc1e1f','Verified','Derived',1),
    (1200080012,'god2-opt-6b127086e0c0',8,12,1200080012,'client:map/array/array10/array10',102,132,22767,'c3bc7ebe8b2e7ea346c376dee25b3b07f90e82208d14f8e26f5feded18bc1e1f','Verified','Derived',1),
    (1200080013,'god2-opt-6b127086e0c0',8,13,1200080013,'client:map/array/array11/array11',87,414,22768,'c3bc7ebe8b2e7ea346c376dee25b3b07f90e82208d14f8e26f5feded18bc1e1f','Verified','Derived',1),
    (1200080014,'god2-opt-6b127086e0c0',8,14,1200080014,'client:map/array/array12/array12',122,32,22769,'c3bc7ebe8b2e7ea346c376dee25b3b07f90e82208d14f8e26f5feded18bc1e1f','Verified','Derived',1),
    (1200080015,'god2-opt-6b127086e0c0',8,15,1200080015,'client:map/array/array13/array13',123,82,22770,'c3bc7ebe8b2e7ea346c376dee25b3b07f90e82208d14f8e26f5feded18bc1e1f','Verified','Derived',1),
    (1200070000,'god2-opt-6b127086e0c0',7,0,1200070000,'client:map/tong/tong',105,105,22771,'c3bc7ebe8b2e7ea346c376dee25b3b07f90e82208d14f8e26f5feded18bc1e1f','Verified','Derived',1),
    (1200070006,'god2-opt-6b127086e0c0',7,6,1200070006,'client:map/tong/tong1/tong1',70,57,22772,'c3bc7ebe8b2e7ea346c376dee25b3b07f90e82208d14f8e26f5feded18bc1e1f','Verified','Derived',1),
    (1200070005,'god2-opt-6b127086e0c0',7,5,1200070005,'client:map/tong/tong2/tong2',73,57,22773,'c3bc7ebe8b2e7ea346c376dee25b3b07f90e82208d14f8e26f5feded18bc1e1f','Verified','Derived',1),
    (1200070004,'god2-opt-6b127086e0c0',7,4,1200070004,'client:map/tong/tong3/tong3',76,57,22774,'c3bc7ebe8b2e7ea346c376dee25b3b07f90e82208d14f8e26f5feded18bc1e1f','Verified','Derived',1),
    (1200070002,'god2-opt-6b127086e0c0',7,2,1200070002,'client:map/tong/tong4/tong4',79,57,22775,'c3bc7ebe8b2e7ea346c376dee25b3b07f90e82208d14f8e26f5feded18bc1e1f','Verified','Derived',1),
    (1200070001,'god2-opt-6b127086e0c0',7,1,1200070001,'client:map/tong/tower3/tower3',75,45,22776,'c3bc7ebe8b2e7ea346c376dee25b3b07f90e82208d14f8e26f5feded18bc1e1f','Verified','Derived',1),
    (1200070003,'god2-opt-6b127086e0c0',7,3,1200070003,'client:map/tong/indoor/towertop',20,9,22777,'c3bc7ebe8b2e7ea346c376dee25b3b07f90e82208d14f8e26f5feded18bc1e1f','Verified','Derived',1),
    (1200070007,'god2-opt-6b127086e0c0',7,7,1200070007,'client:map/tong/tongwar1/tongwar1',73,61,22778,'c3bc7ebe8b2e7ea346c376dee25b3b07f90e82208d14f8e26f5feded18bc1e1f','Verified','Derived',1),
    (1200070008,'god2-opt-6b127086e0c0',7,8,1200070008,'client:map/tong/reborn/reborn',70,61,22779,'c3bc7ebe8b2e7ea346c376dee25b3b07f90e82208d14f8e26f5feded18bc1e1f','Verified','EvidenceBlocked',0),
    (1200070012,'god2-opt-6b127086e0c0',7,12,1200070012,'client:map/tong/mazeto01/mazeto01',70,66,22780,'c3bc7ebe8b2e7ea346c376dee25b3b07f90e82208d14f8e26f5feded18bc1e1f','Verified','Derived',1),
    (1200070011,'god2-opt-6b127086e0c0',7,11,1200070011,'client:map/tong/mazeto02/mazeto02',75,65,22781,'c3bc7ebe8b2e7ea346c376dee25b3b07f90e82208d14f8e26f5feded18bc1e1f','Verified','Derived',1),
    (1200070010,'god2-opt-6b127086e0c0',7,10,1200070010,'client:map/tong/mazeto03/mazeto03',73,66,22782,'c3bc7ebe8b2e7ea346c376dee25b3b07f90e82208d14f8e26f5feded18bc1e1f','Verified','Derived',1),
    (1200070009,'god2-opt-6b127086e0c0',7,9,1200070009,'client:map/tong/mazeto04/mazeto04',70,66,22783,'c3bc7ebe8b2e7ea346c376dee25b3b07f90e82208d14f8e26f5feded18bc1e1f','Verified','Derived',1),
    (1200070013,'god2-opt-6b127086e0c0',7,13,1200070013,'client:map/tong/tonggem/tonggem',70,64,22784,'c3bc7ebe8b2e7ea346c376dee25b3b07f90e82208d14f8e26f5feded18bc1e1f','Verified','Derived',1),
    (130139698,'god2-opt-6b127086e0c0',2,0,130139698,'client:map/island01/island01',735,777,22785,'c3bc7ebe8b2e7ea346c376dee25b3b07f90e82208d14f8e26f5feded18bc1e1f','Verified','Derived',1),
    (1200020002,'god2-opt-6b127086e0c0',2,2,1200020002,'client:map/island01/cave02/cave02',394,96,22786,'c3bc7ebe8b2e7ea346c376dee25b3b07f90e82208d14f8e26f5feded18bc1e1f','Verified','Derived',1),
    (1200020108,'god2-opt-6b127086e0c0',2,108,1200020108,'client:map/island01/cave03/cave03',641,456,22787,'c3bc7ebe8b2e7ea346c376dee25b3b07f90e82208d14f8e26f5feded18bc1e1f','Verified','Derived',1),
    (1200020032,'god2-opt-6b127086e0c0',2,32,1200020032,'client:map/island01/cave04/cave04',119,287,22788,'c3bc7ebe8b2e7ea346c376dee25b3b07f90e82208d14f8e26f5feded18bc1e1f','Verified','Derived',1),
    (1200020004,'god2-opt-6b127086e0c0',2,4,1200020004,'client:map/island01/cityi1/cityi1',477,209,22789,'c3bc7ebe8b2e7ea346c376dee25b3b07f90e82208d14f8e26f5feded18bc1e1f','Verified','Derived',1),
    (192354557,'god2-opt-6b127086e0c0',2,43,192354557,'client:map/island01/maze05/maze05',303,429,22790,'c3bc7ebe8b2e7ea346c376dee25b3b07f90e82208d14f8e26f5feded18bc1e1f','Verified','Derived',1),
    (1200030000,'god2-opt-6b127086e0c0',3,0,1200030000,'client:map/island02/island02',735,483,22791,'c3bc7ebe8b2e7ea346c376dee25b3b07f90e82208d14f8e26f5feded18bc1e1f','Verified','Derived',1),
    (1200030003,'god2-opt-6b127086e0c0',3,3,1200030003,'client:map/island02/jail00/jail00',409,197,22792,'c3bc7ebe8b2e7ea346c376dee25b3b07f90e82208d14f8e26f5feded18bc1e1f','Verified','Derived',1),
    (1200030005,'god2-opt-6b127086e0c0',3,5,1200030005,'client:map/island02/cave05/cave05',674,286,22793,'c3bc7ebe8b2e7ea346c376dee25b3b07f90e82208d14f8e26f5feded18bc1e1f','Verified','Derived',1),
    (1200030007,'god2-opt-6b127086e0c0',3,7,1200030007,'client:map/island02/cave06/cave06',397,422,22794,'c3bc7ebe8b2e7ea346c376dee25b3b07f90e82208d14f8e26f5feded18bc1e1f','Verified','Derived',1),
    (1200030002,'god2-opt-6b127086e0c0',3,2,1200030002,'client:map/island02/cave07/cave07',174,153,22795,'c3bc7ebe8b2e7ea346c376dee25b3b07f90e82208d14f8e26f5feded18bc1e1f','Verified','Derived',1),
    (1200030001,'god2-opt-6b127086e0c0',3,1,1200030001,'client:map/island02/cave11/cave11',221,60,22796,'c3bc7ebe8b2e7ea346c376dee25b3b07f90e82208d14f8e26f5feded18bc1e1f','Verified','Derived',1),
    (1200030010,'god2-opt-6b127086e0c0',3,10,1200030010,'client:map/island02/cityi3/cityi3',551,317,22797,'c3bc7ebe8b2e7ea346c376dee25b3b07f90e82208d14f8e26f5feded18bc1e1f','Verified','Derived',1),
    (1200040000,'god2-opt-6b127086e0c0',4,0,1200040000,'client:map/island03/island03',420,1092,22798,'c3bc7ebe8b2e7ea346c376dee25b3b07f90e82208d14f8e26f5feded18bc1e1f','Verified','Derived',1),
    (1200040012,'god2-opt-6b127086e0c0',4,12,1200040012,'client:map/island03/cave01/cave01',267,637,22799,'c3bc7ebe8b2e7ea346c376dee25b3b07f90e82208d14f8e26f5feded18bc1e1f','Verified','Derived',1),
    (1200040011,'god2-opt-6b127086e0c0',4,11,1200040011,'client:map/island03/cave08/cave08',203,406,22800,'c3bc7ebe8b2e7ea346c376dee25b3b07f90e82208d14f8e26f5feded18bc1e1f','Verified','Derived',1),
    (1200040002,'god2-opt-6b127086e0c0',4,2,1200040002,'client:map/island03/cave09/cave09',91,187,22801,'c3bc7ebe8b2e7ea346c376dee25b3b07f90e82208d14f8e26f5feded18bc1e1f','Verified','Derived',1),
    (1200040013,'god2-opt-6b127086e0c0',4,13,1200040013,'client:map/island03/cave10/cave10',252,744,22802,'c3bc7ebe8b2e7ea346c376dee25b3b07f90e82208d14f8e26f5feded18bc1e1f','Verified','Derived',1),
    (1200040001,'god2-opt-6b127086e0c0',4,1,1200040001,'client:map/island03/cave12/cave12',193,751,22803,'c3bc7ebe8b2e7ea346c376dee25b3b07f90e82208d14f8e26f5feded18bc1e1f','Verified','Derived',1),
    (1675308248,'god2-opt-6b127086e0c0',4,3,1675308248,'client:map/island03/cityi2/cityi2',318,363,22804,'c3bc7ebe8b2e7ea346c376dee25b3b07f90e82208d14f8e26f5feded18bc1e1f','Verified','Derived',1),
    (1200040015,'god2-opt-6b127086e0c0',4,15,1200040015,'client:map/island03/cityi4/cityi4',373,704,22805,'c3bc7ebe8b2e7ea346c376dee25b3b07f90e82208d14f8e26f5feded18bc1e1f','Verified','Derived',1),
    (1200120000,'god2-opt-6b127086e0c0',12,0,1200120000,'client:map/gem/gem',105,105,22806,'c3bc7ebe8b2e7ea346c376dee25b3b07f90e82208d14f8e26f5feded18bc1e1f','Verified','Derived',1),
    (1200120001,'god2-opt-6b127086e0c0',12,1,1200120001,'client:map/gem/gem01/gem01',70,64,22807,'c3bc7ebe8b2e7ea346c376dee25b3b07f90e82208d14f8e26f5feded18bc1e1f','Verified','Derived',1),
    (1200120005,'god2-opt-6b127086e0c0',12,5,1200120005,'client:map/gem/gem02/gem02',73,64,22808,'c3bc7ebe8b2e7ea346c376dee25b3b07f90e82208d14f8e26f5feded18bc1e1f','Verified','Derived',1),
    (1200120004,'god2-opt-6b127086e0c0',12,4,1200120004,'client:map/gem/gem03/gem03',76,64,22809,'c3bc7ebe8b2e7ea346c376dee25b3b07f90e82208d14f8e26f5feded18bc1e1f','Verified','Derived',1),
    (1200120003,'god2-opt-6b127086e0c0',12,3,1200120003,'client:map/gem/gem04/gem04',79,64,22810,'c3bc7ebe8b2e7ea346c376dee25b3b07f90e82208d14f8e26f5feded18bc1e1f','Verified','Derived',1),
    (1200120016,'god2-opt-6b127086e0c0',12,16,1200120016,'client:map/gem/gem04a/gem04a',79,64,22811,'c3bc7ebe8b2e7ea346c376dee25b3b07f90e82208d14f8e26f5feded18bc1e1f','Verified','Derived',1),
    (1200120014,'god2-opt-6b127086e0c0',12,14,1200120014,'client:map/gem/gem04b/gem04b',79,64,22812,'c3bc7ebe8b2e7ea346c376dee25b3b07f90e82208d14f8e26f5feded18bc1e1f','Verified','Derived',1),
    (1200120012,'god2-opt-6b127086e0c0',12,12,1200120012,'client:map/gem/gem04c/gem04c',79,64,22813,'c3bc7ebe8b2e7ea346c376dee25b3b07f90e82208d14f8e26f5feded18bc1e1f','Verified','Derived',1),
    (1200120011,'god2-opt-6b127086e0c0',12,11,1200120011,'client:map/gem/gem04d/gem04d',79,64,22814,'c3bc7ebe8b2e7ea346c376dee25b3b07f90e82208d14f8e26f5feded18bc1e1f','Verified','Derived',1),
    (1200120002,'god2-opt-6b127086e0c0',12,2,1200120002,'client:map/gem/gem05/gem05',70,67,22815,'c3bc7ebe8b2e7ea346c376dee25b3b07f90e82208d14f8e26f5feded18bc1e1f','Verified','Derived',1),
    (1200120018,'god2-opt-6b127086e0c0',12,18,1200120018,'client:map/gem/gem05a/gem05a',70,67,22816,'c3bc7ebe8b2e7ea346c376dee25b3b07f90e82208d14f8e26f5feded18bc1e1f','Verified','Derived',1),
    (1200120017,'god2-opt-6b127086e0c0',12,17,1200120017,'client:map/gem/gem05b/gem05b',70,67,22817,'c3bc7ebe8b2e7ea346c376dee25b3b07f90e82208d14f8e26f5feded18bc1e1f','Verified','Derived',1),
    (1200120015,'god2-opt-6b127086e0c0',12,15,1200120015,'client:map/gem/gem05c/gem05c',70,67,22818,'c3bc7ebe8b2e7ea346c376dee25b3b07f90e82208d14f8e26f5feded18bc1e1f','Verified','Derived',1),
    (1200120013,'god2-opt-6b127086e0c0',12,13,1200120013,'client:map/gem/gem05d/gem05d',70,67,22819,'c3bc7ebe8b2e7ea346c376dee25b3b07f90e82208d14f8e26f5feded18bc1e1f','Verified','Derived',1),
    (1200120007,'god2-opt-6b127086e0c0',12,7,1200120007,'client:map/gem/gem06/gem06',73,67,22820,'c3bc7ebe8b2e7ea346c376dee25b3b07f90e82208d14f8e26f5feded18bc1e1f','Verified','Derived',1),
    (1200120006,'god2-opt-6b127086e0c0',12,6,1200120006,'client:map/gem/gem07/gem07',76,67,22821,'c3bc7ebe8b2e7ea346c376dee25b3b07f90e82208d14f8e26f5feded18bc1e1f','Verified','Derived',1),
    (1200120008,'god2-opt-6b127086e0c0',12,8,1200120008,'client:map/gem/gem08/gem08',79,67,22822,'c3bc7ebe8b2e7ea346c376dee25b3b07f90e82208d14f8e26f5feded18bc1e1f','Verified','Derived',1),
    (1200120010,'god2-opt-6b127086e0c0',12,10,1200120010,'client:map/gem/gem09/gem09',70,70,22823,'c3bc7ebe8b2e7ea346c376dee25b3b07f90e82208d14f8e26f5feded18bc1e1f','Verified','Derived',1),
    (1200120009,'god2-opt-6b127086e0c0',12,9,1200120009,'client:map/gem/gem10/gem10',73,70,22824,'c3bc7ebe8b2e7ea346c376dee25b3b07f90e82208d14f8e26f5feded18bc1e1f','Verified','Derived',1),
    (1200120023,'god2-opt-6b127086e0c0',12,23,1200120023,'client:map/gem/gem01/gem01',70,64,22825,'c3bc7ebe8b2e7ea346c376dee25b3b07f90e82208d14f8e26f5feded18bc1e1f','Verified','Derived',1),
    (1200120021,'god2-opt-6b127086e0c0',12,21,1200120021,'client:map/gem/gem02/gem02',73,64,22826,'c3bc7ebe8b2e7ea346c376dee25b3b07f90e82208d14f8e26f5feded18bc1e1f','Verified','Derived',1),
    (1200120020,'god2-opt-6b127086e0c0',12,20,1200120020,'client:map/gem/gem03/gem03',76,64,22827,'c3bc7ebe8b2e7ea346c376dee25b3b07f90e82208d14f8e26f5feded18bc1e1f','Verified','Derived',1),
    (1200120019,'god2-opt-6b127086e0c0',12,19,1200120019,'client:map/gem/gem04/gem04',79,64,22828,'c3bc7ebe8b2e7ea346c376dee25b3b07f90e82208d14f8e26f5feded18bc1e1f','Verified','Derived',1),
    (1200120025,'god2-opt-6b127086e0c0',12,25,1200120025,'client:map/gem/gem05d/gem05d',70,67,22829,'c3bc7ebe8b2e7ea346c376dee25b3b07f90e82208d14f8e26f5feded18bc1e1f','Verified','Derived',1),
    (1200120024,'god2-opt-6b127086e0c0',12,24,1200120024,'client:map/gem/gem06/gem06',73,67,22830,'c3bc7ebe8b2e7ea346c376dee25b3b07f90e82208d14f8e26f5feded18bc1e1f','Verified','Derived',1),
    (1200120022,'god2-opt-6b127086e0c0',12,22,1200120022,'client:map/gem/gem07/gem07',76,67,22831,'c3bc7ebe8b2e7ea346c376dee25b3b07f90e82208d14f8e26f5feded18bc1e1f','Verified','Derived',1),
    (1200110000,'god2-opt-6b127086e0c0',11,0,1200110000,'client:map/north001/north001',504,504,22832,'c3bc7ebe8b2e7ea346c376dee25b3b07f90e82208d14f8e26f5feded18bc1e1f','Verified','Derived',1),
    (1200110022,'god2-opt-6b127086e0c0',11,22,1200110022,'client:map/north001/cityif01/cityif01',367,401,22833,'c3bc7ebe8b2e7ea346c376dee25b3b07f90e82208d14f8e26f5feded18bc1e1f','Verified','Derived',1),
    (1200110010,'god2-opt-6b127086e0c0',11,10,1200110010,'client:map/north001/cityif02/cityif02',453,284,22834,'c3bc7ebe8b2e7ea346c376dee25b3b07f90e82208d14f8e26f5feded18bc1e1f','Verified','Derived',1),
    (1200110009,'god2-opt-6b127086e0c0',11,9,1200110009,'client:map/north001/cityif03/cityif03',458,286,22835,'c3bc7ebe8b2e7ea346c376dee25b3b07f90e82208d14f8e26f5feded18bc1e1f','Verified','Derived',1),
    (1200110007,'god2-opt-6b127086e0c0',11,7,1200110007,'client:map/north001/jail01/jail01',458,281,22836,'c3bc7ebe8b2e7ea346c376dee25b3b07f90e82208d14f8e26f5feded18bc1e1f','Verified','Derived',1),
    (1200110006,'god2-opt-6b127086e0c0',11,6,1200110006,'client:map/north001/mazeif01/mazeif01',373,232,22837,'c3bc7ebe8b2e7ea346c376dee25b3b07f90e82208d14f8e26f5feded18bc1e1f','Verified','Derived',1),
    (1200110003,'god2-opt-6b127086e0c0',11,3,1200110003,'client:map/north001/mazeif02/mazeif02',216,121,22838,'c3bc7ebe8b2e7ea346c376dee25b3b07f90e82208d14f8e26f5feded18bc1e1f','Verified','Derived',1),
    (1200110004,'god2-opt-6b127086e0c0',11,4,1200110004,'client:map/north001/mazeif03/mazeif03',410,120,22839,'c3bc7ebe8b2e7ea346c376dee25b3b07f90e82208d14f8e26f5feded18bc1e1f','Verified','Derived',1),
    (1200110002,'god2-opt-6b127086e0c0',11,2,1200110002,'client:map/north001/mazeif04/mazeif04',250,74,22840,'c3bc7ebe8b2e7ea346c376dee25b3b07f90e82208d14f8e26f5feded18bc1e1f','Verified','Derived',1),
    (1200110008,'god2-opt-6b127086e0c0',11,8,1200110008,'client:map/north001/mazeif05/mazeif05',453,279,22841,'c3bc7ebe8b2e7ea346c376dee25b3b07f90e82208d14f8e26f5feded18bc1e1f','Verified','Derived',1),
    (1200110035,'god2-opt-6b127086e0c0',11,35,1200110035,'client:map/north001/citymy01/citymy01',458,300,22842,'c3bc7ebe8b2e7ea346c376dee25b3b07f90e82208d14f8e26f5feded18bc1e1f','Verified','Derived',1),
    (1200130000,'god2-opt-6b127086e0c0',13,0,1200130000,'client:map/north002/north002',504,504,22843,'c3bc7ebe8b2e7ea346c376dee25b3b07f90e82208d14f8e26f5feded18bc1e1f','Verified','Derived',1),
    (1200130003,'god2-opt-6b127086e0c0',13,3,1200130003,'client:map/north002/cityjiu01/cityjiu01',111,274,22844,'c3bc7ebe8b2e7ea346c376dee25b3b07f90e82208d14f8e26f5feded18bc1e1f','Verified','Derived',1),
    (1200130012,'god2-opt-6b127086e0c0',13,12,1200130012,'client:map/north002/cityjiu02/cityjiu02',418,403,22845,'c3bc7ebe8b2e7ea346c376dee25b3b07f90e82208d14f8e26f5feded18bc1e1f','Verified','Derived',1),
    (1200130002,'god2-opt-6b127086e0c0',13,2,1200130002,'client:map/north002/cityjiu03/cityjiu03',317,199,22846,'c3bc7ebe8b2e7ea346c376dee25b3b07f90e82208d14f8e26f5feded18bc1e1f','Verified','Derived',1),
    (1200130001,'god2-opt-6b127086e0c0',13,1,1200130001,'client:map/north002/mazejiu01/mazejiu01',443,52,22847,'c3bc7ebe8b2e7ea346c376dee25b3b07f90e82208d14f8e26f5feded18bc1e1f','Verified','Derived',1),
    (1200130004,'god2-opt-6b127086e0c0',13,4,1200130004,'client:map/north002/mazejiu02/mazejiu02',314,326,22848,'c3bc7ebe8b2e7ea346c376dee25b3b07f90e82208d14f8e26f5feded18bc1e1f','Verified','Derived',1),
    (1200130005,'god2-opt-6b127086e0c0',13,5,1200130005,'client:map/north002/mazejiu03/mazejiu03',399,321,22849,'c3bc7ebe8b2e7ea346c376dee25b3b07f90e82208d14f8e26f5feded18bc1e1f','Verified','Derived',1),
    (1200140000,'god2-opt-6b127086e0c0',14,0,1200140000,'client:map/north003/north003',504,504,22850,'c3bc7ebe8b2e7ea346c376dee25b3b07f90e82208d14f8e26f5feded18bc1e1f','Verified','Derived',1),
    (1200140021,'god2-opt-6b127086e0c0',14,21,1200140021,'client:map/north003/cityjiu04/cityjiu04',187,355,22851,'c3bc7ebe8b2e7ea346c376dee25b3b07f90e82208d14f8e26f5feded18bc1e1f','Verified','Derived',1),
    (1200140005,'god2-opt-6b127086e0c0',14,5,1200140005,'client:map/north003/cityjiu05/cityjiu05',237,182,22852,'c3bc7ebe8b2e7ea346c376dee25b3b07f90e82208d14f8e26f5feded18bc1e1f','Verified','Derived',1),
    (1200140001,'god2-opt-6b127086e0c0',14,1,1200140001,'client:map/north003/mazejiu04/mazejiu04',160,58,22853,'c3bc7ebe8b2e7ea346c376dee25b3b07f90e82208d14f8e26f5feded18bc1e1f','Verified','Derived',1),
    (1200140006,'god2-opt-6b127086e0c0',14,6,1200140006,'client:map/north003/mazejiu05/mazejiu05',368,253,22854,'c3bc7ebe8b2e7ea346c376dee25b3b07f90e82208d14f8e26f5feded18bc1e1f','Verified','Derived',1),
    (1200140022,'god2-opt-6b127086e0c0',14,22,1200140022,'client:map/north003/mazejiu06/mazejiu06',394,417,22855,'c3bc7ebe8b2e7ea346c376dee25b3b07f90e82208d14f8e26f5feded18bc1e1f','Verified','Derived',1),
    (170015000,'god2-opt-6b127086e0c0',15,0,170015000,'client:map/north004/north004',504,504,22856,'c3bc7ebe8b2e7ea346c376dee25b3b07f90e82208d14f8e26f5feded18bc1e1f','Verified','Derived',1),
    (170015007,'god2-opt-6b127086e0c0',15,7,170015007,'client:map/north004/citywei01/citywei01',249,246,22857,'c3bc7ebe8b2e7ea346c376dee25b3b07f90e82208d14f8e26f5feded18bc1e1f','Verified','Derived',1),
    (1200150008,'god2-opt-6b127086e0c0',15,8,1200150008,'client:map/north004/citywei02/citywei02',432,370,22858,'c3bc7ebe8b2e7ea346c376dee25b3b07f90e82208d14f8e26f5feded18bc1e1f','Verified','Derived',1),
    (1200150009,'god2-opt-6b127086e0c0',15,9,1200150009,'client:map/north004/citywei03/citywei03',353,412,22859,'c3bc7ebe8b2e7ea346c376dee25b3b07f90e82208d14f8e26f5feded18bc1e1f','Verified','Derived',1),
    (1200150006,'god2-opt-6b127086e0c0',15,6,1200150006,'client:map/north004/mazewei01/mazewei01',379,134,22860,'c3bc7ebe8b2e7ea346c376dee25b3b07f90e82208d14f8e26f5feded18bc1e1f','Verified','Derived',1),
    (1200150005,'god2-opt-6b127086e0c0',15,5,1200150005,'client:map/north004/mazewei02/mazewei02',43,44,22861,'c3bc7ebe8b2e7ea346c376dee25b3b07f90e82208d14f8e26f5feded18bc1e1f','Verified','Derived',1),
    (1200150004,'god2-opt-6b127086e0c0',15,4,1200150004,'client:map/north004/mazewei03/mazewei03',46,44,22862,'c3bc7ebe8b2e7ea346c376dee25b3b07f90e82208d14f8e26f5feded18bc1e1f','Verified','Derived',1),
    (1200150003,'god2-opt-6b127086e0c0',15,3,1200150003,'client:map/north004/mazewei04/mazewei04',49,44,22863,'c3bc7ebe8b2e7ea346c376dee25b3b07f90e82208d14f8e26f5feded18bc1e1f','Verified','Derived',1),
    (1200150002,'god2-opt-6b127086e0c0',15,2,1200150002,'client:map/north004/mazewei05/mazewei05',52,44,22864,'c3bc7ebe8b2e7ea346c376dee25b3b07f90e82208d14f8e26f5feded18bc1e1f','Verified','Derived',1),
    (1200150001,'god2-opt-6b127086e0c0',15,1,1200150001,'client:map/north004/mazewei06/mazewei06',55,44,22865,'c3bc7ebe8b2e7ea346c376dee25b3b07f90e82208d14f8e26f5feded18bc1e1f','Verified','Derived',1),
    (1200150030,'god2-opt-6b127086e0c0',15,30,1200150030,'client:map/north004/mazewei07/mazewei07',58,44,22866,'c3bc7ebe8b2e7ea346c376dee25b3b07f90e82208d14f8e26f5feded18bc1e1f','Verified','Derived',1),
    (1200160000,'god2-opt-6b127086e0c0',16,0,1200160000,'client:map/north005/north005',483,483,22867,'c3bc7ebe8b2e7ea346c376dee25b3b07f90e82208d14f8e26f5feded18bc1e1f','Verified','Derived',1),
    (1200160002,'god2-opt-6b127086e0c0',16,2,1200160002,'client:map/north005/cityhu01/cityhu01',295,145,22868,'c3bc7ebe8b2e7ea346c376dee25b3b07f90e82208d14f8e26f5feded18bc1e1f','Verified','Derived',1),
    (1200160007,'god2-opt-6b127086e0c0',16,7,1200160007,'client:map/north005/mazehu01/mazehu01',62,432,22869,'c3bc7ebe8b2e7ea346c376dee25b3b07f90e82208d14f8e26f5feded18bc1e1f','Verified','Derived',1),
    (1200160009,'god2-opt-6b127086e0c0',16,9,1200160009,'client:map/north005/umg/umg',154,134,22870,'c3bc7ebe8b2e7ea346c376dee25b3b07f90e82208d14f8e26f5feded18bc1e1f','Verified','Derived',1)
;

CREATE TEMPORARY TABLE `god2_portal_link_stage` (
    `portal_link_id` varchar(191) NOT NULL,
    `client_build_id` varchar(64) NOT NULL,
    `source_resource_key` varchar(512) NOT NULL,
    `destination_resource_key` varchar(512) NOT NULL,
    `source_map_id` bigint NOT NULL,
    `destination_map_id` bigint NULL,
    `can_relative_path` varchar(512) NOT NULL,
    `can_record_index` int NOT NULL,
    `can_record_type` tinyint unsigned NOT NULL,
    `source_sha256` char(64) NOT NULL,
    `identity_evidence_status` varchar(30) NOT NULL,
    `source_enabled` tinyint NOT NULL,
    PRIMARY KEY (`portal_link_id`)
) ENGINE=InnoDB;

INSERT INTO `god2_portal_link_stage`
VALUES
    ('client:can-link/island03/1','god2-opt-6b127086e0c0','client:map/island03/island03','client:map/island03/cave12/cave12',1200040000,1200040001,'Original/Data2/map/island03/Island03.can',1,2,'27ce439286bc921699d0067b5362714f083ce0b754939fa6891b2401a0e101d2','Derived',0),
    ('client:can-link/island03/2','god2-opt-6b127086e0c0','client:map/island03/island03','client:map/island03/cave09/cave09',1200040000,1200040002,'Original/Data2/map/island03/Island03.can',2,2,'27ce439286bc921699d0067b5362714f083ce0b754939fa6891b2401a0e101d2','Derived',0),
    ('client:can-link/island03/3','god2-opt-6b127086e0c0','client:map/island03/island03','client:map/island03/cityi2/cityi2',1200040000,1675308248,'Original/Data2/map/island03/Island03.can',3,2,'27ce439286bc921699d0067b5362714f083ce0b754939fa6891b2401a0e101d2','Derived',0),
    ('client:can-link/island03/4','god2-opt-6b127086e0c0','client:map/island03/island03','client:map/island03/indoor/konfu',1200040000,NULL,'Original/Data2/map/island03/Island03.can',4,1,'27ce439286bc921699d0067b5362714f083ce0b754939fa6891b2401a0e101d2','Candidate',0),
    ('client:can-link/island03/5','god2-opt-6b127086e0c0','client:map/island03/island03','client:map/island03/indoor/gr01',1200040000,NULL,'Original/Data2/map/island03/Island03.can',5,1,'27ce439286bc921699d0067b5362714f083ce0b754939fa6891b2401a0e101d2','Candidate',0),
    ('client:can-link/island03/6','god2-opt-6b127086e0c0','client:map/island03/island03','client:map/island03/indoor/gr02',1200040000,NULL,'Original/Data2/map/island03/Island03.can',6,1,'27ce439286bc921699d0067b5362714f083ce0b754939fa6891b2401a0e101d2','Candidate',0),
    ('client:can-link/island03/7','god2-opt-6b127086e0c0','client:map/island03/island03','client:map/island03/indoor/gr03',1200040000,NULL,'Original/Data2/map/island03/Island03.can',7,1,'27ce439286bc921699d0067b5362714f083ce0b754939fa6891b2401a0e101d2','Candidate',0),
    ('client:can-link/island03/8','god2-opt-6b127086e0c0','client:map/island03/island03','client:map/island03/indoor/gr04',1200040000,NULL,'Original/Data2/map/island03/Island03.can',8,1,'27ce439286bc921699d0067b5362714f083ce0b754939fa6891b2401a0e101d2','Candidate',0),
    ('client:can-link/island03/9','god2-opt-6b127086e0c0','client:map/island03/island03','client:map/island03/indoor/hofu',1200040000,NULL,'Original/Data2/map/island03/Island03.can',9,1,'27ce439286bc921699d0067b5362714f083ce0b754939fa6891b2401a0e101d2','Candidate',0),
    ('client:can-link/island03/10','god2-opt-6b127086e0c0','client:map/island03/island03','client:map/island03/indoor/god03',1200040000,NULL,'Original/Data2/map/island03/Island03.can',10,1,'27ce439286bc921699d0067b5362714f083ce0b754939fa6891b2401a0e101d2','Candidate',0),
    ('client:can-link/island03/11','god2-opt-6b127086e0c0','client:map/island03/island03','client:map/island03/cave08/cave08',1200040000,1200040011,'Original/Data2/map/island03/Island03.can',11,2,'27ce439286bc921699d0067b5362714f083ce0b754939fa6891b2401a0e101d2','Derived',0),
    ('client:can-link/island03/12','god2-opt-6b127086e0c0','client:map/island03/island03','client:map/island03/cave01/cave01',1200040000,1200040012,'Original/Data2/map/island03/Island03.can',12,2,'27ce439286bc921699d0067b5362714f083ce0b754939fa6891b2401a0e101d2','Derived',0),
    ('client:can-link/island03/13','god2-opt-6b127086e0c0','client:map/island03/island03','client:map/island03/cave10/cave10',1200040000,1200040013,'Original/Data2/map/island03/Island03.can',13,2,'27ce439286bc921699d0067b5362714f083ce0b754939fa6891b2401a0e101d2','Derived',0),
    ('client:can-link/island03/14','god2-opt-6b127086e0c0','client:map/island03/island03','client:map/island03/indoor/tp_nor_r',1200040000,NULL,'Original/Data2/map/island03/Island03.can',14,1,'27ce439286bc921699d0067b5362714f083ce0b754939fa6891b2401a0e101d2','Candidate',0),
    ('client:can-link/island03/15','god2-opt-6b127086e0c0','client:map/island03/island03','client:map/island03/cityi4/cityi4',1200040000,1200040015,'Original/Data2/map/island03/Island03.can',15,2,'27ce439286bc921699d0067b5362714f083ce0b754939fa6891b2401a0e101d2','Derived',0),
    ('client:can-link/array/1','god2-opt-6b127086e0c0','client:map/array/array','client:map/array/sroom/sroom',1200080000,1200080001,'Original/Data2/map/array/array.Can',1,2,'27ce439286bc921699d0067b5362714f083ce0b754939fa6891b2401a0e101d2','Derived',0),
    ('client:can-link/array/2','god2-opt-6b127086e0c0','client:map/array/array','client:map/array/array06/array06',1200080000,1200080002,'Original/Data2/map/array/array.Can',2,2,'27ce439286bc921699d0067b5362714f083ce0b754939fa6891b2401a0e101d2','Derived',0),
    ('client:can-link/array/3','god2-opt-6b127086e0c0','client:map/array/array','client:map/array/array07/array07',1200080000,1200080003,'Original/Data2/map/array/array.Can',3,2,'27ce439286bc921699d0067b5362714f083ce0b754939fa6891b2401a0e101d2','Derived',0),
    ('client:can-link/array/4','god2-opt-6b127086e0c0','client:map/array/array','client:map/array/array04/array04',1200080000,1200080004,'Original/Data2/map/array/array.Can',4,2,'27ce439286bc921699d0067b5362714f083ce0b754939fa6891b2401a0e101d2','Derived',0),
    ('client:can-link/array/5','god2-opt-6b127086e0c0','client:map/array/array','client:map/array/array01/array01',1200080000,1200080005,'Original/Data2/map/array/array.Can',5,2,'27ce439286bc921699d0067b5362714f083ce0b754939fa6891b2401a0e101d2','Derived',0),
    ('client:can-link/array/6','god2-opt-6b127086e0c0','client:map/array/array','client:map/array/array05/array05',1200080000,1200080006,'Original/Data2/map/array/array.Can',6,2,'27ce439286bc921699d0067b5362714f083ce0b754939fa6891b2401a0e101d2','Derived',0),
    ('client:can-link/array/7','god2-opt-6b127086e0c0','client:map/array/array','client:map/array/array03/array03',1200080000,1200080007,'Original/Data2/map/array/array.Can',7,2,'27ce439286bc921699d0067b5362714f083ce0b754939fa6891b2401a0e101d2','Derived',0),
    ('client:can-link/array/8','god2-opt-6b127086e0c0','client:map/array/array','client:map/array/array02/array02',1200080000,1200080008,'Original/Data2/map/array/array.Can',8,2,'27ce439286bc921699d0067b5362714f083ce0b754939fa6891b2401a0e101d2','Derived',0),
    ('client:can-link/array/9','god2-opt-6b127086e0c0','client:map/array/array','client:map/array/sroom02/sroom02',1200080000,1200080009,'Original/Data2/map/array/array.Can',9,2,'27ce439286bc921699d0067b5362714f083ce0b754939fa6891b2401a0e101d2','Derived',0),
    ('client:can-link/array/10','god2-opt-6b127086e0c0','client:map/array/array','client:map/array/array08/array08',1200080000,1200080010,'Original/Data2/map/array/array.Can',10,2,'27ce439286bc921699d0067b5362714f083ce0b754939fa6891b2401a0e101d2','Derived',0),
    ('client:can-link/array/11','god2-opt-6b127086e0c0','client:map/array/array','client:map/array/array09/array09',1200080000,1200080011,'Original/Data2/map/array/array.Can',11,2,'27ce439286bc921699d0067b5362714f083ce0b754939fa6891b2401a0e101d2','Derived',0),
    ('client:can-link/array/12','god2-opt-6b127086e0c0','client:map/array/array','client:map/array/array10/array10',1200080000,1200080012,'Original/Data2/map/array/array.Can',12,2,'27ce439286bc921699d0067b5362714f083ce0b754939fa6891b2401a0e101d2','Derived',0),
    ('client:can-link/array/13','god2-opt-6b127086e0c0','client:map/array/array','client:map/array/array11/array11',1200080000,1200080013,'Original/Data2/map/array/array.Can',13,2,'27ce439286bc921699d0067b5362714f083ce0b754939fa6891b2401a0e101d2','Derived',0),
    ('client:can-link/array/14','god2-opt-6b127086e0c0','client:map/array/array','client:map/array/array12/array12',1200080000,1200080014,'Original/Data2/map/array/array.Can',14,2,'27ce439286bc921699d0067b5362714f083ce0b754939fa6891b2401a0e101d2','Derived',0),
    ('client:can-link/array/15','god2-opt-6b127086e0c0','client:map/array/array','client:map/array/array13/array13',1200080000,1200080015,'Original/Data2/map/array/array.Can',15,2,'27ce439286bc921699d0067b5362714f083ce0b754939fa6891b2401a0e101d2','Derived',0),
    ('client:can-link/array/16','god2-opt-6b127086e0c0','client:map/array/array','client:map/array/sroom03/sroom03',1200080000,1200080016,'Original/Data2/map/array/array.Can',16,2,'27ce439286bc921699d0067b5362714f083ce0b754939fa6891b2401a0e101d2','Derived',0),
    ('client:can-link/array/17','god2-opt-6b127086e0c0','client:map/array/array','client:map/array/sroom04/sroom04',1200080000,1200080017,'Original/Data2/map/array/array.Can',17,2,'27ce439286bc921699d0067b5362714f083ce0b754939fa6891b2401a0e101d2','Derived',0),
    ('client:can-link/array/18','god2-opt-6b127086e0c0','client:map/array/array','client:map/array/sroom05/sroom05',1200080000,1200080018,'Original/Data2/map/array/array.Can',18,2,'27ce439286bc921699d0067b5362714f083ce0b754939fa6891b2401a0e101d2','Derived',0),
    ('client:can-link/array/19','god2-opt-6b127086e0c0','client:map/array/array','client:map/array/sroom06/sroom06',1200080000,1200080019,'Original/Data2/map/array/array.Can',19,2,'27ce439286bc921699d0067b5362714f083ce0b754939fa6891b2401a0e101d2','Derived',0),
    ('client:can-link/tong/1','god2-opt-6b127086e0c0','client:map/tong/tong','client:map/tong/tower3/tower3',1200070000,1200070001,'Original/Data2/map/tong/TONG.Can',1,2,'27ce439286bc921699d0067b5362714f083ce0b754939fa6891b2401a0e101d2','Derived',0),
    ('client:can-link/tong/2','god2-opt-6b127086e0c0','client:map/tong/tong','client:map/tong/tong4/tong4',1200070000,1200070002,'Original/Data2/map/tong/TONG.Can',2,2,'27ce439286bc921699d0067b5362714f083ce0b754939fa6891b2401a0e101d2','Derived',0),
    ('client:can-link/tong/3','god2-opt-6b127086e0c0','client:map/tong/tong','client:map/tong/indoor/towertop',1200070000,1200070003,'Original/Data2/map/tong/TONG.Can',3,1,'27ce439286bc921699d0067b5362714f083ce0b754939fa6891b2401a0e101d2','Derived',0),
    ('client:can-link/tong/4','god2-opt-6b127086e0c0','client:map/tong/tong','client:map/tong/tong3/tong3',1200070000,1200070004,'Original/Data2/map/tong/TONG.Can',4,2,'27ce439286bc921699d0067b5362714f083ce0b754939fa6891b2401a0e101d2','Derived',0),
    ('client:can-link/tong/5','god2-opt-6b127086e0c0','client:map/tong/tong','client:map/tong/tong2/tong2',1200070000,1200070005,'Original/Data2/map/tong/TONG.Can',5,2,'27ce439286bc921699d0067b5362714f083ce0b754939fa6891b2401a0e101d2','Derived',0),
    ('client:can-link/tong/6','god2-opt-6b127086e0c0','client:map/tong/tong','client:map/tong/tong1/tong1',1200070000,1200070006,'Original/Data2/map/tong/TONG.Can',6,2,'27ce439286bc921699d0067b5362714f083ce0b754939fa6891b2401a0e101d2','Derived',0),
    ('client:can-link/tong/7','god2-opt-6b127086e0c0','client:map/tong/tong','client:map/tong/tongwar1/tongwar1',1200070000,1200070007,'Original/Data2/map/tong/TONG.Can',7,2,'27ce439286bc921699d0067b5362714f083ce0b754939fa6891b2401a0e101d2','Derived',0),
    ('client:can-link/tong/8','god2-opt-6b127086e0c0','client:map/tong/tong','client:map/tong/reborn/reborn',1200070000,1200070008,'Original/Data2/map/tong/TONG.Can',8,2,'27ce439286bc921699d0067b5362714f083ce0b754939fa6891b2401a0e101d2','Derived',0),
    ('client:can-link/tong/9','god2-opt-6b127086e0c0','client:map/tong/tong','client:map/tong/mazeto04/mazeto04',1200070000,1200070009,'Original/Data2/map/tong/TONG.Can',9,2,'27ce439286bc921699d0067b5362714f083ce0b754939fa6891b2401a0e101d2','Derived',0),
    ('client:can-link/tong/10','god2-opt-6b127086e0c0','client:map/tong/tong','client:map/tong/mazeto03/mazeto03',1200070000,1200070010,'Original/Data2/map/tong/TONG.Can',10,2,'27ce439286bc921699d0067b5362714f083ce0b754939fa6891b2401a0e101d2','Derived',0),
    ('client:can-link/tong/11','god2-opt-6b127086e0c0','client:map/tong/tong','client:map/tong/mazeto02/mazeto02',1200070000,1200070011,'Original/Data2/map/tong/TONG.Can',11,2,'27ce439286bc921699d0067b5362714f083ce0b754939fa6891b2401a0e101d2','Derived',0),
    ('client:can-link/tong/12','god2-opt-6b127086e0c0','client:map/tong/tong','client:map/tong/mazeto01/mazeto01',1200070000,1200070012,'Original/Data2/map/tong/TONG.Can',12,2,'27ce439286bc921699d0067b5362714f083ce0b754939fa6891b2401a0e101d2','Derived',0),
    ('client:can-link/tong/13','god2-opt-6b127086e0c0','client:map/tong/tong','client:map/tong/tonggem/tonggem',1200070000,1200070013,'Original/Data2/map/tong/TONG.Can',13,2,'27ce439286bc921699d0067b5362714f083ce0b754939fa6891b2401a0e101d2','Derived',0),
    ('client:can-link/south003/1','god2-opt-6b127086e0c0','client:map/south003/south003','client:map/south003/indoor/shl01',1200060000,NULL,'Original/Data2/map/south003/South003.can',1,1,'27ce439286bc921699d0067b5362714f083ce0b754939fa6891b2401a0e101d2','Candidate',0),
    ('client:can-link/south003/2','god2-opt-6b127086e0c0','client:map/south003/south003','client:map/south003/mazes01/mazes01',1200060000,1200060002,'Original/Data2/map/south003/South003.can',2,2,'27ce439286bc921699d0067b5362714f083ce0b754939fa6891b2401a0e101d2','Derived',0),
    ('client:can-link/south003/3','god2-opt-6b127086e0c0','client:map/south003/south003','client:map/south003/citys07/citys07',1200060000,1200060003,'Original/Data2/map/south003/South003.can',3,2,'27ce439286bc921699d0067b5362714f083ce0b754939fa6891b2401a0e101d2','Derived',0),
    ('client:can-link/south003/4','god2-opt-6b127086e0c0','client:map/south003/south003','client:map/south003/indoor/shl02',1200060000,NULL,'Original/Data2/map/south003/South003.can',4,1,'27ce439286bc921699d0067b5362714f083ce0b754939fa6891b2401a0e101d2','Candidate',0),
    ('client:can-link/south003/5','god2-opt-6b127086e0c0','client:map/south003/south003','client:map/south003/indoor/mhl01',1200060000,NULL,'Original/Data2/map/south003/South003.can',5,1,'27ce439286bc921699d0067b5362714f083ce0b754939fa6891b2401a0e101d2','Candidate',0),
    ('client:can-link/south003/6','god2-opt-6b127086e0c0','client:map/south003/south003','client:map/south003/indoor/shr01',1200060000,NULL,'Original/Data2/map/south003/South003.can',6,1,'27ce439286bc921699d0067b5362714f083ce0b754939fa6891b2401a0e101d2','Candidate',0),
    ('client:can-link/south003/7','god2-opt-6b127086e0c0','client:map/south003/south003','client:map/south003/indoor/mhr01',1200060000,NULL,'Original/Data2/map/south003/South003.can',7,1,'27ce439286bc921699d0067b5362714f083ce0b754939fa6891b2401a0e101d2','Candidate',0),
    ('client:can-link/south003/8','god2-opt-6b127086e0c0','client:map/south003/south003','client:map/south003/indoor/mhl02',1200060000,NULL,'Original/Data2/map/south003/South003.can',8,1,'27ce439286bc921699d0067b5362714f083ce0b754939fa6891b2401a0e101d2','Candidate',0),
    ('client:can-link/south003/9','god2-opt-6b127086e0c0','client:map/south003/south003','client:map/south003/indoor/god01',1200060000,NULL,'Original/Data2/map/south003/South003.can',9,1,'27ce439286bc921699d0067b5362714f083ce0b754939fa6891b2401a0e101d2','Candidate',0),
    ('client:can-link/south003/10','god2-opt-6b127086e0c0','client:map/south003/south003','client:map/south003/mazes02/mazes02',1200060000,1200060012,'Original/Data2/map/south003/South003.can',10,2,'27ce439286bc921699d0067b5362714f083ce0b754939fa6891b2401a0e101d2','Derived',0),
    ('client:can-link/south003/11','god2-opt-6b127086e0c0','client:map/south003/south003','client:map/south003/indoor/god02',1200060000,NULL,'Original/Data2/map/south003/South003.can',11,1,'27ce439286bc921699d0067b5362714f083ce0b754939fa6891b2401a0e101d2','Candidate',0),
    ('client:can-link/south003/12','god2-opt-6b127086e0c0','client:map/south003/south003','client:map/south003/indoor/shr02',1200060000,NULL,'Original/Data2/map/south003/South003.can',12,1,'27ce439286bc921699d0067b5362714f083ce0b754939fa6891b2401a0e101d2','Candidate',0),
    ('client:can-link/south003/13','god2-opt-6b127086e0c0','client:map/south003/south003','client:map/south003/indoor/mhr02',1200060000,NULL,'Original/Data2/map/south003/South003.can',13,1,'27ce439286bc921699d0067b5362714f083ce0b754939fa6891b2401a0e101d2','Candidate',0),
    ('client:can-link/south003/14','god2-opt-6b127086e0c0','client:map/south003/south003','client:map/south003/indoor/god03',1200060000,NULL,'Original/Data2/map/south003/South003.can',14,1,'27ce439286bc921699d0067b5362714f083ce0b754939fa6891b2401a0e101d2','Candidate',0),
    ('client:can-link/south003/15','god2-opt-6b127086e0c0','client:map/south003/south003','client:map/south003/citys08/citys08',1200060000,1200060019,'Original/Data2/map/south003/South003.can',15,2,'27ce439286bc921699d0067b5362714f083ce0b754939fa6891b2401a0e101d2','Derived',0),
    ('client:can-link/south003/16','god2-opt-6b127086e0c0','client:map/south003/south003','client:map/south003/roads07/roads07',1200060000,1200060026,'Original/Data2/map/south003/South003.can',16,2,'27ce439286bc921699d0067b5362714f083ce0b754939fa6891b2401a0e101d2','Derived',0),
    ('client:can-link/south003/17','god2-opt-6b127086e0c0','client:map/south003/south003','client:map/south003/indoor/tp_nor_l',1200060000,NULL,'Original/Data2/map/south003/South003.can',17,1,'27ce439286bc921699d0067b5362714f083ce0b754939fa6891b2401a0e101d2','Candidate',0),
    ('client:can-link/south003/18','god2-opt-6b127086e0c0','client:map/south003/south003','client:map/south003/indoor/tp_nor_r',1200060000,NULL,'Original/Data2/map/south003/South003.can',18,1,'27ce439286bc921699d0067b5362714f083ce0b754939fa6891b2401a0e101d2','Candidate',0)
;

INSERT INTO `god2_game`.`client_map_resources`
    (`resource_key`,`area_code`,`resource_name`,
     `grid_width`,`grid_height`,`minimum_x`,`maximum_x`,`minimum_y`,`maximum_y`,
     `canonical_map_id`,`client_build_id`,`client_map_id`,`client_area_id`,
     `enabled`,`resource_format`,`navigation_format`)
SELECT
    identity_group.`resource_key`,
    SUBSTRING_INDEX(
        SUBSTRING(identity_group.`resource_key`,LENGTH('client:map/')+1),
        '/',1),
    map_row.`resource_identity`,
    map_row.`width`,map_row.`height`,
    map_row.`minimum_x`,map_row.`maximum_x`,
    map_row.`minimum_y`,map_row.`maximum_y`,
    identity_group.`canonical_map_id`,
    identity_group.`client_build_id`,
    identity_group.`client_map_id`,
    identity_group.`client_area_id`,
    0,
    LOWER(SUBSTRING_INDEX(map_row.`resource_identity`,'.',-1)),
    NULL
FROM (
    SELECT
        `resource_key`,
        MIN(`map_id`) AS `canonical_map_id`,
        MIN(`client_build_id`) AS `client_build_id`,
        MIN(`client_map_id`) AS `client_map_id`,
        MIN(`client_area_id`) AS `client_area_id`
    FROM `god2_map_identity_stage`
    GROUP BY `resource_key`
) identity_group
JOIN `god2_game`.`maps` map_row
  ON map_row.`map_id`=identity_group.`canonical_map_id`
ON DUPLICATE KEY UPDATE
    `canonical_map_id`=VALUES(`canonical_map_id`),
    `client_build_id`=VALUES(`client_build_id`),
    `client_map_id`=VALUES(`client_map_id`),
    `client_area_id`=VALUES(`client_area_id`),
    `enabled`=0;

INSERT INTO `god2_game`.`client_map_resources`
    (`resource_key`,`area_code`,`resource_name`,
     `canonical_map_id`,`client_build_id`,`client_map_id`,`client_area_id`,
     `enabled`,`resource_format`,`navigation_format`)
SELECT DISTINCT
    link_row.`destination_resource_key`,
    SUBSTRING_INDEX(
        SUBSTRING(link_row.`destination_resource_key`,LENGTH('client:map/')+1),
        '/',1) AS `area_code`,
    CONCAT(
        SUBSTRING(
            link_row.`destination_resource_key`,
            LENGTH('client:map/')
            + LENGTH(SUBSTRING_INDEX(
                SUBSTRING(link_row.`destination_resource_key`,LENGTH('client:map/')+1),
                '/',1))
            + 2),
        '.hmd'),
    NULL,
    link_row.`client_build_id`,
    NULL,
    NULL,
    0,
    'hmd',
    NULL
FROM `god2_portal_link_stage` link_row
LEFT JOIN `god2_game`.`client_map_resources` resource_row
  ON resource_row.`resource_key`=link_row.`destination_resource_key`
WHERE resource_row.`resource_key` IS NULL
  AND link_row.`can_record_type`=1
ON DUPLICATE KEY UPDATE
    `enabled`=0;

INSERT INTO `god2_game`.`client_map_resource_identities`
    (`map_identity_id`,`client_build_id`,`client_area_id`,`client_map_id`,
     `map_id`,`resource_key`,`world_map_x`,`world_map_y`,`enabled`)
SELECT
    `map_identity_id`,`client_build_id`,`client_area_id`,`client_map_id`,
    `map_id`,`resource_key`,`world_map_x`,`world_map_y`,0
FROM `god2_map_identity_stage`
ON DUPLICATE KEY UPDATE
    `map_id`=VALUES(`map_id`),
    `resource_key`=VALUES(`resource_key`),
    `world_map_x`=VALUES(`world_map_x`),
    `world_map_y`=VALUES(`world_map_y`),
    `enabled`=0;

INSERT INTO `god2_research`.`client_map_resource_identity_evidence`
    (`map_identity_id`,`source_row`,`source_sha256`,
     `identity_evidence_status`,`coordinate_evidence_status`)
SELECT
    `map_identity_id`,`source_row`,`source_sha256`,
    `identity_evidence_status`,`coordinate_evidence_status`
FROM `god2_map_identity_stage`
ON DUPLICATE KEY UPDATE
    `source_row`=VALUES(`source_row`),
    `source_sha256`=VALUES(`source_sha256`),
    `identity_evidence_status`=VALUES(`identity_evidence_status`),
    `coordinate_evidence_status`=VALUES(`coordinate_evidence_status`);

INSERT INTO `god2_research`.`client_map_resource_evidence`
    (`resource_key`,`resource_evidence_status`,`map_identity_evidence_status`,
     `portal_placement_evidence_status`,`navigation_evidence_status`,`admin_note`)
SELECT
    resource_row.`resource_key`,
    'Recovered',
    'Verified',
    'Unknown',
    CASE
      WHEN resource_row.`grid_width` IS NOT NULL
       AND resource_row.`grid_height` IS NOT NULL
      THEN 'Derived'
      ELSE 'EvidenceBlocked'
    END,
    'Rebuilt from pinned Migration 114 after FORGE_SEED_LEDGER_DRIFT; disabled pending exact-client file provenance.'
FROM `god2_game`.`client_map_resources` resource_row
WHERE EXISTS (
    SELECT 1
    FROM `god2_map_identity_stage` identity_row
    WHERE identity_row.`resource_key`=resource_row.`resource_key`
)
ON DUPLICATE KEY UPDATE
    `resource_evidence_status`=VALUES(`resource_evidence_status`),
    `map_identity_evidence_status`=VALUES(`map_identity_evidence_status`),
    `portal_placement_evidence_status`=VALUES(`portal_placement_evidence_status`),
    `navigation_evidence_status`=VALUES(`navigation_evidence_status`),
    `admin_note`=VALUES(`admin_note`);

INSERT INTO `god2_research`.`client_map_resource_evidence`
    (`resource_key`,`can_record_index`,`can_record_type`,`can_relative_path`,
     `resource_evidence_status`,`map_identity_evidence_status`,
     `portal_placement_evidence_status`,`navigation_evidence_status`,`admin_note`)
SELECT
    resource_row.`resource_key`,
    MIN(link_row.`can_record_index`),
    MIN(link_row.`can_record_type`),
    MIN(link_row.`can_relative_path`),
    'Recovered',
    'Unknown',
    'Candidate',
    'EvidenceBlocked',
    'HMD resource recovered from Migration 115 CAN inventory; no numeric map identity, dimensions or file hash.'
FROM `god2_game`.`client_map_resources` resource_row
JOIN `god2_portal_link_stage` link_row
  ON link_row.`destination_resource_key`=resource_row.`resource_key`
WHERE NOT EXISTS (
    SELECT 1
    FROM `god2_map_identity_stage` identity_row
    WHERE identity_row.`resource_key`=resource_row.`resource_key`
)
GROUP BY resource_row.`resource_key`
ON DUPLICATE KEY UPDATE
    `can_record_index`=VALUES(`can_record_index`),
    `can_record_type`=VALUES(`can_record_type`),
    `can_relative_path`=VALUES(`can_relative_path`),
    `resource_evidence_status`=VALUES(`resource_evidence_status`),
    `map_identity_evidence_status`=VALUES(`map_identity_evidence_status`),
    `portal_placement_evidence_status`=VALUES(`portal_placement_evidence_status`),
    `navigation_evidence_status`=VALUES(`navigation_evidence_status`),
    `admin_note`=VALUES(`admin_note`);

INSERT INTO `god2_game`.`portal_resource_links`
    (`portal_link_id`,`portal_id`,`client_build_id`,
     `source_resource_key`,`destination_resource_key`,
     `source_map_id`,`destination_map_id`,`enabled`)
SELECT
    `portal_link_id`,NULL,`client_build_id`,
    `source_resource_key`,`destination_resource_key`,
    `source_map_id`,`destination_map_id`,0
FROM `god2_portal_link_stage`
ON DUPLICATE KEY UPDATE
    `portal_id`=NULL,
    `source_resource_key`=VALUES(`source_resource_key`),
    `destination_resource_key`=VALUES(`destination_resource_key`),
    `source_map_id`=VALUES(`source_map_id`),
    `destination_map_id`=VALUES(`destination_map_id`),
    `enabled`=0;

INSERT INTO `god2_research`.`portal_resource_link_evidence`
    (`portal_link_id`,`can_relative_path`,`can_record_index`,`can_record_type`,
     `source_sha256`,`identity_evidence_status`,
     `source_coordinate_evidence_status`,
     `destination_coordinate_evidence_status`,`trigger_evidence_status`)
SELECT
    `portal_link_id`,`can_relative_path`,`can_record_index`,`can_record_type`,
    `source_sha256`,`identity_evidence_status`,
    'Unknown','Unknown','Unknown'
FROM `god2_portal_link_stage`
ON DUPLICATE KEY UPDATE
    `can_relative_path`=VALUES(`can_relative_path`),
    `can_record_index`=VALUES(`can_record_index`),
    `can_record_type`=VALUES(`can_record_type`),
    `source_sha256`=VALUES(`source_sha256`),
    `identity_evidence_status`=VALUES(`identity_evidence_status`),
    `source_coordinate_evidence_status`='Unknown',
    `destination_coordinate_evidence_status`='Unknown',
    `trigger_evidence_status`='Unknown';

CREATE TEMPORARY TABLE `god2_client_resource_result_guard` (
    `ready` tinyint NOT NULL,
    CONSTRAINT `ck_god2_client_resource_result_guard`
        CHECK (`ready` = 1)
);

INSERT INTO `god2_client_resource_result_guard` (`ready`)
SELECT IF(
    (SELECT COUNT(*) FROM `god2_game`.`client_map_resources`) = 158
    AND (SELECT COUNT(*) FROM `god2_game`.`client_map_resource_identities`) = 144
    AND (SELECT COUNT(*) FROM `god2_game`.`portal_resource_links`) = 65
    AND (SELECT COUNT(*) FROM `god2_research`.`client_map_resource_evidence`) = 158
    AND (SELECT COUNT(*) FROM `god2_research`.`client_map_resource_identity_evidence`) = 144
    AND (SELECT COUNT(*) FROM `god2_research`.`portal_resource_link_evidence`) = 65
    AND (SELECT COUNT(*) FROM `god2_game`.`client_map_resources` WHERE `enabled`<>0) = 0
    AND (SELECT COUNT(*) FROM `god2_game`.`client_map_resource_identities` WHERE `enabled`<>0) = 0
    AND (SELECT COUNT(*) FROM `god2_game`.`portal_resource_links` WHERE `enabled`<>0) = 0,
    1,
    0
);

DROP TEMPORARY TABLE `god2_client_resource_result_guard`;
DROP TEMPORARY TABLE `god2_portal_link_stage`;
DROP TEMPORARY TABLE `god2_map_identity_stage`;
