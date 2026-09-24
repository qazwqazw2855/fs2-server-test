#!/usr/bin/env python3
"""Check pinned portal staging links against pinned map identities without DB access."""

from collections import Counter, defaultdict
import json
from pathlib import Path
import re


ROOT = Path(__file__).resolve().parents[1]
SQL = ROOT / "database/schema/476_repair_forge_client_map_resource_provenance_drift.sql"
OUTPUT = ROOT / "docs/parity/portal-static-links.md"
INVENTORY = ROOT / "db/imports/official/portals/portals.official.json"


def fields(line: str) -> list[str]:
    return [value.strip("'") for value in re.findall(r"'[^']*'|NULL|\d+", line)]


def stage_rows(sql: str, start: str, end: str, prefix: str) -> list[list[str]]:
    segment = sql.split(start, 1)[1].split(end, 1)[0]
    return [fields(line) for line in segment.splitlines()
            if line.strip().startswith(prefix)]


def main() -> None:
    sql = SQL.read_text(encoding="utf-8")
    identities = stage_rows(
        sql, "INSERT INTO `god2_map_identity_stage`\nVALUES",
        "CREATE TEMPORARY TABLE `god2_portal_link_stage`", "(")
    links = stage_rows(
        sql, "INSERT INTO `god2_portal_link_stage`", "\n;\n",
        "('client:can-link/")
    assert len(identities) == 144, len(identities)
    assert len(links) == 65, len(links)

    identity_by_key: dict[str, set[str]] = defaultdict(set)
    for row in identities:
        identity_by_key[row[5]].add(row[4])

    summary: dict[str, Counter[str]] = defaultdict(Counter)
    for row in links:
        link_id, source_key, destination_key = row[0], row[2], row[3]
        source_id, destination_id, status, enabled = row[4], row[5], row[10], row[11]
        assert source_id in identity_by_key[source_key], link_id
        assert enabled == "0", link_id
        if status == "Derived":
            assert destination_id != "NULL", link_id
            assert destination_id in identity_by_key[destination_key], link_id
        elif status == "Candidate":
            assert destination_id == "NULL", link_id
            assert destination_key not in identity_by_key, link_id
        else:
            raise AssertionError(f"Unexpected status for {link_id}: {status}")
        summary[source_key][status] += 1

    assert sum(c["Derived"] for c in summary.values()) == 44
    assert sum(c["Candidate"] for c in summary.values()) == 21
    inventory = json.loads(INVENTORY.read_text(encoding="utf-8"))
    records = {record["id"]: record for record in inventory["records"]}
    link_ids = {row[0] for row in links}
    observed_ids = sorted(records.keys() - link_ids)
    assert inventory["recordCount"] == len(records) == 68
    assert len(link_ids) == 65 and link_ids <= records.keys()
    assert observed_ids == [
        "official-observed/kunlun-portal-to-secret-room",
        "official-observed/training-room-entry",
        "official-observed/yuanshi-transfer-to-fenghua",
    ]
    assert all(records[record_id]["kind"] == "officialVisualTransfer"
               for record_id in observed_ids)
    lines = [
        "# Pinned portal staging link cross-check", "",
        "Source: `database/schema/476_repair_forge_client_map_resource_provenance_drift.sql`.",
        "This offline check compares only pinned staging rows, without querying or changing the production DB.",
        "", "| Source map resource key | Derived with destination identity | Candidate without destination identity |",
        "|---|---:|---:|",
    ]
    for source, counts in sorted(summary.items()):
        lines.append(f"| `{source}` | {counts['Derived']} | {counts['Candidate']} |")
    lines += [
        "| **Total** | **44** | **21** |", "",
        "All 65 links have matching source map IDs and are disabled. All 44 Derived links have matching destination map IDs. The 21 Candidate links have no destination map identity or destination map ID.",
        "This establishes static identity consistency only. Trigger coordinates and transfer semantics remain unverified; no staging link is promoted to gameplay.", "",
        "## Supplemental inventory difference", "",
        "The 68-record supplemental inventory contains these 65 CAN links plus three `officialVisualTransfer` observations. Those three observations are separate evidence records, not missing CAN-link staging rows:", "",
    ]
    for record_id in observed_ids:
        lines.append(f"- `{record_id}`")
    lines += ["", "Their exact destination/map identity, coordinates or packet fields are incomplete. Keep them outside CAN-link staging and gameplay promotion.", ""]
    # Preserve evidence recorded from supplied client bytes when refreshing the
    # generated staging summary. The marker separates operator evidence from
    # content derived solely from the pinned SQL and inventory.
    evidence_marker = "## 2026-09-24 local CAN bytes cross-check"
    evidence = ""
    if OUTPUT.exists():
        existing = OUTPUT.read_text(encoding="utf-8")
        if existing.count(evidence_marker) > 1:
            raise ValueError("Duplicate portal client evidence marker")
        if evidence_marker in existing:
            evidence = existing[existing.index(evidence_marker):].strip()
    generated = "\\n".join(lines).rstrip() + "\\n"
    if evidence:
        generated += "\\n" + evidence + "\\n"
    OUTPUT.write_text(generated, encoding="utf-8")
    print(f"Checked {len(links)} staging links; wrote {OUTPUT.relative_to(ROOT)}")


if __name__ == "__main__":
    main()
