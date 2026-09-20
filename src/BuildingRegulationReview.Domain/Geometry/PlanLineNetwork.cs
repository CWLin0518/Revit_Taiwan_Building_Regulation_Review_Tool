using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace BuildingRegulationReview.Domain.Geometry;

// How a piece of the raw snapshot was changed while it was normalized into a connected network
// (spec 10.2). The kind alone is not enough to explain a boundary back to the model, so every
// repair also carries the geometry it replaced and the distance it moved.
public enum NetworkRepairKind
{
    DuplicateRemoved,
    IntersectionSplit,
    EndpointSnapped,
    GapExtended,
    CollinearMerged
}

// What the repair refused to decide on its own. Errors block region solving and are handed to the
// user; warnings record something the pipeline resolved but a reviewer should still see.
public enum NetworkIssueKind
{
    SelfIntersection,
    CollapsedSegment,
    GapBeyondTolerance,
    DanglingEnd,
    NoClosedLoop
}

public enum NetworkIssueSeverity
{
    Warning,
    Error
}

// One entry of the repair log. Original* is the geometry as extracted, Result* is what replaced it,
// and DistanceFeet is how far the repair had to move a point to get there: the number the tolerance
// decision was made on.
public sealed class NetworkRepair
{
    public NetworkRepair(
        NetworkRepairKind kind,
        Point2D originalStart,
        Point2D originalEnd,
        Point2D resultStart,
        Point2D resultEnd,
        double distanceFeet,
        IEnumerable<SourceRef> sources,
        string message)
    {
        if (!Enum.IsDefined(typeof(NetworkRepairKind), kind)) throw new ArgumentOutOfRangeException(nameof(kind));
        if (double.IsNaN(distanceFeet) || double.IsInfinity(distanceFeet) || distanceFeet < 0)
            throw new ArgumentOutOfRangeException(nameof(distanceFeet), "Repair distance must be a finite, non-negative number.");
        if (string.IsNullOrWhiteSpace(message)) throw new ArgumentException("A repair needs a human-readable message.", nameof(message));

        var copy = (sources ?? Array.Empty<SourceRef>()).Where(x => x is not null).Distinct().ToList();
        if (copy.Count == 0) throw new ArgumentException("A repair must name at least one source element.", nameof(sources));

        Kind = kind;
        OriginalStart = originalStart;
        OriginalEnd = originalEnd;
        ResultStart = resultStart;
        ResultEnd = resultEnd;
        DistanceFeet = distanceFeet;
        Sources = new ReadOnlyCollection<SourceRef>(copy);
        Message = message.Trim();
    }

    public NetworkRepairKind Kind { get; }
    public Point2D OriginalStart { get; }
    public Point2D OriginalEnd { get; }
    public Point2D ResultStart { get; }
    public Point2D ResultEnd { get; }
    public double DistanceFeet { get; }
    public double DistanceMillimeters => PlanUnits.FeetToMillimeters(DistanceFeet);
    public IReadOnlyList<SourceRef> Sources { get; }
    public string Message { get; }

    public override string ToString() => $"{Kind} ({DistanceMillimeters:0.##} mm): {Message}";
}

// Something the pipeline would have had to guess at. Spec 10.2 is explicit that anything beyond
// tolerance is surfaced rather than silently fixed, so these travel with the network instead of
// failing it: the user resolves them in the Region Editor.
public sealed class NetworkIssue
{
    public NetworkIssue(
        NetworkIssueKind kind,
        NetworkIssueSeverity severity,
        Point2D location,
        double distanceFeet,
        IEnumerable<SourceRef> sources,
        string message)
    {
        if (!Enum.IsDefined(typeof(NetworkIssueKind), kind)) throw new ArgumentOutOfRangeException(nameof(kind));
        if (!Enum.IsDefined(typeof(NetworkIssueSeverity), severity)) throw new ArgumentOutOfRangeException(nameof(severity));
        if (double.IsNaN(distanceFeet) || double.IsInfinity(distanceFeet) || distanceFeet < 0)
            throw new ArgumentOutOfRangeException(nameof(distanceFeet), "Issue distance must be a finite, non-negative number.");
        if (string.IsNullOrWhiteSpace(message)) throw new ArgumentException("An issue needs a human-readable message.", nameof(message));

        Kind = kind;
        Severity = severity;
        Location = location;
        DistanceFeet = distanceFeet;
        Sources = new ReadOnlyCollection<SourceRef>((sources ?? Array.Empty<SourceRef>()).Where(x => x is not null).Distinct().ToList());
        Message = message.Trim();
    }

    public NetworkIssueKind Kind { get; }
    public NetworkIssueSeverity Severity { get; }

    /// <summary>Where the reviewer has to look in the Area Plan.</summary>
    public Point2D Location { get; }

    /// <summary>The measured distance that failed the tolerance check, or zero when not distance-based.</summary>
    public double DistanceFeet { get; }

    public double DistanceMillimeters => PlanUnits.FeetToMillimeters(DistanceFeet);
    public IReadOnlyList<SourceRef> Sources { get; }
    public string Message { get; }
    public bool IsError => Severity == NetworkIssueSeverity.Error;

    public override string ToString() => $"[{Severity}] {Kind} @ {Location}: {Message}";
}

/// <summary>A snapped junction. Several extracted endpoints can collapse onto one node.</summary>
public sealed class NetworkNode
{
    public NetworkNode(int id, Point2D position)
    {
        if (id < 0) throw new ArgumentOutOfRangeException(nameof(id));
        Id = id;
        Position = position;
    }

    public int Id { get; }
    public Point2D Position { get; }

    public override string ToString() => $"N{Id}{Position}";
}

/// <summary>
/// A repaired line between two nodes. Sources are plural because duplicate removal and collinear
/// merging fold several model elements into one edge, and all of them stay traceable.
/// </summary>
public sealed class NetworkEdge
{
    public NetworkEdge(int startNodeId, int endNodeId, Point2D start, Point2D end, IEnumerable<SourceRef> sources)
    {
        if (startNodeId < 0) throw new ArgumentOutOfRangeException(nameof(startNodeId));
        if (endNodeId < 0) throw new ArgumentOutOfRangeException(nameof(endNodeId));
        if (startNodeId == endNodeId) throw new ArgumentException("An edge cannot start and end at the same node.", nameof(endNodeId));

        var copy = (sources ?? Array.Empty<SourceRef>()).Where(x => x is not null).Distinct().ToList();
        if (copy.Count == 0) throw new ArgumentException("An edge must keep at least one source element.", nameof(sources));

        StartNodeId = startNodeId;
        EndNodeId = endNodeId;
        Start = start;
        End = end;
        Sources = new ReadOnlyCollection<SourceRef>(copy);
    }

    public int StartNodeId { get; }
    public int EndNodeId { get; }
    public Point2D Start { get; }
    public Point2D End { get; }
    public IReadOnlyList<SourceRef> Sources { get; }
    public SourceRef PrimarySource => Sources[0];
    public double LengthFeet => Start.DistanceTo(End);

    public int OtherNode(int nodeId)
    {
        if (nodeId == StartNodeId) return EndNodeId;
        if (nodeId == EndNodeId) return StartNodeId;
        throw new ArgumentException("This edge does not touch that node.", nameof(nodeId));
    }

    public Segment2D ToSegment() => new Segment2D(Start, End, PrimarySource);

    public override string ToString() => $"N{StartNodeId}->N{EndNodeId} [{PrimarySource}]";
}

// The repaired line network for one package: a planar graph plus the full account of how it was
// derived from the snapshot. P2-T04 solves regions from this; the Region Editor shows the issues.
public sealed class PlanLineNetwork
{
    private readonly int[] _degrees;
    private readonly Dictionary<int, List<int>> _edgesByNode;

    public PlanLineNetwork(
        Guid packageId,
        string hostDocumentUniqueId,
        string levelUniqueId,
        IEnumerable<NetworkNode> nodes,
        IEnumerable<NetworkEdge> edges,
        GeometryTolerance tolerance,
        IEnumerable<NetworkRepair>? repairs = null,
        IEnumerable<NetworkIssue>? issues = null,
        PlanExtent2D? extent = null,
        DateTime? repairedAtUtc = null)
    {
        if (packageId == Guid.Empty) throw new ArgumentException("Package ID cannot be empty.", nameof(packageId));
        if (string.IsNullOrWhiteSpace(hostDocumentUniqueId)) throw new ArgumentException("Host document identity is required.", nameof(hostDocumentUniqueId));
        if (string.IsNullOrWhiteSpace(levelUniqueId)) throw new ArgumentException("Level UniqueId is required.", nameof(levelUniqueId));
        if (nodes is null) throw new ArgumentNullException(nameof(nodes));
        if (edges is null) throw new ArgumentNullException(nameof(edges));

        var nodeList = nodes.ToList();
        if (nodeList.Any(x => x is null)) throw new ArgumentException("A network cannot contain null nodes.", nameof(nodes));
        for (var i = 0; i < nodeList.Count; i++)
        {
            if (nodeList[i].Id != i)
                throw new ArgumentException("Node IDs must form a dense zero-based sequence.", nameof(nodes));
        }

        var edgeList = edges.ToList();
        if (edgeList.Any(x => x is null)) throw new ArgumentException("A network cannot contain null edges.", nameof(edges));
        if (edgeList.Any(e => e.StartNodeId >= nodeList.Count || e.EndNodeId >= nodeList.Count))
            throw new ArgumentException("An edge refers to a node that is not in the network.", nameof(edges));

        PackageId = packageId;
        HostDocumentUniqueId = hostDocumentUniqueId.Trim();
        LevelUniqueId = levelUniqueId.Trim();
        Nodes = new ReadOnlyCollection<NetworkNode>(nodeList);
        Edges = new ReadOnlyCollection<NetworkEdge>(edgeList);
        Tolerance = tolerance ?? throw new ArgumentNullException(nameof(tolerance));
        Repairs = new ReadOnlyCollection<NetworkRepair>((repairs ?? Array.Empty<NetworkRepair>()).Where(x => x is not null).ToList());
        Issues = new ReadOnlyCollection<NetworkIssue>((issues ?? Array.Empty<NetworkIssue>()).Where(x => x is not null).ToList());
        Extent = extent;
        RepairedAtUtc = (repairedAtUtc ?? DateTime.UtcNow).ToUniversalTime();

        _degrees = new int[nodeList.Count];
        _edgesByNode = new Dictionary<int, List<int>>();
        for (var i = 0; i < edgeList.Count; i++)
        {
            _degrees[edgeList[i].StartNodeId]++;
            _degrees[edgeList[i].EndNodeId]++;
            AddIncidence(edgeList[i].StartNodeId, i);
            AddIncidence(edgeList[i].EndNodeId, i);
        }
    }

    public Guid PackageId { get; }
    public string HostDocumentUniqueId { get; }
    public string LevelUniqueId { get; }
    public IReadOnlyList<NetworkNode> Nodes { get; }
    public IReadOnlyList<NetworkEdge> Edges { get; }
    public GeometryTolerance Tolerance { get; }
    public IReadOnlyList<NetworkRepair> Repairs { get; }
    public IReadOnlyList<NetworkIssue> Issues { get; }
    public PlanExtent2D? Extent { get; }
    public DateTime RepairedAtUtc { get; }

    public int DegreeOf(int nodeId) => _degrees[nodeId];

    public IEnumerable<NetworkEdge> EdgesAt(int nodeId) =>
        _edgesByNode.TryGetValue(nodeId, out var indices) ? indices.Select(i => Edges[i]) : Enumerable.Empty<NetworkEdge>();

    /// <summary>Ends that still hang free. Each one is also reported as an issue.</summary>
    public IEnumerable<NetworkNode> DanglingNodes => Nodes.Where(n => DegreeOf(n.Id) == 1);

    public IEnumerable<NetworkIssue> Errors => Issues.Where(i => i.IsError);
    public bool HasErrors => Issues.Any(i => i.IsError);
    public double TotalLengthFeet => Edges.Sum(e => e.LengthFeet);
    public IEnumerable<SourceRef> DistinctSources => Edges.SelectMany(e => e.Sources).Distinct();
    public IEnumerable<NetworkRepair> RepairsOfKind(NetworkRepairKind kind) => Repairs.Where(r => r.Kind == kind);

    /// <summary>Connected components of the graph.</summary>
    public int ComponentCount => CountComponents();

    /// <summary>
    /// The cyclomatic number E - V + C: how many independent closed loops the network contains.
    /// Zero means nothing encloses an area yet, so P2-T04 would have no region to solve.
    /// </summary>
    public int LoopCount => Edges.Count - Nodes.Count + ComponentCount;

    public bool HasClosedLoop => LoopCount > 0;

    private void AddIncidence(int nodeId, int edgeIndex)
    {
        if (!_edgesByNode.TryGetValue(nodeId, out var list))
        {
            list = new List<int>();
            _edgesByNode[nodeId] = list;
        }

        list.Add(edgeIndex);
    }

    private int CountComponents()
    {
        if (Nodes.Count == 0) return 0;

        var parent = Enumerable.Range(0, Nodes.Count).ToArray();
        foreach (var edge in Edges) Union(parent, edge.StartNodeId, edge.EndNodeId);
        return Enumerable.Range(0, Nodes.Count).Select(i => Find(parent, i)).Distinct().Count();
    }

    private static int Find(int[] parent, int x)
    {
        while (parent[x] != x)
        {
            parent[x] = parent[parent[x]];
            x = parent[x];
        }

        return x;
    }

    private static void Union(int[] parent, int a, int b)
    {
        var rootA = Find(parent, a);
        var rootB = Find(parent, b);
        if (rootA != rootB) parent[rootB] = rootA;
    }
}
