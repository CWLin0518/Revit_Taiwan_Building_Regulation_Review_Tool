# Agent Handoff

- Phase: P1
- Completed Task: P1-T03
- Next Task: P1-T04
- Status: READY_FOR_NEW_SESSION
- Commit: e4591b0bcb8e1cbc86ee7deb6d979b0fdf629111
- Spec Version: Draft v1.1 (`docs/fire-review-spec.md`)

## Completed

- 建立 spec 第 6 節的 `ReviewPackage` Domain model、狀態列舉與必要欄位驗證。
- 建立 `IReviewPackageRepository` CRUD port 與可版本化 storage record/mapper。
- 建立 Revit 2024 `DataStorage + Extensible Storage` adapter；每個 package 使用一個 DataStorage，欄位獨立儲存且可由 Package ID 查詢。
- 建立 0.9 → 1.0 migration，未知版本明確拒絕。
- 新增完整欄位 round-trip、migration 與 validation 測試及技術說明。

## Changed Files

- `src/BuildingRegulationReview.Domain/ReviewPackages/**`
- `src/BuildingRegulationReview.Application/ReviewPackages/**`
- `src/BuildingRegulationReview.Revit/ReviewPackages/**`
- `tests/BuildingRegulationReview.Core.Tests/ReviewPackages/**`
- `docs/agent/p1-t03-review-package-persistence.md`
- `docs/agent/phase-state.yaml`
- `docs/agent/HANDOFF.md`

## Decisions and Assumptions

- logical schemaVersion 使用 `1.0`；Revit schema GUID 固定為 `d3272af5-4945-4be6-bf9f-a5a6202bd306`。
- 不以不透明 JSON 儲存，以維持 Package ID 與各 UniqueId 欄位可查詢性。
- adapter 不自行開啟 Transaction；`Save/Delete` 要求 caller 已開啟 transaction，read-only 操作不需 transaction。
- 未新增外掛 command/UI 接線，避免越界進入 P1-T04。

## Verification Results

- Solution build：成功，0 warnings，0 errors。
- Core tests：14/14 passed。
- Revit adapter 已以 Revit 2024 API 編譯成功。

## Known Issues / Risks

- 尚無 P1-T04 的外掛入口可在目前 Revit 文件中觸發 repository，因此 live create/update/delete/reopen 測試留待接線後執行；純 storage record 的完整 round-trip 已測試。
- Revit Extensible Storage schema 一旦註冊不可變；未來新增實體欄位時必須使用新 GUID 並做 physical migration。
- 使用者原有 `.gitignore` 修改與 `.gtoffice/` 未追蹤內容不得納入 commit。

## Exact Next Steps

1. 執行 P1-T04：來源 Floor Plan 選擇、Area Scheme 選擇/建立選項與必要的 Template/Property 選項 UI。
2. 將完成的 setup selection 寫入新的 ReviewPackage，但不要提前建立 Area Plan（P1-T05）。
3. 若 P1-T04 新增可安全觸發的 integration command，補做 repository live CRUD/reopen 驗證。

## Do Not Do

- 不要在 P1-T04 提前建立 Area Plan 或處理 crop/template transaction（屬 P1-T05）。
- 不要修改已發布 schema GUID 內的欄位定義。
- 不要納入或覆蓋使用者 `.gitignore` 與 `.gtoffice/` 內容。
