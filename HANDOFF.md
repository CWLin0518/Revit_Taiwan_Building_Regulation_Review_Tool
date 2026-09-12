# 交接紀錄（2026-09-12）

給下一個接手的人（或下一輪的我）看的現況說明。專案本體說明見 `README.md` / `ARTICLE164.md`；這份文件只記「現在進度到哪、還沒驗證什麼、接下來要做什麼」。

## 現在的狀態

- 程式碼**已修改完成、`dotnet build` 通過（0 錯誤 0 警告）**，但**最新一版還沒有在 Revit 裡實機驗證過**——上一個修的 bug（`Document.Regenerate()` 在兩個 Transaction 之間、沒有交易開啟時呼叫，導致「Modification of the document is forbidden」）修完後，使用者還沒回報是否已經測試通過。
- 部署腳本：`scripts/redeploy.bat`，雙擊即可（會自動等待 Revit 關閉、重新建置、部署到 `%APPDATA%\Autodesk\Revit\Addins\2024\BuildingRegulationReview\`）。
- 目前 Revit 常常開著，部署前要提醒使用者存檔關閉。

## 這個外掛在做什麼（簡述）

檢查建築技術規則第164條（面前道路陰影／建築物高度限制）。面板（「建築技術規則檢討」dockable pane）裡選第164條項目後：

1. **開始檢討**：自動判斷外牆／樓板 → 可勾選要排除的類型 → 選建築線 → 點道路側 → 輸入道路寬度 → 用 RhinoCommon 算 3.6:1 投影與道路陰影面積 As → **直接建立/覆蓋一個專用平面視圖「164條圖說－結果」**（灰色建物平面投影＋符合/不符合上色的道路陰影 FilledRegion＋建築線 Detail Line，其餘模型類別已隱藏）。這個視圖的建立/覆蓋是這次改動的重點，之前是等到「圖說製作」才建，現在改成「開始檢討」當下就建好。
2. **圖說製作**（僅結果為「符合」時可用）：選圖框 → 依圖框紙張大小自動算平面圖比例尺＋圖例文字/色塊大小 → 跳出「排版預覽」視窗，可拖曳調整平面圖／圖例兩個色塊在圖紙上的位置 → 依預覽位置建立 Drafting View 圖例（法規原文＋公式＋計算過程）與 Sheet，把兩個 View 放上去。

檢討結果（含算好的幾何、PlanView 的 ElementId 等）用 `Article164ReviewSession`（一個 `Dictionary<Document, Result>` 的 in-memory 靜態快取）保存，**只在本次 Revit 工作階段有效**，重開 Revit 要重新「開始檢討」。

## 檔案地圖（`src/BuildingRegulationReview/`）

- `Article164Command.cs` — 「開始檢討」主邏輯（收集元件、算陰影、寫入 Session、呼叫 `Article164PlanViewBuilder.Create`）。
- `Article164PlanViewBuilder.cs` — 建立/覆蓋「164條圖說－結果」平面視圖（footprint／道路陰影 FilledRegion、建築線、隱藏模型類別、裁剪範圍）。
- `Article164ReviewSession.cs` — 依 `Document` 保存最近一次檢討結果的靜態快取。
- `Article164ElementFilterWindow.cs` — 開始檢討時的牆/樓板類型勾選視窗。
- `Article164OptionsWindow.cs` — 道路寬度／永久性空地輸入視窗（**下一步要加拉桿，見下方**）。
- `Article164DrawingCommand.cs` — 「圖說製作」入口：Pass/Fail 閘門、兩段 Transaction（建圖紙／排版），中間跳出圖框選擇與排版預覽視窗。
- `Article164DrawingBuilder.cs` — Sheet／Drafting View（圖例）／比例尺計算的實作（**圖例排版是下一步要改的重點**）。
- `Article164LayoutPreviewWindow.cs` — 拖拉排版預覽視窗（純 code-behind WPF，Canvas + 滑鼠拖曳）。
- `Article164TitleBlockWindow.cs` — 圖框選擇視窗。
- `Article164FilledRegionWriter.cs` — 現在只剩 `GetOrCreateFilledRegionType` 靜態工具方法。
- `ReviewPaneControl.xaml(.cs)` / `ReviewPaneProvider.cs` / `App.cs` — 面板 UI 與兩個按鈕（開始檢討／圖說製作）的 ExternalEvent 註冊。

## 這次工作階段修過的 bug（供參考，避免重蹈覆轍）

1. 牆/樓板自動判斷邏輯的 Selection Set 白名單、Crop Box 範圍限制、Wall Function 判斷（初期功能）。
2. `ToRevitLoop` 用寫死 `1e-6` 當短線段門檻，改成用 `Application.ShortCurveTolerance`（Rhino 曲線轉 Revit CurveLoop 時太短的線段會炸例外）。
3. `TextNote.Create` 的 width 參數要落在 `TextNote.GetMinimumAllowedWidth`/`GetMaximumAllowedWidth` 範圍內，不能寫死。
4. 圖例 Drafting View 沒設 `Scale = 1`，內容座標是用「紙張實際英尺數」畫的，比例尺不對就會跟排版預覽對不上。
5. `Article164ReviewSession` 存的 `ShadowSilhouette` 一度誤存成**未裁切的完整陰影投影**（`shadow`），應該存**與道路範圍相交後的區域**（`roadShadow`，也就是真正的 As）——已修正。
6. 最新一個：`Article164DrawingBuilder.GetTitleBlockBounds` 裡的 `_document.Regenerate()` 是在兩個 Transaction 之間（沒有交易開啟時）呼叫，Revit 直接丟「Modification of the document is forbidden」。已移除該行（`tx.Commit()` 本身就會做 regenerate，不需要手動再呼叫）。**這個修正還沒實機驗證**。

## 已完成的三項新需求（2026-09-12 稍晚追加，程式碼已完成，尚未實機驗證）

1. **圖例改成真正的表格，且縮小**：`Article164DrawingBuilder.CreateLegendView` 重寫，改用 `DrawLegendTable` 畫出真正的 2列×2欄表格（外框＋列分隔線＋欄分隔線，皆為 DetailLine），色塊/欄寬/邊界全部依「文字大小」（而非紙張大小）用固定比例常數換算（`SwatchSizeFactor`/`RowPaddingFactor`/`LabelColumnChars`/`BodyTextChars`/`CharWidthFactor`/`LineHeightFactor`），不再隨圖框大小放大。
2. **Drafting View 生成時可選字體大小**：`Article164TitleBlockWindow` 新增字體大小拉桿＋文字框（1.5～6mm，預設2.5mm，雙向同步），選好圖框的同時一併決定文字大小，傳入 `CreateLegendView(textSizeFeet)`。
3. **道路寬度拉桿**：`Article164OptionsWindow` 新增 `Slider`（1～40公尺）與既有 `TextBox` 雙向同步（用 `_syncing` bool 防止無限迴圈），驗證邏輯不變（仍要求大於零）。

### 連動的架構調整

- `Article164DrawingBuilder.ComputeLegendBoxSize(paperWidth,paperHeight)`（舊：依紙張大小算圖例框）已刪除，改成 `EstimateLegendBoxSize(textSizeFeet)`（純算式，依文字大小估算圖例大概尺寸，供排版預覽視窗畫框用；用固定行數 3/6 估計公式/計算過程文字區塊，法規原文用真實字串長度估）。
- `CreateLegendView` 現在回傳 `(View, ActualWidth, ActualHeight)`（實際畫出後量測的大小），並把 Drafting View 的裁剪範圍（CropBox）直接設成這個實際內容的範圍，不再是套進一個外部給定的固定框——所以理論上不會再有「內容被裁掉」的問題，但估算值（排版預覽用）跟實際值可能有落差。
- `Article164DrawingCommand.cs` 會比較「排版預覽時的估計大小」跟「實際建立後的大小」，若實際大小超過估計 15% 以上，完成訊息會提醒使用者「圖例可能跟平面圖重疊，建議重新執行」。

`dotnet build` 通過，0 錯誤 0 警告。Code review 已跑過（review 重點：Slider 雙向同步邊界情況、表格格線座標算法、估算值與實際值落差風險、CropBox 置中語意、TextNoteType 共用/覆寫風險）——**這輪的 review 結果還沒回報，下一位接手者要記得先看 review 結果、視情況修正後再實機測試**。

## 建議下一步順序

1. 先請使用者確認「Regenerate 交易外呼叫」的修正是否已經解決 `Modification of the document is forbidden` 錯誤，以及上面三項新需求（圖例表格／字體大小／道路寬度拉桿）在 Revit 實機操作起來是否正確。
2. 特別注意：目前排版預覽視窗顯示的圖例大小是「估算值」，跟實際建好之後的表格/文字大小可能有落差（尤其字體選很大或很小時），需要實機測試確認 15% 誤差門檻的提醒是否足夠，或需要調整估算公式讓它更貼近實際。
