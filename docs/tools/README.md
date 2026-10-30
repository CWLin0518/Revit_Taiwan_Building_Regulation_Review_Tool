# 工具總覽

每個工具一個資料夾 `docs/tools/<工具 ID>/`，內含 `SPEC.md`、`ARCHITECTURE.md`，必要時有 `rules/`（細部法規設計）與 `history/`（開發紀錄）。
整體清單與共同需求見根目錄 [`SPEC.md`](../../SPEC.md)。

## 工具

| 工具 ID | 說明 | 文件 |
| --- | --- | --- |
| `review-panel` | 檢討面板，所有工具的人工入口 | [SPEC](review-panel/SPEC.md)／[ARCH](review-panel/ARCHITECTURE.md) |
| `mcp-server` | MCP 伺服器，所有工具的代理入口 | [SPEC](mcp-server/SPEC.md)／[ARCH](mcp-server/ARCHITECTURE.md) |
| `article-164-road-shadow` | 第 164 條道路陰影 | [SPEC](article-164-road-shadow/SPEC.md)／[ARCH](article-164-road-shadow/ARCHITECTURE.md)／[使用說明](article-164-road-shadow/USER-GUIDE.md) |
| `fire-review` | 建築防火檢討（下列各條為其子規則） | [SPEC](fire-review/SPEC.md)／[ARCH](fire-review/ARCHITECTURE.md) |

以下各表的法規項目中，第 164 條以外皆屬 `fire-review` 的子規則，文件在 `fire-review/rules/`。

## 已實作功能

| 功能 ID | 法規 | 功能 | 狀態 | 文件 |
| --- | --- | --- | --- | --- |
| `article-164-road-shadow` | 建築技術規則建築設計施工編第 164 條 | 道路陰影檢討與圖說製作 | 已實作 | [第 164 條道路陰影](article-164-road-shadow/SPEC.md) |

## 實作中功能

| 功能 ID | 法規 | 功能 | 狀態 | 文件 |
| --- | --- | --- | --- | --- |
| `curtain-wall-fire-compartment` | 建築技術規則建築設計施工編第 79 條第 3、4 項、第 79-3 條、第 79-4 條 | 防火區劃與帷幕外牆交接檢討 | 規則層已完成，幾何與 Revit 端未開始 | [防火區劃與帷幕牆交接](fire-review/rules/curtain-wall-fire-compartment.md) |
| `vertical-compartment` | 建築技術規則建築設計施工編第 79 條之 2 | 垂直區劃：昇降機道防火設備遮煙性能、管道間維修門時效與遮煙性能、第 3 項挑空免除 | 規則層、參數層、檢查層、一次檢討的接線與檢討視圖標示已完成（檢討表第五列與其下四條要求列、證據基線、未符合設備塗紅並帶出要求名）；第 3 項挑空的兩款免除已完成，免除成立判符合（決議 39），連通區劃改由面積、區劃牆、防火門窗與阻熱性規則檢討。Revit 端實機驗證未做 | [垂直區劃](fire-review/rules/vertical-compartment.md) |
| `article-79-1-area-exemption` | 建築技術規則建築設計施工編第 79 條之 1 | 無法區劃分隔部分（觀眾席、生產線、教室、體育館、零售市場、停車空間）得不受第 79 條第 1 項面積限制 | **設計已定案、程式未開始**（步驟 0／4）。免除成立以人工覆核交付，面積結果不變，放行走人工覆寫 | [第 79 條之 1 面積免除](fire-review/rules/article-79-1-area-exemption.md) |

## 共用輸入約定

| 約定 ID | 法規 | 內容 | 文件 |
| --- | --- | --- | --- |
| `zone-use-vocabulary` | 建築技術規則建築設計施工編第 79 條之 2 第 1 項 | `zone.use`（防火檢討_區劃用途）的議定用字，兩條區劃面積規則共用的豁免清單 | [`zone.use` 用字表](fire-review/rules/zone-use-vocabulary.md) |
| `boundary-edge-elements` | 建築技術規則建築設計施工編第 70 條、第 79 條、第 79 條之 3、第 79 條之 4 | 區劃邊界貼柱／貼牆時結論與邊界無關則逕行判定；外牆帷幕嵌板不屬第 79 條區劃開口 | [區劃邊緣構件與開口](fire-review/rules/boundary-edge-elements.md) |
| `building-use-groups` | 建築技術規則建築設計施工編第 3 條之 3、第 79 條之 1、第 83 條第一款、第二款；營建署106.02.23、國土管理署113.03.13、115.05.15 函 | 用途類組逐區劃填寫（`zone.useGroup`，混合時各類組取嚴）、正規形與寫法折疊、Ｈ－２組但書的比對 | [用途類組：逐區劃填寫與寫法正規化](fire-review/rules/building-use-groups.md) |

## 新增工具的文件

依 [`AGENT.md`](../../AGENT.md) 原則 3 與 §5：建 `docs/tools/<工具 ID>/SPEC.md`（目的、操作流程、輸入、計算與判定、產出、失敗條件、驗收）與 `ARCHITECTURE.md`（類別、資料流、Transaction、持久化、MCP 工具），並在本表與根 `SPEC.md` 登記。
