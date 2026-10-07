using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BuildingRegulationReview.Mcp.Bridge;

namespace BuildingRegulationReview.McpBridge;

/// <summary>
/// stdin/stdout wiring for <see cref="McpStdioBridge"/>: one JSON-RPC message per line each way,
/// diagnostics on stderr (stdout belongs to the protocol).
/// </summary>
internal static class Program
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(3);

    private static int Main()
    {
        var utf8 = new UTF8Encoding(false);
        var input = new StreamReader(Console.OpenStandardInput(), utf8);
        var output = new StreamWriter(Console.OpenStandardOutput(), utf8) { AutoFlush = true, NewLine = "\n" };
        var outputGate = new object();

        void Send(string message)
        {
            lock (outputGate) output.WriteLine(message);
        }

        var bridge = new McpStdioBridge(
            () => McpEndpointFile.TryRead(McpEndpointFile.DefaultEndpointPath),
            new HttpMcpUpstream(),
            McpEndpointFile.DefaultToolCachePath,
            Send);

        using var poller = new Timer(_ => Guard(bridge.Poll), null, PollInterval, PollInterval);

        string? line;
        while ((line = input.ReadLine()) is not null)
        {
            var message = line;
            // Each request on its own: a ten-minute fire_review_run must not hold up a ping.
            Task.Run(() => Guard(() => bridge.Handle(message)));
        }
        return 0;
    }

    private static void Guard(Action action)
    {
        try
        {
            action();
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine("[building-regulation-review bridge] " + exception);
        }
    }
}
