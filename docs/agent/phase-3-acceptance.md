# Phase 3 驗收紀錄（P3-T09）

逐條對照 spec Draft v1.1 第 11 節、第 13～15 節與 16.3 節中和 Phase 3 有關的部分。**自動驗證**欄列出
`tests/BuildingRegulationReview.Core.Tests/` 裡可以重跑的測試，**狀態**欄就是這些測試的結果。需要在
Revit 2024 開著 `建築防火檢討1.rvt` 才能確認的部分列在後段；2026-09-23 已完成部署、參數綁定與時效推定的實機驗證，記錄在最後一節，其餘仍為「未回報」。

## 自動驗證總結

- `dotnet build BuildingRegulationReview.sln -t:Rebuild`：0 warnings、0 errors。
- `dotnet build src/BuildingRegulationReview/BuildingRegulationReview.csproj -t:Rebuild`：0 warnings、0 errors。
- `dotnet test tests/BuildingRegulationReview.Core.Tests`：**1084/1084 通過**（Phase 3 開始前 517，Phase 3 新增 567）。
- ⚠️ 本環境的增量建置曾兩次對編譯錯誤回報「建置成功」；驗證請用 `dotnet clean` + `build` 或 `-t:Rebuild`。
- 無頭驗證：以 PowerShell 5.1 載入外掛 net48 輸出，內建規則檔經 DataContract 載入器讀取與編譯成功；缺檔、壞 JSON、schema 錯誤都回報正確錯誤碼。

## 第 11 節

| 條文 | 內容 | 自動驗證 | 狀態 |
| --- | --- | --- | --- |
| 11.1 | 工作包 Ready 且 Area／Boundary 未過期 | `FireReviewIntegrationTests.Package_that_is_not_ready_blocks_with_a_fix`、`Package_without_area_plan_blocks`、`Stale_boundaries_block_every_status_and_each_reason_is_listed`、`Stale_results_on_fresh_boundaries_can_be_reviewed_again` | 通過 |
| 11.1 | 規則集存在且版本已鎖定 | `Rule_set_that_cannot_load_blocks`、`Locked_version_that_differs_blocks_until_the_user_accepts_the_update`、`Same_locked_version_needs_no_confirmation`、`Ready_package_…_locks_the_rule_set_the_first_time`、`Rerun_after_accepting_the_new_rule_version_locks_it` | 通過 |
| 11.1 | Area Scheme、單位、Phase、Design Option、Link 狀態 | `Design_options_and_links_are_warnings_and_project_facts_are_reported`；Area Scheme 由 P2 失效探測（`ReviewStalenessTests`）納入邊界過期 | 通過（Revit 讀取未實機） |
| 11.1 | 必要參數存在且可讀 | `Parameter_the_rules_need_but_the_project_lacks_blocks_with_how_to_add_it`、`Parameter_bound_to_some_candidate_categories_only_warns_about_the_rest`、`Evidence_only_fields_do_not_require_parameters`、`Unclear_values_are_unreadable_never_false_or_zero` | 通過 |
| 11.1 | 來源元素與區劃空間關係可解析 | `Candidates_that_cannot_be_read_or_have_no_enclosed_zone_block`；P3-T03 `CandidateResolverTests` | 通過 |
| 11.1 | 不成立時停止並列出修正方式 | 上列各項都斷言 `Fix`；`Readiness_log_carries_code_stage_and_fix_and_ends_with_the_verdict` | 通過 |
| 11.2 | 規則資料模型、白名單 DSL | P3-T01／T02 `RuleSetSchemaTests`、`RuleExpressionTests`、`RuleEngineTests`、`RuleSetCompilerTests`；`Shipped_rule_file_compiles_and_covers_all_three_checks` | 通過 |
| 11.3 | 六態、資料不足不誤判 | P3-T01 `ReviewResultTests`；`Missing_building_input_is_insufficient_data_everywhere_and_never_a_fail` | 通過 |
| 11.4 | 區劃面積：Revit Area、交叉驗證、條文 | P3-T04 `CompartmentAreaCheckTests`（32）；`Review_verdicts_follow_the_model_parameters`、`Area_exactly_at_the_limit_passes_and_one_over_fails` | 通過 |
| 11.4-4 | 未符合區劃紅色 Filled Region，含 Package／Run／Zone ID | P3-T08 `ReviewMarkupTests` | 通過（Revit 端未實機） |
| 11.5 | Required／Provided 分離、Type 彙總、缺值 | P3-T05 `FireResistanceCheckTests`（94）；`Assembly_fills_building_zone_type_and_opening_inputs_from_the_snapshot`、`Ratings_and_protection_are_read_without_guessing` | 通過 |
| 11.5-6 | 未符合元素 By Element Override，保存原狀態 | P3-T08 `ReviewMarkupTests` | 通過（Revit 端未實機） |
| 11.6 | 門窗：先判斷適用再看是／否／未設定 | P3-T06 `OpeningProtectionCheckTests`（48）；`Instance_protection_wins_and_type_protection_fills_the_rest`、`Review_verdicts_follow_the_model_parameters` | 通過 |
| 11.7 | 檢討表三列、統計、總狀態規則 | P3-T08 `ReviewTableTests`（28）；`Review_runs_all_three_checks_…`（三列皆非未檢討） | 通過（WPF 未實機） |
| 11.8 | 人工覆寫：原因、操作者、時間、原始／覆寫結果；變更後需重新確認 | P3-T07 `ReviewRunValidityTests`；`Override_is_kept_by_an_identical_rerun_and_needs_reconfirmation_when_its_evidence_moves`、`Changed_parameter_makes_the_touched_results_stale_and_suspends_their_overrides` | 通過（WPF 未實機） |

## 第 13～15 節（與 Phase 3 有關）

| 條文 | 內容 | 自動驗證 | 狀態 |
| --- | --- | --- | --- |
| 13.1 | 參與元素、參數、規則版本、Phase 變更 → Stale | `Changed_parameter_…`、`Rule_version_update_makes_every_old_result_stale`、`Project_phase_change_invalidates_the_whole_run`、`Reopened_model_that_did_not_change_keeps_the_run_fresh`；P3-T07 `ReviewRunValidityTests` | 通過 |
| 13.2 | 只更新本套件受管理元素、重跑不增加 | P3-T08 `ReviewMarkupTests`（同 run 標示三次不增加、新 run 接手）；`Running_three_times_gives_the_same_results` | 通過（Revit 端未實機） |
| 13.2 | 取消、進度、TransactionGroup Rollback | `Cancelling_before_the_first_check_stores_nothing`、`Cancelling_at_a_safe_point_between_checks_stops_there`、`A_check_that_refuses_fails_the_whole_run_without_a_partial_one`；Revit 端 `FireReviewModel.Save` 單一 TransactionGroup | 通過（Rollback 未實機） |
| 14 | 錯誤碼、階段、Package ID、UniqueId、訊息、技術細節、建議、時間 | `Readiness_log_…`、`Performance_over_target_logs_a_diagnosis`；P2 `ReviewLogTests` | 通過 |
| 15 | 背景只處理純資料、分段進度、效能目標與診斷、新增／更新／刪除／略過數量 | `Performance_*`（3）、`Run_reports_the_candidate_count_and_prescan_time`、`Review_runs_all_three_checks_…`（進度四段）；標示數量見 `ReviewMarkupResult.Summary` | 通過（實際耗時未量測） |

## 第 16.3 節：驗收模型情境（與 Phase 3 有關）

| # | 情境 | 自動驗證 | 狀態 |
| --- | --- | --- | --- |
| 5 | 區劃面積剛好等於法規上限 | `Area_exactly_at_the_limit_passes_and_one_over_fails`（內建規則 1500 → 符合、1500.5 → 未符合）；P3-T04 邊界值 | 通過 |
| 6 | 構件參數缺值、錯誤單位及同 Type 多 Instance | `Review_verdicts_follow_the_model_parameters`（樓板無時效 → 資料不足）、`Ratings_and_protection_are_read_without_guessing`；P3-T05 缺值／格式錯誤／Type 彙總 | 通過 |
| 7 | 邊界牆含一般門窗、幕牆門與非 Hosted 開口 | `Review_verdicts_follow_the_model_parameters`（門是 → 符合、窗否 → 未符合、帷幕嵌板與非 Hosted → 人工覆核）；P3-T06 固定模型 | 通過 |
| 8 | 規則版本更新後舊結果變成 Stale | `Rule_version_update_makes_every_old_result_stale`、`Locked_version_that_differs_blocks_…`（圖紙部分屬 Phase 4） | 通過（檢討部分） |
| 10 | 重跑三次後元素數量不增加 | `Running_three_times_gives_the_same_results`；P3-T08 標示三次不增加 | 通過（Revit 端未實機） |

## Revit 實機驗收（部分完成，見文末實機驗收紀錄）

前置：重新部署外掛（`scripts/redeploy.bat`）後重開 Revit 2024，開 `建築防火檢討1.rvt` **並先備份**；套件需已由「防火區劃編輯器」套用到「可開始檢討」。
依規則，專案需有 `防火檢討_防火構造建築物`（專案資訊，是非）、`防火檢討_自動滅火設備`、`防火檢討_區劃用途`（面積）、
`防火檢討_設計防火時效`（牆／柱／結構柱／樓板**類型**）、`防火檢討_設計防火保護`（門／窗／帷幕嵌板**實體**）。

參數定義放在 `assets/SharedParameters/fire-review-shared-params.txt`（共 11 個，GUID 固定；檔案為 Big5／cp950
編碼，改存 UTF-8 會讓 Revit 讀到亂碼的參數名）。其中牆／柱／結構柱／樓板的 `防火檢討_設計防火時效`、`結構材料`、
`防火被覆厚度`（皆類型）與門／窗／帷幕嵌板的 `防火檢討_設計防火保護`（實體）可用 revit-mcp 的
`load_shared_parameters` 綁定，分別由 `fire-review-members-type.txt`、`fire-review-openings-instance.txt` 載入；
**面積與專案資訊兩類 MCP 不支援**，要在「管理 > 專案參數 > 加入 > 共用參數」選上面的主檔手動加：
面積勾 `防火檢討_區劃用途`、`防火檢討_自動滅火設備`、`防火檢討_所在樓層序`，專案資訊勾
`防火檢討_防火構造建築物`、`建築物用途類組`、`地上層數`，都綁在「實體」。
`防火檢討_法規要求防火時效` 保留給未來回寫，現在不必綁。

本輪變更：`建築物用途類組` 與 `地上層數` 去掉 `防火檢討_` 前綴（GUID 不變）；`防火檢討_建築物高度` 移除，
改由 `RevitBuildingHeightReader` 量測模型（最低 Level 到最高構件頂端）；`防火檢討_設計防火時效` 不再綁
**結構構架（梁）**——第71～73條的「樑」款未設尺寸門檻，無法由斷面推定。新增 `結構材料`（RC／SRC／SC）與
`防火被覆厚度`（僅 SC）供「防火參數批次設定」面板依第71～73條推定時效。使用者手冊：
`C:\Users\User\Desktop\防火檢討_Revit參數設定清單.md`。

| # | 步驟 | 預期 | 狀態 |
| --- | --- | --- | --- |
| R1 | 參數都還沒加時按「防火區劃檢討」 | 前置檢查列出 ✖「規則需要…但專案沒有參數…」與加法，「開始檢討」不可按 | 未回報 |
| R2 | 加上參數、專案資訊勾防火構造、面積填灑水，按「重新檢查」 | 前置檢查通過，顯示「首次檢討，將鎖定規則集 tw-bcr-fire 版本 2026.0-provisional」與前置掃描秒數 | 未回報 |
| R3 | 按「開始檢討」 | 進度列四段；完成後三列都有結果；Revit 出現「{AreaScheme}_{平面}_防火檢討」視圖，未符合區劃為紅色斜線、未符合構件／門窗為紅色 | 未回報 |
| R4 | 檢討中按「取消」（大模型較容易按到） | 顯示「檢討已取消…模型未變更」，Revit 復原清單沒有新項目 | 未回報 |
| R5 | 連續檢討三次 | 檢討視圖的 Filled Region 數量不增加；復原清單每次一筆「防火區劃檢討」 | 未回報 |
| R6 | 選一筆項目按「定位」 | 切到檢討視圖、元素被選取並縮放 | 未回報 |
| R7 | 對一筆未符合項目「人工覆寫…」改為符合並填原因 | 明細顯示操作者（Revit 使用者名稱）、時間、原因；檢討視圖該元素紅色被移除 | 未回報 |
| R8 | 存檔、關閉、重開模型，再開「防火區劃檢討」 | 上次的檢討表與覆寫原樣讀回，不需重跑 | 未回報 |
| R9 | 修改 R7 那個元素的 Type 時效，重開檢討視窗 | 該項目標「需更新」、覆寫改為「需重新確認」、總狀態「需更新」；套件狀態變 Stale | 未回報 |
| R10 | 把 `fire-review-rules.json` 的 version 改成 `2026.1-provisional`，重開 Revit 與視窗 | ✖ 規則版本不一致；勾「改用目前規則版本」後可檢討，舊結果全部需更新 | 未回報 |
| R11 | 在編輯器拖動一條區劃邊界後開檢討視窗 | ✖「區劃邊界或面積已過期」，修正方式指向編輯器 | 未回報 |
| R12 | 按「儲存日誌」並開啟檔案 | 每行含時間、嚴重度、錯誤碼、階段、Package ID、元素 UniqueId；技術細節沒有完整路徑 | 未回報 |

## 防火參數批次設定面板（部分實機驗證，見文末）

Ribbon `建築法規檢討` → `防火參數批次設定`。以「類型」為列，收集**目前視圖**可見的牆／柱／結構柱／樓板／
門／窗／帷幕嵌板類型。時效推定表與其條文依據見使用者手冊第 3 節。

| # | 步驟 | 預期 | 狀態 |
| --- | --- | --- | --- |
| P1 | 在含牆柱板的平面按「防火參數批次設定」 | 列出該視圖的類型；「數量」欄顯示「視圖數 / 專案數」；上方黃框提醒這是類型參數 | 未回報 |
| P2 | 對一個 20cm 的 RC 牆類型選 `結構材料 = RC` | 「推定時效」立刻變 `120 min`，滑鼠移上顯示第72條第1款第1目 | 未回報 |
| P3 | 對一個 8cm 的 RC 牆類型選 `RC` | 推定 `60 min`，依據為第73條第1款第1目 | 未回報 |
| P4 | 對一個 6cm 的 RC 牆類型選 `RC` | 推定欄為「—」，依據寫「未達第73條的 7cm 門檻 → 無防火時效」 | 未回報 |
| P5 | 選 `SC` | 「被覆厚度 cm」欄解鎖；未填時推定欄為「—」並提示需填被覆厚度 | 未回報 |
| P6 | SC 牆填被覆 3cm | 推定 `60 min`（第73條第1款第2目），與牆本身厚度無關 | 未回報 |
| P7 | 柱類型選 `RC`，族群 b=40 h=60 | 短邊取 40cm → 推定 `180 min`（第71條第2款） | 未回報 |
| P8 | 多選數列按「選取列設為…」選 RC | 選取列的結構材料一次設定完成，推定欄同步更新 | 未回報 |
| P9 | 按「套用推定值（全部）」 | 「設計防火時效」欄填入推定值；狀態列寫「尚未寫入模型」；未推定的列分類統計 | 未回報 |
| P10 | 按「寫入模型」 | 確認視窗說明影響幾個類型、專案中共幾個實體；確定後一個交易寫入，結果摘要列出寫入／未變更／失敗數 | 未回報 |
| P11 | 某類別尚未綁參數時開面板 | 該列整列淡紅，「依據／提醒」欄顯示「缺少參數：…」；寫入時該列回報失敗，其餘仍寫入 | 未回報 |
| P12 | 門窗列改「防火保護」並寫入 | 只影響**目前視圖**的那些實體（實體參數）；「數量」欄只有一個數字 | 未回報 |
| P13 | 同一門類型在視圖中各實體值不一致 | 「防火保護」欄留白；不動它就不會覆蓋任何實體 | 未回報 |
| P14 | 梁（結構構架） | 不出現在面板中，也不需綁 `防火檢討_設計防火時效` | 未回報 |

## 實機驗收紀錄 — 2026-09-23（模型 `建築防火檢討1.rvt`，FL9）

部署：`%APPDATA%\Autodesk\Revit\Addins\2024\BuildingRegulationReview\` 四個 DLL 為 2026-09-23 14:03:52–55，
部署版 `BuildingRegulationReview.Revit.dll` 含 `柱寬`／`柱深`／`結構材料`／`防火被覆厚度` 字串；
Revit 行程 18:00:37 啟動，晚於部署，載入的確為含 `762787f` 的版本。

### 參數綁定（以 `get_category_fields` 回讀）

| 品類 | 參數 | 層級 | 結果 |
| --- | --- | --- | --- |
| 專案資訊 | `防火檢討_防火構造建築物`、`建築物用途類組`、`地上層數` | 實例 | 通過 |
| 面積 | `防火檢討_區劃用途`、`防火檢討_自動滅火設備`、`防火檢討_所在樓層序` | 實例 | 通過 |
| 牆／樓板／柱／結構柱 | `防火檢討_設計防火時效`、`結構材料`、`防火被覆厚度` | 類型 | 通過（MCP `load_shared_parameters` 綁定） |
| 門／窗／帷幕嵌板 | `防火檢討_設計防火保護` | 實例 | MCP 回報成功；模型門窗數為 0，無法回讀驗證 |

專案資訊與面積兩類 revit-mcp 不支援，由使用者手動加入；其餘四類由 MCP 綁定。

### 推定結果（以模型回讀驗證，非 UI 觀察）

| 類型 | 實體數 | 尺寸來源與值 | 結構材料 | 寫入的時效 | 對應條文 |
| --- | --- | --- | --- | --- | --- |
| `Basic Wall: RC 牆 12cm` | 7 | `Width` = 12cm | RC | `120 min` | 第72條第1款第1目（≥10cm→2hr） |
| `Floor: 通用 - 15cm` | 12 | 複合結構厚 15cm | RC | `120 min` | 第72條第4款第1目（≥10cm→2hr） |
| `混凝土柱-矩形: 30 x 50cm` | 2 | `柱寬`=30、`柱深`=50 → 短邊 30cm | RC | `120 min` | 第72條第2款（短邊≥25cm→2hr） |

三者的推定值與條文門檻完全吻合。柱這一列同時驗證了 `762787f`：中文化柱族群發佈的是 `柱寬`／`柱深`
而非 `b`／`h`，修正前短邊讀不到、推不出時效。

### 檢討輸入值

| 對象 | 參數 | 值 |
| --- | --- | --- |
| 專案資訊 | `防火檢討_防火構造建築物` | Yes（四條規則的 `appliesWhen` 成立） |
| 專案資訊 | `建築物用途類組` | H-2 |
| 區劃 1（No.1，71.59 m²） | `防火檢討_自動滅火設備` | Yes |
| 區劃 3（No.2，38.14 m²） | 同上 | Yes |
| 區劃 2（No.4，8.86 m²） | 同上 | Yes |
| 區劃 4（No.5，5.30 m²） | 同上 | Yes |

`防火檢討_區劃用途` 四個區劃均未填；面積未超限時規則不會用到（樓梯間豁免），暫不影響。

### 對應驗收項狀態更新

| # | 結果 |
| --- | --- |
| P2／P3 類（RC 牆依厚度推定） | **通過**（12cm→120 min，門檻方向正確；8cm→60 min 由單元測試涵蓋） |
| P7 類（柱依短邊推定） | **通過**（30×50 → 短邊 30cm → 120 min；b=40 的 180 min 案例由單元測試涵蓋） |
| P9／P10（套用推定值並寫入模型） | **通過**（三個類型的 `防火檢討_設計防火時效` 已實際寫入） |
| P5／P6（SC 被覆厚度） | 未回報（模型無 SC 構件） |
| P12／P13（門窗防火保護） | 未回報（模型門窗數為 0） |
| R2～R12（檢討執行、標示、覆寫、失效） | 未回報（尚未執行「防火區劃檢討」） |

### 待辦

- 尚未執行一次完整的「防火區劃檢討」。依現有資料推算：四個區劃面積最大 71.59 m²，
  有滅火設備上限 3000 m²，面積檢討應全部符合；牆與樓板 120 min ≥ 規則要求 60 min，應符合；
  門窗無候選元素。總狀態預期為「符合」。
- 要驗到「未符合」路徑，需刻意把某個類型的 `防火檢討_設計防火時效` 改為低於 60 min。
- 開口檢討（R3／R7）需先在區劃邊界牆放置門窗，或改用帷幕嵌板。

---

## 第二輪變更（2026-09-23～24）：第70條、面板三分頁、樓層序推定

### 規則集：2026.0 → 2026.2-provisional

新增第70條主要構造規則四條，與既有第79條防火區劃規則並存：

| ruleId | 對象 | 要求（自頂層起算） | 優先序 |
| --- | --- | --- | --- |
| `tw-bcr-70-column-rating` | 柱（isStructural） | ≤4層 1hr／5～14層 2hr／≥15層 3hr | 20 |
| `tw-bcr-70-beam-rating` | 樑（isStructural） | 同上 | 20 |
| `tw-bcr-70-bearing-wall-rating` | 承重牆壁（isStructural） | ≤14層 1hr／≥15層 2hr | 20 |
| `tw-bcr-70-floor-rating` | 樓地板 | ≤4層 1hr／≥5層 2hr | 20 |
| `tw-bcr-79-wall-rating` | 區劃牆壁（isCompartmentBoundary） | 1hr | 10 |
| `tw-bcr-79-floor-rating` | 區劃樓地板 | 1hr | 10 |

引擎只讓最高優先序中適用的規則決定，因此兩者都涵蓋的元素由第70條決定。此設計
成立的前提是重疊情形下第70條恆不低於第79條（樓地板 1/2/2 對 1；承重牆壁
1/1/2 對 1），已由 `Article_70_governs_a_member_that_article_79_also_reaches_and_never_asks_for_less`
鎖住。非承重的區劃牆壁不屬主要構造，落回第79條，由
`A_non_bearing_compartment_wall_falls_through_to_article_79` 鎖住。

自頂層起算層位 n 的算式（含地下層）：
`n = 樓層序 > 0 ? 地上層數 − 樓層序 + 1 : 地上層數 − 樓層序`

**未納入屋頂**：候選元素解析只蒐集牆、柱、樑、樓板，沒有屋頂品類，加規則不會有
對象。需先擴充 `CandidateCategory` 與 `RevitCandidateObservationReader`。

### 參數變更

- 樑（結構構架）恢復綁定 `防火檢討_設計防火時效`：第70條對樑訂有要求值。先前移除
  是因為第71～73條的「樑」款無尺寸門檻、無法推定設計值，那是面板推定的限制。
- `地上層數`（專案資訊）與 `防火檢討_所在樓層序`（面積）自此為**必要參數**，
  未填時牆、柱、樑、樓板全部判資料不足。

### 面板擴充為三分頁

| 分頁 | 內容 | 層級 |
| --- | --- | --- |
| 構件類型 | 設計防火時效、結構材料、防火被覆厚度；門窗防火保護 | 類型（開口為實體） |
| 區劃 | 區劃用途、自動滅火設備、所在樓層序 | 實體 |
| 專案資訊 | 防火構造建築物、建築物用途類組、地上層數 | 實體 |

三個分頁的變更在同一個交易寫入。面積為唯讀——資料模型不提供寫入途徑。

### 樓層序推定

`LevelFloorNumbering` 由 Level 高程推定：高程 0 以上由下往上 1、2、3…，以下
−1、−2…；同高程視為同一層。面板顯示唯讀的推定值，與手填值不同時標示；
「依樓層填入樓層序」按鈕一次填入所有區劃並帶入地上層數（兩者必須一致，第70條
用它們相除）。

**兩個假設列在面板提示框，不隱含**：以高程 0 為地面（專案基準而非基地地面）；
屋突、女兒牆、結構基準面等非樓層 Level 一樣計入，地上層數可能偏高。

### 驗收項（未實機）

| # | 步驟 | 預期 | 狀態 |
| --- | --- | --- | --- |
| P15 | 面板「區劃」分頁 | 列出全專案的 Area，顯示樓層、面積配置、Revit 量測面積；面積不可編輯 | 未回報 |
| P16 | 滅火設備欄改為「是」 | 同列「適用上限」即時變為「上限 3000 m²」；「否」為 1500；留白為「未填，無法判定上限」 | 未回報 |
| P17 | 按「依樓層填入樓層序」 | 所有區劃填入推定值，專案資訊分頁的地上層數同步帶入；狀態列要求核對 | 未回報 |
| P18 | 手改樓層序與推定值不同 | 該格黃底，提示「目前填的值與依樓層推定的值不同」 | 未回報 |
| P19 | 專案資訊分頁未勾防火構造 | 欄位下方紅字說明整份檢討會判資料不足 | 未回報 |
| P20 | 某類別未綁參數 | 該列／該頁紅底並列出缺少的參數名 | 未回報 |
| P21 | 三分頁都有變更後按寫入模型 | 確認視窗分項列出構件類型／區劃／專案資訊的數量；確定後單一交易寫入 | 未回報 |
| P22 | 樑出現在構件類型分頁 | 結構材料欄停用、推定時效為「—」並說明第71～73條未設尺寸門檻、設計防火時效可輸入 | 未回報 |
| R13 | 填齊參數後跑檢討 | 柱、樑、承重牆、樓地板不再是「不適用」，依第70條得到實際判定 | 未回報 |
| R14 | 規則集版本由 2026.0 升至 2026.2 | 前置檢查顯示版本不一致，勾「改用目前規則版本」後可檢討，舊結果全部需更新 | 未回報 |

---

## 實機檢討結果 — 2026-09-23 17:56（規則集 2026.2-provisional）

第一次跑完整的防火區劃檢討並得到實際判定。RunId `0029e60d-9da1-42a1-89b1-a74f404c3afe`。

### 輸入

| 對象 | 值 |
| --- | --- |
| 防火構造建築物 | Yes |
| 建築物用途類組 | H-2 |
| 地上層數 | 12 |
| 四個區劃所在樓層序 | 9（FL9，由「依樓層填入樓層序」推定） |
| 自動滅火設備 | No（實際無灑水，上限 1500 m²） |

第70條據此得出：自頂層起算 `12 − 9 + 1 = 4` 層 → 不超過第四層 → 柱、樑、承重牆壁、樓地板
皆要求一小時。

### 結果

| 檢討項 | 結果 | 符合 | 未符合 | 待確認 | 不適用 |
| --- | --- | --- | --- | --- | --- |
| 防火區劃面積 | 符合 | 4 | 0 | 0 | 0 |
| 構件防火時效 | 資料不足 | **21** | 0 | 9 | 1 |
| 防火門窗 | 資料不足 | 0 | 0 | 53 | 0 |
| 總狀態 | **待確認** | | | | |

效能：候選元素 68 個；前置掃描 44ms（目標 10s）；檢討 0.1s（目標 30s）。
標示：新增 0、更新 0、刪除 0、失敗 0（沒有未符合項目，無需標示）。

### 驗收項狀態更新

| # | 結果 |
| --- | --- |
| R2 | **通過** — 前置檢查通過，報出 Phase「新營造」、長度 Centimeters、面積 Square meters |
| R3 | **通過（部分）** — 檢討執行完成、三列都有結果；因無未符合項目，紅色標示未驗到 |
| R14 | **通過** — 規則集 2026.2-provisional 生效 |
| P17 | **通過** — 「依樓層填入樓層序」寫入樓層序 9 與地上層數 12 |
| 第70條整體 | **通過** — 構件防火時效由上一版的「柱樑全部不適用」變為符合 21、不適用僅 1 |

### 本次暴露的三個問題

**1. 帷幕牆被 `tw-bcr-79-wall-rating` 要求 60 min（待決策）**

`帷幕牆-150x250cm` 被當成區劃牆壁判定。帷幕牆是外牆，第79條第3項對它的規定是
「外牆面與防火區劃牆壁**交接處之構造**」，而非要求帷幕牆本身具備區劃牆壁時效。

技術原因：規則白名單沒有 `element.isCurtainWall`。`MemberObservation` 其實記錄了
`IsCurtainWall`，只是沒開放給規則引擎。修法是把它加進 `RuleFieldCatalog` 並在
`tw-bcr-79-wall-rating` 的 `appliesWhen` 排除帷幕牆。**條文見解待使用者確認，本輪未動。**

**2. 帷幕嵌板的 53 個「人工覆核」填參數無效（既有設計）**

`OpeningProtectionCheck` 在規則執行前就把帷幕牆上的開口判為 ManualReview
（spec 11.6 第 1 項的 MVP 政策），`防火檢討_設計防火保護` 不會被讀。要改變需先決定
帷幕嵌板的判定政策。

**3. `RC 牆 15cm` 未填設計防火時效 → 資料不足**

模型已變更（新增門窗、區劃 1 面積由 71.59 變為 49.26 m²），使用者先前只填了
`RC 牆 12cm`。屬資料待補，非程式問題。
