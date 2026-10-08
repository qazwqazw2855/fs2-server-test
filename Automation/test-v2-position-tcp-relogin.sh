#!/usr/bin/env bash
# Server authoritative DB restore only; no client screen-position claim.
set -euo pipefail
set +x
ulimit -c 0
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"
set -a
. /home/ubuntu/.config/god2/server-v2.env
set +a
set +x
test "${GOD2_DB_HOST:-}" = 127.0.0.1
test "${GOD2_DB_PORT:-}" = 3308
container="${GOD2_DB_CONTAINER:-god2-runtime-db-test}"
test "$container" = god2-runtime-db-test
test "$(sudo docker port "$container" 3306/tcp)" = '127.0.0.1:3308'
if ss -ltn | awk 'NR>1 {print $4}' | rg ':6002$' >/dev/null; then
  echo '6002 已被占用，停止'; exit 1
fi
for name in $(compgen -v GOD2_PROBE_ || true); do unset "$name"; done
work="$(mktemp -d /tmp/god2-position-tcp-relogin-XXXXXX)"
echo "驗證紀錄：$work"
git rev-parse HEAD > "$work/source-commit.txt"
progress_before="$(sha256sum progress.json)"
fixture_name="p$(python3 -c 'import secrets; print(secrets.token_hex(5))')"
export GOD2_TEST_PASSWORD="$(python3 -c 'import secrets; print(secrets.token_hex(12))')"
password_hash="$(python3 - <<'PY'
import os, secrets, hashlib, base64
salt=secrets.token_bytes(16)
hash=hashlib.pbkdf2_hmac('sha256',os.environ['GOD2_TEST_PASSWORD'].encode(),salt,100000)
print('pbkdf2-sha256$100000$'+base64.b64encode(salt).decode()+'$'+base64.b64encode(hash).decode())
PY
)"
server_pid=''
# Zero means no validated ID has been received; never interpolate unchecked IDs.
account_id=0
character_id=0
fixture_account_token="$(python3 -c 'import secrets; print(secrets.token_hex(16))')"
db_admin() {
  # MariaDB errors may echo SQL containing password_hash. Never retain raw stderr.
  sudo docker exec -i "$container" sh -lc '
    exec mariadb -uroot -p"$(cat "$MARIADB_ROOT_PASSWORD_FILE")" --batch --raw --skip-column-names
  ' 2>/dev/null
}
cleanup() {
  local result=$? cleanup_failed=0 remaining
  trap - EXIT INT TERM
  set +e
  echo "Original test exit code: $result" | tee "$work/cleanup.log"
  if test -n "$server_pid"; then
    kill "$server_pid" 2>/dev/null || true
    wait "$server_pid" 2>/dev/null || true
  fi
  if remaining="$(db_admin <<SQL
USE god2_player;
START TRANSACTION;
-- The random ownership token also protects the fallback when COMMIT succeeded
-- but its IDs never reached the shell. Account-only partial creation is safe.
SET @fixture_account = (
 SELECT account_id FROM accounts
 WHERE username='$fixture_name' AND concurrency_token='$fixture_account_token'
   AND ($account_id=0 OR account_id=$account_id)
);
DELETE c FROM characters c JOIN accounts a ON a.account_id=c.account_id
WHERE a.account_id=@fixture_account AND a.username='$fixture_name'
  AND a.concurrency_token='$fixture_account_token'
  AND ($character_id=0 OR c.character_id=$character_id)
  AND c.name='$fixture_name' AND c.admin_note='PositionTcpReloginFixture';
DELETE FROM accounts
WHERE account_id=@fixture_account AND username='$fixture_name'
  AND concurrency_token='$fixture_account_token'
  AND NOT EXISTS (SELECT 1 FROM characters WHERE characters.account_id=accounts.account_id);
COMMIT;
SELECT (SELECT COUNT(*) FROM accounts WHERE username='$fixture_name'
          OR ($account_id<>0 AND account_id=$account_id))
 + (SELECT COUNT(*) FROM characters WHERE name='$fixture_name'
          OR ($character_id<>0 AND character_id=$character_id));
SQL
)"; then
    echo "remaining_position_tcp_fixture=$remaining" | tee -a "$work/cleanup.log"
    if test "$remaining" != 0; then
      echo 'Fixture 清理失敗：剩餘數量未確認為 0；保留資料供檢查。' | tee -a "$work/cleanup.log"
      cleanup_failed=1
    fi
  else
    echo 'Fixture 清理失敗：DB 操作失敗，剩餘數量未知；原始 DB 錯誤已抑制以保護密碼雜湊。' | tee -a "$work/cleanup.log"
    cleanup_failed=1
  fi
  if test "$progress_before" != "$(sha256sum progress.json)"; then
    echo 'FAIL: progress.json 驗證不符' | tee -a "$work/cleanup.log"
    cleanup_failed=1
  fi
  unset GOD2_TEST_PASSWORD password_hash
  echo "Original test exit code: $result; cleanup_failed=$cleanup_failed; 紀錄：$work" | tee -a "$work/cleanup.log"
  # Keep the original failure status; a cleanup-only failure still fails the run.
  if test "$result" -ne 0; then exit "$result"; fi
  if test "$cleanup_failed" -ne 0; then exit 1; fi
  exit 0
}
trap cleanup EXIT
trap 'exit 130' INT
trap 'exit 143' TERM
bounds="$(db_admin <<'SQL'
SELECT COUNT(*) FROM god2_game.maps WHERE map_id=170015007 AND enabled=1
 AND 16 BETWEEN minimum_x AND maximum_x AND 17 BETWEEN minimum_x AND maximum_x
 AND 14 BETWEEN minimum_y AND maximum_y AND 15 BETWEEN minimum_y AND maximum_y;
SQL
)"
test "$bounds" = 1
if ! ids="$(db_admin <<SQL
USE god2_player;
START TRANSACTION;
INSERT INTO accounts (username,password_hash,status,concurrency_token)
VALUES ('$fixture_name','$password_hash','Active','$fixture_account_token');
SET @account=LAST_INSERT_ID();
INSERT INTO characters
 (account_id,name,class_code,gender_code,appearance_code,map_id,position_x,position_y,
 status,enabled,admin_note,runtime_version,concurrency_token)
VALUES (@account,'$fixture_name','Swordsman','Female','Appearance1',170015007,17,15,
 'Active',1,'PositionTcpReloginFixture',0,REPLACE(UUID(),'-',''));
SET @character=LAST_INSERT_ID();
COMMIT;
SELECT @account,@character;
SQL
)"; then
  unset password_hash
  echo 'Fixture 建立失敗：DB 操作失敗；原始 DB 錯誤已抑制以保護密碼雜湊。'
  exit 1
fi
unset password_hash
if [[ "$ids" =~ ^([0-9]+)[[:blank:]]+([0-9]+)$ ]] &&
   test "${BASH_REMATCH[1]}" -gt 1 && test "${BASH_REMATCH[2]}" -gt 1; then
  account_id="${BASH_REMATCH[1]}"
  character_id="${BASH_REMATCH[2]}"
else
  echo 'Fixture ID 回應無效；將使用 ownership token 安全清理。'
  exit 1
fi
snapshot() {
  db_admin <<SQL
SELECT map_id,position_x,position_y,runtime_version,concurrency_token
FROM god2_player.characters WHERE character_id=$character_id AND account_id=$account_id
 AND name='$fixture_name' AND admin_note='PositionTcpReloginFixture';
SQL
}
snapshot > "$work/db-before.tsv"
for project in src/God2.ServerV2.Host/God2.ServerV2.Host.csproj tools/God2.ServerV2.LoginProbe/God2.ServerV2.LoginProbe.csproj; do
  if ! dotnet build "$project" -c Release --verbosity minimal >> "$work/build.log" 2>&1; then
    tail -n 40 "$work/build.log"; exit 1
  fi
done
GOD2_BIND=127.0.0.1 GOD2_PORT=6002 GOD2_ADVERTISED_ADDRESS=127.0.0.1 \
GOD2_ENABLE_MOVEMENT_PERSISTENCE=1 GOD2_ENFORCE_MOVEMENT_BOUNDS=1 \
GOD2_ENABLE_MERCHANT_EXECUTION=0 GOD2_ENABLE_MERCHANT_SALE_EXECUTION=0 \
dotnet src/God2.ServerV2.Host/bin/Release/net10.0/God2.ServerV2.Host.dll > "$work/server.log" 2>&1 &
server_pid=$!
ready=0
for attempt in {1..100}; do
  kill -0 "$server_pid"
  if rg -q 'Listening on 127\.0\.0\.1:6002' "$work/server.log"; then ready=1; break; fi
  sleep 0.1
done
test "$ready" = 1
export GOD2_TEST_ACCOUNT="$fixture_name" GOD2_EXPECTED_CHARACTER="$fixture_name"
export GOD2_EXPECTED_CHARACTER_ID="$character_id" GOD2_PROBE_PORT=6002
export GOD2_PROBE_LOCAL_ADDRESS=127.0.0.1 GOD2_PROBE_EXPECTED_NPC_HANDLES=3793
probe=tools/God2.ServerV2.LoginProbe/bin/Release/net10.0/God2.ServerV2.LoginProbe.dll
run_probe() {
  local label="$1"; shift
  if ! env "$@" dotnet "$probe" > "$work/$label.log" 2>&1; then
    tail -n 40 "$work/$label.log"; tail -n 40 "$work/server.log"; return 1
  fi
}
# Release log occurs before sessionContext.Dispose; wait for the same connection's Closed.
wait_closed() {
  python3 - "$work/server.log" "$character_id" "$1" <<'PY'
import sys,time,re
from pathlib import Path
path,character,expected=sys.argv[1:]
for _ in range(100):
    text=Path(path).read_text()
    released=re.findall(r'\[([^\]]+)\] World presence released: character='+character+r';',text)
    if len(released)==int(expected) and all('['+cid+'] Closed stage=Closed' in text for cid in released):
        print(f'PASS: {expected} World session(s) fully released');break
    time.sleep(.1)
else: raise SystemExit('World session 完整釋放逾時')
PY
  kill -0 "$server_pid"
}
run_probe movement GOD2_PROBE_VERIFY_MOVEMENT=1 GOD2_PROBE_MOVEMENT_DISCONNECT=1
rg -F 'World 雙 ACK 後 TCP 斷線測試成功' "$work/movement.log"
wait_closed 1
snapshot > "$work/db-committed.tsv"
run_probe relogin GOD2_PROBE_VERIFY_LOGOUT=1
rg -F 'World 正式登出測試成功' "$work/relogin.log"
wait_closed 2
snapshot > "$work/db-relogin.tsv"
python3 - "$work" "$character_id" <<'PY' | tee "$work/acceptance.log"
import sys,re
from pathlib import Path
p=Path(sys.argv[1]); cid=sys.argv[2]
before=(p/'db-before.tsv').read_text().split()
after=(p/'db-committed.tsv').read_text().split()
assert before[:4]==['170015007','17','15','0'],before
assert after[:4]==['170015007','16','15','2'],after
assert len(before[4])==len(after[4])==32 and before[4]!=after[4]
assert (p/'db-relogin.tsv').read_text()==(p/'db-committed.tsv').read_text()
text=(p/'server.log').read_text()
entries=re.findall(r'\[([^\]]+)\] World presence entered: character='+cid+r'; map=170015007; position=\((\d+),(\d+)\); runtimeVersion=(\d+);',text)
assert len(entries)==2 and entries[0][1:]==('17','15','0') and entries[1][1:]==('16','15','2'),entries
assert entries[0][0]!=entries[1][0]
assert text.index('['+entries[0][0]+'] Closed stage=Closed') < text.index('['+entries[1][0]+'] World presence entered:')
for sequence,(x,y) in enumerate([(16,14),(16,15)],1):
    assert re.search(rf'\[{re.escape(entries[0][0])}\] RX WorldMovement bytes=10 x={x} y={y} sequence={sequence} .*persistence=Updated version={sequence};',text)
    assert f'World Movement ACK verified: sequence={sequence}' in (p/'movement.log').read_text()
assert 'Movement persistence: Enabled' in text
assert 'Merchant execution wiring: Disabled; SELL: Disabled; BUY/SELL evidence gates: Blocked' in text
assert text.count('Listening on 127.0.0.1:6002')==1
assert 'Unexpected error:' not in text
print('PASS: TCP 雙 ACK → MariaDB CAS (16,15), version 0→2, token changed → 完整釋放 → TCP relogin authoritative position=(16,15), version=2')
print('Server authoritative DB restore only; client screen-position restore remains unverified.')
print('DB before:', '\t'.join(before));print('DB committed:', '\t'.join(after))
for entry in entries: print('World session:',entry)
PY
