# Agent Handoff
- Phase: P3
- Completed Task: P3-T06
- Next Task: P3-T07
- Status: READY_FOR_NEW_SESSION
- Commit: （feat commit，SHA 由後續 docs commit 記錄）
- Spec Version: Draft v1.1 (`docs/fire-review-spec.md`)
- 任務文件：`docs/agent/p3-t06-opening-protection.md`（判定流程表、證據欄位、門／窗／幕牆統計、設計決策都在這裡）
- P2 實機驗收仍有兩項「未回報」（見 `phase-2-acceptance.md`），使用者選擇先進行 P3。

## Completed
- Application `Checks`：`FireProtectionParameters`（`BCR_ProvidedFireProtection`）、`ProvidedFireProtection`（Yes／No／Missing／Unreadable，`FromBoolean`／`FromInteger`）、
  `FireProtectionText.Parse`（只接受明確是非字，全形、大小寫不拘）、`OpeningFireProtection`（Instance／Type scope）、`OpeningProtectionInputs`（Instance 優先於 Type）、
  `OpeningProtectionCheck.Review(...)` → `OpeningProtectionReview`（`OpeningProtectionFinding` 含 `RequiresProtection`、`OpeningGroupSummary` 門／窗／幕牆三列、`Warnings`）、
  `ReviewCheckTypes.OpeningProtection`。
- 彙總狀態規則抽成 internal `ReviewStatusSeverity.Worst`，`TypeRatingSummary` 共用（行為不變）。

## Changed Files
- `src/BuildingRegulationReview.Application/Checks/ProvidedFireProtection.cs`、`OpeningProtectionInputs.cs`、`OpeningProtectionCheck.cs`、`ReviewStatusSeverity.cs`（新增）
- `src/BuildingRegulationReview.Application/Checks/FireResistanceCheck.cs`（改用 `ReviewStatusSeverity`）、`CompartmentAreaCheck.cs`（check type 常數）
- `tests/BuildingRegulationReview.Core.Tests/Checks/OpeningProtectionCheckTests.cs`（新增）
- `docs/agent/p3-t06-opening-protection.md`（新增）、`docs/agent/phase-state.yaml`、`docs/agent/HANDOFF.md`

## Decisions and Assumptions
- 結果粒度為「開口 × 區劃」；是否需要防火保護完全由規則決定（邊界上的小窗可為 NotApplicable），不在檢查裡寫死。
- 是 → Pass、否 → Fail、未設定 → InsufficientData（`BCR-PARAM-001`）、無法判讀 → InsufficientData（`BCR-PARAM-003`）、不適用 → NotApplicable 並保存適用條件欄位與候選關係。
- 幕牆嵌板、帷幕牆上的門窗、非 Hosted、Host 未解析、link、無位置 → ManualReview（MVP 政策，不讀設計值）。
- 防火門等級（甲種）、時效字樣（F60）不當作「是」，視為無法判讀。
- 統計分組：帷幕嵌板與 Host 為帷幕牆者 → 幕牆；空組狀態 NotRun。

## Verification Results
- Solution build 與外掛 csproj `-t:Rebuild`：各 0 warnings、0 errors。
- Core tests：**857/857 通過**（前 809，新增 48）：是／否／未設定／無法判讀、不適用與適用性證據（小窗、內部開口、building 輸入）、共用牆、
  Instance／Type 來源、固定模型的 MVP 政策歧義（11 筆結果與順序）、帷幕牆門、link、區劃問題、無區劃、無規則、缺資料不為 Pass／Fail、
  門／窗／幕牆統計、可重現與 `ReviewRun` 儲存來回、有效 fixture 端到端、是非文字解析。

## Known Issues / Risks
- 沒有 Revit adapter 讀門窗防火屬性，也沒有讀 Type 防火時效的 adapter；三個 Check 都未接指令／UI，未實機執行（P3-T09 整合）。
- spec 19 第 2、4、5、6 項（條文、Shared Parameter GUID、時效型態、區劃輸入來源）未定；防火門等級等要求沒有白名單欄位。
- `phase-2-acceptance.md` 的未提交修改、`bin/`、`obj/`、`.gitignore`、`.gtoffice/` 不屬於本 Task，刻意不提交。

## Exact Next Steps
1. 讀 spec 11.3、11.8、13、14、18，以及本文件、`p3-t01-rule-and-result-schema.md`（`ReviewRun`、`ReviewRunStorageMapper`）、
   P1-T03（`p1-t03-review-package-persistence.md`，DataStorage 持久化模式）。
2. 執行 P3-T07：結果持久化與失效——Run ID、規則版本、元素證據、模型變更 Stale、人工覆寫稽核（原因、操作者、時間、原始／覆寫結果；
   模型或規則版本變更後覆寫改為需重新確認）。
3. Exit：重開模型可讀；模型／規則變更能使結果失效。

## Do Not Do
- 不做 P3-T08 以後的工作（視圖標示、Filled Region、Override、檢討表 UI）。
- 不寫任何 Revit 設計參數；不在規則引擎加入任意程式碼執行或函式呼叫語法。
- Revit／WPF 型別不得進 Domain／Application；不 push、不 amend、不 `git add .`。
