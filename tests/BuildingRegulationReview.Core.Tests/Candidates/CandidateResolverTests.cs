using System;
using System.Collections.Generic;
using System.Linq;
using BuildingRegulationReview.Application.Candidates;
using BuildingRegulationReview.Application.Diagnostics;
using BuildingRegulationReview.Application.Rules;
using BuildingRegulationReview.Domain.Geometry;
using BuildingRegulationReview.Domain.Reviews;
using BuildingRegulationReview.Domain.Rules;
using Xunit;
using static BuildingRegulationReview.Core.Tests.Candidates.CandidateModel;

namespace BuildingRegulationReview.Core.Tests.Candidates;

public sealed class CandidateResolverTests
{
    private const string None = "-";

    // The fixed model's expected candidate set: every element against zones A and B. "-" means the
    // element is not a candidate for that zone at all.
    public static IEnumerable<object[]> ExpectedRelations() => new[]
    {
        Row("W1-bottom", "Boundary", "Boundary"),
        Row("W2-shared", "Boundary", "Boundary"),
        Row("W3-partition", "Inside", None),
        Row("W4-crossing", "Crossing", "Crossing"),
        Row("W5-offset", None, "Ambiguous:BoundaryOffCenterline"),
        Row("W8-long", "Boundary", None),
        Row("W10-left", "Boundary", None),
        Row("CW-right", None, "Boundary"),
        Row("C1-corner", "Boundary", "Boundary"),
        Row("C2-inside", "Inside", None),
        Row("C3-flush", "Ambiguous:BoundaryAlongOutline", "Ambiguous:BoundaryAlongOutline"),
        Row("B1-shared", "Boundary", "Boundary"),
        Row("F1-slab", "Crossing", "Crossing"),
        Row("D1-shared", "Boundary", "Boundary"),
        Row("WN1-bottom", None, "Boundary"),
        Row("D4-on-boundary", "Boundary", None),
        Row("D5-ambiguous-host", None, "Ambiguous:HostRelationAmbiguous"),
        Row("P1-panel", None, "Ambiguous:CurtainWallOpening"),
        Row("N1-unhosted", "Ambiguous:NonHostedOpening", None),
        Row("D6-ghost-host", "Ambiguous:HostNotResolved", None),
        Row("D7-no-location", "Ambiguous:OpeningLocationUnknown", "Ambiguous:OpeningLocationUnknown")
    };

    private static object[] Row(string uid, string a, string b) => new object[] { uid, a, b };

    [Theory]
    [MemberData(nameof(ExpectedRelations))]
    public void Fixed_model_resolves_each_element_as_expected(string uid, string expectedA, string expectedB)
    {
        var set = Resolve();

        Assert.Equal(expectedA, Describe(Relation(set, uid, ZoneA)));
        Assert.Equal(expectedB, Describe(Relation(set, uid, ZoneB)));
    }

    [Fact]
    public void Fixed_model_candidate_lists_are_exactly_the_expected_ones()
    {
        var set = Resolve();
        var expected = ExpectedRelations().Select(r => (string)r[0]).OrderBy(x => x, StringComparer.Ordinal).ToList();
        var actual = set.Members.Select(m => m.Source.ElementUniqueId)
            .Concat(set.Openings.Select(o => o.Source.ElementUniqueId))
            .OrderBy(x => x, StringComparer.Ordinal).ToList();

        Assert.Equal(expected, actual);
        Assert.Equal(1, set.UnrelatedMemberCount);   // W6-far
        Assert.Equal(2, set.UnrelatedOpeningCount);  // D2 inside a zone, D3 off the boundary
        Assert.DoesNotContain(set.Members, m => m.Relations.Any(r => r.ZoneId == ZoneC));
    }

    [Fact]
    public void Fixed_model_lists_every_ambiguity_once()
    {
        var set = Resolve();
        var actual = set.Ambiguities
            .Select(a => $"{a.Kind}|{(a.ZoneId.HasValue ? Name(a.ZoneId.Value) : "-")}|{string.Join(",", a.SubjectUniqueIds)}")
            .ToList();

        var expected = new[]
        {
            "OpeningLocationUnknown|-|D8-nothing",
            "NoPlanGeometry|-|W9-nogeometry",
            "BoundaryAlongOutline|A|C3-flush",
            "HostNotResolved|A|D6-ghost-host",
            "OpeningLocationUnknown|A|D7-no-location",
            "NonHostedOpening|A|N1-unhosted",
            "BoundaryAlongOutline|B|C3-flush",
            "HostRelationAmbiguous|B|D5-ambiguous-host",
            "OpeningLocationUnknown|B|D7-no-location",
            "CurtainWallOpening|B|P1-panel",
            "BoundaryOffCenterline|B|W5-offset",
            "ZoneNotEnclosed|C|area-c"
        };

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Same_model_read_in_another_order_yields_the_same_signature()
    {
        var first = Resolve();
        var shuffled = CandidateResolver.Resolve(Observations(
            Zones().Reverse(),
            Members().Reverse().Skip(3).Concat(Members().Reverse().Take(3)),
            Openings().OrderBy(o => o.Source.ElementUniqueId.Length).ThenByDescending(o => o.Source.ElementUniqueId, StringComparer.Ordinal)));

        Assert.Equal(first.Signature(), shuffled.Signature());
        Assert.Equal(first.Signature(), Resolve().Signature());
        Assert.Contains("member|Wall|W2-shared|" + ZoneA.ToString("D") + "|Boundary|-|b=10|", first.Signature(), StringComparison.Ordinal);
    }

    [Fact]
    public void Zones_report_revit_and_measured_area_and_whether_they_can_be_reviewed()
    {
        var set = Resolve();

        var a = set.Zone(ZoneA)!;
        Assert.True(a.IsClear);
        Assert.True(a.IsMeasurable);
        Assert.Equal(100, a.GeometricAreaSquareMeters!.Value, 6);
        Assert.Equal(100, a.RevitAreaSquareMeters!.Value, 6);

        var c = set.Zone(ZoneC)!;
        Assert.False(c.IsMeasurable);
        Assert.Equal(new[] { CandidateAmbiguityKind.ZoneNotEnclosed }, c.Problems);
        Assert.Null(c.GeometricAreaSquareMeters);
        Assert.Null(c.RevitAreaSquareMeters);

        Assert.False(set.CanReview);
        Assert.True(CandidateResolver.Resolve(Observations(zones: Zones().Take(2))).CanReview);
        Assert.False(CandidateResolver.Resolve(Observations(zones: Array.Empty<ZoneObservation>())).CanReview);
    }

    [Fact]
    public void Zone_with_a_hole_measures_net_area_and_its_hole_walls_are_boundary_walls()
    {
        var outer = Rect(0, 0, 10, 10);
        var hole = new[] { P(4, 4), P(4, 6), P(6, 6), P(6, 4) };
        var set = CandidateResolver.Resolve(Observations(
            new[] { Zone(ZoneA, "A 區", "area-a", outer, hole) },
            new[] { Wall("shaft", 4, 4, 6, 4), Wall("room", 2, 1, 2, 3) },
            Array.Empty<OpeningObservation>()));

        Assert.Equal(96, set.Zone(ZoneA)!.GeometricAreaSquareMeters!.Value, 6);
        Assert.Equal(ZoneRelationKind.Boundary, Relation(set, "shaft", ZoneA)!.Kind);
        Assert.Equal(ZoneRelationKind.Inside, Relation(set, "room", ZoneA)!.Kind);
    }

    [Theory]
    [InlineData(0.04, "Boundary")]      // within the 50 mm boundary tolerance
    [InlineData(0.09, "Ambiguous:BoundaryOffCenterline")] // inside the 100 mm half thickness
    [InlineData(0.30, "Inside")]        // clear of the boundary
    public void Wall_near_the_boundary_is_decided_by_how_far_its_centreline_sits(double offset, string expected)
    {
        var set = CandidateResolver.Resolve(Observations(
            Zones().Take(1),
            new[] { Wall("W", 1, offset, 9, offset) },
            Array.Empty<OpeningObservation>()));

        Assert.Equal(expected, Describe(Relation(set, "W", ZoneA)));
    }

    [Fact]
    public void Wall_that_only_abuts_the_boundary_is_not_a_boundary_wall()
    {
        var set = CandidateResolver.Resolve(Observations(
            Zones().Take(2),
            new[] { Wall("stub", 10, 5, 12, 5), Wall("short", 9.95, 7, 10.05, 7) },
            Array.Empty<OpeningObservation>()));

        Assert.Equal("Inside", Describe(Relation(set, "stub", ZoneB)));
        Assert.Equal(None, Describe(Relation(set, "stub", ZoneA)));
        Assert.Equal(1, set.UnrelatedMemberCount); // "short" is 100 mm straddling the boundary
    }

    [Fact]
    public void Strategy_per_category_limits_member_candidates_but_keeps_ambiguities_and_hosts()
    {
        var options = new CandidateResolutionOptions(memberRelations: new[]
        {
            new KeyValuePair<CandidateCategory, CandidateRelationKinds>(CandidateCategory.Wall, CandidateRelationKinds.Boundary),
            new KeyValuePair<CandidateCategory, CandidateRelationKinds>(CandidateCategory.Floor, CandidateRelationKinds.None)
        });
        var set = Resolve(options);

        Assert.Equal(None, Describe(Relation(set, "W3-partition", ZoneA)));
        Assert.Equal(None, Describe(Relation(set, "W4-crossing", ZoneA)));
        Assert.Equal("Ambiguous:BoundaryOffCenterline", Describe(Relation(set, "W5-offset", ZoneB)));
        Assert.Equal(None, Describe(Relation(set, "F1-slab", ZoneA)));
        Assert.Equal("Inside", Describe(Relation(set, "C2-inside", ZoneA)));
        Assert.Equal("Boundary", Describe(Relation(set, "D1-shared", ZoneA)));

        var noWalls = Resolve(new CandidateResolutionOptions(memberRelations: new[]
        {
            new KeyValuePair<CandidateCategory, CandidateRelationKinds>(CandidateCategory.Wall, CandidateRelationKinds.None)
        }));
        Assert.DoesNotContain(noWalls.Members, m => m.Category == CandidateCategory.Wall);
        Assert.DoesNotContain(noWalls.Ambiguities, a => a.Kind == CandidateAmbiguityKind.NoPlanGeometry);
        Assert.Equal("Boundary", Describe(Relation(noWalls, "D1-shared", ZoneB)));
    }

    [Fact]
    public void Interior_openings_are_candidates_only_when_asked_for()
    {
        var set = Resolve(new CandidateResolutionOptions(includeInteriorOpenings: true));

        Assert.Equal("Inside", Describe(Relation(set, "D2-partition", ZoneA)));
        Assert.Equal(None, Describe(Relation(set, "D3-off-boundary", ZoneA)));
        Assert.Equal(1, set.UnrelatedOpeningCount);
    }

    [Fact]
    public void Linked_elements_are_manual_review_under_the_mvp_policy()
    {
        var linked = new MemberObservation(Source("L1", link: "link-instance"), CandidateCategory.Wall,
            new[] { P(10, 0), P(10, 10) }, widthFeet: M(0.2));
        var set = CandidateResolver.Resolve(Observations(Zones().Take(2), new[] { linked }, Array.Empty<OpeningObservation>()));

        var relation = Relation(set, "L1", ZoneA)!;
        Assert.Equal(CandidateAmbiguityKind.LinkedElement, relation.Ambiguity);
        Assert.Contains("構成區劃邊界", relation.Message, StringComparison.Ordinal);
        Assert.Equal(2, set.Ambiguities.Count(a => a.Kind == CandidateAmbiguityKind.LinkedElement));
        Assert.Equal("link-instance", set.Members.Single().EvidenceFor(ZoneA).Find("source.linkInstanceUniqueId")!.Text);
    }

    [Fact]
    public void Overlapping_zones_are_flagged_on_both_sides_but_touching_zones_are_not()
    {
        var set = CandidateResolver.Resolve(Observations(
            new[]
            {
                Zone(ZoneA, "A 區", "area-a", Rect(0, 0, 10, 10)),
                Zone(ZoneB, "B 區", "area-b", Rect(8, 0, 18, 10))
            },
            Array.Empty<MemberObservation>(),
            Array.Empty<OpeningObservation>()));

        Assert.All(set.Zones, z => Assert.Equal(new[] { CandidateAmbiguityKind.ZonesOverlap }, z.Problems));
        var overlaps = set.Ambiguities.Where(a => a.Kind == CandidateAmbiguityKind.ZonesOverlap).ToList();
        Assert.Equal(2, overlaps.Count);
        Assert.All(overlaps, a => Assert.Equal(new[] { "area-a", "area-b" }, a.SubjectUniqueIds));
        Assert.Equal(ZoneB.ToString("D"), overlaps.Single(a => a.ZoneId == ZoneA).Evidence.Find("candidate.otherZoneId")!.Text);
        Assert.True(set.CanReview);

        Assert.All(Resolve().Zones.Where(z => z.ZoneId != ZoneC), z => Assert.True(z.IsClear));
    }

    [Fact]
    public void Evidence_traces_the_relation_back_to_the_source_element()
    {
        var set = Resolve();
        var wall = set.Members.Single(m => m.Source.ElementUniqueId == "W2-shared");
        var evidence = wall.EvidenceFor(ZoneA);

        Assert.Equal("Boundary", evidence.Find("candidate.relation")!.Text);
        Assert.Equal(ZoneA.ToString("D"), evidence.Find("candidate.zoneId")!.Text);
        Assert.Equal("W2-shared", evidence.Find("source.elementUniqueId")!.Text);
        Assert.Equal(Document, evidence.Find("source.documentUniqueId")!.Text);
        Assert.Equal("Walls", evidence.Find("source.category")!.Text);
        Assert.Equal("RC 200", evidence.Find("source.typeName")!.Text);
        var boundary = evidence.Find("candidate.boundaryLength")!;
        Assert.Equal(ReviewUnit.Meter, boundary.Unit);
        Assert.Equal(10, boundary.Number, 6);
        Assert.Equal(0.2, evidence.Find("source.width")!.Number, 6);

        var door = set.Openings.Single(o => o.Source.ElementUniqueId == "D1-shared").EvidenceFor(ZoneB);
        Assert.Equal("W2-shared", door.Find("source.hostUniqueId")!.Text);
        Assert.Equal(0, door.Find("candidate.distanceToBoundary")!.Number, 9);
        Assert.False(door.Find("source.hostIsCurtainWall")!.Flag);
    }

    [Fact]
    public void Ambiguity_becomes_a_manual_review_result_attributed_to_the_rule_set()
    {
        var set = Resolve();
        var ruleSet = CompiledRules();
        var runId = Guid.NewGuid();

        var offset = set.Ambiguities.Single(a => a.Kind == CandidateAmbiguityKind.BoundaryOffCenterline);
        var result = offset.ToReviewResult(Guid.NewGuid(), runId, PackageId, "FireResistance", ruleSet);
        Assert.Equal(ReviewStatus.ManualReview, result.Status);
        Assert.Equal(new[] { "W5-offset" }, result.SubjectUniqueIds);
        Assert.Equal(ZoneB.ToString("D"), result.ZoneId);
        Assert.Equal("set", result.RuleId);
        Assert.Equal("2024.1", result.RuleVersion);
        Assert.Contains("人工覆核", result.Message, StringComparison.Ordinal);
        Assert.Equal("BoundaryOffCenterline", result.Evidence.Find("candidate.ambiguity")!.Text);
        Assert.Equal(ReviewErrorCode.CandidateAmbiguous, offset.ErrorCode);

        var global = set.Ambiguities.Single(a => a.SubjectUniqueIds.Contains("W9-nogeometry"));
        Assert.Null(global.ToReviewResult(Guid.NewGuid(), runId, PackageId, "FireResistance", ruleSet).ZoneId);

        var zone = set.Ambiguities.Single(a => a.Kind == CandidateAmbiguityKind.ZoneNotEnclosed);
        Assert.Equal(ReviewErrorCode.CandidateZoneUnusable, zone.ErrorCode);
        Assert.True(ReviewErrorCode.IsKnown(ReviewErrorCode.CandidateAmbiguous));
        Assert.True(ReviewErrorCode.IsKnown(ReviewErrorCode.CandidateZoneUnusable));
    }

    [Fact]
    public void Facts_carry_the_relation_into_the_rule_engine()
    {
        var set = Resolve();
        var wall = set.Members.Single(m => m.Source.ElementUniqueId == "W2-shared");
        var facts = CandidateFacts.ForMember(set, wall, ZoneA);

        Assert.Equal(ZoneA.ToString("D"), facts.Find("zone.id")!.Text);
        Assert.Equal("1F", facts.Find("zone.levelName")!.Text);
        Assert.Equal("Walls", facts.Find("element.category")!.Text);
        Assert.True(facts.Find("element.isCompartmentBoundary")!.Flag);
        Assert.True(facts.Find("element.isStructural")!.Flag);
        Assert.Equal("RC 200", facts.Find("element.typeName")!.Text);
        Assert.NotNull(facts.GapFor("element.providedFireRating"));

        var partition = CandidateFacts.ForMember(set, set.Members.Single(m => m.Source.ElementUniqueId == "W3-partition"), ZoneA);
        Assert.False(partition.Find("element.isCompartmentBoundary")!.Flag);

        var door = CandidateFacts.ForOpening(set, set.Openings.Single(o => o.Source.ElementUniqueId == "D1-shared"), ZoneB);
        Assert.Equal("Door", door.Find("opening.kind")!.Text);
        Assert.True(door.Find("opening.isHosted")!.Flag);
        Assert.True(door.Find("opening.hostIsCompartmentBoundary")!.Flag);
        Assert.Equal("W2-shared", door.Find("opening.hostUniqueId")!.Text);
        Assert.Equal(2.1, door.Find("opening.area")!.Number, 6);
        Assert.NotNull(door.GapFor("opening.providedFireProtection"));

        var ambiguous = set.Members.Single(m => m.Source.ElementUniqueId == "W5-offset");
        Assert.Throws<ArgumentException>(() => CandidateFacts.ForMember(set, ambiguous, ZoneB));
        Assert.Throws<ArgumentException>(() => CandidateFacts.ForMember(set, wall, ZoneC));
    }

    [Fact]
    public void Facts_decide_applicability_without_turning_missing_ratings_into_fail()
    {
        var set = Resolve();
        var rules = CompiledRules();
        var engine = new RuleEngine(rules);
        var context = new RuleEvaluationContext(new DateTime(2025, 6, 1), "TW");

        var boundary = CandidateFacts.ForMember(set, set.Members.Single(m => m.Source.ElementUniqueId == "W2-shared"), ZoneA);
        Assert.Equal(ReviewStatus.InsufficientData, engine.Evaluate(RuleCategory.FireResistance, boundary, context).Status);

        boundary.Set("element.providedFireRating", 120, ReviewUnit.Minute);
        Assert.Equal(ReviewStatus.Pass, engine.Evaluate(RuleCategory.FireResistance, boundary, context).Status);

        var partition = CandidateFacts.ForMember(set, set.Members.Single(m => m.Source.ElementUniqueId == "W3-partition"), ZoneA);
        Assert.Equal(ReviewStatus.NotApplicable, engine.Evaluate(RuleCategory.FireResistance, partition, context).Status);
    }

    [Fact]
    public void Observations_reject_inconsistent_input()
    {
        Assert.Throws<ArgumentException>(() => new MemberObservation(Source("x"), CandidateCategory.Wall, outlines: new[] { Rect(0, 0, 1, 1) }));
        Assert.Throws<ArgumentException>(() => new MemberObservation(Source("x"), CandidateCategory.Column, new[] { P(0, 0), P(1, 0) }));
        Assert.Throws<ArgumentException>(() => new MemberObservation(Source("x"), CandidateCategory.Floor, isCurtainWall: true));
        Assert.Throws<ArgumentOutOfRangeException>(() => new MemberObservation(Source("x"), CandidateCategory.Door));
        Assert.Throws<ArgumentOutOfRangeException>(() => new OpeningObservation(Source("x"), CandidateCategory.Wall, null, null));
        Assert.Throws<ArgumentOutOfRangeException>(() => new OpeningObservation(Source("x"), CandidateCategory.Door, null, null, widthFeet: 0));

        Assert.Throws<ArgumentException>(() => Observations(zones: Zones().Concat(Zones().Take(1))));
        Assert.Throws<ArgumentException>(() => Observations(members: Members().Concat(Members().Take(1))));
        Assert.Throws<ArgumentException>(() => Observations(zones: new[]
        {
            Zone(ZoneA, "A", "area-x", Rect(0, 0, 1, 1)),
            Zone(ZoneB, "B", "area-x", Rect(2, 0, 3, 1))
        }));
    }

    [Fact]
    public void Options_validate_their_ranges()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new CandidateResolutionOptions(boundaryToleranceFeet: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CandidateResolutionOptions(parallelDegrees: 45));
        Assert.Throws<ArgumentException>(() => new CandidateResolutionOptions(boundaryToleranceFeet: M(0.5), openingSearchFeet: M(0.3)));
        Assert.Throws<ArgumentException>(() => new CandidateResolutionOptions(memberRelations: new[]
        {
            new KeyValuePair<CandidateCategory, CandidateRelationKinds>(CandidateCategory.Door, CandidateRelationKinds.All)
        }));

        var defaults = CandidateResolutionOptions.Default;
        Assert.Equal(50, PlanUnits.FeetToMillimeters(defaults.BoundaryToleranceFeet), 6);
        Assert.All(CandidateCategories.Members, c => Assert.Equal(CandidateRelationKinds.All, defaults.RelationsFor(c)));
    }

    private static CompiledRuleSet CompiledRules()
    {
        var rule = new Rule("wall-1", "1", RuleCategory.FireResistance, "第 X 條", new DateTime(2024, 1, 1), "TW", 10,
            new RuleExpression("element.isCompartmentBoundary == true"),
            new RuleExpression("element.providedFireRating >= 60 min"),
            Array.Empty<RuleExpression>());
        var compiled = RuleSetCompiler.Compile(new RuleSet("set", "2024.1", "測試規則集", new[] { rule }));
        Assert.True(compiled.IsSuccess, compiled.IsFailure ? compiled.Error.TechnicalDetail : null);
        return compiled.Value;
    }

    private static string Describe(ZoneRelation? relation) =>
        relation is null ? None : relation.IsAmbiguous ? $"Ambiguous:{relation.Ambiguity}" : relation.Kind.ToString();

    private static string Name(Guid zone) => zone == ZoneA ? "A" : zone == ZoneB ? "B" : zone == ZoneC ? "C" : zone.ToString("D");
}
