using System;
using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using BuildingRegulationReview.Mcp.FireReview;
using BuildingRegulationReview.Mcp.Protocol;
using BuildingRegulationReview.Mcp.Tools;
using BuildingRegulationReview.Mcp.Transport;

namespace BuildingRegulationReview.Mcp
{
    /// <summary>
    /// The add-in's MCP server: the tool modules, the protocol and the loopback HTTP endpoint, started
    /// and stopped from the ribbon. Off until the user turns it on (or <c>BRR_MCP_AUTOSTART=1</c>),
    /// because while it runs any local process can drive the open model.
    /// </summary>
    internal sealed class RevitMcpHost : IDisposable
    {
        public const string ServerName = "building-regulation-review";
        public const int DefaultPort = 8970;
        public const string PortVariable = "BRR_MCP_PORT";
        public const string AutoStartVariable = "BRR_MCP_AUTOSTART";
        public const string TokenVariable = "BRR_MCP_TOKEN";

        /// <summary>What the agent is told on initialize: how the tools fit together.</summary>
        private const string Instructions =
            "這是 Revit「建築技術規則檢討」外掛的 MCP 伺服器，操作的是使用者目前在 Revit 開啟的模型。\n" +
            "工具與外掛按鈕走同一段程式，用來代替人操作並驗證結果。\n" +
            "建議流程：revit_status 確認模型 → fire_review_list_packages 取得 packageId → " +
            "fire_review_scan_parameters 檢查參數 → 需要時 fire_review_set_parameters（先 dryRun）→ " +
            "fire_review_check 看前置檢查 → fire_review_run（驗證時可先 dryRun）→ " +
            "fire_review_get_results / fire_review_describe_result 檢查結果。\n" +
            "Revit 有對話框開著或正在執行指令時無法回應，工具會回報「Revit 忙碌中」，請使用者處理後再試。" +
            "檢討判定（符合／未符合）由外掛的規則決定；人工覆寫不提供給 MCP。";

        private readonly McpServer _server;
        private McpHttpListener _listener;

        public RevitMcpHost(RevitMcpDispatcher dispatcher)
        {
            if (dispatcher == null) throw new ArgumentNullException(nameof(dispatcher));
            Dispatcher = dispatcher;
            Port = ReadPort();
            var configured = Environment.GetEnvironmentVariable(TokenVariable);
            TokenIsConfigured = !string.IsNullOrWhiteSpace(configured);
            Token = TokenIsConfigured ? configured.Trim() : NewToken();

            var tools = new McpToolRegistry()
                .AddModule(new CoreMcpTools(this))
                .AddModule(new FireReviewMcpTools(dispatcher));

            _server = new McpServer(new McpServerOptions(ServerName, Version, "建築技術規則檢討", Instructions), tools);
        }

        public RevitMcpDispatcher Dispatcher { get; }

        public McpServer Server => _server;

        public int Port { get; }

        /// <summary>
        /// The bearer token every request must carry: <c>BRR_MCP_TOKEN</c> when set, otherwise a random
        /// one for this Revit session, shown in the dialog that starts the server.
        /// </summary>
        public string Token { get; }

        public bool TokenIsConfigured { get; }

        public static string Version =>
            Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0";

        public bool IsRunning => _listener?.IsRunning == true;

        public string Endpoint => $"http://127.0.0.1:{Port}/mcp";

        /// <summary>The one-line Claude Code command that connects to this server, token included.</summary>
        public string ClaudeCodeCommand =>
            $"claude mcp add --transport http {ServerName} {Endpoint} --header \"Authorization: Bearer {Token}\"";

        public static bool AutoStartRequested
        {
            get
            {
                var value = Environment.GetEnvironmentVariable(AutoStartVariable);
                return value == "1" || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
            }
        }

        /// <summary>Starts listening. Throws <see cref="System.Net.Sockets.SocketException"/> when the port is taken.</summary>
        public void Start()
        {
            if (IsRunning) return;
            var listener = new McpHttpListener(_server.Handle, Port, bearerToken: Token);
            listener.Start();
            _listener = listener;
        }

        public void Stop()
        {
            _listener?.Stop();
            _listener = null;
        }

        public void Dispose() => Stop();

        private static string NewToken()
        {
            var bytes = new byte[24];
            using (var random = RandomNumberGenerator.Create()) random.GetBytes(bytes);
            return BitConverter.ToString(bytes).Replace("-", string.Empty).ToLowerInvariant();
        }

        private static int ReadPort()
        {
            var text = Environment.GetEnvironmentVariable(PortVariable);
            return int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var port) && port >= 1024 && port <= 65535
                ? port
                : DefaultPort;
        }
    }
}
