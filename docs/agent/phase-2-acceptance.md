# Phase 2 驗收紀錄（P2-T09）

spec Draft v1.1 第 10.6 節與第 16.3 節的逐條對照。**自動驗證**欄列的是
`tests/BuildingRegulationReview.Core.Tests/` 中可重跑的測試，**狀態**欄講的就是這些測試的結果。
必須在 Revit 2024 開著 `建築防火檢討1.rvt` 才能確認的部分尚未執行，集中列在最後一節。

## 自動驗證總結

- `dotnet build BuildingRegulationReview.sln`：成功，0 warnings、0 errors。
- `dotnet build src/BuildingRegulationReview/BuildingRegulationReview.csproj -t:Rebuild`：成功，0 warnings、0 errors。
- `dotnet test BuildingRegulationReview.sln`：**494/494 通過**（P2-T09 前 391，本任務新增 103）。
- 無頭腳本 `headless-t09.ps1`：**30/30 通過**。跑的是 add-in 實際載入的 net48 輸出目錄裡的
  `BuildingRegulationReview.Domain.dll` 與 `BuildingRegulationReview.Application.dll`，不是測試
  專案的 net10.0 組件，所以新的狀態機、失效判斷與日誌是在 Revit 會載入的框架下驗過的。

## 第 10.6 節：Phase 2 驗收

| # | 驗收條件 | 自動驗證 | 狀態 |
| --- | --- | --- | --- |
| 1 | 同一份草稿重複套用不會累加線、Area 或 Drafting View | `PhaseTwoAcceptanceTests.Acceptance_ApplyingTheSameDraftThreeTimesAddsNothingTheSecondOrThirdTime`、`Acceptance_TheDraftingViewCopiesDoNotAccumulateEither`、`Scenario10_*`；另有 P2-T07／T08 的 `ApplyIdempotencyTests`（10 項） | 通過 |
| 2 | 工具不刪除未受管理的元素 | `Acceptance_NeitherHandDrawnWorkNorAnotherPackagesElementsAreEverRemoved`、`Acceptance_AnElementThatLostItsOwnershipMarkIsLeftAloneRatherThanTidiedAway` | 通過 |
| 3 | 面積與 Revit Area 的差異超過容許值時禁止進入 Ready，並指出可能的邊界問題 | `Acceptance_AnAreaRevitMeasuresDifferentlyFromTheDraftBlocksReadyAndSaysWhy`、`Acceptance_AnAreaThatNeverLandedInAClosedRingBlocksReadyToo`、`Acceptance_TheSamePackageReachesReadyOnceTheTwoMeasurementsAgree`、`ReviewPackageProgressTests`（15 項） | 通過 |
| 4 | 所有區劃均可由 Package ID 與 Zone ID 追溯 | `Acceptance_EveryElementInTheModelNamesItsPackageAndItsZone`、`Acceptance_ElementsOfOneZoneCanBeFoundAgainByThatZonesIdAlone` | 通過 |

第 3 項在 P2-T08 之前只寫日誌。本任務把它接到
`ReviewPackageProgress`：寫回結束後由它決定套件狀態，任何一個 `AreaAgreementFinding.BlocksReady`
或任何一個寫入失敗都會讓狀態停在 `BoundaryDraft` 而不是 `Ready`，理由逐條回報給使用者。

## 第 16.3 節：驗收模型情境（與 Phase 2 有關的部分）

| # | 情境 | 自動驗證 | 狀態 |
| --- | --- | --- | --- |
| 1 | 正常矩形單區劃 | `Scenario01_APlainRectangleBecomesOneZoneWithOneAreaAndItsTag` | 通過 |
| 2 | 柱造成短缺口，但在容差内可修復 | `Scenario02_AGapSmallerThanTheToleranceIsRepairedAndTheRoomStillCloses`（0.05 ft 缺口＞10 mm 吸附、＜50 mm 延伸，修復紀錄帶 `GapExtended` 與距離） | 通過 |
| 3 | 缺口超過容差，必須人工修正 | `Scenario03_AGapWiderThanTheToleranceLeavesNoClosedRegionAndSaysSoInsteadOfGuessing`（求解直接回 `geometry.regions.no-closed-loop` 失敗，不回空 map） | 通過 |
| 4 | 一個區劃含孔洞或多個 Region | `Scenario04_TwoRoomsMergedIntoOneZoneLoseTheWallBetweenThemAndKeepOneArea`、`Scenario04_TwoRoomsThatDoNotTouchNeedOneAreaEachAndSayThatBeforeTheyAreMerged`；孔洞另見 P2-T04 `RegionSolverTests` 與 P2-T06 `ApplyPreviewTests` | 通過 |
| 5 | 區劃面積剛好等於法規上限 | `Scenario05_AnAreaSittingExactlyOnALimitIsNotNudgedByTheSignatureRounding`。**讀取上限的規則屬於 Phase 3**；Phase 2 欠它的是「同一份草稿重算不會漂移」，這一條驗的就是這個 | 部分（Phase 2 範圍內通過） |
| 6 | 構件參數缺值、錯誤單位及同 Type 多 Instance | — | **Phase 3**，不在本階段 |
| 7 | 邊界牆含一般門窗、幕牆門與非 Hosted 開口 | — | **Phase 3**，不在本階段 |
| 8 | 規則版本更新後舊結果與圖紙變成 Stale | `ReviewStalenessTests.ANewerRuleSetVersionMakesTheResultStale`。規則版本本身要到 Phase 3 才有來源，判斷路徑已就緒 | 部分（判斷已實作） |
| 9 | 使用者改名或移動視埠後仍可正確更新 | `Scenario09_PanningAndZoomingTheEditorChangesNothingAboutWhatWouldBeWritten`、`Scenario09_RenamingAZoneUpdatesTheAreaAndItsTagAndLeavesTheBoundaryAlone`、`Scenario09_APackageWhoseUserRenamedTheOutputsStillFindsThemByIdentity` | 通過 |
| 10 | 重跑三次後元素數量不增加 | `Scenario10_ThreeRunsOverTwoZonesLeaveTheModelExactlyTheSizeTheFirstOneMadeIt`、`Scenario10_ThePackageDoesNotKeepAdvancingItsRevisionOnRunsThatChangedNothing` | 通過 |

## 第 13.1 節：失效條件

`ReviewStaleness.Evaluate` 逐條對照，測試見 `ReviewStalenessTests`（18 項）：

| spec 13.1 列的變更 | 偵測方式 | 測試 |
| --- | --- | --- |
| 來源視圖 | 套件記的 UniqueId 在模型裡找不到 | `AMissingSourceFloorPlanMakesTheResultStale` |
| Level | 同上，且另外比對 Area Plan 現在掛在哪一層 | `AMissingLevelOrAreaSchemeIsJustAsMuchOfAChange`、`AnAreaPlanRepointedAtAnotherLevelIsAChangeEvenThoughBothStillExist` |
| Area Boundary、Area | 元素上寫著的簽章與由它現在的幾何重算的簽章不同 | `ABoundaryThatWasDraggedMakesTheResultStale` |
| Area Scheme | 套件記的 UniqueId 不見，或 Area Plan 換了配置 | `AnAreaPlanRepointedAtAnotherAreaSchemeIsAChangeToo` |
| 規則版本 | 專案提供的版本與套件記的不同 | `ANewerRuleSetVersionMakesTheResultStale` |
| 幾何容差 | 簽章的量化格是 `GeometryTolerance.ClosureFeet`，容差一變所有簽章就變，走的是上面同一條路徑 | `AToleranceChangeArrivesAsTheSameChangedSignature` |
| 參與檢討的元素、參數 | **Phase 3**：現在沒有任何規則會讀它們 | — |
| 專案 Phase／Design Option | **未實作**，見下方限制 | — |

`ReviewStaleness` 刻意**不存指紋**。spec 13.1 要的每個基準線模型裡都已經有了——視圖靠套件上的
UniqueId 找，邊界與面積各自帶著寫入時的簽章，而簽章本身是用容差量化出來的。要加一個欄位就得換一
組 Revit Extensible Storage schema 並為所有既有套件寫遷移，換來的資訊模型本來就講得出來。

## 第 14 節：錯誤處理與日誌

`ReviewLogEntry` 帶齊 spec 14 要求的八個欄位：錯誤碼、階段、Package ID、元素 UniqueId、使用者訊
息、技術細節、處理建議、時間。測試見 `ReviewLogEntryTests`／`ReviewLogTests`（23 項）。

- **UI 與日誌分流**：`UserText` 不含技術細節，`ToLogLine()` 才有。結果視窗顯示前者，「儲存日誌」
  按鈕寫出後者。
- **路徑遮蔽**：技術細節在進入 `ReviewLogEntry` 時就把 Windows 磁碟路徑、UNC 網芳路徑與
  POSIX 路徑換成 `（已隱藏路徑）檔名`。Revit 例外常常把整個文件路徑寫在訊息裡，spec 14 明文
  禁止留存機密路徑；檔名是使用者認得出文件的部分，上面的資料夾才是會外洩客戶名稱的部分。
- **錯誤碼目錄**：`ReviewErrorCode` 依 spec 14 列的重要錯誤類型分組（`BCR-GEO-*`、`BCR-ELEM-*`、
  `BCR-PARAM-*`、`BCR-VIEW-*`、`BCR-RULE-*`、`BCR-WB-*`、`BCR-AREA-*`、`BCR-STALE-*`）。
  `BCR-PARAM-*`、`BCR-VIEW-*` 與 `BCR-RULE-*` 目前**已定義但尚無發出點**，它們對應的階段要到
  Phase 3、Phase 4 才存在。

## 尚未取得的證據（必須在 Revit 中補）

Phase 2 的自動驗證涵蓋的是「工具決定做什麼」。**Revit 是否接受這些 API 呼叫，從未實跑過。**
下列清單合併 P2-T07（18 項）、P2-T08（21 項）與 P2-T09 新增的 12 項，需開著
`建築防火檢討1.rvt`、**先備份**後依序執行：

1. 逐項執行 `docs/agent/p2-t07-area-plan-write-back.md` 的 18 項手動測試清單。
2. 逐項執行 `docs/agent/p2-t08-color-scheme-and-drafting-view.md` 的 21 項手動測試清單。
3. 逐項執行 `docs/agent/p2-t09-phase-2-integration.md` 的 12 項手動測試清單（狀態、失效、日誌）。

在這三份清單跑完之前，第 10.6 節的四條在 Revit 內的成立與否仍屬未驗證。
