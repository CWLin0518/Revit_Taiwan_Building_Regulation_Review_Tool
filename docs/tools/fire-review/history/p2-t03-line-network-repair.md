# P2-T03 — 線網正規化與修復

## 完成範圍

Domain `Geometry`（純資料，`PlanLineNetwork.cs`）：

- `PlanLineNetwork`：修復後的平面圖：節點、邊、修復紀錄、問題清單，加上容差與來源快照資訊。提供 `DegreeOf`、`EdgesAt`、`DanglingNodes`、`ComponentCount`、`LoopCount`（E − V + C）與 `HasClosedLoop`，供 P2-T04 求解區劃、Region Editor 顯示問題。
- `NetworkNode` / `NetworkEdge`：吸附後的節點與邊。邊的 `Sources` 是複數，因為去重與共線合併會把多個 Revit 元素折成同一條邊，而每一個都必須仍可追溯。
- `NetworkRepair`：修復紀錄。同時保存「原始幾何」`OriginalStart/OriginalEnd`、「修復方式」`Kind`、「距離」`DistanceFeet`（另有 mm 便利屬性）、涉及的來源元素與人類可讀訊息，對應 spec 10.2 的要求。
- `NetworkIssue`：管線拒絕自行猜測的地方。帶 `Severity`（Error / Warning）與座標，讓使用者知道該看平面圖的哪裡。

Application `Geometry`：

- `SegmentGeometry`：修復所需的平面幾何，集中在一處以確保每一步的量法一致。線段交點（含容差 slack）、點到線段／無限直線距離、沿軸投影、射線打線段、共線重疊量測。
- `LineNetworkRepairer`：依 spec 10.2 的順序執行六個步驟，輸入 `PlanGeometrySnapshot`，輸出 `Result<PlanLineNetwork>`：
  1. **去除重複線**：共線、橫向偏差在 `DuplicateFeet` 內且真正重疊者合併為聯集跨距，保留兩者來源。
  2. **分割交點**：所有交叉點成為共用點；落在端點附近（`SnapFeet` 內）者不分割，交由吸附處理，避免製造碎段。
  3. **端點吸附**：`SnapFeet` 內的端點併為同一節點，線段集合自此成為圖。以網格索引查找，且走訪順序固定。
  4. **短缺口受限延伸**：懸空端點只能沿自身方向、在 `GapExtensionFeet` 內延伸到它明顯該接上的線；接上時會把被接的線在接點分割。
  5. **共線合併**：只有 degree 為 2 的節點可合併，因此真正的接頭永遠不會被溶掉。
  6. **閉環偵測**：以 E − V + C 回報封閉範圍數；沒有任何封閉範圍時記為錯誤。實際面的追蹤屬於 P2-T04。

## 驗證

- Solution build：成功，0 warnings / 0 errors（含 net48 Revit 專案）。
- Core tests：129/129 passed（P2-T03 新增 32 個：`LineNetworkRepairerTests` 18、`SegmentGeometryTests` 14）。
- 退出條件四類案例：
  - **正常**：乾淨矩形零修復零問題、被分隔牆切成兩間（7 邊 / 6 節點 / 2 個環）、含島狀孔洞（2 個連通元件 / 2 個環）。
  - **短缺口**：5 mm 未達牆面（吸附即可，不需延伸）、30 mm 缺口沿原方向延伸後閉合。
  - **超容差**：150 mm 缺口不延伸，產生 `GapBeyondTolerance` 錯誤並在訊息中寫出實際距離與容差，同時回報無封閉範圍。
  - **自交**：同一元素的兩段幾何交叉，分割後仍保持平面性，並記一則 `SelfIntersection` 警告。
- 其他：重複線合併保留雙來源、共線合併不跨越 degree 3 接頭、短於吸附容差的碎段被捨棄並記為警告、修復後無剩餘線段回傳 `geometry.repair.empty` 失敗、輸入快照不被修改、同一輸入兩次修復產生完全相同的節點與邊。

## 設計決策

- **不猜測、不遺失**：兩條規則貫穿所有步驟。超過容差的缺口變成使用者要處理的 `NetworkIssue`，不會自動接合；沒有任何步驟會產生模型裡不存在的 Fillet 或轉角（spec 10.2 明列的非目標）。同時每一次改動都寫進修復紀錄，附上被取代的幾何與移動距離，使完成的邊界永遠可以回推到來源元素。
- **容差全部來自 `GeometryTolerance`**：不新增任何 epsilon。唯一的新常數是 `GapReportingFactor = 10`，且它不修任何東西——只決定懸空端點要往外找多遠，好在錯誤訊息裡寫出「差多少」。
- **問題不讓 Result 失敗**：`GapBeyondTolerance` 等問題隨網路一起回傳而非變成失敗，因為使用者要拿著修復後的線網在 Region Editor 裡處理它們。只有「修完什麼都不剩」才是失敗。
- **嚴重度分級**：`GapBeyondTolerance` 與 `NoClosedLoop` 是 Error——前者代表「差一點就接上」的歧義只有人能決定，後者代表無法求解區劃。`DanglingEnd`（附近根本沒有線）與 `SelfIntersection`、`CollapsedSegment` 是 Warning：牆體末端在真實平面圖中到處都是，把它們全部當錯誤會讓任何模型都無法進入下一步。
- **端點附近的交點不分割**：交點落在某條線端點的 `SnapFeet` 內時只記錄另一條的分割，端點那側交給步驟 3 吸附。這讓「牆端差 5 mm 沒碰到另一道牆」自然變成 T 型接頭，而不會先產生一段 5 mm 的碎段再被吸附消滅。
- **延伸是「受限」的**：只沿懸空線段自身方向前進，橫向偏移不得超過 `SnapFeet`，且優先接到既有節點而非切開一條線——接到節點不會增加節點數，也避免在別人端點旁邊幾公釐處切一刀。
- **共線合併只發生在 degree 2**：這是保護接頭的唯一機制。被分隔牆切開的外牆兩半雖然共線，但接頭 degree 為 3，不會被合併掉。
- **輸出是標準形**：節點編號在最後壓實（合併會留下孤兒節點），邊依節點編號排序，因此同一份快照重跑會產生逐字相同的網路，可與前一次結果比對。
- **步驟間的資料結構**：修復期間用可變的 `WorkEdge`，最後才轉成不可變的 Domain 型別。去重與分割是 O(n²) 配上外接矩形預篩，吸附用網格索引；以單層樓的線段量（數百到數千）是可接受的，真正需要時再加空間索引。

## 未解決問題

- **尚未在實模型上執行**。四類案例由單元測試覆蓋，但還沒把 `RevitPlanGeometryExtractor` 的輸出直接餵進修復管線跑一次；`建築防火檢討1.rvt` 的實跑驗證仍與 P2-T02 一起懸著（兩者都是唯讀）。
- `PlanLineNetwork` 還沒有 storage record。修復紀錄目前只存在於記憶體；要讓它進入檢討報告或稽核紀錄，需在 P2-T06／P2-T07 決定持久化格式（不得變更 `PlanGeometrySnapshot` 的既有欄位集合）。
- 懸空端點目前一次過（single pass）處理：延伸 A 之後不會回頭重試先前處理過的端點。實務上缺口彼此獨立，但連鎖缺口（A 要先接上才輪得到 B）目前需要使用者處理。
- 視圖裁切在邊界產生的端點會被當成一般懸空端點（多半落在 `DanglingEnd` 警告）。區劃是否應沿裁切邊界閉合，仍留給 P2-T04 決定。
- `GapReportingFactor = 10`（= 500 mm）與各項容差一樣，尚未接到設定 UI。

## 下一階段目標（P2-T04）

- 由 `PlanLineNetwork` 求解閉環與 Region：閉環、孔洞、MultiPolygon、鄰接關係、草算面積。
- 退出條件：幾何 golden tests 通過；歧義案例輸出錯誤而非猜測。
- 可直接使用的基礎：`LoopCount`／`ComponentCount` 已指出有幾個獨立封閉範圍與幾個連通元件，`EdgesAt`／`DegreeOf` 提供面追蹤所需的鄰接查詢，`Loop2D`／`Region2D`（P2-T01）已具備帶號面積與孔洞扣除。
