using System;
using System.IO;
using System.Text;
using BuildingRegulationReview.Mcp.Json;

namespace BuildingRegulationReview.Mcp.Bridge;

/// <summary>Where a running add-in listens and the token it accepts, as the add-in announced it.</summary>
public sealed class McpEndpoint
{
    public McpEndpoint(string url, string token, int processId, DateTime startedAtUtc, string? serverVersion = null)
    {
        if (string.IsNullOrWhiteSpace(url)) throw new ArgumentException("An endpoint needs a URL.", nameof(url));
        if (string.IsNullOrWhiteSpace(token)) throw new ArgumentException("An endpoint needs a token.", nameof(token));
        Url = url;
        Token = token;
        ProcessId = processId;
        StartedAtUtc = startedAtUtc;
        ServerVersion = serverVersion;
    }

    public string Url { get; }
    public string Token { get; }

    /// <summary>The Revit process that wrote the file; only that process may delete it again.</summary>
    public int ProcessId { get; }

    public DateTime StartedAtUtc { get; }
    public string? ServerVersion { get; }

    /// <summary>Two announcements are the same server when both the URL and the token match.</summary>
    public bool SameServerAs(McpEndpoint? other) =>
        other is not null && other.Url == Url && other.Token == Token;
}

/// <summary>
/// The file through which the add-in tells the stdio bridge where it listens (docs/mcp-server.md §3).
/// The add-in writes it when its server starts and deletes it when the server stops; the bridge reads
/// it before every request, so a Revit restart — new random token, maybe another port — needs no
/// change to the agent's configuration.
/// </summary>
/// <remarks>
/// It lives under the user's <c>%LOCALAPPDATA%</c>, which other Windows accounts cannot read. Any
/// process of this user can, which is no wider than before: that user could already read the token
/// from the dialog, the clipboard or the agent's configuration file.
/// </remarks>
public static class McpEndpointFile
{
    public const string EndpointFileName = "endpoint.json";
    public const string ToolCacheFileName = "tools-cache.json";

    /// <summary><c>%LOCALAPPDATA%\BuildingRegulationReview\Mcp</c>.</summary>
    public static string DefaultDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BuildingRegulationReview", "Mcp");

    public static string DefaultEndpointPath => Path.Combine(DefaultDirectory, EndpointFileName);

    public static string DefaultToolCachePath => Path.Combine(DefaultDirectory, ToolCacheFileName);

    public static void Write(string path, McpEndpoint endpoint)
    {
        if (endpoint is null) throw new ArgumentNullException(nameof(endpoint));
        var json = new JsonObject
        {
            ["endpoint"] = endpoint.Url,
            ["token"] = endpoint.Token,
            ["processId"] = endpoint.ProcessId,
            ["startedAtUtc"] = JsonValue.Of(endpoint.StartedAtUtc),
            ["serverVersion"] = endpoint.ServerVersion
        };
        WriteAtomically(path, json.ToString());
    }

    /// <summary>The announced endpoint, or null when there is none or the file cannot be read.</summary>
    public static McpEndpoint? TryRead(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            if (JsonValue.Parse(File.ReadAllText(path, Encoding.UTF8)) is not JsonObject json) return null;
            var url = json["endpoint"]?.AsString();
            var token = json["token"]?.AsString();
            if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(token)) return null;
            var processId = (int)(json["processId"]?.AsNumber() ?? 0);
            var started = DateTime.TryParse(json["startedAtUtc"]?.AsString(), null,
                System.Globalization.DateTimeStyles.RoundtripKind, out var at) ? at : DateTime.MinValue;
            return new McpEndpoint(url!, token!, processId, started, json["serverVersion"]?.AsString());
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonParseException)
        {
            return null;
        }
    }

    /// <summary>
    /// Deletes the file only if <paramref name="processId"/> wrote it: a second Revit that failed to
    /// start its own server must not take away the first one's announcement when it shuts down.
    /// </summary>
    public static void DeleteIfOwnedBy(string path, int processId)
    {
        var current = TryRead(path);
        if (current is null || current.ProcessId != processId) return;
        try
        {
            File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Left behind, it only makes the bridge try a dead port and report Revit as not running.
        }
    }

    /// <summary>Writes to a temporary file first, so a reader never sees half a file.</summary>
    internal static void WriteAtomically(string path, string text)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllText(temporary, text, new UTF8Encoding(false));
        if (File.Exists(path)) File.Replace(temporary, path, null);
        else File.Move(temporary, path);
    }
}
