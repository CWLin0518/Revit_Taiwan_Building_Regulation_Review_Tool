using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BuildingRegulationReview.Application.Geometry;
using BuildingRegulationReview.Domain.Geometry;
using BuildingRegulationReview.Domain.Reviews;

namespace BuildingRegulationReview.Application.Candidates;

/// <summary>
/// Decides which members and openings each 區劃 has to review, and how each one relates to it
/// (spec 11.0 P3-T03, 11.5 step 1, 11.6 step 1). Pure plan geometry on already-read data: it opens
/// no transaction and touches no Revit object.
/// </summary>
/// <remarks>
/// <para>
/// The zone is the Area the tool wrote, and its boundary was drawn from wall centrelines, so a wall
/// is a boundary wall when its centreline runs along that boundary. A boundary that runs inside the
/// wall's thickness but off its centreline was drawn from something else — a face, an auxiliary
/// line — and that is exactly the case the geometry cannot settle, so it becomes ManualReview.
/// </para>
/// <para>
/// Columns are on the boundary when the boundary passes through them; floors relate to every zone
/// they overlap. Hosted openings take their relation from the host wall at the opening's own
/// location, so a door on the part of a long wall that is not on the boundary is not a boundary
/// opening. Curtain-wall, unhosted and linked elements follow the MVP policy: manual review.
/// </para>
/// <para>
/// Everything is processed in a canonical order and every length is measured exactly, so the same
/// model yields the same <see cref="CandidateSet.Signature"/> whatever order it was read in.
/// </para>
/// </remarks>
public static class CandidateResolver
{
    public static CandidateSet Resolve(CandidateObservationSet observations, CandidateResolutionOptions? options = null)
    {
        if (observations is null) throw new ArgumentNullException(nameof(observations));
        options ??= CandidateResolutionOptions.Default;

        var ambiguities = new List<CandidateAmbiguity>();
        var zones = observations.Zones.OrderBy(z => z.ZoneId).Select(z => new ZoneState(z)).ToList();
        FindZoneProblems(zones, options, ambiguities);
        var measurable = zones.Where(z => !z.Shape.IsEmpty).ToList();

        // Every wall's relations are kept whatever the wall strategy says: openings need their host's.
        var members = observations.Members
            .OrderBy(m => m.Category).ThenBy(m => m.Source, CandidateOrdering.Sources)
            .ToList();
        var memberRelations = new Dictionary<MemberObservation, List<ZoneRelation>>();
        foreach (var member in members)
        {
            var needed = options.Reviews(member.Category) || member.Category == CandidateCategory.Wall;
            memberRelations[member] = needed && member.HasPlanGeometry
                ? measurable.Select(z => RelateMember(member, z, options)).Where(r => r is not null).Select(r => r!).ToList()
                : new List<ZoneRelation>();
        }

        var memberCandidates = new List<MemberCandidate>();
        var unrelatedMembers = 0;
        foreach (var member in members)
        {
            if (!options.Reviews(member.Category)) continue;

            if (!member.HasPlanGeometry)
            {
                ambiguities.Add(new CandidateAmbiguity(
                    CandidateAmbiguityKind.NoPlanGeometry,
                    new[] { member.Source.ElementUniqueId },
                    null,
                    Format("{0}（{1}）沒有可用的平面幾何，無法判定它與哪個區劃相關，需人工覆核。",
                        CandidateCategories.Label(member.Category), member.Source),
                    new ReviewEvidence(CandidateEvidence.Source(member.Source, member.Category, member.TypeName))));
                continue;
            }

            var allowed = options.RelationsFor(member.Category);
            var relations = ApplyLinkPolicy(member.Source, CandidateCategories.Label(member.Category), memberRelations[member])
                .Where(r => r.IsAmbiguous || Allows(allowed, r.Kind))
                .ToList();
            if (relations.Count == 0)
            {
                unrelatedMembers++;
                continue;
            }

            var candidate = new MemberCandidate(member, relations);
            memberCandidates.Add(candidate);
            foreach (var relation in candidate.Relations.Where(r => r.IsAmbiguous))
            {
                ambiguities.Add(new CandidateAmbiguity(relation.Ambiguity!.Value, new[] { member.Source.ElementUniqueId },
                    relation.ZoneId, relation.Message, candidate.EvidenceFor(relation.ZoneId)));
            }
        }

        var walls = members
            .Where(m => m.Category == CandidateCategory.Wall)
            .ToDictionary(m => HostKey(m.Source.DocumentUniqueId, m.Source.LinkInstanceUniqueId, m.Source.ElementUniqueId), StringComparer.Ordinal);

        var openingCandidates = new List<OpeningCandidate>();
        var unrelatedOpenings = 0;
        foreach (var opening in observations.Openings.OrderBy(o => o.Category).ThenBy(o => o.Source, CandidateOrdering.Sources))
        {
            MemberObservation? host = null;
            if (opening.HostUniqueId is not null)
                walls.TryGetValue(HostKey(opening.Source.DocumentUniqueId, opening.Source.LinkInstanceUniqueId, opening.HostUniqueId), out host);

            if (!opening.Location.HasValue && host is null)
            {
                ambiguities.Add(new CandidateAmbiguity(
                    CandidateAmbiguityKind.OpeningLocationUnknown,
                    new[] { opening.Source.ElementUniqueId },
                    null,
                    Format("{0}（{1}）沒有平面位置，也沒有可解析的 Host 牆，無法判定所屬區劃，需人工覆核。",
                        CandidateCategories.Label(opening.Category), opening.Source),
                    new ReviewEvidence(CandidateEvidence.Source(opening.Source, opening.Category, opening.TypeName))));
                continue;
            }

            var relations = measurable
                .Select(z => RelateOpening(opening, host, host is null ? null : memberRelations[host].FirstOrDefault(r => r.ZoneId == z.ZoneId), z, measurable, options))
                .Where(r => r is not null).Select(r => r!)
                .ToList();
            relations = ApplyLinkPolicy(opening.Source, CandidateCategories.Label(opening.Category), relations)
                .Where(r => r.Kind != ZoneRelationKind.Inside || options.IncludeInteriorOpenings)
                .ToList();

            if (relations.Count == 0)
            {
                unrelatedOpenings++;
                continue;
            }

            var candidate = new OpeningCandidate(opening, relations, host);
            openingCandidates.Add(candidate);
            foreach (var relation in candidate.Relations.Where(r => r.IsAmbiguous))
            {
                ambiguities.Add(new CandidateAmbiguity(relation.Ambiguity!.Value, new[] { opening.Source.ElementUniqueId },
                    relation.ZoneId, relation.Message, candidate.EvidenceFor(relation.ZoneId)));
            }
        }

        return new CandidateSet(
            observations.PackageId,
            observations.LevelUniqueId,
            observations.LevelName,
            zones.Select(z => z.ToCandidate()),
            memberCandidates,
            openingCandidates,
            ambiguities,
            unrelatedMembers,
            unrelatedOpenings,
            observations.Warnings);
    }

    // --- zones -------------------------------------------------------------------------------

    private static void FindZoneProblems(List<ZoneState> zones, CandidateResolutionOptions options, List<CandidateAmbiguity> ambiguities)
    {
        foreach (var zone in zones)
        {
            var open = zone.Observation.Parts.Where(p => !p.IsEnclosed).Select(p => p.AreaUniqueId).ToList();
            if (open.Count == 0) continue;

            zone.Problems.Add(CandidateAmbiguityKind.ZoneNotEnclosed);
            ambiguities.Add(new CandidateAmbiguity(
                CandidateAmbiguityKind.ZoneNotEnclosed,
                open,
                zone.ZoneId,
                Format("區劃「{0}」有 {1} 個面積未封閉，範圍不明，相關檢討需人工覆核；請回到「建立區劃範圍」修正邊界後重新套用。",
                    zone.Observation.Name, open.Count),
                ZoneEvidence(zone)));
        }

        var tolerance = options.BoundaryToleranceFeet;
        for (var i = 0; i < zones.Count; i++)
        {
            for (var j = i + 1; j < zones.Count; j++)
            {
                var first = zones[i];
                var second = zones[j];
                if (!Overlap(first.Shape, second.Shape, tolerance)) continue;

                foreach (var (zone, other) in new[] { (first, second), (second, first) })
                {
                    zone.Problems.Add(CandidateAmbiguityKind.ZonesOverlap);
                    ambiguities.Add(new CandidateAmbiguity(
                        CandidateAmbiguityKind.ZonesOverlap,
                        zone.AreaUniqueIds.Concat(other.AreaUniqueIds),
                        zone.ZoneId,
                        Format("區劃「{0}」與區劃「{1}」的面積互相重疊，重疊處的構件無法判定歸屬，需人工覆核。",
                            zone.Observation.Name, other.Observation.Name),
                        new ReviewEvidence(ZoneEvidence(zone).Items.Concat(new[]
                        {
                            new ReviewEvidenceItem("candidate.otherZoneId", ReviewValue.OfText(other.ZoneId.ToString("D")))
                        }))));
                }
            }
        }
    }

    private static bool Overlap(PlanShape first, PlanShape second, double tolerance)
    {
        if (first.IsEmpty || second.IsEmpty) return false;
        if (!first.Overlaps(second.MinX, second.MinY, second.MaxX, second.MaxY, 0)) return false;

        // Zones that share a wall touch along it; only an edge running through the other's interior,
        // or one zone's interior lying inside the other, is an overlap.
        return CandidateGeometry.LengthThrough(first.Edges, second, tolerance) > 0 ||
               CandidateGeometry.LengthThrough(second.Edges, first, tolerance) > 0 ||
               first.InteriorPoints().Any(p => second.ContainsWithClearance(p, tolerance)) ||
               second.InteriorPoints().Any(p => first.ContainsWithClearance(p, tolerance));
    }

    private static ReviewEvidence ZoneEvidence(ZoneState zone) => new ReviewEvidence(new[]
    {
        new ReviewEvidenceItem("candidate.zoneId", ReviewValue.OfText(zone.ZoneId.ToString("D"))),
        new ReviewEvidenceItem("candidate.zoneName", ReviewValue.OfText(zone.Observation.Name)),
        new ReviewEvidenceItem("candidate.areaCount", ReviewValue.Quantity(zone.Observation.Parts.Count, ReviewUnit.Count)),
        new ReviewEvidenceItem("candidate.enclosedAreaCount", ReviewValue.Quantity(zone.Observation.Parts.Count(p => p.IsEnclosed), ReviewUnit.Count))
    });

    // --- members -----------------------------------------------------------------------------

    private static ZoneRelation? RelateMember(MemberObservation member, ZoneState zone, CandidateResolutionOptions options)
    {
        return CandidateCategories.IsLinear(member.Category)
            ? RelateLinear(member, zone, options)
            : RelateOutline(member, zone, options);
    }

    private static ZoneRelation? RelateLinear(MemberObservation member, ZoneState zone, CandidateResolutionOptions options)
    {
        var tolerance = options.BoundaryToleranceFeet;
        var band = ((member.WidthFeet ?? 0) / 2.0) + tolerance;
        var line = member.Centerline;
        if (!zone.Shape.Overlaps(line.Min(p => p.X), line.Min(p => p.Y), line.Max(p => p.X), line.Max(p => p.Y), band)) return null;

        var measure = CandidateGeometry.MeasurePath(CandidateGeometry.Polyline(line), zone.Shape, tolerance, band, options.ParallelRadians);
        var threshold = Math.Min(options.MinimumRelationLengthFeet, measure.TotalFeet / 2.0);
        var measurements = new RelationMeasurements(
            boundaryLengthFeet: measure.OnBoundaryFeet,
            bandLengthFeet: measure.InBandFeet,
            insideLengthFeet: measure.InsideFeet,
            outsideLengthFeet: measure.OutsideFeet);
        var label = CandidateCategories.Label(member.Category);
        var name = zone.Observation.Name;

        if (measure.OnBoundaryFeet >= threshold)
        {
            return new ZoneRelation(zone.ZoneId, ZoneRelationKind.Boundary,
                Format("{0}中心線沿區劃「{1}」邊界 {2} m，構成區劃邊界。", label, name, Meters(measure.OnBoundaryFeet)),
                measurements);
        }

        if (measure.InBandFeet >= threshold)
        {
            return new ZoneRelation(zone.ZoneId, ZoneRelationKind.Ambiguous,
                Format("區劃「{0}」的邊界有 {1} m 落在此{2}的厚度內，但不在中心線上，無法判定是否由此{2}構成邊界，需人工覆核。",
                    name, Meters(measure.InBandFeet), label),
                measurements, CandidateAmbiguityKind.BoundaryOffCenterline);
        }

        if (measure.InsideFeet >= threshold && measure.OutsideFeet >= threshold)
        {
            return new ZoneRelation(zone.ZoneId, ZoneRelationKind.Crossing,
                Format("{0}穿越區劃「{1}」邊界：區劃內 {2} m、區劃外 {3} m。", label, name, Meters(measure.InsideFeet), Meters(measure.OutsideFeet)),
                measurements);
        }

        if (measure.InsideFeet >= threshold)
        {
            return new ZoneRelation(zone.ZoneId, ZoneRelationKind.Inside,
                Format("{0}位於區劃「{1}」內（{2} m），不構成邊界。", label, name, Meters(measure.InsideFeet)),
                measurements);
        }

        return null;
    }

    private static ZoneRelation? RelateOutline(MemberObservation member, ZoneState zone, CandidateResolutionOptions options)
    {
        var tolerance = options.BoundaryToleranceFeet;
        var outline = new PlanShape(member.Outlines);
        if (outline.IsEmpty || !zone.Shape.Overlaps(outline.MinX, outline.MinY, outline.MaxX, outline.MaxY, tolerance)) return null;

        var through = CandidateGeometry.LengthThrough(zone.Shape.Edges, outline, tolerance);
        var perimeter = CandidateGeometry.MeasurePath(outline.Edges, zone.Shape, tolerance, tolerance, options.ParallelRadians);
        var measurements = new RelationMeasurements(
            boundaryLengthFeet: perimeter.OnBoundaryFeet,
            insideLengthFeet: perimeter.InsideFeet,
            outsideLengthFeet: perimeter.OutsideFeet,
            throughLengthFeet: through);
        var label = CandidateCategories.Label(member.Category);
        var name = zone.Observation.Name;
        var threshold = Math.Min(options.MinimumRelationLengthFeet, perimeter.TotalFeet / 4.0);

        if (member.Category == CandidateCategory.Floor)
        {
            var overlaps = through > 0 ||
                           perimeter.InsideFeet >= threshold ||
                           outline.InteriorPoints().Any(p => zone.Shape.ContainsWithClearance(p, tolerance)) ||
                           zone.Shape.InteriorPoints().Any(p => outline.ContainsWithClearance(p, tolerance));
            if (!overlaps) return null;

            return through > 0 || perimeter.OutsideFeet >= threshold
                ? new ZoneRelation(zone.ZoneId, ZoneRelationKind.Crossing,
                    Format("樓板與區劃「{0}」重疊並延伸至區劃外。", name), measurements)
                : new ZoneRelation(zone.ZoneId, ZoneRelationKind.Inside,
                    Format("樓板位於區劃「{0}」範圍內。", name), measurements);
        }

        if (through > 0)
        {
            return new ZoneRelation(zone.ZoneId, ZoneRelationKind.Boundary,
                Format("區劃「{0}」邊界穿過此{1}（{2} m），構成區劃邊界。", name, label, Meters(through)),
                measurements);
        }

        if (perimeter.OnBoundaryFeet >= threshold || (perimeter.InsideFeet > tolerance && perimeter.OutsideFeet > tolerance))
        {
            return new ZoneRelation(zone.ZoneId, ZoneRelationKind.Ambiguous,
                Format("區劃「{0}」的邊界貼著此{1}的外緣而非穿過它，無法判定此{1}是否構成邊界，需人工覆核。", name, label),
                measurements, CandidateAmbiguityKind.BoundaryAlongOutline);
        }

        if (perimeter.InsideFeet > tolerance)
        {
            return new ZoneRelation(zone.ZoneId, ZoneRelationKind.Inside,
                Format("{0}位於區劃「{1}」內，不構成邊界。", label, name), measurements);
        }

        return null;
    }

    // --- openings ----------------------------------------------------------------------------

    private static ZoneRelation? RelateOpening(
        OpeningObservation opening,
        MemberObservation? host,
        ZoneRelation? hostRelation,
        ZoneState zone,
        IReadOnlyList<ZoneState> zones,
        CandidateResolutionOptions options)
    {
        var label = CandidateCategories.Label(opening.Category);
        var name = zone.Observation.Name;

        if (!opening.Location.HasValue)
        {
            // Only reachable with a resolved host; without a place on it, the host alone cannot say
            // whether the opening sits on the boundary part of the wall.
            if (hostRelation is null || !(hostRelation.IsBoundary || hostRelation.IsAmbiguous)) return null;
            return new ZoneRelation(zone.ZoneId, ZoneRelationKind.Ambiguous,
                Format("{0}的 Host 牆與區劃「{1}」邊界相關，但{0}沒有平面位置，無法判定是否位於邊界上，需人工覆核。", label, name),
                RelationMeasurements.None, CandidateAmbiguityKind.OpeningLocationUnknown);
        }

        var location = opening.Location.Value;
        var distance = zone.Shape.DistanceToEdges(location);
        var inside = zone.Shape.Contains(location);
        var measurements = new RelationMeasurements(distanceToBoundaryFeet: distance);
        ZoneRelation? Interior() => inside
            ? new ZoneRelation(zone.ZoneId, ZoneRelationKind.Inside, Format("{0}位於區劃「{1}」內，不在區劃邊界上。", label, name), measurements)
            : null;

        if (host is null)
        {
            if (distance > options.OpeningSearchFeet) return Interior();

            return opening.IsHosted
                ? new ZoneRelation(zone.ZoneId, ZoneRelationKind.Ambiguous,
                    Format("{0}位於區劃「{1}」邊界附近（{2} m），但它的 Host（{3}）不是本次讀取的牆，需人工覆核。",
                        label, name, Meters(distance), opening.HostUniqueId!),
                    measurements, CandidateAmbiguityKind.HostNotResolved)
                : new ZoneRelation(zone.ZoneId, ZoneRelationKind.Ambiguous,
                    Format("{0}位於區劃「{1}」邊界附近（{2} m）但沒有 Host 牆，依 MVP 政策需人工覆核。", label, name, Meters(distance)),
                    measurements, CandidateAmbiguityKind.NonHostedOpening);
        }

        var band = ((host.WidthFeet ?? 0) / 2.0) + options.BoundaryToleranceFeet;
        if (hostRelation is null || distance > band) return Interior();

        if (host.IsCurtainWall || opening.Category == CandidateCategory.CurtainPanel)
        {
            if (hostRelation.Kind == ZoneRelationKind.Inside) return Interior();
            if (host.IsCurtainWall && FacingOutside(host, location, zone, zones) is double probe)
            {
                return new ZoneRelation(zone.ZoneId, ZoneRelationKind.Facade,
                    Format("{0}位於區劃「{1}」邊界的帷幕牆上，該帷幕牆外側 {2} m 處不屬於任何區劃，為建築物外牆而非區劃分隔。",
                        label, name, Meters(probe)),
                    measurements);
            }

            return new ZoneRelation(zone.ZoneId, ZoneRelationKind.Ambiguous,
                Format("{0}位於區劃「{1}」邊界的帷幕牆上，依 MVP 政策需人工覆核。", label, name),
                measurements, CandidateAmbiguityKind.CurtainWallOpening);
        }

        switch (hostRelation.Kind)
        {
            case ZoneRelationKind.Boundary:
                return new ZoneRelation(zone.ZoneId, ZoneRelationKind.Boundary,
                    Format("{0}位於區劃「{1}」的邊界牆上（距邊界 {2} m）。", label, name, Meters(distance)), measurements);
            case ZoneRelationKind.Inside:
                return Interior();
            default:
                // An ambiguous host, or a host crossing the boundary right where the opening sits.
                return new ZoneRelation(zone.ZoneId, ZoneRelationKind.Ambiguous,
                    Format("{0}位於區劃「{1}」邊界上，但 Host 牆與此邊界的關係無法判定，需人工覆核。", label, name),
                    measurements, CandidateAmbiguityKind.HostRelationAmbiguous);
        }
    }

    /// <summary>
    /// How far beyond the curtain wall's faces a point is probed for a zone; the same depth the
    /// 帷幕牆區劃交接 review probes to find which side of a facade is inside.
    /// </summary>
    private const double FacadeProbeMm = 300.0;

    /// <summary>
    /// The probe depth, in feet, when the curtain wall at <paramref name="location"/> has
    /// <paramref name="zone"/> on one side and no zone at all on the other — the building's outer
    /// facade. Null when both sides, or neither, belong to a zone: an interior curtain wall between
    /// two zones is a 區劃分隔 whose openings 第79條 does govern, and that stays a manual review.
    /// </summary>
    private static double? FacingOutside(MemberObservation wall, Point2D location, ZoneState zone, IReadOnlyList<ZoneState> zones)
    {
        var line = wall.Centerline;
        Point2D start = line[0], end = line[1];
        var nearest = double.PositiveInfinity;
        for (var i = 0; i + 1 < line.Count; i++)
        {
            var distance = SegmentGeometry.DistanceToSegment(location, line[i], line[i + 1], out _, out _);
            if (distance >= nearest || line[i].DistanceTo(line[i + 1]) <= GeometryTolerance.ZeroLengthFeet) continue;
            nearest = distance;
            start = line[i];
            end = line[i + 1];
        }

        var length = start.DistanceTo(end);
        if (length <= GeometryTolerance.ZeroLengthFeet) return null;
        var nx = -(end.Y - start.Y) / length;
        var ny = (end.X - start.X) / length;
        var depth = ((wall.WidthFeet ?? 0) / 2.0) + PlanUnits.MillimetersToFeet(FacadeProbeMm);

        // Probed from the wall's centreline, so where along its thickness the panel's point sits does not matter.
        _ = SegmentGeometry.DistanceToSegment(location, start, end, out _, out var foot);
        var left = new Point2D(foot.X + (nx * depth), foot.Y + (ny * depth));
        var right = new Point2D(foot.X - (nx * depth), foot.Y - (ny * depth));
        var leftZones = zones.Where(z => z.Shape.Contains(left)).ToList();
        var rightZones = zones.Where(z => z.Shape.Contains(right)).ToList();

        var outsideRight = leftZones.Count == 1 && leftZones[0] == zone && rightZones.Count == 0;
        var outsideLeft = rightZones.Count == 1 && rightZones[0] == zone && leftZones.Count == 0;
        return outsideRight || outsideLeft ? depth : (double?)null;
    }

    // --- policies and helpers ----------------------------------------------------------------

    private static IEnumerable<ZoneRelation> ApplyLinkPolicy(CandidateSource source, string label, IEnumerable<ZoneRelation> relations)
    {
        if (!source.IsFromLink) return relations;

        return relations.Select(r => r.IsAmbiguous && r.Ambiguity == CandidateAmbiguityKind.LinkedElement
            ? r
            : new ZoneRelation(r.ZoneId, ZoneRelationKind.Ambiguous,
                Format("{0}來自連結模型，依 MVP 政策需人工覆核（幾何判定：{1}）。", label, r.Message),
                r.Measurements, CandidateAmbiguityKind.LinkedElement));
    }

    private static bool Allows(CandidateRelationKinds allowed, ZoneRelationKind kind) => kind switch
    {
        ZoneRelationKind.Boundary => (allowed & CandidateRelationKinds.Boundary) != 0,
        ZoneRelationKind.Crossing => (allowed & CandidateRelationKinds.Crossing) != 0,
        ZoneRelationKind.Inside => (allowed & CandidateRelationKinds.Inside) != 0,
        _ => true
    };

    private static string HostKey(string documentUniqueId, string? linkInstanceUniqueId, string elementUniqueId) =>
        documentUniqueId + "\n" + (linkInstanceUniqueId ?? string.Empty) + "\n" + elementUniqueId;

    private static string Meters(double feet) =>
        PlanUnits.FeetToMeters(feet).ToString("0.###", CultureInfo.InvariantCulture);

    private static string Format(string format, params object[] args) =>
        string.Format(CultureInfo.InvariantCulture, format, args);

    private sealed class ZoneState
    {
        public ZoneState(ZoneObservation observation)
        {
            Observation = observation;
            Shape = new PlanShape(observation.Parts.Where(p => p.IsEnclosed).SelectMany(p => p.BoundaryLoops));
        }

        public ZoneObservation Observation { get; }
        public Guid ZoneId => Observation.ZoneId;
        public PlanShape Shape { get; }
        public HashSet<CandidateAmbiguityKind> Problems { get; } = new HashSet<CandidateAmbiguityKind>();
        public IEnumerable<string> AreaUniqueIds => Observation.Parts.Select(p => p.AreaUniqueId);

        public CandidateZone ToCandidate() => new CandidateZone(
            ZoneId,
            Observation.Name,
            Observation.Parts.Select(p => new CandidateZonePart(p, p.IsEnclosed ? new PlanShape(p.BoundaryLoops).NetAreaSquareFeet() : (double?)null)),
            Problems);
    }
}
