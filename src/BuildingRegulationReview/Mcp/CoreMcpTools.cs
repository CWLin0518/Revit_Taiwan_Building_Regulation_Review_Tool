using System;
using System.Collections.Generic;
using System.Threading;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using BuildingRegulationReview.Mcp.Json;
using BuildingRegulationReview.Mcp.Tools;

namespace BuildingRegulationReview.Mcp
{
    /// <summary>Tools every feature relies on: is Revit there, which model is open, what just happened.</summary>
    internal sealed class CoreMcpTools : IMcpToolModule
    {
        private readonly RevitMcpHost _host;

        public CoreMcpTools(RevitMcpHost host) => _host = host ?? throw new ArgumentNullException(nameof(host));

        public IEnumerable<IMcpTool> Tools => new IMcpTool[] { new StatusTool(_host) };

        private sealed class StatusTool : IMcpTool
        {
            private readonly RevitMcpHost _host;

            public StatusTool(RevitMcpHost host) => _host = host;

            public string Name => "revit_status";
            public string Title => "Revit 與外掛狀態";

            public string Description =>
                "回報 Revit 版本、外掛版本、作用中的模型與視圖，以及最近的工具呼叫（含錯誤）。" +
                "開始操作前先呼叫一次；工具回報「Revit 忙碌中」或逾時後，也用它確認 Revit 是否已恢復。不會修改模型。";

            public JsonObject InputSchema => JsonSchema.Empty();
            public McpToolAnnotations Annotations => McpToolAnnotations.ReadOnly;

            public McpToolResult Call(McpToolArguments arguments, CancellationToken cancellation)
            {
                var result = new JsonObject
                {
                    ["addinVersion"] = RevitMcpHost.Version,
                    ["endpoint"] = _host.Endpoint,
                    ["running"] = _host.Dispatcher.Running,
                    ["queued"] = _host.Dispatcher.Queued,
                    ["recentCalls"] = RecentCalls()
                };

                // The rest needs the API context. If Revit is busy the agent still learns that much,
                // rather than only getting a timeout.
                try
                {
                    var revit = _host.Dispatcher.Invoke(Name, (application, _) => Describe(application),
                        TimeSpan.FromSeconds(30), cancellation);
                    result["revit"] = revit;
                    result["revitResponsive"] = true;
                }
                catch (McpToolException exception)
                {
                    result["revit"] = JsonValue.Null;
                    result["revitResponsive"] = false;
                    result["revitError"] = exception.Message;
                    return McpToolResult.Success(result, exception.Message);
                }

                return McpToolResult.Success(result, "Revit 可回應。");
            }

            private static JsonObject Describe(UIApplication application)
            {
                var revit = new JsonObject
                {
                    ["version"] = application.Application.VersionNumber,
                    ["build"] = application.Application.VersionBuild,
                    ["language"] = application.Application.Language.ToString(),
                    ["username"] = application.Application.Username
                };

                var uiDocument = application.ActiveUIDocument;
                var document = uiDocument?.Document;
                if (document == null)
                {
                    revit["document"] = JsonValue.Null;
                    return revit;
                }

                var view = uiDocument.ActiveGraphicalView ?? document.ActiveView;
                revit["document"] = new JsonObject
                {
                    ["title"] = document.Title,
                    ["path"] = document.PathName,
                    ["isFamilyDocument"] = document.IsFamilyDocument,
                    ["isWorkshared"] = document.IsWorkshared,
                    ["isModified"] = document.IsModified,
                    ["isReadOnly"] = document.IsReadOnly
                };
                revit["activeView"] = view == null ? JsonValue.Null : new JsonObject
                {
                    ["elementId"] = view.Id.Value,
                    ["name"] = view.Name,
                    ["viewType"] = view.ViewType.ToString(),
                    ["level"] = (view as ViewPlan)?.GenLevel?.Name
                };
                revit["selection"] = uiDocument.Selection.GetElementIds().Count;
                return revit;
            }

            private JsonArray RecentCalls()
            {
                var calls = new JsonArray();
                foreach (var call in _host.Server.RecentCalls)
                {
                    calls.Add(new JsonObject
                    {
                        ["tool"] = call.Tool,
                        ["startedAtUtc"] = JsonValue.Of(call.StartedAtUtc),
                        ["seconds"] = Math.Round(call.Elapsed.TotalSeconds, 2),
                        ["isError"] = call.IsError,
                        ["message"] = call.Message
                    });
                }
                return calls;
            }
        }
    }
}
