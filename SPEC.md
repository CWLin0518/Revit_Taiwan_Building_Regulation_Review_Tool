# SPEC.md — 建築技術規則 Revit 檢討外掛 總規格

各工具的詳細規格在 `docs/tools/<工具 ID>/SPEC.md`；這份文件只寫整體範圍、跨工具的共同需求與工具清單。

## 1. 產品目標

在 Revit 2024 中檢討台灣《建築技術規則》：從模型讀取資料、依結構化規則判定、在模型中標示問題並產出檢討圖說。
所有檢討可由人（檢討面板）與 AI 代理（MCP）兩種方式操作，結果相同。

非目標：不以自由文字推論法規結果；不在未確認的情況下修改使用者的牆、柱、樑、樓板、門窗；不處理雲端多人同步。

## 2. 工具清單

| 工具 ID | 名稱 | 法規 | 狀態 | 文件 |
| --- | --- | --- | --- | --- |
| `review-panel` | 檢討面板（所有工具的入口） | — | 已實作並實機驗證（2026-10-30）；動作可用性與狀態列待做 | [SPEC](docs/tools/review-panel/SPEC.md)／[ARCH](docs/tools/review-panel/ARCHITECTURE.md) |
| `mcp-server` | MCP 伺服器（代理入口） | — | 已實作；目前開放防火檢討 | [SPEC](docs/tools/mcp-server/SPEC.md)／[ARCH](docs/tools/mcp-server/ARCHITECTURE.md) |
| `article-164-road-shadow` | 第 164 條道路陰影檢討與圖說 | 建築設計施工編第 164 條 | 已實作；**尚未開放 MCP** | [SPEC](docs/tools/article-164-road-shadow/SPEC.md)／[ARCH](docs/tools/article-164-road-shadow/ARCHITECTURE.md) |
| `fire-review` | 建築防火檢討（防火區劃面積、構件防火時效、防火門窗、帷幕牆交接、垂直區劃、第 79 條之 1） | 建築設計施工編第 70、79、79-1～79-4、83 條等 | Phase 1～3 完成並實機驗證；Phase 4（圖說）未開始 | [SPEC](docs/tools/fire-review/SPEC.md)／[ARCH](docs/tools/fire-review/ARCHITECTURE.md) |

狀態與子規則的細目見 [`docs/tools/README.md`](docs/tools/README.md)。

## 3. 跨工具的共同需求

### R1 每個工具都能被代理使用（MCP）

- 之後新增的每一個法規檢討工具，都必須透過本專案的 MCP 伺服器（`building-regulation-review`）提供給代理使用。
- MCP 工具呼叫與面板相同的 use case；問使用者的事改成參數；會寫入的工具支援 `dryRun`。
- 既有工具中尚未開放的（第 164 條、防火區劃設定、區劃編輯器、參數一鍵建立）列入 MCP SPEC 的待辦，依序補上。
- 詳見 [`docs/tools/mcp-server/SPEC.md`](docs/tools/mcp-server/SPEC.md)。

### R2 檢討工具整合進檢討面板

- Ribbon 空間有限，檢討工具不再放 Ribbon。目前 Ribbon 上的檢討功能整合進「檢討面板」，並符合
  [`DESIGN.md`](DESIGN.md) §2 的檢討面板設計原則。
- MCP 服務的開關**不**整合進面板，保留在 Ribbon「AI 代理」。
- 詳見 [`docs/tools/review-panel/SPEC.md`](docs/tools/review-panel/SPEC.md)。

### R3 文件分層

- 根目錄 `SPEC.md`（本文件）與 `ARCHITECTURE.md` 描述整體；每個工具在 `docs/tools/<工具 ID>/` 有獨立的
  `SPEC.md` 與 `ARCHITECTURE.md`。

### R4 共用優先

- 新工具若專案內已有類似的已開發工具或元件（例如面板形式、套件選擇、檢討表、規則引擎），必須共用。
  共用元件目錄見 [`DESIGN.md`](DESIGN.md) §4。

### R5 Code review 以 subagent ＋ 法規檢討 MCP 驗證

- 開發過程的 code review 由另開的 subagent 執行，並使用法規檢討 MCP 在 Revit 實際驗證。做法見 [`AGENT.md`](AGENT.md) 原則 5。

### R6 一個功能一個 Git 分支

- 每個獨立工具項目的開發都建立新分支；完成並通過測試才合併；合併後刪除分支；同一模組的新功能開新分支，
  不重用舊分支。做法見 [`AGENT.md`](AGENT.md) 原則 6。

### R7 共通品質需求

- 判定可追溯：每項結果保存依據條文、實際值、要求值、相關元素與規則版本。
- 可重跑：工具建立的視圖、類型、圖紙使用穩定名稱並標記為工具所有，重跑時覆蓋，不累積、不刪使用者的元素。
- 失效可見：模型或規則變動後，舊結果標示「需更新」，不沉默沿用。
- 單位：Revit 邊界用 internal feet，顯示與輸入用公制與條文單位。
- 介面與文件用繁體中文。

## 4. 平台

| 項目 | 規格 |
| --- | --- |
| 宿主 | Autodesk Revit 2024 |
| Framework | .NET Framework 4.8（外掛）、netstandard2.0（Domain／Application／MCP 協定層） |
| UI | WPF（Dockable Pane ＋ 獨立視窗） |
| 幾何 | RhinoCommon 8（第 164 條） |
| 代理介面 | MCP（stdio bridge → loopback Streamable HTTP） |

## 5. 完成定義（所有工具共用）

1. 工具的 `SPEC.md` 驗收項目全部達成。
2. 面板可操作（R2），MCP 可操作（R1），兩者結果一致。
3. `dotnet build` 0 錯誤、`dotnet test` 全通過。
4. Subagent code review 與 MCP 實機驗證完成（R5）；未能實機驗證的項目已登記在 [`docs/verification/checklist.md`](docs/verification/checklist.md)。
5. 文件同步：工具 SPEC／ARCHITECTURE、本文件工具清單、`docs/tools/README.md`、MCP 工具清單。
6. 在功能分支完成，經同意合併後刪除分支（R6）。
