#!/usr/bin/env python3
"""Read-only recovery identity audit; does not approve runtime content."""
import hashlib
import json
import subprocess
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
source = ROOT / "db/imports/official/monsters/monsters.official.json"
raw = source.read_bytes()
data = json.loads(raw)
rows = data["records"]
assert data["recordCount"] == len(rows) == 208

used = set()
expected = {}
keys = set()
collisions = 0
for index, row in enumerate(rows):
    key = None
    for field in ("id", "clientItemId", "clientMonsterId", "clientNpcId",
                  "clientQuestId", "clientSkillId", "clientMapId"):
        if field in row:
            value = row[field]
            candidate = value if isinstance(value, str) else json.dumps(
                value, ensure_ascii=False, separators=(",", ":"))
            if candidate.strip():
                key = candidate
                break
    if key is None:
        key = f"monsters:{index:06d}"
    # C# string.Length counts UTF-16 code units.
    if len(key.encode("utf-16-le")) // 2 > 255:
        key = "sha256:" + hashlib.sha256(key.encode()).hexdigest()
    assert key not in keys, f"Duplicate RecordKey: {key}"
    keys.add(key)
    identity = int.from_bytes(hashlib.sha256(key.encode()).digest()[:4],
                              "little") & 0x7fffffff
    identity = identity or 1
    while identity in used:
        collisions += 1
        identity += 1
        if identity == 2147483647:
            identity = 1
    used.add(identity)
    client = row["clientMonsterId"]
    assert isinstance(client, int) and client > 0
    expected[identity] = (f"monster_{client}", key)

sql = """
SELECT JSON_OBJECT('kind','legacy','id',Id,'code',Code,
                   'name',Name,'run',ContentRecoveryRunId)
FROM god2.monsters ORDER BY Id;
SELECT JSON_OBJECT('kind','formal','id',monster_id,'code',code,
                   'name',name_zh_tw,'enabled',enabled)
FROM god2_game.monsters ORDER BY monster_id;
"""
result = subprocess.run(
    ["sudo", "docker", "exec", "-i", "god2-runtime-db-test", "sh", "-lc",
     'password="$(cat "$MARIADB_ROOT_PASSWORD_FILE")"; '
     'exec mariadb -uroot -p"$password" --batch --raw --skip-column-names'],
    input=sql, capture_output=True, text=True)
if result.returncode:
    raise RuntimeError(result.stderr)
observations = [json.loads(line) for line in result.stdout.splitlines()
                if line.strip()]
legacy = {row["id"]: row for row in observations if row["kind"] == "legacy"}
formal = [row for row in observations if row["kind"] == "formal"]
assert len(legacy) == 208
assert set(legacy) == set(expected), "Recovery ID set differs from source"
for identity, (code, key) in expected.items():
    assert legacy[identity]["code"] == code, f"Code mismatch: {key}"
runs = {row["run"] for row in legacy.values()}
assert runs == {"88f531a0-4d5c-469e-9fb5-5c9059dcda8d"}, runs
assert len(formal) == 9, "Formal count changed; review baseline"
for row in formal:
    identity = row["id"]
    assert identity in expected
    assert row["code"] == expected[identity][0]
    assert row["enabled"] == 0, "Unexpected enabled monster"
    print(f'{identity}\t{row["code"]}\t{row["name"]}\t{expected[identity][1]}')

print("source_sha256=" + hashlib.sha256(raw).hexdigest())
print(f"PASS: recovery identity/code 208/208; Formal identity/code 9/9")
print(f"collision_adjustments={collisions}")
print("Scope: import identity only; names, combat values and runtime approval are separate.")
