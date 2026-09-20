# P2-T01 — 純資料 2D 幾何模型與擷取介面

## 完成範圍

- Domain `Geometry` 命名空間定義純資料幾何契約，不相依 Revit 與 UI：
  - `Point2D`（值相等、距離）、`SourceRef`（document / element / link UniqueId + `GeometrySourceKind`）、`Segment2D`（長度、方向、反向、中點）、`Loop2D`（順序閉環驗證、shoelace 帶號面積、方向、周長）、`Region2D`（外環扣孔洞的草算面積）。
- `GeometryTolerance`：spec 10.2 修復流程的單一容差契約（去重／吸附／缺口延伸／共線／閉合），以公釐建構、以英尺儲存，並提供距離與共線判斷。
- `PlanUnits`：英尺 ↔ 公釐／公尺、平方英尺 ↔ 平方公尺的唯一轉換點。
- `PlanTransform2D`：連結模型平面幾何轉入 host 專案座標的剛體轉換（僅 Z 軸旋轉 + 平移）。
- `PlanExtent2D`：擷取範圍（crop region／scope box）的軸對齊界線與包含判斷。
- `PlanGeometrySnapshot`：一次擷取的結果聚合（packageId、host document、level、segments、tolerance、extent、warnings、UTC 時間），含 `SchemaVersion`。
- Application `Geometry`：`PlanGeometryExtractionOptions` / `PlanGeometryExtractionRequest` / `IPlanGeometryExtractor`（回傳 `Result<PlanGeometrySnapshot>`），以及 `PlanGeometrySnapshotRecord` + `PlanGeometryStorageMapper` 序列化映射。

## 驗證

- Solution build：成功，0 warnings / 0 errors。
- Core tests：66/66 passed（P2-T01 新增 49 個）。
- 序列化：mapper 來回與 `System.Text.Json` 來回皆保留座標、來源、容差、extent、warnings 與 UTC 時間。
- 單位與座標轉換：英尺／公釐／公尺與面積轉換來回一致；`PlanTransform2D` 正逆轉換、`Inverse()` 與線段長度保持皆通過。

## 設計決策

- 幾何座標一律為 host 專案座標的十進位英尺；單位換算與 link transform 在進入 Domain 前完成，Domain 不接觸 Revit 型別。
- `Segment2D.DirectionRadians` 正規化到 [0, π)，使線段與其反向具相同方向，供 P2-T03 共線合併比較。
- `GeometryTolerance` 於建構時強制階段順序（duplicate ≤ snap ≤ gapExtension，closure ≤ snap），避免後段修復推翻前段已記錄的結果。
- `Loop2D` 只接受容差內的有序閉環；超出容差視為錯誤拋出，不自動猜測補線（呼應 spec 10.2 與非目標）。
- 序列化沿用 P1-T03 的 StorageRecord + Mapper 模式（扁平 DTO、無序列化器相依）；未知 schemaVersion 直接拋 `NotSupportedException`，不做臆測遷移。
- `IPlanGeometryExtractor` 契約為唯讀，擷取不開 transaction；`IncludeLinkedModels` 預設關閉，待 P2-T02 確立唯讀讀取策略。

## 未解決問題

- `PlanTransform2D` 僅支援旋轉與平移；鏡射或非等比縮放的 link 需由 P2-T02 的 adapter 明確拒絕並回報警告。
- 預設容差（1 / 10 / 50 mm、0.5°）為暫定值，尚未接到專案設定 UI，待實模型驗證後調整。

## 下一階段目標（P2-T02）

- 實作 Revit 幾何擷取 Adapter：牆 Location Curve、柱輪廓、Crop／Scope 範圍、來源 UniqueId、選用 Link 讀取策略。
- 退出條件：固定測試模型輸出穩定且可追溯來源元素。
