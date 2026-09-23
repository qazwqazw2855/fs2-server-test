#!/usr/bin/env bash
set -euo pipefail

if [ "$#" -ne 0 ]; then
    echo "此工具不接受命令列參數；請使用環境變數設定。" >&2
    exit 1
fi

repository_root="$(
    cd "$(dirname "${BASH_SOURCE[0]}")/.." &&
    pwd
)"

environment_file="${GOD2_SERVER_V2_ENV_FILE:-/home/ubuntu/.config/god2/server-v2.env}"
character_id="${GOD2_EXPECTED_CHARACTER_ID:-1}"
account_name="${GOD2_TEST_ACCOUNT:-god2test}"
character_name="${GOD2_EXPECTED_CHARACTER:-test001}"
probe_port="${GOD2_PROBE_PORT:-6001}"

if ! [[ "$character_id" =~ ^[1-9][0-9]*$ ]]; then
    echo "GOD2_EXPECTED_CHARACTER_ID 必須是正整數。" >&2
    exit 1
fi

if ! [[ "$probe_port" =~ ^[0-9]+$ ]] ||
    [ "$probe_port" -lt 1 ] ||
    [ "$probe_port" -gt 65535 ]; then
    echo "GOD2_PROBE_PORT 必須介於 1 到 65535。" >&2
    exit 1
fi

for command_name in dotnet mariadb ss systemctl sudo rg; do
    if ! command -v "$command_name" >/dev/null 2>&1; then
        echo "缺少必要指令：$command_name" >&2
        exit 1
    fi
done

if ! sudo test -r "$environment_file"; then
    echo "無法讀取 Server V2 環境檔：$environment_file" >&2
    exit 1
fi

if ! systemctl is-active --quiet god2-server-v2.service; then
    echo "god2-server-v2.service 目前不是 active。" >&2
    exit 1
fi

if ss -Htn state established |
    awk -v port=":${probe_port}" '
        $4 ~ port "$" { found=1 }
        END { exit(found ? 0 : 1) }
    '; then
    echo "Port ${probe_port} 目前有已建立連線，拒絕重設角色狀態。" >&2
    exit 1
fi

if [ -z "${GOD2_TEST_PASSWORD:-}" ]; then
    read -rsp "請輸入 ${account_name} 測試密碼：" GOD2_TEST_PASSWORD
    echo
fi

export GOD2_TEST_PASSWORD
export GOD2_TEST_ACCOUNT="$account_name"
export GOD2_EXPECTED_CHARACTER="$character_name"
export GOD2_EXPECTED_CHARACTER_ID="$character_id"
export GOD2_PROBE_PORT="$probe_port"
export GOD2_PROBE_VERIFY_PORTAL=1

cleanup()
{
    unset GOD2_TEST_PASSWORD
    unset GOD2_PROBE_VERIFY_PORTAL
}

trap cleanup EXIT

echo "=== Portal Probe Build ==="
dotnet build \
  "$repository_root/tools/God2.ServerV2.LoginProbe/God2.ServerV2.LoginProbe.csproj" \
  --configuration Release --verbosity minimal

echo "=== Portal Fixture Reset ==="

reset_output="$(
    sudo bash -c '
        set -euo pipefail

        environment_file="$1"
        character_id="$2"

        set -a
        . "$environment_file"
        set +a

        exec mariadb \
          --host="$GOD2_DB_HOST" \
          --port="$GOD2_DB_PORT" \
          --user="$GOD2_DB_USER" \
          --password="$GOD2_DB_PASSWORD" \
          --protocol=TCP \
          --batch \
          --raw \
          --skip-column-names \
          --database=god2_player \
          --execute="
UPDATE characters
SET map_id = 170015000,
    position_x = 249,
    position_y = 246,
    runtime_version = runtime_version + 1,
    concurrency_token = LOWER(REPLACE(UUID(), '\''-'\'', '\'''\'')),
    updated_at_utc = UTC_TIMESTAMP(6)
WHERE character_id = ${character_id}
  AND map_id IN (170015000, 170015007)
  AND enabled = 1
  AND deleted_at_utc IS NULL;

SELECT CONCAT('\''AFFECTED='\'', ROW_COUNT());

SELECT CONCAT(
    '\''STATE='\'',
    character_id, '\'','\'',
    map_id, '\'','\'',
    position_x, '\'','\'',
    position_y, '\'','\'',
    runtime_version, '\'','\'',
    concurrency_token
)
FROM characters
WHERE character_id = ${character_id};
"
    ' _ "$environment_file" "$character_id"
)"

printf '%s\n' "$reset_output"

if ! printf '%s\n' "$reset_output" |
    rg -q '^AFFECTED=1$'; then
    echo "Portal fixture reset 失敗；角色不存在、已停用或位於非允許地圖。" >&2
    exit 1
fi

if ! printf '%s\n' "$reset_output" |
    rg -q "^STATE=${character_id},170015000,249,246,"; then
    echo "Portal fixture reset 後狀態不符。" >&2
    exit 1
fi

echo "=== Portal LoginProbe ==="

cd "$repository_root"

printf '%s\n' "$GOD2_TEST_PASSWORD" | sudo env -i PATH=/usr/bin:/bin bash -c '
    set -euo pipefail
    IFS= read -r GOD2_TEST_PASSWORD
    export GOD2_TEST_PASSWORD

    set -a
    . "$1"
    set +a

    export GOD2_TEST_ACCOUNT="$3"
    export GOD2_EXPECTED_CHARACTER="$4"
    export GOD2_EXPECTED_CHARACTER_ID="$5"
    export GOD2_PROBE_PORT="$6"
    export GOD2_PROBE_VERIFY_PORTAL=1

    exec dotnet "$2/tools/God2.ServerV2.LoginProbe/bin/Release/net10.0/God2.ServerV2.LoginProbe.dll"
' _ "$environment_file" "$repository_root" "$account_name" "$character_name" "$character_id" "$probe_port"

echo "=== Portal Probe 完整閉環成功 ==="
