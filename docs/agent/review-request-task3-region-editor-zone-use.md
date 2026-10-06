# 影響審查請求（任務 3）：防火區劃編輯器新增「區劃用途」欄

送審日期：2026-10-06
送審人：fire prtection claude（主 agent）
受審人：fire protection（codex）
Branch：`fire-protection`｜基線 commit：`014bdf0`

**請把完整審查意見寫成 `docs/agent/review-reply-task3-region-editor-zone-use.md`**，寫完再用
`reply-status` 回一句話說檔案已好（`reply-status` 的 payload 只帶一句話，長內容傳不過來）。

---

## 1. 使用者要什麼

> 防火區劃編輯器要能設定垂直開口（管道間、挑空這類空間範圍），並連同批次設定面板、
> 檢討面板、檢討判斷一起修正。

釐清後確定的方案（使用者已從三個選項裡選定）：

**在區劃編輯器的區劃清單上多一個「用途」欄位**，可選 `挑空`／`昇降階梯間`／`樓梯間`／
`昇降機道`／`管道間`（以及第79條之1 的六個用字、留空、自行輸入），套用時一併寫進該區劃
每個 Area 的 `防火檢討_區劃用途`，不必再另開批次設定面板。「空間範圍」就是目前畫的那個區劃
的面，**不新增獨立的垂直開口物件，也不做一次設定貫穿多樓層**（使用者明確沒選這兩個）。

第二個問題使用者的回答：

> 若防火區劃編輯器新增垂直的欄位，是否會需要調整後續的程式碼，若會請調整

即：**沒有已知的既有 bug**，要的是評估下游（批次設定面板、檢討面板、檢討判斷）是否得跟著
調整，有就調整。**這份審查請求的核心問題就是這一題。**

---

## 2. 現況（已查證）

### 2.1 區劃編輯器完全沒有「用途」的概念

`src/BuildingRegulationReview.Domain/Regions/ZoneDraft.cs` 只帶
`Id`／`Name`／`Color`／`FaceIds`／`AllowsDisjointParts`。
`grep -rn "用途\|ZoneUse" src/BuildingRegulationReview/RegionEditor/ src/BuildingRegulationReview.Application/RegionEditing/`
**零命中**。

套用時 `RevitZoneWriteBack` 對 Area 只寫 **`BuiltInParameter.ROOM_NAME`** 一個參數
（`RevitZoneWriteBack.cs:678-679` 的 `SetName`）。`防火檢討_區劃用途` 目前**只**由批次設定
面板寫入。

### 2.2 下游其實都已經完整

| 層 | 現況 |
| --- | --- |
| 批次設定面板 `FireReviewParameterPanelWindow` | 已有「區劃用途」欄（`xaml:362`），下拉含 5 個垂直區劃 + 6 個第79條之1 用字，逐列與批次都有（`xaml.cs:409-411`） |
| 檢討面板 `FireReviewModel` | 已讀 `zone.use`（`FireReviewModel.cs:158-160`），`IsAtrium`／`IsArticle79_1Use` 都在 |
| 檢討判斷 | `ZoneUses.IsVerticalCompartment` 供兩條面積規則豁免；第79條之2 三項附加要求；第3項挑空免除；`AtriumStackResolver` 由各樓層挑空 Area 推連跨樓層數與連通區劃面積 |
| 讀取 | `RevitFireReviewTypeScanner.cs:367,383` 與 `RevitStoreyZoneReader.cs:78` 都從 Area 的 `防火檢討_區劃用途` 讀 |

所以**判斷層與面板層預期不必動**。要動的是編輯器這條鏈。**請重點確認這個預期對不對。**

### 2.3 區劃重開時從模型回讀的路徑

`RevitWrittenZoneReader.Read` →（每個帶本工作包 ownership mark 的 Area）→ `WrittenZoneArea`
（`zoneId`, `zoneName`, `color`, `boundaryLoops`, `placement`）→ `WrittenZoneRestorer.Restore`
→ `ZoneDraftSet`。

`RevitWrittenZoneReader` 的註解明寫：名稱與顏色取自**寫在 Area 上的 signature**，不取 Area
自己的參數——「a user may have retyped those, but the signature is what the tool wrote」。

---

## 3. 我打算怎麼做（請審查）

### 3.1 Domain：`ZoneDraft` 加 `Use`

- 新增 `public string Use { get; }`（預設 `string.Empty`，代表一般區劃／不變更）。
- `WithUse(string? use)`。
- **`Signature()` 要含 `Use`**，否則只改用途時 Undo/Redo 與「未套用變更」偵測看不出變化。
- 不做白名單驗證：`區劃用途` 是自由文字（`ZoneUses` 的註解明寫），編輯器只「提供」那些用字。
  長度上限沿用 `MaximumNameLength` 的理由（要存得進 Area 參數）。

### 3.2 回讀：`WrittenZoneArea` 加 `Use`，而且**從 Area 的參數讀，不從 signature 讀**

這是本次最需要被挑戰的決定。理由：

`防火檢討_區劃用途` 是**使用者的值**，批次設定面板擁有它；signature 是**工具寫了什麼**的紀錄，
用來偵測有人手改了工具寫的東西。兩者目的不同。

**如果不這樣做，會出現資料遺失的 bug**：使用者在批次面板把某區劃設成 `管道間` → 重開編輯器
（回讀不帶用途 → 草稿 `Use` 為空）→ 按套用 → 編輯器把空值寫進 `防火檢討_區劃用途` →
**使用者設的 `管道間` 被清掉，而且第79條之2 的檢討結果跟著不見**。

所以回讀必須從 Area 的 `防火檢討_區劃用途` 取值。同一區劃跨多個 Area（不連通區塊）值不一致時，
照 `RevitStoreyZoneReader` 既有的 `Agreed(...)` 慣例：**不一致就視為沒填**，並在
`ZoneRestoration.Warnings` 出一句話。

### 3.3 Area signature 不含用途

`PlannedElementSignature.ForArea` 維持 `name|hex|point` 三段，**不加第四段**。理由：

- `RevitReviewStalenessProbe.cs:97-100` 對 Area 只比 `ZoneNameOf`（第一個 `|` 之前），加第四段
  雖然不會弄壞它，但 `TryReadArea` 是**從右邊切**的（`LastIndexOf('|')`，註解說明是為了容許
  名稱裡有 `|`），加欄位要改那段邏輯，風險不划算。
- 用途本來就已經是檢討輸入，批次面板改它時檢討已經會變「需更新」，不需要再經由 signature。
- 代價：只改用途、不改幾何時，套用前預覽會把那個 Area 報成「不變」而不是「更新」。
  **我打算接受這個代價，但在預覽視窗的狀態列說明「用途變更不列入元素比對」。請評估這個
  取捨是否可接受，或是否該改走別的路。**

### 3.4 寫入：套用時寫 `防火檢討_區劃用途`

- 在 `RevitZoneWriteBack` 建立／更新 Area 之後寫，與 `SetName` 同一處。
- **只在草稿的 `Use` 與 Area 現值不同時才寫**，避免沒必要的模型變更。
- 參數沒綁到 Area 類別時（使用者還沒跑「防火檢討參數設定」）：**不要靜默吞掉**，在
  `ApplyResult` 出一筆可讀的訊息（「`防火檢討_區劃用途` 尚未綁定至面積，用途未寫入；
  請先執行『防火檢討參數設定』」）。
- 草稿 `Use` 為空時寫空字串（清除），因為 3.2 的回讀已讓草稿與模型一致，空值代表使用者
  真的要清掉。**請確認這個語意對不對**——批次面板的慣例是「空白＝不變更，沒有『清除』」
  （`FireReviewParameterPanelWindow.xaml.cs:485`），編輯器採相反語意會不會讓使用者意外？

### 3.5 UI

- 區劃清單加「用途」欄（目前 `_zoneList` 是 `ListBox`，顯示字串）。
- 按鈕列 `[新增][重新命名][顏色][刪除]` 中間插「用途」（`RegionEditorWindow.cs:158-161`）。
- 用途選擇視窗沿用 `RegionEditorDialogs` 的風格，選項分兩段（垂直區劃／第79條之1），
  與批次面板同一份 `ZoneUses` 清單，可自行輸入。

### 3.6 Application：`RegionEditorSession` 加一個命令

`SetActiveZoneUse(string? use)`，回 `Result`，與既有命令同形（可測）。

---

## 4. 請你回答的問題

1. **3.2 的「從 Area 參數回讀用途」會不會破壞 `RevitWrittenZoneReader` 現有的
   signature-only 契約？** 有沒有我沒看到的地方依賴「Area 參數一律不被信任」？
2. **3.3 的取捨（signature 不含用途 → 預覽報「不變」）可接受嗎？** 有沒有更好的做法？
3. **3.4 的空值語意**（編輯器空白＝清除 vs 批次面板空白＝不變更）會不會造成使用者誤用？
   建議改成什麼？
4. **批次設定面板、檢討面板、檢討判斷真的都不必動嗎？** 我的判斷是不必（見 2.2）。
   特別請你檢查：
   - `AtriumStackResolver`／`RevitStoreyZoneReader` 會不會因為用途現在有兩個寫入來源
     而讀到預期外的狀態？
   - `MergedAtriums`（第79條之2第3項免除成立的挑空與連通區劃之間不是區劃邊界，
     `docs/regulations/vertical-compartment.md` §3.9）會不會因為編輯器現在能直接標挑空，
     而讓使用者在編輯器裡畫出「挑空與連通區劃之間有區劃線」卻預期它不是邊界？
   - `ReviewStaleness`／證據基線會不會多出誤報「需更新」？
5. **寫入時機**：套用時機在 `RevitZoneWriteBack` 的 transaction group 裡，和既有的
   `RevitFireReviewParameterWriter`（批次面板用的，自己開 transaction）是兩條路。
   直接在 write-back 裡寫參數，還是把編輯,器的用途變更轉成 `FireReviewParameterEdit`
   交給既有 writer？後者會變成兩個 transaction，前者會讓 write-back 多認識一個參數。
6. **有沒有我完全沒想到的受影響功能？**（第164條那條鏈、色彩計畫、單線圖、圖例、檢討視圖標示）

---

## 5. 建置與測試基線

```bash
"/c/Program Files/dotnet/dotnet.exe" build BuildingRegulationReview.sln -v q --nologo
"/c/Program Files/dotnet/dotnet.exe" test tests/BuildingRegulationReview.Core.Tests/BuildingRegulationReview.Core.Tests.csproj --nologo -v q
```

`014bdf0` 當下：建置 0 警告，測試全通過。

⚠️ 同一個 repo 上有另一個 session 在改「法規依據」那條（`ReviewEntryReport.cs`、
`ReviewLegalReference.cs` 與對應測試），測試總數不是穩定基線。

⚠️ `BuildingRegulationReview.Revit` 與 WPF 面板**零測試覆蓋**，是專案既有的結構性風險——
本次 3.2／3.4 的改動正好落在那裡，審查時請把這件事算進去。
