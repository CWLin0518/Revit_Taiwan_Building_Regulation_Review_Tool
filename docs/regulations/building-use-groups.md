# `building.use`（建築物用途類組）的寫法正規化

約定 ID：`building-use-groups`
法源：建築技術規則建築設計施工編第 3 條之 3（使用類組）、第 83 條第一款、第二款（Ｈ－２組但書）

## 1. 這份約定要解決什麼

第 83 條第一、二款寫「但建築物使用類組Ｈ–２組使用者，區劃面積得增為二○○平方公尺」，
規則 `tw-bcr-83-area` 因此有兩處 `building.use == "H-2"`。規則 DSL 比的是**逐字相等**。

問題在於「Ｈ－２組」這個組別，法規本身至少有四種寫法：

| 條文 | 印出來的樣子 | 差異 |
| --- | --- | --- |
| 第 83 條 | `Ｈ–２組` | 全形字母與數字，中間是 EN DASH（U+2013） |
| 第 86 條 | `H-2 組` | 半形，中間有空格，帶「組」 |
| 第 88 條附表 | `H-2` | 半形，無綴字 |
| 第 310 條附表 | `Ｈ類第二組` | 全形，國字數字，「類第…組」句式 |

設計者從哪一條抄過來，`建築物用途類組` 這個專案參數就會填成哪一種。原本的行為是：只有
`H-2` 這一種比得中，其餘全部退回非Ｈ－２組的一○○／二○○平方公尺。方向是安全的（只會判得更嚴），
但使用者填了正確的組別卻拿不到條文明文給的放寬，而且沒有任何地方告訴他為什麼——這不是嚴謹，
是難用。

所以這一層把**寫法**收斂掉，判定本身一個字都不動。

## 2. 正規式與認得的寫法

程式端唯一的定義處是
`src/BuildingRegulationReview.Application/Parameters/BuildingUseGroups.cs`。

**正規形**就是第 88 條附表印的半形代碼：`A-1`、`B-2`、`E`、`H-2`、`I`……
第 3 條之 3 的二十四個組別依條文順序列在 `BuildingUseGroups.All`：

```
A-1 A-2 | B-1 B-2 B-3 B-4 | C-1 C-2 | D-1 D-2 D-3 D-4 D-5 | E
F-1 F-2 F-3 F-4 | G-1 G-2 G-3 | H-1 H-2 | I
```

Ｅ類與Ｉ類沒有組別，正規形就是單一個字母。

`BuildingUseGroups.Canonical(text)` 的折疊步驟（`Fold`）：

1. 全形 ASCII（`Ｈ`、`２`、`－`）整段位移回半形。
2. 空白、以及「類」「第」「組」三個字直接丟掉——它們不帶資訊。
3. 國字數字 `一`～`五` 換成 `1`～`5`。
4. 各種破折號（`-` `‐` `‑` `‒` `–` `—` `―` `−` `ー` `﹘` `﹣`）一律換成 `-`。
5. 字母轉大寫。

折完之後只接受三種形狀：`X`、`XN`、`X-N`（`X` 為 `A`～`I`，`N` 為 `1`～`5`），
而且折出來的代碼必須在 `All` 裡。**其他一律回 null。**

因此：

| 填的字 | 判讀 |
| --- | --- |
| `Ｈ–２組`、`H-2 組`、`Ｈ類第二組`、`Ｈ－２`、`H2`、`h-2`、`H-2` | `H-2` |
| `Ｄ－３`、`Ｆ–１組`、`Ｂ－２組`、`g-2`、`A1` | `D-3`、`F-1`、`B-2`、`G-2`、`A-1` |
| `辦公`、`住宿類`、`H`（類不是組）、`H-9`、`J-1`、`H-2、G-2`、`H-2組集合住宅` | **不變**，原字照舊逐字比對 |
| 空白、未填 | 未提供（規則判資料不足） |

幾個決定：

- **認不得就原封不動。** 折不出組別的文字維持原樣交給規則，也就是這件事做之前的行為。
  放寬只發生在「確定是同一個組別、只是寫法不同」的情況。
- **`H` 不等於 `H-2`。** 類不是組。`Ｈ類` 折出 `H`，不在 `All` 裡，回 null。
- **一段話裡提到組別不算。** `H-2組集合住宅`、`H-2、G-2` 都折不成單一形狀，回 null。
  混合用途要由審查者自行判斷或分包檢討（見 §6）。
- **清單錯了也不會改變判定。** 今天只有 `H-2` 會被任何規則讀到
  （`Only_the_h2_group_satisfies_the_proviso` 守著），其餘二十三個組別只決定面板下拉提供什麼、
  哪些寫法會被收斂，不會左右任何檢討結果。

## 3. 為什麼放在 `ReviewInputAssembler`

三個可能的位置，選了第二個：

1. **規則裡堆 `||`**——`building.use == "H-2" || building.use == "Ｈ－２組" || …`。
   每一條將來會讀用途類組的規則都要抄一次，而且抄漏不會有人發現。否決。
2. **`ReviewInputAssembler.Convert`**——參數變成規則輸入的那**唯一**一個地方。
   `UseGroup(...)` 只在 `field == "building.use"` 且讀到的是文字時作用，其餘欄位一律不碰
   （`No_other_text_field_is_folded` 守著）。採用。
3. **擴充 DSL，給規則一個「使用類組」型別**——最乾淨，但要動運算式編譯器、欄位型別與
   規則檔格式，為了一個組別不值得。留著，等到有第二條規則要讀用途類組再說。

這與 `zone.use` 的決定不同，而且是刻意的：`zone.use` 明文**不做**同義詞正規化
（見 [`zone.use` 用字表](zone-use-vocabulary.md) §2 最後一點），因為 `電梯井` 與 `昇降機道`
是兩個**不同的詞**，判它們等價是工具在替設計者認定事實。這裡處理的是同一個**專有代碼**的
排版差異，不是語意判斷。

## 4. 面板怎麼呈現

`FireReviewParameterPanelWindow` 專案資訊分頁的「建築物用途類組」由文字框改為**可編輯下拉**
（`IsEditable="True"`，綁 `Text` 不綁 `SelectedItem`，與「區劃用途」同一個理由：綁
`SelectedItem` 會把使用者自行輸入的值清成空白）：

- 下拉提供 `BuildingUseGroups.All` 的二十四個代碼加一個空白項。
- 底下的提示改為即時反映判讀結果（`FireReviewProjectViewModel.BuildingUseNote`）：
  - 未填：`第83條第一款、第二款的Ｈ－２組但書會讀它；未填時十一層以上的區劃面積判「資料不足」。`
  - 填 `H-2`：`讀作 H-2。`
  - 填 `Ｈ–２組`：`讀作 H-2（全形、各種破折號與「類」「第」「組」字樣都認得，檢討的證據欄會同時記下你填的字）。`
  - 填 `辦公`：`「辦公」不是第3-3條的使用類組，檢討時會當成非Ｈ－２組，十一層以上的區劃面積依較嚴的上限判定。`

**順帶修掉一個錯誤的提示。** 原本寫「目前的規則沒有引用它，先填著以備規則擴充」——自從
`tw-bcr-83-area` 加進來（帷幕牆步驟 7），這句話就是反的：沒填會讓十一層以上的區劃面積判資料不足。

「區劃」分頁的「適用上限」欄同步：`ZoneAreaLimit.Article83` 改用 `BuildingUseGroups.IsH2`，
所以面板與規則對同一個寫法算出同一個數字，`A_project_that_spells_the_group_any_way_gets_the_wider_limit`
兩邊各算一次來守這件事。

## 5. 證據與既有檢討結果

改寫**不是默默改寫**。被折過的值，`ReviewInput.Source` 會同時寫下原字與判讀結果：

```
專案資訊：建築物用途類組（填「Ｈ–２組」，判讀為 H-2）
```

沒被折過的值（本來就填 `H-2`，或填的是 `辦公` 這種不是組別的字）**來源字串一個字都不變**，
值也不變——這是刻意的：證據基線（規格 13.1）會把輸入的值與來源都寫進去，
所以這些專案的既有 stored run 不會因為這一步變成「需更新」
（`A_text_that_needed_no_respelling_reads_exactly_as_it_did_before` 守著）。

**會變成「需更新」的是**：`建築物用途類組` 目前填著 `Ｈ－２`、`H-2組`、`Ｈ類第二組` 這類寫法的專案。
它們的證據基線確實動了——而且判定也跟著動（上限從一○○變二○○），所以這個「需更新」是對的，
不是雜訊。

## 6. 未解決

- **用途類組是整棟一個值。** 混合用途的建築物只有一部分是Ｈ－２組時無法逐區劃區分，
  須由審查者自行判斷或分包檢討。要支援的話合理的作法是在 Area 上另加一個區劃層級的用途類組
  參數，而不是讓專案層級的欄位接受一串組別。
- **第 3 條之 3 的組別清單未經 MCP 條文查詢逐條核對**——`mcp__taiwan-building-code__search_building_code`
  的語料裡沒有第 3 條之 3。清單中的 `A-1 A-2 B-1 B-2 B-3 B-4 C-1 C-2 D-2 D-3 D-4 D-5 E F-1 F-2
  F-3 G-1 G-2 G-3 H-1 H-2 I` 均由第 38、69、79-1、86、88、92、95、96-1、17 條的引用交叉確認過；
  `D-1` 與 `F-4` 未在語料中出現。如 §2 最後一點所述，這兩筆即使有誤也不會改變任何檢討結果。
- **沒有任何規則讀Ｈ－２組以外的組別。** 第 79-1 條（Ａ－１、Ｄ－２觀眾席等免區劃）、
  第 86 條（分間牆不燃材料）、第 88 條（內部裝修）都會讀，都還沒實作。真的要做時，
  正規形已經就位，規則只要比對 `All` 裡的代碼即可。

## 7. 測試

`tests/BuildingRegulationReview.Core.Tests/Parameters/BuildingUseGroupsTests.cs`（40 個案例）：

| 測試 | 守住的事 |
| --- | --- |
| `Every_group_the_panel_offers_is_already_its_canonical_form` | 清單二十四筆，每一筆自己就是正規形 |
| `The_two_classes_with_no_group_are_the_bare_letter` | Ｅ類、Ｉ類沒有組別號 |
| `Every_spelling_the_code_itself_prints_names_the_same_group` | 九種寫法都判成 `H-2`（前四種取自條文） |
| `A_group_of_any_class_is_read_the_same_way` | 其他類別的全形寫法一樣折得動 |
| `Text_that_names_no_group_is_left_exactly_as_it_was` | 七種不是組別的文字原封不動 |
| `Nothing_supplied_stays_nothing` | null／空白不會憑空變成組別 |
| `Only_the_h2_group_satisfies_the_proviso` | 二十四個組別裡只有 `H-2` 會被但書認 |
| `The_canonical_form_is_the_literal_the_shipped_rule_compares` | 常數與出貨規則的字面值一致 |
| `A_project_that_spells_the_group_any_way_gets_the_wider_limit` | 九種寫法各自跑一次規則與面板，兩邊都是二○○ |
| `A_use_group_that_names_no_group_still_gets_the_stricter_limit` | 安全方向沒被放寬掉 |
| `The_evidence_records_what_was_typed_beside_what_it_was_read_as` | 改寫寫進證據 |
| `A_text_that_needed_no_respelling_reads_exactly_as_it_did_before` | 沒折到的值連來源字串都不動（證據基線不漂移） |
| `No_other_text_field_is_folded` | 只有 `building.use` 會被折，`zone.use` 不會 |

`Parameters/ZoneAreaLimitTests.cs` 與 `Rules/Article83AreaRuleTests.cs` 既有的Ｈ－２組測試一條未改，
仍然通過——這一步沒有改變 `H-2` 本身的判定。
