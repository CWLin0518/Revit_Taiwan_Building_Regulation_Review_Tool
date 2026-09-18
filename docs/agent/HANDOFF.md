# Agent Handoff
- Phase: P1
- Completed Task: P1-T04
- Next Task: P1-T05
- Status: READY_FOR_NEW_SESSION
- Commit: 934cbfea50a1b009d5c0f1f33c516c2366a61b5d
- Spec Version: Draft v1.1 (`docs/fire-review-spec.md`)

## Completed
- 新增 Ribbon「防火區劃設定」入口與 WPF 設定視窗。
- 可選來源 Floor Plan、既有 Area Scheme、Area Plan View Template、crop 複製及 Scope Box。
- 確認後在單一 transaction 建立 Setup 狀態的 ReviewPackage，且不提前建立 Area Plan。
- setup options 使用獨立 Extensible Storage schema，未修改 P1-T03 已發布 schema。

## Verification Results
- Core tests：15/15 passed。
- Full add-in build：成功，0 warnings / 0 errors。

## Known Issues / Risks
- Revit 2024 公開 API 無法建立 Area Scheme；UI 明確要求使用者先以 Revit 原生命令建立。
- 尚未在實際 Revit UI 執行 live create/reopen；API adapter 已編譯。
- 使用者原有 `.gitignore` 修改與 `.gtoffice/` 未追蹤內容不得納入 commit。

## Exact Next Steps
執行 P1-T05：從 ReviewPackage 與 setup options 建立/重用 Area Plan，套用 template、crop/scope box，並確保 transaction rollback 與不重複建立。

## Do Not Do
- 不可修改既有 ReviewPackage schema GUID 或欄位。
- 不要納入使用者 `.gitignore` 與 `.gtoffice/` 內容。
