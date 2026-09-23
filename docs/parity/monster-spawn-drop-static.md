# Supplemental monster, spawn and drop static evidence

This offline audit covers the `db/imports/official` recovery files only. These IDs are supplemental and are not joined directly to the formal runtime catalogs.

| Source | Rows | SHA-256 |
|---|---:|---|
| `monsters/monsters.official.json` | 208 | `e222fa38e8ec2e1f2dd5f8fa61ab2b93ad9cef5b24cef5a58b5e09363101d882` |
| `spawns/spawns.official.json` | 69 | `cfab5e58fa5c35a158664fe92ee32f4437f7c23ae953b030f9529c636bcab907` |
| `drop_tables/drop_tables.official.json` | 208 | `10d6e3226f75cad5acc35ce17de540f1a441810f1a5b055546834af7b3925767` |
| `maps/maps.official.json` | 69 | `25445c189111099168e392ca723b9700461d55ad4921e813e471e66070aec0f1` |

Every drop-table candidate references exactly one distinct monster identity, covering all 208 monsters. This establishes identity alignment only: the candidates have no verified drop items, rates or quantities, and all 208 have `runtimeEligibility=false`.

The 69 spawn candidates each reference one of the 69 supplemental map identities. They are map-entry or respawn candidates; all lack a monster template and coordinates, and all have `runtimeEligibility=false`. None of the 208 monster rows carries a spawn or drop reference.

No monster spawn placement, drop behavior, or runtime promotion follows from these counts. The 69 supplemental map IDs are a separate namespace from the 144-map exact-current formal catalog.
