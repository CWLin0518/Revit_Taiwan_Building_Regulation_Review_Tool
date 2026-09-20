using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BuildingRegulationReview.Domain.Common;
using BuildingRegulationReview.Domain.Geometry;

namespace BuildingRegulationReview.Application.Geometry;

/// <summary>
/// Solves the enclosed areas of a repaired <see cref="PlanLineNetwork"/>: closed loops, holes,
/// face adjacency and draft areas (spec 10.0, P2-T04).
/// </summary>
/// <remarks>
/// The network is treated as a planar embedding and walked with half-edges: every edge is traversed
/// once in each direction, and at each node the walk turns onto the neighbour that is next in
/// clockwise order. That produces one cycle per face, counter-clockwise for the areas a boundary
/// encloses and clockwise once per connected part for the space around it. Nothing about which side
/// is "inside" is guessed from a bounding box, and nothing is stitched to make a loop close.
/// <para>
/// The same rule as the repair applies here: where the geometry would have to be interpreted, the
/// solve fails rather than picking a reading. Edges that still cross away from a node, and a hole
/// that could belong to more than one face, both stop the solve with an error the user can act on.
/// Everything the solve resolved without a choice — a free end that bounds nothing, a part that
/// closes nothing, a sliver — travels with the map as an issue.
/// </para>
/// </remarks>
public sealed class RegionSolver
{
    /// <summary>
    /// Below this a traced face is a sliver where two boundaries nearly coincide, not a room. It is
    /// a snap-tolerance square, so it follows the tolerance the repair snapped with rather than
    /// introducing a size of its own; a sliver is reported, never dropped.
    /// </summary>
    public static double SliverAreaSquareFeet(GeometryTolerance tolerance)
    {
        if (tolerance is null) throw new ArgumentNullException(nameof(tolerance));
        return tolerance.SnapFeet * tolerance.SnapFeet;
    }

    public Result<PlanRegionMap> Solve(PlanLineNetwork network, DateTime? solvedAtUtc = null)
    {
        if (network is null) throw new ArgumentNullException(nameof(network));
        return new Run(network).Execute(solvedAtUtc);
    }

    // One traced cycle before it is known whether it bounds a face or the space around a part.
    private sealed class TracedCycle
    {
        public TracedCycle(int id, IReadOnlyList<int> halfEdges, IReadOnlyList<int> nodeIds, IReadOnlyList<Point2D> ring, int componentId)
        {
            Id = id;
            HalfEdges = halfEdges;
            NodeIds = nodeIds;
            Ring = ring;
            ComponentId = componentId;
            SignedArea = RingGeometry.SignedArea(ring);
        }

        public int Id { get; }
        public IReadOnlyList<int> HalfEdges { get; }
        public IReadOnlyList<int> NodeIds { get; }
        public IReadOnlyList<Point2D> Ring { get; }
        public int ComponentId { get; }
        public double SignedArea { get; }
        public double Area => Math.Abs(SignedArea);
        public bool BoundsArea => SignedArea > 0;
    }

    private sealed class Run
    {
        private readonly PlanLineNetwork _network;
        private readonly GeometryTolerance _tolerance;
        private readonly bool[] _alive;
        private readonly List<RegionIssue> _issues = new List<RegionIssue>();
        private readonly double _sliverArea;

        public Run(PlanLineNetwork network)
        {
            _network = network;
            _tolerance = network.Tolerance;
            _alive = Enumerable.Repeat(true, network.Edges.Count).ToArray();
            _sliverArea = SliverAreaSquareFeet(network.Tolerance);
        }

        public Result<PlanRegionMap> Execute(DateTime? solvedAtUtc)
        {
            if (_network.Edges.Count == 0)
            {
                return Result.Failure<PlanRegionMap>(new Error(
                    "geometry.regions.empty",
                    "線網沒有任何線段，無法求解區劃。",
                    Accounting()));
            }

            var crossing = FindUnsplitCrossing();
            if (crossing is not null)
            {
                return Result.Failure<PlanRegionMap>(new Error(
                    "geometry.regions.non-planar",
                    $"線網在節點以外的位置仍有交叉（{Coordinate(crossing.Value)}），無法判斷邊界的內外側，請先在該處分割或移除重疊的線。",
                    Accounting()));
            }

            PruneDanglingEdges();
            if (!_alive.Any(a => a))
            {
                return Result.Failure<PlanRegionMap>(new Error(
                    "geometry.regions.no-closed-loop",
                    "線網中沒有任何封閉範圍，無法求解區劃，請補足缺口或加入輔助線。",
                    Accounting()));
            }

            var components = ComponentsOfAliveEdges();
            var cycles = TraceCycles(components);

            // Two edges between the same pair of nodes leave a cycle with nothing between them.
            // It is the one shape the walk cannot read a side from, so it stops the solve.
            var degenerate = cycles.FirstOrDefault(c => c.HalfEdges.Count < 3);
            if (degenerate is not null)
            {
                return Result.Failure<PlanRegionMap>(new Error(
                    "geometry.regions.degenerate-face",
                    $"{Coordinate(degenerate.Ring[0])} 附近有兩條線段連接同一對節點，無法判斷它們之間是否圍出範圍，請先合併或移除其中一條。",
                    Accounting()));
            }

            var faceCycles = cycles.Where(c => c.BoundsArea).ToList();
            if (faceCycles.Count == 0)
            {
                return Result.Failure<PlanRegionMap>(new Error(
                    "geometry.regions.no-closed-loop",
                    "線網中沒有任何封閉範圍，無法求解區劃，請補足缺口或加入輔助線。",
                    Accounting()));
            }

            var ordered = faceCycles.OrderBy(CanonicalKey, StringComparer.Ordinal).ToList();
            var faceIdByCycle = new Dictionary<int, int>();
            for (var i = 0; i < ordered.Count; i++) faceIdByCycle[ordered[i].Id] = i;

            var holes = AssignHoles(cycles, ordered, out var nestingError);
            if (nestingError is not null) return Result.Failure<PlanRegionMap>(nestingError);

            var faces = new List<PlanFace>();
            for (var i = 0; i < ordered.Count; i++)
            {
                var cycle = ordered[i];
                var holeCycles = holes.TryGetValue(cycle.Id, out var list) ? list : new List<TracedCycle>();
                if (!RingGeometry.TryFindInteriorPoint(cycle.Ring, holeCycles.Select(h => h.Ring), out var interior))
                {
                    return Result.Failure<PlanRegionMap>(new Error(
                        "geometry.regions.degenerate-face",
                        $"封閉範圍（{Coordinate(cycle.Ring[0])}）退化為沒有內部的形狀，無法求解區劃。",
                        Accounting()));
                }

                if (cycle.Area <= _sliverArea)
                {
                    _issues.Add(new RegionIssue(
                        RegionIssueKind.SliverFace,
                        NetworkIssueSeverity.Warning,
                        interior,
                        SourcesOf(cycle),
                        $"此封閉範圍僅 {SquareMillimeters(cycle.Area)} mm²，小於吸附容差的方格，可能是兩條邊界幾乎重疊留下的細縫。",
                        i));
                }

                faces.Add(new PlanFace(i, BoundaryOf(cycle), holeCycles.Select(BoundaryOf), interior));
            }

            var map = new PlanRegionMap(
                _network.PackageId,
                _network.HostDocumentUniqueId,
                _network.LevelUniqueId,
                faces,
                BuildAdjacencies(cycles, faceIdByCycle, holes),
                _tolerance,
                _issues,
                _network.Extent,
                solvedAtUtc);

            return Result.Success(map);
        }

        // Edges may only meet at nodes. Anything else means the repair left an intersection unsplit,
        // and the walk would then have no defined turn order at that point.
        private Point2D? FindUnsplitCrossing()
        {
            var edges = _network.Edges;
            for (var i = 0; i < edges.Count; i++)
            {
                for (var j = i + 1; j < edges.Count; j++)
                {
                    if (SharesNode(edges[i], edges[j])) continue;
                    if (!BoxesOverlap(edges[i], edges[j], _tolerance.SnapFeet)) continue;
                    if (SegmentGeometry.TryIntersect(
                        edges[i].Start, edges[i].End, edges[j].Start, edges[j].End, 0, out _, out _, out var point))
                    {
                        return point;
                    }
                }
            }

            return null;
        }

        // An edge with a free end encloses nothing, and leaving it in would make the walk run out
        // and back along it inside a face. Pruning repeats because removing one end can free another.
        private void PruneDanglingEdges()
        {
            var degrees = new int[_network.Nodes.Count];
            foreach (var edge in _network.Edges)
            {
                degrees[edge.StartNodeId]++;
                degrees[edge.EndNodeId]++;
            }

            var componentsBefore = ComponentsOfAliveEdges();

            var changed = true;
            while (changed)
            {
                changed = false;
                for (var i = 0; i < _network.Edges.Count; i++)
                {
                    if (!_alive[i]) continue;
                    var edge = _network.Edges[i];
                    if (degrees[edge.StartNodeId] > 1 && degrees[edge.EndNodeId] > 1) continue;

                    _alive[i] = false;
                    degrees[edge.StartNodeId]--;
                    degrees[edge.EndNodeId]--;
                    changed = true;

                    _issues.Add(new RegionIssue(
                        RegionIssueKind.DanglingEdgePruned,
                        NetworkIssueSeverity.Warning,
                        edge.ToSegment().Midpoint(),
                        edge.Sources,
                        $"此線段有一端懸空（長 {Millimeters(edge.LengthFeet)} mm），不構成任何封閉範圍，求解區劃時已略過。"));
                }
            }

            ReportComponentsThatClosedNothing(componentsBefore);
        }

        private void ReportComponentsThatClosedNothing(int[] componentsBefore)
        {
            var survivors = new HashSet<int>();
            for (var i = 0; i < _network.Edges.Count; i++)
            {
                if (_alive[i]) survivors.Add(componentsBefore[_network.Edges[i].StartNodeId]);
            }

            var reported = new HashSet<int>();
            for (var i = 0; i < _network.Edges.Count; i++)
            {
                var component = componentsBefore[_network.Edges[i].StartNodeId];
                if (survivors.Contains(component) || !reported.Add(component)) continue;

                _issues.Add(new RegionIssue(
                    RegionIssueKind.OpenComponent,
                    NetworkIssueSeverity.Warning,
                    _network.Edges[i].ToSegment().Midpoint(),
                    _network.Edges[i].Sources,
                    "這一組相連的線段沒有圍出任何封閉範圍，求解區劃時整組略過。"));
            }
        }

        private int[] ComponentsOfAliveEdges()
        {
            var parent = Enumerable.Range(0, _network.Nodes.Count).ToArray();
            for (var i = 0; i < _network.Edges.Count; i++)
            {
                if (!_alive[i]) continue;
                Union(parent, _network.Edges[i].StartNodeId, _network.Edges[i].EndNodeId);
            }

            return Enumerable.Range(0, _network.Nodes.Count).Select(i => Find(parent, i)).ToArray();
        }

        // Half-edge h is edge h/2 walked forwards when h is even and backwards when it is odd.
        private List<TracedCycle> TraceCycles(int[] components)
        {
            var outgoing = new Dictionary<int, List<int>>();
            for (var i = 0; i < _network.Edges.Count; i++)
            {
                if (!_alive[i]) continue;
                AddOutgoing(outgoing, OriginOf(i * 2), i * 2);
                AddOutgoing(outgoing, OriginOf((i * 2) + 1), (i * 2) + 1);
            }

            var slot = new Dictionary<int, int>();
            foreach (var nodeId in outgoing.Keys.ToList())
            {
                var sorted = outgoing[nodeId]
                    .OrderBy(AngleOf)
                    .ThenBy(h => h)
                    .ToList();
                outgoing[nodeId] = sorted;
                for (var i = 0; i < sorted.Count; i++) slot[sorted[i]] = i;
            }

            var cycles = new List<TracedCycle>();
            var visited = new HashSet<int>();
            foreach (var start in outgoing.Values.SelectMany(v => v).OrderBy(h => h))
            {
                if (!visited.Add(start)) continue;

                var walk = new List<int> { start };
                var current = start;
                while (true)
                {
                    // Turn onto the neighbour before the twin in counter-clockwise order, i.e. the
                    // first one clockwise. That keeps the enclosed side on the left throughout.
                    var twin = current ^ 1;
                    var atTarget = outgoing[OriginOf(twin)];
                    var next = atTarget[(slot[twin] - 1 + atTarget.Count) % atTarget.Count];
                    if (next == start) break;
                    if (!visited.Add(next)) break;
                    walk.Add(next);
                    current = next;
                }

                var nodeIds = walk.Select(OriginOf).ToList();
                var ring = nodeIds.Select(id => _network.Nodes[id].Position).ToList();
                cycles.Add(new TracedCycle(cycles.Count, walk, nodeIds, ring, components[nodeIds[0]]));
            }

            return cycles;
        }

        // A clockwise cycle is the space around one connected part. It is either the unbounded face
        // of the whole plan, or a hole in the face that part sits inside.
        private Dictionary<int, List<TracedCycle>> AssignHoles(
            List<TracedCycle> cycles,
            List<TracedCycle> faceCycles,
            out Error? error)
        {
            error = null;
            var holes = new Dictionary<int, List<TracedCycle>>();

            foreach (var outerCycle in cycles.Where(c => !c.BoundsArea))
            {
                var probe = outerCycle.Ring.OrderBy(p => p.X).ThenBy(p => p.Y).First();
                var candidates = new List<TracedCycle>();

                foreach (var face in faceCycles)
                {
                    if (face.ComponentId == outerCycle.ComponentId) continue;

                    var clearance = DistanceToRing(face.Ring, probe);
                    if (clearance <= _tolerance.SnapFeet)
                    {
                        error = new Error(
                            "geometry.regions.ambiguous-nesting",
                            $"{Coordinate(probe)} 附近有兩組未相接但幾乎接觸的邊界（相距 {Millimeters(clearance)} mm），" +
                            "無法判斷內側範圍屬於哪一個區劃，請先接合或拉開它們。",
                            Accounting());
                        return holes;
                    }

                    if (RingGeometry.ContainsPoint(face.Ring, probe)) candidates.Add(face);
                }

                if (candidates.Count == 0) continue;

                var ordered = candidates.OrderBy(c => c.Area).ToList();
                if (ordered.Count > 1 && ordered[1].Area - ordered[0].Area <= _sliverArea)
                {
                    error = new Error(
                        "geometry.regions.ambiguous-nesting",
                        $"{Coordinate(probe)} 的內側範圍同時落在兩個面積幾乎相同的封閉範圍內，無法判斷它是哪一個的孔洞。",
                        Accounting());
                    return holes;
                }

                var parent = ordered[0];
                if (!holes.TryGetValue(parent.Id, out var list))
                {
                    list = new List<TracedCycle>();
                    holes[parent.Id] = list;
                }

                list.Add(outerCycle);
            }

            return holes;
        }

        // Every edge is walked once from each side, so the two cycles that used it are the two faces
        // it separates. A hole boundary counts as its parent face's side of the edge.
        private List<FaceAdjacency> BuildAdjacencies(
            List<TracedCycle> cycles,
            Dictionary<int, int> faceIdByCycle,
            Dictionary<int, List<TracedCycle>> holes)
        {
            var faceByCycle = new Dictionary<int, int>(faceIdByCycle);
            foreach (var pair in holes)
            {
                foreach (var hole in pair.Value) faceByCycle[hole.Id] = faceIdByCycle[pair.Key];
            }

            var cycleByHalfEdge = new Dictionary<int, int>();
            foreach (var cycle in cycles)
            {
                foreach (var halfEdge in cycle.HalfEdges) cycleByHalfEdge[halfEdge] = cycle.Id;
            }

            var shared = new Dictionary<(int First, int Second), (double Length, List<int> Edges)>();
            for (var i = 0; i < _network.Edges.Count; i++)
            {
                if (!_alive[i]) continue;
                if (!cycleByHalfEdge.TryGetValue(i * 2, out var left)) continue;
                if (!cycleByHalfEdge.TryGetValue((i * 2) + 1, out var right)) continue;
                if (!faceByCycle.TryGetValue(left, out var leftFace)) continue;
                if (!faceByCycle.TryGetValue(right, out var rightFace)) continue;
                if (leftFace == rightFace) continue;

                var key = (Math.Min(leftFace, rightFace), Math.Max(leftFace, rightFace));
                if (!shared.TryGetValue(key, out var entry)) entry = (0, new List<int>());
                entry.Length += _network.Edges[i].LengthFeet;
                entry.Edges.Add(i);
                shared[key] = entry;
            }

            return shared
                .OrderBy(kv => kv.Key.First)
                .ThenBy(kv => kv.Key.Second)
                .Select(kv => new FaceAdjacency(kv.Key.First, kv.Key.Second, kv.Value.Length, kv.Value.Edges))
                .ToList();
        }

        private FaceBoundary BoundaryOf(TracedCycle cycle)
        {
            var segments = new List<Segment2D>(cycle.HalfEdges.Count);
            for (var i = 0; i < cycle.HalfEdges.Count; i++)
            {
                var edge = _network.Edges[cycle.HalfEdges[i] / 2];
                var start = cycle.Ring[i];
                var end = cycle.Ring[(i + 1) % cycle.Ring.Count];
                segments.Add(new Segment2D(start, end, edge.PrimarySource));
            }

            return new FaceBoundary(
                new Loop2D(segments, _tolerance),
                cycle.NodeIds,
                cycle.HalfEdges.Select(h => h / 2));
        }

        // Faces are named by the node sequence they run through, rotated to start at the lowest node
        // ID, so the same network always produces the same face IDs in the same order.
        private static string CanonicalKey(TracedCycle cycle)
        {
            var offset = 0;
            for (var i = 1; i < cycle.NodeIds.Count; i++)
            {
                if (cycle.NodeIds[i] < cycle.NodeIds[offset]) offset = i;
            }

            return string.Join(
                "-",
                Enumerable.Range(0, cycle.NodeIds.Count)
                    .Select(i => cycle.NodeIds[(offset + i) % cycle.NodeIds.Count].ToString("D8", CultureInfo.InvariantCulture)));
        }

        private IEnumerable<SourceRef> SourcesOf(TracedCycle cycle) =>
            cycle.HalfEdges.SelectMany(h => _network.Edges[h / 2].Sources).Distinct();

        private int OriginOf(int halfEdge)
        {
            var edge = _network.Edges[halfEdge / 2];
            return (halfEdge % 2) == 0 ? edge.StartNodeId : edge.EndNodeId;
        }

        private double AngleOf(int halfEdge)
        {
            var origin = _network.Nodes[OriginOf(halfEdge)].Position;
            var target = _network.Nodes[OriginOf(halfEdge ^ 1)].Position;
            return Math.Atan2(target.Y - origin.Y, target.X - origin.X);
        }

        private double DistanceToRing(IReadOnlyList<Point2D> ring, Point2D point)
        {
            var best = double.MaxValue;
            for (var i = 0; i < ring.Count; i++)
            {
                var distance = SegmentGeometry.DistanceToSegment(point, ring[i], ring[(i + 1) % ring.Count], out _, out _);
                if (distance < best) best = distance;
            }

            return best;
        }

        private string Accounting() =>
            $"nodes={_network.Nodes.Count}; edges={_network.Edges.Count}; alive={_alive.Count(a => a)}; " +
            $"loopCount={_network.LoopCount}; snapMm={Millimeters(_tolerance.SnapFeet)}";

        private static bool SharesNode(NetworkEdge a, NetworkEdge b) =>
            a.StartNodeId == b.StartNodeId || a.StartNodeId == b.EndNodeId ||
            a.EndNodeId == b.StartNodeId || a.EndNodeId == b.EndNodeId;

        private static bool BoxesOverlap(NetworkEdge a, NetworkEdge b, double toleranceFeet) =>
            Math.Min(a.Start.X, a.End.X) - toleranceFeet <= Math.Max(b.Start.X, b.End.X) &&
            Math.Max(a.Start.X, a.End.X) + toleranceFeet >= Math.Min(b.Start.X, b.End.X) &&
            Math.Min(a.Start.Y, a.End.Y) - toleranceFeet <= Math.Max(b.Start.Y, b.End.Y) &&
            Math.Max(a.Start.Y, a.End.Y) + toleranceFeet >= Math.Min(b.Start.Y, b.End.Y);

        private static void AddOutgoing(Dictionary<int, List<int>> map, int nodeId, int halfEdge)
        {
            if (!map.TryGetValue(nodeId, out var list))
            {
                list = new List<int>();
                map[nodeId] = list;
            }

            list.Add(halfEdge);
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

        private static string Millimeters(double feet) =>
            PlanUnits.FeetToMillimeters(feet).ToString("0.##", CultureInfo.InvariantCulture);

        private static string SquareMillimeters(double squareFeet)
        {
            var perFoot = PlanUnits.FeetToMillimeters(1.0);
            return (squareFeet * perFoot * perFoot).ToString("0.##", CultureInfo.InvariantCulture);
        }

        private static string Coordinate(Point2D point) =>
            $"({Millimeters(point.X)}, {Millimeters(point.Y)}) mm";
    }
}
