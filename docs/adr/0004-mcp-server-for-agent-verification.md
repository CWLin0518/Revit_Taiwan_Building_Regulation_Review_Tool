# ADR 0004：外掛內建 MCP 伺服器，讓 AI 代理操作功能並驗證

日期：2026-10-07
狀態：已採納
相關：`docs/mcp-server.md`（架構與使用說明）、ADR-0001（分層）

## 脈絡

目標是讓 AI 代理（Claude Code 等）能夠**直接操作外掛的功能並驗證結果**，以後新開發的功能也照同一套方式開放。
這樣開發者改完程式之後，可以請代理在真實的 Revit 模型上跑一次並檢查結果，不必每次都自己點按鈕、看表格。

評估過的做法：

| 做法 | 結論 |
| --- | --- |
| 把工具加到另一個 Revit MCP 專案（REVIT_MCP_study），用反射呼叫本外掛 | 不採用。要修改別人的 repo；用反射呼叫沒有型別檢查；兩邊的 Revit 版本與發布節奏綁在一起 |
| 另外寫一個獨立的 stdio 伺服器（Node 或 .NET 8），用 named pipe 連到外掛 | 不採用。多一個程序要部署、啟動、保持版本一致 |
| **外掛自己在 Revit 程序裡開一個本機 HTTP 端點，直接作為 MCP 伺服器** | **採用** |

## 決策

### 1. 外掛本身就是 MCP 伺服器（Streamable HTTP，只接受 loopback 連線）

- 端點是 `http://127.0.0.1:8970/mcp`，可以用環境變數 `BRR_MCP_PORT` 改連接埠。
- 只實作無狀態的 POST → `application/json` 回應。GET（SSE）與 DELETE（session）回 405，規格允許這樣做。
- 用 `TcpListener` 自己處理 HTTP，不用 `HttpListener`。`HttpListener` 走 http.sys，很多機器要系統管理員權限做 URL 保留，綁 loopback socket 則不需要。
- 預設**關閉**，要在 Ribbon「AI 代理 › MCP 服務」手動開啟，或設定 `BRR_MCP_AUTOSTART=1` 在啟動時自動開。服務開著的時候，本機任何程序都能操作開啟中的模型，所以要由使用者決定是否開啟。
- 依 MCP 規格防 DNS rebinding：`Host` 必須是 `127.0.0.1` 或 `localhost`，有 `Origin` 時也必須是 loopback，否則回 403。
- **每個請求都要帶 Bearer 權杖。** loopback 是整台機器共用的（遠端桌面伺服器上的其他使用者、本機開發伺服器上的網頁都連得到），只靠 Host／Origin 檢查不夠。權杖可以由 `BRR_MCP_TOKEN` 固定；未設定時，每個 Revit 工作階段隨機產生一組，顯示在啟動對話框並複製到剪貼簿。自動啟動必須搭配固定權杖。

### 2. 協定層獨立成 `BuildingRegulationReview.Mcp`（netstandard2.0、不參考 Revit）

JSON、JSON-RPC、工具註冊、HTTP 傳輸都放在這個專案。它只知道 `IMcpTool` 這個抽象，所以 `Core.Tests` 可以在沒有 Revit 的環境完整測試。

**JSON 用自己寫的小型 DOM，不引用 Newtonsoft 或 System.Text.Json。** Revit 程序裡已經載入了 Revit 自己和其他外掛的 JSON 函式庫，再帶一份不同版本進去，可能造成組件衝突，而且這種錯誤很難排查。MCP 用到的 JSON 很少，自己寫比較安全。

### 3. 工具是薄殼：UI 與 MCP 走同一個 use case（**以後每個功能都要遵守**）

```
            ┌─ WPF 視窗／Ribbon 按鈕（給人用）
Use case ───┤
（無 UI）    └─ MCP 工具（給代理用）
```

如果 MCP 走的是另一條專為它寫的路，代理驗證通過，也不能代表使用者按按鈕會得到一樣的結果。因此：

- **流程**（掃描 → 檢查 → 執行 → 儲存）放在無 UI 的 use case。視窗和 MCP 工具都只呼叫 use case，再各自呈現結果。
- **使用者的輸入規則**（例如批次設定面板「只寫入與模型不同的值」、「這一列不問這個欄位」）放在不依賴 WPF 的列模型。MCP 工具把值填進同一批列模型，再向它們要寫入清單，不自己另寫一套規則。只寫在 XAML 裡的可否編輯條件（例如被覆厚度只對 SC 開放），MCP 也必須套用同一組述詞。
- **面板靠人工確認擋下的事，MCP 要明確處理。** 例如面板會一起寫入工具提案的嵌板種類，但寫入前的確認視窗會點名這些值；MCP 沒有這個確認步驟，所以只寫入代理有指定的欄位，其餘列在 `unrequested`。
- **工具不開視窗，也不問問題。** 原本要問使用者的事情改成工具參數，例如要用哪個套件、是否接受新版規則集。缺少答案時回傳工具錯誤，並說明可以怎麼選。
- **需要人做專業判斷的操作不開放**，例如人工覆寫的理由與簽核。代理只能讀取覆寫狀態。

本次依這條原則重構的地方：

| 原本 | 改為 |
| --- | --- |
| `FireReviewWindow.Start()` 自己串 `FireReviewRunner.Run` 和 `FireReviewModel.Save` | `FireReviewModel.RunAndSave()`，視窗與 `fire_review_run` 共用 |
| `FireReviewWindow.RequestScan()` 自己包 try/catch | `FireReviewModel.TryScan()` |
| `FireReviewCommand.ChoosePackage()` 自己篩選套件 | `FireReviewModel.AvailablePackages()`，picker 與 `fire_review_list_packages` 共用 |
| 批次設定面板視窗持有各列 ViewModel、樓層推定和收集寫入清單的邏輯 | `FireReviewParameterDraft`（不含 WPF），面板與 `fire_review_set_parameters` 共用 |

### 4. 所有 Revit 呼叫都經過同一個 ExternalEvent 佇列（`RevitMcpDispatcher`）

- 工具呼叫從 socket 執行緒進來，先排進佇列，再 raise 一個 ExternalEvent，由 Revit 在 API context 依序執行。兩個代理，或者代理與使用者自己的按鈕，都不會交錯執行交易。
- Revit 有對話框開著，或正在執行指令時，ExternalEvent 不會觸發。工具 **30 秒內沒開始執行**就撤回，並回報「Revit 忙碌中」，不會讓代理一直等。
- 已開始執行的工作有各自的時限（預設 2 分鐘；會跑前置掃描的工具，例如 `fire_review_run`、`fire_review_check`，是 10 分鐘）。逾時會設定 CancellationToken，讓工作在下一個安全點停下來。
- 呼叫端只要不再等待（逾時、服務停止或任何例外），都會先撤回工作。已撤回的工作不會在之後偷偷執行，所以代理收到錯誤時，可以確定模型沒有被改動。

### 5. 改模型的工具都提供 `dryRun`

`dryRun=true` 時，工作照常執行，然後把整個 `TransactionGroup` 復原：工作本身能看到自己寫入的結果，所以回傳內容與實際執行時完全一樣，但模型保持原狀。驗證時代理可以先用 dryRun 跑一次。

工具附上 MCP annotations（`readOnlyHint`、`destructiveHint`），讓 client 判斷是否要先請使用者確認。

## 後果

- 新功能要開放給代理時，依 `docs/mcp-server.md`「新增功能的工具」一節的清單做：先抽出 use case，再寫一個 `IMcpToolModule`，最後在 `RevitMcpHost` 註冊一行。不必修改協定層。
- 第 164 條檢討目前**沒有**開放給 MCP。它的流程包含 `PickObject`／`PickPoint` 互動選取和兩個對話框，要開放的話得先依第 3 點重構。
- 長時間的工作目前是同步呼叫，沒有進度回報。如果之後模型大到超過 client 的逾時，再改成 job 模式（`*_start` → `job_status` → `job_result`）。
- Revit 會鎖住已載入的 DLL，改完程式仍然要重開 Revit 才會生效。代理目前做不到「改程式 → 驗證」的完整循環，熱載入留待之後評估。
- 協定層有單元測試（`tests/.../Mcp/`）。Revit 端的工具要在 Revit 實際驗證，見 `docs/revit-verification-checklist.md`。

## 驗證

- `dotnet build BuildingRegulationReview.sln --configuration Debug`
- `dotnet test tests/BuildingRegulationReview.Core.Tests/BuildingRegulationReview.Core.Tests.csproj --filter "FullyQualifiedName~Core.Tests.Mcp"`
- 在 Revit 開啟 MCP 服務後，依 `docs/mcp-server.md`「驗證流程範例」執行一次。
