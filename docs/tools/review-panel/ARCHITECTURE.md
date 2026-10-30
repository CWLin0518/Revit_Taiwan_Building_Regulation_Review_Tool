# 檢討面板：架構

規格見 [SPEC.md](SPEC.md)。本文件描述目前的實作。

## 1. 類別

| 類別 | 位置 | 責任 |
| --- | --- | --- |
| `App` | `src/BuildingRegulationReview/App.cs` | Ribbon（開啟檢討面板、MCP 開關）、建立註冊表與派送器、註冊 Dockable Pane |
| `ShowReviewPaneCommand`、`ReviewPaneProvider` | 同目錄 | 顯示面板、提供面板內容 |
| `ReviewPaneControl.xaml(.cs)` | 同目錄 | 搜尋、工具清單、明細（說明、操作步驟、預計產出）、依動作清單產生按鈕 |
| `ReviewItem` | 同目錄 | `Data/review-items.json` 的資料模型（含 `Steps`） |
| `IReviewFeature` | `Features/` | 工具：`Id` 與有序的 `Actions` |
| `ReviewFeatureAction`、`ReviewActionRunner` | `Features/` | 動作：Id、名稱、說明、對話框標題、執行委派（與 `IExternalCommand` 同形） |
| `ReviewFeatureRegistry` | `Features/` | 驗證工具與動作 ID 不重複、`TryGetAction` |
| `Article164ReviewFeature` | `Features/Article164/` | 開始檢討、圖說製作 |
| `FireReviewFeature` | `Features/FireReview/` | 參數一鍵建立、防火區劃設定、參數批次設定、開啟防火區劃編輯器、開始檢討 |
| `ReviewExternalEventDispatcher` | `ExternalEvents/` | 單一 ExternalEvent，以（工具 ID, 動作 ID）派送；一次一個請求；例外寫 Trace 並回報 |

## 2. 流程

```text
review-items.json ──▶ ReviewItem ──▶ 清單／明細
                                       │ 選取
ReviewFeatureRegistry.TryGet(id) ──▶ IReviewFeature.Actions ──▶ 按鈕（Tag = 動作 ID）
                                       │ 按下
ReviewExternalEventDispatcher.Raise(featureId, actionId)
                                       │ Revit API context
ReviewFeatureAction.Run(UIApplication, ref message) ──▶ 各工具的 Command.Run（與 Ribbon 同一個進入點）
```

- 開啟 modeless 視窗的動作（防火區劃設定、編輯器、檢討）由視窗自己的 ExternalEvent 處理後續操作。
- 新增工具：`review-items.json` 加一筆＋實作 `IReviewFeature`＋在 `App.OnStartup` 註冊，不改 XAML。

## 3. 相依

- 共用元件：見 [`DESIGN.md`](../../../DESIGN.md) §4.1。
- MCP：面板不直接對應 MCP 工具；各工具的 MCP 工具與面板動作呼叫同一 use case。
