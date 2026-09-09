-- Fail closed for enhancement success rates that were retained only as
-- compatibility estimates. Preserve the rows and their provenance for
-- administration/research, but exclude them from the enabled runtime catalog.
-- OfficialClient and BahamutVerified rows are intentionally unchanged.

UPDATE `god2_game`.`equipment_enhancement_rates`
SET `enabled` = 0
WHERE `evidence_status` = 'CompatibilityEstimate'
  AND `enabled` = 1;
