# AGENT.md — 開發代理工作規範

給在這個專案工作的 AI 代理（Claude Code、Codex 等）與開發者。`CLAUDE.md` 只是指向本文件的入口，規範以本文件為準。

- 產品要做什麼：[`SPEC.md`](SPEC.md)
- 程式怎麼組成：[`ARCHITECTURE.md`](ARCHITECTURE.md)
- 介面與共用元件怎麼設計：[`DESIGN.md`](DESIGN.md)
- 文件索引：[`docs/README.md`](docs/README.md)

---

## 0. 專案一句話

Revit 2024 外掛（.NET Framework 4.8 / WPF），檢討台灣《建築技術規則》。每一項檢討是一個「工具」，
人從**檢討面板**操作，AI 代理從本專案的 **MCP 伺服器**（`building-regulation-review`）操作，兩者呼叫同一段 use case。

## 1. 開發原則（必須遵守）

以下六條是專案層級的規則，每一條後面註明細節寫在哪裡。

### 原則 1：每個檢討工具都要透過本專案的 MCP 開放給代理

- 之後新增的法規檢討工具，**完成定義包含 MCP 工具**：沒有 MCP 工具的檢討工具不算完成。
- 做法：流程寫成不依賴 WPF 的 use case；面板按鈕與 MCP 工具都呼叫它。原本用對話框問使用者的事改成參數。
  不要為了 MCP 另寫一條路，也不要在 MCP 工具裡重寫檢討規則。
- MCP 工具名稱以工具 ID 為前綴（例如 `fire_review_*`、`article164_*`），註冊在 `RevitMcpHost`。
- 細節：[`docs/tools/mcp-server/SPEC.md`](docs/tools/mcp-server/SPEC.md)（需求與覆蓋表）、
  [`docs/tools/mcp-server/ARCHITECTURE.md`](docs/tools/mcp-server/ARCHITECTURE.md) §9（接入步驟）。

### 原則 2：檢討工具放進「檢討面板」，不再往 Ribbon 加按鈕

- Ribbon 只保留兩個入口：**開啟檢討面板**，以及 **AI 代理 › MCP 服務**開關（MCP 開關不整合進面板）。
- 既有的 Ribbon 檢討按鈕（第 164 條、防火區劃設定、防火參數一鍵建立、防火參數批次設定、防火區劃編輯器、
  防火區劃檢討）要整合進檢討面板；新工具一律只在面板出現。
- 面板必須符合 [`DESIGN.md`](DESIGN.md) §2「檢討面板設計原則」。
- 整合工作的規格：[`docs/tools/review-panel/SPEC.md`](docs/tools/review-panel/SPEC.md)。

### 原則 3：每個工具有自己的 SPEC.md 與 ARCHITECTURE.md

- 根目錄的 [`SPEC.md`](SPEC.md)、[`ARCHITECTURE.md`](ARCHITECTURE.md) 寫整體；每個工具在
  `docs/tools/<工具 ID>/` 下有自己的 `SPEC.md`（做什麼、輸入、判定、產出、驗收）與 `ARCHITECTURE.md`
  （類別、資料流、Transaction、持久化、MCP 工具）。
- 新工具開工時先建這兩份（範本見 §5），再寫程式。工具內的細部法規設計可放在 `docs/tools/<工具 ID>/rules/`。
- 新增或改名工具時，同步更新根 `SPEC.md` 的工具清單與 [`docs/tools/README.md`](docs/tools/README.md)。

### 原則 4：先找可共用的，再寫新的

- 開發新工具前，先查 [`DESIGN.md`](DESIGN.md) §4「共用元件目錄」與既有工具，凡是已經有的形式
  （例如面板、套件選擇視窗、檢討表與篩選、明細、規則引擎、dryRun、MCP 工具基底）都要沿用。
- 若既有元件差一點能用，**擴充它**（加參數、給預設值不影響舊呼叫端），不要複製一份改名。
  例：`PackagePickerWindow` 的提示字與按鈕字改成建構式參數，兩個呼叫端各傳自己的字（C-12）。
- 新做出來、第二個工具也會用到的東西，要搬到共用位置並登記到 `DESIGN.md` §4。

### 原則 5：Code review 要開 subagent，並用法規檢討 MCP 驗證

- 每個功能在合併前做 code review 時，**另開一個 subagent**（`code-reviewer` 類型）負責審查，不由寫程式的同一個
  代理自審。
- Reviewer subagent 除了讀 diff，還要：
  1. 用本專案的 MCP（`building-regulation-review`）在 Revit 實際跑一次受影響的工具：`revit_status` 確認模型 →
     該工具的檢查／執行工具（先 `dryRun`）→ 讀結果，確認與面板操作一致、`dryRun` 不留痕跡。
  2. 用法規查詢 MCP（`taiwan-building-code`）核對程式與文件引用的條文、函釋是否正確、是否為現行版本。
  3. 回報格式：問題（嚴重度、檔案:行號、重現方式）＋ MCP 驗證的實際呼叫與逐字結果。
- Revit 沒開或 MCP 未啟動時，reviewer 要明講「MCP 驗證未執行」與原因，不得當作通過；
  這一項列入該功能的待驗清單（[`docs/verification/checklist.md`](docs/verification/checklist.md)）。
- 給 reviewer 的提示範本見 §4。

### 原則 6：一個功能一個分支，完成並通過測試才合併，合併後刪分支

- 每一個獨立的工具項目或功能都**從最新的 `main` 開新分支**，不在 `main` 上直接開發。
- **同一模組的新功能也開新分支**，不重複使用舊分支（即使舊分支名稱看起來相關）。
- 分支命名：`feature/<工具 ID>-<功能>`、`fix/<工具 ID>-<問題>`、`docs/<主題>`、`refactor/<範圍>`，
  全小寫、以 `-` 分隔，例如 `feature/review-panel-integration`、`fix/fire-review-zone-use-stale`。
- 合併條件（全部滿足才可合併）：
  1. 功能完成，該工具的 `SPEC.md` 驗收項目達成。
  2. `dotnet build` 0 錯誤，`dotnet test` 全部通過。
  3. 原則 5 的 subagent code review 已完成，提出的問題已修正或經使用者同意保留。
  4. 需要 Revit 實機驗證的項目已驗，或已登記在驗證清單並經使用者同意後補。
  5. 文件（工具的 SPEC／ARCHITECTURE、根 SPEC 工具清單、MCP 工具清單）已同步。
- 合併後**刪除該功能分支**（本機與遠端）：`git branch -d <分支>`、`git push origin --delete <分支>`。
- 合併、推送、刪除遠端分支前先取得使用者同意（這些動作影響共享狀態）。

## 2. 建置、測試與部署

```powershell
dotnet build BuildingRegulationReview.sln -c Release
dotnet test tests/BuildingRegulationReview.Core.Tests
.\scripts\install-revit-2024.ps1      # 需先關閉 Revit；或雙擊 scripts\redeploy.bat
```

- Core 測試涵蓋 Domain、Application、MCP 協定層；Revit 端與 WPF 只能實機驗證（或經 MCP）。
- 部署前提醒使用者存檔並關閉 Revit。

## 3. 寫程式的規則

- 分層：Domain → Application → Revit Adapter → 外掛（UI／MCP）。Domain 與 Application 不參考 Revit API 與 WPF。
  詳見 [`ARCHITECTURE.md`](ARCHITECTURE.md) 與 ADR-0001。
- Revit 文件修改只能在 API context：面板經 `ReviewExternalEventDispatcher`，MCP 經 `RevitMcpDispatcher`。
- 判定（符合／未符合）只由規則與 use case 決定；UI 與 MCP 只負責顯示與傳參數。人工覆寫不開放給 MCP。
- 計算在 Revit 邊界用 internal feet，顯示與輸入才換成公制。自動建立的視圖、類型、圖紙用穩定名稱，重跑時覆蓋不累積。
- 介面文字、文件、commit 訊息用繁體中文；識別字、路徑、指令維持英文。
- 重要設計決策寫 ADR（`docs/adr/NNNN-*.md`）。

## 4. Code review subagent 提示範本

```text
請 review 分支 <分支名> 相對 main 的變更（git diff main...HEAD）。
1. 程式審查：正確性、分層是否被破壞、是否重複實作了 DESIGN.md §4 已有的共用元件、Transaction 邊界、錯誤處理。
2. MCP 實機驗證（building-regulation-review）：revit_status → <工具的檢查/執行 MCP 工具，先 dryRun> →
   讀結果；與 SPEC 的驗收項目逐條比對，貼出逐字結果。
3. 法規核對（taiwan-building-code）：程式與文件引用的條號、函釋字號是否正確。
4. 無法執行 MCP 時明確寫出原因，不得視為通過。
回報：問題清單（嚴重度／檔案:行號／重現）＋ MCP 呼叫紀錄。
```

## 5. 新工具開工清單

1. 從 `main` 開分支 `feature/<工具 ID>-<功能>`（原則 6）。
2. 建 `docs/tools/<工具 ID>/SPEC.md` 與 `ARCHITECTURE.md`（可複製 `docs/tools/article-164-road-shadow/` 的章節結構），
   並在根 `SPEC.md`、`docs/tools/README.md` 登記工具 ID 與狀態。
3. 對照 `DESIGN.md` §4，列出要沿用的共用元件（原則 4）。
4. 寫 use case（無 UI）→ 面板項目（`review-items.json` + `IReviewFeature`）→ MCP 工具模組（原則 1、2）。
5. 單元測試 → build／test → subagent code review ＋ MCP 驗證（原則 5）→ 修正。
6. 文件同步 → 經使用者同意合併 → 刪分支（原則 6）。

## 6. 文件放哪裡

| 內容 | 位置 |
| --- | --- |
| 整體需求、工具清單 | `SPEC.md` |
| 整體架構、分層、共用基礎設施 | `ARCHITECTURE.md` |
| 介面原則、檢討面板、共用元件目錄 | `DESIGN.md` |
| 單一工具 | `docs/tools/<工具 ID>/SPEC.md`、`ARCHITECTURE.md`、`rules/`、`history/` |
| 設計決策 | `docs/adr/` |
| Revit 實機驗證 | `docs/verification/checklist.md`（索引）、`log.md`（紀錄） |
