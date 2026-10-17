"""Guarded baseline worker; invoke through test-v2-all-isolated.sh."""
from pathlib import Path
import sys
from regression_evidence import run, require, save
from regression_db_state import collect, inspect_targets, docker


def finish(code, evidence, admin, user, host):
    cleanup_ok = False
    try:
        admin(f"DROP USER IF EXISTS '{user}'@'{host}';")
        require(admin(f"SELECT COUNT(*) FROM mysql.user WHERE User='{user}';") == "0", 'Isolated regression safety check failed')
        cleanup_ok = True
        print("PASS: isolated baseline fault account removed", flush=True)
    except Exception:
        print("STOP: isolated baseline fault account cleanup failed; details omitted", flush=True)
    save(evidence / "baseline-cleanup.json", {"temporary_fault_account_removed": cleanup_ok})
    try:
        checks_ok = collect(evidence / "db-after.json")
    except Exception:
        checks_ok = False
        print("STOP: final DB evidence collection failed", flush=True)
    if code == 0 and (not cleanup_ok or not checks_ok):
        code = 1
    return code


def main():
    import json
    import os
    import re
    from pathlib import Path

    path = Path("/home/ubuntu/games/god2-regression-db-secrets/test-credentials.json")
    require(path.is_file(), 'Isolated regression safety check failed')
    require(path.stat().st_mode & 0o077 == 0, "STOP: credentials permissions")
    c = json.loads(path.read_text())
    require(c["host"] == "127.0.0.1" and c["port"] == 3311, 'Isolated regression safety check failed')
    require(c["container"] == "god2-regression-db-isolated", 'Isolated regression safety check failed')
    require(re.fullmatch(r"regtest_[0-9a-f]{16}", c["user"]), 'Isolated regression safety check failed')
    require(all(re.fullmatch(r"[0-9a-f]{64}", c[key])
               for key in ("password", "loginPassword")), 'Isolated regression safety check failed')

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
    inspect_targets()
    require(subprocess.check_output(["git", "branch", "--show-current"], text=True).strip() == "codex/inventory-grant-20261003", 'Isolated regression safety check failed')
    evidence = Path(os.environ["GOD2_REGRESSION_EVIDENCE"])
    require(collect(evidence / "db-before.json"), "Isolated DB preflight checks failed")
    env["GOD2_REGRESSION_EVIDENCE"] = str(evidence)
    host = docker("inspect", "-f", "{{range .NetworkSettings.Networks}}{{.Gateway}}{{end}}", container).stdout.strip()
    require(re.fullmatch(r"[0-9]+\.[0-9]+\.[0-9]+\.[0-9]+", host), 'Isolated regression safety check failed')

    user = "grantfault_" + secrets.token_hex(8)
    password = secrets.token_hex(32)
    env["GOD2_GRANT_FAULT_USER"] = user
    env["GOD2_GRANT_FAULT_PASSWORD"] = password

    def admin(sql):
        result = subprocess.run([
            "sudo", "docker", "--host=unix:///var/run/docker.sock", "exec", "-i", container, "sh", "-lc",
            'set -eu; password="$(cat /run/secrets/regression-root-password)"; '
            'exec mariadb --no-defaults -uroot --password="$password" '
            '--batch --raw --skip-column-names',
        ], input=sql, text=True, capture_output=True)
        if result.returncode:
            raise RuntimeError("Isolated fault account SQL failed; credentials omitted.")
        return result.stdout.strip()

    require(admin(f"SELECT COUNT(*) FROM mysql.user WHERE User='{user}';") == "0", 'Isolated regression safety check failed')
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
            require(match is not None, 'Isolated regression safety check failed')
            names = match.group(1).split()
            require(all(re.fullmatch(r"[A-Za-z0-9_]+", name) for name in names), 'Isolated regression safety check failed')
            filter_value = "&".join("FullyQualifiedName!~" + name for name in names)
            results = str(evidence / "persistence")
            Path(results).mkdir()
            print("persistence_results=" + results, flush=True)
            code = run([
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
                require(counters is not None, 'Isolated regression safety check failed')
                require(all(counters.get(key) == "80"
                           for key in ("total", "executed", "passed")), 'Isolated regression safety check failed')
                require(counters.get("failed") == counters.get("notExecuted") == "0", 'Isolated regression safety check failed')
                print("PASS: Persistence 80/80", flush=True)
        else:
            code = run([
                "bash", "Automation/test-v2-all-no-progress.sh"
            ], env=env)
    finally:
        code = finish(code, evidence, admin, user, host)
    return code


if __name__ == '__main__':
    try:
        sys.exit(main())
    except Exception as error:
        print('STOP: isolated regression worker failed (' + type(error).__name__ + '); sensitive details omitted', flush=True)
        sys.exit(1)
