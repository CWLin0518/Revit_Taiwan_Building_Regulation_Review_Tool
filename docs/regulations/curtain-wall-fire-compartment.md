# 防火區劃與帷幕牆交接：第 79 條、第 79-3 條、第 79-4 條

> 狀態：**實作中**。§12 的步驟 1–15 已完成（步驟 11、12 隨決議 13 移除），**步驟 16a、16b、16c、16d、16e
> 已完成，並已於 2026-09-28 部署與實機驗證**（16c 的讀取、16d 的面板、16e 的綁定與寫入皆已驗到，
> **檢討表上 CW-O 玻璃那一路的判定已逐字核對**；驗證證據與其界線見 §12 的 16e 列）。
> 步驟 16（決議 16）把帷幕嵌板分成兩路作答：實心嵌板以 `防火檢討_設計防火時效` 作答且時效比照牆體由
> 模型尺寸推定，玻璃嵌板與帷幕牆門窗以 `防火檢討_設計防火保護` 作答；種類由新參數 `防火檢討_嵌板種類`
> 宣告，未宣告為資料不足。16a 是判定層（規則、幾何分流、六態），16b 是參數層（尺寸推定支援帷幕嵌板、
> 型別列依種類決定要填哪一格、共享參數檔加 `…0013`），16c 是 Revit 讀取層（讀種類與嵌板厚、每片嵌板都
> 讀防火保護、由材料提案種類、綁定用的共享參數檔加 `…0013`），16d 是參數面板（「嵌板種類」欄、提案以
> 黃底與「（提案）」標示、選實心才亮結構材料）。**部署前必須先完成 §12 的綁定程序**：
> `防火檢討_嵌板種類` 已是必要參數，Curtain Panels 沒綁到它時整個檢討會被 `BCR-PARAM-001` 擋下。
> CW-H、CW-V 不受決議 16 影響。步驟 13 已於 2026-09-27 實機驗證 `Fail` 路徑。**步驟 14（決議 14）與步驟 15（決議 15）已於 2026-09-28 部署並實機驗證，使用者確認 CW-H 檢討結果無誤**——CW-H 的但書路徑因此首次在 Revit 端驗到。步驟 14：防火帶一律以實體牆元素取代該段帷幕牆，交點因此落在帷幕牆定位線的延長線上，CW-H 照樣判定；交接帶的高程已改為區劃牆與帷幕牆的交集，未符合的實體外牆會被塗紅，躺在立面內的區劃牆不再產出 CW-H。步驟 15：外側法線不再採信 `wall.Orientation` 的正負，改由所屬區劃落在哪一側推定（§4.6 的三站多數決）——2026-09-27 於 Revit 量到帷幕牆的 `wall.Orientation` 純由定位線方向決定、`Wall.Flipped` 改不了它，反向畫的那一片會整片從檢討中消失，而步驟 14 預期的交接處擁有者正好是這一片。步驟 9 依設計決策取消 Area 上人工填寫室內裝修等級，改由區劃實際關聯的牆與天花板類型推導 `zone.interiorFinish`；**Revit 端尚未實機驗證**。

## 1. 功能摘要

| 項目 | 內容 |
| --- | --- |
| 功能 ID | `curtain-wall-fire-compartment` |
| 法規 | 建築技術規則建築設計施工編 第 79 條第 3、4 項、第 79-3 條、第 79-4 條；區劃來源含第 83 條 |
| 核心目的 | 當防火區劃（區劃牆壁／區劃樓地板）的邊界落在帷幕外牆上時，檢查交接處是否維持區劃連續性 |
| 模型輸入 | 區劃邊界牆、區劃樓地板、帷幕牆（Curtain Wall／Curtain System）、帷幕嵌板、Area 區劃 |
| 主要產出 | 交接處六態檢討結果、檢討 View 紅色標示與註記、檢討表列 |
| 前置 | Phase 2 區劃範圍已建立且未過期；`防火檢討_設計防火時效` 與 `防火檢討_嵌板種類` 已綁定至 Curtain Panels；交接帶已依 §4.5 整理 grid line |

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
- `continuousFireRatedLength`：以交點為中心，沿帷幕牆面往兩側**水平**連續具時效之構造長度**總和**（左 + 右）。**採總和判定，不要求兩側各 ≥ 450 mm**；交點一側為 900 mm、另一側為 0 mm 亦視為符合。依據為條文只寫「交接處之外牆面長度有九十公分以上」，未區分兩側。
- **供給這個長度的是帷幕牆立面內的實體外牆，不是帷幕嵌板。** 條文但書的主詞是「**該外牆構造**」；帷幕嵌板依第 1 條第 26 款是非承重外牆的一部分，實務上不做防火時效認證，達成但書的做法是在該交接處的立面內設一段具時效的實體外牆（層間牆／防火牆垛）。因此 CW-H **完全不讀嵌板的 `防火檢討_設計防火時效`**——該參數仍然是 CW-O（第 79-4 條）的輸入，兩項互不影響。求解見 §4.2。
- 「具時效」的門檻內建在這個量測裡（見 §5.2）：只有 `providedFireRating >= hostRequiredFireRating` 的**實體外牆**才計入長度。條文的「且該外牆構造具有與防火區劃之牆壁同等以上防火時效」因此不需要另一個判定式。
- 計入的實體外牆型別 `防火檢討_設計防火時效` 未填或不可讀時，幾何層**不供給** `continuousFireRatedLength`，交由引擎判 `InsufficientData`（複合構造在 Check 層轉為 `ManualReview`），不得推定合格；讀得到但未達區劃牆要求時供給 0，判 `Fail`。
- 交點上**完全沒有**實體外牆（該處立面是嵌板、或什麼都沒有）時供給 0，判 `Fail`。這是「知道這段外牆面沒有時效」而不是「不知道」，因此不 withhold。
- **交點不必落在帷幕牆自己的範圍內**（決議 14）。防火帶以實體牆元素取代該段帷幕牆時，帷幕牆會被切成兩片，區劃牆的交點落在兩片之間、也就是帷幕牆定位線的**延長線**上。只要那個位置被同一立面內、且與該帷幕牆相接的實體外牆連續段覆蓋，就照樣是第 79 條第 3 項的交接處，照樣量該段長度是否 ≥ 900 mm。求解與歸屬見 §4.2。
- 實體外牆上另開的門窗**不讀**：連續累積只看牆型別的時效。帶內開口要由審查者自行確認，見 §9。

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

對象為不落在 CW-H 之 90 cm 帶、也不落在 CW-V 之 90 cm 帶內的帷幕嵌板。**嵌板分兩路作答**（決議 16）：

```text
實心嵌板  Pass ⟺ panelMinFireRating >= 30 min
玻璃嵌板  Pass ⟺ panelMinFireProtection == "是"
```

一片帷幕牆上兩種嵌板都有時，**產出兩列**：同一個 `junction.kind`（`CurtainPanelOther`），但 `junction.id`
分別以 `:solid`／`:glazed` 結尾，`junction.panelKind` 為 `Solid`／`Glazed`。分兩列而不是取一個最不利值，
是因為兩路量的是不同的東西：一路是分鐘數，一路是是非題，湊不成同一個門檻。§7.2 的檢討表仍是三列——
分組看的是 `junction.kind`，兩列都落在「帷幕牆其他部分時效」那一列裡。

嵌板屬於哪一路由型別參數 `防火檢討_嵌板種類`（`實心`／`玻璃`）宣告，**嵌板為牆者不必填**（一片 `Wall`
必然是實心構造，讀取層直接認定）。帷幕牆上的門與窗跟玻璃嵌板同一路：它們是防火設備，答的是防火保護，
沒有自己的時效可讀（決議 16）。

為什麼要宣告、不由工具猜：沒有種類就分不出「實心嵌板漏填時效」（`InsufficientData`，使用者補得起來）
與「玻璃嵌板宣告不是防火設備」（`Fail`，是設計本身不符合）。這兩個狀態的處置完全不同，猜錯的代價是
把未符合說成資料不足。工具**可以提案**——`Material.MaterialClass` 是 `Glass` 或材料名含「玻璃」時預設
提 `玻璃`——但提案要使用者在參數面板確認後寫入參數，與 `結構材料` ＋ 推定時效 同一套模式。

實心嵌板的時效**比照牆體由模型尺寸推定**（決議 16、§6）：第 72 條第 1 款第 1 目（RC／SRC 厚 10 cm 以上
→ 二小時）、第 73 條第 1 款第 1 目（厚 7 cm 以上 → 一小時），SC 走被覆厚度。第 72、73 條的「牆壁」款
未限於承重牆壁，帷幕嵌板是外牆構造，套同一款與工具對一般牆的處置一致。推定只是提案，檢討讀的仍是
使用者接受後寫入的 `防火檢討_設計防火時效`（spec 11.5 步驟 3）。

以「防火設備」回答第 79-4 條的「半小時以上防火時效」是**解釋選擇**，記在 §11 決議 16：條文的文字是
時效而非設備。採認的理由是玻璃嵌板無法填寫構造時效，若不容許以認可的防火玻璃（防火設備）作答，
任何玻璃帷幕牆都必然在第 79-4 條判未符合。若個案審查機關採狹義見解，在規則集
`tw-bcr-79-4-curtain-wall-other-glazed` 的 `appliesWhen` 排除即可，不需改程式。

### 3.4 六態對照

| 情形 | 狀態 |
| --- | --- |
| 本文成立（突出 ≥ 500 mm，或 CW-O 之時效達標） | `Pass` |
| 本文不成立但但書成立（連續長度／高度 ≥ 900 mm） | `NotApplicable`，理由 `Exempt`（得免突出） |
| 本文與但書皆不成立，且所有輸入齊備 | `Fail` |
| CW-H：交點上的實體外牆型別缺 `防火檢討_設計防火時效` 值，且該項需依 900 mm 但書判定 | `InsufficientData` |
| CW-V／CW-O：`防火檢討_設計防火時效` 未綁定至 Curtain Panels，或層間帶內任一嵌板型別缺值 | `InsufficientData` |
| CW-O 玻璃嵌板：型別已綁定 `防火檢討_設計防火保護` 但未勾選 | `Fail`（spec 11.6「已綁定未勾選視為否」） |
| CW-O 玻璃嵌板：`防火檢討_設計防火保護` 未綁定或讀不出是非 | `InsufficientData` |
| CW-O：嵌板型別未填 `防火檢討_嵌板種類`（嵌板為牆者除外） | `InsufficientData`，訊息明示請指定嵌板種類 |
| 帷幕牆為曲面、傾斜面、雙曲面，或嵌板非平面 | `ManualReview` |
| 交點解析出兩組以上候選，或區劃牆端點與帷幕牆距離超過搜尋公差 | `ManualReview` |
| CW-H：同一段立面同時有嵌板與實體外牆（沿牆位置與高程都重疊） | `ManualReview` |
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

反過來也成立、而且要明講：**帷幕牆不進 `FireResistanceCheck` 的第 79 條第 1 項區劃牆壁時效判定**。
它在 Revit 裡是一片 `Wall`，會被收成 `MemberObservation` 並依幾何判成區劃邊界，所以那條規則必須
自己把它排除，見 §5.5。

嵌板集合取自 `CurtainGrid.GetPanelIds()`；嵌板可能是 `Panel`（FamilyInstance）或「嵌板為牆」的 `Wall`，兩者都要能讀型別參數。

**豎框（`Mullion`）不列入判定。** 法規未對豎框定義獨立的防火時效，本工具不讀取、不比較、也不因豎框而判 `InsufficientData`。豎框本身的防火填塞與嵌板背檔屬施工項目，同層間縫隙一併排除於本工具之外（見第 9 節）。

**工具不跨越 grid line 累積連續段。** `continuousFireRatedLength` 與 `continuousFireRatedHeight` 只在單一嵌板內累積：以交點（CW-H）或取樣點（CW-V）所在的那一片嵌板為準，取該點兩側長度／上下高度之和；走到嵌板邊界即停止，因為那裡就是一條 grid line。這是刻意的：由工具自行「橋接」被切斷的嵌板，等於讓程式猜測設計意圖，而模型上那條線是否代表真實的構造斷點，工具無從判斷。

越過邊界的嵌板只在一種情況下被讀到，而且只用來提問不用來判定：連續段不足 900 mm 時，工具會往兩側走訪相鄰且時效足夠的嵌板，看看「若這些 grid line 不存在，是否就達 900 mm」。答案為是才回 `SplitByGridLine`（§4.5），答案為否則該斷點為真，以實測的連續段判定。

正確的作法在建模端——**層間帶與區劃牆交接帶應以連續嵌板表達，多餘的 grid line 應在建模時刪除**，詳見 §4.5。

### 4.2 CW-H 交點求解

1. 取 `element.isCompartmentBoundary == true` 且非帷幕的區劃牆，取其 `LocationCurve`。候選來源含第 79 條第 1 項與**第 83 條**所生之區劃牆；兩者共用同一套判定，僅證據中的 `hostLegalReference` 不同。
2. 往兩端各延伸 `JunctionSearchToleranceMm`（預設 300 mm），與帷幕牆定位線求交，得交點 `P` 與帷幕牆參數 `u`。交點落在帷幕牆定位線的**延長線**上（`u < 0` 或 `u > 1`）時不立刻放棄，見「交點落在實體外牆上」。
3. 自 `u` 往兩側逐一走訪**立面內的實體外牆**，累積連續具時效段長度；交點落在某道牆內部時，該牆只計交點到其邊界的部分。**不走訪嵌板**——理由與求解細節見下一小節。
4. `projectionDepth = max(0, 區劃牆端點沿帷幕牆外法線方向超出帷幕牆外側面之距離)`。

**躺在立面內的區劃牆不產出 CW-H**（決議 14）。區劃牆的定位線本身就落在該帷幕牆的定位面內（`IsInFacadePlane`
成立，即共面且平行）時，它不是「與帷幕牆的交接處」，它**就是**那一段外牆；這種牆的交接處在它自己的兩端，
由垂直於立面的區劃牆各自產生。此時直接不產出這一列，`UnresolvedIntersection` 只留給「端點差一點沒碰到
帷幕牆」的情況。一道牆同時是區劃牆與實體外牆時，它仍以**實體外牆**身分供給別人的但書長度（兩個身分並存，
見下一小節第 1 點）。沒有這一條，凡是與帷幕牆共面相接的實體外牆都會因為兩線平行求不到交點，而掉進
「最近端點距離」的退路，回報一句「端點距帷幕牆 0 mm，超過搜尋公差 300 mm」的自相矛盾訊息。

#### 交接處之外牆面：立面內的實體外牆（取代決議 7）

但書的連續長度由**帷幕牆立面內的實體外牆**供給。判定依據從舊決議 7 的「牆有沒有填滿帷幕牆留下的洞」
換成「立面上這一段外牆本身有沒有時效」，後者才是條文問的問題。

1. 讀取層另外登錄**實體外牆**（`FacadeWallObservation`）：非帷幕的牆，其定位線落在該帷幕牆的定位面內
   （橫向偏差 ≤ `FacadePlaneToleranceMm`）、方向與帷幕牆定位線平行（夾角 ≤ `FacadeAngleToleranceDeg`），
   且高程範圍與讀取範圍重疊。一道牆同時是區劃牆與實體外牆時**兩邊都登錄**（平行於立面而躺在區劃邊界上
   的牆），兩種身分互不排斥。
2. 只有**高程上涵蓋整個交接帶**的實體外牆才計入（容差 `CurtainPanelObservation.TouchToleranceMm`）。
   交接帶的高程是**區劃牆與帷幕牆高程的交集**（決議 14）：

   ```text
   bandBottom = max(host.Bottom, curtainWall.Base)
   bandTop    = min(host.Top,    curtainWall.Top)
   ```

   理由：突出 50 cm 要擋的是火焰沿立面繞行，繞行的高度就是區劃牆在該處的高度——但只在該處的外牆面
   真的是**帷幕外牆**的那一段。區劃牆通常是整層高（含樓板厚度），而帷幕牆常自樓板面往上若干公分才
   起算（本文件所據模型為 450 mm）；那 450 mm 的外牆面是樓板邊緣，屬第 79 條之 3 的層間帶（CW-V），
   不是第 79 條第 3 項的帷幕外牆面。以區劃牆全高當門檻會要求實體外牆往下長進樓板，那是工具逼建模
   配合工具，不是條文的要求。牆比交集帶更高更低都沒有關係——一道通層具時效的實體外牆比 90 cm 帶更
   充分，不是更不充分。

   交集高度 ≤ `TouchToleranceMm` 時**不產出這一列**：區劃牆與這片帷幕牆在高程上沒有真正的交接面。

   交接帶的高程同時決定 `junction.placement` 的 Z 範圍、CW-O 要扣掉的嵌板，以及標示層塗紅的範圍，
   三者一律用同一個交集帶，不再各用一套。
3. 自交點 `u` 沿帷幕牆**水平**往兩側走，累積連續且達標（`providedFireRating >= hostRequiredFireRating`）
   的實體外牆覆蓋長度，兩側總和即 `continuousFireRatedLength`。相鄰兩道牆之間的接縫在 `TouchToleranceMm`
   內視為連續。
4. `projectionDepth` 的算法不變。

**量的是水平長度。** 舊決議 7 量的是上下嵌板之間的垂直帶高，與條文的「外牆面**長度**」不符——§11 記錄
過這個矛盾，改由實體外牆水平供給後兩者一致，該矛盾解除。

#### 交點落在實體外牆上（決議 14）

防火帶以**實體牆元素取代該段帷幕牆**是唯一的建模路徑（見「建模要求」），因此正確建模的立面長成
「帷幕牆 ─ 實體外牆 ─ 帷幕牆」，區劃牆的交點落在中間那一段，也就是**兩片帷幕牆的定位線之外**。
交點求解因此分兩段：

1. 交點落在帷幕牆定位線段內（`0 ≤ u ≤ 1`，容差 `SnapMm`）→ 與決議 13 相同，`at = u × length`。
2. 交點落在延長線上 → **不夾到端點、保留延長線上的參數 `at`**（可為負或大於牆長），再問兩件事：
   - `at` 是否被該立面內的實體外牆**連續段**覆蓋？連續段即接縫 ≤ `TouchToleranceMm` 的相鄰實體外牆
     串接的結果（與 `FacadeRun` 的串接規則同一個）。
   - 該連續段是否與**這片**帷幕牆相接（連續段的某一端與帷幕牆的端點距離 ≤ `TouchToleranceMm`）？

   兩者皆是 → 交點視為求得（`IsResolved`），照 §3.1 判定；否則這片帷幕牆與這道區劃牆沒有交接處，
   不產出這一列（不是 `ManualReview`——它們真的沒有交接）。

**歸屬（同一個交接處只能有一列）。** 連續段兩端都接著帷幕牆時，兩片牆都會走到上面第 2 條，必須有一個
與方向無關的擁有者判準，否則同一個交接處會出兩列、兩個檢討圖號。判準：

- 取所有與該連續段共面且相接的帷幕牆，比較各自**與連續段相接的那個端點**的座標，依 `(X, Y)` 字典序
  最小者擁有這個交接處；完全相同（不可能，兩個相接點必不同）時退回 `UniqueId` 的 ordinal 序。
- 不用「沿軸較低側」這種說法實作，因為帷幕牆各有自己的軸向：本文件所據模型中兩片牆都是從交點往外畫，
  兩片的 `at` 都是負數，用軸向分不出高低。座標字典序與畫牆方向無關，也與讀取順序無關。

擁有者決定 `junction.id`（`CW-H:<帷幕牆>:<區劃牆>`）、檢討圖號與定位，其餘鄰接的帷幕牆不重複產出。
證據 `junction.facadeWallUniqueIds` 則列出計入長度的每一道實體外牆，追溯時看得到真正供給長度的是誰。

#### 為什麼不再走嵌板，決議 7 為什麼整條作廢

嵌板路徑（自 `u` 往兩側累積具時效嵌板）與決議 7（以上下嵌板之間的間隔高度供給長度）**一併作廢**，
連同決議 7 的四項建模前置條件（§9 舊版）。新設計直接解掉它原本卡死的兩件事：

- **帷幕牆逐層建不再是問題。** 實體外牆是獨立元素，不必屬於交接處那一片帷幕牆的 `Panels`，所以上下
  帷幕牆是兩個元素、防火帶跨在樓板上，都照樣量得到。這是舊決議 7 在實務模型上等於不成立的主因
  （實測見 §12 步驟 12）。
- **不必貼齊嵌板。** 舊設計要求防火帶牆的上下緣與收邊嵌板貼齊（容差 0.5 mm、不得重疊），搭接 10 mm
  這種常見做法就會落入判 `Fail`。新設計不問這件事。

`PanelLookup`（交點落在端部或中間豎框上時，把查詢位置移到同一格內鄰近嵌板的邊緣）隨嵌板路徑一併
作廢：CW-H 不再量嵌板，就沒有「交點被豎框佔著」這個問題。**CW-V 與 CW-O 不受影響**——它們本來就
以沿牆取樣而非點查詢定位，且第 79-3 條、第 79-4 條的判定對象確實是嵌板。

#### 嵌板與實體外牆重疊時判 `ManualReview`

同一段立面同時有嵌板與實體外牆（沿牆位置與高程**都**重疊超過 `TouchToleranceMm`）時，模型對同一片
外牆講了兩件互相矛盾的事，工具不替使用者選一個，判 `ManualReview` 並在證據中同時列出兩者的 UniqueId。
這一條同時是建模錯誤的偵測器：把實體牆疊在玻璃嵌板前面而沒有把嵌板拿掉，是這個設計最容易踩的建模錯。

#### 建模要求

要讓某個交接處走但書，該處立面必須長成這樣（決議 14 定為**唯一**路徑）：

- **該段立面以實體牆元素取代帷幕牆**：把帷幕牆切成兩片，中間那一段放一道實體外牆。防火帶是實體構造，
  在模型裡就該是一片牆，不是「一片帷幕牆加上被刪掉的嵌板」。
- 實體外牆與兩側帷幕牆**共面**（橫向偏差 ≤ `FacadePlaneToleranceMm`、夾角 ≤ `FacadeAngleToleranceDeg`）
  且**相接**（接縫 ≤ `TouchToleranceMm`）；沿牆長度自交點起兩側合計 ≥ 900 mm。
- 高程涵蓋該交點的**交接帶**（區劃牆與帷幕牆高程的交集，見本節第 2 點）。實體外牆與帷幕牆的起訖高程
  相同時自動成立，不必為了工具把牆往下長進樓板。
- 該實體外牆的型別填 `防火檢討_設計防火時效` ≥ 區劃牆要求時效（第 79 條第 1 項、第 83 條第 1 款皆為
  60 min）。

**兩側帷幕牆的定位線往哪個方向畫不影響判定**（決議 15）。帷幕牆的 `wall.Orientation` 純由定位線方向
決定，工具不採信它的正負，改由所屬區劃落在哪一側推定外側法線，見 §4.6。決議 15 之前這是一條建模要求，
反向畫的那一片會整片從檢討中消失。

**不必、也不應該保留連續的帷幕牆再刪掉該柱的嵌板。** 決議 13 的實作只認得「交點落在帷幕牆自己的範圍內」
這一種情形，因此曾經把「保留連續帷幕牆＋刪嵌板」寫成可行路徑之一；決議 14 把交點求解擴充到延長線上的
實體外牆連續段之後，這條路不再需要，也不再是建議做法。「嵌板與實體外牆重疊判 `ManualReview`」
（BCR-CW-005）保留：實體牆疊在嵌板上仍然是模型對同一片外牆說了兩種構造。

**區劃牆本身抵到帷幕牆不算。** 區劃牆通常垂直於立面、不在帷幕牆的定位面內，因此不會被登錄為實體外牆。
這是刻意的：條文但書的主詞是外牆，若改以區劃牆的時效作答，任何具時效的區劃牆抵上玻璃帷幕牆都會自動
合格，第 79 條第 4 項即形同虛設。

不想改立面的話，另一條路是把區劃牆突出帷幕牆外側面 ≥ 500 mm——本文成立，引擎在讀但書之前就判 `Pass`。

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

另有三個幾何容差不屬於「工具設定」而屬於「什麼算同一片構造」，因此與判定式無關、也不開放調整，
分別掛在它們所描述的那個觀測型別上（與 `CurtainPanelObservation.TouchToleranceMm` 同一個作法）：

| 常數 | 預設值 | 位置 | 意義 |
| --- | --- | --- | --- |
| `TouchToleranceMm` | 0.5 | `CurtainPanelObservation` | 兩片構造貼在一起（嵌板、實體外牆的接縫） |
| `FacadePlaneToleranceMm` | 150 | `FacadeWallObservation` | 實體外牆的定位線離帷幕牆定位線多遠還算同一個立面（半個常見牆厚） |
| `FacadeAngleToleranceDeg` | 5 | `FacadeWallObservation` | 實體外牆與帷幕牆定位線的夾角多大還算平行 |

長度一律以 mm 進入 Domain 層；Revit internal feet 的轉換只發生在 `BuildingRegulationReview.Revit` 邊界。

### 4.5 建模前置條件：交接帶不得被多餘 grid line 分割

本檢討把 grid line 當作構造斷點，因此模型必須先滿足這個條件：

- **層間帶**（樓板上下各 900 mm 範圍）內，同一垂直線上的實板應為**單一連續嵌板**；純粹為了分割立面而拉的水平 grid line 應刪除。
- 真正代表構造斷點的 grid line（例如視線玻璃與實板的交界）必須保留——那正是累積應該停止的地方。

**這一節只約束 CW-V。** 決議 13 之後 CW-H 不讀嵌板，grid line 切不到它的但書長度；CW-H 的建模前置
條件改成 §4.2「建模要求」那幾條（該段立面以實體牆取代帷幕牆、與兩側帷幕牆共面且相接、高程涵蓋交接帶）。實體
外牆之間的接縫以 `TouchToleranceMm` 判連續，因此把一道牆切成幾段不影響判定——牆不是嵌板，切開它
不代表構造斷點。

Phase 3 前置檢查會掃出違反此條件的位置：當層間帶內偵測到 grid line，且其兩側嵌板的設計時效皆足夠時，判 `ManualReview`，訊息列出該 grid line 的 ElementId 與位置，提示「層間帶被 grid line 分割，請確認是否為真實構造斷點；若否，請刪除該 grid line 後重跑」。

刪除 grid line 後重跑，該點即依連續嵌板的實際尺寸正常判定。工具不提供「自動忽略豎框」的開關——一旦可以忽略，就無法分辨真斷點與假斷點。

### 4.6 外側法線定向（決議 15）

**`wall.Orientation` 的正負不可採信。** 2026-09-27 於 Revit 2024 量測同一道立面上的兩片帷幕牆，
數字是這樣的（室內在 `Y > 4790.71`，由區劃牆 `292657` 自 `(3000, 10879.28)` 往南抵到 `(3000, 4790.71)` 確立）：

| 牆 | 定位線方向 | `Wall.Flipped` | `wall.Orientation` |
| --- | --- | --- | --- |
| `282773`（X 3450 → 10000） | `(+1, 0, 0)` | `false` | `(0, −1, 0)`，朝室外 |
| `298700`（X 2550 → 0） | `(−1, 0, 0)` | `true` | `(0, +1, 0)`，**朝室內** |

也就是說，帷幕牆的 `wall.Orientation` 等於定位線方向繞 Z 軸轉 −90°，**與 `Wall.Flipped` 無關**：
`298700` 在 Revit UI 裡翻過面（`Flipped` 由 `false` 變 `true`）之後 `Orientation` 一動也沒動。
所以「外側朝哪邊」在 Revit UI 裡改不掉，唯一的模型端解法是把定位線往特定的旋轉方向畫——
那是使用者無從得知、也無法目視檢查的義務，不能當成建模要求。

法線的正負有三個消費者，錯了的後果不只是少一列：

| 位置 | 法線反了會怎樣 |
| --- | --- |
| `ZoneOf` | 往室外探 300 mm，找不到所屬區劃 → 整片牆的 CW-H 與 CW-O 靜默消失 |
| `CurtainWallObservation.OutwardDistanceOf`／`ProjectionOf` | 突出量量到反側；真正突出的區劃牆算出負值，被 `Math.Max(0, …)` 夾成 `0` → 第 79 條第 3 項本文的 `projectionDepth >= 500 mm` 判成「不突出」 |
| `Spandrels` 對樓板邊界的射線 | 同一個正負問題，下垂壁的突出量量到反側 |

因此**定向一次，三處同時正確**；只修 `ZoneOf`（例如找不到區劃時往反向再探一次）會讓該列現身，
但突出量仍量在錯的一側，等於把「靜默消失」換成「給出錯的 `Pass`／`Fail`」，那更糟。

定向規則——**由所屬區劃落在哪一側決定，不猜**：

1. 沿定位線取三個測站（牆長的 1/4、1/2、3/4），每站往兩側各探一點，深度同 `ZoneOf`
   （`ExteriorOffsetMm + ZoneProbeMm`）：`−n` 側為 `p − n·depth`，`+n` 側為 `p + n·depth`。
2. 數出各側找到區劃的測站數。`+n` 側**嚴格多於** `−n` 側時，把法線取反。
3. 其餘情形（兩側相同、含兩側都是 0）**保持原法線不變**。兩側都有區劃代表這片牆在室內、
   或探測深度碰到了鄰接區劃，取反是擲硬幣；兩側都沒有代表這片牆不屬於任何區劃，那是既有行為
   （該牆的 CW-H 與 CW-O 不產出），留在 §9。

取三站而非只取中點，是因為單一測站會被門洞、未建區劃的缺口或跨兩個區劃的立面誤導；
取多數而非「任一站」，是因為立面兩端可能各自貼到不同的區劃。

定向只改法線的正負，**`Start`、`End`、`Direction`、`ExteriorOffsetMm` 一律不動**——交接處的位置、
`ParameterOf`／`PointAt`、擁有權比對（`Owns`／`TouchingEnd` 比的是世界座標）與所有 `junction.id`
都必須保持不變。定向不留使用者可見的紀錄：它是把模型讀對，與把法線正規化成單位向量同一性質，
不是放寬或收緊任何判定。

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
| `junction.panelKind` | Text | CW-O 這一列答的是哪一路嵌板：`Solid`（實心，讀時效）／`Glazed`（玻璃與帷幕牆門窗，讀防火保護）。CW-H、CW-V 不設此欄位 |
| `junction.minFireProtection` | Text | CW-O 玻璃那一路的最不利防火保護讀值：`是`／`否`；未設定或讀不出時不設值，落入 `InsufficientData` |
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

資料來源是區劃實際關聯的牆與天花板類型參數 `防火檢討_室內裝修等級`（見 §12 步驟 9）。規則欄位仍是 `zone.interiorFinish`，但它是檢討時由模型彙總出的事實，不再是 Area 人工輸入。

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
  "version": "2",
  "category": "CompartmentContinuity",
  "legalReference": "建築技術規則建築設計施工編第79條之4",
  "effectiveDate": "2024-01-01",
  "jurisdiction": "TW",
  "priority": 20,
  "appliesWhen": "building.fireResistiveConstruction == true && junction.kind == \"CurtainPanelOther\" && junction.panelKind == \"Solid\"",
  "requiredValue": "junction.minFireRating >= 30 min",
  "exemptions": [],
  "evidenceFields": [ "junction.curtainWallUniqueId", "junction.zoneId", "junction.panelKind" ],
  "severity": "Error"
}
```

```json
{
  "ruleId": "tw-bcr-79-4-curtain-wall-other-glazed",
  "version": "1",
  "category": "CompartmentContinuity",
  "legalReference": "建築技術規則建築設計施工編第79條之4（玻璃嵌板以認可之防火設備作答，解釋選擇見決議 16）",
  "effectiveDate": "2024-01-01",
  "jurisdiction": "TW",
  "priority": 20,
  "appliesWhen": "building.fireResistiveConstruction == true && junction.kind == \"CurtainPanelOther\" && junction.panelKind == \"Glazed\"",
  "requiredValue": "junction.minFireProtection == \"是\"",
  "exemptions": [],
  "evidenceFields": [ "junction.curtainWallUniqueId", "junction.zoneId", "junction.panelKind", "junction.minFireProtection" ],
  "severity": "Error"
}
```

四條規則互斥：前三條以 `junction.kind` 分開，後兩條再以 `junction.panelKind` 分開，同一交接處不會有兩條
同時適用，因此不會落入引擎的 Conflict 路徑。兩條 CW-O 規則同為優先序 20，這不衝突——引擎只在同一優先序
有**兩條都適用**時才判 Conflict，而 `panelKind` 保證只有一條適用。

第四條是第 83 條的區劃面積規則。它不是 `CompartmentContinuity` 而是 `CompartmentArea`，因為它判定的是區劃本身的面積；它出現在這份文件裡，是因為 §7.2 的第 83 條那一列與 §2.5 的來源標記都靠它：

```json
{
  "ruleId": "tw-bcr-83-area",
  "version": "2",
  "category": "CompartmentArea",
  "legalReference": "建築技術規則建築設計施工編第83條第1款至第4款（十一層以上部分之區劃面積，暫定）",
  "effectiveDate": "2024-01-01",
  "jurisdiction": "TW",
  "priority": 20,
  "appliesWhen": "building.fireResistiveConstruction == true && zone.floorNumber >= 11",
  "requiredValue": "zone.area <= (zone.sprinklered ? 2 : 1) * (zone.interiorFinish == \"耐燃一級含底材\" ? 500 m2 : (zone.interiorFinish == \"耐燃一級\" ? (building.use == \"H-2\" ? 400 m2 : 200 m2) : (building.use == \"H-2\" ? 200 m2 : 100 m2)))",
  "exemptions": [ "zone.use == \"挑空\"", "zone.use == \"昇降階梯間\"", "zone.use == \"樓梯間\"", "zone.use == \"昇降機道\"", "zone.use == \"管道間\"" ],
  "evidenceFields": [ "zone.area", "zone.floorNumber", "zone.interiorFinish", "zone.sprinklered", "zone.use", "building.use", "building.fireResistiveConstruction" ],
  "severity": "Error"
}
```

幾個寫法上的決定：

- **優先序 20，壓在第 79 條的 `tw-bcr-79-area`（優先序 10）之上。** 引擎只讓最高優先序中適用的規則作答，低優先序的規則在高優先序有規則適用時根本不會執行。這裡可以這樣安排，是因為第 83 條的上限在每一階都嚴於第 79 條的一、五○○平方公尺——即使第三款五○○再依第四款加倍，也只到一、○○○——所以十一層以上交給第 83 條決定，不可能放過第 79 條會攔下的區劃。十層以下第 83 條的適用條件不成立，自然落回第 79 條。這與第 70 條（優先序 20）壓在第 79 條時效規則（優先序 10）上是同一套安排。
- **第四款的「得免計算二分之一」寫成「上限加倍」。** 規則 DSL 比較的實際值是 Revit 的面積本身（規格 11.4 步驟 2），規則不改寫實際值，所以折半寫在門檻那一側。`tw-bcr-79-area` 的一、五○○／三、○○○ 已經是同一個寫法。
- **第一款、第二款的Ｈ–２組但書讀 `building.use`，比對半角 `"H-2"`。** 這是第 88 條附表印的寫法，也是專案裡 `建築物用途類組` 實際填入的寫法（見 `docs/agent/phase-3-acceptance.md` 的實機記錄）。規則只比這一個字面值；`Ｈ–２組`、`H-2 組`、`Ｈ類第二組` 等條文自己也在用的其他寫法，由 `ReviewInputAssembler` 在參數變成規則輸入時折成正規形，不在規則裡堆 `||`（見 [`building.use` 寫法正規化](building-use-groups.md)）。第三款沒有Ｈ–２組放寬，規則也就沒有。
- **「除依第七十九條之二規定之垂直區劃外」寫成豁免條件。** 豁免成立即 `NotApplicable`，那些垂直空間由第 79 條之 2 管（規則層已完成，見 [垂直區劃](vertical-compartment.md)）。豁免清單直接取自第 79 條之 2 第 1 項所列的五種垂直空間：`挑空`、`昇降階梯間`、`樓梯間`、`昇降機道`、`管道間`（見 [`zone.use` 用字表](zone-use-vocabulary.md)）。同一份清單也是 `tw-bcr-79-area` 的豁免清單——見該文件「為什麼兩條面積規則共用同一份清單」。條文另有「其他類似部分」，無法逐一列舉，所以清單外的用字一律不豁免，是安全的方向（只會多檢討、不會漏檢討）。
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

### 5.5 帷幕牆自第 79 條第 1 項之區劃牆壁時效排除（`tw-bcr-79-wall-rating` 版本 2）

Revit 裡帷幕牆**就是一片 `Wall`**（`WallKind.Curtain`），所以它跟一般牆一樣被收成
`MemberObservation`；而 `CandidateResolver.RelateLinear` 判定區劃邊界**純看幾何**——中心線沿邊界
走到門檻長度就給 `ZoneRelationKind.Boundary`，不讀 `IsCurtainWall`。版本 2 之前，這就足以讓
`tw-bcr-79-wall-rating` 要求一片帷幕牆具備一小時以上防火時效。

實機證實（模型「建築防火檢討1」，2026-09-27）：

```
source.typeName          = 帷幕牆-150x250cm
source.width             = 0.025 m          ← 2.5 cm
candidate.relation       = Boundary
candidate.boundaryLength = 7.05 m
candidate.insideLength   = 0 m
規則                      = tw-bcr-79-wall-rating（要求 >= 60 min）
狀態                      = 資料不足
```

**這是條文的誤用。** 第 79 條第 1 項的「牆壁」是把區劃彼此分隔開的牆；帷幕牆是外牆，分隔的是
室內與室外（`insideLength = 0` 正是這件事的量測證據）。它在區劃邊界上該滿足的是：

| 條文 | 要求 | 既有規則 |
| --- | --- | --- |
| 第 79 條第 3 項 | 區劃牆壁突出 50 cm，或交接處外牆面 90 cm 以上且同等防火時效 | `tw-bcr-79-curtain-wall-junction` |
| 第 79 條之 3 第 2 項 | 樓地板交接處同上（層間帶） | `tw-bcr-79-3-curtain-wall-spandrel` |
| 第 79 條之 4 | 其他部分外牆半小時以上 | `tw-bcr-79-4-curtain-wall-other` |

三條都已存在且走 `junction.*`，所以這個排除**移走的是本來不屬於這片牆的要求，沒有少檢討任何
事**。修法：新增白名單欄位 `element.isCurtainWall`，`appliesWhen` 加 `&& element.isCurtainWall != true`，
規則 `version` 由 `1` 跳到 `2`（同一個模型的答案改變了，儲存的結果帶著版本，不跳版兩者分不開）。

**第 70 條不需要同樣的排除**：`tw-bcr-70-bearing-wall-rating` 已經要求
`element.isStructural == true`，而帷幕牆不是承重牆。排除只放一條規則，不放兩條。

修好之後帷幕牆**不會從檢討表消失**，而是顯示「不適用」：`RuleEngine.NoRuleApplies` 會把
`appliesWhen` 的欄位當證據輸出，所以讀報告的人直接看到 `element.isCurtainWall = true`，知道是
為什麼不適用。也因此這個欄位**不必**加進 `evidenceFields`。

`element.isCurtainWall` 由 `CandidateFacts.ForMember` **無條件**設定（「不是帷幕牆」是答案而不是
缺漏）。這一點有守門測試，而且是這組測試裡最重要的一條：一旦組裝層漏掉這個事實，規則連自己的
`appliesWhen` 都判不出來，**專案裡每一片邊界牆都會變成資料不足**，而且是靜默的——每片牆照樣
有一列。測試見 `tests/BuildingRegulationReview.Core.Tests/Rules/CurtainWallBoundaryRatingTests.cs`。

## 6. 參數需求

| 參數 | 類型 | 綁定 | 用途 |
| --- | --- | --- | --- |
| `防火檢討_設計防火時效` | Type（沿用既有定義） | **新增綁定** Curtain Panels；Walls 既有 | 嵌板之設計時效（CW-V、CW-O）與交接處實體外牆之設計時效（CW-H，決議 13），同為 `junction.minFireRating` 來源。不綁 Curtain Wall Mullions |
| `防火檢討_設計防火保護` | Type、YESNO（既有） | 既有 Doors、Windows、Curtain Panels | 交接帶內開口是否受防護；**自決議 16 起同時是 CW-O 玻璃那一路的答案**（`junction.minFireProtection`） |
| `防火檢討_嵌板種類` | Type、TEXT（共享參數 GUID `…0013`） | **新增綁定** Curtain Panels（只綁這一個類別） | 宣告 `實心`／`玻璃`，決定 CW-O 這一列讀時效還是讀防火保護（`junction.panelKind`）。未填為 `InsufficientData`——工具可由材料類別提案，但不替使用者決定 |
| `結構材料`、`防火被覆厚度` | Type（既有，GUID `…000b`／`…000c`） | **加綁** Curtain Panels（既有 Walls、Columns、Floors、結構柱） | 實心嵌板的時效比照牆體由尺寸推定所需（決議 16、§3.3） |
| `防火檢討_法規要求防火時效` | Type、寫回（既有） | 不新增綁定 | 交接檢討不寫回型別，結果只存在 ReviewRun |
| `防火檢討_室內裝修等級` | Type、TEXT（共享參數 GUID `…000e`） | **綁定** Walls、Ceilings | 第 83 條第一至三款的區劃面積上限；檢討時以區劃內最弱等級推導 `zone.interiorFinish` |
| `建築物用途類組` | Instance、TEXT（既有，原本規則未讀） | 既有 Project Information | 第 83 條第一、二款的Ｈ–２組但書，`building.use` 來源 |

後兩者都因為第 83 條的面積規則而成為**必要參數**：`ReviewInputSources.NeededBy` 只看規則有沒有讀這個欄位，不分樓層，所以即使是五層樓的專案，前置檢查也會要求把這兩個參數加進專案才能檢討（`BCR-PARAM-001`）。這是刻意的——沒有這兩個參數，工具無法分辨十一層以上的區劃。

未綁定時 `InsufficientData`；已綁定但空值，對嵌板亦為 `InsufficientData`——時效是量值，不適用 spec 11.6 針對門窗 YESNO 的「已綁定未勾選視為否」界定。

此參數的存在正是 §3.2「樓板不突出」情境的先決條件：沒有它就無法證明層間嵌板達到該樓層樓地板的同等時效，該交接點只能停在 `InsufficientData`。設定流程（`FireReviewSetupFeature`）須把 Curtain Panels 列入必要綁定清單，並在前置檢查未綁定時明示「帷幕嵌板未綁定設計防火時效，層間交接無法判定」。

`防火檢討_嵌板種類` 同樣是**必要參數**：`junction.panelKind` 寫在兩條 CW-O 規則的 `appliesWhen` 裡，
`ReviewInputSources.NeededBy` 因此會要求它綁到 Curtain Panels 才能開始檢討（`BCR-PARAM-001`）。這與
`室內裝修等級`、`建築物用途類組` 同一個道理——沒有它工具分不出該讀哪一個參數。**嵌板為牆的嵌板不需要
這個宣告**：它的型別是 `WallType`，讀取層由元素類別直接認定為實心，參數面板上它也本來就出現在「牆」
那一列（`結構材料` 與尺寸推定都已可用）。

**CW-H 讀的是 Walls 上的同一個參數**（決議 13），那個綁定早就存在（第 79 條第 1 項的牆壁時效在用），
所以 Curtain Panels 的綁定與否不影響 CW-H；反過來，交接處那道實體外牆的型別沒填時，CW-H 停在
`InsufficientData`，與嵌板沒填時 CW-V 的處境相同。

## 7. Revit 產出

### 7.1 檢討 View 標示

| 項目 | 表現 |
| --- | --- |
| CW-H 未符合 | **計入長度的實體外牆**（`junction.facadeWallUniqueIds`）與交接帶涵蓋的帷幕嵌板（`junction.panels`）By Element Override 紅色；平面圖於交點標註**檢討圖號**與實測 `continuousFireRatedLength`、`projectionDepth`。交點上完全沒有實體外牆（純玻璃立面）時兩者都是空的，只有標註，見 §9 |
| CW-V 未符合 | 立面／剖面 View 之層間帶 Filled Region 紅色；平面圖標註**檢討圖號**、實測 `continuousFireRatedHeight` 與「詳見立面 {圖號}」 |
| CW-O 未符合 | 嵌板紅色 Override |

**實體外牆走自己的欄位，不借 `junction.panels`**（決議 14）。塗紅的來源有兩個清單，標示層各自讀：
`junction.panelUniqueIds` 是交接帶涵蓋的帷幕嵌板，`junction.facadeWallUniqueIds` 是計入長度的實體外牆。
把牆塞進嵌板清單雖然也會被塗紅，卻會讓證據說「那道牆是一片嵌板」，CW-O 的扣除也會跟著錯。

實體外牆可能同時是別的區劃的邊界牆，也可能被多個交接處計入：塗紅沿用 Phase 3 既有的 `ReviewMarkup`
覆蓋機制，以 Run 與元素為鍵，重複計入不會疊出第二筆。

#### 檢討圖號

每一筆未符合的 CW-H／CW-V 交接都有一個**檢討圖號**：`CW-H-01`、`CW-V-01`（`CurtainWallMarkNumbers`）。
同一個圖號同時出現在三個地方，未符合的那一列才找得到它被判定時看的那張圖：

1. 檢討表該列（檢討視窗的清單列與明細的「檢討圖號」）；
2. 檢討平面上的文字標註（標註內容以圖號開頭，CW-V 另加「詳見立面 {圖號}」）；
3. CW-V 自動產生的立面視圖名稱：`{檢討視圖}_{圖號}_{帷幕牆}_帷幕牆立面`；層間帶填滿區域的
   `Comments` 也寫入圖號。

編號規則與其理由：

- **圖號由檢討表推導，不是由標示計畫推導**（`CurtainWallMarkNumbers.Assign(ReviewTable)`）。檢討視窗不
  建標示計畫也要顯示同一組號碼，而兩邊各自算一次就必須保證算出同一個答案。
- 依類別（CW-H 先、CW-V 後）分別流水，同類別內**依 `junction.id` 排序**——同一片帷幕牆的層間帶因此
  號碼相連，共用同一張立面時名稱才短。
- **圖號是這一次檢討的圖面參照，不是身分。**交接處的身分仍然是 `junction.id`，重跑仍以它覆蓋既有標示。
  修好一處之後其後的號碼會遞補，與圖面刪掉一張詳圖後重新編號相同。
- 只有 CW-H 與 CW-V 編號。CW-O 只塗紅嵌板、不寫任何標註，號碼沒有地方可以出現、也沒有東西可以對應。
- 未符合但無法標示（缺位置、缺嵌板、區劃找不到）的交接**仍然有圖號**，略過訊息會講出那個號碼，
  使用者才知道檢討表上的哪一列沒有對應的圖。

一片帷幕牆的立面同時畫著多層的層間帶時，視圖名稱列出至多 3 個圖號，超過就寫 `CW-V-01等 N 處`。
重跑若使號碼改變，**只有在視圖名稱仍然是工具上次寫進去的那個名字時才改名**（比對擁有權標記裡存的
簽章）；使用者改過名就不動它，只在結果清單說明本次的圖號是什麼——與裁剪範圍只放大不縮小、
視圖改名照樣接手是同一個原則。

所有標示元素寫入 Package ID、Run ID、Zone ID，僅更新目前 Run 管理的元素，沿用 Phase 3 既有的 `ReviewMarkup` 機制。帷幕牆標示的擁有權標記另外帶 `ReviewMarkKind`（`JunctionNote`／`SpandrelBand`）與 `JunctionId`，重跑時以 `JunctionId` 覆蓋既有標示而非重複產生（案例 19）；區劃填滿區域維持原本的 token 格式，既有模型不受影響。

CW-V 的立面**由工具自己建立**，名稱帶著上面說的檢討圖號，使用者不必事先備妥：每片有層間帶的帷幕牆一個剖面視圖，以
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
| `CurtainWallJunctionResolver` | Application/Candidates | 由候選集合組出交接處清單（無 Revit 相依）；每片帷幕牆先經 `Oriented` 依所屬區劃定向外側法線（決議 15） |
| `ICurtainWallGeometryReader` | Application/Abstractions | 幾何讀取介面 |
| `FacadeWallObservation` | Application/Candidates | 立面內的實體外牆（CW-H 但書的來源，決議 13）與其兩個容差常數 |
| `RevitCurtainWallGeometryReader` | Revit/Geometry | 實作：嵌板走訪、交點、突出量、層間高度、立面內實體外牆 |
| `RuleCategory.CompartmentContinuity` | Domain/Rules | 新增列舉值 |
| `RuleFieldCatalog` | Domain/Rules | 新增 `junction.*` 欄位 |
| `FireReviewRunner` | Application/Reviews | 串接第四類檢查與檢討表彙總 |
| `ReviewMarkup`（`ReviewMarkKind`／`PlannedReviewNote`／`PlannedSpandrelBand`） | Application/Reviews | §7.1 三種標示的標示計畫與依類別分家的差異比對 |
| `CurtainWallMarkNumbers` | Application/Reviews | §7.1 檢討圖號：由檢討表指派 `CW-H-01`／`CW-V-01`，供檢討表、標註與立面名稱共用 |
| `RevitReviewViewMarker` | Revit/Reviews | 實作：交接處 `TextNote`、層間帶 `FilledRegion` 與帶圖號的帷幕牆檢討立面 |

`FireReviewRunner` 的總狀態規則不變：任一 `Fail` 為未符合；無 `Fail` 但有 `InsufficientData` 或 `ManualReview` 為待確認；其餘皆 `Pass`／`NotApplicable` 才顯示符合。

## 9. 已知限制

- 只支援平面帷幕牆；曲面、傾斜面回 `ManualReview`。Curtain System 置於面上、沒有定位線，本版一律以
  「無法解析定位面」回 `ManualReview`，不會從檢討表消失。
- **CW-V** 的層間帶被帷幕牆自身的頂底截斷、且不足 900 mm 時不供給，判 `InsufficientData`：立面在那裡
  接到另一片未讀取的牆，工具不知道它是否延續，而不是知道它不足。整棟通高的帷幕牆不受此限。
  **CW-H 沒有這個問題**：實體外牆是獨立元素，延伸到帷幕牆範圍之外的那一段照樣讀得到、照樣計入。
- 嵌板時效以型別參數為唯一來源，不解析複合構造層，也**不由模型判斷玻璃種類**——是不是玻璃由
  `防火檢討_嵌板種類` 宣告（決議 16），工具只在參數面板提案。此來源只服務 CW-V 與 CW-O；CW-H 不讀嵌板
  時效（§3.1、§4.2）。
- **玻璃嵌板只在 CW-O 答得出來。** 勾了 `防火檢討_設計防火保護` 的玻璃嵌板滿足第 79-4 條（決議 16），
  但**不供給 CW-H 的 90 cm 長度、也不供給 CW-V 的 90 cm 高度**：那兩條但書要的是「同等以上防火時效」，
  防火設備不是防火時效。層間帶全為玻璃時 CW-V 仍然只能靠樓板突出 500 mm 過關（§3.2、案例 13）。
- **玻璃嵌板的防火時效讀值不進 CW-O 的判定。** 玻璃填了 `設計防火時效` 也不會改變玻璃那一路的答案
  （規則只讀 `junction.minFireProtection`），反之實心填了防火保護也不會讓時效那一路通過。填錯欄位的
  代價是 `InsufficientData`，不是誤判為通過（守門測試釘住）。
- **CW-H 的但書只認立面內的實體外牆，因此認可的防火玻璃嵌板無法作答**（決議 16 讓玻璃嵌板答得出
  第 79-4 條，但**沒有**鬆動這一條——兩者問的不是同一件事）。 若專案確實以認證防火玻璃
  達成第 79 條第 3 項但書，本版會判 `Fail`，須由審查者以其他方式說明。要支援這種做法必須讓嵌板重新
  參與 CW-H 的時效判定，那是**未決議的設計擴張**（使用者已明確選擇以實體牆建模為唯一路徑）。
- **CW-H 的實體外牆要在帷幕牆的定位面內。** 區劃牆通常垂直於立面，不會被登錄為實體外牆；把實體牆
  建在立面之外（例如貼在帷幕牆背面而非共面）也不會被認定。建模要求見 §4.2。
- **交點落在帷幕牆之外時，實體外牆連續段必須與該帷幕牆相接**（決議 14）。連續段與帷幕牆之間留了縫
  （超過 `TouchToleranceMm`）時視為沒有交接處，該列不產出——工具不會替使用者橋接立面上的縫隙，理由
  與不跨越 grid line 累積相同。整條立面完全沒有帷幕牆時本來就不是第 79 條第 3 項的情形，也不產出。
- **不屬於任何區劃的帷幕牆會整片從檢討中消失。** 所屬區劃是往室內探 300 mm 找出來的（`ZoneOf`），
  兩側都找不到區劃時靜默丟掉該牆的 CW-H 與 CW-O。決議 15 之後，「法線方向相反」不再是這個現象的成因
  ——外側法線已改由所屬區劃落在哪一側推定（§4.6），定位線往哪個方向畫都一樣。剩下的成因是模型真的
  沒有在該處建 Area 區劃。**本版沒有偵測**；把「兩側都找不到所屬區劃」改判 `ManualReview` 而不是丟棄，
  是**未決議**的擴充。
- **兩側都有區劃的帷幕牆保留原本的法線正負**（§4.6 第 3 點）。室內的帷幕牆、或探測深度碰到鄰接區劃時
  會落入這一種，此時 `wall.Orientation` 的正負若是反的，突出量就量在反側（`ProjectionOf` 夾成 `0`，
  本文判成「不突出」，接著才去看但書）。定向步驟刻意不在這種情形猜測——由區劃位置決定外側是本功能唯一
  的依據，兩側都有區劃時這個依據就沒了。第 79 條第 3 項的對象是**外牆**，室內帷幕牆本來就不在適用範圍，
  但工具不會替使用者判斷哪一片是室內的。
- **CW-H 的實體外牆高程須涵蓋該交點的交接帶**，即區劃牆與帷幕牆高程的**交集**（決議 14）。只封住其中
  一段（例如區劃牆通層高、實體外牆只做 90 cm 帶）不成立，因為火焰沿立面繞行的高度就是區劃牆在該處
  與帷幕外牆相接的高度。交集之外那一段外牆面（常見是樓板邊緣的 45 cm）不由 CW-H 回答，它是第 79 條
  之 3 的層間帶，由 CW-V 回答。
- 實體外牆**不讀該牆上的門窗**：連續累積只看該牆型別的時效。帶內另開的門窗要由審查者自行確認，
  工具不會偵測。這一點與舊決議 7 相同。因此 CW-H **不供給** `junction.hasUnprotectedOpening`——那是
  一件工具沒有查的事，供 `false` 會讓證據說謊（CW-V 仍然供給，它讀的是嵌板）。
- **CW-H 未符合時塗紅計入長度的實體外牆與交接帶涵蓋的嵌板**（決議 14，§7.1）。**交點上完全沒有實體
  外牆時仍然沒有可塗紅的元素**：純玻璃立面的交接帶本來就既沒有牆也沒有中點落在帶內的嵌板，標示會被
  略過並記一筆略過訊息。交接處的文字標註（含量測值）仍會建立，圖號與檢討表也仍然對得上，所以不影響
  追溯。要在這種情形也塗出「該處立面」得改塗帷幕牆本身或畫填滿區域，屬未排入的工作。
- 未涵蓋第 110 條防火間隔對外牆與開口的要求（獨立功能）。
- 未涵蓋第 80 條、第 84 條（非防火構造建築物）。
- 第 83 條區劃已納入（見 §2.5），面積規則 `tw-bcr-83-area` 已加入規則集（§5.3）。但工具不會替使用者切出第 83 條的區劃：`isCompartmentBoundary` 是純幾何判定（牆是否躺在 Area 邊界上），十一層以上沒有切出 100／200／500 ㎡ 區劃的模型會少算交接點，同時在區劃面積這一項判 `Fail`，不會誤報為符合。
- 第 83 條第五款「防火門窗等防火設備應具有一小時以上之阻熱性」不檢討：阻熱性沒有白名單欄位也沒有參數，`防火檢討_設計防火保護` 只答「是不是防火門窗」。第 83 條的牆壁一小時時效與開口防火門窗仍由第 79 條的三條規則涵蓋（不分區劃來源）。
- 第 83 條的Ｈ–２組但書以 `building.use == "H-2"` 逐字比對，但**寫法差異已不再影響判定**：`Ｈ－２`（全角）、`Ｈ–２組`、`H-2 組`、`Ｈ類第二組`、`H2` 等寫法都會在 `ReviewInputAssembler` 折成 `H-2`，折不成組別的文字一律原封不動照舊逐字比對（見 [`building.use` 寫法正規化](building-use-groups.md)）。**仍未解決的是**：用途類組是整棟一個值，混合用途的建築物只有一部分是Ｈ–２組時無法逐區劃區分，須由審查者自行判斷或分包檢討。
- 第 83 條「除依第七十九條之二規定之垂直區劃外」以 `zone.use` 的五個字樣（`挑空`、`昇降階梯間`、`樓梯間`、`昇降機道`、`管道間`）豁免，`tw-bcr-79-area` 已統一為同一份清單（見 [`zone.use` 用字表](zone-use-vocabulary.md)）。條文的「其他類似部分」無法列舉，清單外的用字一律不豁免。
- `tw-bcr-83-area` 的法源條文字串是「這個區劃是第 83 條的」唯一判斷依據（`FireReviewRunner.HostLegalReferences` 以子字串比對）。因此規則集標題、`tw-bcr-79-area` 的法源條文，以及任何區劃結果可能借用的文字都不得出現「第83條」三字——被擱置的區劃（`ManualReview`）與「該類別沒有規則」的結果都會拿規則集標題當法源條文。此約束有單元測試守著。
- 豎框不判定，且工具不跨越 grid line 累積連續段（見 §4.1、§4.5）。**層間帶**內的實板必須在模型中就是連續嵌板，這是**建模前置條件**而非工具限制；未整理的模型會停在 `ManualReview`，不會誤判為符合。CW-H 自決議 13 起不讀嵌板，因此不受這一條約束，它的建模前置條件是 §4.2「建模要求」。
- Link 模型中的帷幕牆依既有 `LinkGeometryPolicy` 處理，預設不檢討。
- 不檢討樓板邊緣與帷幕牆背面之層間縫隙塞火：該縫的填塞屬施工項目，模型幾何無法證明，本工具不納入判定，須由設計與監造以其他方式確認。
- 帷幕牆檢討立面的視距方向由層間帶的起訖方向決定（`起點→終點` × `Z`），不讀模型判斷哪一側是室外：位置一律只由結果證據還原（§7.1），立面因此可能從室內側看向該面，層間帶的位置與尺寸不受影響。
- 帷幕牆檢討立面在該牆已無層間帶時**不會被刪除**，只是變成空的立面。工具建的視圖可能已被使用者放進圖框，自動刪除的代價高於留下一個空視圖。
- 既有檢討立面的裁剪範圍只會被放大、不會縮小。層間帶落在範圍外等於沒有標示，但使用者縮小過的範圍在其他方向仍然保留。

## 10. 測試案例

| # | 情境 | 期望 |
| --- | --- | --- |
| 1 | 區劃牆突出帷幕牆 600 mm | CW-H `Pass` |
| 2 | 區劃牆突出 499 mm、交接處立面為玻璃 | CW-H `Fail` |
| 3 | 無突出、交接處立面為 900 mm、60 min 之實體外牆 | CW-H `NotApplicable`／`Exempt`（得免突出） |
| 4 | 無突出、同上但實體外牆只有 899 mm | CW-H `Fail`（邊界值） |
| 5 | 交接處實體外牆 60 min 900 mm，隔壁一段只有 30 min | CW-H `NotApplicable`（該段不計入，但 900 mm 已達）；反之交點所在那段 30 min 則 `Fail` |
| 6 | 交接處實體外牆 900 mm 且達時效，但牆上另開門窗 | 本版**不檢討**該門窗（§9），依 900 mm 判 `NotApplicable` |
| 7 | 無突出、交點左側 900 mm 為實體外牆、右側 0 mm | CW-H `NotApplicable`（採總和，不要求兩側各半） |
| 8 | 交接帶內同時有實體外牆與帷幕嵌板重疊 | CW-H `ManualReview`，訊息含兩者的 ElementId（建模錯誤：嵌板未刪除） |
| 8b | 承上，刪除該處嵌板後重跑 | CW-H `NotApplicable`（但書成立） |
| 8c | 交接處實體外牆與帷幕牆不共面（離 300 mm）或不平行 | CW-H `Fail`（不算立面內的外牆，§9） |
| 9 | 第 83 條區劃牆與帷幕牆交接、無突出、交接處實體外牆 900 mm 60 min | CW-H `NotApplicable`，證據 `hostLegalReference == "第83條"` |
| 10 | 層間實板 900 mm、60 min，該樓層樓地板要求 60 min | CW-V `NotApplicable`（得免突出） |
| 11 | 層間實板 900 mm、60 min，但該樓層樓地板要求 120 min（自頂層起算第 5 層以上） | CW-V `Fail`（該段不計入高度） |
| 12 | 層間實板 900 mm 但只有 30 min | CW-V `Fail` |
| 13 | 樓板外突 500 mm、層間全玻璃且嵌板無時效值 | CW-V `Pass`（突出條件已成立，不要求嵌板時效） |
| 14 | 樓板不突出、層間帶其中一片嵌板型別缺時效值 | CW-V `InsufficientData`（不得以其他片推定） |
| 15 | 嵌板類別未綁定 `防火檢討_設計防火時效` | CW-V 與 CW-O 為 `InsufficientData`；CW-H 不受影響（它讀實體外牆的型別參數，同一個參數未填／未綁時同樣為 `InsufficientData`） |
| 16 | `fireResistiveConstruction == false` | 全項 `NotApplicable` |
| 17 | 三層連跨挑空帷幕牆 | CW-V `NotApplicable`，證據載明轉第 79-2 條 |
| 18 | 曲面帷幕牆 | `ManualReview` |
| 19 | 重跑同一 Package | 標示元素被覆蓋而非重複產生 |
| 20 | 修改嵌板或交接處實體外牆的型別時效後重跑 | 舊 Run 轉 Stale，新結果反映新值 |
| 21 | 12F 區劃 90 ㎡、裝修等級 `無` | 區劃面積由 `tw-bcr-83-area` 判 `Pass`（上限 100 ㎡），該區劃的邊界牆記為第 83 條 |
| 22 | 12F 區劃 1,200 ㎡、裝修等級 `無` | `Fail`（第 79 條的 1,500 ㎡ 不會來救它） |
| 23 | 12F 區劃 480 ㎡、裝修等級 `耐燃一級含底材` | `Pass`（第三款 500 ㎡） |
| 24 | 12F 區劃、裝修等級未填 | `InsufficientData`（不論面積大小），仍記為第 83 條來源 |
| 25 | 10F 區劃 1,200 ㎡ | 由 `tw-bcr-79-area` 判 `Pass`，邊界牆記為第 79 條 |

決議 14 新增：

| # | 情境 | 期望 |
| --- | --- | --- |
| 26 | 立面為「帷幕牆 ─ 900 mm 實體外牆（60 min） ─ 帷幕牆」，區劃牆抵在實體外牆中點、無突出 | CW-H `NotApplicable`，**只有一列**，`junction.facadeWallUniqueIds` 含該實體外牆 |
| 27 | 同 26，但實體外牆只有 899 mm | CW-H `Fail`（邊界值），標示計畫塗紅該實體外牆 |
| 28 | 同 26，兩片帷幕牆與實體外牆都相接 | 擁有者為相接端點 `(X, Y)` 字典序較小的那一片；另一片不產出 CW-H |
| 29 | 同 26，但實體外牆與帷幕牆之間留 5 mm 縫 | 不產出 CW-H（沒有交接處），不是 `ManualReview` |
| 30 | 區劃牆躺在帷幕牆的定位面內（共面平行，例如立面內的實體外牆自己也是區劃邊界） | 不產出 CW-H；該牆仍以實體外牆身分供給別人的但書長度 |
| 31 | 區劃牆 28000–31500（整層）、帷幕牆與實體外牆 28450–31500、實體外牆 900 mm 60 min | CW-H `NotApplicable`（交接帶為交集 28450–31500，實體外牆涵蓋之）；`junction.placement` 的 Z 為 28450–31500 |
| 32 | 同 31，但實體外牆只做 28450–30000 | CW-H `Fail`（未涵蓋整個交接帶，該牆不計入） |
| 33 | 區劃牆只在樓板厚度內（28000–28450）與立面相鄰，帷幕牆自 28450 起 | 不產出 CW-H（交集高度為 0） |

決議 15 新增：

| # | 情境 | 期望 |
| --- | --- | --- |
| 34 | 案例 26 的立面，但該片帷幕牆的 `exteriorNormal` 給成朝室內 | 結果與案例 26 **逐欄相同**（同一個 `junction.id`、同樣一列 `NotApplicable`、同樣的 `facadeWallUniqueIds`）——定向只改法線正負 |
| 35 | 同 34，區劃牆突出室外 600 mm | CW-H `Pass`（`projectionDepth` = 600 mm，不是被夾成 0 的 `Fail`） |
| 36 | 帷幕牆兩側都有區劃（室內帷幕牆） | 法線不取反，維持輸入的正負；不因定向步驟而改變任何既有結果 |
| 37 | 帷幕牆兩側都沒有區劃 | 法線不取反，該牆的 CW-H 與 CW-O 不產出（既有行為，§9） |
| 38 | 立面跨兩個區劃，法線朝室內，三個測站中兩站在 `+n` 側找到區劃、一站兩側皆無 | 取反（多數決，`2 > 0`） |
| 39 | 定向前後的 `Start`／`End`／`Direction`／`ExteriorOffsetMm`／`LengthMm` | 完全相同（守門測試：定向不得動到位置與長度） |

決議 16 新增：

| # | 情境 | 期望 |
| --- | --- | --- |
| 40 | 一片帷幕牆上同時有實心嵌板（60 min）與玻璃嵌板（防火保護＝是），兩者都在 90 cm 帶外 | CW-O **兩列**：`:solid` 帶時效讀值、`:glazed` 帶防火保護讀值，各自只含自己那些嵌板；兩列都 `Pass`，檢討表仍是同一列「帷幕牆其他部分時效」 |
| 41 | 玻璃嵌板型別已綁定 `防火檢討_設計防火保護` 但未勾選 | CW-O 玻璃那一路 `Fail`（不是資料不足） |
| 42 | 兩片玻璃，一片未填防火保護、一片填否 | 取未填的那一片 → `InsufficientData`（先講補得起來的缺口，與時效那一路同一套順序） |
| 43 | 嵌板型別未填 `防火檢討_嵌板種類` | 自成一列 `:undeclared`，`junction.panelKind` 不供值 → `InsufficientData`，訊息指名該參數 |
| 44 | 帷幕牆上一扇窗（不帶時效）與一片 60 min 實心嵌板，都在帶外 | 窗歸玻璃那一路以防火保護作答；實心那一路仍讀到 60 min（修掉「一扇窗把整片牆拖成資料不足」的舊缺陷） |
| 45 | 玻璃那一路的三態 | 是 → `Pass`、否 → `Fail`、未設定 → `InsufficientData`，證據帶 `protection.kind` 與 `protection.parameter` |
| 46 | 未宣告種類時的訊息 | 含 `防火檢討_嵌板種類` 與「實心／玻璃」，錯誤碼 `BCR-PARAM-001` |
| 47 | 層間帶全為玻璃嵌板且都勾了防火保護、樓板不突出 | CW-V **仍不**成立但書（`continuousFireRatedHeight` 不供值）——守門測試，防火設備不是防火時效 |
| 48 | 實心嵌板填錯欄位（勾了防火保護、沒填時效）／玻璃嵌板填錯欄位（填了時效、沒勾防火保護） | 兩者都 `InsufficientData`，不因為填了「另一路」的值而通過（守門測試） |

## 11. 決議紀錄

| # | 議題 | 決議 | 落在文件何處 |
| --- | --- | --- | --- |
| 1 | 900 mm 是總和或兩側／上下各半 | **採總和 ≥ 900 mm**，不要求各半 | §3.1、§3.2 |
| 2 | 樓板不突出時的證明方式 | 嵌板**型別**必須有 `防火檢討_設計防火時效`，且須 **≥ 該樓層樓地板之要求時效**（非固定 60 min）；任一片缺值為 `InsufficientData` | §3.2「樓板不突出時的參數要求」、§6 |
| 3 | 豎框無時效定義時如何處理 | **不判定**，不綁參數、不讀值、不計入取小。工具**不跨越 grid line 累積**；層間帶的連續性由建模保證——刪除多餘 grid line 使嵌板連續。偵測到疑似多餘 grid line 時判 `ManualReview` 並指出位置。決議 13 之後這一項只約束 CW-V（CW-H 不讀嵌板，豎框與 grid line 都不再影響它） | §4.1、§4.5、§9 |
| 4 | 第 83 條區劃是否納入 | **納入**，與第 79 條共用 CW-H 判定，證據以 `hostLegalReference` 區分來源 | §2.5、§4.2、§7.2 |
| 5 | 第 83 條面積規則與第 79 條如何並存 | **優先序 20 壓在第 79 條之上**，不併成一條、也不並列同一優先序（否則結論不一致會判 Conflict）。可以這樣壓，是因為第 83 條每一階的上限都嚴於第 79 條 | §5.3 |
| 6 | 裝修等級如何表達 | 單一 Text 欄位 `zone.interiorFinish`（`無`／`耐燃一級`／`耐燃一級含底材`），不拆成兩個是非欄位——一個參數、一段門檻，且未填時是資料不足而非默認放寬 | §5.2、§5.3、§6 |
| 7 | 上下帷幕牆之間以實體牆做 90 cm 防火帶 | **已由決議 13 取代，本項作廢。** 原採認為：`continuousFireRatedLength` 由該間隔的**垂直**高度供給，且僅在交點上沒有任何嵌板、間隔由同一片帷幕牆的嵌板上下收邊、該牆貼齊填滿間隔時成立。作廢理由見決議 13 | §4.2、§12 步驟 13 |
| 13 | CW-H 的但書由誰供給時效 | **由帷幕牆立面內的實體外牆供給，嵌板完全不參與 CW-H 的時效判定**，量的是沿立面的**水平**長度。取代決議 7 | §3.1、§4.2「交接處之外牆面」、§9 |
| 14 | 防火帶怎麼建模、交點不在帷幕牆上時怎麼判 | **一律以實體牆元素取代該段帷幕牆**（帷幕牆切成兩片），不再要求保留連續帷幕牆並刪嵌板。交點落在帷幕牆定位線的延長線上、且被與該帷幕牆相接的實體外牆連續段覆蓋時，**照樣以 CW-H 判該段長度 ≥ 90 cm**；同一交接處只由相接端點座標字典序最小的帷幕牆產出一列。另外三件配套：交接帶高程改為**區劃牆 × 帷幕牆的交集**；未符合時**塗紅該實體外牆**；**躺在立面內的區劃牆不產出 CW-H** | §3.1、§4.2「交點落在實體外牆上」「建模要求」、§7.1、§9、§12 步驟 14 |
| 15 | 帷幕牆的外側法線從哪裡來 | **不採信 `wall.Orientation` 的正負，改由所屬區劃落在哪一側推定**（沿定位線三站、兩側各探一點、多數決；相同或兩側皆無則不動）。實測證實帷幕牆的 `wall.Orientation` 純由定位線方向決定、`Wall.Flipped` 改不了它，「外側法線方向要一致」因此不能當成建模要求 | §4.6、§4.2「建模要求」、§9、§10 案例 34–39、§12 步驟 15 |

| 16 | 帷幕嵌板分實心與玻璃兩種，各以什麼作答 | **實心嵌板以 `防火檢討_設計防火時效` 作答，時效比照牆體由模型尺寸推定；玻璃嵌板與帷幕牆門窗以 `防火檢討_設計防火保護` 作答（同玻璃窗）。** 種類由型別參數 `防火檢討_嵌板種類` 宣告，未宣告為 `InsufficientData`；一片帷幕牆兩種都有時 CW-O 產出兩列。CW-H、CW-V 完全不受影響——防火設備不供給 90 cm 但書 | §3.3、§3.4、§5.2、§5.3、§6、§9、§10 案例 40–48、§12 步驟 16 |

第 4 項屬解釋選擇而非條文明文（第 83 條本身未規定突出或 90 cm），若個案審查機關採狹義見解，於規則集 `appliesWhen` 排除即可，不需改程式。

第 16 項有三個層次，要分開看：**分兩路作答是實務對齊**（玻璃嵌板無法填構造時效，工具卻要求它填，
結果是整片玻璃帷幕牆永遠停在資料不足）；**以防火設備回答「半小時以上防火時效」是解釋選擇**，條文
第 79-4 條寫的是時效而非設備，若個案審查機關採狹義見解，在 `tw-bcr-79-4-curtain-wall-other-glazed`
的 `appliesWhen` 排除即可；**要求宣告種類則是拒絕猜測**——工具讀得到材料、讀得到厚度，但那些只夠拿來
提案，不足以把「未符合」與「資料不足」分開，而這兩者的處置完全不同。三者都不鬆動決議 13：CW-H 的
但書仍然只認立面內的實體外牆，玻璃嵌板在那裡照樣無法作答（§9）。

第 7 項原本與條文字面有落差，**決議 13 已解除這個落差**。舊採認的問題是：第 79 條第 3 項但書寫的是「與其交接處之外牆面**長度**有九十公分以上」，是沿外牆面量的**水平**尺寸；上下帷幕牆之間的實體牆所提供的 900 mm 卻是**垂直**尺寸（那是第 79-3 條「外牆面**高度**」的量法）。決議 13 改由立面內的實體外牆沿立面**水平**供給長度，量測方向與條文一致，不再需要這項採認。

第 14 項不是解釋選擇，是**把實作對齊實務建模**：防火帶是實體構造，模型裡就該是一片牆，決議 13 的實作
卻只認得「交點落在帷幕牆自己的範圍內」，逼得使用者要嘛保留一片假的連續帷幕牆、要嘛接受誤判。條文對
「交接處之外牆面」既沒有要求那一段必須是帷幕牆元素，也沒有要求它必須在某一片帷幕牆的長度範圍內——
要求的是那一段外牆面有 90 cm 以上且具同等時效。第 14 項的高程交集同理：條文問的是**帷幕外牆面**，
交集之外的樓板邊緣本來就由第 79 條之 3 回答，拿它當 CW-H 的門檻只會逼建模配合工具。

第 15 項既不是解釋選擇也不是實務對齊，是**修掉一個讀錯模型的 bug**，只是修法有兩條路、必須選一條。
`wall.Orientation` 對帷幕牆而言不帶「哪邊是室外」的資訊（實測見 §4.6），所以另一條路——要求使用者把
定位線往特定的旋轉方向畫——等於把工具的實作細節寫成建模義務，而且那是使用者在 Revit UI 裡看不到、
按空白鍵也改不掉的東西。改由所屬區劃推定則是拿本功能已經在用的同一份資訊（`ZoneOf` 的區劃探測）
回答同一個問題，不新增任何輸入。代價是兩側都有區劃時無法作答，記在 §9。

第 13 項本身也是解釋選擇，方向相反——它**收緊**而非放寬：條文但書的主詞是「該外牆構造」，本工具據此認定只有帷幕牆立面內的實體外牆能作答，帷幕嵌板（含認證防火玻璃）與抵到立面的區劃牆本身都不能。收緊的理由是，若容許以區劃牆自身的時效作答，任何具時效的區劃牆抵上玻璃帷幕牆都會自動合格，第 79 條第 4 項即形同虛設。代價記在 §9：以認證防火玻璃達成但書的專案會被判 `Fail`。

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
| 7 | 第 83 條面積規則 `tw-bcr-83-area` 與 `zone.interiorFinish`，規則集版本升至 `2026.4-provisional` | **已完成** |
| 8 | `防火檢討_室內裝修等級` 進批次參數面板的「區劃」分頁，「適用上限」欄改為兩條規則共用的 `ZoneAreaLimit` | **已完成**（Revit 端待實機驗證） |
| 9 | 取消 Area 人工輸入；由區劃內牆／天花板類型的耐燃等級彙總第 83 條輸入，最弱者控制、缺值不猜測 | **已完成**（取代步驟 8 的輸入方式，Revit 端待實機驗證） |
| 10 | 未符合交接的**檢討圖號** `CW-H-01`／`CW-V-01`：檢討表列、平面標註、自動產生的帷幕牆立面名稱三者共用 | **已完成**（Revit 端待實機驗證） |
| 11 | 決議 7：上下帷幕牆之間的實體牆防火帶（`CurtainWallJunctionResolver.SolidWallBand`、`CompartmentWallObservation.ProvidedFireRating`） | **已移除**（決議 13 取代，見第 13 列）。程式碼與其測試都已隨步驟 13 刪除 |
| 12 | 交點落在豎框上時取該格的嵌板（`CurtainWallJunctionResolver.PanelLookup`） | **已移除**（曾實機驗證成立：`CW-H-01` 由「未符合／0 m」轉為「資料不足」，`panelCount` 0 → 1；但隨決議 13 一併作廢——CW-H 不再查嵌板，豎框就不再擋路） |
| 13 | CW-H 改由立面內的實體外牆供給但書長度（決議 13）：新增 `FacadeWallObservation`／`CurtainWallObservation.IsInFacadePlane`／`FacadeRun`，移除 `SolidWallBand`、`PanelLookup` 與 `WallJunction` 的嵌板量測路徑 | **已完成並實機驗證**（建置 0 警告、1620 條測試全通過；2026-09-27 於 Revit 重跑，`CW-H-01` 由「資料不足」轉為 `Fail`／0 m，七項證據逐項相符）。但書成立的路徑（`NotApplicable`）當時未驗到，使用者依 §4.2 建模要求改立面後，已隨步驟 14、15 於 2026-09-28 驗到 |
| 14 | 交點落在實體外牆連續段上也判 CW-H（決議 14）：交點求解擴充到延長線、交接帶高程改為交集、未符合時塗紅實體外牆、躺在立面內的區劃牆不產出 CW-H | **已完成並實機驗證**（建置 0 警告、1637 條測試全通過；§10 案例 26–33 已釘住，含整合層的「一列且 `NotApplicable`」）。2026-09-28 與步驟 15 一併部署後重跑，使用者確認 CW-H 檢討結果無誤 |
| 15 | 外側法線改由所屬區劃推定（決議 15）：`CurtainWallJunctionResolver` 新增定向步驟，`§4.6` 的三站多數決 | **已完成並實機驗證**（建置 0 警告、1637 條測試全通過；§10 案例 34–39 已釘住，含守門測試）。2026-09-28 與步驟 14 一併部署後重跑，使用者確認 CW-H 檢討結果無誤 |
| 16a | 決議 16 的**判定層**：`CurtainPanelKind`／`CurtainPanelKinds`、`CurtainPanelObservation.Kind`、`junction.panelKind` 與 `junction.minFireProtection` 兩個白名單欄位、`CurtainWallJunctionResolver` 把 CW-O 拆三路、`CurtainWallJunctionCheck` 寫入兩個新 fact、規則集加 `tw-bcr-79-4-curtain-wall-other-glazed`（規則集版本升至 `2026.7-provisional`） | **已完成**（建置 0 警告、1650 條測試全通過；§10 案例 40–48 已釘住）。Revit 端尚未接線 |
| 16b | 決議 16 的**參數層**：`FireRatingDeriver` 支援 `CandidateCategory.CurtainPanel`（沿用牆壁門檻）、`FireReviewTypeRow` 的 `PanelKind` 欄位與 `CarriesRating`／`CarriesProtection` 改看種類、共享參數檔加 `防火檢討_嵌板種類`（`…0013`） | **已完成**（建置 0 警告、1672 條測試全通過）。Revit 端尚未接線 |
| 16c | 決議 16 的**Revit 讀取層**：`RevitCurtainWallGeometryReader.ReadPanels` 分類並一律讀防火保護、`RevitFireReviewTypeScanner` 讀嵌板厚度與種類（含材料提案）、`fire-review-openings-type.txt` 加 `…0013` | **已完成**（建置 0 警告、1707 條測試全通過）。**Revit 端已於 2026-09-28 實機驗證**：型別 12611（`玻璃 1.0cm`、材料 `玻璃`）由材料提案出「玻璃」並顯示在面板上，寫入後模型讀回 `防火檢討_嵌板種類 = 玻璃`，CW-O 因此走 `tw-bcr-79-4-curtain-wall-other-glazed` 作答；`RevitFireReviewParameterWriter` 早就寫得到（依名稱查參數、`FireReviewEditKind.Text`），不必改。`FamilySymbol`（訂製嵌板族）那一條讀取路徑這個模型驗不到，仍未驗證 |
| 16d | 決議 16 的**參數面板**：帷幕嵌板列的種類下拉、選實心才亮結構材料與推定時效、選玻璃只亮防火保護、提案看得出是提案 | **已完成**（建置 0 警告、1707 條測試全通過）。**Revit 端已於 2026-09-28 實機驗證**：使用者開「防火檢討參數設定」→「構件類型」分頁核對後回報「面板沒問題」——**這是一句整體確認，沒有逐欄基線**，六個繫結、黃底提案、種類連動亮暗各自是否逐一無誤並未分項記錄。面板是 net48 WPF，核心測試專案不參照它，**這一段仍然沒有任何自動化測試蓋得到**，XAML 的繫結路徑也不由編譯器檢查 |
| 16e | 決議 16 的部署與實機驗證：依下方「16c 的綁定程序」綁參數、填值、重跑，核對 CW-O 由 `InsufficientData` 轉為有判定 | **已完成**（2026-09-28）。部署、綁定、面板核對、填值寫入、重跑全部做完，CW-O 玻璃那一路的檢討表列已逐字核對；證據與剩餘界線見下方「16e 的驗證證據」 |

#### 16e 的驗證證據（誠實記錄，含推定的那一段）

**部署**：2026-09-28 03:12 以 `scripts/install-revit-2024.ps1` 換版（`Application.dll` 658,432 → 668,160 bytes），
03:17 重開 Revit，03:38 的檢討執行跑的是新版。

**綁定**：兩次 `load_shared_parameters`（`categories: ["CurtainPanels"]`、`bindToInstance: false`）全部
`Success: true`、`TotalFailed: 0`；`防火檢討_嵌板種類` 新綁，`結構材料`／`防火被覆厚度` 擴充綁定至新品類。

**面板**：使用者核對後回報「面板沒問題」——整體確認，無逐欄基線（見 16d 列）。

**寫入與讀回（機器證據，非人眼）**：`get_element_info 12611` 讀回
`防火檢討_嵌板種類 = 玻璃`、`防火檢討_設計防火保護 = Yes`、`防火檢討_設計防火時效` 空白——提案值確實寫進
模型，且玻璃那一路不佔用時效欄。型別 298048（`Wall`、材料 `<By Category>`）讀不到材料因此不提案，
`防火檢討_嵌板種類` 整格不存在，留白行為正確。順帶證實：`get_element_info` **只列有值的參數**，
所以綁了但空白的參數不出現在它的清單裡，不能用它判斷綁定成功與否。

**CW-O 的結論是推定出來的，不是讀到 CW-O 那一列**：檢討結果存在 Extensible Storage，revit-mcp 讀不到，
檢討表也是 WPF 視窗。日誌只給出整個檢討項的合計 `帷幕牆區劃交接 符合（符合 28／未符合 0／待確認 0／不適用 6）`。
推定的三個前提是：(1) `ReviewTable` 的 `待確認` = `InsufficientData + ManualReview + NotRun`，所以 0 代表
34 筆裡一筆資料不足都沒有；(2) `OtherPanelJunctions` 的 undeclared 那一列必然產生 `InsufficientData`
（`junction.panelKind` 缺值），因此該列不存在；(3) `rest = wall.Panels.Where(p => !covered.Any(b => b.Covers(p)))`
且 `PanelBand.Covers` 是中點規則——帷幕牆 282773 下排嵌板中點 Z≈29715 遠高於 900 mm 帶（28000–28900），
所以 CW-O 必然有列。三者合起來得到「CW-O 已由 `InsufficientData` 轉為 `符合`」。

**CW-O 那一列已逐字核對（2026-09-28，使用者貼出檢討表原文）**：這一列答的是帷幕牆
`…-00045089`（ElementId 282761）的玻璃那一路，狀態 `符合`、實際值與規定值同為 `是`。與
`fire-review-rules.json` 的 `tw-bcr-79-4-curtain-wall-other-glazed` 逐項對得上——`版本 1`、
`legalReference` 含「（玻璃嵌板以認可之防火設備作答，解釋選擇見帷幕牆規格決議 16）」全文、
`appliesWhen` 要求的 `junction.panelKind == "Glazed"` 在證據裡是 `Glazed`、`requiredValue` 為
`junction.minFireProtection == "是"` 而說明寫「`junction.minFireProtection = 「是」`，符合 == 「是」」、
四個 `evidenceFields`（`curtainWallUniqueId`／`zoneId`／`panelKind`／`minFireProtection`）全部出現、
`junction.id` 為 `CW-O:…-00045089:glazed`（符合 `CurtainWallJunctionResolver` 的 `GlazedSuffix`）。
證據另含 `protection.kind = Yes`、`protection.parameter = 防火檢討_設計防火保護`、`protection.raw = 1`，
證實玻璃那一路讀的確實是防火保護而不是時效——這是決議 16 判定路徑在 Revit 端的第一次逐字證據。

**但推定前提 (3) 的具體算例仍未驗證。** 貼出的這一列是 282761，不是前提 (3) 算的 282773。282761 共
14 片嵌板（2 列 × 7 行，`get_curtain_wall_info`），CW-O 只數了 7 片（`junction.panelCount = 7`，
284158–284164），另外 7 片確實被交接帶扣掉了——這反而證明中點規則的扣除是活的，但也表示「282773 下排
嵌板中點遠高於 900 mm 帶、因此一片都不扣」這個算例沒有被這一列證實。帷幕牆 282773 是否另有一列 CW-O、
其 `panelCount` 是多少，尚未核對。**CW-H 與 CW-V 兩列同樣仍未逐字核對**（步驟 14、15 的實機驗證是
使用者一句「CW-H 檢討無誤」的整體確認，沒有逐欄基線）。

**一併驗到的既有缺陷（不屬於決議 16，未修）**：`RevitCurtainWallGeometryReader.cs:285` 的警告
「帷幕嵌板（Id X）的尺寸無法讀取，該處的交接帶將判為資料不足。」把後果說得比實際嚴重——程式碼是
`continue`（略過該片嵌板），並不會把任何東西標成資料不足。這次三片觸發警告的嵌板（284298、284309、
284310，都在帷幕牆 282773 上）經 `get_solid_triangulation` 證實 `solidCount: 0`、`faceCount: 0`，是被
合併掉的空嵌板槽（鄰居 284296 寬 2970 mm、10 個面的 L 形，吞掉了兩格），略過是正確處理，該處由合併後的
鄰居覆蓋，所以 `待確認` 是 0。這段警告自步驟 4（`5abf1ed`）就存在，改它是一個獨立決定。

#### 16c 的綁定程序（16e 要執行的那一段）

Curtain Panels 需要**兩次** `load_shared_parameters`，兩次都 `categories: ["CurtainPanels"]`、
`bindToInstance: false`：

| 檔案 | 綁過去的參數 | 為什麼 |
| --- | --- | --- |
| `assets/SharedParameters/fire-review-openings-type.txt` | `防火檢討_設計防火保護`（`…000d`）、`防火檢討_遮煙性能`（`…000f`）、`防火檢討_嵌板種類`（`…0013`）、`防火檢討_設計防火時效`（`…0008`） | 前三個是玻璃那一路與昇降機道出入口要答的，最後一個是實心那一路與 CW-O 的輸入 |
| `assets/SharedParameters/fire-review-members-type.txt` | `結構材料`（`…000b`）、`防火被覆厚度`（`…000c`） | 實心嵌板的時效由厚度＋材料推定（決議 16），這兩個原本只綁主要構造 |

`…0008` 在兩個檔裡都有，第二次會回報「已存在相符綁定，跳過」。**不必新增第三個檔**：構件檔本來就
只有這三個參數，把它對 Curtain Panels 再跑一次剛好補上缺的兩個，而且不會讓門窗拿到 `結構材料`。
反過來，`fire-review-openings-type.txt` 綁到門或窗時會把 `防火檢討_嵌板種類` 一起綁上——對門窗無害
（種類由類別認定，`RevitFireReviewTypeScanner` 的 `PanelKind` 旗標由類別把關，面板不會在門窗列上顯示
它），只是 Revit 的性質面板上會多一格空欄位。前置檢查也不會因為多綁而抱怨：`ReviewReadiness.Parameters`
只查「有沒有綁到需要的類別」，不查有沒有綁到別的類別。

### 步驟 9：室內裝修等級改由模型推導

- `防火檢討_室內裝修等級` 改為綁在**牆類型與天花板類型**，不再綁 Area 實體。
- 每個區劃讀取與其有空間關係的牆，以及同樓層、中心點位於 Area 邊界內的天花板。
- 三階值仍為 `無`／`耐燃一級`／`耐燃一級含底材`；整個區劃以最弱的牆面或天花板系統為準。
- 任何必要表面缺參數、未填或值不在白名單內，`zone.interiorFinish` 不供值，規則結果為 `InsufficientData`，不得自行放寬。
- 批次參數面板移除 Area 的「室內裝修等級」欄、批次填入與寫回；第 11 層以上的「適用上限」在真正檢討前顯示需由模型推導。
- 規則集版本升為 `2026.5-provisional`，使既有人工 Area 輸入產生的結果失效並要求重跑。

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

### 步驟 8 的產出與驗證

把 `防火檢討_室內裝修等級` 接進批次參數面板的「區劃」分頁，使用者不必再到 Revit 的屬性面板逐一選 Area。

改動的檔案：

- `src/BuildingRegulationReview.Application/Parameters/ZoneAreaLimits.cs`（新檔）：
  - `InteriorFinishGrades`：第 83 條第一至三款的三個值（`無`／`耐燃一級`／`耐燃一級含底材`），
    與 `tw-bcr-83-area` 的比對字串逐字相同。面板因此能以**下拉選單**取代自由文字——規則把不認得的
    字當成「沒做到放寬條件」而退回第一款的 100 ㎡（§11.3），打錯字不會報錯，只會悄悄變嚴。
  - `ZoneAreaLimit.For(floorNumber, sprinklered, interiorFinish, buildingUse)`：面板顯示用的上限，
    **重述**（不是取代）`tw-bcr-79-area` 與 `tw-bcr-83-area`，包含「哪個欄位沒填就答不出來」這件事——
    樓層序決定由哪一條作答，所以它沒填時不再往下問；第三款不讀用途類組，所以那一階也不問它。
- `src/BuildingRegulationReview.Application/Parameters/FireReviewInputRows.cs`：`FireReviewZoneRow`
  新增 `InteriorFinish`、`FireReviewZoneParameters.InteriorFinish = 8` 與 `MissingParameters` 一筆；
  `FireReviewParameterSet.ZonesMissingInteriorFinish` 只列**樓層序 ≥ 11 且等級未填**的區劃
  （十層以下的區劃根本不會讀到這個值，樓層序未填的則由既有的提示負責）。
- `src/BuildingRegulationReview.Revit/Parameters/RevitFireReviewTypeScanner.cs`：`Zones()` 讀取這個
  參數的存在與值。
- `src/BuildingRegulationReview/FireReview/FireReviewInputViewModels.cs`：新增 `InteriorFinish`、
  `InteriorFinishChoices`（三個值加上「這個 Area 目前存著的不認得值」，否則 ComboBox 顯示不出
  舊值，還會在繫結時把它清成空白）、由面板推入的 `BuildingUse`，`LimitText` 改讀 `ZoneAreaLimit`。
- `src/BuildingRegulationReview/FireReview/FireReviewParameterPanelWindow.xaml(.cs)`：「區劃」分頁多一欄
  下拉、「區劃批次填入…」多一欄、狀態列多一項「待填裝修等級 N 個十一層以上區劃」，並在專案資訊分頁的
  `建築物用途類組` 變動時把值推給每一列（`PushBuildingUse`），讓上限即時反映Ｈ–２組但書。

「適用上限」欄因此從 `上限 3000 m²` 變成會講條號、也會講缺什麼：`第83條 上限 400 m²`、
`第79條 上限 1500 m²`、`未填裝修等級、用途類組、滅火設備，無法判定上限`、
`第83條 上限 100 m²（裝修等級非放寬條件）`。

新增 37 個測試（`Parameters/ZoneAreaLimitTests.cs` 與 `Parameters/FireReviewInputRowTests.cs`）。
關鍵的一組是**與出貨規則交叉比對**：同一組輸入分別交給 `ZoneAreaLimit` 與 `RuleEngine`，斷言面板顯示的
數字就是規則要求的數字，而且面板說「未填」的欄位就是引擎會判 `InsufficientData` 的欄位——面板在 C# 裡
重述規則，唯一會壞的方式就是兩邊講的不一樣，所以那件事要有測試守著。全套 **1269 個測試通過**，
`BuildingRegulationReview.sln` 與 WPF 外掛專案皆 0 警告 0 錯誤。

桌面上那份 `防火檢討_Revit參數設定清單.md` 的附錄 I 已改寫成面板的操作說明（原本寫「尚未進面板」）。

### 步驟 10 的產出與驗證

§7.1「檢討圖號」的實作。未符合的 CW-H／CW-V 從檢討表的一列走到圖面之前少了一個參照，這一步把它補上。

- `src/BuildingRegulationReview.Application/Reviews/CurtainWallMarkNumbers.cs`（新檔）：`Assign(ReviewTable)`
  回傳「結果 ID → 圖號」，`Of`、`Format`、`Join`、`Prefix`。編號只看檢討表，所以檢討視窗不必建標示
  計畫就能顯示同一組號碼。
- `CurtainWallReviewMarks.cs`：`PlannedReviewNote.Number`、`PlannedSpandrelBand.Number`；標註文字改以
  圖號開頭，CW-V 另加「（詳見立面 {圖號}）」；`BandSignature` 納入圖號，**號碼變了就是變了**，
  否則重新編號之後立面名稱會繼續指著舊號碼卻判定為 `Unchanged`。
- `ReviewMarkup.cs`：`ReviewMarkupPlan.Numbers`；`PlanJunction` 取號碼、略過訊息（`Subject`）帶上號碼。
- `ReviewOutputNaming.CurtainWallElevation(reviewViewName, curtainWallLabel, markNumbers)`：多一個可選的
  圖號段，不給就是原本的名字，既有模型裡的立面照樣認得。
- `RevitReviewViewMarker.cs`：立面名稱由**這一次計畫的全部層間帶**算出（不是只有變動的那些）；
  新增 `EnsureNumberedName`，只有在視圖名稱仍等於擁有權標記存的簽章時才改名，使用者改過名就只回報；
  層間帶填滿區域的 `Comments` 寫入圖號。
- `FireReviewWindow.cs`：清單列與明細顯示「檢討圖號」，號碼來自 `CurtainWallMarkNumbers.Assign(_table)`。

新增 5 個測試（`Reviews/ReviewMarkupTests.cs`），其中兩個是這一步的關鍵：
**`The_number_the_table_shows_is_the_number_the_marks_carry`** 斷言視窗那條路徑（只有檢討表）與標示計畫
那條路徑算出完全相同的號碼——兩邊各算一次，唯一會壞的方式就是講得不一樣；
**`Two_spandrels_of_one_wall_differ_by_their_number_alone_so_a_renumber_is_a_change`** 用同一片牆、同一個
矩形、不同號碼的兩條層間帶，守住簽章必須納入圖號這件事。全套 **1278 個測試通過**，
`BuildingRegulationReview.sln` 與 WPF 外掛專案皆 0 警告 0 錯誤。

**Revit 端待驗證**：立面名稱是否為 `{檢討視圖}_{圖號}_{帷幕牆}_帷幕牆立面`；同一片牆多層層間帶時
名稱是否列出至多 3 個號碼、超過寫「等 N 處」；修好其中一處後重跑，號碼遞補且立面自動改名；
把立面手動改名後重跑，名稱不被改回、結果清單出現「視圖名稱已由使用者變更」的說明。

### 步驟 11 的產出與驗證

決議 7 的實作（§4.2「實體牆防火帶」、§11 決議 7）。幾何層與規則層見 commit `4b248cf`，
限制與建模前置條件見 §9。

**第一次實機執行就不成立，原因不在決議 7 的判定式，在讀取範圍。** 使用者模型的 `CW-H-01`
（帷幕牆 `cbf3353e…0004508b`、區劃牆 `3310ec05…00048a5f`）：

```
狀態：未符合          實際值 0 m，規定值 0.5 m
junction.projectionDepth            = 0 Meter
junction.continuousFireRatedLength  = 0 Meter
junction.hostRequiredFireRating     = 60 Minute
junction.panelCount                 = 0 Count
junction.placement                  = 10, 4.79071, 10, 4.79071, 27.55, 28.45
```

三件事把病因夾死，不必進 Revit：

1. `panelCount = 0` → `rows.Count == 0`，所以 `SolidWallBand` 確實被呼叫了。
2. 證據裡**沒有 `junction.minFireRating`**。只要 `SolidWallBand` 讀到了該牆型別的
   `防火檢討_設計防火時效`（不論夠不夠），`MinFireRating` 就非 null 而一定產生那一列
   （`CurtainWallJunctionCheck.cs` 的 `MinRatingField`）。它不在，就代表回的是
   `RunMeasurement.Nothing`——**卡在幾何守門，不是時效參數沒填**。
3. `placement` 的高程範圍 27.55–28.45 恰好是 900 mm，而 `ReadPanels` 的讀取範圍是
   `(樓層標高 − MinFireRatedRunMm, 次一樓層標高 + MinFireRatedRunMm)`，**外擴量也是 900 mm**。
   一道貼齊樓層底面的 900 mm 帶，其下方收邊嵌板的頂端因此正好等於 `storey.Bottom`，
   被 `box.Max.Z <= storey.Bottom` 丟掉 → `below is null` → `Nothing` → 判 `Fail`。

也就是說：**外擴量與防火帶高度是同一個數，使得「帶貼齊樓層」這個最常見的建法必然踩在
讀取範圍的邊界上**，而那是一個浮點數等值比較，成立與否取決於進位。決議 7 在修掉這一點
之前於實務模型上等於失效。

- `RevitCurtainWallGeometryReader.ReadPanels`：讀取範圍的邊界判斷改帶 `SnapFeet`（1 mm）公差，
  貼齊邊界的嵌板留下。只影響「原本恰好落在邊界上」的嵌板，其餘一字未動。

**Revit 端待驗證**（本輪尚未驗證，MCP 連線持續回 409）：重新部署後 `CW-H-01` 是否由「未符合」
轉為「得免突出」；若轉為「資料不足」，則是該防火帶牆的**型別**未填 `防火檢討_設計防火時效`；
若仍為「未符合」且證據出現 `junction.minFireRating`，則是時效低於 `hostRequiredFireRating`（60 min）；
若仍為「未符合」且證據**仍無** `junction.minFireRating`，則守門的另外兩項不成立——上下嵌板未貼齊
牆的上下緣（容差 0.5 mm），要回報那一柱下方嵌板的頂端高程與上方嵌板的底端高程。
另依 §9 以「帶在樓層底部」與「帶在樓層頂部」兩種模型各跑一次。

### 步驟 12 的產出與驗證

步驟 11 的讀取範圍公差修正部署後（部署時間晚於修正 commit，已核對時間戳），`CW-H-01` **仍判「未符合」
且證據仍無 `junction.minFireRating`**，即落入步驟 11 所列的第四個分支。這一輪 `revit-mcp` 恢復連線，
直接量到實機幾何，結論與步驟 11 的假設**完全不同**：

| 元素 | 實機幾何 |
| --- | --- |
| 帷幕牆 `282763`（`…-0004508b`） | X = 10000，Y 4790.71 → 7933.57，**Z 24500–28000**，2 列 × 3 行共 6 片嵌板**全部存在** |
| 交點所在嵌板柱 | 沿牆 **30 – 1485 mm**；Z 24530–27000 與 27030–27970 |
| 區劃牆 `297567`（`…-00048a5f`） | RC 牆 15cm，X 0 → 10000 @ Y = 4790.71，**Z 27550–28450（僅 900 mm 高）**，與帷幕牆**垂直** |
| 交點 | (10000, 4790.71)＝帷幕牆中心線**起點** → `u` = 0 |

步驟 11 的三個假設全部不成立：嵌板**沒有**被刪（6 片全在）；防火帶**沒有**貼齊樓層（FL9 = 28000，帶是
27550–28450，跨在樓板上下各 450 mm，讀取範圍 27100–32400 兩側嵌板都在範圍內）；帷幕牆也不是整棟通高
而是**逐層建**（已核對 `282765` = 24500–28000、`282789` = 31500–35000）。

真正的病因有兩個，互相獨立，各自都足以讓這個交接處判 `Fail`：

1. **交點落在端部豎框上**（本輪已修，見 §4.2「交點落在豎框上」）。`u` = 0，而嵌板沿牆自 30 mm 起算
   （端部收邊豎框半寬），`CoversAlong` 的公差只有 `TouchToleranceMm` = 0.5 mm → 嵌板柱是空集合 →
   嵌板列空（因此走了決議 7）、`gapBottom`／`gapTop` 也都 null → `Nothing` → 供給 0 → `Fail`。
   **這是與決議 7 無關的通用缺陷**：任何區劃牆抵在帷幕牆端點、或對齊任一支豎框的交接處都會這樣，
   而區劃牆對齊豎框是實務常態做法。
2. **帷幕牆逐層建，決議 7 結構性不成立**（本輪未修，已改寫進 §9 第三項）。900 mm 帶跨在 FL9 樓板上，
   必然與下層（24500–28000）與上層（28000–31500）**各重疊 450 mm**，於是產生兩個 CW-H 交接處，
   每一個都只看得到自己那一片帷幕牆，另一側的收邊嵌板永遠不在 `wall.Panels` 裡。原 §9 第三項只寫到
   「兩片獨立帷幕牆連交接處都不會產生」，**漏掉了這個跨樓板的常態建法**——它會產生交接處，而且兩個都 `Fail`。

另經使用者確認：那道 900 mm 高的 RC 牆是**刻意建的決議 7 測試件**，但建法不符 §4.2——決議 7 的防火帶是
「填在**帷幕牆平面內**、上下段之間的實體牆」，該牆與帷幕牆垂直，且與交點所在的上排嵌板（27030–27970）
在高程上重疊 420 mm，另落入 §9 第一項。使用者選擇**只修病因 1**，病因 2 的放寬（跨帷幕牆元素找收邊嵌板）
列為未決議的設計擴張。

順帶記錄：`CW-H-01` 判「未符合」在法律上其實是對的（交點處帷幕牆為無時效玻璃、區劃牆突出 0），
錯的是證據把理由講成了「什麼都讀不到」。

#### 這一輪的診斷法（可重用）

實機不必逐一猜測，三步就能夾死一個 CW-H 為什麼不成立：

1. `junction.panelCount = 0` → 嵌板列為空 → 走的是決議 7 的實體牆路徑。
2. 證據**有沒有** `junction.minFireRating`：只要 `MinRating` 非 null 就一定產生那一列
   （`CurtainWallJunctionCheck` 的 `MinRatingField`）。它不在 → 回的是 `RunMeasurement.Nothing` →
   **卡在幾何守門，不是時效參數沒填**。
3. 用 `get_element_geometry` 量三樣東西，對照 §4.2 的守門逐項核對：**帷幕牆的 Z 範圍**（決定它是通高
   還是逐層建）、**交點所在嵌板柱的沿牆範圍**（決定交點是否落在豎框上）、**區劃牆的 Z 範圍**。
   交點位置由 `junction.placement` 的前四個數字給出，與帷幕牆中心線起點比對即知 `u`。

#### 修改的檔案

- `src/BuildingRegulationReview.Application/Candidates/CurtainWallJunctionResolver.cs`：新增
  `PanelLookup`，`WallJunction` 的嵌板列篩選、`Measure` 與 `SolidWallBand` 都改用它回的查詢位置；
  交接處自己的位置（`placement`、所屬區劃、供 CW-O 扣除的已涵蓋帶）一律沿用真實交點。
- `docs/regulations/curtain-wall-fire-compartment.md`：§4.2 新增「交點落在豎框上」；§9 第三項改寫。
- `tests/.../Candidates/CurtainWallJunctionResolverTests.cs`：新增兩條測試——
  `A_junction_on_the_end_mullion_measures_the_panel_beside_it_rather_than_no_panel_at_all`（已確認
  在修正前紅：實得 0、期望 2940）與其守門
  `A_column_the_curtain_wall_really_left_unpanelled_does_not_borrow_the_next_column_s_panel`。

**Revit 端待驗證**：重新部署後 `CW-H-01` 應由「未符合／實際值 0 m」轉為**「資料不足」**——查詢位置移到
嵌板 `284178` 上後改走嵌板路徑，而該片是 `玻璃 1.0cm`、型別未填 `防火檢討_設計防火時效`，依輸入契約
withhold 不供給。證據應**出現** `junction.minFireRating` 且 `junction.panelCount` 不再是 0。
若要讓這個測試件真的判「得免突出」，須依 §4.2 重建：上下帷幕牆合成**一片**元素、中間那一柱不鋪嵌板、
實體牆置於**帷幕牆平面內**且上下緣與上下嵌板貼齊（容差 0.5 mm、不得重疊）。

### 步驟 13：CW-H 改由立面內的實體外牆供給（決議 13）

步驟 12 的修正**實機驗證通過**：`CW-H-01` 由「未符合／實際值 0 m」轉為「資料不足」，`junction.panelCount`
由 0 變 1、`junction.panels` 指向嵌板 `284178`，withhold 的理由是 `provided.parameter = 防火檢討_設計防火時效`
未填。修正成立。

但驗證過程暴露了更上游的問題：`玻璃 1.0cm`（Type Id `12611`）**根本沒有 `防火檢討_設計防火時效` 這個參數**
——共用參數未綁定至 Curtain Panels。而使用者據此指出：**帷幕牆本身依法不需要防火時效，達成但書的方式
是用實體牆建模**。此見解與條文一致（但書主詞是「該外牆構造」），因此推翻決議 7、確立決議 13。

#### 實機幾何（`revit-mcp` 實測，非推定）

| 元素 | 型別 | 幾何 |
| --- | --- | --- |
| `282763` | 帷幕牆-150x250cm | X = 10000 立面，Y 4790.71 → 7933.57，**Z 24500 – 28000**；2 列 × 3 行 = 6 片，全為 `玻璃 1.0cm` |
| `282775` | 帷幕牆-150x250cm | 同一立面，Y 4790.71 → 8219.28，**Z 28000 – 31500**；同樣 6 片全玻璃 |
| `297567` | RC 牆 15cm | X 0 → 10000（沿 X 向），Y 中心 4790.71，**Z 27550 – 28450**（900 mm） |

關鍵發現：`282763` 的頂 = `282775` 的底 = **28000**，**兩片帷幕牆直接相接，中間沒有任何間隔**，兩片都
鋪滿嵌板。那道 900 mm 的 RC 牆 `297567` **就是區劃牆本身**——沿 X 方向抵到 X = 10000，**不在帷幕牆的
定位面內**，且與 `282763` 頂排嵌板（27030 – 27970）在高程上重疊 420 mm。

因此「上下帷幕牆之間有 90 cm 間隔、中間建了實體牆」這個狀態**在模型裡不存在**；決議 7 判不出防火帶不是
程式漏判，是立面從 24500 到 31500 全部是玻璃。這也再次說明決議 7 的建模前置條件在實務上難以成立。

#### 使用者確認的兩件事（重要，不要重問）

- **CW-H 的實牆認定採「立面內的實牆」**，不採「區劃牆抵到即可」。後者會讓第 79 條第 4 項形同虛設。
- **CW-O（第 79-4 條，其餘嵌板 ≥ 30 min）保留**，不移除也不預設停用。因此 `防火檢討_設計防火時效`
  **仍須綁定至 Curtain Panels**——那是 CW-O 的輸入，與 CW-H 無關。

#### 待辦的綁定（決議 13 當時的紀錄）

> 這一段是決議 13 當時寫下的三個參數。下面「參數綁定：已確認早已綁定（實機查證）」證實它們早就綁好了，
> 而決議 16 之後 Curtain Panels 還要多綁三個（`防火檢討_嵌板種類`、`結構材料`、`防火被覆厚度`）——
> **要照做的是 §12「16c 的綁定程序」那兩次呼叫，不是這一段。**

`fire-review-openings-type.txt` 的三個參數須以 **Type** 層級綁到 CurtainPanels，GUID 與構件檔同一組：

| 參數 | GUID | 用途 |
| --- | --- | --- |
| `防火檢討_設計防火時效` | `bcf10001-0000-4a00-9b00-000000000008` | CW-O（`ReviewInputSources.cs:132`） |
| `防火檢討_設計防火保護` | `bcf10001-0000-4a00-9b00-00000000000d` | 可開啟嵌板視為防火門窗（`:120`） |
| `防火檢討_遮煙性能` | `bcf10001-0000-4a00-9b00-00000000000f` | 昇降機道出入口可能是帷幕嵌板（`:150`） |

三者正好是 `ReviewInputSources` 期望的清單，不會產生 `UnexpectedCategoryBinding`。`RevitFireReviewParameterWriter`
**不會自動建立綁定**（`RevitFireReviewParameterWriter.cs:101-106` 只回報「請先在管理 > 專案參數綁定」），
所以這一步必須另外做——手動，或用 `revit-mcp` 的 `load_shared_parameters`（`categories: ["CurtainPanels"]`、
`bindToInstance: false`）。

#### 產出（已完成，0 警告、1620 測試全通過）

1. **讀取層**：新增 `FacadeWallObservation`（`UniqueId`、`Start`、`End`、`BottomElevationMm`、
   `TopElevationMm`、`TypeName`、`ProvidedFireRating`，加 `CoversElevations`／`OverlapsElevations`／
   `HasReadableRating`／`Qualifies`）與 `CurtainWallObservationSet.FacadeWalls`。共面與平行的判定寫成
   `CurtainWallObservation.IsInFacadePlane`，容差常數 `FacadePlaneToleranceMm`（150）與
   `FacadeAngleToleranceDeg`（5）掛在 `FacadeWallObservation` 上（§4.4）。
   `RevitCurtainWallGeometryReader.ReadFacadeWalls` 以同一個 `IsInFacadePlane` 預篩模型裡的非帷幕直線牆
   ——**一個述詞兩邊共用**，讀取層不會交出解析層不看的牆，也不會漏掉它會算的牆。
   **「嵌板為牆」的嵌板要排掉**：它本身是一片沒有 `CurtainGrid` 的 `Wall`，而且必然躺在帷幕牆的定位面
   內，不排掉就會同時以嵌板與實體外牆兩個身分出現，每個交接處都自己跟自己重疊而判人工覆核。
   `CurtainPanelUniqueIds` 直接從模型裡**所有**帷幕格線（含 Curtain System 的 `CurtainGrids`）的嵌板清單
   建集合，而不是從本次讀到的嵌板——被讀取範圍或尺寸讀取失敗篩掉的嵌板仍然是嵌板。
2. **解析層**：`WallJunction` 的但書供給改走 `FacadeRun`（自交點沿立面往兩側累積連續且達標的實體外牆
   覆蓋長度，接縫在 `TouchToleranceMm` 內視為連續）。`SolidWallBand`、`PanelLookup` 與 CW-H 的嵌板量測
   路徑已移除；`Along`（CW-H 專用的嵌板列）一併移除，因為 CW-V 走的是 `Up`，留著就是死碼。
   `Measure`／`Walk`／`Slab`／`Up`／`SplitMessage` 保留給 CW-V。新增 `Clash`：交接帶內嵌板與實體外牆
   重疊即判 `ManualReview`（新的 `CurtainWallJunctionDoubtKind.FacadeWallOverlapsPanel`、錯誤碼
   `BCR-CW-005`）。
   **累積不截在帷幕牆的端點上**：實體外牆是獨立元素，延伸出去的那一段照樣是外牆面，所以舊的
   「連續段被帷幕牆自身端點截斷則不供給」對 CW-H 不再適用（對 CW-V 仍適用）。
3. **證據**：新增 `junction.facadeWallUniqueIds`（量到的那幾道牆），`junction.panels`／`panelCount` 在
   CW-H 改為**交接帶涵蓋的嵌板**（供 CW-O 扣除與 §7.1 標示）。`junction.minFireRating` 的資料不足訊息
   依 `junction.kind` 換主詞（CW-H 為「交接處實體外牆之設計防火時效」），`RuleFieldCatalog` 的欄位說明
   同步改為「交接帶內構造之最小設計防火時效（水平交接讀實體外牆，層間與其他部分讀嵌板）」。
   CW-H 不再供給 `junction.hasUnprotectedOpening`（§9）。
   `CompartmentWallObservation.ProvidedFireRating` **移除**：決議 7 作廢後沒有讀者，留著會讓人以為區劃牆
   自己的時效能答但書，那正是使用者否決的讀法；讀取層也不再讀它。
4. **測試**：`CurtainWallJunctionResolverTests` 的 CW-H 段整段重寫為 25 條測試（共面／平行守門、
   高程涵蓋、接縫連續、缺口停止、隔壁未達標停止、止於讀不出時效的牆則 withhold、總和不各半、交點落在
   端部豎框、重疊判人工覆核、逐層建的帷幕牆跨元素仍量得到、帶內嵌板記錄並自 CW-O 扣除）。屬於決議 7 與
   `PanelLookup` 的測試逐條確認後：守門意義已由新設計取代者移除，仍然有效者改寫。**原本掛在 CW-H 上但
   守的是 `Measure`／`Walk` 的四條（grid line 分割、真實斷點、未受防護開口、被牆頂截斷）改寫成 CW-V 版**
   ——那條路徑還活著，測試不能跟著 CW-H 一起消失。`FireReviewIntegrationTests` 的帷幕牆 fixture 改為
   「交接帶留空 + 一道實體外牆」的正確建法，`bandMinutes` 同時餵嵌板與實體外牆，案例 3、20 的意義不變。

#### 參數綁定：已確認早已綁定（實機查證）

`防火檢討_設計防火時效`、`防火檢討_設計防火保護`、`防火檢討_遮煙性能` 三個參數**早已以 Type 層級綁在
CurtainPanels 上**。2026-09-27 以 `load_shared_parameters`（`categories: ["CurtainPanels"]`、
`bindToInstance: false`）查證，回報 `TotalBound: 0`、三者皆「已存在相符綁定，跳過」，模型未被改動。

先前判斷「CW-O 每片嵌板都資料不足是因為沒綁參數」**是錯的**：綁定在，缺的是嵌板型別上的**值**。要讓
CW-O 作答，得由設計者在各嵌板型別上填這三個參數，不是再綁一次。這件事**只影響 CW-O**，不影響 CW-H。

#### 使用者要驗出「得免突出」須怎麼建模

依 §4.2「建模要求」：把 `282763` 頂部（或 `282775` 底部）該柱的嵌板拿掉留出 27550 – 28450 的帶，在
X = 10000 的定位面內、沿 Y 方向放一道實體外牆跨該高程帶、沿牆長度自交點起兩側合計 ≥ 900 mm，並在其
型別填 `防火檢討_設計防火時效` ≥ 60 min。或者直接把區劃牆 `297567` 突出帷幕牆外側面 ≥ 500 mm。

**嵌板一定要刪。** 只放實體外牆而留著原本的玻璃嵌板，會落入新的 `FacadeWallOverlapsPanel` 分支判
人工覆核——那不是誤判，是模型同時說了玻璃與實體牆兩件事。

#### 實機驗證結果（2026-09-27，現行模型／純玻璃立面）

已部署並在 Revit 重跑。`CW-H-01` 由「資料不足」轉為 **`Fail`／實際值 0 m**，逐項與決議 13 的預期相符：

| 預期 | 實測證據 |
| --- | --- |
| 本文不成立 | `junction.projectionDepth = 0 Meter`（規定 0.5 m） |
| 但書供給 0（立面是玻璃，那是事實不是未知） | `junction.continuousFireRatedLength = 0 Meter` |
| 交點上沒有實體外牆 | 證據中無 `junction.facadeWallUniqueIds` |
| CW-H 不再供給未受防護開口 | 證據中無 `junction.hasUnprotectedOpening` |
| 交接帶＝區劃牆高程帶 | `junction.placement` Z 段 27.55 – 28.45 |
| 區劃牆要求時效 | `junction.hostRequiredFireRating = 60 Minute` |
| 帶內嵌板數 | `junction.panelCount = 0` |

`panelCount = 0` 是 `PanelBand.Covers` 的**中點規則**使然，不是漏算：帷幕牆 `282763` 以 FL8（24500）為底、
高 3500，兩列嵌板的上列是 26250 – 28000，中點 27125；其上方那片帷幕牆首列 28000 – 31500 之半，中點 28875。
兩者都落在帶（27550 – 28450）之外，因此整層高的玻璃嵌板只是「擦過」交接帶，依 §3.3 歸第 79 條之 4（CW-O）
回答，不列入交接處。此時 CW-H 未符合卻無可塗紅的元素，即 §9 已記的已知限制——文字標註與圖號仍正常產出。

**但書成立的路徑**（`NotApplicable`／得免突出）在步驟 13 這一輪還驗不到——當時立面 24500 – 31500 全是
玻璃。使用者隨後照上面那段改了模型，該路徑已於 2026-09-28 隨步驟 14、15 驗到（見步驟 14 的「實機驗證」段）。

### 步驟 14：交點落在實體外牆連續段上也判 CW-H（決議 14）

**狀態：已完成並實機驗證（2026-09-28）。** 觸發這一步的是 2026-09-27 的實機結果：使用者依決議 13 的建議改模型，
把 FL9 立面（Y = 4790.71）切成帷幕牆 `298700`（X 0 – 2550）＋ RC 牆 `299434`（2550 – 3450，900 mm、
`RC 牆 15cm`、120 min）＋帷幕牆 `282773`（3450 – 10000），區劃牆 `292657` 抵在 X = 3000。結果兩列
CW-H 都判 `ManualReview`：

| 實測結果 | 成因 |
| --- | --- |
| host = `299434`，訊息「端點距帷幕牆 0 mm，超過搜尋公差 300 mm」 | RC 牆自己也躺在區劃邊界上而被列為區劃牆，與帷幕牆**平行**，`FindCrossing` 的 `denominator ≈ 0` 求不到交點，掉進「最近端點」退路；端點正好在帷幕牆起點上（距離 0），訊息卻寫死「超過搜尋公差」 |
| host = `292657`，訊息「端點距帷幕牆 450 mm，超過搜尋公差 300 mm」 | 真正的區劃牆端點 X = 3000 落在 RC 牆那一段裡，離 `282773` 的起點 3450 有 450 mm；交點在帷幕牆定位線的延長線上，決議 13 的實作只認段內 |

兩者都不是誤判邏輯的 bug，而是決議 13 的設計沒有涵蓋「防火帶以實體牆取代該段帷幕牆」這種建模。決議 14
以使用者的判定原則收斂：**垂直於外牆的區劃牆碰到的若是 RC 外牆、而該 RC 外牆兩端為帷幕牆，就要以 CW-H
判該實體外牆長度是否 ≥ 90 cm。**

要改的東西：

1. `CurtainWallJunctionResolver.WallJunction`
   - 開頭加「躺在立面內的 host 不產出」：`wall.IsInFacadePlane(host.Start, host.End)` 成立即 `return null`。
   - 交接帶高程改為交集 `[max(host.Bottom, wall.Base), min(host.Top, wall.Top)]`，取代現有那句
     `host.TopElevationMm <= wall.BaseElevationMm + SnapMm || …` 的前置判斷；交集 ≤ `TouchToleranceMm`
     時 `return null`。`PanelBand.Horizontal`、`nearby` 的 `OverlapsElevations`、`FacadeRun` 的
     `CoversElevations`、`CurtainWallJunctionPlacement.At` 全部改吃這個帶，不再各自讀 host 的高程。
     `FacadeRun` 的簽章因此由 `CompartmentWallObservation host` 改成帶的上下緣加 `requiredMinutes`。
2. `CurtainWallJunctionResolver.FindCrossing`
   - 交點落在延長線上時**不要夾到 [0, length]**，把延長線上的參數原樣帶出（`Crossing` 加一個「在段外」
     的旗標或直接以未夾的 `Along` 表示）。
   - 段外時要求：`at` 被實體外牆連續段覆蓋（接縫 ≤ `TouchToleranceMm`，與 `Chain` 同一套串接），且該
     連續段與**這片**帷幕牆的某一端相接。兩者皆是才算求得交點；否則 `return null`（沒有交接處），
     `UnresolvedIntersection` 只留給「端點差一點沒碰到帷幕牆」。
   - `FindCrossing` 目前不看 `facades`，要把它（或串接後的連續段）傳進去。
3. 歸屬判準（新函式）：與該連續段共面且相接的帷幕牆中，取**相接端點 `(X, Y)` 字典序**最小者擁有，
   相同時退回 `UniqueId` ordinal。不可用「沿軸較低側」——實測模型裡兩片帷幕牆都是從交點往外畫，
   兩片的 `at` 都是負值。
4. `CurtainWallJunctionResolver.ZoneOf`：`at` 在段外時現在會被 `Math.Min(Math.Max(at, 0), length)` 夾到
   端點，探點因此偏離真正的交點（實測偏 450 mm）。改為允許段外的 `at` 直接外插（`PointAt` 的線性外插），
   只有真的要限制在牆上時才夾。CW-H 的交點左右兩側探點（`lateral: true`）必須落在交點兩側，否則
   `ZoneId` 會挑錯邊。
5. 標示：`CurtainWallReviewMarks` 加 `junction.facadeWallUniqueIds` 的讀取（欄位證據已經在寫，見步驟 6b-1），
   `ReviewMarkup` 的 CW-H 未符合把這些牆一併塗紅。**不得**把牆塞進 `panelUniqueIds`（見 §7.1）。
6. 測試：§10 案例 26 – 33 加進 `CurtainWallJunctionResolverTests`（幾何層）與
   `CurtainWallJunctionRuleTests`／`FireReviewIntegrationTests`（整合層）。特別要釘住的三條：
   案例 28（同一交接處只有一列，換 `set.CurtainWalls` 的順序仍是同一個擁有者）、案例 30（平行 host
   不產出，且該牆仍供給別人的長度）、案例 33（交集為 0 不產出）。
7. 既有測試會動到期望值的地方：`junction.placement` 的 Z 段由 host 全高改為交集帶（步驟 13 的實機基線
   27.55 – 28.45 那一列是 host 高程，不是交集，要重算）；`CurtainWallJunctionResolverTests` 裡以 host
   高程建帶的案例。**判定式與規則檔不動**，`tw-bcr-79-curtain-wall-junction` 仍是
   `projectionDepth >= 500 mm` 加豁免 `continuousFireRatedLength >= 900 mm`，規則集版本不升。

#### 實作結果

上面七項全部做完，建置 0 警告、1631 條測試全通過。幾件實作時才定案的細節：

- **交點段外時的三段判斷寫在 `FindCrossing` 裡**：`t ∈ [0, 1]`（區劃牆延伸後確實跨過定位線）成立而
  `u` 落在段外時，才問實體外牆連續段；連續段蓋不住或接不上這片帷幕牆就 `return null`。`t` 不在
  `[0, 1]` 的情形（區劃牆停在帷幕牆前面）仍走「最近端點」的 `UnresolvedIntersection` 退路，兩者互不
  干擾——那一條既有測試沒有動。
- **覆蓋交點的連續段不篩高程、也不篩時效**，只問幾何：案例 32（實體外牆只封住帶的一部分）要的是
  「產出這一列並判 `Fail`」，若在求交點時就篩掉高程不足的牆，整列會消失。高程與時效的篩選留在
  `FacadeRun`，維持「求得交點」與「量得長度」兩件事分開。
- **歸屬 `Owns` 以連續段的世界座標端點比對每一片帷幕牆的 `Start`／`End`**，不是比參數——兩片牆各有
  自己的軸向，參數不可比。`CurtainWallObservationSet` 本來就把 `CurtainWalls` 依 `UniqueId` 排序，
  因此案例 28 改以「左片取字典序較大的名字」驗證擁有者仍是座標較小的那一片。
- `Chain` 拆成可回傳 null 的 `ChainAt`，求交點與量長度共用同一套串接規則（§4.2 明文要求兩者同一套）。
- `ZoneOf` 的夾取整個移除：原本的 `Math.Min(Math.Max(at, 0), length)` 只在 CW-H 的 `lateral` 探點會
  生效，其餘呼叫端的 `at` 本來就在段內，移除後既有測試無一改變。
- 標示層的略過訊息只在**兩個清單都空**時才記，CW-H 的訊息改為「沒有記錄交接處的帷幕嵌板或實體外牆」；
  CW-O 的訊息不變。
- 第 7 點預期會動的既有期望值**實際上一條都沒動**：測試 fixture 的區劃牆與帷幕牆高程本來就相同，
  交集等於原本的 host 全高。步驟 13 的實機基線（27.55 – 28.45）是 Revit 端的數字，重跑時才會改變。

實機驗證（**已完成，2026-09-28**）：與步驟 15 一併部署（`install-revit-2024.ps1`，0 警告）後在
「建築防火檢討1」重跑檢討，**使用者確認 CW-H 檢討結果無誤**。模型幾何未再改動——`299434` 的 900 mm 與
120 min 都已達標，高程 28450 – 31500 與帷幕牆相同，交集帶成立。驗證前列出的預期值為：`CW-H` 由
`ManualReview` 轉為 **`NotApplicable`**、`junction.continuousFireRatedLength = 0.9 m`、
`junction.facadeWallUniqueIds` 含 `299434`、只出現一列、擁有者為 `298700`（相接端點 X = 2550 < 3450）、
`junction.placement` 的 Z 段為 28450 – 31500、host = `299434` 的那一列消失。這是 CW-H 的但書路徑
（`NotApplicable`）第一次在 Revit 端驗到。

> 使用者是以「CW-H 檢討無誤」整體確認，未逐欄回貼面板明細，因此上表列的是驗證前的預期值，而非逐欄
> 核對過的實測值。逐列判定存在 Extensible Storage，revit-mcp 讀不到；若之後需要逐欄基線，得請使用者
> 從面板把那一列貼出（步驟 13 的實機基線即是這樣取得的）。

### 步驟 15：外側法線改由所屬區劃推定（決議 15）

狀態：**已完成並實機驗證（2026-09-28）**。設計見 §4.6，測試案例見 §10 案例 34–39。

實作落在兩個檔案：`CurtainWallObservation.WithReversedExteriorNormal()`（只改法線正負的複製路徑）與
`CurtainWallJunctionResolver.Oriented()`（§4.6 的三站多數決），後者在 `ForCurtainWall` 開頭套用，
之後整個方法用定向後的那一片。`CurtainWallObservationSet` **不重建**：沒有任何地方讀「另一片」帷幕牆
的法線，`Owns`／`TouchingEnd`／`Adjoins` 比的都是世界座標。建置 0 警告、1637 條測試全通過
（原 1631 條加案例 34–39 六條）。判定式與規則檔未動，規則集版本維持 `2026.4-provisional`。

#### 為什麼要做

2026-09-27 為了驗證步驟 14，先去確認交接處擁有者 `298700` 的外側法線方向，量出來的結果推翻了
「這是一條建模要求」這個前提：

1. `get_wall_info` 回 `OrientationX/Y = (0, 1)`，朝室內（室內在 `Y > 4790.71`）。
2. 在 Revit UI 裡對 `298700` 按空白鍵翻面，`Wall.Flipped` 由 `false` 變 `true`——`wall.Orientation`
   **完全沒變**，定位線方向也沒有反轉（仍是 X 2550 → 0）。以 `diagnose_curtain_wall_elevation_direction`
   覆核（該工具直接回報 `wall.Orientation`）得到同樣的 `(0, +1, 0)`。
3. 同一道立面的 `282773`（X 3450 → 10000，`Flipped: false`）回 `(0, −1, 0)`。

兩片對照下來，帷幕牆的 `wall.Orientation` 等於定位線方向繞 Z 轉 −90°，`Wall.Flipped` 對它沒有作用。
結論：外側方向在 Revit UI 裡改不掉，把它列為建模要求等於要求使用者猜定位線的旋轉方向。

順帶量到的一件事：實體外牆 `299434` 的法線也朝室內（`(0, +1)`），但**不影響判定**——全專案只有
`RevitCurtainWallGeometryReader.cs:170` 一處讀 `Orientation`，`ReadFacadeWalls` 純幾何、不讀法線。

#### 要做什麼

1. `CurtainWallObservation` 加一個只改法線正負的複製路徑（`Start`／`End`／`ExteriorOffsetMm`／
   `Panels`／`GridLines` 一律沿用），`netstandard2.0` 沒有 `with`，照 `RevitCurtainWallGeometryReader:180`
   既有的重建寫法即可。
2. `CurtainWallJunctionResolver` 在處理一片帷幕牆之前先定向（§4.6 的三站多數決）。放在
   `ForCurtainWall` 開頭即可，不必重建 `CurtainWallObservationSet`：已查證沒有任何地方讀「另一片」
   帷幕牆的法線——`Owns`／`TouchingEnd`／`Adjoins` 比的都是世界座標。
3. §10 案例 34–39 六條測試，其中案例 39 是守門測試（定向不得動到位置與長度）。
4. §4.2「建模要求」原本的「兩側帷幕牆外側法線方向要一致」已在本次文件更新中刪除，實作完成後不需再動。
5. **判定式與規則檔不動**，規則集版本**不升**：決議 15 沒有改任何判定式，只改讀到的幾何。

#### 實機驗證結果

2026-09-28 部署（`install-revit-2024.ps1`，建置 0 警告；部署前 1637 條測試重跑全通過），在
「建築防火檢討1」重跑檢討後**使用者確認 CW-H 檢討結果無誤**。驗證一次涵蓋兩件事：步驟 15
（`298700` 重新出現在檢討中，不再因法線朝內整片消失）與步驟 14（那一列是 `NotApplicable`、0.9 m、
只有一列）。逐項預期值與「以整體確認、未逐欄回貼」這個限制，見步驟 14 的「實機驗證」段。

決議 15 刻意不猜的兩種情形仍未被實機觸及，維持 §9 的記載：兩側都有區劃的帷幕牆保留原法線，
兩側都找不到區劃的帷幕牆整片靜默消失。

### 步驟 16：帷幕嵌板分實心與玻璃兩路作答（決議 16）

起因是 CW-O 在實機上全部 `InsufficientData`：模型裡的嵌板型別 `玻璃 1.0cm`（TypeId `12611`）沒有、
也填不出 `防火檢討_設計防火時效`。追下去發現缺的不是一個值，而是一個分類——玻璃嵌板根本沒有構造
時效可填，它的防火性能是以認可之防火設備表達的。§3.3 與決議 16 是這個分類的結論。

#### 步驟 16a 的產出（已完成，0 警告、1650 條測試全通過）

| 檔案 | 改了什麼 |
| --- | --- |
| `Application/Checks/CurtainPanelKinds.cs`（新增） | `CurtainPanelKind`（`Solid`／`Glazed`／`Opening`）、參數名 `防火檢討_嵌板種類`、用字解析（`實心`／`玻璃` 及別名）、`RuleText` 兩路映射 |
| `Application/Candidates/CurtainWallObservations.cs` | `CurtainPanelObservation.Kind`、`AnswersByRating`／`AnswersByProtection`；門窗一律 `Opening`；`Protection` 不再是門窗專屬讀值 |
| `Domain/Rules/RuleFieldCatalog.cs` | `junction.panelKind`、`junction.minFireProtection` 兩個欄位，仍只對 `CompartmentContinuity` 開放 |
| `Application/Checks/CurtainWallJunctionInputs.cs` | `PanelAnswerKind`、`MinFireProtection` 兩個屬性；`OtherPanels`（實心，簽章不變）、`OtherGlazedPanels`、`OtherUndeclaredPanels` 三個工廠 |
| `Application/Candidates/CurtainWallJunctionResolver.cs` | `OtherPanelJunctions` 把其餘嵌板拆成最多三列（`:solid`／`:glazed`／`:undeclared`），新增防火保護的 `Worst` 取值 |
| `Application/Checks/CurtainWallJunctionCheck.cs` | 寫入兩個新 fact、`protection.*` 證據、未宣告種類時的訊息與 `BCR-PARAM-001` |
| `Application/Reviews/ReviewInputSources.cs` | 登錄 `junction.panelKind` → `防火檢討_嵌板種類`（Type、Curtain Panels），使它成為前置檢查要求的必要參數 |
| `Data/fire-review-rules.json` | `tw-bcr-79-4-curtain-wall-other` 升版本 2（`appliesWhen` 加 `panelKind == "Solid"`）、新增 `tw-bcr-79-4-curtain-wall-other-glazed`；規則集版本 `2026.6-provisional` → `2026.7-provisional` |

**為什麼未宣告種類不必在判定層特判**：兩條 CW-O 規則的 `appliesWhen` 都讀 `junction.panelKind`，
不供值時兩條的適用與否都算不出來，引擎的 `UndecidedApplicability` 自己就答 `InsufficientData` 並指名
缺的欄位。判定層只把訊息改寫得更白話（指名參數與兩個可填的值）。反過來，CW-H、CW-V 不設 `panelKind`
也不會被這兩條規則誤傷：`junction.kind` 先不符，三值邏輯的 `false && unknown` 仍是 `false`（案例已釘住）。

**為什麼是三列而不是一列取最不利**：一路是分鐘數、一路是是非題，湊不成同一個門檻；而且合成一列之後，
「實心漏填時效」與「玻璃宣告不是防火設備」會退化成同一個狀態，前者使用者補得起來，後者是設計本身
不符合。三列同為 `CurtainPanelOther`，所以 §7.2 的檢討表仍是三列、`Groups.Count == 3` 的守門測試不動。

#### 改動過的既有測試（誠實記錄）

- `CurtainWallJunctionRuleTests.Shipped_rule_set_compiles_the_three_continuity_rules`：釘住的規則 id 由
  三個變四個。
- `CurtainWallJunctionRuleTests.Other_panels_need_half_an_hour`／`The_three_kinds_never_apply_at_the_same_time`：
  加上 `junction.panelKind`，斷言本身不變。
- `CurtainWallJunctionResolverTests` 的 `Panel`／`Unrated` fixture：預設宣告為實心，既有案例的語意不變。
- `FireReviewIntegrationTests`：規則集版本字串、`NeededBy` 的預期清單加 `防火檢討_嵌板種類`，
  fixture 的玻璃嵌板改為宣告玻璃並勾防火保護（第四列因此改由 `-glazed` 那條規則作答）。

#### 步驟 16b 的產出（已完成，0 警告、1672 條測試全通過）

參數層：尺寸推定認得帷幕嵌板，型別列依種類決定該填哪一格，共享參數檔補上 `…0013`。**Revit 端仍然
完全沒有接線**——讀取層還不會讀 `防火檢討_嵌板種類`，所以當時每一片嵌板在型別列上都是未宣告。
（這一句是 16b 當下的狀態；**16c 已經接上讀取層**，見下一節。）

| 檔案 | 改了什麼 |
| --- | --- |
| `Application/Checks/FireRatingDerivation.cs` | `DerivableCategories` 加 `CurtainPanel`；`DimensionLabel` 回「嵌板厚」；新增私有 `IsWallLike`，`Concrete` 與 `Steel` 兩處的條文選擇改用它，帷幕嵌板因此與牆**共用同一組門檻與條文字串**（第 72 條一(一)／(二)、第 73 條一(一)／(二)） |
| `Application/Parameters/FireReviewTypeTable.cs` | `FireReviewTypeRow` 加 `panelKind` 建構參數（尾端、有預設值）、`PanelKind`／`ParsedPanelKind`／`AwaitsPanelKind`／`SupportsDerivation`／`PanelKindEdit(kind)`；`CarriesRating`／`CarriesProtection` 改看種類；`WouldChangeRating` 與 `RatingEdit()` 改以 `SupportsDerivation` 把關；`FireReviewTypeParameters` 加 `PanelKind = 32`；`FireReviewTypeTable` 加 `AwaitingPanelKind`，`AwaitingMaterial`／`AwaitingCover` 改為只收 `SupportsDerivation` 的列 |
| `FireReview/FireReviewTypeRowViewModel.cs` | `PanelKind` 可編輯屬性與 `PanelKindChoices`（空白＋實心＋玻璃）、`IsCurtainPanel`、`CarriesMaterial`；`CarriesRating`／`CarriesProtection`／`SupportsDerivation`／`NeedsCover`／`CanApplyDerived`／`RatingDiffers` 改看**目前選的**種類而非模型存的；`MissingParameters` 把 `防火檢討_嵌板種類` 排在最前面、材料與被覆改以 `CarriesMaterial` 把關；`Edits()` 先寫種類，並在宣告實心時才寫 `結構材料`／`防火被覆厚度` |
| `assets/SharedParameters/fire-review-shared-params.txt` | 新增 `PARAM bcf10001-0000-4a00-9b00-000000000013 防火檢討_嵌板種類 TEXT`（GROUP 1、可見、使用者可改），插在 `防火檢討_遮煙性能` 之後。**檔案仍是 Big5/cp950＋CRLF**，以位元組層級接上去，其餘位元組未動 |

**為什麼型別列的三個 `Carries*` 要分開**：實心嵌板只欠時效、玻璃嵌板只欠防火保護，**未宣告的兩個都欠**。
未宣告時故意兩個都標成缺口，是因為挑任何一邊當預設，都會在面板上把另一邊的缺口藏起來——那正是決議 16
要避免的「把未符合說成資料不足」。`遮煙性能` 不受種類影響：昇降機道出入口可能是任何一種嵌板（垂直區劃文件 §4）。

**為什麼玻璃嵌板照樣送進 `FireRatingDeriver`**：讀取層不必先分種類再決定要不要推定。玻璃不是 RC／SRC／SC，
結構材料空白時回 `MaterialMissing`；就算有人硬填了材料，1 cm 的玻璃也到不了第 73 條的 7 cm，回 `NotRated`。
兩條路都不會替玻璃編出時效。真正的把關在 `SupportsDerivation`：宣告玻璃或尚未宣告時，`WouldChangeRating`
與 `RatingEdit()` 一律不出手，「套用推定值」因此不會把數字寫進檢討根本不會讀的參數。

#### 16b 改動過的既有測試（誠實記錄）

- `FireReviewTypeTableTests.A_curtain_panel_derives_no_rating_of_its_own`：**已刪除並改寫**。原本釘住
  「帷幕嵌板永遠不推定」，決議 16 就是要推翻這一句；改寫成
  `A_solid_curtain_panel_derives_its_rating_from_its_thickness`、
  `A_glazed_curtain_panel_is_never_given_a_derived_rating`、
  `An_undeclared_curtain_panel_derives_nothing_until_it_declares_a_kind` 三條，把原本那一句拆成
  「宣告實心才推定」。
- `FireReviewTypeTableTests.A_curtain_panel_answers_both_the_rating_and_the_protection`：更名為
  `An_undeclared_curtain_panel_answers_both_…`，斷言不變——未宣告時兩個都要答仍然成立，只是現在
  這是「未宣告」的行為，不是「帷幕嵌板」的行為。
- `SharedParameterFileTests.Every_parameter_keeps_its_own_guid_and_name`：`PARAM` 行數 16 → 17。

#### 步驟 16c 的產出（已完成，0 警告、1707 條測試全通過）

Revit 讀取層接線：嵌板的種類讀得到了，嵌板厚度讀得到了，每一片嵌板都讀防火保護，未宣告的型別由材料
提案一個種類給使用者確認。**Revit 端尚未實機驗證**——這一段幾乎全在 Revit API 上，核心測試專案只參照
Domain 與 Application，蓋不到轉接層。

| 檔案 | 改了什麼 |
| --- | --- |
| `Application/Checks/CurtainPanelKinds.cs` | 新增 `Classify(isOpening, isPanelAsWall, declaredText)` 與 `ProposeFrom(materialClass, materialName, typeName)`。**兩個判斷都寫在 Application 層**，讀取層只負責把 Revit 的事實交進來，述詞因此只有一份而且測得到 |
| `Application/Parameters/FireReviewTypeTable.cs` | `FireReviewTypeRow` 建構子尾端加 `CurtainPanelKind? proposedPanelKind`；新增 `ProposedPanelKind`（讀取層的提案，不是模型的值）與 `PanelKindProposal`（只在型別還沒宣告時才有值）。`Opening` 不接受為提案——它沒有 `ParameterText`，沒有任何 edit 載得動它 |
| `FireReview/FireReviewTypeRowViewModel.cs` | 建構子在 `防火檢討_嵌板種類` 完全空白時用提案當下拉初值（已宣告或填了讀不懂的字都照原樣顯示）；新增 `PanelKindIsProposed`／`PanelKindProposalNote` 供 16d 在列上說明「這是提案、還沒寫入」 |
| `Revit/Geometry/RevitCurtainWallGeometryReader.cs` | `ReadPanels` 改為**每片嵌板都讀** `防火檢討_設計防火保護`（原本只有門窗讀），種類交給 `CurtainPanelKinds.Classify`（`element is Wall` 即「嵌板為牆」），結果填進 `CurtainPanelObservation` 的 `kind`；新增私有 `Text(element, name)` 讀型別的文字參數 |
| `Revit/Parameters/RevitFireReviewTypeScanner.cs` | `Dimension` 對 `CurtainPanel` 回 `Structure(type)`（`PanelType` 也是 `HostObjAttributes`，複合構造與牆、樓板同一條路）；`Row` 讀 `防火檢討_嵌板種類`、在**帷幕嵌板類別**帶這個參數時設 `FireReviewTypeParameters.PanelKind` 旗標、並以新增的 `PanelMaterial`（複合構造最厚的一層，其次 `MATERIAL_ID_PARAM`）算出提案 |
| `assets/SharedParameters/fire-review-openings-type.txt` | 加入 `PARAM …0013 防火檢討_嵌板種類 TEXT`（定義行自主檔逐位元組複製），並在檔頭補上它只對帷幕嵌板有意義、以及帷幕嵌板另需構件檔那兩個參數的說明。**檔案仍是 Big5/cp950＋LF**（主檔是 CRLF，差異是刻意的），`git diff --numstat` 為 `7 0` |
| `tests/…/BuildingRegulationReview.Core.Tests.csproj` | 把 `fire-review-openings-type.txt` 一併複製到輸出，讓新的位元組層級測試讀得到 |

**為什麼宣告優先於「嵌板為牆」**：`防火檢討_嵌板種類` 只綁 Curtain Panels，牆型別實務上讀到的是 null，
於是落到 `Solid`——與決議 16 原本寫的「嵌板為牆一律實心」同一個結果。但真有人在牆型別上明寫了種類時，
明寫的那句話比推論可靠，所以 `Classify` 先看宣告。這是嚴格的超集，不是改判。

**為什麼提案只往玻璃一邊**：「不是玻璃」推不出「是實心」。一片 3 mm 鋁板不是玻璃，但把它宣告成實心
等於說它的防火時效由厚度推定，那是法規上的分類，不是工具替設計者決定的事。型別名稱是三個訊號裡最弱的
一個，只在材料類別與材料名稱都沒話說時才看——系統嵌板常常沒設材料，而型別就叫「玻璃 1.0cm」，那正是
起初讓 CW-O 全部資料不足的那一列。

**提案不等於宣告**：`ProposedPanelKind` 不影響 `AwaitsPanelKind`、不影響 `Present`、不影響
`SupportsDerivation`，所以該列照樣出現在面板的「待宣告」清單裡，CW-O 照樣答資料不足。值只進下拉，
寫入要使用者按下套用才發生（`A_proposed_kind_is_not_a_declaration` 是守門測試）。

**為什麼每片嵌板都讀防火保護是安全的**：`IsUnprotectedOpening` 仍然以 `IsOpening` 把關，所以 CW-H 與
CW-V 的連續段判定一個字都沒變；改的只是玻璃嵌板從此拿得到 `Protection`，`OtherGlazedPanels` 才答得出
第 79 條之 4。型別沒綁這個參數時讀到的是 `ProvidedFireProtection.Missing`（原本是 null），兩者對
`AnswersByProtection` 那一路都一樣是不作答。

#### 16c 新增與改動過的測試（誠實記錄）

- **新增** `Checks/CurtainPanelKindsTests.cs`（9 個測試方法、26 個案例）：`Classify` 的四條分支（門窗一律 Opening、嵌板為牆
  未宣告即實心、宣告優先於推論、其餘未宣告為 null）與 `ProposeFrom`（材料類別、材料名、型別名三個訊號；
  **任何輸入都不提案實心**；提案一定是使用者可宣告的那兩種）。
- **新增** `Parameters/OpeningsSharedParameterFileTests.cs`（6 條）：綁定用檔案的 Big5＋LF、`…0013` 的欄位、
  四個 PARAM、每個 PARAM 的 GROUP 在同一個檔裡有宣告、與主檔的 GUID／名稱／資料型別逐位元組相符。
  **`DESCRIPTION` 與 `GROUP` 刻意不比對**：說明文字是寫給綁這一份的人看的，兩個檔對 `…000d` 的說明
  本來就不同（主檔點名「門窗帷幕嵌板類型」，這一份講未勾選的意思），GROUP 的編號是檔案內部的。
  這件事是新測試第一次跑就抓出來的，不是事後放寬。
- `Parameters/FireReviewTypeTableTests.cs`：`Row` 輔助方法加 `proposedPanelKind` 參數（既有斷言一個字
  沒動），另新增 `A_proposed_kind_is_not_a_declaration`、`A_declared_kind_leaves_no_proposal_to_offer`、
  `An_opening_is_never_proposed_as_a_kind` 三條。

#### 步驟 16d 的產出（已完成，0 警告、1707 條測試全通過）

參數面板的「構件類型」分頁多一欄「嵌板種類」，帷幕嵌板從此在面板上宣告得了種類，右邊該填哪一格也跟著
亮暗。**判定與資料都在 16b、16c 做完了，16d 全部是呈現層**——沒有新的述詞，`Edits()` 一個字沒動。

| 檔案 | 改了什麼 |
| --- | --- |
| `FireReview/FireReviewParameterPanelWindow.xaml` | 「斷面尺寸」之後、「結構材料」之前插入 `DataGridTemplateColumn`「嵌板種類」（寬 126）：`CellTemplate` 顯示 `PanelKind` 並在 `PanelKindIsProposed` 時附「（提案）」，`CellEditingTemplate` 是可輸入的 `ComboBox`（`ItemsSource="{Binding PanelKindChoices}"`、`Text="{Binding PanelKind}"`），`CellStyle` 在 `IsCurtainPanel` 為 `False` 時停用並灰底、在 `PanelKindIsProposed` 時改黃底並把 `ToolTip` 換成 `PanelKindProposalNote`。「結構材料」欄的 `IsEnabled` 由 `SupportsDerivation` 改為 `CarriesMaterial`（兩個 `Style` 都改）。分頁上方的說明補一句嵌板種類與提案怎麼看 |
| `FireReview/FireReviewTypeRowViewModel.cs` | 新增 `AwaitsPanelKind => Source.AwaitsPanelKind`，給狀態列數「待宣告」用。轉發而不重算，是為了讓面板數的與 `FireReviewTypeTable.AwaitingPanelKind` 是同一個定義（那一個有測試蓋） |
| `FireReview/FireReviewParameterPanelWindow.xaml.cs` | `UpdateStatus` 在「可推定」之後加「待宣告嵌板種類 N 列」；`SetMaterial_OnClick` 的篩選由 `SupportsDerivation` 改為 `CarriesMaterial`（提示文字一併改）；`Write_OnClick` 的確認視窗在有未修改的提案時多一行點名它有幾列 |

**為什麼種類欄綁 `Text` 而不是 `SelectedItem`**：`FireReviewTypeRowViewModel` 刻意把讀不懂的宣告照原樣
留著（使用者才看得出模型裡填了什麼）。清單裡只有 `""`／`實心`／`玻璃`，綁 `SelectedItem` 時一個讀不懂的
值（例如「膠合」）會找不到對應項而被清成空白，`Edits()` 隨即把那個空白寫回模型，把使用者原本填的內容
弄掉。這與「區劃用途」欄避開的是同一個坑。

**為什麼「結構材料」欄改看 `CarriesMaterial`**：欄位問的是「這一列填不填結構材料」，不是「填了推不推得出
時效」。梁兩者不同——第 71～73 條第三款把梁的要求寫成構造別而沒有尺寸門檻，所以推不出時效，但梁仍然要
填材料，`MissingParameters` 一直在要它，而舊的 `SupportsDerivation` 把梁那一格鎖住了。帷幕嵌板則是宣告
實心之後才亮，正是決議 16 要的行為。同一個理由，「選取列設為…」的篩選也改成同一個述詞，否則選了梁按
下去會得到一句看不懂的「請先選取牆、柱或樓板」。

**提案在面板上一定看得出來**：黃底、值後面加「（提案）」、滑過去的說明寫「由嵌板材料提案為『…』，尚未
寫入模型」。狀態列的「待宣告」也照樣把它算進去（`AwaitsPanelKind` 看的是模型裡的值，不是下拉裡的值），
而且按「寫入模型」時確認視窗會點名「其中 N 個帷幕嵌板類型的『嵌板種類』是工具由材料提案、您未修改的
值」——使用者為了別的欄位按下寫入時，不該順手替自己宣告了嵌板種類卻不知道。

**沒有做的**：「待宣告」只做成狀態列的一個計數，沒有做成可點選、可跳列的清單（`AwaitingMaterial`、
`AwaitingCover` 一樣從來沒有 UI 讀過）。狀態列在建構時算一次，之後被各個按鈕的訊息覆蓋，不會隨編輯即時
更新——這是面板既有的行為，16d 沒有改它。

#### 16d 的測試情況（誠實記錄）

**沒有新增任何測試，也沒有任何既有測試蓋得到這一段。** `src/BuildingRegulationReview` 是 net48 WPF，
`BuildingRegulationReview.Core.Tests`（net10.0）不參照它；1707 條全通過只代表 16d 沒有弄壞判定層與參數層。
XAML 的繫結路徑不由編譯器檢查（markup compile 過了不表示 `{Binding PanelKindIsProposed}` 拼對了），
所以下列六個繫結在 16e 之前**只經人工核對**：`IsCurtainPanel`、`PanelKind`、`PanelKindChoices`、
`PanelKindIsProposed`、`PanelKindProposalNote`、`CarriesMaterial`。16e 開面板後使用者回報「面板沒問題」，
**這是整體確認，六個繫結沒有逐一分項的基線**（見 §12 的 16d 與 16e 兩列）。

#### 16e 完成後的狀態

決議 16 的五個步驟 16a–16e 全部完成，CW-O 已在 Revit 端作答，且**玻璃那一路的檢討表列已逐字核對**
（2026-09-28，帷幕牆 282761，見上方「16e 的驗證證據」）。**仍未驗到的四處**：
`RevitFireReviewTypeScanner` 對 `FamilySymbol`（訂製嵌板族）的讀取路徑（這個模型的兩個嵌板型別都是
`PanelType`）、實心那一路的完整鏈路（本模型沒有宣告為實心的嵌板，`結構材料`＋`防火被覆厚度` 推定時效
未在 Revit 端跑過）、帷幕牆 282773 是否另有一列 CW-O（推定前提 (3) 算的是它，但核對到的是 282761），
以及檢討表 CW-H 與 CW-V 兩列的逐字內容。
