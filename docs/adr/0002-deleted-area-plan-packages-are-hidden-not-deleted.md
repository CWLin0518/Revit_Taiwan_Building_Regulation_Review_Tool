# ADR-0002：Area Plan 被刪除的檢討套件只隱藏，不刪除

- 狀態：Accepted
- 日期：2026-10-06
- 對應任務：使用者交辦任務 2（刪掉的 Area Plan 仍出現在面板上，變成一串代碼）

## 背景

檢討套件（`ReviewPackage`）存在 Revit 的 `DataStorage` + Extensible Storage 裡
（spec §8「關聯資料應存於 Revit DataStorage + Extensible Storage」），而它指向的 Area Plan
是一個獨立的視圖元素。使用者在專案瀏覽器刪掉 Area Plan **不會**刪掉工作包。

於是「防火區劃編輯器」與「防火區劃檢討」的套件選單照樣列出那個工作包，而標籤的 fall back 鏈
（`RegionEditorCommand.ChoosePackage`、`FireReviewModel.LabelOf`）最後一段是
`package.PackageId.ToString("D")`——使用者看到的就是一串 GUID。使用者的原話：

> 若我不小心用「防火區劃設定」建 Area Plan 建錯，把它刪掉後，在防火區劃編輯器與檢討面板，
> 還是會看得到那個被刪掉的 area plan 但變成一串代碼，請自動判斷我刪除的 AreaPlan 並讓她
> 無法在其他面板被看見。

## 決策

**從面板選單過濾掉，但不刪除工作包，也不改儲存層的回傳內容。**

1. 判定述詞放 Application 層：`ReviewPackageAvailability.Classify` / `.Partition`，三態
   `AwaitingAreaPlan`（還沒建 Area Plan）／`AreaPlanDeleted`（建過但被刪）／`Available`。
   「Area Plan 還在不在」以 `Func<string, bool>` 注入，所以這條規則在沒有開啟文件的情況下可斷言。
2. Revit 層只做轉換：`RevitAreaPlanProbe.IsLiveAreaPlan`。
3. 兩個面板（`RegionEditorCommand.ChoosePackage`、`FireReviewCommand.ChoosePackage`）改用
   `Partition(...).Available`；被藏起來的套件以一行說明交代
   （`ReviewPackageSelection.HiddenNotice`，顯示在 `PackagePickerWindow` 選單下方，
   或在選單為空時併進 TaskDialog）。

## 為什麼不刪除工作包

刪除是不可逆的模型變更，而孤兒工作包其實是**可救回的**：
`RevitAreaPlanProvisioner.Provision` 在記錄的 Area Plan 已不符時，會沿用**同一個** `PackageId`
建一個新的 Area Plan 並存回去。使用者只要重新執行「防火區劃設定」，工作包身上的
`DraftingViewUniqueId`、`LegendViewUniqueIds`、`SheetUniqueId`、`GeneratedElementUniqueIds`、
`BoundaryRevision`、`LastReviewRunId` 與歷次 `ReviewRun` 就全部接回新的 Area Plan。
這正是 spec 附錄 9.5 驗收項「Undo、Transaction Rollback、重複執行與**刪除後修復**」要的行為。

刪掉工作包會同時毀掉這條修復路徑，並讓它掛著的單線圖視圖、圖例與已寫入元素變成再也關聯不回來的
孤兒——而那些視圖使用者可能已經放進圖框。使用者要的是「看不到」，不是「刪掉」。

若日後實務上需要清掉真正不要的套件，正確的形式是在「防火區劃設定」加一個明確的動作：
列出要刪什麼、使用者確認後才在 Transaction 裡刪。不做靜默的模型變更。

## 為什麼不改 `RevitReviewPackageRepository.GetAll()`

儲存層應該照實回傳存了什麼。更要緊的是「防火區劃設定」**必須**看得到 Area Plan 已被刪的工作包：
`FireReviewSetupCommand.Apply` 以「同樣的來源樓層平面＋面積配置」去 `existing` 裡找既有工作包來
重用。若 `GetAll()` 偷偷過濾，設定流程會找不到它、改建一個新工作包，模型裡就真的留下一個永遠
接不回去的孤兒 `DataStorage`，而且每刪一次 Area Plan 就多一個。

## 為什麼 Area Plan 的述詞比幾何層寬

`RevitAreaPlanProbe` 用 `is ViewPlan && !IsTemplate && ViewType == ViewType.AreaPlan`，
刻意**不**像 `RevitCurtainWallGeometryReader` 與 `RevitPlanGeometryExtractor` 那樣要求
`GenLevel is not null`。`GenLevel` 為 null 的 Area Plan 是「存在但不可用」，不是「已被刪除」；
讓它進選單，幾何讀取會用 `geometry.extraction.areaPlanMissing` 之類的訊息指名問題所在。
從選單直接消失的話，使用者沒有任何線索可循。

## 只剩一個套件時不出說明

兩個面板既有的行為是「只有一個套件就直接開，不問」。那條路徑不經過選單，所以
`HiddenNotice` 不會出現。這是刻意保留的：為了一個使用者自己刻意刪掉的視圖，每次開面板都先跳一個
對話框並不值得。選單為空時（所有套件的 Area Plan 都被刪了）說明仍會併進 TaskDialog——那時使用者
是真的被卡住，需要知道「重新執行防火區劃設定」這條路。

## 影響

- `FireReviewModel.LabelOf` 的 fall back 鏈**不動**。它服務的是 `AwaitingAreaPlan` 的套件，
  那時顯示來源樓層平面的名稱是對的；過濾在選單層做，`LabelOf` 不需要知道這件事。
- `ReviewStaleness` 對缺失 Area Plan 判 `Error` 的行為**不動**。那是另一條路徑（已開啟的編輯器、
  已在跑的檢討），而且判得對。
- `FireReviewSetupCommand` 的重複判定**不動**。既然 `Provision` 會重用工作包，刪 Area Plan 本身
  不會造出重複；但若模型裡已經有重複的孤兒套件（例如舊版本留下的），使用者會被一個在面板上
  看不見的東西擋住。留案，不在本次範圍。
- 單元測試：`tests/.../ReviewPackages/ReviewPackageAvailabilityTests.cs`（10 條）。
  面板接線與 Revit 行為只能實機驗，寫成 `docs/revit-verification-checklist.md` 的 **V-28**（第 10 輪）。

## 影響審查與後續修正（2026-10-06）

送 codex fire protection 做影響審查（`docs/agent/review-request-task2-deleted-area-plan-packages.md`，
回覆在 `docs/agent/review-reply-task2-deleted-area-plan-packages.md`）。結論是**支持**本方案、認定為
必要變更，但指出兩個 bug，都已修正：

1. **`RevitAreaPlanProbe.IsLiveAreaPlan` 吞掉所有例外**（本次實作造成）。`catch (Exception)` 會把文件
   失效、API context 錯誤之類的**真實故障**一律當成 `AreaPlanDeleted`，於是畫面顯示「Area Plan 已被
   刪除」，把故障藏在一句怪罪使用者的話後面。改為只捕捉 `ArgumentException`（Revit 無法解析的
   UniqueId，那確實等同不存在），其餘交由命令自己的 try/catch 當故障回報。
2. **`RevitReviewStalenessProbe.Observe` 只做 `as ViewPlan`**（既有缺口）。沒有檢查 `ViewType` 與
   `IsTemplate`，一個現在指向樓層平面或視圖樣板的引用會被當成本套件的 Area Plan，失效報告就會描述
   別人的視圖。改為共用同一個身分判斷 `RevitAreaPlanProbe.IsLiveAreaPlan(Element?)`。

同時依審查調整措辭：說明文字由「Area Plan **已被刪除**」改為「Area Plan **已不存在（通常是被刪除了）**」。
探測器分不出「使用者刪了視圖」與「引用失效」，而一句斷言使用者做了什麼的話，總有說錯的時候。

審查另指出兩件**不在本次範圍、留案**的事，與原本就記在上面的留案一致：

- `FireReviewSetupCommand` 的重複判定在孤兒套件存在時會擋住使用者，而「請先清理」沒有入口。審查建議
  至少把來源視圖／面積配置、重複數、`PackageId` 顯示出來；**不要**改成只計 live 套件或 `First` 選一筆。
- 「刪除後修復」只保留關聯與歷史，**不**保證舊結果仍有效（`Status` 原樣保留，可能是 `Reviewed`）。
  區劃與檢討都要重新確認。這一點已在 V-28 第 8 項驗證。
