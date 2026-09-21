# P3-T07 — 結果持久化與失效

## 完成範圍

- Domain `Reviews`：
  - `ReviewBaseline`：一次檢討的**元素證據**。包含一個 context 指紋，以及每個 subject 各自的指紋（元素 UniqueId、Area UniqueId、`zone:<ZoneId>`）；`None` 表示沒有保存證據。
  - `ReviewOverride` + `ReviewOverrideStanding`（Active／NeedsReconfirmation／Superseded）：人工覆寫稽核紀錄，記錄原因（必填）、註解、操作者、時間、原始狀態、覆寫狀態、規則版本、相依指紋與前一筆 ID。
  - `ReviewRun`：新增 `Baseline`、`Overrides`、`Result(id)`、`CurrentOverrideFor`、`EffectiveStatus`、`WithOverrides`；`Complete(..., baseline)`。Schema 版本 `1.0` → `1.1`。
- Application `Reviews`：
  - `ReviewEnvironment`：來源視圖、樓層、Area Scheme、Phase、Design Option、幾何容差、專案單位等具名事實，由 adapter 提供。
  - `ReviewBaselineBuilder.Build(set, environment, areaInputs, ratingInputs, protectionInputs)`：用 SHA-256 算出 baseline；`DependencyFingerprint(baseline, result)` 算出單一結果的相依指紋。
  - `ReviewBaselineKeys`：結果相依的 key，也就是它的 subjects 加上 `zone:<ZoneId>`。
  - `ReviewRunValidity.Evaluate(run, currentBaseline, ruleSetId, ruleSetVersion, boundaryRevision?)` → `ReviewRunFreshness`（Reasons、StaleResultIds、Added／Removed／ChangedSubjects、`InvalidatesAll`、`OverridesToReconfirm`）；
    另有 `WithOverridesSuspended`、`ApplyTo(package)`（Reviewed／Documented → Stale）、`Explain`（log）。
  - `ReviewOverrides.Apply`／`Reconfirm`／`Withdraw`／`CarryOver(previous, next)` → `OverrideCarryOverReport`（Kept／NeedsReconfirmation／Dropped）。
  - `IReviewRunRepository` + `GetLatest` 擴充方法；`ReviewRunStorageRecord` 新增 `ContextFingerprint`、`SubjectFingerprints`、`Overrides`。Mapper 可讀 `1.0`（視為沒有證據、沒有覆寫）與 `1.1`。
- Revit：`RevitReviewRunRepository`，每個 run 存成一個 DataStorage（`BCR.ReviewRun.<runId>`）。共用四個 schema：
  - Run `966fd4d4-3fba-42b3-912e-9cd3d0819302`
  - Result `f45e3cb0-e14b-4958-bbcf-d15cb26b84be`
  - Value `f00e87ac-3314-4fc7-a449-4fb6821a155e`
  - Override `61936165-bf36-482a-8379-20605b78d02d`

  每個屬性都是獨立欄位，巢狀資料用 sub-entity，不存成 JSON blob。寫入需要呼叫端開啟 transaction。
- 診斷：新增 `ReviewStage.Review`（開始檢討），以及錯誤碼 `BCR-OVR-001` 人工覆寫不成立、`BCR-OVR-002` 人工覆寫需重新確認、`BCR-RUN-001` 檢討紀錄無法讀取（保留給 P3-T09 讀取失敗時使用）。

## 失效規則（spec 13.1）

| 變更 | 判定 | 失效範圍 |
| --- | --- | --- |
| 規則集 ID 或版本不同 | InvalidatesAll | 全部結果 |
| 環境事實變更（來源視圖、樓層、Area Scheme、Phase、Design Option、容差、單位）或建築物輸入變更 | context 指紋不同 | 全部結果 |
| 套件 BoundaryRevision 與 run 不同 | 區劃邊界已重新套用 | 全部結果 |
| Run 不是 Completed，或沒有 baseline（包含 1.0 紀錄） | 無法證明仍有效 | 全部結果 |
| 元素幾何、Type、Structural／帷幕牆、與區劃的關係與量測、Type 防火時效、開口防火屬性、Host 變更 | subject 指紋不同 | 以該元素為 subject 的結果 |
| Area 面積、外框、放置點、區劃名稱或問題、區劃輸入變更 | `zone:` 與 Area 指紋不同 | 該區劃內的全部結果 |
| 元素被刪除 | RemovedSubjects | 以該元素為 subject 的結果 |
| 新增元素／區劃 | AddedSubjects | Run 標為 Stale（沒有結果可以失效，但 run 已不完整） |

長度與面積都四捨五入到微米。讀取順序不影響指紋，所以同一個模型重讀，會得到相同的 baseline。

## 人工覆寫（spec 11.8）

- 必須填寫原因與操作者，覆寫狀態不可等於計算狀態，也不可為 NotRun；只有 Completed 的 run 可以覆寫。傳入 freshness 時，已失效的結果不可覆寫。
- 稽核紀錄只增不改：再次覆寫、重新確認或撤回，都會把前一筆標為 Superseded（附 StandingReason），同一結果最多只有一筆非 Superseded 的紀錄。
- `EffectiveStatus`：只有 Active 覆寫會取代計算狀態；NeedsReconfirmation 狀態下顯示計算結果。
- 模型或規則變更後，`WithOverridesSuspended` 會把失效結果上的 Active 覆寫改為 NeedsReconfirmation。`Reconfirm` 會新增一筆 Active 紀錄，原因可以沿用或重寫。
- 新 run 的 `CarryOver`：依 check type、ruleId、zoneId 與排序後的 subjects 配對結果。
  - 規則版本、相依指紋與計算狀態都相同時 → Kept。
  - 任一項不同，或前次已是 NeedsReconfirmation → NeedsReconfirmation，並說明原因。
  - 沒有對應結果、新結果已有自己的覆寫，或新計算結果已等於覆寫狀態 → Dropped。

## 驗證

- Solution build 與 `-t:Rebuild`，以及外掛 csproj `-t:Rebuild`：各 0 warnings、0 errors。已確認 `RevitReviewRunRepository` 在 Debug `BuildingRegulationReview.Revit.dll` 內。
- Core tests：**889/889 通過**（原 857，新增 32，全部在 `Reviews/ReviewRunValidityTests.cs`）。測試用 FireResistance 與 OpeningProtection 兩個 check 對固定模型跑出真實 run，涵蓋：
  - 同模型重讀指紋相同；讀取順序不影響結果。
  - 只有 Type 時效變更的構件失效；移動的牆失效；門參數變更只影響門的結果；刪除元素；新增元素；Area 變更只影響該區劃。
  - 規則版本變更、規則集變更、Phase 變更、邊界版次變更、沒有證據、未完成的 run，全部失效。
  - 套件狀態轉換；log 階段與錯誤碼。
  - 覆寫的稽核欄位、拒絕條件、失效後不可覆寫、再次覆寫與撤回保留紀錄、模型變更後暫停覆寫並重新確認。
  - CarryOver 的 Kept／規則版本變更／證據與結論變更／Dropped。
  - JSON 儲存來回（證據、覆寫全部欄位），讀回後仍為 fresh，改模型後變 stale；1.0 紀錄讀成沒有證據；壞紀錄拒絕讀取；Domain 不變式。
- **未實機驗證**：`RevitReviewRunRepository` 沒有在 Revit 中存檔、重開、讀回。目前沒有指令或 UI 呼叫它（P3-T09 整合）。「重開模型可讀」目前只由 storage record 來回測試保證，Extensible Storage 的實際寫讀要在 P3-T09 的實機驗收中確認。

## 設計決策

- 證據用指紋，不存原始值：結果本身的 evidence 已保存可追溯的數值（spec 18 第 4 點），指紋只負責回答「有沒有變」。這樣 run 的大小與元素數量成正比，而且 Application 以後擴大指紋涵蓋範圍時，不需要遷移 schema。
- 規則版本不放進 context 指紋，而是單獨比較，這樣使用者看到的是「規則版本已從 X 更新為 Y」，而不是籠統的「環境變更」。
- baseline 只由純資料（CandidateSet + inputs + 環境事實）計算，Revit 端不需要新的讀取器。P3-T09 用同一組 reader 重新讀模型，再呼叫 `Build`，就能得到 current baseline。
- `ReviewResult.reviewedBy/reviewedAtUtc` 維持 P3-T01 的語意，不由覆寫流程寫入；覆寫者資訊以 `ReviewOverride` 為準。
- 撤回沒有獨立的操作者欄位，撤回者、時間與原因寫在 Superseded 紀錄的 StandingReason 裡。
- run 全部保留，不自動刪除舊 run；保留政策留待 P3-T08／T09 決定。

## 未解決問題

- spec 19 第 2、4、5、6、7 項仍未定（條文、參數 GUID、時效型態、輸入來源、容差）。
- `ReviewEnvironment` 的事實要由 adapter 從 Revit 讀取（Phase、Design Option、單位），尚未實作，屬於 P3-T09。
- 讀取損壞 run 時的 UI 呈現（`BCR-RUN-001`）屬於 P3-T09。
