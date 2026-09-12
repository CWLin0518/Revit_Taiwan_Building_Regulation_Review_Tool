# Revit Taiwan Building Regulation Review Tool

Revit 2024 外掛原型，用來搜尋、選擇建築技術規則檢討項目，後續可擴充模型檢核與圖說產生流程。

## 目前功能

- Revit 功能區「建築技術規則」頁籤與「開啟檢討面板」按鈕
- 可停駐於 Revit 左側的 WPF 面板
- 依項目、法條、分類及說明搜尋
- 點選項目後顯示法規依據、檢討說明與預計產出
- JSON 格式的檢討項目資料，便於持續新增內容

## 建置與安裝（Revit 2024）

請先關閉 Revit，再於 PowerShell 執行：

```powershell
.\scripts\install-revit-2024.ps1
```

重新開啟 Revit 後，在「建築技術規則」頁籤點擊「開啟檢討面板」。

## 專案結構

- `src/BuildingRegulationReview`：Revit 外掛與 WPF 面板
- `src/BuildingRegulationReview/Data/review-items.json`：檢討項目資料
- `BuildingRegulationReview.addin`：Revit Add-in manifest 範本
- `scripts/install-revit-2024.ps1`：建置並安裝到目前使用者的 Revit 2024 Addins 目錄
