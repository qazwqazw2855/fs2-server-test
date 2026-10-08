#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"

if ss -ltn | awk 'NR>1 {print $4}' | rg ':6002$' >/dev/null; then
  echo "6002 已被占用，停止"
  exit 1
fi

set -a
. /home/ubuntu/.config/god2/server-v2.env
set +a
test "${GOD2_DB_HOST:-}" = "127.0.0.1"
test "${GOD2_DB_PORT:-}" = "3308"

if test -z "${GOD2_TEST_PASSWORD:-}"; then
  read -r -s -p 'god2test 測試帳號密碼：' GOD2_TEST_PASSWORD </dev/tty
  printf '\n'
fi
test -n "$GOD2_TEST_PASSWORD"
export GOD2_TEST_PASSWORD

for name in $(compgen -v GOD2_PROBE_ || true); do
  unset "$name"
done
export GOD2_TEST_ACCOUNT=god2test
export GOD2_EXPECTED_CHARACTER=test001
export GOD2_EXPECTED_CHARACTER_ID=1
export GOD2_PROBE_PORT=6002
export GOD2_PROBE_LOCAL_ADDRESS=127.0.0.1
export GOD2_PROBE_HOLD_SECONDS=0
export GOD2_PROBE_EXPECTED_NPC_HANDLES=3793

work="$(mktemp -d /tmp/god2-host-lifecycle-XXXXXX)"
printf '驗證紀錄：%s\n' "$work"
git rev-parse HEAD > "$work/source-commit.txt"
progress_before="$(sha256sum progress.json)"

server_pid=""
holder_pid=""
cleanup() {
  result=$?
  trap - EXIT
  for pid in "$holder_pid" "$server_pid"; do
    if test -n "$pid"; then
      kill "$pid" 2>/dev/null || true
      wait "$pid" 2>/dev/null || true
    fi
  done
  printf '\nLifecycle exit code：%s\n紀錄：%s\n' "$result" "$work"
  exit "$result"
}
trap cleanup EXIT

for project in \
  src/God2.ServerV2.Host/God2.ServerV2.Host.csproj \
  tools/God2.ServerV2.LoginProbe/God2.ServerV2.LoginProbe.csproj
do
  if ! dotnet build "$project" -c Release --verbosity minimal \
    >> "$work/build.log" 2>&1
  then
    tail -n 40 "$work/build.log"
    exit 1
  fi
done

GOD2_BIND=127.0.0.1 \
GOD2_PORT=6002 \
GOD2_ADVERTISED_ADDRESS=127.0.0.1 \
GOD2_ENABLE_MOVEMENT_PERSISTENCE=0 \
GOD2_ENFORCE_MOVEMENT_BOUNDS=1 \
GOD2_ENABLE_MERCHANT_EXECUTION=0 \
GOD2_ENABLE_MERCHANT_SALE_EXECUTION=0 \
dotnet src/God2.ServerV2.Host/bin/Release/net10.0/God2.ServerV2.Host.dll \
  > "$work/server.log" 2>&1 &
server_pid=$!

ready=0
for attempt in {1..100}; do
  kill -0 "$server_pid" 2>/dev/null || {
    tail -n 40 "$work/server.log"
    exit 1
  }
  if rg -q 'Listening on 127\.0\.0\.1:6002' "$work/server.log"; then
    ready=1
    break
  fi
  sleep 0.1
done
test "$ready" = 1 || { echo "Host 啟動逾時"; exit 1; }

probe="tools/God2.ServerV2.LoginProbe/bin/Release/net10.0/God2.ServerV2.LoginProbe.dll"
released=0

wait_release() {
  local count
  for attempt in {1..50}; do
    count="$(rg -c 'World presence released: character=1;' \
      "$work/server.log" || true)"
    if test "${count:-0}" -ge "$released"; then
      kill -0 "$server_pid"
      return 0
    fi
    sleep 0.1
  done
  echo "presence 清理逾時"
  tail -n 40 "$work/server.log"
  return 1
}

run_case() {
  local label="$1" mode="$2" expected="$3"
  echo "=== $label ==="
  if ! env "$mode=1" dotnet "$probe" > "$work/$label.log" 2>&1; then
    tail -n 40 "$work/$label.log"
    tail -n 40 "$work/server.log"
    return 1
  fi
  rg -F "$expected" "$work/$label.log"
  released=$((released + 1))
  wait_release
}

run_case logout-1 GOD2_PROBE_VERIFY_LOGOUT 'World 正式登出測試成功'
run_case logout-2 GOD2_PROBE_VERIFY_LOGOUT 'World 正式登出測試成功'
run_case pending GOD2_PROBE_VERIFY_PENDING_OWNERSHIP \
  'Pending ownership 阻擋第二次登入測試成功'

echo "=== active-duplicate ==="
GOD2_PROBE_HOLD_SECONDS=15 dotnet "$probe" \
  > "$work/holder.log" 2>&1 &
holder_pid=$!

ready=0
for attempt in {1..100}; do
  kill -0 "$holder_pid" 2>/dev/null || {
    tail -n 40 "$work/holder.log"
    exit 1
  }
  if rg -q 'NPC Spawn 完整接收：3793' "$work/holder.log"; then
    ready=1
    break
  fi
  sleep 0.1
done
test "$ready" = 1 || { echo "World 連線建立逾時"; exit 1; }

if ! GOD2_PROBE_LOCAL_ADDRESS=127.0.0.2 \
  GOD2_PROBE_EXPECT_DUPLICATE_LOGIN=1 \
  dotnet "$probe" > "$work/duplicate.log" 2>&1
then
  tail -n 40 "$work/duplicate.log"
  tail -n 40 "$work/server.log"
  exit 1
fi
rg -F '重複登入拒絕測試成功' "$work/duplicate.log"
kill -0 "$holder_pid"
if ! wait "$holder_pid"; then
  holder_pid=""
  tail -n 40 "$work/holder.log"
  exit 1
fi
holder_pid=""
rg -F 'World 保持在線測試成功：15 秒' "$work/holder.log"
released=$((released + 1))
wait_release

run_case idle GOD2_PROBE_VERIFY_IDLE_TIMEOUT \
  'World 30 秒閒置逾時測試成功'
run_case after-idle GOD2_PROBE_VERIFY_LOGOUT 'World 正式登出測試成功'

run_case movement GOD2_PROBE_VERIFY_MOVEMENT \
  'World 連續移動、雙 ACK 與連線維持測試成功'
run_case duplicate-movement GOD2_PROBE_VERIFY_DUPLICATE_MOVEMENT \
  'World 重複移動序號拒絕、無第二次 ACK 測試成功'
run_case after-duplicate-movement GOD2_PROBE_VERIFY_LOGOUT \
  'World 正式登出測試成功'

python3 - "$work/server.log" <<'PY'
import sys
from pathlib import Path
text = Path(sys.argv[1]).read_text()
assert text.count("Listening on 127.0.0.1:6002") == 1
assert text.count("World presence entered: character=1;") == 9
assert text.count("World presence released: character=1;") == 9
assert "World idle timeout after 30 seconds." in text
print("PASS: 同一 Host 完成全部案例；presence 進入／釋放各 9 次")
PY

test "$progress_before" = "$(sha256sum progress.json)"
echo "PASS: progress.json 未變動"
