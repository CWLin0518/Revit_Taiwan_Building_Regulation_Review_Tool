# P1-T04 防火區劃設定

## 行為

- Ribbon 的「防火區劃設定」會列出非樣板 Floor Plan、Area Scheme、Area Plan View Template 與 Scope Box。
- 使用者確認後，在單一 Revit transaction 建立 `ReviewPackage`（狀態為 `Setup`）及對應的 setup options。
- 本步驟不建立 Area Plan；P1-T05 再使用所選 template、crop 與 scope box 設定建立視圖。

## 儲存

- ReviewPackage 延用既有 schema，不修改已發布的 immutable schema。
- P1-T04 選項使用獨立 schema `763174a4-c95d-4fa0-9b82-a463f599c28b`，由 Package ID 關聯。

## Revit 2024 限制

Revit 2024 公開 API 沒有建立 Area Scheme 的方法。介面會清楚提示使用者先以 Revit 原生命令建立，再重新開啟設定；程式不會建立假的識別碼或提前建立 Area Plan。
