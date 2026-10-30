using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BuildingRegulationReview.Mcp.Bridge;
using BuildingRegulationReview.Mcp.Json;
using BuildingRegulationReview.Mcp.Transport;
using Xunit;

namespace BuildingRegulationReview.Core.Tests.Mcp;

public sealed class McpStdioBridgeTests : IDisposable
{
    private const string RevitTools = "[{\"name\":\"revit_status\"},{\"name\":\"fire_review_run\"}]";

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "brr-bridge-" + Guid.NewGuid().ToString("N"));
    private readonly List<string> _sent = new();
    private readonly FakeUpstream _upstream = new();
    private McpEndpoint? _endpoint;

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }

    private string CachePath => Path.Combine(_directory, McpEndpointFile.ToolCacheFileName);

    private McpStdioBridge Bridge() => new(() => _endpoint, _upstream, CachePath, line =>
    {
        lock (_sent) _sent.Add(line);
    });

    private JsonObject Last() => (JsonObject)JsonValue.Parse(_sent.Last());

    private static string Request(int id, string method, string parameters = "{}") =>
        $"{{\"jsonrpc\":\"2.0\",\"id\":{id},\"method\":\"{method}\",\"params\":{parameters}}}";

    private static McpEndpoint Endpoint(string token = "t1", int port = 8970) =>
        new($"http://127.0.0.1:{port}/mcp", token, 42, DateTime.UtcNow);

    // --- 沒開 Revit 也要連得上 ---------------------------------------------------------------------

    [Fact]
    public void Initialize_is_answered_even_when_Revit_is_not_running()
    {
        var bridge = Bridge();

        bridge.Handle(Request(1, "initialize", "{\"protocolVersion\":\"2025-06-18\"}"));

        var result = (JsonObject)Last()["result"]!;
        Assert.Equal("2025-06-18", result["protocolVersion"]!.AsString());
        Assert.Equal(true, ((JsonObject)((JsonObject)result["capabilities"]!)["tools"]!)["listChanged"]!.AsBoolean());
        Assert.Equal(McpStdioBridge.ServerName, ((JsonObject)result["serverInfo"]!)["name"]!.AsString());
        Assert.Contains("Revit", result["instructions"]!.AsString());
        Assert.Empty(_upstream.Bodies);
    }

    [Fact]
    public void Without_Revit_or_a_cache_the_tool_list_is_the_one_tool_that_explains()
    {
        var bridge = Bridge();

        bridge.Handle(Request(1, "tools/list"));

        var tools = (JsonArray)((JsonObject)Last()["result"]!)["tools"]!;
        Assert.Equal(McpStdioBridge.OfflineToolName, ((JsonObject)Assert.Single(tools))["name"]!.AsString());
    }

    [Fact]
    public void A_call_while_Revit_is_away_is_a_tool_error_that_says_what_to_do()
    {
        var bridge = Bridge();

        bridge.Handle(Request(7, "tools/call", "{\"name\":\"revit_status\",\"arguments\":{}}"));

        var reply = Last();
        Assert.Equal(7, reply["id"]!.AsNumber());
        var result = (JsonObject)reply["result"]!;
        Assert.Equal(true, result["isError"]!.AsBoolean());
        var text = ((JsonObject)((JsonArray)result["content"]!)[0])["text"]!.AsString()!;
        Assert.Contains("MCP 服務", text);
        Assert.Contains("不必重新啟動代理程式", text);
    }

    // --- Revit 開著時照轉 ---------------------------------------------------------------------------

    [Fact]
    public void With_Revit_up_the_tool_list_is_Revits_and_is_cached_for_the_next_session()
    {
        _endpoint = Endpoint();
        _upstream.ToolsJson = RevitTools;

        Bridge().Handle(Request(1, "tools/list"));

        Assert.Equal(RevitTools, ((JsonObject)Last()["result"]!)["tools"]!.ToString());

        // 下一個工作階段先於 Revit 啟動：仍看得到真正的工具清單。
        _endpoint = null;
        _sent.Clear();
        Bridge().Handle(Request(2, "tools/list"));
        Assert.Equal(RevitTools, ((JsonObject)Last()["result"]!)["tools"]!.ToString());
    }

    [Fact]
    public void A_call_is_forwarded_with_the_announced_token_and_the_answer_passed_through()
    {
        _endpoint = Endpoint("secret");
        _upstream.Answer = body => "{\"jsonrpc\":\"2.0\",\"id\":3,\"result\":{\"content\":[],\"isError\":false}}";

        Bridge().Handle(Request(3, "tools/call", "{\"name\":\"fire_review_list_packages\"}"));

        Assert.Equal("secret", _upstream.Tokens.Single());
        Assert.Contains("fire_review_list_packages", _upstream.Bodies.Single());
        Assert.Equal(McpStdioBridge.ToolCallTimeout, _upstream.Timeouts.Single());
        Assert.Equal(false, ((JsonObject)Last()["result"]!)["isError"]!.AsBoolean());
    }

    [Fact]
    public void An_answer_spread_over_several_lines_goes_out_as_one()
    {
        _endpoint = Endpoint();
        _upstream.Answer = _ => "{\"jsonrpc\":\"2.0\",\r\n \"id\":3,\n\"result\":{}}";

        Bridge().Handle(Request(3, "tools/call", "{\"name\":\"x\"}"));

        Assert.DoesNotContain('\n', _sent.Single());
        Assert.Equal(3, Last()["id"]!.AsNumber());
    }

    [Fact]
    public void Notifications_and_ping_never_reach_Revit()
    {
        _endpoint = Endpoint();
        var bridge = Bridge();

        bridge.Handle("{\"jsonrpc\":\"2.0\",\"method\":\"notifications/initialized\"}");
        bridge.Handle(Request(1, "ping"));

        Assert.Empty(_upstream.Bodies);
        Assert.Equal("{}", Last()["result"]!.ToString());
    }

    // --- Revit 重開、換權杖 --------------------------------------------------------------------------

    [Fact]
    public void A_rejected_token_is_retried_once_with_a_newer_announcement()
    {
        var stale = Endpoint("old");
        var fresh = Endpoint("new");
        var reads = 0;
        _upstream.Answer = body => "{\"jsonrpc\":\"2.0\",\"id\":1,\"result\":{\"isError\":false}}";
        _upstream.Reject = token => token == "old";
        var bridge = new McpStdioBridge(() => ++reads == 1 ? stale : fresh, _upstream, CachePath, _sent.Add);

        bridge.Handle(Request(1, "tools/call", "{\"name\":\"revit_status\"}"));

        Assert.Equal(new[] { "old", "new" }, _upstream.Tokens);
        Assert.Equal(false, ((JsonObject)Last()["result"]!)["isError"]!.AsBoolean());
    }

    [Fact]
    public void A_rejected_token_with_nothing_newer_says_to_restart_the_service()
    {
        _endpoint = Endpoint("old");
        _upstream.Reject = _ => true;

        Bridge().Handle(Request(1, "tools/call", "{\"name\":\"revit_status\"}"));

        var text = ((JsonObject)((JsonArray)((JsonObject)Last()["result"]!)["content"]!)[0])["text"]!.AsString()!;
        Assert.Contains("HTTP 401", text);
        Assert.Single(_upstream.Tokens);
    }

    [Fact]
    public void Poll_announces_the_real_tools_once_Revit_comes_up()
    {
        var bridge = Bridge();
        bridge.Handle(Request(1, "initialize"));
        bridge.Handle(Request(2, "tools/list"));
        _sent.Clear();

        bridge.Poll();
        Assert.Empty(_sent);                       // Revit 還沒開：不打擾

        _endpoint = Endpoint();
        _upstream.ToolsJson = RevitTools;
        bridge.Poll();
        Assert.Equal("notifications/tools/list_changed", Last()["method"]!.AsString());

        _sent.Clear();
        bridge.Poll();
        Assert.Empty(_sent);                       // 沒變就不再通知
    }

    [Fact]
    public void Poll_says_nothing_before_the_client_has_initialized()
    {
        _endpoint = Endpoint();
        _upstream.ToolsJson = RevitTools;

        Bridge().Poll();

        Assert.Empty(_sent);
    }

    // --- 端點檔 --------------------------------------------------------------------------------------

    [Fact]
    public void The_endpoint_file_round_trips_and_only_its_writer_may_delete_it()
    {
        var path = Path.Combine(_directory, McpEndpointFile.EndpointFileName);
        McpEndpointFile.Write(path, new McpEndpoint("http://127.0.0.1:8970/mcp", "tok", 1234, DateTime.UtcNow, "1.0.0.0"));

        var read = McpEndpointFile.TryRead(path)!;
        Assert.Equal("tok", read.Token);
        Assert.Equal(1234, read.ProcessId);
        Assert.Equal("1.0.0.0", read.ServerVersion);

        McpEndpointFile.DeleteIfOwnedBy(path, 999);
        Assert.True(File.Exists(path));
        McpEndpointFile.DeleteIfOwnedBy(path, 1234);
        Assert.False(File.Exists(path));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("not json")]
    [InlineData("{\"endpoint\":\"http://127.0.0.1:8970/mcp\"}")]
    public void A_missing_or_broken_endpoint_file_reads_as_no_endpoint(string? content)
    {
        var path = Path.Combine(_directory, McpEndpointFile.EndpointFileName);
        if (content is not null)
        {
            Directory.CreateDirectory(_directory);
            File.WriteAllText(path, content);
        }

        Assert.Null(McpEndpointFile.TryRead(path));
    }

    // --- 真的走 HTTP ---------------------------------------------------------------------------------

    [Fact]
    public void Http_upstream_reaches_the_listener_and_reports_401_and_unreachable()
    {
        using var listener = new McpHttpListener((body, _) => "{\"jsonrpc\":\"2.0\",\"id\":1,\"result\":{\"ok\":\"區劃\"}}",
            port: 0, bearerToken: "right");
        listener.Start();
        var upstream = new HttpMcpUpstream();

        var ok = upstream.Post(new McpEndpoint(listener.Endpoint, "right", 1, DateTime.UtcNow), "{}", TimeSpan.FromSeconds(5));
        Assert.Equal(McpUpstreamStatus.Answered, ok.Status);
        Assert.Contains("區劃", ok.Body);

        var wrong = upstream.Post(new McpEndpoint(listener.Endpoint, "wrong", 1, DateTime.UtcNow), "{}", TimeSpan.FromSeconds(5));
        Assert.Equal(McpUpstreamStatus.Unauthorized, wrong.Status);

        var port = listener.Port;
        listener.Stop();
        var gone = upstream.Post(new McpEndpoint($"http://127.0.0.1:{port}/mcp", "right", 1, DateTime.UtcNow), "{}", TimeSpan.FromSeconds(5));
        Assert.Equal(McpUpstreamStatus.Unreachable, gone.Status);
    }

    private sealed class FakeUpstream : IMcpUpstream
    {
        public List<string> Bodies { get; } = new();
        public List<string> Tokens { get; } = new();
        public List<TimeSpan> Timeouts { get; } = new();
        public string? ToolsJson { get; set; }
        public Func<string, string>? Answer { get; set; }
        public Func<string, bool> Reject { get; set; } = _ => false;

        public McpUpstreamReply Post(McpEndpoint endpoint, string body, TimeSpan timeout)
        {
            var method = (JsonValue.Parse(body) as JsonObject)?["method"]?.AsString();
            if (method == "tools/list")
            {
                return ToolsJson is null
                    ? McpUpstreamReply.Unreachable("down")
                    : McpUpstreamReply.Answered("{\"jsonrpc\":\"2.0\",\"id\":\"bridge-tools\",\"result\":{\"tools\":" + ToolsJson + "}}");
            }

            Bodies.Add(body);
            Tokens.Add(endpoint.Token);
            Timeouts.Add(timeout);
            if (Reject(endpoint.Token)) return McpUpstreamReply.Unauthorized("權杖不符（HTTP 401）");
            return Answer is null ? McpUpstreamReply.Unreachable("down") : McpUpstreamReply.Answered(Answer(body));
        }
    }
}
