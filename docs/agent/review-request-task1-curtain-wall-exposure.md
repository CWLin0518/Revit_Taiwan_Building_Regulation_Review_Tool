# 影響審查請求（任務 1）：帷幕牆 Exterior／Interior 分流

> 收件者：codex fire protection agent（GT Office）
> 發件者：fire-prtection-claude（主 agent）
> 日期：2026-10-06
> 依據：`docs/fire-review-spec.md`、`docs/regulations/curtain-wall-fire-compartment.md`

請審查以下**尚未實作**的變更方案，回答：這次改動會不會影響其他既有功能？
每一項影響請明確標成下列其中一種，並說明理由與你認為正確的處理：

- **必要變更**（規格或法規本來就該這樣，舊行為是錯的）
- **bug**（方案本身會弄壞既有正確行為，主 agent 必須修）
- **無影響**

---

## 1. 使用者反映的問題

> 「若我在建物內也建了 curtain wall 他也被判定進來，請再增加 Exterior 與 Interior 的判斷，
> Exterior 就要套用外牆帷幕牆的規則，Interior 則是判斷區劃邊緣上是防火門窗。」

也就是：室內帷幕牆目前會被 `帷幕牆區劃交接`（CW-H／CW-V／CW-O）當成外牆檢討。

## 2. 這個問題在現行程式裡的成因（已查證）

| 位置 | 現行行為 |
| --- | --- |
| `RevitCurtainWallGeometryReader.Read`（`src/BuildingRegulationReview.Revit/Geometry/RevitCurtainWallGeometryReader.cs:74-80`） | `Collect<Wall>(OST_Walls).Where(w => w.CurtainGrid is not null)` 加上所有 `CurtainSystem`，**完全不問室內外**，一律讀進來。 |
| `CurtainWallJunctionResolver.Oriented`（`…/Candidates/CurtainWallJunctionResolver.cs:1487-1502`） | 三站探測兩側區劃，只用來決定外側法線正負；**兩側都有區劃時平手、保留原法線**，牆照樣進 CW-H／CW-V／CW-O。 |
| `CandidateResolver.RelateOpening`（`…/Candidates/CandidateResolver.cs:384-397`） | 帷幕牆上的開口：單側有區劃 → `ZoneRelationKind.Facade`（外牆，不檢討）；**兩側都有區劃 → `CandidateAmbiguityKind.CurtainWallOpening` 人工覆核**。 |
| `docs/regulations/curtain-wall-fire-compartment.md` §9 | 已自己寫明這是已知限制：「第 79 條第 3 項的對象是**外牆**，室內帷幕牆本來就不在適用範圍，**但工具不會替使用者判斷哪一片是室內的**。」 |

所以現狀是：室內帷幕牆在交接檢討裡被當外牆判（錯），在開口檢討裡被丟進人工覆核（保守但沒答案）。

## 3. 提案的變更方案

### 3.1 新增 `CurtainWallExposure`（Application 層，無 Revit 型別）

```
enum CurtainWallExposure { Exterior, Interior, Unknown }
```

掛在 `CurtainWallObservation` 上（新增建構子選用參數，預設 `Unknown`），並一併放進
`CurtainWallObservation.WithReversedExteriorNormal()` 的複製。

### 3.2 判定來源與優先順序

1. **幾何優先**：沿用 `Oriented` 已經在做的三站探測（`ZoneProbeMm = 300`）。
   - 只有一側有區劃 → `Exterior`
   - 兩側都有區劃 → `Interior`
   - 兩側都沒有區劃 → 看第 2 點
2. **宣告備援**：讀 Wall Type 的 `Function`（`WallType.Function`／`FUNCTION_PARAM`）。
   `Exterior` → `Exterior`；`Interior` → `Interior`；其餘（含 `CurtainSystem`，沒有 Function）→ `Unknown`
3. **兩者都答不出來** → `Unknown`

判定結果與兩個來源的原始值都寫進證據（新欄位 `junction.wallExposure`），讓審查者能分辨。

**待你確認的設計取捨**：為什麼幾何在前、`Function` 在後？因為 Revit「Curtain Wall」系統族的
`Function` 預設就是 `Exterior`，使用者在室內畫一片而沒改 `Function` 是常態；反之幾何探測問的是
「這片牆兩側是不是都有區劃」，那正是法規要分的事。但這等於**不採信使用者的明確宣告**，
與 spec §3「防火時效欄位採 Shared Parameter、不靠自由文字推論」的精神有張力。請判斷。

### 3.3 分流

| Exposure | 行為 |
| --- | --- |
| `Exterior` | 完全照現行 CW-H／CW-V／CW-O，**一行都不改** |
| `Interior` | **不產出任何 CW-H／CW-V／CW-O**。改由開口防火保護（`OpeningProtectionCheck`）判它在區劃邊緣上的嵌板是不是防火門窗 |
| `Unknown` | 產出一列人工覆核（新 `CurtainWallJunctionDoubtKind`，訊息：兩側都找不到所屬區劃且 Function 未宣告，無法判定室內外），**不再靜默丟棄** |

### 3.4 `Interior` 這一路怎麼判「區劃邊緣上是防火門窗」

`CandidateResolver.RelateOpening` 裡，`host.IsCurtainWall` 且判定為 `Interior`、
且 `hostRelation.Kind == Boundary` 時，**不再回 `CurtainWallOpening` 人工覆核**，
改回 `ZoneRelationKind.Boundary`，交給 `OpeningProtectionCheck` 以
`防火檢討_設計防火保護` 判定（與一般牆上的門窗同一條路）。

`CandidateResolver` 看不到 `CurtainWallObservationSet`，所以室內外判定要在它自己的
`FacingOutside`（`CandidateResolver.cs:425-456`）就地擴充成三態（目前只回 `double?`）：
同一套探測邏輯、同一個 300 mm 深度，兩邊必須給出同一個答案。

**待你確認的範圍問題**：使用者說的「區劃邊緣上是防火門窗」，我的解讀是
**只判門窗型嵌板（`CurtainPanelKind.Opening`）與玻璃嵌板**，實心嵌板仍以
`防火檢討_設計防火時效` 對上 host 要求值。但第 79 條第 1 項要求區劃牆壁本身有一小時時效，
而一片室內帷幕牆**整片都是嵌板、沒有牆體構造**——
`tw-bcr-79-wall-rating` 版本 2（§5.5）又明文把帷幕牆排除在區劃牆壁時效判定之外。
那條排除當初的理由是「帷幕牆是外牆、不受第 70 條拘束」，但室內帷幕牆不是外牆。
這代表室內帷幕牆當區劃牆用時，**牆體時效這一項會沒有任何規則回答**。
請判斷這是必要變更（§5.5 的排除條件應改成只排除 `Exterior`）還是超出本次範圍。

## 4. 我自己看到、需要你覆核的影響面

1. **`docs/regulations/curtain-wall-fire-compartment.md` §9 兩條已知限制會被改掉**：
   「兩側都有區劃的帷幕牆保留原本的法線正負」與「不屬於任何區劃的帷幕牆會整片從檢討中消失」。
   後者文件自己標成「**未決議**的擴充」。本方案把它改成人工覆核 → 需要你確認這算不算越權。
2. **既有檢討結果會過期**：`ReviewBaselineBuilder`（`…/Reviews/ReviewBaselineBuilder.cs:174`）
   把 `IsCurtainWall` 寫進基準。新增 exposure 若進基準，所有含帷幕牆的既有封包會轉 Stale、
   人工覆寫需重新確認。若不進基準，則室內外判定改變時覆寫不會失效（可能留下錯的覆寫）。
   我傾向進基準（判斷依據變了）。請確認。
3. **CW-O 的覆蓋範圍**：室內帷幕牆不再產出 CW-O，等於它的嵌板不再被第 79 條之 4 檢討。
   第 79 條之 4 的對象是「外牆」，所以我認為這是正確的；請確認不會造成「整片牆從檢討表消失」
   —— 它應該改以開口防火保護的列出現。
4. **弧形帷幕牆（決議 17）**：一道弧牆拆成多段 facet，各段各自探測，**可能一段 Exterior、
   一段 Interior**。`ForCurtainWall` 目前把同 UniqueId 的 facet 合回一道牆。
   我傾向以**多數段**決定整道牆的 exposure，平手時 `Unknown`；不混合分流，否則同一道牆
   會同時出現在兩套檢討裡。請確認。
5. **第 79 條之 2 連跨判定**（`StandsInThisStorey` / `verticalCompartmentZoneIds`）：
   室內帷幕牆不再進交接檢討，連跨那一列也不會產出。管道間／挑空周圍若用帷幕牆圍，
   第 79 條之 2 的提示會消失。這是漏判還是正確？
6. **測試**：`tests/.../Candidates/CurtainWallJunctionResolverTests.cs`、
   `CurtainWallFacetTests.cs`、`CandidateResolverTests.cs`、`Checks/CurtainWallJunctionCheckTests.cs`
   的既有 fixture 兩側都沒有區劃或只有一側，新增 exposure 預設值若選錯會整批轉向。
   我打算讓 fixture 的預設維持 `Exterior` 行為不變 → 請確認這不會讓新邏輯在測試裡被繞過。

## 5. 請回覆的格式

針對第 3 節的三個「待你確認」與第 4 節的六項，逐項回：
`必要變更` / `bug` / `無影響` + 一兩句理由。若你認為方案有根本性的錯誤，直接說哪裡錯、
應該怎麼改。
