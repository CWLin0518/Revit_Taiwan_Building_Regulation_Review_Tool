# Agent Handoff
- Phase: P3
- Completed Task: P3-T08
- Next Task: P3-T09
- Status: READY_FOR_NEW_SESSION
- Commit: 5faa685（feat）；SHA 由本 docs commit 記錄
- Spec Version: Draft v1.1 (`docs/fire-review-spec.md`)
- 任務文件：`docs/agent/p3-t08-review-marking-and-table.md`（檢討表規則、標示計畫與差異、schema GUID、設計決策）
- P2 實機驗收仍有兩項「未回報」（見 `phase-2-acceptance.md`），使用者選擇先進行 P3。

## Completed
- Application `Reviews/ReviewTable.cs`：`ReviewStatusAggregation`（六態列彙總、spec 11.7 總狀態規則）、`ReviewVerdict`（含 NeedsUpdate）、`ReviewStatusCounts`、
  `ReviewTable`／`ReviewTableSection`／`ReviewTableGroup`／`ReviewTableEntry`（依區劃、類別＋Type、門／窗／幕牆統計；定位、條文、覆寫、失效）。
- Application `Reviews/ReviewMarkup.cs`：`ReviewMarkKey`（Package／Run／Zone ID）、`ReviewMarkupPlan`（紅色區域與元素覆寫，依 EffectiveStatus，失效／連結略過）、
  `ReviewMarkupDiff`（只動本套件標記與工具紀錄的元素）、`RecordedElementOverride`＋`ReviewOverrideRestore`（原始狀態與使用者修改保留）、`ReviewMarkupResult`。
- `ManagedOutputKind.ReviewView`、`ReviewOutputNaming.ReviewView`、`ManagedOwnership` 認得 `BCRRV` token；錯誤碼 `BCR-MARK-001/002/003`。
- Revit `Reviews/`：`RevitReviewViewMarker`（找回／複製檢討視圖、Filled Region、By Element Override、恢復、`Locate`，TransactionGroup）、
  `RevitOverrideStateCodec`、`ReviewViewOverrideStorage`（view 上的 Extensible Storage）；`ManagedElementMark.Write(element, ReviewMarkKey, …)`。

## Changed Files
- `src/BuildingRegulationReview.Application/Reviews/ReviewTable.cs`、`ReviewMarkup.cs`（新增）
- `src/BuildingRegulationReview.Application/WriteBack/ManagedOutput.cs`、`ReviewOutputNaming.cs`
- `src/BuildingRegulationReview.Application/Diagnostics/ReviewErrorCode.cs`
- `src/BuildingRegulationReview.Revit/Reviews/RevitReviewViewMarker.cs`、`RevitOverrideStateCodec.cs`（新增）
- `src/BuildingRegulationReview.Revit/WriteBack/ManagedElementMark.cs`
- `tests/BuildingRegulationReview.Core.Tests/Reviews/ReviewTableTests.cs`、`ReviewMarkupTests.cs`（新增）
- `docs/agent/p3-t08-review-marking-and-table.md`（新增）、`docs/agent/phase-state.yaml`、`docs/agent/HANDOFF.md`

## Decisions and Assumptions
- 一個套件一個檢討視圖（來源平面圖的複本），以擁有權標記找回；不改 `ReviewPackage` schema。
- 紅色區域跨 run 以 package＋zone＋part 配對，新 run 接手（Update），重跑不增加元素；Run ID 寫在 token 與 Comments。
- 原視圖狀態＝元素在檢討視圖的原始 `OverrideGraphicSettings`，只在第一次上色時擷取；恢復時若使用者已改就保留。
- 失效結果不標示，列為略過；run 或任一結果失效時總狀態為「需更新」。
- 空的檢討列顯示未檢討但不影響總狀態；三個檢查都要跑由 P3-T09 保證。

## Verification Results
- Solution `-t:Rebuild`、外掛 csproj `-t:Rebuild`：各 0 warnings、0 errors。
- Core tests：**931/931 通過**（前 889，新增 42：ReviewTableTests 28、ReviewMarkupTests 14）。
- 未實機驗證：Revit 端的視圖複製、Filled Region、覆寫與恢復、view 上的 Extensible Storage 都只經過編譯（P3-T09 實機驗收）。

## Known Issues / Risks
- `RevitReviewViewMarker` 尚未被任何指令呼叫；Filled Region 的 Z 取 `GenLevel.Elevation`，需實機確認。
- `ReviewEnvironment` 事實與門窗／Type 參數的 Revit reader、WPF 檢討表、定位（選取＋縮放）、人工覆核 UI 都在 P3-T09。
- spec 19 第 2、4、5、6、7 項未定。
- `phase-2-acceptance.md` 的未提交修改、`bin/`、`obj/`、`.gitignore`、`.gtoffice/` 不屬於本 Task，刻意不提交。

## Exact Next Steps
1. 讀 spec 11.0（P3-T09）、11.1、11.8、13.2、14、15、16.3，以及 `p3-t03`～`p3-t08` 任務文件。
2. 執行 P3-T09：整合 Phase 3——前置檢查（`ReviewPreconditions`）、三類檢討一次執行、`ReviewEnvironment` 與參數 reader、
   `RevitReviewRunRepository` 存讀、`ReviewTable` WPF 視窗（展開、定位、條文、人工覆核）、`RevitReviewViewMarker.Mark`、取消／Rollback、日誌與效能。
3. 實機驗收：重開模型可讀 run、模型／規則變更變 Stale、檢討視圖標示與重跑三次元素數量不變。
4. P3-T09 是 Phase 3 最後一項：完成後發 `PHASE_COMPLETE` 並停止，不得開始 Phase 4。

## Do Not Do
- 不開始 Phase 4（Legend／Sheet／圖說）。
- 不寫任何 Revit 設計參數；標示只動檢討視圖，不動來源平面圖與 Area Plan。
- Revit／WPF 型別不得進 Domain／Application；不 push、不 amend、不 `git add .`。
