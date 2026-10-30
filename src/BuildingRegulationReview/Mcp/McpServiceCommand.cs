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

            // A TaskDialog's text cannot be selected, and nobody should retype an install path.
            var copied = false;
            try
            {
                System.Windows.Clipboard.SetText(RevitMcpHost.ClaudeCodeBridgeCommand);
                copied = true;
            }
            catch (Exception)
            {
                // The clipboard is busy in another program; the command is still shown below.
            }

            var dialog = new TaskDialog(DialogTitle)
            {
                MainInstruction = "MCP 服務已啟動",
                MainContent = $"端點：{host.Endpoint}\n\n" +
                              "只接受本機、帶正確權杖的連線。權杖已寫入本使用者的端點檔，AI 代理透過 bridge 自動取得，不必手動設定。" +
                              "服務執行期間，代理程式可以讀取並修改目前開啟的模型。\n\n" +
                              "Claude Code 只需設定一次（安裝腳本已代為設定；" + (copied ? "指令已複製到剪貼簿" : "指令如下") + "）：\n" +
                              RevitMcpHost.ClaudeCodeBridgeCommand,
                FooterText = $"MCP 服務預設隨 Revit 自動啟動；不要自動啟動時，設定環境變數 {RevitMcpHost.AutoStartVariable}=0。"
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
