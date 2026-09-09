-- Formal runtime database consolidation.
-- The live server uses only god2, god2_game, god2_game_meta, and god2_player.
-- Research imports, recovered staging tables, and duplicated Traditional-Chinese
-- mirror schemas are not runtime authority and must not remain in the formal DB.

DROP DATABASE IF EXISTS `god2_game_meta_zh_tw`;
DROP DATABASE IF EXISTS `god2_game_zh_tw`;
DROP DATABASE IF EXISTS `god2_player_zh_tw`;
DROP DATABASE IF EXISTS `god2_recovered`;
DROP DATABASE IF EXISTS `god2_research`;
