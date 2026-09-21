# Agent Handoff
- Phase: P3
- Completed Task: P3-T09（Phase 3 最後一個任務）
- Next Task: PHASE_COMPLETE — **不得開始 Phase 4**，需由使用者／Orchestrator 明確啟動，並先補 Revit 實機驗收
- Status: PHASE_COMPLETE_PENDING_REVIT_ACCEPTANCE
- Commit: 5d01da0（feat）；SHA 由本 docs commit 記錄
- Spec Version: Draft v1.1 (`docs/fire-review-spec.md`)
- 任務文件：`docs/agent/p3-t09-phase-3-integration.md`；驗收對照：`docs/agent/phase-3-acceptance.md`

## Completed
- Application `Reviews`：`ReviewInputSources`（欄位 → `BCR_*` 參數目錄、規則實際需要的參數）、`ReviewParameterSnapshot`＋`ReviewInputAssembler`
  （參數值 → 三類檢查輸入，不猜測）、`ReviewReadiness`（spec 11.1 前置檢查，阻擋／提醒＋修正方式）、`FireReviewRunner`
  （三類一次執行、安全點取消、覆寫沿用、套件 Reviewed＋鎖定規則版本、日誌）、`ReviewPerformance`（spec 15 目標與診斷）、
  `StoredRunInspection`（重開模型時判定失效並暫停覆寫）。錯誤碼 `BCR-PRE-001/002`、`BCR-ENV-001`、`BCR-RUN-002/003/004`、`BCR-PERF-001`。
- Domain：`ReviewPackage.WithReviewRun`。
- Revit：`RevitReviewParameterReader`、`RevitReviewEnvironmentReader`（唯讀）。
- 外掛：內建暫定規則 `Data/fire-review-rules.json`（`tw-bcr-fire 2026.0-provisional`）、`FireReviewRuleSetSource`、`FireReviewModel`
  （掃描、儲存＋標示同一 TransactionGroup、定位）、`FireReviewWindow`（前置檢查、進度／取消、檢討表、明細、覆寫、重新標示、日誌）、
  `FireReviewOverrideDialog`、`FireReviewCommand`、ribbon「防火區劃檢討」。

## Changed Files
- 新增：`src/BuildingRegulationReview.Application/Reviews/{ReviewInputSources,ReviewParameterSnapshot,ReviewReadiness,FireReviewRunner,StoredRunInspection}.cs`
- 新增：`src/BuildingRegulationReview.Revit/Reviews/{RevitReviewParameterReader,RevitReviewEnvironmentReader}.cs`
- 新增：`src/BuildingRegulationReview/FireReviewCommand.cs`、`src/BuildingRegulationReview/FireReview/*.cs`、`src/BuildingRegulationReview/Data/fire-review-rules.json`
- 修改：`ReviewErrorCode.cs`、`ReviewPackage.cs`、`App.cs`、外掛 csproj（規則檔輸出）、測試 csproj（連結規則檔）
- 新增測試：`tests/BuildingRegulationReview.Core.Tests/Reviews/FireReviewIntegrationTests.cs`
- 文件：`docs/agent/p3-t09-phase-3-integration.md`、`docs/agent/phase-3-acceptance.md`、`HANDOFF.md`、`phase-state.yaml`

## Decisions and Assumptions
- spec 19 第 2、4～6 項未定：規則為暫定示意；輸入以參數名稱讀取（專案資訊／面積／構件類型／門窗實體或類型），文字、整數、是非皆可，無法判讀即資料不足。
- 「必要參數」由規則實際讀取的欄位決定；完全沒有參數才阻擋，部分類別沒綁只提醒。
- 鎖定的規則版本與外掛提供者不同 → 阻擋，使用者勾選「改用目前規則版本」後才以新版檢討並鎖定。
- run＋套件＋檢討視圖標示是同一個 undo；例外整批復原；標示整批失敗時保留已存結果並提示重新標示。
- 開窗時就判定最新 run 的失效並寫回（覆寫暫停、套件 Stale）。

## Verification Results
- Solution 與外掛 csproj `-t:Rebuild`：各 0 warnings、0 errors。
- Core tests：**982/982 通過**（原 931，新增 51）。
- 無頭（net48 實際輸出）：內建規則載入成功（4 條）；缺檔 `BCR-RULE-001`、壞 JSON 與 schema 錯誤 `BCR-RULE-005`。
- **Revit 實機驗收未執行**（本 session Revit MCP 未連線；新指令需重新部署與重開 Revit）：`phase-3-acceptance.md` R1～R12 皆「未回報」。

## Known Issues / Risks
- Revit 端（參數／環境 reader、TransactionGroup、檢討視圖標示、Extensible Storage 存讀、WPF 視窗）只經編譯與無頭驗證。
- 檢討期間使用者改模型不會在儲存時重比對（下次開窗判定為需更新）。
- 柱、梁沒有規則（不適用）；連結模型與非主要設計選項不讀取；Type 時效只讀 `BCR_ProvidedFireRating`。
- P2 實機驗收仍有兩項未回報；`docs/agent/phase-2-acceptance.md` 的未提交修改、`bin/`、`obj/`、`.gitignore`、`.gtoffice/` 不屬本任務，未提交。

## Exact Next Steps
1. 使用者重新部署外掛並依 `phase-3-acceptance.md` 最後一節做 R1～R12 實機驗收，回報結果；有問題以 `fix(fire-review): [P3] …` 修正。
2. 決定 spec 19 第 2、4～7 項後替換內建規則檔（提高 version）與參數來源。
3. Phase 4 僅在使用者或 Orchestrator 明確啟動後開始。

## Do Not Do
- 不開始 Phase 4（Legend／Sheet／圖說）。
- 不寫任何 Revit 設計參數；標示只動檢討視圖。
- Revit／WPF 型別不得進 Domain／Application；不 push、不 amend、不 `git add .`。
