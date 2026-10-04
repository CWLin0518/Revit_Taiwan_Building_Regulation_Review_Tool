using System;
using System.Collections.Generic;
using System.Linq;
using BuildingRegulationReview.Domain.Common;
using BuildingRegulationReview.Domain.Geometry;

namespace BuildingRegulationReview.Application.Geometry;

// Everything the P2-T02 adapter does once a Revit curve has been reduced to plan points: drop
// degenerate pieces, clip to the view scope, keep provenance, and account for what was discarded.
// Living in Application rather than in the adapter keeps these rules under test without Revit.
public sealed class PlanGeometryBuilder
{
    private readonly PlanGeometryExtractionRequest _request;
    private readonly string _hostDocumentUniqueId;
    private readonly string _levelUniqueId;
    private readonly List<Segment2D> _segments = new List<Segment2D>();
    private readonly List<string> _warnings = new List<string>();
    private readonly HashSet<string> _seenWarnings = new HashSet<string>(StringComparer.Ordinal);

    private int _degenerateCount;
    private int _outsideExtentCount;
    private int _clippedCount;

    public PlanGeometryBuilder(PlanGeometryExtractionRequest request, string hostDocumentUniqueId, string levelUniqueId)
    {
        _request = request ?? throw new ArgumentNullException(nameof(request));
        if (string.IsNullOrWhiteSpace(hostDocumentUniqueId))
            throw new ArgumentException("Host document identity is required.", nameof(hostDocumentUniqueId));
        if (string.IsNullOrWhiteSpace(levelUniqueId))
            throw new ArgumentException("Level UniqueId is required.", nameof(levelUniqueId));

        _hostDocumentUniqueId = hostDocumentUniqueId.Trim();
        _levelUniqueId = levelUniqueId.Trim();
    }

    public GeometryTolerance Tolerance => _request.Options.Tolerance;
    public int SegmentCount => _segments.Count;
    public int DegenerateCount => _degenerateCount;
    public int OutsideExtentCount => _outsideExtentCount;
    public int ClippedCount => _clippedCount;

    /// <summary>The crop region or scope box the adapter resolved; null means the whole level.</summary>
    public PlanExtent2D? Extent { get; private set; }

    /// <summary>Clipping only applies when the caller asked to honour the view scope (spec 10.1).</summary>
    public bool ClipsToExtent => Extent is not null && _request.Options.RestrictToViewExtent;

    public void UseExtent(PlanExtent2D? extent) => Extent = extent;

    public void AddWarning(string message)
    {
        if (string.IsNullOrWhiteSpace(message)) return;
        var trimmed = message.Trim();
        if (_seenWarnings.Add(trimmed)) _warnings.Add(trimmed);
    }

    /// <summary>
    /// Adds one open or closed polyline, already tessellated and transformed into host plan
    /// coordinates. Returns how many segments survived clipping.
    /// </summary>
    public int AddPolyline(IEnumerable<Point2D> points, SourceRef source, bool closed = false)
    {
        if (points is null) throw new ArgumentNullException(nameof(points));
        if (source is null) throw new ArgumentNullException(nameof(source));
        if (!_request.Options.Includes(source.Kind))
            throw new ArgumentException("This source kind was excluded from the extraction options.", nameof(source));

        var vertices = points.ToArray();
        if (vertices.Length < 2) return 0;

        var added = 0;
        var lastIndex = closed ? vertices.Length : vertices.Length - 1;
        for (var i = 0; i < lastIndex; i++)
            added += AddSegment(vertices[i], vertices[(i + 1) % vertices.Length], source);

        return added;
    }

    public int AddSegment(Point2D start, Point2D end, SourceRef source)
    {
        if (source is null) throw new ArgumentNullException(nameof(source));
        if (!_request.Options.Includes(source.Kind))
            throw new ArgumentException("This source kind was excluded from the extraction options.", nameof(source));

        if (IsDegenerate(start, end))
        {
            _degenerateCount++;
            return 0;
        }

        if (ClipsToExtent)
        {
            if (!PlanExtentClipper.TryClip(start, end, Extent!, Tolerance.SnapFeet, out var clippedStart, out var clippedEnd))
            {
                _outsideExtentCount++;
                return 0;
            }

            var wasClipped = !IsDegenerate(start, clippedStart) || !IsDegenerate(end, clippedEnd);
            if (IsDegenerate(clippedStart, clippedEnd))
            {
                // Only a corner or an edge of the segment touched the scope; nothing usable remains.
                _outsideExtentCount++;
                return 0;
            }

            if (wasClipped) _clippedCount++;
            start = clippedStart;
            end = clippedEnd;
        }

        _segments.Add(new Segment2D(start, end, source));
        return 1;
    }

    public Result<PlanGeometrySnapshot> Build(DateTime? extractedAtUtc = null)
    {
        if (_segments.Count == 0)
        {
            return Result.Failure<PlanGeometrySnapshot>(new Error(
                "geometry.extraction.empty",
                "在此 Area Plan 的範圍內找不到任何可用的牆、柱、房間分隔線或輔助線，無法建立區劃線網。",
                BuildAccountingDetail()));
        }

        var snapshot = new PlanGeometrySnapshot(
            _request.PackageId,
            _hostDocumentUniqueId,
            _levelUniqueId,
            InSourceOrder(),
            Tolerance,
            Extent,
            _warnings.Concat(SummaryWarnings()),
            extractedAtUtc);

        return Result.Success(snapshot);
    }

    // A FilteredElementCollector makes no promise about the order it hands elements back, so two
    // runs over an unchanged model could otherwise produce snapshots that differ only in ordering
    // and compare as changed. Sorting by source gives a reproducible file; LINQ's ordering is
    // stable, so the segments of one element keep the order their curve was traced in.
    private IEnumerable<Segment2D> InSourceOrder() => _segments
        .OrderBy(s => s.Source.Kind)
        .ThenBy(s => s.Source.DocumentUniqueId, StringComparer.Ordinal)
        .ThenBy(s => s.Source.LinkInstanceUniqueId ?? string.Empty, StringComparer.Ordinal)
        .ThenBy(s => s.Source.ElementUniqueId, StringComparer.Ordinal);

    // Per-element noise would bury the real problems, so the discards are reported as one line each.
    private IEnumerable<string> SummaryWarnings()
    {
        if (_degenerateCount > 0)
            yield return $"已略過 {_degenerateCount} 段長度為零或重複端點的幾何。";
        if (_outsideExtentCount > 0)
            yield return $"已排除 {_outsideExtentCount} 段位於視圖範圍外的幾何。";
        if (_clippedCount > 0)
            yield return $"已將 {_clippedCount} 段跨越視圖範圍邊界的幾何裁切至邊界。";
    }

    private string BuildAccountingDetail() =>
        $"degenerate={_degenerateCount}; outsideExtent={_outsideExtentCount}; clipped={_clippedCount}; " +
        $"extent={(Extent is null ? "none" : "set")}; restrictToViewExtent={_request.Options.RestrictToViewExtent}";

    private static bool IsDegenerate(Point2D start, Point2D end) =>
        start.DistanceTo(end) <= GeometryTolerance.ZeroLengthFeet;
}
