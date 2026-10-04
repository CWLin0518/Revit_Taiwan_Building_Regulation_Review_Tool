# 垂直區劃：第 79 條之 2

功能 ID：`vertical-compartment`

狀態：**規則層、參數層、檢查層、一次檢討的接線與檢討視圖標示已完成**（本文件 §5、§6、§7、§8、§10）。
按「開始檢討」會產生第 79 條之 2 的結果、檢討表第五列與三條要求列，證據基線看得見遮煙性能，未符合的
防火設備會在檢討視圖被塗紅、描述帶出是哪幾項要求未符合（不出註解、不發圖號，§7.2）。第 3 項挑空的
兩款免除**設計已定案、程式未開始**：事實來源與判定見 §3.6，產出見 §7.3，決議 23～29，實作分
步驟 7b～7d（§12）。

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
| 第二款：連跨樓層數在三層以下 | `zone.spannedFloors`（Quantity，整數層） | **新** 參數 `防火檢討_連跨樓層數`（Instance、Integer、Areas） | 步驟 7c |
| 第二款：樓地板面積在一千五百平方公尺以下 | `zone.area` | **既有**（Revit Area，模型擁有，`CompartmentAreaInputs.ModelOwnedFields` 不許由輸入覆蓋） | 已完成 |
| 第 1 項的適用前提：防火構造建築物 | `building.fireResistiveConstruction` | **既有** | 已完成 |

兩個新欄位是 `zone.*`（對所有類別開放），因為它們是 Area 實體上的事實，與 `zone.floorNumber`
同一類；**沒有任何規則讀它們**，它們只餵第 3 項的判定。連帶兩件事：

- `ReviewInputSources.NeededBy` 是規則驅動的（`FieldsUsedBy`），所以這兩個參數**不會**變成前置
  檢查的阻擋項——這是刻意的（決議 27）。
- 但它們仍要進 `RuleFieldCatalog`：組裝層把整份 `zone.*` 輸入 `ApplyTo(facts)` 到每個設備主體，
  白名單外的欄位會在那裡例外。

為什麼這兩件事只能由設計者宣告：

- **避難層**是「具有出入口通達基地地面或道路之樓層」（第 1 條），基地地面與道路的關係不在本工具
  的資料模型裡，而且一棟建築物可能有多個避難層。「通達其直上層或直下層」還要知道挑空的兩端各是
  哪一層——不是一個 Area 看得出來的事。
- **連跨樓層數**沒有可靠的模型表達：一個連跨三層的挑空，設計者可能只在起始層放一個 Area、也可能
  每層各放一個，工具的區劃是逐層的（`zone.levelName` 是單數）。要由幾何推導就得跨樓層追挑空的
  垂直孔洞，那是新的幾何解析，與 §4「本功能不需要新的幾何解析」相衝。

#### 判定式與狀態

```
第一款成立 ⟺ zone.linksRefugeFloor == true
            && zone.interiorFinish ∈ { 耐燃一級, 耐燃一級含底材 }
第二款成立 ⟺ zone.spannedFloors <= 3 && zone.area <= 1500 m²
第 3 項免除成立 ⟺ 第一款成立 或 第二款成立
```

「三層以下」與「一千五百平方公尺以下」都含等號。第一款只要求「以耐燃一級材料裝修」，沒有第 83 條
第三款那種「包括底材」的字，所以 `耐燃一級含底材` 也算符合——它比款文要求的更嚴。

| 模型情形 | 判定 | 狀態 |
| --- | --- | --- |
| 區劃範圍有問題（未封閉、重疊） | 不判定 | `人工覆核`（同 §4 的界線） |
| 非防火構造建築物 | 第 1 項本來就不適用，無免除可言 | `不適用` |
| 防火構造未填 | 連第 1 項適不適用都不知道 | `資料不足` |
| 連跨 2 層、面積 320 ㎡ | 符合第二款 | `人工覆核` |
| 避難層通達＝是、裝修耐燃一級 | 符合第一款 | `人工覆核` |
| 任一款已成立，另一款的事實缺著 | 免除已成立 | `人工覆核`（缺口不影響，同 §3.3 的精神） |
| 連跨 5 層、避難層通達＝否 | 兩款均不成立 | `不適用` |
| 連跨 5 層、避難層通達＝是、裝修等級＝無 | 兩款均不成立 | `不適用` |
| 連跨 5 層、避難層通達未填 | 第一款無法判定、第二款不成立 | `資料不足`（缺 `防火檢討_避難層通達`） |
| 連跨未填、避難層通達＝否 | 第二款無法判定、第一款不成立 | `資料不足`（缺 `防火檢討_連跨樓層數`） |
| 連跨 5 層、避難層通達＝是、裝修等級讀不出來 | 第一款無法判定、第二款不成立 | `資料不足`（缺裝修等級） |
| 面積 2000 ㎡、連跨未填、避難層通達＝否 | 第二款已確定不成立（面積），第一款不成立 | `不適用`（不必問連跨） |

三條規矩，和引擎的六態同一個形狀：**已成立的一款讓其他缺口不重要**；**已確定不成立的一款不必
再問它缺的事實**；**只有「這一款無法判定、另一款不成立」才是資料不足**。

「未填」與「同一區劃的各 Area 填得不一致」是同一種缺口：後者在 `ReviewParameterSnapshot.ZoneInput`
就變成 `ReviewInput.Unreadable`（與 `zone.use` 同一條路），判定層兩者都當作無法判定，不去挑一個值。

**沒有 `符合`，也沒有 `未符合`。** 不符合第 3 項不是違規——它只表示第 1 項照常適用，而第 1 項本文
的要求已由既有的邊界規則檢討（§3.4、決議 3）。因為沒有 `未符合`，第 3 項的結果永遠不會被塗紅
（`ReviewMarkupPlan` 只處理 `Fail`），與決議 20 一致。

免除成立為什麼是 `人工覆核` 而不是 `不適用`／`符合`：免除一旦成立，該挑空若真的不依第 1 項區劃，
它就不是「依第七十九條之二規定之垂直區劃」，第 83 條的除外文字（§2.3）不再涵蓋它，其樓地板面積
應回到第 79 條／第 83 條的面積檢討。工具目前對挑空是**無條件**免面積檢討的（§9 第 3 項），所以
這個連帶影響只能由人接手——完整理由見決議 25、26。

判定寫成一個純計算 `AtriumExemption.For(...)`（回傳 `AtriumExemption`：成立的款次、缺口旗標、
給人看的一句話），與 `ZoneAreaLimit.For(...)` 同形，所以判定本身可以單獨測、也可以在批次面板上
顯示（面板側未排程）。三態由三個屬性讀出：`Holds`（免除成立）、`IsUndecided`（`Gaps` 非空，資料
不足）、`IsInapplicable`（兩者皆非）。缺口旗標除了上表四項事實，還有一項 `CompartmentArea`：
`CandidateZone.RevitAreaSquareMeters` 是 `double?`（任一 Area 未放置或未封閉就讀不到），純函式
不能把它當成零，所以面積讀不到時第二款也是無法判定。

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
| `防火檢討_連跨樓層數` | Instance、Integer、GUID `…0010` | Areas | 第 3 項第二款之連跨樓層數（`zone.spannedFloors`） | 已完成 |
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

第 3 項的兩個新參數（`防火檢討_連跨樓層數`、`防火檢討_避難層通達`）與上面三個不同，**不列入前置
檢查的必要參數**：它們只有在專案裡真的有挑空時才需要，列為必要會讓沒有挑空的專案也無法開始檢討
（決議 27）。沒填就是該挑空一筆資料不足，訊息指名參數名。兩者都只寫進主檔
`assets/SharedParameters/fire-review-shared-params.txt`（Areas 的參數不在門／窗／嵌板那份裡），
一樣維持 Big5／cp950。

**「沒填」對這兩個參數不是同一件事**（決議 30）。Revit 的 YESNO 與 INTEGER 都存成整數，參數一綁上
Areas，全專案每個 Area 立刻讀得到值，沒有空白狀態：

- `防火檢討_避難層通達` 未勾選讀成 `否`。這是一個答案，而且是嚴的那一邊——第一款直接不成立，不會
  誤放免除，所以照單全收，與 `防火檢討_自動滅火設備` 同。
- `防火檢討_連跨樓層數` 未填讀成 `0`。「連跨 0 層」不是任何設計說得出口的事實，但第二款的
  「三層以下」會把它當成成立，於是**沒有人說過話的挑空被自動免除**——正好與 §3.6 狀態表
  「連跨未填＝資料不足」相反。因此**小於 1 的讀數一律視同未填**，判定寫在
  `AtriumExemption.StatedSpannedFloors`，批次面板與 `ReviewInputAssembler` 都走它，兩邊對「未填」
  的認定是同一個決定。

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
3. **第 3 項（挑空的兩款免除）：步驟 7d 起會產生結果，但它不改變任何既有檢討。** 兩個新事實
   （連跨樓層數、避難層通達）由設計者宣告。即使免除成立，工具**仍然**
   無條件讓挑空免於面積檢討、仍然由既有邊界規則要求它的牆與開口——面積檢討的連帶影響只以
   一筆`人工覆核`交給人（決議 25、26）。所以這個方向依舊是**寬鬆**的，與清單外用字不豁免的
   保守方向相反，必須記在這裡。第 3 項不成立時工具同樣不會提出新的要求（第 1 項本文由既有
   規則檢討，§3.4）。
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
11. **第 3 項第二款的「樓地板面積」讀作該挑空自己的面積**（`zone.area`），不是它連通各層樓地板
   面積的合計（決議 28）。這是一個明示的解讀：另一種讀法會讓幾乎每個挑空都超過一千五百平方
   公尺，而工具也沒有「這個挑空連通哪些樓層的哪些面積」的資料。連跨樓層數同樣是**設計者宣告**
   的數字，工具不驗證它與模型是否相符——一個挑空在 Revit 裡逐層各放一個 Area 時，每個 Area 都要
   填同一個連跨樓層數，工具不會比對它們是否一致（各 Area 填得不一致時的處理與 `zone.use` 同，
   見第 10 項）。
12. **第 3 項的免除不擴及樓梯間、昇降階梯間**，雖然兩款的文字都寫「挑空、樓梯及其他類似部分」
   （決議 29）。

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
| 25 | 免除成立是 `人工覆核`，不是 `不適用`，更不是 `符合` | 免除一旦成立，該挑空若不依第 1 項區劃就不是「依第七十九條之二規定之垂直區劃」，第 83 條的除外文字不再涵蓋它，其樓地板面積應回到面積檢討；工具目前無條件豁免挑空的面積檢討，這件事只能由人接手。用 `符合` 會讓它在檢討表算進符合（spec 11.7），讀起來像這個挑空已經處理完了 |
| 26 | 面積豁免**維持無條件**，不改成「第 3 項成立就不豁免」 | 引擎的豁免語意是「豁免無法判定且本文不成立 → 資料不足」。挑空常常大於一○○平方公尺，所以只要這兩個新參數沒填，每個既有專案的大挑空都會從「免適用」變成「資料不足」——正是 §3.4 要避開的那種全專案資料不足。代價是方向仍然寬鬆（§9 第 3 項），但工具現在會在免除成立時明講連帶影響 |
| 27 | 兩個新事實**不進前置檢查的阻擋項** | 前置檢查是全專案的，而這兩個參數只有在專案裡真的有挑空時才需要；列為必要參數會讓沒有挑空的專案也無法開始檢討。缺了就是該挑空一筆資料不足、訊息指名參數名（同 `An_unbound_smoke_seal_is_insufficient_data_and_names_its_own_parameter`）。附帶好處：`ReviewInputSources.NeededBy` 是規則驅動的，沒有規則讀這兩個欄位，所以不必為此在 `NeededBy` 開後門 |
| 28 | 第二款的「樓地板面積」是**該挑空自己的** `zone.area` | 兩款的敘述都在修飾「挑空、樓梯及其他類似部分」，連跨樓層數與樓地板面積是同一個主語的兩個屬性。合計的讀法會讓幾乎每個挑空都超標，而工具也沒有那份資料。記在 §9 第 11 項當明示解讀 |
| 29 | 第 3 項只給 `挑空`，不擴及 `樓梯間`／`昇降階梯間` | 前言是「挑空符合下列情形之一者」（§2.1 第三點）；把免除擴及樓梯間是放寬，方向不對。兩款文字裡的「樓梯」因此不另立用字 |
| 30 | `防火檢討_連跨樓層數` 小於 1 視同未填 | Revit 的 INTEGER 參數沒有空白狀態，一綁上 Areas 全專案立刻讀成 0；「連跨 0 層」會通過第二款的「三層以下」，讓沒人填過的挑空自動免除，與 §3.6 狀態表相反。避難層通達那個 YESNO 不需要同樣處理：未勾選＝否是答案，而且是嚴的那一邊（§6） |

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
