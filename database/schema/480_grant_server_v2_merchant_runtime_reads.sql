-- Grant only the formal merchant catalog reads required by Server V2.
--
-- The AWS runtime account uses explicit least-privilege grants rather than
-- inheriting the historical broad god2_runtime_role catalog grant.
--
-- These permissions support:
--   MerchantInteractionResolver / merchant runtime authority
--   MariaDbMerchantPurchaseWriter
--   MariaDbMerchantSaleWriter
--   exact-current merchant client catalog identity resolution
--
-- No god2_research access is granted to runtime.

GRANT SELECT
ON `god2_game`.`merchants`
TO 'god2_v2'@'172.17.0.1';

GRANT SELECT
ON `god2_game`.`merchant_inventory`
TO 'god2_v2'@'172.17.0.1';

GRANT SELECT
ON `god2_game`.`merchant_client_catalog_identities`
TO 'god2_v2'@'172.17.0.1';
