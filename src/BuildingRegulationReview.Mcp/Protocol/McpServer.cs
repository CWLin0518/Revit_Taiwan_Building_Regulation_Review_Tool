using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using BuildingRegulationReview.Mcp.Json;
using BuildingRegulationReview.Mcp.Tools;

namespace BuildingRegulationReview.Mcp.Protocol;

public sealed class McpServerOptions
{
    public McpServerOptions(string name, string version, string? title = null, string? instructions = null)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("A server needs a name.", nameof(name));
        if (string.IsNullOrWhiteSpace(version)) throw new ArgumentException("A server needs a version.", nameof(version));
        Name = name;
        Version = version;
        Title = title;
        Instructions = instructions;
    }

    public string Name { get; }
    public string Version { get; }
    public string? Title { get; }

    /// <summary>Sent with <c>initialize</c>: how the tools fit together, so the agent does not have to guess the order.</summary>
    public string? Instructions { get; }
}

/// <summary>One finished tool call, kept for <c>revit_status</c> so an agent can see what just happened.</summary>
public sealed class McpCallRecord
{
    internal McpCallRecord(string tool, DateTime startedAtUtc, TimeSpan elapsed, bool isError, string? message)
    {
        Tool = tool;
        StartedAtUtc = startedAtUtc;
        Elapsed = elapsed;
        IsError = isError;
        Message = message;
    }

    public string Tool { get; }
    public DateTime StartedAtUtc { get; }
    public TimeSpan Elapsed { get; }
    public bool IsError { get; }
    public string? Message { get; }
}

/// <summary>
/// The MCP server side of JSON-RPC 2.0: <c>initialize</c>, <c>ping</c>, <c>tools/list</c> and
/// <c>tools/call</c>. Stateless — every message stands on its own — so it needs no session and can
/// sit behind any transport.
/// </summary>
/// <remarks>
/// A tool that fails is not a protocol error: the failure goes back as a result with
/// <c>isError: true</c>, which is what lets the agent read it and try again (MCP specification,
/// "Error Handling"). Only a malformed message or an unknown tool is a JSON-RPC error.
/// </remarks>
public sealed class McpServer
{
    /// <summary>Newest first. A client asking for one of these gets it back; any other gets the newest.</summary>
    public static readonly IReadOnlyList<string> SupportedProtocolVersions = new[]
    {
        "2025-11-25", "2025-06-18", "2025-03-26", "2024-11-05"
    };

    public const int ParseError = -32700;
    public const int InvalidRequest = -32600;
    public const int MethodNotFound = -32601;
    public const int InvalidParams = -32602;
    public const int InternalError = -32603;

    private const int RecentCallLimit = 20;

    private readonly McpServerOptions _options;
    private readonly McpToolRegistry _tools;
    private readonly LinkedList<McpCallRecord> _recent = new();
    private readonly object _gate = new();

    public McpServer(McpServerOptions options, McpToolRegistry tools)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _tools = tools ?? throw new ArgumentNullException(nameof(tools));
    }

    public McpServerOptions Options => _options;

    /// <summary>The last tool calls, newest first.</summary>
    public IReadOnlyList<McpCallRecord> RecentCalls
    {
        get
        {
            lock (_gate) return _recent.ToList();
        }
    }

    /// <summary>
    /// Handles one HTTP body: a request, a notification, a response, or a batch of them. Returns the
    /// JSON to send back, or null when there is nothing to answer (notifications and responses).
    /// </summary>
    public string? Handle(string body, CancellationToken cancellation = default)
    {
        JsonValue message;
        try
        {
            message = JsonValue.Parse(body ?? string.Empty);
        }
        catch (JsonParseException exception)
        {
            return Error(JsonValue.Null, ParseError, "Parse error: " + exception.Message).ToString();
        }

        if (message is JsonArray batch)
        {
            if (batch.Count == 0) return Error(JsonValue.Null, InvalidRequest, "Empty batch").ToString();
            var answers = new JsonArray();
            foreach (var item in batch)
            {
                var answer = HandleOne(item, cancellation);
                if (answer is not null) answers.Add(answer);
            }
            return answers.Count == 0 ? null : answers.ToString();
        }

        return HandleOne(message, cancellation)?.ToString();
    }

    private JsonObject? HandleOne(JsonValue message, CancellationToken cancellation)
    {
        if (message is not JsonObject request || request["jsonrpc"]?.AsString() != "2.0")
            return Error(JsonValue.Null, InvalidRequest, "Not a JSON-RPC 2.0 message");

        var method = request["method"]?.AsString();
        var isNotification = !request.Contains("id");
        var id = request["id"] ?? JsonValue.Null;

        // A response to something we never asked: nothing to answer.
        if (method is null)
            return request.Contains("result") || request.Contains("error")
                ? null
                : Error(id, InvalidRequest, "Missing method");

        if (isNotification) return null;

        if (id.Kind != JsonKind.String && id.Kind != JsonKind.Number)
            return Error(JsonValue.Null, InvalidRequest, "The id must be a string or a number");

        var parameters = request["params"] as JsonObject ?? new JsonObject();
        try
        {
            switch (method)
            {
                case "initialize": return Result(id, Initialize(parameters));
                case "ping": return Result(id, new JsonObject());
                case "tools/list": return Result(id, ListTools());
                case "tools/call": return CallTool(id, parameters, cancellation);
                default: return Error(id, MethodNotFound, "Method not found: " + method);
            }
        }
        catch (Exception exception)
        {
            return Error(id, InternalError, exception.Message);
        }
    }

    private JsonObject Initialize(JsonObject parameters)
    {
        var requested = parameters["protocolVersion"]?.AsString();
        var version = requested is not null && SupportedProtocolVersions.Contains(requested)
            ? requested
            : SupportedProtocolVersions[0];

        var serverInfo = new JsonObject { ["name"] = _options.Name, ["version"] = _options.Version };
        if (_options.Title is not null) serverInfo["title"] = _options.Title;

        var result = new JsonObject
        {
            ["protocolVersion"] = version,
            ["capabilities"] = new JsonObject { ["tools"] = new JsonObject { ["listChanged"] = false } },
            ["serverInfo"] = serverInfo
        };
        if (_options.Instructions is not null) result["instructions"] = _options.Instructions;
        return result;
    }

    private JsonObject ListTools()
    {
        var tools = new JsonArray();
        foreach (var tool in _tools.Tools)
        {
            tools.Add(new JsonObject
            {
                ["name"] = tool.Name,
                ["title"] = tool.Title,
                ["description"] = tool.Description,
                ["inputSchema"] = tool.InputSchema,
                ["annotations"] = tool.Annotations.ToJson(tool.Title)
            });
        }
        return new JsonObject { ["tools"] = tools };
    }

    private JsonObject CallTool(JsonValue id, JsonObject parameters, CancellationToken cancellation)
    {
        var name = parameters["name"]?.AsString();
        if (name is null) return Error(id, InvalidParams, "tools/call needs a tool name");
        if (!_tools.TryGet(name, out var tool)) return Error(id, InvalidParams, "Unknown tool: " + name);

        var rawArguments = parameters["arguments"];
        if (rawArguments is not null && !rawArguments.IsNull && rawArguments is not JsonObject)
            return Error(id, InvalidParams, "arguments must be an object");

        var started = DateTime.UtcNow;
        var watch = Stopwatch.StartNew();
        McpToolResult result;
        try
        {
            result = tool.Call(new McpToolArguments(rawArguments as JsonObject), cancellation)
                     ?? McpToolResult.Failure("工具沒有回傳結果。");
        }
        catch (McpToolException exception)
        {
            result = McpToolResult.Failure(exception.Message, exception.Detail);
        }
        catch (OperationCanceledException)
        {
            result = McpToolResult.Failure("工具呼叫已取消。");
        }
        catch (Exception exception)
        {
            // A bug, not an expected failure — but the agent still deserves to see what broke.
            result = McpToolResult.Failure($"工具執行時發生未預期的錯誤（{exception.GetType().Name}）：{exception.Message}",
                new JsonObject { ["exception"] = exception.ToString() });
        }
        watch.Stop();

        Remember(new McpCallRecord(name, started, watch.Elapsed, result.IsError, result.IsError ? result.Summary : null));
        return Result(id, result.ToJson());
    }

    private void Remember(McpCallRecord record)
    {
        lock (_gate)
        {
            _recent.AddFirst(record);
            while (_recent.Count > RecentCallLimit) _recent.RemoveLast();
        }
    }

    private static JsonObject Result(JsonValue id, JsonObject result) =>
        new() { ["jsonrpc"] = "2.0", ["id"] = id, ["result"] = result };

    private static JsonObject Error(JsonValue id, int code, string message) =>
        new()
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id,
            ["error"] = new JsonObject { ["code"] = code, ["message"] = message }
        };
}
