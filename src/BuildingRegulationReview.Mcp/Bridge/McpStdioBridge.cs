using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using BuildingRegulationReview.Mcp.Json;
using BuildingRegulationReview.Mcp.Protocol;

namespace BuildingRegulationReview.Mcp.Bridge;

/// <summary>
/// The MCP server an agent launches over stdio, standing in front of the add-in's HTTP endpoint
/// (docs/mcp-server.md §3). It exists so that the agent's configuration never changes: the bridge
/// starts with the agent whether Revit is open or not, finds the add-in through
/// <see cref="McpEndpointFile"/> on every request, and tells the agent when the tool list changes.
/// </summary>
/// <remarks>
/// It answers <c>initialize</c> and <c>ping</c> itself — an agent that starts before Revit must still
/// connect — and forwards everything else. Tool lists are cached on disk so a later session that
/// starts before Revit still shows the real tools; a call made while Revit is away comes back as a
/// tool error that says what to do, not as a broken connection.
/// </remarks>
public sealed class McpStdioBridge
{
    public const string ServerName = "building-regulation-review";
    public const string OfflineToolName = "revit_status";

    /// <summary>Generous on purpose: a call may wait 30 s in the add-in's queue and then run for 10 min.</summary>
    public static readonly TimeSpan ToolCallTimeout = TimeSpan.FromMinutes(12);

    public static readonly TimeSpan QuickTimeout = TimeSpan.FromSeconds(5);

    private const string DefaultInstructions =
        "這是 Revit「建築技術規則檢討」外掛的 MCP 伺服器（經由本機 bridge 連線）。Revit 尚未開啟時工具會回報無法連線；" +
        "請使用者開啟 Revit 2024 並確認功能區「建築法規檢討 › AI 代理 › MCP 服務」為執行中後再呼叫，不必重新啟動代理程式。" +
        "開始操作前先呼叫 revit_status。";

    private readonly Func<McpEndpoint?> _endpoints;
    private readonly IMcpUpstream _upstream;
    private readonly string? _cachePath;
    private readonly Action<string> _send;
    private readonly object _gate = new();

    private string? _announcedTools;
    private bool _initialized;

    public McpStdioBridge(Func<McpEndpoint?> endpoints, IMcpUpstream upstream, string? cachePath, Action<string> send)
    {
        _endpoints = endpoints ?? throw new ArgumentNullException(nameof(endpoints));
        _upstream = upstream ?? throw new ArgumentNullException(nameof(upstream));
        _cachePath = cachePath;
        _send = send ?? throw new ArgumentNullException(nameof(send));
    }

    public static string Version =>
        typeof(McpStdioBridge).GetTypeInfo().Assembly.GetName().Version?.ToString() ?? "0.0.0";

    /// <summary>Handles one line from stdin. Safe to call from several threads at once.</summary>
    public void Handle(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return;

        JsonValue message;
        try
        {
            message = JsonValue.Parse(line);
        }
        catch (JsonParseException exception)
        {
            _send(Error(JsonValue.Null, McpServer.ParseError, "Parse error: " + exception.Message));
            return;
        }

        if (message is JsonArray batch)
        {
            foreach (var item in batch) HandleOne(item);
            return;
        }
        HandleOne(message);
    }

    /// <summary>
    /// Called on a timer: when Revit has come up (or come back with different tools) since the agent
    /// last listed them, says so with <c>notifications/tools/list_changed</c>.
    /// </summary>
    public void Poll()
    {
        lock (_gate)
        {
            if (!_initialized) return;
        }
        var fresh = FetchTools(QuickTimeout);
        if (fresh is null) return;

        bool changed;
        lock (_gate)
        {
            changed = _announcedTools is not null && _announcedTools != fresh;
            if (changed) _announcedTools = fresh;
        }
        if (changed)
            _send(new JsonObject { ["jsonrpc"] = "2.0", ["method"] = "notifications/tools/list_changed" }.ToString());
    }

    private void HandleOne(JsonValue message)
    {
        if (message is not JsonObject request || request["jsonrpc"]?.AsString() != "2.0")
        {
            _send(Error(JsonValue.Null, McpServer.InvalidRequest, "Not a JSON-RPC 2.0 message"));
            return;
        }

        var method = request["method"]?.AsString();
        // Notifications (initialized, cancelled) and responses: the add-in is stateless and has
        // nothing to do with either, so they stop here.
        if (method is null || !request.Contains("id")) return;

        var id = request["id"] ?? JsonValue.Null;
        switch (method)
        {
            case "initialize":
                _send(Result(id, Initialize(request["params"] as JsonObject)));
                return;
            case "ping":
                _send(Result(id, new JsonObject()));
                return;
            case "tools/list":
                _send(Result(id, ListTools()));
                return;
            default:
                Forward(id, request, method);
                return;
        }
    }

    private JsonObject Initialize(JsonObject? parameters)
    {
        var requested = parameters?["protocolVersion"]?.AsString();
        var version = requested is not null && McpServer.SupportedProtocolVersions.Contains(requested)
            ? requested
            : McpServer.SupportedProtocolVersions[0];

        lock (_gate) _initialized = true;

        return new JsonObject
        {
            ["protocolVersion"] = version,
            ["capabilities"] = new JsonObject { ["tools"] = new JsonObject { ["listChanged"] = true } },
            ["serverInfo"] = new JsonObject { ["name"] = ServerName, ["version"] = Version, ["title"] = "建築技術規則檢討" },
            ["instructions"] = Instructions()
        };
    }

    /// <summary>The add-in's own instructions when it is up, else the last ones seen, else a default.</summary>
    private string Instructions()
    {
        var endpoint = _endpoints();
        if (endpoint is not null)
        {
            var probe = new JsonObject
            {
                ["jsonrpc"] = "2.0",
                ["id"] = "bridge-initialize",
                ["method"] = "initialize",
                ["params"] = new JsonObject { ["protocolVersion"] = McpServer.SupportedProtocolVersions[0] }
            };
            var reply = _upstream.Post(endpoint, probe.ToString(), QuickTimeout);
            var instructions = ResultOf(reply)?["instructions"]?.AsString();
            if (instructions is not null)
            {
                SaveCache(instructions: instructions);
                return instructions;
            }
        }
        return ReadCache()?["instructions"]?.AsString() ?? DefaultInstructions;
    }

    private JsonObject ListTools()
    {
        var fresh = FetchTools(QuickTimeout);
        var tools = fresh ?? ReadCache()?["tools"]?.ToString() ?? OfflineTools().ToString();
        lock (_gate) _announcedTools = tools;
        return new JsonObject { ["tools"] = JsonValue.Parse(tools) };
    }

    /// <summary>The add-in's tool array as JSON text, cached on the way; null when it cannot be reached.</summary>
    private string? FetchTools(TimeSpan timeout)
    {
        var endpoint = _endpoints();
        if (endpoint is null) return null;
        var request = new JsonObject { ["jsonrpc"] = "2.0", ["id"] = "bridge-tools", ["method"] = "tools/list" };
        if (ResultOf(_upstream.Post(endpoint, request.ToString(), timeout))?["tools"] is not JsonArray tools) return null;

        var text = tools.ToString();
        SaveCache(tools: tools);
        return text;
    }

    private void Forward(JsonValue id, JsonObject request, string method)
    {
        var timeout = method == "tools/call" ? ToolCallTimeout : QuickTimeout;
        var endpoint = _endpoints();
        var reply = endpoint is null
            ? McpUpstreamReply.Unreachable("Revit 沒有公告 MCP 端點（Revit 未開啟，或 MCP 服務未啟動）")
            : _upstream.Post(endpoint, request.ToString(), timeout);

        // The file may have been rewritten between the read and the call — Revit restarted with a new token.
        if (reply.Status is McpUpstreamStatus.Unauthorized or McpUpstreamStatus.Unreachable)
        {
            var again = _endpoints();
            if (again is not null && !again.SameServerAs(endpoint))
                reply = _upstream.Post(again, request.ToString(), timeout);
        }

        switch (reply.Status)
        {
            case McpUpstreamStatus.Answered:
                // stdio frames are lines. A raw line break can only be whitespace between JSON tokens
                // (inside a string it would be escaped), so dropping it changes nothing else.
                _send(reply.Body!.Replace("\r", string.Empty).Replace("\n", string.Empty));
                return;
            case McpUpstreamStatus.NoContent:
                return;
        }

        var text = Unavailable(reply);
        if (method == "tools/call")
        {
            // A tool error, not a protocol error: the agent reads it, tells the user, and simply calls again.
            _send(Result(id, new JsonObject
            {
                ["content"] = new JsonArray { new JsonObject { ["type"] = "text", ["text"] = text } },
                ["isError"] = true
            }));
        }
        else
        {
            _send(Error(id, McpServer.InternalError, text));
        }
    }

    private static string Unavailable(McpUpstreamReply reply) => reply.Status switch
    {
        McpUpstreamStatus.Unauthorized =>
            $"Revit 外掛拒絕了連線：{reply.Detail}。通常是上一次 Revit 留下的端點檔與現在的服務不符；" +
            "請在 Revit 功能區「建築法規檢討 › AI 代理 › MCP 服務」按一下停止、再按一下啟動，然後再呼叫一次。",
        McpUpstreamStatus.Failed =>
            $"Revit 外掛沒有正常回應：{reply.Detail}。Revit 可能正在執行長時間的工作，請用 revit_status 確認後再試。",
        _ =>
            $"無法連線到 Revit 的建築技術規則檢討外掛（{reply.Detail}）。請確認：1. Revit 2024 已開啟並載入外掛；" +
            "2. 功能區「建築法規檢討 › AI 代理 › MCP 服務」顯示執行中（預設隨 Revit 自動啟動，若顯示已停止請按一下）。" +
            "Revit 準備好後直接再呼叫一次即可，不必重新啟動代理程式。"
    };

    /// <summary>What the agent sees before Revit has ever been reached: one tool that explains the situation.</summary>
    private static JsonArray OfflineTools() => new()
    {
        new JsonObject
        {
            ["name"] = OfflineToolName,
            ["title"] = "Revit 連線狀態",
            ["description"] = "回報 Revit 與建築技術規則檢討外掛的狀態。Revit 尚未開啟時會說明如何讓外掛上線；" +
                              "上線後完整的工具清單會自動出現。",
            ["inputSchema"] = new JsonObject { ["type"] = "object", ["properties"] = new JsonObject() },
            ["annotations"] = new JsonObject { ["readOnlyHint"] = true }
        }
    };

    private static JsonObject? ResultOf(McpUpstreamReply reply)
    {
        if (reply.Status != McpUpstreamStatus.Answered) return null;
        try
        {
            return (JsonValue.Parse(reply.Body!) as JsonObject)?["result"] as JsonObject;
        }
        catch (JsonParseException)
        {
            return null;
        }
    }

    private JsonObject? ReadCache()
    {
        if (_cachePath is null) return null;
        try
        {
            return File.Exists(_cachePath) ? JsonValue.Parse(File.ReadAllText(_cachePath, Encoding.UTF8)) as JsonObject : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonParseException)
        {
            return null;
        }
    }

    private void SaveCache(string? instructions = null, JsonArray? tools = null)
    {
        if (_cachePath is null) return;
        lock (_gate)
        {
            var cache = ReadCache() ?? new JsonObject();
            var changed = false;
            if (instructions is not null && cache["instructions"]?.AsString() != instructions)
            {
                cache["instructions"] = instructions;
                changed = true;
            }
            if (tools is not null && cache["tools"]?.ToString() != tools.ToString())
            {
                cache["tools"] = tools;
                changed = true;
            }
            if (!changed) return;
            try
            {
                McpEndpointFile.WriteAtomically(_cachePath, cache.ToString());
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // Only a convenience for the next session; this one already has what it needs.
            }
        }
    }

    private static string Result(JsonValue id, JsonObject result) =>
        new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id, ["result"] = result }.ToString();

    private static string Error(JsonValue id, int code, string message) =>
        new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id,
            ["error"] = new JsonObject { ["code"] = code, ["message"] = message }
        }.ToString();
}
