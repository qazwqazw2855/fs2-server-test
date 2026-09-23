#!/usr/bin/env bash
set -euo pipefail

root="$(git rev-parse --show-toplevel)"
cd "$root"
container="${GOD2_DB_CONTAINER:-god2-runtime-db-test}"
output_json="${1:-docs/parity/gameplay-catalog-current.json}"
output_md="${output_json%.json}.md"
[[ "$output_json" == *.json ]] || { echo "Output must end in .json" >&2; exit 1; }

for command_name in git sudo docker python3; do
  command -v "$command_name" >/dev/null || {
    echo "Missing command: $command_name" >&2
    exit 1
  }
done

work_dir="$(mktemp -d)"
trap 'rm -rf "$work_dir"' EXIT

cat <<'SQL' | sudo docker exec -i "$container" sh -lc '
password="$(cat "$MARIADB_ROOT_PASSWORD_FILE")"
exec mariadb -uroot -p"$password" --batch --raw --skip-column-names
' > "$work_dir/counts.tsv"
SELECT 'monsters', COUNT(*), COALESCE(SUM(enabled=1),0),
       COALESCE(SUM(level IS NOT NULL AND max_hp IS NOT NULL),0)
FROM god2_game.monsters
UNION ALL
SELECT 'monster_spawns', COUNT(*), COALESCE(SUM(enabled=1),0),
       COALESCE(SUM(position_x IS NOT NULL AND position_y IS NOT NULL),0)
FROM god2_game.monster_spawns
UNION ALL
SELECT 'monster_drops', COUNT(*), COALESCE(SUM(enabled=1),0),
       COALESCE(SUM(drop_rate IS NOT NULL),0)
FROM god2_game.monster_drops
UNION ALL
SELECT 'npcs', COUNT(*), COALESCE(SUM(enabled=1),0), 0
FROM god2_game.npcs
UNION ALL
SELECT 'npc_spawns', COUNT(*), COALESCE(SUM(enabled=1),0),
       COALESCE(SUM(position_x IS NOT NULL AND position_y IS NOT NULL),0)
FROM god2_game.npc_spawns
UNION ALL
SELECT 'npc_dialogs', COUNT(*), COALESCE(SUM(enabled=1),0), 0
FROM god2_game.npc_dialogs
UNION ALL
SELECT 'quests', COUNT(*), COALESCE(SUM(enabled=1),0),
       COALESCE(SUM(start_npc_id IS NOT NULL AND end_npc_id IS NOT NULL),0)
FROM god2_game.quests
UNION ALL
SELECT 'quest_objectives', COUNT(*), COALESCE(SUM(enabled=1),0), 0
FROM god2_game.quest_objectives
UNION ALL
SELECT 'quest_rewards', COUNT(*), COALESCE(SUM(enabled=1),0), 0
FROM god2_game.quest_rewards
UNION ALL
SELECT '__migration__', COALESCE(MAX(Version),0), 0, 0
FROM god2.__schemaversion;
SQL

python3 - "$work_dir/counts.tsv" "$output_json" "$output_md" "$container" <<'PYTHON'
from datetime import datetime, timezone
from pathlib import Path
import hashlib
import json
import subprocess
import sys

counts_path, json_path, markdown_path = map(Path, sys.argv[1:4])
container = sys.argv[4]
names = (
    "monsters", "monster_spawns", "monster_drops", "npcs", "npc_spawns",
    "npc_dialogs", "quests", "quest_objectives", "quest_rewards",
)
counts = {}
for line in counts_path.read_text(encoding="utf-8").splitlines():
    name, *numbers = line.split("\t")
    if name in counts or len(numbers) != 3:
        raise ValueError(f"Invalid count row: {line!r}")
    values = tuple(map(int, numbers))
    if any(value < 0 for value in values):
        raise ValueError(f"Negative count: {line!r}")
    counts[name] = values
if set(counts) != set(names) | {"__migration__"}:
    raise ValueError(f"Unexpected table names: {set(counts)}")
if any(enabled > total or measured > total for name, (total, enabled, measured)
       in counts.items() if name != "__migration__"):
    raise ValueError("A table count exceeds its total")

source_paths = {
    "monsters": "db/imports/official/monsters/monsters.official.json",
    "spawns": "db/imports/official/spawns/spawns.official.json",
    "drops": "db/imports/official/drop_tables/drop_tables.official.json",
    "npcs": "db/imports/official/npcs/npcs.official.json",
    "dialogs": "db/imports/official/dialogs/dialogs.official.json",
    "quests": "db/imports/official/quests/quests.official.json",
    "rewards": "db/imports/official/rewards/rewards.official.json",
}
sources = {name: {"path": path, "sha256": hashlib.sha256(Path(path).read_bytes()).hexdigest()}
           for name, path in source_paths.items()}
commit = subprocess.check_output(["git", "rev-parse", "HEAD"], text=True).strip()
snapshot = {
    "schema": "God2GameplayCatalogCountSnapshot/1",
    "generatedAtUtc": datetime.now(timezone.utc).isoformat(),
    "repositoryCommit": commit,
    "database": container,
    "schemaVersion": counts.pop("__migration__")[0],
    "productionDatabaseTouched": False,
    "authorityBoundary": "Formal DB counts and supplemental client recovery counts are separate namespaces; no direct ID join or gameplay promotion is inferred.",
    "tables": {name: dict(zip(("rows", "enabledRows", "measuredRows"), counts[name])) for name in names},
    "supplementalSources": sources,
}
json_path.parent.mkdir(parents=True, exist_ok=True)
json_path.write_text(json.dumps(snapshot, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
lines = [
    "# Gameplay catalog count snapshot", "",
    f"- Generated UTC: `{snapshot['generatedAtUtc']}`",
    f"- Repository commit: `{commit}`",
    f"- DB schema version: `{snapshot['schemaVersion']}`", "",
    "| Formal DB table | Rows | Enabled | Measured rows |",
    "|---|---:|---:|---:|",
]
for name in names:
    total, enabled, measured = counts[name]
    lines.append(f"| `{name}` | {total} | {enabled} | {measured} |")
lines += [
    "", "Measured rows mean level+HP for monsters, X+Y for spawns, known drop rate for monster drops, and start+end NPC for quests; other rows show zero in this column by design.",
    "", "Formal DB row presence and `enabled` do not establish live gameplay parity. Supplemental sources are pinned by SHA-256 in the JSON output and must not be joined to Formal IDs by row count.",
    "", "No database writes were performed.", "",
]
markdown_path.write_text("\n".join(lines), encoding="utf-8")
print(markdown_path.read_text(encoding="utf-8"))
print(f"Wrote {json_path} and {markdown_path}")
PYTHON
