using System;
using System.Diagnostics;
using System.Net.Sockets;
using System.Reflection;
using Autodesk.Revit.UI;
using BuildingRegulationReview.ExternalEvents;
using BuildingRegulationReview.Features;
using BuildingRegulationReview.Features.Article164;
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
            panel.AddItem(new PushButtonData("OpenBuildingRegulationReview", "開啟檢討\n面板", path, typeof(ShowReviewPaneCommand).FullName));
            var button = (PushButton)panel.AddItem(new PushButtonData("ReviewArticle164", "第164條\n道路陰影", path, typeof(Article164Command).FullName));
            button.ToolTip = "以 RhinoCommon 計算 3.6:1 道路陰影，並在目前平面視圖建立 FilledRegion。";
            var setupButton = (PushButton)panel.AddItem(new PushButtonData("FireReviewSetup", "防火區劃\n設定", path, typeof(FireReviewSetupCommand).FullName));
            setupButton.ToolTip = "選擇來源樓層平面、面積配置與 Area Plan 選項，建立檢討套件。";
            var parameterSetupButton = (PushButton)panel.AddItem(new PushButtonData("FireReviewParameterSetup", "防火參數\n一鍵建立", path, typeof(FireReviewParameterSetupCommand).FullName));
            parameterSetupButton.ToolTip = "把防火區劃檢討要讀的共用參數一次加進目前專案並綁好類別（專案資訊、面積、牆柱樑樓板、天花板、門窗、帷幕嵌板）。先顯示差異預覽，已綁好的不動，同名但定義不符的不覆蓋只給修正方式。";
            var parameterButton = (PushButton)panel.AddItem(new PushButtonData("FireReviewParameters", "防火參數\n批次設定", path, typeof(FireReviewParameterPanelCommand).FullName));
            parameterButton.ToolTip = "收集目前視圖的牆、柱、樓板、門窗類型，批次填寫結構材料與防火時效；RC／SRC／SC 加上斷面尺寸可依第71～73條自動推定時效。";
            var editorButton = (PushButton)panel.AddItem(new PushButtonData("RegionEditor", "防火區劃\n編輯器", path, typeof(RegionEditorCommand).FullName));
            editorButton.ToolTip = "讀取 Area Plan 的牆、柱、房間分隔線與輔助線，求解封閉範圍，並以左右鍵編輯防火區劃草稿。模型不會被更動。";
            var reviewButton = (PushButton)panel.AddItem(new PushButtonData("FireReview", "防火區劃\n檢討", path, typeof(FireReviewCommand).FullName));
            reviewButton.ToolTip = "前置檢查後一次檢討防火區劃面積、構件防火時效與防火門窗，在專用檢討視圖標示未符合項目，並可定位與人工覆寫。";

            var featureRegistry = new ReviewFeatureRegistry(new IReviewFeature[]
            {
                new Article164ReviewFeature(),
                new FireReviewSetupFeature(),
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

                // Started unattended only with a token the agent's configuration already knows: a
                // random one would be shown to nobody.
                if (RevitMcpHost.AutoStartRequested && !McpHost.TokenIsConfigured)
                {
                    Trace.TraceWarning($"建築技術規則檢討 MCP 未自動啟動：自動啟動需要同時設定 {RevitMcpHost.TokenVariable}。");
                }
                else if (RevitMcpHost.AutoStartRequested)
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
