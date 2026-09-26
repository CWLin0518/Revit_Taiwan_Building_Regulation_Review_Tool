# 垂直區劃：第 79 條之 2

功能 ID：`vertical-compartment`

狀態：**規則層與參數層已完成**（本文件 §5、§6、§10）。檢查層與 Revit 產出未開始（§12）。

## 1. 功能摘要

防火構造建築物內的挑空、昇降階梯間、樓梯間、昇降機道、管道間，依第 79 條之 2 第 1 項各自
以一小時以上防火時效之牆壁、防火門窗等防火設備與該處防火構造之樓地板**單獨**區劃分隔。它們
因此不受第 79 條、第 83 條的區劃**面積**規定拘束——第 83 條本文就明寫「除依第七十九條之二規定
之垂直區劃外」——這件事已在
[`zone.use` 用字表](zone-use-vocabulary.md) 與兩條面積規則的豁免清單裡完成。

本功能接手那批被豁免的垂直區劃，但**只檢討第 1 項本文之外的附加要求**：

| 要求 | 條文 | 現況 |
| --- | --- | --- |
| 昇降機道裝設之防火設備應具有遮煙性能 | 第 1 項第 2 句 | 本功能 |
| 管道間之維修門應具有一小時以上防火時效 | 第 1 項第 3 句前段 | 本功能 |
| 管道間之維修門應具有遮煙性能 | 第 1 項第 3 句後段 | 本功能 |
| 區劃牆壁一小時以上防火時效 | 第 1 項本文 | 既有 `tw-bcr-79-wall-rating` |
| 區劃樓地板為防火構造 | 第 1 項本文 | 既有 `tw-bcr-70-floor-rating`／`tw-bcr-79-floor-rating` |
| 區劃開口應為防火門窗等防火設備 | 第 1 項本文 | 既有 `tw-bcr-79-opening` |

本文的三項不在本功能裡重複，理由見 §3.4。

## 2. 法源條文

### 2.1 第 79 條之 2

> **第 1 項** 防火構造建築物內之挑空部分、昇降階梯間、安全梯之樓梯間、昇降機道、垂直貫穿樓板之
> 管道間及其他類似部分，應以具有一小時以上防火時效之牆壁、防火門窗等防火設備與該處防火構造之
> 樓地板形成區劃分隔。昇降機道裝設之防火設備應具有遮煙性能。管道間之維修門並應具有一小時以上
> 防火時效及遮煙性能。
>
> **第 2 項** 前項昇降機道前設有昇降機間且併同區劃者，昇降機間出入口裝設具有遮煙性能之防火設備
> 時，昇降機道出入口得免受應裝設具遮煙性能防火設備之限制；昇降機間出入口裝設之門非防火設備但
> 開啟後能自動關閉且具有遮煙性能時，昇降機道出入口之防火設備得免受應具遮煙性能之限制。
>
> **第 3 項** 挑空符合下列情形之一者，得不受第一項之限制：一、避難層通達直上層或直下層之挑空、
> 樓梯及其他類似部分，其室內牆面與天花板以耐燃一級材料裝修者。二、連跨樓層數在三層以下，且
> 樓地板面積在一千五百平方公尺以下之挑空、樓梯及其他類似部分。
>
> **第 4 項** 第一項應予區劃之空間範圍內，得設置公共廁所、公共電話等類似空間，其牆面及天花板
> 裝修材料應為耐燃一級材料。

讀條文時要注意的三件事：

- **遮煙性能只加在昇降機道與管道間維修門上。** 第 1 項對樓梯間、挑空、昇降階梯間只要求本文的
  一小時牆壁與防火設備，沒有加遮煙。相對地，地下建築物的第 203 條同一組要求把樓梯也納入遮煙
  （見 §9）——兩條文不同，不可互相套用。
- **第 2 項的兩個但書效力不同。** 前者是「昇降機道出入口**得免裝設**具遮煙性能之防火設備」，
  後者是「昇降機道出入口之**防火設備得免受應具遮煙性能**之限制」。後者仍要求有防火設備，前者
  連防火設備本身都放寬。本版只模型化兩者共通的「免遮煙」，見 §9。
- **第 3 項的免除只寫給挑空。** 前言是「挑空符合下列情形之一者」，雖然兩款的敘述都寫成
  「挑空、樓梯及其他類似部分」。本版未實作第 3 項，見 §9。

### 2.2 用語（第 1 條）

| 款 | 用語 | 定義 |
| --- | --- | --- |
| 三十一 | 防火時效 | 建築物主要結構構件、防火設備及防火區劃構造遭受火災時可耐火之時間 |
| 四十五 | **遮煙性能** | 在常溫及中溫標準試驗條件下，建築物出入口裝設之一般門或區劃出入口裝設之防火設備，當其構造二側形成火災情境下之壓差時，具有漏煙通氣量不超過規定值之能力 |
| 四十六 | 昇降機道 | 建築物供昇降機廂運行之垂直空間 |
| 四十七 | 昇降機間 | 昇降機廂駐停於建築物各樓層時，供使用者進出及等待搭乘等之空間 |

遮煙性能是**試驗結果**，和阻熱性（第 32 款）一樣不可能從模型幾何推導，只能是設計者宣告的輸入
（§6）。

### 2.3 第 83 條的對應（垂直區劃不計面積）

> 建築物自第十一層以上部分，**除依第七十九條之二規定之垂直區劃外**，應依左列規定區劃……

第 79 條本文沒有同樣的除外文字，但工具對兩條面積規則採同一份豁免清單。這是一個明示的判斷：
第 79 條之 2 對這些垂直空間的要求不因所在樓層而異，它們本來就要單獨區劃分隔，不是第 79 條那種
水平區劃的對象。完整理由與守門測試見 [`zone.use` 用字表](zone-use-vocabulary.md)。

## 3. 檢查項目與判定式

### 3.1 受檢主體是「一項要求」，不是一個元素

帷幕牆功能的主體是一個交接處，一個交接處只有一項要求。這裡不是：**同一扇管道間維修門同時被
要求一小時防火時效與遮煙性能**，而規則的 `requiredValue` 只能寫一個比較式
（`RuleExpressionParser.ParseRequirement`：實際值欄位 比較運算子 運算式），同類別同優先序的兩條
規則若結論不同又會被引擎判 Conflict。

所以本類別的受檢主體是 **（防火設備, 要求）這一對**，由 `shaft.requirement` 指名是哪一項要求。
一扇維修門會產生兩個主體、兩筆結果。三項要求因此天然互斥，任一主體只會命中一條規則。

| `shaft.requirement` | 條文 | 判定式 | 豁免 |
| --- | --- | --- | --- |
| `HoistwaySmokeSeal` | 第 1 項第 2 句 | `shaft.providedSmokeProtection == "是"` | `shaft.elevatorLobbyProtected == true`（第 2 項） |
| `ShaftDoorRating` | 第 1 項第 3 句前段 | `shaft.providedFireRating >= 60 min` | 無 |
| `ShaftDoorSmokeSeal` | 第 1 項第 3 句後段 | `shaft.providedSmokeProtection == "是"` | 無 |

用字定義在 `VerticalCompartmentRequirement`／`VerticalCompartmentRequirements`
（`Application/Checks/VerticalCompartmentInputs.cs`），`RuleText` 是規則比對的字樣，`UseOf` 則把
每項要求綁回 `ZoneUses` 的 `zone.use` 用字，所以用字表與規則不會各走各的。

### 3.2 哪個區劃用途要受哪些要求

| `zone.use` | 附加要求 |
| --- | --- |
| `昇降機道` | `HoistwaySmokeSeal` |
| `管道間` | `ShaftDoorRating`、`ShaftDoorSmokeSeal` |
| `挑空`、`昇降階梯間`、`樓梯間` | 無（只有第 1 項本文，由既有規則檢討） |

`VerticalCompartmentRequirements.ForUse(use)` 就是這張表。清單外的用字回空集合——「其他類似部分」
無法列舉，不在清單裡就不產生本類別的主體（與面積規則的豁免同一個方向，見 §9）。

### 3.3 六態對照

以昇降機道的防火設備為例：

| 模型情形 | 引擎路徑 | 狀態 |
| --- | --- | --- |
| 遮煙性能 = 是 | 豁免不成立 → 本文成立 | `Pass` |
| 遮煙性能 = 是，昇降機間狀況未填 | 豁免無法判定，但本文成立 | `Pass`（豁免的資料缺口不影響） |
| 遮煙性能 = 否，昇降機間但書成立 | 豁免成立，本文不再評估 | `NotApplicable`／`Exempt` |
| 遮煙性能 = 否，昇降機間但書不成立 | 豁免不成立 → 本文不成立 | `Fail` |
| 遮煙性能 = 否，昇降機間狀況未填 | 本文不成立 + 豁免有資料缺口 | `InsufficientData` |
| 遮煙性能未填 | 實際值缺口 | `InsufficientData` |
| 非防火構造建築物 | 三條規則適用條件均不成立 | `NotApplicable`／`NoRuleApplies` |

管道間維修門的兩項要求沒有豁免，所以少掉中間三列：填了就 `Pass`／`Fail`，沒填就
`InsufficientData`。

### 3.4 為什麼不重寫第 1 項本文的三項要求

垂直區劃是區劃。它的邊界牆已經被 `tw-bcr-79-wall-rating` 要求 60 分鐘、邊界上的開口已經被
`tw-bcr-79-opening` 要求是防火設備、樓地板已經被第 70 條要求防火構造時效——這三條都只看
`element.isCompartmentBoundary` 與 `element.category`，不問區劃是哪一條條文生出來的，所以垂直
區劃不會漏檢討。在本類別再寫一次只會讓同一面牆拿到兩筆結論。這與第 83 條第 5 款的阻熱性被留在
外面是同一條線（[帷幕牆文件](curtain-wall-fire-compartment.md) §5.3）。

還有一個更硬的理由，它決定了「不重寫」不只是整潔問題：

> 引擎只讓**最高優先序中適用的規則**作答，而**適用性無法判定的規則會擋住所有較低優先序**
> （`RuleEngine.Evaluate`，守門測試 `RuleEngineTests`）。

若要讓第 79 條之 2 具名接手牆與開口，那些規則的 `appliesWhen` 勢必要讀 `zone.use`，且優先序要
壓在第 79 條之上。而 `zone.use` 在 `防火檢討_區劃用途` 空白時**根本不存在**
（`ReviewInputAssembler.Convert` 對空值回 `null`），那是一般房間的常態。結果會是：全專案沒有
填區劃用途的牆與門全部變成「資料不足」。本類別的三項要求不會有這個問題——它的主體只在用途已知
時才產生。

守門測試：`Article79_2VerticalCompartmentRuleTests.Article_79_reviews_the_boundary_without_reading_the_zone_use`。

### 3.5 面積豁免訊息要說出誰接手

引擎的豁免訊息只講成立的條件（`符合豁免條件：zone.use == "管道間"`），單看會像「這個區劃沒有被
檢討」。`CompartmentAreaCheck` 因此在豁免成立、且 `zone.use` 屬於垂直區劃清單時，把
`ZoneUses.VerticalCompartmentHandoff` 接在訊息後面，指名第 79 條之 2 第 1 項。面板側對應的是
`ZoneAreaLimit.Description` 的 `第79條／第83條 免適用（第79條之2 垂直區劃）`。

## 4. 幾何解析

**本功能不需要新的幾何解析。** 受檢主體是既有候選集合裡的開口：

- `HoistwaySmokeSeal`：`zone.use == "昇降機道"` 之區劃邊界上的門窗與嵌板（昇降機道出入口）。
- `ShaftDoorRating`／`ShaftDoorSmokeSeal`：`zone.use == "管道間"` 之區劃邊界上的門（維修門）。

也就是 `CandidateSet.OpeningsOf(zoneId)` 已經解出來的那批開口，與 `OpeningProtectionCheck` 用的
同一批。窗不會是維修門，所以 `ShaftDoor*` 只取 `CandidateCategory.Door`；昇降機道出入口則不限
門或嵌板，條文說的是「裝設之防火設備」。這部分屬檢查層，見 §12。

## 5. 規則集擴充

### 5.1 新增 `RuleCategory`

```csharp
public enum RuleCategory
{
    CompartmentArea,
    FireResistance,
    OpeningProtection,
    CompartmentContinuity,
    VerticalCompartment   // 新增
}
```

理由：一個類別對一個主體只給一項要求（§3.1）。硬塞進 `OpeningProtection` 會有兩種壞法——同優先
序 → Conflict；較高優先序 → 取代掉「應為防火設備」那一問，等於把既有檢討換掉而不是加上去。

### 5.2 新增白名單欄位（`shaft.*`，僅 `VerticalCompartment` 可用）

| 欄位 | 型別 | 說明 |
| --- | --- | --- |
| `shaft.requirement` | Text | `HoistwaySmokeSeal` / `ShaftDoorRating` / `ShaftDoorSmokeSeal` |
| `shaft.elementUniqueId` | Text | 受檢防火設備（門窗或嵌板）UniqueId |
| `shaft.providedFireRating` | Quantity(min) | 該防火設備之設計／認證防火時效 |
| `shaft.providedSmokeProtection` | Text | 該防火設備是否具遮煙性能（`是`／`否`） |
| `shaft.elevatorLobbyProtected` | Boolean | 第 2 項：昇降機道前是否設有併同區劃、且出入口具遮煙性能之昇降機間 |

`zone.*` 與 `building.*` 對所有類別開放，所以 `zone.id`、`zone.use`、
`building.fireResistiveConstruction` 直接可用，不另立 `shaft.zoneId`、`shaft.use`。

`shaft.providedFireRating` 不沿用 `element.providedFireRating`：後者只對 `FireResistance` 開放，
而且它的同伴欄位（`element.category`、`element.isStructural`）在這裡沒有語意。做法與
`junction.minFireRating` 相同。

### 5.3 已加入的規則（`Data/fire-review-rules.json`，規則集版本 `2026.6-provisional`）

```json
{
  "ruleId": "tw-bcr-79-2-hoistway-smoke-seal",
  "version": "1",
  "category": "VerticalCompartment",
  "legalReference": "建築技術規則建築設計施工編第79條之2第1項（昇降機道裝設之防火設備應具有遮煙性能）",
  "effectiveDate": "2024-01-01",
  "jurisdiction": "TW",
  "priority": 10,
  "appliesWhen": "building.fireResistiveConstruction == true && shaft.requirement == \"HoistwaySmokeSeal\"",
  "requiredValue": "shaft.providedSmokeProtection == \"是\"",
  "exemptions": [ "shaft.elevatorLobbyProtected == true" ],
  "evidenceFields": [ "zone.id", "zone.use", "shaft.elementUniqueId", "shaft.elevatorLobbyProtected", "building.fireResistiveConstruction" ],
  "severity": "Error"
}
```

```json
{
  "ruleId": "tw-bcr-79-2-shaft-door-rating",
  "version": "1",
  "category": "VerticalCompartment",
  "legalReference": "建築技術規則建築設計施工編第79條之2第1項（管道間之維修門應具有一小時以上防火時效）",
  "effectiveDate": "2024-01-01",
  "jurisdiction": "TW",
  "priority": 10,
  "appliesWhen": "building.fireResistiveConstruction == true && shaft.requirement == \"ShaftDoorRating\"",
  "requiredValue": "shaft.providedFireRating >= 60 min",
  "exemptions": [],
  "evidenceFields": [ "zone.id", "zone.use", "shaft.elementUniqueId", "building.fireResistiveConstruction" ],
  "severity": "Error"
}
```

```json
{
  "ruleId": "tw-bcr-79-2-shaft-door-smoke-seal",
  "version": "1",
  "category": "VerticalCompartment",
  "legalReference": "建築技術規則建築設計施工編第79條之2第1項（管道間之維修門應具有遮煙性能）",
  "effectiveDate": "2024-01-01",
  "jurisdiction": "TW",
  "priority": 10,
  "appliesWhen": "building.fireResistiveConstruction == true && shaft.requirement == \"ShaftDoorSmokeSeal\"",
  "requiredValue": "shaft.providedSmokeProtection == \"是\"",
  "exemptions": [],
  "evidenceFields": [ "zone.id", "zone.use", "shaft.elementUniqueId", "building.fireResistiveConstruction" ],
  "severity": "Error"
}
```

幾個寫法上的決定：

- **三條同優先序 10、以 `shaft.requirement` 互斥。** 不需要用優先序表達先後，因為三項要求同時
  成立、互不取代。
- **法源條文一律含「第79條之2」且不得出現「第83條」。** `FireReviewRunner.HostLegalReferences`
  以子字串比對判斷區劃是不是第 83 條的（帷幕牆文件 §2.5），誤觸會讓帷幕牆交接讀錯區劃來源。
  守門測試 `Every_rule_cites_article_79_2`。
- **`appliesWhen` 都帶 `building.fireResistiveConstruction == true`。** 第 1 項本文的主語是
  「防火構造建築物內之……」。非防火構造建築物由第 80 條、第 81 條另管（未實作）。
- **第 2 項寫成豁免條件，不寫進要求值。** 與第 79 條的九十公分但書同一個理由（帷幕牆文件 §5.4）：
  頂層不是比較運算的式子會被規則編譯器以 `InvalidForm` 退回，而且拆開後六態的每個邊界都自然
  正確（§3.3）。

## 6. 參數需求

| 參數 | 類型 | 綁定 | 用途 | 現況 |
| --- | --- | --- | --- | --- |
| `防火檢討_設計防火時效` | Type（既有，GUID `…0008`） | **本輪加綁 Doors**（既有 Walls、Columns、Floors、Curtain Panels） | `shaft.providedFireRating` 來源 | 已完成 |
| `防火檢討_遮煙性能` | Type、YESNO、GUID `…000f` | Doors、Windows、Curtain Panels | `shaft.providedSmokeProtection` 來源 | 已完成 |
| `防火檢討_區劃用途` | Instance（既有） | Areas | 決定一個區劃產生哪些主體 | 已存在，下拉見 [`zone.use` 用字表](zone-use-vocabulary.md) |

兩份定義檔都要改：`assets/SharedParameters/fire-review-shared-params.txt`（主檔，手動加入專案參數時選它）
與 `assets/SharedParameters/fire-review-openings-type.txt`（revit-mcp `load_shared_parameters` 綁門／窗／
帷幕嵌板類型用的那份，本輪同時補上 `防火檢討_設計防火時效`）。**兩檔一律維持 Big5／cp950。**

`shaft.providedFireRating` 只綁在**門**：維修門是門，窗與嵌板不是（§4）。`防火檢討_遮煙性能` 則綁三類，
昇降機道出入口可能是門、窗或帷幕嵌板。

一個參數同時回答兩個欄位（`element.providedFireRating` 與 `shaft.providedFireRating`）帶來兩個連帶處理：

- `ReviewInputSources.ParameterNames` 與 `RevitReviewParameterReader.Names(host)` 都要去重，否則同一個
  名字讀兩次會讓讀取器的 `ToDictionary` 以重複鍵例外收場。
- 前置檢查的缺參數訊息分成兩句：專案完全沒有這個參數時說「專案沒有參數 X」，有參數但沒綁到這個欄位
  需要的類別時說「參數 X 沒有綁定到 門」。同一個參數因此會出現兩筆阻擋項，各自說自己缺的類別。

`shaft.elevatorLobbyProtected` 沒有參數來源：昇降機間是不是「併同區劃」是一個空間關係，不是一個
可以掛在門上的性質。§9 記錄了這個缺口。

`防火檢討_遮煙性能` 與 `防火檢討_設計防火保護` 是兩個問題，不可合併：後者答「是不是防火門窗等
防火設備」，前者答「這個防火設備有沒有通過遮煙試驗」。條文對昇降機道同時要求兩者。

## 7. Revit 產出

未開始。預期與 `OpeningProtection` 同形：受檢設備在檢討視圖上標示、檢討表新增三列（列名取
`VerticalCompartmentRequirements.Label`）。

## 8. 程式組成

| 類別 | 責任 | 現況 |
| --- | --- | --- |
| `RuleCategory.VerticalCompartment` | 類別 | 已完成 |
| `RuleFieldCatalog` 的 `shaft.*` | 白名單欄位 | 已完成 |
| `VerticalCompartmentRequirement`／`VerticalCompartmentRequirements` | 三項要求的用字、標籤與所屬用途 | 已完成 |
| `ReviewCheckTypes.VerticalCompartment` | 結果歸檔的檢討類型 | 已完成 |
| `ZoneUses.VerticalCompartmentHandoff` | 面積豁免訊息指向第 79 條之 2 | 已完成 |
| `VerticalCompartmentCheck`／`VerticalCompartmentInputs` | 由候選開口與輸入產生主體、跑引擎、產生結果 | **未開始** |
| `FireReviewRunner` 的接線 | 把新檢查併入一次檢討 | **未開始** |
| `ReviewTable`／`ReviewMarkup` | 檢討表列與視圖標示 | **未開始** |
| `SmokeProtectionParameters.Provided` | 參數名（`防火檢討_遮煙性能`），與 `FireProtectionParameters` 分立 | 已完成 |
| `ReviewInputSources` 的兩筆 `shaft.*` 來源 | 讀取器、前置檢查都照 `All` 跑，不需另接串接層 | 已完成 |
| `FireReviewTypeRow.ProvidedSmokeProtection`／批次面板「遮煙性能」欄 | 使用者填值的地方 | 已完成 |

## 9. 已知限制

1. **第 2 項只模型化兩個但書的共通效力。** 兩者都讓昇降機道出入口免遮煙，但前者連「應裝設防火
   設備」本身都放寬。工具只放寬遮煙，防火設備仍由 `tw-bcr-79-opening` 要求——安全方向（多要求
   一個防火設備，不會漏檢討）。
2. **`shaft.elevatorLobbyProtected` 沒有輸入來源。** 「昇降機間併同區劃」是空間關係，
   「其出入口具遮煙性能」是另一扇門的性質。沒填就是 `InsufficientData`（遮煙未達時），不會誤判
   未符合。
3. **第 3 項（挑空的兩款免除）未實作。** 需要兩個目前沒有的事實：挑空連跨的樓層數，以及該挑空
   是否為「避難層通達直上層或直下層」。挑空目前只是「免於面積檢討」，本功能也不對它提出要求，
   所以第 3 項不成立時工具同樣不會提出要求——這個方向是**寬鬆**的，與清單外用字不豁免的保守
   方向相反，必須記在這裡。
4. **第 4 項（區劃範圍內得設公共廁所等，其裝修應為耐燃一級）未實作。** 需要區劃內附屬空間的
   概念，目前資料模型沒有。
5. **「其他類似部分」沒有任何表達方式。** 與面積規則的豁免清單同一個限制
   （[`zone.use` 用字表](zone-use-vocabulary.md)）。要支援應另加是非參數，不要擴充用字。
6. **地下建築物的第 203 條未實作。** 條文幾乎與第 79 條之 2 第 1、2 項同字，但**樓梯也要遮煙**，
   且區劃對象是「與其他部分之間」。不可直接套用本功能的規則。
7. **遮煙性能無法由模型推導。** 它是第 1 條第 45 款的試驗結果，和阻熱性一樣只能由設計者宣告。
8. **一扇維修門會產生兩筆結果**（時效、遮煙）。這是 §3.1 的直接後果；檢討表與標示要設計得讓
   使用者看得懂同一扇門為什麼出現兩次。

## 10. 測試案例

`tests/BuildingRegulationReview.Core.Tests/Rules/Article79_2VerticalCompartmentRuleTests.cs`
（規則層，跑的是實際出貨的規則檔）：

| 測試 | 守的事 |
| --- | --- |
| `Shipped_rule_set_compiles_the_three_vertical_compartment_rules` | 三條規則存在、欄位都在本類別可用 |
| `The_three_rules_are_one_priority_and_mutually_exclusive` | 同優先序、`shaft.requirement` 一對一，不會落入 Conflict |
| `Every_rule_cites_article_79_2` | 法源條文含第 79 條之 2、不含第 83 條 |
| `Shaft_fields_are_hidden_from_the_other_categories` | `shaft.*` 只有本類別可讀；本類別讀不到 `element.*`／`opening.*`／`junction.*` |
| `Each_requirement_of_the_vocabulary_has_a_rule_that_answers_it` | 程式的用字與規則檔的字樣一致 |
| `Each_requirement_belongs_to_a_use_the_panel_offers` | 每項要求的用途都在 `ZoneUses.VerticalCompartments` 裡 |
| `Only_the_hoistway_and_the_shaft_carry_extra_requirements` | 挑空／昇降階梯間／樓梯間不產生本類別的主體 |
| 昇降機道六態（`Pass`／`Fail`／`Exempt`／兩種 `InsufficientData`） | §3.3 的每一列 |
| 管道間維修門 60／120／59／30 分鐘 | 一小時門檻的兩側邊界 |
| `The_elevator_lobby_proviso_does_not_reach_the_shaft_door` | 第 2 項但書只給昇降機道，不給維修門 |
| `A_building_that_is_not_fire_resistive_is_out_of_scope` | 非防火構造 → `NoRuleApplies` |
| `A_requirement_outside_the_vocabulary_matches_no_rule` | 不認得的要求是不適用，不是默默通過 |
| `Article_79_reviews_the_boundary_without_reading_the_zone_use` | §3.4 的硬理由：第 79 條的規則不得讀 `zone.use` |

`tests/BuildingRegulationReview.Core.Tests/Checks/CompartmentAreaCheckTests.cs`：

| 測試 | 守的事 |
| --- | --- |
| `An_exempt_vertical_compartment_is_told_which_article_takes_over` | 面積豁免訊息指名第 79 條之 2 |
| `An_exemption_that_is_not_a_vertical_compartment_gets_no_such_note` | 其他豁免不會被安上第 79 條之 2 |

## 11. 決議紀錄

| # | 決議 | 理由 |
| --- | --- | --- |
| 1 | 受檢主體是（防火設備, 要求）這一對，不是元素 | 一扇維修門同時欠兩項要求，而 `requiredValue` 只能寫一個比較（§3.1） |
| 2 | 新增 `RuleCategory.VerticalCompartment`，不塞進 `OpeningProtection` | 同優先序會 Conflict、較高優先序會取代掉既有檢討（§5.1） |
| 3 | 第 1 項本文的三項要求不在本類別重寫 | 既有規則不分區劃來源已涵蓋；且重寫必須讀 `zone.use`，會讓全專案未填用途的牆與門變成資料不足（§3.4） |
| 4 | `shaft.*` 自己一組欄位，不借用 `element.*` | 可用類別不同，同伴欄位在這裡沒有語意；同 `junction.*` 做法（§5.2） |
| 5 | 第 2 項只放寬遮煙，不放寬「應裝設防火設備」 | 兩個但書效力不同，取共通且較嚴的那一邊（§9 第 1 項） |
| 6 | 遮煙性能另立 `防火檢討_遮煙性能`，不併入 `防火檢討_設計防火保護` | 兩者是不同的問題，條文對昇降機道同時要求（§6） |
| 7 | 規則集版本升為 `2026.6-provisional` | 新增規則就升版，與步驟 7 加入 `tw-bcr-83-area` 時同一做法 |
| 8 | `防火檢討_設計防火時效` 加綁**門**，批次面板的「設計防火時效」欄對門開放 | 沒有它，管道間維修門的一小時時效永遠是資料不足。窗不開放——沒有任何條文對窗訂時效（§6） |
| 9 | 遮煙性能綁門／窗／帷幕嵌板三類，時效只綁門 | 昇降機道出入口的「防火設備」可能是門、窗或嵌板；維修門只會是門（§4） |
| 10 | 前置檢查的缺參數訊息分成「專案沒有參數」與「沒有綁定到 門」兩句 | 一個參數回答兩個欄位後，舊訊息會在參數明明存在時說「專案沒有參數」（§6） |

## 12. 實作進度

| 步驟 | 內容 | 狀態 |
| --- | --- | --- |
| 1 | 本文件：條文、用語、主體設計、限制 | **已完成** |
| 2 | 規則層：`RuleCategory.VerticalCompartment`、`shaft.*` 欄位、三條規則、要求用字、面積豁免訊息 | **已完成** |
| 3 | Shared Parameter `防火檢討_遮煙性能`（Big5 定義檔、輸入來源、讀取器、批次參數面板欄位） | **已完成** |
| 4 | `VerticalCompartmentCheck`／`VerticalCompartmentInputs`：由候選開口產生主體並跑引擎 | 未開始 |
| 5 | `FireReviewRunner` 接線、`ReviewTable` 三列、`ReviewReadiness` 參數需求 | 未開始 |
| 6 | 檢討視圖標示與圖號 | 未開始 |
| 7 | 第 3 項挑空的兩款免除（需要連跨樓層數與避難層通達的事實） | 未開始 |

### 步驟 1、2 的驗證

- `dotnet build BuildingRegulationReview.sln --no-incremental`：0 警告 0 錯誤。
- `dotnet build src\BuildingRegulationReview\BuildingRegulationReview.csproj --no-incremental`：
  0 警告 0 錯誤。
- `dotnet test tests\BuildingRegulationReview.Core.Tests`：1339 通過、0 失敗（原 1307，新增 32）。
- 沒有既有測試需要改寫；唯一調整的既有常數是 `FireReviewIntegrationTests.ShippedVersion`
  （`2026.5-provisional` → `2026.6-provisional`）。
- **未實機驗證。** 本階段沒有任何 Revit 端改動，不需要開模型；步驟 3 起才需要。

### 步驟 3 的產出與驗證

改動的檔案：

- `assets/SharedParameters/fire-review-shared-params.txt`：新增 GUID `…000f` 的 `防火檢討_遮煙性能`
  （YESNO），並把 `防火檢討_設計防火時效` 的說明改成「牆柱樓板、門窗帷幕嵌板的類型；梁不適用」。
- `assets/SharedParameters/fire-review-openings-type.txt`：新增 `防火檢討_遮煙性能` 與
  `防火檢討_設計防火時效`（GUID 沿用 `…0008`），並在檔頭寫下兩個參數為何不可合併。
  兩檔都以 `[System.Text.Encoding]::GetEncoding(950)` 讀寫，`git diff` 確認其餘行的位元組未變。
- `src/BuildingRegulationReview.Application/Checks/VerticalCompartmentInputs.cs`：新增
  `SmokeProtectionParameters.Provided`。
- `src/BuildingRegulationReview.Application/Reviews/ReviewInputSources.cs`：新增
  `shaft.providedFireRating`（門、Type）與 `shaft.providedSmokeProtection`（門窗嵌板、Type）兩筆來源；
  `ParameterNames` 去重。
- `src/BuildingRegulationReview.Application/Reviews/ReviewReadiness.cs`：缺參數訊息分成兩句（決議 10）。
- `src/BuildingRegulationReview.Revit/Reviews/RevitReviewParameterReader.cs`：`Names(host)` 去重。
- `src/BuildingRegulationReview.Application/Parameters/FireReviewTypeTable.cs`：
  `FireReviewTypeRow.ProvidedSmokeProtection`、`CarriesSmokeProtection`、
  `FireReviewTypeParameters.SmokeSeal`，`CarriesRating` 改為門也算（決議 8）。
- `src/BuildingRegulationReview.Revit/Parameters/RevitFireReviewTypeScanner.cs`：讀取新參數。
- `src/BuildingRegulationReview/FireReview/FireReviewTypeRowViewModel.cs`、
  `FireReviewParameterPanelWindow.xaml`：批次面板新增「遮煙性能」勾選欄與說明。

測試：`dotnet test` **1344 通過、0 失敗**（原 1339）。新增 5 項（門也填時效、每個開口都有遮煙性能欄、
未綁定與未勾選要分得開、遮煙性能不是防火保護、缺綁定的兩種訊息）；改寫 4 項既有測試——它們原本假設
`設計防火時效` 只有一個欄位在讀、門不填時效。`BuildingRegulationReview.sln` 與 WPF 外掛專案皆 0 警告
0 錯誤。

**未實機驗證。** 需要在 Revit 中以 `load_shared_parameters` 依 `fire-review-openings-type.txt` 重新綁定
門／窗／帷幕嵌板類型（新增兩個參數），再開批次面板確認「遮煙性能」欄可勾選、門的「設計防火時效」欄
可輸入、寫入模型後回讀正確。**模型若已綁舊的 openings 定義檔，只是少兩個參數，不必移除重綁。**
