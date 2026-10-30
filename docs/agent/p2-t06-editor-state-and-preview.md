# P2-T06 — Editor 狀態與差異預覽

## 完成範圍

對應 spec 10.3 剩餘的四項（Undo/Redo、唯一歸屬的呈現、非連通警告與明確確認、離開前提示未套用變更）與 spec 10.4（套用前差異預覽）。

### Domain

- `Regions/ZoneDraft.cs`：新增 `AllowsDisjointParts`——使用者是否已明確同意這個區劃可以由互不相連的區塊組成（spec 10.3）。`WithDisjointAllowed` 與既有的 `WithName`／`WithColor`／`WithFaces`／`Including`／`Excluding` 一樣回傳新值並保留這個旗標。另新增 `Signature()`：把 ID、名稱、顏色、確認旗標與面 ID 串成決定性的指紋，只涵蓋「會寫進模型的東西」。
- `Regions/ZoneDraftSet.cs`：新增 `SetDisjointAllowed`（只負責保存答案，什麼時候問是 Editor 的事）與 `Signature()`（把全部區劃的指紋接起來）。
- `Geometry/PlanRegionMap.cs`：`MultiFaceRegion` 改為持有 `ContiguousParts`（分組後的面 ID，組內升冪、組間依最小面 ID 排序），`ContiguousPartCount` 由它導出；新增 `ContiguousPartsOf(faceIds)`。寫回需要的是分組而不只是數量——一個 Revit Area 只能座落在一塊連通的範圍裡。

### Application — `RegionEditing`

- `EditorHistory.cs`（新增）：`EditorState`（區劃草稿＋作用中區劃 ID）、`EditorHistoryEntry`（狀態＋離開這個狀態的那個編輯的名稱）、`EditorHistory`（Record／Undo／Redo／Clear，上限 200 步，記錄新編輯時丟棄 redo 分支）。因為 `ZoneDraftSet` 不可變，一步就是「先前的值」，不需要反向操作。
- `RegionEditorSession.cs`：
  - `Undo()`／`Redo()`／`CanUndo`／`CanRedo`／`UndoLabel`／`RedoLabel`。每個會改到草稿的操作在成功後才記錄，失敗則把草稿還原到操作前——所以一次編輯要嘛整個發生、要嘛完全沒發生。縮放、平移、選取與 `SetActiveZone` 刻意不進歷史。
  - `HasUnappliedChanges`／`UnappliedChangesPrompt`／`MarkApplied()`：比對內容指紋而不是歷史步數，因此「一路復原回到已套用的狀態」會正確地回報沒有待套用的變更。
  - 非連通確認：`AddFaceAt`／`AddFaces`／`AddSelectionToActiveZone` 都多了 `confirmDisjoint`。未確認時若這次加入會讓區劃的連通塊數**增加**，操作以 `RegionEditorSession.DisjointNotConfirmedCode`（`regions.zone.disjointNotConfirmed`）被拒，訊息本身就是要問使用者的那一句。確認後旗標寫回草稿；區劃重新相連時旗標自動收回，所以下一次分裂要重新確認。
  - `ZoneMembershipChange.Warning`／`FullMessage`：移出範圍把區劃切成兩塊時會附帶警告（切割不是合併，不需要確認，但必須說出來）。
  - `BuildPreview(existing)`：產生 spec 10.4 的差異。
- `RegionEditorView.cs`：`ZoneVisual.AllowsDisjointParts`（標籤上顯示「n 塊不相連（已確認）」）、`RegionEditorView.CanUndo`／`CanRedo`／`HasUnappliedChanges`／`DisjointZones`／`DraftState`。

### Application — `WriteBack`（新增）

- `ManagedElement.cs`：
  - `ManagedElementKind`：面積邊界線、面積、面積標註、單線圖細部線。
  - `ManagedElementKey`：工具擁有權標記＝`BCR/{packageId}/{zoneId}/{kind}/{part}/{ordinal}`，可 `ToToken()`／`TryParse()`。`KeyParameterName`（`BCR_ManagedKey`）與 `SignatureParameterName`（`BCR_ManagedSignature`）是 P2-T07 寫入、預覽讀取的欄位契約。
  - `PlannedElement`：草稿說模型「應該有」的一個元素，帶著幾何、放置點、名稱、顏色與內容指紋。
  - `ExistingManagedElement`：模型裡帶著擁有權標記的元素。標記解析不出來或屬於別的 Package 時，`BelongsTo` 為 false，該元素永遠不會進入刪除清單。
- `ZoneWritePlan.cs`：由求解結果與草稿產生 `PlannedElement`。每個連通塊產生一個面積、一個標註，以及外圈上的每一段邊界線與對應的單線圖細部線。
- `ApplyPreview.cs`：`ApplyChangeKind`（新增／更新／刪除／不變）、`ApplyPreviewItem`、`ApplyPreview.Build(...)`，含 `Added`／`Updated`／`Deleted`／`Unchanged`／`Warnings`／`UntouchedElementCount`／`KindSummaries`／`Summary`／`IsEmpty`。

### Revit

- `WriteBack/RevitManagedElementInventory.cs`（新增）：在套件的 Area Plan 與 Drafting View 中，讀出帶有 `BCR_ManagedKey` 的元素。參數還沒被綁定時就讀不到任何東西，預覽因此把每個元素列為「新增」——這正是還沒被寫入過的模型的實情。

### Add-in（WPF）

- `RegionEditor/RegionEditorPreviewWindow.cs`（新增）：差異預覽對話框。標題行是總計，接著是每個元素種類一行的小計、警告，以及「刪除只會發生在這個檢討套件自己建立的元素上」這句明說。
- `RegionEditor/RegionEditorWindow.cs`：新增「復原」「重做」「套用前預覽」三個按鈕（含 tooltip 顯示會被復原的是哪一個動作）、狀態列第三行 `DraftState`、標題列在有未套用變更時加上 `*`、`Closing` 時的確認、非連通合併的 Yes/No 對話框、`ReadExistingElements` 掛點。
- `RegionEditor/RegionEditorCanvas.cs`：`ConfirmDisjoint` 回呼與 `HistoryStepRequested` 事件；左鍵與 Enter 走「先試一次 → 被拒就問 → 帶著答案再試一次」的流程；Ctrl+Z／Ctrl+Y。
- `RegionEditorCommand.cs`：把 `RevitManagedElementInventory` 接到視窗上，並在預覽被按下時才讀取（編輯器是 modeless 的，模型可能在開啟期間被改動）。
- `RegionEditor/RegionEditorDialogs.cs`：`PackageChoice` 帶上 `DraftingViewUniqueId`。

## 驗證

- Solution build（Domain／Application／Revit／Tests）：成功，0 warnings / 0 errors。
- Add-in 專案（`src/BuildingRegulationReview/`，net48 WPF，不在 .sln 內需另外建置）`-t:Rebuild`：成功，0 warnings / 0 errors。
- Core tests：**299/299 passed**（本任務前 247，新增 52：`EditorHistoryTests` 7、`RegionEditorSessionTests` +20、`ZoneDraftSetTests` +8、`ApplyPreviewTests` 17）。
- 自動化覆蓋的退出條件項目：
  - **狀態回放穩定**：復原後再重做回到同一個草稿；連續復原回到空草稿且沒有作用中區劃；新的編輯會丟棄 redo 分支；失敗的編輯（撞名改名）既不進歷史也不改草稿；刪除區劃後復原會連同作用中區劃一起回來；縮放／平移／選取／切換作用中區劃都不算一步；復原不會動到視埠比例；歷史達到上限時丟掉最舊的一步而不是無限成長。
  - **唯一歸屬**：沿用 P2-T05 的搬移＋回報，訊息中指名來源區劃（既有測試）。
  - **非連通確認**：未確認時以 `regions.zone.disjointNotConfirmed` 被拒且草稿不變；確認後合併成功、旗標寫入、標籤出現「（已確認）」；已經分成兩塊時再加入不會重複詢問；重新相連後旗標收回，下一次分裂重新詢問；加入相鄰的房間永遠不問；移出範圍造成切割時以警告呈現而不是攔截。
  - **未套用提示**：初始沒有提示；編輯後有提示與 `DraftState`；`MarkApplied` 之後再編輯再復原回去，提示消失；改名也算變更。
  - **差異只涵蓋目前 Package**：擁有權標記可往返；格式錯誤、別的 Package、非本工具的字串都解析不出鑰匙；別的 Package 與人工元素只被計為「未受管理」，永遠不出現在刪除清單；刪除清單中每一筆的 `PackageId` 都是本套件。
  - **差異內容正確**：單一房間＝4 條邊界線＋4 條細部線＋1 個面積＋1 個標註；同一區劃的兩個房間之間那道牆不畫（6 條）；不屬於區劃的孔洞保留（8 條），島狀面併入同一區劃時孔洞消失（4 條）；不相連的區劃每塊各自一個面積與標註；同一份草稿重複套用時全部為「不變」（spec 10.6 的不累加）；只改名時只有面積與標註是「更新」、邊界線維持「不變」。
- 以實際編譯產物（net48 DLL）跑過的無頭流程（`scratchpad/headless-t06.ps1`，四房＋島狀孔洞＋一條懸空牆，求得 5 個面、5 筆鄰接、1 則問題）：
  - 對角房間加入時被 `regions.zone.disjointNotConfirmed` 擋下，訊息為「加入後『防火區劃 A』會分成 2 塊互不相連的區塊，將各自產生一個面積。確定要合併嗎？」；
  - 確認後合併成功，標籤為「防火區劃 A / 102.19 m² / 孔洞 1 / 2 塊不相連（已確認）」；
  - 補上中間的房間後 `parts=1`、確認旗標自動收回；
  - 復原／重做的訊息與面數逐步正確，一路復原到底後 `unapplied=False`、沒有關閉提示；
  - 預覽為「將新增 20 個…」（邊界線 9、細部線 9、面積 1、標註 1；9 條邊界線＝60×40 外框被三個節點切開後的段數，且島狀面的孔洞因為同屬一個區劃而消失）；
  - 把這 20 個元素當成已寫入再跑一次 → 「模型已經與草稿一致，套用不會變更任何元素（不變 20 個）」，`IsEmpty=True`；
  - 只改名 → 「更新 2 個」，正好是面積與標註，18 條線維持不變；
  - 刪除整個區劃 → 「刪除 20 個」，另外放進去的別套件元素與人工元素被算成「未受管理 2 個不會被更動」。
- WPF 視窗本身仍**無法在本機自動渲染**（此環境的 `MS.Internal.FontCache.Util` 初始化失敗，與本任務程式碼無關），視窗層以下面的手動清單驗收。

## 手動測試清單（在 Revit 中執行）

前置：關閉 Revit → 執行 `scripts/redeploy.bat` → 開啟 `建築防火檢討1.rvt` → Ribbon「建築法規檢討 → 防火區劃編輯器」。全程唯讀，不會更動模型。

| # | 操作 | 預期結果 |
| --- | --- | --- |
| 1 | 開啟編輯器 | 標題列沒有 `*`；狀態列第三行為「草稿與模型一致」；「復原」「重做」為灰 |
| 2 | 新增一個區劃並加入一個房間 | 標題列出現 `*`；第三行變成「草稿尚未套用到模型」；「復原」變亮，tooltip 顯示「復原：將 1 個範圍加入…」 |
| 3 | 按「復原」 | 房間變回未指派；「重做」變亮，tooltip 顯示同一個動作 |
| 4 | 按「重做」 | 房間回到區劃中 |
| 5 | 按 Ctrl+Z 與 Ctrl+Y（焦點在畫布上） | 與 3、4 相同 |
| 6 | 連續復原到底 | 區劃清單清空、標題列的 `*` 消失、第三行回到「草稿與模型一致」 |
| 7 | 左鍵點一個與目前區劃**不相鄰**的房間 | 跳出 Yes/No 詢問「加入後『…』會分成 2 塊互不相連的區塊…」，預設按鈕是「否」 |
| 8 | 在步驟 7 選「否」 | 狀態列顯示「已取消：…」，房間沒有被加入 |
| 9 | 重做步驟 7 並選「是」 | 房間加入；狀態列訊息後面接「注意：『…』2 塊不相連，套用時會各自建立一個面積。」；清單與畫面標籤顯示「2 塊不相連（已確認）」；第三行加上「1 個區劃不相連」 |
| 10 | 再加入第三個也不相鄰的房間 | **不會**再次詢問（已經確認過這個區劃可以不相連） |
| 11 | 把中間的房間也加進來，讓兩塊接起來 | 標籤不再顯示「不相連」；此時再加一個不相鄰的房間**會**重新詢問 |
| 12 | 選取一個區劃中間的房間，按 Delete | 該區劃被切成兩塊；狀態列出現「注意：『…』2 塊不相連…」的警告，但**不會**跳出確認（切割不是合併） |
| 13 | 選「加入選取的範圍」且選取內容會造成不相連 | 同樣跳出步驟 7 的詢問 |
| 14 | 按「套用前預覽」 | 開啟差異預覽視窗：總計行、四種元素各一行小計、警告（未指派的範圍數、不相連的區劃、求解錯誤）、以及「刪除只會發生在這個檢討套件自己建立的元素上」 |
| 15 | 在預覽視窗中檢視清單 | 目前模型尚未被寫入過，所有項目都在「新增」之下；滑鼠停在任一列上會顯示該元素的擁有權標記 |
| 16 | 關閉預覽後再按一次「套用前預覽」 | 結果相同（預覽是唯讀的，不會改變草稿） |
| 17 | 建立一個空的區劃後開啟預覽 | 警告中出現「區劃『…』還沒有任何範圍，套用時會被略過。」 |
| 18 | 在有未套用草稿時關閉視窗 | 跳出「有 n 個區劃草稿（共 m 個範圍）尚未套用到模型，關閉後會遺失。仍要關閉嗎？」，預設按鈕是「取消」 |
| 19 | 在步驟 18 選「取消」 | 視窗留著，草稿還在 |
| 20 | 一路復原到空草稿後關閉視窗 | 直接關閉，不再詢問 |
| 21 | 全程結束後檢查 Revit | 模型沒有任何變更，Undo 堆疊是空的（編輯器的復原只作用在草稿上，與 Revit 的 Undo 無關） |

## 設計決策

- **Undo 存的是舊值，不是反向操作**：`ZoneDraftSet` 是不可變值，所以一步歷史就是「先前的那個值」加上作用中區劃 ID。沒有反向操作要寫、要配對、要出錯；回放出來的狀態一定是草稿自己走得到的狀態。上限 200 步是為了讓長時間編輯不會無限成長，掉的是最舊、也最沒人回去的那一步。
- **縮放、平移、選取不進歷史**：它們不是對草稿的編輯。把視埠也一起還原，會讓使用者按一次復原就發現畫面莫名跳走；`Undo()` 因此只動草稿與作用中區劃。
- **失敗的編輯要把草稿還原**：`AddFaces`／`RemoveFaces` 是逐面套用的迴圈，中途可能已經改過 `Zones`。失敗時還原到操作前的快照，讓「一次編輯」在歷史上與在資料上都是原子的。
- **未套用與否比對內容，不比對步數**：如果只看「做過幾步」，使用者一路復原回到已套用的狀態時仍會被問「要放棄變更嗎」。改用 `Signature()` 比對之後，回到原狀就是回到原狀。指紋只涵蓋會寫進模型的欄位，所以純粹的檢視操作不會讓草稿看起來變髒。
- **只在連通塊數「增加」時才問**：spec 10.3 禁止的是不連通的**合併**。若區劃已經是兩塊，再加一個與其中一塊相鄰的房間並沒有讓情況變壞，這時候再問只是噪音。用「塊數是否增加」當條件，剛好精準對上「這次操作造成了新的分裂」。
- **確認保存在草稿上，相連後自動收回**：確認是針對「這個區劃現在是分開的」這件事，不是一張永久通行證。區劃重新連成一塊時旗標歸零，下一次分裂必須重新確認；旗標也進 `Signature()`，因為它會影響寫回時要建立幾個面積。
- **移出造成的切割只警告不攔截**：使用者的意圖很明確（把這個房間拿出去），攔下來只會逼他再按一次。但區劃確實被切開了，所以訊息要說出來。
- **區劃外框用「邊只被自己人用過一次」求得**：求解時每一段邊界都帶著網格的 edge index，同一個區劃內部的牆會在兩個面上各出現一次。留下只出現一次的段，就得到聯集外框——合併房間時中間的牆自動消失，島狀面併進同一區劃時孔洞自動閉合。不需要多邊形布林運算，也不需要任何新的容差。
- **序數依幾何排序而不是 edge index**：edge index 只在同一次求解內穩定。把保留下來的段正規化方向後依座標排序，重新求解仍會得到同樣的序數，所以「沒有改變的邊界線」在下一次預覽時才會落在「不變」而不是一刪一增。
- **指紋的量化格度取自 `GeometryTolerance.ClosureFeet`**：重新求解會帶來浮點噪音，不量化的話每次重跑都會報成「更新」。格度必須是既有的容差，不能自己發明一個 epsilon。
- **一個連通塊一個面積**：Revit 的 Area 只能座落在一塊封閉範圍裡，所以 `ManagedElementKey` 帶 `PartIndex`，`MultiFaceRegion` 也因此需要分組而不只是塊數。
- **擁有權標記是一個字串欄位**：`BCR/{package}/{zone}/{kind}/{part}/{ordinal}` 放得進單一文字參數，讀回來就能同時回答「這是不是我的」「屬於哪個區劃」「是第幾個」。讀不出鑰匙的元素一律視為別人的，連「未受管理」以外的分類都不給——刪除的範圍因此在資料結構上就被限制住，而不是靠呼叫端自律。
- **Revit 讀取器現在就寫**：預覽如果沒有「模型現況」這個輸入，`更新` 與 `刪除` 兩條路在真實專案裡永遠走不到。先把讀取器寫好，P2-T07 只需要負責把 `BCR_ManagedKey` 與 `BCR_ManagedSignature` 兩個欄位寫上去。

## 已知限制

- 草稿仍然只在記憶體中，關窗即失；持久化不在 spec 10.3／10.4 的範圍內，`MarkApplied()` 是留給 P2-T07 寫回成功後呼叫的掛點。
- `BCR_ManagedKey`／`BCR_ManagedSignature` 尚未被任何程式寫入，因此在真實專案中預覽目前一律顯示為「新增」。這是實情而非缺陷，但代表手動清單第 15 項在 P2-T07 之後需要重驗一次「更新」與「刪除」。
- 預覽把細部線視為邊界線的複本（同樣的幾何、同樣的序數）。Drafting View 的實際建立與命名是 P2-T08。
- 面積標註目前只規劃「一個面積一個標註」，標註型別的選擇留給 P2-T07。
- 面 ID 來自當次求解；重新求解後舊草稿可能失效，`ZoneDraftSet.RetainFaces` 已備好但仍沒有流程呼叫。
