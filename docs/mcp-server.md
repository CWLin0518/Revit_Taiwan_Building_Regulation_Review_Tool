# MCP 伺服器：讓 AI 代理操作外掛並驗證

外掛內建一個 [Model Context Protocol](https://modelcontextprotocol.io/) 伺服器。AI 代理（Claude Code、Claude Desktop 等）連上之後，可以呼叫外掛的功能來操作使用者目前開啟的 Revit 模型，並讀回結果做驗證。

設計決策與理由見 [ADR-0004](adr/0004-mcp-server-for-agent-verification.md)。這份文件說明架構、使用方式，以及以後新功能如何接入。

目前開放的功能：**防火區劃檢討**與**防火參數批次設定**。第 164 條檢討還沒有開放（見 §9）。

---

## 1. 架構總覽

```text
AI 代理（Claude Code …）
   │  HTTP POST  http://127.0.0.1:8970/mcp   （JSON-RPC 2.0，MCP Streamable HTTP）
   ▼
┌──────────────────────────────── Revit 程序 ────────────────────────────────┐
│ BuildingRegulationReview.Mcp（netstandard2.0，不參考 Revit）               │
│   McpHttpListener   loopback socket、Host／Origin 檢查、HTTP 解析          │
│   McpServer         initialize / ping / tools/list / tools/call            │
│   McpToolRegistry   IMcpTool、IMcpToolModule                               │
│   Json*             自己寫的小型 JSON DOM（不帶第三方 JSON 函式庫進 Revit） │
│                         │ tools/call                                       │
│                         ▼                                                  │
│ BuildingRegulationReview（外掛，net48）                                    │
│   Mcp/RevitMcpHost        組裝模組、啟停端點                               │
│   Mcp/RevitMcpDispatcher  單一 ExternalEvent 佇列 → Revit API context      │
│   Mcp/RevitMcpTool        在 API context 執行的工具基底、dryRun            │
│   Mcp/CoreMcpTools        revit_status                                     │
│   Mcp/FireReview/*        fire_review_* 工具、結果轉 JSON                  │
│                         │ 呼叫與 UI 相同的 use case                        │
│                         ▼                                                  │
│   FireReview/FireReviewModel           AvailablePackages / TryScan /       │
│                                        RunAndSave / Save                   │
│   FireReview/FireReviewParameterDraft  批次設定面板的列與寫入清單          │
│                         │                                                  │
│                         ▼                                                  │
│ Application（規則、檢討表）・Revit（Adapter）・Domain                      │
└────────────────────────────────────────────────────────────────────────────┘
```

### 1.1 核心原則：UI 和 MCP 走同一條路

```text
             ┌─ WPF 視窗／Ribbon 按鈕（給人用）
Use case ────┤
（無 UI）     └─ MCP 工具（給代理用）
```

MCP 工具只是薄殼，做三件事：讀參數、呼叫 use case、把結果轉成 JSON。檢討規則、寫入規則、檢討表的組成，**全部**沿用視窗呼叫的同一段程式。所以代理驗證通過，代表使用者按按鈕也會得到一樣的結果。

| 功能 | 視窗呼叫 | MCP 工具呼叫 |
| --- | --- | --- |
| 列出套件 | `FireReviewCommand` → `FireReviewModel.AvailablePackages` | `fire_review_list_packages` → 同左 |
| 前置掃描與檢查 | `FireReviewWindow.RequestScan` → `FireReviewModel.TryScan` | `fire_review_check` → 同左 |
| 開始檢討 | `FireReviewWindow.Start` → `FireReviewModel.RunAndSave` | `fire_review_run` → 同左 |
| 檢討表 | `ReviewTable`、`ReviewTableFilter` | `fire_review_get_results` → 同左 |
| 明細 | `ReviewEntryReport.Describe` | `fire_review_describe_result` → 同左 |
| 批次設定 | 面板 → `FireReviewParameterDraft` → `RevitFireReviewParameterWriter` | `fire_review_set_parameters` → 同左 |

---

## 2. 專案與檔案

| 位置 | 內容 |
| --- | --- |
| `src/BuildingRegulationReview.Mcp/Json/` | `JsonValue`（DOM）、`JsonParser`（嚴格 RFC 8259，深度上限 128）、`JsonWriter` |
| `src/BuildingRegulationReview.Mcp/Protocol/McpServer.cs` | JSON-RPC 分派、協定版本協商、工具錯誤轉換、最近 20 筆呼叫紀錄 |
| `src/BuildingRegulationReview.Mcp/Tools/` | `IMcpTool`、`IMcpToolModule`、`McpToolResult`、`McpToolException`、`McpToolArguments`、`JsonSchema`、`McpToolRegistry` |
| `src/BuildingRegulationReview.Mcp/Transport/McpHttpListener.cs` | loopback HTTP 端點 |
| `src/BuildingRegulationReview/Mcp/RevitMcpHost.cs` | 伺服器名稱、連接埠、`instructions`、註冊模組 |
| `src/BuildingRegulationReview/Mcp/RevitMcpDispatcher.cs` | ExternalEvent 佇列、忙碌偵測、逾時取消 |
| `src/BuildingRegulationReview/Mcp/RevitMcpTool.cs` | 在 Revit 執行的工具基底：`RequireProject`、`WithDryRun` |
| `src/BuildingRegulationReview/Mcp/McpServiceCommand.cs` | Ribbon「MCP 服務」開關 |
| `src/BuildingRegulationReview/Mcp/CoreMcpTools.cs` | `revit_status` |
| `src/BuildingRegulationReview/Mcp/FireReview/FireReviewMcpTools.cs` | 七個 `fire_review_*` 工具 |
| `src/BuildingRegulationReview/Mcp/FireReview/FireReviewJson.cs` | 套件、前置檢查、檢討表、明細、參數列轉 JSON |
| `tests/BuildingRegulationReview.Core.Tests/Mcp/` | JSON、協定、HTTP 端點的單元測試 |

部署時多一個 `BuildingRegulationReview.Mcp.dll`，`scripts/install-revit-2024.ps1` 已經加入。

---

## 3. 啟動與連線

### 3.1 在 Revit 開啟服務

Ribbon「建築法規檢討 › AI 代理 › **MCP 服務**」。按一下就啟動，再按一下就停止。按鈕文字會顯示目前狀態。沒有開模型時也可以按。

| 環境變數 | 預設 | 作用 |
| --- | --- | --- |
| `BRR_MCP_PORT` | `8970` | 連接埠（1024–65535） |
| `BRR_MCP_TOKEN` | 未設 | 固定的存取權杖。未設時，每次開啟 Revit 會隨機產生一組，只在按鈕的對話框中顯示 |
| `BRR_MCP_AUTOSTART` | 未設 | 設為 `1` 時，Revit 啟動就自動開啟服務（適合讓代理自動驗證的工作機）。**必須同時設定 `BRR_MCP_TOKEN`**，否則不會自動開啟，因為隨機權杖不會有人看到 |

連接埠被占用（例如同時開了兩個 Revit）時，按鈕會顯示錯誤並說明怎麼改連接埠，外掛的其他功能不受影響。

### 3.2 連線

每個請求都要帶 `Authorization: Bearer <權杖>`，否則回 401。按下按鈕時，對話框會顯示已含權杖的 Claude Code 指令，並**自動複製到剪貼簿**：

```bash
claude mcp add --transport http building-regulation-review http://127.0.0.1:8970/mcp --header "Authorization: Bearer <權杖>"
```

使用固定權杖（`BRR_MCP_TOKEN`）時，可以寫在專案的 `.mcp.json`，權杖從環境變數展開，不必寫進檔案：

```json
{
  "mcpServers": {
    "building-regulation-review": {
      "type": "http",
      "url": "http://127.0.0.1:8970/mcp",
      "headers": { "Authorization": "Bearer ${BRR_MCP_TOKEN}" }
    }
  }
}
```

**請調高 client 的工具逾時。** 一次呼叫最多可能是排隊 30 秒加上執行 10 分鐘，Claude Code 可以用環境變數 `MCP_TOOL_TIMEOUT`（毫秒）設定，例如 `720000`。client 逾時並不會取消 Revit 端的工作：如果代理在逾時後重試 `fire_review_run`，會排進第二次執行。

連上之後，代理看到的工具名稱會加上伺服器名稱作為前綴，例如 `mcp__building-regulation-review__fire_review_run`。這個伺服器可以和其他 Revit MCP（例如 REVIT_MCP_study 的 `revit-mcp`）同時使用，兩邊的工具不會衝突。

---

## 4. 一次工具呼叫的流程

```text
socket 執行緒                                     Revit 主執行緒（API context）
─────────────                                     ─────────────────────────────
McpHttpListener 讀取 HTTP、檢查 Host／Origin
McpServer.Handle → tools/call
McpToolArguments 包好參數
RevitMcpTool.Call
  └ RevitMcpDispatcher.Invoke ──── 排入佇列，raise ExternalEvent
       等待「開始」（最多 30 秒） ┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄ Revit 閒置時 → Execute
                                                   依序取出工作
                                                     RequireProject
                                                     WithDryRun（需要時開 TransactionGroup）
                                                       use case（Scan／RunAndSave／Writer…）
                                                     結果轉成 JSON
       等待「完成」（工具各自的時限） ◄┄┄┄┄┄┄┄┄┄┄┄ 設定完成
McpToolResult → CallToolResult
HTTP 200 application/json
```

### 4.1 錯誤分兩種

| 情況 | 回傳方式 | 例子 |
| --- | --- | --- |
| 協定錯誤 | JSON-RPC `error` | JSON 格式壞掉、未知的方法、未知的工具 |
| 工具錯誤 | `result.isError = true`，`structuredContent.error` 放說明 | 參數錯誤、沒開模型、Revit 忙碌中、前置檢查未通過、寫入失敗 |

工具錯誤是**給代理讀了之後修正再試的**，所以訊息會指出是哪一個參數、怎麼修正，必要時附上結構化資料。例如前置檢查沒通過時，會附上整份 `readiness`；有多個套件卻沒指定時，會附上套件清單。

---

## 5. 執行緒、逾時與「Revit 忙碌中」

- **單一佇列**：所有工具共用一個 ExternalEvent，依到達順序一次執行一個，不會有兩個交易交錯執行。
- **Revit 只在閒置時執行外部請求**。有 modal 對話框開著，或正在執行指令（繪製、選取、編輯模式）時，工作不會開始。等待期間每 2 秒會重新 raise 一次 ExternalEvent，避免剛好錯過 Revit 的處理時機。工作在 **30 秒**內沒開始，就會撤回並回報「Revit 忙碌中」。
- **呼叫端放棄的工作絕不會執行**：不論是逾時、服務停止還是其他例外，等待端一律先撤回工作。所以代理收到錯誤時，可以確定模型沒有被改動。
- **執行時限**：預設 2 分鐘，`fire_review_run` 是 10 分鐘。超過時限會設定 CancellationToken，`FireReviewRunner` 會在下一個安全點停下來，不寫入任何東西；再等 60 秒仍沒結束，就回報「仍在執行」。
- `revit_status` 在 Revit 忙碌時仍會回應：`revitResponsive: false`，並附上正在執行的工具名稱（`running`）、排隊數量（`queued`）和最近的呼叫紀錄。

---

## 6. 工具清單

標記：**R**＝唯讀；**U**＝會寫入外掛自己的狀態（例如套件狀態、需更新標記）；**W**＝會改使用者的資料或檢討結果，client 可能會要求確認。

| 工具 | 類型 | 作用 |
| --- | --- | --- |
| `revit_status` | R | Revit／外掛版本、作用中的文件與視圖、目前在執行的工作、最近 20 筆呼叫 |
| `fire_review_list_packages` | R | 可檢討的套件（`packageId`、名稱、狀態、規則集） |
| `fire_review_scan_parameters` | R | 批次設定面板的內容：類型、區劃、專案資訊，每列的 `values`、`asks`、`derived`、`missingParameters` |
| `fire_review_set_parameters` | W | 用面板的規則寫入參數，只寫與模型不同的值，整批一個交易；支援 `dryRun` |
| `fire_review_check` | U | 前置掃描與前置檢查：`readiness.items`（Blocking／Warning／Info＋修正方式）、`canRun`、上次結果是否需更新 |
| `fire_review_run` | W | 開始檢討：檢查 → 檢討 → 儲存 → 標示檢討視圖；支援 `dryRun`；回傳總狀態、統計與篩選後的項目 |
| `fire_review_get_results` | U | 最近一次的檢討表，並依目前模型標示需更新（`checkFreshness`，預設 true）；可篩選 |
| `fire_review_describe_result` | U | 單一項目的完整明細，內容與視窗「複製明細」相同（`checkFreshness` 預設 false） |

`fire_review_check`、`get_results`、`describe_result` 標為 U，是因為它們和視窗開啟時一樣，會把「需更新」等判定寫回套件，但不會產生新的檢討結果。

`checkFreshness=true` 會重跑一次前置掃描，大模型可能要數十秒。逐項查看明細時，用預設的 false 直接讀取儲存的結果會快很多，但不會標示需更新；需要「與視窗逐字相同」時，再設為 true。

### 6.1 共用參數

- `packageId`：省略時，如果專案只有一個套件就自動選它；有多個套件時，回傳工具錯誤並附上套件清單。
- 篩選（`run`、`get_results`）：`statuses`（`Fail`／`Pending`／`Pass`／`NotApplicable`）、`checkType`（`CompartmentArea`、`AreaExemption`、`FireResistance`、`OpeningProtection`、`CompartmentContinuity`、`VerticalCompartment`）、`search`、`staleOnly`、`limit`。**統計永遠計算全部項目**，篩選只決定列出哪些項目，和視窗的篩選列相同。
- `dryRun`：照常執行後整批復原。回傳內容和實際執行時相同，模型與 Revit 的復原清單都不會改變。

### 6.2 回傳值的慣例

- 狀態同時提供兩種：列舉名稱（例如 `"status": "Fail"`，方便比對），以及中文標籤（例如 `"statusText": "未符合"`，方便報告）。
- 元素同時提供 `uniqueIds` 與 `elementIds`。`elementIds` 可以直接交給其他 Revit 工具使用。
- 項目超過 `limit` 時，會回傳 `truncated: true`，並附上 `matchedCount`。
- 日誌依嚴重度排序，最多 100 筆，並附上 `errors`、`warnings` 的筆數。

### 6.3 `fire_review_set_parameters` 的值

寫法和面板相同，由面板的列模型決定實際寫入的值：

| 欄位 | 寫法 |
| --- | --- |
| 類型 `material` | `RC`、`SRC`、`SC`；`null` 清除 |
| 類型 `coverCm` | 公分，數字 |
| 類型 `rating` | 例如 `1h`、`2h`、`60min` |
| 類型 `applyDerivedRating` | `true`＝面板的「套用推定值」，不能和 `rating` 同時用 |
| 類型 `panelKind` | `實心`、`玻璃` |
| 類型 `fireProtection`／`smokeProtection`／`insulation` | `true`／`false`（面板的勾選框沒有「未填」） |
| 區劃 `use` | 必須和清單上的字逐字相同才有作用（見 `docs/regulations/zone-use-vocabulary.md`）；`null`＝一般區劃 |
| 區劃 `sprinklered`／`linksRefugeFloor`／`cannotBeSubdivided` | `true`／`false`／`null`（是／否／未填） |
| 專案 `fireResistiveConstruction` | 同上；不是 `true` 的話整份檢討都會判資料不足 |
| `deriveFloors` | `true`＝面板的「依樓層推定」，先填樓層序與地上層數，再套用其他值 |

**欄位的可否編輯條件和面板相同**：面板上那一格是灰的（例如玻璃嵌板的時效、非 SC 的被覆厚度、不是第79條之1 用途的「無法區劃分隔」、Revit 保留的嵌板型別），MCP 就不寫入，改列在 `ignored` 並說明原因。填的值和模型相同時也列在 `ignored`。`material`、`panelKind` 只接受面板下拉選單有的值，其他值會直接回傳錯誤。

**只寫入代理有指定的欄位。** 面板的列有時會帶著使用者沒改過的值，最主要的是工具依嵌板材料提案、但模型還沒宣告的「嵌板種類」。面板會在確認視窗點名這些值，由人決定是否寫入；MCP 沒有這個確認步驟，所以這些值**不寫入**，改列在 `unrequested`。如果確定要一起寫入，可以設 `acceptProposedPanelKinds: true`。

寫入器會略過寫不進去的值（例如參數沒有綁定到該類別），其餘照常寫入。只要有任何一筆失敗，工具就回傳 `isError`，失敗的值列在 `failures`。

**不會寫入** `防火檢討_法規要求防火時效`，這個參數由規則回寫。

---

## 7. 安全

- 只綁 `127.0.0.1`，並以獨占模式佔用連接埠，其他機器無法連線，其他程序也不能同時綁定同一個連接埠。
- **每個請求都要帶 Bearer 權杖**。loopback 是同一台機器上所有工作階段共用的，例如遠端桌面伺服器上其他使用者的程序，或本機開發伺服器上的網頁，都連得到 127.0.0.1；沒有權杖就無法操作。權杖比對採固定時間，比對時間不會透露權杖內容。
- 同時最多 8 條連線，超過的回 503。
- 依 MCP 規格防 DNS rebinding：`Host` 不是 `127.0.0.1`／`localhost`，或 `Origin` 不是 loopback 時，一律回 403。瀏覽器裡的網頁沒辦法操作 Revit。
- 預設關閉，由使用者在 Ribbon 開啟。**服務開著的時候，本機任何程序都能讀寫開啟中的模型**，對話框會明白提醒這一點。
- 改模型的工具標有 `destructiveHint`／`readOnlyHint`，client 可以據此要求使用者確認。
- 人工覆寫（Override／Reconfirm／Withdraw）需要理由與簽核人，不開放給 MCP。

---

## 8. 驗證流程範例

請代理在開發完成後驗證一次「防火區劃檢討」的流程：

```text
1. revit_status                          → 確認模型與 Revit 可以回應
2. fire_review_list_packages             → 取得 packageId
3. fire_review_scan_parameters  onlyNeedingAttention=true
                                         → 找出缺參數、待填材料的類型與區劃
4. fire_review_set_parameters   dryRun=true → 檢查 edits／ignored 是否符合預期
5. fire_review_set_parameters            → 實際寫入
6. fire_review_check                     → canRun=true？Blocking 項目都處理了嗎？
7. fire_review_run              dryRun=true → 先看結果，模型不變
8. fire_review_run                       → 儲存並標示
9. fire_review_get_results      statuses=["Fail"]
10. fire_review_describe_result resultId=… → 逐項核對條文、實際值與要求值
```

開發時可以把驗證要點寫成測試案例，例如「這個 RC 牆類型推定為 2h；某個區劃面積超過上限要判未符合」，再請代理照上面的流程跑一次，逐項回報是否符合。

---

## 9. 新增功能的工具（開發規範）

以後的新功能要開放給代理時，照這個清單做：

1. **抽出 use case**：把 Command 或視窗裡的流程（收集 → 計算 → 寫入）搬到一個不依賴 WPF 的類別或方法。視窗改成呼叫它，視窗本身的行為不變。
2. **把問題改成參數**：原本用 `TaskDialog`、`ShowDialog`、`PickObject` 問使用者的事，改成 use case 的參數。UI 版照樣問使用者，再把答案傳進去；MCP 版從工具參數取得。
3. **寫工具模組**：在 `src/BuildingRegulationReview/Mcp/<功能>/` 新增 `IMcpToolModule`，每個工具繼承 `RevitMcpTool`：

   ```csharp
   internal sealed class MyFeatureMcpTools : IMcpToolModule
   {
       private readonly RevitMcpDispatcher _dispatcher;
       public MyFeatureMcpTools(RevitMcpDispatcher dispatcher) => _dispatcher = dispatcher;
       public IEnumerable<IMcpTool> Tools => new IMcpTool[] { new RunTool(_dispatcher) };

       private sealed class RunTool : RevitMcpTool
       {
           public RunTool(RevitMcpDispatcher dispatcher) : base(dispatcher) { }
           public override string Name => "my_feature_run";          // 小寫、底線、以功能為前綴
           public override string Title => "執行某某檢討";
           public override string Description => "做什麼、何時用、會改什麼（代理只靠這段文字判斷）";
           public override JsonObject InputSchema => JsonSchema.Object(new JsonObject
           {
               ["dryRun"] = JsonSchema.Boolean("只計算不留下任何變更。", false)
           });
           public override McpToolAnnotations Annotations => McpToolAnnotations.Updates;

           protected override McpToolResult Execute(UIApplication app, McpToolArguments args, CancellationToken token)
           {
               var document = RequireProject(app);
               var dryRun = args.OptionalBool("dryRun", false);
               return WithDryRun(document, dryRun, "某某檢討", () =>
               {
                   var outcome = MyFeatureModel.Run(document, /* 參數 */, token);   // 與按鈕相同的 use case
                   return McpToolResult.Success(MyFeatureJson.Outcome(outcome), outcome.Message);
               });
           }
       }
   }
   ```

4. **註冊**：在 `RevitMcpHost` 的建構子加一行 `.AddModule(new MyFeatureMcpTools(dispatcher))`。
5. **回傳值**：遵守 §6.2 的慣例。預期內的失敗用 `McpToolException` 或 `McpToolResult.Failure`，並說明怎麼修正。
6. **文件**：在本文件 §6 加入工具，在 `docs/revit-verification-checklist.md` 加入實機驗證項目，並更新 `RevitMcpHost.Instructions` 的建議流程。

**不要做的事**：

- 在工具裡重寫一份檢討或寫入規則。
- 在工具裡開視窗或 `TaskDialog`，這會卡住 ExternalEvent，直到逾時。
- 從工具直接呼叫 `ExternalEvent.Raise`。所有 Revit 呼叫都要經過 `RevitMcpDispatcher`。
- 在 `BuildingRegulationReview.Mcp` 參考 Revit API 或 Domain。協定層要保持可以單獨測試。

---

## 10. 限制與後續

| 項目 | 現況 | 之後的方向 |
| --- | --- | --- |
| 第 164 條檢討 | 沒有開放。流程中有 `PickObject`／`PickPoint` 和兩個對話框 | 依 §9 把建築線、道路側與選項抽成參數後再開放 |
| 防火區劃設定、區劃編輯器、參數一鍵建立 | 沒有開放 | 編輯器以手動操作為主，最多開放「讀取區劃」或 headless 寫回 |
| 長時間工作 | 同步呼叫，沒有進度回報 | 需要時改成 job 模式（`*_start` → `job_status` → `job_result`） |
| 開發迴圈 | 改完程式要重開 Revit | 評估用薄外殼熱載入核心 DLL |
| 協定 | 只支援 tools；沒有 resources、prompts、SSE、session | 有需要再加 |
| Revit 端測試 | 只能實機驗證（`docs/revit-verification-checklist.md` 第 12 輪） | 用 MCP 本身做自動化的回歸驗證 |
