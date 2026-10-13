#!/usr/bin/env bash
set -euo pipefail
test "$#" -le 1
case "${1:-}" in
  ""|--persistence) ;;
  *) echo "STOP: invalid argument"; exit 1 ;;
esac
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"
test "$(git branch --show-current)" = "codex/inventory-grant-20261003"
container="god2-regression-db-isolated"
test "$(sudo docker port "$container" 3306/tcp)" = "127.0.0.1:3311"
test "$(sudo docker inspect -f '{{index .Config.Labels "god2.purpose"}}' "$container")" = \
  "isolated-regression-baseline"

python3 - "$@" <<'PY_EXEC'
import json
import os
import re
from pathlib import Path

path = Path("/home/ubuntu/games/god2-regression-db-secrets/test-credentials.json")
assert path.is_file()
assert path.stat().st_mode & 0o077 == 0, "STOP: credentials permissions"
c = json.loads(path.read_text())
assert c["host"] == "127.0.0.1" and c["port"] == 3311
assert c["container"] == "god2-regression-db-isolated"
assert re.fullmatch(r"regtest_[0-9a-f]{16}", c["user"])
assert all(re.fullmatch(r"[0-9a-f]{64}", c[key])
           for key in ("password", "loginPassword"))

env = {key: value for key, value in os.environ.items()
       if not key.startswith("GOD2_")}
env.update({
    "GOD2_RUN_DB_INTEGRATION": "1",
    "GOD2_DB_HOST": c["host"],
    "GOD2_DB_PORT": str(c["port"]),
    "GOD2_DB_USER": c["user"],
    "GOD2_DB_PASSWORD": c["password"],
    "GOD2_TEST_PASSWORD": c["loginPassword"],
})
import secrets
import subprocess
import sys
import tempfile

container = c["container"]
host = subprocess.check_output([
    "sudo", "docker", "inspect", "-f",
    "{{range .NetworkSettings.Networks}}{{.Gateway}}{{end}}", container,
], text=True).strip()
assert re.fullmatch(r"[0-9]+\.[0-9]+\.[0-9]+\.[0-9]+", host)

user = "grantfault_" + secrets.token_hex(8)
password = secrets.token_hex(32)
env["GOD2_GRANT_FAULT_USER"] = user
env["GOD2_GRANT_FAULT_PASSWORD"] = password

def admin(sql):
    result = subprocess.run([
        "sudo", "docker", "exec", "-i", container, "sh", "-lc",
        'set -eu; password="$(cat /run/secrets/regression-root-password)"; '
        'exec mariadb --no-defaults -uroot --password="$password" '
        '--batch --raw --skip-column-names',
    ], input=sql, text=True, capture_output=True)
    if result.returncode:
        raise RuntimeError("Isolated fault account SQL failed; credentials omitted.")
    return result.stdout.strip()

assert admin(f"SELECT COUNT(*) FROM mysql.user WHERE User='{user}';") == "0"
grant = f"CREATE USER '{user}'@'{host}' IDENTIFIED BY '{password}';\n"
for schema in ("god2_game", "god2_player"):
    grant += f"GRANT CREATE TEMPORARY TABLES ON {schema}.* TO '{user}'@'{host}';\n"
for table, permissions in (
    ("god2_player.characters", "SELECT"),
    ("god2_player.player_inventory_state", "SELECT,UPDATE"),
    ("god2_player.character_inventory", "SELECT,INSERT,UPDATE,DELETE"),
    ("god2_player.inventory_item_identity_sequence", "SELECT,INSERT"),
    ("god2_player.inventory_transaction_idempotency", "SELECT,INSERT"),
    ("god2_player.inventory_audit_ledger", "SELECT"),
    ("god2_game.items", "SELECT"),
):
    grant += f"GRANT {permissions} ON {table} TO '{user}'@'{host}';\n"

code = 1
try:
    admin(grant)
    print("PASS: isolated baseline fault account prepared", flush=True)
    if sys.argv[1:] == ["--persistence"]:
        source = Path("Automation/test-v2-all-no-progress.sh").read_text()
        match = re.search(r"^special=\(\n(.*?)^\)", source, re.M | re.S)
        assert match is not None
        names = match.group(1).split()
        assert all(re.fullmatch(r"[A-Za-z0-9_]+", name) for name in names)
        filter_value = "&".join("FullyQualifiedName!~" + name for name in names)
        results = tempfile.mkdtemp(prefix="god2-persistence-isolated-", dir="/tmp")
        print("persistence_results=" + results, flush=True)
        code = subprocess.call([
            "dotnet", "test",
            "tests/God2.ServerV2.Persistence.IntegrationTests/"
            "God2.ServerV2.Persistence.IntegrationTests.csproj",
            "-c", "Release", "--filter", filter_value,
            "--logger", "trx;LogFileName=Persistence.Integration.trx",
            "--results-directory", results, "--verbosity", "minimal",
        ], env=env)
        if code == 0:
            import xml.etree.ElementTree as ET
            counters = ET.parse(
                Path(results) / "Persistence.Integration.trx"
            ).getroot().find(".//{*}Counters")
            assert counters is not None
            assert all(counters.get(key) == "80"
                       for key in ("total", "executed", "passed"))
            assert counters.get("failed") == counters.get("notExecuted") == "0"
            print("PASS: Persistence 80/80", flush=True)
    else:
        code = subprocess.call([
            "bash", "Automation/test-v2-all-no-progress.sh"
        ], env=env)
finally:
    admin(f"DROP USER IF EXISTS '{user}'@'{host}';")
    assert admin(f"SELECT COUNT(*) FROM mysql.user WHERE User='{user}';") == "0"
    print("PASS: isolated baseline fault account removed", flush=True)
sys.exit(code)
PY_EXEC
