# Exact-current map resource path differences

Evidence: `docs/parity/map-hierarchy-exact-current.json` transcribes the official `Map_City_Coordniate` section of `Data2/Patch/Comm/gamedata.csvZ` (SHA-256 `f8b0762f111c532f1cbb38d66f06b6a4c6c685c278a5dcc98fbaead780e2376f`). The comparison target is `database/schema/114_publish_official_map_catalog.sql`. Paths below preserve the spelling in each source; case and separators alone are ignored.

| Area / map | Migration 114 resource identity | Official resource path | Official source row |
|---|---|---|---:|
| A5 / M40 | `mazes05/mazes05.mdt` | `South002/cityS04/cityS04.mdt` | 22766 |
| A8 / M2 | `array06/array06.mdt` | `array/9tyr2/9tyr.mdt` | 22791 |
| A8 / M13 | `array11/array11.mdt` | `array/array011/array011.mdt` | 22796 |
| A8 / M14 | `array12/array12.mdt` | `array/array012/array012.mdt` | 22797 |
| A8 / M15 | `array13/array13.mdt` | `array/array013/array013.mdt` | 22798 |
| A15 / M7 | `citywei01/citywei01.mdt` | `North004/Citywei01/Citywie01.mdt` | 22885 |
| A15 / M8 | `citywei02/citywei02.mdt` | `North004/Citywei02/Citywie02.mdt` | 22886 |
| A15 / M9 | `citywei03/citywei03.mdt` | `North004/Citywei03/Citywie03.mdt` | 22887 |
| A15 / M30 | `mazewei07/mazewei07.mdt` | `North004/Mazewei06/Mazewei07.mdt` | 22894 |

The official file identifies a map resource path. This comparison does not establish that the corresponding client asset exists locally or that a `client_map_resources.resource_key` can be changed safely. Keep current runtime and production mappings unchanged until the exact-current client asset and foreign-key dependents are verified. The map promotion gate remains blocked.
