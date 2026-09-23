# Supplemental NPC, merchant and dialog static evidence

This offline check covers only recovered `db/imports/official` client data; it does not query the Formal DB or verify live NPC behavior.

| Source | Rows | SHA-256 |
|---|---:|---|
| `npcs/npcs.official.json` | 319 | `294019afd865e6dd05cfc9b3b1cee19dd8e2d037367fb23542b06a59560a1a66` |
| `merchants/merchants.official.json` | 16 | `2d8e83957c1ce19062c70aa7378cc46953d69bb5fb4fe8ab72dde584ab616a99` |
| `dialogs/dialogs.official.json` | 18 catalogs | `1dac00755c38dea5089a1c555dd3eaf5b7be2bb7ec79e88c14ebf92228082072` |

All 16 merchant candidates reference an existing NPC template identity, but all 16 inventories are unknown. The 319 NPC templates have no spawn placement in NPC.csv and their template indexes are blank/unknown in that source. Four NPC rows carry separate official visual map candidates, all without exact positions.

Twelve NPC rows carry dialog group candidates. All 18 dialog catalogs say the message-to-NPC relationship is unknown; their row counts do not establish a callable NPC dialog or branch behavior.

NPC spawn, merchant purchase/sale, and dialog activation remain gated by separate map, inventory and protocol evidence. No runtime rows were promoted.
