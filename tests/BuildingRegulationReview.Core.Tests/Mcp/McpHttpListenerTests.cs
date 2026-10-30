using System;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using BuildingRegulationReview.Mcp.Transport;
using Xunit;

namespace BuildingRegulationReview.Core.Tests.Mcp;

public sealed class McpHttpListenerTests : IDisposable
{
    private readonly McpHttpListener _listener;
    private readonly HttpClient _client = new();
    private string? _lastBody;

    public McpHttpListenerTests()
    {
        _listener = new McpHttpListener((body, _) =>
        {
            _lastBody = body;
            return body.Contains("\"id\"") ? "{\"ok\":\"區劃\"}" : null;
        }, port: 0);
        _listener.Start();
    }

    public void Dispose()
    {
        _client.Dispose();
        _listener.Dispose();
    }

    [Fact]
    public async Task Post_HandsTheBodyToTheHandlerAndReturnsJson()
    {
        var response = await _client.PostAsync(_listener.Endpoint, Json("{\"id\":1,\"text\":\"防火\"}"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType!.MediaType);
        Assert.Equal("{\"ok\":\"區劃\"}", await response.Content.ReadAsStringAsync());
        Assert.Equal("{\"id\":1,\"text\":\"防火\"}", _lastBody);
    }

    [Fact]
    public async Task Post_WithNothingToAnswer_IsAccepted()
    {
        var response = await _client.PostAsync(_listener.Endpoint, Json("{\"method\":\"notifications/initialized\"}"));

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
    }

    [Fact]
    public async Task Get_IsMethodNotAllowed()
    {
        var response = await _client.GetAsync(_listener.Endpoint);

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
    }

    [Fact]
    public async Task OtherPaths_AreNotFound()
    {
        var response = await _client.PostAsync($"http://127.0.0.1:{_listener.Port}/other", Json("{}"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ForeignOrigin_IsRefused()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, _listener.Endpoint) { Content = Json("{\"id\":1}") };
        request.Headers.Add("Origin", "https://evil.example");

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Null(_lastBody);
    }

    [Fact]
    public async Task LoopbackOrigin_IsAccepted()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, _listener.Endpoint) { Content = Json("{\"id\":1}") };
        request.Headers.Add("Origin", "http://localhost:3000");

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ForeignHost_IsRefused()
    {
        // What a DNS-rebinding page would send: the loopback address, under someone else's name.
        var request = new HttpRequestMessage(HttpMethod.Post, _listener.Endpoint) { Content = Json("{\"id\":1}") };
        request.Headers.Host = "attacker.example:" + _listener.Port;

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public void Start_OnATakenPort_Throws()
    {
        using var second = new McpHttpListener((_, _) => null, _listener.Port);

        Assert.Throws<SocketException>(() => second.Start());
    }

    [Fact]
    public async Task Stop_ClosesThePort()
    {
        _listener.Stop();

        await Assert.ThrowsAsync<HttpRequestException>(() => _client.PostAsync(_listener.Endpoint, Json("{}")));
        Assert.False(_listener.IsRunning);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("Bearer wrong-token")]
    [InlineData("Basic c2VjcmV0")]
    public async Task WithAToken_RequestsWithoutIt_AreUnauthorized(string? authorization)
    {
        using var secured = new McpHttpListener((_, _) => "{}", port: 0, bearerToken: "s3cret-token");
        secured.Start();
        var request = new HttpRequestMessage(HttpMethod.Post, secured.Endpoint) { Content = Json("{\"id\":1}") };
        if (authorization is not null) request.Headers.TryAddWithoutValidation("Authorization", authorization);

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("Bearer", response.Headers.WwwAuthenticate.ToString());
    }

    [Fact]
    public async Task WithAToken_TheRightBearerIsAccepted()
    {
        using var secured = new McpHttpListener((_, _) => "{}", port: 0, bearerToken: "s3cret-token");
        secured.Start();
        var request = new HttpRequestMessage(HttpMethod.Post, secured.Endpoint) { Content = Json("{\"id\":1}") };
        request.Headers.TryAddWithoutValidation("Authorization", "Bearer s3cret-token");

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Post_WithAByteOrderMark_IsHandedOnWithoutIt()
    {
        var response = await _client.PostAsync(_listener.Endpoint, Json("﻿{\"id\":1}"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("{\"id\":1}", _lastBody);
    }

    private static StringContent Json(string body) => new(body, Encoding.UTF8, "application/json");
}
