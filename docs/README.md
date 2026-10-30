# 文件索引

## 根目錄

- [AGENT.md](../AGENT.md)：開發代理工作規範（六條開發原則）；[CLAUDE.md](../CLAUDE.md) 指向它
- [SPEC.md](../SPEC.md)：整體規格、工具清單、跨工具需求
- [ARCHITECTURE.md](../ARCHITECTURE.md)：整體架構
- [DESIGN.md](../DESIGN.md)：檢討面板設計原則、共用元件目錄

## 工具（`docs/tools/<工具 ID>/`）

- [工具總覽](tools/README.md)
- `review-panel/`：檢討面板（整合既有 Ribbon 工具）
- `mcp-server/`：MCP 伺服器（[ARCHITECTURE](tools/mcp-server/ARCHITECTURE.md)）
- `article-164-road-shadow/`：第 164 條（[SPEC](tools/article-164-road-shadow/SPEC.md)、[使用說明](tools/article-164-road-shadow/USER-GUIDE.md)、`history/`）
- `fire-review/`：建築防火檢討（[SPEC](tools/fire-review/SPEC.md)、`rules/` 各條細部設計、`history/` Phase 1～3 任務與交接紀錄）

## 其他

- [ADR](adr/)：設計決策
- [驗證清單](verification/checklist.md)、[驗證紀錄](verification/log.md)
