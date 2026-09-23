#!/usr/bin/env python3
"""Audit supplemental NPC, merchant and dialog references without DB access."""

from pathlib import Path
import hashlib
import json

ROOT = Path(__file__).resolve().parents[1]
BASE = ROOT / "db/imports/official"
OUTPUT = ROOT / "docs/parity/npc-merchant-dialog-static.md"


def load(category: str):
    path = BASE / category / f"{category}.official.json"
    data = json.loads(path.read_text(encoding="utf-8"))
    rows = data["records"]
    assert data["recordCount"] == len(rows)
    assert len({row["id"] for row in rows}) == len(rows)
    return rows, hashlib.sha256(path.read_bytes()).hexdigest()


def main():
    npcs, npc_hash = load("npcs")
    merchants, merchant_hash = load("merchants")
    dialogs, dialog_hash = load("dialogs")
    npc_ids = {row["id"] for row in npcs}
    assert len(npcs) == 319 and len(merchants) == 16 and len(dialogs) == 18
    assert all(row["npcTemplateId"] in npc_ids for row in merchants)
    assert all(row["inventoryStatus"] == "unknownFromPhase2_2ClientEvidence"
               for row in merchants)
    assert all(row["spawnInformation"]["status"] == "notPresentInNpcCsv"
               for row in npcs)
    visual_spawn_candidates = [row for row in npcs
                               if row["spawnInformation"]["candidates"]]
    assert len(visual_spawn_candidates) == 4
    assert all(candidate["position"] is None
               for row in visual_spawn_candidates
               for candidate in row["spawnInformation"]["candidates"])
    assert all(row["relationStatus"] == "catalogAvailable_messageToNpcRelationUnknown"
               for row in dialogs)
    dialog_ref_count = sum(bool(row["dialogReferences"]) for row in npcs)
    assert dialog_ref_count == 12
    assert all(row["templateIndexStatus"] == "unknownBlankInNpcCsv" for row in npcs)

    lines = [
        "# Supplemental NPC, merchant and dialog static evidence", "",
        "This offline check covers only recovered `db/imports/official` client data; it does not query the Formal DB or verify live NPC behavior.", "",
        "| Source | Rows | SHA-256 |", "|---|---:|---|",
        f"| `npcs/npcs.official.json` | 319 | `{npc_hash}` |",
        f"| `merchants/merchants.official.json` | 16 | `{merchant_hash}` |",
        f"| `dialogs/dialogs.official.json` | 18 catalogs | `{dialog_hash}` |", "",
        "All 16 merchant candidates reference an existing NPC template identity, but all 16 inventories are unknown. The 319 NPC templates have no spawn placement in NPC.csv and their template indexes are blank/unknown in that source. Four NPC rows carry separate official visual map candidates, all without exact positions.", "",
        "Twelve NPC rows carry dialog group candidates. All 18 dialog catalogs say the message-to-NPC relationship is unknown; their row counts do not establish a callable NPC dialog or branch behavior.", "",
        "NPC spawn, merchant purchase/sale, and dialog activation remain gated by separate map, inventory and protocol evidence. No runtime rows were promoted.", "",
    ]
    OUTPUT.write_text("\n".join(lines), encoding="utf-8")
    print(f"Audited {len(npcs)} NPCs, {len(merchants)} merchants and {len(dialogs)} dialog catalogs; wrote {OUTPUT.relative_to(ROOT)}")


if __name__ == "__main__":
    main()
