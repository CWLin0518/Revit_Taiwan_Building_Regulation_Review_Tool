using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace BuildingRegulationReview.Domain.Geometry;

// What region solving noticed but did not act on. Anything genuinely ambiguous fails the solve
// instead of landing here (spec 10.0, P2-T04), so these are all things the user may want to look at
// rather than things the pipeline had to choose between.
public enum RegionIssueKind
{
    /// <summary>An edge with a free end. Nothing encloses area along it, so it bounds no face.</summary>
    DanglingEdgePruned,

    /// <summary>A connected part of the network that closed nothing at all.</summary>
    OpenComponent,

    /// <summary>A face too small to be a room: a sliver left where two boundaries nearly coincide.</summary>
    SliverFace
}

public sealed class RegionIssue
{
    public RegionIssue(
        RegionIssueKind kind,
        NetworkIssueSeverity severity,
        Point2D location,
        IEnumerable<SourceRef> sources,
        string message,
        int? faceId = null)
    {
        if (!Enum.IsDefined(typeof(RegionIssueKind), kind)) throw new ArgumentOutOfRangeException(nameof(kind));
        if (!Enum.IsDefined(typeof(NetworkIssueSeverity), severity)) throw new ArgumentOutOfRangeException(nameof(severity));
        if (string.IsNullOrWhiteSpace(message)) throw new ArgumentException("An issue needs a human-readable message.", nameof(message));
        if (faceId is < 0) throw new ArgumentOutOfRangeException(nameof(faceId));

        Kind = kind;
        Severity = severity;
        Location = location;
        Sources = new ReadOnlyCollection<SourceRef>((sources ?? Array.Empty<SourceRef>()).Where(x => x is not null).Distinct().ToList());
        Message = message.Trim();
        FaceId = faceId;
    }

    public RegionIssueKind Kind { get; }
    public NetworkIssueSeverity Severity { get; }

    /// <summary>Where the reviewer has to look in the Area Plan.</summary>
    public Point2D Location { get; }

    public IReadOnlyList<SourceRef> Sources { get; }
    public string Message { get; }

    /// <summary>The face this is about, when it is about one.</summary>
    public int? FaceId { get; }

    public bool IsError => Severity == NetworkIssueSeverity.Error;

    public override string ToString() => $"[{Severity}] {Kind} @ {Location}: {Message}";
}

/// <summary>
/// One traced cycle of the planar network: the loop itself plus the nodes and edges it ran through,
/// so a finished boundary stays traceable to the network and from there to the model elements.
/// </summary>
public sealed class FaceBoundary
{
    public FaceBoundary(Loop2D loop, IEnumerable<int> nodeIds, IEnumerable<int> edgeIndices)
    {
        Loop = loop ?? throw new ArgumentNullException(nameof(loop));

        var nodes = (nodeIds ?? throw new ArgumentNullException(nameof(nodeIds))).ToList();
        var edges = (edgeIndices ?? throw new ArgumentNullException(nameof(edgeIndices))).ToList();
        if (nodes.Count != loop.Segments.Count)
            throw new ArgumentException("A boundary needs one start node per segment.", nameof(nodeIds));
        if (edges.Count != loop.Segments.Count)
            throw new ArgumentException("A boundary needs one network edge per segment.", nameof(edgeIndices));
        if (nodes.Any(id => id < 0)) throw new ArgumentOutOfRangeException(nameof(nodeIds));
        if (edges.Any(id => id < 0)) throw new ArgumentOutOfRangeException(nameof(edgeIndices));

        NodeIds = new ReadOnlyCollection<int>(nodes);
        EdgeIndices = new ReadOnlyCollection<int>(edges);
        Vertices = new ReadOnlyCollection<Point2D>(loop.Segments.Select(s => s.Start).ToList());
    }

    public Loop2D Loop { get; }

    /// <summary>Network node IDs in traversal order, aligned with <see cref="Loop"/>'s segments.</summary>
    public IReadOnlyList<int> NodeIds { get; }

    /// <summary>Indices into <see cref="PlanLineNetwork.Edges"/>, aligned with the segments.</summary>
    public IReadOnlyList<int> EdgeIndices { get; }

    public IReadOnlyList<Point2D> Vertices { get; }
    public double SignedAreaSquareFeet => Loop.SignedAreaSquareFeet;
    public double AreaSquareFeet => Loop.AreaSquareFeet;
    public double PerimeterFeet => Loop.PerimeterFeet;
    public bool IsCounterClockwise => !Loop.IsClockwise;
    public IEnumerable<SourceRef> DistinctSources => Loop.Segments.SelectMany(s => new[] { s.Source }).Distinct();

    public override string ToString() => $"{NodeIds.Count} nodes, {AreaSquareFeet:0.###} sq ft";
}

/// <summary>
/// A solved enclosed area: one outer boundary, counter-clockwise, plus the clockwise boundaries of
/// whatever sits inside it as a hole. This is the unit the user clicks to build a 區劃.
/// </summary>
public sealed class PlanFace
{
    public PlanFace(int id, FaceBoundary outer, IEnumerable<FaceBoundary>? holes, Point2D representativePoint)
    {
        if (id < 0) throw new ArgumentOutOfRangeException(nameof(id));
        Outer = outer ?? throw new ArgumentNullException(nameof(outer));

        var holeList = (holes ?? Array.Empty<FaceBoundary>()).ToList();
        if (holeList.Any(x => x is null)) throw new ArgumentException("Holes cannot be null.", nameof(holes));

        Id = id;
        Holes = new ReadOnlyCollection<FaceBoundary>(holeList);
        RepresentativePoint = representativePoint;
        Geometry = new Region2D(outer.Loop, holeList.Select(h => h.Loop));
    }

    public int Id { get; }
    public FaceBoundary Outer { get; }
    public IReadOnlyList<FaceBoundary> Holes { get; }

    /// <summary>A point strictly inside the face and outside its holes: the Editor's hit target.</summary>
    public Point2D RepresentativePoint { get; }

    public Region2D Geometry { get; }

    /// <summary>Draft area only; the Area element Revit reports after write-back is authoritative.</summary>
    public double NetAreaSquareFeet => Geometry.NetAreaSquareFeet;

    public double NetAreaSquareMeters => Geometry.NetAreaSquareMeters;
    public double PerimeterFeet => Outer.PerimeterFeet;
    public bool HasHoles => Holes.Count > 0;

    public IEnumerable<FaceBoundary> AllBoundaries => new[] { Outer }.Concat(Holes);

    public IEnumerable<int> EdgeIndices => AllBoundaries.SelectMany(b => b.EdgeIndices).Distinct();

    public IEnumerable<SourceRef> DistinctSources => AllBoundaries.SelectMany(b => b.DistinctSources).Distinct();

    public bool Contains(Point2D point) =>
        RingGeometry.ContainsPoint(Outer.Vertices, point) &&
        !Holes.Any(h => RingGeometry.ContainsPoint(h.Vertices, point));

    public override string ToString() => $"F{Id} ({NetAreaSquareMeters:0.##} m2, {Holes.Count} holes)";
}

/// <summary>Two faces that share boundary. The shared length is what makes a common fire-rated wall
/// visible to later phases; a shared point alone is not adjacency.</summary>
public sealed class FaceAdjacency
{
    public FaceAdjacency(int firstFaceId, int secondFaceId, double sharedLengthFeet, IEnumerable<int> sharedEdgeIndices)
    {
        if (firstFaceId < 0) throw new ArgumentOutOfRangeException(nameof(firstFaceId));
        if (secondFaceId < 0) throw new ArgumentOutOfRangeException(nameof(secondFaceId));
        if (firstFaceId == secondFaceId) throw new ArgumentException("A face is not adjacent to itself.", nameof(secondFaceId));
        if (double.IsNaN(sharedLengthFeet) || double.IsInfinity(sharedLengthFeet) || sharedLengthFeet <= 0)
            throw new ArgumentOutOfRangeException(nameof(sharedLengthFeet), "Adjacency needs a positive shared length.");

        var edges = (sharedEdgeIndices ?? Array.Empty<int>()).Distinct().OrderBy(i => i).ToList();
        if (edges.Count == 0) throw new ArgumentException("Adjacency must name the shared edges.", nameof(sharedEdgeIndices));

        FirstFaceId = Math.Min(firstFaceId, secondFaceId);
        SecondFaceId = Math.Max(firstFaceId, secondFaceId);
        SharedLengthFeet = sharedLengthFeet;
        SharedEdgeIndices = new ReadOnlyCollection<int>(edges);
    }

    public int FirstFaceId { get; }
    public int SecondFaceId { get; }
    public double SharedLengthFeet { get; }
    public double SharedLengthMeters => PlanUnits.FeetToMeters(SharedLengthFeet);
    public IReadOnlyList<int> SharedEdgeIndices { get; }

    public bool Touches(int faceId) => faceId == FirstFaceId || faceId == SecondFaceId;
    public int Other(int faceId) => faceId == FirstFaceId ? SecondFaceId : FirstFaceId;

    public override string ToString() => $"F{FirstFaceId}-F{SecondFaceId} ({SharedLengthMeters:0.##} m)";
}

/// <summary>
/// Several faces taken as one 區劃. Spec 10.3 allows a merged zone to be a MultiPolygon but requires
/// the Editor to show whether it is contiguous, so connectivity is reported here and the policy
/// decision stays with the user.
/// </summary>
public sealed class MultiFaceRegion
{
    public MultiFaceRegion(IEnumerable<PlanFace> faces, int contiguousPartCount)
    {
        if (faces is null) throw new ArgumentNullException(nameof(faces));
        if (contiguousPartCount < 0) throw new ArgumentOutOfRangeException(nameof(contiguousPartCount));

        var copy = faces.ToList();
        if (copy.Any(x => x is null)) throw new ArgumentException("A region cannot contain null faces.", nameof(faces));

        Faces = new ReadOnlyCollection<PlanFace>(copy);
        ContiguousPartCount = contiguousPartCount;
    }

    public IReadOnlyList<PlanFace> Faces { get; }

    /// <summary>How many connected clusters the chosen faces fall into. One means contiguous.</summary>
    public int ContiguousPartCount { get; }

    public bool IsContiguous => ContiguousPartCount <= 1;
    public bool IsEmpty => Faces.Count == 0;
    public IEnumerable<int> FaceIds => Faces.Select(f => f.Id);
    public IEnumerable<Region2D> Polygons => Faces.Select(f => f.Geometry);
    public double NetAreaSquareFeet => Faces.Sum(f => f.NetAreaSquareFeet);
    public double NetAreaSquareMeters => PlanUnits.SquareFeetToSquareMeters(NetAreaSquareFeet);
    public IEnumerable<SourceRef> DistinctSources => Faces.SelectMany(f => f.DistinctSources).Distinct();

    public override string ToString() =>
        $"{Faces.Count} faces, {NetAreaSquareMeters:0.##} m2, {(IsContiguous ? "contiguous" : $"{ContiguousPartCount} parts")}";
}

/// <summary>
/// Every enclosed area the repaired network defines, with how those areas touch each other. The
/// Region Editor selects faces out of this map; write-back reads the boundaries back out of it.
/// </summary>
public sealed class PlanRegionMap
{
    private readonly Dictionary<int, List<FaceAdjacency>> _adjacencyByFace = new Dictionary<int, List<FaceAdjacency>>();

    public PlanRegionMap(
        Guid packageId,
        string hostDocumentUniqueId,
        string levelUniqueId,
        IEnumerable<PlanFace> faces,
        IEnumerable<FaceAdjacency> adjacencies,
        GeometryTolerance tolerance,
        IEnumerable<RegionIssue>? issues = null,
        PlanExtent2D? extent = null,
        DateTime? solvedAtUtc = null)
    {
        if (packageId == Guid.Empty) throw new ArgumentException("Package ID cannot be empty.", nameof(packageId));
        if (string.IsNullOrWhiteSpace(hostDocumentUniqueId)) throw new ArgumentException("Host document identity is required.", nameof(hostDocumentUniqueId));
        if (string.IsNullOrWhiteSpace(levelUniqueId)) throw new ArgumentException("Level UniqueId is required.", nameof(levelUniqueId));
        if (faces is null) throw new ArgumentNullException(nameof(faces));
        if (adjacencies is null) throw new ArgumentNullException(nameof(adjacencies));

        var faceList = faces.ToList();
        if (faceList.Any(x => x is null)) throw new ArgumentException("A map cannot contain null faces.", nameof(faces));
        for (var i = 0; i < faceList.Count; i++)
        {
            if (faceList[i].Id != i) throw new ArgumentException("Face IDs must form a dense zero-based sequence.", nameof(faces));
        }

        var adjacencyList = adjacencies.ToList();
        if (adjacencyList.Any(x => x is null)) throw new ArgumentException("A map cannot contain null adjacencies.", nameof(adjacencies));
        if (adjacencyList.Any(a => a.SecondFaceId >= faceList.Count))
            throw new ArgumentException("An adjacency refers to a face that is not in the map.", nameof(adjacencies));

        PackageId = packageId;
        HostDocumentUniqueId = hostDocumentUniqueId.Trim();
        LevelUniqueId = levelUniqueId.Trim();
        Faces = new ReadOnlyCollection<PlanFace>(faceList);
        Adjacencies = new ReadOnlyCollection<FaceAdjacency>(adjacencyList);
        Tolerance = tolerance ?? throw new ArgumentNullException(nameof(tolerance));
        Issues = new ReadOnlyCollection<RegionIssue>((issues ?? Array.Empty<RegionIssue>()).Where(x => x is not null).ToList());
        Extent = extent;
        SolvedAtUtc = (solvedAtUtc ?? DateTime.UtcNow).ToUniversalTime();

        foreach (var adjacency in adjacencyList)
        {
            Index(adjacency.FirstFaceId, adjacency);
            Index(adjacency.SecondFaceId, adjacency);
        }
    }

    public Guid PackageId { get; }
    public string HostDocumentUniqueId { get; }
    public string LevelUniqueId { get; }
    public IReadOnlyList<PlanFace> Faces { get; }
    public IReadOnlyList<FaceAdjacency> Adjacencies { get; }
    public GeometryTolerance Tolerance { get; }
    public IReadOnlyList<RegionIssue> Issues { get; }
    public PlanExtent2D? Extent { get; }
    public DateTime SolvedAtUtc { get; }

    public bool IsEmpty => Faces.Count == 0;
    public double TotalNetAreaSquareFeet => Faces.Sum(f => f.NetAreaSquareFeet);
    public double TotalNetAreaSquareMeters => PlanUnits.SquareFeetToSquareMeters(TotalNetAreaSquareFeet);
    public IEnumerable<RegionIssue> Errors => Issues.Where(i => i.IsError);
    public bool HasErrors => Issues.Any(i => i.IsError);
    public IEnumerable<PlanFace> FacesWithHoles => Faces.Where(f => f.HasHoles);

    public PlanFace Face(int faceId) => Faces[faceId];

    public IEnumerable<FaceAdjacency> AdjacenciesOf(int faceId) =>
        _adjacencyByFace.TryGetValue(faceId, out var list) ? list : Enumerable.Empty<FaceAdjacency>();

    public IEnumerable<PlanFace> NeighboursOf(int faceId) =>
        AdjacenciesOf(faceId).Select(a => Faces[a.Other(faceId)]);

    public bool AreAdjacent(int firstFaceId, int secondFaceId) =>
        AdjacenciesOf(firstFaceId).Any(a => a.Touches(secondFaceId));

    /// <summary>The face a click lands in, or null outside every face. Holes are not inside.</summary>
    public PlanFace? FaceAt(Point2D point) => Faces.FirstOrDefault(f => f.Contains(point));

    /// <summary>
    /// Takes a set of faces as one zone and reports how many contiguous parts it falls into, which
    /// is what the Editor needs before it lets a non-contiguous merge through.
    /// </summary>
    public MultiFaceRegion Combine(IEnumerable<int> faceIds)
    {
        if (faceIds is null) throw new ArgumentNullException(nameof(faceIds));

        var chosen = faceIds.Distinct().OrderBy(id => id).ToList();
        foreach (var id in chosen)
        {
            if (id < 0 || id >= Faces.Count) throw new ArgumentOutOfRangeException(nameof(faceIds), $"Face {id} is not in this map.");
        }

        return new MultiFaceRegion(chosen.Select(id => Faces[id]), CountContiguousParts(chosen));
    }

    private int CountContiguousParts(List<int> chosen)
    {
        if (chosen.Count == 0) return 0;

        var members = new HashSet<int>(chosen);
        var seen = new HashSet<int>();
        var parts = 0;

        foreach (var start in chosen)
        {
            if (!seen.Add(start)) continue;
            parts++;

            var queue = new Queue<int>();
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                foreach (var adjacency in AdjacenciesOf(current))
                {
                    var other = adjacency.Other(current);
                    if (!members.Contains(other) || !seen.Add(other)) continue;
                    queue.Enqueue(other);
                }
            }
        }

        return parts;
    }

    private void Index(int faceId, FaceAdjacency adjacency)
    {
        if (!_adjacencyByFace.TryGetValue(faceId, out var list))
        {
            list = new List<FaceAdjacency>();
            _adjacencyByFace[faceId] = list;
        }

        list.Add(adjacency);
    }
}
