# Formal monster snapshot and supplemental identity cross-check

Observation: user-executed read-only SQL against the AWS `god2-runtime-db-test` container on 2026-09-23. This is a point-in-time operator-provided result, not a reproducible offline database export. Supplemental source: `db/imports/official/monsters/monsters.official.json` (208 records).

The Formal DB query returned 9 monsters, all disabled; each has NULL `monster_family`, `resource_id`, `level`, `max_hp` and `max_mp`. `monster_spawns` and `monster_drops` returned 0 rows.

| Formal monster ID | Formal code | Formal name | Supplemental client ID | Supplemental name |
|---:|---|---|---:|---|
| 68242252 | `monster_55` | 馬賊 | 55 | 马贼 |
| 504941989 | `monster_87` | 幽魂 | 87 | 幽魂 |
| 686778597 | `monster_47` | 鯨龍 | 47 | 鲸龙 |
| 1059940160 | `monster_54` | 穿山甲 | 54 | 穿山甲 |
| 1145916388 | `monster_21` | 森林矮人 | 21 | 森林矮人 |
| 1188807826 | `monster_184` | 狐狸 | 184 | 狐狸 |
| 1281800168 | `monster_6` | 怨靈 | 6 | 怨灵 |
| 1786764046 | `monster_89` | 蚌精 | 89 | 蚌精 |
| 1920676347 | `monster_30` | 野兔 | 30 | 野兔 |

All nine `monster_N` codes have a unique numeric match in the 208-record supplemental client inventory. Three names differ only in traditional/simplified Chinese characters (馬賊/马贼, 鯨龍/鲸龙, 怨靈/怨灵); the other six names match exactly. Code and name agreement is an identity lead, not evidence of combat statistics, spawn placement, drop semantics or runtime eligibility.

This snapshot must be refreshed before making decisions based on Formal DB counts. No database writes or runtime promotion were performed.
