-- V2 quest reward infrastructure.
-- Apply through the reviewed schema migration workflow.
-- No gameplay content or runtime privileges are granted here.

CREATE TABLE god2_player.v2_quest_instances (
    QuestInstanceId CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    CharacterId BIGINT NOT NULL,
    QuestId BIGINT NOT NULL,
    DefinitionFingerprint CHAR(64)
        CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    QuestVersion BIGINT NOT NULL DEFAULT 0,
    State VARCHAR(32) CHARACTER SET ascii COLLATE ascii_bin
        NOT NULL DEFAULT 'Accepted',
    CompletionEventId CHAR(36)
        CHARACTER SET ascii COLLATE ascii_bin NULL,
    AcceptedAtUtc DATETIME(6) NOT NULL DEFAULT UTC_TIMESTAMP(6),
    ReadyAtUtc DATETIME(6) NULL,
    CompletedAtUtc DATETIME(6) NULL,
    UpdatedAtUtc DATETIME(6) NOT NULL DEFAULT UTC_TIMESTAMP(6)
        ON UPDATE CURRENT_TIMESTAMP(6),
    PRIMARY KEY (QuestInstanceId),
    UNIQUE KEY ux_v2_quest_instance_owner (QuestInstanceId, CharacterId),
    UNIQUE KEY ux_v2_quest_completion_event (CompletionEventId),
    KEY ix_v2_quest_character_state (CharacterId, State),
    CONSTRAINT fk_v2_quest_character FOREIGN KEY (CharacterId)
        REFERENCES god2_player.characters (character_id),
    CONSTRAINT fk_v2_quest_definition FOREIGN KEY (QuestId)
        REFERENCES god2_game.quests (quest_id),
    CONSTRAINT ck_v2_quest_version CHECK (QuestVersion >= 0),
    CONSTRAINT ck_v2_quest_state CHECK (
        State IN ('Accepted', 'Ready', 'Completed', 'Abandoned')
    ),
    CONSTRAINT ck_v2_quest_completion_state CHECK (
        (State IN ('Ready', 'Completed')
            AND CompletionEventId IS NOT NULL
            AND ReadyAtUtc IS NOT NULL)
        OR
        (State IN ('Accepted', 'Abandoned')
            AND CompletionEventId IS NULL
            AND ReadyAtUtc IS NULL)
    ),
    CONSTRAINT ck_v2_quest_completed_at CHECK (
        (State = 'Completed' AND CompletedAtUtc IS NOT NULL)
        OR
        (State <> 'Completed' AND CompletedAtUtc IS NULL)
    )
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4
COMMENT='V2 任務實例；完成資格由伺服器任務流程產生';

CREATE TABLE god2_player.v2_quest_reward_snapshots (
    QuestInstanceId CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    RewardFingerprint CHAR(64)
        CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    RewardJson LONGTEXT NOT NULL,
    EvidenceReference VARCHAR(500) NOT NULL,
    CreatedAtUtc DATETIME(6) NOT NULL DEFAULT UTC_TIMESTAMP(6),
    PRIMARY KEY (QuestInstanceId),
    CONSTRAINT fk_v2_reward_snapshot_instance FOREIGN KEY (QuestInstanceId)
        REFERENCES god2_player.v2_quest_instances (QuestInstanceId),
    CONSTRAINT ck_v2_reward_snapshot_json CHECK (JSON_VALID(RewardJson)),
    CONSTRAINT ck_v2_reward_snapshot_evidence CHECK (
        CHAR_LENGTH(TRIM(EvidenceReference)) > 0
    )
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4
COMMENT='完整獎勵快照；來源文字不等於授權，建立流程須另行驗證';

CREATE TABLE god2_player.v2_quest_reward_claims (
    QuestInstanceId CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    CharacterId BIGINT NOT NULL,
    ClaimTransactionId CHAR(36)
        CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    RewardFingerprint CHAR(64)
        CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    InventoryVersionBefore BIGINT NOT NULL,
    InventoryVersionAfter BIGINT NOT NULL,
    ResultJson LONGTEXT NOT NULL,
    ClaimedAtUtc DATETIME(6) NOT NULL DEFAULT UTC_TIMESTAMP(6),
    PRIMARY KEY (QuestInstanceId),
    UNIQUE KEY ux_v2_quest_claim_transaction (ClaimTransactionId),
    CONSTRAINT fk_v2_quest_claim_owner
        FOREIGN KEY (QuestInstanceId, CharacterId)
        REFERENCES god2_player.v2_quest_instances
            (QuestInstanceId, CharacterId),
    CONSTRAINT fk_v2_quest_claim_snapshot FOREIGN KEY (QuestInstanceId)
        REFERENCES god2_player.v2_quest_reward_snapshots (QuestInstanceId),
    CONSTRAINT ck_v2_quest_claim_versions CHECK (
        InventoryVersionBefore >= 0
        AND InventoryVersionAfter > InventoryVersionBefore
    ),
    CONSTRAINT ck_v2_quest_claim_result CHECK (JSON_VALID(ResultJson))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4
COMMENT='成功領獎紀錄；與入包及任務完成狀態在同一交易提交';
