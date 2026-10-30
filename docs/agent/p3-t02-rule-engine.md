# P3-T02 — 受限規則引擎

## 完成範圍

- Domain `Rules.Expressions`（全新，不相依 Revit／UI）：
  - `RuleExpressionParser`：自寫的遞迴下降 parser。只認得字面值、白名單欄位、`! - * / + - < <= > >= == != && || ? :` 與括號；
    沒有函式呼叫、指派、成員存取、索引或陳述式。上限 1000 字元、巢狀 32 層。錯誤一律拋 `RuleExpressionException`（穩定 code＋字元位置）。
  - `RuleExpressionNode`：編譯時即做型別與單位檢查的 AST；`Evaluate(RuleFacts)` 只讀 facts；`ToString()` 為完整括號的正規形式（用於衝突偵測與訊息）。
  - `RuleValueType`（是非值／文字／帶單位數值）、`RuleUnits`（字面單位換算、單位運算、比較容差）、`RuleEvaluation`（已知值／未知＋缺漏欄位／運算失敗）。
  - `RuleRequirement`：`requiredValue` 編譯結果 = 實際值欄位 + 比較子 + 要求值運算式。
- Domain `Rules`：
  - `RuleFieldCatalog` / `RuleFieldDefinition`：白名單欄位（名稱、型別與單位、可用的規則類別）；`Default` 共 21 個欄位（building／zone 全類別可用，element 只給 FireResistance，opening 只給 OpeningProtection）。
  - `RuleFacts`：單一主體的欄位值；設定時即檢查欄位在白名單且型別／單位正確；未設定＝Missing、`MarkUnreadable`＝格式無法判讀。
  - `CompiledRule` / `CompiledRuleSet`、`RuleEngine`、`RuleEvaluationContext`（審查日期＋轄區）、`RuleOutcome`（`ToReviewResult(...)`）、`RuleOutcomeReason`。
- Application `Rules`：
  - `RuleSetCompiler`：`Load(document)`（schema 驗證 → 編譯）、`Compile(ruleSet)`、`Check(...)`；一次列出所有問題（沿用 `RuleSchemaIssue` 與 `rules[i].xxx` 路徑）。
    運算式／欄位錯誤 → `BCR-RULE-005`；只有必然衝突 → `BCR-RULE-002`。
  - `RuleOutcomeErrorCode.For(outcome)`：NoRule → `BCR-RULE-001`、Conflict → `BCR-RULE-002`、ComputationFailed → `BCR-RULE-003`。
- Fixture：`valid-ruleset.json` 表達式改為定案 DSL；新增 `invalid-expressions.json`。

## DSL 定案

- 欄位一律 `scope.name`（大小寫敏感），必須在白名單且適用於規則類別。
- 數值與帶單位量必須寫明單位：`zone.area <= 1500` 是型別錯誤，`zone.area <= 1500 m2` 才正確。
  字面單位：`m2`、`㎡`、`m`、`cm`、`mm`、`min`、`h`、`hr`，解析時即換成標準單位（m2／m／min）。
  單位運算：同單位加減；純數值可乘除任何單位；m×m=m2、m2÷m=m、同單位相除=純數值；其他組合編譯失敗。
- 比較不可連寫；`<` 等只用於數值；`==`／`!=` 需兩邊同型別同單位。文字用雙引號，跳脫只有 `\"`、`\\`。
- `appliesWhen`、`exemptions` 必須是是非值。
- `requiredValue` 必須是 `實際值欄位 比較子 要求值運算式`，例如
  `zone.area <= (zone.sprinklered ? 3000 m2 : 1500 m2)`、`element.providedFireRating >= 60 min`、`opening.providedFireProtection == "是"`；
  右側不可再引用實際值欄位。

## 引擎語意

1. 只考慮 `effectiveDate <= 審查日` 且轄區相同的規則；一條都沒有 → ManualReview（NoRule）。
2. 由高優先序往下：第一個有規則適用的優先序決定結果。若該優先序（或更高）有規則「不知是否適用」 → InsufficientData；低優先序不會在高優先序可能適用時搶先判定。
3. 同一優先序多條規則適用：各自評估，狀態、比較子與要求值都一致才採用（取 ruleId 最小者），否則 ManualReview（Conflict，列出各規則結論）。
4. 豁免成立 → NotApplicable（Exempt，保存豁免欄位證據）。豁免無法判定時：要求值通過 → Pass；未通過 → InsufficientData（可能符合豁免）。
5. 實際值或要求值缺漏／無法判讀 → InsufficientData，仍盡量回報可算出的要求值。**缺資料永遠不會變成 Fail。**
6. 運算錯誤（除以零、溢位）→ ManualReview（ComputationFailed），不是 Fail。
7. 無規則適用 → NotApplicable（NoRuleApplies），證據包含適用條件用到的已知欄位。
8. 邏輯採三值：`false && 未知` = false、`true || 未知` = true；條件未知的 `? :` 只在兩分支相等時有值。
9. 數值比較使用相對容差 1e-9，「等於上限」穩定判為符合。

## 驗證

- Solution build 與外掛 csproj `-t:Rebuild`：各 0 warnings、0 errors。
- Core tests：**643/643 通過**（P3-T02 前 565，新增 78）。
  - `RuleExpressionTests`：優先序與正規形式、單位換算、單位／型別錯誤、條件必須是是非值、**任意程式碼樣式一律拒絕**（呼叫、`=`、`;`、單引號、`&`、索引、`${}`、`new`、成員存取）、白名單與類別限制、錯誤位置、長度與巢狀上限、跳脫、要求值形式、三值邏輯、除以零、比較容差、facts 型別檢查、預設白名單。
  - `RuleEngineTests`：面積／時效邊界值（等於上限、容差、超限）、h→min、缺實際值、缺要求值輸入、格式錯誤、不適用與證據、適用性未知、豁免成立／未知、優先序覆蓋、高優先序未知、同優先序衝突／一致、生效日與轄區、無規則、運算錯誤、門窗 是／否／未設定、證據順序、所有狀態都能轉成合法 `ReviewResult`、白名單不一致拒絕、單獨計算要求值。
  - `RuleSetCompilerTests`：有效 fixture 端到端、無效運算式 fixture 逐項 (path, code)、結構錯誤先擋、必然衝突 `BCR-RULE-002`、不構成衝突的情況、編譯集合完整性。

## 設計決策

- 靜態衝突只抓「同類別、同優先序、同轄區、正規化後同一適用條件、要求不同」這種必然衝突，並在載入時拒絕；依資料才重疊的情況交給引擎逐主體回報 ManualReview。
- 引擎只接受用同一個白名單建立的 `RuleFacts`（參考相等），避免欄位定義不一致。
- 高優先序的規則適用時，豁免不會把判定交還給低優先序規則：豁免屬於該規則本身。
- `RuleOutcome` 不帶 run／package／subject；由 P3-T04～T06 的檢查呼叫 `ToReviewResult(...)` 補上。NoRule 時 ruleId／version 用規則集自身的 id／version。
- 格式錯誤目前一律 InsufficientData（訊息寫明「格式無法判讀」）；複合構造等需人工判斷的情況由 P3-T05 自行回 ManualReview。

## 未解決問題

- 白名單欄位是依 spec 11.4～11.6 推估的最小集合，P3-T03～T06 實作取值時可能需增減（改 `RuleFieldCatalog.Default` 並補測試）。
- 規則集來源與正式條文（spec 19 第 2、6 項）仍未定，fixture 只是測試資料。
- `ReviewUnit.Count` 目前沒有字面單位，也沒有欄位使用；層數用純數值。

## 下一階段目標（P3-T03）

- 候選元素與空間關係解析：區劃、邊界牆、相交構件、Hosted 開口、來源證據；歧義回傳 ManualReview。
- 產出應能填入本 Task 的 `RuleFacts` 欄位（zone.*／element.*／opening.*）。
