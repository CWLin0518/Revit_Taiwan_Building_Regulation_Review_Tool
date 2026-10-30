# MCP 伺服器：規格

工具 ID：`mcp-server`（伺服器名稱 `building-regulation-review`）
狀態：已實作；目前開放 `revit_status` 與防火檢討七個工具。架構、連線、工具清單與接入步驟見 [ARCHITECTURE.md](ARCHITECTURE.md)。

## 1. 需求

| 編號 | 需求 |
| --- | --- |
| MCP-1 | 本專案每一個法規檢討工具都必須透過本 MCP 提供給代理使用；新工具沒有 MCP 工具不算完成。 |
| MCP-2 | MCP 工具呼叫與檢討面板相同的 use case，不另寫一條路，不重寫判定或寫入規則。 |
| MCP-3 | 原本以對話框、`PickObject`、`PickPoint` 問使用者的資訊改為工具參數。 |
| MCP-4 | 會寫入模型或結果的工具支援 `dryRun`，以 TransactionGroup 回復，不留痕跡。 |
| MCP-5 | 工具名稱小寫底線、以工具前綴開頭（`fire_review_*`、`article164_*`…）；描述寫明用途、時機與會改什麼。 |
| MCP-6 | 人工覆寫不開放給 MCP；判定只由外掛規則決定。 |
| MCP-7 | MCP 服務開關保留在 Ribbon「AI 代理」，不整合進檢討面板。 |
| MCP-8 | MCP 是 code review subagent 的驗證工具（[`AGENT.md`](../../../AGENT.md) 原則 5）。 |

## 2. 覆蓋表

| 工具 | 面板動作 | MCP 工具 | 狀態 |
| --- | --- | --- | --- |
| 共通 | — | `revit_status` | ✅ |
| `fire-review` | 開始檢討、看結果 | `fire_review_list_packages`、`_check`、`_run`、`_get_results`、`_describe_result` | ✅ |
| `fire-review` | 參數批次設定 | `fire_review_scan_parameters`、`fire_review_set_parameters` | ✅ |
| `fire-review` | 參數一鍵建立 | — | ⏳ 待補（先做預覽／dryRun 版） |
| `fire-review` | 區劃設定 | — | ⏳ 待補（來源樓層平面改為參數） |
| `fire-review` | 區劃編輯器 | — | ⏳ 只規劃「讀取區劃」與 headless 寫回 |
| `article-164-road-shadow` | 開始檢討 | — | ⏳ 待補：建築線、道路側、道路寬度、排除類型改為參數 |
| `article-164-road-shadow` | 圖說製作 | — | ⏳ 待補：圖框、文字大小、版面位置改為參數 |

待補項目各自依 [`AGENT.md`](../../../AGENT.md) 原則 6 開分支實作。

## 3. 驗收（每個新增的 MCP 工具）

1. 與面板操作同一 use case，結果逐字一致。
2. `dryRun` 前後模型無差異。
3. Revit 忙碌時回報「Revit 忙碌中」，不卡住。
4. 已加入 ARCHITECTURE.md §6 工具清單、`RevitMcpHost.Instructions` 建議流程、驗證清單。
