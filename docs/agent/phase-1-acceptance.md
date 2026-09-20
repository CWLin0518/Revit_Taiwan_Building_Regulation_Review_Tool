# Phase 1 驗收紀錄（P1-T06）

## 自動驗證

- `dotnet build BuildingRegulationReview.sln --no-restore -v quiet`：成功，0 warnings、0 errors。
- `dotnet test tests/BuildingRegulationReview.Core.Tests/BuildingRegulationReview.Core.Tests.csproj --no-restore -v quiet`：17/17 通過。
- UI 命令已接到 `RevitAreaPlanProvisioner`。同一來源平面與面積配置會重用 ReviewPackage；設定與 Area Plan 寫入由 `TransactionGroup` 包住。

## Revit 2024 手動驗收步驟

需使用含樓層平面與 Area Scheme 的測試專案，並載入外掛。

1. 執行「防火區劃設定」，選擇來源平面與 Area Scheme，點「建立 Area Plan」。確認顯示「已建立」、Package ID，且 Area Plan 存在。
2. 用同一組來源平面與 Area Scheme 重跑。確認顯示「已重用」，Package ID 與 Area Plan 數量不變。
3. 分別選擇有效 Area Plan 樣板、複製裁切及 Scope Box；新建時檢查可寫設定。不可寫設定應顯示警告，且視圖仍保留。
4. 用交易失敗的測試情境檢查 Revit Undo 與模型狀態，確認設定、套件與新視圖一併回復。
5. 關閉並重新開啟模型，確認 Package ID 和 Area Plan 關聯可讀回。

## 尚未完成的實機驗證

此環境未提供已開啟的 Revit 測試模型或可自動操控的 Revit 工作階段。因此上述 live create、重跑、警告、rollback、重開驗證均未執行；Phase 1 的實機驗收尚未通過。既有 Area Plan 重用時不重新套用樣板、裁切與 Scope Box，UI 會明示。
