-- Server-owned social foundation. Only the NameCard relationship is runtime-enabled.
-- Official client packet codecs remain evidence-blocked.

CREATE TABLE IF NOT EXISTS `god2_player`.`player_social_invitations` (
    `invitation_id` char(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    `kind` varchar(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    `requester_character_id` bigint NOT NULL,
    `target_character_id` bigint NOT NULL,
    `status` varchar(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    `requested_at_utc` datetime(6) NOT NULL,
    `responded_at_utc` datetime(6) NULL,
    `response_actor_id` bigint NULL,
    `version` bigint NOT NULL DEFAULT 1,
    PRIMARY KEY (`invitation_id`),
    KEY `ix_social_invitation_requester_status` (`requester_character_id`,`kind`,`status`),
    KEY `ix_social_invitation_target_status` (`target_character_id`,`kind`,`status`),
    CONSTRAINT `fk_social_invitation_requester` FOREIGN KEY (`requester_character_id`)
        REFERENCES `god2_player`.`characters` (`character_id`),
    CONSTRAINT `fk_social_invitation_target` FOREIGN KEY (`target_character_id`)
        REFERENCES `god2_player`.`characters` (`character_id`),
    CONSTRAINT `fk_social_invitation_response_actor` FOREIGN KEY (`response_actor_id`)
        REFERENCES `god2_player`.`characters` (`character_id`),
    CONSTRAINT `ck_social_invitation_distinct_characters` CHECK (`requester_character_id` <> `target_character_id`),
    CONSTRAINT `ck_social_invitation_kind` CHECK (`kind` IN ('NameCard')),
    CONSTRAINT `ck_social_invitation_status` CHECK (`status` IN ('Pending','Accepted','Rejected','Cancelled','Expired')),
    CONSTRAINT `ck_social_invitation_version` CHECK (`version` >= 1)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `god2_player`.`player_relationships` (
    `character_id_low` bigint NOT NULL,
    `character_id_high` bigint NOT NULL,
    `relationship_kind` varchar(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    `status` varchar(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    `created_at_utc` datetime(6) NOT NULL,
    `ended_at_utc` datetime(6) NULL,
    `version` bigint NOT NULL DEFAULT 1,
    PRIMARY KEY (`character_id_low`,`character_id_high`,`relationship_kind`),
    KEY `ix_player_relationship_high` (`character_id_high`,`relationship_kind`,`status`),
    CONSTRAINT `fk_player_relationship_low` FOREIGN KEY (`character_id_low`)
        REFERENCES `god2_player`.`characters` (`character_id`),
    CONSTRAINT `fk_player_relationship_high` FOREIGN KEY (`character_id_high`)
        REFERENCES `god2_player`.`characters` (`character_id`),
    CONSTRAINT `ck_player_relationship_order` CHECK (`character_id_low` < `character_id_high`),
    CONSTRAINT `ck_player_relationship_kind` CHECK (`relationship_kind` IN ('NameCard')),
    CONSTRAINT `ck_player_relationship_status` CHECK (`status` IN ('Active','Ended')),
    CONSTRAINT `ck_player_relationship_version` CHECK (`version` >= 1)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `god2_player`.`player_social_blocks` (
    `blocker_character_id` bigint NOT NULL,
    `blocked_character_id` bigint NOT NULL,
    `created_at_utc` datetime(6) NOT NULL,
    PRIMARY KEY (`blocker_character_id`,`blocked_character_id`),
    KEY `ix_player_social_blocked` (`blocked_character_id`),
    CONSTRAINT `fk_player_social_blocker` FOREIGN KEY (`blocker_character_id`)
        REFERENCES `god2_player`.`characters` (`character_id`),
    CONSTRAINT `fk_player_social_blocked` FOREIGN KEY (`blocked_character_id`)
        REFERENCES `god2_player`.`characters` (`character_id`),
    CONSTRAINT `ck_player_social_block_distinct` CHECK (`blocker_character_id` <> `blocked_character_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `god2_player`.`player_social_operations` (
    `actor_character_id` bigint NOT NULL,
    `request_id` varchar(96) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    `operation_kind` varchar(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    `payload_fingerprint` varchar(200) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    `result_code` varchar(48) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    `invitation_id` char(36) CHARACTER SET ascii COLLATE ascii_bin NULL,
    `related_character_id` bigint NULL,
    `mutated` tinyint(1) NOT NULL,
    `blocked` tinyint(1) NULL,
    `created_at_utc` datetime(6) NOT NULL,
    PRIMARY KEY (`actor_character_id`,`request_id`),
    KEY `ix_player_social_operation_invitation` (`invitation_id`),
    CONSTRAINT `fk_player_social_operation_actor` FOREIGN KEY (`actor_character_id`)
        REFERENCES `god2_player`.`characters` (`character_id`),
    CONSTRAINT `fk_player_social_operation_invitation` FOREIGN KEY (`invitation_id`)
        REFERENCES `god2_player`.`player_social_invitations` (`invitation_id`),
    CONSTRAINT `fk_player_social_operation_related` FOREIGN KEY (`related_character_id`)
        REFERENCES `god2_player`.`characters` (`character_id`),
    CONSTRAINT `ck_player_social_operation_kind` CHECK (`operation_kind` IN ('NameCardRequest','NameCardResponse','SetBlock')),
    CONSTRAINT `ck_player_social_operation_mutated` CHECK (`mutated` IN (0,1)),
    CONSTRAINT `ck_player_social_operation_blocked` CHECK (`blocked` IS NULL OR `blocked` IN (0,1))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
