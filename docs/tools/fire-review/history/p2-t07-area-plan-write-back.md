# P2-T07 — Area Plan 寫回

## 完成範圍

對應 spec 10.5 的第 1、2 項（Area Boundary Line 與 Area 的建立／更新）與面積標註，加上工具擁有權標記、安全刪除與 `TransactionGroup`。spec 10.5 的第 3 到 5 項（Area Color Scheme、Drafting View、單線圖命名）屬於 P2-T08，本任務把它們列為「延後」並在預覽與日誌中說明，不做半套。

### Application — `WriteBack`

- `ApplyPreview.cs`：`ApplyPreviewItem` 新增 `Planned`（這一列要寫的 `PlannedElement`，刪除列為 null）。**寫回執行的就是使用者看過的那份預覽**，不再另外推導一份可能不一樣的計畫。
- `ApplyPlan.cs`（新增）：
  - `ApplyStage`：`Remove` → `Boundaries` → `Areas` → `Tags` → `DetailCurves`。階段存在的理由是 Revit 的相依鏈，不是為了批次：標註要有面積、面積要有一圈閉合的邊界線，而面積一旦失去那一圈就變成未放置且不會自己回來。
  - `ApplyStep`：一個步驟＝預覽裡的一列。建構時就擋掉「要寫卻沒有幾何」與「要改卻沒有元素」。
  - `ApplyPlan.Build(preview, writableKinds)`：把預覽排成執行順序；`writableKinds` 以外的新增／更新列進 `Deferred`（本任務＝單線圖細部線），**刪除不受 `writableKinds` 限制**——刪除不需要知道元素是什麼種類，若因為不會建立就不敢刪，模型會累積沒有人會清的孤兒。
  - `ExistingElementIds`：本套件的元素現在在模型的哪裡（不變＋更新，不含刪除）。標註需要被指派它要標的面積，而那個面積常常是這次沒有變動的。
  - `ApplyFailurePolicy`：`SkipAndLog`／`RollBackEverything`，對應 spec 10.5 的「局部錯誤可由使用者選擇略過」。
- `ApplyResult.cs`（新增）：`ApplyOutcome`（已建立／已更新／已刪除／已略過／失敗）、`ApplyResultItem`、`ApplyResult`（計數、`Notes`、`Log`、`Summary`、`IsRolledBack`、`IsComplete`、`Problems`）與 `ApplyResult.Builder`。**復原的那一次不回報任何元素**——已經收集的行都沒有發生過，列出來等於告訴使用者模型裡有不存在的東西。
- `AreaAgreement.cs`（新增）：草算面積與 Revit 量到的面積的比對（spec 10.6），預設容許 1%；量到 0 m² 另外講，因為那不是誤差而是面積沒有落在封閉範圍內。本任務只寫進日誌，用它擋住 Ready 是 P2-T09。

### Revit

- `WriteBack/ManagedElementMark.cs`（新增）：擁有權標記的讀寫。**用 Extensible Storage 而不是共用參數**：參數必須先綁到 Category 才寫得進去，而面積邊界線不是專案參數能觸及的 Category——只靠參數的話，最需要冪等性的那些元素剛好沒有身分。Extensible Storage 可以掛在任何元素上、不需要事先設定專案，也不會被手動改掉。若 `BCR_ManagedKey`／`BCR_ManagedSignature` 剛好存在且可寫，同樣的值會鏡射進去，方便排程表看到；讀取時以儲存為主、參數為備援。
- `WriteBack/RevitZoneWriteBack.cs`（新增）：執行 `ApplyPlan`。整個流程包在一個 `TransactionGroup` 裡，每個階段是其中一個 `Transaction`，因為 Revit 必須在放邊界與放面積之間重新產生；群組復原會一起還原所有階段。找不到 Area Plan、沒有樓層、階段無法提交、或任何逸出的例外都是致命錯誤，整組 `RollBack` 並回報原因。
- `WriteBack/RevitManagedElementInventory.cs`：改讀 `ManagedElementMark`（儲存優先、參數備援）。

### Add-in（WPF）

- `RegionEditor/RegionEditorPreviewWindow.cs`：差異預覽變成套用前的確認對話框——多了「套用到模型」按鈕、「遇到任何一個元素寫不進去，就整批復原」核取方塊，以及延後項目（單線圖細部線）的說明。`Show(...)` 回傳 `ApplyDecision` 或 null。
- `RegionEditor/RegionEditorApplyResultWindow.cs`（新增）：寫回結果。問題列排在最前面，並可把整份日誌存成 `我的文件\防火區劃寫回日誌_yyyyMMdd_HHmmss.txt`——spec 10.5 要求略過的元素要留下紀錄，而使用者關掉就消失的對話框不算紀錄。
- `RegionEditor/RegionEditorWindow.cs`：`RequestApply` 掛點與 `ReportApplied(result)`；寫回期間停用復原／重做／預覽按鈕並擋住關窗；`IsComplete` 時才呼叫 `MarkApplied()`。
- `RegionEditorCommand.cs`：`ApplyHandler`（`IExternalEventHandler`）。編輯器是 modeless 的，寫入必須在 Revit 自己的執行緒上發生。

## 驗證

- Solution build（Domain／Application／Revit／Tests）：成功，0 warnings / 0 errors。
- Add-in 專案（`src/BuildingRegulationReview/`，net48 WPF，不在 .sln 內）`-t:Rebuild`：成功，0 warnings / 0 errors。**Revit 專案能編譯代表 `NewAreaBoundaryLine`／`NewArea`／`NewAreaTag`／`SetGeometryCurve`／`TagHeadPosition`／`ShortCurveTolerance` 的用法對得上 Revit 2024 的真實 API。**
- Core tests：**330/330 passed**（本任務前 299，新增 31）：
  - `WriteBack/ApplyPlanTests.cs`（13）：階段歸屬、延後與刪除的不對稱、階段相依順序、標註先於面積、邊界先刪後畫、排序決定性、步驟攜帶的資料、缺資料的列不得成為步驟、計數與摘要。
  - `WriteBack/ApplyResultTests.cs`（12，含 `AreaAgreementTests` 5）：計數與摘要、失敗留在日誌且不算完成、略過不算失敗、復原不回報任何元素但保留說明、延後與警告進日誌、面積比對的三種結果。
  - `WriteBack/ApplyIdempotencyTests.cs`（6）：**退出條件本身**——同一份草稿連跑三次元素數不變、改名只更新不新增、縮小區劃只刪多餘的邊界、清空草稿只清自己、人工元素與別的套件的元素在任何一次都沒被動到、兩個套件共存互不刪除。
- 以實際編譯產物（net48 DLL）跑過的無頭流程（腳本在本 session scratchpad 的 `headless-t07.ps1`，60×40 四房＋一個人工元素＋一個別套件元素）：第一次建立 8 個（邊界線 6、面積 1、標註 1）→ 第二次與第三次都是「沒有需要寫入模型的變更」，模型維持 10 個元素 → 改名只更新 2 個 → 清空草稿刪掉 8 個，倖存者剛好是 `hand-drawn` 與 `other-package` → 面積比對三種結果的文字正確。
- **WPF 視窗仍無法在本機渲染**（`MS.Internal.FontCache.Util` 初始化失敗，與本任務程式碼無關），視窗層與 Revit 實際寫入以下面的手動清單驗收。

## 手動測試清單（在 Revit 中執行）

前置：`scripts/redeploy.bat` 部署後開啟測試專案，先用「防火區劃設定」建立一個有 Area Plan 的檢討套件，再開啟防火區劃編輯器並畫出至少兩個區劃。

1. 按「套用前預覽」，對話框列出全部是「新增」，底下有「單線圖細部線 n 個尚未寫入」。
2. 按「套用到模型」，結果視窗顯示「已建立 n 個、更新 0 個、刪除 0 個元素。」，標題為「寫回完成」。
3. 回到 Area Plan，確認每個區劃的外圈都有面積邊界線，內部沒有多餘的分隔線（同一區劃的兩個房間之間那道牆不應該有邊界線）。
4. 確認每個區劃裡有一個 Area，名稱等於區劃名稱，且有一個面積標註。
5. 編輯器標題列的 `*` 消失，狀態列不再提示未套用變更。
6. 再按一次「套用前預覽」：摘要應為「模型已經與草稿一致，套用不會變更任何元素」，「套用到模型」按鈕停用。
7. 再重複兩次（合計三次），用 Revit 的明細表確認 Area 與 Area Boundary 的數量沒有增加。
8. 把某個區劃改名後套用：結果只有「已更新 2 個」（面積與標註），Area Plan 上的 Area 名稱跟著改，線的數量不變。
9. 把某個房間移出區劃後套用：多餘的邊界線被刪除，剩下的被更新，其他區劃不受影響。
10. 手動在 Area Plan 上畫一條面積邊界線（人工元素），再清空一個區劃並套用：人工畫的那條線必須還在。
11. 在同一個專案建立第二個檢討套件並各自套用，兩者的元素都在；把其中一個清空套用，另一個的元素不受影響。
12. 勾選「遇到任何一個元素寫不進去，就整批復原」，製造一個必定失敗的情境（例如把某個區劃的邊界線縮到極短），套用後應顯示「寫回失敗，已全部復原，模型沒有任何變更」，且 Area Plan 完全沒有變化。
13. 不勾選同一個選項再套用一次：其他元素照常寫入，失敗的那一個出現在結果視窗最上方的紅字，且編輯器標題列的 `*` 仍在（沒有全部寫入就不算已套用）。
14. 在結果視窗按「儲存日誌」，確認 `我的文件` 下產生 txt，內容包含摘要、延後說明與每一行結果。
15. 把某個區劃的邊界故意留一個缺口後套用，結果視窗的說明應出現「面積沒有落在封閉的邊界內」或「相差 n%」。
16. 按 Ctrl+Z 在 Revit 中復原一次，確認整批寫回是**一個**復原步驟（`TransactionGroup.Assimilate`）。
17. 寫回進行中（大模型時）嘗試關閉編輯器，應被擋下並提示「正在寫回模型」。
18. 選取任一條工具建立的面積邊界線，確認 `BCR_ManagedKey` 參數若專案有綁定則顯示 token；沒有綁定時元素仍然可以被第二次執行辨識為「不變」（這是儲存而不是參數在起作用）。

## 設計決策

- **擁有權標記用 Extensible Storage，不用共用參數。** 面積邊界線（`OST_AreaSchemeLines`）不是專案參數能綁的 Category，只靠參數會讓最需要冪等性的元素沒有身分。儲存掛得上任何元素、不需要改專案設定、也不會被手動編輯掉。參數保留為鏡射，有綁就寫，沒綁也不影響。
- **執行的是使用者看過的那份預覽。** `ApplyPreviewItem` 帶著 `PlannedElement`，寫回不再重新推導一份計畫——重新推導的結果可能和畫面上顯示的不一樣。
- **階段順序來自 Revit 的相依鏈。** 標註先於面積被刪除（否則標註會跟著宿主消失，變成「更新一個不存在的元素」）；邊界線的刪除與建立在**同一個交易**內完成，拆成兩次提交會讓還活著的面積在中間那一刻失去包圍它的那一圈，而面積一旦變成未放置就不會自己回來。
- **刪除不受「會不會建立」限制。** 刪除不需要知道種類；若因為本階段不會建立單線圖細部線就不敢刪它，模型會累積永遠沒有人清的孤兒。
- **刪除前再問元素一次。** 預覽說這是我們的，但編輯器是 modeless 的、模型是活的，所以 `ManagedElementMark.IsOwnedBy` 在刪除的那一刻重新判斷。更新也一樣：標記不見了或換人了，就當作不存在、另外建立，而不是覆寫別人的東西。
- **找不到的更新改成建立。** 宿主面積被重建時 Revit 會一併刪掉它的標註；如果這種情況只記成失敗，就要跑第二次才會恢復，冪等性形同虛設。
- **復原的那一次不回報任何元素**，只留下說明與原因。
- **整批復原是使用者的選擇，不是預設。** spec 10.5 說局部錯誤「可由使用者選擇略過」，所以預設是略過並記錄，要整批復原得自己勾。
- **全部寫入才算已套用。** 有任何失敗時不呼叫 `MarkApplied()`，標題列的 `*` 留著——草稿和模型確實還不是同一件事。
- **一次執行只建立一個 SketchPlane。** `SketchPlane.Create` 每次呼叫都產生一個新元素，五十道牆沒有理由留下五十個一模一樣的平面。
- **面積比對只寫日誌。** spec 10.6 的「禁止進入 Ready」是狀態機的事，屬於 P2-T09；這裡先把數字講清楚。
- **不設定 Area 的編號。** 讓 Revit 自己配，避免重複編號跳出警告對話框打斷 modeless 流程。

## 已知限制

- 單線圖細部線（Drafting View）尚未寫入，`ApplyPlan` 把它列為延後。因此套用成功後預覽仍會顯示這些細部線是「新增」——這是實情，P2-T08 補上。
- `MarkApplied()` 在 Area Plan 全部寫入成功時就呼叫，而此時單線圖還是空的。P2-T08 接上後要重新檢視這個判準。
- 面積標註沿用 Area Plan 的預設標註型別，沒有提供選擇。
- 面積比對的結果只進日誌，沒有阻擋任何後續動作。
- 日誌是手動按鈕存檔，專案還沒有集中式的 log 設施。
- `建築防火檢討1.rvt` 的端到端實跑仍未執行（本任務起編輯器會真的寫入模型，實跑前請先備份）。
- 編輯器仍不能畫輔助線；線網缺口要回 Revit 補線後重開編輯器。
