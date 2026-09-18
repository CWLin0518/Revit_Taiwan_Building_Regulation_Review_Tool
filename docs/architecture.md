# 整體專案架構

## 1. 專案定位

本專案是 Autodesk Revit 2024 外掛，提供台灣建築技術規則的模型檢核與檢討圖說產生功能。目前目標框架為 .NET Framework 4.8，介面使用 WPF，Revit 操作透過 Revit API，複雜平面幾何運算使用 RhinoCommon。

## 2. 技術組成

| 項目 | 技術或版本 | 用途 |
| --- | --- | --- |
| 執行環境 | Revit 2024 | 外掛宿主 |
| Framework | .NET Framework 4.8 | 外掛執行框架 |
| UI | WPF | Dockable Pane 與選項視窗 |
| BIM API | RevitAPI / RevitAPIUI | 模型查詢、交易與圖面建立 |
| 幾何運算 | RhinoCommon 8 | 投影輪廓與布林運算 |
| 功能目錄 | JSON | 面板顯示的法規項目資料 |

## 3. 目錄結構

```text
Revit_Taiwan_Building_Regulation_Review_Tool/
├─ docs/                              技術與法規文件
├─ scripts/
│  ├─ install-revit-2024.ps1          建置與安裝外掛
│  └─ redeploy.bat                    等待 Revit 關閉後重新部署
├─ src/BuildingRegulationReview/
│  ├─ App.cs                          Revit 外掛啟動點
│  ├─ ShowReviewPaneCommand.cs        開啟 Dockable Pane
│  ├─ ReviewPaneProvider.cs           註冊面板內容
│  ├─ ReviewPaneControl.xaml(.cs)     法規清單與操作介面
│  ├─ ReviewItem.cs                   法規目錄資料模型
│  ├─ Data/review-items.json          法規目錄資料
│  └─ Article164*.cs                  第 164 條功能
├─ BuildingRegulationReview.addin     Revit Add-in manifest 範本
└─ README.md                          專案入口說明
```

## 4. 啟動與 UI 流程

```text
Revit 載入 .addin
    │
    ▼
App.OnStartup
    ├─ 建立 Ribbon Tab / Panel / Button
    ├─ 建立 Revit ExternalEvent
    └─ 註冊 ReviewPaneProvider
             │
             ▼
       ReviewPaneControl
             ├─ 載入 Data/review-items.json
             ├─ 搜尋與顯示法規項目
             └─ 使用 ExternalEvent 提交檢討或圖說工作
                           │
                           ▼
                 Revit API 執行環境
```

Dockable Pane 是 modeless WPF UI，不能在按鈕事件內直接修改 Revit 文件。因此面板必須呼叫 `ExternalEvent.Raise()`，再由 `IExternalEventHandler.Execute()` 進入合法的 Revit API 執行環境。

## 5. 功能資料流

目前法規檢討的一般資料流為：

```text
ReviewItem（顯示資料）
    │
    ▼
ReviewPaneControl（使用者操作）
    │ ExternalEvent
    ▼
法規 Command（選取、收集、計算、判定）
    ├─ 法規 Result / Session（暫存結果）
    ├─ PlanViewBuilder（建立檢討視圖）
    └─ DrawingCommand / DrawingBuilder（建立圖說）
```

## 6. Revit Transaction 邊界

- 模型元素、View、FilledRegion、TextNote、Sheet 或 Viewport 的建立與修改，必須位於 `Transaction` 中。
- 使用者選取、WPF 視窗、純幾何計算應放在 Transaction 外，縮短文件鎖定時間。
- 需要 `Document.Regenerate()` 的版面量測必須在有效 Transaction 內執行。
- 大型操作建議拆成明確交易，例如「建立圖紙」與「圖面排版」。

## 7. 外部依賴與部署

專案檔直接引用本機安裝路徑：

- `C:\Program Files\Autodesk\Revit 2024\RevitAPI.dll`
- `C:\Program Files\Autodesk\Revit 2024\RevitAPIUI.dll`
- `C:\Program Files\Rhino 8\System\RhinoCommon.dll`

部署流程：

1. 以 Release 組態建置專案。
2. 將 DLL、PDB 與 `Data` 複製到 Revit 2024 Addins 目錄。
3. 以實際 DLL 路徑取代 `.addin` 範本的 `__ASSEMBLY_PATH__`。
4. 重新啟動 Revit 2024 載入外掛。

## 8. 新增法規功能的接入點

目前分支仍以每項法規各自建立 Command、ExternalEventHandler 和 UI 判斷的方式接入。新增法規至少需要：

1. 在 `Data/review-items.json` 增加目錄項目與唯一 `Id`。
2. 建立該法規的檢討 Command。
3. 若由 Dockable Pane 啟動，建立對應 `IExternalEventHandler`。
4. 在 `App.OnStartup` 建立 ExternalEvent，並傳入面板。
5. 在 `ReviewPaneControl` 依 `ReviewItem.Id` 派送功能。
6. 視需求建立 Result、Session、ViewBuilder、DrawingCommand 與 DrawingBuilder。
7. 在 `docs/regulations/` 增加功能文件。

> 架構重整 commit `a070382` 已在 `Building_Regulation_Review_Tool_Project-Architecure` 分支建立功能 Registry 與共用 ExternalEvent Dispatcher；目前 `fire-protection` 分支尚未包含該 commit。

## 9. 共用設計原則

- 法規 ID 必須在 JSON、UI 派送與功能實作間保持一致。
- 計算值在 Revit API 邊界使用 internal feet；顯示與輸入時才轉換成公制。
- 純法規公式應逐步與 Revit 元素收集、UI 及文件寫入分離，便於單元測試。
- 自動建立的 View、Sheet、FilledRegionType 與 TextNoteType 應使用穩定名稱，重跑時先尋找或覆蓋，避免重複累積。
- Session 中保存的 `ElementId` 在使用前必須重新確認元素仍存在。

