using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace BuildingRegulationReview.Mcp.Transport;

/// <summary>
/// The MCP Streamable HTTP transport, reduced to what a local, stateless server needs: one endpoint
/// that takes a JSON-RPC message by POST and answers with <c>application/json</c>.
/// </summary>
/// <remarks>
/// <para>
/// Built on <see cref="TcpListener"/> rather than <c>HttpListener</c>: the latter goes through
/// http.sys, which asks for a URL reservation (administrator rights) on many machines. A plain
/// socket on the loopback address needs none, and nothing outside this machine can reach it.
/// </para>
/// <para>
/// The specification's DNS-rebinding defence is enforced: a request whose <c>Host</c> is not this
/// loopback endpoint, or whose <c>Origin</c> is not a loopback page, is refused with 403 — a web
/// page in the user's browser must never be able to drive Revit. GET (the optional SSE stream) and
/// DELETE (sessions) are answered with 405, which the specification allows.
/// </para>
/// <para>
/// Loopback is shared by every session on the machine (a terminal server, another user's
/// processes, a local dev server's pages), so when a bearer token is given every request must
/// carry it in <c>Authorization</c>; anything else is answered 401 before the body is looked at.
/// </para>
/// </remarks>
public sealed class McpHttpListener : IDisposable
{
    public const int MaxBodyBytes = 8 * 1024 * 1024;
    private const int MaxHeaderBytes = 32 * 1024;
    private const int MaxConnections = 8;
    private const char ByteOrderMark = (char)0xFEFF;
    private static readonly TimeSpan ReadTimeout = TimeSpan.FromSeconds(30);

    private readonly Func<string, CancellationToken, string?> _handler;
    private readonly int _requestedPort;
    private readonly string _path;
    private readonly string? _bearerToken;
    private readonly SemaphoreSlim _connections = new(MaxConnections, MaxConnections);
    private readonly object _gate = new();
    private TcpListener? _listener;
    private CancellationTokenSource? _stopping;

    /// <param name="handler">Takes the body of a POST and returns the JSON to answer, or null for 202 Accepted.</param>
    /// <param name="port">The loopback port; 0 picks a free one (tests).</param>
    /// <param name="bearerToken">When set, every request must send <c>Authorization: Bearer</c> with it.</param>
    public McpHttpListener(Func<string, CancellationToken, string?> handler, int port, string path = "/mcp", string? bearerToken = null)
    {
        _handler = handler ?? throw new ArgumentNullException(nameof(handler));
        if (port < 0 || port > 65535) throw new ArgumentOutOfRangeException(nameof(port));
        if (string.IsNullOrWhiteSpace(path) || path[0] != '/') throw new ArgumentException("The path must start with '/'.", nameof(path));
        _requestedPort = port;
        _path = path.TrimEnd('/');
        _bearerToken = string.IsNullOrWhiteSpace(bearerToken) ? null : bearerToken!.Trim();
    }

    public bool IsRunning
    {
        get
        {
            lock (_gate) return _listener is not null;
        }
    }

    /// <summary>The port actually bound; the requested one until <see cref="Start"/>.</summary>
    public int Port { get; private set; }

    public string Endpoint => $"http://127.0.0.1:{Port}{_path}";

    /// <summary>Binds the loopback port and starts accepting. Throws <see cref="SocketException"/> when the port is taken.</summary>
    public void Start()
    {
        lock (_gate)
        {
            if (_listener is not null) return;
            // Exclusive, so no other process can bind the same port alongside us and take requests.
            var listener = new TcpListener(IPAddress.Loopback, _requestedPort) { ExclusiveAddressUse = true };
            listener.Start();
            Port = ((IPEndPoint)listener.LocalEndpoint).Port;
            _listener = listener;
            _stopping = new CancellationTokenSource();
            var token = _stopping.Token;
            Task.Run(() => AcceptLoop(listener, token));
        }
    }

    public void Stop()
    {
        lock (_gate)
        {
            if (_listener is null) return;
            _stopping!.Cancel();
            _listener.Stop();
            _listener = null;
            _stopping.Dispose();
            _stopping = null;
        }
    }

    public void Dispose() => Stop();

    private async Task AcceptLoop(TcpListener listener, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await listener.AcceptTcpClientAsync().ConfigureAwait(false);
            }
            catch (Exception) when (token.IsCancellationRequested)
            {
                return;
            }
            catch (SocketException)
            {
                continue;
            }
            catch (Exception)
            {
                // The listener broke underneath us. Say so through IsRunning rather than keep showing
                // 「執行中」 while nothing is accepted any more.
                lock (_gate)
                {
                    if (ReferenceEquals(_listener, listener)) _listener = null;
                }
                return;
            }

            if (!_connections.Wait(0))
            {
                // Only local processes can get here; one flooding us should not starve Revit of threads.
                _ = Task.Run(() => Refuse(client));
                continue;
            }

            _ = Task.Run(() =>
            {
                try
                {
                    Serve(client, token);
                }
                finally
                {
                    _connections.Release();
                }
            });
        }
    }

    private static void Refuse(TcpClient client)
    {
        using (client)
        {
            try
            {
                HttpResponse.Text(503, "Service Unavailable", "Too many connections.").WriteTo(client.GetStream());
            }
            catch (Exception)
            {
            }
        }
    }

    private void Serve(TcpClient client, CancellationToken token)
    {
        using (client)
        {
            try
            {
                client.ReceiveTimeout = (int)ReadTimeout.TotalMilliseconds;
                var stream = client.GetStream();
                var response = Respond(stream, token);
                response.WriteTo(stream);
            }
            catch (IOException)
            {
                // The client went away or stalled; there is nobody left to answer.
            }
            catch (SocketException)
            {
            }
            catch (ObjectDisposedException)
            {
            }
        }
    }

    private HttpResponse Respond(Stream stream, CancellationToken token)
    {
        var request = HttpRequest.Read(stream);
        if (request is null) return HttpResponse.Text(400, "Bad Request", "Malformed HTTP request.");
        if (request.TooLarge) return HttpResponse.Text(413, "Payload Too Large", "The request body is too large.");

        var target = request.Target;
        var query = target.IndexOf('?');
        if (query >= 0) target = target.Substring(0, query);
        if (!string.Equals(target.TrimEnd('/'), _path, StringComparison.Ordinal))
            return HttpResponse.Text(404, "Not Found", $"The MCP endpoint is {_path}.");

        if (!IsLoopbackHost(request.Header("Host")))
            return HttpResponse.Text(403, "Forbidden", "Invalid Host header.");
        if (!IsLoopbackOrigin(request.Header("Origin")))
            return HttpResponse.Text(403, "Forbidden", "Requests from other origins are refused.");
        if (!IsAuthorized(request.Header("Authorization")))
            return HttpResponse.Text(401, "Unauthorized", "Missing or wrong bearer token.", ("WWW-Authenticate", "Bearer"));

        if (!string.Equals(request.Method, "POST", StringComparison.Ordinal))
            return HttpResponse.Text(405, "Method Not Allowed", "Only POST is supported.", ("Allow", "POST"));

        if (request.Header("Transfer-Encoding") is not null)
            return HttpResponse.Text(411, "Length Required", "Send the body with Content-Length.");

        string? answer;
        try
        {
            // A BOM is not JSON, but some clients send one; it is not worth a parse error.
            answer = _handler(request.Body.TrimStart(ByteOrderMark), token);
        }
        catch (Exception exception)
        {
            return HttpResponse.Text(500, "Internal Server Error", exception.Message);
        }

        return answer is null
            ? HttpResponse.Empty(202, "Accepted")
            : HttpResponse.Json(answer);
    }

    private bool IsLoopbackHost(string? host)
    {
        if (string.IsNullOrWhiteSpace(host)) return false;
        var value = host!.Trim().ToLowerInvariant();
        var port = Port.ToString(System.Globalization.CultureInfo.InvariantCulture);
        foreach (var name in new[] { "127.0.0.1", "localhost" })
            if (value == name || value == name + ":" + port) return true;
        return false;
    }

    private bool IsAuthorized(string? authorization)
    {
        if (_bearerToken is null) return true;
        const string scheme = "Bearer ";
        if (authorization is null || !authorization.StartsWith(scheme, StringComparison.OrdinalIgnoreCase)) return false;
        return FixedTimeEquals(authorization.Substring(scheme.Length).Trim(), _bearerToken);
    }

    /// <summary>Compares without returning early, so the time taken says nothing about how much of the token matched.</summary>
    private static bool FixedTimeEquals(string a, string b)
    {
        var difference = a.Length ^ b.Length;
        for (var i = 0; i < Math.Min(a.Length, b.Length); i++) difference |= a[i] ^ b[i];
        return difference == 0;
    }

    private static bool IsLoopbackOrigin(string? origin)
    {
        if (origin is null) return true;
        if (!Uri.TryCreate(origin.Trim(), UriKind.Absolute, out var uri)) return false;
        return uri.IsLoopback;
    }

    private sealed class HttpRequest
    {
        private readonly Dictionary<string, string> _headers;

        private HttpRequest(string method, string target, Dictionary<string, string> headers, string body, bool tooLarge)
        {
            Method = method;
            Target = target;
            _headers = headers;
            Body = body;
            TooLarge = tooLarge;
        }

        public string Method { get; }
        public string Target { get; }
        public string Body { get; }
        public bool TooLarge { get; }

        public string? Header(string name) => _headers.TryGetValue(name, out var value) ? value : null;

        public static HttpRequest? Read(Stream stream)
        {
            var head = ReadHead(stream, out var leftover);
            if (head is null) return null;

            var lines = head.Split(new[] { "\r\n" }, StringSplitOptions.None);
            var requestLine = lines[0].Split(' ');
            if (requestLine.Length != 3 || !requestLine[2].StartsWith("HTTP/1.", StringComparison.Ordinal)) return null;

            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 1; i < lines.Length; i++)
            {
                if (lines[i].Length == 0) continue;
                var colon = lines[i].IndexOf(':');
                if (colon <= 0) return null;
                headers[lines[i].Substring(0, colon).Trim()] = lines[i].Substring(colon + 1).Trim();
            }

            var length = 0;
            if (headers.TryGetValue("Content-Length", out var lengthText) &&
                (!int.TryParse(lengthText, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out length) || length < 0))
                return null;

            if (length > MaxBodyBytes) return new HttpRequest(requestLine[0], requestLine[1], headers, string.Empty, tooLarge: true);

            var body = new byte[length];
            var copied = Math.Min(leftover.Length, length);
            Array.Copy(leftover, body, copied);
            var read = copied;
            while (read < length)
            {
                var n = stream.Read(body, read, length - read);
                if (n == 0) return null;
                read += n;
            }

            return new HttpRequest(requestLine[0], requestLine[1], headers, Encoding.UTF8.GetString(body), tooLarge: false);
        }

        /// <summary>Reads up to the blank line; whatever came with it belongs to the body.</summary>
        private static string? ReadHead(Stream stream, out byte[] leftover)
        {
            leftover = System.Array.Empty<byte>();
            var buffer = new byte[4096];
            var head = new MemoryStream();
            while (head.Length < MaxHeaderBytes)
            {
                var n = stream.Read(buffer, 0, buffer.Length);
                if (n == 0) return null;
                head.Write(buffer, 0, n);

                var bytes = head.ToArray();
                var end = IndexOfBlankLine(bytes);
                if (end < 0) continue;

                leftover = new byte[bytes.Length - end - 4];
                Array.Copy(bytes, end + 4, leftover, 0, leftover.Length);
                return Encoding.ASCII.GetString(bytes, 0, end);
            }
            return null;
        }

        private static int IndexOfBlankLine(byte[] bytes)
        {
            for (var i = 0; i + 3 < bytes.Length; i++)
                if (bytes[i] == '\r' && bytes[i + 1] == '\n' && bytes[i + 2] == '\r' && bytes[i + 3] == '\n') return i;
            return -1;
        }
    }

    private sealed class HttpResponse
    {
        private readonly int _status;
        private readonly string _reason;
        private readonly string? _contentType;
        private readonly byte[] _body;
        private readonly (string Name, string Value)[] _headers;

        private HttpResponse(int status, string reason, string? contentType, byte[] body, (string, string)[] headers)
        {
            _status = status;
            _reason = reason;
            _contentType = contentType;
            _body = body;
            _headers = headers;
        }

        public static HttpResponse Json(string json) =>
            new(200, "OK", "application/json; charset=utf-8", Encoding.UTF8.GetBytes(json), System.Array.Empty<(string, string)>());

        public static HttpResponse Text(int status, string reason, string text, params (string, string)[] headers) =>
            new(status, reason, "text/plain; charset=utf-8", Encoding.UTF8.GetBytes(text), headers);

        public static HttpResponse Empty(int status, string reason) =>
            new(status, reason, null, System.Array.Empty<byte>(), System.Array.Empty<(string, string)>());

        public void WriteTo(Stream stream)
        {
            var head = new StringBuilder();
            head.Append("HTTP/1.1 ").Append(_status).Append(' ').Append(_reason).Append("\r\n");
            if (_contentType is not null) head.Append("Content-Type: ").Append(_contentType).Append("\r\n");
            head.Append("Content-Length: ").Append(_body.Length).Append("\r\n");
            head.Append("Cache-Control: no-store\r\n");
            head.Append("Connection: close\r\n");
            foreach (var (name, value) in _headers) head.Append(name).Append(": ").Append(value).Append("\r\n");
            head.Append("\r\n");

            var headBytes = Encoding.ASCII.GetBytes(head.ToString());
            stream.Write(headBytes, 0, headBytes.Length);
            stream.Write(_body, 0, _body.Length);
            stream.Flush();
        }
    }
}
