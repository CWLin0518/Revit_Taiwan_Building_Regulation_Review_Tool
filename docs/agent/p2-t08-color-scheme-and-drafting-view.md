# P2-T08 — Color Scheme 與 Drafting View 輸出

## 完成範圍

對應 spec 10.5 的第 3 到 5 項：Area Color Scheme Entry 的建立／更新、Drafting View 的建立／更新、以 Detail Curve 複製區劃單線圖，以及 `{AreaScheme}_{SourceFloorPlan}_防火區劃` 的預設命名與「用唯一識別防止改名失聯」。P2-T07 把這三項列為「延後」，本任務把它們接上，因此**預覽不再有任何延後項目**。

### Application — `WriteBack`

- `ReviewOutputNaming.cs`（新增）：`Default(areaSchemeName, sourceFloorPlanName)` 產生 `{AreaScheme}_{SourceFloorPlan}_防火區劃`；`MakeUnique(baseName, isTaken)` 在名稱被占用時加 `(2)`、`(3)`。名稱組件會去掉 Revit 不接受的字元（`\ : { } [ ] | ; < > ? ` ~`）以及分隔字元本身——視圖真的可以叫 `1F_東棟`，不處理的話名稱看起來會變成四段。**名稱只在「建立的那一刻」用到**；已經存在的輸出一律保留使用者改過的名字。
- `ManagedOutput.cs`（新增）：`ManagedOutputKind`（`DraftingView`／`ColorFillScheme`）、`ManagedOutputKey`（token 前綴 `BCROUT`，格式 `BCROUT/{packageId:N}/{kind}`）、`ManagedOwnership.TryReadPackageId`／`BelongsTo`。容器一個套件只有一個，沒有區劃，所以不能用 `ManagedElementKey`（它要求 ZoneId 非空）；兩種 token 前綴不同，互相解析不到，不會讓元素憑空多出一個區劃。
- `ColorSchemePlan.cs`（新增）：
  - `ColorSchemeEntries.From(preview)`：從**使用者看過的那份預覽**的面積列讀出「名稱 → 顏色」。**包含「不變」的列**——第二次執行時 Area Plan 沒有任何變動，但色彩配置仍然要說出它全部的項目；只看變更的話，一次沒有變動的執行看起來會像所有區劃都消失了。
  - `ColorSchemePlan.Build(entries, existing)`：與配置現況比對，產生 `Add`／`Update`／`Remove`／`Unchanged`。**使用中的孤兒項目永遠不刪**（Revit 也不允許），改為人工處理項。
  - `ExistingColorSchemeEntry.IsColorKnown`：Revit 對尚未解析的項目會回傳 invalid color，顏色讀不到時一律視為要更新，不跟一個編出來的值比對。
- `ManualAction.cs`（新增）：`Subject`／`Reason`／`Suggestion` 三段都必填。spec 10.5 第 3 項要求「列出需人工處理項」，而只寫「不支援」的項目是死路——必須講出使用者要去點什麼。
- `ZoneWriteBackRequest.cs`（新增）：一次寫回需要的套件本體與預設輸出名稱。**帶的是整個 `ReviewPackage` 而不是幾個 UniqueId**，因為這一次執行可能要把新建的 Drafting View 記回套件，而那件事必須發生在同一個 `TransactionGroup` 內。
- `ApplyPlan.cs`：新增 `AllKinds`（由 enum 推導，不是手寫清單）與 `ColorEntries`；`ApplyStage.DetailCurves` 的註解改寫。
- `ApplyResult.cs`：新增 `ManualActions`、`Builder.Manual(...)`，納入 `Log` 與 `Summary`。**人工處理項不是失敗**：邊界線都寫進去了、只有一個色彩項目需要人去處理時，把它算成失敗會讓編輯器宣稱草稿從來沒有套用過。**復原時人工處理項會保留**，因為「工具為什麼做不到」在模型復原之後仍然成立。

### Domain

- `ReviewPackage.WithDraftingView(uniqueId, updatedAtUtc)`（新增）。`DraftingViewUniqueId` 欄位在 P1-T03 就存在，storage record 的欄位集合沒有變動，不需要遷移路徑。

### Revit

- `WriteBack/RevitDraftingViewWriter.cs`（新增）：`Ensure(package, defaultName)` 回傳 `DraftingViewLookup`（視圖／是否本次建立／沒有的話為什麼）。**只用套件上的 UniqueId 找視圖，不用名稱找**——這就是 spec 10.5 第 5 項的「唯一識別」，也順便決定了另一半：已經存在的視圖保留使用者改過的名字。專案沒有任何 Drafting View 類型時回傳人工處理項，不丟例外。
- `WriteBack/RevitColorSchemeWriter.cs`（新增）：`Sync(areaPlan, packageId, entries, log)`。
  - **只處理帶有本套件擁有權標記的色彩配置**。色彩配置是專案層級的元素，改別人的等於覆寫別人的專案設定（DoD 第 6 項禁止）。
  - 沒有自己的配置時用 `ColorFillScheme.Duplicate(name)` 複製一份同一個 Area Scheme 的配置——Revit API 沒有從無到有建立色彩配置的方法。專案裡一個可複製的都沒有時列為人工處理項。
  - 依據參數固定為面積的「名稱」（`BuiltInParameter.ROOM_NAME`），因為寫回本來就把區劃名稱寫進去了；改用別的參數會讓平面按一個工具從來不寫的值上色。`GetSupportedParameterIds()` 不接受時列為人工處理項並停止同步項目。
  - `AddEntry` 前先問 `IsEntryConsistentWithScheme`；遇到 `ValueDuplicated` 轉成更新——只要有面積帶著那個值，Revit 會自己生出項目，新增時發現已經在那裡是正常的，轉成更新才保持冪等。
  - 刪除前**在刪除的當下**再問一次 `IsInUse`／`CanRemoveEntry`，因為這一次執行剛放下去的面積可能已經占用了那個值。
  - 套用到視圖：視圖目前掛的是**別的**色彩配置時不覆寫，改列人工處理項並告訴使用者去哪裡改。
- `WriteBack/RevitZoneWriteBack.cs`：`Apply(plan, ZoneWriteBackRequest)`（原本是 `Apply(plan, areaPlanUniqueId, policy)`）。新增 `DetailCurves`（`ApplyStage.DetailCurves`，含視圖的建立與 `ReviewPackage.WithDraftingView` 的儲存，都在同一個交易內）與 `ColorScheme` 兩個階段，兩者都在原本的 `TransactionGroup` 內，所以一次執行的所有輸出要嘛全部留下、要嘛全部沒有。
- `WriteBack/ManagedElementMark.cs`：`Write` 多一個 `ManagedOutputKey` 多載；`IsOwnedBy` 改走 `ManagedOwnership.BelongsTo`，兩種 token 都認得。

### Add-in（WPF）

- `RegionEditorWindow.cs`：`ApplyPlan.Build(preview, ApplyPlan.AllKinds)`。
- `RegionEditorCommand.cs`：`ApplyHandler` 改持有 `packageId`，每次執行時從 repository 重新讀套件（上一次執行可能剛把 Drafting View 記上去）；`ReadExistingElements` 同樣每次重讀套件的 `DraftingViewUniqueId`——**沒有這一條，第一次建立的細部線在下一次預覽時會被當成「新增」，冪等性就斷了**。新增 `LoadPackage`／`DefaultOutputName`。
- `RegionEditorApplyResultWindow.cs`：人工處理項以橘色排在摘要下方。
- `RegionEditorPreviewWindow.cs`：說明這一次寫入同時包含色彩配置與單線圖。

## 刻意沒有做的事

- **重複區劃名稱的歧義判斷**：色彩配置以名稱為鍵，兩個同名不同色的區劃確實沒有正確答案，但 `ZoneDraftSet.Add`／`Rename` 本來就拒絕重複名稱（`regions.zone.duplicateName`），這個情境從領域層就到不了。寫第二道防線只會多一段永遠測不到的分支，規則留在能當著使用者的面講出來的地方。
- **`WriteDraftingView`／`WriteColorScheme` 開關**：一度加過，但沒有任何呼叫端會把它設成 false，而且會製造一個真實的矛盾——使用者「選擇不輸出」時草稿其實沒有完整進模型，編輯器的 `*` 卻會被清掉。spec 10.5 要求兩者都做，所以開關直接移除。
- **找不到 Drafting View 時記成「略過」**：改成 `Failed`。草稿沒有進模型就是沒有進，記成略過會讓 `IsComplete` 為真而清掉未套用標記；記成失敗則 `*` 保留，而且「任一失敗就整批復原」的設定也能照常生效。

## 驗證

- Solution build（Domain／Application／Revit／Tests）：成功，0 warnings / 0 errors。
- Add-in 專案（`src/BuildingRegulationReview/`，net48 WPF，不在 .sln 內）`-t:Rebuild`：成功，0 warnings / 0 errors。**Revit 專案能編譯代表 `ColorFillScheme`／`ColorFillSchemeEntry`／`IsEntryConsistentWithScheme`／`CanRemoveEntry`／`GetSupportedParameterIds`／`View.SetColorFillSchemeId`／`ViewDrafting.Create`／`NewDetailCurve` 的用法對得上 Revit 2024 的真實 API**（另以反射核對過 `C:\Program Files\Autodesk\Revit 2024\RevitAPI.dll` 的成員簽章）。
- Core tests：**391/391 passed**（本任務前 330，新增 61）：
  - `WriteBack/ReviewOutputNamingTests.cs`（13）：預設格式、去空白、分隔字元與 Revit 禁用字元、缺件時的 `未命名`、加序號、無限迴圈的出口。
  - `WriteBack/ColorSchemePlanTests.cs`（15）：每個區劃一個項目、名稱排序、顏色就是編輯器的顏色、空區劃不產生項目、多塊區劃只產生一個項目、刪除中的區劃不產生顏色；以及空配置全新增、第二次全不變、改色只更新、顏色讀不到時強制更新、沒人用的孤兒項目刪除、使用中的孤兒項目變人工處理項、清空草稿只清自己、摘要計數。
  - `WriteBack/ManagedOutputTests.cs`（14）：token round-trip、兩種 token 互不誤認、壞 token 不解析、擁有權兩種形狀都認得、跨套件不認、`Guid.Empty` 不擁有任何東西。
  - `WriteBack/ApplyPlanTests.cs`（+4）：`AllKinds` 由 enum 推導、細部線成為步驟且沒有延後、細部線階段排在 Area Plan 全部之後、色彩項目含「不變」的列、刪除中的區劃不帶顏色。
  - `WriteBack/ApplyResultTests.cs`（+5）：人工處理項不是失敗但進摘要與日誌、三段式文字、缺任一段就不是人工處理項、復原後仍保留。
  - `WriteBack/ApplyIdempotencyTests.cs`（+4）：**退出條件本身**——含單線圖連跑三次元素數不變（14 個）、縮小區劃時細部線跟著減少、清空草稿連細部線一起清且人工元素倖存、先只寫 Area Plan 再補寫單線圖時只新增細部線不重複既有元素。
  - `ReviewPackages/ReviewPackageTests.cs`（+2）：`WithDraftingView` 記下識別且不動到其他欄位、空白識別被拒絕。
- 以實際編譯產物（net48 DLL）跑過的無頭流程（腳本在本 session scratchpad 的 `headless-t08.ps1`，20×10 兩房兩區劃＋一個人工元素＋一個別套件元素）：第一次建立 20 個（邊界線 8、細部線 8、面積 2、標註 2）且沒有任何延後項目 → 第二、三次都是「沒有需要寫入模型的變更」，模型維持 22 個 → 色彩項目兩個且依名稱排序、空配置全新增、已同步的配置零變更、混合情境只刪沒人用的孤兒並把使用中的變成人工處理項 → 改名只更新 2 個且色彩項目跟著改名 → 預設名稱與加序號正確 → 清空草稿刪 20 個，倖存者剛好是 `hand-drawn` 與 `other-package`，且色彩項目歸零。
- **WPF 視窗仍無法在本機渲染**（`MS.Internal.FontCache.Util` 初始化失敗，與本任務程式碼無關），視窗層與 Revit 實際寫入以下面的手動清單驗收。

## 手動測試清單（在 Revit 中執行）

前置：`scripts/redeploy.bat` 部署後開啟測試專案，先用「防火區劃設定」建立一個有 Area Plan 的檢討套件，再開啟防火區劃編輯器並畫出至少兩個顏色不同的區劃。**本任務起會建立新的視圖與色彩配置，實跑前請先備份。**

### 單線圖 Drafting View

1. 按「套用前預覽」，底部說明提到「同一次寫入還會更新本套件的面積色彩配置，並把單線圖細部線寫進專屬的繪圖視圖」，且**列表中不再出現任何「尚未寫入」的延後說明**。
2. 按「套用到模型」，專案瀏覽器的「繪圖視圖」下出現一個名為 `{面積配置名稱}_{來源樓層平面名稱}_防火區劃` 的新視圖。
3. 打開那個視圖，確認區劃外圈的單線圖與 Area Plan 上的面積邊界線形狀一致，且同一區劃內部沒有多餘的分隔線。
4. 把該視圖改名成別的名字，再套用一次：**不會產生第二個視圖**，改過的名字也保留下來（唯一識別生效）。
5. 把某個區劃改小後套用：單線圖的線跟著減少，其他區劃的線不受影響。
6. 手動在單線圖視圖裡畫一條細部線，清空一個區劃並套用：手畫的那條線必須還在。
7. 刪掉整個單線圖視圖後再套用一次：會重新建立一個新視圖（名稱回到預設），且細部線完整重畫。
8. 連續套用三次，用 Revit 的明細表或框選確認細部線數量沒有增加。

### 面積色彩配置 Color Scheme

9. 第一次套用後打開 Area Plan 的「色彩配置」對話框，確認多出一個名為 `{面積配置名稱}_{Area Plan 名稱}_防火區劃` 的配置，依據參數是「名稱」，且每個區劃各有一個項目、顏色與編輯器一致。
10. 若該 Area Plan 原本沒有套用任何色彩配置，確認視圖已自動套上這個新配置，平面上看得到區劃顏色。
11. 在編輯器裡把某個區劃改色後套用，確認色彩配置裡那一項的顏色跟著改，**而且沒有新增第二個同名項目**。
12. 在編輯器裡把某個區劃改名後套用，確認出現新名稱的項目，舊名稱的項目在沒有任何面積使用時被移除。
13. 手動在專案中放一個名稱與某舊區劃相同的面積，再把該區劃刪掉並套用：結果視窗出現橘色的「需人工處理：色彩項目「…」」，而該項目**沒有**被刪除。
14. 先把 Area Plan 的色彩配置手動改成別的配置，再套用一次：結果視窗出現「這個 Area Plan 目前套用的是另一個色彩配置，工具不會覆寫既有設定」，視圖上的配置維持使用者選的那個。
15. 在一個沒有任何面積色彩配置可複製的專案中套用：結果視窗出現「這個專案的面積配置底下沒有任何色彩配置可以複製」，其餘寫入照常完成，且沒有因此整批復原。

### 整體與冪等

16. 再按一次「套用前預覽」：摘要應為「模型已經與草稿一致，套用不會變更任何元素」，「套用到模型」按鈕停用。
17. 全部寫入成功（沒有失敗）時，編輯器標題列的 `*` 消失。
18. 勾選「遇到任何一個元素寫不進去，就整批復原」，在一個沒有 Drafting View 類型的專案中套用：應顯示「寫回失敗，已全部復原，模型沒有任何變更」，Area Plan 與色彩配置都沒有變化。
19. 不勾選同一個選項、同樣在沒有 Drafting View 類型的專案中套用：Area Plan 照常寫入，細部線出現在結果視窗最上方的紅字，且標題列的 `*` **仍在**。
20. 在結果視窗按「儲存日誌」，確認 txt 內含摘要、色彩配置的變更摘要、每一則人工處理項與每一行結果。
21. 在同一個專案建立第二個檢討套件並各自套用：兩個套件各有自己的單線圖視圖與色彩配置；把其中一個清空套用，另一個的視圖、細部線與色彩項目完全不受影響。

## 已知限制

- `ApplyPlan.IsEmpty` 為真時（模型已與草稿一致）整個寫回會提早返回，所以**使用者手動改壞色彩配置後，要等到下一次真的有元素變更才會被修回來**。預覽的「套用到模型」按鈕在這種狀態下本來就是停用的。
- 色彩配置的依據參數固定為面積的「名稱」，沒有提供選擇。
- 單線圖只有細部線，沒有區劃名稱文字或圖例；圖例是 Phase 4（P4-T03）。
- 細部線沿用視圖的預設線型，沒有按區劃顏色上色——`OST_Lines` 的線型是專案層級設定，為每個區劃建立線型會污染專案；區劃顏色目前只透過色彩配置呈現。
- 單線圖視圖沒有套用任何 View Template。
- 面積比對（`AreaAgreement`）仍只寫日誌，擋住 Ready 是 P2-T09。
- 日誌仍靠結果視窗的按鈕存檔，專案還沒有集中式 log 設施。
