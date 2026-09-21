# Agent Handoff
- Phase: P3
- Completed Task: P3-T02
- Next Task: P3-T03
- Status: READY_FOR_NEW_SESSION
- Commit: （feat commit SHA 記錄於後續 docs commit）
- Spec Version: Draft v1.1 (`docs/fire-review-spec.md`)
- 任務文件：`docs/agent/p3-t02-rule-engine.md`（DSL 定案與引擎語意都在這裡）
- P2 實機驗收仍有兩項「未回報」（見 `phase-2-acceptance.md`），使用者選擇先進行 P3。

## Completed
- Domain `Rules.Expressions`：自寫 parser（`RuleExpressionParser`）、型別化 AST（`RuleExpressionNode`）、`RuleValueType`、`RuleUnits`、`RuleEvaluation`、`RuleRequirement`、`RuleExpressionException`。
- Domain `Rules`：`RuleFieldCatalog`（白名單，`Default` 21 欄位）、`RuleFacts`、`CompiledRule`／`CompiledRuleSet`、`RuleEngine`、`RuleEvaluationContext`、`RuleOutcome`（`ToReviewResult`）。
- Application `Rules`：`RuleSetCompiler`（Load／Compile／Check，`BCR-RULE-005`／`BCR-RULE-002`）、`RuleOutcomeErrorCode`（`BCR-RULE-001/002/003`）。
- Fixture：`valid-ruleset.json` 改為定案 DSL；新增 `invalid-expressions.json`。

## Changed Files
- `src/BuildingRegulationReview.Domain/Rules/Expressions/*.cs`（新增 6 檔）
- `src/BuildingRegulationReview.Domain/Rules/RuleFieldCatalog.cs`、`RuleFacts.cs`、`CompiledRuleSet.cs`、`RuleEngine.cs`、`RuleOutcome.cs`（新增）
- `src/BuildingRegulationReview.Application/Rules/RuleSetCompiler.cs`、`RuleOutcomeErrorCode.cs`（新增）
- `tests/BuildingRegulationReview.Core.Tests/Rules/RuleExpressionTests.cs`、`RuleEngineTests.cs`、`RuleSetCompilerTests.cs`（新增）
- `tests/BuildingRegulationReview.Core.Tests/Rules/Fixtures/valid-ruleset.json`（修改）、`invalid-expressions.json`（新增）
- `docs/agent/p3-t02-rule-engine.md`（新增）、`docs/agent/phase-state.yaml`、`docs/agent/HANDOFF.md`

## Decisions and Assumptions
- `requiredValue` 必須寫成 `實際值欄位 比較子 要求值`；數值必須帶單位，字面單位在解析時換成 m2／m／min。
- 缺資料／格式錯誤 → InsufficientData；運算錯誤、無規則、同優先序衝突 → ManualReview；絕不因缺資料得到 Fail。
- 高優先序規則可能適用但無法判定時，不讓低優先序規則判定（InsufficientData）。
- 豁免無法判定只在「未通過」時才讓結果變 InsufficientData。
- 載入時只拒絕「必然衝突」（同類別／優先序／轄區／正規化條件、要求不同）；資料相依的衝突由引擎逐主體回報。
- 數值比較相對容差 1e-9。

## Verification Results
- Solution build 與外掛 csproj `-t:Rebuild`：各 0 warnings、0 errors。
- Core tests：**643/643 通過**（前 565，新增 78）。
- 本 Task 不涉及 Revit 端，沒有手動驗證項目。

## Known Issues / Risks
- 白名單欄位是推估的最小集合，P3-T03～T06 取值時可能要增減（改 `RuleFieldCatalog.Default` 並補測試）。
- 規則集來源與正式條文版本（spec 19 第 2、6 項）未定；fixture 只是測試資料。
- `phase-2-acceptance.md` 的未提交修改、`bin/`、`obj/`、`.gitignore`、`.gtoffice/` 不屬於本 Task，刻意不提交。

## Exact Next Steps
1. 讀 spec 11.1、11.4～11.6、16，以及本文件與 `p3-t02-rule-engine.md`。
2. 執行 P3-T03：候選元素與空間關係解析（區劃、邊界牆、相交構件、Hosted 開口、來源證據），固定模型的候選集合要可重現，歧義回傳 ManualReview。
3. 解析結果應能轉成 `RuleFacts`（zone.*／element.*／opening.*），但實際的面積／時效／門窗檢討是 P3-T04～T06。

## Do Not Do
- 不得在規則引擎加入任意程式碼執行（Roslyn scripting、`DataTable.Compute`、反射呼叫等）或函式呼叫語法。
- 不做 P3-T04 以後的檢討、持久化、視圖標示。
- Revit／WPF 型別不得進 Domain／Application；不 push、不 amend、不 `git add .`。
