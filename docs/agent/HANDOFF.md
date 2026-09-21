# Agent Handoff
- Phase: P3
- Completed Task: P3-T05
- Next Task: P3-T06
- Status: READY_FOR_NEW_SESSION
- Commit: 6a30af3（feat）；SHA 由後續 docs commit 記錄
- Spec Version: Draft v1.1 (`docs/fire-review-spec.md`)
- 任務文件：`docs/agent/p3-t05-fire-resistance.md`（判定流程表、證據欄位、Type 彙總、設計決策都在這裡）
- P2 實機驗收仍有兩項「未回報」（見 `phase-2-acceptance.md`），使用者選擇先進行 P3。

## Completed
- Application `Checks`：`FireRatingParameters`（`BCR_ProvidedFireRating`／`BCR_RequiredFireRating`）、`ProvidedFireRating`（Rated／Missing／Unreadable／Undeterminable）、
  `FireRatingText.Parse`（分鐘、小時、中文、全形；多個時效＝複合構造）、`TypeFireRating`（拒絕以 Required 參數為來源）、`FireResistanceInputs`、
  `FireResistanceCheck.Review(...)` → `FireResistanceReview`（`MemberRatingFinding`、`TypeRatingSummary`、`Warnings`）、`ReviewCheckTypes.FireResistance`。
- spec 11.1 前置檢查抽成 internal `ReviewPreconditions.Zones`，面積檢查共用（訊息不變）。
- `ReviewErrorCode.FireRatingUndetermined = "BCR-RATE-001"`。

## Changed Files
- `src/BuildingRegulationReview.Application/Checks/ProvidedFireRating.cs`、`FireResistanceInputs.cs`、`FireResistanceCheck.cs`、`ReviewPreconditions.cs`（新增）
- `src/BuildingRegulationReview.Application/Checks/CompartmentAreaCheck.cs`（改用 `ReviewPreconditions`、新增 check type 常數）
- `src/BuildingRegulationReview.Application/Diagnostics/ReviewErrorCode.cs`（`BCR-RATE-001`）
- `tests/BuildingRegulationReview.Core.Tests/Checks/FireRatingTextTests.cs`、`FireResistanceCheckTests.cs`（新增）
- `docs/agent/p3-t05-fire-resistance.md`（新增）、`docs/agent/phase-state.yaml`、`docs/agent/HANDOFF.md`

## Decisions and Assumptions
- 結果粒度為「構件 × 區劃」；Inside／Crossing 也送進規則，由規則決定是否適用。
- 設計值只當 `actualValue`，要求值只當 `requiredValue`；檢查不寫參數、不改輸入。寫回要求值只能用 `BCR_RequiredFireRating`（P3-T07／T08 決定是否需要）。
- 缺值 → InsufficientData（`BCR-PARAM-001`）、格式錯誤 → InsufficientData（`BCR-PARAM-003`）、複合構造且只缺設計值 → ManualReview（`BCR-RATE-001`），都不會變成 Fail。
- Type 狀態取最嚴重：Fail ＞ ManualReview ＞ InsufficientData ＞ Pass ＞ NotApplicable。
- 提供值型態（spec 19 第 5 項）未定：文字與數值參數都支援，裸數字單位可設定（預設分鐘）。

## Verification Results
- Solution build 與外掛 csproj `-t:Rebuild`：各 0 warnings、0 errors。
- Core tests：**809/809 通過**（前 715，新增 94）：各 Category 邊界值、building 輸入決定要求值、不覆寫 Provided、缺值／格式錯誤／複合構造、
  歧義與區劃問題、整批拒絕、無規則、Type 彙總、輸入契約、可重現與 `ReviewRun` 儲存來回、有效 fixture 端到端；時效解析另有完整測試。

## Known Issues / Risks
- 沒有 Revit adapter 讀 Type 防火時效參數；Check 未接指令／UI，未實機執行（P3-T09 整合）。
- 區劃用途、灑水、構造、層數等輸入來源（spec 19 第 6 項）、規則集正式條文（第 2 項）、防火時效參數 GUID 與型態（第 4、5 項）未定。
- `phase-2-acceptance.md` 的未提交修改、`bin/`、`obj/`、`.gitignore`、`.gtoffice/` 不屬於本 Task，刻意不提交。

## Exact Next Steps
1. 讀 spec 11.1、11.3、11.6、13、14、17.1，以及本文件、`p3-t05-fire-resistance.md`、`p3-t03-spatial-candidates.md`（開口判定表）、`p3-t02-rule-engine.md`。
2. 執行 P3-T06：門窗防火保護檢討。以 `CandidateFacts.ForOpening` 起始，讀 `opening.providedFireProtection`（是／否／未設定），
   先由規則判斷是否需要防火保護；幕牆嵌板、非 Hosted、Host 未解析依 MVP 政策（`CandidateAmbiguity.ToReviewResult` → ManualReview）；
   不適用仍保存適用性證據。可比照 `FireResistanceCheck` 的結構（`ReviewPreconditions`、區劃問題 ManualReview、依門／窗／幕牆統計）。
3. Exit：是／否／未設定／不適用結果與證據正確。

## Do Not Do
- 不做 P3-T07 以後的工作（結果持久化、失效、視圖標示、檢討表）。
- 不寫任何 Revit 參數；不在規則引擎加入任意程式碼執行或函式呼叫語法。
- Revit／WPF 型別不得進 Domain／Application；不 push、不 amend、不 `git add .`。
