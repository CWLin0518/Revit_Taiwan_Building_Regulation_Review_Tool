# P3-T06 — 門窗防火保護檢討

## 完成範圍

- Application `Checks`（不相依 Revit／UI，core tests 覆蓋）：
  - `ProvidedFireProtection.cs`：
    - `FireProtectionParameters.Provided = 防火檢討_設計防火保護`（預設來源參數名稱；檢查只讀不寫）。
    - `ProvidedFireProtection`：四種狀態 `Yes`（是）／`No`（否）／`Missing`（未設定）／`Unreadable`（無法判讀），保留原始文字與原因；
      `RuleText` 給規則用（`是`／`否`）；`FromBoolean`、`FromInteger`（Revit 是非參數 1／0，其他值 Unreadable）。
    - `FireProtectionText.Parse(raw)`：全形轉半形、不分大小寫；只接受明確的是非字（`是 有 yes y true 1`／`否 無 无 沒有 没有 no n false 0`），
      空白＝Missing；`甲種防火門`、`F60`、`是否`、`2` 等 → Unreadable（不猜測等級或時效）。
  - `OpeningProtectionInputs.cs`：`OpeningFireProtection`（`FireProtectionScope.Instance`／`Type`、UniqueId、值、來源參數）、
    `OpeningProtectionInputs`（building／zone 輸入沿用 `CompartmentAreaInputs`，因此 `opening.*` 無法以輸入提供；同一 scope＋UniqueId 重複即拒絕）。
    **Instance 值優先於 Type 值**，沒有 Instance 值才用 Type 值。
  - `OpeningProtectionCheck.Review(set, inputs, engine, context, runId, newResultId)` → `Result<OpeningProtectionReview>`：
    `OpeningProtectionFinding`（每個開口 × 區劃一筆，另加每個歧義開口關係一筆；含 `RequiresProtection`）、
    `OpeningGroupSummary`（門／窗／幕牆三列，永遠三列）、`Warnings`。
  - `ReviewStatusSeverity.Worst`（internal）：彙總狀態取最嚴重，`TypeRatingSummary` 改用它（行為不變）。
  - `ReviewCheckTypes.OpeningProtection = "OpeningProtection"`。

## 判定流程（每個開口 × 區劃）

| 條件 | 結果 | 錯誤碼 |
| --- | --- | --- |
| 沒有區劃，或有區劃沒有任何封閉面積 | 整批 `Result.Failure`（spec 11.1） | `BCR-CAND-002` |
| 關係為 Ambiguous（幕牆開口、帷幕嵌板、非 Hosted、Host 未解析、Host 關係歧義、無位置、link） | `CandidateAmbiguity.ToReviewResult` → ManualReview，歸屬規則集；**不看設計值** | `BCR-CAND-001` |
| 沒有位置也沒有 Host（`OpeningLocationUnknown`，不指定區劃） | ManualReview，ZoneId 為 null | `BCR-CAND-001` |
| 區劃有 `Problems`（未封閉、重疊） | ManualReview，不跑規則，證據含 `zone.problems` | `BCR-CAND-002` |
| 其他 | facts＝`CandidateFacts.ForOpening`＋building／zone 輸入＋`opening.providedFireProtection` → `RuleEngine.Evaluate(OpeningProtection)` | 依 `RuleOutcomeErrorCode` |
| 　規則不適用（例如小窗、區劃內部開口、非防火構造） | NotApplicable，`RequiresProtection = false`，證據保存適用條件用到的欄位＋候選關係 | — |
| 　需要防火保護且為 `是` | Pass | — |
| 　需要防火保護且為 `否` | Fail，實際值 `否`、要求值 `是` | — |
| 　`未設定` | InsufficientData，仍回報要求值 | `BCR-PARAM-001` |
| 　無法判讀 | InsufficientData，訊息含原始值與原因 | `BCR-PARAM-003` |
| 　適用性所需輸入缺漏（如 `building.fireResistiveConstruction`） | InsufficientData，`RequiresProtection = null` | — |
| 　沒有 OpeningProtection 規則 | ManualReview（NoRule） | `BCR-RULE-001` |

缺資料、無法判讀、歧義都**不會**變成 Fail 或 Pass。是否需要防火保護一律由規則決定，位於邊界本身不構成要求（spec 11.6 第 2 點）。

## `RequiresProtection`

- `true`：Pass／Fail，或唯一缺的是設計值且規則已給出要求值。
- `false`：NotApplicable。
- `null`：歧義、區劃問題、無規則、衝突、適用性未知。

## 證據

引擎證據（`opening.*`、`zone.*`、規則 `evidenceFields`）之後依序加上：候選關係（`candidate.zoneId／relation／distanceToBoundary`…）、
來源（`source.elementUniqueId／documentUniqueId／category／typeName／hostUniqueId／hostIsCurtainWall`）、`source.typeUniqueId`、
`provided.kind`、`provided.scope`（Instance／Type）、`provided.parameter`、`provided.raw`、`provided.reason`、每個有來源的輸入 `source[<欄位>]`、缺漏欄位 `rule.gaps`。

## 依門／窗／幕牆統計（spec 11.7）

- 分組：帷幕嵌板、以及 Host 為帷幕牆的門窗（含 `CurtainWallOpening` 歧義）→ 幕牆；其餘依類別為門或窗。
- 每個開口只算一次（共用邊界牆上的門在兩區各一筆結果，`OpeningCount` 仍為 1）；`Count(status)`。
- 狀態取最嚴重：Fail ＞ ManualReview ＞ InsufficientData ＞ Pass ＞ NotApplicable；沒有任何結果的組為 NotRun（未檢討）。
- `Groups` 永遠是門、窗、幕牆三列，檢討表不需要自行補列。

## 驗證

- Solution build：0 warnings／0 errors；外掛 csproj `-t:Rebuild`：0 warnings／0 errors。
- Core tests：**857/857 通過**（P3-T06 前 809，新增 48，全部在 `OpeningProtectionCheckTests`）：
  - **是／否**：`是 有 Yes １` → Pass、`否 無 false` → Fail；實際值／要求值、主體、區劃、規則、Host、訊息與證據欄位。
  - **未設定**：無輸入、空字串、空白 → InsufficientData `BCR-PARAM-001`，保留要求值與 `rule.gaps`。
  - **無法判讀**：`甲種`、`F60`、`是/否` → InsufficientData `BCR-PARAM-003`，訊息含原始值。
  - **不適用與適用性證據**：邊界上的小窗（2.1 m² ≤ 3 m²）即使為 `否` 也 NotApplicable 並保存 `opening.kind／area／hostIsCompartmentBoundary`；
    大窗 `否` → Fail；區劃內部開口預設不列候選、開啟 `includeInteriorOpenings` 後 NotApplicable（relation Inside）；
    依 building 輸入決定是否需要（true → Fail、false → NotApplicable、缺漏 → InsufficientData）。
  - 共用邊界牆上的門兩區各一筆、統計只算一次；Instance 優先於 Type、Type 補值、輸入不被修改。
  - **MVP 政策**：固定模型（P3-T03 `CandidateModel`，A／B 兩區）逐筆比對 11 筆結果與順序、7 筆歧義種類；帷幕嵌板即使為 `否` 仍 ManualReview；
    帷幕牆上的門歸幕牆組；link 門 → LinkedElement；構件歧義不混入。
  - 重疊區劃 `BCR-CAND-002`、無區劃／未封閉區劃整批拒絕、無規則 `BCR-RULE-001`、缺資料永不為 Pass／Fail。
  - 門／窗／幕牆統計、群組最嚴重狀態；排序與反轉輸入可重現、`ReviewRun` 儲存來回；有效 fixture（`tw-bcr-76-door`）端到端。
  - 是非文字解析、整數／布林參數對應。
- 未實機執行（Check 尚未接指令／UI，也沒有讀門窗參數的 Revit adapter）。

## 設計決策

- **結果粒度為「開口 × 區劃」**，與 P3-T05 一致；共用牆上的門對兩區可能有不同要求。
- **歧義一律 ManualReview，不讀設計值**：幕牆、非 Hosted、Host 未解析屬 V1.1 範圍（spec 17.1／17.2），此時設計值無法證明任何事。
- **Instance 優先於 Type**：Revit 的防火門屬性可能放在 Instance 或 Type；兩者都有時以 Instance 為準（如同 Revit 參數覆寫）。
- **解析不猜測**：防火門等級（甲種／乙種）、時效字樣（F60）不等於「是」，視為無法判讀 → 資料不足；等級或時效要求需另立規則欄位（未做）。
- 錯誤碼沿用 spec 14「參數缺失／資料型態錯誤」，未新增。

## 未解決問題

- 尚無 Revit adapter 讀取門窗防火屬性（參數名稱、Instance／Type、Yes/No 或 Text 儲存型態）；P3-T09 整合時需決定並實跑。
- spec 19 第 4 項（門窗防火屬性既有 Shared Parameter GUID）未定；`防火檢討_設計防火保護` 只是預設名稱。
- 規則集正式條文（spec 19 第 2 項）未定，測試規則只是示意；防火門窗的等級／遮煙性能等要求目前沒有白名單欄位。
- 本 Task 不做結果持久化（P3-T07）、紅色 Override 標示與檢討表（P3-T08）。

## 下一階段目標（P3-T07）

- 結果持久化與失效：Run ID、規則版本、元素證據、模型變更 Stale、人工覆寫稽核（spec 11.3、11.8、13）。
- Exit：重開模型可讀；模型／規則變更能使結果失效。
