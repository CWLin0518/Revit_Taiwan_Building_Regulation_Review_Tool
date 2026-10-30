# P3-T04 — 區劃面積檢討

## 完成範圍

- Application `Checks`（全新，不相依 Revit／UI，core tests 覆蓋）：
  - `ReviewInput`：一個規則欄位的輸入值（`Known`／`Unreadable`＋原因）與來源文字（例如「專案設定」、參數名稱），`ApplyTo(RuleFacts)`。
    沒提供的欄位就是缺漏，不以 false／0 代替。
  - `CompartmentAreaInputs`：建築物輸入（只能 `building.*`）與各區劃輸入（只能 `zone.*`，依 Zone ID）；
    `zone.id`、`zone.area`、`zone.levelName` 由模型取得，**不得當作輸入**（避免輸入蓋過模型）；同欄位重複提供即拒絕。
  - `CompartmentAreaOptions`：交叉驗證相對容差，預設 1%（= `AreaAgreement.DefaultRelativeTolerance`，與 P2 寫回一致）。
  - `CompartmentAreaCheck.Review(set, inputs, engine, context, runId, options, newResultId)` → `Result<CompartmentAreaReview>`；
    `CompartmentAreaReview`（每區劃一個 `ZoneAreaFinding`，依 Zone ID 排序、`Warnings`、`Count(status)`）；
    `ZoneAreaFinding`（`ReviewResult`、引擎 `RuleOutcome`、Revit／幾何面積、`AreaCrossCheck`、相對差異、錯誤碼）；
    `ReviewCheckTypes.CompartmentArea = "CompartmentArea"`。

## 判定流程（每個區劃）

| 條件 | 結果 | 錯誤碼 |
| --- | --- | --- |
| 工作包沒有區劃，或有區劃完全沒有封閉面積（`CandidateSet.CanReview` 為否） | 整批不檢討，回 `Result.Failure` 並列出區劃與修正方式（spec 11.1） | `BCR-CAND-002` |
| 區劃有 `Problems`（部分面積未封閉、與其他區劃重疊） | ManualReview，不跑規則；主體＝區劃所有 Area＋相關歧義主體；歸屬規則集 id／版本 | `BCR-CAND-002` |
| 其他 | facts＝`CandidateFacts.ForZone`＋`zone.area`（Revit Area 合計）＋輸入 → `RuleEngine.Evaluate(CompartmentArea)` | 依 `RuleOutcomeErrorCode` |
| 　Revit Area 未回報 | `zone.area` 缺漏 → 引擎 InsufficientData（仍回報要求值） | — |
| 　Revit Area 為 0 | `zone.area` 標為無法判讀 → InsufficientData | — |
| 　Revit 與邊界量算差異 > 容差，引擎為 Pass／Fail | 改為 ManualReview，保留實際值、要求值與規則歸屬，訊息寫出兩個面積、差異與原本結論 | `BCR-AREA-001` |
| 　差異 > 容差，引擎為其他狀態 | 狀態不變，訊息附註面積來源無法確認 | `BCR-AREA-001`（若無更優先的碼） |

適用條件、上限（例如依 `zone.sprinklered` 1500／3000 m²）、豁免與缺資料都由 P3-T02 規則引擎決定；等於上限依引擎相對容差 1e-9 判為符合。

## 證據（保存計算過程）

引擎證據（實際值欄位＋規則 `evidenceFields`，例如 `zone.area`、`zone.sprinklered`、`building.fireResistiveConstruction`）之後依序加上：
`zone.name`、`area.partCount`、每個 Area 的 `area.part[<UniqueId>].revit`／`.geometric`、`area.revit`、`area.geometric`、
`area.crossCheck`（Agrees／Differs／NotComparable）、`area.relativeDifference`、`area.crossCheckTolerance`、
每個有來源的輸入 `source[<欄位>]`、缺漏欄位 `rule.gaps`。區劃問題的結果另有 `zone.problems`。

## 驗證

- Solution build：0 warnings／0 errors；外掛 csproj `-t:Rebuild`：0 warnings／0 errors。
- Core tests：**715/715 通過**（P3-T04 前 683，新增 32，`tests/.../Checks/CompartmentAreaCheckTests.cs`）：
  - **等於上限／超限**：1499.9、1500 → Pass；1500.5 → Fail；灑水 3000 → Pass、3000.5 → Fail；多 Area 以合計檢討；孔洞不計入。
  - **缺輸入**：缺灑水（面積再大也是 InsufficientData，要求值為 null）、缺構造（適用性未定）、輸入無法判讀（含原因與來源）、
    Revit Area 未回報、Revit Area 為 0；四種缺漏組合一律不會變成 Fail。
  - **豁免**：用途「樓梯間」→ NotApplicable（Exempt）；豁免欄位缺漏時未超限 Pass、超限 InsufficientData；構造不適用 → NotApplicable 且保存證據；
    規則集沒有面積規則 → ManualReview `BCR-RULE-001`。
  - **交叉驗證**：差 20% 時原本 Pass／Fail 都改 ManualReview `BCR-AREA-001`；差 0.99%／0.9% 時由 Revit Area 決定；容差可設定；
    非判定狀態（豁免、缺資料）不被改寫但記錄錯誤碼。
  - **區劃問題**：重疊區劃雙方 ManualReview 且不跑規則；部分未封閉的區劃 ManualReview、其他區劃照常檢討；沒有區劃／無封閉面積整批拒絕。
  - **輸入契約**：模型欄位不得輸入、scope 錯誤、重複欄位、型別錯誤皆拒絕；不在工作包的區劃輸入列為警告。
  - **可重現／可保存**：依 Zone ID 排序、相同輸入結果相同；結果放入 `ReviewRun` 經 `ReviewRunStorageMapper` 來回不變；有效 fixture 端到端。

## 設計決策

- **Revit Area 為主要實際值，幾何量算只做交叉驗證**（spec 11.4 第 2 點）。兩者不一致時不猜哪個對：Pass／Fail 需要可信的實際值，因此改為人工覆核；
  其他狀態本來就不是判定，不需改寫。容差沿用 P2 的 1%，使「能進入 Ready」與「面積檢討可判定」採同一標準。
- **區劃層級問題直接 ManualReview**，比照 P3-T03 歧義，以規則集 id／版本歸屬；未封閉的部分面積也列為主體，方便定位。
- **輸入來源仍未定**（spec 19 第 6 項），所以以 `ReviewInput` 純資料接收並把來源寫進證據；之後 UI／參數讀取只需產生 `CompartmentAreaInputs`。
- 輸入型別錯誤是呼叫端的程式錯誤（拋 `ArgumentException`），不是資料不足；參數值無法解析才用 `ReviewInput.Unreadable`。
- `ReviewResult` 不在此持久化或標示；Filled Region（spec 11.4 第 4 點）與檢討表（第 5 點）屬 P3-T08，持久化屬 P3-T07。

## 未解決問題

- 區劃用途、灑水、構造、樓層等輸入的實際來源（專案設定、Area 參數或 UI）未定；尚無 Revit adapter 讀取這些值。
- 規則集來源與正式條文（spec 19 第 2 項）未定；測試規則只是示意。
- 交叉驗證容差為暫定值，未接專案設定 UI（spec 19 第 7 項相關）。
- Check 尚未接到指令／UI，P3-T09 整合時需實跑。

## 下一階段目標（P3-T05）

- 構件防火時效檢討：Required／Provided 分離、Type 彙總、缺值與格式錯誤；以 `CandidateFacts.ForMember` 起始、讀 `element.providedFireRating`，
  不覆寫 Provided 值；歧義成員以 `CandidateAmbiguity.ToReviewResult` 回 ManualReview。
- Exit：各 Category 與邊界值測試通過，不覆寫 Provided 值。
