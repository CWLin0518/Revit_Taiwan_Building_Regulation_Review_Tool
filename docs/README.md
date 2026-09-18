# 專案文件

本目錄集中記錄 Revit Taiwan Building Regulation Review Tool 的技術架構與各法規功能設計。

## 文件索引

- [整體專案架構](architecture.md)
- [防火檢討工具完整規格](fire-review-spec.md)
- [Phase 1 任務計畫](agent/phase-1-plan.md)
- [ADR-0001：分層與 Revit 版本適配](adr/0001-layered-architecture-and-revit-versioning.md)
- [Agent Phase 狀態](agent/phase-state.yaml)
- [Agent 交接](agent/HANDOFF.md)
- [法規功能總覽](regulations/README.md)
- [建築技術規則第 164 條：道路陰影檢討](regulations/article-164-road-shadow.md)

## 維護原則

新增或修改法規功能時，應同步更新：

1. `regulations/README.md` 的功能清單與狀態。
2. 該法規的獨立架構文件。
3. 若新增共用元件或改變呼叫流程，同步更新 `architecture.md`。
