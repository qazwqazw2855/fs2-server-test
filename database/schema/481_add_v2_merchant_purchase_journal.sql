-- Durable immutable server requests for uncertain merchant BUY recovery.
-- This table does not approve evidence or authorize automatic retries.
CREATE TABLE IF NOT EXISTS god2_player.v2_merchant_purchase_journal (
    IdempotencyKeyHash CHAR(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    TransactionFingerprintSha256 CHAR(64)
        CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    TransactionId CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    CharacterId BIGINT NOT NULL,
    RequestJson JSON NOT NULL,
    CreatedAtUtc DATETIME(6) NOT NULL,
    PRIMARY KEY (IdempotencyKeyHash),
    UNIQUE KEY ux_v2_purchase_journal_transaction (TransactionId),
    KEY ix_v2_purchase_journal_character (CharacterId),
    CONSTRAINT fk_v2_purchase_journal_character
        FOREIGN KEY (CharacterId)
        REFERENCES god2_player.characters (character_id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
