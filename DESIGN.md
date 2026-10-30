# DESIGN.md — 介面與共用設計原則

這份文件規定「工具長什麼樣、怎麼被操作、哪些東西要共用」。程式分層見 [`ARCHITECTURE.md`](ARCHITECTURE.md)，
開發流程見 [`AGENT.md`](AGENT.md)。

---

## 1. 三個操作入口，一條執行路徑

```text
人  ── Ribbon「開啟檢討面板」──▶ 檢討面板（Dockable Pane）──┐
                                                          ├─▶ 工具 use case（無 UI）──▶ Application / Domain / Revit Adapter
代理 ── MCP（building-regulation-review）──▶ MCP 工具 ─────┘
```

1. **人從檢討面板操作**：所有檢討工具都從面板啟動；Ribbon 只放「開啟檢討面板」與「AI 代理 › MCP 服務」開關。
2. **代理從 MCP 操作**：每個工具都有對應的 MCP 工具（[`docs/tools/mcp-server/SPEC.md`](docs/tools/mcp-server/SPEC.md)）。
3. **兩邊走同一段 use case**：面板按鈕與 MCP 工具只是外殼，判定、寫入、結果格式都在 use case。
   代理用 MCP 驗證通過，就代表使用者按按鈕也得到相同結果。

## 2. 檢討面板設計原則

檢討面板是 Revit 的 Dockable Pane「建築技術規則檢討」（`ReviewPaneControl`），是所有檢討工具的唯一人工入口。
整合既有工具的需求與步驟見 [`docs/tools/review-panel/SPEC.md`](docs/tools/review-panel/SPEC.md)。

### 2.1 角色：目錄＋啟動器＋狀態，不是把所有視窗塞進去

- 面板負責：**找工具**（搜尋、分類）、**看說明**（法條、操作步驟、產出）、**按動作**、**看狀態**。
- 複雜的工作畫面（防火區劃編輯器、參數批次設定、檢討表）維持獨立視窗，由面板的動作按鈕開啟。
  不把大型表格或畫布硬塞進停駐面板，避免窄欄位不可用。
- 面板常駐開著，不得因為某個工具出錯而整個失效：單一工具的例外只影響該工具。

### 2.2 資料驅動：新增工具不改 XAML

- 面板上的工具清單、分類、法條、說明、操作步驟、預計產出全部來自 `Data/review-items.json`。
- 工具行為來自 `IReviewFeature`（註冊於 `ReviewFeatureRegistry`）。新增工具＝加一筆 JSON＋實作一個
  `IReviewFeature`，**不修改** `ReviewPaneControl.xaml`。
- 工具 ID 是唯一鍵，JSON、`IReviewFeature.Id`、文件資料夾 `docs/tools/<工具 ID>/`、MCP 工具前綴必須一致。
- 不得把某一個工具的操作說明寫死在 XAML（目前第 164 條的操作說明就是寫死的，整合時移到 JSON）。

### 2.3 一個工具＝一組有順序的動作

- 每個工具在面板上顯示為一個項目；選取後在下方顯示該工具的**動作清單**，依工作流程排序。
  例如防火檢討：`參數一鍵建立 → 區劃設定 → 參數批次設定 → 區劃編輯器 → 開始檢討`；
  第 164 條：`開始檢討 → 圖說製作`。
- 動作由 `IReviewFeature` 宣告（ID、名稱、說明、是否可用、不可用的原因），面板依宣告產生按鈕，
  不再只有固定的「開始檢討／圖說製作」兩顆。
- 動作不可用時按鈕停用，並以文字說明原因與先做哪一步（例如「需先完成開始檢討且結果為符合」）。
  （尚未實作，見 review-panel SPEC §7；目前由動作本身在執行時擋下並說明。）
- 動作名稱用動詞開頭、與實際結果一致（「開啟編輯器」開的就是編輯器，見 C-12 的教訓）。

### 2.4 狀態看得見

- 每個工具項目可顯示狀態：`未開始／進行中／完成／需更新／錯誤`（沿用防火檢討 spec §7 的五態）。
- 狀態由工具自己回報（讀模型或套件狀態），面板只負責顯示；面板開啟或文件切換時重新讀取。
- 「需更新」必須說明是什麼變了（模型、規則集、參數），不能只亮一個標誌。

### 2.5 不在 UI 執行緒改模型

- 面板是 modeless，按鈕事件內**不得**直接呼叫會修改文件的 Revit API。一律經
  `ReviewExternalEventDispatcher.Raise(featureId, action)` 進入 API context。
- Revit 忙碌（`Raise` 未被接受）時，告訴使用者「Revit 正在執行其他命令，請完成目前操作後再試一次」，不排隊重送。
- 動作開啟的 modeless 視窗（例如防火區劃檢討視窗）自己的 ExternalEvent 維持現狀；整合時不改它們的內部流程。

### 2.6 視覺與用字

- 沿用目前面板的視覺：底色 `#F5F6F7`、卡片白底＋`#DDE0E3` 細框＋圓角 4、強調色 `#246BCE`、次要文字 `#62666A`、
  標題 20pt SemiBold。新視窗沿用同一組色彩，不另訂主題。
- 寬度以面板最小寬度 300 px 設計，文字一律可折行，不出現水平捲軸。
- 介面文字繁體中文；法條寫法統一為「建築技術規則建築設計施工編第 N 條」。
- 元素以 Revit 元素編號（ElementId）稱呼，數值以條文單位呈現（`㎡`、`m`、`分鐘`），規則同防火檢討 spec §11.7.1。

### 2.7 結果呈現共用一套規則

- 有檢討表的工具沿用 `ReviewTable` / `ReviewTableFilter` / `ReviewEntryReport` 的呈現與篩選規則
  （防火檢討 spec §11.7.1、§11.7.2），六態結果與四個狀態帶（未符合／待確認／符合／不適用）全專案一致。
- 總狀態規則全專案一致：任何未符合 → 未符合；無未符合但有資料不足或待覆核 → 待確認；其餘才是符合。
- 判定不得在 UI 推導；UI 只顯示 use case 給的結果。

## 3. Ribbon 規則

| 面板 | 按鈕 | 說明 |
| --- | --- | --- |
| 法規檢討 | 開啟檢討面板 | 唯一的人工入口 |
| AI 代理 | MCP 服務（啟動／停止） | 保留在 Ribbon，不整合進檢討面板 |

- **不再新增任何檢討工具按鈕到 Ribbon。**
- 既有的檢討按鈕已於 2026-10-30 整合進面板並移除（見 review-panel SPEC §7）。

## 4. 共用元件目錄

開發新工具前先查這張表（[`AGENT.md`](AGENT.md) 原則 4）。能用就沿用，差一點就擴充，第二個工具也用得到的新東西要登記進來。

### 4.1 面板與 UI

| 元件 | 位置 | 用途 |
| --- | --- | --- |
| 檢討面板 | `ReviewPaneControl.xaml(.cs)`、`ReviewPaneProvider` | 所有工具的入口與動作按鈕 |
| 工具目錄資料 | `Data/review-items.json`、`ReviewItem` | 面板的項目、法條、說明 |
| 工具註冊與派送 | `Features/IReviewFeature`、`ReviewFeatureAction`、`ReviewFeatureRegistry`、`ExternalEvents/ReviewExternalEventDispatcher` | 宣告工具的有序動作；面板動作 → API context |
| 套件選擇視窗 | `RegionEditor/RegionEditorDialogs.cs` 的 `PackagePickerWindow`（文字可由建構式參數覆寫） | 選擇要處理的 Area Plan／檢討套件 |
| 文字輸入、色彩選擇 | 同上的 `TextPromptWindow`、`ZoneColorPickerWindow` | 簡單輸入對話框 |
| 檢討結果視窗 | `FireReview/FireReviewWindow.cs` | 前置檢查、進度／取消、檢討表、明細、覆寫、日誌；新工具的結果視窗以它為範本 |
| 人工覆寫對話框 | `FireReview/FireReviewOverrideDialog.cs` | 覆寫原因與註解 |
| 排版預覽 | `Article164LayoutPreviewWindow`、`Article164TitleBlockWindow` | 圖說排版、圖框與文字大小（之後出圖工具應抽成共用） |

### 4.2 Application／Domain

| 元件 | 位置 | 用途 |
| --- | --- | --- |
| 結果模型 | `Domain/Reviews/ReviewResult`、`ReviewStatus`、`ReviewEvidence`、`ReviewRun`、`ReviewOverride` | 六態結果、證據、覆寫 |
| 規則引擎 | `Domain/Rules/*`、`Application/Rules/RuleSetCompiler` 等 | 結構化、有版本的規則（JSON） |
| 檢討表與篩選 | `Application/Reviews/ReviewTable`、`ReviewTableFilter` | 統計、分組、篩選 |
| 明細與可讀文字 | `ReviewEntryReport`、`ReviewReadableText`、`ReviewLegalReference` | 給人看的明細、條文引用 |
| 前置檢查 | `ReviewReadiness`、`ReviewPreconditions` | 阻擋／提醒＋修正方式 |
| 失效判定 | `ReviewStaleness`、`ReviewRunValidity`、`ReviewBaselineBuilder` | 模型或規則變動後標示需更新 |
| 檢討視圖標示 | `ReviewMarkup`、`Revit/Reviews/RevitReviewViewMarker` | 在專用視圖塗色、標示未符合 |
| 檢討套件 | `Domain/ReviewPackages/ReviewPackage`、`IReviewPackageRepository` | 來源視圖與衍生視圖的關聯、狀態 |
| 工具產生元素的標記與命名 | `Revit/WriteBack/ManagedElementMark`、`ReviewOutputNaming` | 重跑時找回自己建立的元素，不碰使用者的 |
| 結果型別與錯誤 | `Domain/Common/Result`、`Error`、`Diagnostics/ReviewErrorCode`、`ReviewLog` | 預期內失敗、錯誤碼、日誌 |

### 4.3 MCP

| 元件 | 位置 | 用途 |
| --- | --- | --- |
| 工具基底 | `Mcp/RevitMcpTool`（`RequireProject`、`WithDryRun`） | 在 API context 執行、dryRun 回復 |
| 派送器 | `Mcp/RevitMcpDispatcher` | 單一 ExternalEvent 佇列、忙碌偵測、逾時 |
| 協定層 | `BuildingRegulationReview.Mcp`（`McpToolRegistry`、`JsonSchema`、`McpToolArguments`、`Json*`） | 不參考 Revit，可單獨測試 |

## 5. 新工具的設計檢查表

- [ ] 工具 ID 已定，JSON、`IReviewFeature`、文件資料夾、MCP 前綴一致。
- [ ] 面板項目與動作清單已定義，未改 XAML、未加 Ribbon 按鈕。
- [ ] 每個動作都有對應的 use case，且有 MCP 工具（或在 MCP SPEC 寫明暫不開放的理由與計畫）。
- [ ] 已列出沿用的共用元件；新做的共用元件已登記到 §4。
- [ ] 結果呈現遵守 §2.7；介面遵守 §2.6。
