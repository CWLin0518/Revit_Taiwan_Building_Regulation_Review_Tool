# Agent Handoff
- Phase: P3
- Completed Task: P3-T01
- Next Task: P3-T02
- Status: READY_FOR_NEW_SESSION
- Commit: b888904（feat）；SHA 記錄於後續 docs commit
- Spec Version: Draft v1.1 (`docs/fire-review-spec.md`)
- 任務文件：`docs/agent/p3-t01-rule-and-result-schema.md`
- Phase 3 由使用者於 2026-09-22 明確啟動。P2 實機驗收仍有兩項「未回報」（見 `phase-2-acceptance.md`），使用者選擇先開始 P3。

## Completed
- Domain `Rules`：`Rule`、`RuleSet`、`RuleExpression`（只存原始文字）、`RuleCategory`、`RuleSeverity`。
- Domain `Reviews`：六態 `ReviewStatus` + `ReviewStatusText`、`ReviewValue`（數量含 `ReviewUnit`／文字／布林）、
  `ReviewEvidence`、`ReviewResult`、`ReviewRun`（`ReviewRunState`、`Complete`）。
- Application `Rules`：`RuleSetDocument`、`RuleSetSchemaValidator`（全部問題一次列出）、`RuleSetDocumentMapper`。
- Application `Reviews`：`ReviewRunStorageRecord` + `ReviewRunStorageMapper`。
- `ReviewErrorCode.RuleSchemaInvalid`（`BCR-RULE-005`）。
- 規則 fixture：1 個有效、4 個無效。

## Changed Files
- `src/BuildingRegulationReview.Domain/Rules/Rule.cs`（新增）
- `src/BuildingRegulationReview.Domain/Rules/RuleSet.cs`（新增）
- `src/BuildingRegulationReview.Domain/Reviews/ReviewStatus.cs`（新增）
- `src/BuildingRegulationReview.Domain/Reviews/ReviewValue.cs`（新增）
- `src/BuildingRegulationReview.Domain/Reviews/ReviewEvidence.cs`（新增）
- `src/BuildingRegulationReview.Domain/Reviews/ReviewResult.cs`（新增）
- `src/BuildingRegulationReview.Domain/Reviews/ReviewRun.cs`（新增）
- `src/BuildingRegulationReview.Application/Rules/RuleSetDocument.cs`（新增）
- `src/BuildingRegulationReview.Application/Rules/RuleSetSchemaValidator.cs`（新增）
- `src/BuildingRegulationReview.Application/Rules/RuleSetDocumentMapper.cs`（新增）
- `src/BuildingRegulationReview.Application/Reviews/ReviewRunStorageRecord.cs`（新增）
- `src/BuildingRegulationReview.Application/Diagnostics/ReviewErrorCode.cs`
- `tests/BuildingRegulationReview.Core.Tests/BuildingRegulationReview.Core.Tests.csproj`（fixture 複製到輸出）
- `tests/BuildingRegulationReview.Core.Tests/Rules/RuleSetSchemaTests.cs`（新增）
- `tests/BuildingRegulationReview.Core.Tests/Rules/RuleTests.cs`（新增）
- `tests/BuildingRegulationReview.Core.Tests/Rules/Fixtures/*.json`（新增 5 個）
- `tests/BuildingRegulationReview.Core.Tests/Reviews/ReviewResultTests.cs`（新增）
- `tests/BuildingRegulationReview.Core.Tests/Reviews/ReviewRunTests.cs`（新增）
- `docs/agent/p3-t01-rule-and-result-schema.md`（新增）
- `docs/agent/phase-state.yaml`、`docs/agent/HANDOFF.md`

## Decisions and Assumptions
- Pass／Fail 缺實際值或規定值時，建構子直接拒絕，這種情況只能是 InsufficientData（spec 18 第 5 項）；儲存紀錄讀回也走建構子。
- Fail／InsufficientData／ManualReview 必須有 message；除 NotRun 外至少要有 zoneId 或 subject。
- 規則檔與儲存紀錄的列舉都用完整名稱（大小寫敏感、不收數字）。
- 驗證器只做結構檢查；表達式語意留給 P3-T02。fixture 的表達式文字只是示意，DSL 定案後可改。
- Application 不相依序列化器；測試用 `System.Text.Json`（camelCase、拒絕未知屬性）。

## Verification Results
- Solution build 與外掛 csproj `-t:Rebuild`：各 0 warnings、0 errors。
- Core tests：**565/565 通過**（前 517，新增 48）。
- 本 Task 不涉及 Revit 端，沒有手動驗證項目。

## Known Issues / Risks
- 規則集來源（內建檔、DataStorage、外部路徑）未定；spec 19 第 2、6 項（條文版本、面積規則輸入來源）仍待使用者確認。fixture 條文只是測試資料。
- P2 實機驗收尚有兩項未回報；`phase-2-acceptance.md` 的未提交修改刻意不納入 P3 commit。
- 工作目錄中 `bin/`、`obj/`、`.gitignore`、`.gtoffice/` 的變更不屬於本 Task，刻意不提交。

## Exact Next Steps
1. 讀 spec 11.1、11.2、11.4–11.6，以及本文件與 `p3-t01-rule-and-result-schema.md`。
2. 執行 P3-T02：受限規則引擎（白名單欄位、Expression DSL、優先序、豁免、單位、衝突偵測），
   輸入是 `RuleSet` / `RuleExpression.Source`，輸出以 `ReviewStatus` / `ReviewValue` 表達。
3. 需要時更新 fixture 的表達式文字，使其符合定案的 DSL。

## Do Not Do
- 不得執行任意程式碼（不用 Roslyn scripting、`DataTable.Compute`、反射呼叫等）。
- 不做 P3-T03 以後的候選元素解析、Revit 讀取、持久化、視圖標示。
- Revit／WPF 型別不得進 Domain／Application；不 push、不 amend、不 `git add .`。
