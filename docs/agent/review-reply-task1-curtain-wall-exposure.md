# 任務 1：帷幕牆室內外分流完整審查回覆

日期：2026-10-06。對象：review-request-task1-curtain-wall-exposure.md 第 3、4 節。此為尚未實作方案的靜態審查；以下行號指本次核對的現行程式，表示變更應落點，不表示方案中的新程式已存在。未改程式，未執行 build 或 Revit 驗證。法規判斷依本專案規格與已決議文件，並非新增法規決議。

結論：支持室內外分流；目前方案不能直接照寫。三態資料模型可用，但幾何的「未找到區劃」不能等同「建築物外部」，兩條候選路徑必須共用判定結果，混合 facet、嵌板種類及垂直區劃提示需補齊。

## 第 3 節：三個待確認

### 3-A 幾何優先、Function 備援：bug

三站探測原本只為法線定向；區劃可能沒有完整建模、300 mm 探點可能跨越狹小空間，單側無區劃不足以證實 Exterior。兩側均有探點也必須確認為不同且有效的空間，不能把重疊區劃、同一區劃繞牆兩側或不同站的混合結果直接定為 Interior。Function 與幾何矛盾時若直接蓋掉宣告，可能把真實外牆移出全部 CW 規則。spec §3 的 Shared Parameter 要求針對防火性能，不能用它推導 Function 與幾何誰必然優先。

壞點及修改：`src/BuildingRegulationReview.Application/Candidates/CurtainWallJunctionResolver.cs:1486-1503` 的 Oriented 只累計 inward/outward，未保留每站區劃身分；應新增獨立 exposure 判定服務，保留探點、區劃 ID、宣告值與衝突理由。證據不足、混合或衝突回 Unknown/人工覆核；未建模的一側不得自動當成已證實外部。若希望保留單側探測推定，必須明確列為推定並可覆核，而非確定事實。

### 3-B 兩個 resolver 給相同答案：bug

同為 300 mm 不代表同一算法。`CandidateResolver` 在開口最近線段的投影點探測，交接 resolver 在 facet 的 25%、50%、75% 探測；前者用半牆寬，後者用 ExteriorOffsetMm。局部內牆與外牆混合、弧牆及厚度差異都可能使同一嵌板被兩路相反分類。

壞點及修改：`src/BuildingRegulationReview.Application/Candidates/CandidateResolver.cs:384-397、427-456` 與 `CurtainWallJunctionResolver.cs:90、1486-1503`。不要各自擴充三態；讓兩路讀同一個分類服務/不可變分類結果，採一致的 facet、位置、空間與厚度資料。開口需取得其所屬區段結果；host Relation 非 Boundary 或分類 Unknown 時維持覆核，不能無條件放行 Boundary。

### 3-C 室內帷幕牆牆體時效：必要變更

依 request 引用的規格 §5.5，排除理由是外牆，不能繼續以 IsCurtainWall 排除室內區劃牆。此為使用者要求室內區劃分流的必要完整性，不宜留下「門窗回答了、區劃牆本體沒回答」的缺口。

修改落點：`src/BuildingRegulationReview/Data/fire-review-rules.json:139` 現行 appliesWhen 為 `element.isCurtainWall != true`；`src/BuildingRegulationReview.Application/Candidates/CandidateFacts.cs:44` 只有布林事實。補 exposure 事實及欄位定義，只排除已確認 Exterior；Interior 進入區劃牆時效路徑，Unknown 明確覆核。同步規則版本與 §5.5。實心嵌板的設計時效必須對應實際構造，不得僅用 host 型別值宣告整片含門窗牆體均符合。

## 第 4 節：六項影響

### 4-1 §9 已知限制、Unknown 不靜默消失：必要變更

室內帷幕牆不適用外牆交接規則，應更新「兩側都有區劃仍保留法線照判」限制。不能確定適用性的模型保留人工覆核，符合 spec 資料不足不得誤報符合的要求；文件標記未決議不表示必須繼續靜默略過。需同步記錄新決議及範圍。

`CurtainWallJunctionResolver.cs:137-138` 現行 wallZone 為 null 就不產生嵌板列；Unknown 覆核應在此類提前退出前產生，且不得要求必有 zoneId 才能建立結果。訊息須按真正原因區分無區劃、衝突、混合、未宣告，不能全部写成「Function 未宣告」。

### 4-2 baseline 與既有覆寫失效：必要變更

應進基準。分類改變會改適用規則、候選集合與覆寫意義；保留原覆寫會把舊的外牆結論錯套在室內區劃。既有含帷幕牆封包轉 Stale 是合理遷移結果，需說明重新檢討及重新確認覆寫。

修改落點：`src/BuildingRegulationReview.Application/Reviews/ReviewBaselineBuilder.cs:174、188`，同時涵蓋牆與開口 host。指紋須含實際使用的區段分類、宣告來源與算法/規則版本，不能只加整牆 enum 卻漏掉分類所依賴的輸入。

### 4-3 CW-O 移除與開口替代：bug

已確認 Interior 不產出第79條之4外牆列是必要且正確的分流；但「所有嵌板改走 OpeningProtectionCheck」會把實心嵌板誤當防火設備，或者在排除時失去構造檢討。現行一般開口 observation 沒有傳入 CurtainPanelKind，無法完成方案聲稱的門窗/玻璃/實心區分。

壞點及修改：`src/BuildingRegulationReview.Revit/Candidates/RevitCandidateObservationReader.cs:249-282` 將 OST_CurtainWallPanels 統一建立 OpeningObservation，未帶種類；`CandidateResolver.cs:384-397` 依 host/CurtainPanel 一律處理；`src/BuildingRegulationReview.Application/Checks/OpeningProtectionCheck.cs:229、289` 接手這些候選後以開口規則回答。須將讀取器已知的嵌板種類可靠傳到候選資料：門窗及已確認玻璃按方案走保護，實心構造走時效，種類缺失人工覆核。不能靠名称猜。reader `:259` 亦會排除非 Wall host 的 CurtainSystem panel；若 Interior CurtainSystem 不在本次支援範圍，必須保留明確覆核而非宣稱已完整接手。

### 4-4 弧牆 facet 多數決整牆分類：bug

多數段會使少數但真實的室內或外牆區段適用錯誤規則；段數還受網格細分影響，與長度和法規適用性無關。「同一道牆出現在兩套檢討」不是錯誤，兩套若各自回答不同區段即可。

壞點及修改：`src/BuildingRegulationReview.Application/Candidates/CurtainWallJunctionResolver.cs:72、90-93` 按 UniqueId 分組並建立整道 Elevation，`:134、138` 又用合併立面產出結果。保留 facet/區段 exposure，外部區段產生 CW，內部區段產生區劃構造/開口檢討，證據及結果鍵帶區段識別並防止跨接錯區段。若本階段不做分段分流，混合整牆應 Unknown/覆核，不能多數決後刪掉少數區段檢討。

### 4-5 第79條之2連跨提示：bug

移除 Interior 的 CW-H/V/O 並不表示管道間、挑空的垂直區劃要求消失。若直接在 ForCurtainWall 開頭 yield break，既有「本層無楼板/跨層」提示也會一起消失；僅開口防護不能替代這項幾何提醒。

壞點及修改：`src/BuildingRegulationReview.Application/Candidates/CurtainWallJunctionResolver.cs:101、134、780-800、1580` 的 StandsInThisStorey/NoFloorAtThisLevel 與 verticalCompartmentZoneIds 路徑。將垂直區劃辨識及必要覆核抽出至獨立垂直區劃檢查，不以 Exterior 作入口；Interior 不再套外牆層間帶要求，但仍提供適用的區劃牆時效、門窗防護和跨層證據。新路徑完成前保留明確人工覆核，不可靜默漏列。

### 4-6 fixture 預設 Exterior：bug

若全面將 fixture 預設指定 Exterior，使 resolver 信任它而略過分類，既有測試雖綠卻無法發現新算法錯誤。舊有純 CW 檢查測試可明確提供「已確認 Exterior」以隔離規則測試，這部分無影響；resolver 與整合測試必須驗真正分類輸入及輸出。

修改落點：`tests/BuildingRegulationReview.Core.Tests/Candidates/CurtainWallJunctionResolverTests.cs`、`CurtainWallFacetTests.cs`、`CandidateResolverTests.cs`、`Checks/CurtainWallJunctionCheckTests.cs` 的 observation helper/fixture（方案尚未寫入，沒有可引用的新 helper 行號）。production observation 預設 Unknown，複製反轉法線必須保留 exposure。補兩側不同區劃、缺區劃、Function 衝突、兩 resolver 同一開口一致、混合 facet、實心/玻璃/門窗/未知種類、垂直區劃及 baseline 失效案例；不能把缺區劃 fixture 改預設 Exterior 當成分類驗證。

## 驗證與交付界線

已靜態核對 request、前次交接與上述現行程式落點。本回覆補齊三個設計問題與六項影響，不宣告尚未實作方案已通過；程式修改、規則版本調整、build/test 及 Revit 驗收由主 agent 後續執行。
