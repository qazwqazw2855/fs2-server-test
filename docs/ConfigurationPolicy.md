# Configuration Policy

`config/` 只保留營運必要設定：

- `server.json`
- `database.json`
- `network.json`
- `logging.json`
- `security.json`
- `localization.json`
- `persistence.json`
- `rates.json`

官方固定玩法與流程不進 Config。`rates.json` 目前只允許 `experienceRate` 與 `dropRate`。

所有 `config/*.json` 必須使用 UTF-8、不得 BOM、4 spaces、繁體中文說明。每個 JSON 必須包含 `_comment`、`_version`、`_example`；每個正式欄位必須有 `_欄位名稱`，且說明包含用途、預設值、可接受範圍、是否需要重新啟動、影響。

啟動時 validation 必須指出檔名、欄位、目前值、合法值或範圍，不得只顯示 `Configuration Error`。
