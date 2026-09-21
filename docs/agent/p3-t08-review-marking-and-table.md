# P3-T08 — 視圖標示與檢討表

## 完成範圍

- Application `Reviews/ReviewTable.cs`（spec 11.7）：
  - `ReviewStatusAggregation.Row`：一列的六態狀態，最差者優先：Fail > InsufficientData > ManualReview > NotRun > Pass > NotApplicable；空列為 NotRun。
  - `ReviewStatusAggregation.Verdict`：總狀態規則——任何 Fail → 未符合；無 Fail 但有資料不足／人工覆核／未檢討 → 待確認；全部 Pass／NotApplicable → 符合；沒有任何結果 → 未檢討。
  - `ReviewVerdict`：NotReviewed／Pass／Pending／Fail／**NeedsUpdate**（run 或任一結果已失效時優先顯示「需更新」，spec 7、13.1）。
  - `ReviewStatusCounts`：六態數量；`Unknown` = 資料不足 + 人工覆核 + 未檢討。
  - `ReviewTable.Build(run, freshness?)`：固定三列（防火區劃面積、構件防火時效、防火門窗），未知 check type 放 `OtherEntries`，不丟棄。
    - 統計：區劃面積依區劃；構件依類別、再依類別＋Type；門窗依 門／窗／幕牆（帷幕嵌板與 Host 為帷幕牆的門都算幕牆）。
    - `ReviewTableEntry`：計算狀態、`EffectiveStatus`、目前覆寫（Active／需重新確認）、是否失效、區劃名稱（取自同 run 面積結果的 `zone.name`）、類別、Type、開口種類、是否連結模型、`LocateUniqueIds`（定位）、條文、規則 ID／版本、實際值、規定值、訊息、`StatusText`。
  - 所有狀態都用 `ReviewRun.EffectiveStatus`：Active 覆寫生效，需重新確認的覆寫不生效。
- Application `Reviews/ReviewMarkup.cs`（spec 11.4 第 4 點、11.5 第 6 點、11.6 第 4 點、13.2）：
  - `ReviewMarkKey`（token `BCRRV/{package}/{run}/{zone}/{part}`）：紅色 Filled Region 的擁有權標記，含 Package ID、Run ID、Zone ID；跨 run 以 `Slot`（package＋zone＋part）配對。`ToLabel()` 為寫入 Comments 的可讀文字。`ManagedOwnership` 已可辨識此格式。
  - `ReviewMarkupPlan.Build(table, zones)`：只有 Completed run 可標示（否則 `BCR-MARK-001`）。EffectiveStatus = Fail 的區劃面積結果 → 每個封閉 Area 一個紅色區域（外框＋孔洞）；構件／開口 Fail → 每個元素一個紅色 By Element Override（依 UniqueId 去重）。失效結果、連結模型元素、找不到範圍或沒有封閉面積的區劃 → `Skipped`（附原因），不會默默略過。
  - `ReviewMarkupDiff.Compute(plan, existingMarks, recordedOverrides)`：
    - 區域：沒有 → Create；同 slot、同 run、同 signature → Unchanged；前次 run 或外框變更 → Update；同 slot 重複 → Remove 多餘者；本套件不再需要的 → Remove。其他套件與無法解析的標記只計入 `ForeignMarks`，不動。
    - 覆寫：沒紀錄 → Create；本 run 已紀錄 → Unchanged；前次 run 紀錄 → Update（保留最初的原始狀態）；已紀錄但不再未符合 → Remove（恢復原狀）。
    - `Summary`：套用前的範圍摘要（spec 15）。
  - `RecordedElementOverride`（元素、run、原始狀態、套用狀態）與 `ReviewOverrideRestore.Decide`：目前顯示等於工具套用的 → 恢復原始；使用者已改 → 保留使用者修改（`BCR-MARK-003`）；元素已刪 → 只刪紀錄。
  - `ReviewMarkupResult`／`ReviewMarkupItem`：新增／更新／刪除／略過／失敗數量（spec 15）。
- Application 其他：`ManagedOutputKind.ReviewView`（token `reviewview`）、`ReviewOutputNaming.ReviewView` → `{AreaScheme}_{SourceFloorPlan}_防火檢討`；錯誤碼 `BCR-MARK-001` 檢討視圖無法標示、`BCR-MARK-002` 未符合項目未標示、`BCR-MARK-003` 保留使用者修改的元素顯示。
- Revit `Reviews/`：
  - `RevitReviewViewMarker`：
    - `FindView`：以 `ManagedOutputKey(ReviewView)` 標記找回檢討視圖（改名不失聯）。沒有時複製來源平面圖（`ViewDuplicateOption.Duplicate`，不帶註解），命名用 `MakeUnique`。
    - `ReadMarks`／`ReadOverrides`、`Mark(package, plan, defaultName)`：一個 TransactionGroup，內含「建立視圖」與「更新標示」兩個 transaction；視圖失敗或例外時整批復原，單一元素被 Revit 拒絕只記 Failed 並繼續。
    - 紅色區域：Filled Region Type `BCR_防火檢討_未符合`（紅色 45° 斜線、非遮罩，第一次使用時建立；既有同名類型照使用者設定使用），Z 取視圖樓層高程；更新時先建新的再刪舊的；刪除前再次檢查元素上的擁有權標記。
    - 紅色覆寫：投影線／截面線紅色、表面與截面前景實心紅色。
    - `Locate(entry)`：把 `LocateUniqueIds` 解析為本文件的 ElementId（略過連結與已刪除元素），選取與縮放由 P3-T09 的 UI 呼叫。
  - `RevitOverrideStateCodec`：`OverrideGraphicSettings` ↔ 文字快照（`ogs1;...`，固定順序，pattern 用 UniqueId）。
  - `ReviewViewOverrideStorage`：覆寫紀錄存在檢討視圖本身的 Extensible Storage：
    - View schema `3b8e0f6a-94d2-4c1e-8a57-2f60c9d4b713`（`BCR_ReviewViewOverrides_v1`，`Entries` 陣列）
    - Entry schema `a7c41d2e-5b93-4f08-b6e1-8d2f35c0a9e4`（ElementUniqueId、RunId、OriginalState、AppliedState）

## 設計決策

- 檢討視圖一個套件一個，重複使用於每次 run；Run ID 寫在每個紅色區域上。新 run 接手舊 run 的區域（Update），不另建，所以重跑不增加元素（spec 16.3 第 10 項）。
- 視圖用來源平面圖的複本，而不是 Area Plan：構件與門窗在平面圖中顯示完整，也不會和 Area Color Scheme 疊色；標示永遠不動使用者的原平面圖與 Area Plan。
- 視圖識別用擁有權標記，沒有在 `ReviewPackage` 新增欄位，避免 P1 的套件 schema 遷移；需要時 P3-T09 可再記錄到 `GeneratedElementUniqueIds`。
- 「保存原視圖狀態」＝每個被覆寫元素在檢討視圖中的原始 `OverrideGraphicSettings`，只在第一次上色時擷取；之後的 run 沿用最初的原始狀態。
- 失效結果不上色（避免把過期結果當成目前模型狀態），列為略過。
- 紅色區域用斜線而非實心，才能看到底下的構件；使用者可自行修改同名 Filled Region Type，工具不會覆寫。
- 檢討表空列（沒有候選）顯示未檢討，但不影響總狀態；三個檢查是否都執行由 P3-T09 的指令保證。

## 驗證

- Solution `-t:Rebuild` 與外掛 csproj `-t:Rebuild`：各 0 warnings、0 errors。
- Core tests：**931/931 通過**（原 889，新增 42）：
  - `Reviews/ReviewTableTests.cs`（28）：六態列彙總、總狀態規則、Unknown 計數、三列順序、區劃超限、類別／Type 統計、門窗 是／否／未設定、幕牆歸類、未知 check type、Active 覆寫生效、需重新確認的覆寫不生效、失效 → 需更新、規則版本變更全部失效、未完成 run、freshness 與 run 不符。
  - `Reviews/ReviewMarkupTests.cs`（14）：區域 key 含 Package／Run／Zone、token 來回與擁有權、構件與門上色且去重、依 EffectiveStatus、失效與連結與找不到區劃的略過原因、未完成 run 拒絕、**同一 run 標示三次不增加元素**、新 run 接手舊標示、恢復原始顯示、其他套件與無法解析標記不動、外框變更與重複區域、使用者修改保留、結果統計、視圖命名與容器標記。
- **未實機驗證**：`RevitReviewViewMarker`、`RevitOverrideStateCodec`、`ReviewViewOverrideStorage` 只經過編譯，還沒有指令呼叫它們；在 Revit 中建立視圖、Filled Region、覆寫與恢復，需在 P3-T09 接上指令後實機驗收。

## 未解決問題

- spec 19 第 2、4、5、6、7 項仍未定。
- WPF 檢討表視窗、定位（選取＋縮放）、人工覆核操作與指令整合屬於 P3-T09。
- 連結模型元素無法在主模型視圖逐元素覆寫（MVP 政策：列為略過，spec 17.2 V1.1 再處理）。
