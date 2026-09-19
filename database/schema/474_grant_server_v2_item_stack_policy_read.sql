-- Server V2 reads item stack rules without catalog mutation rights.
GRANT SELECT (`item_id`, `name_zh_tw`, `maximum_stack`, `enabled`)
ON `god2_game`.`items`
TO `god2_runtime_role`;
