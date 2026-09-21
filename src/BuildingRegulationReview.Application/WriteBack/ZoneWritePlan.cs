using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using BuildingRegulationReview.Domain.Geometry;
using BuildingRegulationReview.Domain.Regions;

namespace BuildingRegulationReview.Application.WriteBack;

/// <summary>
/// What the current 區劃 drafts say the model should contain (spec 10.5, read-only here): the Area
/// Boundary Lines that fence each contiguous part, the Area inside it, its tag, and the Detail Curve
/// copy for the Drafting View. P2-T06 only compares this with the model; P2-T07 creates it.
/// </summary>
/// <remarks>
/// The outline of a part is taken by dropping every network edge that two of its own faces share, so
/// merging rooms removes the wall between them and an island inside a hole closes that hole. No
/// polygon union and no new tolerance are involved: the solver already gave each boundary segment a
/// network edge index, and an interior edge is simply one that appears twice.
/// </remarks>
public static class ZoneWritePlan
{
    /// <summary>Builds the planned elements, in key order, for every non-empty 區劃 draft.</summary>
    public static IReadOnlyList<PlannedElement> Build(
        Guid packageId,
        PlanRegionMap map,
        ZoneDraftSet zones,
        GeometryTolerance? tolerance = null)
    {
        if (packageId == Guid.Empty) throw new ArgumentException("Package ID cannot be empty.", nameof(packageId));
        if (map is null) throw new ArgumentNullException(nameof(map));
        if (zones is null) throw new ArgumentNullException(nameof(zones));

        var quantum = (tolerance ?? map.Tolerance).ClosureFeet;
        var planned = new List<PlannedElement>();

        foreach (var zone in zones.Zones)
        {
            if (zone.IsEmpty) continue;

            var parts = map.ContiguousPartsOf(zone.FaceIds);
            for (var partIndex = 0; partIndex < parts.Count; partIndex++)
            {
                planned.AddRange(PlanPart(packageId, map, zone, parts[partIndex], partIndex, parts.Count, quantum));
            }
        }

        return new ReadOnlyCollection<PlannedElement>(planned);
    }

    private static IEnumerable<PlannedElement> PlanPart(
        Guid packageId,
        PlanRegionMap map,
        ZoneDraft zone,
        IReadOnlyList<int> part,
        int partIndex,
        int partCount,
        double quantum)
    {
        var faces = part.Select(map.Face).ToList();
        var outline = Outline(faces);

        for (var ordinal = 0; ordinal < outline.Count; ordinal++)
        {
            var segment = outline[ordinal];
            var points = new[] { segment.Start, segment.End };
            var geometry = PlannedElementSignature.ForSegment(segment.Start, segment.End, quantum);

            yield return new PlannedElement(
                new ManagedElementKey(packageId, zone.Id, ManagedElementKind.AreaBoundaryLine, partIndex, ordinal),
                geometry,
                string.Format(CultureInfo.InvariantCulture, "「{0}」的面積邊界線 #{1}", zone.Name, ordinal + 1),
                points,
                zoneName: zone.Name,
                color: zone.Color);

            yield return new PlannedElement(
                new ManagedElementKey(packageId, zone.Id, ManagedElementKind.DetailCurve, partIndex, ordinal),
                geometry,
                string.Format(CultureInfo.InvariantCulture, "「{0}」的單線圖細部線 #{1}", zone.Name, ordinal + 1),
                points,
                zoneName: zone.Name,
                color: zone.Color);
        }

        // One Revit Area can only sit in one closed part, so a merged zone gets one per part.
        var anchor = faces.OrderByDescending(f => f.NetAreaSquareFeet).First();
        var netArea = PlanUnits.SquareFeetToSquareMeters(faces.Sum(f => f.NetAreaSquareFeet));
        // A zone that is all one piece needs no part number; a merged one has to say which piece.
        var partSuffix = partCount <= 1
            ? string.Empty
            : string.Format(CultureInfo.InvariantCulture, "第 {0} 塊", partIndex + 1);

        yield return new PlannedElement(
            new ManagedElementKey(packageId, zone.Id, ManagedElementKind.Area, partIndex, 0),
            PlannedElementSignature.ForArea(zone.Name, zone.Color.ToHex(), anchor.RepresentativePoint, quantum),
            string.Format(CultureInfo.InvariantCulture, "「{0}」{1}的面積（{2:0.##} m²）", zone.Name, partSuffix, netArea),
            placement: anchor.RepresentativePoint,
            zoneName: zone.Name,
            color: zone.Color,
            netAreaSquareMeters: netArea);

        yield return new PlannedElement(
            new ManagedElementKey(packageId, zone.Id, ManagedElementKind.AreaTag, partIndex, 0),
            PlannedElementSignature.ForTag(zone.Name, anchor.RepresentativePoint, quantum),
            string.Format(CultureInfo.InvariantCulture, "「{0}」{1}的面積標註", zone.Name, partSuffix),
            placement: anchor.RepresentativePoint,
            zoneName: zone.Name,
            color: zone.Color,
            netAreaSquareMeters: netArea);
    }

    /// <summary>
    /// The segments fencing one contiguous part: every boundary segment of its faces whose network
    /// edge no second face of the same part also uses. Ordered canonically so the same geometry
    /// always yields the same ordinals, and a re-run therefore matches what was written last time.
    /// </summary>
    private static IReadOnlyList<Segment2D> Outline(IReadOnlyList<PlanFace> faces)
    {
        var uses = new Dictionary<int, int>();
        var segments = new List<KeyValuePair<int, Segment2D>>();

        foreach (var boundary in faces.SelectMany(f => f.AllBoundaries))
        {
            for (var i = 0; i < boundary.Loop.Segments.Count; i++)
            {
                var edge = boundary.EdgeIndices[i];
                uses[edge] = uses.TryGetValue(edge, out var count) ? count + 1 : 1;
                segments.Add(new KeyValuePair<int, Segment2D>(edge, boundary.Loop.Segments[i]));
            }
        }

        return segments
            .Where(pair => uses[pair.Key] == 1)
            .Select(pair => Canonical(pair.Value))
            .OrderBy(s => s.Start.X).ThenBy(s => s.Start.Y).ThenBy(s => s.End.X).ThenBy(s => s.End.Y)
            .ToList();
    }

    /// <summary>Points a segment one way only, so its order does not depend on which face traced it.</summary>
    private static Segment2D Canonical(Segment2D segment) =>
        PlannedElementSignature.IsCanonical(segment.Start, segment.End) ? segment : segment.Reversed();

}
