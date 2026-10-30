# 第 164 條道路陰影檢討 — 架構

規格見 [SPEC.md](SPEC.md)。程式位於 `src/BuildingRegulationReview/Article164*.cs`，入口為 `Features/Article164/Article164ReviewFeature.cs`（面板）與 `Article164Command`（Ribbon，整合後移除）。

> 註：下表中的 `Article164ExternalEventHandler`、`Article164DrawingExternalEventHandler` 已由共用的 `ReviewExternalEventDispatcher` 取代。

## 1. 類別與責任

| 類別 | 責任 |
| --- | --- |
| `Article164Command` | 主流程、元素收集、使用者選取、投影計算與合規判定 |
| `Article164ExternalEventHandler` | 從 Dockable Pane 進入第 164 條檢討 |
| `Article164ElementFilterWindow` | 依牆／樓板類型篩選輸入 |
| `Article164OptionsWindow` | 道路寬度與永久性空地輸入 |
| `Article164ReviewSession` | 依 Revit Document 暫存最近一次結果 |
| `Article164PlanViewBuilder` | 建立檢討平面圖與標註 |
| `Article164FilledRegionWriter` | 建立或取得填滿區域類型與樣式 |
| `Article164DrawingCommand` | 驗證結果並協調圖說流程 |
| `Article164DrawingExternalEventHandler` | 從 Dockable Pane 進入圖說製作 |
| `Article164TitleBlockWindow` | 選擇圖框與圖例文字大小 |
| `Article164LayoutPreviewWindow` | 預覽並調整圖面配置 |
| `Article164DrawingBuilder` | 建立 Sheet、圖例、文字與 Viewport |

## 2. 結果資料與生命週期

`Article164ReviewSession.Result` 保存：

- 合規結果。
- 建物與陰影 Rhino 曲線。
- 建築線端點與道路寬度。
- 面積與容許面積。
- Level、來源 View、檢討 View 和 FilledRegionType 的 ElementId。

結果只存在記憶體中：

- 關閉 Revit 後消失。
- 重新開啟模型後必須重新檢討。
- 使用者刪除結果指向的視圖或類型後，圖說製作可能要求重新檢討。

## 3. 待改進（對應共同需求）

- 計算、Revit 查詢與 UI 集中在 `Article164Command`，要抽成無 UI 的 use case，才能開放 MCP（建築線、道路側、道路寬度、排除類型改為參數）。
- 結果只存在記憶體（`Article164ReviewSession`），重開 Revit 需重跑；之後可改用共用的結果模型與持久化（[DESIGN.md](../../../DESIGN.md) §4.2）。
- 排版預覽與圖框選擇視窗是之後其他出圖工具的共用候選。
