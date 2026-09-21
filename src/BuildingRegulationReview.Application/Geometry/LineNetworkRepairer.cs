using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BuildingRegulationReview.Domain.Common;
using BuildingRegulationReview.Domain.Geometry;

namespace BuildingRegulationReview.Application.Geometry;

/// <summary>
/// Turns a raw <see cref="PlanGeometrySnapshot"/> into a connected <see cref="PlanLineNetwork"/> by
/// running spec 10.2 in order: remove duplicates, split at intersections, snap endpoints, extend
/// short gaps, merge collinear runs, then report on closure.
/// </summary>
/// <remarks>
/// Two rules shape every step. Nothing is guessed: a gap wider than the tolerance becomes an issue
/// for the user instead of an automatic fix, and no step invents a fillet or a corner the model does
/// not have. Nothing is lost: every change is written to the repair log with the geometry it
/// replaced and the distance it moved, so a finished boundary can always be explained back to the
/// elements it came from.
/// <para>
/// Column outlines are the one input that never becomes an edge. Spec 10.1 reads them 用於判斷遮斷
/// 與補線: they tell the repair that a wall stopping at a column was cut by it, and the wall is carried
/// through to where the centrelines meet. The boundary, and so the 單線圖, follows wall centrelines
/// only.
/// </para>
/// </remarks>
public sealed class LineNetworkRepairer
{
    /// <summary>
    /// How far past the extension tolerance a dangling end still looks for a neighbour, only so the
    /// issue can quote the distance the user has to close. Purely a reporting window: nothing inside
    /// it is repaired.
    /// </summary>
    public const double GapReportingFactor = 10.0;

    public Result<PlanLineNetwork> Repair(PlanGeometrySnapshot snapshot, DateTime? repairedAtUtc = null)
    {
        if (snapshot is null) throw new ArgumentNullException(nameof(snapshot));
        return new Run(snapshot).Execute(repairedAtUtc);
    }

    // One segment while it is being repaired. Mutable on purpose: the pipeline moves endpoints and
    // absorbs sources in place, and the log records what each move replaced.
    private sealed class WorkEdge
    {
        public WorkEdge(Point2D start, Point2D end, IEnumerable<SourceRef> sources)
        {
            Start = start;
            End = end;
            Sources = sources.ToList();
        }

        public Point2D Start;
        public Point2D End;
        public List<SourceRef> Sources;
        public bool Removed;
        public int StartNode = -1;
        public int EndNode = -1;

        public double LengthFeet => Start.DistanceTo(End);
        public double DirectionRadians => SegmentGeometry.DirectionRadians(Start, End);

        public void AbsorbSources(IEnumerable<SourceRef> sources)
        {
            foreach (var source in sources)
            {
                if (!Sources.Contains(source)) Sources.Add(source);
            }
        }

        public int OtherNode(int nodeId) => nodeId == StartNode ? EndNode : StartNode;

        public Point2D PointAtNode(int nodeId) => nodeId == StartNode ? Start : End;

        public void MoveNodeTo(int nodeId, int newNodeId, Point2D position)
        {
            if (nodeId == StartNode)
            {
                StartNode = newNodeId;
                Start = position;
            }
            else
            {
                EndNode = newNodeId;
                End = position;
            }
        }
    }

    // One column's plan silhouette, possibly several rings. Containment is even-odd over all of its
    // segments together, which needs no ring order and treats a hollow column the way it looks.
    private sealed class ColumnOutline
    {
        private readonly IReadOnlyList<Segment2D> _segments;
        private readonly double _minX;
        private readonly double _minY;
        private readonly double _maxX;
        private readonly double _maxY;

        public ColumnOutline(SourceRef source, IReadOnlyList<Segment2D> segments)
        {
            Source = source;
            _segments = segments;
            _minX = segments.Min(s => Math.Min(s.Start.X, s.End.X));
            _minY = segments.Min(s => Math.Min(s.Start.Y, s.End.Y));
            _maxX = segments.Max(s => Math.Max(s.Start.X, s.End.X));
            _maxY = segments.Max(s => Math.Max(s.Start.Y, s.End.Y));
        }

        public SourceRef Source { get; }

        /// <summary>The diagonal of the column's box: no end is carried further than this.</summary>
        public double ReachFeet => new Point2D(_minX, _minY).DistanceTo(new Point2D(_maxX, _maxY));

        public bool IsNear(Point2D point, double slackFeet) =>
            point.X >= _minX - slackFeet && point.X <= _maxX + slackFeet &&
            point.Y >= _minY - slackFeet && point.Y <= _maxY + slackFeet;

        /// <summary>Inside the outline, or on it within <paramref name="slackFeet"/>.</summary>
        public bool Holds(Point2D point, double slackFeet)
        {
            if (!IsNear(point, slackFeet)) return false;

            var inside = false;
            foreach (var segment in _segments)
            {
                if (SegmentGeometry.DistanceToSegment(point, segment.Start, segment.End, out _, out _) <= slackFeet) return true;

                var a = segment.Start;
                var b = segment.End;
                if ((a.Y > point.Y) != (b.Y > point.Y) &&
                    point.X < a.X + ((point.Y - a.Y) * (b.X - a.X) / (b.Y - a.Y)))
                {
                    inside = !inside;
                }
            }

            return inside;
        }
    }

    // A wall end sitting in a column, with the direction the wall was heading when it got there.
    private readonly struct ColumnEnd
    {
        public ColumnEnd(WorkEdge edge, bool atStart)
        {
            Edge = edge;
            AtStart = atStart;
            Point = atStart ? edge.Start : edge.End;
            Far = atStart ? edge.End : edge.Start;
            var length = Far.DistanceTo(Point);
            DirX = (Point.X - Far.X) / length;
            DirY = (Point.Y - Far.Y) / length;
        }

        public WorkEdge Edge { get; }
        public bool AtStart { get; }
        public Point2D Point { get; }
        public Point2D Far { get; }
        public double DirX { get; }
        public double DirY { get; }
    }

    private sealed class Run
    {
        private readonly PlanGeometrySnapshot _snapshot;
        private readonly GeometryTolerance _tolerance;
        private readonly List<WorkEdge> _edges;
        private readonly List<ColumnOutline> _columns;
        private readonly List<NetworkRepair> _repairs = new List<NetworkRepair>();
        private readonly List<NetworkIssue> _issues = new List<NetworkIssue>();
        private readonly List<Point2D> _nodes = new List<Point2D>();
        private readonly Dictionary<(long X, long Y), List<int>> _nodeGrid = new Dictionary<(long X, long Y), List<int>>();

        public Run(PlanGeometrySnapshot snapshot)
        {
            _snapshot = snapshot;
            _tolerance = snapshot.Tolerance;

            // Column outlines never become edges: a 區劃 boundary runs along wall centrelines, and
            // a column silhouette in the network would put a notch around every column into the
            // boundary lines and the 單線圖. They are kept aside, grouped per column, only to tell
            // which wall ends stopped at a column and may be carried through it.
            _edges = snapshot.Segments
                .Where(s => s.Source.Kind != GeometrySourceKind.ColumnOutline)
                .Select(s => new WorkEdge(s.Start, s.End, new[] { s.Source }))
                .ToList();
            _columns = snapshot.Segments
                .Where(s => s.Source.Kind == GeometrySourceKind.ColumnOutline)
                .GroupBy(s => s.Source)
                .Select(g => new ColumnOutline(g.Key, g.ToList()))
                .ToList();
        }

        public Result<PlanLineNetwork> Execute(DateTime? repairedAtUtc)
        {
            JoinWallsThroughColumns();
            RemoveDuplicates();
            SplitAtIntersections();
            SnapEndpoints();
            ExtendShortGaps();
            MergeCollinear();
            return Compose(repairedAtUtc);
        }

        // Step 0: a wall drawn up to a column stops at the column face, so its centreline never
        // reaches the walls on the other sides. Every end that stops inside a column is carried to
        // the point where the centrelines meet, which is where the 單線圖 draws the corner. Only
        // ends inside a column move, and never further than the column reaches: a gap in open floor
        // is still step 4's to judge.
        private void JoinWallsThroughColumns()
        {
            foreach (var column in _columns)
            {
                var ends = new List<ColumnEnd>();
                foreach (var edge in _edges)
                {
                    if (edge.Removed) continue;

                    // A piece lying wholly inside the column has no direction worth following, and
                    // one passing straight through it is already continuous.
                    var startInside = column.Holds(edge.Start, _tolerance.SnapFeet);
                    var endInside = column.Holds(edge.End, _tolerance.SnapFeet);
                    if (startInside == endInside) continue;
                    if (edge.LengthFeet <= GeometryTolerance.ZeroLengthFeet) continue;

                    ends.Add(new ColumnEnd(edge, startInside));
                }

                if (ends.Count == 0 || !TryFindJunction(column, ends, out var junction)) continue;

                foreach (var end in ends) CarryEnd(end, junction, column);
            }
        }

        // Where the walls meeting at a column meet each other. The widest-angled pair of ends that
        // are not collinear decides it, because that pair pins the point down most firmly. A wall
        // run the column merely interrupts meets in the middle; a lone stub follows its own
        // direction to the first line crossing the column. Anything else is left for step 4.
        private bool TryFindJunction(ColumnOutline column, List<ColumnEnd> ends, out Point2D junction)
        {
            junction = default;
            var found = false;
            var bestSine = Math.Sin(_tolerance.CollinearRadians);

            for (var i = 0; i < ends.Count; i++)
            {
                for (var j = i + 1; j < ends.Count; j++)
                {
                    var a = ends[i];
                    var b = ends[j];
                    var sine = Math.Abs((a.DirX * b.DirY) - (a.DirY * b.DirX));
                    if (sine <= bestSine) continue;

                    var t = (((b.Point.X - a.Point.X) * b.DirY) - ((b.Point.Y - a.Point.Y) * b.DirX)) /
                            ((a.DirX * b.DirY) - (a.DirY * b.DirX));
                    var meeting = new Point2D(a.Point.X + (a.DirX * t), a.Point.Y + (a.DirY * t));
                    if (!column.IsNear(meeting, _tolerance.SnapFeet)) continue;

                    bestSine = sine;
                    junction = meeting;
                    found = true;
                }
            }

            if (found) return true;

            if (ends.Count == 2)
            {
                // Collinear by now. Only a pair facing each other is one wall broken by the column;
                // two walls arriving side by side from the same direction share no junction.
                var first = ends[0];
                var second = ends[1];
                if ((first.DirX * second.DirX) + (first.DirY * second.DirY) >= 0) return false;

                junction = new Point2D((first.Point.X + second.Point.X) / 2.0, (first.Point.Y + second.Point.Y) / 2.0);
                return true;
            }

            if (ends.Count != 1) return false;

            var lone = ends[0];
            var reach = column.ReachFeet + _tolerance.SnapFeet;
            var nearest = double.MaxValue;
            foreach (var candidate in _edges)
            {
                if (candidate.Removed || ReferenceEquals(candidate, lone.Edge)) continue;
                if (!SegmentGeometry.TryRayHitSegment(lone.Point, lone.DirX, lone.DirY, reach,
                        candidate.Start, candidate.End, out var distance, out _, out var hit))
                {
                    continue;
                }

                if (distance >= nearest || !column.IsNear(hit, _tolerance.SnapFeet)) continue;

                nearest = distance;
                junction = hit;
                found = true;
            }

            return found;
        }

        // Moves one end along its own centreline to the junction. A wall whose centreline misses the
        // junction by more than the snap tolerance stops level with it and gets a short connector
        // inside the column, so the move stays on the wall's own line and the jog is logged.
        private void CarryEnd(ColumnEnd end, Point2D junction, ColumnOutline column)
        {
            var edge = end.Edge;
            var point = end.Point;
            var far = end.Far;
            var along = ((junction.X - far.X) * end.DirX) + ((junction.Y - far.Y) * end.DirY);

            // Never fold the wall back over itself.
            if (along <= _tolerance.SnapFeet) return;

            var lateral = Math.Abs(((junction.X - far.X) * -end.DirY) + ((junction.Y - far.Y) * end.DirX));
            var target = lateral <= _tolerance.SnapFeet
                ? junction
                : new Point2D(far.X + (end.DirX * along), far.Y + (end.DirY * along));

            var originalStart = edge.Start;
            var originalEnd = edge.End;
            if (end.AtStart) edge.Start = target;
            else edge.End = target;

            var sources = edge.Sources.Concat(new[] { column.Source }).ToList();
            var moved = point.DistanceTo(target);
            if (moved > GeometryTolerance.ZeroLengthFeet)
            {
                _repairs.Add(new NetworkRepair(
                    NetworkRepairKind.JoinedThroughColumn,
                    originalStart,
                    originalEnd,
                    edge.Start,
                    edge.End,
                    moved,
                    sources,
                    $"端點停在柱內，已沿牆心線延伸 {Mm(moved)} mm 至牆心線交點；柱輪廓不列入區劃邊界。"));
            }

            if (target.DistanceTo(junction) <= GeometryTolerance.ZeroLengthFeet) return;

            _edges.Add(new WorkEdge(target, junction, sources));
            _repairs.Add(new NetworkRepair(
                NetworkRepairKind.JoinedThroughColumn,
                target,
                target,
                target,
                junction,
                target.DistanceTo(junction),
                sources,
                $"牆心線與交點錯開 {Mm(lateral)} mm，已在柱內補一段連接線。"));
        }

        // Step 1: the same wall drawn twice, or a wall and an auxiliary line laid over it. Both are
        // collinear within tolerance and genuinely overlap, so the survivor takes the union of the
        // two spans and keeps both sources.
        private void RemoveDuplicates()
        {
            var changed = true;
            while (changed)
            {
                changed = false;
                for (var i = 0; i < _edges.Count; i++)
                {
                    var keeper = _edges[i];
                    if (keeper.Removed) continue;

                    for (var j = i + 1; j < _edges.Count; j++)
                    {
                        var candidate = _edges[j];
                        if (candidate.Removed) continue;
                        if (!BoxesOverlap(keeper, candidate, _tolerance.DuplicateFeet)) continue;
                        if (!SegmentGeometry.TryMeasureCollinearOverlap(
                                keeper.Start, keeper.End, candidate.Start, candidate.End,
                                _tolerance, _tolerance.DuplicateFeet, out _, out var lateral))
                        {
                            continue;
                        }

                        var absorbed = candidate.Sources.ToList();
                        var originalStart = candidate.Start;
                        var originalEnd = candidate.End;

                        ExtendOverAxis(keeper, candidate);
                        keeper.AbsorbSources(absorbed);
                        candidate.Removed = true;

                        _repairs.Add(new NetworkRepair(
                            NetworkRepairKind.DuplicateRemoved,
                            originalStart,
                            originalEnd,
                            keeper.Start,
                            keeper.End,
                            lateral,
                            absorbed.Concat(new[] { keeper.Sources[0] }),
                            $"與既有線重疊，已合併為同一段（偏差 {Mm(lateral)} mm）。"));
                        changed = true;
                    }
                }
            }
        }

        // Grows the keeper along its own axis so it spans both segments. Projecting rather than
        // taking the candidate's endpoints directly keeps the result exactly on the keeper's line;
        // the lateral difference was already measured and logged.
        private static void ExtendOverAxis(WorkEdge keeper, WorkEdge candidate)
        {
            var length = keeper.LengthFeet;
            if (length <= GeometryTolerance.ZeroLengthFeet) return;

            var ux = (keeper.End.X - keeper.Start.X) / length;
            var uy = (keeper.End.Y - keeper.Start.Y) / length;
            var first = SegmentGeometry.ProjectOntoAxis(candidate.Start, keeper.Start, keeper.End);
            var second = SegmentGeometry.ProjectOntoAxis(candidate.End, keeper.Start, keeper.End);

            var low = Math.Min(0, Math.Min(first, second));
            var high = Math.Max(length, Math.Max(first, second));
            var origin = keeper.Start;

            keeper.Start = new Point2D(origin.X + (ux * low), origin.Y + (uy * low));
            keeper.End = new Point2D(origin.X + (ux * high), origin.Y + (uy * high));
        }

        // Step 2: every crossing becomes a shared point. Crossings that land on an endpoint are left
        // alone: snapping handles those, and splitting there would only create a sliver.
        private void SplitAtIntersections()
        {
            var splits = new List<double>[_edges.Count];
            for (var i = 0; i < _edges.Count; i++) splits[i] = new List<double>();

            for (var i = 0; i < _edges.Count; i++)
            {
                var a = _edges[i];
                if (a.Removed) continue;

                for (var j = i + 1; j < _edges.Count; j++)
                {
                    var b = _edges[j];
                    if (b.Removed) continue;
                    if (!BoxesOverlap(a, b, _tolerance.SnapFeet)) continue;
                    if (!SegmentGeometry.TryIntersect(a.Start, a.End, b.Start, b.End, _tolerance.SnapFeet,
                            out var tA, out var tB, out var point))
                    {
                        continue;
                    }

                    var splitA = TryRecordSplit(splits[i], a, tA);
                    var splitB = TryRecordSplit(splits[j], b, tB);

                    // Two pieces of the same element crossing in their interiors means the source
                    // geometry crosses itself. The split keeps the network planar, but the model is
                    // still wrong, so the reviewer is told where.
                    if (splitA && splitB && a.Sources[0].Equals(b.Sources[0]))
                    {
                        _issues.Add(new NetworkIssue(
                            NetworkIssueKind.SelfIntersection,
                            NetworkIssueSeverity.Warning,
                            point,
                            0,
                            a.Sources.Concat(b.Sources),
                            "同一個元素的幾何在此自交，已在交點分割，請確認來源圖元是否正確。"));
                    }
                }
            }

            var rebuilt = new List<WorkEdge>(_edges.Count);
            for (var i = 0; i < _edges.Count; i++)
            {
                var edge = _edges[i];
                if (edge.Removed) continue;
                if (splits[i].Count == 0)
                {
                    rebuilt.Add(edge);
                    continue;
                }

                splits[i].Sort();
                var cuts = new List<double> { 0 };
                cuts.AddRange(splits[i]);
                cuts.Add(1);

                for (var k = 0; k < cuts.Count - 1; k++)
                {
                    var start = SegmentGeometry.PointAt(edge.Start, edge.End, cuts[k]);
                    var end = SegmentGeometry.PointAt(edge.Start, edge.End, cuts[k + 1]);
                    if (start.DistanceTo(end) <= GeometryTolerance.ZeroLengthFeet) continue;
                    rebuilt.Add(new WorkEdge(start, end, edge.Sources));
                }

                foreach (var t in splits[i])
                {
                    var point = SegmentGeometry.PointAt(edge.Start, edge.End, t);
                    _repairs.Add(new NetworkRepair(
                        NetworkRepairKind.IntersectionSplit,
                        edge.Start,
                        edge.End,
                        point,
                        point,
                        0,
                        edge.Sources,
                        "在與其他線的交點處分割。"));
                }
            }

            _edges.Clear();
            _edges.AddRange(rebuilt);
        }

        private bool TryRecordSplit(List<double> splits, WorkEdge edge, double t)
        {
            var length = edge.LengthFeet;
            if (length <= GeometryTolerance.ZeroLengthFeet) return false;

            var fromStart = t * length;
            var fromEnd = (1 - t) * length;
            if (fromStart <= _tolerance.SnapFeet || fromEnd <= _tolerance.SnapFeet) return false;

            var minimumSpacing = _tolerance.SnapFeet / length;
            if (splits.Any(existing => Math.Abs(existing - t) <= minimumSpacing)) return false;

            splits.Add(t);
            return true;
        }

        // Step 3: endpoints within the snap tolerance become one node, which is what turns a pile of
        // segments into a graph. Clustering walks a deterministic order so the same model always
        // produces the same node set.
        private void SnapEndpoints()
        {
            foreach (var edge in _edges)
            {
                if (edge.Removed) continue;

                var originalStart = edge.Start;
                var originalEnd = edge.End;

                edge.StartNode = ResolveNode(edge.Start);
                edge.Start = _nodes[edge.StartNode];
                edge.EndNode = ResolveNode(edge.End);
                edge.End = _nodes[edge.EndNode];

                RecordSnap(edge, originalStart, originalEnd, originalStart, edge.Start);
                RecordSnap(edge, originalStart, originalEnd, originalEnd, edge.End);

                if (edge.StartNode == edge.EndNode)
                {
                    // Both ends landed on one node: the piece was shorter than the snap tolerance.
                    edge.Removed = true;
                    _issues.Add(new NetworkIssue(
                        NetworkIssueKind.CollapsedSegment,
                        NetworkIssueSeverity.Warning,
                        edge.Start,
                        originalStart.DistanceTo(originalEnd),
                        edge.Sources,
                        $"長度 {Mm(originalStart.DistanceTo(originalEnd))} mm 的線段小於吸附容差，已併入同一節點。"));
                }
            }

            RemoveParallelEdges();
        }

        private void RecordSnap(WorkEdge edge, Point2D originalStart, Point2D originalEnd, Point2D before, Point2D after)
        {
            var moved = before.DistanceTo(after);
            if (moved <= GeometryTolerance.ZeroLengthFeet) return;

            _repairs.Add(new NetworkRepair(
                NetworkRepairKind.EndpointSnapped,
                originalStart,
                originalEnd,
                edge.Start,
                edge.End,
                moved,
                edge.Sources,
                $"端點吸附至共用節點（移動 {Mm(moved)} mm）。"));
        }

        private int ResolveNode(Point2D point)
        {
            var cellX = CellIndex(point.X);
            var cellY = CellIndex(point.Y);

            var best = -1;
            var bestDistance = double.MaxValue;
            for (var dx = -1; dx <= 1; dx++)
            {
                for (var dy = -1; dy <= 1; dy++)
                {
                    if (!_nodeGrid.TryGetValue((cellX + dx, cellY + dy), out var candidates)) continue;
                    foreach (var id in candidates)
                    {
                        var distance = point.DistanceTo(_nodes[id]);
                        if (distance <= _tolerance.SnapFeet && distance < bestDistance)
                        {
                            best = id;
                            bestDistance = distance;
                        }
                    }
                }
            }

            if (best >= 0) return best;

            var newId = _nodes.Count;
            _nodes.Add(point);
            var key = (cellX, cellY);
            if (!_nodeGrid.TryGetValue(key, out var bucket))
            {
                bucket = new List<int>();
                _nodeGrid[key] = bucket;
            }

            bucket.Add(newId);
            return newId;
        }

        // Cells are one snap tolerance wide, so any node within the tolerance is in the 3x3
        // neighbourhood of the point being resolved.
        private long CellIndex(double value) => (long)Math.Floor(value / _tolerance.SnapFeet);

        // Snapping can leave two straight edges between the same pair of nodes, which are the same
        // line by definition. Folding them keeps the loop count honest.
        private void RemoveParallelEdges()
        {
            var seen = new Dictionary<(int Low, int High), WorkEdge>();
            foreach (var edge in _edges)
            {
                if (edge.Removed) continue;

                var key = (Math.Min(edge.StartNode, edge.EndNode), Math.Max(edge.StartNode, edge.EndNode));
                if (!seen.TryGetValue(key, out var keeper))
                {
                    seen[key] = edge;
                    continue;
                }

                var absorbed = edge.Sources.ToList();
                keeper.AbsorbSources(absorbed);
                edge.Removed = true;
                _repairs.Add(new NetworkRepair(
                    NetworkRepairKind.DuplicateRemoved,
                    edge.Start,
                    edge.End,
                    keeper.Start,
                    keeper.End,
                    0,
                    absorbed.Concat(new[] { keeper.Sources[0] }),
                    "吸附後與既有線共用兩端節點，已合併為同一段。"));
            }
        }

        // Step 4: a dangling end may travel along its own direction, no further than the extension
        // tolerance, to reach a line it was clearly meant to meet. Anything else is reported, never
        // guessed: no fillet, no sideways move, no corner the model does not have.
        private void ExtendShortGaps()
        {
            var incidence = BuildIncidence();
            var dangling = incidence
                .Where(pair => pair.Value.Count == 1)
                .Select(pair => pair.Key)
                .OrderBy(id => id)
                .ToList();

            foreach (var nodeId in dangling)
            {
                if (!incidence.TryGetValue(nodeId, out var edges) || edges.Count != 1) continue;

                var edge = edges[0];
                if (edge.Removed) continue;

                var origin = _nodes[nodeId];
                var far = _nodes[edge.OtherNode(nodeId)];
                var length = far.DistanceTo(origin);
                if (length <= GeometryTolerance.ZeroLengthFeet) continue;

                var dirX = (origin.X - far.X) / length;
                var dirY = (origin.Y - far.Y) / length;

                if (TryExtend(nodeId, edge, origin, dirX, dirY, incidence)) continue;

                ReportUnreachableEnd(nodeId, edge, origin);
            }
        }

        private bool TryExtend(int nodeId, WorkEdge edge, Point2D origin, double dirX, double dirY, Dictionary<int, List<WorkEdge>> incidence)
        {
            var bestDistance = double.MaxValue;
            var bestNode = -1;
            WorkEdge? bestTarget = null;
            var bestPoint = origin;

            // An existing junction just off the ray is the better answer when there is one: it keeps
            // the node count down and avoids splitting a line a hair away from its own end.
            foreach (var pair in incidence)
            {
                if (pair.Key == nodeId || pair.Value.Count == 0) continue;
                if (pair.Value.Contains(edge)) continue;

                var target = _nodes[pair.Key];
                var along = ((target.X - origin.X) * dirX) + ((target.Y - origin.Y) * dirY);
                if (along <= GeometryTolerance.ZeroLengthFeet || along > _tolerance.GapExtensionFeet) continue;

                var lateral = Math.Abs(((target.X - origin.X) * -dirY) + ((target.Y - origin.Y) * dirX));
                if (lateral > _tolerance.SnapFeet) continue;

                var distance = origin.DistanceTo(target);
                if (distance >= bestDistance) continue;

                bestDistance = distance;
                bestNode = pair.Key;
                bestTarget = null;
                bestPoint = target;
            }

            foreach (var candidate in _edges)
            {
                if (candidate.Removed || ReferenceEquals(candidate, edge)) continue;
                if (candidate.StartNode == nodeId || candidate.EndNode == nodeId) continue;

                if (!SegmentGeometry.TryRayHitSegment(origin, dirX, dirY, _tolerance.GapExtensionFeet,
                        candidate.Start, candidate.End, out var distance, out _, out var hit))
                {
                    continue;
                }

                if (distance <= GeometryTolerance.ZeroLengthFeet || distance >= bestDistance) continue;

                bestDistance = distance;
                bestNode = -1;
                bestTarget = candidate;
                bestPoint = hit;
            }

            if (bestNode < 0 && bestTarget is null) return false;

            var targetNode = bestNode >= 0 ? bestNode : SplitAt(bestTarget!, bestPoint, incidence);

            // Landing on this edge's own far node would fold it into nothing, which is not a repair.
            if (targetNode == nodeId || targetNode == edge.OtherNode(nodeId)) return false;

            edge.MoveNodeTo(nodeId, targetNode, _nodes[targetNode]);
            incidence[nodeId].Remove(edge);
            incidence[targetNode].Add(edge);

            _repairs.Add(new NetworkRepair(
                NetworkRepairKind.GapExtended,
                origin,
                origin,
                _nodes[targetNode],
                _nodes[targetNode],
                bestDistance,
                edge.Sources,
                $"沿原方向延伸 {Mm(bestDistance)} mm 以接合缺口（容差 {Mm(_tolerance.GapExtensionFeet)} mm）。"));
            return true;
        }

        private int SplitAt(WorkEdge target, Point2D point, Dictionary<int, List<WorkEdge>> incidence)
        {
            var nodeId = ResolveNode(point);
            var position = _nodes[nodeId];
            if (nodeId == target.StartNode || nodeId == target.EndNode) return nodeId;

            var tailNode = target.EndNode;
            var tail = new WorkEdge(position, target.End, target.Sources)
            {
                StartNode = nodeId,
                EndNode = tailNode
            };

            target.End = position;
            target.EndNode = nodeId;
            _edges.Add(tail);

            incidence[tailNode].Remove(target);
            incidence[tailNode].Add(tail);
            if (!incidence.TryGetValue(nodeId, out var bucket))
            {
                bucket = new List<WorkEdge>();
                incidence[nodeId] = bucket;
            }

            bucket.Add(target);
            bucket.Add(tail);

            _repairs.Add(new NetworkRepair(
                NetworkRepairKind.IntersectionSplit,
                target.Start,
                tail.End,
                position,
                position,
                0,
                target.Sources,
                "在延伸接合點處分割被接上的線。"));
            return nodeId;
        }

        private void ReportUnreachableEnd(int nodeId, WorkEdge edge, Point2D origin)
        {
            var nearest = double.MaxValue;
            var window = _tolerance.GapExtensionFeet * GapReportingFactor;
            List<SourceRef> nearestSources = edge.Sources;

            foreach (var candidate in _edges)
            {
                if (candidate.Removed || ReferenceEquals(candidate, edge)) continue;
                if (candidate.StartNode == nodeId || candidate.EndNode == nodeId) continue;

                var distance = SegmentGeometry.DistanceToSegment(origin, candidate.Start, candidate.End, out _, out _);
                if (distance >= nearest) continue;

                nearest = distance;
                nearestSources = edge.Sources.Concat(candidate.Sources).ToList();
            }

            if (nearest <= window)
            {
                _issues.Add(new NetworkIssue(
                    NetworkIssueKind.GapBeyondTolerance,
                    NetworkIssueSeverity.Error,
                    origin,
                    nearest,
                    nearestSources,
                    $"端點距離最近的線 {Mm(nearest)} mm，超過 {Mm(_tolerance.GapExtensionFeet)} mm 的延伸容差，需人工確認要如何接合。"));
                return;
            }

            _issues.Add(new NetworkIssue(
                NetworkIssueKind.DanglingEnd,
                NetworkIssueSeverity.Warning,
                origin,
                0,
                edge.Sources,
                "端點未與任何其他線相接，此線不會構成區劃邊界。"));
        }

        // Step 5: a wall cut into pieces by doors and intersections that turned out not to matter
        // becomes one edge again. Only a node of degree two can merge, so a real junction is never
        // dissolved.
        private void MergeCollinear()
        {
            var changed = true;
            while (changed)
            {
                changed = false;
                var incidence = BuildIncidence();

                foreach (var nodeId in incidence.Keys.OrderBy(id => id).ToList())
                {
                    var edges = incidence[nodeId];
                    if (edges.Count != 2) continue;

                    var first = edges[0];
                    var second = edges[1];
                    if (first.Removed || second.Removed) continue;

                    var farFirst = first.OtherNode(nodeId);
                    var farSecond = second.OtherNode(nodeId);
                    if (farFirst == farSecond) continue;
                    if (!_tolerance.AreCollinearDirections(first.DirectionRadians, second.DirectionRadians)) continue;
                    if (incidence[farFirst].Any(e => !e.Removed && !ReferenceEquals(e, first) && e.OtherNode(farFirst) == farSecond)) continue;

                    var pivot = _nodes[nodeId];
                    var startPoint = first.PointAtNode(farFirst);
                    var endPoint = second.PointAtNode(farSecond);
                    var deviation = SegmentGeometry.DistanceToLine(pivot, startPoint, endPoint);

                    var absorbed = second.Sources.ToList();
                    first.Start = startPoint;
                    first.StartNode = farFirst;
                    first.End = endPoint;
                    first.EndNode = farSecond;
                    first.AbsorbSources(absorbed);
                    second.Removed = true;

                    edges.Clear();
                    incidence[farSecond].Remove(second);
                    incidence[farSecond].Add(first);

                    _repairs.Add(new NetworkRepair(
                        NetworkRepairKind.CollinearMerged,
                        pivot,
                        pivot,
                        startPoint,
                        endPoint,
                        deviation,
                        absorbed.Concat(first.Sources),
                        $"共線的兩段在此節點合併為一段（偏移 {Mm(deviation)} mm）。"));
                    changed = true;
                }
            }
        }

        private Dictionary<int, List<WorkEdge>> BuildIncidence()
        {
            var incidence = new Dictionary<int, List<WorkEdge>>();
            foreach (var edge in _edges)
            {
                if (edge.Removed) continue;
                Add(incidence, edge.StartNode, edge);
                Add(incidence, edge.EndNode, edge);
            }

            return incidence;

            static void Add(Dictionary<int, List<WorkEdge>> map, int nodeId, WorkEdge edge)
            {
                if (!map.TryGetValue(nodeId, out var list))
                {
                    list = new List<WorkEdge>();
                    map[nodeId] = list;
                }

                list.Add(edge);
            }
        }

        // Step 6: the network is handed over with its closure stated. Tracing the actual faces is
        // P2-T04; what matters here is whether anything encloses an area at all.
        private Result<PlanLineNetwork> Compose(DateTime? repairedAtUtc)
        {
            var alive = _edges.Where(e => !e.Removed).ToList();
            if (alive.Count == 0)
            {
                return Result.Failure<PlanLineNetwork>(new Error(
                    "geometry.repair.empty",
                    "線網修復後沒有剩下任何線段，無法建立區劃邊界。",
                    BuildAccountingDetail()));
            }

            // Node IDs are compacted so the graph has no orphans left by merging, and edges are put
            // in a canonical order so two runs over the same model produce identical networks.
            var remap = new Dictionary<int, int>();
            var nodes = new List<NetworkNode>();
            foreach (var oldId in alive.SelectMany(e => new[] { e.StartNode, e.EndNode }).Distinct().OrderBy(id => id))
            {
                remap[oldId] = nodes.Count;
                nodes.Add(new NetworkNode(nodes.Count, _nodes[oldId]));
            }

            var edges = alive
                .Select(e => new NetworkEdge(remap[e.StartNode], remap[e.EndNode], e.Start, e.End, e.Sources))
                .OrderBy(e => Math.Min(e.StartNodeId, e.EndNodeId))
                .ThenBy(e => Math.Max(e.StartNodeId, e.EndNodeId))
                .ToList();

            var network = new PlanLineNetwork(
                _snapshot.PackageId,
                _snapshot.HostDocumentUniqueId,
                _snapshot.LevelUniqueId,
                nodes,
                edges,
                _tolerance,
                _repairs,
                _issues,
                _snapshot.Extent,
                repairedAtUtc);

            if (!network.HasClosedLoop)
            {
                var withClosure = new PlanLineNetwork(
                    _snapshot.PackageId,
                    _snapshot.HostDocumentUniqueId,
                    _snapshot.LevelUniqueId,
                    nodes,
                    edges,
                    _tolerance,
                    _repairs,
                    _issues.Concat(new[]
                    {
                        new NetworkIssue(
                            NetworkIssueKind.NoClosedLoop,
                            NetworkIssueSeverity.Error,
                            nodes[0].Position,
                            0,
                            edges.SelectMany(e => e.Sources).Take(1),
                            "修復後的線網沒有任何封閉範圍，無法求解區劃，請補足缺口或加入輔助線。")
                    }),
                    _snapshot.Extent,
                    repairedAtUtc);
                return Result.Success(withClosure);
            }

            return Result.Success(network);
        }

        private string BuildAccountingDetail() =>
            $"inputSegments={_snapshot.Segments.Count}; repairs={_repairs.Count}; issues={_issues.Count}; " +
            $"snapMm={Mm(_tolerance.SnapFeet)}; gapMm={Mm(_tolerance.GapExtensionFeet)}";

        private static bool BoxesOverlap(WorkEdge a, WorkEdge b, double toleranceFeet) =>
            Math.Min(a.Start.X, a.End.X) - toleranceFeet <= Math.Max(b.Start.X, b.End.X) &&
            Math.Max(a.Start.X, a.End.X) + toleranceFeet >= Math.Min(b.Start.X, b.End.X) &&
            Math.Min(a.Start.Y, a.End.Y) - toleranceFeet <= Math.Max(b.Start.Y, b.End.Y) &&
            Math.Max(a.Start.Y, a.End.Y) + toleranceFeet >= Math.Min(b.Start.Y, b.End.Y);

        private static string Mm(double feet) =>
            PlanUnits.FeetToMillimeters(feet).ToString("0.##", CultureInfo.InvariantCulture);
    }
}
