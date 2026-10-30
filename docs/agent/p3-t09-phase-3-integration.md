# P3-T09 — 整合 Phase 3 與驗收

Phase 3 的最後一項任務：把前置檢查、三類檢討、存讀、失效、人工覆寫、檢討視圖標示、取消／Rollback、日誌與效能串成一個指令。
spec 第 11 節與 16.3 的逐條對照見 `docs/agent/phase-3-acceptance.md`。

## 完成範圍

### Application `Reviews`（純資料，core tests 覆蓋）

- `ReviewInputSources.cs`：規則欄位 → 參數來源目錄（spec 19 第 4～6 項未定，先以名稱讀取）：
  | 欄位 | 參數 | 層級 | 類別 |
  | --- | --- | --- | --- |
  | `building.fireResistiveConstruction`／`use`／`floorsAboveGround`／`height` | `防火檢討_防火構造建築物`／`防火檢討_建築物用途類組`／`防火檢討_地上層數`／`防火檢討_建築物高度` | 實體 | 專案資訊 |
  | `zone.use`／`sprinklered`／`floorNumber` | `防火檢討_區劃用途`／`防火檢討_自動滅火設備`／`防火檢討_所在樓層序` | 實體 | 面積（工具寫入的 Area） |
  | `element.providedFireRating` | `防火檢討_設計防火時效` | 類型 | 牆、柱、梁、樓板 |
  | `opening.providedFireProtection` | `防火檢討_設計防火保護` | 實體或類型 | 門、窗、帷幕嵌板 |
  - `FieldsUsedBy(ruleSet)`：規則的適用條件、要求值兩側、豁免用到的欄位（只出現在 evidenceFields 的不算）；`NeededBy(ruleSet)`：規則需要的參數。
- `ReviewParameterSnapshot.cs`：`ParameterReading`（Absent／Empty／Text／Integer／YesNo／Number／Length，adapter 只換長度單位）、
  `ReviewParameterSnapshot`（綁定類別＋專案資訊值＋各元素值）、`ReviewInputAssembler`：
  - 是非欄位：YesNo／整數 1、0，或文字（是 有 yes…／否 無 no…）；其他 → Unreadable（不當 false）。
  - 數值：整數／數字／可解析文字（全形可）；高度可為長度參數或 `45.5 m`／`45.5公尺`。
  - 同一區劃多個 Area 的值必須一致（空白也算不一致）→ 否則 Unreadable「填寫不一致」。
  - Type 時效：文字走 `FireRatingText.Parse`，數值走 `FromNumber`；是非參數 → Unreadable；沒有參數／未填 → Missing。
  - 門窗：實體值優先，沒有實體值才用類型值。
- `ReviewReadiness.cs`（spec 11.1）：`ReviewReadinessInput` → `ReviewReadinessReport`（Blocking／Warning／Info，每項附修正方式、`ToLog`）。
  | 條件 | 阻擋 | 提醒／資訊 |
  | --- | --- | --- |
  | 工作包 | 沒有 Area Plan；Setup／BoundaryDraft／Error；P2 失效探測有任何邊界／面積原因（`BCR-STALE-001`） | Stale 但邊界未過期 → 可重新檢討 |
  | 規則集 | 無法載入（`BCR-RULE-001/005`）；鎖定版本與外掛提供者不同且未勾「改用目前規則版本」（`BCR-RULE-004`） | 首次檢討將鎖定；勾選改用後為提醒；缺某類規則 |
  | 專案設定 | — | Design Option、連結模型（`BCR-ENV-001`）；Phase／單位資訊 |
  | 必要參數 | 規則需要的參數完全沒綁到應有類別（`BCR-PARAM-001`，附「管理 > 專案參數」加法） | 有候選元素的類別沒綁 → 該類資料不足 |
  | 空間關係 | 候選讀取失敗、沒有區劃、區劃完全沒有封閉面積（`BCR-CAND-002`） | 部分未封閉／重疊區劃；歧義數量；reader 警告 |
- `FireReviewRunner.cs`：`FireReviewRequest` → `FireReviewOutcome`（Completed／Cancelled／Failed）。
  - 依序跑區劃面積 → 構件防火時效 → 防火門窗 → baseline 與覆寫沿用；每一步之間是安全點，檢查 `CancellationToken` 並回報 `FireReviewProgress`。
  - 取消 → 不產生 run、套件不變（`BCR-RUN-002`）；任一檢查拒絕 → 整批 Failed，不存半套結果。
  - 完成 → `ReviewRun.Complete(…, baseline)`；前次 Completed run 有覆寫時 `ReviewOverrides.CarryOver`；套件 `WithReviewRun`（Reviewed、鎖定規則集／版本、LastReviewRunId，BoundaryRevision 不動）。
  - 日誌五類，每一類都有自己的碼（驗證清單 C-01）：檢查警告（`BCR-RUN-006`）／檢查項目未執行（`BCR-RUN-007`）／檢查項目摘要（`BCR-RUN-008`）、結果（含元素 UniqueId；檢查給了自己的錯誤碼就用那個碼，否則 `BCR-RUN-005`）、覆寫沿用（`BCR-OVR-003`）／需重新確認（`BCR-OVR-002`）／不再沿用（`BCR-OVR-004`）、檢討摘要（`BCR-RUN-003`，整次檢討只有這一筆）、效能（`BCR-PERF-001`／`BCR-PERF-002`）。
    - severity 的依據是「這一筆是不是工具遇到的麻煩」，不是判定好壞：結果自己帶錯誤碼就是某個檢查在抱怨它讀到的輸入（缺參數、時效讀不出來、幾何歧義、面積來源無法確認），不論最後判成資料不足、人工覆核或未符合，一律 Warning；沒有自己錯誤碼的結果純粹是判定（實務上就是未符合），記 Info，由檢討表負責統計。這樣 `ReviewLogDigest` 依嚴重度取樣的前 20 筆才會先端出資料問題，而不是被幾百筆未符合擠滿——反過來綁在「未符合」上等於沒有任何資料問題進得了 warnings，因為所有會賦碼的分支結論都是資料不足／人工覆核／不適用，從來不是未符合。
    - 刻意的例外：檢討摘要（`BCR-RUN-003`）仍依 `Verdict == Fail ? Warning : Info`。它整次檢討只有一筆，不會淹沒任何東西；維持 Warning 是為了保證它不被幾百筆 Info 從 digest 的前 20 筆擠出去——這一筆是整份日誌的入口，不是「工具健康度」的一部分。
  - `ReviewPerformance`（spec 15）：5,000 個候選以內前置掃描 10 秒、檢討 30 秒；超過 5,000 按比例放寬；超標 → `BCR-PERF-001` 警告並附各階段毫秒數，未超標 → `BCR-PERF-002` 效能紀錄。
- `StoredRunInspection.cs`：重開模型時以目前 baseline 判定最新 run（`ReviewRunValidity`）、暫停失效結果上的覆寫、Reviewed → Stale，回傳要不要寫回。
- `ReviewErrorCode`：`BCR-PRE-001/002`、`BCR-ENV-001`、`BCR-RUN-002/003/004/005/006/007/008`、`BCR-OVR-003/004`、`BCR-PERF-001/002`。

### Domain

- `ReviewPackage.WithReviewRun(ruleSetId, version, runId, status)`；儲存欄位本來就有，不需遷移。

### Revit（唯讀 reader）

- `RevitReviewParameterReader`：`ParameterBindings` → 綁定類別（`Category.BuiltInCategory` 對應）；專案資訊、Area、構件 Type、門窗實體與 Type 的值。YesNo 以 `SpecTypeId.Boolean.YesNo` 辨識，長度以 `SpecTypeId.Length` 換公尺。
- `RevitReviewEnvironmentReader`：`ReviewEnvironment`（來源視圖、樓層、Area Plan 的 Area Scheme、來源視圖 Phase、主要設計選項、候選容差設定、專案長度／面積單位）與 `ReviewModelConditions`。

### 外掛（WPF）

- `Data/fire-review-rules.json`：外掛內建規則集 `tw-bcr-fire 2026.0-provisional`（面積、牆、樓板、邊界開口四條；**暫定示意**，待 spec 19 第 2 項）。測試專案以連結檔直接驗證它可編譯。
- `FireReview/FireReviewRuleSetSource`：DataContractJsonSerializer 讀檔 → `RuleSetCompiler.Load`；缺檔 `BCR-RULE-001`、壞 JSON／schema `BCR-RULE-005`。
- `FireReview/FireReviewModel`：API context 內的操作。
  - `Scan`：P2 失效探測（變更即存）→ 規則 → 候選 → 參數 → 環境 → 前置檢查 → 輸入；讀最新 run 並 `StoredRunInspection`（需要時寫回覆寫暫停與 Stale）；量前置掃描時間。
  - `Save`：一個 TransactionGroup「防火區劃檢討」＝存 run＋套件，再 `RevitReviewViewMarker.Mark`（依 EffectiveStatus）；例外 → 整批 RollBack（`BCR-RUN-004`）；標示整批失敗時結果仍保存並提示「重新標示」。
  - `Locate`：切到檢討視圖（有的話）、選取並 `ShowElements`。
- `FireReview/FireReviewWindow`（非模態）：前置檢查清單（✖／▲／・＋修正方式）、「改用目前規則版本」勾選、重新檢查／開始檢討／取消／進度列；
  檢討表 TreeView（三列 → 區劃／類別＋Type／門窗幕牆 → 逐項），右側明細（實際值、規定值、條文、規則版本、說明、元素、覆寫與歷史、證據）；
  定位、人工覆寫／重新確認／撤回（原因必填，操作者取 Revit 使用者名稱）、重新標示、儲存日誌（我的文件 `防火區劃檢討日誌_*.txt`）。
  檢查在背景執行緒跑純資料（spec 15），所以取消按鈕在檢討中可用。
- `FireReview/FireReviewOverrideDialog`、`FireReviewCommand`（佇列式 ExternalEvent）、ribbon「防火區劃檢討」。

## 設計決策

- **輸入來源先用參數名稱**：spec 19 第 4～6 項未定；名稱與 P3-T05／T06 已用的 `BCR_Provided*` 一致，並允許文字／整數／是非三種型態。之後定案 GUID 只需換 reader。
- **「必要參數」由規則決定**：只要求規則實際讀取的欄位有參數；規則換了，要求跟著換。參數存在但部分類別沒綁只提醒（那些元素會是資料不足，不會誤判）。
- **規則版本鎖定需使用者確認**：外掛版本與套件鎖定版本不同時先阻擋；勾選改用後才以新版本檢討並鎖定，舊 run 以 spec 13.1 標為需更新。
- **儲存與標示同一個 undo**：run、套件與檢討視圖標示在同一個 TransactionGroup；標示整批失敗不撤銷已儲存的結果（避免標示的持續性問題讓結果永遠存不進去），並提示重新標示。
- **既有 run 在開窗時就判定失效並寫回**，下一個 session 不會把過期結果當成目前結果。
- **不寫任何設計參數**：reader 全部唯讀；`防火檢討_法規要求防火時效` 仍不寫回（spec 11.5 第 4 點「若需要」，未要求）。

## 驗證

- `dotnet build BuildingRegulationReview.sln -t:Rebuild`：0 warnings、0 errors；外掛 csproj `-t:Rebuild`：0 warnings、0 errors。
- Core tests：**982/982 通過**（原 931，新增 51，`Reviews/FireReviewIntegrationTests.cs`）。
- 無頭驗證（PowerShell 5.1，載入外掛實際的 net48 Debug 輸出）：`FireReviewRuleSetSource.Load` 讀內建規則成功（4 條規則、面積規則 1 條豁免）；缺檔 → `BCR-RULE-001`；壞 JSON → `BCR-RULE-005`；schema 錯誤 → `BCR-RULE-005`「1 處不符合 schema」。
- **未實機驗證**：本 session 的 Revit MCP 沒有連線，新指令需重新部署並重開 Revit。實機清單見 `phase-3-acceptance.md` 最後一節。

## 已知限制

- 內建規則為暫定示意（spec 19 第 2 項）；柱、梁沒有規則，結果為不適用。
- 檢討期間若使用者在 Revit 修改模型，儲存時不會重新比對；下次開窗的失效判定會把它標為需更新。
- 連結模型、非主要設計選項不讀取（MVP，前置檢查提醒）。
- 構件 Type 時效只讀 `防火檢討_設計防火時效`，不讀 Revit 內建「Fire Rating」參數；裸數字一律視為分鐘。
- run 全部保留，沒有保留政策；覆寫、重新標示會重存整個 run。
