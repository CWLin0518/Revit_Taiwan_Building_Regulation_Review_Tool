# Revit 建築防火檢討工具 — 優化功能規格

## 1. 文件資訊

- 文件狀態：Draft v1.1（加入四階段 Task Boundary、Commit、Handoff 與 Session 切換協議）
- 目標平台：Autodesk Revit（實際支援版本由專案設定決定）
- 實作型態：Revit Add-in（C# / .NET，WPF 或等效桌面 UI）
- 主要目的：建立可追溯、可重跑、可出圖的建築防火區劃檢討流程
- 核心原則：模型資料、法規規則、檢討結果與圖說輸出分離；所有自動修改皆可預覽、確認、復原及重跑

## 2. 目標與非目標

### 2.1 目標

1. 由樓層平面視圖建立對應的 Area Plan。
2. 以牆中心線等幾何資訊輔助建立防火區劃，並寫回 Area Boundary 與 Area。
3. 依結構化法規規則檢討區劃面積、構件防火時效及區劃上的門窗。
4. 在模型視圖中標示問題、產生可讀的檢討明細，並建立圖例與圖紙。
5. 保存來源視圖與衍生視圖之間的關聯，使結果能更新而非重複建立。
6. 對每項結果保存依據、證據、時間與模型版本，支援人工覆核。

### 2.2 非目標

- 不以自由文字直接推論法規結果；規則必須先轉換為有版本的結構化規則。
- 不在未確認的情況下自動修改原始牆、柱、梁、樓板、門或窗。
- 不保證僅靠牆中心線可正確辨識所有區劃；幾何異常必須交由使用者修正或確認。
- 第一版不處理跨專案自動同步、雲端多人同時編輯與中央模型排程執行。

## 3. Revit 概念與實作修正

| 原始稱呼／需求 | 規格化定義 |
| --- | --- |
| Area Plan Type | 使用 `Area Scheme`。使用者選擇 Area Scheme 與來源 Floor Plan；系統建立相對應 Area Plan。 |
| Drafting View 的 Area Boundary | Drafting View 中建立的是 Detail Curve 複本，用於出圖或參考；真正的 Area Boundary 只寫入 Area Plan。 |
| 覆蓋 Area Boundary | 僅刪除本工具擁有且屬於目前工作包的 Boundary；不得刪除使用者或其他工具建立的線。 |
| Legend 自動表格 | 優先以可控的文字、Detail Component、Filled Region 或 Schedule/Key Schedule 組成。若目標 Revit API 無法完整控制 Legend Component，使用預先配置的 Legend Template 再更新其可編輯內容。 |
| 防火時效欄位 | 採 Shared Parameter，綁定至指定 Category 的 Type；避免只建立不可跨專案辨識的 Project Parameter。 |
| 防火門窗 bool | 建議使用可稽核的列舉文字參數（例如 `未設定／是／否／不適用`），而非只能表達 True/False 的布林值。 |

## 4. 使用者角色

- 建築設計者：建立區劃、修正幾何、執行檢討及出圖。
- 法規維護者：維護規則版本、適用條件、條文依據及優先順序。
- BIM 管理者：設定 Shared Parameter、命名規則、樣板、圖紙與權限。
- 覆核者：查看結果、證據與例外，執行人工確認或覆寫。

## 5. 整體架構

### 5.1 模組

1. **Project Setup**：版本、Area Scheme、參數、樣板與命名設定。
2. **View Package Manager**：管理來源 Floor Plan、Area Plan、Drafting View、Legend、Sheet 的一對一工作包關聯。
3. **Geometry Extraction**：取得牆中心線、柱輪廓、連接關係與視圖範圍。
4. **Region Editor**：在獨立編輯畫布顯示線網、Region、選取狀態與錯誤。
5. **Model Writer**：經使用者確認後，使用 Revit Transaction 寫入 Boundary、Area、參數與視圖元素。
6. **Rule Engine**：載入有版本的法規規則，判定適用條件、需求值與結果。
7. **Evidence Engine**：保存實際值、規定值、條文、元素 UniqueId、幾何與錯誤原因。
8. **Visualization**：建立 Filled Region、視圖 Override、文字與圖例。
9. **Documentation**：建立或更新 Legend、Sheet、Viewport 與檢討摘要。
10. **Persistence & Audit**：保存工作包、工具生成元素、檢討紀錄及人工覆寫。

### 5.2 分層

- UI 層：Wizard、檢討表、Region Editor、差異預覽與錯誤訊息。
- Application 層：四階段 Use Case、工作包狀態機、Transaction 協調。
- Domain 層：區劃、構件需求、開口需求、法規判定與檢討結果；不得依賴 Revit UI。
- Infrastructure 層：Revit API Adapter、規則資料來源、Extensible Storage／DataStorage、日誌與輸出。

## 6. 工作包資料模型

每個來源 Floor Plan 建立一個 `ReviewPackage`，其永久識別不可依賴視圖名稱。

```yaml
ReviewPackage:
  packageId: uuid
  schemaVersion: string
  sourceFloorPlanUniqueId: string
  levelUniqueId: string
  areaSchemeUniqueId: string
  areaPlanUniqueId: string?
  draftingViewUniqueId: string?
  legendViewUniqueIds: [string]
  sheetUniqueId: string?
  generatedElementUniqueIds: [string]
  boundaryRevision: integer
  ruleSetId: string?
  ruleSetVersion: string?
  lastReviewRunId: string?
  status: Setup | BoundaryDraft | Ready | Reviewed | Documented | Stale | Error
  updatedAtUtc: datetime
```

關聯資料應存於 Revit `DataStorage + Extensible Storage`；必要的 Package ID 同步寫到工具所建立元素，以支援追蹤、更新與安全刪除。外部 JSON 僅作匯出、除錯或備份，不作唯一真實來源。

## 7. UI、階段與 Agent 執行流程

主介面採四步驟導引，同時提供「專案設定」「規則版本」「執行紀錄」入口：

1. 建立視圖
2. 建立區劃範圍
3. 開始檢討
4. 製作圖說

每一步顯示 `未開始／進行中／完成／需更新／錯誤`。上游資料改變時，下游狀態必須標示為 `需更新`，不可沿用過期結果。

### 7.1 四階段執行限制

- 一次執行只允許選擇一個 Phase；不得在同一次執行自動跨越 Phase。
- Phase 內拆成多個有順序的 Task。每個新 Session 只負責一個 Task Boundary 內的工作。
- Agent 達成目前 Task 的 Exit Criteria 後，必須依序執行：驗證、更新進度、建立 Git commit、寫入 handoff、發出 `NEW_SESSION` 事件。
- Session Manager 收到事件後開啟乾淨 Session，載入 SPEC、Phase 狀態與 handoff，接續同一 Phase 的下一個 Task。
- 當 Phase 最後一個 Task 完成時，Agent 建立 Phase 完成 commit 與 handoff，發出 `PHASE_COMPLETE` 並停止。不得自行開始下一 Phase。
- 下一 Phase 必須由使用者或 Orchestrator 明確啟動。
- Task 未達 Exit Criteria、測試失敗、需求需決策或存在阻斷問題時，不得宣告 Boundary 完成或建立虛假的完成 commit；改發出 `BLOCKED`。

### 7.2 Task Boundary 定義

每個 Task 必須標示以下欄位：

| 欄位 | 說明 |
| --- | --- |
| Task ID | 穩定識別，例如 `P2-T03`。 |
| Goal | 此 Session 唯一主要目標。 |
| In Scope | 允許修改的模組、檔案與行為。 |
| Out of Scope | 明確禁止順手實作的後續功能。 |
| Inputs | 開始前必須存在的程式、設定、決策或前一份 handoff。 |
| Deliverables | 本 Task 必須產生的程式碼、測試、文件或資料。 |
| Verification | 必須執行的 build、test、lint 或 Revit 手動驗證。 |
| Exit Criteria | 可安全 commit 與交棒的客觀條件。 |
| Next Task | 同一 Phase 的下一個 Task ID；最後一項填 `PHASE_COMPLETE`。 |

禁止只以 token 數、對話長度或「看起來差不多完成」作為 Boundary。若 Task 過大，Agent可在修改程式前提出拆分，但須更新 SPEC／Phase Plan 並保存新的 Task ID，不可在執行途中私自擴大範圍。

### 7.3 Boundary Commit 協議

1. 確認目前分支與 `git status`；不得覆蓋或納入不屬於本 Task 的既有變更。
2. 僅 stage 本 Task 允許範圍內的檔案，禁止 `git add .`。
3. 執行 Task 指定的 Verification，保存命令與結果摘要。
4. 更新 `docs/agent/phase-state.yaml` 與 `docs/agent/HANDOFF.md`。
5. 建立 commit：`feat(fire-review): [P{phase}-T{task}] <summary>`；純測試、修正或文件可用 `test`、`fix`、`docs`。
6. 取得 commit SHA，補入 handoff。若因此修改 handoff，使用同一 Task 的第二個 `docs` commit；不得 amend 已由其他 Agent 取得的 commit。
7. 不自動 push、merge、rebase、reset 或切換分支，除非 Orchestrator 另有明確授權。

若工作目錄包含使用者或其他 Agent 的未提交變更且無法安全分離，發出 `BLOCKED_DIRTY_WORKTREE`，不得把那些變更包入本次 commit。

### 7.4 Handoff 格式

`docs/agent/HANDOFF.md` 每次覆寫為最新交棒內容，Git 歷史保留先前版本：

```markdown
# Agent Handoff
- Phase: P2
- Completed Task: P2-T03
- Next Task: P2-T04
- Status: READY_FOR_NEW_SESSION
- Commit: <sha>
- Spec Version: <version or commit>

## Completed
## Changed Files
## Decisions and Assumptions
## Verification Results
## Known Issues / Risks
## Exact Next Steps
## Do Not Do
```

`docs/agent/phase-state.yaml` 為機器可讀狀態：

```yaml
workflow: fire-review-addon
activePhase: P2
completedTask: P2-T03
nextTask: P2-T04
status: ready_for_new_session
commit: <sha>
updatedAtUtc: <iso-8601>
```

### 7.5 Session Manager 介面

Agent 完成 commit 與 handoff 後，輸出單一 JSON 事件；外層 CLI Hook、Agent-HQ 或其他 Orchestrator 負責真的建立新 Session：

```json
{
  "event": "NEW_SESSION",
  "phase": "P2",
  "completedTask": "P2-T03",
  "nextTask": "P2-T04",
  "commit": "<sha>",
  "handoffPath": "docs/agent/HANDOFF.md"
}
```

新 Session 的啟動內容只需包含：system/developer instructions、目前 SPEC、`phase-state.yaml`、`HANDOFF.md`、指定 Phase／Task，以及必要程式碼。不得把完整舊對話當成主要交接方式。

若下一項是 `PHASE_COMPLETE`，事件改為：

```json
{
  "event": "PHASE_COMPLETE",
  "phase": "P2",
  "commit": "<sha>",
  "handoffPath": "docs/agent/HANDOFF.md"
}
```

Session Manager 此時不得建立下一階段 Session。

### 7.6 新 Session 啟動檢查

新 Agent 必須在修改前完成：

1. 讀取完整 SPEC 中的共通規則、目前 Phase 與指定 Task。
2. 讀取 `phase-state.yaml` 與 `HANDOFF.md`。
3. 確認目前 `HEAD` 等於 handoff 記載的 commit，或解釋可接受的差異。
4. 檢查前一 Task 的 Deliverables 與 Verification 證據確實存在。
5. 僅執行 `nextTask`；不得重做已完成 Task 或提前進入後續 Task。

## 8. 共通前置設定（納入 Phase 1）

### 8.1 設定項目

- Revit 支援版本與專案單位。
- Area Scheme。
- 防火時效 Shared Parameter Definition、名稱、GUID、Category Binding 與資料型態。
- 防火開口屬性 Shared Parameter Definition，建議值為 `未設定／是／否／不適用`。
- 規則集與版本。
- Area Color Scheme 名稱及配色策略。
- Area Boundary、問題標註、圖例、圖紙及視埠的命名規則。
- Blank Legend、Title Block、View Template 等樣板。
- 幾何容差：端點吸附、延伸上限、最短線段、最小 Region 面積。

### 8.2 參數建立

「檢查／建立參數」先產生差異預覽，列出缺少的 Definition、Binding、Category、型別與 GUID 衝突。經確認後才建立或補綁。既有同名但 GUID 或型別不符時禁止直接覆蓋，顯示修正指引。

## 9. Phase 1：建立視圖

### 9.0 Phase 1 任務邊界

| Task ID | Goal | In Scope / Deliverables | Verification / Exit Criteria | Next |
| --- | --- | --- | --- | --- |
| P1-T01 | 建立外掛骨架與共通 Domain Contract | Solution、Revit 版本適配層、DI／模組邊界、Result/Error 型別；不實作視圖建立 | Solution 可 build，核心測試可執行，架構決策有文件 | P1-T02 |
| P1-T02 | 建立專案設定與 Shared Parameter 檢查 | 第 8 節設定模型、GUID／Binding 驗證、差異預覽；不實際建立 Area Plan | 單元測試涵蓋缺參數、同名異 GUID、錯誤型別；Revit 測試模型能完成 dry-run | P1-T03 |
| P1-T03 | 建立 ReviewPackage 持久化 | DataStorage + Extensible Storage Schema、Repository、schemaVersion 與遷移測試 | 建立、讀取、更新後 ID 穩定；重新開啟模型仍可讀 | P1-T04 |
| P1-T04 | 實作來源視圖與 Area Scheme 選擇 UI | Floor Plan 篩選、多選、Area Scheme、Template、Crop 策略、輸入驗證 | UI 狀態與輸入驗證測試完成，不建立模型元素 | P1-T05 |
| P1-T05 | 實作 Area Plan 建立／更新服務 | 重複偵測、Area Plan 建立、可寫入 Crop／Scope Box／Template 設定、Transaction | Revit 整合測試證明重跑不新增重複視圖，失敗可 Rollback | P1-T06 |
| P1-T06 | 整合 Phase 1、驗收與使用說明 | 串接 UI、服務、進度、錯誤與日誌；完成第 9.4 節驗收 | Phase 1 驗收全數通過，建立完成 handoff；不得開始 Phase 2 | PHASE_COMPLETE |

### 9.1 輸入

- 一個或多個非 Template 的 Floor Plan。
- Area Scheme。
- Area Plan View Template（選填）。
- Crop 設定：複製來源／使用 Template／不設定。

### 9.2 流程

1. 檢查來源視圖、Level 與 Area Scheme。
2. 搜尋是否已有相同 `sourceFloorPlan + areaScheme` 的工作包。
3. 若已有 Area Plan，提供「開啟」「更新設定」；不得再建立重複視圖。
4. 建立 Area Plan，套用 View Template，複製可寫入的 CropBox、CropBoxActive、CropBoxVisible 與 Scope Box 設定。
5. 建立並保存 ReviewPackage。

### 9.3 Area and Volume Computations

按鈕開啟 Revit 原生設定頁（若 API／版本允許）；否則導引使用者至正確位置，不模擬未公開 API 操作。設定值在執行面積檢討前須再次驗證。

### 9.4 驗收

- 每個來源視圖與 Area Scheme 最多一個受管理的 Area Plan。
- 重新執行不產生重複視圖。
- 無法複製的視圖設定會列入警告，不造成整批失敗。

## 10. Phase 2：建立區劃範圍

### 10.0 Phase 2 任務邊界

| Task ID | Goal | In Scope / Deliverables | Verification / Exit Criteria | Next |
| --- | --- | --- | --- | --- |
| P2-T01 | 定義純資料 2D 幾何模型與擷取介面 | Segment、Loop、Region、SourceRef、座標與容差 Contract；不做 UI／Revit 寫回 | 幾何資料可序列化，單位與座標轉換測試通過 | P2-T02 |
| P2-T02 | 實作 Revit 幾何擷取 Adapter | 牆 Location Curve、柱輪廓、Crop／Scope、來源 UniqueId、選用 Link 讀取策略 | 固定測試模型輸出穩定，可追溯來源元素 | P2-T03 |
| P2-T03 | 實作線網正規化與修復 | 去重、交點分割、吸附、受限延伸、共線合併、修復紀錄 | 正常、短缺口、超容差、自交案例測試通過 | P2-T04 |
| P2-T04 | 實作閉環與 Region 求解 | 閉環、孔洞、MultiPolygon、鄰接關係、草算面積 | 幾何 golden tests 通過；歧義案例輸出錯誤而非猜測 | P2-T05 |
| P2-T05 | 實作 Region Editor 基礎互動 | 顯示、縮放、選取、左右鍵增減、建立／刪除、名稱、顏色 | UI 測試或可重現手動測試清單通過 | P2-T06 |
| P2-T06 | 完成 Editor 狀態與差異預覽 | Undo/Redo、唯一歸屬、非連通警告、未套用提示、Add/Update/Delete Preview | 狀態回放穩定，差異只涵蓋目前 Package | P2-T07 |
| P2-T07 | 實作 Area Plan 寫回 | 工具擁有權標記、Boundary／Area 冪等建立更新、安全刪除、TransactionGroup | 三次重跑元素數量不增加；不刪除人工元素；失敗 Rollback | P2-T08 |
| P2-T08 | 實作 Color Scheme 與 Drafting View 輸出 | 可用 API 路徑、Template fallback、Detail Curve 複本與命名更新 | 重跑不重複，API 不支援時產生明確人工處理項 | P2-T09 |
| P2-T09 | 整合 Phase 2 與驗收 | 串接抽取、編輯、預覽、寫回、狀態失效與日誌 | 第 10.6 節及相關驗收模型全數通過；不得開始 Phase 3 | PHASE_COMPLETE |

### 10.1 幾何提取

預設範圍為所選 Area Plan 的 Level、Crop／Scope Box 與可見模型。可設定納入：

- Wall Location Curve（中心線、核心中心線或指定策略）。
- 柱的平面 Bounding/輪廓，用於判斷遮斷與補線。
- Linked Model（第一版可設為唯讀參考，寫入仍發生於 Host）。
- 使用者指定的防火區劃輔助線。

幾何處理須先轉換為一致的 2D 平面座標，並保留來源 Element UniqueId。

### 10.2 線網修復

依序執行：去除重複線、分割交點、端點吸附、短缺口延伸、共線合併、閉環偵測。不得無限制自動 Fillet。每個修復需保存原始幾何、修復方式與距離；超出容差者標示為錯誤並交由使用者處理。

### 10.3 Region Editor

- 平移、縮放、框選、顯示來源元素。
- 左鍵加入 Region，右鍵移除 Region。
- 支援建立、選取、重新命名、改色、刪除區劃草稿。
- 一個 Region 同一時間只能屬於一個區劃。
- 合併後區劃允許為 MultiPolygon，但需顯示是否連通；預設禁止不連通區塊合併，除非使用者明確確認。
- 顯示區劃名稱、草算面積、孔洞與未閉合錯誤。
- 支援 Undo/Redo；離開前提示未套用變更。

### 10.4 套用前差異預覽

列出將新增、更新、刪除的 Area Boundary、Area、Detail Curve 與標註。刪除範圍只限相同 Package ID 的工具生成元素。

### 10.5 寫回 Revit

1. 在 Area Plan 建立／更新 Area Boundary Line。
2. 在各閉合區劃內建立／更新 Area，寫入區劃 ID、名稱及必要分類參數。
3. 更新或建立 Area Color Scheme Entry；若 API 版本不支援完整編輯，套用預先配置方案並列出需人工處理項。
4. 建立／更新 Drafting View，以 Detail Curve 複製區劃單線圖。
5. Drafting View 命名預設為 `{AreaScheme}_{SourceFloorPlan}_防火區劃`，並用唯一識別防止名稱變更造成失聯。

所有寫入使用 `TransactionGroup`。發生致命錯誤時整組 Rollback；局部錯誤可由使用者選擇略過，但須寫入日誌。

### 10.6 驗收

- 同一份草稿重複套用不會累加線、Area 或 Drafting View。
- 工具不刪除未受管理的元素。
- 面積與 Revit Area 的差異超過容許值時，禁止進入 Ready，並指出可能的邊界問題。
- 所有區劃均可由 Package ID 與 Zone ID 追溯。

## 11. Phase 3：開始檢討

### 11.0 Phase 3 任務邊界

| Task ID | Goal | In Scope / Deliverables | Verification / Exit Criteria | Next |
| --- | --- | --- | --- | --- |
| P3-T01 | 定義規則與結果 Schema | Rule、RuleSet、ReviewRun、ReviewResult、六態狀態、schema 驗證 | 有效／無效規則 fixture 與序列化測試通過 | P3-T02 |
| P3-T02 | 實作受限規則引擎 | 白名單欄位、Expression DSL、優先序、豁免、單位與衝突偵測 | 禁止任意程式碼；邊界值、衝突、缺資料測試通過 | P3-T03 |
| P3-T03 | 實作候選元素與空間關係解析 | 區劃、邊界牆、相交構件、Hosted 開口、來源證據 | 固定模型的候選集合可重現；歧義回傳 ManualReview | P3-T04 |
| P3-T04 | 實作區劃面積檢討 | 適用條件、Revit Area、幾何交叉驗證、要求值與證據 | 等於上限、超限、缺輸入與豁免測試通過 | P3-T05 |
| P3-T05 | 實作構件防火時效檢討 | Required／Provided 分離、Type 彙總、缺值與格式錯誤 | 各 Category 與邊界值測試通過，不覆寫 Provided 值 | P3-T06 |
| P3-T06 | 實作門窗防火保護檢討 | 適用性、Hosted Door/Window；幕牆／非 Hosted 依 MVP 政策 | 是／否／未設定／不適用結果與證據正確 | P3-T07 |
| P3-T07 | 實作結果持久化與失效 | Run ID、規則版本、元素證據、模型變更 Stale、人工覆寫稽核 | 重開模型可讀；模型／規則變更能使結果失效 | P3-T08 |
| P3-T08 | 實作視圖標示與檢討表 | 專用檢討 View、Filled Region、Override、定位、條文與統計 | 只更新目前 Run 管理元素，六態彙總規則正確 | P3-T09 |
| P3-T09 | 整合 Phase 3 與驗收 | 前置檢查、三類檢討、取消／Rollback、日誌與效能 | 第 11 節及相關驗收模型全數通過；不得開始 Phase 4 | PHASE_COMPLETE |

### 11.1 前置檢查

- 工作包為 Ready，且 Area／Boundary 未過期。
- 規則集存在且版本已鎖定。
- Area Scheme、專案單位、Phase、Design Option、Link 狀態符合設定。
- 必要參數存在且可讀。
- 來源元素與區劃空間關係可解析。

任一關鍵條件不成立時停止檢討並列出修正方式。

### 11.2 規則資料模型

```yaml
Rule:
  ruleId: string
  version: string
  category: CompartmentArea | FireResistance | OpeningProtection
  legalReference: string
  effectiveDate: date
  jurisdiction: string
  priority: integer
  appliesWhen: expression
  requiredValue: expression
  exemptions: [expression]
  evidenceFields: [string]
  severity: Error | Warning | Info
```

規則運算採白名單欄位與受限 Expression DSL，不執行任意程式碼。所有單位進入 Domain 層前轉成明確單位值。

### 11.3 結果模型

每個檢查項目結果為：`未檢討／符合／未符合／資料不足／不適用／人工覆核`，不可將資料不足誤判為未符合。

```yaml
ReviewResult:
  resultId: uuid
  runId: uuid
  packageId: uuid
  checkType: string
  subjectUniqueIds: [string]
  zoneId: string?
  status: NotRun | Pass | Fail | InsufficientData | NotApplicable | ManualReview
  actualValue: object?
  requiredValue: object?
  ruleId: string
  ruleVersion: string
  legalReference: string
  message: string
  evidence: object
  reviewedBy: string?
  reviewedAtUtc: datetime?
```

### 11.4 防火區劃面積

1. 依區劃用途、樓層、構造、灑水設備等規則輸入判斷適用規定。
2. 取得 Revit Area 作為主要實際值，幾何草算作為交叉驗證。
3. 比較實際值與規定上限，保存計算過程與條文。
4. 未符合區劃於檢討用 View 建立紅色 Filled Region；標註元素須含 Package ID、Run ID、Zone ID。
5. 符合、未符合、資料不足均顯示於檢討表。

### 11.5 柱、梁、牆、樓板防火時效

1. 取得與區劃相交、構成邊界或依規則需檢查的構件集合；不同類別的空間關係策略須可設定。
2. 由規則引擎算出「要求防火時效」。
3. 讀取所選 Type Parameter 作為「設計／認證防火時效」。規則要求值不得直接覆寫設計值。
4. 若需要寫回計算結果，使用獨立參數，例如 `防火檢討_法規要求防火時效`；原設計值使用 `防火檢討_設計防火時效`。
5. `Provided >= Required` 為符合；缺值、格式錯誤或複合構造無法判定為資料不足／人工覆核。
6. 未符合元素在專用檢討 View 使用 By Element Override 紅色標示；保存原視圖狀態與本工具覆寫的元素集合。
7. 生成材料／牆體防火時效圖例。圖例內容按 Type 彙總，不為每個 Instance 重複列出。

### 11.6 防火門窗

1. 以區劃邊界牆及其 Hosted Door/Window 為主要候選；對非 Hosted、幕牆嵌板、Link 元素與幾何相交案例採獨立策略。
2. 先判斷該開口是否依法需要防火保護，再檢查 `ProvidedFireProtection`，不得只要位於邊界就一律要求 True。
3. `是` 且滿足要求為符合；`否` 為未符合；`未設定` 為資料不足；`不適用` 仍須保存適用性證據。
   `ProvidedFireProtection` 來源為門／窗／帷幕嵌板**類型**上的是非（YESNO）共享參數
   `防火檢討_設計防火保護`——是不是防火門窗是型號的性質，勾一次即代表該型號的所有實體。
   Revit 的是非參數在介面上無法區分「未勾選」與「從未設定」，故此項的「未設定」專指
   **參數未綁定到該類別**；已綁定而未勾選視為 `否`（未符合）。若不如此，任何模型都無法表達
   「這個型號不是防火門窗」，所有開口將永遠停在待確認。此為第 11.3 節「不可將資料不足誤判為
   未符合」在是非參數上的界定，不是放寬：未綁定參數仍然是資料不足。
4. 未符合元素在專用檢討 View 以紅色 Override 標示。

### 11.7 檢討表

| 項目 | 狀態 | 統計 | 操作 |
| --- | --- | --- | --- |
| 防火區劃面積 | 六態結果 | Pass／Fail／Unknown 數量 | 展開、定位、顯示條文 |
| 構件防火時效 | 六態結果 | 依類別與 Type 統計 | 展開、選取元素、人工覆核 |
| 防火門窗 | 六態結果 | 依門／窗／幕牆統計 | 展開、選取元素、人工覆核 |

總狀態規則：任何 Fail 則為未符合；無 Fail 但有資料不足或人工覆核則為待確認；其餘檢查皆 Pass／NotApplicable 才可顯示符合。

### 11.8 人工覆寫

人工覆寫需輸入原因，可附註解；保存操作者、時間、原始結果與覆寫結果。模型或規則版本變更後，覆寫不得無條件沿用，狀態改為需重新確認。

## 12. Phase 4：製作圖說

### 12.0 Phase 4 任務邊界

| Task ID | Goal | In Scope / Deliverables | Verification / Exit Criteria | Next |
| --- | --- | --- | --- | --- |
| P4-T01 | 定義圖說資料模型與版面配置 | 摘要 Row、Legend Model、Sheet Layout、Viewport Slot、樣板設定 | 設定 schema 與版面碰撞測試通過 | P4-T02 |
| P4-T02 | 實作區劃檢討摘要 | 名稱、實際值、規定值、狀態、條文、單位與精度 | 六態輸出完整，不產生只有 `ok` 的不可追溯文字 | P4-T03 |
| P4-T03 | 實作防火時效與狀態圖例 | Type 彙總、色彩說明、規則版本、時間；Legend Template fallback | 重跑更新既有內容，不重複建立；fallback 有明確警告 | P4-T04 |
| P4-T04 | 實作 Sheet 與 Viewport 編排 | Title Block、視圖放置、座標、碰撞、保留人工位置、重新排版選項 | 重跑不新增重複 Sheet；碰撞／不可放置錯誤可操作 | P4-T05 |
| P4-T05 | 實作過期防護與批次更新 | Stale 阻擋、Package 選擇、預覽、進度、取消與結果摘要 | 過期結果不能成正式圖說；批次局部失敗可追蹤 | P4-T06 |
| P4-T06 | Phase 4 全流程與最終驗收 | 完成圖說、文件、安裝／操作說明、完整回歸與效能測試 | 第 12.4、16、18 節全數通過，建立最終 handoff | PHASE_COMPLETE |

### 12.1 區劃檢討摘要

每一區劃至少顯示：

`{區劃名稱}：{實際面積} ≤ {規定面積} — {結果}（{條文}）`

若不符合、資料不足或人工覆核，顯示明確原因，不使用只有 `...ok` 的自由文字。面積採專案單位並統一精度。

### 12.2 圖例

- 區劃面積檢討摘要。
- 牆／樓板／柱／梁 Type 與防火時效對照。
- 圖面顏色與狀態說明。
- 法規版本、檢討時間及執行狀態。

### 12.3 Sheet 組裝

一個 ReviewPackage 預設對應一張 Sheet，內容可由配置檔定義：

- Area Plan 或專用檢討平面圖。
- Drafting View 區劃單線圖（選填）。
- 一個或多個 Legend／Schedule。
- Title Block 與自訂圖紙參數。

建立前顯示視埠位置預覽與碰撞檢查。既有 Sheet 預設更新受管理內容，而非重新建立。若人工移動過 Viewport，更新時應保留位置，除非選擇「重新排版」。

### 12.4 驗收

- 每個有效工作包可產生或更新一張 Sheet。
- 視圖不可放置、已在其他圖紙、超出邊界或互相重疊時顯示可操作的錯誤。
- 圖紙標示的規則版本與 ReviewRun 完全一致。
- 過期結果不得標示為正式完成圖說。

## 13. 更新、失效與冪等性

### 13.1 失效條件

以下任一變更使相關結果成為 Stale：來源視圖／Level、Area Boundary、Area、參與檢討的元素、參數、Area Scheme、規則版本、專案 Phase／Design Option 或幾何容差。

### 13.2 更新策略

- 以 UniqueId + Package ID + Zone ID／Run ID 尋找既有物件。
- 更新既有受管理元素；不存在才新增。
- 只刪除已確認屬於該工作包且已不再需要的工具生成元素。
- 所有批次操作支援取消、進度回報與 TransactionGroup Rollback。

## 14. 錯誤處理與日誌

錯誤至少包含：錯誤碼、階段、Package ID、元素 UniqueId、使用者訊息、技術細節、處理建議與時間。UI 顯示使用者可理解的訊息，技術堆疊僅寫入本機日誌。不得在錯誤紀錄中保存機密路徑或憑證。

重要錯誤類型：

- 幾何未閉合、容差修復失敗、重疊或自交。
- 元素位於 Group、Design Option、Link 或不可編輯 Workset。
- 參數缺失、GUID 衝突、資料型態錯誤或唯讀。
- 視圖／樣板不相容、名稱衝突或無法放置。
- 規則缺失、條件衝突、運算錯誤或版本不一致。

## 15. 效能與可用性需求

- 不在 Revit API Context 外存取 `Document`、`Element` 等 API 物件；背景執行僅處理已轉換的純資料。
- 長任務分段處理並回報進度，允許在安全點取消。
- 幾何快取鍵至少包含 Document、Element UniqueId、Geometry 版本與設定雜湊。
- 目標效能（基準模型需另定）：單樓層 5,000 個候選元素，前置掃描 10 秒內、一般檢討 30 秒內；超標須輸出效能診斷。
- 所有自動修改前提供範圍摘要；完成後提供新增／更新／刪除／略過數量。

## 16. 測試策略

### 16.1 單元測試

- 規則適用條件、單位換算、上下限與豁免。
- 線段吸附、交點分割、閉環、孔洞與 MultiPolygon。
- 結果彙總、失效判斷及人工覆寫。
- 工作包命名、識別與冪等更新。

### 16.2 Revit 整合測試

- 建立／更新 Area Plan、Area Boundary、Area、Drafting View、Legend、Sheet。
- Shared Parameter Binding 與不同 Category。
- Hosted Door/Window、Curtain Panel、Link、Group、Design Option、Worksharing。
- Undo、Transaction Rollback、重複執行與刪除後修復。

### 16.3 驗收模型情境

1. 正常矩形單區劃。
2. 柱造成短缺口，但在容差内可修復。
3. 缺口超過容差，必須人工修正。
4. 一個區劃含孔洞或多個 Region。
5. 區劃面積剛好等於法規上限。
6. 構件參數缺值、錯誤單位及同 Type 多 Instance。
7. 邊界牆含一般門窗、幕牆門與非 Hosted 開口。
8. 規則版本更新後舊結果與圖紙變成 Stale。
9. 使用者改名或移動視埠後仍可正確更新。
10. 重跑三次後元素數量不增加。

## 17. MVP 與後續版本

### 17.1 MVP

- 單一 Host Model、單一 Area Scheme。
- Floor Plan → Area Plan 工作包。
- 牆 Location Curve 提取、受限容差修復、Region Editor。
- Area Boundary／Area 寫回與 Detail Curve Drafting View。
- 結構化區劃面積規則與結果證據。
- 基本牆／樓板／柱／梁時效參數檢查。
- Hosted Door／Window 防火屬性檢查。
- 專用檢討 View 標示、摘要 Legend 與單張 Sheet 更新。
- DataStorage 持久化、冪等更新與日誌。

### 17.2 V1.1

- Linked Model、幕牆開口、Design Option、複雜 Worksharing。
- 更完整 Legend Template 與 Schedule 產生。
- 規則編輯器、規則衝突檢查與簽核。
- 多樓層批次檢討與報表匯出。

### 17.3 V2

- MCP／Agent 產生候選規則或 SPEC，但正式執行前需 schema 驗證與人工核准。
- 中央法規資料庫、規則發佈與版本治理。
- 跨專案儀表板及差異比較。

## 18. 完成定義（Definition of Done）

功能只有在以下條件均成立時才視為完成：

1. 通過單元、整合及上述驗收情境。
2. 相同輸入重跑不產生重複元素。
3. 所有修改可透過 Revit Undo 或 Transaction Rollback 安全復原。
4. 每項判定能追溯到規則版本、條文、實際值、規定值與元素。
5. 資料不足不會被誤標為未符合或符合。
6. 工具不刪除、覆寫非本工具擁有的模型／視圖元素。
7. 模型或規則變更後，過期結果會被辨識且不會誤用於正式圖說。
8. 安裝、Shared Parameter、樣板與使用流程均有文件。
9. 每個 Task Boundary 均有獨立驗證紀錄、範圍受控的 Git commit 與符合格式的 handoff。
10. Session Manager 能依 `NEW_SESSION` 事件只接續同一 Phase 的下一個 Task，並在 `PHASE_COMPLETE` 停止。
11. 新 Session 不依賴舊對話即可從 SPEC、Phase State、Handoff 與 Repository 重建必要上下文。

## 19. 開發前待確認事項

1. 支援的 Revit 年份，以及是否需同時支援多版本。
2. 採用的建築技術規則條文、適用地區、版本與生效日期。
3. Area Scheme、Area Color Scheme、View Template、Legend Template、Title Block 的公司標準。
4. 防火時效與門窗防火屬性的既有 Shared Parameter GUID。（`防火檢討_遮煙性能`（YESNO，GUID `…000f`，門／窗／帷幕嵌板類型）已定義在 `assets/SharedParameters/`：第 79 條之 2 對昇降機道防火設備與管道間維修門要求的遮煙性能（第 1 條第 45 款）沒有既有參數可借用，見 [垂直區劃](regulations/vertical-compartment.md) §6。同一輪起 `防火檢討_設計防火時效` 也要綁到**門**，管道間維修門的一小時時效由它提供。公司既有 GUID 若與此不同仍待確認。）
5. 防火時效提供值的單位與資料型態（分鐘、文字代碼或列舉）。
6. 區劃面積規則所需的用途、構造、樓層、灑水設備等輸入來源。（用途的**用字**已議定其中會改變判定的部分——第 79 條之 2 第 1 項的五種垂直區劃，見 [`zone.use` 用字表](regulations/zone-use-vocabulary.md)；其餘用途仍為自由文字。那五種用字同時決定一個區劃要受第 79 條之 2 的哪些附加要求，見 [垂直區劃](regulations/vertical-compartment.md) §3.2。建築物用途類組（`building.use`）另有正規形與寫法折疊，第 83 條的Ｈ－２組但書據以比對，見 [`building.use` 寫法正規化](regulations/building-use-groups.md)。第 79 條之 1 另議定六個用字（觀眾席、生產線、教室、體育館、零售市場、停車空間），它們**不**豁免面積、只產生人工覆核，且其中三個要併看 `building.use`，見 [第 79 條之 1 面積免除](regulations/article-79-1-area-exemption.md)。來源本身仍待確認。）
7. 防火區劃邊界的實際判定方式，以及柱、梁、板是否依相交、邊界或服務區域納入。
8. Legend 自動化在目標 Revit API 版本的限制與可接受的樣板替代方案。
9. 是否納入 Linked Model、Design Option、Phase、Group 與 Worksharing。
10. Sheet 版型、視埠座標、圖號規則及重複視圖的處理政策。
11. 實際負責開啟新 Session 的 CLI Hook／Agent-HQ／Orchestrator，以及其事件接收與啟動命令。
12. Repository 的分支策略、允許 Agent commit 的分支，以及是否由人員負責 push／PR／merge。

