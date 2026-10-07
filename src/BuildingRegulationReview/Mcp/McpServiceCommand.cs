using System;
using System.Net.Sockets;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace BuildingRegulationReview.Mcp
{
    /// <summary>
    /// The ribbon's MCP 服務 button: turns the add-in's MCP server on or off and says how to connect.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public sealed class McpServiceCommand : IExternalCommand
    {
        private const string DialogTitle = "MCP 服務";

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var host = App.McpHost;
            if (host == null)
            {
                message = "MCP 服務沒有初始化，請重新啟動 Revit。";
                return Result.Failed;
            }

            if (host.IsRunning)
            {
                host.Stop();
                App.RefreshMcpButton();
                TaskDialog.Show(DialogTitle, "MCP 服務已停止。AI 代理程式無法再操作這個外掛。");
                return Result.Succeeded;
            }

            try
            {
                host.Start();
            }
            catch (SocketException exception)
            {
                message = $"無法在 {host.Endpoint} 啟動 MCP 服務：{exception.Message}\n\n" +
                          $"這個連接埠可能已被其他程式使用。可設定環境變數 {RevitMcpHost.PortVariable} 改用其他連接埠後重新啟動 Revit。";
                TaskDialog.Show(DialogTitle, message);
                return Result.Failed;
            }

            App.RefreshMcpButton();

            // A TaskDialog's text cannot be selected, and a 48-character token is not something to retype.
            var copied = false;
            try
            {
                System.Windows.Clipboard.SetText(host.ClaudeCodeCommand);
                copied = true;
            }
            catch (Exception)
            {
                // The clipboard is busy in another program; the command is still shown below.
            }

            var dialog = new TaskDialog(DialogTitle)
            {
                MainInstruction = copied ? "MCP 服務已啟動（連線指令已複製到剪貼簿）" : "MCP 服務已啟動",
                MainContent = $"端點：{host.Endpoint}\n\n" +
                              "只接受本機、帶正確權杖的連線。服務執行期間，持有權杖的 AI 代理程式可以讀取並修改目前開啟的模型。\n\n" +
                              $"Claude Code 連線指令（已含權杖，請勿外流）：\n{host.ClaudeCodeCommand}" +
                              (host.TokenIsConfigured ? string.Empty : "\n\n這個權杖只在本次 Revit 工作階段有效，重開 Revit 後要重新設定。"),
                FooterText = $"同時設定環境變數 {RevitMcpHost.TokenVariable}（固定權杖）與 {RevitMcpHost.AutoStartVariable}=1，可在 Revit 啟動時自動開啟服務。"
            };
            dialog.Show();
            return Result.Succeeded;
        }
    }

    /// <summary>Lets the MCP button work with no document open, so the server can be started first.</summary>
    public sealed class McpServiceAvailability : IExternalCommandAvailability
    {
        public bool IsCommandAvailable(UIApplication applicationData, CategorySet selectedCategories) => true;
    }
}
