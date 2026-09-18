# Agent Handoff

- Phase: P1
- Completed Task: P1-T02
- Next Task: P1-T03
- Status: READY_FOR_NEW_SESSION
- Commit: 7a5b00629bb90113e59889b1b0ee5cdf8e4a5823
- Spec Version: Draft v1.1 (`docs/fire-review-spec.md`)

## Completed

- 建立 Project Setup 與 Shared Parameter requirement contract。
- 建立純讀取的 dry-run 差異服務，涵蓋缺少參數、同名異 GUID、型別、Instance/Type Binding、缺少與多餘 Category Binding。
- 建立 Revit 2024 inventory adapter；只讀取 ParameterBindings 與 SharedParameterElement，完全不開啟 Transaction。
- 新增 6 個 P1-T02 單元測試與手動驗證紀錄。
- 確認 Revit MCP、Rhino MCP 與 Grasshopper MCP 均可連線；Revit 目前專案為「建築道路陰影分析1」。

## Changed Files

- `src/BuildingRegulationReview.Domain/ProjectSetup/**`
- `src/BuildingRegulationReview.Application/ProjectSetup/**`
- `src/BuildingRegulationReview.Revit/ProjectSetup/**`
- `tests/BuildingRegulationReview.Core.Tests/ProjectSetup/**`
- `docs/agent/p1-t02-shared-parameter-dry-run.md`
- `docs/agent/phase-state.yaml`
- `docs/agent/HANDOFF.md`

## Decisions and Assumptions

- Category 使用穩定的 `BIC:<ElementId.Value>` key，避免 Revit 語系造成 Category.Name 不一致。
- 未取得公司正式 Shared Parameter GUID；測試 GUID `11111111-2222-4333-8444-555555555555` 僅為 fixture，不可用於正式模型。
- 本 Task 只提供 inventory 與差異預覽，不提供建立／修改／刪除參數 API。

## Verification Results

- Solution build：成功，0 warnings，0 errors。
- Core tests：9/9 passed。
- Revit MCP `get_project_info`：成功讀取目前專案，證明連線正常。
- Rhino MCP：available，127.0.0.1:9876；Grasshopper MCP：available，127.0.0.1:9999。

## Known Issues / Risks

- 正式參數名稱、GUID、資料型別與 Category Binding 清單尚待公司確認，因此沒有建立 production configuration。
- Revit MCP 目前沒有直接載入本次新 assembly 執行 dry-run 的工具；adapter 已在 Revit 2024 API 下成功編譯，但完整模型差異需在外掛入口接線後執行。
- 工作樹原有使用者變更 `.gitignore` 與未追蹤 `.gtoffice/`，本 Task 未納入 commit。

## Exact Next Steps

1. 執行 P1-T03：依規格第 6 節建立 ReviewPackage Domain model。
2. 定義 DataStorage + Extensible Storage schema、repository port 與 Revit adapter。
3. 加入 schemaVersion、CRUD、migration 與 reopen/round-trip 測試。
4. 保留 P1-T02 正式 GUID 未決事項，不得自行發明 production GUID。

## Do Not Do

- 不要進入 Area Plan（P1-T05）或來源視圖 UI（P1-T04）。
- 不要修改或建立正式 Shared Parameter，直到名稱、GUID、型別、Category 清單獲確認。
- 不要納入或覆蓋使用者現有 `.gitignore` 與 `.gtoffice/` 變更。
