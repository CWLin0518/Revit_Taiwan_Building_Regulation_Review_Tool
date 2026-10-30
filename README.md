# Revit Taiwan Building Regulation Review Tool

Revit 2024 外掛，在模型中檢討台灣《建築技術規則》並產出檢討視圖與圖說。人從「檢討面板」操作，AI 代理經本專案的 MCP 伺服器操作，兩者走同一段程式。

## 目前工具

- **第 164 條道路陰影**：3.6:1 投影、陰影面積判定、檢討圖與圖紙。
- **建築防火檢討**：防火參數建立與批次設定、防火區劃設定與編輯器、區劃面積／構件防火時效／防火門窗／帷幕牆交接／垂直區劃檢討。
- **MCP 伺服器**：讓 Claude Code 等代理操作防火檢討並驗證（Ribbon「AI 代理 › MCP 服務」）。

完整清單與狀態見 [SPEC.md](SPEC.md)。

## 建置與安裝（Revit 2024）

先關閉 Revit，再於 PowerShell 執行：

```powershell
.\scripts\install-revit-2024.ps1
```

重新開啟 Revit，在「建築法規檢討」頁籤按「開啟檢討面板」。

## 文件

| 文件 | 內容 |
| --- | --- |
| [AGENT.md](AGENT.md) | 開發原則與代理工作規範（[CLAUDE.md](CLAUDE.md) 指向它） |
| [SPEC.md](SPEC.md) | 整體規格、工具清單 |
| [ARCHITECTURE.md](ARCHITECTURE.md) | 整體架構與目錄結構 |
| [DESIGN.md](DESIGN.md) | 檢討面板設計原則、共用元件目錄 |
| [docs/README.md](docs/README.md) | 全部文件索引；各工具文件在 `docs/tools/<工具 ID>/` |
