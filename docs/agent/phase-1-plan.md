# Phase 1：建立視圖 — Task Boundary

本計畫將[完整功能規格](../fire-review-spec.md)第 8、9 節轉為可交接的工作邊界。一次 Session 僅執行一項 Task，完成後依規格提交、寫入 handoff，並停止於 `NEW_SESSION` 或 `PHASE_COMPLETE`。

## P1-T01 — 外掛骨架與共通 Domain Contract

- Goal：建立可編譯、可測試且隔離 Revit API 的分層骨架。
- In Scope：Solution、Domain／Application／Revit Adapter／核心測試專案、模組註冊介面、Result/Error、Revit 2024 版本適配入口、架構 ADR。
- Out of Scope：專案設定、Shared Parameter、ReviewPackage、DataStorage、UI、Area Plan 建立與任何 Revit 模型寫入。
- Inputs：既有 Revit 2024 外掛、`docs/fire-review-spec.md`。
- Deliverables：`BuildingRegulationReview.sln`、三個分層專案、測試專案、ADR-0001。
- Verification：Solution build；核心測試由乾淨輸出重新執行。
- Exit Criteria：0 build errors；核心測試全數通過；Domain/Application 不參考 Revit API；架構決策已記錄。
- Next Task：P1-T02。

## P1-T02 — 專案設定與 Shared Parameter 檢查

- Goal：定義第 8 節設定模型及可預覽、不可破壞的參數差異檢查。
- In Scope：設定 Contract、Shared Parameter definition／GUID／型別／Category Binding 驗證、dry-run 差異模型及測試。
- Out of Scope：Area Plan、ReviewPackage 持久化、未經確認的參數建立。
- Inputs：P1-T01 handoff；公司 GUID 尚未確認時須使用明確 fixture，不得假裝為正式 GUID。
- Deliverables：設定模型、檢查服務、Revit dry-run Adapter、單元測試與手動驗證紀錄。
- Verification：缺參數、同名異 GUID、錯誤型別測試；Revit 測試模型 dry-run。
- Exit Criteria：差異預覽完整且不修改模型；指定案例通過。
- Next Task：P1-T03。

## P1-T03 — ReviewPackage 持久化

- Goal：以 DataStorage + Extensible Storage 保存穩定的 ReviewPackage。
- In Scope：Schema、Repository、版本遷移、CRUD 與重新開啟驗證。
- Out of Scope：來源視圖 UI、Area Plan 建立。
- Inputs：P1-T02 handoff、規格第 6 節。
- Deliverables：Domain 模型、Repository Port、Revit Adapter、遷移測試。
- Verification：建立／讀取／更新 ID 穩定；模型重開可讀。
- Exit Criteria：schemaVersion 可驗證與遷移；失敗不破壞既有資料。
- Next Task：P1-T04。

## P1-T04 — 來源視圖與 Area Scheme 選擇 UI

- Goal：收集並驗證 Phase 1 建立視圖所需輸入。
- In Scope：非 Template Floor Plan 篩選、多選、Area Scheme、可選 Template、Crop 策略與 UI 狀態測試。
- Out of Scope：建立或修改 Revit 元素。
- Inputs：P1-T03 handoff。
- Deliverables：UI/ViewModel、輸入 DTO、驗證錯誤。
- Verification：UI 狀態與驗證測試；手動操作清單。
- Exit Criteria：所有無效輸入可在寫入前阻擋並提供可操作訊息。
- Next Task：P1-T05。

## P1-T05 — Area Plan 建立／更新服務

- Goal：冪等建立或更新受管理 Area Plan。
- In Scope：重複偵測、建立、Template、可寫 Crop／Scope Box 設定、Transaction 與 Rollback。
- Out of Scope：Phase 2 Boundary／Area 寫回。
- Inputs：P1-T04 handoff、Revit 測試模型。
- Deliverables：Use Case、Revit Writer、整合測試／可重現驗證紀錄。
- Verification：同輸入重跑不新增視圖；設定不可寫時警告；致命錯誤 Rollback。
- Exit Criteria：規格 9.2 核心流程可重跑且不重複。
- Next Task：P1-T06。

## P1-T06 — Phase 1 整合與驗收

- Goal：整合 UI、Use Case、進度、錯誤與日誌並完成 Phase 1。
- In Scope：串接、回歸、操作文件、規格 9.4 驗收。
- Out of Scope：Phase 2 幾何與區劃功能。
- Inputs：P1-T05 handoff。
- Deliverables：可操作 Phase 1、驗收紀錄、Phase 完成 handoff。
- Verification：每個來源視圖與 Area Scheme 最多一個受管理 Area Plan；重跑無重複；不可複製設定僅警告。
- Exit Criteria：Phase 1 驗收全數通過。
- Next Task：`PHASE_COMPLETE`。

