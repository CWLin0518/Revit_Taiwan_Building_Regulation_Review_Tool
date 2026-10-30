# 建築防火檢討 — 架構

工具 ID：`fire-review`。規格見 [SPEC.md](SPEC.md)（§5 模組與分層、§6 工作包、§11 檢討、§13 失效）；
各條的細部設計見 [`rules/`](rules/)；Phase 1～3 的任務與交接紀錄見 [`history/`](history/)。

## 1. 動作與入口

| 面板動作 | 原 Ribbon 按鈕（已移除） | Command／視窗 | use case | MCP |
| --- | --- | --- | --- | --- |
| 1. 參數一鍵建立 | 防火參數一鍵建立 | `FireReviewParameterSetupCommand`、`FireReviewParameterSetupWindow` | `FireReviewParameterSetupPlanner`、`RevitFireReviewParameterInstaller` | — |
| 2. 防火區劃設定 | 防火區劃設定 | `FireReviewSetupCommand`、`FireReviewSetupWindow`（modeless） | `ReviewPackageSetup`、`AreaPlanProvisioning` | — |
| 3. 參數批次設定 | 防火參數批次設定 | `FireReviewParameterPanelCommand`、`FireReviewParameterPanelWindow` | `FireReviewParameterDraft`、`RevitFireReviewParameterWriter` | `fire_review_scan_parameters`、`_set_parameters` |
| 4. 開啟防火區劃編輯器 | 防火區劃編輯器 | `RegionEditorCommand`、`RegionEditor/*`（modeless） | `RegionEditorSession`、`RegionSolver`、`ApplyPlan`、`RevitZoneWriteBack` | — |
| 5. 開始檢討 | 防火區劃檢討 | `FireReviewCommand`、`FireReviewWindow`（modeless） | `FireReviewModel`（`AvailablePackages`／`TryScan`／`RunAndSave`）→ `FireReviewRunner` | `fire_review_list_packages`、`_check`、`_run`、`_get_results`、`_describe_result` |

五個動作在檢討面板的 `fire-review` 項目中依序列出（`Features/FireReview/FireReviewFeature.cs`），見 [review-panel SPEC](../review-panel/SPEC.md)。

## 2. 分層對應

| 層 | 專案／目錄 | 內容 |
| --- | --- | --- |
| Domain | `Domain/{Geometry,Regions,ReviewPackages,Reviews,Rules,ProjectSetup}` | 幾何、區劃草稿、套件、結果、規則引擎與運算式 |
| Application | `Application/Geometry` | 線網修復、區域求解、裁切 |
| | `Application/RegionEditing`、`WriteBack` | 編輯器狀態、差異預覽、寫回計畫、色彩方案 |
| | `Application/Candidates` | 區劃邊界候選、帷幕牆曝露與交接、挑空堆疊、樓層帶 |
| | `Application/Checks` | 面積、防火時效、防火門窗、帷幕牆交接、垂直區劃、第 79 條之 1 |
| | `Application/Parameters`、`ProjectSetup` | 參數目錄、用途類組、區劃用途、樓層編號、共用參數 |
| | `Application/Reviews`、`ReviewPackages` | Runner、檢討表、篩選、明細、標示、覆寫、失效 |
| | `Application/Rules` | 規則集文件、Schema 驗證、編譯 |
| Revit Adapter | `Revit/{Candidates,Geometry,Parameters,ProjectSetup,ReviewPackages,Reviews,WriteBack,Versioning}` | 讀取模型、寫回 Area、標示檢討視圖、Extensible Storage |
| 外掛 | `src/BuildingRegulationReview/{FireReview,RegionEditor}`、`FireReview*Command.cs`、`RegionEditorCommand.cs` | WPF 視窗、Command、ExternalEvent |
| MCP | `src/BuildingRegulationReview/Mcp/FireReview/` | `FireReviewMcpTools`、`FireReviewJson` |
| 規則資料 | `src/BuildingRegulationReview/Data/fire-review-rules.json` | 有版本的結構化規則 |
| 共用參數 | `assets/SharedParameters/*.txt` | Big5＋CRLF，見 `.gitattributes` |

## 3. 資料與持久化

- 檢討套件（`ReviewPackage`）、檢討紀錄（`ReviewRun`）、覆寫存在 Revit `DataStorage + Extensible Storage`
  （`RevitReviewPackageRepository`、`RevitReviewRunRepository`）。
- 工具建立的元素以 `ManagedElementMark` 標記，重跑時只處理自己的元素。
- 失效：`ReviewStaleness`、`ReviewRunValidity`、`ReviewBaselineBuilder` 比對證據基線，標示需更新。

## 4. 設計決策

ADR-0002（刪除的 Area Plan 套件隱藏不刪）、ADR-0003（編輯器寫入區劃用途）、ADR-0004（MCP）、ADR-0005（用途類組逐區劃），
見 [`docs/adr/`](../../adr/)。

## 5. 驗證

單元測試在 `tests/BuildingRegulationReview.Core.Tests`；Revit 實機驗證見 [`docs/verification/`](../../verification/checklist.md)。
