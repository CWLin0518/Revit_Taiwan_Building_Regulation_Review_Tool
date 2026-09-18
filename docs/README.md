# 專案文件

本目錄集中記錄 Revit Taiwan Building Regulation Review Tool 的技術架構與各法規功能設計。

## 文件索引

- [整體專案架構](architecture.md)
- [法規功能總覽](regulations/README.md)
- [建築技術規則第 164 條：道路陰影檢討](regulations/article-164-road-shadow.md)

## 維護原則

新增或修改法規功能時，應同步更新：

1. `regulations/README.md` 的功能清單與狀態。
2. 該法規的獨立架構文件。
3. 若新增共用元件或改變呼叫流程，同步更新 `architecture.md`。

