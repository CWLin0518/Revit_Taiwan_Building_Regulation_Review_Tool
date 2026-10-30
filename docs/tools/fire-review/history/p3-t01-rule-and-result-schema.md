# P3-T01 — 規則與結果 Schema

## 完成範圍

- Domain `Rules`（全新，不相依 Revit／UI）：
  - `RuleCategory`（CompartmentArea／FireResistance／OpeningProtection）、`RuleSeverity`（Error／Warning／Info）。
  - `RuleExpression`：受限 DSL 的**原始文字**，只保證不空白；解析、白名單欄位與單位是 P3-T02 的工作。
  - `Rule`：spec 11.2 的全部欄位（ruleId、version、category、legalReference、effectiveDate、jurisdiction、priority、appliesWhen、requiredValue、exemptions、evidenceFields、severity）。
  - `RuleSet`：`ruleSetId + version` 為識別，一個規則集對同一 ruleId 只鎖定一個版本；`Find`、`OfCategory`。
- Domain `Reviews`（全新）：
  - `ReviewStatus` 六態（NotRun／Pass／Fail／InsufficientData／NotApplicable／ManualReview）＋ `ReviewStatusText.Label`（未檢討／符合／未符合／資料不足／不適用／人工覆核）。
  - `ReviewValue`：數量（有明確 `ReviewUnit`：None／SquareMeter／Meter／Minute／Count）、文字、布林三種。
  - `ReviewEvidence` / `ReviewEvidenceItem`：欄位唯一、保留記錄順序。
  - `ReviewResult`：spec 11.3 全部欄位。
  - `ReviewRun`：runId、packageId、鎖定的 ruleSetId／ruleSetVersion、boundaryRevision、開始／結束時間、`ReviewRunState`（Running／Completed／Cancelled／Failed）、結果集合；`Complete(...)`。
- Application `Rules`：`RuleSetDocument` / `RuleDocument`（作者撰寫的扁平形狀，列舉用名稱）、`RuleSetSchemaValidator`（一次列出所有問題，含 JSON 風格路徑與穩定 issue code）、`RuleSetDocumentMapper`（先驗證再建 Domain，失敗回 `Result` 並帶 `BCR-RULE-005`）。
- Application `Reviews`：`ReviewRunStorageRecord` 系列 + `ReviewRunStorageMapper`。
- `ReviewErrorCode.RuleSchemaInvalid = "BCR-RULE-005"`（規則格式不符合 schema）。
- 測試 fixture：`tests/BuildingRegulationReview.Core.Tests/Rules/Fixtures/`（1 個有效、4 個無效）。

## 驗證

- Solution build 與外掛 csproj `-t:Rebuild`：各 0 warnings、0 errors。
- Core tests：**565/565 通過**（P3-T01 前 517，新增 48）。
  - `RuleSetSchemaTests`：有效 fixture 載入全部欄位；四個無效 fixture 逐一比對 (path, code) 清單；載入失敗時一個錯誤列出全部問題；null 文件／null 規則；缺值 vs 未知值的區分；未知 JSON 屬性拒絕反序列化；JSON 來回不變。
  - `RuleTests`：Rule／RuleSet／RuleExpression 的不變式。
  - `ReviewResultTests`：六態標籤、Pass／Fail 必須帶兩個值、資料不足可無實際值、非通過必須有原因、主體或區劃必填、覆核者與時間成對、值與證據。
  - `ReviewRunTests`：狀態與時間一致性、結果歸屬與唯一、六態結果 JSON 來回、未知 schema、數字或大小寫錯誤的狀態名稱、無法偷渡沒有要求值的 Fail。

## 設計決策

- **資料不足不可能被誤判（spec 18 第 5 項）靠建構子保證**：Pass／Fail 缺實際值或規定值一律拋例外，這種情況只能是 InsufficientData。儲存紀錄讀回時一樣經過建構子，所以被竄改的紀錄也進不來。
- Fail／InsufficientData／ManualReview 必須有 message（spec 12.1「顯示明確原因」）；Pass／NotApplicable／NotRun 可省略。
- 除 NotRun 外，結果至少要有 zoneId 或一個 subject UniqueId，才可追溯（spec 18 第 4 項）。
- 規則檔的列舉必須完整拼出名稱（大小寫敏感、不接受數字），因為 `Enum.TryParse` 會接受 `"1"` 與 `"Error, Info"`。儲存紀錄的狀態也用名稱不用數字，避免列舉重排時「符合」悄悄變成別的狀態。
- 驗證器只檢查**結構**；表達式能否解析、欄位是否在白名單、規則之間是否衝突留給 P3-T02。所以 fixture 裡的表達式文字只是示意，P3-T02 定案 DSL 後可能要跟著改。
- Application 維持無序列化器相依（與既有 StorageRecord 模式一致）；測試用 `System.Text.Json`（camelCase＋拒絕未知屬性）示範規則檔的讀法。正式的規則檔讀取位置與來源尚未決定。
- `ReviewRun` 存 `boundaryRevision`，給 P3-T07 失效判斷用；spec 11.3 的 `reviewedBy/reviewedAtUtc` 已在 schema 中，但人工覆寫流程（原因、原始結果、覆寫結果）是 P3-T07。
- 六態彙總（spec 11.7 總狀態規則）是 P3-T08，這裡沒做。

## 未解決問題

- 規則集的實際來源（外掛內建 JSON、專案 DataStorage 或外部路徑）未定；spec 19 第 2 項（採用條文、版本、生效日期）仍待確認，fixture 的條文內容只是測試資料，不是正式規則。
- `ReviewUnit` 只有五個單位，P3-T02 單位處理若需要更多再擴充。

## 下一階段目標（P3-T02）

- 實作受限規則引擎：白名單欄位、Expression DSL、優先序、豁免、單位與衝突偵測。
- 退出條件：禁止任意程式碼；邊界值、衝突、缺資料測試通過。
