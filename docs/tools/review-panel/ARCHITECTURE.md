# 檢討面板：架構

規格見 [SPEC.md](SPEC.md)。本文件描述現況與整合後的目標結構。

## 1. 現況

| 類別 | 位置 | 責任 |
| --- | --- | --- |
| `App` | `src/BuildingRegulationReview/App.cs` | 建 Ribbon、建 `ReviewFeatureRegistry` 與派送器、註冊 Dockable Pane |
| `ShowReviewPaneCommand` | 同目錄 | 顯示面板 |
| `ReviewPaneProvider` | 同目錄 | 提供面板內容 |
| `ReviewPaneControl.xaml(.cs)` | 同目錄 | 搜尋、清單、明細、「開始檢討／圖說製作」兩顆按鈕 |
| `ReviewItem` | 同目錄 | `review-items.json` 的資料模型 |
| `IReviewFeature`、`ReviewFeatureRegistry` | `Features/` | 工具註冊；目前只有 `RunReview`／`RunDrawing` |
| `ReviewExternalEventDispatcher` | `ExternalEvents/` | 單一 ExternalEvent，一次只接受一個請求 |

註冊的工具：`Article164ReviewFeature`、`FireReviewSetupFeature`。其餘防火功能只在 Ribbon。

## 2. 目標結構

```text
review-items.json ──▶ ReviewItem（說明、步驟、動作順序）
                              │
ReviewPaneControl ──選取──▶ IReviewFeature
    │                         ├─ Actions: IReadOnlyList<ReviewFeatureActionInfo>（Id、名稱、說明）
    │                         ├─ GetStatus(UIApplication) → 五態＋原因
    │                         └─ Run(actionId, UIApplication, ref message)
    │  按鈕
    ▼
ReviewExternalEventDispatcher.Raise(featureId, actionId) ──▶ API context ──▶ 工具 use case／既有視窗
```

- `ReviewFeatureAction` 列舉改為字串動作 ID，派送器的單一請求模型不變。
- 開啟 modeless 視窗的動作（區劃設定、編輯器、檢討）仍由視窗自己的 ExternalEvent 處理後續操作。
- 狀態讀取在 API context 執行（例如讀 `ReviewPackage` 狀態），結果交回 UI 執行緒顯示。

## 3. 相依

- 共用元件：見 [`DESIGN.md`](../../../DESIGN.md) §4.1。
- MCP：面板不直接對應 MCP 工具；各工具的 MCP 工具與面板動作呼叫同一 use case。
