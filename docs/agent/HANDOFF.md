# Agent Handoff
- Phase: P2
- Completed Task: P2-T09（Phase 2 最後一個任務）
- Next Task: PHASE_COMPLETE — **不得開始 Phase 3**，需先在 Revit 中補完實機驗收
- Status: PHASE_COMPLETE_PENDING_REVIT_ACCEPTANCE
- Spec Version: Draft v1.1 (`docs/fire-review-spec.md`)
- 驗收對照：`docs/agent/phase-2-acceptance.md`
- 任務文件：`docs/agent/p2-t09-phase-2-integration.md`

## Completed
- `ReviewErrorCode` / `ReviewLogEntry` / `ReviewLog`（Application `Diagnostics`，全新）：spec 14 要求的
  八個欄位（錯誤碼、階段、Package ID、元素 UniqueId、使用者訊息、技術細節、處理建議、時間）、依
  spec 14「重要錯誤類型」分組的錯誤碼目錄，以及 UI 訊息與本機日誌的分流。技術細節在建構當下就把
  Windows／UNC／POSIX 路徑換成「（已隱藏路徑）檔名」。
- `ReviewPackageProgress`（Application `ReviewPackages`，全新）：寫回結束後唯一決定套件狀態的地方，
  也是 spec 10.6 第 3 項「面積差異超過容許值禁止進入 Ready」的執行點。
- `ReviewStaleness` + `ReviewModelObservation` + `StalenessVerdict`（全新）：spec 13.1 的失效判斷。
  刻意不存指紋——視圖靠 UniqueId 找，邊界與面積帶著寫入時的簽章，而簽章是用容差量化出來的。
- `ReviewRunReport`（全新）：把「寫回做了什麼」「套件變成什麼」「日誌」收成一個值，就是 P2-T09 要
  的那道接縫。
- `AreaAgreement`：新增 `AreaAgreementKind` / `AreaAgreementFinding`（帶 `ManagedElementKey`，所以
  Package ID 與 Zone ID 跟著判決走）/ `Compare(...)`；`Describe(...)` 保留並委派。
- `ApplyResult`：新增 `AreaFindings` / `AreaDisagreements` / `Builder.Area(...)`；復原時丟棄。
- `PlannedElementSignature`（全新）：簽章拼法獨立出來，`ZoneWritePlan` 與 Revit 端失效探測共用。
- `ReviewPackage.WithProgress` / `WithNextBoundaryRevision`（Domain）。**儲存欄位集合未變動**。
- `RevitReviewStalenessProbe`（Revit，全新）：唯讀，重算簽章比對邊界線與細部線，面積比對名稱。
- `RegionEditorCommand`：改 `TransactionMode.Manual`；開窗前 `CheckForStaleResults` 並存下狀態；
  寫回後重讀套件、組報告、存回狀態。
- `RegionEditorWindow.ReportApplied(ReviewRunReport)`；結果視窗顯示狀態與阻擋原因，日誌改寫
  `ReviewLog.ToText()`。

## Changed Files
- `src/BuildingRegulationReview.Application/Diagnostics/ReviewErrorCode.cs`（新增）
- `src/BuildingRegulationReview.Application/Diagnostics/ReviewLogEntry.cs`（新增）
- `src/BuildingRegulationReview.Application/Diagnostics/ReviewLog.cs`（新增）
- `src/BuildingRegulationReview.Application/ReviewPackages/ReviewPackageProgress.cs`（新增）
- `src/BuildingRegulationReview.Application/ReviewPackages/ReviewStaleness.cs`（新增）
- `src/BuildingRegulationReview.Application/ReviewPackages/ReviewRunReport.cs`（新增）
- `src/BuildingRegulationReview.Application/WriteBack/PlannedElementSignature.cs`（新增）
- `src/BuildingRegulationReview.Application/WriteBack/AreaAgreement.cs`
- `src/BuildingRegulationReview.Application/WriteBack/ApplyResult.cs`
- `src/BuildingRegulationReview.Application/WriteBack/ZoneWritePlan.cs`
- `src/BuildingRegulationReview.Domain/ReviewPackages/ReviewPackage.cs`
- `src/BuildingRegulationReview.Revit/ReviewPackages/RevitReviewStalenessProbe.cs`（新增）
- `src/BuildingRegulationReview.Revit/WriteBack/RevitZoneWriteBack.cs`
- `src/BuildingRegulationReview/RegionEditorCommand.cs`
- `src/BuildingRegulationReview/RegionEditor/RegionEditorWindow.cs`
- `src/BuildingRegulationReview/RegionEditor/RegionEditorApplyResultWindow.cs`
- `tests/BuildingRegulationReview.Core.Tests/Diagnostics/ReviewLogTests.cs`（新增）
- `tests/BuildingRegulationReview.Core.Tests/ReviewPackages/ReviewPackageProgressTests.cs`（新增）
- `tests/BuildingRegulationReview.Core.Tests/ReviewPackages/ReviewStalenessTests.cs`（新增）
- `tests/BuildingRegulationReview.Core.Tests/WriteBack/PhaseTwoAcceptanceTests.cs`（新增）
- `tests/BuildingRegulationReview.Core.Tests/WriteBack/PlannedElementSignatureTests.cs`（新增）
- `tests/BuildingRegulationReview.Core.Tests/WriteBack/ApplyResultTests.cs`
- `tests/BuildingRegulationReview.Core.Tests/ReviewPackages/ReviewPackageTests.cs`
- `docs/agent/p2-t09-phase-2-integration.md`（新增）
- `docs/agent/phase-2-acceptance.md`（新增）
- `docs/agent/phase-state.yaml`
- `docs/agent/HANDOFF.md`

## Decisions and Assumptions
- 面積不符擋 Ready，色彩人工處理項不擋：spec 10.6 問的是邊界圍出來的東西對不對，色彩是外觀。
  寫入失敗會擋，因為草稿確實還沒進模型。
- 「什麼都沒做」的那一次不動狀態：模型本來就與草稿一致時沒有任何 Area 被重新量測，這一輪沒有學到
  任何事，升級成 Ready 等於做了沒人做過的宣稱。
- 邊界改了就把 `Reviewed`／`Documented` 打回 Ready／BoundaryDraft（spec 13.1）。
- 失效判斷不存指紋，理由見 `phase-2-acceptance.md`；容差變更走「簽章不同」同一條路徑。
- 日誌是值不是 logger：之後要加檔案寫入或 Revit journal 都不必動到各階段。
- `RegionEditorCommand` 從 ReadOnly 改成 Manual，只為了存狀態那一筆寫入。

## Verification Results
- Solution build 與 add-in `-t:Rebuild`：各 0 warnings、0 errors。
- Core tests：**494/494 通過**（P2-T09 前 391，新增 103）。
- 無頭腳本 `headless-t09.ps1`：**30/30 通過**，跑的是 add-in 實際載入的 net48 輸出目錄裡的
  `BuildingRegulationReview.Domain.dll`／`BuildingRegulationReview.Application.dll`。
- WPF 視窗仍無法在本機渲染（`MS.Internal.FontCache.Util` 初始化失敗，與程式碼無關）。

## Known Issues / Risks
- **Revit 內的實際寫入從未執行過。** P2-T07 的 18 項、P2-T08 的 21 項與 P2-T09 的 12 項手動清單
  全部未跑，`建築防火檢討1.rvt` 的端到端實跑仍未執行。實跑前務必備份。
- Design Option、專案 Phase、參與檢討的元素與參數的失效判斷未實作（前兩項整條流程不讀取，後兩項
  要到 Phase 3 才有意義）。
- 面積標註與面積顏色沒有被納入失效比對；色彩配置本身也沒有。
- 規則版本目前沒有來源：`RevitReviewStalenessProbe.Observe` 的 `ruleSetVersion` 沒有呼叫端會傳。
- 失效判斷每次都要掃描整個 Area Plan 與單線圖的受管理元素，大平面上未量測。
- 日誌只在結果視窗按按鈕時寫檔；失效判斷產生的日誌目前只進對話框，沒有落檔。
- 草稿仍只在記憶體，關窗即失；`ZoneDraftSet.RetainFaces` 仍沒有流程呼叫。
- 編輯器不能畫輔助線：線網缺口仍要回 Revit 補線後重開編輯器。
- 單線圖只有細部線，沒有區劃名稱文字或圖例（圖例是 P4-T03）。
