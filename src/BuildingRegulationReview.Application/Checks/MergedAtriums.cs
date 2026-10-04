using System;
using System.Collections.Generic;
using System.Linq;
using BuildingRegulationReview.Application.Candidates;
using BuildingRegulationReview.Application.Geometry;
using BuildingRegulationReview.Application.Parameters;
using BuildingRegulationReview.Domain.Geometry;
using BuildingRegulationReview.Domain.Reviews;

namespace BuildingRegulationReview.Application.Checks;

/// <summary>
/// The 挑空 of one storey that 第79條之2第3項 exempts from 單獨區劃分隔, and the lines that are
/// therefore no 區劃邊界 (垂直區劃規格 §3.9、決議 37).
/// </summary>
/// <remarks>
/// <para>
/// Once 第3項 holds, the 挑空 and the 區劃 around it are one 連通區劃 (內政部106年9月27日函): the line
/// between them is inside that 區劃, so the railing or glass along an atrium's edge owes no 一小時
/// 防火時效 and its openings need be no 防火設備. Holding them to 第79條第1項 would paint a correct
/// design red.
/// </para>
/// <para>
/// Only that line, and only where an element runs along it the whole way: every point of the
/// element that lies on a 區劃 boundary must lie between one merged 挑空 and a 區劃 that is no 垂直區劃,
/// and every 區劃 the element bounds must be met that way somewhere along it. The element is sampled
/// at a spacing no wider than the tolerance, so a short stretch along a 管道間 cannot fall between
/// two samples; and a 垂直區劃 among the 區劃 it bounds keeps it a boundary outright.
/// An outside wall that merely runs along the 挑空 and its neighbour side by side, a wall that goes
/// on to separate two other 區劃, a wall facing a 樓梯間 or 昇降機道 (第1項 still separates those on
/// their own), the line between two exempt 挑空 (each has its own 連通區劃, reviewed apart), and every
/// line of a 挑空 whose 第3項 does not hold or cannot be decided yet all stay boundaries: the strict
/// reading is kept until the facts say otherwise.
/// </para>
/// <para>
/// A boundary drawn on the face of a railing or glass wall, rather than on its centreline, makes the
/// relation doubtful (<see cref="CandidateAmbiguityKind.BoundaryOffCenterline"/>) — the usual case
/// when a 挑空 is closed with room separation lines. Such a doubtful relation counts here as well:
/// whether the line bounds or not, it is interior to one 連通區劃.
/// </para>
/// </remarks>
public sealed class MergedAtriums
{
    /// <summary>What a result says when it was decided as an interior line.</summary>
    public const string Note =
        "此構件位於依第79條之2第3項免除單獨區劃之挑空與其連通區劃之間，屬同一連通區劃內部，不視為區劃邊界。";

    public const string EvidenceField = "atrium.interiorBoundary";

    /// <summary>
    /// How far past half the wall's width a boundary may lie and still be the wall's own — the same
    /// tolerance candidate resolution used to call it a boundary at all.
    /// </summary>
    private static readonly double ToleranceFeet = CandidateResolutionOptions.Default.BoundaryToleranceFeet;

    private readonly HashSet<Guid> _merged;
    private readonly Dictionary<Guid, string?> _uses;
    private readonly Dictionary<Guid, PlanShape> _shapes;

    private readonly HashSet<Guid> _unreadable;

    private MergedAtriums(HashSet<Guid> merged, Dictionary<Guid, string?> uses, Dictionary<Guid, PlanShape> shapes, HashSet<Guid> unreadable)
    {
        _merged = merged;
        _uses = uses;
        _shapes = shapes;
        _unreadable = unreadable;
    }

    /// <summary>The 挑空 of the set whose 第3項 holds, read from the same facts the 第3項 result reads.</summary>
    public static MergedAtriums Of(CandidateSet set, CompartmentAreaInputs inputs)
    {
        if (set is null) throw new ArgumentNullException(nameof(set));
        if (inputs is null) throw new ArgumentNullException(nameof(inputs));

        var uses = new Dictionary<Guid, string?>();
        var unreadable = new HashSet<Guid>();
        var merged = new HashSet<Guid>();
        var shapes = new Dictionary<Guid, PlanShape>();
        foreach (var zone in set.Zones)
        {
            var supplied = inputs.ForZone(zone.ZoneId);
            var input = supplied.FirstOrDefault(i => string.Equals(i.Field, "zone.use", StringComparison.Ordinal));
            var use = input is { IsUnreadable: false, Value: { Kind: ReviewValueKind.Text } text } ? text.Text.Trim() : null;
            uses[zone.ZoneId] = use;
            if (input is { IsUnreadable: true }) unreadable.Add(zone.ZoneId);
            shapes[zone.ZoneId] = new PlanShape(zone.Parts.SelectMany(p => p.Observation.BoundaryLoops));

            if (zone.IsClear &&
                string.Equals(use, ZoneUses.Atrium, StringComparison.Ordinal) &&
                AtriumExemptionFacts.Read(inputs.Building.Concat(supplied)).Exemption.Holds)
                merged.Add(zone.ZoneId);
        }

        return new MergedAtriums(merged, uses, shapes, unreadable);
    }

    public bool Any => _merged.Count > 0;

    /// <summary>Whether a wall or beam lies only between a merged 挑空 and its 連通區劃.</summary>
    public bool IsInterior(MemberCandidate member)
    {
        if (member is null) throw new ArgumentNullException(nameof(member));
        if (!CandidateCategories.IsLinear(member.Category)) return false;
        var line = member.Observation.Centerline;
        return IsInterior(member.Relations, spacing => Samples(line, spacing), HalfWidth(member.Observation.WidthFeet));
    }

    /// <summary>Whether a door, window or panel lies only between a merged 挑空 and its 連通區劃.</summary>
    public bool IsInterior(OpeningCandidate opening)
    {
        if (opening is null) throw new ArgumentNullException(nameof(opening));
        var halfWidth = HalfWidth(opening.Host?.WidthFeet ?? opening.Observation.WidthFeet);
        if (opening.Observation.Location is Point2D location)
            return IsInterior(opening.Relations, _ => new[] { location }, halfWidth);
        return opening.Host is { } host && IsInterior(opening.Relations, spacing => Samples(host.Centerline, spacing), halfWidth);
    }

    public ReviewEvidenceItem Evidence() => new(EvidenceField, ReviewValue.OfBoolean(true));

    private bool IsInterior(IEnumerable<ZoneRelation> relations, Func<double, IReadOnlyList<Point2D>> sample, double halfWidth)
    {
        if (_merged.Count == 0) return false;

        var bounded = relations.Where(Counts).Select(r => r.ZoneId).Distinct().Where(_shapes.ContainsKey).ToList();
        if (bounded.Count < 2 || !bounded.Any(_merged.Contains)) return false;

        // A 垂直區劃 anywhere along the element keeps it a boundary, however short that stretch is — and
        // so does a 區劃 whose 用途 cannot be read (its Areas disagree): it might be one. A 用途 left
        // blank is an ordinary 區劃, the tool's convention for every 區劃 that is not special.
        if (bounded.Any(z => !_merged.Contains(z) && (_unreadable.Contains(z) || ZoneUses.IsVerticalCompartment(UseOf(z)))))
            return false;

        var tolerance = halfWidth + ToleranceFeet;
        var met = new HashSet<Guid>();
        foreach (var point in sample(tolerance))
        {
            var near = bounded.Where(z => _shapes[z].DistanceToEdges(point) <= tolerance).ToList();
            if (near.Count == 0) continue;

            // One side only is an outside wall, or the stretch of a wall that separates something
            // else. Exactly one exempt 挑空 and at least one other 區劃 is the line between them.
            if (near.Count(_merged.Contains) != 1 || !near.Any(z => !_merged.Contains(z))) return false;
            met.UnionWith(near);
        }

        // Every 區劃 the element bounds must have been met on that line: one met nowhere lies along a
        // stretch the samples did not reach, and is not known to be interior.
        return met.Count > 0 && bounded.All(met.Contains);
    }

    /// <summary>A boundary, or a doubt about whether the element is one (a line on its face).</summary>
    private static bool Counts(ZoneRelation relation) =>
        relation.IsBoundary ||
        (relation.IsAmbiguous &&
         (relation.Ambiguity == CandidateAmbiguityKind.BoundaryOffCenterline ||
          relation.Ambiguity == CandidateAmbiguityKind.BoundaryAlongOutline ||
          relation.Ambiguity == CandidateAmbiguityKind.HostRelationAmbiguous));

    private static double HalfWidth(double? widthFeet) => widthFeet is double w && w > 0 ? w / 2.0 : 0;

    /// <summary>Points along a line no further apart than <paramref name="spacing"/>, ends excluded.</summary>
    private static IReadOnlyList<Point2D> Samples(IReadOnlyList<Point2D> line, double spacing)
    {
        var points = new List<Point2D>();
        for (var i = 0; i + 1 < line.Count; i++)
        {
            var steps = Math.Max(2, (int)Math.Ceiling(line[i].DistanceTo(line[i + 1]) / Math.Max(spacing, 1e-3)));
            for (var k = 1; k < steps; k++)
                points.Add(SegmentGeometry.PointAt(line[i], line[i + 1], k / (double)steps));
        }
        return points;
    }

    private string? UseOf(Guid zoneId) => _uses.TryGetValue(zoneId, out var use) ? use : null;
}
