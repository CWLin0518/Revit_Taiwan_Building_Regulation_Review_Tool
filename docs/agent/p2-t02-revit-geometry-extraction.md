# P2-T02 — Revit 幾何擷取 Adapter

## 完成範圍

Application `Geometry`（可測試、不相依 Revit）：

- `PlanGeometryBuilder`：擷取規則的唯一實作位置。接收已展平為 `Point2D` 的折線與 `SourceRef`，負責去除退化線段、依視圖範圍裁切、累積警告、統計被捨棄的幾何，最後組成 `PlanGeometrySnapshot`。空結果回傳 `geometry.extraction.empty` 失敗而非空快照。
- `PlanExtentClipper`：Liang–Barsky 線段對軸對齊範圍的裁切。跨越邊界的牆被裁到邊界而非整條丟棄，範圍外者才排除。
- `LinkGeometryPolicy` + `BasisVector`：把 link instance 的 transform 判定為可接受的平面剛體轉換，或以明確錯誤碼拒絕（`geometry.link.scaled` / `skewed` / `tilted` / `mirrored`）。

Revit `Geometry`（唯讀 adapter）：

- `RevitPlanGeometryExtractor`：`IPlanGeometryExtractor` 的 Revit 實作。解析 Area Plan 與 Level、決定擷取範圍與切平面高程、讀取牆中心線／柱平面輪廓／輔助線、選用唯讀 link，全程不開 transaction。
- `RevitPlanShapeReader`：唯一接觸 `XYZ` 的地方。曲線展平（直線保留兩點、弧與雲線使用 `Tessellate()`）、以 `ExtrusionAnalyzer` 取柱的平面投影輪廓、由 `BoundingBoxXYZ` 的八個轉換後角點求平面範圍。
- `RevitDocumentIdentity`：以 `ProjectInformation.UniqueId` 作為文件識別，路徑與標題僅為 fallback。

## 驗證

- Solution build：成功，0 warnings / 0 errors（含 net48 的 Revit 專案，對 Revit 2024 API 編譯通過）。
- Core tests：97/97 passed（P2-T02 新增 31 個）。
- 裁切：完全在內、完全在外、僅外框重疊、單端裁切、雙端裁切、沿邊界、容差邊緣共 7 種案例通過。
- Link transform：無旋轉、旋轉＋平移、點位轉換、鏡射、縮放、傾斜、上下顛倒、非正交共 8 種案例通過。
- 輸出穩定性：相同元素以不同加入順序建構的兩份快照，segment 序列完全相同；同一元素內折線順序保留。
- 來源可追溯：每個 segment 都帶 document / element / link UniqueId 與 `GeometrySourceKind`，序列化來回後仍保留。

## 設計決策

- **邏輯下沉到 Application**：測試專案（net10.0）無法參照 net48 的 Revit 專案，因此擷取規則全部放進 `PlanGeometryBuilder` 等類別，Revit adapter 只負責把 Revit 物件轉成 `Point2D` 與 `SourceRef`。
- **範圍優先序**：Scope Box 優先於 Crop Region，兩者皆無時以整個樓層擷取並發出警告。裁切容差採 `GeometryTolerance.SnapFeet`，避免僅差數公釐的幾何被整條丟棄。
- **視圖範圍即可見性**：`RestrictToViewExtent` 為真時使用 view-scoped `FilteredElementCollector`，同時尊重視圖隱藏與裁切（spec 10.1「可見模型」）；為假時改用 `ElementLevelFilter`。
- **柱輪廓用投影而非切面**：`ExtrusionAnalyzer` 沿 Z 軸投影實體取得平面外框，不受視圖切平面落在柱內何處影響。分析失敗時退回外接矩形，並明確警告該處需人工確認，而非假裝那就是真實輪廓。
- **輔助線一律經由視圖讀取**：輔助線是檢討者在該 Area Plan 畫的，視圖中看不到的就不屬於這次檢討。可選的線型白名單預設為空（全部接受），待設定 UI 提供選擇。
- **Link 一律唯讀且先驗證**：鏡射／縮放／傾斜／非正交的 link 直接跳過並寫入警告，不壓平成看似合理的錯誤答案。Link 元素的樓層歸屬以幾何判定（擷取平面是否穿過其 bounding box，容差 50 mm），不依賴跨文件的樓層名稱對應。
- **輸出排序**：`FilteredElementCollector` 不保證回傳順序，快照在 `Build()` 時依（來源種類、文件、link、元素 UniqueId）穩定排序，使同一模型重跑產生逐字相同的結果。
- **警告去重與彙總**：逐元素的噪音會淹沒真正的問題，重複警告只記一次，被捨棄的幾何以「退化 / 範圍外 / 已裁切」三行摘要呈現。

## 未解決問題

- 尚未在實際模型上執行。退出條件中的「固定測試模型輸出穩定」目前由單元測試層級的排序不變性覆蓋，仍需在 `建築防火檢討1.rvt` 上實跑驗證元素數量與來源追溯（擷取為唯讀，不會修改模型）。
- 擷取尚未接到任何指令或 UI 入口，需等 P2-T05 的 Region Editor 或 P2-T09 整合時串接。
- 裁切會在視圖邊界產生新端點，這些端點不對應任何模型元素；若區劃需要沿裁切邊界閉合，須由 P2-T03／P2-T04 決定是否補上邊界線。
- 輔助線線型白名單、預設容差（1 / 10 / 50 mm、0.5°）與 link 樓層判定容差（50 mm）皆為暫定值，尚未接到專案設定 UI。

## 下一階段目標（P2-T03）

- 實作線網正規化與修復：去重、交點分割、端點吸附、受限延伸、共線合併、閉環偵測，並保存每次修復的原始幾何、方式與距離。
- 退出條件：正常、短缺口、超容差、自交四類案例測試通過；超出容差者標示為錯誤交由使用者處理，不自動猜測。
