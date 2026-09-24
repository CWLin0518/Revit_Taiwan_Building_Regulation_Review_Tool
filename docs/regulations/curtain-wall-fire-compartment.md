# 防火區劃與帷幕牆交接：第 79 條、第 79-3 條、第 79-4 條

> 狀態：**規劃中（尚未實作）**。本文件為實作前的規格草案，經確認後才寫入 Revit 端程式與規則集。

## 1. 功能摘要

| 項目 | 內容 |
| --- | --- |
| 功能 ID | `curtain-wall-fire-compartment` |
| 法規 | 建築技術規則建築設計施工編 第 79 條第 3、4 項、第 79-3 條、第 79-4 條；區劃來源含第 83 條 |
| 核心目的 | 當防火區劃（區劃牆壁／區劃樓地板）的邊界落在帷幕外牆上時，檢查交接處是否維持區劃連續性 |
| 模型輸入 | 區劃邊界牆、區劃樓地板、帷幕牆（Curtain Wall／Curtain System）、帷幕嵌板、Area 區劃 |
| 主要產出 | 交接處六態檢討結果、檢討 View 紅色標示與註記、檢討表列 |
| 前置 | Phase 2 區劃範圍已建立且未過期；`防火檢討_設計防火時效` 已綁定至 Curtain Panels；交接帶已依 §4.5 整理 grid line |

適用邊界：本功能只處理**防火構造建築物**。非防火構造建築物的區劃牆突出規定（第 80 條第 2 項、第 84 條）數值雖相同，但適用前提不同，列為後續版本。

## 2. 法源條文

### 2.1 第 79 條第 3、4 項（垂直區劃牆 × 帷幕牆）

> 防火區劃之牆壁，應突出建築物外牆面五十公分以上。但與其交接處之外牆面長度有九十公分以上，且該外牆構造具有與防火區劃之牆壁同等以上防火時效者，得免突出。
>
> **建築物外牆為帷幕牆者，其外牆面與防火區劃牆壁交接處之構造，仍應依前項之規定。**

### 2.2 第 79-3 條（區劃樓地板 × 帷幕牆）

> 防火構造建築物之樓地板應為連續完整面，並應突出建築物外牆五十公分以上。但與樓板交接處之外牆面高度有九十公分以上，且該外牆構造具有與樓地板同等以上防火時效者，得免突出。
>
> **外牆為帷幕牆者，其牆面與樓地板交接處之構造，應依前項之規定。**
>
> 建築物有連跨複數樓層，無法逐層區劃分隔之垂直空間者，應依前條規定。

### 2.3 第 79-4 條（其餘外牆）

> 防火構造建築物之外牆，除本編第七十九條及第七十九條之三及第一百十條規定外，其他部分外牆應具有半小時以上防火時效。

### 2.4 用語（第 1 條第 26 款）

> 帷幕牆：構架構造建築物之外牆，除承載本身重量及其所受之地震、風力外，不再承載或傳導其他載重之牆壁。

法規語意結論：帷幕牆是非承重外牆，因此不受第 70 條主要構造時效拘束，但**第 79 條第 4 項與第 79-3 條第 2 項明文把帷幕牆拉回同一套交接規定**，不得因非承重而免除。

### 2.5 第 83 條（11 層以上之區劃來源）

> 建築物自第十一層以上部分，除依第七十九條之二規定之垂直區劃外，應依左列規定區劃：一、樓地板面積超過一○○平方公尺，應按每一○○平方公尺範圍內，以具有一小時以上防火時效之牆壁、防火門窗等防火設備與各該樓層防火構造之樓地板形成區劃分隔。（二、三款依裝修等級放寬至 200、500 平方公尺）

第 83 條本身沒有突出或 90 cm 的規定。本工具採**廣義解釋**：第 79 條第 3 項所稱「防火區劃之牆壁」涵蓋第 83 條所生之區劃牆壁，因此第 83 條區劃牆與帷幕牆的交接處一併適用 CW-H。

此為解釋選擇，非條文明文，實作時必須在結果證據中標明區劃來源條文（`junction.hostLegalReference`），讓審查者能分辨該交接點是來自第 79 條或第 83 條。若個案審查機關採狹義見解，可在規則集中以 `appliesWhen` 排除第 83 條來源，不需改程式。

## 3. 檢查項目與判定式

三個檢查項，共用同一組幾何解析結果。

### 3.1 CW-H：區劃牆與帷幕牆之水平交接（第 79 條、第 83 條）

```text
Pass ⟺  projectionDepth >= 500 mm
     ∨ ( continuousFireRatedLength >= 900 mm
         ∧ junctionMinFireRating >= hostRequiredFireRating
         ∧ hasUnprotectedOpening == false )
```

- `hostRequiredFireRating`：該區劃牆由規則引擎算出的要求時效（第 79 條第 1 項、第 83 條第 1 款均為 60 min）。
- `continuousFireRatedLength`：以交點為中心，沿帷幕牆面往兩側連續具時效之構造長度**總和**（左 + 右）。
  **採總和判定，不要求兩側各 ≥ 450 mm**；交點一側為 900 mm、另一側為 0 mm 亦視為符合。依據為條文只寫「交接處之外牆面長度有九十公分以上」，未區分兩側。
- 90 cm 帶內若存在未受防護開口（`防火檢討_設計防火保護` 非「是」的門窗或可開啟嵌板），連續累積在該處中斷。
- 交接帶涵蓋的每一片嵌板，其型別都必須有 `防火檢討_設計防火時效` 值；任一片缺值即為 `InsufficientData`，不得以其他片的值推定。

### 3.2 CW-V：區劃樓地板與帷幕牆之層間交接（第 79-3 條）

```text
Pass ⟺  projectionDepth >= 500 mm
     ∨ ( continuousFireRatedHeight >= 900 mm
         ∧ junctionMinFireRating >= hostRequiredFireRating )
```

- `continuousFireRatedHeight` = 下層嵌板實板高（樓板底以下連續段）＋ 樓板厚度投影段 ＋ 上層嵌板實板高（樓板頂以上連續段）。同樣採**總和**，不要求上下各半。
- `hostRequiredFireRating`：該樓地板要求時效，沿用現有 `tw-bcr-70-floor-rating` 與 `tw-bcr-79-floor-rating` 的優先序結果（取高者）。因此**該值會隨樓層變動**：自頂層起算第 5 層以上為 120 min，其餘為 60 min。

#### 樓板不突出時的參數要求

`projectionDepth < 500 mm` 而必須走 900 mm 但書時，第 79-3 條要求該段外牆「具有與樓地板**同等以上**防火時效」，因此：

1. 層間帶內每一片嵌板的**型別**都必須有 `防火檢討_設計防火時效` 值。任一片缺值 → `InsufficientData`，不得推定，也不得因其他片有值就放行。
2. 比較對象是**該樓層樓地板的要求時效**，不是固定 60 min。例如地上 20 層建築的 8 樓（自頂層起算第 13 層）樓地板要求 120 min，該層層間嵌板就必須 ≥ 120 min；只有 60 min 即為 `Fail`。
3. `junctionMinFireRating` 取層間帶內所有嵌板的**最小值**，一片不足即不足。

樓板突出 ≥ 500 mm 時，第一個條件已成立，不再要求嵌板時效，缺參數也不影響本項判定（該嵌板仍會落入 CW-O 的 30 min 檢查）。

### 3.3 CW-O：其餘帷幕牆面（第 79-4 條）

```text
Pass ⟺ panelMinFireRating >= 30 min
```

對象為不落在 CW-H 之 90 cm 帶、也不落在 CW-V 之 90 cm 帶內的帷幕嵌板。

### 3.4 六態對照

| 情形 | 狀態 |
| --- | --- |
| 判定式成立 | `Pass` |
| 判定式不成立，且所有輸入齊備 | `Fail` |
| `防火檢討_設計防火時效` 未綁定至 Curtain Panels，或交接帶內任一嵌板型別缺值，且該項需依 900 mm 但書判定 | `InsufficientData` |
| 帷幕牆為曲面、傾斜面、雙曲面，或嵌板非平面 | `ManualReview` |
| 交點解析出兩組以上候選，或區劃牆端點與帷幕牆距離超過搜尋公差 | `ManualReview` |
| 交接帶被 grid line 分割，且兩側嵌板時效皆足夠（疑為多餘 grid line，見 §4.5） | `ManualReview` |
| `building.fireResistiveConstruction == false` | `NotApplicable` |
| 該處為連跨複數樓層之挑空帷幕牆 | `NotApplicable`（改由第 79-2 條垂直區劃處理，證據須記錄轉出原因） |

判定式不成立時，不得因「現場可能另有補強」而放寬為 `ManualReview`；90 cm／50 cm 是幾何可量測的門檻，量測結果不足就是 `Fail`。

## 4. 幾何解析

### 4.1 帷幕牆識別

| 來源 | 判定 |
| --- | --- |
| `Wall` 且 `Wall.CurtainGrid != null` | 帷幕牆 |
| `CurtainSystem` | 帷幕牆 |
| Wall Type `Function == Exterior` 且非帷幕 | 一般外牆，走既有 `FireResistanceCheck`，不進本功能 |

嵌板集合取自 `CurtainGrid.GetPanelIds()`；嵌板可能是 `Panel`（FamilyInstance）或「嵌板為牆」的 `Wall`，兩者都要能讀型別參數。

**豎框（`Mullion`）不列入判定。** 法規未對豎框定義獨立的防火時效，本工具不讀取、不比較、也不因豎框而判 `InsufficientData`。豎框本身的防火填塞與嵌板背檔屬施工項目，同層間縫隙一併排除於本工具之外（見第 9 節）。

**工具不跨越 grid line 累積連續段。** `continuousFireRatedLength` 與 `continuousFireRatedHeight` 只在單一嵌板內、以及相鄰且共邊的具時效嵌板之間累積；遇到 grid line 即停止。這是刻意的：由工具自行「橋接」被切斷的嵌板，等於讓程式猜測設計意圖，而模型上那條線是否代表真實的構造斷點，工具無從判斷。

正確的作法在建模端——**層間帶與區劃牆交接帶應以連續嵌板表達，多餘的 grid line 應在建模時刪除**，詳見 §4.5。

### 4.2 CW-H 交點求解

1. 取 `element.isCompartmentBoundary == true` 且非帷幕的區劃牆，取其 `LocationCurve`。候選來源含第 79 條第 1 項與**第 83 條**所生之區劃牆；兩者共用同一套判定，僅證據中的 `hostLegalReference` 不同。
2. 往兩端各延伸 `JunctionSearchToleranceMm`（預設 300 mm），與帷幕牆定位線求交，得交點 `P` 與帷幕牆參數 `u`。
3. 自 `u` 往兩側逐一走訪嵌板，累積連續具時效段長度；交點落在嵌板內部時，該嵌板只計交點到其邊界的部分。
4. `projectionDepth = max(0, 區劃牆端點沿帷幕牆外法線方向超出帷幕牆外側面之距離)`。

### 4.3 CW-V 層間帶求解

1. 對每個樓層的區劃樓地板，取 `Floor` 邊界與帷幕牆定位面相交的水平投影線段。
2. 沿該線段取樣（預設每 600 mm 一點，至少 3 點），取最不利值。
3. 每個取樣點求上下連續具時效之帷幕牆面高度。
4. `projectionDepth = max(0, 樓板／防火簷板外緣超出帷幕牆外側面之距離)`。

### 4.4 公差與常數

集中於 `CurtainWallJunctionOptions`，不散落在計算程式：

| 常數 | 預設值 | 來源 |
| --- | --- | --- |
| `MinProjectionMm` | 500 | 第 79 條第 3 項、第 79-3 條第 1 項 |
| `MinFireRatedRunMm` | 900 | 同上但書 |
| `OtherWallRequiredMinutes` | 30 | 第 79-4 條 |
| `JunctionSearchToleranceMm` | 300 | 工具設定 |
| `SamplingIntervalMm` | 600 | 工具設定 |

長度一律以 mm 進入 Domain 層；Revit internal feet 的轉換只發生在 `BuildingRegulationReview.Revit` 邊界。

### 4.5 建模前置條件：交接帶不得被多餘 grid line 分割

本檢討把 grid line 當作構造斷點，因此模型必須先滿足這個條件：

- **層間帶**（樓板上下各 900 mm 範圍）內，同一垂直線上的實板應為**單一連續嵌板**；純粹為了分割立面而拉的水平 grid line 應刪除。
- **區劃牆交接帶**（交點左右合計 900 mm 範圍）內，同一水平帶上的實板應為單一連續嵌板；多餘的垂直 grid line 應刪除。
- 真正代表構造斷點的 grid line（例如視線玻璃與實板的交界）必須保留——那正是累積應該停止的地方。

Phase 3 前置檢查會掃出違反此條件的位置：當交接帶內偵測到 grid line，且其兩側嵌板的設計時效皆足夠時，判 `ManualReview`，訊息列出該 grid line 的 ElementId 與位置，提示「交接帶被 grid line 分割，請確認是否為真實構造斷點；若否，請刪除該 grid line 後重跑」。

刪除 grid line 後重跑，該點即依連續嵌板的實際尺寸正常判定。工具不提供「自動忽略豎框」的開關——一旦可以忽略，就無法分辨真斷點與假斷點。

## 5. 規則集擴充

### 5.1 新增 `RuleCategory`

```csharp
public enum RuleCategory
{
    CompartmentArea,
    FireResistance,
    OpeningProtection,
    CompartmentContinuity   // 新增
}
```

理由：檢查對象是「交接處」而非單一元素，`element.*` 與 `opening.*` 的欄位語意都不成立；硬塞進 `FireResistance` 會讓既有規則的 `appliesWhen` 全部需要額外排除條件。

### 5.2 新增白名單欄位（`junction.*`，僅 `CompartmentContinuity` 可用）

| 欄位 | 型別 | 說明 |
| --- | --- | --- |
| `junction.kind` | Text | `WallToCurtainWall` / `FloorToCurtainWall` / `CurtainPanelOther` |
| `junction.zoneId` | Text | 所屬區劃 |
| `junction.curtainWallUniqueId` | Text | 帷幕牆 UniqueId |
| `junction.hostUniqueId` | Text | 區劃牆或樓地板 UniqueId |
| `junction.hostRequiredFireRating` | Quantity(min) | 區劃牆／樓地板之要求時效（樓地板隨樓層變動） |
| `junction.hostLegalReference` | Text | 區劃來源條文，`第79條` 或 `第83條` |
| `junction.minFireRating` | Quantity(min) | 交接帶內構造之最小設計時效 |
| `junction.continuousFireRatedLength` | Quantity(m) | 水平連續具時效長度 |
| `junction.continuousFireRatedHeight` | Quantity(m) | 層間連續具時效高度 |
| `junction.projectionDepth` | Quantity(m) | 突出帷幕牆外牆面之深度 |
| `junction.hasUnprotectedOpening` | Boolean | 交接帶內是否有未受防護開口 |

`mm` 字面量已由 `RuleUnits` 支援（換算為 `ReviewUnit.Meter`），不需新增單位。

### 5.3 規則草案（加入 `Data/fire-review-rules.json`）

```json
{
  "ruleId": "tw-bcr-79-curtain-wall-junction",
  "version": "1",
  "category": "CompartmentContinuity",
  "legalReference": "建築技術規則建築設計施工編第79條第3項、第4項（區劃來源含第83條）",
  "effectiveDate": "2024-01-01",
  "jurisdiction": "TW",
  "priority": 10,
  "appliesWhen": "building.fireResistiveConstruction == true && junction.kind == \"WallToCurtainWall\"",
  "requiredValue": "junction.projectionDepth >= 500 mm || (junction.continuousFireRatedLength >= 900 mm && junction.minFireRating >= junction.hostRequiredFireRating && junction.hasUnprotectedOpening == false)",
  "exemptions": [],
  "evidenceFields": [ "junction.curtainWallUniqueId", "junction.hostUniqueId", "junction.hostLegalReference", "junction.projectionDepth", "junction.continuousFireRatedLength", "junction.minFireRating", "junction.hostRequiredFireRating", "junction.hasUnprotectedOpening" ],
  "severity": "Error"
}
```

```json
{
  "ruleId": "tw-bcr-79-3-curtain-wall-spandrel",
  "version": "1",
  "category": "CompartmentContinuity",
  "legalReference": "建築技術規則建築設計施工編第79條之3",
  "effectiveDate": "2024-01-01",
  "jurisdiction": "TW",
  "priority": 10,
  "appliesWhen": "building.fireResistiveConstruction == true && junction.kind == \"FloorToCurtainWall\"",
  "requiredValue": "junction.projectionDepth >= 500 mm || (junction.continuousFireRatedHeight >= 900 mm && junction.minFireRating >= junction.hostRequiredFireRating)",
  "exemptions": [],
  "evidenceFields": [ "junction.curtainWallUniqueId", "junction.hostUniqueId", "junction.projectionDepth", "junction.continuousFireRatedHeight", "junction.minFireRating", "junction.hostRequiredFireRating" ],
  "severity": "Error"
}
```

```json
{
  "ruleId": "tw-bcr-79-4-curtain-wall-other",
  "version": "1",
  "category": "CompartmentContinuity",
  "legalReference": "建築技術規則建築設計施工編第79條之4",
  "effectiveDate": "2024-01-01",
  "jurisdiction": "TW",
  "priority": 20,
  "appliesWhen": "building.fireResistiveConstruction == true && junction.kind == \"CurtainPanelOther\"",
  "requiredValue": "junction.minFireRating >= 30 min",
  "exemptions": [],
  "evidenceFields": [ "junction.curtainWallUniqueId", "junction.minFireRating" ],
  "severity": "Error"
}
```

## 6. 參數需求

| 參數 | 類型 | 綁定 | 用途 |
| --- | --- | --- | --- |
| `防火檢討_設計防火時效` | Type（沿用既有定義） | **新增綁定** Curtain Panels | 嵌板之設計時效，`junction.minFireRating` 來源。不綁 Curtain Wall Mullions |
| `防火檢討_設計防火保護` | Type、YESNO（既有） | 既有 Doors、Windows、Curtain Panels | 交接帶內開口是否受防護 |
| `防火檢討_法規要求防火時效` | Type、寫回（既有） | 不新增綁定 | 交接檢討不寫回型別，結果只存在 ReviewRun |

未綁定時 `InsufficientData`；已綁定但空值，對嵌板亦為 `InsufficientData`——時效是量值，不適用 spec 11.6 針對門窗 YESNO 的「已綁定未勾選視為否」界定。

此參數的存在正是 §3.2「樓板不突出」情境的先決條件：沒有它就無法證明層間嵌板達到該樓層樓地板的同等時效，該交接點只能停在 `InsufficientData`。設定流程（`FireReviewSetupFeature`）須把 Curtain Panels 列入必要綁定清單，並在前置檢查未綁定時明示「帷幕嵌板未綁定設計防火時效，層間交接無法判定」。

## 7. Revit 產出

### 7.1 檢討 View 標示

| 項目 | 表現 |
| --- | --- |
| CW-H 未符合 | 交接處帷幕嵌板 By Element Override 紅色；平面圖於交點標註實測 `continuousFireRatedLength` 與 `projectionDepth` |
| CW-V 未符合 | 立面／剖面 View 之層間帶 Filled Region 紅色；標註實測 `continuousFireRatedHeight` |
| CW-O 未符合 | 嵌板紅色 Override |

所有標示元素寫入 Package ID、Run ID、Zone ID，僅更新目前 Run 管理的元素，沿用 Phase 3 既有的 `ReviewMarkup` 機制。

### 7.2 檢討表新增列

| 項目 | 狀態 | 統計 |
| --- | --- | --- |
| 帷幕牆區劃交接（水平） | 六態 | 依交接處計數，並分列第 79 條／第 83 條來源 |
| 帷幕牆區劃交接（層間） | 六態 | 依樓層／帷幕牆計數 |
| 帷幕牆其他部分時效 | 六態 | 依嵌板 Type 彙總 |

### 7.3 圖說

層間剖詳圖（spandrel 剖面）不在本版自動產生範圍，僅輸出量測值供人工繪製。

## 8. 程式組成

| 類別 | 專案 | 責任 |
| --- | --- | --- |
| `CurtainWallJunctionCheck` | Application/Checks | 呼叫規則引擎、產生六態 `ReviewResult` |
| `CurtainWallJunctionInputs` | Application/Checks | 交接處事實的純資料模型 |
| `CurtainWallJunctionOptions` | Application/Checks | 500／900／30／公差常數 |
| `CurtainWallJunctionResolver` | Application/Candidates | 由候選集合組出交接處清單（無 Revit 相依） |
| `ICurtainWallGeometryReader` | Application/Abstractions | 幾何讀取介面 |
| `RevitCurtainWallGeometryReader` | Revit/Geometry | 實作：嵌板走訪、交點、突出量、層間高度、間隙 |
| `RuleCategory.CompartmentContinuity` | Domain/Rules | 新增列舉值 |
| `RuleFieldCatalog` | Domain/Rules | 新增 `junction.*` 欄位 |
| `FireReviewRunner` | Application/Reviews | 串接第四類檢查與檢討表彙總 |

`FireReviewRunner` 的總狀態規則不變：任一 `Fail` 為未符合；無 `Fail` 但有 `InsufficientData` 或 `ManualReview` 為待確認；其餘皆 `Pass`／`NotApplicable` 才顯示符合。

## 9. 已知限制

- 只支援平面帷幕牆；曲面、傾斜面回 `ManualReview`。
- 嵌板時效以型別參數為唯一來源，不解析複合構造層，也不判斷玻璃種類。
- 未涵蓋第 110 條防火間隔對外牆與開口的要求（獨立功能）。
- 未涵蓋第 80 條、第 84 條（非防火構造建築物）。
- 第 83 條區劃已納入（見 §2.5），但其適用前提是 `isCompartmentBoundary` 能正確反映第 83 條的 100／200／500 ㎡ 區劃。Phase 2 若未依裝修等級切出該區劃，本功能只會少算交接點，不會誤報。
- 豎框不判定，且工具不跨越 grid line 累積連續段（見 §4.1、§4.5）。交接帶內的實板必須在模型中就是連續嵌板，這是**建模前置條件**而非工具限制；未整理的模型會停在 `ManualReview`，不會誤判為符合。
- Link 模型中的帷幕牆依既有 `LinkGeometryPolicy` 處理，預設不檢討。
- 不檢討樓板邊緣與帷幕牆背面之層間縫隙塞火：該縫的填塞屬施工項目，模型幾何無法證明，本工具不納入判定，須由設計與監造以其他方式確認。

## 10. 測試案例

| # | 情境 | 期望 |
| --- | --- | --- |
| 1 | 區劃牆突出帷幕牆 600 mm | CW-H `Pass` |
| 2 | 區劃牆突出 499 mm、交接帶無時效 | CW-H `Fail` |
| 3 | 無突出、交接帶 900 mm 且嵌板 60 min | CW-H `Pass` |
| 4 | 無突出、交接帶 899 mm 且嵌板 60 min | CW-H `Fail`（邊界值） |
| 5 | 交接帶 1200 mm 但其中一片 30 min | CW-H `Fail`（取最小值） |
| 6 | 交接帶 900 mm 但含未受防護窗 | CW-H `Fail` |
| 7 | 無突出、交點左側 900 mm 具時效、右側 0 mm | CW-H `Pass`（採總和，不要求兩側各半） |
| 8 | 交接帶 900 mm 但中間有一條多餘 grid line，兩側嵌板皆 60 min | CW-H `ManualReview`，訊息含該 grid line 的 ElementId |
| 8b | 承上，刪除該 grid line 使嵌板連續後重跑 | CW-H `Pass` |
| 8c | 交接帶內 grid line 一側為 60 min 實板、另一側為無時效玻璃 | CW-H 依實際連續長度判定（該 grid line 是真實斷點，不列 `ManualReview`） |
| 9 | 第 83 條區劃牆與帷幕牆交接、無突出、交接帶 900 mm 60 min | CW-H `Pass`，證據 `hostLegalReference == "第83條"` |
| 10 | 層間實板 900 mm、60 min，該樓層樓地板要求 60 min | CW-V `Pass` |
| 11 | 層間實板 900 mm、60 min，但該樓層樓地板要求 120 min（自頂層起算第 5 層以上） | CW-V `Fail`（同等以上） |
| 12 | 層間實板 900 mm 但只有 30 min | CW-V `Fail` |
| 13 | 樓板外突 500 mm、層間全玻璃且嵌板無時效值 | CW-V `Pass`（突出條件已成立，不要求嵌板時效） |
| 14 | 樓板不突出、層間帶其中一片嵌板型別缺時效值 | CW-V `InsufficientData`（不得以其他片推定） |
| 15 | 嵌板類別未綁定 `防火檢討_設計防火時效` | 依 900 mm 但書判定之項目全為 `InsufficientData` |
| 16 | `fireResistiveConstruction == false` | 全項 `NotApplicable` |
| 17 | 三層連跨挑空帷幕牆 | CW-V `NotApplicable`，證據載明轉第 79-2 條 |
| 18 | 曲面帷幕牆 | `ManualReview` |
| 19 | 重跑同一 Package | 標示元素被覆蓋而非重複產生 |
| 20 | 修改嵌板型別時效後重跑 | 舊 Run 轉 Stale，新結果反映新值 |

## 11. 決議紀錄

| # | 議題 | 決議 | 落在文件何處 |
| --- | --- | --- | --- |
| 1 | 900 mm 是總和或兩側／上下各半 | **採總和 ≥ 900 mm**，不要求各半 | §3.1、§3.2 |
| 2 | 樓板不突出時的證明方式 | 嵌板**型別**必須有 `防火檢討_設計防火時效`，且須 **≥ 該樓層樓地板之要求時效**（非固定 60 min）；任一片缺值為 `InsufficientData` | §3.2「樓板不突出時的參數要求」、§6 |
| 3 | 豎框無時效定義時如何處理 | **不判定**，不綁參數、不讀值、不計入取小。工具**不跨越 grid line 累積**；交接帶的連續性由建模保證——刪除多餘 grid line 使嵌板連續。偵測到疑似多餘 grid line 時判 `ManualReview` 並指出位置 | §4.1、§4.5、§9 |
| 4 | 第 83 條區劃是否納入 | **納入**，與第 79 條共用 CW-H 判定，證據以 `hostLegalReference` 區分來源 | §2.5、§4.2、§7.2 |

第 4 項屬解釋選擇而非條文明文（第 83 條本身未規定突出或 90 cm），若個案審查機關採狹義見解，於規則集 `appliesWhen` 排除即可，不需改程式。

## 12. 實作啟動條件

本文件經確認後才寫入程式。實作順序建議：

1. `RuleCategory.CompartmentContinuity` 與 `RuleFieldCatalog` 的 `junction.*` 欄位（含 `hostLegalReference`）。
2. `fire-review-rules.json` 三條規則。
3. `CurtainWallJunctionInputs` / `Options` / `Check`（純 Domain 與 Application，可先以 fixture 跑完 §10 的 20 個案例）。
4. `ICurtainWallGeometryReader` 介面與 `RevitCurtainWallGeometryReader` 實作。
5. `FireReviewSetupFeature` 加入 Curtain Panels 綁定。
6. `FireReviewRunner`、`ReviewMarkup`、檢討表串接。

第 3 步完成即為一個可獨立驗證的邊界，不需等到 Revit 端完成。
