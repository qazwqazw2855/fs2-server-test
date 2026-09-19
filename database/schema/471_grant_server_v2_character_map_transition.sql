-- Server V2 map transition requires the runtime account to atomically update
-- character map + position while preserving column-level least privilege.

GRANT UPDATE (
    `map_id`,
    `position_x`,
    `position_y`,
    `runtime_version`,
    `concurrency_token`,
    `updated_at_utc`
)
ON `god2_player`.`characters`
TO `god2_runtime_role`;
