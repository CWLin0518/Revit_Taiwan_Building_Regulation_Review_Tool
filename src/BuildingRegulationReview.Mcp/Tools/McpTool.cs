using System;
using System.Collections.Generic;
using System.Threading;
using BuildingRegulationReview.Mcp.Json;

namespace BuildingRegulationReview.Mcp.Tools;

/// <summary>
/// One operation an agent can call. A tool is a thin shell over a use case the UI calls too
/// (docs/adr/0004): it reads its arguments, asks the use case, and turns the answer into JSON. It
/// never opens a window and never asks the user a question — a question becomes an argument.
/// </summary>
public interface IMcpTool
{
    /// <summary>Unique, <c>[A-Za-z0-9_-]</c>, at most 64 characters; the agent sees it prefixed by the server name.</summary>
    string Name { get; }

    string Title { get; }

    /// <summary>What the tool does, when to call it and what it changes — the agent decides from this alone.</summary>
    string Description { get; }

    /// <summary>The JSON Schema of the arguments object.</summary>
    JsonObject InputSchema { get; }

    McpToolAnnotations Annotations { get; }

    /// <summary>
    /// Runs the tool. Expected failures (bad arguments, no document, Revit busy) are thrown as
    /// <see cref="McpToolException"/> or returned through <see cref="McpToolResult.Failure"/>;
    /// both reach the agent as a tool error it can read and act on.
    /// </summary>
    McpToolResult Call(McpToolArguments arguments, CancellationToken cancellation);
}

/// <summary>A feature's tools, registered together so adding a feature never edits the server.</summary>
public interface IMcpToolModule
{
    IEnumerable<IMcpTool> Tools { get; }
}

/// <summary>The MCP tool annotations: hints for the client, e.g. whether to ask the user before a call.</summary>
public sealed class McpToolAnnotations
{
    /// <summary>Reads the model and changes nothing.</summary>
    public static readonly McpToolAnnotations ReadOnly = new(readOnly: true, destructive: false, idempotent: true);

    /// <summary>Writes to the model, but only adds or updates what the add-in owns; calling again with the same arguments changes nothing more.</summary>
    public static readonly McpToolAnnotations Updates = new(readOnly: false, destructive: false, idempotent: true);

    /// <summary>Writes to the model and may overwrite what the user entered.</summary>
    public static readonly McpToolAnnotations Overwrites = new(readOnly: false, destructive: true, idempotent: true);

    public McpToolAnnotations(bool readOnly, bool destructive, bool idempotent)
    {
        ReadOnlyHint = readOnly;
        DestructiveHint = destructive;
        IdempotentHint = idempotent;
    }

    public bool ReadOnlyHint { get; }
    public bool DestructiveHint { get; }
    public bool IdempotentHint { get; }

    internal JsonObject ToJson(string title) => new()
    {
        ["title"] = title,
        ["readOnlyHint"] = ReadOnlyHint,
        ["destructiveHint"] = DestructiveHint,
        ["idempotentHint"] = IdempotentHint,
        // Everything a tool touches is the open Revit model on this machine.
        ["openWorldHint"] = false
    };
}

/// <summary>An expected failure the agent should read and act on, rather than a bug.</summary>
public class McpToolException : Exception
{
    public McpToolException(string message, JsonObject? detail = null)
        : base(message) => Detail = detail;

    /// <summary>Anything structured that helps the agent recover, e.g. the readiness items that blocked a run.</summary>
    public JsonObject? Detail { get; }
}

/// <summary>What a tool call returns: structured data for the agent, and the same as text for clients that only read text.</summary>
public sealed class McpToolResult
{
    private McpToolResult(bool isError, string? summary, JsonObject? data)
    {
        IsError = isError;
        Summary = summary;
        Data = data;
    }

    public bool IsError { get; }

    /// <summary>One human-readable line, put in front of the JSON in the text content.</summary>
    public string? Summary { get; }

    public JsonObject? Data { get; }

    public static McpToolResult Success(JsonObject data, string? summary = null) =>
        new(false, summary, data ?? throw new ArgumentNullException(nameof(data)));

    public static McpToolResult Failure(string message, JsonObject? data = null)
    {
        if (string.IsNullOrWhiteSpace(message)) throw new ArgumentException("A failure needs a message.", nameof(message));
        var payload = new JsonObject { ["error"] = message };
        if (data is not null)
            foreach (var member in data)
                payload[member.Key] = member.Value;
        return new McpToolResult(true, message, payload);
    }

    /// <summary>The <c>CallToolResult</c> of the MCP specification.</summary>
    public JsonObject ToJson()
    {
        var json = Data is null ? string.Empty : Data.ToString();
        var text = Summary is null ? json : json.Length == 0 ? Summary : Summary + "\n" + json;
        var result = new JsonObject
        {
            ["content"] = new JsonArray { new JsonObject { ["type"] = "text", ["text"] = text } }
        };
        if (Data is not null) result["structuredContent"] = Data;
        result["isError"] = IsError;
        return result;
    }
}
