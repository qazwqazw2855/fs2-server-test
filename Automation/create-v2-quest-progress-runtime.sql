-- Objective snapshots are created only by trusted quest acceptance.
-- No official quest objectives are inserted by this infrastructure script.

CREATE TABLE god2_player.v2_quest_objective_progress (
    QuestInstanceId CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    ObjectiveId BIGINT NOT NULL,
    ObjectiveKind VARCHAR(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    TargetId BIGINT NOT NULL,
    RequiredCount BIGINT NOT NULL,
    CurrentCount BIGINT NOT NULL DEFAULT 0,
    UpdatedAtUtc DATETIME(6) NOT NULL DEFAULT UTC_TIMESTAMP(6)
        ON UPDATE CURRENT_TIMESTAMP(6),
    PRIMARY KEY (QuestInstanceId, ObjectiveId),
    CONSTRAINT fk_v2_objective_instance FOREIGN KEY (QuestInstanceId)
        REFERENCES god2_player.v2_quest_instances (QuestInstanceId),
    CONSTRAINT ck_v2_objective_identity CHECK (
        ObjectiveId > 0 AND TargetId > 0
    ),
    CONSTRAINT ck_v2_objective_kind CHECK (
        ObjectiveKind IN ('DefeatMonster', 'InteractNpc', 'VisitMap')
    ),
    CONSTRAINT ck_v2_objective_count CHECK (
        RequiredCount > 0
        AND CurrentCount >= 0
        AND CurrentCount <= RequiredCount
    )
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4
COMMENT='V2 任務目標快照與進度；不能以客戶端輸入建立';

CREATE TABLE god2_player.v2_quest_progress_events (
    QuestInstanceId CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    EventId CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    CharacterId BIGINT NOT NULL,
    EventFingerprint CHAR(64)
        CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    QuestVersionBefore BIGINT NOT NULL,
    QuestVersionAfter BIGINT NOT NULL,
    ResultJson LONGTEXT NOT NULL,
    AppliedAtUtc DATETIME(6) NOT NULL DEFAULT UTC_TIMESTAMP(6),
    PRIMARY KEY (QuestInstanceId, EventId),
    KEY ix_v2_progress_event (EventId),
    CONSTRAINT fk_v2_progress_event_owner
        FOREIGN KEY (QuestInstanceId, CharacterId)
        REFERENCES god2_player.v2_quest_instances
            (QuestInstanceId, CharacterId),
    CONSTRAINT ck_v2_progress_event_versions CHECK (
        QuestVersionBefore >= 0
        AND QuestVersionAfter >= QuestVersionBefore
    ),
    CONSTRAINT ck_v2_progress_event_result CHECK (JSON_VALID(ResultJson))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4
COMMENT='每個任務實例的事件防重紀錄；與目標進度原子提交';
