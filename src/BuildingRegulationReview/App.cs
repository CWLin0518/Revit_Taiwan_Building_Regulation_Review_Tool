using System;
using System.Diagnostics;
using System.Net.Sockets;
using System.Reflection;
using Autodesk.Revit.UI;
using BuildingRegulationReview.ExternalEvents;
using BuildingRegulationReview.Features;
using BuildingRegulationReview.Features.Article164;
using BuildingRegulationReview.Features.FireReview;
using BuildingRegulationReview.Mcp;

namespace BuildingRegulationReview
{
    public sealed class App : IExternalApplication
    {
        public static readonly DockablePaneId ReviewPaneId = new DockablePaneId(new Guid("AE47CF0C-CFE8-4803-A97C-381AF4F1E760"));
        private ReviewExternalEventDispatcher _eventDispatcher;
        private RevitMcpDispatcher _mcpDispatcher;
        private static PushButton _mcpButton;
        private const string StoppedText = "MCP 服務\n（已停止）";

        /// <summary>The MCP server; created at startup, listening only while switched on.</summary>
        internal static RevitMcpHost McpHost { get; private set; }

        public Result OnStartup(UIControlledApplication application)
        {
            const string tabName = "建築法規檢討";
            try { application.CreateRibbonTab(tabName); } catch (Autodesk.Revit.Exceptions.ArgumentException) { }
            var panel = application.CreateRibbonPanel(tabName, "法規檢討");
            var path = Assembly.GetExecutingAssembly().Location;
            // Ribbon 只放兩個入口：開啟檢討面板，與「AI 代理」的 MCP 服務開關。檢討工具一律從面板操作，
            // 不再往 Ribbon 加按鈕（DESIGN.md §3、docs/tools/review-panel/SPEC.md）。
            var openButton = (PushButton)panel.AddItem(new PushButtonData("OpenBuildingRegulationReview", "開啟檢討\n面板", path, typeof(ShowReviewPaneCommand).FullName));
            openButton.ToolTip = "開啟「建築技術規則檢討」面板：第164條道路陰影、建築防火檢討等所有檢討工具都在面板中依步驟操作。";

            var featureRegistry = new ReviewFeatureRegistry(new IReviewFeature[]
            {
                new Article164ReviewFeature(),
                new FireReviewFeature(),
            });
            _eventDispatcher = new ReviewExternalEventDispatcher(featureRegistry);
            _eventDispatcher.Initialize();
            application.RegisterDockablePane(ReviewPaneId, "建築技術規則檢討",
                new ReviewPaneProvider(featureRegistry, _eventDispatcher));

            StartMcp(application, tabName, path);
            return Result.Succeeded;
        }

        /// <summary>
        /// The MCP server and its on/off button, on a panel of its own. A failure here must never take
        /// the review tools down with it, so it is logged and the add-in carries on without MCP.
        /// </summary>
        private void StartMcp(UIControlledApplication application, string tabName, string path)
        {
            try
            {
                _mcpDispatcher = new RevitMcpDispatcher();
                _mcpDispatcher.Initialize();
                McpHost = new RevitMcpHost(_mcpDispatcher);

                var panel = application.CreateRibbonPanel(tabName, "AI 代理");
                var data = new PushButtonData("McpService", StoppedText, path, typeof(McpServiceCommand).FullName)
                {
                    AvailabilityClassName = typeof(McpServiceAvailability).FullName
                };
                _mcpButton = (PushButton)panel.AddItem(data);

                // The random token needs no one to see it: the server announces it to the bridge through
                // the endpoint file in the user's profile (docs/tools/mcp-server/ARCHITECTURE.md §3).
                if (RevitMcpHost.AutoStartRequested)
                {
                    try
                    {
                        McpHost.Start();
                    }
                    catch (SocketException exception)
                    {
                        Trace.TraceWarning("建築技術規則檢討 MCP 無法自動啟動：" + exception.Message);
                    }
                }
                RefreshMcpButton();
            }
            catch (Exception exception)
            {
                Trace.TraceError("建築技術規則檢討 MCP 初始化失敗：" + exception);
            }
        }

        /// <summary>Shows on the button whether the server is listening, and where.</summary>
        internal static void RefreshMcpButton()
        {
            if (_mcpButton == null || McpHost == null) return;
            var running = McpHost.IsRunning;
            _mcpButton.ItemText = running ? "MCP 服務\n（執行中）" : StoppedText;
            _mcpButton.ToolTip = running
                ? $"MCP 服務執行中：{McpHost.Endpoint}\n按一下停止。"
                : "讓本機的 AI 代理程式（例如 Claude Code）透過 MCP 操作本外掛的防火區劃檢討與參數設定，用於自動驗證。按一下啟動。";
        }

        public Result OnShutdown(UIControlledApplication application)
        {
            McpHost?.Dispose();
            McpHost = null;
            _mcpDispatcher?.Dispose();
            _mcpDispatcher = null;
            _eventDispatcher?.Dispose();
            _eventDispatcher = null;
            return Result.Succeeded;
        }
    }
}
