# P1-T05 — Area Plan 建立／更新服務

## 完成範圍

- 新增可測試的 Area Plan 冪等判斷與 ReviewPackage 更新邏輯。
- 新增 Revit Area Plan writer；以 package 已記錄且仍符合 level / Area Scheme 的視圖作為重用依據。
- 新建時解析來源 Floor Plan、Level、Area Scheme，建立唯一命名的 Area Plan。
- Area Plan template、來源 crop 與 Scope Box 為可選設定；不存在、受 template 控制或不可寫時回傳警告，不刪除已建立視圖。
- 建立視圖及更新 ReviewPackage 均在同一 Revit transaction；致命錯誤會 rollback。
- setup options repository 可依 ReviewPackage 讀回 P1-T04 的選項。

## 驗證

- Core tests：17/17 passed。
- Solution build：成功，0 warnings / 0 errors。
- 尚未在實際 Revit 測試模型執行 live create / rerun；留待 P1-T06 整合驗收。

## 設計決策

- package 的 AreaPlanUniqueId 指向有效且 level / Area Scheme 相符的 Area Plan 時直接重用，不開 transaction。
- Template、crop、Scope Box 屬非致命選項；主要元素解析、Area Plan 建立或 package persistence 失敗才 rollback。
- ReviewPackage 維持 Setup 狀態，BoundaryDraft / Ready 屬 Phase 2 寫回流程。
