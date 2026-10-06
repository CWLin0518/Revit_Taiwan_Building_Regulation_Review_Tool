# 垂直區劃：第 79 條之 2

功能 ID：`vertical-compartment`

狀態：**規則層、參數層、檢查層、一次檢討的接線與檢討視圖標示已完成**（本文件 §5、§6、§7、§8、§10）。
按「開始檢討」會產生第 79 條之 2 的結果、檢討表第五列與三條要求列，證據基線看得見遮煙性能，未符合的
防火設備會在檢討視圖被塗紅、描述帶出是哪幾項要求未符合（不出註解、不發圖號，§7.2）。第 3 項挑空的
兩款免除**已完成**（§3.6、§7.3，決議 23～30，步驟 7b～7d）。步驟 8 依內政部 106 年 9 月 27 日
內授營建管字第 1060814830 號函修正兩件事（§3.7，決議 31～33）：第二款的樓地板面積改讀**連通區劃
之合計面積**；第 3 項免除成立的挑空**不再無條件免面積檢討**，其連通區劃回到第 79 條、所跨樓層含
第十一層以上者並依第 83 條。連跨樓層數、起始樓層與連通區劃面積**由各樓層的區劃推得**，不再由人工
填寫（§3.8，決議 35）；挑空只剩 `防火檢討_避難層通達` 一個參數。

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

本文的三項不在本功能裡重複，理由見 §3.4。第 3 項（挑空得不受第 1 項限制的兩款情形）不是一項
要求而是一項分類，它自己一個受檢主體，見 §3.6。

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
  「挑空、樓梯及其他類似部分」。免除因此不擴及樓梯間（決議 29）。第 3 項的事實來源與判定見
  §3.6，它不改變任何既有檢討的理由見 §9 第 3 項。

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

### 3.6 第 3 項：挑空的兩款免除

第 3 項是本條唯一能把整個區劃義務免掉的條文，而且只寫給挑空（§2.1 第三點）。它和 §3.1 的三項
要求不是同一種東西：**它不是要求，是分類**——「這個挑空到不到得依第 1 項單獨區劃分隔」。因此它
自己一個受檢主體、自己一條檢討表要求列，而且不走規則引擎（決議 24）。

#### 受檢主體

| | |
| --- | --- |
| 主體 | **挑空區劃本身**（不是開口）。`zone.use == "挑空"` 的每一個區劃一筆結果 |
| `SubjectUniqueIds` | `zone.AreaUniqueIds`——與 `CompartmentAreaCheck` 同一批 Area，所以「定位／選取元素」指到同一個地方 |
| 證據 | `shaft.requirement == "AtriumExemption"`、`shaft.requirementLabel == "挑空免除（第3項）"`、`zone.name`，以及下表每一個讀到的事實 |
| 法源條文 | `建築技術規則建築設計施工編第79條之2第3項（挑空得不受第1項限制）`。**不得出現「第83條」**（同 §5.3 的理由） |
| `RuleId`／`RuleVersion` | 規則集的 id 與版本——沒有規則作答（決議 24），與 `Withhold`／`Unresolved` 兩條路徑同形 |

`shaft.requirement` 用既有欄位、既有 `ReviewTableGrouping.ShaftRequirement` 分組，所以檢討表不需要
新的分組方式，第 3 項自然成為第四條要求列（§7.3）。

#### 事實來源

| 條文要素 | 欄位 | 來源 | 現況 |
| --- | --- | --- | --- |
| 第一款：避難層通達其直上層或直下層 | `zone.linksRefugeFloor`（Boolean） | **新** 參數 `防火檢討_避難層通達`（Instance、YESNO、Areas） | 步驟 7c |
| 第一款：室內牆面與天花板以耐燃一級材料裝修 | `zone.interiorFinish` | **既有**（由區劃內牆與天花板 Type 的 `防火檢討_室內裝修等級` 彙總，`InteriorFinishAssessment`） | 已完成 |
| 第二款：連跨樓層數在三層以下 | `zone.spannedFloors`（Quantity，整數層） | **由各樓層區劃推得**（§3.8，決議 35；步驟 7c 的參數 `防火檢討_連跨樓層數` 已作廢） | 步驟 8 |
| 第二款：樓地板面積在一千五百平方公尺以下 | `zone.connectedArea`（Quantity，㎡） | **由各樓層區劃推得**：各開口樓層的所在區劃，加上起始樓層位於所在區劃正下方的區劃之**合計**（§3.8，決議 31、35，取代決議 28 的 `zone.area`） | 步驟 8 |
| 第 1 項的適用前提：防火構造建築物 | `building.fireResistiveConstruction` | **既有** | 已完成 |

兩個新欄位是 `zone.*`（對所有類別開放），因為它們是 Area 實體上的事實，與 `zone.floorNumber`
同一類；**沒有任何規則讀它們**，它們只餵第 3 項的判定。連帶兩件事：

- `ReviewInputSources.NeededBy` 是規則驅動的（`FieldsUsedBy`），所以這兩個參數**不會**變成前置
  檢查的阻擋項——這是刻意的（決議 27）。
- 但它們仍要進 `RuleFieldCatalog`：組裝層把整份 `zone.*` 輸入 `ApplyTo(facts)` 到每個設備主體，
  白名單外的欄位會在那裡例外。

為什麼避難層通達只能由設計者宣告（步驟 7a 原本認為連跨樓層數也是，決議 35 已改為推得）：

- **避難層**是「具有出入口通達基地地面或道路之樓層」（第 1 條），基地地面與道路的關係不在本工具
  的資料模型裡，而且一棟建築物可能有多個避難層。「通達其直上層或直下層」還要知道挑空的兩端各是
  哪一層——不是一個 Area 看得出來的事。
- ~~**連跨樓層數**沒有可靠的模型表達~~：決議 35 定下建模慣例——每一層開洞的樓板各放一個挑空 Area——
  之後，工具就能跨樓層讀區劃、由所在區劃往上下層找挑空（§3.8）。

#### 判定式與狀態

```
第一款成立 ⟺ zone.linksRefugeFloor == true
            && zone.interiorFinish ∈ { 耐燃一級, 耐燃一級含底材 }
第二款成立 ⟺ zone.spannedFloors <= 3 && zone.connectedArea <= 1500 m²   （合計，決議 31）
第 3 項免除成立 ⟺ 第一款成立 或 第二款成立
```

「三層以下」與「一千五百平方公尺以下」都含等號。第一款只要求「以耐燃一級材料裝修」，沒有第 83 條
第三款那種「包括底材」的字，所以 `耐燃一級含底材` 也算符合——它比款文要求的更嚴。

| 模型情形 | 判定 | 狀態 |
| --- | --- | --- |
| 區劃範圍有問題（未封閉、重疊） | 不判定 | `人工覆核`（同 §4 的界線） |
| 非防火構造建築物 | 第 1 項本來就不適用，無免除可言 | `不適用` |
| 防火構造未填 | 連第 1 項適不適用都不知道 | `資料不足` |
| 連跨 2 層、連通面積 320 ㎡ | 符合第二款 | `符合`（決議 39） |
| 避難層通達＝是、裝修耐燃一級 | 符合第一款 | `符合`（決議 39） |
| 任一款已成立，另一款的事實缺著 | 免除已成立 | `符合`（缺口不影響，同 §3.3 的精神） |
| 連跨 5 層、避難層通達＝否 | 兩款均不成立 | `不適用` |
| 連跨 5 層、避難層通達＝是、裝修等級＝無 | 兩款均不成立 | `不適用` |
| 連跨 5 層、避難層通達未填 | 第一款無法判定、第二款不成立 | `資料不足`（缺 `防火檢討_避難層通達`） |
| 連跨推不出來、避難層通達＝否 | 第二款無法判定、第一款不成立 | `資料不足`（帶出推不出來的原因，§3.8） |
| 連跨 5 層、避難層通達＝是、裝修等級讀不出來 | 第一款無法判定、第二款不成立 | `資料不足`（缺裝修等級） |
| 面積 2000 ㎡、連跨未填、避難層通達＝否 | 第二款已確定不成立（面積），第一款不成立 | `不適用`（不必問連跨） |

三條規矩，和引擎的六態同一個形狀：**已成立的一款讓其他缺口不重要**；**已確定不成立的一款不必
再問它缺的事實**；**只有「這一款無法判定、另一款不成立」才是資料不足**。

「未填」與「同一區劃的各 Area 填得不一致」是同一種缺口：後者在 `ReviewParameterSnapshot.ZoneInput`
就變成 `ReviewInput.Unreadable`（與 `zone.use` 同一條路），判定層兩者都當作無法判定，不去挑一個值。

**沒有 `未符合`。**（免除成立自決議 39 起為 `符合`。）不符合第 3 項不是違規——它只表示第 1 項照常適用，而第 1 項本文
的要求已由既有的邊界規則檢討（§3.4、決議 3）。因為沒有 `未符合`，第 3 項的結果永遠不會被塗紅
（`ReviewMarkupPlan` 只處理 `Fail`），與決議 20 一致。

免除成立為什麼是 `符合`（決議 39，取代決議 25 的 `人工覆核`）：免除一旦成立，該挑空不再是「依第七十九條
之二規定之垂直區劃」，第 83 條的除外文字（§2.3）不再涵蓋它。其**面積**由 §3.7 的兩條規則接手（決議 32）；
連通範圍與其他部分之間的區劃分隔——一小時以上防火時效的牆、具一小時以上阻熱性的防火設備（同函）——由
該區劃 Area 邊界上的構件防火時效、防火門窗與阻熱性規則逐層檢討（決議 38）。是否合規看那些結果。

判定寫成一個純計算 `AtriumExemption.For(...)`（回傳 `AtriumExemption`：成立的款次、缺口旗標、
給人看的一句話），與 `ZoneAreaLimit.For(...)` 同形，所以判定本身可以單獨測、也可以在批次面板上
顯示（面板側未排程）。三態由三個屬性讀出：`Holds`（免除成立）、`IsUndecided`（`Gaps` 非空，資料
不足）、`IsInapplicable`（兩者皆非）。第五個缺口旗標是 `ConnectedArea`（步驟 8 前叫
`CompartmentArea`、讀的是 Revit Area）：連通區劃面積推不出來時（§3.8 的無法判定情形）第二款無法
判定，缺口說明會帶出推不出來的原因。

五項事實由 `AtriumExemptionFacts.Read(...)` 從區劃的輸入讀出，垂直區劃檢查（本節的主體）與面積
檢查（§3.7）都走它，兩邊對「這個挑空免不免」不可能有不同答案。

### 3.7 第 3 項成立後的面積檢討（步驟 8）

法源：內政部 106 年 9 月 27 日內授營建管字第 1060814830 號函。依第 3 項免除單獨區劃的挑空，其
連通範圍應以一小時以上防火時效之牆壁、防火設備與未鄰接挑空部分區劃分隔；第二款的「樓地板面積」
是被連通各層中鄰接挑空、納入該區劃範圍之樓地板、樓梯等面積的合計；所跨樓層含第十一層以上者，
仍應依第 83 條區劃（函第 4 點）。

工具的做法：面積檢查（`CompartmentAreaCheck`）為每個區劃推得三個衍生欄位，再由兩條比既有面積
規則更高優先序的規則判定。

| 衍生欄位 | 推得方式 | 推不出來時 |
| --- | --- | --- |
| `zone.atriumMerged` | 用途不是 `挑空`（含空白、不可讀）→ `false`；挑空 → `AtriumExemptionFacts` 的第 3 項判定（成立 `true`、不成立 `false`） | 第 3 項無法判定 → 留空 |
| `zone.atriumTopFloor` | `起始樓層序 + 連跨樓層數 − 1`，跨過地下到地上時跳過 0（`CompartmentAreaCheck.TopFloor`）。兩者都由 §3.8 推得，同一組挑空的每個 Area 結果相同 | 推不出來 → 留空並帶原因；本 Area 的所在樓層序不在範圍內（樓層序參數填錯）→ 標為無法判讀並說明矛盾 |
| `zone.atriumCompartmentArea` | §3.8 推得的連通區劃面積 | 推不出來 → 留空並帶原因 |

| 規則 | 優先序 | 適用條件 | 要求 |
| --- | --- | --- | --- |
| `tw-bcr-83-area-atrium` | 30 | 防火構造 `&& zone.atriumMerged == true && (zone.floorNumber >= 11 \|\| zone.atriumTopFloor >= 11)` | `zone.atriumCompartmentArea` ≤ 第 83 條第一～四款上限（同 `tw-bcr-83-area` 的式子） |
| `tw-bcr-79-area-atrium` | 25 | 防火構造 `&& zone.atriumMerged == true` | `zone.atriumCompartmentArea` ≤ 1500／3000 ㎡ |

結果：

| 情形 | 區劃面積結果 |
| --- | --- |
| 非挑空（含用途空白） | 不變，由 `tw-bcr-79-area`／`tw-bcr-83-area` 判定 |
| 挑空、第 3 項不成立 | 不變：仍是垂直區劃，用途豁免 → `不適用` |
| 挑空、第 3 項成立、所跨樓層都在十層以下 | `tw-bcr-79-area-atrium` 以合計面積判定 |
| 挑空、第 3 項成立、所在或所跨樓層含第十一層以上 | `tw-bcr-83-area-atrium` 以合計面積判定 |
| 挑空、第 3 項成立、在十層以下但跨到哪一層推不出來 | `資料不足`（不知道是否跨到第十一層） |
| 挑空、第 3 項成立、連通區劃面積推不出來 | `資料不足` |
| 挑空、第 3 項無法判定 | `資料不足`（決議 32 接受的代價） |

為什麼讀衍生欄位、不直接讀兩個參數：`ReviewInputSources.NeededBy` 是規則驅動的。規則若直接讀
`zone.spannedFloors`／`zone.connectedArea`，這兩個參數就會變成全專案的前置檢查阻擋項，沒有挑空的
專案也無法開始檢討——決議 27 要避免的事。衍生欄位沒有輸入來源，所以不會被列入；它們也列在
`CompartmentAreaInputs.ModelOwnedFields`，不許由輸入覆蓋第 3 項的判定（決議 33）。

為什麼用途空白也要設 `false` 而不是留空：適用條件裡缺欄位就是資料不足，留空會讓全專案每一個沒填
用途的一般區劃都變成資料不足。代價是直接以規則引擎測面積規則的測試，組事實時要自己補上
`zone.atriumMerged = false`（`Article83AreaRuleTests` 等五個檔案的 helper 已補）。

**面積核對不影響免除成立的挑空。** 一般區劃的 Revit 面積與邊界量得面積差超過容許值時，
符合／未符合會降為人工覆核；併入連通區劃的挑空比的是設計者宣告的合計，與這個 Area 的面積無關，
所以結論保留、只在訊息附註差異（code review 第 1 項）。

**第 83 條的帷幕牆交接歸類。** `FireReviewRunner.HostLegalReferences` 以面積結果的法源是否含
「第83條」判定區劃來源；引擎無法判定適用與否時，回傳的是第一條仍有疑問的規則的法源，2F 挑空第 3 項
無法判定時就會帶著 `tw-bcr-83-area-atrium` 的法源。`RuleOutcome.IsApplicabilityUndecided` 把這種
「沒有規則作出判定」的結果標出來，`HostLegalReferences` 不採計（code review 第 2 項）。

第 83 條的上限讀的是**挑空這個區劃**的 `zone.interiorFinish`，不是連通範圍各層的裝修等級；後者
不在資料模型裡（§9 第 13 項）。

### 3.8 由各樓層區劃推得連跨樓層數與連通區劃面積（步驟 8，決議 35）

**建模慣例：** 每一層開洞的樓板，在該層的 Area Plan 上放一個挑空 Area（`防火檢討_區劃用途` = `挑空`）。
例如只有 2F 樓板開洞的挑空，就在 2F 放一個；2F、3F 都開洞，就兩層各放一個。

**推算（`AtriumStackResolver`）：**

1. **所在區劃**：同一層裡，與挑空 Area 共用一段邊界（至少 300 mm，容許 50 mm 誤差）的區劃，
   **不含任何垂直區劃**（挑空、樓梯間、昇降階梯間、昇降機道、管道間）——它們依第 1 項單獨區劃分隔，不屬於
   挑空的連通範圍；算進來還會讓逐層堆疊的樓梯間把兩個無關的挑空串成一個（決議 36）。
2. **往上下層找**：上（下）一層裡，若有挑空的**所在區劃**與本層挑空的所在區劃在平面上重疊，兩者就是
   同一個挑空——兩個開口本身不必重疊。重複到上（下）一層的相關區劃裡沒有挑空為止。
3. **起始樓層**＝最低開口層的下一層（洞口下方、有樓板的那層）；**最高樓層**＝最高開口層；
   **連跨樓層數**＝最高 − 起始 + 1（以樓層排序計，跳過沒有區劃的樓層）。
4. **連通區劃面積**＝每一開口層的所在區劃面積，加上起始樓層裡與最低開口層的所在區劃**或洞口本身**在
   平面上重疊的區劃面積（使用者選 (2)：所在區劃正下方，不只洞口正下方；洞口正下方也算——形狀恰好等於
   洞口的 1F 大廳落在所在區劃的「洞」裡，不在其內部，要另外比對洞口）。挑空 Area 與垂直區劃都不計。
5. **樓層要連續**：「樓層」是有區劃的 Level（依 `ProjectElevation` 排序，同高程再依 UniqueId），所以還
   沒建區劃的樓層會被跳過。各樓層的樓層序都知道時，起始樓層與各開口層的樓層序必須一層接一層（跳過 0），
   否則判無法判定並說明「之間的樓層沒有區劃，或樓層序填錯」——避免 2F、4F 被當成相鄰而少算一層。
6. 起始樓層序取起始樓層那些區劃一致的 `防火檢討_所在樓層序`；沒填或不一致時，用最低開口層的
   樓層序減一（跳過 0）。最高樓層序取最高開口層的樓層序。

```
3F   ┌── 區劃 Y ──────────┐
     │          [挑空B]   │   ← 與 A 錯位，但在 Y 裡、Y 與 X 重疊 → 同一個挑空
2F   ┌── 區劃 X ──────────┐
     │ [挑空A]            │
1F   ┌── 區劃 W ──────────┐   ← W 裡沒有挑空 → 起始樓層 1F
連跨 3 層；連通區劃面積 = W + X + Y
```

**讀哪些區劃：** `RevitStoreyZoneReader` 讀與本工作包同一個 Area Scheme、由本工具寫入（帶工作包標記）
的所有 Area，不分樓層，以（工作包, Zone ID, Level）組成區劃；用途與樓層序讀同一組 Area 參數，同一區劃的
Area 不一致時視為沒填。同一個（工作包, Zone ID）出現在多個 Level——把 Area 複製到別層時標記會一起複製——
各層分開列出並帶問題「可能是複製到其他樓層的 Area」，碰到它的挑空不推算。本工作包的區劃以本層 Level
找（`StoreyZoneMap.Find(…, levelUniqueId)`）。只有本工作包有挑空時才讀其他樓層，沒有挑空的工作包不付
這個成本。讀到的 `StoreyZoneMap` 放進 `ReviewModelFacts.Storeys`，由
`ReviewInputAssembler.AtriumInputs` 只為用途是挑空的區劃產生 `zone.spannedFloors`、
`zone.atriumBaseFloor`、`zone.connectedArea` 三個輸入，來源文字列出挑空與每個計入的區劃和面積。

**無法判定時：** 輸入以 `Unreadable` 帶原因，第 3 項與區劃面積結果的缺口都會說明：

| 情形 | 影響 |
| --- | --- |
| 某個挑空 Area 周圍沒有相接的區劃 | 連跨樓層數、連通面積都推不出來 |
| 最低開口已在最低一個有區劃的樓層 | 同上（沒有起始樓層） |
| 起始樓層沒有位於所在區劃正下方的區劃 | 連通面積推不出來，連跨樓層數仍可 |
| 計入的區劃沒有 Revit 面積（未放置／未封閉） | 同上 |
| 挑空或計入的區劃被複製到其他樓層 | 同上，說明可能是複製的 Area |
| 開口層與起始樓層的樓層序不連續 | 連跨樓層數、連通面積都推不出來（中間樓層未建區劃或樓層序填錯） |
| 沒有讀到其他樓層（找不到 Area Plan、沒有 Area Scheme、讀取例外） | 三個輸入都以 `Unreadable` 帶原因 |

**需更新：** 推得的值是區劃輸入的一部分，證據基線會記錄；其他樓層的區劃面積或範圍改變時，本工作包
已存的檢討會變成「需更新」。

### 3.9 免除成立的挑空與連通區劃之間不是區劃邊界（決議 37）

第 3 項免除成立後，挑空和它的所在區劃是同一個連通區劃，兩者之間的分界線（常見是欄杆或玻璃）在區劃
內部，不需要一小時防火時效，開口也不必是防火設備。`MergedAtriums`（`Application/Checks`）找出本層
免除成立的挑空（與第 3 項結果用同一份 `AtriumExemptionFacts`），三個地方據此把構件視為非區劃邊界：

| 檢查 | 改變 |
| --- | --- |
| 構件防火時效（`FireResistanceCheck`） | `element.isCompartmentBoundary = false` → 第 79 條區劃牆規則不適用；第 70 條主要構造照舊 |
| 防火門窗（`OpeningProtectionCheck`） | `opening.hostIsCompartmentBoundary = false` → `tw-bcr-79-opening` 不適用 |
| 帷幕牆交接（`FireReviewRunner.HostLegalReferences`） | 該牆不列為區劃牆，不產生交接 |

結果訊息附上說明，證據多一筆 `atrium.interiorBoundary`。

**判斷用幾何，不只看構件接觸哪些區劃：** 沿構件中心線以不大於容差的間距取樣（開口用其位置），每個
落在區劃邊線上（容許半個牆厚加候選解析的邊界容差）的點，都必須同時貼著**恰好一個**免除成立的挑空與
另一個非挑空區劃；構件接觸的每個區劃都必須在這樣的點上出現過；構件接觸的區劃裡只要有垂直區劃就直接
維持邊界。區劃線畫在欄杆或牆**面**上（關係不明，`BoundaryOffCenterline`）也照同樣判斷——用房間分隔線
圍挑空時最常見——構件防火時效走「關係不明逕行判定」那條路，開口走主體牆關係不明那條路，判定為內部線
時直接給結果，不再進人工覆核。所以：

- 沿挑空與相鄰區劃外側並排的外牆（只貼著一側）→ 仍是區劃邊界；
- 一段在挑空與區劃之間、另一段繼續分隔其他區劃的長牆 → 仍是區劃邊界；
- 挑空與樓梯間、昇降機道等垂直區劃之間 → 仍是區劃邊界（它們依第 1 項單獨區劃），長牆只有一小段面向
  管道間也一樣；
- 兩個免除都成立、相鄰的挑空之間 → 仍是區劃邊界（各自的連通區劃分開檢討）；
- 第 3 項不成立或無法判定的挑空 → 邊界照舊（第 1 項正好要求這條邊界）。

**已存的檢討不會自動變成「需更新」**：規則集版本與輸入都沒變，舊結論（例如欄杆「區劃牆壁未符合」）會
留到重新執行「開始檢討」為止。

**已知限制**：鄰區用途**沒填**時視為可連通的一般區劃（與 §3.8 一致，一般區劃本來就不必填用途）；
若它其實是漏填用途的樓梯間，這條線會被放寬，請確實填寫垂直區劃的用途。用途**讀不懂**（同一區劃各 Area
填得不一致）時維持區劃邊界。

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
| ~~`防火檢討_連跨樓層數`~~ | Instance、Integer、GUID `…0010` | Areas | **步驟 8 作廢**，改由各樓層區劃推得（決議 35） | 已移除 |
| `防火檢討_避難層通達` | Instance、YESNO、GUID `…0011` | Areas | 第 3 項第一款之「避難層通達其直上層或直下層」（`zone.linksRefugeFloor`） | 已完成 |

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

第 3 項只剩 `防火檢討_避難層通達` 一個參數（決議 35），它與上面三個不同，**不列入前置檢查的必要
參數**：只有在專案裡真的有挑空時才需要，列為必要會讓沒有挑空的專案也無法開始檢討（決議 27）。它只
寫進主檔 `assets/SharedParameters/fire-review-shared-params.txt`，維持 Big5／cp950。未勾選讀成 `否`：
這是一個答案，而且是嚴的那一邊——第一款直接不成立，不會誤放免除（決議 30 的後半）。

**作廢的參數**（主檔已移除並在檔頭註記 GUID）：`防火檢討_連跨樓層數`（…0010，步驟 7c）、
`防火檢討_連通區劃面積`（…0014）、`防火檢討_挑空起始樓層序`（…0015，後兩者都是步驟 8 中途加入、未曾
出貨）。已綁過 …0010 的模型，該專案參數會留著但不再被讀，可在「管理 > 專案參數」移除。

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

### 7.3 第 3 項的產出（步驟 7d）

| 產出 | 第 3 項 | 理由 |
| --- | --- | --- |
| 檢討表要求列 | **第四條**：`挑空免除（第3項）` | `shaft.requirement` 分組已經存在（§3.6），`VerticalCompartmentRequirements.Order` 依 `All` 的索引排序，把它放在三項第 1 項要求之後就是條文順序 |
| 日誌那行的件數 | **有**：與三條要求列同一行寫出 | 「0 件」與整列沒檢討讀起來一樣（§7.1 的同一個理由）。專案裡沒有挑空時第四列是「0 件未檢討」 |
| 元素紅色覆寫 | **沒有** | 第 3 項不產生 `Fail`（§3.6），`ReviewMarkupPlan` 只處理 `Fail` |
| 文字標註、填滿區域、檢討圖號 | 沒有 | 同決議 20 |

`VerticalCompartmentRequirement` 因此多一個成員 `AtriumExemption`，`All` 變成四項（條文順序：
三項第 1 項要求、然後第 3 項）。**設備迴圈不可以看到它**：另立
`VerticalCompartmentRequirements.DeviceRequirements`（就是原本的三項），`ForUse` 只從它篩，
`ActualField`／`Categories` 對 `AtriumExemption` 沒有意義（第 3 項不由單一欄位回答、主體也不是
開口）。守門測試 `Only_the_hoistway_and_the_shaft_carry_extra_requirements` 的意思因此收窄成
「挑空／昇降階梯間／樓梯間不產生（設備, 要求）主體」——挑空現在會產生第 3 項那一種主體（決議 23）。

檢討表的第五列（`垂直區劃`）本身不變，變的是它裡面的要求列從三條變四條。`ReviewTable` 不需要
改分組邏輯：`ShaftRequirementOf` 照 `All` 比對字樣，列名照 `shaft.requirementLabel` 證據讀回來
（決議 17），舊版結果讀不到第四列也不會出錯。

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
| `AtriumExemption`／`AtriumExemptionClause`／`AtriumExemptionGap` | 第 3 項兩款的純計算判定（§3.6），與 `ZoneAreaLimit` 同形 | 已完成 |
| `RuleFieldCatalog` 的 `zone.spannedFloors`／`zone.linksRefugeFloor` | 白名單欄位（沒有規則讀，只餵第 3 項判定） | 已完成 |
| `ReviewInputSources` 的兩筆新 `zone.*` 來源、兩個 Shared Parameter、批次面板兩欄 | 事實進得來（§6）；證據基線不需改動 | 已完成 |
| `AtriumExemption.StatedSpannedFloors` | 連跨樓層數 0 視同未填（Revit 整數參數沒有空白狀態） | 已完成 |
| `VerticalCompartmentRequirement.AtriumExemption`／`DeviceRequirements` | 第四條要求列的用字；設備迴圈只看 `DeviceRequirements`（§7.3） | 已完成 |
| `VerticalCompartmentCheck` 的挑空主體 | 每個 `zone.use == "挑空"` 的區劃一筆結果，不走規則引擎 | 已完成 |

## 9. 已知限制

1. **第 2 項只模型化兩個但書的共通效力。** 兩者都讓昇降機道出入口免遮煙，但前者連「應裝設防火
   設備」本身都放寬。工具只放寬遮煙，防火設備仍由 `tw-bcr-79-opening` 要求——安全方向（多要求
   一個防火設備，不會漏檢討）。
2. **`shaft.elevatorLobbyProtected` 沒有輸入來源。** 「昇降機間併同區劃」是空間關係，
   「其出入口具遮煙性能」是另一扇門的性質。沒填就是 `InsufficientData`（遮煙未達時），不會誤判
   未符合。
3. ~~**第 3 項免除成立後，工具仍無條件讓挑空免於面積檢討。**~~ **步驟 8 已解決**（§3.7、
   決議 32）：免除成立的挑空，其連通區劃合計面積回到第 79 條、所跨樓層含第十一層以上者並依
   第 83 條。仍然寬鬆的部分：免除成立時，挑空自己的邊界牆與開口仍由既有邊界規則要求一小時
   ——那是嚴的方向；連通範圍與未鄰接挑空部分之間的區劃分隔（一小時、阻熱性）不在模型裡，只在
   第 3 項的人工覆核訊息裡交給人。
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
11. **連通區劃面積與連跨樓層數由各樓層區劃推得，依賴建模慣例**（§3.8，決議 35）。工具認定的
   「連通範圍」是挑空 Area 的所在區劃與起始樓層位於其正下方的區劃，不是函釋所說「鄰接挑空、納入
   該區劃範圍」逐一判斷的結果；區劃怎麼劃就怎麼算。挑空沒有在每一層開洞的樓板各放一個 Area、
   所在區劃畫得比實際連通範圍大或小、或不同 Area Scheme 混用時，數字會跟著偏。推得的值與各區劃
   名稱、面積都寫在輸入來源，檢討表上看得到。
12. **第 3 項的免除不擴及樓梯間、昇降階梯間**，雖然兩款的文字都寫「挑空、樓梯及其他類似部分」
   （決議 29）。
13. **免除成立的挑空檢討第 83 條時，裝修等級讀的是挑空這個區劃的 `zone.interiorFinish`**，不是連通
   範圍各層的。連通範圍各層不是一個模型裡的區劃，工具沒有它們的牆與天花板。
14. **挑空正下方的自動滅火設備有效範圍未處理。** 國土管理署 113 年 7 月 15 日國署建管字第
   1131109943 號函：挑空正下方原則上不得計入自動滅火設備的有效範圍，除非設有放水型撒水頭。工具
   的 `zone.sprinklered` 是整個區劃一個是非值，免除成立的挑空檢討第 79 條時仍會讓上限加倍——寬鬆
   的方向。要支援需要「有效範圍面積」這個事實，目前沒有。

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
| `Only_the_hoistway_and_the_shaft_hold_a_device_to_a_requirement` | 挑空／昇降階梯間／樓梯間的防火設備不被任何要求拘束；`DeviceRequirements` 就是 `All` 去掉第 3 項（決議 23） |
| `The_third_paragraph_is_the_fourth_review_table_row_and_no_rule_answers_it` | 第 3 項排在四列的最後、用字與標籤固定、沒有規則認領它，`ActualField`／`Categories` 對它拋例外（§7.3） |
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
| `A_use_with_no_extra_requirement_produces_no_subject` | 昇降階梯間／樓梯間／清單外用字／未填用途都不產生主體（挑空另有自己的主體） |
| `A_building_that_is_not_fire_resistive_is_out_of_scope` | 非防火構造 → 每個主體 `NoRuleApplies` |
| `An_unbound_smoke_seal_is_insufficient_data_and_names_its_own_parameter` | 證據指向 `防火檢討_遮煙性能`，不是 `防火檢討_設計防火保護`（決議 6） |
| `An_unbound_maintenance_door_rating_names_the_rating_parameter` | 時效缺口指向 `防火檢討_設計防火時效` |
| `A_subject_carries_only_the_field_its_requirement_reads` | 遮煙主體不報時效的缺口，一個缺參數不會被算兩次 |
| `A_maintenance_door_with_no_single_rating_is_manual_review` | 「1hr/2hr」是人工覆核，與構件防火時效同一做法 |
| `An_opening_of_a_zone_whose_extent_is_in_doubt_is_manual_review` | 沒有封閉面積的區劃在前置檢查就擋下 |
| `An_ambiguous_opening_relation_is_manual_review` | 關係有疑義的開口仍產生一筆人工覆核，不會從件數消失（§4） |
| `An_atrium_is_one_subject_of_its_own_whatever_openings_it_has` | 挑空一個區劃一筆結果、主體是該區劃的 Area、沒有 Type／沒有 `RuleOutcome`（決議 23、24） |
| `An_atrium_within_three_storeys_and_the_area_limit_is_exempt_and_manual_review` | 第二款成立 → 人工覆核，訊息說明面積檢討的連帶影響（決議 25） |
| `An_atrium_linking_the_refuge_floor_with_a_class_one_finish_is_exempt` | 第一款成立 |
| `An_atrium_that_meets_neither_clause_is_not_applicable_rather_than_a_failure` | 兩款不成立是不適用，不是未符合；沒有 actual／required 值 |
| `An_atrium_outside_a_fire_resistive_building_has_no_exemption_to_speak_of` | 非防火構造時不得接「第1項照常適用」 |
| `An_atrium_still_waiting_on_a_fact_is_insufficient_data_and_names_it` | 「這一款無法判定、另一款不成立」才是資料不足，訊息點名缺的事實 |
| `A_span_below_one_storey_never_reaches_the_second_clause` | 決議 30 在檢查層再守一次 |
| `An_unreadable_fact_is_the_same_gap_as_a_missing_one` | 各 Area 填得不一致與未填同一種缺口，呼叫端不挑值 |
| `The_third_paragraph_result_cites_its_own_paragraph_and_locates_the_areas` | 法源是第 3 項且不含第 83 條、`RuleId`／`RuleVersion` 取規則集、證據帶要求名與讀到的事實 |
| `The_third_paragraph_never_passes_and_never_fails` | 五種事實組合都只落在人工覆核／資料不足／不適用 |
| `An_atrium_whose_extent_is_in_doubt_is_withheld` | 範圍有問題的挑空不判定，與其他主體同一條界線 |
| `The_four_rows_are_always_reported_even_when_empty` | 檢討表四列恆存在、列名取 `VerticalCompartmentRequirements.Label` |
| `A_device_of_a_type_the_package_has_no_opening_of_is_reported_as_a_warning`／`A_zone_that_is_not_in_the_package_…` | 輸入指到工作包外的東西是警告，不是靜默 |

`tests/BuildingRegulationReview.Core.Tests/Reviews/FireReviewIntegrationTests.cs`：

| 測試 | 守的事 |
| --- | --- |
| `Every_opening_type_is_assembled_into_the_vertical_compartment_inputs` | 組裝層讀開口 Type 的兩個參數，並與其他檢查共用同一份 `CompartmentAreaInputs` |
| `An_unbound_smoke_seal_names_its_own_parameter_and_not_the_protection_one` | `ReviewInputAssembler.Protection` 的參數名引數（決議 11） |
| `Review_runs_every_check_and_leaves_the_package_reviewed_with_the_rule_set_locked` | 六段進度（`垂直區劃（5/6）`）、沒有垂直區劃用途時第五列是未檢討 |
| `A_shaft_maintenance_door_is_two_results_of_the_fifth_review_table_row` | 接線成立：一扇維修門兩筆結果、兩條要求列、法源含第 79 條之 2、日誌寫出四列件數（含空列的 0 件） |
| `A_maintenance_door_short_of_an_hour_fails_only_the_rating_row` | 兩項要求各自判定，時效未達不會拖累遮煙那一列（決議 12 的端到端版） |
| `Unticking_a_types_smoke_seal_makes_the_stored_run_need_an_update` | 證據基線看得見遮煙性能；重跑後反映新值（§9 第 9 項） |
| `Changing_a_maintenance_doors_rating_makes_the_stored_run_need_an_update` | 門的設計防火時效同樣進基線——維修門是開口，不會被 `rating｜` 那行收到 |
| `A_zone_use_outside_the_vocabulary_produces_no_vertical_compartment_subject` | 清單外用途不產生主體，整列未檢討且日誌不寫件數那一行（§9 第 10 項） |
| `An_exempt_atrium_is_one_manual_review_in_the_fourth_requirement_row` | 第 3 項端到端：第四條要求列、列名、類別欄是「區劃」、定位指到 Area、日誌四列件數 |
| `An_atriums_openings_are_held_to_none_of_the_first_paragraphs_requirements` | 挑空裡的維修門不產生第 1 項的主體（決議 23 的端到端版） |
| `The_third_paragraph_never_fails_and_so_is_never_marked` | 第 3 項不會是未符合，也不會進 `ReviewMarkupPlan` 的 override（決議 20） |

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
| 23 | 第 3 項自己一個受檢主體（**挑空區劃本身**）與第四條要求列，`VerticalCompartmentRequirement` 加一個 `AtriumExemption`；設備迴圈改看新的 `DeviceRequirements` | 第 3 項的主體不是一扇設備，也不由單一欄位回答，塞進三項要求任一項都會讓 `ActualField`／`Categories` 說謊。用既有的 `shaft.requirement` 分組則什麼都不用改：檢討表、日誌、`Order` 都照 `All` 跑。連帶：守門測試的意思收窄成「挑空不產生（設備, 要求）主體」，步驟 7d 已把 `Only_the_hoistway_and_the_shaft_carry_extra_requirements` 改名為 `Only_the_hoistway_and_the_shaft_hold_a_device_to_a_requirement` 並改寫斷言（§7.3） |
| 24 | 第 3 項**不走規則引擎**，由檢查層的純計算判定 | 引擎的每條規則都要一個可比較的 `requiredValue`，而第 3 項是免除、不是要求；第 1 項本文對挑空的要求已由既有邊界規則檢討（決議 3），本類別沒有式子可寫。兩種硬寫法都被排除：(a) 把「免除成立」寫成 `requiredValue` → 不成立就是 `Fail`、會被塗紅，但不符合第 3 項不是違規；(b) 另加一個設計者宣告「本挑空已依第 1 項單獨區劃分隔」當 `requiredValue`、兩款當 `exemptions` → 等於重寫本文（決議 3），而那個問題模型已經用邊界構件回答了 |
| 25 | ~~免除成立是 `人工覆核`，不是 `不適用`，更不是 `符合`~~ **已由決議 39 取代** | 免除一旦成立，該挑空若不依第 1 項區劃就不是「依第七十九條之二規定之垂直區劃」，第 83 條的除外文字不再涵蓋它，其樓地板面積應回到面積檢討；工具目前無條件豁免挑空的面積檢討，這件事只能由人接手。用 `符合` 會讓它在檢討表算進符合（spec 11.7），讀起來像這個挑空已經處理完了 |
| 26 | ~~面積豁免**維持無條件**，不改成「第 3 項成立就不豁免」~~ **已由決議 32 取代** | 引擎的豁免語意是「豁免無法判定且本文不成立 → 資料不足」。挑空常常大於一○○平方公尺，所以只要這兩個新參數沒填，每個既有專案的大挑空都會從「免適用」變成「資料不足」——正是 §3.4 要避開的那種全專案資料不足。代價是方向仍然寬鬆（§9 第 3 項），但工具現在會在免除成立時明講連帶影響 |
| 27 | 兩個新事實**不進前置檢查的阻擋項** | 前置檢查是全專案的，而這兩個參數只有在專案裡真的有挑空時才需要；列為必要參數會讓沒有挑空的專案也無法開始檢討。缺了就是該挑空一筆資料不足、訊息指名參數名（同 `An_unbound_smoke_seal_is_insufficient_data_and_names_its_own_parameter`）。附帶好處：`ReviewInputSources.NeededBy` 是規則驅動的，沒有規則讀這兩個欄位，所以不必為此在 `NeededBy` 開後門 |
| 28 | ~~第二款的「樓地板面積」是**該挑空自己的** `zone.area`~~ **已由決議 31 取代** | 兩款的敘述都在修飾「挑空、樓梯及其他類似部分」，連跨樓層數與樓地板面積是同一個主語的兩個屬性。合計的讀法會讓幾乎每個挑空都超標，而工具也沒有那份資料。記在 §9 第 11 項當明示解讀 |
| 29 | 第 3 項只給 `挑空`，不擴及 `樓梯間`／`昇降階梯間` | 前言是「挑空符合下列情形之一者」（§2.1 第三點）；把免除擴及樓梯間是放寬，方向不對。兩款文字裡的「樓梯」因此不另立用字 |
| 30 | `防火檢討_連跨樓層數` 小於 1 視同未填 | Revit 的 INTEGER 參數沒有空白狀態，一綁上 Areas 全專案立刻讀成 0；「連跨 0 層」會通過第二款的「三層以下」，讓沒人填過的挑空自動免除，與 §3.6 狀態表相反。避難層通達那個 YESNO 不需要同樣處理：未勾選＝否是答案，而且是嚴的那一邊（§6） |
| 31 | 第二款的「樓地板面積」是**連通區劃之合計面積**（~~由新參數 `防火檢討_連通區劃面積` 宣告~~，**決議 35 改為推得**） | 內政部 106 年 9 月 27 日內授營建管字第 1060814830 號函明定應合計被挑空連通各樓層中鄰接挑空、納入該區劃範圍之樓地板、樓梯等面積；決議 28「合計會讓幾乎每個挑空都超標」不是讀法的理由，而正是條文要擋的情形。合計無法由模型推得（工具的區劃是逐層的），所以由設計者宣告，與連跨樓層數同類。NUMBER 而非 AREA，免去面板寫入時的單位換算 |
| 32 | 第 3 項免除成立的挑空**不再免面積檢討**：其連通區劃合計面積依第 79 條（`tw-bcr-79-area-atrium`，優先序 25），所在或所跨樓層含第十一層以上者依第 83 條（`tw-bcr-83-area-atrium`，優先序 30）；第 3 項無法判定時面積結果是資料不足 | 免除成立的挑空不是「依第七十九條之二規定之垂直區劃」，第 83 條本文的除外不涵蓋它；同函第 4 點明定所跨樓層含第十一層以上仍應依第 83 條。決議 26 擔心的「全專案資料不足」只會落在**用途填了挑空**的區劃上——非挑空區劃由衍生欄位明確設為 `false`，不受影響。免除不成立的挑空仍是垂直區劃，照舊豁免 |
| 33 | 兩條挑空規則只讀檢查層推得的 `zone.atriumMerged`／`zone.atriumTopFloor`／`zone.atriumCompartmentArea`，三者沒有輸入來源、列在 `ModelOwnedFields` | 讀原始參數會讓 `NeededBy` 把連跨樓層數與連通區劃面積列成全專案必要參數（違反決議 27）；把第 3 項的判定集中在 `AtriumExemptionFacts`，面積檢查與第 3 項主體的答案不會分歧；不許輸入覆蓋，參數就不能繞過第 3 項的判定 |
| 34 | ~~所跨最高樓層由新參數 `防火檢討_挑空起始樓層序`（INTEGER、0＝未填）＋連跨樓層數算出~~ **已由決議 35 取代**，不用 Area 自己的所在樓層序；本 Area 的所在樓層序不在宣告範圍內時判無法判讀 | code review 第 3 項：工作包以樓層為單位，跨三層的挑空常是逐層各放一個 Area、都填連跨 3；以各 Area 的所在樓層序起算，跨 8F～10F 的挑空在 9F、10F 會算成到 11F、12F，對合規設計判第 83 條未符合。三個選項（只在最低層填、新增起始樓層序、只因跨層成立時降為人工覆核）中使用者選新增參數：各 Area 填同一組數字就得到同一個答案，也能順便檢查三個數字是否自相矛盾 |
| 35 | 連跨樓層數、起始樓層與連通區劃面積**由各樓層的區劃推得**，三個參數（`防火檢討_連跨樓層數`、`防火檢討_連通區劃面積`、`防火檢討_挑空起始樓層序`）全部取消。建模慣例是每一層開洞的樓板各放一個挑空 Area；連跨以**區劃**判斷——上下層與本層所在區劃重疊的區劃裡有挑空，就是同一個挑空，兩個開口不必重疊；起始樓層是最低開口的下一層；連通面積是各開口層的所在區劃加上起始樓層位於所在區劃正下方的區劃，挑空 Area 本身不計 | 使用者決定：人工填合計面積容易填錯，也無法隨模型變動；工具既然已經在每層寫入區劃，就讀得到。使用者對三個選項的選擇：加總範圍選「與挑空相關的區劃」而非整層、挑空 Area 不計、起始樓層取所在區劃正下方而非洞口正下方。逐層各放 Area 時，同一組挑空每個 Area 推得的數字相同，決議 34 要解決的問題自然消失 |
| 36 | 垂直區劃（挑空、樓梯間、昇降階梯間、昇降機道、管道間）不算所在區劃，也不計入起始樓層；起始樓層也計入洞口正下方的區劃；開口層與起始樓層的樓層序須連續；同一區劃標記出現在多個 Level 時判不可用 | 決議 35 的 code review：樓梯間依第 1 項單獨區劃分隔、不屬於挑空連通範圍，而且逐層堆疊會把無關的挑空串在一起（使用者選 A）；所在區劃的「洞」就是挑空範圍，只比對所在區劃會漏掉形狀等於洞口的 1F 區劃；樓層以「有區劃的 Level」計，中間樓層尚未建區劃時會被跳過、少算連跨數而讓「三層以下」誤判成立；Area 複製到別層會帶走工作包標記，讀取器若只以（工作包, Zone ID）分組會把兩層疊成一個區劃 |
| 37 | 第 3 項免除成立的挑空與其非垂直的相鄰區劃之間，不視為區劃邊界（牆不受第 79 條區劃牆規則、開口不受 `tw-bcr-79-opening`、帷幕牆不產生交接）；以沿構件取樣的幾何判斷，外牆、部分分隔其他區劃的牆、面向垂直區劃的牆仍是邊界；免除不成立或無法判定時不變 | 使用者要求「挑空與區劃之間的邊界不要判成區劃邊界」。依同函，免除成立後挑空與連通範圍是同一區劃，分界線在其內部；免除不成立時第 1 項正要求這條邊界，所以只在免除成立時放寬。只看構件接觸哪些區劃會把沿兩區外側的外牆誤判為內部線（fixture 的 W1-bottom），所以用幾何 |
| 38 | 新增參數 `防火檢討_阻熱性`（Type／實體、YESNO、GUID `…0016`，門／窗／帷幕嵌板）與規則 `tw-bcr-79-opening-insulation`（優先序 15）：區劃邊界上已是防火設備的開口，須具一小時以上阻熱性；不是防火設備或沒填時落回 `tw-bcr-79-opening`。不設豁免，垂直區劃那一側也要求 | 第 79 條第 1 項本文「防火設備並應具有一小時以上之阻熱性」，使用者選擇套用到所有第 79 條區劃邊界開口，同函也要求挑空連通範圍的防火設備具阻熱性。引擎同一優先序兩條規則結論不同會判衝突，所以用較高優先序、適用條件含「已是防火設備」的規則接手；若給垂直區劃豁免，較高優先序的豁免會把整筆門窗結果變成不適用、蓋掉「應為防火設備」的要求，故不設豁免（對樓梯間一側比第 79 條之 2 嚴，方向保守）。讀法同設計防火保護：實體優先、再讀類型，未勾＝否，未綁＝資料不足。這是必要參數（規則直接讀它） |
| 39 | 第 3 項免除成立改為 `符合`（取代決議 25 的人工覆核），實際值為成立的款次、要求值「第一款或第二款」 | 決議 25 交給人的兩件事都已由工具檢討：連通區劃面積（決議 32）、連通範圍的區劃分隔——該區劃 Area 邊界上的牆由第 79 條區劃牆規則、防火設備由防火門窗與阻熱性規則（決議 38）逐層檢討；使用者確認連通範圍的劃法沒有問題。是否真的合規看那些結果，第 3 項本身只是分類，所以不會有未符合、也不塗紅 |

| 40 | 防火區劃編輯器的區劃清單加「用途」欄，套用時寫進該區劃每個 Area 的 `防火檢討_區劃用途`。用途是三態：**不變更**（`null`）／**一般區劃**（`""`，清除）／該用途。回讀時從 **Area 的參數**取值（不是工具的 signature），各面積不一致或讀不到一律回「不變更」。用途不進 Area signature，另立 `ZoneUseOperations` 隨預覽與計畫帶到寫回，並計入兩層的 `IsEmpty` | 使用者要求「編輯器要能設定垂直開口（管道間、挑空這類空間範圍）」。用途有**兩個**寫入入口（編輯器與批次設定面板），草稿若不知道模型現在填了什麼，套用就會把空白寫上去，把使用者設的用途連同第79條之2 的結果一起清掉，所以回讀必須讀 Area 參數、三態必須分得出「不變更」與「清除」。用途不進 signature 是為了不動 `TryReadArea` 從右切的邏輯；但只在 `SetName` 附近寫會被 `plan.IsEmpty` 與 `UnchangedAreas` 只驗不寫整個跳過（codex 影響審查指出），所以另立操作計畫。下游（批次面板、檢討面板、檢討判斷、`AtriumStackResolver`、`MergedAtriums`、證據基線）讀同一個參數，演算法不變。完整理由見 `docs/adr/0003-region-editor-writes-zone-use.md` |

### 11.1 設用途不等於免除成立（決議 40 的邊界）

在編輯器把一個區劃標成 `挑空`，只是把 `防火檢討_區劃用途` 填上去。它**不會**：

- 讓第 3 項的免除成立——免除仍取決於既有事實（避難層通達、連跨樓層數、連通區劃面積、裝修材料），
  由 `AtriumExemptionFacts` 判定（§3.6、§3.7）；
- 讓挑空與連通區劃之間的線不算區劃邊界——那由 `MergedAtriums` 依免除是否成立**與幾何關係**判定
  （§3.9），編輯器不刪線、不改模型的其他部分；
- 讓一個挑空自己跨樓層——建模慣例仍是每一層開洞的樓板各放一個挑空 Area（§3.8），
  `AtriumStackResolver` 靠的就是這個。編輯器**沒有**「一次設定貫穿多樓層」。

## 12. 實作進度

| 步驟 | 內容 | 狀態 |
| --- | --- | --- |
| 1 | 本文件：條文、用語、主體設計、限制 | **已完成** |
| 2 | 規則層：`RuleCategory.VerticalCompartment`、`shaft.*` 欄位、三條規則、要求用字、面積豁免訊息 | **已完成** |
| 3 | Shared Parameter `防火檢討_遮煙性能`（Big5 定義檔、輸入來源、讀取器、批次參數面板欄位） | **已完成** |
| 4 | `VerticalCompartmentCheck`／`VerticalCompartmentInputs`：由候選開口產生主體並跑引擎 | **已完成** |
| 5 | `FireReviewRunner` 接線、`ReviewTable` 三列、證據基線納入遮煙性能 | **已完成** |
| 6 | 檢討視圖標示與圖號（只塗紅、描述帶要求名、不發圖號） | **已完成** |
| 7a | 第 3 項的設計：事實來源、判定式與狀態、產出、決議 23～29（§3.6、§7.3） | **已完成** |
| 7b | `AtriumExemption` 純計算判定 + `zone.spannedFloors`／`zone.linksRefugeFloor` 兩個白名單欄位 | **已完成** |
| 7c | 參數層：兩個 Shared Parameter（Big5 主檔）、輸入來源、批次面板兩欄與批次填入 | **已完成** |
| 7d | 檢查層接線：挑空主體、檢討表第四條要求列、日誌件數、`AtriumExemption` 用字與 `DeviceRequirements` | **已完成** |
| 8 | 依 106 年 9 月 27 日函修正：第二款改讀連通區劃合計面積、第 3 項成立的挑空回到第 79 條／第 83 條面積檢討（§3.7）；code review 四項修正；連跨樓層數與連通面積改由各樓層區劃推得（§3.8，決議 31～35） | **已完成**（2026-10-04 實機驗證完成） |

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

**2026-10-04 實機驗證完成**：兩個參數寫入模型沒有問題——遮煙性能可勾選、門的設計防火時效可輸入、
寫入正確。未綁定那一側已驗：窗類別取消綁定後，窗列轉淡紅、提醒欄逐字為 `缺少參數：防火檢討_遮煙性能`。
已綁定但未勾選那一側亦已驗：未勾選的門列維持正常底色、無此提醒。步驟 3 的實機驗證項目全數完成。

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

**2026-10-04 實機驗證完成**：第五列「垂直區劃」出現，維修門在兩條要求列各出現一次，參數填齊後皆為
符合。回報為整體確認，未附列名與日誌行的逐字原文。

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

**2026-10-04 實機驗證完成**：使用者確認該扇門只塗紅一次、標示紀錄寫出兩項要求名。回報為整體確認，
未附標示紀錄的逐字原文。

### 步驟 7a 的產出與驗證

本步驟**只動文件**，沒有程式改動，所以沒有新的測試；`dotnet test` 仍是 1382 通過、0 失敗。
產出是第 3 項的設計定案：

- §3.6：受檢主體（挑空區劃本身）、五項事實各自的來源與現況、判定式、十二列狀態表、
  「沒有 `符合`／`未符合`」與「免除成立是 `人工覆核`」的理由。
- §6：兩個新參數列，以及它們不進前置檢查的理由。
- §7.3：檢討表第四條要求列、日誌件數、不塗紅不發圖號，以及 `AtriumExemption` 加進
  `VerticalCompartmentRequirement` 之後設備迴圈要改看 `DeviceRequirements` 這件事。
- §8：步驟 7b／7c／7d 各自要動的類別。
- §9 第 3 項改寫，新增第 11、12 項（面積的解讀、免除不擴及樓梯間）。
- §11 決議 23～29。

條文以 `mcp__taiwan-building-code__search_building_code`（查詢「挑空 連跨樓層 耐燃一級」）核對過，
與 §2.1 已抄錄的文字一致，本步驟沒有修改 §2.1。

本步驟順手查證、寫進上面設計裡的三件既有行為（7b／7c 不必再查）：

1. **裝修等級讀不出來就是「沒有這個輸入」。** `InteriorFinishAssessment.Derive` 在沒有表面、或
   任一表面的等級不是三個用字之一時回傳 `null`，`ZoneInput` 對沒有值的讀數回傳 `null`，所以
   `zone.interiorFinish` 根本不會出現在該區劃的輸入裡。§3.6 狀態表「裝修等級讀不出來」指的就是
   這件事，不是另一種值。
2. **輸入的組裝是泛用的**：`ReviewParameterSnapshot` 照 `ReviewInputSources.All` 跑，凡是掛在
   `Areas` 的來源都變成區劃輸入，`Convert` 再依欄位型別（Boolean／Text／Quantity）分派。所以
   步驟 7c 只要加上兩筆 `ReviewInputSource` 與兩個白名單欄位（7b），事實就會自己流進
   `CompartmentAreaInputs`，**不需要改組裝層**。
3. **同一區劃各 Area 填得不一致時是 `ReviewInput.Unreadable`**（`ZoneInput` 的最後一段），與
   `zone.use` 完全同一條路——這就是 §9 第 11 項所說「與第 10 項同」的機制。判定層因此要把
   「不一致」當成缺口（無法判定），不是當成某個值。

`zone.spannedFloors` 用 `RuleValueType.Quantity(ReviewUnit.None)`，與 `zone.floorNumber` 同；
Revit 的 Integer 參數由 `Quantity` 那條路讀進來。

### 步驟 7b 的產出與驗證

改動的檔案：

- `src/BuildingRegulationReview.Domain/Rules/RuleFieldCatalog.cs`：`zone.spannedFloors`
  （`Quantity(ReviewUnit.None)`）與 `zone.linksRefugeFloor`（`Boolean`）兩個白名單欄位，`all` 類別，
  排在 `zone.interiorFinish` 之後。註解寫明**沒有任何規則讀它們**、它們只餵第 3 項的判定，以及
  為什麼仍要進白名單（組裝層 `ApplyTo` 會對白名單外的欄位例外）。
- `src/BuildingRegulationReview.Application/Checks/AtriumExemption.cs`（新檔）：`AtriumExemptionClause`
  （`None`／`FirstClause`／`SecondClause`）、`[Flags] AtriumExemptionGap`（`FireResistiveConstruction`／
  `RefugeFloorLink`／`InteriorFinish`／`SpannedFloors`／`CompartmentArea`）與 `AtriumExemption`
  （`Clause`、`Gaps`、`Holds`、`IsUndecided`、`IsInapplicable`、`Description`）。
- `tests/BuildingRegulationReview.Core.Tests/Checks/AtriumExemptionTests.cs`（新檔）。

定案時說的 `AtriumExemptions` 靜態類別**沒有建立**：`ZoneAreaLimits.cs` 裡並沒有 `ZoneAreaLimits`
這個類別，`For` 是結果型別自己的靜態工廠（`ZoneAreaLimit.For`），所以這裡照同一個形狀寫成
`AtriumExemption.For(...)`，不另加一層只轉呼叫的門面。§3.6 與 §8 已同步改寫。

`AtriumExemption.For(bool? fireResistive, bool? linksRefugeFloor, string? interiorFinish,
int? spannedFloors, double? areaSquareMeters)` 是純函式，不吃 `CandidateZone` 也不吃 `ReviewInput`——
把輸入的三態（有值／不可讀／沒有）翻成值或 `null` 是呼叫端（步驟 7d）的事。判定順序就是 §3.6 的
三條規矩：先讀防火構造（未填＝資料不足、否＝不適用），再各自讀兩款；任一款成立就回傳該款且
`Gaps` 必為空；兩款都沒成立時才把兩款的缺口聯集起來，聯集非空是資料不足、空的是不適用。每一款
都**先判「已確定不成立」再判缺口**，這樣「面積 2000 ㎡、連跨未填」才會是不適用而不是資料不足。

驗證：

- `dotnet build BuildingRegulationReview.sln`：0 警告 0 錯誤。
- `dotnet build src\BuildingRegulationReview\BuildingRegulationReview.csproj`：0 警告 0 錯誤。
- `dotnet test tests\BuildingRegulationReview.Core.Tests`：**1405 通過、0 失敗**（原 1382，新增 23）。
  **沒有改寫任何既有測試。**
- 新測試逐條對照 §3.6 狀態表（「區劃範圍有問題」那一列屬步驟 7d，這裡不測），外加門檻兩側
  （連跨 1／3／4 層、面積 1500.0／1500.1 ㎡、`耐燃一級含底材` 與 `耐燃二級`）、面積讀不到時的
  `CompartmentArea` 缺口、一條把 3×3×5×5×5 種事實組合跑過的三態互斥測試，以及一條守門測試
  `The_third_paragraphs_two_facts_are_whitelisted_but_read_by_no_rule`（兩個新欄位在白名單裡、
  對每個類別開放，但不在 `FieldsUsedBy` 也不在 `NeededBy` 裡——決議 24、27）。
- **未實機驗證。** 本步驟沒有 Revit 端改動，也沒有接線，所以既有行為完全不變。

### 步驟 7c 的產出與驗證

改動的檔案：

- `assets/SharedParameters/fire-review-shared-params.txt`：新增 `防火檢討_連跨樓層數`
  （INTEGER、GUID `…0010`）與 `防火檢討_避難層通達`（YESNO、GUID `…0011`），兩列都接在
  `防火檢討_所在樓層序` 之後（Area 的參數擺一起）。以 `[System.Text.Encoding]::GetEncoding(950)`
  讀寫，`git diff --numstat` 是 `2 0`——只加兩行，其餘位元組與 CRLF 未動。門／窗／嵌板那份
  `fire-review-openings-type.txt` **沒有改**：Areas 的參數不屬於那份。
- `src/BuildingRegulationReview.Application/Reviews/ReviewInputSources.cs`：常數
  `SpannedFloors`／`LinksRefugeFloor`，以及 `All` 裡的兩筆 `ReviewInputSource`
  （`zone.spannedFloors`／`zone.linksRefugeFloor`，`Instance`、`Areas`）。
- `src/BuildingRegulationReview.Application/Checks/AtriumExemption.cs`：新增
  `StatedSpannedFloors(int?)` 與 `IsStatedSpannedFloors(double)`（決議 30）。
- `src/BuildingRegulationReview.Application/Reviews/ReviewParameterSnapshot.cs`：`ZoneInput` 讀每個
  Area 前先過 `Stated(source, reading)`，把小於 1 的 `連跨樓層數` 換成 `ParameterReading.Empty`。
- `src/BuildingRegulationReview.Application/Parameters/FireReviewInputRows.cs`：
  `FireReviewZoneRow.SpannedFloors`／`LinksRefugeFloor`／`IsAtrium`、
  `FireReviewZoneParameters` 的兩個新旗標，`MissingParameters` 只在挑空時報這兩個（決議 27）。
- `src/BuildingRegulationReview.Revit/Parameters/RevitFireReviewTypeScanner.cs`：`Zones()` 讀這兩個
  參數與它們的存在與否。
- `src/BuildingRegulationReview/FireReview/FireReviewInputViewModels.cs`：`SpannedFloors`／
  `LinksRefugeFloor` 兩個可編輯屬性、`IsAtrium`、`Edits()` 的兩筆寫回（連跨先過
  `StatedSpannedFloors` 再比較與寫入，所以在格子裡打 0 是清除而不是「連跨 0 層」）。
- `src/BuildingRegulationReview/FireReview/FireReviewParameterPanelWindow.xaml`：區劃分頁新增
  「連跨樓層數」與「避難層通達」兩欄，用途不是挑空時淡出（仍可編輯，因為用途可能還沒填）。
- `src/BuildingRegulationReview/FireReview/FireReviewParameterPanelWindow.xaml.cs`：`ZoneFillWindow`
  新增「區劃用途」（可編輯下拉，清單是五個垂直區劃字樣）、「連跨樓層數」與「避難層通達」三欄，
  `FillZones_OnClick` 一併套用；狀態列多一句「待填挑空免除事實 N 個區劃」，只數挑空。
- `src/BuildingRegulationReview.Domain/Rules/RuleFieldCatalog.cs`：註解裡的 `AtriumExemptions`
  改成實際存在的 `AtriumExemption.For`（7b 的偏離 1）。

**`ReviewBaselineBuilder` 不需改動，這與交接時的預期不同。** 它的 `AppendInputs(text,
name + ".zone", inputs?.ForZone(...))` 本來就把該區劃的**每一筆**輸入寫進指紋，所以兩個新欄位一
進 `ReviewInputSources.All` 就自動進了證據基線；兩條既有的證據基線守門測試（`Unticking_a_types_
smoke_seal_…`、`Changing_a_maintenance_doors_rating_…`）**因此完全不必改**，它們仍然通過。新增一條
`Changing_an_atriums_spanned_floors_makes_the_stored_run_need_an_update` 把這件事釘住。

`RevitReviewParameterReader` 也不需改動：`Names(host)` 照 `All` 篩 `Areas`，新來源自動被讀。

**既有 stored run 何時會變成「需更新」**：不是因為這次的程式改動——參數還沒綁到模型時，`ZoneInput`
讀不到值就不產生輸入，指紋一個位元組都沒變。但**使用者把這兩個參數綁上 Areas 的那一刻**，全專案
每個 Area 都開始讀得到值（YESNO 讀成 `否`、INTEGER 的 0 被 `Stated` 濾掉，所以實際多出來的是
`zone.linksRefugeFloor=b:False` 那一行），該樓層的區劃指紋因此一次性改變，既有檢討紀錄會變成
「需更新」。與 §9 第 9 項同一類的一次性成本。

驗證：

- `& "C:\Program Files\dotnet\dotnet.exe" build BuildingRegulationReview.sln`：0 警告 0 錯誤。
- `... build src\BuildingRegulationReview\BuildingRegulationReview.csproj`：0 警告 0 錯誤。
- `... test tests\BuildingRegulationReview.Core.Tests`：**1419 通過、0 失敗**（原 1405，新增 14）。
  **沒有改寫任何既有測試**；唯一動到的既有測試是 `AtriumExemptionTests` 那條白名單守門測試，在
  原有斷言之後**追加**「兩個欄位現在各有一筆 Instance／Areas 的來源，但仍不在 `NeededBy` 裡」。
- 新測試：`FireReviewIntegrationTests` 5 條（兩個事實流進區劃輸入、0 是未填而不是連跨 0 層、
  未勾選的避難層通達是否而不是未填、改連跨樓層數讓 stored run 需更新、有來源但不擋前置檢查）、
  `FireReviewInputRowTests` 3 條（小於 1 視同未填、避難層通達三態、只有挑空才報缺參數）、
  `AtriumExemptionTests` 1 條 Theory（6 個案例）。
- **未實機驗證。** 要驗時：以 `load_shared_parameters` 依
  `assets/SharedParameters/fire-review-shared-params.txt` 把兩個新參數綁到 Areas（實體參數），
  開批次面板的「區劃」分頁，把一個挑空的「區劃用途」填成 `挑空`，確認「連跨樓層數」與
  「避難層通達」兩欄由淡出轉為正常、可輸入、寫入模型後回讀正確，且在連跨欄打 0 會被清成空白。
  另確認選取多個樓梯間後按批次填入，「區劃用途」欄可一次設成 `樓梯間`。
  **本步驟仍然不產生任何第 3 項的結果**——接線是 7d。
- **2026-10-04 實機驗證完成**（驗證清單 V-04／第 2 輪）：用途填 `挑空` 後「避難層通達」欄轉為可勾、
  寫入後回讀正確，批次填入可一次設 `樓梯間`。「連跨樓層數」欄已因決議 35 移除，連跨欄那幾項不再適用。
  回報為整體確認，未附逐欄原文。

### 步驟 7d 的產出與驗證

**第 3 項現在會產生結果。** 判定層（7b）與參數層（7c）都在，本步驟把它們接起來。

改動的檔案：

- `src/BuildingRegulationReview.Application/Checks/VerticalCompartmentInputs.cs`：
  `VerticalCompartmentRequirement` 新增 `AtriumExemption`（`All` 變四項、排在三項第 1 項要求之後
  就是條文順序）；新增 `DeviceRequirements`（原本的三項），`ForUse` 改從它篩；
  `RuleText`＝`"AtriumExemption"`、`Label`＝`挑空免除（第3項）`、`UseOf`＝`挑空`；
  `ActualField`／`Categories` 對它**照舊落到 `_ => throw`**，並在 XML 註解寫明那是刻意的。
- `src/BuildingRegulationReview.Application/Checks/VerticalCompartmentCheck.cs`：區劃迴圈在進設備
  迴圈之前先判 `zone.use == "挑空"`，每個挑空產生一筆 `Atrium(...)` 結果；`VerticalCompartmentFinding`
  的 `Category` 改為可為 null、新增 `Exemption`；新增 `Supplied`／`Flag`／`Text`／`Span` 四個把
  `ReviewInput` 三態翻成值或 `null` 的私有輔助，以及 `AtriumEvidence`／`AtriumFactEvidence`。
- `src/BuildingRegulationReview.Application/Reviews/FireReviewRunner.cs`：日誌那行的抬頭由
  「垂直區劃（第79條之2第1項）」改為「垂直區劃（第79條之2）」——第四列是第 3 項，再寫第 1 項就
  不實。四列的件數由 `shaft.Groups` 自動帶出，不需另外接線。
- `src/BuildingRegulationReview.Application/Reviews/ReviewTable.cs`：`ToEntry` 的 `isArea` 擴充成
  「主體是區劃」——第 3 項的結果因此與區劃面積一樣顯示類別「區劃」、不顯示 Type 欄。
  **`ShaftRequirementOf` 與 `GroupsFor` 完全沒動**：它們照 `All` 比對字樣，第四列自然出現。

### 本輪的四個決定

1. **`ForUse` 改從 `DeviceRequirements` 篩，而不是在 `All` 之後再排除挑空。** 兩種寫法結果一樣，
   但前者讓「設備迴圈看不到第 3 項」是型別上的事實而不是一個 `if`。連帶：
   `Article79_2VerticalCompartmentRuleTests` 的 `Requirements()` MemberData 也改從
   `DeviceRequirements` 取——那兩條 Theory（每項要求都有規則作答、每項要求都屬於面板有的用途）
   問的本來就是「有規則的那幾項」，第 3 項不在其列。
2. **`AtriumExemption.For` 的三態直接對到三個 `ReviewStatus`，沒有第四種寫法。** `Holds`→人工覆核、
   `IsUndecided`→資料不足、`IsInapplicable`→不適用；訊息也分三段。唯一的例外是**非防火構造**那一
   種不適用，它不能接「第1項之區劃分隔照常適用」那句（第 1 項本來就不適用），所以那句只在
   `building.fireResistiveConstruction == true` 時才接。
3. **`Span(...)` 在檢查層再問一次 `IsStatedSpannedFloors`。** 組裝層（`ReviewParameterSnapshot.Stated`）
   已經把 0 濾掉了，但輸入也可能來自測試夾具或未來的面板；「連跨 0 層」在任何一條路上都不可以被
   讀成「三層以下」，所以決議 30 在兩層各守一次。
4. **資料不足的錯誤碼分兩種**：缺口只有 `CompartmentArea`（Area 未放置或未封閉）時用
   `BCR-AREA-002`，其餘（避難層通達、連跨樓層數、裝修等級、防火構造）用 `BCR-PARAM-001`。
   面積讀不到不是「參數沒填」，把它併進去會把人指向錯的地方。

### 測試結果

- `& "C:\Program Files\dotnet\dotnet.exe" build BuildingRegulationReview.sln`：0 警告 0 錯誤。
- `... build src\BuildingRegulationReview\BuildingRegulationReview.csproj`：0 警告 0 錯誤。
- `... test tests\BuildingRegulationReview.Core.Tests`：**1437 通過、0 失敗**（原 1419，新增 19、
  移除 1 個 Theory 案例）。
- 新測試：`VerticalCompartmentCheckTests` 11 條（含一條五案例的 Theory，共 15 個案例）、
  `Article79_2VerticalCompartmentRuleTests` 1 條、`FireReviewIntegrationTests` 3 條。
- 改寫的既有測試（五項，都是「多了一列／多了一個主體」造成的預期值，決議 23 已允許）：
  - `Only_the_hoistway_and_the_shaft_carry_extra_requirements` → 改名
    `Only_the_hoistway_and_the_shaft_hold_a_device_to_a_requirement`，斷言追加「挑空不在
    `DeviceRequirements` 裡」與「`DeviceRequirements` 就是 `All` 去掉第 3 項」。
  - `The_three_rows_are_always_reported_even_when_empty` → 改名 `The_four_rows_…`，列名多一列。
  - `The_three_rules_are_one_priority_and_mutually_exclusive`：比對對象由 `All` 改成
    `DeviceRequirements`（規則檔仍然只有三條，沒有變）。
  - `A_use_with_no_extra_requirement_produces_no_subject`：移除 `挑空` 那個 InlineData——它現在
    有自己的主體了；同一件事由新的挑空測試與上面那條守門測試接手。
  - `A_shaft_maintenance_door_is_two_results_of_the_fifth_review_table_row`／
    `A_zone_use_outside_the_vocabulary_produces_no_vertical_compartment_subject`：日誌抬頭字串
    由「垂直區劃（第79條之2第1項）」改成「垂直區劃（第79條之2）」，前者並加上第四列的件數斷言。
- **兩條證據基線守門測試沒有改，也仍然通過**（7c 已查證過，7d 同樣不需要）。

**未實機驗證。** 本步驟沒有任何 Revit 端改動（面板、參數、標示都沒動），但要在模型上看到第 3 項
的結果，需要 7c 的兩個參數已綁到 Areas、且挑空的「區劃用途」填成 `挑空`。驗時：開「開始檢討」，
確認檢討表第五列「垂直區劃」底下出現第四條要求列「挑空免除（第3項）」、該列的類別欄是「區劃」、
「定位／選取元素」指到該挑空的 Area，且日誌那行寫出四列件數。**第 3 項的結果永遠不會被塗紅。**

**2026-10-04 實機驗證完成**（驗證清單 V-05／第 2 輪）：上述各項皆如預期，免除成立時為符合（決議 39）。
回報為整體確認，日誌那一行的逐字原文未貼。

### 步驟 8 的產出與驗證

依使用者提供的整理筆記〈防火區劃與挑空規則〉與其引用的內政部 106 年 9 月 27 日函，修正與函釋相反
的兩處：決議 28（第二款面積讀挑空自己的面積）與決議 26（免除成立仍無條件免面積檢討）。

- **判定層**：`AtriumExemption.For` 第五個參數改為連通區劃合計面積，缺口旗標 `CompartmentArea`
  改名 `ConnectedArea`；新增 `StatedConnectedArea`、`AtriumExemptionFacts`（兩個檢查共用的事實讀取）。
- **規則層**：白名單新增 `zone.connectedArea` 與三個衍生欄位；規則檔新增 `tw-bcr-83-area-atrium`、
  `tw-bcr-79-area-atrium`，版本 `2026.7-provisional` → `2026.8-provisional`。規則集標題不得出現
  「第83條」（`FireReviewRunner` 以法源條文文字判斷區劃來源），標題改寫為「十一層以上之區劃面積規定」。
- **檢查層**：`CompartmentAreaCheck.DeriveAtriumFacts`；`VerticalCompartmentCheck` 的挑空主體改走
  `AtriumExemptionFacts`，免除成立的訊息改說面積由區劃面積結果接手；`ConnectedArea` 缺口的錯誤碼是
  `ParameterMissing`（不再是 `AreaNotEnclosed`）。
- **參數層**：`防火檢討_連通區劃面積`（GUID `bcf10001-0000-4a00-9b00-000000000014`、NUMBER、Areas，
  主檔 Big5 加一行）；`ReviewParameterSnapshot.Stated` 把 ≤ 0 讀成未填；`FireReviewZoneRow`／
  `FireReviewZoneParameters.ConnectedArea`、Revit 掃描器、批次面板一欄（挑空以外淡出）與批次填入、
  寫入器接受 NUMBER 規格的 Double 參數。面板「適用上限」欄對挑空加註
  `ZoneAreaLimit.AtriumNote`。
- **測試**：`dotnet test tests\BuildingRegulationReview.Core.Tests` **1875 通過、0 失敗**（原 1852）。
  新檔 `Checks/AtriumAreaReviewTests.cs`（以出貨規則端到端跑面積檢查）；`AtriumExemptionTests`、
  `VerticalCompartmentCheckTests`、`FireReviewIntegrationTests` 各補連通面積的案例。改寫的既有測試
  都是預期內的：參數數 16→17、主檔 17→18 行、面積規則 2→4 條與優先序、`ShippedVersion`、五個以
  規則引擎直接組事實的 helper 補 `zone.atriumMerged = false`、挑空的面板上限說明多一段註記。
- `dotnet build src\BuildingRegulationReview\BuildingRegulationReview.csproj --no-incremental`：0 警告 0 錯誤。

**Code review 後的修正**（四項重要問題中，三項照建議修、一項由使用者選方案）：

1. 面積核對（Revit 面積 vs. 邊界量得面積）不再把併入連通區劃的挑空的符合／未符合降為人工覆核。
2. `RuleOutcome.IsApplicabilityUndecided`；`FireReviewRunner.HostLegalReferences` 不把「沒有規則作出
   判定」的結果算成第 83 條區劃。
3. 新參數 `防火檢討_挑空起始樓層序`（GUID `…0015`），最高樓層改由它算（決議 34）；Area 所在樓層序
   不在範圍內時判無法判讀並說明。
4. 面板數字解析搬到 `Application/Parameters/TypedNumbers.cs`（可測），讀不懂的輸入不寫入、格子標紅；
   顯示與寫入保留完整精度。

另採納兩項建議：檢討讀取器與面板掃描器遇到 AREA 規格會換算成 ㎡；同一區劃各 Area 填得不一致的原因
帶到區劃面積結果的缺口。`tw-bcr-79-area-atrium` 的證據加上樓層序與最高樓層序。測試 **1904 通過、
0 失敗**；外掛專案 0 警告 0 錯誤。

**決議 35：改由各樓層區劃推得。** 使用者決定取消三個參數（連跨樓層數、連通區劃面積、挑空起始樓層序），
改從其他樓層的區劃推算。以下取代上面「參數層」與 code review 第 3、4 項的做法：

- **新增** `Application/Candidates/AtriumStack.cs`（`StoreyZone`、`StoreyZoneMap`、`AtriumStack`、
  `AtriumStackResolver`）與 `Revit/Candidates/RevitStoreyZoneReader.cs`；`ReviewModelFacts.Storeys`／
  `WithStoreys`；`ReviewInputAssembler.AtriumInputs` 為挑空區劃產生三個輸入；`FireReviewModel` 在掃描時
  讀各樓層區劃。
- **移除** 三個參數的輸入來源、參數目錄宣告、主檔三行（檔頭註記作廢 GUID）、`FireReviewZoneRow`／
  `FireReviewZoneParameters` 的對應成員、Revit 掃描器讀取、批次面板「連跨樓層數」欄與批次填入欄位、
  `ReviewParameterSnapshot.Stated`、`TypedNumbers`（面板不再有數字欄需要解析）、讀取器與寫入器為
  NUMBER／AREA 加的處理。
- **保留** code review 第 1、2 項的修正、`AtriumExemptionFacts`、衍生欄位與兩條挑空面積規則；
  `zone.atriumBaseFloor` 白名單欄位保留，改由推算器供給。
- **測試**：`dotnet test` **1902 通過、0 失敗**。新檔 `Candidates/AtriumStackTests.cs`（14 條，含錯位挑空、
  上層區劃無挑空即止、所在區劃正下方皆計、各種無法判定）；整合測試改以 `AtriumStoreys()` 注入各樓層
  區劃，並驗「其他樓層區劃面積改變 → 本工作包需更新」。參數數改為 15、主檔 16 行。

**未實機驗證。** 驗時：

1. 「防火參數一鍵建立」應列 **15** 個參數，不再有連跨樓層數；批次面板「區劃」分頁只剩「避難層通達」
   一個挑空欄位。
2. 依 §3.8 的慣例放挑空 Area（例如只有 2F 開洞就只在 2F 放），挑空用途填 `挑空`、避難層通達視情況。
3. 開始檢討 2F 那個工作包：檢討表「挑空免除（第3項）」的證據來源應寫「由跨樓層區劃推得：挑空 2F「…」；
   連通區劃 1F「…」 N ㎡、2F「…」 M ㎡」，連跨 2 層，連通面積為兩者之和。
4. 改 1F 區劃的範圍或面積後回到 2F 工作包：已存的檢討應變成「需更新」。
5. 錯位挑空（3F 挑空不在 2F 挑空正上方、但在 2F 所在區劃正上方的區劃裡）應算成同一個挑空、連跨 3 層。
6. 挑空 Area 沒有相接區劃、或放在最低一層時，結果是資料不足並說明原因。

**決議 36（code review 後）**：垂直區劃不算所在區劃；起始樓層也計入洞口正下方的區劃；樓層序須連續；
跨 Level 的區劃標記判不可用（讀取器以（工作包, Zone ID, Level）分組、`StoreyZoneMap.Find` 以本層 Level 找）；
讀不到其他樓層時三個輸入帶原因（`ReviewModelFacts.WithStoreysUnavailable`）；只有本工作包有挑空才讀其他
樓層；樓層排序改用 `ProjectElevation`、同高程依 UniqueId；`CompartmentAreaCheck` 的矛盾訊息不再提已刪除
的參數；移除 `StatedSpannedFloors`／`StatedFloorNumber` 與讀取器多餘的 try/catch。測試 **1907 通過、0 失敗**
（`AtriumStackTests` 新增 6 條、整合測試 1 條）。

**決議 37**：新增 `Checks/MergedAtriums.cs`；`FireResistanceCheck`、`OpeningProtectionCheck`、
`FireReviewRunner.HostLegalReferences` 據以把免除成立的挑空與連通區劃之間的構件視為非區劃邊界（§3.9）。
整合測試 3 條（內部線不算邊界且外牆仍算、樓梯間旁仍算、免除不成立仍算）。Code review 後修正：取樣
改為固定間距並要求每個接觸的區劃都被取樣碰到、接觸垂直區劃即維持邊界、兩個免除挑空之間維持邊界、
容差改用候選解析設定、區劃線畫在牆面上（關係不明）也適用；`Checks/MergedAtriumsTests.cs` 5 條。測試
**1915 通過、0 失敗**。

**決議 38、39**：新增 `防火檢討_阻熱性` 與 `tw-bcr-79-opening-insulation`（規則集 `2026.8-provisional` →
`2026.9-provisional`）、批次面板類型分頁「阻熱性」欄、證據基線納入阻熱性；第 3 項免除成立改為符合。
測試 **1920 通過、0 失敗**。

**規則集版本為 `2026.9-provisional`，既有工作包第一次檢討會要求確認改用新版本。**

**2026-10-04 實機驗證完成**（驗證清單 V-23／第 2 輪）：上列 6 項，以及決議 36～39 的驗證項目
（連通面積計入範圍、無法推得的各種情形、第 83 條那一路、內部線不算區劃邊界、阻熱性）皆如預期。
一鍵建立實際列 16 個參數（決議 38 加入 `防火檢討_阻熱性` 後，上面第 1 項的 15 已過時）。
回報為整體確認，證據來源字串的逐字原文未貼。
