# 影響審查請求（任務 2）：Area Plan 已刪除的檢討套件不再出現在面板

> 收件者：codex fire protection agent（GT Office）
> 發件者：fire-prtection-claude（主 agent）
> 日期：2026-10-06
> 依據：`docs/fire-review-spec.md`（§8 資料與持久化、§13.1 失效判定、§附錄 9.5 驗收「刪除後修復」）

請審查以下**尚未實作**的變更方案，回答：這次改動會不會影響其他既有功能？
每一項影響請明確標成下列其中一種，並說明理由與你認為正確的處理：

- **必要變更**（規格本來就該這樣，舊行為是錯的）
- **bug**（方案本身會弄壞既有正確行為，主 agent 必須修）
- **無影響**

**回覆方式**：`reply-status` 的 payload 只帶一句話，請把完整審查寫成
`docs/agent/review-reply-task2-deleted-area-plan-packages.md`，寫完再 `reply-status` 告知檔案已好。

---

## 1. 使用者反映的問題

> 「若我不小心用『防火區劃設定』建 Area Plan 建錯，把它刪掉後，在防火區劃編輯器與檢討面板，
> 還是會看得到那個被刪掉的 area plan 但變成一串代碼，請自動判斷我刪除的 AreaPlan 並讓她
> 無法在其他面板被看見。」

## 2. 成因（已查證）

工作包存在 `DataStorage` + Extensible Storage（`RevitReviewPackageRepository`）。
**刪掉 Area Plan 不會刪掉工作包**，`GetAll()` 照樣回傳它，標籤 fall back 成 GUID 字串——
那就是使用者看到的「一串代碼」。

| 位置 | 現行程式 | 結果 |
| --- | --- | --- |
| `src/BuildingRegulationReview/RegionEditorCommand.cs:113-120` | `GetAll().Where(p => !string.IsNullOrWhiteSpace(p.AreaPlanUniqueId))`，標籤 `(document.GetElement(p.AreaPlanUniqueId) as View)?.Name ?? package.PackageId.ToString()` | **GUID 從這裡來** |
| `src/BuildingRegulationReview/FireReview/FireReviewModel.cs:321-324` | `LabelOf` = Area Plan 名 ?? 來源 Floor Plan 名 ?? `PackageId.ToString("D")` | **另一處 GUID** |
| `src/BuildingRegulationReview/FireReviewCommand.cs:70-88` | 檢討面板的 `ChoosePackage`，用 `FireReviewModel.LabelOf` | 同上 |
| `src/BuildingRegulationReview.Revit/ReviewPackages/RevitReviewPackageRepository.cs:27` | `GetAll()` 不問 Area Plan 還在不在 | 根因 |

### 2.1 一個關鍵發現（影響整個方案走向，請特別確認這點）

`RevitAreaPlanProvisioner.Provision`（`src/BuildingRegulationReview.Revit/ProjectSetup/RevitAreaPlanProvisioner.cs:26-57`）
在 `IsMatchingAreaPlan(existing, package) == false` 時，**會沿用同一個工作包**，建新的 Area Plan 並
`AreaPlanProvisioning.Complete(package, areaPlan.UniqueId)` 存回去。

也就是說：**Area Plan 被刪掉的工作包是可以救回來的**——使用者只要重新執行「防火區劃設定」，
同一個 `PackageId` 就會接上新建的 Area Plan，而它身上的 `DraftingViewUniqueId`、
`LegendViewUniqueIds`、`SheetUniqueId`、`BoundaryRevision`、`LastReviewRunId`、歷次 `ReviewRun`
全部保留。這正好對上 spec §附錄 9.5 的驗收項「Undo、Transaction Rollback、重複執行與**刪除後修復**」。

→ 所以方案選「只過濾，不刪除」。請確認這個推論有沒有錯。

## 3. 提案的變更方案

### 3.1 新增 Application 層述詞 `ReviewPackageAvailability`（可測，無 Revit 型別）

新檔 `src/BuildingRegulationReview.Application/ReviewPackages/ReviewPackageAvailability.cs`：

```csharp
public enum ReviewPackageAvailabilityState
{
    /// 還沒建 Area Plan（AreaPlanUniqueId 為 null／空白）——設定流程還沒跑完。
    AwaitingAreaPlan,
    /// 記錄了 Area Plan，但那個 Area Plan 已不在模型中（使用者刪掉了）。
    AreaPlanDeleted,
    /// Area Plan 還在，面板可以提供這個工作包。
    Available
}

public static class ReviewPackageAvailability
{
    public static ReviewPackageAvailabilityState Classify(
        ReviewPackage package, Func<string, bool> areaPlanIsLive);

    /// 面板選單可以列出的工作包（只有 Available）。
    public static IReadOnlyList<ReviewPackage> Selectable(
        IEnumerable<ReviewPackage> packages, Func<string, bool> areaPlanIsLive);

    /// Area Plan 已被刪除的工作包（給訊息用，知道「有幾個被藏起來」）。
    public static IReadOnlyList<ReviewPackage> WithDeletedAreaPlan(
        IEnumerable<ReviewPackage> packages, Func<string, bool> areaPlanIsLive);
}
```

述詞本身不碰 Revit，`Func<string, bool>` 由 Revit 層注入——與任務 1 同樣的分層
（`CurtainWallExposureClassifier` 在 Application、`RevitWallFunctionReader` 只做轉換）。

### 3.2 新增 Revit 層探測 `RevitAreaPlanProbe`（只做轉換）

新檔 `src/BuildingRegulationReview.Revit/ReviewPackages/RevitAreaPlanProbe.cs`：

```csharp
public bool IsLiveAreaPlan(string? uniqueId) =>
    !string.IsNullOrWhiteSpace(uniqueId) &&
    _document.GetElement(uniqueId) is ViewPlan view &&
    !view.IsTemplate &&
    view.ViewType == ViewType.AreaPlan;
```

**述詞一致性問題，請判定**：現有程式有三套不完全一樣的 Area Plan 述詞——

| 位置 | 述詞 |
| --- | --- |
| `RevitCurtainWallGeometryReader.cs:44-45` | `is ViewPlan && ViewType == AreaPlan && GenLevel is not null` |
| `RevitPlanGeometryExtractor.cs:52-53` | `is ViewPlan && !IsTemplate && ViewType == AreaPlan` |
| `RevitReviewStalenessProbe.cs:40` | 只有 `as ViewPlan`（不問 `ViewType`、不問 `IsTemplate`） |

我選了中間那一套（`!IsTemplate && ViewType == AreaPlan`），刻意**不**加 `GenLevel is not null`：
`GenLevel` 為 null 的 Area Plan 是「存在但不可用」，應該讓它進面板然後由幾何讀取回報錯誤，
而不是直接從選單消失（消失了使用者就不知道為什麼）。這樣對嗎？還是應該全部統一？

### 3.3 三個面板的接線（只改這三處，不改 repository）

1. **防火區劃編輯器** `RegionEditorCommand.ChoosePackage`：
   `.Where(p => !string.IsNullOrWhiteSpace(p.AreaPlanUniqueId))`
   → `ReviewPackageAvailability.Selectable(repository.GetAll(), probe.IsLiveAreaPlan)`。
   標籤的 `?? package.PackageId.ToString()` fall back 保留（理論上到不了，留著當防線）。

2. **防火區劃檢討** `FireReviewCommand.ChoosePackage`：同上換成 `Selectable(...)`。

3. **空選單時的訊息**：目前是「這個專案還沒有建立 Area Plan 的檢討套件，請先執行『防火區劃設定』」。
   當 `WithDeletedAreaPlan` 非空時改成能說明狀況的版本，例如：
   「有 N 個檢討套件的 Area Plan 已被刪除，已不列出。重新執行『防火區劃設定』可以為它們重建
   Area Plan 並接回原有的單線圖與檢討紀錄。」
   → 這樣使用者才知道「刪除後修復」這條路存在。

### 3.4 刻意**不**做的事（請逐項確認是否同意）

| 不做的事 | 理由 |
| --- | --- |
| **不**刪除孤兒 `DataStorage` | 不可逆的模型變更；工作包還掛著 `DraftingViewUniqueId`／`LegendViewUniqueIds`／`SheetUniqueId`／`GeneratedElementUniqueIds`，那些視圖與元素可能已被放進圖框。刪掉工作包會讓它們變成再也關聯不回來的孤兒。而且 §2.1 的「刪除後修復」會失效。使用者只說「讓她無法被看見」，沒有說要刪。 |
| **不**改 `RevitReviewPackageRepository.GetAll()` | 它是儲存層，應該照實回傳。而且 `FireReviewSetupCommand` **必須**看得到孤兒工作包才能重用它（§2.1）；若 `GetAll()` 偷偷過濾，設定流程會為同一個樓層平面＋面積配置再建一個新工作包，模型裡就真的留下重複的孤兒 `DataStorage`。 |
| **不**改 `FireReviewModel.LabelOf` 的 fall back 鏈 | 它服務的是「Area Plan 還沒建好」的工作包（`AwaitingAreaPlan`），那時顯示來源 Floor Plan 名是對的。過濾在選單層做，`LabelOf` 不必知道這件事。 |
| **不**改 `ReviewStaleness` 對缺失 Area Plan 判 `Error` 的行為 | 既有行為正確，且是另一條路徑（已開啟的編輯器）。 |
| **不**在「防火區劃設定」加清理動作 | 任務 2 範圍外。若你認為需要，請標成必要變更，我回報使用者再決定。 |

## 4. 請你特別審查的影響面

1. **`FireReviewSetupCommand.Apply` 的重複判定**（`FireReviewSetupCommand.cs:65-70`）：
   `existing.Count(同 floorplan && 同 scheme) > 1` → 報「請先清理重複資料」。
   既然 `Provision` 會重用工作包，刪 Area Plan 本身應該不會造出重複；但如果模型裡**已經**有
   重複的孤兒工作包（例如舊版本留下的），這條會擋住使用者，而使用者在面板上**看不到**那些孤兒，
   等於被一個看不見的東西擋住。這算不算必要變更？我的傾向是任務 2 先不動，只在審查記錄裡留案。

2. **`ReviewPaneControl` / `ShowReviewPaneCommand`**：這兩支有沒有另一條列出工作包的路徑我沒查到
   `GetAll()` 呼叫，請幫我確認有沒有漏。

3. **`RevitReviewRunRepository` 與結果儲存**：孤兒工作包的 `ReviewRun` 會不會在別處被列出來
   （例如報表、匯出）而仍然顯示 GUID 標籤？`src/BuildingRegulationReview.Revit/Reviews/` 底下。

4. **`RevitReviewSetupOptionsRepository`**：孤兒工作包的 setup options 也還在，有沒有被別處讀出來顯示？

5. **既有測試**：`tests/BuildingRegulationReview.Core.Tests/ReviewPackages/` 下三支測試會不會被
   新述詞影響？我預期不會（新增檔案、不改既有型別）。

6. **分層**：述詞放 Application、探測放 Revit，`RevitAreaPlanProbe` 需不需要抽成 Application 層介面
   （像 `IReviewPackageRepository` 那樣）才符合這個專案的既有慣例？還是 `Func<string, bool>` 就夠？

## 5. 測試計畫

新增 `tests/BuildingRegulationReview.Core.Tests/ReviewPackages/ReviewPackageAvailabilityTests.cs`：

- `AreaPlanUniqueId` 為 null／空白 → `AwaitingAreaPlan`，不在 `Selectable`
- 有 id 但 probe 回 false → `AreaPlanDeleted`，不在 `Selectable`，在 `WithDeletedAreaPlan`
- 有 id 且 probe 回 true → `Available`，在 `Selectable`
- 混合清單：只回傳 Available，順序與輸入一致
- `packages` 為 null／空 → 空清單，不丟例外
- probe 丟例外時的行為（我傾向讓它往外丟，由面板的既有 try/catch 處理；請確認）

`BuildingRegulationReview.Revit` 與 WPF 面板**零測試覆蓋**是專案既有結構性風險，
面板那一層只接線，Revit 實機驗證會寫成 `docs/revit-verification-checklist.md` 的新一條。
