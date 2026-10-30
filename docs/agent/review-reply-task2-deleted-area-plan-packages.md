# 任務 2 影響審查

審查日期：2026-10-06。審查請求基線 b4e35fb；實際讀取 HEAD 014bdf0，該版本已包含 ReviewPackageAvailability.Partition、RevitAreaPlanProbe 與兩個選單接線。因此以下同時審查原提案與現行實作，並非宣告尚未實作的方案已完成驗收。未修改程式；未執行 build 或 Revit 實機測試。

## 結論

支持「只在操作選單過濾、不刪除儲存資料」。這是必要變更。現行實作有一項需修正：RevitAreaPlanProbe 捕捉所有 Exception 並當作已刪除，可能把讀取失敗誤報成使用者刪除。修復工作包也不等於舊結果重新有效，需保持既有失效與重新檢討流程。

## 2.1：孤兒工作包修復

**必要變更**：保留工作包與紀錄。FireReviewSetupCommand.Apply 以 SourceFloorPlanUniqueId + AreaSchemeUniqueId 尋找既有包；RevitAreaPlanProvisioner.Provision 在舊視圖不符時建新 Area Plan，經 AreaPlanProvisioning.Complete → ReviewPackage.WithAreaPlan 更新。WithAreaPlan 保留 PackageId、衍生視圖 ID、生成元素 ID、BoundaryRevision、LastReviewRunId 及 Status；ReviewRun 另存於 DataStorage，不因換視圖而刪除。推論成立。

但「接回」只指保留關聯，不保證原視圖仍存在、不會復活已被 Revit 刪除的 Area/邊界，也不會自動重建原區劃。Status 原樣保留，包括 Error/Reviewed，不能把此動作當作結果有效證明。應驗證重建後重新建立區劃、重新檢討與舊人工覆寫失效；訊息宜說「保留原有關聯與歷史紀錄；區劃與檢討需重新確認」。此限制是既有修復流程風險，非選單過濾新增的破壞。

## 3.2：三套述詞

**必要變更**：共用「是非樣板 Area Plan」身分判斷；不要強迫所有用途採完全相同的可用性條件。選單辨識存在性不需 GenLevel；幾何擷取仍需 GenLevel，Provision 還需樓層與 scheme 相符。存在但缺 Level 的視圖應保留可診斷途徑，這項取捨合理。

**bug（既有缺口）**：RevitReviewStalenessProbe.Observe 只 as ViewPlan，不檢查 AreaPlan/IsTemplate；損壞引用可能被當作 Area Plan 找到。應用共用身分判斷，再各自檢查 Level/scheme。缺失的 Error 判定不需改。

**bug（現行實作）**：RevitAreaPlanProbe.IsLiveAreaPlan 的 catch(Exception) 把文件失效、API context 錯誤等全吞成 false。false 又被標成 AreaPlanDeleted，會隱藏真實故障。只捕捉確認代表非法 ID 的特定例外並提供資料損壞診斷；其他例外交由命令 try/catch 回報。最好區分不存在與類型不符；若仍使用三態，提示應稱「Area Plan 已不存在或引用無效」，不能斷言均由使用者刪除。

## 3.4：刻意不做的五件事

| 項目 | 判定 | 理由與正確處理 |
| --- | --- | --- |
| 不刪孤兒 DataStorage | 無影響 | 同意；保留衍生元素關聯與歷史，沒有清理授權，禁止自動刪除。 |
| 不改 GetAll | 無影響 | 同意；repository 必須照實回傳，否則 setup 無法重用且可能產生重複包。 |
| 不改 LabelOf fallback | 無影響 | 同意；選單過濾承擔可選性，標籤仍可用於未完成設定或診斷。不要用改標籤掩蓋無效包。 |
| 不改缺 Area Plan 判 Error | 無影響 | 同意；已開視窗之後刪除仍需執行時防線。新選單不能替代 Apply/Scan 的驗證。 |
| setup 不加自動清理 | 無影響 | 同意；不選任意包、不偷偷刪除歷史。重複資料診斷另見下項。 |

## 4：六個影響面

| 影響面 | 判定 | 理由與正確處理 |
| --- | --- | --- |
| setup 重複判定 | 必要變更（診斷） | 刪視圖本身不新增包；Count > 1 阻擋仍正確，不能因包不可見而忽略它。可是「請先清理」沒有入口，應至少顯示來源視圖/scheme、重複數、PackageId/DataStorage 元素 ID 與可行處理指引。清理工具可另立任務；不要在本任務改成只計算 live 包或 First 選一筆。 |
| ReviewPaneControl/ShowReviewPaneCommand | 無影響 | 前者載入 Data/review-items.json 的功能列表並 dispatch 外部事件；後者只 Show dockable pane。沒有另一套工作包清單；最終經既有指令選單。 |
| Run repository/結果儲存 | 無影響 | RevitReviewRunRepository 以 RunId 或 PackageId Get/GetForPackage；FireReviewModel 以已選定包 GetLatest。未查到獨立列出所有孤兒包的報表/匯出入口。儲存歷史不等於出現在操作面板，不應過濾或刪歷史。未來新增全包報表需自行區分歷史與可操作包。 |
| Setup options repository | 無影響 | 以 PackageId 保存/讀取設定，沒有使用者清單入口；setup Save 保留同一 ID 的設定關係。 |
| 原 ReviewPackages 測試 | 無影響 | 新分類不改 repository/domain 合約；既有持久化、進度與失效測試不需改期待。新增分類測試及命令/Revit 驗證，不用為過濾放寬既有測試。 |
| 分層/介面 | 無影響 | Application 只接 Func<string,bool>，無 Revit 型別；此小型同步探測不強制另設介面。單次 Partition 比 Selectable + WithDeleted 重複探測好；需保持 API context。 |

## 驗證要求

- 分類：null/空白 ID 不呼叫 probe；混合清單保持順序；只有 Available 可選；null 清單為空；真正 probe 故障應回報而不是藏包。
- Revit：兩個面板只剩已存在 Area Plan；全部刪除與部分刪除均有合理訊息；Undo 刪除後包重現。
- 重建：同 PackageId、沒有新孤兒 DataStorage；衍生關聯與歷史保留；原區劃缺失須可重建，舊結果/覆寫不得直接視為有效。
- 已開視窗後刪除的 Apply/Scan 保持防線；重複孤兒包阻擋但提供可操作診斷。

spec 請求引用章節編號與目前主文件略不同；本審查依主文件 §6、§13、§16.2（刪除後修復）、§18 的資料關聯、失效與復原要求。
