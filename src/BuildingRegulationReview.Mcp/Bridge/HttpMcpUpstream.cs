using System;
using System.IO;
using System.Net;
using System.Text;

namespace BuildingRegulationReview.Mcp.Bridge;

public enum McpUpstreamStatus
{
    /// <summary>The server answered with a JSON-RPC message.</summary>
    Answered,

    /// <summary>The server accepted a notification and has nothing to say (202).</summary>
    NoContent,

    /// <summary>Nothing listens there: Revit is closed, or its MCP service is switched off.</summary>
    Unreachable,

    /// <summary>Something listens but refused the token — usually an endpoint file left by an earlier Revit.</summary>
    Unauthorized,

    /// <summary>It answered with anything else, or did not finish in time.</summary>
    Failed
}

public sealed class McpUpstreamReply
{
    private McpUpstreamReply(McpUpstreamStatus status, string? body, string? detail)
    {
        Status = status;
        Body = body;
        Detail = detail;
    }

    public McpUpstreamStatus Status { get; }

    /// <summary>The JSON the server sent, when <see cref="Status"/> is <see cref="McpUpstreamStatus.Answered"/>.</summary>
    public string? Body { get; }

    /// <summary>Why it did not work, in words for the agent.</summary>
    public string? Detail { get; }

    public static McpUpstreamReply Answered(string body) => new(McpUpstreamStatus.Answered, body, null);
    public static McpUpstreamReply NoContent() => new(McpUpstreamStatus.NoContent, null, null);
    public static McpUpstreamReply Unreachable(string detail) => new(McpUpstreamStatus.Unreachable, null, detail);
    public static McpUpstreamReply Unauthorized(string detail) => new(McpUpstreamStatus.Unauthorized, null, detail);
    public static McpUpstreamReply Failed(string detail) => new(McpUpstreamStatus.Failed, null, detail);
}

/// <summary>Sends one JSON-RPC message to the add-in's HTTP endpoint.</summary>
public interface IMcpUpstream
{
    McpUpstreamReply Post(McpEndpoint endpoint, string body, TimeSpan timeout);
}

/// <summary>
/// <see cref="IMcpUpstream"/> over plain HTTP, the transport <see cref="Transport.McpHttpListener"/>
/// speaks. <see cref="HttpWebRequest"/> rather than HttpClient, so the net48 bridge needs no extra
/// assembly.
/// </summary>
public sealed class HttpMcpUpstream : IMcpUpstream
{
    public McpUpstreamReply Post(McpEndpoint endpoint, string body, TimeSpan timeout)
    {
        if (endpoint is null) throw new ArgumentNullException(nameof(endpoint));
        var milliseconds = (int)Math.Min(int.MaxValue, Math.Max(1, timeout.TotalMilliseconds));
        try
        {
            var request = (HttpWebRequest)WebRequest.Create(endpoint.Url);
            request.Method = "POST";
            request.ContentType = "application/json";
            request.Accept = "application/json, text/event-stream";
            request.Headers[HttpRequestHeader.Authorization] = "Bearer " + endpoint.Token;
            // A system-wide proxy must never see loopback traffic, let alone the token.
            request.Proxy = null;
            request.Timeout = milliseconds;
            request.ReadWriteTimeout = milliseconds;
            request.KeepAlive = false;

            var bytes = Encoding.UTF8.GetBytes(body ?? string.Empty);
            request.ContentLength = bytes.Length;
            using (var stream = request.GetRequestStream()) stream.Write(bytes, 0, bytes.Length);

            using var response = (HttpWebResponse)request.GetResponse();
            if (response.StatusCode == HttpStatusCode.Accepted || response.StatusCode == HttpStatusCode.NoContent)
                return McpUpstreamReply.NoContent();
            using var reader = new StreamReader(response.GetResponseStream()!, Encoding.UTF8);
            var text = reader.ReadToEnd();
            return string.IsNullOrWhiteSpace(text) ? McpUpstreamReply.NoContent() : McpUpstreamReply.Answered(text);
        }
        catch (WebException exception)
        {
            if (exception.Response is HttpWebResponse failed)
            {
                using (failed)
                {
                    return failed.StatusCode == HttpStatusCode.Unauthorized
                        ? McpUpstreamReply.Unauthorized("權杖不符（HTTP 401）")
                        : McpUpstreamReply.Failed($"HTTP {(int)failed.StatusCode} {failed.StatusDescription}");
                }
            }
            return exception.Status == WebExceptionStatus.Timeout
                ? McpUpstreamReply.Failed($"等了 {timeout.TotalSeconds:0} 秒仍沒有回應")
                : McpUpstreamReply.Unreachable($"{endpoint.Url} 沒有回應（{exception.Status}）");
        }
        catch (IOException exception)
        {
            return McpUpstreamReply.Unreachable($"{endpoint.Url} 連線中斷（{exception.Message}）");
        }
    }
}
