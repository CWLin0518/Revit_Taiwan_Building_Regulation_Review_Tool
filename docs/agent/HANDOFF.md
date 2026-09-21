# Agent Handoff
- Phase: P3
- Completed Task: P3-T03
- Next Task: P3-T04
- Status: READY_FOR_NEW_SESSION
- Commit: （feat）；SHA 由後續 docs commit 記錄
- Spec Version: Draft v1.1 (`docs/fire-review-spec.md`)
- 任務文件：`docs/agent/p3-t03-spatial-candidates.md`（判定規則表、可重現性、設計決策都在這裡）
- P2 實機驗收仍有兩項「未回報」（見 `phase-2-acceptance.md`），使用者選擇先進行 P3。

## Completed
- Application `Candidates`：輸入契約（`CandidateObservationSet` 等）、`CandidateResolutionOptions`（各類別可設定的空間關係策略）、
  `CandidateResolver`、輸出 `CandidateSet`（`ZoneRelation`、`MemberCandidate`、`OpeningCandidate`、`CandidateZone`、`CandidateAmbiguity`、`Signature()`、`CanReview`）、
  `CandidateFacts`（轉 `RuleFacts`）、internal `CandidateGeometry`（精確 capsule 區間、邊界穿過長度）。
- 歧義（邊界在厚度內但不在中心線、邊界貼柱面、Host 不明、帷幕牆／非 Hosted／link 的 MVP 政策、無幾何、無位置、區劃未封閉／重疊）
  → `CandidateAmbiguity.ToReviewResult` 產生 ManualReview。
- `ReviewErrorCode` 新增 `BCR-CAND-001`、`BCR-CAND-002`。
- Revit `RevitCandidateObservationReader`（唯讀）：受管理 Area、樓層帶內牆／柱／梁、本樓層樓板、門／窗／帷幕嵌板。

## Changed Files
- `src/BuildingRegulationReview.Application/Candidates/CandidateObservations.cs`、`CandidateResolutionOptions.cs`、`CandidateSet.cs`、
  `CandidateGeometry.cs`、`CandidateResolver.cs`、`CandidateFacts.cs`（新增）
- `src/BuildingRegulationReview.Application/Diagnostics/ReviewErrorCode.cs`（新增兩個錯誤碼）
- `src/BuildingRegulationReview.Revit/Candidates/RevitCandidateObservationReader.cs`（新增）
- `src/BuildingRegulationReview.Revit/WriteBack/RevitWrittenZoneReader.cs`（`ReadLoops` 改 internal 以重用）
- `tests/BuildingRegulationReview.Core.Tests/Candidates/CandidateModel.cs`、`CandidateResolverTests.cs`（新增）
- `docs/agent/p3-t03-spatial-candidates.md`（新增）、`docs/agent/phase-state.yaml`、`docs/agent/HANDOFF.md`

## Decisions and Assumptions
- 區劃範圍＝本工具寫入的 Area（Revit 回報的邊界）；中心線沿邊界 → Boundary，邊界在厚度內但偏離中心線 → ManualReview。
- 預設：邊界容差 50 mm、平行 5°、最短關係長度 200 mm、開口搜尋 300 mm；構件四類預設 Boundary＋Crossing＋Inside 全取，開口只取邊界（內部開口可開啟）。
- 所有牆的關係都算（開口需要 Host），策略只影響輸出的候選。
- 歧義不轉 facts（`CandidateFacts` 拋例外），直接以規則集 id／版本產生 ManualReview。
- 樓板的 `element.isCompartmentBoundary` 恆為 false；樓板作為垂直區劃構件由規則用 `element.category` 判定。
- 區劃層級問題（未封閉、重疊）記在 `CandidateZone.Problems`，不改寫元素關係；由 T04～T06 決定是否 ManualReview。

## Verification Results
- Solution build 與外掛 csproj `-t:Rebuild`：各 0 warnings、0 errors。
- Core tests：**683/683 通過**（前 643，新增 40）。固定模型 21 列關係、候選清單、12 筆歧義、打亂輸入簽章相同，詳見任務文件。
- Revit adapter 只完成編譯，未在實機模型執行。

## Known Issues / Risks
- Adapter 未接指令／UI，未實機驗證（樓層帶、樓板 level filter、梁寬與門窗尺寸參數、帷幕嵌板 Host）；P3-T09 整合時需在 `建築防火檢討1.rvt` 實跑。
- 寬度未知的牆／梁無法偵測偏離中心線的歧義。
- 容差與門檻為暫定值，未接專案設定；spec 19 第 7 項（邊界判定方式、柱梁板納入方式）未定。
- Phase／Group／Worksharing 未納入；規則集來源與正式條文（spec 19 第 2、6 項）未定。
- `phase-2-acceptance.md` 的未提交修改、`bin/`、`obj/`、`.gitignore`、`.gtoffice/` 不屬於本 Task，刻意不提交。

## Exact Next Steps
1. 讀 spec 11.1、11.3、11.4、13、14，以及本文件、`p3-t03-spatial-candidates.md`、`p3-t02-rule-engine.md`。
2. 執行 P3-T04：區劃面積檢討。以 `CandidateZone.RevitAreaSquareMeters` 為主要實際值（`zone.area`）、`GeometricAreaSquareMeters` 交叉驗證、
   `CandidateFacts.ForZone` 起始 facts，再補用途／灑水等輸入；區劃有 `Problems` 時用 `CandidateAmbiguity.ToReviewResult` 回 ManualReview。
3. Exit：等於上限、超限、缺輸入與豁免測試通過。

## Do Not Do
- 不做 P3-T05 以後的檢討（構件時效、門窗）、結果持久化、視圖標示。
- 不在規則引擎加入任意程式碼執行或函式呼叫語法。
- Revit／WPF 型別不得進 Domain／Application；不 push、不 amend、不 `git add .`。
