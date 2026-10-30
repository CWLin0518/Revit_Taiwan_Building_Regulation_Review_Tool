# 檢討面板：規格

工具 ID：`review-panel`
狀態：**規格已定、未實作**。目前面板是雛形（只有第 164 條與防火區劃設定兩個項目、固定兩顆按鈕）。
實作時依 [`AGENT.md`](../../../AGENT.md) 原則 6 另開分支 `feature/review-panel-integration`。
設計原則見 [`DESIGN.md`](../../../DESIGN.md) §2、§3。

## 1. 目的

Ribbon 空間有限，工具一多一定放不下。把所有檢討工具集中到 Dockable Pane「建築技術規則檢討」，
Ribbon 只留「開啟檢討面板」與「AI 代理 › MCP 服務」開關（MCP 開關不整合進面板）。

## 2. 現況與整合對照

| 目前 Ribbon 按鈕 | Command | 整合後在面板的位置 |
| --- | --- | --- |
| 開啟檢討面板 | `ShowReviewPaneCommand` | 保留在 Ribbon |
| 第164條 道路陰影 | `Article164Command` | 工具 `article-164-road-shadow` › 動作「開始檢討」「圖說製作」 |
| 防火參數 一鍵建立 | `FireReviewParameterSetupCommand` | 工具 `fire-review` › 動作 1「參數一鍵建立」 |
| 防火區劃 設定 | `FireReviewSetupCommand` | 工具 `fire-review` › 動作 2「區劃設定」 |
| 防火參數 批次設定 | `FireReviewParameterPanelCommand` | 工具 `fire-review` › 動作 3「參數批次設定」 |
| 防火區劃 編輯器 | `RegionEditorCommand` | 工具 `fire-review` › 動作 4「開啟編輯器」 |
| 防火區劃 檢討 | `FireReviewCommand` | 工具 `fire-review` › 動作 5「開始檢討」 |
| MCP 服務 | `McpServiceCommand` | **保留在 Ribbon「AI 代理」** |

`review-items.json` 目前的 `fire-review-setup` 項目改為 `fire-review`（整個防火檢討一個項目，五個動作）。

## 3. 功能需求

| 編號 | 需求 |
| --- | --- |
| RP-1 | 面板列出所有工具，可依名稱、法條、分類、說明搜尋（沿用現有搜尋）。 |
| RP-2 | 選取工具後顯示法條、說明、操作步驟、預計產出與**動作清單**；動作依工作流程排序。 |
| RP-3 | 動作按鈕依 `IReviewFeature` 宣告產生；不可用時停用並顯示原因。 |
| RP-4 | 操作說明（目前第 164 條寫死在 XAML 的說明框）移到 `review-items.json`，各工具自己的說明。 |
| RP-5 | 每個工具顯示狀態（未開始／進行中／完成／需更新／錯誤）；面板開啟、切換文件、動作完成後重新讀取。 |
| RP-6 | 所有動作經 `ReviewExternalEventDispatcher` 執行；Revit 忙碌時提示，不排隊重送。 |
| RP-7 | 既有視窗（編輯器、批次設定、檢討視窗等）的行為不變，只是改由面板開啟。 |
| RP-8 | Ribbon 只剩「開啟檢討面板」與「MCP 服務」；移除其他檢討按鈕。 |
| RP-9 | 新增工具不需修改 `ReviewPaneControl.xaml`。 |

## 4. 非目標

- 不把編輯器畫布、批次設定表格或檢討表搬進停駐面板。
- 不改任何檢討判定或寫入行為。
- 面板本身不提供 MCP 工具（MCP 直接呼叫各工具的 use case）；但各工具的狀態查詢應可經 MCP 取得。

## 5. 實作步驟（建議）

1. 擴充 `IReviewFeature`：以動作清單（ID、名稱、說明、可用性）取代固定的 `RunReview`／`RunDrawing`；
   `ReviewFeatureAction` 改為字串動作 ID。
2. 把五個防火 Command 的流程包成 `IReviewFeature` 動作（Command 保留給過渡期使用）。
3. `review-items.json` 擴充欄位：操作步驟、動作順序；移除 XAML 寫死的說明。
4. 面板改為資料驅動的動作清單與狀態列。
5. 實機驗證全部動作後，移除 Ribbon 上的檢討按鈕。

## 6. 驗收

1. 從面板可完成第 164 條「開始檢討→圖說製作」與防火檢討五個動作，結果與原 Ribbon 按鈕相同。
2. Ribbon 只剩兩個按鈕，MCP 開關功能不變。
3. 動作不可用時有原因說明；Revit 忙碌時有提示。
4. 加一個假的測試工具（只加 JSON＋`IReviewFeature`）即出現在面板，未改 XAML。
5. 面板寬 300 px 時文字可讀、無水平捲軸。
