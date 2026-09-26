# 垂直區劃：第 79 條之 2

功能 ID：`vertical-compartment`

狀態：**規則層、參數層、檢查層、一次檢討的接線與檢討視圖標示已完成**（本文件 §5、§6、§7、§8、§10）。
按「開始檢討」會產生第 79 條之 2 的結果、檢討表第五列與三條要求列，證據基線看得見遮煙性能，未符合的
防火設備會在檢討視圖被塗紅、描述帶出是哪幾項要求未符合（不出註解、不發圖號，§7.2）。第 3 項挑空的
兩款免除未開始（§12 步驟 7）。

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
門或嵌板，條文說的是「裝設之防火設備」。這張對照表是
`VerticalCompartmentRequirements.Categories(requirement)`。

與 `OpeningProtectionCheck` 同樣的兩條界線也適用：區劃範圍有問題（未封閉、重疊）時該區劃的開口
是**人工覆核**，開口與區劃的關係有疑義（帷幕牆、未寄主、連結模型）時也是，兩者都不跑規則。
後者在本類別仍然產生一筆結果而不是默默消失——一扇關係有疑義的昇降機道出入口若被略過，檢討表的
件數就會少算一件。

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

### 7.1 檢討表（已完成）

`ReviewTable` 第五列「垂直區劃」（`ReviewCheckTypes.VerticalCompartment`），統計以
`ReviewTableGrouping.ShaftRequirement` 分成三條要求列，列名取 `VerticalCompartmentRequirements.Label`：

| 要求列 | 受檢設備 | 讀的欄位 |
| --- | --- | --- |
| 昇降機道防火設備遮煙性能 | 昇降機道的門、窗、帷幕嵌板 | `shaft.providedSmokeProtection` |
| 管道間維修門防火時效 | 管道間的門 | `shaft.providedFireRating` |
| 管道間維修門遮煙性能 | 管道間的門 | `shaft.providedSmokeProtection` |

三條列**固定照條文順序**排，不照這次檢討遇到它們的順序（`ReviewTable.RequirementOrder`）；否則同一
份專案的不同樓層會因為先遇到昇降機道還是管道間而排出不同順序。列名是從結果的
`shaft.requirementLabel` 證據讀回來的——檢討表是只讀已存結果重建的，不會回頭讀模型。一扇維修門
因此在兩條列裡各出現一次，這是 §3.1 的直接後果，不是重複。

日誌另有一行 `垂直區劃（第79條之2第1項）：…`，把三條列的件數與狀態寫出來（`FireReviewRunner.Findings`）。
**空的列也會寫**：「昇降機道防火設備遮煙性能 0 件未檢討」與整列沒檢討讀起來一樣，沒有件數分不出來。

### 7.2 檢討視圖標示（已完成）

垂直區劃的未符合**只塗紅，不出註解、不發圖號**，與門窗（`OpeningProtection`）和帷幕牆的 CW-O 同形：

| 產出 | 垂直區劃 | 理由 |
| --- | --- | --- |
| 元素紅色覆寫（`PlannedElementOverride`） | **有** | 未符合的是這扇設備本身 |
| 文字標註（`PlannedReviewNote`） | 沒有 | 未符合的是「設備性能不足」，不是平面上的某個位置；沒有交接處那種「要標在哪裡」的幾何問題 |
| 填滿區域（`PlannedReviewRegion`／`PlannedSpandrelBand`） | 沒有 | 受檢主體是開口，不是區劃面積，也不是層間帶 |
| 檢討圖號（`CurtainWallMarkNumbers`） | 沒有 | 圖號的用途是把檢討表的列、標註與產生的立面綁在一起；沒有註解也沒有產生的視圖，號碼沒有地方出現、也沒有東西可以對照（同 CW-O） |

**一個元素只塗紅一次，不論它欠幾項要求。** `ReviewMarkupPlan` 依 `table.Entries` 泛用地跑，第 79 條之 2
的 Fail 落在最後的「element override」分支（不是面積、不是連結模型、沒有 `JunctionKind`），再依元素
group 起來，所以一扇兩項都不符合的維修門是**一筆** `PlannedElementOverride` 帶**兩個** `ResultIds`。
元素是紅的或不是紅的，塗第二次不會多說任何事。

**要求名要寫進標示的描述。** 否則同一扇門在檢討表出現兩次、在標示紀錄也出現兩次，讀起來像工具講了
兩遍（§9 第 8 項）。`PlannedElementOverride` 因此多一個 `ShaftRequirements`（照條文順序、去重），
`Description` 是：

```
未符合門「SD1」 D-shaft（管道間維修門防火時效、管道間維修門遮煙性能）
```

要求名與檢討表的列名同樣是從 `shaft.requirementLabel` **證據**讀回來的（決議 17 的同一個理由），
`ShaftRequirements` 對其他四個檢討類型是空的——只有第 79 條之 2 把一個元素拆成多個受檢主體。
略過的標示（`SkippedReviewMark.Subject`）也帶要求名，理由相同：一扇位於連結模型的維修門會被略過兩次。

排序用 `VerticalCompartmentRequirements.Order`，檢討表的三條要求列與這裡共用同一個函式（原本是
`ReviewTable` 的私有 `RequirementOrder`）。

擁有權機制不變：`PlannedElementOverride` 沒有 `Signature`，`ReviewMarkupDiff` 對元素覆寫只比
`ElementUniqueId` 與 `RunId`，所以描述改字不會讓既有標示被判成需重建。

## 8. 程式組成

| 類別 | 責任 | 現況 |
| --- | --- | --- |
| `RuleCategory.VerticalCompartment` | 類別 | 已完成 |
| `RuleFieldCatalog` 的 `shaft.*` | 白名單欄位 | 已完成 |
| `VerticalCompartmentRequirement`／`VerticalCompartmentRequirements` | 三項要求的用字、標籤與所屬用途 | 已完成 |
| `ReviewCheckTypes.VerticalCompartment` | 結果歸檔的檢討類型 | 已完成 |
| `ZoneUses.VerticalCompartmentHandoff` | 面積豁免訊息指向第 79 條之 2 | 已完成 |
| `VerticalCompartmentCheck` | 由候選開口與 `zone.use` 產生主體、跑引擎、產生結果與三列統計 | 已完成 |
| `VerticalCompartmentInputs`／`ShaftDeviceProperties` | 每個開口 Type 的遮煙性能與設計防火時效 | 已完成 |
| `ReviewInputAssembler` 的 `ReviewInputAssembly.VerticalCompartment` | 從參數快照組出上一列 | 已完成 |
| `FireReviewRunner` 的接線 | 把新檢查併入一次檢討（第 5 段、進度 5/6、三列統計寫進日誌） | 已完成 |
| `ReviewTable` 的第五列與 `ReviewTableGrouping.ShaftRequirement` | 檢討表列與三條要求列（§7.1） | 已完成 |
| `ReviewBaselineBuilder` 的 `shaftDevice｜` 行 | 遮煙性能與門的設計防火時效納入證據基線 | 已完成 |
| `ReviewMarkupPlan` 的 `PlannedElementOverride.ShaftRequirements` | 標示描述帶要求名；不出註解、不發圖號（§7.2） | 已完成 |
| `VerticalCompartmentRequirements.Order` | 條文順序，檢討表要求列與標示描述共用 | 已完成 |
| `RevitReviewViewMarker` | 不需改動——它只把 `Description` 寫進標示紀錄 | 已完成 |
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
8. ~~**一扇維修門會產生兩筆結果**（時效、遮煙）。~~ **步驟 6 已解決**：這是 §3.1 的直接後果。
   檢查層把要求名寫進結果訊息與 `shaft.requirementLabel` 證據，檢討表（§7.1）分成兩條要求列，
   標示（§7.2）把要求名寫進 `PlannedElementOverride.Description` 與略過訊息的主體。門仍然只塗紅
   一次——這是對的，元素是紅的或不是紅的。守門測試
   `A_maintenance_door_failing_both_requirements_is_painted_once_and_the_mark_names_both`。
9. ~~**證據基線還沒有看到遮煙性能。**~~ **步驟 5 已解決**：`ReviewBaselineBuilder` 為每個候選開口
   寫一行 `shaftDevice｜`，收 `遮煙性能` 與（門的）`設計防火時效`。兩者都不在其他輸入的涵蓋範圍內
   ——維修門是開口，不是構件，所以 `rating｜` 那行從來看不到它——改了任一個，既有檢討就會變成
   「需更新」（守門測試 `Unticking_a_types_smoke_seal_makes_the_stored_run_need_an_update`、
   `Changing_a_maintenance_doors_rating_makes_the_stored_run_need_an_update`）。**基線格式因此改變，
   步驟 5 之前存下的檢討紀錄會一次性變成「需更新」，重跑一次即可。**
10. **`zone.use` 判讀不出來時該區劃不產生任何主體。** 一個區劃由多個 Area 組成、各 Area 的
   `防火檢討_區劃用途` 填得不一致時，`ReviewInputAssembler` 會把它變成 `Unreadable`，本檢查因此
   讀不到用途、不產生主體。這個不一致由 `CompartmentAreaCheck` 報出來（它會是該區劃的資料不足），
   不會沒有人講；但本類別的件數會少算，方向是**寬鬆**的，與第 3 項同一類缺口。

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

`tests/BuildingRegulationReview.Core.Tests/Checks/VerticalCompartmentCheckTests.cs`
（檢查層，同樣跑實際出貨的規則檔）：

| 測試 | 守的事 |
| --- | --- |
| `A_smoke_sealed_hoistway_door_passes` | 主體成立、命中昇降機道規則、歸檔在 `ReviewCheckTypes.VerticalCompartment` |
| `A_hoistway_door_without_a_smoke_seal_waits_for_the_elevator_lobby_proviso` | 遮煙未達不是未符合，缺口是 `shaft.elevatorLobbyProtected`（§9 第 2 項） |
| `A_hoistway_holds_a_window_and_a_curtain_panel_to_the_smoke_seal_too` | 昇降機道出入口不限門（§4） |
| `A_protected_elevator_lobby_is_reported_as_exempt` | 第 2 項但書的 `Exempt` 路徑（但書目前沒有輸入來源，故直接餵 facts） |
| `One_maintenance_door_answers_two_requirements` | 一扇維修門兩筆結果，訊息各自指名要求（§9 第 8 項） |
| `A_maintenance_door_needs_a_full_hour` | 60／59 分鐘的門檻兩側 |
| `A_window_on_a_shaft_boundary_is_no_maintenance_door` | 管道間只取門（§4） |
| `A_use_with_no_extra_requirement_produces_no_subject` | 挑空／昇降階梯間／樓梯間／清單外用字／未填用途都不產生主體 |
| `A_building_that_is_not_fire_resistive_is_out_of_scope` | 非防火構造 → 每個主體 `NoRuleApplies` |
| `An_unbound_smoke_seal_is_insufficient_data_and_names_its_own_parameter` | 證據指向 `防火檢討_遮煙性能`，不是 `防火檢討_設計防火保護`（決議 6） |
| `An_unbound_maintenance_door_rating_names_the_rating_parameter` | 時效缺口指向 `防火檢討_設計防火時效` |
| `A_subject_carries_only_the_field_its_requirement_reads` | 遮煙主體不報時效的缺口，一個缺參數不會被算兩次 |
| `A_maintenance_door_with_no_single_rating_is_manual_review` | 「1hr/2hr」是人工覆核，與構件防火時效同一做法 |
| `An_opening_of_a_zone_whose_extent_is_in_doubt_is_manual_review` | 沒有封閉面積的區劃在前置檢查就擋下 |
| `An_ambiguous_opening_relation_is_manual_review` | 關係有疑義的開口仍產生一筆人工覆核，不會從件數消失（§4） |
| `The_three_rows_are_always_reported_even_when_empty` | 檢討表三列恆存在、列名取 `VerticalCompartmentRequirements.Label` |
| `A_device_of_a_type_the_package_has_no_opening_of_is_reported_as_a_warning`／`A_zone_that_is_not_in_the_package_…` | 輸入指到工作包外的東西是警告，不是靜默 |

`tests/BuildingRegulationReview.Core.Tests/Reviews/FireReviewIntegrationTests.cs`：

| 測試 | 守的事 |
| --- | --- |
| `Every_opening_type_is_assembled_into_the_vertical_compartment_inputs` | 組裝層讀開口 Type 的兩個參數，並與其他檢查共用同一份 `CompartmentAreaInputs` |
| `An_unbound_smoke_seal_names_its_own_parameter_and_not_the_protection_one` | `ReviewInputAssembler.Protection` 的參數名引數（決議 11） |
| `Review_runs_every_check_and_leaves_the_package_reviewed_with_the_rule_set_locked` | 六段進度（`垂直區劃（5/6）`）、沒有垂直區劃用途時第五列是未檢討 |
| `A_shaft_maintenance_door_is_two_results_of_the_fifth_review_table_row` | 接線成立：一扇維修門兩筆結果、兩條要求列、法源含第 79 條之 2、日誌寫出三列件數（含空列的 0 件） |
| `A_maintenance_door_short_of_an_hour_fails_only_the_rating_row` | 兩項要求各自判定，時效未達不會拖累遮煙那一列（決議 12 的端到端版） |
| `Unticking_a_types_smoke_seal_makes_the_stored_run_need_an_update` | 證據基線看得見遮煙性能；重跑後反映新值（§9 第 9 項） |
| `Changing_a_maintenance_doors_rating_makes_the_stored_run_need_an_update` | 門的設計防火時效同樣進基線——維修門是開口，不會被 `rating｜` 那行收到 |
| `A_zone_use_outside_the_vocabulary_produces_no_vertical_compartment_subject` | 清單外用途不產生主體，整列未檢討且日誌不寫三列（§9 第 10 項） |

`tests/BuildingRegulationReview.Core.Tests/Reviews/ReviewTableTests.cs`：

| 測試 | 守的事 |
| --- | --- |
| `A_clean_model_reads_pass_on_all_three_rows` | 檢討表五列的列名與順序 |
| `Vertical_compartment_results_are_counted_by_requirement_in_clause_order` | 三條要求列照條文順序、一扇維修門在兩條列各一次（決議 16） |
| `A_stored_requirement_label_is_what_the_row_shows` | 列名取自已存證據，不由新版用字覆寫（決議 17） |

`tests/BuildingRegulationReview.Core.Tests/Reviews/ReviewMarkupTests.cs`（§7.2）：

| 測試 | 守的事 |
| --- | --- |
| `A_maintenance_door_failing_both_requirements_is_painted_once_and_the_mark_names_both` | 一筆 override、兩個 `ResultIds`，描述帶兩項要求且照條文順序（決議 21） |
| `A_vertical_compartment_failure_is_only_painted_red_and_carries_no_number_or_note` | 沒有註解、沒有填滿區域、`Numbers` 是空的（決議 20）；沒有 Type 名稱時描述仍成立 |
| `The_wording_a_mark_shows_is_the_one_the_result_stored` | 要求名取自 `shaft.requirementLabel` 證據（同決議 17） |
| `A_skipped_vertical_compartment_result_is_named_by_its_requirement` | 連結模型的維修門被略過兩次，兩行主體分得開 |
| `A_door_that_fails_a_shaft_requirement_and_an_opening_check_is_still_one_mark` | 兩個檢討類型共用一筆標示，只有第 79 條之 2 的要求被寫出來 |
| `Re_running_takes_a_shaft_doors_mark_over_instead_of_painting_it_again` | 擁有權不變：改描述不會讓既有標示被重建（決議 22）；修好一項要求後仍是同一筆標示 |

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
| 11 | `ReviewInputAssembler.Protection` 多一個參數名引數，不另寫一個同形函式 | 遮煙性能與設計防火保護的讀法完全相同（決議 6 之下的三態讀法），差別只有資料不足時要指名哪個參數；複製一份會讓兩邊各自漂移 |
| 12 | 每個受檢主體只帶**它自己的要求要讀的那一個欄位** | 一扇維修門的兩個主體若都帶時效與遮煙，同一個缺參數會在兩筆結果各報一次缺口，使用者看到的缺項數量會是實際的兩倍（守門測試 `A_subject_carries_only_the_field_its_requirement_reads`） |
| 13 | 關係有疑義的開口在本類別仍產生一筆人工覆核結果，不沿用 `OpeningProtectionCheck` 的「略過、另由 `set.Ambiguities` 收」做法 | 本類別的主體是（設備, 要求）這一對，略過會讓檢討表的件數少算；而 `set.Ambiguities` 一筆只能對一個元素，無法拆成兩項要求（§4） |
| 14 | `ReviewInputAssembly` 為每個開口 Type 都造一筆 `ShaftDeviceProperties`，不先篩出垂直區劃的開口 | 組裝層不知道哪些區劃是垂直區劃——那是檢查層讀 `zone.use` 才知道的事。步驟 5 把遮煙性能納入證據基線時也需要看見全部的 Type |
| 15 | 垂直區劃是檢討表的**第五列**，放在帷幕牆區劃交接之後；沒有主體時是「未檢討」而不是消失 | `ReviewTable.CheckTypes` 的列固定存在，空列讀作未檢討——這層樓沒有昇降機道、管道間，跟沒檢討是兩件事，但表格算的是結果而不是列（spec 11.7）。要分開就靠日誌那行的件數 |
| 16 | 三條要求列固定照條文順序排，不照這次檢討遇到的順序 | 其他列（例如帷幕牆的三種交接）也是照規格列的順序；照遇到的順序會讓同一份專案的不同樓層排出不同順序，使用者無法對照 |
| 17 | 列名從 `shaft.requirementLabel` **證據**讀回來，讀不到才退回 `VerticalCompartmentRequirements.Label` | 檢討表是只讀已存結果重建的（spec 11.7），不能回頭讀模型；而舊版結果的用字要照它當時存下來的顯示，不可被新版用字覆寫 |
| 18 | `shaftDevice｜` 這行對**每個候選開口**都寫，不只寫垂直區劃的開口 | 基線是在檢查之外建的，和決議 14 同一個理由：這一層不知道哪些區劃是垂直區劃。而且改了用途之後才發現某扇門的遮煙性能早就變過，也必須算「需更新」 |
| 19 | 垂直區劃的 `Context`（建築、區劃輸入）也進 context 指紋（`shaft.building`／`shaft.zone`） | 與 `rating`／`protection` 同形。組裝出來的三者其實是同一個 `CompartmentAreaInputs`，多寫一次不會漏；但若有人只傳垂直區劃輸入，`zone.use` 仍然要在基線裡 |
| 20 | 垂直區劃的未符合**只塗紅**，不出文字標註、不發檢討圖號 | 未符合的是「這扇設備的性能不足」，不是平面上的某個位置，沒有帷幕牆交接那種「要標在哪裡」的幾何問題；圖號的用途是綁住檢討表列、標註與產生的立面，沒有註解也沒有產生的視圖時，號碼沒有地方出現（§7.2，同 CW-O 的理由）。守門測試 `A_vertical_compartment_failure_is_only_painted_red_and_carries_no_number_or_note` |
| 21 | 一個元素**只塗紅一次**，要求名寫進 `Description` 與略過訊息，不改成一元素多筆標示 | 元素是紅的或不是紅的，塗第二次不會多說任何事；但不寫要求名，同一扇門在檢討表與標示紀錄都出現兩次，讀起來像工具講了兩遍（§9 第 8 項）。要求名從 `shaft.requirementLabel` 證據讀回來（同決議 17） |
| 22 | 改 `Description` 不動擁有權機制 | `PlannedElementOverride` 沒有 `Signature`，`ReviewMarkupDiff` 對元素覆寫只比 `ElementUniqueId` 與 `RunId`；若描述進了簽章，改一句話就會讓全模型既有標示被判成需重建 |

## 12. 實作進度

| 步驟 | 內容 | 狀態 |
| --- | --- | --- |
| 1 | 本文件：條文、用語、主體設計、限制 | **已完成** |
| 2 | 規則層：`RuleCategory.VerticalCompartment`、`shaft.*` 欄位、三條規則、要求用字、面積豁免訊息 | **已完成** |
| 3 | Shared Parameter `防火檢討_遮煙性能`（Big5 定義檔、輸入來源、讀取器、批次參數面板欄位） | **已完成** |
| 4 | `VerticalCompartmentCheck`／`VerticalCompartmentInputs`：由候選開口產生主體並跑引擎 | **已完成** |
| 5 | `FireReviewRunner` 接線、`ReviewTable` 三列、證據基線納入遮煙性能 | **已完成** |
| 6 | 檢討視圖標示與圖號（只塗紅、描述帶要求名、不發圖號） | **已完成** |
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

### 步驟 4 的產出與驗證

改動的檔案：

- `src/BuildingRegulationReview.Application/Checks/VerticalCompartmentInputs.cs`：
  `VerticalCompartmentRequirements` 新增 `RequirementField`／`ElementField`／`FireRatingField`／
  `SmokeProtectionField` 四個欄位名常數、`ActualField(requirement)`（決議 12）與
  `Categories(requirement)`（§4）；新增 `ShaftDeviceProperties` 與 `VerticalCompartmentInputs`。
- `src/BuildingRegulationReview.Application/Checks/VerticalCompartmentCheck.cs`（新檔）：
  `VerticalCompartmentCheck`、`VerticalCompartmentFinding`、`VerticalCompartmentGroupSummary`、
  `VerticalCompartmentReview`。
- `src/BuildingRegulationReview.Application/Reviews/ReviewParameterSnapshot.cs`：
  `ReviewInputAssembly.VerticalCompartment`；`Assemble` 為每個開口 Type 造一筆
  `ShaftDeviceProperties`；`ReviewInputAssembler.Protection` 多一個參數名引數（決議 11）。

做法上值得記下來的三件事：

- **facts 從區劃長出來，不用 `CandidateFacts.ForOpening`。** `opening.*` 在本類別不可讀（§5.2），
  所以主體的 facts 是 `CandidateFacts.ForZone` 加上 `shaft.requirement`、`shaft.elementUniqueId`
  與該要求要讀的那一個欄位。
- **時效沿用 `ReviewInputAssembler.Rating`、遮煙沿用 `Protection`。** 兩者的 `Missing`／`Unreadable`
  ／`Undeterminable` 三態與既有檢查完全一致，「1hr/2hr」一樣是人工覆核。
- **`zone.use` 從區劃輸入讀出來，而不是從 facts。** 要求清單必須在造 facts 之前決定，因為它決定
  有沒有主體。判讀不出來的用途視同沒有用途（§9 第 10 項）。

測試：`dotnet test` **1369 通過、0 失敗**（原 1344）。新增 25 項（`VerticalCompartmentCheckTests`
17 項含 Theory 展開共 23 個案例，`FireReviewIntegrationTests` 2 項）。**沒有既有測試需要改寫。**
`BuildingRegulationReview.sln` 與 WPF 外掛專案皆 0 警告 0 錯誤。

**未實機驗證。** 本階段沒有 Revit 端改動，而且這個檢查還沒接進 `FireReviewRunner`（步驟 5），
按「開始檢討」不會產生第 79 條之 2 的結果。步驟 3 的實機驗證項目仍然待辦。

### 步驟 5 的產出與驗證

改動的檔案：

- `src/BuildingRegulationReview.Application/Reviews/FireReviewRunner.cs`：`FireReviewStep` 新增
  `VerticalCompartment`（在 `CurtainWallJunction` 之後、`Evidence` 之前）、`Label` 加「垂直區劃」、
  `total` 5 → 6、第 5 段跑 `VerticalCompartmentCheck.Review` 並把結果併入 `results`；`Findings`
  多收一個 `VerticalCompartmentReview`，收它的警告、逐筆結果，並寫出三列統計那一行日誌。
- `src/BuildingRegulationReview.Application/Reviews/ReviewTable.cs`：`CheckTypes` 加第五列、
  `Title` 加「垂直區劃」、`ReviewTableGrouping.ShaftRequirement`、`GroupsFor` 的新分支（照
  `RequirementOrder` 排序）、`ReviewTableEntry.ShaftRequirement`／`ShaftRequirementLabel` 與
  `ShaftRequirementOf` 證據解析。
- `src/BuildingRegulationReview.Application/Reviews/ReviewBaselineBuilder.cs`：`Build` 兩個多載都
  多收 `VerticalCompartmentInputs`；context 加 `shaft.building`／`shaft.zone`；每個開口多一行
  `shaftDevice｜<遮煙 Kind|Raw|Reason>|<時效 Kind|分鐘|Raw|Reason>`。
- `src/BuildingRegulationReview/FireReview/FireReviewWindow.cs`：`GroupingOf` 對第五列回傳
  `ShaftRequirement`，不再落到 `OpeningKind`。

`ReviewReadiness` 不需改動（步驟 3 已就緒）。

改寫的既有測試（三項，都是「多了一列／多了一段」造成的預期值）：

- `ReviewTableTests.A_clean_model_reads_pass_on_all_three_rows`：列名多「垂直區劃」，空列清單改成兩列。
- `FireReviewIntegrationTests.Review_runs_all_three_checks_…` 改名為 `Review_runs_every_check_…`：
  進度六段、空列清單兩列，並加上「這層樓沒有垂直區劃用途」的斷言。
- `FireReviewIntegrationTests.Run_reports_the_candidate_count_and_prescan_time`：`Stages.Count` 5 → 6。

**`ReviewRunValidityTests` 不需調整。** 它兩邊（存檢討、判新鮮度）都走
`ReviewBaselineBuilder.Build` 的逐項多載且不傳垂直區劃輸入，格式因此沒變；新的 `shaftDevice｜` 行
只在有傳入時才寫。走組裝多載的路徑（`FireReviewRunner`、`FireReviewModel` 的前置掃描、
`FireReviewIntegrationTests.CurrentBaseline`）兩邊一致，所以不會無故變成「需更新」——但**步驟 5
之前存下的檢討紀錄會一次性變成「需更新」**（§9 第 9 項）。

測試：`dotnet test` **1376 通過、0 失敗**（原 1369）。新增 7 項（`FireReviewIntegrationTests` 5 項、
`ReviewTableTests` 2 項）。`BuildingRegulationReview.sln` 與 WPF 外掛專案皆 0 警告 0 錯誤。

**未實機驗證。** Revit 端只動了 `FireReviewWindow.GroupingOf` 一行。要驗時：在有昇降機道或管道間的
模型上，把該區劃各 Area 的 `防火檢討_區劃用途` 填成 `昇降機道`／`管道間`，門窗嵌板類型填
`防火檢討_遮煙性能`、門再填 `防火檢討_設計防火時效`，按「開始檢討」應看到檢討表第五列「垂直區劃」
展開成要求列，一扇維修門在兩條列各出現一次。步驟 3 的參數綁定實機驗證仍然待辦。

### 步驟 6 的產出與驗證

改動的檔案（3 個程式檔，Revit 端不需改動）：

- `src/BuildingRegulationReview.Application/Checks/VerticalCompartmentInputs.cs`：
  `VerticalCompartmentRequirements.Order(string?)` 與 `Order(VerticalCompartmentRequirement)`
  ——條文順序，檢討表的三條要求列與標示描述共用。
- `src/BuildingRegulationReview.Application/Reviews/ReviewTable.cs`：私有 `RequirementOrder` 刪除，
  改叫 `VerticalCompartmentRequirements.Order`。行為不變（守門測試
  `Vertical_compartment_results_are_counted_by_requirement_in_clause_order` 未改）。
- `src/BuildingRegulationReview.Application/Reviews/ReviewMarkup.cs`：
  `PlannedElementOverride.ShaftRequirements`（照條文順序、去重、取自 `shaft.requirementLabel` 證據）、
  `Description` 在有要求時附上要求名、`ReviewMarkupPlan.Subject` 對略過的標示也附上要求名；
  element override 分支加上「為什麼不出註解、不發圖號」的註解。

**沒有改動的地方，以及為什麼：**

- `RevitReviewViewMarker`：它只把 `PlannedElementOverride.Description` 寫進標示紀錄
  （`ApplyOverrides`），描述變長就自動反映，沒有邏輯要改。
- `ReviewMarkupDiff`：元素覆寫只比 `ElementUniqueId` 與 `RunId`，`PlannedElementOverride` 沒有
  `Signature`，所以描述改字不會讓既有標示被判成需重建（決議 22，守門測試
  `Re_running_takes_a_shaft_doors_mark_over_instead_of_painting_it_again`）。
- `CurtainWallMarkNumbers`：`Assign` 只收 `JunctionKind is not null` 的列，垂直區劃本來就拿不到
  號碼，決議 20 是把這件事確認下來並加上守門測試，不是改它。帷幕牆的既有圖號守門測試未動。
- `VerticalCompartmentCheck` 的證據：`opening.EvidenceFor(zone)` 已經帶了
  `source.category`／`source.typeName`／`source.linkInstanceUniqueId`（走 `CandidateEvidence.Source`），
  所以描述讀得到「門「SD1」」，連結模型的維修門也已經走略過那條路。

測試：`dotnet test` **1382 通過、0 失敗**（原 1376）。新增 6 項，全部在 `ReviewMarkupTests`
（§10 最後一張表）。**沒有既有測試需要改寫**；唯一調整的是把該檔的私有輔助 `JunctionRun` 改名為
`HandRun`（它現在也用來組垂直區劃的檢討紀錄）。`BuildingRegulationReview.sln` 與 WPF 外掛專案皆
0 警告 0 錯誤。

**未實機驗證**——本階段沒有任何 Revit 端改動。要驗時：照「步驟 5 的產出與驗證」把模型填好，讓一扇
管道間維修門兩項要求都不符合，按「開始檢討」後標示檢討視圖，該扇門應只被塗紅一次，標示紀錄的那一行
應寫出兩項要求名。
