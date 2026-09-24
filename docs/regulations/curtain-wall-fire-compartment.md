# 防火區劃與帷幕牆交接：第 79 條、第 79-3 條、第 79-4 條

> 狀態：**實作中**。§12 的步驟 1–7 全部已完成（規則類別、`junction.*` 欄位、三條交接規則、第 83 條面積規則與 `zone.interiorFinish`，`CurtainWallJunctionInputs`／`Options`／`Check`，`ICurtainWallGeometryReader`／`CurtainWallJunctionResolver`／`RevitCurtainWallGeometryReader`，Curtain Panels 的 `防火檢討_設計防火時效` 綁定與前置檢查，`FireReviewRunner` 的第四類檢查與檢討表第四列，§7.1 三種標示的標示計畫與差異比對含案例 19，以及 `RevitReviewViewMarker` 把交接處標註與層間帶畫進檢討平面與工具自建的帷幕牆檢討立面，共 143 個規則層、Check 層、幾何層、參數層、串接層與標示層測試）；**Revit 端尚未實機驗證**，且第 83 條的 `防火檢討_室內裝修等級` 尚未進入批次參數面板（見 §12 步驟 7）。

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

> 建築物自第十一層以上部分，除依第七十九條之二規定之垂直區劃外，應依左列規定區劃：
>
> 一、樓地板面積超過一○○平方公尺，應按每一○○平方公尺範圍內，以具有一小時以上防火時效之牆壁、防火門窗等防火設備與各該樓層防火構造之樓地板形成區劃分隔。但建築物使用類組Ｈ–２組使用者，區劃面積得增為二○○平方公尺。
>
> 二、自地板面起一‧二公尺以上之室內牆面及天花板均使用耐燃一級材料裝修者，得按每二○○平方公尺範圍內，以具有一小時以上防火時效之牆壁、防火門窗等防火設備與各該樓層防火構造之樓地板區劃分隔；供建築物使用類組Ｈ–２組使用者，區劃面積得增為四○○平方公尺。
>
> 三、室內牆面及天花板（包括底材）均以耐燃一級材料裝修者，得按每五○○平方公尺範圍內，以具有一小時以上防火時效之牆壁、防火門窗等防火設備與各該樓層防火構造之樓地板區劃分隔。
>
> 四、前三款區劃範圍內，如備有效自動滅火設備者得免計算其有效範圍樓地面板面積之二分之一。
>
> 五、第一款至第三款之防火門窗等防火設備應具有一小時以上之阻熱性。

第 83 條本身沒有突出或 90 cm 的規定。本工具採**廣義解釋**：第 79 條第 3 項所稱「防火區劃之牆壁」涵蓋第 83 條所生之區劃牆壁，因此第 83 條區劃牆與帷幕牆的交接處一併適用 CW-H。

一個區劃**是不是**第 83 條的，不從模型讀、也不從樓層直接推定，而是看區劃面積規則在哪一條下判定它：`FireReviewRunner.HostLegalReferences` 以區劃面積結果的法源條文文字判斷，區劃邊界牆兩側區劃都是第 83 條的才記為第 83 條，跨越第 79 條與第 83 條區劃的牆記為第 79 條（同一交接點只能有一個來源，且每次重跑都必須讀到同一個答案）。因此規則集裡沒有第 83 條的面積規則時，§7.2 檢討表的第 83 條那一列**永遠是空的**——這正是 §5.3 補上 `tw-bcr-83-area` 要解決的事。

反過來說，第 83 條的區劃**不是工具替使用者切出來的**：Phase 2 的 `element.isCompartmentBoundary` 只表示「這道牆躺在 Area 邊界上」，是純幾何判定，不認得第 83 條。模型在十一層以上沒有切出 100／200／500 ㎡ 的區劃時，工具看到的是一個超限的大區劃，它會在區劃面積這一項判 `Fail`，而不是憑空生出交接點——交接點少算、面積未符合，兩者都指向同一個設計問題。

此為解釋選擇，非條文明文，實作時必須在結果證據中標明區劃來源條文（`junction.hostLegalReference`），讓審查者能分辨該交接點是來自第 79 條或第 83 條。若個案審查機關採狹義見解，可在規則集中以 `appliesWhen` 排除第 83 條來源，不需改程式。

## 3. 檢查項目與判定式

三個檢查項，共用同一組幾何解析結果。

判定式依條文自身的結構寫成「本文 + 但書」，而不是一條大的布林式——這同時是規則 DSL 的要求（見 §5.4）：

```text
本文（requiredValue）：projectionDepth >= 500 mm
但書（exemption）    ：continuousFireRatedLength >= 900 mm   ← CW-H
                       continuousFireRatedHeight >= 900 mm   ← CW-V
```

因此**但書成立時的狀態是 `NotApplicable` 而非 `Pass`**：條文寫的是「得**免**突出」，也就是本文的突出要求不再適用。依 spec 11.7，`NotApplicable` 與 `Pass` 同樣不阻礙總狀態顯示符合。

### 3.1 CW-H：區劃牆與帷幕牆之水平交接（第 79 條、第 83 條）

| 情形 | 狀態 |
| --- | --- |
| `projectionDepth >= 500 mm` | `Pass` |
| 未突出，但 `continuousFireRatedLength >= 900 mm` | `NotApplicable`（得免突出） |
| 未突出，且連續長度不足 | `Fail` |
| 未突出，且連續長度無法量測 | `InsufficientData` |

- `hostRequiredFireRating`：該區劃牆由規則引擎算出的要求時效（第 79 條第 1 項、第 83 條第 1 款均為 60 min）。
- `continuousFireRatedLength`：以交點為中心，沿帷幕牆面往兩側連續具時效之構造長度**總和**（左 + 右）。
  **採總和判定，不要求兩側各 ≥ 450 mm**；交點一側為 900 mm、另一側為 0 mm 亦視為符合。依據為條文只寫「交接處之外牆面長度有九十公分以上」，未區分兩側。
- 「具時效」的門檻內建在這個量測裡（見 §5.2）：只有 `providedFireRating >= hostRequiredFireRating` 的嵌板才計入長度。條文的「且該外牆構造具有與防火區劃之牆壁同等以上防火時效」因此不需要另一個判定式。
- 90 cm 帶內若存在未受防護開口（`防火檢討_設計防火保護` 非「是」的門窗或可開啟嵌板），連續累積在該處中斷。
- 交接帶涵蓋的任一嵌板型別缺 `防火檢討_設計防火時效` 值時，幾何層**不供給** `continuousFireRatedLength`，交由引擎判 `InsufficientData`；不得以其他片的值推定，也不得當作 0 而判 `Fail`。

### 3.2 CW-V：區劃樓地板與帷幕牆之層間交接（第 79-3 條）

| 情形 | 狀態 |
| --- | --- |
| `projectionDepth >= 500 mm` | `Pass` |
| 未突出，但 `continuousFireRatedHeight >= 900 mm` | `NotApplicable`（得免突出） |
| 未突出，且連續高度不足 | `Fail` |
| 未突出，且連續高度無法量測 | `InsufficientData` |

- `continuousFireRatedHeight` = 下層嵌板實板高（樓板底以下連續段）＋ 樓板厚度投影段 ＋ 上層嵌板實板高（樓板頂以上連續段）。同樣採**總和**，不要求上下各半。
- `hostRequiredFireRating`：該樓地板要求時效，沿用現有 `tw-bcr-70-floor-rating` 與 `tw-bcr-79-floor-rating` 的優先序結果（取高者）。因此**該值會隨樓層變動**：自頂層起算第 5 層以上為 120 min，其餘為 60 min。

#### 樓板不突出時的參數要求

`projectionDepth < 500 mm` 而必須走 900 mm 但書時，第 79-3 條要求該段外牆「具有與樓地板**同等以上**防火時效」，因此：

1. 層間帶內每一片嵌板的**型別**都必須有 `防火檢討_設計防火時效` 值。任一片缺值 → `InsufficientData`，不得推定，也不得因其他片有值就放行。
2. 比較對象是**該樓層樓地板的要求時效**，不是固定 60 min。例如地上 20 層建築的 8 樓（自頂層起算第 13 層）樓地板要求 120 min，該層層間嵌板就必須 ≥ 120 min；只有 60 min 的嵌板不計入 `continuousFireRatedHeight`，高度湊不到 900 mm 即為 `Fail`。
3. 高度只累計達標的嵌板，因此一片不足等同於該段不連續。

樓板突出 ≥ 500 mm 時，本文的要求已成立，引擎在讀但書之前就判 `Pass`，缺參數不影響本項判定（該嵌板仍會落入 CW-O 的 30 min 檢查）。這是把「突出」寫成 requiredValue、「90 cm」寫成 exemption 的直接好處：豁免條件算不出來只有在本文未過時才會轉成 `InsufficientData`。

### 3.3 CW-O：其餘帷幕牆面（第 79-4 條）

```text
Pass ⟺ panelMinFireRating >= 30 min
```

對象為不落在 CW-H 之 90 cm 帶、也不落在 CW-V 之 90 cm 帶內的帷幕嵌板。

### 3.4 六態對照

| 情形 | 狀態 |
| --- | --- |
| 本文成立（突出 ≥ 500 mm，或 CW-O 之時效達標） | `Pass` |
| 本文不成立但但書成立（連續長度／高度 ≥ 900 mm） | `NotApplicable`，理由 `Exempt`（得免突出） |
| 本文與但書皆不成立，且所有輸入齊備 | `Fail` |
| `防火檢討_設計防火時效` 未綁定至 Curtain Panels，或交接帶內任一嵌板型別缺值，且該項需依 900 mm 但書判定 | `InsufficientData` |
| 帷幕牆為曲面、傾斜面、雙曲面，或嵌板非平面 | `ManualReview` |
| 交點解析出兩組以上候選，或區劃牆端點與帷幕牆距離超過搜尋公差 | `ManualReview` |
| 交接帶被 grid line 分割，且兩側嵌板時效皆足夠（疑為多餘 grid line，見 §4.5） | `ManualReview` |
| `building.fireResistiveConstruction == false` | `NotApplicable` |
| 該處為連跨複數樓層之挑空帷幕牆 | `NotApplicable`（改由第 79-2 條垂直區劃處理，證據須記錄轉出原因） |

判定式不成立時，不得因「現場可能另有補強」而放寬為 `ManualReview`；90 cm／50 cm 是幾何可量測的門檻，量測結果不足就是 `Fail`。

嵌板的 `防火檢討_設計防火時效` 若填成複合構造（例如 `1hr/2hr`），且那是唯一缺口，則判 `ManualReview`
（錯誤碼 `BCR-RATE-001`）而非 `InsufficientData`：這與 `FireResistanceCheck` 對同一個參數的處置一致——
值不是缺，是要由人決定哪一層作數，使用者補不了這個「缺口」。

## 4. 幾何解析

### 4.1 帷幕牆識別

| 來源 | 判定 |
| --- | --- |
| `Wall` 且 `Wall.CurtainGrid != null` | 帷幕牆 |
| `CurtainSystem` | 帷幕牆 |
| Wall Type `Function == Exterior` 且非帷幕 | 一般外牆，走既有 `FireResistanceCheck`，不進本功能 |

嵌板集合取自 `CurtainGrid.GetPanelIds()`；嵌板可能是 `Panel`（FamilyInstance）或「嵌板為牆」的 `Wall`，兩者都要能讀型別參數。

**豎框（`Mullion`）不列入判定。** 法規未對豎框定義獨立的防火時效，本工具不讀取、不比較、也不因豎框而判 `InsufficientData`。豎框本身的防火填塞與嵌板背檔屬施工項目，同層間縫隙一併排除於本工具之外（見第 9 節）。

**工具不跨越 grid line 累積連續段。** `continuousFireRatedLength` 與 `continuousFireRatedHeight` 只在單一嵌板內累積：以交點（CW-H）或取樣點（CW-V）所在的那一片嵌板為準，取該點兩側長度／上下高度之和；走到嵌板邊界即停止，因為那裡就是一條 grid line。這是刻意的：由工具自行「橋接」被切斷的嵌板，等於讓程式猜測設計意圖，而模型上那條線是否代表真實的構造斷點，工具無從判斷。

越過邊界的嵌板只在一種情況下被讀到，而且只用來提問不用來判定：連續段不足 900 mm 時，工具會往兩側走訪相鄰且時效足夠的嵌板，看看「若這些 grid line 不存在，是否就達 900 mm」。答案為是才回 `SplitByGridLine`（§4.5），答案為否則該斷點為真，以實測的連續段判定。

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

第 83 條的面積規則另外需要一個 `zone.*` 欄位，它與 `zone.use`、`zone.sprinklered` 同一層級，對所有規則類別開放：

| 欄位 | 型別 | 說明 |
| --- | --- | --- |
| `zone.interiorFinish` | Text | 室內裝修等級，對應第 83 條第一至三款：`無`（第一款）／`耐燃一級`（第二款，自地板面起 1.2 m 以上之牆面與天花板）／`耐燃一級含底材`（第三款，含底材） |

參數來源是 `防火檢討_室內裝修等級`（面積實體參數，見 §6）。等級是設計者宣告的事實，不是模型量得的，所以是輸入欄位而非模型欄位。

### 5.3 已加入的規則（`Data/fire-review-rules.json`，規則集版本 `2026.4-provisional`）

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
  "requiredValue": "junction.projectionDepth >= 500 mm",
  "exemptions": [ "junction.continuousFireRatedLength >= 900 mm" ],
  "evidenceFields": [ "junction.curtainWallUniqueId", "junction.hostUniqueId", "junction.hostLegalReference", "junction.continuousFireRatedLength", "junction.minFireRating", "junction.hostRequiredFireRating", "junction.hasUnprotectedOpening" ],
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
  "requiredValue": "junction.projectionDepth >= 500 mm",
  "exemptions": [ "junction.continuousFireRatedHeight >= 900 mm" ],
  "evidenceFields": [ "junction.curtainWallUniqueId", "junction.hostUniqueId", "junction.continuousFireRatedHeight", "junction.minFireRating", "junction.hostRequiredFireRating" ],
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
  "evidenceFields": [ "junction.curtainWallUniqueId", "junction.zoneId" ],
  "severity": "Error"
}
```

三條規則以 `junction.kind` 互斥，同一交接處不會有兩條同時適用，因此不會落入引擎的 Conflict 路徑。

第四條是第 83 條的區劃面積規則。它不是 `CompartmentContinuity` 而是 `CompartmentArea`，因為它判定的是區劃本身的面積；它出現在這份文件裡，是因為 §7.2 的第 83 條那一列與 §2.5 的來源標記都靠它：

```json
{
  "ruleId": "tw-bcr-83-area",
  "version": "1",
  "category": "CompartmentArea",
  "legalReference": "建築技術規則建築設計施工編第83條第1款至第4款（十一層以上部分之區劃面積，暫定）",
  "effectiveDate": "2024-01-01",
  "jurisdiction": "TW",
  "priority": 20,
  "appliesWhen": "building.fireResistiveConstruction == true && zone.floorNumber >= 11",
  "requiredValue": "zone.area <= (zone.sprinklered ? 2 : 1) * (zone.interiorFinish == \"耐燃一級含底材\" ? 500 m2 : (zone.interiorFinish == \"耐燃一級\" ? (building.use == \"H-2\" ? 400 m2 : 200 m2) : (building.use == \"H-2\" ? 200 m2 : 100 m2)))",
  "exemptions": [ "zone.use == \"樓梯間\"", "zone.use == \"昇降機道\"", "zone.use == \"管道間\"", "zone.use == \"挑空\"" ],
  "evidenceFields": [ "zone.area", "zone.floorNumber", "zone.interiorFinish", "zone.sprinklered", "zone.use", "building.use", "building.fireResistiveConstruction" ],
  "severity": "Error"
}
```

幾個寫法上的決定：

- **優先序 20，壓在第 79 條的 `tw-bcr-79-area`（優先序 10）之上。** 引擎只讓最高優先序中適用的規則作答，低優先序的規則在高優先序有規則適用時根本不會執行。這裡可以這樣安排，是因為第 83 條的上限在每一階都嚴於第 79 條的一、五○○平方公尺——即使第三款五○○再依第四款加倍，也只到一、○○○——所以十一層以上交給第 83 條決定，不可能放過第 79 條會攔下的區劃。十層以下第 83 條的適用條件不成立，自然落回第 79 條。這與第 70 條（優先序 20）壓在第 79 條時效規則（優先序 10）上是同一套安排。
- **第四款的「得免計算二分之一」寫成「上限加倍」。** 規則 DSL 比較的實際值是 Revit 的面積本身（規格 11.4 步驟 2），規則不改寫實際值，所以折半寫在門檻那一側。`tw-bcr-79-area` 的一、五○○／三、○○○ 已經是同一個寫法。
- **第一款、第二款的Ｈ–２組但書讀 `building.use`，比對半角 `"H-2"`。** 這是專案裡 `建築物用途類組` 實際填入的寫法（見 `docs/agent/phase-3-acceptance.md` 的實機記錄）。第三款沒有Ｈ–２組放寬，規則也就沒有。
- **「除依第七十九條之二規定之垂直區劃外」寫成豁免條件。** 豁免成立即 `NotApplicable`，那些垂直空間由第 79 條之 2 管（本版未實作）。`zone.use` 目前沒有議定的用字表（spec 19 第 6 項未定），所以只列得出 `樓梯間`、`昇降機道`、`管道間`、`挑空` 四個；填別的字不會豁免，是安全的方向（只會多檢討、不會漏檢討）。
- **裝修等級沒填 → `InsufficientData`，不是「當作第一款」。** 三段上限差距太大，三元運算式的條件未知且分支不同值時引擎給不出答案，這正好就是規格 11.3 要的：放寬是設計要自己證明的許可，沒填就是判不出來，既不能默認寬的五○○，也不該直接判一○○未符合。填了工具不認得的字（例如 `耐燃二級`）則是「已知不符合放寬條件」，落回第一款的一○○平方公尺。這與第 79 條遇到沒填 `防火檢討_自動滅火設備` 的區劃是同一種答案。
- **第五款（防火設備一小時以上阻熱性）沒有寫進規則**：阻熱性沒有對應的白名單欄位與參數，`防火檢討_設計防火保護` 只答「是不是防火門窗」。區劃牆上的開口仍由 `tw-bcr-79-opening` 要求防火門窗，牆與樓地板的一小時時效仍由 `tw-bcr-79-wall-rating`／`tw-bcr-79-floor-rating` 要求，兩者不分區劃來源，所以第 83 條這兩項不會漏掉；只有阻熱性這個附加要求不檢討（見 §9）。

### 5.4 為什麼判定式拆成「要求值 + 豁免條件」

`RuleExpressionParser.ParseRequirement` 規定 `requiredValue` 必須是 **`實際值欄位 比較運算子 運算式`**，而且右側不得再引用左側那個欄位。原先草擬的

```text
junction.projectionDepth >= 500 mm || (junction.continuousFireRatedLength >= 900 mm && ...)
```

頂層是 `||` 而不是比較運算，會被規則編譯器以 `InvalidForm` 退回。引擎也不會把同一類別的多條規則做 AND：同優先序的規則若結論不一致會判 Conflict，低優先序的規則在高優先序有規則適用時根本不會執行。

拆法不是為了遷就工具，而是回到條文本來的寫法——「應突出五十公分以上（本文）」「但……者，得免突出（但書）」。落到引擎的 `EvaluateApplicable` 流程上，每個邊界都自然正確：

| 模型情形 | 引擎路徑 | 狀態 |
| --- | --- | --- |
| 突出 600 mm | 豁免不成立 → 本文成立 | `Pass` |
| 突出 600 mm，但嵌板缺時效值 | 豁免無法判定，但本文成立 | `Pass`（豁免的資料缺口不影響） |
| 未突出，連續 900 mm | 豁免成立，本文不再評估 | `NotApplicable`／`Exempt` |
| 未突出，連續 850 mm | 豁免不成立 → 本文不成立 | `Fail` |
| 未突出，連續長度無法量測 | 本文不成立 + 豁免有資料缺口 | `InsufficientData` |

最後一列正是 spec 11.3「不可將資料不足誤判為未符合」在這個檢查上的落點，而且不需要 `CurtainWallJunctionCheck` 寫任何特例——引擎的既有語意就給出正確答案。

另一個連帶結論：條文的「且該外牆構造具有同等以上防火時效」不寫成獨立條件，而是內建在 `continuousFireRatedLength` / `continuousFireRatedHeight` 的定義裡（§5.2）。這兩個量測只累計 `providedFireRating >= hostRequiredFireRating` 的嵌板，所以「90 cm 的具時效連續面」是一個量、一個門檻，剛好符合 DSL 的單一比較形式。

## 6. 參數需求

| 參數 | 類型 | 綁定 | 用途 |
| --- | --- | --- | --- |
| `防火檢討_設計防火時效` | Type（沿用既有定義） | **新增綁定** Curtain Panels | 嵌板之設計時效，`junction.minFireRating` 來源。不綁 Curtain Wall Mullions |
| `防火檢討_設計防火保護` | Type、YESNO（既有） | 既有 Doors、Windows、Curtain Panels | 交接帶內開口是否受防護 |
| `防火檢討_法規要求防火時效` | Type、寫回（既有） | 不新增綁定 | 交接檢討不寫回型別，結果只存在 ReviewRun |
| `防火檢討_室內裝修等級` | Instance、TEXT（**新增**，共享參數 GUID `…000e`） | **新增綁定** Areas | 第 83 條第一至三款的區劃面積上限，`zone.interiorFinish` 來源 |
| `建築物用途類組` | Instance、TEXT（既有，原本規則未讀） | 既有 Project Information | 第 83 條第一、二款的Ｈ–２組但書，`building.use` 來源 |

後兩者都因為第 83 條的面積規則而成為**必要參數**：`ReviewInputSources.NeededBy` 只看規則有沒有讀這個欄位，不分樓層，所以即使是五層樓的專案，前置檢查也會要求把這兩個參數加進專案才能檢討（`BCR-PARAM-001`）。這是刻意的——沒有這兩個參數，工具無法分辨十一層以上的區劃。

未綁定時 `InsufficientData`；已綁定但空值，對嵌板亦為 `InsufficientData`——時效是量值，不適用 spec 11.6 針對門窗 YESNO 的「已綁定未勾選視為否」界定。

此參數的存在正是 §3.2「樓板不突出」情境的先決條件：沒有它就無法證明層間嵌板達到該樓層樓地板的同等時效，該交接點只能停在 `InsufficientData`。設定流程（`FireReviewSetupFeature`）須把 Curtain Panels 列入必要綁定清單，並在前置檢查未綁定時明示「帷幕嵌板未綁定設計防火時效，層間交接無法判定」。

## 7. Revit 產出

### 7.1 檢討 View 標示

| 項目 | 表現 |
| --- | --- |
| CW-H 未符合 | 交接處帷幕嵌板 By Element Override 紅色；平面圖於交點標註實測 `continuousFireRatedLength` 與 `projectionDepth` |
| CW-V 未符合 | 立面／剖面 View 之層間帶 Filled Region 紅色；標註實測 `continuousFireRatedHeight` |
| CW-O 未符合 | 嵌板紅色 Override |

所有標示元素寫入 Package ID、Run ID、Zone ID，僅更新目前 Run 管理的元素，沿用 Phase 3 既有的 `ReviewMarkup` 機制。帷幕牆標示的擁有權標記另外帶 `ReviewMarkKind`（`JunctionNote`／`SpandrelBand`）與 `JunctionId`，重跑時以 `JunctionId` 覆蓋既有標示而非重複產生（案例 19）；區劃填滿區域維持原本的 token 格式，既有模型不受影響。

CW-V 的立面**由工具自己建立**，使用者不必事先備妥：每片有層間帶的帷幕牆一個剖面視圖，以
`ManagedOutputKind.CurtainWallElevation` 加上帷幕牆 UniqueId 為擁有權標記（與檢討平面圖、單線圖視圖、
色彩配置同一套機制），重跑時依標記找回而不是依名稱，使用者改名照樣接手。同一片帷幕牆在多個樓層的
層間帶共用一個立面——層間帶都落在該牆的平面定位線所在的垂直面上，一個立面就看得完。

標示的位置不是在標示時重新讀模型量出來的，而是檢討當下由幾何層寫進結果證據的 `junction.placement`（見 §12 步驟 6b-1）——檢討表與標示計畫都是從已儲存的結果重建的，重新讀會把標示畫到模型現在的位置，而結果說的是當時的狀態。

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
| `ReviewMarkup`（`ReviewMarkKind`／`PlannedReviewNote`／`PlannedSpandrelBand`） | Application/Reviews | §7.1 三種標示的標示計畫與依類別分家的差異比對 |
| `RevitReviewViewMarker` | Revit/Reviews | 實作：交接處 `TextNote`、層間帶 `FilledRegion` 與帷幕牆檢討立面 |

`FireReviewRunner` 的總狀態規則不變：任一 `Fail` 為未符合；無 `Fail` 但有 `InsufficientData` 或 `ManualReview` 為待確認；其餘皆 `Pass`／`NotApplicable` 才顯示符合。

## 9. 已知限制

- 只支援平面帷幕牆；曲面、傾斜面回 `ManualReview`。Curtain System 置於面上、沒有定位線，本版一律以
  「無法解析定位面」回 `ManualReview`，不會從檢討表消失。
- 連續段被帷幕牆自身的端點或頂底截斷、且不足 900 mm 時不供給，判 `InsufficientData`：立面在那裡接到
  另一片未讀取的牆，工具不知道它是否延續，而不是知道它不足。整棟通高的帷幕牆不受此限。
- 嵌板時效以型別參數為唯一來源，不解析複合構造層，也不判斷玻璃種類。
- 未涵蓋第 110 條防火間隔對外牆與開口的要求（獨立功能）。
- 未涵蓋第 80 條、第 84 條（非防火構造建築物）。
- 第 83 條區劃已納入（見 §2.5），面積規則 `tw-bcr-83-area` 已加入規則集（§5.3）。但工具不會替使用者切出第 83 條的區劃：`isCompartmentBoundary` 是純幾何判定（牆是否躺在 Area 邊界上），十一層以上沒有切出 100／200／500 ㎡ 區劃的模型會少算交接點，同時在區劃面積這一項判 `Fail`，不會誤報為符合。
- 第 83 條第五款「防火門窗等防火設備應具有一小時以上之阻熱性」不檢討：阻熱性沒有白名單欄位也沒有參數，`防火檢討_設計防火保護` 只答「是不是防火門窗」。第 83 條的牆壁一小時時效與開口防火門窗仍由第 79 條的三條規則涵蓋（不分區劃來源）。
- 第 83 條的Ｈ–２組但書以 `building.use == "H-2"` 逐字比對。專案把 `建築物用途類組` 填成 `Ｈ－２`（全角）或 `H-2組` 都不會比中，會退回較嚴的一○○／二○○平方公尺——方向安全，但要在參數清單裡把可接受字樣寫清楚。用途類組是整棟一個值，混合用途的建築物只有一部分是Ｈ–２組時無法逐區劃區分，須由審查者自行判斷或分包檢討。
- 第 83 條「除依第七十九條之二規定之垂直區劃外」只以 `zone.use` 的四個字樣（`樓梯間`、`昇降機道`、`管道間`、`挑空`）豁免；`zone.use` 沒有議定用字表（spec 19 第 6 項）。第 79 條的 `tw-bcr-79-area` 目前只豁免 `樓梯間`，兩條規則的豁免清單不一致——統一它會改動既有規則的行為，列為獨立變更。
- `tw-bcr-83-area` 的法源條文字串是「這個區劃是第 83 條的」唯一判斷依據（`FireReviewRunner.HostLegalReferences` 以子字串比對）。因此規則集標題、`tw-bcr-79-area` 的法源條文，以及任何區劃結果可能借用的文字都不得出現「第83條」三字——被擱置的區劃（`ManualReview`）與「該類別沒有規則」的結果都會拿規則集標題當法源條文。此約束有單元測試守著。
- 豎框不判定，且工具不跨越 grid line 累積連續段（見 §4.1、§4.5）。交接帶內的實板必須在模型中就是連續嵌板，這是**建模前置條件**而非工具限制；未整理的模型會停在 `ManualReview`，不會誤判為符合。
- Link 模型中的帷幕牆依既有 `LinkGeometryPolicy` 處理，預設不檢討。
- 不檢討樓板邊緣與帷幕牆背面之層間縫隙塞火：該縫的填塞屬施工項目，模型幾何無法證明，本工具不納入判定，須由設計與監造以其他方式確認。
- 帷幕牆檢討立面的視距方向由層間帶的起訖方向決定（`起點→終點` × `Z`），不讀模型判斷哪一側是室外：位置一律只由結果證據還原（§7.1），立面因此可能從室內側看向該面，層間帶的位置與尺寸不受影響。
- 帷幕牆檢討立面在該牆已無層間帶時**不會被刪除**，只是變成空的立面。工具建的視圖可能已被使用者放進圖框，自動刪除的代價高於留下一個空視圖。
- 既有檢討立面的裁剪範圍只會被放大、不會縮小。層間帶落在範圍外等於沒有標示，但使用者縮小過的範圍在其他方向仍然保留。

## 10. 測試案例

| # | 情境 | 期望 |
| --- | --- | --- |
| 1 | 區劃牆突出帷幕牆 600 mm | CW-H `Pass` |
| 2 | 區劃牆突出 499 mm、交接帶無時效 | CW-H `Fail` |
| 3 | 無突出、交接帶 900 mm 且嵌板 60 min | CW-H `NotApplicable`／`Exempt`（得免突出） |
| 4 | 無突出、交接帶 899 mm 且嵌板 60 min | CW-H `Fail`（邊界值） |
| 5 | 交接帶 1200 mm 但其中一片 30 min | CW-H `Fail`（該片不計入長度，連續段湊不到 900 mm） |
| 6 | 交接帶 900 mm 但含未受防護窗 | CW-H `Fail`（累積在開口處中斷） |
| 7 | 無突出、交點左側 900 mm 具時效、右側 0 mm | CW-H `NotApplicable`（採總和，不要求兩側各半） |
| 8 | 交接帶 900 mm 但中間有一條多餘 grid line，兩側嵌板皆 60 min | CW-H `ManualReview`，訊息含該 grid line 的 ElementId |
| 8b | 承上，刪除該 grid line 使嵌板連續後重跑 | CW-H `NotApplicable`（但書成立） |
| 8c | 交接帶內 grid line 一側為 60 min 實板、另一側為無時效玻璃 | CW-H 依實際連續長度判定（該 grid line 是真實斷點，不列 `ManualReview`） |
| 9 | 第 83 條區劃牆與帷幕牆交接、無突出、交接帶 900 mm 60 min | CW-H `NotApplicable`，證據 `hostLegalReference == "第83條"` |
| 10 | 層間實板 900 mm、60 min，該樓層樓地板要求 60 min | CW-V `NotApplicable`（得免突出） |
| 11 | 層間實板 900 mm、60 min，但該樓層樓地板要求 120 min（自頂層起算第 5 層以上） | CW-V `Fail`（該段不計入高度） |
| 12 | 層間實板 900 mm 但只有 30 min | CW-V `Fail` |
| 13 | 樓板外突 500 mm、層間全玻璃且嵌板無時效值 | CW-V `Pass`（突出條件已成立，不要求嵌板時效） |
| 14 | 樓板不突出、層間帶其中一片嵌板型別缺時效值 | CW-V `InsufficientData`（不得以其他片推定） |
| 15 | 嵌板類別未綁定 `防火檢討_設計防火時效` | 依 900 mm 但書判定之項目全為 `InsufficientData` |
| 16 | `fireResistiveConstruction == false` | 全項 `NotApplicable` |
| 17 | 三層連跨挑空帷幕牆 | CW-V `NotApplicable`，證據載明轉第 79-2 條 |
| 18 | 曲面帷幕牆 | `ManualReview` |
| 19 | 重跑同一 Package | 標示元素被覆蓋而非重複產生 |
| 20 | 修改嵌板型別時效後重跑 | 舊 Run 轉 Stale，新結果反映新值 |
| 21 | 12F 區劃 90 ㎡、裝修等級 `無` | 區劃面積由 `tw-bcr-83-area` 判 `Pass`（上限 100 ㎡），該區劃的邊界牆記為第 83 條 |
| 22 | 12F 區劃 1,200 ㎡、裝修等級 `無` | `Fail`（第 79 條的 1,500 ㎡ 不會來救它） |
| 23 | 12F 區劃 480 ㎡、裝修等級 `耐燃一級含底材` | `Pass`（第三款 500 ㎡） |
| 24 | 12F 區劃、裝修等級未填 | `InsufficientData`（不論面積大小），仍記為第 83 條來源 |
| 25 | 10F 區劃 1,200 ㎡ | 由 `tw-bcr-79-area` 判 `Pass`，邊界牆記為第 79 條 |

## 11. 決議紀錄

| # | 議題 | 決議 | 落在文件何處 |
| --- | --- | --- | --- |
| 1 | 900 mm 是總和或兩側／上下各半 | **採總和 ≥ 900 mm**，不要求各半 | §3.1、§3.2 |
| 2 | 樓板不突出時的證明方式 | 嵌板**型別**必須有 `防火檢討_設計防火時效`，且須 **≥ 該樓層樓地板之要求時效**（非固定 60 min）；任一片缺值為 `InsufficientData` | §3.2「樓板不突出時的參數要求」、§6 |
| 3 | 豎框無時效定義時如何處理 | **不判定**，不綁參數、不讀值、不計入取小。工具**不跨越 grid line 累積**；交接帶的連續性由建模保證——刪除多餘 grid line 使嵌板連續。偵測到疑似多餘 grid line 時判 `ManualReview` 並指出位置 | §4.1、§4.5、§9 |
| 4 | 第 83 條區劃是否納入 | **納入**，與第 79 條共用 CW-H 判定，證據以 `hostLegalReference` 區分來源 | §2.5、§4.2、§7.2 |
| 5 | 第 83 條面積規則與第 79 條如何並存 | **優先序 20 壓在第 79 條之上**，不併成一條、也不並列同一優先序（否則結論不一致會判 Conflict）。可以這樣壓，是因為第 83 條每一階的上限都嚴於第 79 條 | §5.3 |
| 6 | 裝修等級如何表達 | 單一 Text 欄位 `zone.interiorFinish`（`無`／`耐燃一級`／`耐燃一級含底材`），不拆成兩個是非欄位——一個參數、一段門檻，且未填時是資料不足而非默認放寬 | §5.2、§5.3、§6 |

第 4 項屬解釋選擇而非條文明文（第 83 條本身未規定突出或 90 cm），若個案審查機關採狹義見解，於規則集 `appliesWhen` 排除即可，不需改程式。

## 12. 實作進度

| 步驟 | 內容 | 狀態 |
| --- | --- | --- |
| 1 | `RuleCategory.CompartmentContinuity` 與 `RuleFieldCatalog` 的 `junction.*` 欄位（含 `hostLegalReference`） | **已完成** |
| 2 | `fire-review-rules.json` 三條規則，規則集版本升至 `2026.3-provisional` | **已完成** |
| 3 | `CurtainWallJunctionInputs` / `Options` / `Check`（純 Domain 與 Application，以 fixture 跑完 §10 全部案例） | **已完成** |
| 4 | `ICurtainWallGeometryReader` 介面、`CurtainWallJunctionResolver` 與 `RevitCurtainWallGeometryReader` 實作 | **已完成**（Revit 端待實機驗證） |
| 5 | `FireReviewSetupFeature` 加入 Curtain Panels 綁定 | **已完成** |
| 6a | `FireReviewRunner` 第四類檢查與檢討表第四列（含案例 20 的失效判定） | **已完成**（Revit 端待實機驗證） |
| 6b-1 | `ReviewMarkup`：§7.1 三種標示的標示計畫與差異比對，含案例 19 | **已完成** |
| 6b-2 | `RevitReviewViewMarker`：把 6b-1 的標註與層間帶真的畫進檢討視圖與立面／剖面 | **已完成**（Revit 端待實機驗證） |
| 7 | 第 83 條面積規則 `tw-bcr-83-area` 與 `zone.interiorFinish`，規則集版本升至 `2026.4-provisional` | **已完成**（`防火檢討_室內裝修等級` 尚未進批次參數面板） |

### 步驟 1、2 的驗證

`tests/BuildingRegulationReview.Core.Tests/Rules/CurtainWallJunctionRuleTests.cs`，15 個測試涵蓋：規則集編譯、`junction.*` 的類別隔離、CW-H／CW-V／CW-O 的 `Pass`／`NotApplicable`／`Fail`／`InsufficientData` 四態與邊界值、三種 `junction.kind` 互斥不衝突。

規則集版本異動會使既有工作包的檢討結果標示為需更新（spec 13.1），這是預期行為。

### 步驟 3 的產出與驗證

`src/BuildingRegulationReview.Application/Checks/`：

- `CurtainWallJunctionInputs.cs`：`CurtainWallJunctionKind`、`CurtainWallJunctionDoubt`（四種幾何無法判定的情形）、
  `CurtainWallJunction`（交接處事實，四個具名工廠 `WallJunction`／`Spandrel`／`OtherPanels`／`Doubtful`
  在建構時就擋掉違反輸入契約的資料）、`CurtainWallJunctionInputs`、`CurtainWallJunctionOptions`。
- `CurtainWallJunctionCheck.cs`：`CurtainWallJunctionCheck.Review(...)` 產生六態 `ReviewResult`，
  以及 `CurtainWallJunctionFinding`、`CurtainWallJunctionGroupSummary`（§7.2 的三列，CW-H 分列第 79／83 條）、
  `CurtainWallJunctionReview`。

幾何層的長度一律以 **mm** 交給 `CurtainWallJunction`（屬性名即帶單位，如 `ProjectionDepthMm`），
Check 在進入規則引擎前換算為欄位宣告的 m，交界只有這一處。

新增錯誤碼 `BCR-CW-001`～`BCR-CW-004`（非平面、交點無法解析、被 grid line 分割、連跨複數樓層），
與新的 `ReviewCheckTypes.CompartmentContinuity`。

`tests/BuildingRegulationReview.Core.Tests/Checks/CurtainWallJunctionCheckTests.cs`，34 個測試，
以 shipped 規則集跑完 §10 中不需要真實幾何的案例（1–7、8、8b、8c、9–18），另含結果證據、
檢討表三列、順序可重現與輸入契約的防呆。全套 1137 個測試通過。

尚未由 Check 層涵蓋的 §10 案例：19（重跑覆蓋標示元素）、20（改型別後重跑轉 Stale）屬步驟 6；
案例 8、17、18 在此層以 `CurtainWallJunctionDoubt` 為輸入驗證，真正的偵測在步驟 4。

### 步驟 3 的輸入契約

幾何層供給 `CurtainWallJunctionInputs` 時必須遵守：

- `continuousFireRatedLength` / `continuousFireRatedHeight` 只累計 `providedFireRating >= hostRequiredFireRating` 的嵌板，遇 grid line、未受防護開口或時效不足的嵌板即停止累積。
- 交接帶內任一嵌板缺時效值時，**不供給**該長度／高度欄位，讓引擎判 `InsufficientData`（不可填 0）。
- `projectionDepth` 一律供給，沒有突出就是 0，不可省略——省略會讓本文變成資料不足。

### 步驟 4 的產出與驗證

分成「讀」與「解」兩層，中間隔一層純資料，理由與 Phase 2 的 `CandidateObservationSet` 相同：
Revit 只負責轉換，所有判定都在可用 fixture 驗證的地方。

- `src/BuildingRegulationReview.Application/Candidates/CurtainWallObservations.cs`：
  `CurtainPanelObservation`（嵌板在帷幕牆自身平面上的 `(u, z)` 位置、設計時效、是否為未受防護開口）、
  `CurtainGridLineObservation`、`CurtainWallObservation`（定位線、外側法線、外面偏移、起訖標高、
  `NonPlanarReason`）、`CompartmentWallObservation`、`CompartmentFloorObservation`、
  `CurtainWallZoneObservation`、`CurtainWallObservationSet`。**全部以 mm 表示**。
- `src/BuildingRegulationReview.Application/Abstractions/ICurtainWallGeometryReader.cs`：
  `CurtainWallReadRequest`（工作包、Area Plan、各 host 的要求時效與來源條文、`CurtainWallJunctionOptions`）
  與唯讀的 `Read` 介面。**request 中列出的牆與樓板就是本次要讀的區劃邊界**——是不是區劃牆、來自第 79
  條還是第 83 條，由候選解析與規則引擎決定，不由讀取器判斷。
- `src/BuildingRegulationReview.Application/Candidates/CurtainWallJunctionResolver.cs`：
  `Resolve(observations, options?)` → `IReadOnlyList<CurtainWallJunction>`，§4 全部在這裡發生。
- `src/BuildingRegulationReview.Revit/Geometry/RevitCurtainWallGeometryReader.cs`：
  Revit 端轉換，feet → mm 只在這裡發生一次。

`tests/BuildingRegulationReview.Core.Tests/Candidates/CurtainWallJunctionResolverTests.cs`，27 個測試，
涵蓋 §10 中需要真實幾何的案例（8、8b、8c、17、18）與 §4 的每一條規則：突出量量測、連續段採兩側總和、
時效不足與未受防護開口中斷累積、交接帶缺時效值時不供給、疑似多餘 grid line 的偵測與訊息、
交點求解與「靠近但沒碰到」的 `UnresolvedIntersection`、層間帶取樣取最不利值、連跨複數樓層、
區劃歸屬與順序可重現。全套 1164 個測試通過，全方案 0 警告 0 錯誤。

幾個在實作時才定案、規格原本沒寫死的決定：

1. **連續段只在單一嵌板內量測**，越界嵌板只用來回答 §4.5 的「刪掉這條 grid line 會不會就夠」
   （§4.1 已據此改寫）。
2. **連續段被帷幕牆自身端點／頂底截斷且不足 900 mm 時不供給**：立面接到另一片沒讀到的牆，
   那是「不知道」不是「不足」，判 `InsufficientData` 而非 `Fail`。
3. **CW-O 以嵌板中心是否落在 90 cm 帶內判定歸屬**，不用重疊：一片通層玻璃只是伸進層間帶，
   仍是第 79 條之 4 的「其他部分外牆」，要照樣檢討。
4. **CW-H 的交點若兩側都有區劃，結果歸屬 ZoneId 較小的那一個**——一個交接處只產生一筆結果，
   歸在哪一區劃必須每次重跑都一樣。
5. **`hostRequiredFireRating` 為 null 時不供給連續段**：沒有門檻就沒有「具同等以上防火時效」可比，
   供 0 會讓沒量過的帶被判成未符合。

### 步驟 5 的產出與驗證

「綁定」在本工具裡是一句宣告：`ReviewInputSources.All` 說某個欄位由哪個參數、綁在哪些類別上供給，
前置檢查、參數讀取器與批次填寫面板都照著這句宣告走。步驟 5 就是把 Curtain Panels 加進
`element.providedFireRating` 的宣告，再讓三個跟著這句宣告走的地方都正確：

- `ReviewInputSources.FireRatingHosts`（新增）＝ `MemberHosts` + `CurtainPanels`，
  `element.providedFireRating` 改用它。`opening.providedFireProtection` 的 `OpeningHosts` 不動——
  兩份清單在帷幕嵌板上重疊，但各自回答不同的問題（時效 vs 防火門窗）。豎框不列入。
- `ReviewReadiness`：`UsedHosts` 原本以 `SequenceEqual(MemberHosts)`／`SequenceEqual(OpeningHosts)`
  分流，設計防火時效橫跨兩邊之後這個分流不再成立，改為把構件與開口併成一份候選清單再用
  `source.Hosts.Contains` 過濾——對既有兩個來源行為完全相同。未綁定 Curtain Panels 時，警告訊息
  額外附上 §6 要求的明示句「帷幕嵌板未綁定設計防火時效，層間交接無法判定。」；此句只加在
  設計防火時效上，其他類別的缺口仍是單純的一行。
- `RevitReviewParameterReader`：開口原本一律以 Doors 的參數清單讀取，帷幕嵌板多一個參數之後這個
  捷徑就是錯的，改為依每個開口自己的類別取參數名。
- `FireReviewTypeRow.CarriesRating`／`CarriesProtection`（新增）取代面板裡的 `IsOpening` 分流：
  帷幕嵌板兩者皆真，門窗只有保護，構件只有時效。批次填寫面板的「設計防火時效」欄位因此對帷幕嵌板
  可編輯、`Edits()` 會寫回，`MissingParameters` 也會指出嵌板型別缺這個參數。
  第 71～73 條沒給嵌板尺寸門檻，所以「推定時效」對嵌板仍是空的，值由設計者填。

`tests/BuildingRegulationReview.Core.Tests/Reviews/FireReviewIntegrationTests.cs`（5 項）、
`Parameters/FireReviewTypeTableTests.cs`（4 項）與 `Candidates/CurtainWallJunctionResolverTests.cs`（1 項）
新增 10 個測試，涵蓋宣告本身、未綁定時的明示訊息與其邊界（不得加到其他類別）、面板的欄位歸屬，
以及 §10 案例 15：類別未綁定時讀到的是「沒有這個參數」，與空白值同樣不供給連續段，
CW-H 的長度與 CW-V 的高度都留空，由引擎判 `InsufficientData`。全套 1174 個測試通過，全方案 0 警告 0 錯誤。

案例 15 在幾何層與 Check 層是同一條路徑：`ReviewInputAssembler.Rating(ParameterReading.Absent)` 與
空白值都得到 `ProvidedFireRating.Missing`，差別只在訊息。這是刻意的——「沒綁參數」與「綁了沒填」
對判定而言都是不知道，不是 0。

### 步驟 6a 的產出與驗證

第四類檢查接在防火門窗之後，是一次檢討裡**唯一在執行中讀模型**的一步：交接帶要量的是「具與區劃
同等以上防火時效」，那個門檻是同一次檢討的構件防火時效檢查算出來的，所以幾何不能在前置掃描時就
讀完。`FireReviewRequest` 因此多收一個 `ICurtainWallGeometryReader`（Application 的介面，Revit 型別
沒有滲進判定路徑），沒有給讀取器時帷幕牆那一列就是「未檢討」，日誌明說原因，不猜。

- `FireReviewRunner`：新增 `FireReviewStep.CurtainWallJunction`（步驟總數 4 → 5），
  `Junctions()` 組出 `CurtainWallReadRequest` → `ICurtainWallGeometryReader.Read` →
  `CurtainWallJunctionResolver.Resolve` → `CurtainWallJunctionCheck.Review`，結果併入同一個 Run。
  讀取失敗與其他檢查一樣中止整次檢討（理由見該類別的備註）；讀取器回報的警告逐條進日誌。
- **要求時效**（`RequiredFireRatingMinutes`）取自構件防火時效檢查的 `RequiredMinutes`，同一元素跨
  兩個區劃時取最嚴者。**只列 host**：這份字典與 `LegalReferences` 的 key 聯集就是讀取器要讀的區劃
  邊界，若把柱或區劃內部的牆也列進去，它們會被當成區劃邊界讀取。
- **區劃來源條文**（`LegalReferences`）：牆為第79條／第83條，樓地板為第79條之3。第83條不從模型判
  斷，而是看該區劃的面積結果是由哪一條規則判的（`LegalReference` 含「第83條」）——這是規則引擎的
  答案，不是程式的。一道牆兩側分屬第79條與第83條區劃時記為第79條（一般條文），確保每次重跑一致。
  目前出貨規則集沒有第83條的面積規則，所以實際上全為第79條；規則集加上之後不必改程式。
- `ReviewTable`：新增第四列 `CompartmentContinuity`「帷幕牆區劃交接」。§7.2 的三列以
  `ReviewTableGrouping.JunctionKind` 呈現（水平／層間／其他部分），CW-H 分列第79／83條則是
  `JunctionLegalReference`，與構件防火時效「類別 + Type」的雙層統計同一個機制。表格是從已儲存的
  結果重建的，所以 `junction.hostLegalReference` 一律寫進證據（連沒有規則判定的交接處也寫）。
  空的第四列狀態為「未檢討」，不影響總狀態——總狀態數的是結果，不是列。
- **案例 20**：嵌板型別時效改變要讓舊 Run 失效。它不是任何 Check 的輸入（決策 2 不變，仍只由
  `RevitCurtainWallGeometryReader` 直接讀），但證據基準必須知道它，否則改了型別沒人發現：
  `ReviewInputAssembly.PanelRatings`（由參數快照組出，與讀取器讀的是同一個型別參數）進入
  `ReviewBaselineBuilder`，寫成該嵌板 subject 的 `panelRating|` 一行。副作用是同一片嵌板的防火門窗
  結果也會一起被標為需更新——基準的粒度是 subject，構件的時效同樣寫在構件那一行，一致。
- **基準必須用同一個多載**：`ReviewBaselineBuilder.Build(set, environment, inputs)` 是給執行檢討與
  前置掃描兩邊共用的。少傳一項（例如只傳三份 inputs）會得到不同的指紋，模型什麼都沒動也會顯示
  「需更新」。`FireReviewModel.ReadStoredRun` 與測試的 `CurrentBaseline` 都已改用這個多載。
- **執行緒**：檢討改在 Revit API context 執行（`FireReviewWindow.Start` 以 `Post` 包住整段，取代
  `Task.Run`），因為這一步要讀模型。前置掃描本來就是這樣跑的；取消仍然有效——token 由 UI 執行緒
  設定，檢討在每個安全點檢查。進度條上限改由回報的 `Total` 決定。

新增 5 個測試：`Reviews/FireReviewIntegrationTests.cs` 4 項（讀取請求列出的 host 與要求時效、
交接處成為檢討表第四列、讀取失敗中止整次檢討、案例 20 的失效與新值）與
`Reviews/ReviewTableTests.cs` 1 項（§7.2 三列與第79／83條分列）。全套 1179 個測試通過，
`BuildingRegulationReview.sln` 與 WPF 外掛專案皆 0 警告 0 錯誤。

未涵蓋：§7.1 的檢討視圖標示（步驟 6b），以及 Revit 端實機驗證。

### 步驟 6b-1 的產出與驗證

標示計畫是從**已儲存的結果**重建的（spec 13.1），所以 §7.1 要用的每一項資訊都必須在證據裡：重新讀
一次模型會把標示畫在「模型現在的位置」，而結果說的是當時的狀態，兩者不一致正是 13.1 禁止的。因此
交接處的位置改由幾何層交出、由 Check 寫進證據，標示層只讀不算。

- **`CurtainWallJunctionPlacement`**（Application/Checks）：帷幕牆定位線上的一段（CW-H 起訖同點，
  即交點；CW-V 為層間帶）加上上下標高，單位 mm。`ToEvidenceText()`／`TryParseEvidence()` 以六個
  **公尺**數字（起點 X、Y，終點 X、Y，下緣，上緣）存成單一證據欄位 `junction.placement`，與
  `junction.panels` 同樣是一個欄位而不是六個。
- `CurtainWallJunctionResolver`：CW-H 交出 `PointAt(交點)` 與區劃牆的上下標高；CW-V 交出樓板邊緣
  跨距與樓板上下各 `MinFireRatedRunMm`（900 mm）的範圍，**並裁到帷幕牆本身**（§4.5 的層間帶定義）。
- `CurtainWallJunctionCheck`：證據新增 `junction.placement` 與 `junction.curtainWallUniqueId`
  （後者原本只有規則讀到時才會留下，現在連沒有規則判定的交接處也寫——CW-V 的層間帶要靠它決定畫
  在哪一面帷幕牆的立面上）。
- **`ReviewMarkKind`** 與擴充後的 `ReviewMarkKey`：`Zone`（既有的區劃填滿區域）、`JunctionNote`、
  `SpandrelBand`。區劃標示**維持原本的五段 token**，既有模型裡的標示照樣解析、照樣被接手；帷幕牆
  標示為七段，多出類別與 `JunctionId`（`Subject`，不得含 `/`）。`Slot` 因此含類別與 Subject。
- `ReviewMarkupPlan` 新增 `Notes` 與 `Bands`。帷幕牆結果**不走既有的逐元素塗紅路徑**：
  `SubjectUniqueIds` 含帷幕牆本體與區劃牆，整片塗紅會失真，改用證據裡的 `junction.panels`。
  CW-H＝嵌板紅色＋交點標註；CW-V＝層間帶 Filled Region＋高度標註（**不塗嵌板**，層間帶畫在立面，
  平面塗嵌板說明不了什麼）；CW-O＝嵌板紅色，無標註。缺嵌板、缺位置、缺交接處代號、找不到區劃，
  都逐項列入 `Skipped` 並說明原因，不靜靜丟掉。
- 量不到的值標註為「未量得」而不是 0——與 Check 層區分「未符合」與「資料不足」同一個理由。
- `ReviewMarkupDiff` 的比對改成**依類別分家**（`Match<T>`）：一次沒有層間帶的檢討並沒有對區劃標示
  說任何話，不能把別家的 slot 當成自己不再需要的標示刪掉。案例 19 因此成立——同一 Package 重跑時
  帷幕牆標示判 `Update`／`Unchanged`，不會產生第二份。

新增 15 個測試：`Candidates/CurtainWallJunctionResolverTests.cs` 5 項（交點位置、層間帶範圍、裁到
牆頂、證據往返、CW-O 無位置）、`Checks/CurtainWallJunctionCheckTests.cs` 2 項（證據記錄位置、無位置
時不寫）、`Reviews/ReviewMarkupTests.cs` 8 項（CW-H／CW-V／CW-O 三種標示、未量得、缺資料略過、
案例 19、兩類標示互不接手、mark key 往返與舊格式相容）。全套 1194 個測試通過，
`BuildingRegulationReview.sln` 與 WPF 外掛專案皆 0 警告 0 錯誤。

這一步只到「計畫」為止：`RevitReviewViewMarker` 當時只畫平面的區劃填滿區域，`Notes` 與 `Bands`
還沒落到模型上。那是下一步的事，見「步驟 6b-2 的產出與驗證」。

### 步驟 6b-2 的產出與驗證

6b-1 規劃的 `Notes` 與 `Bands` 真的落到模型上，全部在 `RevitReviewViewMarker` 一個檔案裡，
Application 層只補了三樣它需要的東西。

- **`ReadMarks` 一併收集 `TextNote`**（改用 `ElementMulticlassFilter`）。只收 `FilledRegion` 的話，
  標註每次重跑都會被當成不存在而重畫——案例 19 在單元測試裡成立，在真模型上不會成立。
- **`Mark` 收集全部視圖的既有標示**：檢討平面圖，加上 `FindCurtainWallElevations()` 找到的每一個
  帷幕牆檢討立面。差異比對看不到的標示等於已經消失的標示，會被重畫成第二份。
- **交接處標註**（`ApplyNotes`／`WriteNote`）：在檢討平面圖以 `TextNote.Create` 建立，位置取
  `Placement.MidpointMm` 轉 internal feet、Z 取檢討平面圖 `GenLevel` 的標高。文字型別取專案的
  `ElementTypeGroup.TextNoteType` 預設值，沒有預設就取第一個；完全沒有文字型別時逐項記 `Failed`。
  **更新時就地改**（`Text` 與 `Coord` 都可寫）而不是刪掉重畫，使用者加上去的 leader 因此留得住；
  填滿區域的邊界 API 改不動，所以那邊仍然是先建新的再刪舊的。
- **層間帶**（`ApplyBands`／`DrawBand`）：畫在該帷幕牆的檢討立面裡。四個角由新增的
  `CurtainWallJunctionPlacement.Corners()` 交出（下起、下訖、上訖、上起，走一圈剛好閉合），
  轉 feet 後**沿立面法線投影到該 View 的工作平面**再組 `CurveLoop`。畫之前確認立面方向確實沿著這面
  帷幕牆（法線與層間帶方向垂直、且法線水平），否則會被壓扁——這種情況記 `Skipped` 並說明要刪掉該
  立面重標，不硬畫。
- **帷幕牆檢討立面**（`EnsureCurtainWallElevations`）：`ManagedOutputKind` 新增
  `CurtainWallElevation`，`ManagedOutputKey` 因此多了 `Subject`（帷幕牆 UniqueId）——一個工作包只有
  一個單線圖視圖、一個色彩配置、一個檢討平面圖，但帷幕牆立面是一牆一個。**既有的三段 token 格式
  維持不變**，既有模型裡的標記照樣解析；帶 Subject 的是四段。視圖以 `ViewSection.CreateSection`
  建立，剖面框由 `SpandrelFrame` 從**這片牆的全部層間帶**算出（沿牆向 `BasisX`、`Z` 為 `BasisY`，
  範圍取所有角點的聯集再各留 1 m，視深 3 m），所以同一片牆在多個樓層的層間帶都在框內。名稱為
  `{檢討視圖名}_{帷幕牆 Mark 或 Id}_帷幕牆立面`，`ReviewOutputNaming.CurtainWallElevation()`。
- **立面在單獨一個 transaction 裡先建好**：視圖要先存在並 `Regenerate()` 過，才畫得進東西。三個
  transaction（建檢討平面圖／建帷幕牆立面／更新標示）仍都在同一個 `TransactionGroup` 裡，「單一元素
  被 Revit 拒絕就記一筆繼續跑、整體失敗才全部復原」的既有行為不變。
- **既有立面的裁剪範圍只放大不縮小**（`EnsureVisible`）：這次的層間帶在範圍外就放大到容納得下並記一筆
  `Updated`；Revit 拒絕（例如綁了 scope box）則記 `Skipped` 說明層間帶仍會建立但可能看不到。
- 計數不必另外處理：`ReviewMarkupResult.Summary` 數的是 items，標註、層間帶與立面都在裡面，
  `FireReviewWindow` 三處顯示的都是這個 `Summary`。

新增 13 個測試：`WriteBack/ManagedOutputTests.cs` 6 項（帶 Subject 的 token 往返、三段舊格式仍解析、
立面缺帷幕牆不成立、單一容器不得帶 Subject、Subject 不得含分隔字元、擁有權讀得出立面）、
`WriteBack/ReviewOutputNamingTests.cs` 3 項（立面命名、無 Mark 時的命名、視圖名保留底線但仍去掉
Revit 不接受的字元）、`Candidates/CurtainWallJunctionResolverTests.cs` 1 項（`Corners()` 的順序）、
`Describe` 與 token 往返各補 `ReviewView`／`CurtainWallElevation` 案例。全套 **1207 個測試通過**，
`BuildingRegulationReview.sln` 與 WPF 外掛專案皆 0 警告 0 錯誤。

**實機驗證清單**（尚未執行，需要真的 Revit 模型）：

1. 有 CW-H 未符合時，檢討平面圖的交點上出現標註，文字是實測 mm 值；同一工作包重跑不會多出第二份。
2. 有 CW-V 未符合時，出現一個名為 `{檢討視圖名}_{帷幕牆}_帷幕牆立面` 的剖面視圖，層間帶為紅色填滿
   區域，位置對得上樓板上下各 900 mm。
3. 同一片帷幕牆在兩個樓層都未符合時，兩條層間帶在**同一個**立面裡。
4. 手動改掉標註的文字或位置後重跑，標註回到工具算出的值（判 `Update`）而不是多出一份。
5. 把某個層間帶的填滿區域的擁有權標記改成別的工作包，重跑時該項記 `Skipped` 且元素不動。
6. 立面被使用者縮小裁剪範圍後，下一次有落在範圍外的層間帶時範圍會放大，並在摘要裡看得到那一筆。

### 步驟 7 的產出與驗證

第 83 條的區劃面積規則。在此之前 §7.2 檢討表的「第 83 條」那一列是空的，不是因為模型沒有那種交接點，
而是因為**規則集裡沒有任何區劃面積規則以第 83 條作答**——`FireReviewRunner.HostLegalReferences` 是以
區劃面積結果的法源條文判斷來源的（§2.5），所以沒有第 83 條的面積規則，就永遠讀不到第 83 條的來源。

改動的檔案：

- `src/BuildingRegulationReview.Domain/Rules/RuleFieldCatalog.cs`：新增白名單欄位 `zone.interiorFinish`
  （Text，對所有規則類別開放，與 `zone.use`、`zone.sprinklered` 同級）。
- `src/BuildingRegulationReview/Data/fire-review-rules.json`：新增 `tw-bcr-83-area`（`CompartmentArea`、
  優先序 20），規則集版本 `2026.3-provisional` → `2026.4-provisional`，標題補上兩層優先序的理由。
  **標題裡不能出現「第83條」三字**（見 §9 最後一則）。
- `src/BuildingRegulationReview.Application/Reviews/ReviewInputSources.cs`：新增 `InteriorFinish`
  （`防火檢討_室內裝修等級`）→ `zone.interiorFinish`，綁在面積（Area 實體參數）。`ReviewInputAssembler`
  與前置檢查都是照 `ReviewInputSources.All` 跑的，因此不需另外改串接層。
- `assets/SharedParameters/fire-review-shared-params.txt`：新增 GUID `…000e` 的
  `防火檢討_室內裝修等級`（TEXT）。**此檔必須維持 Big5／cp950 編碼**，存成 UTF-8 會讓 Revit 讀到亂碼
  參數名；本輪是以 `[System.Text.Encoding]::GetEncoding(950)` 讀寫的，`git diff` 確認只多一行。

連帶影響：`建築物用途類組` 從「只做證據」變成規則真的會讀的必要參數（第一、二款的Ｈ–２組但書），
前置檢查因此會在專案缺這個參數時阻擋檢討。`FireReviewIntegrationTests` 原本有一項就是拿它當「證據欄位
不需要參數」的例子，已改寫成用 `element.typeName`、`zone.id`、`opening.hostUniqueId` 舉例，並另加一項
明確斷言 `建築物用途類組` 現在會阻擋。

新增 24 個測試（`Rules/Article83AreaRuleTests.cs`）：規則集編出兩條面積規則且優先序 10／20、只有第 83 條
那條的法源條文含「第83條」而規則集標題不含、十層以下仍由第 79 條作答、十一層以上由第 83 條作答、
1,200 ㎡ 在 12F 判 `Fail`（第 79 條救不了它）、三段裝修等級各自的上限與邊界值、不認得的等級退回第一款、
Ｈ–２組在第一、二款加倍而第三款不加倍、灑水把每一階加倍、最寬的第 83 條上限仍嚴於第 79 條、
裝修等級未填為 `InsufficientData`（面積大小都一樣）、四種垂直空間豁免、豁免先於裝修等級判定、
用途未填且超限時是 `InsufficientData` 而非 `Fail`。全套 **1232 個測試通過**，`BuildingRegulationReview.sln`
與 WPF 外掛專案皆 0 警告 0 錯誤。

**尚未做（下一段）**：`防火檢討_室內裝修等級` 還沒進批次參數面板——`FireReviewZoneRow`、
`FireReviewZoneParameters`、`RevitFireReviewTypeScanner`、`FireReviewInputViewModels` 與
`FireReviewParameterPanelWindow` 都還不認得它，使用者目前只能在 Revit 的屬性面板逐一填。
桌面上那份 `防火檢討_Revit參數設定清單.md` 也要補這個參數與 `建築物用途類組` 的必要性變更。
