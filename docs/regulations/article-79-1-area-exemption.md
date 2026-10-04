# 第 79 條之 1：無法區劃分隔部分得不受第 79 條第 1 項限制

功能 ID：`article-79-1-area-exemption`

狀態：**步驟 1～4 全部完成；2026-10-05 Revit 實機驗證完成**（驗證清單 V-06、V-07／第 5 輪）。
本文件是規格；實作分步驟 1～4（§12）。規則檔至今一個字都沒動，既有判定沒有任何變化。

## 1. 功能摘要

第 79 條第 1 項要求防火構造建築物每一、五○○平方公尺切一塊防火區劃。第 79 條之 1 給了六種
用途一條出路：那些**無法區劃分隔**的部分，只要自己以一小時以上防火時效之牆壁、防火門窗等
防火設備與該處防火構造之樓地板**自成一個區劃**，就不受那個面積上限拘束——觀眾席不必為了
一、五○○平方公尺被切成兩半。

本功能把這件事做成一個**分類**：某個區劃到不到得依第 79 條之 1 免除第 79 條第 1 項的面積
上限。它和 [垂直區劃](vertical-compartment.md) §3.6 的第 79 條之 2 第 3 項挑空免除是同一種
東西（不是要求、不走規則引擎、三態），但方向相反：

| | 第 79 條之 2 第 3 項（挑空） | 第 79 條之 1（本功能） |
| --- | --- | --- |
| 免除的對象 | 第 79 條之 2 第 1 項的**單獨區劃義務** | 第 79 條第 1 項的**面積上限** |
| 工具目前的立場 | 比條文**寬**（挑空無條件免面積檢討） | 比條文**嚴**（面積照常檢討，免除不自動放行） |
| 免除成立時的狀態 | 人工覆核 | 人工覆核 |

「比條文嚴」是刻意的，理由見 §3.6、§3.7：第 79 條之 1 的三個要素裡有兩個工具看不到，
工具不能替設計者宣告它們成立。

## 2. 法源條文

### 2.1 第 79 條之 1

> 防火構造建築物供左列用途使用，無法區劃分隔部分，以具有一小時以上防火時效之牆壁、防火門窗等
> 防火設備與該處防火構造之樓地板自成一個區劃者，不受前條第一項之限制：
> 一、建築物使用類組為Ａ－１組或Ｄ－２組之觀眾席部分。
> 二、建築物使用類組為Ｃ類之生產線部分、Ｄ－３組或Ｄ－４組之教室、體育館、零售市場、停車空間
> 及其他類似用途建築物。
> 前項之防火設備應具有一小時以上之阻熱性。

拆成四個要素：

| 要素 | 條文字句 | 工具看得到嗎 |
| --- | --- | --- |
| （甲）防火構造建築物 | 「防火構造建築物」 | **看得到** `building.fireResistiveConstruction` |
| （乙）用途落在兩款之一 | 第一款、第二款 | **看得到** `building.use` ＋ `zone.use`（§3.4） |
| （丙）無法區劃分隔 | 「無法區劃分隔部分」 | 看不到，只能由設計者宣告（§3.3） |
| （丁）自成一個區劃 | 「以具有一小時以上防火時效之牆壁、防火門窗等防火設備與該處防火構造之樓地板自成一個區劃者」＋第 2 項的一小時阻熱性 | **看不到**（§3.7） |

### 2.2 「前條第一項」是第 79 條第 1 項，不是整條第 79 條

第 79 條有四項：第 1 項是區劃面積與區劃構造，第 2 項是自動滅火設備得減半計算，第 3、4 項是
區劃牆壁突出外牆面（已由 [帷幕牆交接](curtain-wall-fire-compartment.md) 實作）。第 79 條之 1
只寫「不受前條**第一項**之限制」，所以：

- 免除的只有 `tw-bcr-79-area`（第 79 條第 1 項的面積上限）。
- `tw-bcr-79-wall-rating`、`tw-bcr-79-floor-rating`、`tw-bcr-79-opening`（第 1 項的區劃構造
  要求）**不受影響**——而且不能受影響：第 79 條之 1 的免除條件本身就要求那道牆有一小時時效、
  那些開口是防火門窗。免掉它們等於免掉免除的前提。
- `tw-bcr-79-curtain-wall-junction`（第 3、4 項）不受影響。

### 2.3 與第 83 條的關係：第 79 條之 1 不碰十一層以上部分

第 83 條是十一層以上部分的區劃面積規定，是**另一條條文**，第 79 條之 1 沒有提到它。所以一個
在第 12 層的觀眾席，即使符合第 79 條之 1，仍受第 83 條的一○○／二○○平方公尺上限拘束。

規則引擎的優先序機制已經替我們做對這件事，不需要額外寫程式：`tw-bcr-83-area` 在優先序 20、
`tw-bcr-79-area` 在優先序 10，引擎**逐層**處理（`RuleEngine.Evaluate` 的 `foreach (var tier ...)`），
高優先序有規則適用時就在那一層作答，低優先序那一層根本不會執行。十一層以上由第 83 條作答，
第 79 條之 1 的免除連被問到的機會都沒有。這件事由測試守著（§10 的
`Article_79_1_does_not_reach_the_eleventh_storey`）。

十層以下只有 `tw-bcr-79-area` 適用，本功能才有作用範圍。

## 3. 檢查項目與判定式

### 3.1 這是分類，不是要求

和 [垂直區劃](vertical-compartment.md) §3.6 決議 24 同一個理由：規則引擎的每條規則都要有一個
可比較的 `requiredValue`，而第 79 條之 1 沒有——「得不受面積限制」不是一個量。所以它不走規則
引擎，寫成一個純計算 `Article79_1Exemption.For(...)`，與 `AtriumExemption`、`ZoneAreaLimit`
同形。

### 3.2 受檢主體

| | |
| --- | --- |
| 主體 | **區劃本身**。`zone.use` 落在 §3.4 六個用字之一的每個區劃一筆結果 |
| `SubjectUniqueIds` | `zone.AreaUniqueIds`——與 `CompartmentAreaCheck` 同一批 Area，所以「定位／選取元素」指到同一個地方 |
| 檢討類型 | 新的 `ReviewCheckTypes.AreaExemption`（§5.1）。**不是**新的 `RuleCategory`——本功能不走引擎 |
| 證據 | `zone.id`、`zone.use`、`building.use`、`building.fireResistiveConstruction`、`zone.cannotBeSubdivided`，以及 §3.5 讀到的每一個事實 |
| 法源條文 | `建築技術規則建築設計施工編第79條之1（無法區劃分隔部分得不受第79條第1項限制）`。**不得出現「第83條」**（§2.3） |
| `RuleId`／`RuleVersion` | 規則集的 id 與版本——沒有規則作答，與 `AtriumExemption` 同形 |

`zone.use` 不在六個用字之列時**不產生主體**，不是產生一筆「不適用」。理由與 §3.4 最後一點
相同：那是絕大多數的區劃，每個都掛一筆空結果只會把檢討表淹掉。

### 3.3 事實來源

| 條文要素 | 欄位 | 來源 | 現況 |
| --- | --- | --- | --- |
| （甲）防火構造建築物 | `building.fireResistiveConstruction` | 既有 | 已完成 |
| （乙）建築物使用類組 | `building.use` | 既有，正規形已就位（[`building.use` 寫法正規化](building-use-groups.md)） | 已完成 |
| （乙）該部分之用途 | `zone.use` | 既有欄位，**新增六個議定用字**（§3.4） | 步驟 1 |
| （丙）無法區劃分隔 | `zone.cannotBeSubdivided`（Boolean） | **新**參數 `防火檢討_無法區劃分隔`（Instance、YESNO、Areas） | 步驟 2 |
| （丁）自成一個區劃、防火設備阻熱性 | — | **沒有欄位**，永遠由人確認（§3.7） | 不實作 |

（丙）只能由設計者宣告：「無法區劃分隔」是對設計本身的判斷——這個觀眾席能不能被一道牆切成
兩塊而仍然是個觀眾席。它不是幾何事實（任何空間在幾何上都切得開），是用途與機能的判斷，模型
裡沒有任何東西表達它。這與 `zone.spannedFloors`、`zone.linksRefugeFloor` 是同一類：Area 實體
上的事實，`zone.*` 欄位，對所有類別開放，**沒有任何規則讀它**，只餵這個判定。連帶兩件事與
[垂直區劃](vertical-compartment.md) §3.6 完全相同：

- `ReviewInputSources.NeededBy` 是規則驅動的（`FieldsUsedBy`），所以這個參數**不會**成為前置
  檢查的阻擋項。刻意的：沒有觀眾席的專案不該被要求綁一個用不到的參數。
- 但它仍要進 `RuleFieldCatalog`：組裝層把整份 `zone.*` 輸入 `ApplyTo(facts)` 到每個受檢主體，
  白名單外的欄位會在那裡例外。

### 3.4 兩款的用字

第一款只有一個用途，第二款的斷句有歧義。條文是：

> Ｃ類之生產線部分、Ｄ－３組或Ｄ－４組之教室、體育館、零售市場、停車空間及其他類似用途建築物

「Ｄ－３組或Ｄ－４組之」到底管到哪裡？只管「教室」，或連「體育館、零售市場、停車空間」一起
管？**零售市場與停車空間都不是 D-3／D-4 的用途**（D 類是休閒、文教類），所以那個限定詞管不到
它們——這一點文義上很清楚。體育館則兩種讀法都說得通。

議定的用字表，以及每個用字要不要一併比對類組：

| 用字 | 條文用語 | 款次 | 併看的 `building.use` |
| --- | --- | --- | --- |
| `觀眾席` | Ａ－１組或Ｄ－２組之觀眾席部分 | 第一款 | `A-1`、`D-2` |
| `生產線` | Ｃ類之生產線部分 | 第二款 | `C-1`、`C-2` |
| `教室` | Ｄ－３組或Ｄ－４組之教室 | 第二款 | `D-3`、`D-4` |
| `體育館` | 體育館 | 第二款 | **不比對** |
| `零售市場` | 零售市場 | 第二款 | **不比對** |
| `停車空間` | 停車空間 | 第二款 | **不比對** |
| 其他任何文字 | — | — | 不產生第 79 條之 1 主體 |

決定：**體育館採「不比對類組」的寬讀法。** 這在別的地方會是危險的方向，在這裡不是——本功能的
免除**從來不會**變成「符合」，也不會把面積結果從未符合改成符合（§3.7）。寬讀只會多出一筆人工
覆核，嚴讀則會漏掉一個設計者真的有權主張的免除。兩種錯誤的代價不對稱，所以取寬。同樣的理由，
`零售市場` 與 `停車空間` 不比對類組是文義加上這個不對稱的結果，不只是文義。

**「其他類似用途建築物」不列。** 與 [`zone.use` 用字表](zone-use-vocabulary.md) §2 的
「其他類似部分」同樣處理：列不完，而且它是給審查者裁量的字眼。清單外不產生主體，審查者要主張
時走 spec §11.8 的人工覆寫，直接在面積結果上寫理由——那才是「裁量」該落地的地方。

**不做全形／同義詞折疊。** `zone.use` 的立場不變（同文件 §2 最後一點）：`觀眾廳`、`看台`、
`停車場` 都不算。要放寬只能擴充這張表。`building.use` 那一側的折疊已完成，所以類組填
`Ａ－１組` 或 `A-1` 都比得中——這是 `building.use` 的性質，不是 `zone.use` 的。

### 3.5 判定式與狀態

```
款次成立（use）⟺ zone.use == use
                 && （該 use 不比對類組 或 building.use ∈ 該 use 的類組清單）
第79條之1 免除成立 ⟺ （甲）building.fireResistiveConstruction == true
                    && （乙）某一個款次成立
                    && （丙）zone.cannotBeSubdivided == true
```

（丁）不在判定式裡：它沒有欄位，永遠是人工確認的那一段（§3.7）。

| 模型情形 | 判定 | 狀態 |
| --- | --- | --- |
| 區劃範圍有問題（未封閉、重疊） | 不判定 | `人工覆核` |
| 非防火構造建築物 | 第 79 條第 1 項本不適用，無免除可言 | `不適用` |
| 防火構造未填 | 連第 79 條第 1 項適不適用都不知道 | `資料不足` |
| 用途類組 `A-1`、`觀眾席`、無法區劃分隔＝是 | 符合第一款 | `人工覆核` |
| 用途類組 `C-2`、`生產線`、無法區劃分隔＝是 | 符合第二款 | `人工覆核` |
| `體育館`、無法區劃分隔＝是（類組任何值） | 符合第二款 | `人工覆核` |
| `觀眾席`、無法區劃分隔＝**否** | （丙）不成立 | `不適用` |
| 用途類組 `B-2`、`觀眾席` | 類組不符第一款，`觀眾席` 也不在第二款 | `不適用` |
| `觀眾席`、**類組未填** | 無法判定類組是否為 A-1／D-2 | `資料不足`（缺建築物用途類組） |
| `觀眾席`、類組未填、無法區劃分隔＝**否** | （丙）已確定不成立 | `不適用`（不必問類組） |
| `體育館`、無法區劃分隔未填 | 無法判定（丙） | `資料不足`（缺無法區劃分隔） |
| 第 12 層的 `觀眾席`（任何填法） | 由第 83 條作答，本功能不介入 | **不產生主體**（§2.3） |

三條規矩與 `AtriumExemption` 同一個形狀：**已確定不成立的要素讓其他缺口不必再問**；**已成立的
要素不再是缺口**；**只有「這個要素無法判定、且沒有其他要素已經否決」才是資料不足**。（丙）最
便宜也最能一槌定音（一個是非值），所以先讀它；（甲）決定第 79 條第 1 項適不適用，所以比（乙）
先讀。順序只影響訊息說什麼，不影響三態。

**沒有 `符合`，也沒有 `未符合`。** 與 `AtriumExemption` 同理：不符合第 79 條之 1 不是違規，
只表示第 79 條第 1 項照常適用。因為沒有 `未符合`，本功能的結果永遠不會被塗紅
（`ReviewMarkupPlan` 只處理 `Fail`）。

「未填」與「同一區劃各 Area 填得不一致」是同一種缺口：後者在
`ReviewParameterSnapshot.ZoneInput` 就變成 `ReviewInput.Unreadable`，判定層兩者都當作無法判定，
不去挑一個值。

### 3.6 為什麼不寫進 `tw-bcr-79-area` 的豁免清單

技術上做得到：規則 DSL 的豁免條件是布林運算式，支援 `&&`／`||`，寫
`zone.use == "觀眾席" && (building.use == "A-1" || building.use == "D-2") && zone.cannotBeSubdivided == true`
會過編譯，而且三值邏輯的 `false && unknown == false` 保證只有真的填了 `觀眾席` 的區劃才會去問
類組，不會波及其他專案的既有判定。

不這樣做的理由是判定內容，不是技術：

1. **（丁）是免除的條件，而工具驗不到它。** 豁免成立時引擎給 `NotApplicable`，訊息是
   「符合豁免條件：…」——那是工具在宣告這個區劃已經自成一個一小時防火區劃、而且它的防火設備
   有一小時阻熱性。工具沒有阻熱性這個欄位（第 83 條第 5 款同樣因此不檢討，見
   [帷幕牆交接](curtain-wall-fire-compartment.md) §9），也沒有「這個區劃的周界是否封閉且全部
   是區劃邊界」的檢查。這是 spec §11.3「不可將資料不足誤判為符合」的正面違反。
2. **`NotApplicable` 會把它從檢討表上藏起來。** 一個三○○○平方公尺的觀眾席，最需要被看見的
   就是它——是否真的無法區劃分隔、是否真的自成一區劃，正是審查者要看的兩件事。
3. **挑空的無條件豁免不是先例。** `zone.use == "挑空"` 那類豁免之所以可以無條件，是因為第 83 條
   本文自己寫了「除依第七十九條之二規定之垂直區劃外」——**條文**把它們排除在面積計算外。第 79
   條之 1 的免除是**附條件**的，條件裡有工具看不到的事實。兩者形式相似、性質不同。

所以：`tw-bcr-79-area` 與 `tw-bcr-83-area` 的豁免清單**一字不動**，兩條規則的 `version` 也不
變（沒有行為變更）。這一點由 §10 的 `The_area_rules_carry_no_article_79_1_exemption` 守著，
避免後續 session 把六個新用字順手加進豁免清單。

### 3.7 為什麼面積結果仍然是「未符合」

一個符合第 79 條之 1 的三○○○平方公尺觀眾席，`tw-bcr-79-area` 照樣判未符合，本功能另給一筆
人工覆核。這不是漏洞，是分工：

- 工具說得出口的是「面積超過一、五○○平方公尺」（事實）與「這個區劃看起來落在第 79 條之 1
  的兩款之內，設計者也宣告無法區劃分隔」（也是事實）。
- 工具說不出口的是「所以它合法」——那要（丁）成立，而（丁）要人看圖。
- 放行的機制已經有了：spec §11.8 的人工覆寫，需輸入原因、保存操作者與時間、規則版本變更後轉為
  需重新確認。本功能的人工覆核結果因此要把**覆寫時該引用的條文與該確認的兩件事**寫在描述裡
  （§7.1），讓覆寫的人不必自己回去翻條文。

方向是嚴的，而嚴的方向是安全的方向——與 `zone.use` 用字表「清單外不豁免」同一個判斷。

## 4. 幾何解析

**本功能不需要新的幾何解析。** 四個要素裡三個是參數，第四個（丁）不實作。區劃範圍本身的問題
（未封閉、重疊）沿用 `CompartmentAreaCheck` 已有的界線，結果是人工覆核。

## 5. 規則集擴充

**沒有新規則，規則檔一個字都不動。** `Data/fire-review-rules.json` 的 `rules` 陣列不變、
`version` 不變、`title` 不變。要改的是兩處常數／白名單：

### 5.1 新增 `ReviewCheckTypes.AreaExemption`，**不**新增 `RuleCategory`

這兩件事在程式裡是分開的，設計時很容易混掉：

- `RuleCategory`（`Domain/Rules/Rule.cs`）是**引擎**用的：`engine.Evaluate(RuleCategory.X, …)`
  會去規則集裡找那個類別的規則，找不到就回 `ManualReview` ＋ `RuleOutcomeReason.NoRule`，訊息是
  「規則集…沒有…規則，需人工覆核」。
- `ReviewCheckTypes`（`Application/Checks/CompartmentAreaCheck.cs`）是**檢討表**用的字串常數，
  決定結果落在哪一列；`FireReviewWindow.GroupingOf` 再把它對映到一個 `ReviewTableGrouping`。

第 79 條之 1 不走引擎（§3.1），所以**不得**新增 `RuleCategory`：那會是一個永遠沒有規則的類別，
只要有人呼叫 `Evaluate` 就得到一句誤導的「規則集沒有這個類別的規則」。需要的只有：

| 新增 | 值 |
| --- | --- |
| `ReviewCheckTypes.AreaExemption` | `"AreaExemption"` |
| `GroupingOf` 對映 | `ReviewCheckTypes.AreaExemption => ReviewTableGrouping.Zone`——一個區劃一筆結果，與 `CompartmentArea` 同一種分組，不需要新的 `ReviewTableGrouping` 成員 |

常數名稱用 `AreaExemption` 而不是 `Article79_1`：後續第 79 條之 2 第 3 項的挑空免除若要從
`VerticalCompartment` 搬出來，會落在同一個檢討類型下。**本輪不搬**——挑空免除已經在
`VerticalCompartment` 下運作、有守門測試，搬家是獨立的一次變更。

**還有兩處要一起改，否則檢討表那一列會顯示成空的類別名稱。** 都是「主體是區劃還是元素」的分支，
本功能的主體是區劃、沒有 `CategoryLabel` 也沒有 `TypeName`，兩處都要同時認 `AreaExemption`：

- `ReviewTable.ToEntry` 的 `isArea`（`ReviewTable.cs`）：目前是「`CheckType == CompartmentArea`
  **或** `shaftRequirement == AtriumExemption`」。這一處決定 `CategoryLabel` 是不是「區劃」、
  `TypeKey`／`TypeName` 是不是 `null`。（步驟 3 實作時才發現，原本本節只寫了下面那一處。）
- `FireReviewWindow.EntryText`：主體文字只有 `ReviewCheckTypes.CompartmentArea` 走
  「顯示 `ZoneName ?? ZoneId`」那一支，其餘全部走
  「`{CategoryLabel}「{TypeName}」 {UniqueId 前幾碼}＠{ZoneName}`」。

### 5.2 新增白名單欄位

| 欄位 | 型別 | 說明 | 開放類別 |
| --- | --- | --- | --- |
| `zone.cannotBeSubdivided` | Boolean | 第 79 條之 1 之無法區劃分隔 | 全部（與 `zone.linksRefugeFloor` 同理由） |

沒有任何規則讀它，理由與必要性見 §3.3。

## 6. 參數需求

| 參數 | 型別 | 綁定 | 類別 | GUID | 必要 |
| --- | --- | --- | --- | --- | --- |
| `防火檢討_無法區劃分隔` | YESNO | Instance | Areas | `bcf10001-0000-4a00-9b00-000000000012` | **否** |

寫入 `assets/SharedParameters/fire-review-shared-params.txt`（Big5／cp950、CRLF，不得改變編碼與
換行）。描述文字：
`第79條之1之無法區劃分隔部分（只有觀眾席、生產線、教室、體育館、零售市場、停車空間需要填，非必要參數）`。

**非必要參數**：`ReviewInputSources.NeededBy` 不列它，前置檢查不因它未綁定而阻擋。沒有這六種
用途的專案不必綁。

未綁定時 `zone.cannotBeSubdivided` 讀不到值 → §3.5 的「無法區劃分隔未填」→ 資料不足。**這是
正確的**：工具不能替設計者認定一個觀眾席無法區劃分隔。

## 7. Revit 產出

### 7.1 檢討表

第 79 條之 1 是新的一列（`ReviewCheckTypes.AreaExemption`，分組 `ReviewTableGrouping.Zone`），
列在「防火區劃面積」之後——它講的是那一列的例外。每個受檢主體一筆結果，描述由
`Article79_1Exemption.Description` 提供：

| 判定 | 描述 |
| --- | --- |
| 成立（第一款） | `符合第一款（觀眾席、用途類組 A-1、設計者宣告無法區劃分隔）。得不受第79條第1項面積限制，惟須人工確認：(1) 本區劃是否以一小時以上防火時效之牆壁、防火門窗等防火設備與防火構造樓地板自成一個區劃；(2) 該防火設備是否具有一小時以上之阻熱性（第2項）。確認後以人工覆寫放行面積結果。` |
| 不成立 | `不符合第79條之1（無法區劃分隔＝否），第79條第1項照常適用。` |
| 資料不足 | `缺建築物用途類組，無法判定是否符合第79條之1。` |

「須人工確認」那兩句是 §3.7 的落地：覆寫的人看得到要確認什麼、該引哪一條。

面積結果那一列的訊息不改。**不要**在面積的未符合訊息裡接上「但可能符合第 79 條之 1」——面積
規則的訊息由引擎產生，接字會讓 `tw-bcr-79-area` 的既有斷言全部漂移，而且同一件事在兩列各講
一次。兩列在檢討表上相鄰，`SubjectUniqueIds` 也相同，展開時指到同一批 Area。

### 7.2 檢討視圖標示

**不產生任何標示、不發圖號。** 本功能沒有 `Fail`（§3.5），而 `ReviewMarkupPlan` 只處理 `Fail`；
超過面積的區劃本來就會被面積規則塗紅，再塗一次沒有意義。與挑空免除（[垂直區劃](vertical-compartment.md)
§7.3）相同。

### 7.3 參數面板

「區劃用途」下拉清單加入六個新用字。清單因此分成兩段，需要視覺上分開——垂直區劃的五個字免的是
**面積**，第 79 條之 1 的六個字免的是**同一個上限但要人工確認**，兩者的後果不同，混在一張平坦的
清單裡會讓人以為填了就免了。同一列的「適用上限」欄在選到六個新用字之一時顯示
`第79條 1500 ㎡（第79條之1 待人工確認）`，**仍然顯示數字**——因為上限真的還在適用。

新增一欄「無法區劃分隔」，只在「區劃用途」是六個新用字之一時可編輯，其餘情況停用並留空：這個
參數對其他用途沒有意義，能填會讓人以為它有作用。（實作時改成三選一下拉而不是 `CheckBox`，
見決議 15；已有值的列不停用，見決議 17。）

## 8. 程式組成

| 類別 | 責任 | 檔案 | 步驟 |
| --- | --- | --- | --- |
| `ZoneUses` | 六個新用字常數、`Article79_1Uses` 清單、`GroupsFor(use)`、`IsArticle79_1Use(text)` | `Application/Parameters/ZoneUses.cs`（擴充） | 1 |
| `Article79_1Exemption` | 純計算：成立的款次、缺口旗標、給人看的一句話 | `Application/Checks/Article79_1Exemption.cs`（新） | 1 |
| `RuleFieldCatalog` | `zone.cannotBeSubdivided` 白名單 | `Domain/Rules/RuleFieldCatalog.cs`（擴充） | 2 |
| `ReviewCheckTypes` | `AreaExemption` 常數（**不**動 `RuleCategory`，§5.1） | `Application/Checks/CompartmentAreaCheck.cs`（擴充） | 2 |
| `ReviewParameterSnapshot` | 讀 `防火檢討_無法區劃分隔`，一區劃多 Area 不一致時 `Unreadable` | `Application/Reviews/ReviewParameterSnapshot.cs`（擴充） | 2 |
| `Article79_1ExemptionCheck` | 產生受檢主體與結果，狀態由 `Article79_1Exemption` 三態對映 | `Application/Checks/Article79_1ExemptionCheck.cs`（新） | 3 |
| `FireReviewRunner` | 接上新 Check、法源條文 | `Application/Reviews/FireReviewRunner.cs`（擴充） | 3 |
| `ReviewTable` | 新檢討類型的列 | `Application/Reviews/ReviewTable.cs`（擴充） | 3 |
| `FireReviewWindow` | `GroupingOf` 對映、`EntryText` 的主體文字（§5.1 最後一段） | `FireReview/FireReviewWindow.cs`（擴充） | 3 |
| 面板 | 下拉分兩段（`ZoneUseChoice`）、新「無法區劃分隔」欄、「適用上限」文字 | `FireReview/FireReviewInputViewModels.cs`、`FireReviewParameterPanelWindow.xaml`(`.cs`) | 4 |
| `FireReviewZoneRow` | 面板這一側讀寫 `防火檢討_無法區劃分隔`、`IsArticle79_1Use`、`MissingParameters` | `Application/Parameters/FireReviewInputRows.cs`（擴充） | 4 |
| `ZoneAreaLimit` | `NeedsArticle79_1Confirmation` 與「待人工確認」字樣（**不是** `IsExempt`，決議 11） | `Application/Parameters/ZoneAreaLimits.cs`（擴充） | 4 |
| `RevitFireReviewTypeScanner` | 從 Area 讀 `防火檢討_無法區劃分隔` 給面板 | `Revit/Parameters/RevitFireReviewTypeScanner.cs`（擴充） | 4 |

判定（`Article79_1Exemption`）與產生結果（`Article79_1ExemptionCheck`）分開，與
`AtriumExemption`／`VerticalCompartmentCheck` 同一個切法：判定可以單獨測、也可以在面板上顯示
而不必跑一次檢討。

## 9. 已知限制

1. **（丁）不檢討。** 「自成一個區劃」與防火設備的一小時阻熱性沒有欄位，永遠是人工確認。阻熱性
   與第 83 條第 5 款是同一個缺口，補上要新的類型參數（例如 `防火檢討_阻熱性`），本輪不做。
2. **面積結果仍是未符合。** 合法的免除要靠人工覆寫放行，不是自動的（§3.7）。
3. **「其他類似用途建築物」沒有表達方式。** 要支援，合理的作法是另加一個是非參數
   （例如 `防火檢討_第79條之1用途`）而不是繼續擴充用字——與
   [`zone.use` 用字表](zone-use-vocabulary.md) §7 對「其他類似部分」的結論相同。
4. **`無法區劃分隔` 的「未勾選」與「從未設定」分不開。** Revit 的 YESNO 在介面上沒有空白狀態，
   已綁定而未勾選讀成 `否` → `不適用`。這與 `防火檢討_避難層通達` 是同一個限制
   （[垂直區劃](vertical-compartment.md) §9）。方向是嚴的：預設不免除。
5. **用途類組是整棟一個值。** 一棟 B-2 的建築物裡真的有 A-1 觀眾席時，`building.use` 只有一個
   值，第一款比不中。這是 [`building.use` 寫法正規化](building-use-groups.md) 已記錄的限制；
   要解只能在 Area 上另加區劃層級的用途類組參數。本功能的六個用字裡有三個因此受影響
   （`觀眾席`、`生產線`、`教室`），另三個不比對類組所以不受影響。
6. **十一層以上不介入。** 第 12 層的觀眾席受第 83 條拘束，本功能不產生主體（§2.3）。條文是這樣
   寫的，但審查實務上這一點常被誤解，所以檢討表上**沒有**任何說明——這是刻意的，不產生主體就
   不會有一列講半件事的結果。若使用者反映困惑，再考慮在面積結果的描述裡提一句。

## 10. 測試案例

`tests/BuildingRegulationReview.Core.Tests/Checks/Article79_1ExemptionTests.cs`（判定）與
`Rules/Article79_1AreaExemptionTests.cs`（規則集與接線）。

| 測試 | 守住的事 |
| --- | --- |
| `The_area_rules_carry_no_article_79_1_exemption` | 兩條面積規則的豁免清單完全不含六個新用字——§3.6 的決定本身 |
| `Article_79_1_does_not_reach_the_eleventh_storey` | 第 12 層的 `觀眾席` 由 `tw-bcr-83-area` 作答，且不產生第 79 條之 1 主體 |
| `An_auditorium_of_group_a1_with_the_declaration_holds` | 第一款成立、`人工覆核` |
| `An_auditorium_of_group_d2_holds_too` | 第一款的兩個類組都認 |
| `A_production_line_holds_for_both_c_groups` | `C-1`、`C-2` 都認 |
| `A_classroom_holds_only_for_groups_d3_and_d4` | `D-1`、`D-5` 不認 |
| `A_gymnasium_holds_whatever_the_group_is` | §3.4 的寬讀決定——`B-2`、未填都成立 |
| `A_retail_market_and_a_car_park_ignore_the_group` | 同上，兩個用字各一次 |
| `An_auditorium_of_another_group_is_inapplicable` | `B-2` ＋ `觀眾席` → `不適用`，不是資料不足 |
| `A_use_outside_the_list_gets_no_subject` | `辦公`、`觀眾廳`、`看台`、空字串都不產生主體 |
| `Without_the_declaration_nothing_holds` | 無法區劃分隔＝否 → `不適用` |
| `A_missing_declaration_is_insufficient_data` | 未填 → `資料不足`，訊息點名缺哪個參數 |
| `A_declared_no_needs_no_other_fact` | 無法區劃分隔＝否、類組未填 → `不適用`（不必問類組） |
| `A_non_fire_resistive_building_is_inapplicable` | 第 79 條第 1 項本不適用 |
| `A_missing_construction_is_insufficient_data` | 防火構造未填 → `資料不足` |
| `The_exemption_never_passes_and_never_fails` | 所有案例的狀態都不是 `Pass` 也不是 `Fail`——§3.5 |
| `The_holding_result_names_what_a_person_must_confirm` | 描述含（丁）兩件事與「人工覆寫」 |
| `The_area_result_is_untouched_by_the_exemption` | 同一個 run 裡 `tw-bcr-79-area` 的狀態與訊息與沒有本功能時逐字相同 |
| `No_marking_is_planned_for_an_exemption` | `ReviewMarkupPlan` 不為本檢討類型產生任何標示或圖號 |
| `The_rule_set_gains_no_category_for_the_exemption` | `RuleCategory` 的成員數不變、規則檔的 `rules` 與 `version` 不變——決議 8 |
| `The_table_names_the_zone_not_a_category` | 新列的主體文字是區劃名稱，不是空的 `CategoryLabel`（§5.1 最後一段） |
| `The_vocabulary_matches_only_the_exact_wording` | 六個用字逐字比對，`ZoneUses.Article79_1Uses` 是唯一定義處 |

`The_area_result_is_untouched_by_the_exemption` 與 `The_area_rules_carry_no_article_79_1_exemption`
是這個功能最重要的兩條守門測試：它們守的是「本功能不改變任何既有判定」。

### 步驟 1 寫完後的對照

已寫（51 條，兩個檔）：上表除了 `A_use_outside_the_list_gets_no_subject`、
`The_area_result_is_untouched_by_the_exemption`、`No_marking_is_planned_for_an_exemption`、
`The_table_names_the_zone_not_a_category` 以外全部；其中
`A_use_outside_the_list_gets_no_subject` 的判定那一半寫成 `A_use_outside_the_list_claims_nothing`
（清單外的用字回 `不適用`），「不產生主體」那一半與其餘三條都要等步驟 3 的 Check 才有東西可驗。
`Article_79_1_does_not_reach_the_eleventh_storey` 目前只驗規則引擎那一半。

步驟 1 另外加的（上表沒有、但守著實作時才浮現的決定）：`An_unrecognised_group_is_not_a_gap`
（決議 13）、`A_missing_group_is_insufficient_data`、`Every_missing_fact_is_named_in_reading_order`
（（丙）→（甲）→（乙）的訊息順序）、`The_two_vocabularies_share_no_word`（六個用字與五個垂直
區劃用字不重疊）、`Only_three_words_are_read_together_with_a_group`、`Nothing_here_speaks_of_article_83`、
`Both_lists_are_still_only_the_vertical_compartments`、
`An_article_79_1_use_is_still_reviewed_against_the_limit`（六個用字各一次，3000 ㎡ 照樣未符合）、
`The_area_message_says_nothing_of_the_exemption`（決議 12 的基線：`觀眾席` 與 `辦公` 的面積訊息
逐字相同）。

## 11. 決議紀錄

| # | 決議 | 理由 |
| --- | --- | --- |
| 1 | 第 79 條之 1 不進規則引擎，寫成純計算 | 沒有可比較的 `requiredValue`（§3.1，同挑空免除決議 24） |
| 2 | 兩條面積規則的豁免清單一字不動 | （丁）驗不到，`NotApplicable` 等於工具替設計者宣告事實（§3.6） |
| 3 | 面積結果仍是未符合，放行走人工覆寫 | 嚴的方向是安全的方向；覆寫機制已存在（§3.7） |
| 4 | 十一層以上不介入，靠引擎優先序自然達成 | 第 79 條之 1 只免「前條第一項」（§2.3） |
| 5 | `體育館`、`零售市場`、`停車空間` 不比對類組 | 文義加上「寬讀只多一筆人工覆核、嚴讀會漏掉合法免除」的不對稱（§3.4） |
| 6 | 「其他類似用途建築物」不列入用字 | 列不完，且是裁量字眼；審查者走人工覆寫（§3.4） |
| 7 | `zone.use` 不做同義詞折疊 | 沿用既有立場，`觀眾廳`／`看台` 不算（§3.4） |
| 8 | 只新增 `ReviewCheckTypes.AreaExemption`，**不**新增 `RuleCategory` | 本功能不走引擎，零規則的 `RuleCategory` 會讓 `Evaluate` 回一句誤導的「規則集沒有這個類別的規則」（§5.1） |
| 8a | 常數叫 `AreaExemption` 不叫 `Article79_1` | 挑空免除將來可能搬進同一個檢討類型；但本輪不搬（§5.1） |
| 9 | `防火檢討_無法區劃分隔` 是非必要參數 | 沒有這六種用途的專案不必綁（§6） |
| 10 | `zone.use` 不在六個用字之列時不產生主體 | 絕大多數區劃都不在，每個掛一筆空結果會淹掉檢討表（§3.2） |
| 11 | 面板的「適用上限」仍顯示數字 | 上限真的還在適用，顯示「免適用」會誤導（§7.3） |
| 12 | 不改面積結果的訊息 | 既有斷言會全部漂移，且同一件事兩列各講一次（§7.1） |
| 13 | `building.use` 填了但**不是**第 3-3 條的類組（例如 `住宿類`）時，讀成「不是這幾組」→`不適用`，不是資料不足 | 步驟 1 實作時才出現的問題。`ZoneAreaLimit` 對第 83 條Ｈ－２組但書就是這樣讀的（`ZoneAreaLimits.cs`：只有 `IsNullOrWhiteSpace` 才算缺口，無法辨識的文字讓 `IsH2` 回 false），同一個字在兩處判法不同會更難解釋。訊息把讀到的原文引回來（`用途類組 住宿類 非 A-1、D-2`），所以不會是無聲的誤判 |
| 14 | 樓層序**未填**時，先算免除：免除可能成立就回`資料不足`（訊息點名缺樓層序），免除已確定不成立就回`不適用`、不問樓層序 | 步驟 3 實作時才出現的問題（§12「步驟 3 的判定補充」）。§3.5 只寫了第 12 層不產生主體，沒寫未填。不產生主體會把專案裡最大的區劃藏起來，正是本功能要讓人看見的那一個；而免除不成立時，不論第 79 條第 1 項或第 83 條適用，答案都是`不適用`——這就是 §3.5「已確定不成立的要素讓其他缺口不必再問」用在高一層 |
| 15 | 面板的「無法區劃分隔」用**空白／是／否 三選一下拉**，不是 `CheckBox` | 步驟 4 實作時才出現的問題。§7.3 寫的是 `CheckBox`，但打勾框只有兩態，而檢討分得出三態：未填是`資料不足`、否是`不適用`（§3.5）。用打勾框的話，誤填的「是」在面板上退不回「未填」——取消勾選寫的是 0，那是另一個答案。同一張表的「自動滅火設備」「避難層通達」也都是這個三選一，形狀一致 |
| 16 | 「適用上限」的字串沿用既有格式，寫成 `第79條 上限 1500 m²（第79條之1 待人工確認）` | §7.3 舉的例子是 `第79條 1500 ㎡（…）`，但 `ZoneAreaLimit.Description` 既有的格式是「上限 N m²」，既有測試也照這個字串斷言。要點是決議 11 的「仍顯示數字、不寫免適用」，那一點完全照辦 |
| 17 | 用途已不在六個字之列、但格子裡還有值的那一列，「無法區劃分隔」欄**不停用** | §7.3 只說「其餘情況停用」，沒說已有值的怎麼辦。全面停用會讓一個因為用途改掉而留下的舊值只能回 Revit 清。條件因此是「是六個用字**或**格子非空」 |

## 12. 實作進度

| 步驟 | 內容 | 狀態 |
| --- | --- | --- |
| 0 | 設計（本文件） | **已完成** |
| 1 | `ZoneUses` 六個用字與類組對照、`Article79_1Exemption` 純計算與其測試 | **已完成** |
| 2 | 參數層：`防火檢討_無法區劃分隔`、`zone.cannotBeSubdivided` 白名單、`ReviewCheckTypes.AreaExemption`、`ReviewParameterSnapshot` 讀取 | **已完成** |
| 3 | 檢查層與接線：`Article79_1ExemptionCheck`、`FireReviewRunner`、`ReviewTable` 新列 | **已完成** |
| 4 | 參數面板：下拉六個新用字、「無法區劃分隔」欄、「適用上限」文字 | **已完成** |

### 步驟 1 的實際產出

- `ZoneUses`：六個常數（`Auditorium`、`ProductionLine`、`Classroom`、`Gymnasium`、`RetailMarket`、
  `CarPark`）、`Article79_1Uses`、`IsArticle79_1Use(text)`、`GroupsFor(use)`（三個不比對類組的用字
  回空清單，清單外的文字也回空——所以呼叫端要先過 `IsArticle79_1Use`）。
- `Article79_1Exemption.For(fireResistive, buildingUse, zoneUse, cannotBeSubdivided)`：
  `Article79_1Clause`／`Article79_1Gap`／`Holds`／`IsUndecided`／`IsInapplicable`／`Description`，
  以及 `PersonMustConfirm`（（丁）那兩句，公開常數，步驟 3、4 直接引用）。
- 測試 51 條（`Checks/Article79_1ExemptionTests.cs`、`Rules/Article79_1AreaExemptionTests.cs`），
  全套 1528 通過。`Article_79_1_does_not_reach_the_eleventh_storey` 目前只驗規則引擎那一半
  （第 12 層由 `tw-bcr-83-area` 作答）；「不產生主體」那一半要等步驟 3 的 Check。
- 尚未接線：`Article79_1Exemption` 目前沒有任何呼叫端，判定不會出現在檢討表上。

### 步驟 2 的實際產出

四件事全部落地，**仍然沒有任何呼叫端**——檢討表上還是看不到第 79 條之 1，這一輪只是讓那個事實
填得進去、讀得出來。

- `assets/SharedParameters/fire-review-shared-params.txt`：新增
  `防火檢討_無法區劃分隔`（YESNO、GROUP 1、GUID `…0012`），描述文字如 §6。檔案維持
  Big5／cp950 ＋ CRLF、無 BOM。
- `RuleFieldCatalog`：`zone.cannotBeSubdivided`（Boolean、全類別開放）。
- `ReviewCheckTypes.AreaExemption = "AreaExemption"`；`RuleCategory` 一個成員都沒加。
- `ReviewInputSources.CannotBeSubdivided` 與 `All` 的一列（Instance、Areas）。

**`ReviewParameterSnapshot` 一行都不必改。** `ReviewInputAssembler.Assemble` 是走
`ReviewInputSources.All` 的泛型迴圈，只要欄位在白名單、來源在 `All`，區劃輸入就自動帶進來，
一區劃多 Area 填不一致時也自動走 `ReviewInput.Unreadable` 那條既有路徑（與
`防火檢討_避難層通達` 同一條）。Revit 端的 `RevitReviewParameterReader` 同理，它讀的是
`ReviewInputSources.ParameterNames`。§8 的表把這一列寫成「擴充」是設計時的預估，實際上不需要。

測試 14 條，全套 **1542 通過**（基線 1528）：

- **新檔** `tests/BuildingRegulationReview.Core.Tests/Parameters/Article79_1ParameterTests.cs`（9 條）：
  白名單欄位是 Boolean 且全類別開放、`FieldsUsedBy`／`NeededBy` 都沒有它（決議 9 的守門）、
  參數來源是 Areas 的 Instance 參數、讀進區劃輸入、未勾選讀成 `否`、未綁定什麼都不供給、
  同一區劃多 Area 不一致 → `Unreadable`、一致 → 一個事實、
  `The_review_gains_a_check_type_but_no_rule_category`（決議 8）。
- **新檔** `tests/BuildingRegulationReview.Core.Tests/Parameters/SharedParameterFileTests.cs`（5 條）：
  共用參數檔逐位元組驗——沒有 BOM、不是合法 UTF-8（所以還是 ANSI）、全檔 CRLF、新參數的
  GUID／型別／群組／描述、16 條 PARAM 且 GUID 與名稱都不重複。測試專案的 `.csproj` 因此把
  這個檔複製到輸出的 `Assets/`。**這個檔沒有 cp950 解碼器可用**（net10.0 不內建、
  `System.Text.Encoding.CodePages` 不在快取裡），所以中文片語以 Big5 位元組的十六進位常數寫死，
  用 Latin-1 當位元組↔字元的恆等映射來比對。

- **新檔** `.gitattributes`：`assets/SharedParameters/*.txt -text`。寫這條的原因是驗到的：
  這個 repo 的 `core.autocrlf=true`，所以共用參數檔在 HEAD 裡其實一直是 LF，工作區的 CRLF 是
  簽出時還原出來的——換一台 `core.autocrlf=false` 的機器或在非 Windows 上簽出就會拿到 LF。
  `-text` 讓 git 逐位元組存放，§6 的「不得改變換行」從此由 repo 宣告，而不是靠設定值的巧合。
  這一輪因此把該檔重新正規化過一次（diff 上是 27 刪 28 增，內容只多了一行）。

### 步驟 3 的實際產出

**第 79 條之 1 從這一輪開始出現在檢討表上。** 檢討表多了一列「區劃面積免除（第79條之1）」，排在
「防火區劃面積」之後，分組 `ReviewTableGrouping.Zone`；檢討流程多了一段
`FireReviewStep.AreaExemption`（進度 2/7，緊接在區劃面積之後，因為它讀的是同一批區劃輸入）。

- **新檔** `src/BuildingRegulationReview.Application/Checks/Article79_1ExemptionCheck.cs`：
  `Article79_1ExemptionCheck.Review(set, inputs, ruleSet, runId, newResultId)`、
  `Article79_1ExemptionReview`、`Article79_1ExemptionFinding`（`Exemption` 在區劃範圍有問題時是
  `null`——那種區劃一個事實都沒讀）、公開常數 `LegalReference`。
  **取 `CompiledRuleSet` 而不是 `RuleEngine`**：本檢討不評估任何規則，只需要 `RuleSetId`／
  `Version` 填進結果（與挑空免除同形），拿引擎會讓簽章說謊。
  輸入型別直接用 `CompartmentAreaInputs`——四個事實全部在區劃面積那一批輸入裡，不必新型別。
- `FireReviewRunner`：`FireReviewStep.AreaExemption`（列在 `CompartmentArea` 之後）、`Label`、
  `total` 6→7、結果併入 `run.Results`、警告與 finding 進日誌。
  **`HostLegalReferences` 不必改**（§8 的表把它列成步驟 3 的工作是設計時的預估）：它是從
  `CompartmentAreaReview.Findings` 的法源條文挑出第 83 條的區劃，只看面積那一批結果，本功能的
  結果不在裡面。這也是為什麼 §3.2「法源條文不得出現第83條」不只是文件上的潔癖——那個字串真的
  被別的功能拿去判斷。
- `ReviewTable` 共三處：`CheckTypes`（六列）、`Title`、`GroupsFor`（與 `CompartmentArea` 共用
  同一個 `case`）。
- `ReviewTable.ToEntry` 的 `isArea`：加認 `AreaExemption`。**這一處 §5.1 原本沒寫**（只點名
  `FireReviewWindow.EntryText`），漏掉會讓那一列的主體被當成元素，去找不存在的
  `CategoryLabel`／`TypeName`。
- `FireReviewWindow`：`GroupingOf` 加 `AreaExemption => Zone`、`EntryText` 的主體文字加認
  `AreaExemption`。

### 步驟 3 的判定補充：樓層序未填（決議 14）

§3.5 的表只列了「第 12 層的觀眾席 → 不產生主體」，沒有說樓層序**未填**時怎麼辦。實作時必須決定，
決議見 §11 決議 14：先算免除，再看樓層序——

| 情形 | 結果 |
| --- | --- |
| 樓層序 ≥ 11 | **不產生主體**（§2.3，第 83 條作答） |
| 樓層序未填、且免除可能成立（`Holds` 或 `IsUndecided`） | `資料不足`，訊息點名缺樓層序並說明十一層以上由第 83 條作答 |
| 樓層序未填、但免除已確定不成立（`IsInapplicable`） | `不適用`，不問樓層序 |
| 樓層序 ≤ 10 | 照 §3.5 的三態 |

### 步驟 3 寫完後的測試對照

§10 表上原本缺的四條全部補上，`Article_79_1_does_not_reach_the_eleventh_storey` 的「不產生主體」
那一半也補上（同名測試現在有兩個：`Rules/Article79_1AreaExemptionTests` 驗規則引擎那一半，
`Checks/Article79_1ExemptionCheckTests` 驗檢查層那一半）。

- **新檔** `tests/.../Checks/Article79_1ExemptionCheckTests.cs`（12 個 `[Fact]`／`[Theory]`）：
  `A_use_outside_the_list_gets_no_subject`（`辦公`、`觀眾廳`、`看台`、`停車場`、空字串、未填）、
  `Article_79_1_does_not_reach_the_eleventh_storey`（11／12／30 層，三種宣告填法都不產生主體）、
  `A_missing_storey_leaves_the_exemption_undecided_unless_it_already_failed`（決議 14）、
  `A_holding_exemption_is_manual_review_and_names_what_a_person_must_confirm`、
  `A_declaration_of_no_is_inapplicable_and_a_missing_one_is_insufficient_data`、
  `The_exemption_never_passes_and_never_fails`（六用字×七類組×三宣告×三構造＝378 組）、
  `A_zone_whose_extent_is_in_doubt_is_manual_review_and_nothing_is_judged`、
  `The_result_keeps_every_fact_it_read_and_where_it_came_from`、
  `The_area_result_is_untouched_by_the_exemption`、`No_marking_is_planned_for_an_exemption`、
  `The_table_names_the_zone_not_a_category`。
- `tests/.../Reviews/FireReviewIntegrationTests.cs` 新增五條端到端（參數→輸入→判定→檢討表→標示）：
  `An_auditorium_that_cannot_be_subdivided_is_one_manual_review_beside_the_area_row`、
  `The_area_row_reads_the_same_whether_or_not_the_exemption_holds`、
  `The_declaration_decides_the_exemption_and_a_missing_one_is_insufficient_data`、
  `The_declaration_has_a_source_but_never_holds_a_review_up`（決議 9 的端到端守門）、
  `The_exemption_is_never_marked_in_the_review_view`。
  這五條是第一次真的讓 `防火檢討_無法區劃分隔` 從參數讀到結果。
- 既有測試改了四處斷言（都是列數／段數，不是判定）：`ReviewTableTests` 的列標題與空列清單、
  `FireReviewIntegrationTests` 的 `Stages.Count` 6→7、兩處進度序列。
- 全套 **1565 通過、0 失敗**（步驟 2 的基線 1542）。兩個專案 0 警告。
- ~~未實機驗證~~ **2026-10-05 實機驗證完成**（驗證清單 V-06）：檢討表新列出現、`防火檢討_無法區劃分隔` 確實被讀到、
  結果只有人工覆核或資料不足、面積列不受影響。整體確認，未附逐字原文。

### 步驟 4 的實際產出

面板三件事全部完成，並補上一條步驟 2 沒接的線：`防火檢討_無法區劃分隔` 原本只有**檢討**那一側
會讀（`ReviewParameterSnapshot`），面板這一側的 `FireReviewZoneRow` 根本沒有這個欄位，所以參數
面板既讀不到也寫不回。步驟 4 把它接起來。

**參數層補線**（`FireReviewZoneRow`、`FireReviewZoneParameters`、`RevitFireReviewTypeScanner`）：

- `FireReviewZoneRow` 新增 `cannotBeSubdivided` 建構參數與 `CannotBeSubdivided` 屬性（三態），
  以及 `IsArticle79_1Use`（照 `IsAtrium` 的形狀）。
- `FireReviewZoneParameters.CannotBeSubdivided = 32`；`MissingParameters` 只在六個用字時報它缺
  （與第 3 項那兩欄同樣的理由，決議 9）。原本的 `if (!IsAtrium) return missing;` 改成兩個獨立的
  區塊，既有行為不變（`IsAtrium` 與 `IsArticle79_1Use` 互斥）。
- `RevitFireReviewTypeScanner` 讀 present 旗標與值。

**（1）下拉分兩段**（`FireReviewInputViewModels.cs`、`FireReviewParameterPanelWindow.xaml`）：

- 新型別 `ZoneUseChoice`（`Text`／`Label`／`IsHeading`／`IsSelectable`），`UseChoices` 因此從
  `IReadOnlyList<string>` 變成 `IReadOnlyList<ZoneUseChoice>`，內容是
  「（未填）／標題／五個垂直區劃／標題／六個第79條之1用字」，靜態共用一份。
- 標題是清單裡的一項，靠 `ItemContainerStyle` 的 `IsEnabled="{Binding IsSelectable}"` 停用，
  所以點不到、鍵盤也跳不到；`ToString()` 回 `Text`（標題是空字串），萬一真被選到也只是清空格子，
  不會把標題文字當成用途填進去。
- 兩段標題：`── 第79條之2 垂直區劃（免面積上限） ──`、
  `── 第79條之1（上限仍適用，須人工確認） ──`。措辭刻意把差別講在標題上。
- `ZoneFillWindow`（批次填入）同步加六個用字與同樣兩段標題，標題用 `IsEnabled = false` 的
  `ComboBoxItem`（`ComboBoxItem` 本身就是容器，所以停用有效）。

**（2）「無法區劃分隔」欄**：

- `FireReviewZoneRowViewModel` 新增 `CannotBeSubdivided`（字串三態，沿用 `YesNoChoices`）、
  `IsArticle79_1Use`、`CanEditCannotBeSubdivided`；`Use` 的 setter 多 `Raise` 後兩者。
- `Edits()` 新增一段，走 `ReviewInputSources.CannotBeSubdivided` ＋ `YesNoParameterText`
  （寫 1／0／空字串）。寫回路徑本來就通——面板的寫入是逐欄列舉，不是泛型迴圈，所以這一段必須自己加。
- XAML 是 `DataGridComboBoxColumn`，`CellStyle` 的 `DataTrigger` 在
  `CanEditCannotBeSubdivided == False` 時 `IsEnabled="False"`。ToolTip 寫上條文重點與「填是也只是
  待人工確認、上限照樣適用」。
- `ZoneFillWindow` 也加了這一欄，並在狀態列加上「待填無法區劃分隔 N 個區劃」（只數六個用字的列，
  與挑空那一條同樣的理由）。

**（3）「適用上限」文字**（`ZoneAreaLimits.cs`）：

- 新增 `NeedsArticle79_1Confirmation` 旗標與公開常數 `Article79_1Pending`
  （`（第79條之1 待人工確認）`）。**不是** `IsExempt`——那會顯示「免適用」，正是決議 11 要避免的。
- 只在 `floorNumber < 11` 時掛上（§2.3：第 83 條作答時第 79 條之 1 碰不到），上限數字、`Gaps`、
  `Clause` 一律不動。缺格子時字串是 `未填滅火設備，無法判定上限（第79條之1 待人工確認）`——
  註記照掛，因為那一列的第 79 條之 1 問題不會因為別的格子沒填就消失。
- `Equals`／`GetHashCode` 一併加上新欄位。

### 步驟 4 寫完後的測試對照

- `tests/.../Parameters/ZoneAreaLimitTests.cs` 新增 8 條（六用字×上限、
  `The_limit_cell_shows_the_number_and_says_the_confirmation_is_pending`、
  `A_vertical_compartment_still_reads_as_exempt_and_asks_for_no_confirmation`、
  `From_the_eleventh_storey_up_no_article_79_1_confirmation_is_pending`、
  `A_use_outside_the_article_asks_for_no_confirmation`、
  `An_article_79_1_zone_still_names_the_box_it_waits_on`、
  `The_panel_path_carries_the_note_below_the_eleventh_storey_only`、
  `The_note_does_not_move_the_number_the_rules_require`）。最後一條是拿規則引擎的
  `RequiredValue` 對面板顯示的數字，與這個檔案既有的交叉比對同一個做法——守著「第 79 條之 1
  不得移動面積上限」。`Facts()` 多了一個 `zoneUse` 選用參數，預設仍是 `辦公`，既有斷言不受影響。
- `tests/.../Parameters/FireReviewInputRowTests.cs` 新增 3 條（三態、六個用字才報缺、
  其餘用字不報缺）。
- 全套 **1594 通過、0 失敗**（步驟 3 的基線 1565，新增 29）。既有斷言一條都沒改。
- 兩個專案 0 警告：`BuildingRegulationReview.Application`、`BuildingRegulationReview`（WPF）。
- 兩段清單的守門在 `Article79_1ExemptionTests`（六個用字與五個垂直區劃互不相屬），已存在。
  面板本身是 WPF／net48，不在測試專案的參照範圍內，所以 `ZoneUseChoice` 與新欄位的行為沒有
  單元測試，只有編譯保證。
- ~~未實機驗證~~ **2026-10-05 實機驗證完成**（驗證清單 V-07）：下拉的兩段標題、新欄位的停用與寫回、「適用上限」的新字串
  皆如預期。整體確認，未附標題與字串原文。
