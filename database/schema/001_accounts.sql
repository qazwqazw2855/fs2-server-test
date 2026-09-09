CREATE TABLE IF NOT EXISTS `accounts` (
    `Id` bigint NOT NULL AUTO_INCREMENT,
    `LoginName` varchar(64) NOT NULL,
    `PasswordHash` varchar(255) NOT NULL,
    `CreatedAtUtc` datetime(6) NOT NULL,
    `UpdatedAtUtc` datetime(6) NOT NULL,
    `ConcurrencyToken` varchar(64) NOT NULL,
    PRIMARY KEY (`Id`),
    UNIQUE KEY `UX_Accounts_LoginName` (`LoginName`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
