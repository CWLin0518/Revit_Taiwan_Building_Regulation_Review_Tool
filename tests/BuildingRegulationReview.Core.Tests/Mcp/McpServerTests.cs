using System;
using System.Linq;
using System.Threading;
using BuildingRegulationReview.Mcp.Json;
using BuildingRegulationReview.Mcp.Protocol;
using BuildingRegulationReview.Mcp.Tools;
using Xunit;

namespace BuildingRegulationReview.Core.Tests.Mcp;

public sealed class McpServerTests
{
    [Fact]
    public void Initialize_EchoesASupportedVersionAndAnnouncesTools()
    {
        var response = Call(NewServer(), "initialize", new JsonObject { ["protocolVersion"] = "2025-06-18" });

        var result = response["result"]!.AsObject()!;
        Assert.Equal("2025-06-18", result["protocolVersion"]!.AsString());
        Assert.NotNull(result["capabilities"]!.AsObject()!["tools"]);
        Assert.Equal("test-server", result["serverInfo"]!.AsObject()!["name"]!.AsString());
        Assert.Equal("如何使用", result["instructions"]!.AsString());
    }

    [Fact]
    public void Initialize_AnswersAnUnknownVersionWithTheNewest()
    {
        var response = Call(NewServer(), "initialize", new JsonObject { ["protocolVersion"] = "1999-01-01" });

        Assert.Equal(McpServer.SupportedProtocolVersions[0], response["result"]!.AsObject()!["protocolVersion"]!.AsString());
    }

    [Fact]
    public void ToolsList_DescribesEveryToolWithItsSchemaAndAnnotations()
    {
        var response = Call(NewServer(), "tools/list");

        var tools = response["result"]!.AsObject()!["tools"]!.AsArray()!;
        var echo = tools.Single(t => t.AsObject()!["name"]!.AsString() == "echo").AsObject()!;
        Assert.Equal("object", echo["inputSchema"]!.AsObject()!["type"]!.AsString());
        Assert.True(echo["annotations"]!.AsObject()!["readOnlyHint"]!.AsBoolean());
        Assert.False(echo["annotations"]!.AsObject()!["openWorldHint"]!.AsBoolean());
    }

    [Fact]
    public void ToolsCall_ReturnsStructuredContentAndTheSameAsText()
    {
        var response = Call(NewServer(), "tools/call", new JsonObject
        {
            ["name"] = "echo",
            ["arguments"] = new JsonObject { ["text"] = "區劃" }
        });

        var result = response["result"]!.AsObject()!;
        Assert.False(result["isError"]!.AsBoolean());
        Assert.Equal("區劃", result["structuredContent"]!.AsObject()!["echo"]!.AsString());
        var text = result["content"]!.AsArray()![0].AsObject()!["text"]!.AsString()!;
        Assert.Contains("{\"echo\":\"區劃\"}", text);
    }

    [Fact]
    public void ToolsCall_TurnsAToolExceptionIntoAToolErrorTheAgentCanRead()
    {
        var response = Call(NewServer(), "tools/call", new JsonObject { ["name"] = "echo", ["arguments"] = new JsonObject() });

        var result = response["result"]!.AsObject()!;
        Assert.True(result["isError"]!.AsBoolean());
        Assert.Contains("text", result["structuredContent"]!.AsObject()!["error"]!.AsString());
        Assert.Null(response["error"]);
    }

    [Fact]
    public void ToolsCall_ReportsAnUnexpectedExceptionAsAToolErrorAndRemembersIt()
    {
        var server = NewServer();
        var response = Call(server, "tools/call", new JsonObject { ["name"] = "broken" });

        Assert.True(response["result"]!.AsObject()!["isError"]!.AsBoolean());
        var record = Assert.Single(server.RecentCalls);
        Assert.Equal("broken", record.Tool);
        Assert.True(record.IsError);
    }

    [Fact]
    public void ToolsCall_UnknownToolIsAProtocolError()
    {
        var response = Call(NewServer(), "tools/call", new JsonObject { ["name"] = "nope" });

        Assert.Equal(McpServer.InvalidParams, response["error"]!.AsObject()!["code"]!.AsNumber());
    }

    [Fact]
    public void UnknownMethod_IsMethodNotFound()
    {
        var response = Call(NewServer(), "resources/list");

        Assert.Equal(McpServer.MethodNotFound, response["error"]!.AsObject()!["code"]!.AsNumber());
    }

    [Fact]
    public void Notification_GetsNoAnswer()
    {
        Assert.Null(NewServer().Handle("{\"jsonrpc\":\"2.0\",\"method\":\"notifications/initialized\"}"));
    }

    [Fact]
    public void MalformedJson_IsAParseError()
    {
        var response = JsonValue.Parse(NewServer().Handle("{oops")!).AsObject()!;

        Assert.Equal(McpServer.ParseError, response["error"]!.AsObject()!["code"]!.AsNumber());
        Assert.True(response["id"]!.IsNull);
    }

    [Fact]
    public void Batch_AnswersEachRequestAndSkipsNotifications()
    {
        var answer = NewServer().Handle(
            "[{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"ping\"},{\"jsonrpc\":\"2.0\",\"method\":\"notifications/x\"},{\"jsonrpc\":\"2.0\",\"id\":\"b\",\"method\":\"ping\"}]");

        var array = JsonValue.Parse(answer!).AsArray()!;
        Assert.Equal(2, array.Count);
        Assert.Equal("b", array[1].AsObject()!["id"]!.AsString());
    }

    [Fact]
    public void Registry_RefusesDuplicateAndInvalidNames()
    {
        var registry = new McpToolRegistry().Add(new EchoTool());

        Assert.Throws<ArgumentException>(() => registry.Add(new EchoTool()));
        Assert.Throws<ArgumentException>(() => registry.Add(new BrokenTool("fire review")));
    }

    [Fact]
    public void Arguments_NameTheArgumentThatIsWrong()
    {
        var arguments = new McpToolArguments(new JsonObject { ["limit"] = 1.5, ["id"] = "x", ["flag"] = "yes" });

        Assert.Contains("limit", Assert.Throws<McpToolException>(() => arguments.OptionalInt("limit", 0)).Message);
        Assert.Contains("id", Assert.Throws<McpToolException>(() => arguments.RequireGuid("id")).Message);
        Assert.Contains("flag", Assert.Throws<McpToolException>(() => arguments.OptionalBool("flag", false)).Message);
        Assert.Contains("missing", Assert.Throws<McpToolException>(() => arguments.RequireString("missing")).Message);
    }

    [Fact]
    public void Arguments_TellAnExplicitNullFromAMissingValue()
    {
        var arguments = new McpToolArguments(new JsonObject { ["clear"] = JsonValue.Null });

        Assert.True(arguments.Has("clear"));
        Assert.Null(arguments.OptionalString("clear"));
        Assert.False(arguments.Has("absent"));
    }

    private static McpServer NewServer() =>
        new(new McpServerOptions("test-server", "1.0.0", instructions: "如何使用"),
            new McpToolRegistry().Add(new EchoTool()).Add(new BrokenTool("broken")));

    private static JsonObject Call(McpServer server, string method, JsonObject? parameters = null)
    {
        var request = new JsonObject { ["jsonrpc"] = "2.0", ["id"] = 1, ["method"] = method };
        if (parameters is not null) request["params"] = parameters;
        return JsonValue.Parse(server.Handle(request.ToString())!).AsObject()!;
    }

    private sealed class EchoTool : IMcpTool
    {
        public string Name => "echo";
        public string Title => "Echo";
        public string Description => "Returns its text.";
        public JsonObject InputSchema => JsonSchema.Object(new JsonObject { ["text"] = JsonSchema.String("text") }, "text");
        public McpToolAnnotations Annotations => McpToolAnnotations.ReadOnly;

        public McpToolResult Call(McpToolArguments arguments, CancellationToken cancellation) =>
            McpToolResult.Success(new JsonObject { ["echo"] = arguments.RequireString("text") });
    }

    private sealed class BrokenTool : IMcpTool
    {
        public BrokenTool(string name) => Name = name;

        public string Name { get; }
        public string Title => "Broken";
        public string Description => "Always throws.";
        public JsonObject InputSchema => JsonSchema.Empty();
        public McpToolAnnotations Annotations => McpToolAnnotations.Updates;

        public McpToolResult Call(McpToolArguments arguments, CancellationToken cancellation) =>
            throw new InvalidOperationException("boom");
    }
}
