using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace BuildingRegulationReview.Mcp.Tools;

/// <summary>Every tool the server offers, in registration order — the order <c>tools/list</c> shows them.</summary>
public sealed class McpToolRegistry
{
    private static readonly Regex ValidName = new("^[A-Za-z0-9_-]{1,64}$", RegexOptions.CultureInvariant);

    private readonly List<IMcpTool> _tools = new();
    private readonly Dictionary<string, IMcpTool> _byName = new(StringComparer.Ordinal);

    public IReadOnlyList<IMcpTool> Tools => _tools;

    public McpToolRegistry Add(IMcpTool tool)
    {
        if (tool is null) throw new ArgumentNullException(nameof(tool));
        if (!ValidName.IsMatch(tool.Name ?? string.Empty))
            throw new ArgumentException($"Tool name '{tool.Name}' must be 1–64 characters of A–Z, a–z, 0–9, '_' or '-'.", nameof(tool));
        if (_byName.ContainsKey(tool.Name!))
            throw new ArgumentException($"A tool named '{tool.Name}' is already registered.", nameof(tool));
        if (tool.InputSchema is null) throw new ArgumentException($"Tool '{tool.Name}' has no input schema.", nameof(tool));

        _tools.Add(tool);
        _byName.Add(tool.Name!, tool);
        return this;
    }

    public McpToolRegistry AddModule(IMcpToolModule module)
    {
        if (module is null) throw new ArgumentNullException(nameof(module));
        foreach (var tool in module.Tools.ToList()) Add(tool);
        return this;
    }

    public bool TryGet(string name, out IMcpTool tool) => _byName.TryGetValue(name ?? string.Empty, out tool!);
}
