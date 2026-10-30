# P3-T05 — 構件防火時效檢討

## 完成範圍

- Application `Checks`（不相依 Revit／UI，core tests 覆蓋）：
  - `ProvidedFireRating.cs`：
    - `FireRatingParameters`：`Provided = 防火檢討_設計防火時效`、`Required = 防火檢討_法規要求防火時效`（spec 11.5 第 4 點），`IsRequiredParameter`。
    - `ProvidedFireRating`：Type 的設計／認證防火時效，四種狀態 `Rated`（分鐘）／`Missing`／`Unreadable`（格式錯誤）／`Undeterminable`（複合構造），
      保留原始文字與原因；`FromNumber(value, FireRatingUnit)` 給數值參數用。上限 24 小時。
    - `FireRatingText.Parse(raw, bareNumberUnit)`：純數字（單位依設定，預設分鐘）、`min／分／分鐘`、`h／hr／hour／小時／時`、
      `一／二／兩／三／四小時`、`…小時半`、`半小時`，全形轉半形、不分大小寫。一個值含 ≥2 個時效（`1hr/2hr`、`60~120`、`一小時、兩小時`）→ Undeterminable；
      其他（`耐燃`、`-30`、`60 m`、`六十分`）→ Unreadable 並附原因。`Format(minutes)` → `90 min（1.5 小時）`。
  - `FireResistanceInputs.cs`：`TypeFireRating`（Type UniqueId、讀到的時效、來源參數名稱；**來源不得是 `防火檢討_法規要求防火時效`**）、
    `FireResistanceInputs`（building／zone 輸入沿用 `CompartmentAreaInputs`，因此 `element.*` 無法以輸入提供；同一 Type 重複即拒絕）。
  - `FireResistanceCheck.Review(set, inputs, engine, context, runId, newResultId)` → `Result<FireResistanceReview>`：
    `MemberRatingFinding`（每個構件 × 區劃一筆，另加每個歧義構件關係一筆）、`TypeRatingSummary`（按 Type 彙總）、`Warnings`。
  - `ReviewPreconditions.Zones(set, subject)`（internal）：spec 11.1 前置檢查從 `CompartmentAreaCheck` 抽出共用，面積檢查訊息不變。
  - `ReviewCheckTypes.FireResistance = "FireResistance"`。
- `ReviewErrorCode.FireRatingUndetermined = "BCR-RATE-001"`（複合構造無法判定防火時效）。

## 判定流程（每個構件 × 區劃）

| 條件 | 結果 | 錯誤碼 |
| --- | --- | --- |
| 沒有區劃，或有區劃沒有任何封閉面積 | 整批 `Result.Failure`（spec 11.1） | `BCR-CAND-002` |
| 關係為 Ambiguous（邊界偏離中心線、貼柱面、link、無平面幾何…） | `CandidateAmbiguity.ToReviewResult` → ManualReview，歸屬規則集 | `BCR-CAND-001` |
| 區劃有 `Problems`（未封閉、重疊） | ManualReview，不跑規則，證據含 `zone.problems` | `BCR-CAND-002` |
| 其他 | facts＝`CandidateFacts.ForMember`＋building／zone 輸入＋`element.providedFireRating` → `RuleEngine.Evaluate(FireResistance)` | 依 `RuleOutcomeErrorCode` |
| 　Provided ≥ Required | Pass（等於要求值依引擎容差判符合） | — |
| 　Provided < Required | Fail，實際值＝設計值、要求值＝規則值 | — |
| 　Type 沒提供時效／構件沒有 Type | 欄位缺漏 → InsufficientData，仍回報要求值 | `BCR-PARAM-001` |
| 　格式錯誤 | 欄位 unreadable → InsufficientData，訊息含原始值與原因 | `BCR-PARAM-003` |
| 　複合構造，且唯一缺的就是設計時效 | ManualReview，保留要求值與規則歸屬，訊息寫出要求時效 | `BCR-RATE-001` |
| 　複合構造，但還缺其他輸入 | InsufficientData | `BCR-RATE-001` |
| 　規則不適用（例如區劃內隔間牆） | NotApplicable（設計時效不影響） | — |

缺資料、格式錯誤、複合構造都**不會**變成 Fail。

## Required／Provided 分離

- 設計值只從 `TypeFireRating` 讀入，永遠是結果的 `actualValue`；規則算出的只放 `requiredValue`。檢查不寫任何參數，也不修改輸入物件（測試以 `Assert.Same` 驗證）。
- `TypeFireRating` 拒絕以 `防火檢討_法規要求防火時效` 當設計值來源，避免要求值被當成設計值回讀。
- 之後若要寫回要求值（P3-T07／T08 決定），只能寫 `防火檢討_法規要求防火時效`；`TypeRatingSummary.HighestRequiredMinutes` 是 Type 層級的要求值。

## 證據

引擎證據（`element.*`、`zone.*`、規則 `evidenceFields`）之後依序加上：候選關係（`candidate.zoneId／relation／…` 與量測長度）、
來源（`source.elementUniqueId／documentUniqueId／category／typeName／width`）、`source.typeUniqueId`、`provided.kind`、`provided.parameter`、
`provided.raw`、`provided.reason`、每個有來源的輸入 `source[<欄位>]`、缺漏欄位 `rule.gaps`。

## Type 彙總（spec 11.5 第 7 點、11.7「依類別與 Type 統計」）

- 依（類別、Type UniqueId）分組；沒有平面幾何的構件不在 `CandidateSet.Members`，以類別＋Type 名稱在候選集合中唯一對應回 Type UniqueId，無法對應才以名稱分組。
- 每個 Instance 只算一次（共用邊界牆在兩區各一筆結果，`InstanceCount` 仍為 1）；`Count(status)`、`HighestRequiredMinutes`、`Provided`。
- Type 狀態取最嚴重者：Fail ＞ ManualReview ＞ InsufficientData ＞ Pass ＞ NotApplicable；只要有一個 Instance 待確認，Type 就不會顯示符合。
- 排序：類別 → Type 名稱 → Type UniqueId。

## 驗證

- Solution build：0 warnings／0 errors；外掛 csproj `-t:Rebuild`：0 warnings／0 errors。
- Core tests：**809/809 通過**（P3-T05 前 715，新增 94）：
  - `FireRatingTextTests`：分鐘／小時／中文／全形／半小時、裸數字單位設定、空白＝缺漏、無法判讀（含原因）、複合構造、數值參數、格式化、要求值參數不得作為來源。
  - `FireResistanceCheckTests`：
    - **各 Category 邊界值**：牆 59／60／61、柱 119／120、梁 119.5／120、樓板 89／90／60（小時與中文寫法），檢查 actual／required、主體、區劃、訊息。
    - 要求值依 building 輸入（10 層 2h、11 層 3h）；隔間牆 NotApplicable；共用邊界牆兩區各一筆。
    - **不覆寫 Provided**：Fail 時 actual＝30 min、required＝60 min，輸入物件不變、證據無 Required 欄位；`防火檢討_法規要求防火時效` 當來源被拒絕。
    - **缺值與格式錯誤**：四類別缺值 InsufficientData 並保留要求值、構件無 Type、`耐燃`／`-30`／`25 hr`；複合構造 ManualReview `BCR-RATE-001`；
      複合構造遇不適用或其他缺漏時不改 ManualReview；三種缺漏組合一律不會 Pass／Fail。
    - **歧義與區劃問題**：貼柱面 ManualReview（規則集歸屬）、link 與無幾何構件、重疊區劃 `BCR-CAND-002`、整批拒絕、無規則 `BCR-RULE-001`。
    - **Type 彙總**：Instance 去重、狀態取最嚴重、無時效的 Type、link Instance 使 Type 不為 Pass。
    - 輸入契約（重複 Type、未使用的 Type 與區劃警告）；排序與反轉輸入可重現、`ReviewRun` 儲存來回；有效 fixture 端到端。

## 設計決策

- **結果粒度為「構件 × 區劃」**：同一構件對不同區劃的關係（邊界／內部）可能不同，要求值也可能不同；彙總另由 `TypeRatingSummary` 提供。
- **Inside／Crossing 也送進規則**：是否需要時效由規則決定（spec 11.5 第 2 點），不在檢查裡寫死「只有邊界構件才檢討」；類別與關係的納入策略仍由 P3-T03 的 `CandidateResolutionOptions` 設定。
- **複合構造只在「唯一缺的是設計時效」時轉 ManualReview**：此時要求值已知、構件確定適用，人可以判斷；若還缺其他輸入，先補資料才有意義。
- **提供值的單位與型態（spec 19 第 5 項）未定**，所以同時支援文字與數值參數，裸數字單位可設定；解析不猜測，無法辨識就是資料不足。
- 錯誤碼：缺值／格式錯誤沿用 spec 14 的「參數缺失／資料型態錯誤」，複合構造新增 `BCR-RATE-001`。

## 未解決問題

- 尚無 Revit adapter 讀取 Type 參數（參數名稱選擇、Integer／Text 儲存型態）；P3-T09 整合時需決定並實跑。
- spec 19 第 4、5 項（既有防火時效 Shared Parameter GUID、提供值單位與型態）未定。
- 樓板對上下層區劃的關係仍只在平面上判定（P3-T03 決策）；規則須以 `element.category` 判定樓板。
- 規則集正式條文（spec 19 第 2 項）未定，測試規則只是示意。
- 本 Task 不做結果持久化（P3-T07）、By Element Override 與圖例（P3-T08／Phase 4）。

## 下一階段目標（P3-T06）

- 門窗防火保護檢討：以 `CandidateFacts.ForOpening` 起始，先判斷是否依法需要防火保護，再讀 `opening.providedFireProtection`（是／否／未設定）；
  幕牆與非 Hosted 依 MVP 政策（歧義 → ManualReview）；不適用仍保存適用性證據。
- Exit：是／否／未設定／不適用結果與證據正確。
