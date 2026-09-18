# P1-T02 Shared Parameter Dry-run 驗證紀錄

## 安全界線

- `SharedParameterDryRunService` 只依 `ISharedParameterInventoryReader` 取得快照並產生差異，不提供建立、修改或刪除參數的方法。
- `RevitSharedParameterInventoryReader` 只讀取 `Document.ParameterBindings`、`SharedParameterElement.GuidValue` 與 definition data type，不開啟 Transaction。
- 公司正式 Shared Parameter GUID 尚未提供；自動測試使用明確 fixture GUID `11111111-2222-4333-8444-555555555555`，不得作為正式 GUID。

## 差異類型

- 缺少參數
- 同名但 GUID 不同（也涵蓋同名非 Shared Parameter）
- 資料型別不同或不支援
- Instance／Type Binding 不同
- 缺少或多出的 Category Binding

## Revit 手動驗證

- 2026-09-19：透過 Revit MCP 成功只讀連線目前專案「建築道路陰影分析1」。
- 待公司確認正式參數名稱、GUID、型別與 Category 清單後，才可建立 production configuration 並執行完整專案 dry-run。
- 本 Task 不建立 Shared Parameter、不修改模型，也不進入 Area Plan 流程。
