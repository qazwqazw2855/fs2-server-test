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

## Pinned client file manifest cross-check

The pinned `db/imports/official/maps/God2_exact_current_map_sha256.csv` has SHA-256 `b2362491b9045a7c4b2f489706ad66eb36c71bcb999c22c103aab47035e56a51`. The lookup below is case-insensitive and checks the path with the client file `.mdtZ` extension. Each Migration 114 identity resolves to one inventory entry; none of the nine official `gamedata.csvZ` paths resolves to an inventory entry. This is evidence of a disagreement between sources, not proof that an unlisted file does not exist in a complete client.

| Area / map | Migration identity in pinned manifest | File SHA-256 | Official path in manifest |
|---|---|---|---|
| A5 / M40 | `Data2\map\south002\mazes05\mazeS05.mdtZ` | `76023CF8C140F26F32A704AFE8B7AFFC1A1B4D055BF87BC698A3094D6843E355` | No |
| A8 / M2 | `Data2\map\array\array06\array06.mdtZ` | `24D14394345F1D77ADCA1E2A4CC3113B99C344EDECF717DAF8284492DE472201` | No |
| A8 / M13 | `Data2\map\array\array11\array11.mdtZ` | `E5E87081D72C977866690A9112ADBC5E862FED9B9A33FE8D7F8513B89112DD84` | No |
| A8 / M14 | `Data2\map\array\array12\array12.mdtZ` | `A896F1CB40FAD379D95DB8A3B214DCAE4E01EAEAFF9720E0CA7C72A8C5AFAB13` | No |
| A8 / M15 | `Data2\map\array\array13\array13.mdtZ` | `958A371344BBF81344DBE5045F5221856F92834185C6C08534322C320995C409` | No |
| A15 / M7 | `Data2\map\north004\citywei01\Citywei01.mdtZ` | `D72BA90B33E44D1A97D8C3F52AEBEDB6AAF1B7248F686DA1B749C06B61C5E47B` | No |
| A15 / M8 | `Data2\map\north004\citywei02\Citywei02.mdtZ` | `8E97C973F54B35BF527C5AF2CACB757F2AD2312C9E6FFFF36FE5F5795749AF49` | No |
| A15 / M9 | `Data2\map\north004\citywei03\Citywei03.mdtZ` | `8E97C973F54B35BF527C5AF2CACB757F2AD2312C9E6FFFF36FE5F5795749AF49` | No |
| A15 / M30 | `Data2\map\north004\mazewei07\Mazewei07.mdtZ` | `86F2FB2C8942090C77AA1D0DFFF92B63CCF29162A963201209EC867E538564AA` | No |

The manifest records names and hashes, not the bytes of these client assets. Before changing resource keys or runtime enablement, inspect the exact-current client files and confirm how the client resolves the official `gamedata.csvZ` path against the on-disk resource.
