# P2-T05 — Region Editor 基礎互動

## 完成範圍

Domain `Regions`（純資料，不含 WPF 與 Revit）：

- `ZoneColor.cs`：`ZoneColor` 是單純的 RGB 值物件（`FromHex`／`ToHex`／`RelativeLuminance`／`IsDark`），不是 WPF 的 `Color`、也不是 Revit 的 `Color`，因此同一個顏色可以從 Editor 一路帶到 P2-T08 的 Area Color Scheme，兩邊都不必擁有型別。`ZoneColorPalette` 是十個彼此分得開、可壓黑字的分類色，`NextUnused` 讓新建的區劃自動拿到還沒被用掉的顏色——同樣的編輯順序永遠得到同樣的配色。
- `ZoneDraft.cs`：一個區劃草稿＝名稱＋顏色＋所屬的面 ID（升冪、去重）。不可變，`WithName`／`WithColor`／`Including`／`Excluding` 都回傳新物件，P2-T06 的 Undo/Redo 因此可以直接留存舊值，不必自己做深拷貝。
- `ZoneDraftSet.cs`：一個 Package 的全部區劃草稿，負責 spec 10.3 明文寫出的那條規則——**一個 Region 同一時間只能屬於一個區劃**。`Assign` 把面指派給新區劃時會自動從舊區劃移出，並在 `ZoneAssignment.PreviousZoneId` 回報是從哪裡搬過來的；`Rename`／`Recolor`／`Remove`／`Unassign`／`RetainFaces` 全部回傳 `Result`。名稱以忽略大小寫的方式唯一。

Application `RegionEditing`（可單元測試，無 UI 相依）：

- `EditorViewport.cs`：`ScreenPoint`／`ScreenSize`／`ScreenRect`（像素、Y 向下）與 `EditorViewport`（模型英尺、Y 向上）之間的換算。不可變，`ZoomAt` 會把游標底下的模型點釘在原位，`PanByPixels` 以像素平移，`FitTo` 把整張平面連同留白放進畫布。縮放上下限是顯示限制不是幾何容差，超出時夾住而不是失敗。
- `RegionEditorView.cs`：一次重繪所需的全部資料，且已經是螢幕座標。`FaceVisual`（外環＋孔洞環、填色、選取狀態、標註錨點）、`ZoneVisual`（名稱、草算面積、孔洞數、連通塊數、`Label`）、`EditorIssue`（把線網修復問題與求解問題合成同一份清單）、`RegionEditorView.Summary`（畫面下方那一行統計）。UI 層因此不持有任何幾何，顯示規則可以在沒有視窗的情況下被斷言。
- `RegionEditorSession.cs`：Editor 本身。持有求解結果、區劃草稿、選取狀態與視埠，把手勢翻譯成編輯：`AddFaceAt`（左鍵）、`RemoveFaceAt`（右鍵）、`SelectAt`／`SelectInBox`／`SelectAll`／`SelectZoneFaces`、`CreateZone`／`RenameZone`／`RecolorZone`／`DeleteZone`／`SetActiveZone`、`ZoomByWheel`／`PanByPixels`／`ZoomToFit`／`ZoomToZone`、`SourcesOfFace`／`SourcesAt`／`SourcesOfSelection`／`SourcesOfZone`。每個操作都回傳 `Result`，訊息就是 UI 狀態列要顯示的那一句。

Revit Add-in（WPF，純 code-behind，沿用專案既有視窗風格）：

- `RegionEditor/RegionEditorCanvas.cs`：`FrameworkElement`，`OnRender` 只畫 `BuildView()` 給的東西（面、孔洞、區劃標籤、問題標記、框選橡皮筋），滑鼠與鍵盤事件一律轉成 session 呼叫。
- `RegionEditor/RegionEditorWindow.cs`：畫布＋區劃清單＋問題清單＋狀態列。按鈕：新增／重新命名／顏色／刪除、加入選取的範圍／移出選取的範圍、全部顯示／顯示此區劃／在 Revit 中選取來源。
- `RegionEditor/RegionEditorDialogs.cs`：`TextPromptWindow`（命名）、`ZoneColorPickerWindow`（從共用調色盤挑色）、`PackagePickerWindow`（多個檢討套件時選一個）。
- `RegionEditorCommand.cs`：Ribbon 的「防火區劃編輯器」。讀套件 → 擷取 → 修復 → 求解 → 開啟編輯器，全程唯讀、不開 Transaction。「在 Revit 中選取來源」透過 `ExternalEvent` 回到 Revit 執行緒選取元素。
- `App.cs`：新增 Ribbon 按鈕。

## 驗證

- Solution build（Domain／Application／Revit／Tests）：成功，0 warnings / 0 errors。
- Revit Add-in 專案（`src/BuildingRegulationReview/`，net48 WPF，不在 .sln 內需另外建置）：`-t:Rebuild` 成功，0 warnings / 0 errors。
- Core tests：247/247 passed（本任務前 160，新增 87：`ZoneDraftSetTests` 27、`EditorViewportTests` 18、`RegionEditorSessionTests` 42）。
- 自動化覆蓋的退出條件項目：
  - 顯示：每個面畫出外環與孔洞環、未指派面不填色、已指派面填該區劃的顏色、區劃標籤含名稱與草算面積、孔洞數與不連通塊數會寫進標籤、空區劃沒有標註位置、修復與求解問題合成一份清單且錯誤排在前面、統計行文字。
  - 縮放與平移：游標底下的模型點在縮放後不動、來回換算一致、縮放到上下限時停住而不是失敗、`FitTo` 依寬高取較小比例並置中、`ZoomToZone` 只框住該區劃、視窗縮放不改變比例與中心。
  - 選取：點選、點空白處清空、框選（代表點／邊界頂點／框中心落在面內三條規則）、小框拖在大房間內仍選到該房間、Add 與 Toggle 模式、由清單選取整個區劃的面。
  - 左右鍵增減：沒有作用中區劃時左鍵報 `regions.editor.noActiveZone`、空白處報 `regions.editor.noFaceHere`、重複加入報 `regions.zone.faceAlreadyInZone`、加入第二個區劃時自動從第一個移出並在訊息寫出來源區劃、右鍵移出、對未指派的面右鍵報 `regions.zone.faceNotAssigned`、整批加入與移出選取。
  - 建立／刪除／名稱／顏色：自動命名「區劃 n」並跳過使用者已佔用的名稱、自動配色不重複、同名被拒、重新命名與改色、刪除後面回到未指派且作用中區劃移到清單同一位置、刪除最後一個區劃後沒有作用中區劃。
  - 來源元素：面可回推到牆的 UniqueId，選取與區劃的來源清單去重。
- 以實際編譯產物跑過的無頭流程（`scratchpad/headless-editor.ps1`，四房＋島狀孔洞＋一條懸空牆）：求得 5 個面、5 筆鄰接、1 則問題；依序做「沒有區劃先左鍵 → 建區劃 A → 左鍵兩次 → 重複左鍵 → 點空白 → 建區劃 B → 搶面 → 右鍵移出 → 再右鍵 → 全框選 → 整批加入 → 改名撞名 → 改色 → 刪除」，每一步的中文訊息與錯誤碼都與預期一致；統計行為「5 個範圍，已指派 5 個（222.97 m²），未指派 0 個；區劃 2 個；問題 2 則（錯誤 0 則）」，222.97 m² 正好等於 60×40 ft 的整塊面積，代表孔洞扣除與島狀面相加沒有重複計算。
- WPF 視窗本身**未能在本機自動渲染**：這個環境的 `MS.Internal.FontCache.Util` 型別初始設定失敗（`UriFormatException`），任何 `System.Windows.Window` 都建不起來，與本任務程式碼無關。視窗層因此改以下面的手動清單驗收。

## 手動測試清單（在 Revit 中執行）

前置：關閉 Revit → 執行 `scripts/redeploy.bat` → 開啟 `建築防火檢討1.rvt` → 確認已用「防火區劃設定」建立過至少一個 Area Plan 檢討套件。全程不會更動模型，可在正式檔上直接測。

| # | 操作 | 預期結果 |
| --- | --- | --- |
| 1 | Ribbon「建築法規檢討 → 防火區劃編輯器」 | 有多個套件時先跳出選擇視窗；選定後開啟編輯器，整張平面置中顯示，狀態列出現操作提示與統計行 |
| 2 | 滾輪在某個房間上前後滾動 | 以游標為中心縮放，游標底下的位置不會跑掉 |
| 3 | 按住中鍵拖曳（或 Alt＋左鍵拖曳） | 畫面平移；放開後游標回復箭頭 |
| 4 | 按 F | 回到整張平面 |
| 5 | 尚未建立區劃時左鍵點一個房間 | 狀態列顯示「請先建立或選取一個區劃，再把範圍加進去。」，畫面不變 |
| 6 | 按「新增」，接受預設名稱 | 清單出現「區劃 1」與色塊，成為作用中區劃 |
| 7 | 左鍵點三個相鄰房間 | 三個房間填上該區劃顏色，清單顯示面數與草算面積，標籤畫在最大的那個房間上 |
| 8 | 對同一個房間再左鍵一次 | 狀態列顯示「這個範圍已經屬於目前的區劃。」，面數不變 |
| 9 | 左鍵點在牆線上或平面外 | 狀態列顯示「這裡沒有封閉範圍，請點在已經圍起來的區域內。」 |
| 10 | 按「新增」建立第二個區劃，左鍵點步驟 7 已經加入的房間 | 該房間改成新區劃的顏色，狀態列寫出「其中 1 個原屬於『區劃 1』，已改為此區劃。」，舊區劃面數減一 |
| 11 | 右鍵點該房間 | 房間變回未指派（灰），狀態列寫出從哪個區劃移出 |
| 12 | 對未指派的房間右鍵 | 狀態列顯示「這個範圍不屬於任何區劃。」 |
| 13 | 在空白處拖曳框住數個房間，放開 | 橡皮筋框在拖曳時可見，放開後被框住的面以橘色粗框標示，狀態列回報選取數 |
| 14 | 按「加入選取的範圍」 | 全部進入作用中區劃；原本屬於其他區劃的會在訊息中被指名 |
| 15 | Ctrl＋左鍵點一個已選取的面 | 該面從選取中移除（切換選取），不會被加入區劃 |
| 16 | 選取數個面後按 Delete 鍵 | 等同「移出選取的範圍」 |
| 17 | 清單選一個區劃 → 按「重新命名」，輸入既有區劃的名稱 | 狀態列顯示「已經有名為『…』的區劃，請換一個名稱。」，名稱不變 |
| 18 | 同上，輸入新名稱 | 清單、畫面標籤同時更新 |
| 19 | 按「顏色」挑一個色票 | 該區劃的填色、清單色塊立即改變 |
| 20 | 按「顯示此區劃」 | 畫面框住該區劃；空區劃則回到整張平面 |
| 21 | 選一個區劃或數個面，按「在 Revit 中選取來源」 | Revit 視圖中對應的牆／柱／輔助線被選取；若含連結模型元素，會跳出說明有幾個無法選取 |
| 22 | 把兩個不相鄰的房間放進同一個區劃 | 清單與標籤標示「2 塊不相連」（此階段僅顯示，確認機制在 P2-T06） |
| 23 | 有孔洞的房間（例如中庭）加入區劃 | 標籤顯示「孔洞 n」，草算面積已扣除孔洞 |
| 24 | 點問題清單中任一則 | 畫面移到該位置；畫面上該處有紅（錯誤）或橘（警告）圓點 |
| 25 | 按「刪除」刪掉一個區劃 | 先跳出確認；確認後該區劃的面回到未指派，清單選取落在同一個位置的下一個區劃 |
| 26 | 關閉編輯器後再次點 Ribbon 按鈕 | 重新擷取、重新求解並開啟新的編輯器（此階段草稿不保存，屬 P2-T06／T07） |
| 27 | 全程結束後檢查 Revit | 模型沒有任何變更，Undo 堆疊是空的（本階段完全唯讀） |

## 設計決策

- **互動規則全部放在 Application 層**：`RegionEditorSession` 決定一次點擊是什麼意思、`RegionEditorView` 決定畫面上出現什麼，WPF 只負責畫和轉發事件。因此 87 個測試能在沒有視窗的情況下驗證「顯示、縮放、選取、左右鍵增減、建立／刪除、名稱、顏色」這整份退出條件，UI 層剩下的手動項目也就縮到最小。
- **唯一歸屬用「搬移＋回報」而不是「拒絕」**：spec 10.3 要求一個 Region 只能屬於一個區劃。拒絕會逼使用者先回舊區劃把面移出再回來；沉默地搬走則會讓舊區劃莫名少一塊。所以資料結構上直接搬移，並把來源區劃寫進 `ZoneAssignment` 與狀態列訊息。
- **草稿是不可變值**：`ZoneDraftSet` 的每個操作都產生新集合，session 只換一個參考。P2-T06 的 Undo/Redo 只要留存這些值即可，不需要反向操作或深拷貝；縮放、平移與選取刻意不屬於這份歷史，因為它們不是對草稿的編輯。
- **顏色不用 WPF 型別**：`ZoneColor` 留在 Domain，P2-T08 要寫 Area Color Scheme 時同一個值可以直接用，不必在 UI 與 Revit 之間轉兩次。
- **框選採 crossing 式三條規則**：框內有代表點、框內有邊界頂點、或框本身落在面內，三者任一即選中。第三條是為了讓小框拖在大房間裡也選得到那間房；三條都是 O(頂點數) 的確定性判斷，不需要多邊形裁切。
- **左鍵＝加入、Ctrl＋左鍵＝切換選取**：spec 指定左鍵加入 Region，但使用者仍需要純選取。用 Ctrl 區分，拖曳則一律是框選，右鍵維持移除，平移讓給中鍵與 Alt＋左鍵——右鍵已經有語意，不能同時用來平移。
- **修復問題與求解問題合成一份清單**：對檢討者而言兩者是同一個問題（邊界為什麼沒有閉合），而且都帶得出來源元素，所以 `EditorIssue` 統一它們，錯誤排在警告前面。
- **啟動指令做完整管線但唯讀**：`RegionEditorCommand` 走擷取 → 修復 → 求解，不開任何 Transaction。沒有這個入口，手動清單就無從執行；寫回仍然留在 P2-T07。
- **區劃名稱唯一（忽略大小寫）**：名稱之後會寫進 Area 參數，重複的名稱在那裡分不出來。追溯仍然靠 Package ID 與 Zone ID，名稱唯一只是為了讓人看得懂。

## 已知限制

- 草稿只存在記憶體：關閉編輯器就沒了，也還沒有 storage record。保存、Undo/Redo 與「離開前提示未套用變更」是 P2-T06，寫回 Revit 是 P2-T07。
- 不連通的區劃目前**只顯示**「n 塊不相連」，還沒有 spec 10.3 要求的明確確認機制（P2-T06）。
- `BuildView()` 每次重繪都會重算全部面的螢幕座標（O(總頂點數)），且沒有視埠裁切。小型樓層沒問題，大型平面的效能與裁切留給 P2-T09 一併量測。
- WPF 視窗尚未實機執行過：本機環境無法初始化 WPF 字型快取，`RegionEditorWindow` 的渲染與事件繫結只經過編譯與程式碼檢查，必須靠上面的手動清單在 Revit 中完成驗收。
- `建築防火檢討1.rvt` 的端到端實跑仍然未做（自 P2-T02 懸著）；`RegionEditorCommand` 現在提供了執行它的入口。
- 編輯器不提供繪製輔助線的功能：線網缺口仍要回 Revit 補線後重新開啟編輯器。
- 面 ID 來自當次求解。重新求解後舊草稿的面 ID 可能失效，`ZoneDraftSet.RetainFaces` 已經備好清理用的路徑，但還沒有任何流程呼叫它（P2-T06 持久化時接上）。
