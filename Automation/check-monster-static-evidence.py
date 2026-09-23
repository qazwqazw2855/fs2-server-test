#!/usr/bin/env python3
"""Audit supplemental monster, spawn and drop identities without runtime writes."""

import hashlib
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
BASE = ROOT / "db/imports/official"
OUTPUT = ROOT / "docs/parity/monster-spawn-drop-static.md"


def load(category: str):
    path = BASE / category / f"{category}.official.json"
    data = json.loads(path.read_text(encoding="utf-8"))
    assert data["recordCount"] == len(data["records"])
    assert len({row["id"] for row in data["records"]}) == len(data["records"])
    return data["records"], hashlib.sha256(path.read_bytes()).hexdigest()


def main():
    monsters, monster_hash = load("monsters")
    spawns, spawn_hash = load("spawns")
    drops, drop_hash = load("drop_tables")
    maps, map_hash = load("maps")
    monster_ids = {row["id"] for row in monsters}
    map_ids = {row["id"] for row in maps}
    drop_targets = [row["monsterTemplateId"] for row in drops]

    assert len(monsters) == len(drops) == 208
    assert len(spawns) == len(maps) == 69
    assert set(drop_targets) == monster_ids and len(set(drop_targets)) == 208
    assert {row["mapId"] for row in spawns} == map_ids
    assert all(row["templateId"] is None and row["x"] is None and row["y"] is None
               and row["runtimeEligibility"] is False for row in spawns)
    assert all(row["runtimeEligibility"] is False for row in drops)
    assert all(not row["spawnReferences"] and not row["dropReferences"]
               for row in monsters)

    lines = [
        "# Supplemental monster, spawn and drop static evidence", "",
        "This offline audit covers the `db/imports/official` recovery files only. These IDs are supplemental and are not joined directly to the formal runtime catalogs.", "",
        "| Source | Rows | SHA-256 |",
        "|---|---:|---|",
        f"| `monsters/monsters.official.json` | 208 | `{monster_hash}` |",
        f"| `spawns/spawns.official.json` | 69 | `{spawn_hash}` |",
        f"| `drop_tables/drop_tables.official.json` | 208 | `{drop_hash}` |",
        f"| `maps/maps.official.json` | 69 | `{map_hash}` |", "",
        "Every drop-table candidate references exactly one distinct monster identity, covering all 208 monsters. This establishes identity alignment only: the candidates have no verified drop items, rates or quantities, and all 208 have `runtimeEligibility=false`.", "",
        "The 69 spawn candidates each reference one of the 69 supplemental map identities. They are map-entry or respawn candidates; all lack a monster template and coordinates, and all have `runtimeEligibility=false`. None of the 208 monster rows carries a spawn or drop reference.", "",
        "No monster spawn placement, drop behavior, or runtime promotion follows from these counts. The 69 supplemental map IDs are a separate namespace from the 144-map exact-current formal catalog.", "",
    ]
    OUTPUT.write_text("\n".join(lines), encoding="utf-8")
    print(f"Audited 208 monsters, 69 spawn candidates and 208 drop candidates; wrote {OUTPUT.relative_to(ROOT)}")


if __name__ == "__main__":
    main()
