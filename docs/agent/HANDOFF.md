# Agent Handoff

- Phase: P1
- Completed Task: P1-T01
- Next Task: P1-T02
- Status: READY_FOR_NEW_SESSION
- Commit: d9c9b6336734010d0884e9b25a98885c52ceeda4
- Spec Version: Draft v1.1 (`docs/fire-review-spec.md`)

## Completed

- 將使用者提供的完整 spec 納入 `docs/fire-review-spec.md`，並建立 Phase 1 的完整 Task Boundary。
- 建立 Domain／Application／Revit Adapter／Core Tests 分層 Solution。
- 建立 `Error`、`Result`、`Result<T>` 共通 Domain Contract。
- 建立不綁定特定容器的模組與服務註冊介面。
- 建立 Revit 2024 版本適配入口；尚未宣稱支援其他版本。
- 新增 ADR-0001，記錄依賴方向、框架選擇與舊外掛漸進整合決策。

## Changed Files

- `.gitignore`
- `BuildingRegulationReview.sln`
- `docs/README.md`
- `docs/fire-review-spec.md`
- `docs/adr/0001-layered-architecture-and-revit-versioning.md`
- `docs/agent/phase-1-plan.md`
- `docs/agent/phase-state.yaml`
- `docs/agent/HANDOFF.md`
- `src/BuildingRegulationReview.Domain/**`
- `src/BuildingRegulationReview.Application/**`
- `src/BuildingRegulationReview.Revit/**`
- `tests/BuildingRegulationReview.Core.Tests/**`

## Decisions and Assumptions

- 目前鎖定 Revit 2024／.NET Framework 4.8；核心層使用 `netstandard2.0`。
- Core Tests 使用本機唯一可用的 .NET 10 SDK；產品核心仍可由 net48 Revit Adapter 參考。
- P1-T01 僅建立抽象 DI 邊界，不導入第三方 DI Container。
- 舊 `src/BuildingRegulationReview` 專案未改動，也尚未納入新 Solution，避免在骨架 Task 擴大遷移範圍。

## Verification Results

- `dotnet clean BuildingRegulationReview.sln --configuration Debug`：成功。
- `dotnet build BuildingRegulationReview.sln --configuration Debug --no-incremental`：成功，0 warnings，0 errors。
- `dotnet test tests/BuildingRegulationReview.Core.Tests/BuildingRegulationReview.Core.Tests.csproj --configuration Debug --no-build`：成功，3/3 passed。
- 核心專案檔與原始碼的 Revit API 依賴掃描：僅 Revit Adapter 含 Revit API reference。

## Known Issues / Risks

- Shared Parameter 正式 GUID、Category、資料型態及公司標準尚未由使用者確認；P1-T02 不得猜測正式值。
- P1-T02 的 Revit dry-run 需要可安全操作的測試模型；目前連線模型為「建築道路陰影分析1」，是否可作測試模型尚未確認。
- 舊 Article 164 外掛功能仍由既有單一專案建置；新骨架尚未接入既有 Ribbon／Dockable Pane。

## Exact Next Steps

1. 讀取 `docs/fire-review-spec.md` 第 8 節、`docs/agent/phase-1-plan.md` 的 P1-T02、此 handoff 與 phase state。
2. 確認 HEAD 符合 handoff commit。
3. 建立專案設定 Contract 與 Shared Parameter dry-run 差異模型。
4. 以 fixture 測試缺參數、同名異 GUID、錯誤型別；正式 GUID 未定時不得寫入模型。
5. 透過 Revit MCP 在經確認的測試模型執行唯讀／dry-run 驗證。

## Do Not Do

- 不建立 Area Plan；那是 P1-T05。
- 不建立 ReviewPackage DataStorage；那是 P1-T03。
- 不在未確認 GUID／型別時建立或覆寫 Shared Parameter。
- 不開始 Phase 2。
