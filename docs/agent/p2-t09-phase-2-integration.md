# P2-T09 — 整合 Phase 2 與驗收

Phase 2 的最後一個任務。串接抽取、編輯、預覽、寫回、狀態失效與日誌，並補齊 spec 10.6 的驗收證
據。逐條驗收對照寫在 `docs/agent/phase-2-acceptance.md`。

## 完成範圍

### Application — `Diagnostics`（全新）

- `ReviewErrorCode.cs`：`ReviewStage`（擷取／修復／求解／編輯／預覽／寫回／狀態）、
  `ReviewSeverity`、以及依 spec 14「重要錯誤類型」分組的錯誤碼目錄，帶 `Describe`／`IsKnown`／
  `All`。
- `ReviewLogEntry.cs`：spec 14 要求的八個欄位。`UserText` 給畫面（不含技術細節），`ToLogLine()`
  給本機日誌（全部欄位）。技術細節進來時就遮蔽 Windows／UNC／POSIX 路徑，只留檔名。
- `ReviewLog.cs`：一次流程的日誌，是**值不是 sink**。`FromApplyResult` 把寫回結果轉成條目；
  `ToText()` 產生可寫檔的文字；`Builder` 供各階段收集。

### Application — `ReviewPackages`（全新）

- `ReviewPackageProgress.cs`：寫回結束後唯一決定套件狀態的地方，也是 spec 10.6 第 3 項
  「面積差異超過容許值禁止進入 Ready」的執行點。`Explain` 產生對應的日誌條目。
- `ReviewStaleness.cs`：`ReviewModelObservation`（純觀察，無判斷）＋ `StalenessVerdict` ＋
  `Evaluate`／`Explain`，對應 spec 13.1。
- `ReviewRunReport.cs`：把「寫回做了什麼」「套件變成什麼」「日誌」三件事收成一個值，
  `For(package, result)` 一次算完。這就是 P2-T09 要的那道接縫。

### Application — `WriteBack`

- `AreaAgreement.cs`：新增 `AreaAgreementKind`、`AreaAgreementFinding`（帶 `ManagedElementKey`，
  所以 Package ID 與 Zone ID 跟著判決走）與 `Compare(...)`。原本的 `Describe(...)` 保留，改為委
  派給 `Compare`，既有行為與測試不變。
- `ApplyResult.cs`：新增 `AreaFindings`、`AreaDisagreements`、`Builder.Area(...)`；摘要與日誌帶
  上不符的區劃；復原時丟棄（那些 Area 已經不在模型裡了）。
- `PlannedElementSignature.cs`（新增）：簽章拼法獨立出來，`ZoneWritePlan` 與 Revit 端的失效探測
  共用同一份。含 `ForCanonicalSegment`，因為從 Revit 讀回的曲線方向是 Revit 決定的。

### Domain

- `ReviewPackage.WithProgress(status, boundaryRevision?, updatedAtUtc?)` 與
  `WithNextBoundaryRevision(status)`。**儲存欄位集合沒有變動**（`Status` 與 `BoundaryRevision`
  本來就在），不需要遷移。BoundaryRevision 只進不退。

### Revit

- `WriteBack/RevitZoneWriteBack.cs`：`VerifyPlacedAreas` 改為 `log.Area(AreaAgreement.Compare(...))`，
  把判決放到結果上而不是直接寫日誌——因為它要被狀態機讀回去。
- `ReviewPackages/RevitReviewStalenessProbe.cs`（新增）：唯讀，產出 `ReviewModelObservation`。
  邊界線與細部線用重算簽章比對，面積比對名稱那一半。

### Add-in（WPF）

- `RegionEditorCommand`：改為 `TransactionMode.Manual`（只為了存狀態這一筆寫入）；開啟編輯器前
  先做 `CheckForStaleResults`，把失效原因告訴使用者並存下狀態；`ApplyHandler` 在寫回後**重讀套
  件**、組 `ReviewRunReport`、必要時存回狀態。
- `RegionEditorWindow.ReportApplied(ReviewRunReport)`：改收報告。未套用標記仍依
  `ApplyResult.IsComplete` 清除，不依是否 Ready——兩者答的是不同問題。
- `RegionEditorApplyResultWindow`：顯示套件狀態與阻擋原因；「儲存日誌」改寫 `ReviewLog.ToText()`。

## 決策

- **面積不符擋 Ready，色彩人工處理項不擋。** spec 10.6 問的是邊界圍出來的東西對不對；色彩是外觀。
  寫入失敗會擋，因為草稿確實還沒進模型。
- **「什麼都沒做」的那一次不動狀態。** 模型本來就與草稿一致時沒有任何 Area 被重新量測，這一輪
  沒有學到任何事。把套件升級成 Ready 等於做了沒人做過的宣稱。
- **邊界改了就把下游狀態打回來。** `Reviewed`／`Documented` 是建立在舊邊界上的，spec 13.1 說這種
  情況結果失效，所以成功的寫回一律重新從這一輪能擔保的東西算起。
- **失效判斷不存指紋。** 見 `phase-2-acceptance.md` 的說明：spec 13.1 要的基準線模型裡都有了，
  加欄位要換 Extensible Storage schema 並為既有套件寫遷移，換不到新資訊。
- **容差變更走「簽章不同」這條路徑**，不是另外一條。簽章的量化格就是 `ClosureFeet`。
- **日誌是值不是 logger。** 專案還沒有集中式 log 設施；保持值的形式，之後要加檔案寫入或 Revit
  journal 都不必動到各階段，而且 spec 14 的欄位集合可以在沒有文件開著的情況下被斷言。
- **技術細節在建構當下遮蔽，不是寫檔時。** 只要有一條路徑漏掉就等於沒遮。
- **`RegionEditorCommand` 從 ReadOnly 改成 Manual。** 只有存狀態那一筆寫入用到交易。算出來卻不
  存的狀態會讓下一個 session 讀到一個其實不 ready 的 Ready 套件。

## 刻意沒有做的事

- **沒有做 Design Option／專案 Phase 的失效判斷。** spec 13.1 有列，但目前整條流程沒有任何地方
  讀取它們——擷取不分 Design Option，寫回也不指定。在有東西會因此不同之前先寫判斷，等於寫一個
  沒有呼叫端的分支。列在下方限制。
- **沒有為 `BCR-PARAM-*`／`BCR-VIEW-*`／`BCR-RULE-*` 造發出點。** 錯誤碼目錄依 spec 14 列全，但
  這三組對應的階段要到 Phase 3／Phase 4 才存在。
- **沒有把失效原因塞進編輯器的問題清單。** 失效講的是「模型」與「上次寫入的結果」之間的關係，
  不是某一條邊界的問題；跟擷取警告一樣，開窗前講一次比較誠實。
- **沒有為 `ReviewPackageStatus` 加 UI。** 狀態會存、會在寫回結果視窗顯示，但套件列表還沒有欄位
  顯示它。那是 Phase 4 出圖與總覽的範圍。

## 驗證

- Solution build：成功，0 warnings / 0 errors。Add-in 專案 `-t:Rebuild`：成功，0 warnings /
  0 errors。
- Core tests：**494/494 通過**（本任務前 391，新增 103）。
  - `Diagnostics/ReviewLogTests.cs`：23（日誌欄位、UI 與日誌分流、路徑遮蔽、錯誤碼目錄）
  - `ReviewPackages/ReviewPackageProgressTests.cs`：15（Ready 閘門、復原、空跑、revision）
  - `ReviewPackages/ReviewStalenessTests.cs`：18（spec 13.1 逐條）
  - `WriteBack/PhaseTwoAcceptanceTests.cs`：20（spec 10.6 四條 ＋ spec 16.3 情境 1–5、9、10）
  - `WriteBack/PlannedElementSignatureTests.cs`：14（兩端簽章一致）
  - `WriteBack/ApplyResultTests.cs`：+9、`ReviewPackages/ReviewPackageTests.cs`：+4
- 無頭腳本 `headless-t09.ps1`：**30/30 通過**，跑的是 add-in 實際載入的 net48 輸出目錄裡的
  `BuildingRegulationReview.Domain.dll`／`BuildingRegulationReview.Application.dll`。
- **WPF 視窗仍無法在本機渲染**（`MS.Internal.FontCache.Util` 初始化失敗，與程式碼無關）。

## 手動測試清單（在 Revit 中執行）

開 `建築防火檢討1.rvt` **並先備份**。前置：`p2-t07` 的 18 項與 `p2-t08` 的 21 項。

### 套件狀態（spec 10.6 第 3 項）

1. 對一個邊界完整的區劃套用一次，確認結果視窗出現綠字「面積與邊界都已寫入且互相符合，套件進入
   「可開始檢討」」。
2. 在 Revit 中把某個區劃的一段邊界線拖開造成缺口，重開編輯器、重新指派、再套用，確認結果視窗出
   現紅字「▲」列出「⋯可能有邊界沒有閉合」，且狀態訊息說「尚不得進入「可開始檢討」」。
3. 把缺口補回、重開編輯器、改一次區劃名稱後套用，確認狀態回到「可開始檢討」。
4. 連續套用三次不做任何修改，確認第二、三次顯示「模型已經與草稿一致，套件狀態維持⋯」，且狀態
   與 BoundaryRevision 都不再變動。

### 失效（spec 13.1）

5. 套用成功一次後關閉編輯器，在 Revit 中刪掉其中一條工具產生的面積邊界線，重開編輯器，確認開窗
   前出現「模型有 N 項變更，這個套件的結果已失效⋯」的是／否對話框，列出「⋯已被修改」或「⋯都
   已不在模型中」。
6. 在第 5 步的對話框選「否」，確認編輯器不開啟。
7. 重做第 5 步並選「是」，確認編輯器照常開啟，且預覽把被刪掉的那條線列為「新增」。
8. 刪掉套件的單線圖視圖後重開編輯器，確認失效原因含「單線圖視圖已不在模型中」。
9. 刪掉套件的 Area Plan 後重開編輯器，確認訊息是「Area Plan 已不在模型中，無法再寫入」，而不是
   一般的失效訊息。
10. 完成第 5～9 任一項後存檔、關閉、重開模型，重開編輯器，確認上一次算出的狀態有被存下來（不會
    每次都重新宣告一次變更）。

### 日誌（spec 14）

11. 任一次套用後在結果視窗按「儲存日誌」，開啟「我的文件」下的 `防火區劃寫回日誌_*.txt`，確認
    每一行帶時間、嚴重度、錯誤碼、階段、Package ID、元素 UniqueId，且表頭齊全。
12. 製造一次會失敗的寫入（例如把 Area Plan 所在 Workset 設為不可編輯），儲存日誌，確認技術細節
    欄裡**沒有任何完整的檔案路徑**，只有「（已隱藏路徑）檔名」，且畫面上顯示的訊息不含技術細節。

## 已知限制

- **Revit 內的實際寫入從未執行過。** 上面 12 項與 P2-T07／T08 的 39 項全部未跑。
- **Design Option 與專案 Phase 的失效判斷未實作**（spec 13.1 有列）。整條流程目前不讀取它們。
- **參與檢討的元素與參數的失效判斷未實作**，要到 Phase 3 有規則讀取它們之後才有意義。
- **面積標註沒有被納入失效比對**：標註上沒有任何探測端可以重算的幾何。邊界線、細部線與面積名稱
  有比對。
- **面積的顏色沒有被納入失效比對**：顏色不在元素上，只有名稱那一半可比。
- **色彩配置本身沒有被納入失效比對**：使用者手動改壞色彩配置後，要等下一次真有元素變更才會修回
  來（P2-T08 的既有限制）。
- **失效判斷每次都要重讀整個 Area Plan 與單線圖的受管理元素**，大平面上這是一次完整的
  `FilteredElementCollector` 掃描。沒有量測過。
- **規則版本目前沒有來源**：`RevitReviewStalenessProbe.Observe` 的 `ruleSetVersion` 參數沒有呼叫
  端會傳，判斷路徑已就緒但要到 Phase 3 才會被觸發。
- **日誌只在結果視窗按按鈕時寫檔**，專案還是沒有集中式 log 設施；失效判斷產生的日誌目前只進對話
  框，沒有落檔。
- 草稿仍只在記憶體，關窗即失；`ZoneDraftSet.RetainFaces` 仍沒有流程呼叫。
- 編輯器不能畫輔助線：線網缺口仍要回 Revit 補線後重開編輯器。
- 單線圖只有細部線，沒有區劃名稱文字或圖例（圖例是 P4-T03）。
