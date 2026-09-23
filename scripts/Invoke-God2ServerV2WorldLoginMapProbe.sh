#!/usr/bin/env bash
set -euo pipefail

repository_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
environment_file="${GOD2_SERVER_V2_ENV_FILE:-/home/ubuntu/.config/god2/server-v2.env}"
test_port=6002

if [ "$#" -ne 0 ]; then
    echo "This probe takes no command-line arguments." >&2
    exit 1
fi

if ! sudo test -r "$environment_file"; then
    echo "Server environment file is not readable." >&2
    exit 1
fi

if ss -Htlpn | rg -q '127[.]0[.]0[.]1:6002[[:space:]]'; then
    echo "Port 6002 is occupied; no server was started." >&2
    exit 1
fi

if ss -Htn state established | awk '$4 ~ /:6001$/ { found=1 } END { exit(found ? 0 : 1) }'; then
    echo "An active game session exists on 6001; log out before using this account." >&2
    exit 1
fi

dotnet build "$repository_root/src/God2.ServerV2.Host/God2.ServerV2.Host.csproj" -c Release --verbosity minimal
dotnet build "$repository_root/tools/God2.ServerV2.LoginProbe/God2.ServerV2.LoginProbe.csproj" -c Release --verbosity minimal

read -rsp 'Test account password: ' test_password
echo
if [ -z "$test_password" ]; then
    echo "Test password is required." >&2
    exit 1
fi

# Password is supplied through stdin; no database write or fixture reset is performed.
printf '%s\n' "$test_password" | sudo bash -c '
    set -euo pipefail
    IFS= read -r GOD2_TEST_PASSWORD
    export GOD2_TEST_PASSWORD
    set -a
    . "$1"
    set +a
    root="$2"
    export GOD2_BIND=127.0.0.1
    export GOD2_ADVERTISED_ADDRESS=127.0.0.1
    export GOD2_PORT=6002
    export GOD2_ENABLE_MOVEMENT_PERSISTENCE=0
    export GOD2_PROBE_PORT=6002
    export GOD2_TEST_ACCOUNT=god2test
    export GOD2_EXPECTED_CHARACTER=test001
    export GOD2_EXPECTED_CHARACTER_ID=1
    export GOD2_PROBE_VERIFY_WORLD_LOGIN_MAP=1
    log="$(mktemp)"
    server_pid=""
    cleanup() {
        if [ -n "$server_pid" ]; then
            kill "$server_pid" 2>/dev/null || true
            wait "$server_pid" 2>/dev/null || true
        fi
        if [ -f "$log" ]; then
            rg "Listening on|World presence entered|World bootstrap completed|World login location rejected|Unexpected error|Stopped" "$log" || true
            rm -f "$log"
        fi
    }
    trap cleanup EXIT
    dotnet "$root/src/God2.ServerV2.Host/bin/Release/net10.0/God2.ServerV2.Host.dll" >"$log" 2>&1 &
    server_pid="$!"
    for attempt in {1..40}; do
        if ss -Htlpn | rg -q "127[.]0[.]0[.]1:6002[[:space:]]"; then
            break
        fi
        if ! kill -0 "$server_pid" 2>/dev/null; then
            echo "Isolated server exited unexpectedly." >&2
            exit 1
        fi
        sleep 0.25
    done
    if ! ss -Htlpn | rg -q "127[.]0[.]0[.]1:6002[[:space:]]"; then
        echo "Isolated server did not start." >&2
        exit 1
    fi
    dotnet "$root/tools/God2.ServerV2.LoginProbe/bin/Release/net10.0/God2.ServerV2.LoginProbe.dll"
' _ "$environment_file" "$repository_root"
