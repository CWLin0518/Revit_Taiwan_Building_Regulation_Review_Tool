using System;
using System.Collections.Generic;
using System.Linq;
using BuildingRegulationReview.Application.Candidates;
using BuildingRegulationReview.Domain.Geometry;

namespace BuildingRegulationReview.Core.Tests.Candidates;

/// <summary>
/// The fixed candidate model: two 10 m × 10 m zones side by side (A at x 0–10, B at x 10–20) and a
/// third zone whose Area is not enclosed, with one element for every case P3-T03 has to decide.
/// Coordinates below are metres; the observations are in feet like the adapter's.
/// </summary>
internal static class CandidateModel
{
    public const string Document = "doc-host";

    public static readonly Guid PackageId = Guid.Parse("11111111-2222-3333-4444-555555555555");
    public static readonly Guid ZoneA = Guid.Parse("aaaaaaaa-0000-0000-0000-00000000000a");
    public static readonly Guid ZoneB = Guid.Parse("aaaaaaaa-0000-0000-0000-00000000000b");
    public static readonly Guid ZoneC = Guid.Parse("aaaaaaaa-0000-0000-0000-00000000000c");

    public static double M(double meters) => PlanUnits.MetersToFeet(meters);

    public static Point2D P(double x, double y) => new(M(x), M(y));

    public static IReadOnlyList<Point2D> Rect(double x0, double y0, double x1, double y1) =>
        new[] { P(x0, y0), P(x1, y0), P(x1, y1), P(x0, y1) };

    public static CandidateSource Source(string uid, string? link = null) => new(Document, uid, link);

    public static ZoneObservation Zone(Guid id, string name, string areaUid, params IReadOnlyList<Point2D>[] loops) =>
        new(id, name, new[] { new ZonePartObservation(areaUid, loops, loops.Length == 0 ? null : PlanUnits.SquareMetersToSquareFeet(100)) });

    public static MemberObservation Wall(string uid, double x0, double y0, double x1, double y1, double width = 0.2, bool curtain = false) =>
        new(Source(uid), CandidateCategory.Wall, new[] { P(x0, y0), P(x1, y1) }, widthFeet: M(width),
            typeUniqueId: curtain ? "type-cw" : "type-rc200", typeName: curtain ? "帷幕牆" : "RC 200", isStructural: !curtain, isCurtainWall: curtain);

    public static MemberObservation Beam(string uid, double x0, double y0, double x1, double y1, double width = 0.3) =>
        new(Source(uid), CandidateCategory.StructuralFraming, new[] { P(x0, y0), P(x1, y1) }, widthFeet: M(width), typeName: "B30x60", isStructural: true);

    public static MemberObservation Column(string uid, double x0, double y0, double x1, double y1) =>
        new(Source(uid), CandidateCategory.Column, outlines: new[] { Rect(x0, y0, x1, y1) }, typeName: "C40x40", isStructural: true);

    public static MemberObservation Floor(string uid, double x0, double y0, double x1, double y1) =>
        new(Source(uid), CandidateCategory.Floor, outlines: new[] { Rect(x0, y0, x1, y1) }, typeName: "RC 150", isStructural: true);

    public static OpeningObservation Door(string uid, string? host, double? x, double? y, CandidateCategory category = CandidateCategory.Door) =>
        new(Source(uid), category, host, x.HasValue && y.HasValue ? P(x.Value, y.Value) : (Point2D?)null,
            widthFeet: M(1.0), heightFeet: M(2.1), typeName: category == CandidateCategory.Window ? "W1" : "D1");

    public static IReadOnlyList<ZoneObservation> Zones() => new[]
    {
        Zone(ZoneA, "A 區", "area-a", Rect(0, 0, 10, 10)),
        Zone(ZoneB, "B 區", "area-b", Rect(10, 0, 20, 10)),
        Zone(ZoneC, "C 區", "area-c")
    };

    public static IReadOnlyList<MemberObservation> Members() => new[]
    {
        Wall("W1-bottom", 0, 0, 20, 0),
        Wall("W2-shared", 10, 0, 10, 10),
        Wall("W3-partition", 5, 0, 5, 10),
        Wall("W4-crossing", 8, 5, 12, 5),
        Wall("W5-offset", 10, 10.08, 20, 10.08),
        Wall("W6-far", 30, 0, 30, 10),
        Wall("W8-long", -4, 10, 10, 10),
        new MemberObservation(Source("W9-nogeometry"), CandidateCategory.Wall, widthFeet: M(0.2)),
        Wall("W10-left", 0, 0, 0, 10),
        Wall("CW-right", 20, 0, 20, 10, width: 0.1, curtain: true),
        Column("C1-corner", 9.8, 9.8, 10.2, 10.2),
        Column("C2-inside", 2.8, 2.8, 3.2, 3.2),
        Column("C3-flush", 10, 4, 10.4, 4.4),
        Beam("B1-shared", 10, 0, 10, 10),
        Floor("F1-slab", 0, 0, 20, 10)
    };

    public static IReadOnlyList<OpeningObservation> Openings() => new[]
    {
        Door("D1-shared", "W2-shared", 10, 3),
        Door("D2-partition", "W3-partition", 5, 4),
        Door("WN1-bottom", "W1-bottom", 15, 0, CandidateCategory.Window),
        Door("D3-off-boundary", "W8-long", -2, 10),
        Door("D4-on-boundary", "W8-long", 4, 10),
        Door("D5-ambiguous-host", "W5-offset", 15, 10.08),
        Door("P1-panel", "CW-right", 20, 5, CandidateCategory.CurtainPanel),
        Door("N1-unhosted", null, 0, 5),
        Door("D6-ghost-host", "ghost", 0, 7),
        Door("D7-no-location", "W2-shared", null, null),
        Door("D8-nothing", null, null, null)
    };

    public static CandidateObservationSet Observations(
        IEnumerable<ZoneObservation>? zones = null,
        IEnumerable<MemberObservation>? members = null,
        IEnumerable<OpeningObservation>? openings = null) =>
        new(PackageId, "level-1F", "1F", zones ?? Zones(), members ?? Members(), openings ?? Openings());

    public static CandidateSet Resolve(CandidateResolutionOptions? options = null) =>
        CandidateResolver.Resolve(Observations(), options);

    public static ZoneRelation? Relation(CandidateSet set, string uid, Guid zone)
    {
        var member = set.Members.FirstOrDefault(m => m.Source.ElementUniqueId == uid);
        if (member is not null) return member.RelationTo(zone);
        return set.Openings.FirstOrDefault(o => o.Source.ElementUniqueId == uid)?.RelationTo(zone);
    }
}
