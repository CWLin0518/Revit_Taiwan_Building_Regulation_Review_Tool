# P3-T03 — 候選元素與空間關係解析

## 完成範圍

- Application `Candidates`（全新，不相依 Revit／UI，core tests 覆蓋）：
  - `CandidateObservations.cs`：輸入契約。`CandidateCategory`（Wall／Column／StructuralFraming／Floor／Door／Window／CurtainPanel）、
    `CandidateSource`（document／element／link UniqueId）、`MemberObservation`（牆與梁用中心線＋寬度，柱與樓板用平面輪廓）、
    `OpeningObservation`（Host UniqueId、平面位置、寬高）、`ZonePartObservation`／`ZoneObservation`（工具寫入的 Area、Revit 回報的邊界與面積）、
    `CandidateObservationSet`（重複區劃、重複 Area、重複元素一律拒絕）。
  - `CandidateResolutionOptions`：邊界容差（預設 50 mm＝P2 的 gap extension 上限）、平行角度（5°）、最短關係長度（200 mm）、
    開口搜尋距離（300 mm）、**各類別可設定的空間關係策略**（`CandidateRelationKinds`：Boundary／Crossing／Inside，預設全開；spec 11.5 第 1 點）、
    是否納入區劃內部開口（預設否，spec 11.6 以邊界牆與其開口為主要候選）。
  - `CandidateResolver.Resolve(...)`：輸出 `CandidateSet`。
  - `CandidateSet.cs`：`ZoneRelation`（Boundary／Crossing／Inside／Ambiguous＋量測值＋說明）、`MemberCandidate`、`OpeningCandidate`、
    `CandidateZone`（各 Area 的 Revit 面積與幾何量算面積、區劃層級問題）、`CandidateAmbiguity`（`ToReviewResult(...)` → ManualReview）、
    `CandidateSet.Signature()`（正規化文字，可重現性比對用）、`CanReview`（spec 11.1「來源元素與區劃空間關係可解析」）。
  - `CandidateFacts`：轉成 `RuleFacts`（`zone.id`、`zone.levelName`、`element.category／typeName／isCompartmentBoundary／isStructural`、
    `opening.kind／isHosted／hostUniqueId／hostIsCompartmentBoundary／area`）。
  - `CandidateGeometry.cs`（internal）：capsule 區間的精確解（矩形＋兩端圓的聯集）、區間聯集／補集、`PlanShape`（even-odd 環集合）、
    沿邊界量測與「邊界穿過輪廓」長度。
- `ReviewErrorCode`：新增 `BCR-CAND-001`（元素與區劃的空間關係無法判定）、`BCR-CAND-002`（區劃範圍無法用於檢討）。
- Revit `Candidates/RevitCandidateObservationReader`（唯讀，不開 transaction）：讀本 package 的受管理 Area（重用 `RevitWrittenZoneReader.ReadLoops`，
  改為 internal）、樓層帶（本樓層＋50 mm 至上一樓層−50 mm）內的牆／柱／梁、本樓層的樓板、門／窗／帷幕嵌板；只讀主要設計選項；不讀 link。

## 判定規則

| 對象 | 依據 | 結果 |
| --- | --- | --- |
| 牆、梁 | 中心線沿平行（≤5°）邊界段、距離 ≤ 容差的長度 ≥ 門檻 | Boundary |
| | 否則沿邊界、距離 ≤ 半寬＋容差的長度 ≥ 門檻（邊界在構件厚度內但不在中心線上） | **Ambiguous**（BoundaryOffCenterline） |
| | 否則離開邊界的部分同時有區劃內與區劃外 ≥ 門檻 | Crossing |
| | 否則區劃內 ≥ 門檻 | Inside |
| 柱 | 區劃邊界以 > 容差的淨距穿過柱輪廓 | Boundary |
| | 邊界貼著柱面（≥ 門檻）或輪廓內外皆有但邊界未穿過 | **Ambiguous**（BoundaryAlongOutline） |
| | 輪廓在區劃內 | Inside |
| 樓板 | 與區劃重疊；邊界穿過樓板或樓板延伸到區劃外 → Crossing，否則 Inside | Crossing／Inside |
| Hosted 門窗 | Host 牆在此區劃為 Boundary，且開口位置距邊界 ≤ Host 半寬＋容差 | Boundary |
| | Host 為 Ambiguous／Crossing 且開口正在邊界上 | **Ambiguous**（HostRelationAmbiguous） |
| | 開口不在邊界上但在區劃內 | Inside（預設不列為候選） |
| 帷幕牆開口、帷幕嵌板 | Host 為帷幕牆且在邊界上（MVP 政策） | **Ambiguous**（CurtainWallOpening） |
| 非 Hosted 開口 | 距邊界 ≤ 搜尋距離（MVP 政策） | **Ambiguous**（NonHostedOpening） |
| Host 不是本次讀到的牆 | 距邊界 ≤ 搜尋距離 | **Ambiguous**（HostNotResolved） |
| 開口沒有位置 | 有 Host → 依 Host 在邊界上則 Ambiguous；沒有 Host → 不指定區劃的 Ambiguous | OpeningLocationUnknown |
| 構件沒有平面幾何 | — | 不指定區劃的 Ambiguous（NoPlanGeometry） |
| 連結模型元素 | 幾何判定有關係者一律改為（MVP 政策） | **Ambiguous**（LinkedElement） |
| 區劃 | 有 Area 未封閉 | ZoneNotEnclosed（`IsMeasurable` 為否時 `CanReview`=false） |
| | 兩區劃的邊界穿過彼此內部或內部點落在對方內（共用牆不算） | ZonesOverlap（雙方各一筆） |

門檻＝`min(最短關係長度, 總長/2)`（柱／樓板為周長/4），避免極短構件永遠不成立。

## 可重現性

- 輸入先依（類別、UniqueId、文件、link）排序，區劃依 Zone ID 排序；所有長度以精確區間計算，不取樣。
- `CandidateSet.Signature()` 以微米四捨五入輸出每個事實；測試以反轉／打亂輸入順序驗證簽章完全相同。

## 驗證

- Solution build：0 warnings／0 errors；外掛 csproj `-t:Rebuild`：0 warnings／0 errors（含 net48 Revit adapter，對 Revit 2024 API 編譯）。
- Core tests：**683/683 通過**（P3-T03 前 643，新增 40）。
  - 固定模型（`Candidates/CandidateModel.cs`）：兩個 10 m × 10 m 區劃＋一個未封閉區劃、15 個構件、11 個開口，
    逐元素比對 A／B 兩區的關係（21 列）、候選清單完全相符、未相關數量、12 筆歧義的種類／區劃／主體與順序。
  - 可重現：反轉與打亂輸入後簽章相同。
  - 牆距邊界 40 mm → Boundary、90 mm → Ambiguous、300 mm → Inside；只抵住邊界的牆不算邊界牆；孔洞邊界牆；區劃淨面積。
  - 策略可設定（牆只取 Boundary、樓板關閉），歧義與 Host 關係仍保留；內部開口開關；link 政策；區劃重疊與相鄰。
  - 證據欄位（來源 UniqueId、文件、類別、Type、邊界長度 m、Host）；歧義 → ManualReview（規則集 id／版本、Zone ID、錯誤碼）。
  - `CandidateFacts` 經 `RuleEngine`：邊界牆缺時效 → InsufficientData、補 120 min → Pass、內部隔間 → NotApplicable；歧義關係拒絕轉 facts。
- 本 Task 未在 Revit 實機執行 adapter（見未解決問題）。

## 設計決策

- **以工具寫入的 Area 為區劃範圍**：Area 邊界是由牆中心線畫出的，因此「中心線沿邊界」＝構成邊界；邊界落在厚度內卻不在中心線上，
  代表邊界來自牆面或輔助線，幾何無法斷定 → ManualReview，不猜。
- **所有牆的關係都計算**（即使牆類別被策略關閉），因為開口要用 Host 的關係；只有輸出候選時才套策略。
- **開口依 Host 在開口位置的關係判定**：長牆只有一段在邊界上時，位於非邊界段的門不是邊界開口。
- **歧義不進規則引擎**：`CandidateFacts` 對 Ambiguous 關係拋例外；歧義以 `CandidateAmbiguity.ToReviewResult` 直接成為 ManualReview，
  比照 NoRule 以規則集本身的 id／版本歸屬。
- **樓板**：`element.isCompartmentBoundary` 指平面上的區劃邊界，樓板永遠為 false；樓板作為上下層區劃構件須由規則以 `element.category` 判定。
- **區劃層級問題不改寫元素關係**：重疊或部分未封閉的區劃，其元素關係仍照算，由 P3-T04～T06 依 `CandidateZone.Problems` 決定該區結果是否 ManualReview。
- **Provided 值不在此讀取**：防火時效、防火保護、區劃用途等由擁有它們的檢查（P3-T04～T06）讀取與驗證，加到同一份 facts。

## 未解決問題

- Adapter 尚未接到指令或 UI，也未在實際模型上執行；需在 P3-T09 整合時於 `建築防火檢討1.rvt` 實跑，確認樓層帶、樓板 ElementLevelFilter、
  梁寬參數（`STRUCTURAL_SECTION_COMMON_WIDTH`／`b`）、門窗寬高參數與帷幕嵌板 Host 讀取正確。
- 寬度未知的牆／梁（`WidthFeet` 為 null）無法偵測「邊界在厚度內但不在中心線上」，只能以容差判定。
- 預設容差、平行角度、門檻與搜尋距離皆為暫定值，尚未接到專案設定 UI；spec 19 第 7 項（邊界實際判定方式、柱梁板納入方式）仍待確認。
- Phase、Group、Worksharing 狀態未納入候選判定（spec 11.1 前置檢查由 P3-T09 整合）。

## 下一階段目標（P3-T04）

- 區劃面積檢討：適用條件、以 `CandidateZone.RevitAreaSquareMeters` 為主要實際值、`GeometricAreaSquareMeters` 交叉驗證、要求值與證據；
  區劃有 `Problems` 時回 ManualReview（`CandidateAmbiguity.ToReviewResult`）。
- Exit：等於上限、超限、缺輸入與豁免測試通過。
