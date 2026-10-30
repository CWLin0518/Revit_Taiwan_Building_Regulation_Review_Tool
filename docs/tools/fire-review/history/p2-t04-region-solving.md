# P2-T04 — 閉環與 Region 求解

## 完成範圍

Domain `Geometry`（純資料）：

- `RingGeometry.cs`：環（閉合點列）層級的平面幾何。`SignedArea`（Shoelace，逆時針為正）、`ContainsPoint`（crossing number，Y 採半開區間規則，射線穿過頂點不會重複計數）、`TryFindInteriorPoint`（先試形心，落在形外時改用水平帶掃描：帶的 Y 取兩個頂點高度的中間值，掃描線因此絕不穿過頂點，交點必成對）。放在 Domain 是因為求解完成的面要能自己回答「這個點在不在我裡面」——Region Editor 的點選不該再繞回 Application。
- `PlanRegionMap.cs`：
  - `FaceBoundary`：一條追蹤出來的封閉環。除了 `Loop2D` 之外同時保存走過的 `NodeIds` 與 `EdgeIndices`（對齊 `Loop.Segments`），所以完成的邊界可以一路回推到線網、再回推到 Revit 元素。
  - `PlanFace`：求解出的一個封閉面積。外環逆時針，孔洞環順時針；`Geometry` 為 `Region2D`（P2-T01 已具備帶號面積與孔洞扣除），`RepresentativePoint` 是嚴格落在面內、且不在任何孔洞內的點，供 Region Editor 當點選目標；`Contains` 供命中測試。
  - `FaceAdjacency`：兩個面共用的邊界，含共用長度與共用邊索引。只共用一個點不算鄰接。
  - `MultiFaceRegion`：spec 10.3 的 MultiPolygon。保存被選為同一區劃的多個面、合計草算面積，以及 `ContiguousPartCount` / `IsContiguous`——連通與否只回報，「預設禁止不連通合併」是 Editor 的政策，不在幾何層決定。
  - `PlanRegionMap`：一個 Package 的全部面與鄰接關係。`FaceAt`（點命中）、`NeighboursOf`、`AreAdjacent`、`Combine`（把若干面當成一個區劃並算出連通塊數）。
  - `RegionIssue` / `RegionIssueKind`：求解過程中「解決了、但檢討者仍該看一眼」的事，嚴重度沿用 `NetworkIssueSeverity`。

Application `Geometry`：

- `RegionSolver.cs`：`Result<PlanRegionMap> Solve(PlanLineNetwork, DateTime?)`。把線網當成平面嵌入，用 half-edge 走訪：每條邊正反各走一次，在每個節點轉向「順時針方向的下一條」，於是圍出面積的環是逆時針、每個連通部分外圍的環是順時針各一個。依序執行：
  1. **非平面檢查**：任兩條不共節點的邊若仍相交，代表修復階段漏掉一個分割點，直接失敗。
  2. **修剪懸空邊**：有自由端的邊圍不出任何面積，反覆修剪到收斂，每條都記為 Warning；整個連通部分都被剪光時另記一則 `OpenComponent`。
  3. **追蹤閉環**：half-edge 走訪，得到每個面一個環。
  4. **孔洞判定**：順時針環若落在另一個連通部分的某個面內，就成為該面的孔洞；取「包含它且面積最小」的面為母面。都不被包含者即為整張圖的無界面，捨棄。
  5. **鄰接關係**：同一條邊的兩個 half-edge 分屬的兩個面即為鄰接，累加共用長度；孔洞環算在母面這一側。
  6. **草算面積**：`Region2D.NetAreaSquareFeet` = 外環面積 − 各孔洞面積。

## 驗證

- Solution build：成功，0 warnings / 0 errors（含 net48 Revit 專案）。
- Core tests：160/160 passed（P2-T04 新增 31 個：`RegionSolverTests` 21、`RingGeometryTests` 10）。
- 退出條件「幾何 golden tests」：
  - 乾淨矩形 → 1 個面、100 ft²、周長 40 ft、零鄰接、零問題。
  - 分隔牆切成兩間 → 2 個面各 50 ft²、1 筆鄰接、共用長度 10 ft、共用 1 條邊。
  - 島狀孔洞 → 外面 375 ft² 帶 1 個順時針孔洞（25 ft²）、島本身 25 ft²、鄰接共用長度 20 ft／4 條邊；外面不會宣稱島內的點屬於自己。
  - 三層同心環 → 最內環成為中層的孔洞而非最外層的，三個面各為 500 / 300 / 100 ft²。
  - L 形房間 → 形心落在形外，仍求得 64 ft² 與一個真正在內部的代表點。
  - 懸空邊、連鎖懸空邊、整組圍不出面積的線段 → 各自產生 Warning，房間照樣求解成功。
  - 同一份輸入求解兩次 → 面 ID、面積、代表點、孔洞數、鄰接、問題訊息逐字相同。
- 退出條件「歧義案例輸出錯誤而非猜測」：
  - `geometry.regions.non-planar`：節點以外仍有交叉。
  - `geometry.regions.ambiguous-nesting`：兩組未相接的邊界相距 5 mm（在吸附容差內），無法判斷中間的帶狀區域屬於誰；錯誤訊息寫出實際距離。
  - `geometry.regions.degenerate-face`：兩條邊連接同一對節點。
  - `geometry.regions.no-closed-loop`：修剪後沒有任何封閉範圍。

## 設計決策

- **走訪決定內外，不用包圍盒猜**：面的內外側完全由 half-edge 的轉向規則決定，沒有任何一步用「誰的 bounding box 比較大」來判斷誰包誰。孔洞的歸屬只用「包含且面積最小」，並在兩個候選面積相差小於細縫門檻時直接判為歧義。
- **失敗與問題的分界**：需要「挑一種解釋」的才失敗（非平面、歧義巢狀、退化面、無封閉範圍）；求解過程中沒有選擇餘地的只記為問題隨 map 回傳（懸空邊、圍不出面積的連通部分、細縫面）。這與 P2-T03 的分界一致。
- **細縫面回報但不刪除**：面積小於吸附容差方格（10 mm × 10 mm）的面記為 `SliverFace` 警告，但仍保留為正式的面。刪掉它會讓鄰接關係缺一塊，反而讓後續階段看到不完整的拓樸。
- **門檻仍然只來自 `GeometryTolerance`**：`RegionSolver.SliverAreaSquareFeet` 是 `SnapFeet²`，沿用修復階段吸附的容差而非自訂一個尺寸；歧義巢狀的「幾乎接觸」判定同樣用 `SnapFeet`。沒有新增任何 epsilon。
- **修剪懸空邊而非讓它留在環裡**：不修剪的話走訪會沿著懸空邊「出去再回來」，面積雖然仍正確（來回對 Shoelace 貢獻為零），但邊界會多出無意義的尖刺。修剪後每條都留下 Warning 與來源元素，使用者仍看得到它們。
- **輸出為標準形**：面依「從最小節點編號開始旋轉後的節點序列」排序後才編號，鄰接依面編號排序，因此同一份線網永遠得到相同的面 ID。
- **連通與否只回報**：`MultiFaceRegion.ContiguousPartCount` 說明選取的面落在幾個連通塊，是否允許合併由 Editor（P2-T06）依 spec 10.3 決定。

## 已知限制

- 尚未在實模型上跑過：`RevitPlanGeometryExtractor` → `LineNetworkRepairer` → `RegionSolver` 的端到端實跑仍從 P2-T02 懸著。
- `PlanRegionMap` 沒有 storage record，求解結果只存在記憶體；持久化格式留給 P2-T06／P2-T07，且不得變更 `PlanGeometrySnapshot` 既有欄位集合。
- 非平面檢查是 O(n²) 配包圍盒預篩，與修復階段同量級；大型平面圖的效能待 P2-T09 一併量測。
- 視圖裁切邊界上的端點在修復階段是懸空端點，到這裡會被修剪掉，所以區劃目前不會沿裁切邊界閉合。使用者需要時要自己補輔助線。
- 細縫門檻、歧義判定距離都還沒接到設定 UI。
