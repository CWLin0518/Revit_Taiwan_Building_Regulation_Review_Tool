# MCP 伺服器：讓 AI 代理操作外掛並驗證

外掛內建一個 [Model Context Protocol](https://modelcontextprotocol.io/) 伺服器。AI 代理（Claude Code、Claude Desktop 等）連上之後，可以呼叫外掛的功能來操作使用者目前開啟的 Revit 模型，並讀回結果做驗證。

設計決策與理由見 [ADR-0004](adr/0004-mcp-server-for-agent-verification.md)。這份文件說明架構、使用方式，以及以後新功能如何接入。

目前開放的功能：**防火區劃檢討**與**防火參數批次設定**。第 164 條檢討還沒有開放（見 §9）。

---

## 1. 架構總覽

```text
AI 代理（Claude Code …）
   │  stdio（JSON-RPC 2.0，一行一則）
   ▼
BuildingRegulationReview.McpBridge.exe（net48，代理啟動；Revit 沒開也連得上）
   │  每次請求先讀 %LOCALAPPDATA%\BuildingRegulationReview\Mcp\endpoint.json（端點＋權杖）
   │  HTTP POST  http://127.0.0.1:8970/mcp   （MCP Streamable HTTP，Bearer 權杖）
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
| `src/BuildingRegulationReview.Mcp/Bridge/` | `McpEndpointFile`（端點檔）、`HttpMcpUpstream`、`McpStdioBridge`（stdio 那一端的全部邏輯，Core.Tests 測） |
| `src/BuildingRegulationReview.McpBridge/` | bridge exe：只有 stdin／stdout 接線與每 3 秒一次的 `Poll` |
| `scripts/register-claude-mcp.ps1` | 把 bridge 註冊為 Claude Code 使用者層級的 stdio MCP（安裝腳本會呼叫） |
| `src/BuildingRegulationReview/Mcp/RevitMcpHost.cs` | 伺服器名稱、連接埠、`instructions`、註冊模組 |
| `src/BuildingRegulationReview/Mcp/RevitMcpDispatcher.cs` | ExternalEvent 佇列、忙碌偵測、逾時取消 |
| `src/BuildingRegulationReview/Mcp/RevitMcpTool.cs` | 在 Revit 執行的工具基底：`RequireProject`、`WithDryRun` |
| `src/BuildingRegulationReview/Mcp/McpServiceCommand.cs` | Ribbon「MCP 服務」開關 |
| `src/BuildingRegulationReview/Mcp/CoreMcpTools.cs` | `revit_status` |
| `src/BuildingRegulationReview/Mcp/FireReview/FireReviewMcpTools.cs` | 七個 `fire_review_*` 工具 |
| `src/BuildingRegulationReview/Mcp/FireReview/FireReviewJson.cs` | 套件、前置檢查、檢討表、明細、參數列轉 JSON |
| `tests/BuildingRegulationReview.Core.Tests/Mcp/` | JSON、協定、HTTP 端點的單元測試 |

部署時多一個 `BuildingRegulationReview.Mcp.dll`，以及 `McpBridge\` 資料夾（bridge exe），`scripts/install-revit-2024.ps1` 都已經處理。

---

## 3. 啟動與連線

### 3.1 一次設定，之後自動連線

```text
scripts\install-revit-2024.ps1
  ├─ 部署外掛與 McpBridge\BuildingRegulationReview.McpBridge.exe
  └─ scriptsegister-claude-mcp.ps1
       claude mcp add --scope user building-regulation-review -- "<安裝目錄>\McpBridge\BuildingRegulationReview.McpBridge.exe"
       （同時移除舊版留下、寫死權杖的 HTTP 設定）
```

之後：

- **Revit**：外掛載入時 MCP 服務**自動啟動**，把端點與這次的隨機權杖寫進
  `%LOCALAPPDATA%\BuildingRegulationReview\Mcp\endpoint.json`；服務停止或 Revit 關閉時刪掉（只刪自己寫的）。
- **Claude Code**：每個工作階段啟動時自己把 bridge 帶起來。bridge 自己回答 `initialize`，所以**先開 Claude、後開 Revit 也連得上**。
- **bridge**：每個請求都重讀端點檔，所以 Revit 重開、權杖換新、改連接埠都**不必改代理設定、也不必重開 Claude**。
  每 3 秒向 Revit 要一次工具清單，有變化就送 `notifications/tools/list_changed`，代理會自動重新列出工具。
- Revit 沒開時呼叫工具，回傳的是工具錯誤（`isError`），說明要開 Revit、看 MCP 按鈕；Revit 開好後直接再呼叫即可。
- 工具清單快取在同一個資料夾的 `tools-cache.json`。從沒連上過 Revit 時，清單只有一個說明用的 `revit_status`，連上後自動換成完整清單。

| 環境變數 | 預設 | 作用 |
| --- | --- | --- |
| `BRR_MCP_PORT` | `8970` | 連接埠（1024–65535）。端點檔會寫實際的連接埠，bridge 不必另外設定 |
| `BRR_MCP_TOKEN` | 未設 | 固定權杖。未設時每次開 Revit 隨機產生一組，經端點檔交給 bridge |
| `BRR_MCP_AUTOSTART` | 未設（＝自動啟動） | 設為 `0` 或 `false` 時，Revit 啟動不自動開服務，要按 Ribbon 的按鈕 |

Ribbon「建築法規檢討 › AI 代理 › **MCP 服務**」仍可手動停止／啟動；啟動時的對話框會把 bridge 的註冊指令複製到剪貼簿，
給沒跑安裝腳本的機器用。連接埠被占用（例如同時開了兩個 Revit）時，第二個 Revit 的服務不會啟動，也不會覆蓋第一個的端點檔。

### 3.2 其他 client

任何支援 stdio 的 MCP client 都可以直接啟動 bridge exe。只支援 HTTP 的 client 仍可直連 `http://127.0.0.1:8970/mcp`，
權杖從端點檔讀（`Authorization: Bearer <token>`）；但那樣每次 Revit 重開都要更新權杖，這正是 bridge 要解決的問題。

**請調高 client 的工具逾時。** 一次呼叫最多可能是排隊 30 秒加上執行 10 分鐘（bridge 對 `tools/call` 等 12 分鐘）。
Claude Code 可以用環境變數 `MCP_TOOL_TIMEOUT`（毫秒）設定，例如 `720000`。client 逾時並不會取消 Revit 端的工作：
如果代理在逾時後重試 `fire_review_run`，會排進第二次執行。

連上之後，代理看到的工具名稱會加上伺服器名稱作為前綴，例如 `mcp__building-regulation-review__fire_review_run`。
這個伺服器可以和其他 Revit MCP（例如 REVIT_MCP_study 的 `revit-mcp`）同時使用，兩邊的工具不會衝突。

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
| `fire_review_check` | U | 前置掃描與前置檢查：`readiness.items`（Blocking／Warning／Info＋修正方式）、`canRun`、上次結果是否需更新；日誌筆數用 `logLimit` |
| `fire_review_run` | W | 開始檢討：檢查 → 檢討 → 儲存 → 標示檢討視圖；支援 `dryRun`；回傳總狀態、統計與篩選後的項目；日誌筆數用 `logLimit` |
| `fire_review_get_results` | U | 最近一次的檢討表，並依目前模型標示需更新（`checkFreshness`，預設 true）；可篩選 |
| `fire_review_describe_result` | U | 單一項目的完整明細，內容與視窗「複製明細」相同（`checkFreshness` 預設 false） |

`fire_review_check`、`get_results`、`describe_result` 標為 U，是因為它們和視窗開啟時一樣，會把「需更新」等判定寫回套件，但不會產生新的檢討結果。

`checkFreshness=true` 會重跑一次前置掃描，大模型可能要數十秒。逐項查看明細時，用預設的 false 直接讀取儲存的結果會快很多，但不會標示需更新；需要「與視窗逐字相同」時，再設為 true。

`fire_review_scan_parameters` 的 `onlyNeedingAttention` 與「待填」的定義一律跟著面板：**面板那一格是停用的，就不算待填**。例如已宣告「玻璃」的帷幕嵌板不以防火時效作答，所以它不會被列為待填結構材料，`derived.basis` 也會說明這一列為什麼不做尺寸推定，而不是叫人去填一個填不到的欄位。

### 6.1 共用參數

- `packageId`：省略時，如果專案只有一個套件就自動選它；有多個套件時，回傳工具錯誤並附上套件清單。
- 篩選（`run`、`get_results`）：`statuses`（`Fail`／`Pending`／`Pass`／`NotApplicable`）、`checkType`（`CompartmentArea`、`AreaExemption`、`FireResistance`、`OpeningProtection`、`CompartmentContinuity`、`VerticalCompartment`）、`search`、`staleOnly`、`limit`。**統計永遠計算全部項目**，篩選只決定列出哪些項目，和視窗的篩選列相同。`limit` 預設值：`run` 20 筆（它的回傳還帶著規則集、前置檢查與日誌）、`get_results` 100 筆；超過時 `matchedCount` 與 `truncated` 照樣說出全部有幾筆。
- `logLimit`（`check`、`run`，即會回 `log` 的工具）：日誌最多回幾筆，**預設 20**，`0` 表示只回統計不回 `entries`，上限 2000。一次檢討的日誌可以上百筆、每筆好幾百字，整包回傳會超過 client 一次能讀的上限，所以預設只給依嚴重度排序後最重要的前幾筆。`log.total`、`log.errors`、`log.warnings` 永遠是全部的實情，`log.listed` 是這次列出的筆數，被截斷時 `log.truncated` 為 `true`；要看全部就把 `logLimit` 開大。
- `dryRun`：照常執行後整批復原，模型與 Revit 的復原清單都不會改變。檢討與寫入的**計算結果**和實際執行時相同，唯一的差別是回傳會把「沒有留下任何東西」這件事說出來：`fire_review_run` 的 `saved.saved` 為 `false`、`saved.rolledBack` 為 `true`、`saved.note` 說明已整批復原（`saved.mark.summary` 仍是實際算出的標示結果，可以用來比對），`fire_review_set_parameters` 的 `committed` 為 `false`；訊息開頭都是「〔試跑，已復原〕」。

### 6.2 回傳值的慣例

- 狀態同時提供兩種：列舉名稱（例如 `"status": "Fail"`，方便比對），以及中文標籤（例如 `"statusText": "未符合"`，方便報告）。
- 元素同時提供 `uniqueIds` 與 `elementIds`。`elementIds` 可以直接交給其他 Revit 工具使用。
- 項目超過 `limit` 時，會回傳 `truncated: true`，並附上 `matchedCount`。
- 日誌依嚴重度遞減排序，最多 `logLimit` 筆（預設 20），並附上 `total`、`errors`、`warnings`、`listed` 與 `truncated`。**筆數上限只影響列出哪幾筆，統計永遠是全部。**
- **同一段內容只回一次。** 規則集的說明文（`ruleSet.title` 有兩千多字）只放在回傳的頂層 `ruleSet`；`readiness` 不再重複一份，它用 `needsRuleSetConfirmation` 與 `ruleSetChanges` 表達前置檢查關心的事。

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
- 預設隨 Revit 自動啟動（`BRR_MCP_AUTOSTART=0` 可關）。權杖寫在 `%LOCALAPPDATA%` 的端點檔，**其他 Windows 帳號讀不到，
  同一個帳號的程序讀得到**——這和以前把權杖放在對話框、剪貼簿、`~/.claude.json` 的範圍相同。**服務開著的時候，
  本帳號的程序可以讀寫開啟中的模型**，啟動對話框會明白提醒這一點。
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
