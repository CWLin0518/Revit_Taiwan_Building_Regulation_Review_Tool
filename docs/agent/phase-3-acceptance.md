# Phase 3 驗收紀錄（P3-T09）

逐條對照 spec Draft v1.1 第 11 節、第 13～15 節與 16.3 節中和 Phase 3 有關的部分。**自動驗證**欄列出
`tests/BuildingRegulationReview.Core.Tests/` 裡可以重跑的測試，**狀態**欄就是這些測試的結果。需要在
Revit 2024 開著 `建築防火檢討1.rvt` 才能確認的部分還沒有執行，集中列在最後一節，狀態為「未回報」。

## 自動驗證總結

- `dotnet build BuildingRegulationReview.sln -t:Rebuild`：0 warnings、0 errors。
- `dotnet build src/BuildingRegulationReview/BuildingRegulationReview.csproj -t:Rebuild`：0 warnings、0 errors。
- `dotnet test tests/BuildingRegulationReview.Core.Tests`：**1028/1028 通過**（Phase 3 開始前 517，Phase 3 新增 465；P3-T09 新增 51；防火參數批次設定新增 46）。
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

## Revit 實機驗收（未執行）

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

## 防火參數批次設定面板（新，未實機）

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
