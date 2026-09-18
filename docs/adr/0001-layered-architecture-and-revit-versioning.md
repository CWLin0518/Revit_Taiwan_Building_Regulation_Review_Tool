# ADR-0001：分層架構與 Revit 版本適配

- 狀態：Accepted
- 日期：2026-09-19
- 對應任務：P1-T01

## 背景

既有外掛以單一 .NET Framework 4.8 專案承載 UI、Revit API、法規邏輯與圖說建立。新的防火檢討流程需要可測試的 Domain Contract、明確的模組邊界，以及未來支援不同 Revit 版本時可替換的 API 適配層。

## 決策

Solution 先建立三個產品專案與一個核心測試專案：

```text
BuildingRegulationReview.Domain      netstandard2.0，純 Domain 型別
          ↑
BuildingRegulationReview.Application netstandard2.0，Use Case 與 Port
          ↑
BuildingRegulationReview.Revit       net48，Revit API Adapter

BuildingRegulationReview.Core.Tests  net10.0，測試 Domain/Application
```

- Domain 不參考 Application、Revit API 或 UI。
- Application 只參考 Domain；以 `IApplicationModule` 與 `IServiceRegistry` 定義組合邊界，不在核心層綁定特定 DI Container。
- Revit Adapter 參考 Application、Domain 與目標版本 Revit API。
- 版本差異封裝於 `IRevitVersionAdapter`；目前實作 `Revit2024VersionAdapter`。
- 可預期的業務失敗以 `Result`／`Result<T>` 與結構化 `Error` 回傳；例外保留給程式錯誤或不可恢復的外部 API 問題。
- 舊的 `src/BuildingRegulationReview` 專案暫時維持原狀。其功能移轉不屬於 P1-T01，後續只能透過明確 Task 漸進整合。

## 後果

- 核心邏輯可在未載入 Revit 的情況下執行單元測試。
- Revit 版本支援仍需各版本 Adapter 專案或多目標建置策略；本決策只建立替換點，不宣稱已支援多版本。
- P1-T01 不新增 Area Plan、設定模型、Shared Parameter 或資料持久化行為。

## 驗證

- `dotnet build BuildingRegulationReview.sln --configuration Debug`
- `dotnet test tests/BuildingRegulationReview.Core.Tests/BuildingRegulationReview.Core.Tests.csproj --configuration Debug`

