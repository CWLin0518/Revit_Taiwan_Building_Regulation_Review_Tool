# ARCHITECTURE.md — 整體架構

各工具的架構在 `docs/tools/<工具 ID>/ARCHITECTURE.md`；介面原則與共用元件目錄見 [`DESIGN.md`](DESIGN.md)。

## 1. 專案組成

| 專案 | Framework | 內容 | 可參考 |
| --- | --- | --- | --- |
| `BuildingRegulationReview.Domain` | netstandard2.0 | 結果、規則引擎、幾何、區劃、套件 | — |
| `BuildingRegulationReview.Application` | netstandard2.0 | use case、檢查、檢討表、寫回計畫 | Domain |
| `BuildingRegulationReview.Revit` | net48 | Revit API Adapter（讀取、寫回、版本適配） | Domain、Application |
| `BuildingRegulationReview.Mcp` | netstandard2.0 | MCP 協定層（JSON、JSON-RPC、HTTP），不參考 Revit | — |
| `BuildingRegulationReview.McpBridge` | net48 | 代理啟動的 stdio bridge | Mcp |
| `BuildingRegulationReview` | net48 | 外掛：Ribbon、檢討面板、WPF 視窗、MCP 工具 | 全部 |
| `tests/BuildingRegulationReview.Core.Tests` | net10.0 | Domain、Application、Mcp 單元測試 | Domain、Application、Mcp |

分層規則（ADR-0001）：Domain → Application → Revit Adapter → 外掛。Domain 與 Application 不參考 Revit API 與 WPF。

## 2. 執行路徑

```text
人：Ribbon「開啟檢討面板」→ ReviewPaneControl → ReviewExternalEventDispatcher ─┐
代理：McpBridge (stdio) → HTTP loopback → McpServer → RevitMcpDispatcher ──────┤
                                                                              ▼
                                                      工具 use case（無 UI，兩邊共用）
                                                                              ▼
                                                      Application / Domain / Revit Adapter
```

- Dockable Pane 與 modeless 視窗不能直接改文件，一律經 ExternalEvent 進 API context。
- MCP 工具只做三件事：讀參數、呼叫 use case、轉 JSON（[`docs/tools/mcp-server/ARCHITECTURE.md`](docs/tools/mcp-server/ARCHITECTURE.md) §1.1）。

## 3. 目錄結構

```text
/
├─ AGENT.md / CLAUDE.md        代理工作規範（CLAUDE.md 指向 AGENT.md）
├─ SPEC.md / ARCHITECTURE.md   整體規格與架構
├─ DESIGN.md                   介面原則、檢討面板、共用元件目錄
├─ README.md                   入口與安裝
├─ assets/SharedParameters/    共用參數主檔（Big5＋CRLF，見 .gitattributes）
├─ docs/
│  ├─ README.md                文件索引
│  ├─ adr/                     設計決策
│  ├─ tools/<工具 ID>/         SPEC.md、ARCHITECTURE.md、rules/、history/
│  └─ verification/            Revit 實機驗證清單與紀錄
├─ scripts/                    安裝、重新部署、註冊 MCP
├─ src/                        見 §1
└─ tests/
```

## 4. Revit Transaction 邊界

- 建立或修改元素、View、FilledRegion、TextNote、Sheet、Viewport 必須在 `Transaction` 內。
- 使用者選取、WPF 視窗、純幾何計算放在 Transaction 外。
- `Document.Regenerate()` 只能在有效 Transaction 內呼叫。
- 大型操作拆成明確交易；MCP 的 `dryRun` 以 TransactionGroup 回復。

## 5. 新增工具的接入點

1. `Data/review-items.json` 加項目（工具 ID）。
2. 實作 `IReviewFeature`，註冊到 `App.OnStartup` 的 `ReviewFeatureRegistry`（不加 Ribbon 按鈕）。
3. 流程寫成 use case，面板動作與 MCP 工具共用。
4. `src/BuildingRegulationReview/Mcp/<工具>/` 新增 `IMcpToolModule`，在 `RevitMcpHost` 註冊。
5. 建 `docs/tools/<工具 ID>/SPEC.md`、`ARCHITECTURE.md`。

## 6. 共用設計原則

- 工具 ID 在 JSON、`IReviewFeature`、文件資料夾、MCP 前綴間一致。
- Revit 邊界用 internal feet，顯示與輸入才轉公制。
- 自動建立的元素用穩定名稱並標記為工具所有，重跑時覆蓋。
- 暫存的 `ElementId` 使用前重新確認元素仍存在。
