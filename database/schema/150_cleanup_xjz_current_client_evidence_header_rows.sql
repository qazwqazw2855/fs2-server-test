DELETE FROM god2_game.xjz_item_effect_visual_evidence
WHERE (source_table = 'ItemEft3' AND effect_key = '档案标识符')
   OR (source_table = 'GodItemEft' AND effect_key = '档案标识符');
