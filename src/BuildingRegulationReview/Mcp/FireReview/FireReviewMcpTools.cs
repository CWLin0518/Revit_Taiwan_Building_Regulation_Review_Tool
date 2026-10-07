using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using BuildingRegulationReview.Application.Checks;
using BuildingRegulationReview.Application.Parameters;
using BuildingRegulationReview.Application.Reviews;
using BuildingRegulationReview.Domain.Reviews;
using BuildingRegulationReview.FireReview;
using BuildingRegulationReview.Mcp.Json;
using BuildingRegulationReview.Mcp.Tools;
using BuildingRegulationReview.Revit.Parameters;

namespace BuildingRegulationReview.Mcp.FireReview
{
    /// <summary>
    /// 防火區劃檢討 and 防火參數批次設定 as MCP tools. Each one calls what the window's button calls
    /// (<see cref="FireReviewModel"/>, <see cref="FireReviewParameterDraft"/>,
    /// <see cref="RevitFireReviewParameterWriter"/>); nothing here decides anything about the review.
    /// </summary>
    internal sealed class FireReviewMcpTools : IMcpToolModule
    {
        private readonly RevitMcpDispatcher _dispatcher;

        public FireReviewMcpTools(RevitMcpDispatcher dispatcher) =>
            _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));

        public IEnumerable<IMcpTool> Tools => new IMcpTool[]
        {
            new ListPackagesTool(_dispatcher),
            new ScanParametersTool(_dispatcher),
            new SetParametersTool(_dispatcher),
            new CheckTool(_dispatcher),
            new RunTool(_dispatcher),
            new GetResultsTool(_dispatcher),
            new DescribeResultTool(_dispatcher)
        };

        // ---- shared argument handling -------------------------------------------------------

        private const string PackageIdDescription =
            "檢討套件 ID（fire_review_list_packages 的 packageId）。專案只有一個套件時可省略。";

        private static JsonObject PackageIdSchema() => JsonSchema.String(PackageIdDescription);

        /// <summary>The package the call is about: the one named, or the only one there is.</summary>
        private static FireReviewPackageResolution ResolvePackage(Document document, McpToolArguments arguments)
        {
            var packages = FireReviewModel.AvailablePackages(document);
            var requested = arguments.OptionalGuid("packageId");

            if (requested.HasValue)
            {
                var package = packages.Package(requested.Value);
                if (package != null) return new FireReviewPackageResolution(package.PackageId, packages);
                throw new McpToolException(
                    "找不到這個檢討套件，或它的 Area Plan 已被刪除。" + (packages.HiddenNotice == null ? string.Empty : " " + packages.HiddenNotice),
                    new JsonObject { ["packages"] = PackageList(document, packages) });
            }

            if (packages.Choices.Count == 1) return new FireReviewPackageResolution(packages.Choices[0].PackageId, packages);

            if (packages.Choices.Count == 0)
            {
                throw new McpToolException(
                    "這個專案還沒有可檢討的套件，請先在 Revit 執行「防火區劃設定」與「防火區劃編輯器」。" +
                    (packages.HiddenNotice == null ? string.Empty : " " + packages.HiddenNotice));
            }

            throw new McpToolException("專案有多個檢討套件，請用 packageId 指定其中一個。",
                new JsonObject { ["packages"] = PackageList(document, packages) });
        }

        private static JsonArray PackageList(Document document, FireReviewPackageList packages)
        {
            var list = new JsonArray();
            foreach (var choice in packages.Choices)
            {
                var package = packages.Package(choice.PackageId);
                if (package != null) list.Add(FireReviewJson.Package(package, FireReviewModel.LabelOf(document, package)));
            }
            return list;
        }

        /// <summary>A scan that failed outright is a tool error; one that ran says what it found, blocking items included.</summary>
        private static FireReviewScan Scan(Document document, Guid packageId, bool acceptRuleSetUpdate)
        {
            var scan = FireReviewModel.TryScan(document, packageId, acceptRuleSetUpdate);
            if (scan.Failure != null) throw new McpToolException(scan.Failure);
            return scan;
        }

        private static JsonObject CheckFreshnessSchema(bool fallback) => JsonSchema.Boolean(
            "是否重新讀取模型判定哪些項目需更新（與視窗開啟時相同，大模型可能要數十秒）。false 時直接讀儲存的結果，" +
            "快，但不會標示需更新。", fallback);

        /// <summary>
        /// The latest run as a 檢討表: judged against the model through a full pre-scan, as the window
        /// does, or — when the caller only needs what was stored — read straight from the model's
        /// storage, which is what lets an agent walk many results without a pre-scan each time.
        /// </summary>
        private static StoredResult ReadStored(Document document, FireReviewPackageResolution resolution, bool checkFreshness)
        {
            if (checkFreshness)
            {
                var scan = Scan(document, resolution.PackageId, acceptRuleSetUpdate: false);
                return new StoredResult(FireReviewJson.Package(scan.Package, scan.PackageLabel), scan.StoredTable, freshnessChecked: true);
            }

            var package = resolution.Packages.Package(resolution.PackageId);
            ReviewRun latest;
            try
            {
                latest = FireReviewModel.LatestRun(document, resolution.PackageId);
            }
            catch (Exception exception)
            {
                throw new McpToolException("上一次的檢討紀錄無法讀取：" + exception.Message);
            }

            return new StoredResult(FireReviewJson.Package(package, FireReviewModel.LabelOf(document, package)),
                latest == null ? null : ReviewTable.Build(latest), freshnessChecked: false);
        }

        private sealed class StoredResult
        {
            public StoredResult(JsonObject package, ReviewTable table, bool freshnessChecked)
            {
                Package = package;
                Table = table;
                FreshnessChecked = freshnessChecked;
            }

            public JsonObject Package { get; }
            public ReviewTable Table { get; }
            public bool FreshnessChecked { get; }
        }

        private static JsonObject ScanSummary(FireReviewScan scan) => new JsonObject
        {
            ["package"] = FireReviewJson.Package(scan.Package, scan.PackageLabel),
            ["ruleSet"] = scan.RuleSet.IsSuccess ? FireReviewJson.RuleSet(scan.RuleSet.Value.RuleSet) : JsonValue.Null,
            ["ruleSetError"] = scan.RuleSet.IsFailure ? scan.RuleSet.Error.Message : null,
            ["prescanSeconds"] = scan.Prescan.TotalSeconds,
            ["readiness"] = FireReviewJson.Readiness(scan.Readiness)
        };

        private static readonly IReadOnlyList<string> StatusBandNames =
            ReviewStatusBands.All.Select(band => band.ToString()).ToList();

        private static JsonObject FilterSchemaProperties(JsonObject properties, int defaultLimit)
        {
            properties["statuses"] = JsonSchema.Array(
                JsonSchema.Enum("狀態類別", StatusBandNames),
                "只列出這些狀態類別的項目：Fail＝未符合、Pending＝待確認（資料不足／人工覆核／未檢討）、Pass＝符合、NotApplicable＝不適用。省略表示全部。統計數字永遠是全部項目。");
            properties["checkType"] = JsonSchema.Enum("只列出這個檢討項目。", ReviewTable.CheckTypes);
            properties["search"] = JsonSchema.String("全文搜尋：元素編號、類型名稱、區劃名稱、圖號、條文、規則編號、說明；空白分隔的詞必須全部命中。");
            properties["staleOnly"] = JsonSchema.Boolean("只列出需更新的項目。", false);
            properties["limit"] = JsonSchema.Integer("最多列出幾筆項目。", 0, 2000, defaultLimit);
            return properties;
        }

        private static ReviewTableFilter Filter(McpToolArguments arguments)
        {
            var bands = new List<ReviewStatusBand>();
            foreach (var name in arguments.OptionalStrings("statuses"))
            {
                if (!Enum.TryParse(name, ignoreCase: true, out ReviewStatusBand band) || !Enum.IsDefined(typeof(ReviewStatusBand), band))
                    throw new McpToolException($"statuses 只能是 {string.Join("、", StatusBandNames)}，收到「{name}」。");
                bands.Add(band);
            }

            var checkType = arguments.OptionalString("checkType");
            if (checkType != null && !ReviewTable.CheckTypes.Contains(checkType, StringComparer.Ordinal))
                throw new McpToolException($"checkType 只能是 {string.Join("、", ReviewTable.CheckTypes)}。");

            return new ReviewTableFilter(bands, checkType, arguments.OptionalString("search"), arguments.OptionalBool("staleOnly", false));
        }

        /// <summary>
        /// 一次檢討的日誌可以有上百筆、每筆好幾百字，整包回傳會超過代理一次能讀的上限，連統計都看不到。
        /// 所以回傳的筆數由呼叫端決定，預設只給最重要的前
        /// <see cref="Application.Diagnostics.ReviewLogDigest.DefaultLimit"/> 筆（B-04）。
        /// </summary>
        private const string LogLimitDescription =
            "日誌最多回傳幾筆（依嚴重度遞減取前幾筆）。0＝只回統計不回 entries。" +
            "log.total、log.errors、log.warnings 永遠是全部的實情，被截斷時 log.truncated 為 true；" +
            "要看全部就把這個值開大。";

        private static JsonObject LogLimitSchema() => JsonSchema.Integer(
            LogLimitDescription, 0, 2000, Application.Diagnostics.ReviewLogDigest.DefaultLimit);

        private static int LogLimit(McpToolArguments arguments) =>
            arguments.OptionalInt("logLimit", Application.Diagnostics.ReviewLogDigest.DefaultLimit, 0, 2000);

        private static JsonObject AddLog(JsonObject result, int logLimit, params Application.Diagnostics.ReviewLog[] logs)
        {
            result["log"] = FireReviewJson.Log(logs.Where(log => log != null).SelectMany(log => log.Entries), logLimit);
            return result;
        }

        // ---- tools --------------------------------------------------------------------------

        private sealed class ListPackagesTool : RevitMcpTool
        {
            public ListPackagesTool(RevitMcpDispatcher dispatcher) : base(dispatcher) { }

            public override string Name => "fire_review_list_packages";
            public override string Title => "列出防火區劃檢討套件";

            public override string Description =>
                "列出目前專案中可以執行防火區劃檢討的套件（每個套件是一個樓層的 Area Plan）。" +
                "其他 fire_review_* 工具用回傳的 packageId 指定套件。不會修改模型。";

            public override JsonObject InputSchema => JsonSchema.Empty();
            public override McpToolAnnotations Annotations => McpToolAnnotations.ReadOnly;

            protected override McpToolResult Execute(UIApplication application, McpToolArguments arguments, CancellationToken cancellation)
            {
                var document = RequireProject(application);
                var packages = FireReviewModel.AvailablePackages(document);
                var list = PackageList(document, packages);
                return McpToolResult.Success(new JsonObject
                {
                    ["packages"] = list,
                    ["awaitingAreaPlan"] = packages.Selection.AwaitingAreaPlan.Count,
                    ["hiddenNotice"] = packages.HiddenNotice
                }, $"共 {list.Count} 個可檢討的套件。");
            }
        }

        private sealed class CheckTool : RevitMcpTool
        {
            public CheckTool(RevitMcpDispatcher dispatcher) : base(dispatcher) { }

            public override string Name => "fire_review_check";
            public override string Title => "防火區劃檢討前置檢查";

            public override string Description =>
                "執行「防火區劃檢討」視窗開啟時的前置掃描與前置檢查：讀取套件、規則集、候選元素與參數，回傳每一項" +
                "必須修正（Blocking）／建議確認（Warning）的項目與修正方式，以及 canRun。也會判定上一次檢討結果是否需更新。" +
                "會把『需更新』等狀態寫回套件（與視窗相同），但不會產生新的檢討結果。";

            public override JsonObject InputSchema => JsonSchema.Object(new JsonObject
            {
                ["packageId"] = PackageIdSchema(),
                ["acceptRuleSetUpdate"] = JsonSchema.Boolean(
                    "套件鎖定的規則集版本與外掛目前的不同時，是否改用目前版本（視窗的「改用目前規則版本」勾選框）。舊結果與人工覆寫會標示為需更新。", false),
                ["logLimit"] = LogLimitSchema()
            });

            public override McpToolAnnotations Annotations => McpToolAnnotations.Updates;
            protected override TimeSpan RunTimeout => TimeSpan.FromMinutes(10);

            protected override McpToolResult Execute(UIApplication application, McpToolArguments arguments, CancellationToken cancellation)
            {
                var document = RequireProject(application);
                var package = ResolvePackage(document, arguments);
                var scan = Scan(document, package.PackageId, arguments.OptionalBool("acceptRuleSetUpdate", false));

                var result = ScanSummary(scan);
                result["storedResult"] = scan.StoredTable == null ? JsonValue.Null : new JsonObject
                {
                    ["runId"] = JsonValue.Of(scan.StoredTable.RunId),
                    ["verdict"] = JsonValue.Of(scan.StoredTable.Verdict),
                    ["verdictText"] = ReviewVerdictText.Label(scan.StoredTable.Verdict),
                    ["counts"] = FireReviewJson.Counts(scan.StoredTable.Counts),
                    ["isStale"] = scan.StoredTable.IsStale,
                    ["staleReasons"] = JsonValue.Array(scan.StoredTable.StaleReasons)
                };
                return McpToolResult.Success(AddLog(result, LogLimit(arguments), scan.Log), scan.Readiness.Message);
            }
        }

        private sealed class RunTool : RevitMcpTool
        {
            public RunTool(RevitMcpDispatcher dispatcher) : base(dispatcher) { }

            public override string Name => "fire_review_run";
            public override string Title => "執行防火區劃檢討";

            public override string Description =>
                "與視窗的「開始檢討」相同：前置掃描 → 前置檢查 → 檢討防火區劃面積、區劃面積免除、構件防火時效、防火門窗、" +
                "帷幕牆區劃交接與垂直區劃 → 儲存結果並標示檢討視圖。前置檢查未通過時不執行，回傳必須修正的項目。" +
                "dryRun=true 時照常計算並回傳結果，但結束後整批復原，模型（含檢討紀錄與標示）完全不變——驗證時建議先用；" +
                "此時 saved.saved 為 false、saved.rolledBack 為 true，saved.mark.summary 仍是實際算出的標示結果。" +
                "回傳總狀態、各檢討項目統計與依篩選列出的項目；完整表格可再用 fire_review_get_results。";

            public override JsonObject InputSchema => JsonSchema.Object(FilterSchemaProperties(new JsonObject
            {
                ["packageId"] = PackageIdSchema(),
                ["acceptRuleSetUpdate"] = JsonSchema.Boolean("規則集版本不同時是否改用目前版本（同 fire_review_check）。", false),
                ["dryRun"] = JsonSchema.Boolean("只計算不留下任何變更。", false),
                ["logLimit"] = LogLimitSchema()
            }, defaultLimit: RunEntryLimit));

            /// <summary>
            /// 這個工具的回傳本來就帶著規則集、前置檢查、統計與日誌，項目再預設給五十筆就讀不完了。
            /// 看完整表格有 fire_review_get_results（預設 100 筆）；這裡預設少一點，要多再自己開大。
            /// matchedCount 與 truncated 照舊說出全部有幾筆（B-04）。
            /// </summary>
            private const int RunEntryLimit = 20;

            public override McpToolAnnotations Annotations => new McpToolAnnotations(readOnly: false, destructive: false, idempotent: false);

            protected override TimeSpan RunTimeout => TimeSpan.FromMinutes(10);

            protected override McpToolResult Execute(UIApplication application, McpToolArguments arguments, CancellationToken cancellation)
            {
                var document = RequireProject(application);
                var package = ResolvePackage(document, arguments);
                var accept = arguments.OptionalBool("acceptRuleSetUpdate", false);
                var dryRun = arguments.OptionalBool("dryRun", false);
                var filter = Filter(arguments);
                var limit = arguments.OptionalInt("limit", RunEntryLimit, 0, 2000);
                var logLimit = LogLimit(arguments);

                return WithDryRun(document, dryRun, "防火區劃檢討", () =>
                {
                    var scan = Scan(document, package.PackageId, accept);
                    var result = ScanSummary(scan);
                    result["dryRun"] = dryRun;

                    if (!FireReviewModel.CanRun(scan))
                    {
                        AddLog(result, logLimit, scan.Log);
                        return McpToolResult.Failure(scan.Readiness.Message, result);
                    }

                    var run = FireReviewModel.RunAndSave(document, scan, cancellation);
                    if (run.Outcome == null)
                    {
                        AddLog(result, logLimit, scan.Log);
                        return McpToolResult.Failure(run.Failure, result);
                    }

                    result["outcome"] = FireReviewJson.Outcome(run.Outcome);
                    // dryRun 時 saved.saved 回 false：整批在這個呼叫回傳之前就被復原了（B-05）。下面的
                    // 失敗判斷讀的是領域物件 run.Saved.Saved，不是這個 JSON，所以試跑仍然走 Success。
                    result["saved"] = FireReviewJson.Saved(run.Saved, rolledBack: dryRun);
                    if (run.Outcome.Table != null)
                        result["result"] = FireReviewJson.Table(run.Outcome.Table, filter, limit, includeGroups: false);
                    AddLog(result, logLimit, scan.Log, run.Outcome.Log, run.Saved?.Log);

                    if (!run.Outcome.IsCompleted) return McpToolResult.Failure(run.Outcome.Message, result);
                    if (run.Saved != null && !run.Saved.Saved)
                        return McpToolResult.Failure("檢討已完成，但結果沒有寫入模型（已整批復原）：" + run.Saved.Error, result);

                    return McpToolResult.Success(result, (dryRun ? "〔試跑，已復原〕" : string.Empty) + run.Outcome.Message);
                });
            }
        }

        private sealed class GetResultsTool : RevitMcpTool
        {
            public GetResultsTool(RevitMcpDispatcher dispatcher) : base(dispatcher) { }

            public override string Name => "fire_review_get_results";
            public override string Title => "讀取防火區劃檢討結果";

            public override string Description =>
                "讀取套件最近一次的檢討結果（檢討表），並依目前模型判定哪些項目需更新，與視窗開啟時看到的相同。" +
                "可依狀態、檢討項目、全文搜尋篩選；統計永遠是全部項目。單一項目的完整明細用 fire_review_describe_result。";

            public override JsonObject InputSchema => JsonSchema.Object(FilterSchemaProperties(new JsonObject
            {
                ["packageId"] = PackageIdSchema(),
                ["includeGroups"] = JsonSchema.Boolean("是否附上各檢討項目依區劃／類別／類型的分組統計。", true),
                ["checkFreshness"] = CheckFreshnessSchema(fallback: true)
            }, defaultLimit: 100));

            public override McpToolAnnotations Annotations => McpToolAnnotations.Updates;
            protected override TimeSpan RunTimeout => TimeSpan.FromMinutes(10);

            protected override McpToolResult Execute(UIApplication application, McpToolArguments arguments, CancellationToken cancellation)
            {
                var document = RequireProject(application);
                var package = ResolvePackage(document, arguments);
                var filter = Filter(arguments);
                var limit = arguments.OptionalInt("limit", 100, 0, 2000);
                var stored = ReadStored(document, package, arguments.OptionalBool("checkFreshness", true));

                var result = new JsonObject { ["package"] = stored.Package, ["freshnessChecked"] = stored.FreshnessChecked };
                if (stored.Table == null)
                {
                    result["result"] = JsonValue.Null;
                    return McpToolResult.Success(result, "這個套件尚未檢討。");
                }

                result["result"] = FireReviewJson.Table(stored.Table, filter, limit, arguments.OptionalBool("includeGroups", true));
                return McpToolResult.Success(result,
                    $"總狀態：{ReviewVerdictText.Label(stored.Table.Verdict)}（{stored.Table.Counts.Text}）。");
            }
        }

        private sealed class DescribeResultTool : RevitMcpTool
        {
            public DescribeResultTool(RevitMcpDispatcher dispatcher) : base(dispatcher) { }

            public override string Name => "fire_review_describe_result";
            public override string Title => "檢討項目明細";

            public override string Description =>
                "回傳檢討表中一個項目的完整明細（視窗右側明細面板的內容）：檢討對象、判定與理由、實際值與要求值、條文、規則與證據。";

            public override JsonObject InputSchema => JsonSchema.Object(new JsonObject
            {
                ["packageId"] = PackageIdSchema(),
                ["resultId"] = JsonSchema.String("項目的 resultId（fire_review_run／fire_review_get_results 回傳的 entries[].resultId）。"),
                ["checkFreshness"] = CheckFreshnessSchema(fallback: false)
            }, "resultId");

            public override McpToolAnnotations Annotations => McpToolAnnotations.Updates;
            protected override TimeSpan RunTimeout => TimeSpan.FromMinutes(10);

            protected override McpToolResult Execute(UIApplication application, McpToolArguments arguments, CancellationToken cancellation)
            {
                var document = RequireProject(application);
                var package = ResolvePackage(document, arguments);
                var resultId = arguments.RequireGuid("resultId");
                var stored = ReadStored(document, package, arguments.OptionalBool("checkFreshness", false));

                var table = stored.Table ?? throw new McpToolException("這個套件尚未檢討。");
                var entry = table.Entry(resultId) ?? throw new McpToolException("最近一次的檢討結果中沒有這個 resultId；結果可能已被新的檢討取代。");
                var mark = CurtainWallMarkNumbers.Of(CurtainWallMarkNumbers.Assign(table), entry.ResultId);
                var sections = ReviewEntryReport.Describe(table, entry, mark);

                return McpToolResult.Success(new JsonObject
                {
                    ["entry"] = FireReviewJson.Entry(entry, mark),
                    ["sections"] = FireReviewJson.Detail(sections),
                    ["text"] = ReviewEntryReport.ToText(sections)
                }, ReviewEntryReport.Headline(entry, mark));
            }
        }

        private sealed class ScanParametersTool : RevitMcpTool
        {
            public ScanParametersTool(RevitMcpDispatcher dispatcher) : base(dispatcher) { }

            public override string Name => "fire_review_scan_parameters";
            public override string Title => "讀取防火檢討參數";

            public override string Description =>
                "讀取「防火參數批次設定」面板的內容：牆／柱／樑／樓板／門窗／帷幕嵌板的每個類型（結構材料、被覆厚度、設計防火時效、" +
                "防火門窗、遮煙性能、阻熱性、嵌板種類）、每個區劃（用途、滅火設備、樓層序…）與專案資訊。" +
                "asks 表示這一列需要填哪些欄位，derived 是依第71～73條由材料與尺寸推定的時效。不會修改模型。";

            public override JsonObject InputSchema => JsonSchema.Object(new JsonObject
            {
                ["viewId"] = JsonSchema.Integer("用來排序類型與計算「視圖中的數量」的視圖 ElementId；省略時用作用中視圖。類型清單本身一律涵蓋整個專案。"),
                ["onlyNeedingAttention"] = JsonSchema.Boolean("只列出缺參數、待填材料／被覆／嵌板種類、推定時效與填寫值不同的類型，以及缺必要值的區劃。", false),
                ["limit"] = JsonSchema.Integer("類型與區劃各最多列出幾筆。", 0, 5000, 500)
            });

            public override McpToolAnnotations Annotations => McpToolAnnotations.ReadOnly;

            protected override McpToolResult Execute(UIApplication application, McpToolArguments arguments, CancellationToken cancellation)
            {
                var document = RequireProject(application);
                var view = ResolveView(application, document, arguments.OptionalNullableInt("viewId", 0));
                var draft = new FireReviewParameterDraft(new RevitFireReviewTypeScanner(document).ScanAll(view));
                var limit = arguments.OptionalInt("limit", 500, 0, 5000);
                var attention = arguments.OptionalBool("onlyNeedingAttention", false);

                var result = FireReviewJson.ParameterSet(draft, view == null ? "專案" : "視圖「" + view.Name + "」");
                var types = draft.Rows.Where(r => !attention || NeedsAttention(r)).ToList();
                var zones = draft.Zones.Where(z => !attention || z.HasMissingParameters || string.IsNullOrEmpty(z.Sprinklered) || string.IsNullOrEmpty(z.FloorNumber)).ToList();
                result["types"] = new JsonArray(types.Take(limit).Select(r => (JsonValue)FireReviewJson.TypeRow(r)));
                result["zones"] = new JsonArray(zones.Take(limit).Select(z => (JsonValue)FireReviewJson.ZoneRow(z)));
                result["typeCount"] = types.Count;
                result["zoneCount"] = zones.Count;
                result["truncated"] = types.Count > limit || zones.Count > limit;

                return McpToolResult.Success(result,
                    $"類型 {draft.Rows.Count} 個、區劃 {draft.Zones.Count} 個；可推定時效 {draft.Rows.Count(r => r.CanApplyDerived)} 列。");
            }

            /// <remarks>
            /// 待填材料／被覆走 <c>AwaitsMaterial</c>／<c>AwaitsCover</c>（<c>FireRatingDerivationGaps</c>），
            /// 與面板狀態列和 <c>FireReviewTypeTable.AwaitingMaterial</c> 同一個判斷。只比
            /// <c>Derivation.Kind</c> 會把已宣告玻璃的帷幕嵌板列為待處理，而它那一格是停用的（決議 16、B-07）。
            /// </remarks>
            private static bool NeedsAttention(FireReviewTypeRowViewModel row) =>
                row.MissingParameters.Length > 0 || row.AwaitsPanelKind || row.RatingDiffers ||
                row.AwaitsMaterial || row.AwaitsCover;
        }

        private sealed class SetParametersTool : RevitMcpTool
        {
            public SetParametersTool(RevitMcpDispatcher dispatcher) : base(dispatcher) { }

            public override string Name => "fire_review_set_parameters";
            public override string Title => "批次寫入防火檢討參數";

            public override string Description =>
                "與「防火參數批次設定」面板相同：把值填進面板的列，只寫入與模型不同的值，整批一個交易（任何錯誤整批復原）。" +
                "類型參數會套用到專案中該類型的所有實體。值的寫法與面板相同：是否類欄位用 true／false（null＝清除），" +
                "被覆厚度單位為公分，防火時效如「1h」「60min」。面板不會問的欄位（見 fire_review_scan_parameters 的 asks）" +
                "會被略過並列在 ignored。不會寫入「防火檢討_法規要求防火時效」。dryRun=true 時只回報會寫入什麼，模型不變。";

            public override JsonObject InputSchema => JsonSchema.Object(new JsonObject
            {
                ["types"] = JsonSchema.Array(JsonSchema.Object(new JsonObject
                {
                    ["typeUniqueId"] = JsonSchema.String("類型的 UniqueId（scan 的 typeUniqueId）；或改用 typeElementId。"),
                    ["typeElementId"] = JsonSchema.Integer("類型的 ElementId。"),
                    ["panelKind"] = JsonSchema.NullableString("帷幕嵌板種類：實心 或 玻璃；null 清除。"),
                    ["material"] = JsonSchema.NullableString("結構材料：RC、SRC 或 SC；null 清除。"),
                    ["coverCm"] = JsonSchema.NullableNumber("SC 的防火被覆厚度（公分）；null 清除。", 0),
                    ["rating"] = JsonSchema.NullableString("防火檢討_設計防火時效，例如 1h、2h、60min；null 清除。"),
                    ["applyDerivedRating"] = JsonSchema.Boolean("改用依第71～73條推定的時效（面板的「套用推定值」）。不可與 rating 同時使用。"),
                    ["fireProtection"] = JsonSchema.Boolean("防火檢討_設計防火保護（防火門窗）。"),
                    ["smokeProtection"] = JsonSchema.Boolean("防火檢討_遮煙性能。"),
                    ["insulation"] = JsonSchema.Boolean("防火檢討_阻熱性。")
                }), "要修改的構件類型。"),
                ["zones"] = JsonSchema.Array(JsonSchema.Object(new JsonObject
                {
                    ["elementUniqueId"] = JsonSchema.String("區劃（Area）的 UniqueId；或改用 elementId。"),
                    ["elementId"] = JsonSchema.Integer("區劃（Area）的 ElementId。"),
                    ["use"] = JsonSchema.NullableString("防火檢討_區劃用途；一般區劃留空（null）。必須與清單逐字相符才有作用，例如 挑空、樓梯間、昇降機道、管道間、觀眾席。"),
                    ["sprinklered"] = JsonSchema.NullableBoolean("防火檢討_自動滅火設備。"),
                    ["floorNumber"] = JsonSchema.NullableInteger("防火檢討_所在樓層序。"),
                    ["linksRefugeFloor"] = JsonSchema.NullableBoolean("防火檢討_避難層通達（只有挑空需要）。"),
                    ["cannotBeSubdivided"] = JsonSchema.NullableBoolean("防火檢討_無法區劃分隔（只有第79條之1 的六種用途需要）。")
                }), "要修改的區劃。"),
                ["project"] = JsonSchema.Object(new JsonObject
                {
                    ["fireResistiveConstruction"] = JsonSchema.NullableBoolean("防火檢討_防火構造建築物。未設為 true 時整份檢討都會是資料不足。"),
                    ["buildingUse"] = JsonSchema.NullableString("建築物用途類組，例如 H-2。"),
                    ["floorsAboveGround"] = JsonSchema.NullableInteger("地上層數。")
                }),
                ["deriveFloors"] = JsonSchema.Boolean("先依樓層高程填入每個區劃的樓層序與地上層數（面板的「依樓層推定」），再套用其他值。", false),
                ["acceptProposedPanelKinds"] = JsonSchema.Boolean(
                    "一併寫入工具由嵌板材料提案、模型尚未宣告的嵌板種類（面板按「寫入模型」時會一起寫入的那些）。預設不寫，只列在 unrequested。", false),
                ["dryRun"] = JsonSchema.Boolean("只回報會寫入的變更，不修改模型。", false)
            });

            public override McpToolAnnotations Annotations => McpToolAnnotations.Overwrites;

            protected override McpToolResult Execute(UIApplication application, McpToolArguments arguments, CancellationToken cancellation)
            {
                var document = RequireProject(application);
                var dryRun = arguments.OptionalBool("dryRun", false);
                var draft = new FireReviewParameterDraft(new RevitFireReviewTypeScanner(document).ScanAll(null));
                var requested = new List<RequestedValue>();
                var ignored = new JsonArray();

                if (arguments.OptionalBool("deriveFloors", false))
                {
                    var derived = draft.DeriveFloors();
                    if (derived == null) ignored.Add(new JsonObject { ["field"] = "deriveFloors", ["reason"] = "模型中沒有樓層可以判斷樓層序。" });
                    else
                    {
                        if (derived.UnknownZones.Count > 0)
                            ignored.Add(new JsonObject { ["field"] = "deriveFloors", ["reason"] = "這些區劃讀不到所屬樓層", ["zones"] = JsonValue.Array(derived.UnknownZones) });
                        foreach (var zone in draft.Zones.Where(z => z.DerivedFloorNumber.HasValue))
                            requested.Add(new RequestedValue("deriveFloors", zone.Source.ElementUniqueId, zone.DisplayName, ReviewInputSources.FloorNumber, null));
                        if (draft.Project != null)
                            requested.Add(new RequestedValue("deriveFloors", draft.Project.Source.ElementUniqueId, "專案資訊", ReviewInputSources.FloorsAboveGround, null));
                    }
                }

                foreach (var item in arguments.OptionalArray("types") ?? new JsonArray())
                    ApplyType(document, draft, McpToolArguments.Of(item, "types"), requested);
                foreach (var item in arguments.OptionalArray("zones") ?? new JsonArray())
                    ApplyZone(document, draft, McpToolArguments.Of(item, "zones"), requested);
                if (arguments.Raw("project") is JsonObject project)
                    ApplyProject(draft, new McpToolArguments(project), requested);

                // The panel's rows can hold values nobody asked for — chiefly the 嵌板種類 the reader
                // proposes from a panel's material, which the panel writes along with everything else
                // once its confirmation is accepted (決議 16、D3). An agent has no such confirmation, so
                // only what it named is written, and the rest is listed for it to decide on.
                var acceptProposals = arguments.OptionalBool("acceptProposedPanelKinds", false);
                var batch = draft.CollectEdits();
                var edits = new List<FireReviewParameterEdit>();
                var unrequested = new JsonArray();
                foreach (var edit in batch.All)
                {
                    var asked = requested.Any(v => v.Reason == null && v.UniqueId == edit.ElementUniqueId && v.Parameter == edit.ParameterName);
                    var proposal = acceptProposals && edit.ParameterName == CurtainPanelKindParameters.Provided;
                    if (asked || proposal) edits.Add(edit);
                    else unrequested.Add(FireReviewJson.Edit(edit));
                }

                foreach (var value in requested.Where(v => v.Field != "deriveFloors" && !edits.Any(e => e.ElementUniqueId == v.UniqueId && e.ParameterName == v.Parameter)))
                {
                    ignored.Add(new JsonObject
                    {
                        ["field"] = value.Field,
                        ["elementUniqueId"] = value.UniqueId,
                        ["name"] = value.Name,
                        ["reason"] = value.Reason ?? "值與模型相同，或面板不會在這一列問這個欄位（見 fire_review_scan_parameters 的 asks）。"
                    });
                }

                var touchedTypes = draft.Rows
                    .Where(r => edits.Any(e => e.ElementUniqueId == r.Source.TypeUniqueId))
                    .ToList();
                var result = new JsonObject
                {
                    ["dryRun"] = dryRun,
                    ["edits"] = new JsonArray(edits.Select(e => (JsonValue)FireReviewJson.Edit(e))),
                    ["typeEdits"] = edits.Count(batch.TypeEdits.Contains),
                    ["zoneEdits"] = edits.Count(batch.ZoneEdits.Contains),
                    ["projectEdits"] = edits.Count(batch.ProjectEdits.Contains),
                    ["affectedTypes"] = touchedTypes.Count,
                    ["affectedInstances"] = touchedTypes.Sum(r => r.Source.ProjectInstanceCount),
                    ["ignored"] = ignored,
                    ["unrequested"] = unrequested
                };

                if (edits.Count == 0) return McpToolResult.Success(result, "沒有任何變更需要寫入。");

                var write = WithDryRun(document, dryRun, "批次設定防火檢討參數",
                    () => new RevitFireReviewParameterWriter(document).Apply(edits));

                result["written"] = write.Written;
                result["unchanged"] = write.Unchanged;
                result["committed"] = write.Committed && !dryRun;
                result["failures"] = new JsonArray(write.Failures.Select(f => (JsonValue)new JsonObject
                {
                    ["elementUniqueId"] = f.Edit.ElementUniqueId,
                    ["name"] = f.TypeName,
                    ["parameter"] = f.Edit.ParameterName,
                    ["reason"] = f.Reason
                }));
                if (!write.Committed) return McpToolResult.Failure("寫入失敗，已整批復原：" + write.Error, result);

                // The writer commits what it could and skips what it could not (a parameter not bound to
                // that category, say). Half a request is not a success the agent should read past.
                var prefix = dryRun ? "〔試跑，已復原〕" : string.Empty;
                if (write.Failures.Count > 0)
                    return McpToolResult.Failure($"{prefix}有 {write.Failures.Count} 個值沒有寫入（其餘 {write.Written} 個已寫入）：見 failures。", result);
                return McpToolResult.Success(result, prefix + write.Summary);
            }

            private static void ApplyType(Document document, FireReviewParameterDraft draft, McpToolArguments item,
                List<RequestedValue> requested)
            {
                var uniqueId = UniqueIdOf(document, item, "typeUniqueId", "typeElementId", "types");
                var row = draft.Rows.FirstOrDefault(r => r.Source.TypeUniqueId == uniqueId)
                          ?? throw new McpToolException($"types 中的 {uniqueId} 不是面板會列出的類型（牆、柱、樑、樓板、門、窗、帷幕嵌板）。");

                if (item.Has("rating") && item.OptionalBool("applyDerivedRating", false))
                    throw new McpToolException($"{row.DisplayName}：rating 與 applyDerivedRating 不可同時使用。");

                // Each field is set only when the panel would let a user type into that cell — the same
                // predicates the XAML binds IsEnabled to, read after the fields before it were set,
                // because the 嵌板種類 decides which of the rest are asked and the material decides
                // whether a cover is. A field the panel would not offer is reported, not written.
                bool Offer(string field, string parameter, bool editable, string notAsked)
                {
                    var reason = row.IsSubstituted
                        ? "這一列是 Revit 的保留嵌板型別，參數唯讀；請改它的來源牆型別「" + row.SubstitutionSource + "」。"
                        : editable ? null : notAsked;
                    requested.Add(new RequestedValue(field, uniqueId, row.DisplayName, parameter, reason));
                    return reason == null;
                }

                if (item.Has("panelKind") &&
                    Offer("panelKind", CurtainPanelKindParameters.Provided, row.CanEditPanelKind, "只有帷幕嵌板要填嵌板種類。"))
                {
                    row.PanelKind = PanelKindText(item.OptionalString("panelKind"), row.DisplayName);
                }
                if (item.Has("material") &&
                    Offer("material", StructuralMaterialParameters.Material, row.CanEditMaterial, "這一列不填結構材料（門窗，或尚未宣告為實心的帷幕嵌板）。"))
                {
                    row.Material = MaterialText(item.OptionalString("material"), row.DisplayName);
                }
                if (item.Has("coverCm") &&
                    Offer("coverCm", StructuralMaterialParameters.Cover, row.CanEditMaterial && row.NeedsCover, "只有結構材料為 SC 的列要填防火被覆厚度。"))
                {
                    var cover = item.OptionalNumber("coverCm", 0);
                    row.CoverCm = cover.HasValue ? cover.Value.ToString(CultureInfo.InvariantCulture) : string.Empty;
                }
                if (item.OptionalBool("applyDerivedRating", false) &&
                    Offer("applyDerivedRating", FireRatingParameters.Provided, row.CanEditRating && row.CanApplyDerived,
                        row.CanEditRating ? "無法推定時效：" + row.DerivedBasis : "這一列不填設計防火時效。"))
                {
                    row.ApplyDerived();
                }
                if (item.Has("rating") &&
                    Offer("rating", FireRatingParameters.Provided, row.CanEditRating, "這一列不填設計防火時效（窗、玻璃嵌板以防火門窗作答）。"))
                {
                    row.Rating = item.OptionalString("rating") ?? string.Empty;
                }

                var opening = row.CarriesProtection && row.IsEditable;
                if (item.Has("fireProtection") &&
                    Offer("fireProtection", FireProtectionParameters.Provided, opening, "這一列不問防火門窗。"))
                {
                    row.Protection = RequireBool(item, "fireProtection", row.DisplayName);
                }
                if (item.Has("insulation") &&
                    Offer("insulation", InsulationParameters.Provided, opening, "這一列不問阻熱性。"))
                {
                    row.Insulation = RequireBool(item, "insulation", row.DisplayName);
                }
                if (item.Has("smokeProtection") &&
                    Offer("smokeProtection", SmokeProtectionParameters.Provided, row.CarriesSmokeProtection && row.IsEditable, "只有門窗要填遮煙性能。"))
                {
                    row.SmokeProtection = RequireBool(item, "smokeProtection", row.DisplayName);
                }
            }

            /// <summary>實心／玻璃 as the panel's dropdown offers them, or blank; anything else is refused rather than written.</summary>
            private static string PanelKindText(string text, string row)
            {
                if (string.IsNullOrWhiteSpace(text)) return string.Empty;
                var kind = CurtainPanelKinds.Parse(text);
                if (kind == null || !CurtainPanelKinds.Declarable.Contains(kind.Value))
                    throw new McpToolException($"{row}：panelKind 只能是 {string.Join("、", CurtainPanelKinds.Declarable.Select(CurtainPanelKinds.ParameterText))} 或 null，收到「{text}」。");
                return CurtainPanelKinds.ParameterText(kind.Value);
            }

            /// <summary>RC／SRC／SC as the panel's dropdown offers them, or blank; anything else is refused rather than written.</summary>
            private static string MaterialText(string text, string row)
            {
                if (string.IsNullOrWhiteSpace(text)) return string.Empty;
                var material = StructuralMaterialText.Parse(text);
                if (material == null)
                    throw new McpToolException($"{row}：material 只能是 {string.Join("、", StructuralMaterialText.All.Select(StructuralMaterialText.Code))} 或 null，收到「{text}」。");
                return StructuralMaterialText.Code(material.Value);
            }

            private static void ApplyZone(Document document, FireReviewParameterDraft draft, McpToolArguments item, List<RequestedValue> requested)
            {
                var uniqueId = UniqueIdOf(document, item, "elementUniqueId", "elementId", "zones");
                var zone = draft.Zones.FirstOrDefault(z => z.Source.ElementUniqueId == uniqueId)
                           ?? throw new McpToolException($"zones 中的 {uniqueId} 不是防火檢討會讀的區劃（Area）。");

                void Ask(string field, string parameter) =>
                    requested.Add(new RequestedValue(field, uniqueId, zone.DisplayName, parameter, null));

                if (item.Has("use"))
                {
                    zone.Use = item.OptionalString("use") ?? string.Empty;
                    Ask("use", ReviewInputSources.ZoneUse);
                }
                if (item.Has("sprinklered"))
                {
                    zone.Sprinklered = YesNo(item.OptionalNullableBool("sprinklered"));
                    Ask("sprinklered", ReviewInputSources.Sprinklered);
                }
                if (item.Has("floorNumber"))
                {
                    zone.FloorNumber = Integer(item.OptionalNullableInt("floorNumber"));
                    Ask("floorNumber", ReviewInputSources.FloorNumber);
                }
                if (item.Has("linksRefugeFloor"))
                {
                    zone.LinksRefugeFloor = YesNo(item.OptionalNullableBool("linksRefugeFloor"));
                    Ask("linksRefugeFloor", ReviewInputSources.LinksRefugeFloor);
                }
                if (item.Has("cannotBeSubdivided"))
                {
                    // Read after 用途 was set, exactly like the panel's column lights up when 用途 changes.
                    if (zone.CanEditCannotBeSubdivided)
                    {
                        zone.CannotBeSubdivided = YesNo(item.OptionalNullableBool("cannotBeSubdivided"));
                        Ask("cannotBeSubdivided", ReviewInputSources.CannotBeSubdivided);
                    }
                    else
                    {
                        requested.Add(new RequestedValue("cannotBeSubdivided", uniqueId, zone.DisplayName,
                            ReviewInputSources.CannotBeSubdivided, "只有第79條之1 的六種用途要填無法區劃分隔（先設 use）。"));
                    }
                }
            }

            private static void ApplyProject(FireReviewParameterDraft draft, McpToolArguments item, List<RequestedValue> requested)
            {
                var project = draft.Project ?? throw new McpToolException("這個文件沒有可讀取的專案資訊。");
                var uniqueId = project.Source.ElementUniqueId;

                void Ask(string field, string parameter) =>
                    requested.Add(new RequestedValue(field, uniqueId, "專案資訊", parameter, null));

                if (item.Has("fireResistiveConstruction"))
                {
                    project.FireResistive = YesNo(item.OptionalNullableBool("fireResistiveConstruction"));
                    Ask("fireResistiveConstruction", ReviewInputSources.FireResistiveConstruction);
                }
                if (item.Has("buildingUse"))
                {
                    project.BuildingUse = item.OptionalString("buildingUse") ?? string.Empty;
                    draft.PushBuildingUse();
                    Ask("buildingUse", ReviewInputSources.BuildingUse);
                }
                if (item.Has("floorsAboveGround"))
                {
                    project.FloorsAboveGround = Integer(item.OptionalNullableInt("floorsAboveGround"));
                    Ask("floorsAboveGround", ReviewInputSources.FloorsAboveGround);
                }
            }

            private static string UniqueIdOf(Document document, McpToolArguments item, string uniqueIdName, string elementIdName, string where)
            {
                var uniqueId = item.OptionalString(uniqueIdName);
                if (!string.IsNullOrWhiteSpace(uniqueId)) return uniqueId.Trim();

                var elementId = item.OptionalNullableInt(elementIdName, 1);
                if (!elementId.HasValue) throw new McpToolException($"{where} 的每一項都要有 {uniqueIdName} 或 {elementIdName}。");
                var element = document.GetElement(new ElementId((long)elementId.Value))
                              ?? throw new McpToolException($"{where} 中的 ElementId {elementId.Value} 不存在。");
                return element.UniqueId;
            }

            private static bool RequireBool(McpToolArguments item, string name, string row) =>
                item.OptionalNullableBool(name) ?? throw new McpToolException($"{row}：{name} 只能是 true 或 false（面板的勾選框沒有「未填」）。");

            /// <summary>是／否／空白, the three values the panel's 是否 columns hold.</summary>
            private static string YesNo(bool? value) =>
                value == true ? FireReviewEditableRow.YesText : value == false ? FireReviewEditableRow.NoText : string.Empty;

            private static string Integer(int? value) =>
                value.HasValue ? value.Value.ToString(CultureInfo.InvariantCulture) : string.Empty;

            private sealed class RequestedValue
            {
                public RequestedValue(string field, string uniqueId, string name, string parameter, string reason)
                {
                    Field = field;
                    UniqueId = uniqueId;
                    Name = name;
                    Parameter = parameter;
                    Reason = reason;
                }

                public string Field { get; }
                public string UniqueId { get; }
                public string Name { get; }
                public string Parameter { get; }
                public string Reason { get; }
            }
        }

        private static View ResolveView(UIApplication application, Document document, int? viewId)
        {
            if (viewId.HasValue)
            {
                var view = document.GetElement(new ElementId((long)viewId.Value)) as View
                           ?? throw new McpToolException($"ElementId {viewId.Value} 不是視圖。");
                if (view.IsTemplate) throw new McpToolException("這是視圖樣板，沒有可收集的構件。");
                return view;
            }

            var active = application.ActiveUIDocument?.ActiveGraphicalView ?? document.ActiveView;
            return active == null || active.IsTemplate ? null : active;
        }

        private sealed class FireReviewPackageResolution
        {
            public FireReviewPackageResolution(Guid packageId, FireReviewPackageList packages)
            {
                PackageId = packageId;
                Packages = packages;
            }

            public Guid PackageId { get; }
            public FireReviewPackageList Packages { get; }
        }
    }
}
