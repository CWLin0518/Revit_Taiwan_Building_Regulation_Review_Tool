# ADR 0003：防火區劃編輯器設定區劃用途（垂直開口）

日期：2026-10-06
狀態：已採納
相關：`docs/regulations/vertical-compartment.md`、`docs/regulations/zone-use-vocabulary.md`、
`docs/agent/review-request-task3-region-editor-zone-use.md`、
`docs/agent/review-reply-task3-region-editor-zone-use.md`（codex fire protection 的影響審查）

## 脈絡

使用者要求「防火區劃編輯器要能設定垂直開口（管道間、挑空這類空間範圍），並連同批次設定面板、
檢討面板、檢討判斷一起修正」。釐清後確定的範圍是：**在區劃清單上加一個「用途」欄**，套用時寫進
該區劃每個 Area 的 `防火檢討_區劃用途`；不新增獨立的垂直開口物件，也不做一次設定貫穿多樓層。

動工前的現況：

- 區劃編輯器完全沒有用途的概念。`ZoneDraft` 只有名稱／顏色／面，套用時對 Area 只寫
  `BuiltInParameter.ROOM_NAME`。要標管道間必須事後另開批次設定面板。
- 批次設定面板**已經**有「區劃用途」欄，下拉就是 `ZoneUses` 的 5 個垂直區劃＋6 個第79條之1 用字。
- 檢討面板與檢討判斷**已經**完整：兩條面積規則的豁免、第79條之2 的三項附加要求、第3項挑空免除、
  `AtriumStackResolver` 由各樓層挑空 Area 推連跨樓層數與連通區劃面積，全部讀同一個參數。

所以真正要解決的不是「怎麼讓使用者選用途」，而是**讓第二個寫入入口不要破壞第一個**。

## 決策

### 1. 用途是三態，不是字串

`ZoneDraft.Use` 是 `string?`：

| 值 | 意思 | 套用時 |
| --- | --- | --- |
| `null` | 不變更（各面積維持現狀） | 什麼都不寫 |
| `""` | 一般區劃（使用者明確要清除） | 寫空字串 |
| 其他 | 該用途 | 寫該值 |

**為什麼不能只用一個字串**：空白沒辦法同時代表「一般區劃」和「維持原值」。若空白一律解成清除，
下面第 2 點的資料遺失就無法避免；若空白一律解成不變更，使用者就永遠無法把一個區劃改回一般區劃。

編輯器的用途視窗因此把三者列成三個明確的選項，而不是一個可以留白的文字框，並在「設為一般區劃」
那一項直接寫出會清掉幾個面積。批次設定面板維持它既有的「空白＝不變更、沒有清除」語意——兩邊的
操作語意不同，所以兩邊都把語意寫在畫面上。

### 2. 回讀用途要讀 Area 的參數，不讀工具的 signature

`RevitWrittenZoneReader` 的既有契約是：區劃的名稱與顏色取自寫在 Area 上的 signature，不取 Area
自己的參數——「使用者可能手改了那些，但 signature 才是工具寫過的東西」。

用途是**例外**，它從 Area 的 `防火檢討_區劃用途` 讀。理由是這個欄位的所有權不同：名稱與顏色是
工具的產出，手改它是要回報的漂移；用途是**使用者的值**，批次設定面板跟編輯器一樣有權寫它。

**不這樣做會有資料遺失**：使用者在批次面板把區劃設成 `管道間` → 重開編輯器（草稿不知道，用途為空）
→ 按套用 → 編輯器把空白寫進去 → `管道間` 連同它的第79條之2 檢討結果一起消失。

同一區劃跨多個 Area 而值不一致時，回成 `null`（不變更）並在問題清單說明。**不是**「視為未填」——
檢討的缺口可以那樣表示，編輯器那樣做會把它清掉。參數未綁定或讀不到也回 `null`，而且**不出警告**：
那是整個模型的狀態，一個區劃一行會把寫回時那句真正有用的訊息淹掉。

### 3. 用途不進 Area signature，另立一份操作計畫

`PlannedElementSignature.ForArea` 維持 `name|hex|point` 三段。加第四段要改 `TryReadArea` 從右切的
邏輯（那是為了容許名稱裡有 `|`），而且用途本來就已經是檢討輸入，批次面板改它時檢討已經會變需更新，
不需要再經由 signature。

代價是：用途改了但幾何沒改時，Area 在元素比對裡是「不變」。**最初的打算是接受這個代價、只在狀態列
說明——那是錯的**，codex 的審查指出真正的後果不是文案而是**完全不會寫入**：

- `RevitZoneWriteBack.Apply` 在 `plan.IsEmpty` 時只跑 `VerifyAreas` 就返回；
- 正常路徑只對 `ApplyStage.Areas` 的 step 做寫入，`UnchangedAreas` 只驗證不寫。

只改用途 → 沒有任何 step → `plan.IsEmpty` 為真 → 一行都不寫，而畫面會說套用完成。

因此另立 `ZoneUseOperations`：由草稿的用途與模型現值算出要寫哪些 Area，隨 `ApplyPreview` →
`ApplyPlan` 一路帶到寫回，並且**計入 `IsEmpty`**（兩層都是），所以「只改用途」是一個會發生的 run。
預覽列表、兩個 `Summary`、結果記錄都有它。

產生操作的規則：

- 草稿用途是 `null` → 不產生（第 2 點的保護就在這裡生效）。
- Area 現值等於目標值 → 不產生。同值不重寫，因為無謂的寫入也是模型變更，也會讓檢討變成需更新。
- 這次要新建的 Area，目標是空字串 → 不產生。新 Area 本來就是空的。
- 目標是空字串、而現值讀不到 → 不產生。沒有東西可清，報成寫入失敗是在怪使用者沒填過的欄位。
- 目標是具體用途、而現值讀不到 → **產生**。使用者明確要求了，就去試，並把 Revit 的回答報出來。

### 4. 寫入在 write-back 自己的交易群組裡

不呼叫 `RevitFireReviewParameterWriter`（批次面板用的那個，它自己開交易）。幾何與用途必須共用同一個
Undo 與同一套回滾策略，否則一個回滾了元素的 run 會把用途留在模型裡。

寫入前逐項檢查：擁有權標記、參數是否存在、`StorageType`、唯讀、`Set` 的回傳值，並在交易內**重讀一次
現值**（預覽是交易開啟前取的）。每一種拒絕都記成結果列，失敗計入 `FailureCount`，所以
`RollBackEverything` 會把它一起復原。參數沒綁定時是 `Failed` 而不是靜默略過——否則畫面會說套用完成，
而第79條之2 的檢討什麼也看不到。

## 後果

- 下游**不必修改**：批次設定面板、檢討面板、檢討判斷、`AtriumStackResolver`、`RevitStoreyZoneReader`、
  `MergedAtriums`、證據基線都讀同一個參數，多一個寫入入口不改變它們的演算法。用途改變讓既有檢討
  變「需更新」是既有且正確的行為。
- `ZoneDraft.Signature()` 含用途與三態旗標，所以 Undo／Redo 與「未套用變更」偵測看得到用途變更，
  也分得出「不變更」與「清除」。所有 `With*`／`Including`／`Excluding` 都保留用途。
- 設用途**不會**自動讓第79條之2第3項的免除成立，也**不會**刪掉挑空與連通區劃之間的線。免除仍取決於
  既有事實，內部線仍由 `MergedAtriums` 依免除是否成立與幾何關係判定（垂直區劃 §3.9）。
- 單元測試 20 條（`ZoneUseRestorationTests` 5、`ZoneUseOperationsTests` 12、`RegionEditorSessionTests` 3）。
  **Revit 寫入與 WPF 這一段零測試覆蓋**，由 V-29 實機驗收。

## 考慮過但沒有採用

- **新增獨立的「垂直開口」物件**，自己畫範圍而不從解出的面挑。使用者在三個選項裡沒有選它，而且
  現有的面解析對有牆圍著的管道間本來就會解出一個面。
- **一次設定貫穿多樓層**，自動在各層 Area Plan 放 Area。使用者也沒有選它，而且建模慣例
  （每個開洞樓層一個 Area）正是 `AtriumStackResolver` 推算連跨樓層數的依據，改動它要連同
  垂直區劃 §3.8 一起重新設計。
- **把用途加進 Area signature**。見第 3 點。
- **「不一致視為未填」**（沿用 `RevitStoreyZoneReader` 的 `Agreed` 讀法）。檢討的缺口可以那樣表示，
  編輯器那樣做會把不一致的值清掉；警告不等於資料保護。
- **把用途變更轉成 `FireReviewParameterEdit` 交給既有的批次 writer**。見第 4 點。

## 審查

送 codex fire protection 做影響審查（`docs/agent/review-request-task3-region-editor-zone-use.md`），
回覆在 `docs/agent/review-reply-task3-region-editor-zone-use.md`。它指出的兩項缺陷都已修正，並且
正是上面第 1 點與第 3 點的由來：

1. **用途-only 漏寫**（第 3 點）。原方案只在 `SetName` 附近寫用途，會被 `plan.IsEmpty` 與
   `UnchangedAreas` 只驗不寫這兩條路整個跳過。已改為獨立的操作計畫並計入 `IsEmpty`。
2. **混合值清除風險**（第 1、2 點）。原方案把「各面積不一致」回成空白草稿，等於下一次套用就清掉。
   已改為三態，不一致與讀不到都回「不變更」。

未採用的審查意見一項，連同理由：

- 審查要求處理「開著的批次設定面板持有舊快照」。**批次設定面板是 modal**（`ShowDialog()`），不可能在
  編輯器套用時開著，所以這條在現行架構下不成立。相關但確實存在的那個序列（先在編輯器明確選用途、
  再開面板改同一個區劃、回來套用）已記在 V-29 的「已知限制」：編輯器明確選過的值會覆蓋，結果訊息
  裡的「原為…」會是預覽當下的舊值。

審查另提到第164條的 `InteriorFinishReview` 使用 `MergedAtriums` 而須一併迴歸。查證後**沒有這個類別**，
`InteriorFinishAssessment` 也不引用 `MergedAtriums`；`MergedAtriums` 的使用者只有 `FireResistanceCheck`、
`OpeningProtectionCheck` 與 `FireReviewRunner`。本次也沒有更動 `MergedAtriums` 的任何判定。
