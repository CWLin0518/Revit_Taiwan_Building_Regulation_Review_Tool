# Agent Handoff
- Phase: P3
- Completed Task: P3-T07
- Next Task: P3-T08
- Status: READY_FOR_NEW_SESSION
- Commit: f03d428（feat）；SHA 由本 docs commit 記錄
- Spec Version: Draft v1.1 (`docs/fire-review-spec.md`)
- 任務文件：`docs/agent/p3-t07-result-persistence.md`（失效規則表、人工覆寫流程、schema GUID、設計決策）
- P2 實機驗收仍有兩項「未回報」（見 `phase-2-acceptance.md`），使用者選擇先進行 P3。

## Completed
- Domain：`ReviewBaseline`（元素證據指紋）、`ReviewOverride`＋`ReviewOverrideStanding`（稽核紀錄）；`ReviewRun` 新增 Baseline／Overrides／EffectiveStatus，schema 1.1。
- Application `Reviews`：`ReviewEnvironment`、`ReviewBaselineBuilder`（SHA-256，微米四捨五入）、`ReviewBaselineKeys`、
  `ReviewRunValidity`（Evaluate／WithOverridesSuspended／ApplyTo／Explain）、`ReviewOverrides`（Apply／Reconfirm／Withdraw／CarryOver）、
  `IReviewRunRepository`＋`GetLatest`；storage record／mapper 可讀 1.0 與 1.1。
- Revit：`RevitReviewRunRepository`（每個 run 一個 DataStorage，四個 sub-schema，所有欄位各自獨立）。
- 診斷：`ReviewStage.Review`；`BCR-OVR-001`、`BCR-OVR-002`、`BCR-RUN-001`。

## Changed Files
- `src/BuildingRegulationReview.Domain/Reviews/ReviewBaseline.cs`、`ReviewOverride.cs`（新增）、`ReviewRun.cs`
- `src/BuildingRegulationReview.Application/Reviews/ReviewBaselineBuilder.cs`、`ReviewRunValidity.cs`、`ReviewOverrides.cs`、`IReviewRunRepository.cs`（新增）、`ReviewRunStorageRecord.cs`
- `src/BuildingRegulationReview.Application/Diagnostics/ReviewErrorCode.cs`、`ReviewLogEntry.cs`
- `src/BuildingRegulationReview.Revit/Reviews/RevitReviewRunRepository.cs`（新增）
- `tests/BuildingRegulationReview.Core.Tests/Reviews/ReviewRunValidityTests.cs`（新增）
- `docs/agent/p3-t07-result-persistence.md`（新增）、`docs/agent/phase-state.yaml`、`docs/agent/HANDOFF.md`

## Decisions and Assumptions
- 證據存指紋而不是原始值；結果的 evidence 已經保存可追溯的數值。
- 結果的相依 key 是它的 subjects 加上 `zone:<ZoneId>`；規則版本、環境、邊界版次、run 未完成或沒有證據時，全部結果失效。
- 新增元素會讓 run 變成 Stale，但沒有具體結果可以失效。
- 覆寫只增不改；只有 Active 覆寫會影響 EffectiveStatus。模型或規則變更後改為 NeedsReconfirmation；新 run 用 CarryOver 依結果 key 配對，只有規則版本、指紋與計算狀態都相同時才沿用。
- `ReviewResult.reviewedBy/At` 不由覆寫流程寫入。舊 run 不自動刪除。

## Verification Results
- Solution `-t:Rebuild` 與外掛 csproj `-t:Rebuild`：各 0 warnings、0 errors。
- Core tests：**889/889 通過**（前 857，新增 32）。
- 未實機驗證：Revit Extensible Storage 的實際存檔、重開與讀回，要在 P3-T09 接上指令後驗證。

## Known Issues / Risks
- `ReviewEnvironment` 事實（Phase、Design Option、單位）與門窗／Type 參數的 Revit reader 尚未實作；三個 Check 都還沒接指令或 UI（P3-T09）。
- spec 19 第 2、4、5、6、7 項未定。
- `phase-2-acceptance.md` 的未提交修改、`bin/`、`obj/`、`.gitignore`、`.gtoffice/` 不屬於本 Task，刻意不提交。

## Exact Next Steps
1. 讀 spec 11.4 第 4 點、11.5 第 6–7 點、11.6 第 4 點、11.7、13.2，以及本文件、`p3-t04`～`p3-t07` 任務文件、P2 的 `ManagedElementMark`／`RevitManagedElementInventory`（受管理元素標記模式）。
2. 執行 P3-T08：視圖標示與檢討表——專用檢討 View、紅色 Filled Region（含 Package ID／Run ID／Zone ID）、By Element Override（保存原視圖狀態與覆寫元素集合）、定位、條文與統計；檢討表的六態彙總用 `ReviewRun.EffectiveStatus`。
3. Exit：只更新目前 Run 管理的元素，六態彙總規則正確。

## Do Not Do
- 不做 P3-T09（指令／UI 整合、前置檢查串接、取消／Rollback、實機驗收），也不開始 Phase 4。
- 不寫任何 Revit 設計參數；不在規則引擎加入任意程式碼執行。
- Revit／WPF 型別不得進 Domain／Application；不 push、不 amend、不 `git add .`。
