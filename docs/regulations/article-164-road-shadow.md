# 建築技術規則第 164 條：道路陰影檢討

## 1. 功能摘要

| 項目 | 內容 |
| --- | --- |
| 功能 ID | `article-164-road-shadow` |
| 法規 | 建築技術規則建築設計施工編第 164 條 |
| 核心目的 | 以 3.6:1 斜率計算建築物投影於面前道路的陰影 |
| 模型輸入 | 外牆、樓板、直線建築線、道路側點、道路寬度 |
| 主要產出 | 檢討平面圖、陰影面積、合規結果及檢討圖紙 |

## 2. 使用者操作流程

```text
選擇「第164條道路陰影檢討」
    │
    ▼
開始檢討
    ├─ 確認來源牆與樓板類型
    ├─ 選取直線建築線
    ├─ 點選道路所在側
    └─ 輸入道路寬度與永久性空地條件
             │
             ▼
       計算投影與判定
             │
             ▼
建立「日照陰影檢討圖」
             │
       符合時可繼續
             ▼
圖說製作 → 選圖框 → 排版預覽 → 建立圖紙
```

## 3. 輸入元素與篩選

### 3.1 模型元素

優先尋找名稱為 `164條_外牆與樓板` 的 Revit Selection Set：

- 如果存在，只使用其中的 `Wall` 與 `Floor`。
- 如果不存在，自動收集目前平面視圖 Crop Box 範圍內的外牆與全部樓板。

外牆符合下列任一條件：

- Wall Type 的 `Function` 是 `Exterior`。
- Wall Type 或 Wall Instance 的自訂參數 `Exterior Wall` 是 Yes、True、1 或「是」。

使用者可以在 `Article164ElementFilterWindow` 依元素類型排除不需要參與計算的牆或樓板。

### 3.2 幾何與選項

- 建築線必須是直線 `CurveElement`。
- 道路側由使用者在建築線其中一側點選。
- 道路寬度以公尺輸入，進入計算前轉為 Revit internal feet。
- 使用者指定道路對側是否有永久性空地。

## 4. 幾何計算

每個 Revit Solid Face 先三角化，再將每個頂點投影至檢討平面：

```text
P' = (X, Y) + N × max(0, Z - Z0) / 3.6
```

其中：

- `N`：垂直建築線、指向道路側的平面單位向量。
- `Z0`：目前平面視圖所屬 Level 高程。
- `3.6`：法規投影斜率。

處理流程：

1. 取得牆與樓板的 Revit 幾何。
2. 將 Solid Face 三角化。
3. 使用 RhinoCommon 建立投影三角形。
4. 對三角形執行 Boolean Union，形成陰影輪廓。
5. 另以零位移投影建立建物平面輪廓。
6. 陰影輪廓與道路矩形執行 Boolean Intersection。
7. 加總道路範圍內的陰影面積 `As`。

RhinoCommon 布林運算的 tolerance 取自 `Application.ShortCurveTolerance`。

## 5. 合規判定

道路對側沒有永久性空地：

```text
As ≤ (L × Sw) / 2
```

道路對側有永久性空地：

```text
As ≤ L × Sw
```

另外，任何陰影都不得超過道路對側境界線：

```text
最大道路方向投影深度 ≤ Sw
```

只有面積與道路對側越界兩項都通過，結果才是符合。

變數：

- `As`：道路範圍內陰影面積。
- `L`：所選建築線長度。
- `Sw`：道路寬度。

## 6. Revit 產出

### 6.1 檢討平面圖

`Article164PlanViewBuilder` 建立或覆蓋 `日照陰影檢討圖`：

- 複製來源視圖的 View Template、比例、Detail Level、Display Style、Discipline、Phase 與 Phase Filter。
- 隱藏模型分類，只保留檢討用 2D 元素。
- 灰色 FilledRegion：本案新建建物平面投影。
- 斜線或紅色 FilledRegion：符合或不符合的道路陰影。
- Detail Curve：建築線。
- TextNote：各陰影區塊面積。

### 6.2 圖說

檢討通過後，`Article164DrawingCommand` 可以建立：

- Sheet Number：`164-1`。
- Sheet Name：`第164條道路陰影檢討`。
- 平面圖 Viewport。
- Drafting View `164條圖說－圖例`。
- FilledRegion 圖例、法規文字、公式、代入值與判定結果。

圖框、文字大小與圖面位置由使用者在 WPF 視窗中設定。

## 7. 程式架構

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

## 8. 結果資料與生命週期

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

## 9. 已知限制

- 建築線目前只接受直線。
- 輸入模型幾何必須能取得有效 Solid 與三角網格。
- Rhino Boolean Union 或 Intersection 失敗時無法完成檢討。
- Session 以 `Document` 為 key，目前未在文件關閉事件中主動清除。
- 計算、Revit 查詢與 UI 還集中在 `Article164Command`，尚未拆成可獨立測試的服務。

## 10. 回歸測試清單

- 外掛與 Dockable Pane 可正常載入。
- JSON 項目可正常顯示與搜尋。
- Selection Set 存在與不存在兩種來源都能正確收集元素。
- 元素類型排除功能正常。
- 無永久性空地時採用一半容許面積。
- 有永久性空地時採用完整容許面積。
- 面積符合但陰影越界時仍判定不符合。
- 重跑檢討會覆蓋舊檢討視圖。
- 不符合時禁止圖說製作。
- 符合時能建立圖紙、圖例與 Viewport。
- 不同圖框尺寸與文字大小下，排版不重疊。

