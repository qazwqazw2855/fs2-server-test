#!/usr/bin/env python3
"""Audit supplemental quest presentation and reward evidence offline."""

from pathlib import Path
import hashlib
import json

ROOT = Path(__file__).resolve().parents[1]
BASE = ROOT / "db/imports/official"
OUTPUT = ROOT / "docs/parity/quest-static-evidence.md"


def load(category: str):
    path = BASE / category / f"{category}.official.json"
    data = json.loads(path.read_text(encoding="utf-8"))
    assert data["recordCount"] == len(data["records"])
    return data, hashlib.sha256(path.read_bytes()).hexdigest()


def main():
    quests, quest_hash = load("quests")
    rewards, reward_hash = load("rewards")
    rows = quests["records"]
    assert len(rows) == len({row["clientQuestId"] for row in rows}) == 418
    assert all(row["questType"] == "client-presentation" for row in rows)
    assert all(row["objectives"] for row in rows)
    assert all(row["startNpcReference"] is None and row["endNpcReference"] is None
               and row["serverStateMachineId"] is None and not row["rewardCandidates"]
               for row in rows)
    item_target_count = sum(bool(row["targetReferences"]["item"]) for row in rows)
    assert item_target_count == 169
    assert all(not row["targetReferences"][key] for row in rows
               for key in ("npc", "monster", "map"))
    assert rewards["recordCount"] == 0
    assert rewards["canDirectImportToGameplay"] is False

    lines = [
        "# Supplemental quest static evidence", "",
        "This offline check covers recovered client presentation data, not Formal DB or verified quest state transitions.", "",
        "| Source | Rows | SHA-256 |", "|---|---:|---|",
        f"| `quests/quests.official.json` | 418 | `{quest_hash}` |",
        f"| `rewards/rewards.official.json` | 0 | `{reward_hash}` |", "",
        "All 418 quest IDs are distinct client-presentation entries with objective text. None has a bound start/end NPC, server state-machine ID or reward candidate. Exactly 169 quests have client item target references; item identity and count semantics are a separate deferred item task. No NPC, monster or map target references are populated in these records.", "",
        "The separate reward inventory contains zero records. Do not treat objective text or client item references as verified server quest triggers or rewards. Quest runtime activation remains blocked pending NPC/dialog, item, state-machine and reward evidence.", "",
    ]
    OUTPUT.write_text("\n".join(lines), encoding="utf-8")
    print(f"Audited {len(rows)} client quest entries and {rewards['recordCount']} reward entries; wrote {OUTPUT.relative_to(ROOT)}")


if __name__ == "__main__":
    main()
