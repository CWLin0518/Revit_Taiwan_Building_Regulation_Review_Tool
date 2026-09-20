# Agent Handoff
- Phase: P1
- Completed Task: P1-T05
- Next Task: P1-T06
- Status: READY_FOR_NEW_SESSION
- Commit: 209a116
- Spec Version: Draft v1.1 (`docs/fire-review-spec.md`)

## Completed
- 完成 Area Plan 冪等建立／重用服務與 Revit writer。
- 新建視圖可套用 Area Plan template、來源 crop 與 Scope Box；非致命設定失敗會回傳警告。
- 建立及 ReviewPackage 更新位於同一 transaction，致命錯誤 rollback。
- setup options 可從 Extensible Storage 讀回。

## Verification Results
- Core tests：17/17 passed。
- Full add-in build：成功，0 warnings / 0 errors。

## Known Issues / Risks
- 尚未在實際 Revit UI 執行 live create / rerun / rollback 驗證。
- P1-T05 writer 尚未接到 Ribbon UI；此為 P1-T06 範圍。
- 使用者原有 `.gitignore` 修改與 `.gtoffice/` 未追蹤內容不得納入 commit。

## Exact Next Steps
執行 P1-T06：把 P1-T04 設定 UI 串接 P1-T05 writer，呈現 created/reused 與 warnings，完成 Revit 手動驗收及 Phase 1 回歸。

## Do Not Do
- 不可修改既有 ReviewPackage schema GUID 或欄位。
- 不要納入使用者 `.gitignore` 與 `.gtoffice/` 內容。
