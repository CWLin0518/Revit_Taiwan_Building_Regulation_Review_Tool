# Agent Handoff
- Phase: P3
- Completed Task: P3-T04
- Next Task: P3-T05
- Status: READY_FOR_NEW_SESSION
- Commit: 5ee1e4c（feat）；SHA 由後續 docs commit 記錄
- Spec Version: Draft v1.1 (`docs/fire-review-spec.md`)
- 任務文件：`docs/agent/p3-t04-compartment-area.md`（判定流程表、證據欄位、設計決策都在這裡）
- P2 實機驗收仍有兩項「未回報」（見 `phase-2-acceptance.md`），使用者選擇先進行 P3。

## Completed
- Application `Checks`：`ReviewInput`（Known／Unreadable＋來源）、`CompartmentAreaInputs`（building.*／各區劃 zone.*，模型欄位不得輸入）、
  `CompartmentAreaOptions`（交叉驗證容差，預設 1%）、`CompartmentAreaCheck.Review(...)` → `CompartmentAreaReview`／`ZoneAreaFinding`、
  `ReviewCheckTypes.CompartmentArea`。
- Revit Area 合計為 `zone.area`；邊界量算交叉驗證，超過容差時 Pass／Fail 改 ManualReview（`BCR-AREA-001`）。
- 區劃有 `Problems` → ManualReview（`BCR-CAND-002`，歸屬規則集）；沒有區劃或有區劃無封閉面積 → 整批 `Result.Failure`（spec 11.1）。
- 證據保存每個 Area 的兩個面積、合計、差異、容差、輸入來源與缺漏欄位。

## Changed Files
- `src/BuildingRegulationReview.Application/Checks/ReviewInput.cs`、`CompartmentAreaInputs.cs`、`CompartmentAreaCheck.cs`（新增）
- `tests/BuildingRegulationReview.Core.Tests/Checks/CompartmentAreaCheckTests.cs`（新增）
- `docs/agent/p3-t04-compartment-area.md`（新增）、`docs/agent/phase-state.yaml`、`docs/agent/HANDOFF.md`

## Decisions and Assumptions
- 兩個面積不一致時不猜：只有 Pass／Fail 會被改成 ManualReview，其他狀態保留並附註；容差與 P2 寫回相同（1%）。
- 輸入來源（spec 19 第 6 項）未定，因此以純資料 `ReviewInput` 接收，來源寫入證據 `source[<欄位>]`。
- 輸入型別／單位錯誤＝呼叫端程式錯誤（拋例外）；參數值無法解析才是 `Unreadable` → InsufficientData。
- 本 Task 不做持久化（P3-T07）、Filled Region 與檢討表（P3-T08）。

## Verification Results
- Solution build 與外掛 csproj `-t:Rebuild`：各 0 warnings、0 errors。
- Core tests：**715/715 通過**（前 683，新增 32）：等於上限、超限、缺輸入（含 Revit Area 缺值／為 0）、豁免（成立／未知）、不適用、無規則、
  交叉驗證（差 20%／容差內／可設定／非判定狀態）、區劃重疊與部分未封閉、整批拒絕、輸入契約、排序與 `ReviewRun` 儲存來回、有效 fixture 端到端。

## Known Issues / Risks
- 區劃用途、灑水、構造、樓層等輸入沒有 Revit adapter 或 UI；P3-T09 整合時要決定來源。
- Check 未接指令／UI，未實機執行；P3-T03 的 `RevitCandidateObservationReader` 也仍未實跑。
- 規則集來源與正式條文（spec 19 第 2 項）未定；容差暫定（spec 19 第 7 項）。
- `phase-2-acceptance.md` 的未提交修改、`bin/`、`obj/`、`.gitignore`、`.gtoffice/` 不屬於本 Task，刻意不提交。

## Exact Next Steps
1. 讀 spec 11.1、11.3、11.5、13、14，以及本文件、`p3-t04-compartment-area.md`、`p3-t03-spatial-candidates.md`、`p3-t02-rule-engine.md`。
2. 執行 P3-T05：構件防火時效檢討。以 `CandidateFacts.ForMember` 起始 facts，讀 Type 的 Provided 時效（缺值、格式錯誤 → InsufficientData，
   複合構造無法判定 → ManualReview），規則引擎算 Required；Required 與 Provided 分離，不得覆寫 Provided；結果按 Type 彙總；
   歧義成員用 `CandidateAmbiguity.ToReviewResult` 回 ManualReview。可比照本 Task 的 `Checks` 命名空間與 `ReviewInput` 模式。
3. Exit：各 Category（牆／柱／梁／樓板）與邊界值測試通過，不覆寫 Provided 值。

## Do Not Do
- 不做 P3-T06 以後的檢討（門窗）、結果持久化、視圖標示。
- 不在規則引擎加入任意程式碼執行或函式呼叫語法。
- Revit／WPF 型別不得進 Domain／Application；不 push、不 amend、不 `git add .`。
