using System;
using System.Collections.Generic;
using System.Linq;
using BuildingRegulationReview.Application.Candidates;
using BuildingRegulationReview.Application.Checks;
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
        // 牆跨越區劃邊界：關係是 Crossing，但「算不算這道區劃的邊界」仍存疑（C-18）。
        Row("W4-crossing", "Crossing:BoundaryCrossed", "Crossing:BoundaryCrossed"),
        Row("W5-offset", None, "Ambiguous:BoundaryOffCenterline"),
        Row("W8-long", "Boundary", None),
        Row("W10-left", "Boundary", None),
        Row("CW-right", None, "Boundary"),
        Row("C1-corner", "Boundary", "Boundary"),
        Row("C2-inside", "Inside", None),
        Row("C3-flush", "Ambiguous:BoundaryAlongOutline", "Ambiguous:BoundaryAlongOutline"),
        Row("B1-shared", "Boundary", "Boundary"),
        // 樓板跨越區劃邊界是常態而非疑義，所以不掛 BoundaryCrossed（C-18，與 W4-crossing 對照）。
        Row("F1-slab", "Crossing", "Crossing"),
        Row("D1-shared", "Boundary", "Boundary"),
        Row("WN1-bottom", None, "Boundary"),
        Row("D4-on-boundary", "Boundary", None),
        Row("D5-ambiguous-host", None, "Ambiguous:HostRelationAmbiguous"),
        Row("P1-panel", None, "Facade"), // nothing beyond CW-right: the outer facade
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
            "BoundaryCrossed|A|W4-crossing",
            "BoundaryAlongOutline|B|C3-flush",
            "HostRelationAmbiguous|B|D5-ambiguous-host",
            "OpeningLocationUnknown|B|D7-no-location",
            "BoundaryCrossed|B|W4-crossing",
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

    /// <summary>
    /// C-18：牆的中心線跨越區劃邊界時，幾何既沒說它構成邊界、也沒說它不構成，所以 Crossing 關係要
    /// 掛一筆 <see cref="CandidateAmbiguityKind.BoundaryCrossed"/> 並登成一筆疑義。不掛的話
    /// <c>element.isCompartmentBoundary</c> 逕取 false，第79條第1項整列「不適用」而靜默免除。
    /// 對照組是樓板：一片樓板本來就跨多個區劃，那是常態而非疑義，不掛也不登。
    /// </summary>
    [Fact]
    public void Wall_crossing_a_boundary_is_a_doubt_about_whether_it_bounds_but_a_floor_is_not()
    {
        var set = Resolve();

        foreach (var zone in new[] { ZoneA, ZoneB })
        {
            var wall = Relation(set, "W4-crossing", zone)!;
            Assert.Equal(ZoneRelationKind.Crossing, wall.Kind);
            Assert.Equal(CandidateAmbiguityKind.BoundaryCrossed, wall.Ambiguity);
            Assert.True(wall.CarriesAmbiguity);
            Assert.False(wall.IsBoundary);
            Assert.False(wall.IsAmbiguous);
            Assert.Contains(set.Ambiguities, a =>
                a.Kind == CandidateAmbiguityKind.BoundaryCrossed &&
                a.ZoneId == zone &&
                a.SubjectUniqueIds.Contains("W4-crossing"));
        }

        var slab = Relation(set, "F1-slab", ZoneA)!;
        Assert.Equal(ZoneRelationKind.Crossing, slab.Kind);
        Assert.False(slab.CarriesAmbiguity);
        Assert.DoesNotContain(set.Ambiguities, a => a.SubjectUniqueIds.Contains("F1-slab"));
    }

    /// <summary>
    /// 不變量：除了「跨越邊界 ＋ BoundaryCrossed」這一個組合，已判定的關係一律不得帶疑義——
    /// 帶了就表示某處把「已判定」與「存疑」混為一談（C-18）。白名單是雙向的：BoundaryCrossed
    /// 講的是「中心線跨過去了，但算不算這道區劃的邊界沒有答案」，那句話也只有 Crossing 說得通，
    /// 掛到別的 kind 上 <c>IsBoundaryDoubt</c> 仍會收它、<c>Settled</c> 仍會評兩支，等於用一個
    /// 量不出來的前提去推論。
    /// </summary>
    [Fact]
    public void Only_an_ambiguous_relation_or_a_crossing_one_may_name_an_ambiguity()
    {
        Assert.Throws<ArgumentException>(() => new ZoneRelation(ZoneA, ZoneRelationKind.Ambiguous, "無法判定。"));
        Assert.Throws<ArgumentException>(() => new ZoneRelation(ZoneA, ZoneRelationKind.Boundary, "構成邊界。",
            null, CandidateAmbiguityKind.BoundaryCrossed));
        Assert.Throws<ArgumentException>(() => new ZoneRelation(ZoneA, ZoneRelationKind.Crossing, "跨越邊界。",
            null, CandidateAmbiguityKind.BoundaryOffCenterline));

        // 反向：BoundaryCrossed 只屬於 Crossing。Ambiguous 帶它會穿過上面兩道檢查，所以要單獨擋。
        Assert.Throws<ArgumentException>(() => new ZoneRelation(ZoneA, ZoneRelationKind.Ambiguous, "無法判定。",
            null, CandidateAmbiguityKind.BoundaryCrossed));

        var crossing = new ZoneRelation(ZoneA, ZoneRelationKind.Crossing, "跨越邊界。",
            null, CandidateAmbiguityKind.BoundaryCrossed);
        Assert.True(crossing.CarriesAmbiguity);
        Assert.False(crossing.IsAmbiguous);

        var plain = new ZoneRelation(ZoneA, ZoneRelationKind.Crossing, "跨越邊界。");
        Assert.False(plain.CarriesAmbiguity);
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

    /// <summary>
    /// 驗證清單 C-16：開口位置偏離 host 牆位置線時，不得拿那個位置去排除它。實機是一扇室內帷幕牆上的
    /// 門，位置回退到含門扇的外接矩形（門扇 90 度開啟，中心被拉離牆面 502 mm，而半牆厚＋容許值只有
    /// 175 mm），於是被判「離邊界太遠」→ Inside → 被 IncludeInteriorOpenings 濾空 → 併入
    /// UnrelatedOpeningCount，檢討表、日誌、統計三邊都不留痕跡。
    /// </summary>
    [Fact]
    public void An_opening_whose_location_misses_its_host_centreline_stays_a_candidate_for_manual_review()
    {
        var set = CandidateResolver.Resolve(Observations(
            Zones().Take(1),
            new[] { Wall("CW-boundary", 10, 0, 10, 10, width: 0.25, curtain: true) },
            new[] { Door("D-off-centreline", "CW-boundary", 10.5, 4) }));

        // 沒有從候選集合裡消失，而且留下了看得見的說明。
        Assert.Equal("Ambiguous:OpeningOffHostCenterline", Describe(Relation(set, "D-off-centreline", ZoneA)));
        Assert.Empty(set.UnrelatedOpenings);

        var ambiguity = set.Ambiguities.Single(a => a.Kind == CandidateAmbiguityKind.OpeningOffHostCenterline);
        Assert.Equal(new[] { "D-off-centreline" }, ambiguity.SubjectUniqueIds);
        Assert.Equal(ZoneA, ambiguity.ZoneId);
        Assert.Contains("0.5 m", ambiguity.Message, StringComparison.Ordinal);
        Assert.Contains("0.175 m", ambiguity.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// C-16 的護欄要擋的是「host 不是判在區劃內部」的每一種，不只 Boundary 與 Ambiguous。Crossing
    /// （C-18 讓它帶 BoundaryCrossed 疑義而 Kind 仍是 Crossing）兩個述詞都是 false，漏掉它就等於對
    /// 「開在跨越邊界的牆上、位置又不可信」的門重開 C-16 這個洞；同一個函式的 default: 分支對同一道
    /// Crossing host 只要位置可信就已經是 HostRelationAmbiguous 人工覆核了。
    /// </summary>
    [Fact]
    public void The_guardrail_also_covers_an_opening_hosted_on_a_wall_that_crosses_the_boundary()
    {
        var crossing = Wall("W-crossing", 8, 5, 12, 5);
        var set = CandidateResolver.Resolve(Observations(
            Zones().Take(1),
            new[] { crossing },
            new[] { Door("D-on-crossing", "W-crossing", 9, 5.5) }));

        // host 本身是 Crossing ＋ BoundaryCrossed（C-18），兩個述詞都是 false。
        Assert.Equal("Crossing:BoundaryCrossed", Describe(Relation(set, "W-crossing", ZoneA)));

        // 門偏離位置線 0.5 m（band = 0.1 + 0.05 = 0.15 m）：仍然是候選，仍然留下說明。
        Assert.Equal("Ambiguous:OpeningOffHostCenterline", Describe(Relation(set, "D-on-crossing", ZoneA)));
        Assert.Empty(set.UnrelatedOpenings);
        Assert.Contains(set.Ambiguities, a => a.Kind == CandidateAmbiguityKind.OpeningOffHostCenterline &&
                                              a.SubjectUniqueIds.Contains("D-on-crossing"));
    }

    /// <summary>
    /// 同一個洞的另一半：連平面位置都沒有的開口，host 判 Crossing 時也不得靜默消失。C-17 的佔位嵌板
    /// 正好走這條路（沒有量體、問不出格子時連位置都沒有），所以這一條與上一條是同一個判準。
    /// </summary>
    [Fact]
    public void An_opening_with_no_location_on_a_crossing_host_is_manual_review_not_silence()
    {
        var set = CandidateResolver.Resolve(Observations(
            Zones().Take(1),
            new[] { Wall("W-crossing", 8, 5, 12, 5) },
            new[] { Door("P-no-location", "W-crossing", null, null, CandidateCategory.CurtainPanel) }));

        Assert.Equal("Ambiguous:OpeningLocationUnknown", Describe(Relation(set, "P-no-location", ZoneA)));
        Assert.Empty(set.UnrelatedOpenings);
    }

    /// <summary>
    /// C-16 的對照組：位置確實落在 host 的位置線上、只是沿著牆離那段邊界很遠的開口，仍然不是候選
    /// ——一道長牆上只有一段構成邊界，站在另一段的門不是區劃開口（本類別的 remarks）。這一條是護欄的
    /// 邊界：擋的是「位置不可信」，不是「位置可信但離邊界遠」。
    /// </summary>
    [Fact]
    public void An_opening_on_the_centreline_but_away_from_the_boundary_is_still_not_a_candidate()
    {
        var set = CandidateResolver.Resolve(Observations(
            Zones().Take(1),
            new[] { Wall("W-long", -4, 10, 10, 10) },
            new[] { Door("D-far", "W-long", -2, 10) }));

        Assert.Equal(None, Describe(Relation(set, "D-far", ZoneA)));
        Assert.DoesNotContain(set.Ambiguities, a => a.Kind == CandidateAmbiguityKind.OpeningOffHostCenterline);
        Assert.Equal(1, set.UnrelatedOpeningCount);
    }

    /// <summary>
    /// C-16：不是候選的開口不可以只剩一個數字。每一件都要留下身分、離最近邊界多遠、是落在區劃內部
    /// 還是不屬於任何區劃——否則「因位置讀錯而假性落空」與「本來就不在邊界上」在輸出上長得一模一樣。
    /// </summary>
    [Fact]
    public void Openings_that_are_candidates_for_no_zone_are_listed_with_why()
    {
        var set = Resolve();

        Assert.Equal(new[] { "D2-partition", "D3-off-boundary" },
            set.UnrelatedOpenings.Select(o => o.Source.ElementUniqueId).ToArray());
        Assert.Equal(set.UnrelatedOpenings.Count, set.UnrelatedOpeningCount);

        var inside = set.UnrelatedOpenings.Single(o => o.Source.ElementUniqueId == "D2-partition");
        Assert.True(inside.IsInsideAZone);
        Assert.Equal(CandidateCategory.Door, inside.Category);
        Assert.Equal("D1", inside.TypeName);
        Assert.Equal(4.0, PlanUnits.FeetToMeters(inside.NearestBoundaryDistanceFeet!.Value), 9);

        var offBoundary = set.UnrelatedOpenings.Single(o => o.Source.ElementUniqueId == "D3-off-boundary");
        Assert.False(offBoundary.IsInsideAZone);
        Assert.Equal(2.0, PlanUnits.FeetToMeters(offBoundary.NearestBoundaryDistanceFeet!.Value), 9);
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

    /// <summary>
    /// C-18：這道守衛問的是「關係帶著疑義」而不是「關係是 Ambiguous」。跨越區劃邊界的牆關係是
    /// <see cref="ZoneRelationKind.Crossing"/>（量得出來、不是 Ambiguous），但它算不算這道區劃的
    /// 邊界沒有答案——讓它穿過守衛，<c>element.isCompartmentBoundary</c> 就會逕取 <c>false</c>，
    /// 第79條第1項整列「不適用」而靜默免除，正是 C-18 的因果鏈。
    /// </summary>
    /// <remarks>
    /// 今天沒有呼叫端會踩到它（主迴圈已先 <c>CarriesAmbiguity → continue</c>，<c>Settled</c> 一律傳
    /// <c>Assuming</c> 的複本），所以這一條守的是**將來**：任何 check 新增一個 <c>ForMember</c>
    /// 呼叫而忘了先排除帶疑義的關係時，這裡會擋下來，而不是再靜默免除一次。
    /// </remarks>
    [Fact]
    public void Facts_refuse_a_crossing_relation_whose_boundary_role_has_no_answer()
    {
        var set = Resolve();
        var crossing = set.Members.Single(m => m.Source.ElementUniqueId == "W4-crossing");
        var relation = crossing.Relations.First(r => r.Kind == ZoneRelationKind.Crossing);

        Assert.False(relation.IsAmbiguous, "前提：Crossing 不是 Ambiguous，所以舊守衛放得過它。");
        Assert.True(relation.CarriesAmbiguity);
        Assert.Equal(CandidateAmbiguityKind.BoundaryCrossed, relation.Ambiguity);

        Assert.Throws<ArgumentException>(() => CandidateFacts.ForMember(set, crossing, relation.ZoneId));
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

    // --- 室內帷幕牆（docs/tools/fire-review/rules/curtain-wall-fire-compartment.md §4.8）---------------------
    //
    // The fixture's CW-right at x = 20 has 區劃 B inside it and nothing beyond: the building's 外牆,
    // whose openings 第79條第1項 does not govern (row P1-panel above, Facade). A curtain wall on the
    // A｜B line at x = 10 has a 區劃 on each side instead, so it is a 區劃分隔 and its openings are
    // judged like any boundary wall's.

    private static CandidateSet WithInteriorCurtainWall(params OpeningObservation[] openings)
    {
        var members = Members()
            .Where(m => m.Source.ElementUniqueId != "W2-shared")
            .Append(Wall("CW-shared", 10, 0, 10, 10, width: 0.1, curtain: true))
            .ToList();

        return CandidateResolver.Resolve(Observations(members: members, openings: openings));
    }

    [Fact]
    public void An_interior_curtain_wall_is_classified_interior_on_the_member_candidate()
    {
        var member = WithInteriorCurtainWall().Members.Single(m => m.Source.ElementUniqueId == "CW-shared");

        Assert.Equal(CurtainWallExposure.Interior, member.CurtainWallExposure!.Exposure);
        Assert.Equal("Interior", member.CurtainWallExposureText);

        // 證據要說出判定依據，審查者才能分辨這是量到的還是猜的。
        var evidence = member.EvidenceFor(ZoneA);
        Assert.Equal(ReviewValue.OfText("Interior"), evidence.Find("source.curtainWallExposure"));
        Assert.NotNull(evidence.Find("source.curtainWallExposureReason"));
    }

    [Fact]
    public void An_exterior_curtain_wall_is_classified_exterior_and_a_plain_wall_is_neither()
    {
        var set = Resolve();

        Assert.Equal("Exterior", set.Members.Single(m => m.Source.ElementUniqueId == "CW-right").CurtainWallExposureText);

        var plain = set.Members.Single(m => m.Source.ElementUniqueId == "W2-shared");
        Assert.Null(plain.CurtainWallExposure);
        Assert.Equal(MemberCandidate.NotCurtainWallText, plain.CurtainWallExposureText);
        Assert.Null(plain.EvidenceFor(ZoneA).Find("source.curtainWallExposure"));
    }

    [Fact]
    public void A_door_in_an_interior_curtain_wall_relates_to_both_zones_as_a_boundary()
    {
        var set = WithInteriorCurtainWall(Door("D-cw", "CW-shared", 10, 4));

        foreach (var zone in new[] { ZoneA, ZoneB })
        {
            var relation = Relation(set, "D-cw", zone);
            Assert.Equal(ZoneRelationKind.Boundary, relation!.Kind);
            Assert.Contains("區劃分隔而非建築物外牆", relation.Message);
        }
    }

    [Fact]
    public void The_opening_route_and_the_member_route_classify_the_same_wall_alike()
    {
        // 審查 3-B：兩條路徑探的位置不同（開口在它自己的位置、構件在牆的四分之一處），判定表只有一份,
        // 所以同一道牆兩邊必須得到同一個答案。
        var set = WithInteriorCurtainWall(Door("D-cw", "CW-shared", 10, 4));

        Assert.Equal(CurtainWallExposure.Interior,
            set.Members.Single(m => m.Source.ElementUniqueId == "CW-shared").CurtainWallExposure!.Exposure);
        Assert.Equal(ZoneRelationKind.Boundary, Relation(set, "D-cw", ZoneA)!.Kind);
    }

    [Fact]
    public void A_solid_panel_in_an_interior_curtain_wall_is_not_answered_by_an_opening_rule()
    {
        var panel = new OpeningObservation(Source("P-solid"), CandidateCategory.CurtainPanel, "CW-shared",
            P(10, 7), M(1.5), M(2.5), "type-panel", "實心板",
            CurtainPanelKind.Solid);

        var relation = Relation(WithInteriorCurtainWall(panel), "P-solid", ZoneA);

        Assert.Equal(ZoneRelationKind.Ambiguous, relation!.Kind);
        Assert.Equal(CandidateAmbiguityKind.CurtainPanelIsConstruction, relation.Ambiguity);
    }

    [Fact]
    public void A_panel_of_undeclared_kind_in_an_interior_curtain_wall_is_insufficient_data()
    {
        var panel = new OpeningObservation(Source("P-unknown"), CandidateCategory.CurtainPanel, "CW-shared",
            P(10, 7), M(1.5), M(2.5), "type-panel", "系統嵌板");

        var relation = Relation(WithInteriorCurtainWall(panel), "P-unknown", ZoneA);

        Assert.Equal(ZoneRelationKind.Ambiguous, relation!.Kind);
        Assert.Equal(CandidateAmbiguityKind.CurtainPanelKindUndeclared, relation.Ambiguity);
    }

    [Fact]
    public void An_opening_near_the_end_of_an_exterior_curtain_wall_is_still_a_facade()
    {
        // 沿牆 ±300 mm 的測站會踩出牆的兩端，但探測找的是**區劃**而不是牆，而區劃通常延伸到牆端之外,
        // 所以三站掉一站不影響多數——這個貼在 y = 0.2 m 的開口照樣判 Facade。
        var set = CandidateResolver.Resolve(Observations(openings: new[]
        {
            Door("P-corner", "CW-right", 20, 0.2, CandidateCategory.CurtainPanel)
        }));

        Assert.Equal(ZoneRelationKind.Facade, Relation(set, "P-corner", ZoneB)!.Kind);
    }

    [Fact]
    public void A_facade_opening_facing_another_zone_is_not_on_this_zones_boundary()
    {
        // 外牆那一路要求「有區劃的那一側，在開口自己的位置上，就是本區劃」——這是決議 18 之前的條件
        // 逐字。CW-right 的室內側是 B 區，所以它的嵌板與 A 區沒有邊界關係。
        var set = CandidateResolver.Resolve(Observations(openings: new[]
        {
            Door("P-corner", "CW-right", 20, 5, CandidateCategory.CurtainPanel)
        }));

        Assert.Equal(ZoneRelationKind.Facade, Relation(set, "P-corner", ZoneB)!.Kind);
        Assert.Null(Relation(set, "P-corner", ZoneA));
    }

    [Fact]
    public void A_boundary_curtain_wall_whose_exposure_is_undecided_gets_a_manual_review_of_its_own()
    {
        // 審查 4-5：判不出室內外的帷幕牆，牆體時效沒有規則回答（版本 3 排除 Unknown），外牆的三項
        // 交接規定也不適用。這一列讓那個空缺出現在檢討表上，而且**不依賴帷幕牆區劃交接那一端**——
        // 兩端的探測輸入不同，一端判外牆、另一端判未定時，少了它整片牆的牆體時效會靜默無答案。
        var wall = new MemberObservation(Source("CW-conflict"), CandidateCategory.Wall,
            new[] { P(20, 0), P(20, 10) }, widthFeet: M(0.1),
            typeUniqueId: "type-cw", typeName: "帷幕牆", isStructural: false, isCurtainWall: true,
            curtainWallFunction: CurtainWallFunctionDeclaration.Interior);

        var set = CandidateResolver.Resolve(Observations(members: new[] { wall }));

        var member = set.Members.Single(m => m.Source.ElementUniqueId == "CW-conflict");
        Assert.Equal(CurtainWallExposure.Unknown, member.CurtainWallExposure!.Exposure);
        Assert.Equal(CurtainWallExposureReason.DeclarationConflict, member.CurtainWallExposure.Reason);

        var ambiguity = Assert.Single(set.Ambiguities,
            a => a.Kind == CandidateAmbiguityKind.CurtainWallExposureUndecided);

        Assert.Equal(ZoneB, ambiguity.ZoneId);
        Assert.Contains("CW-conflict", ambiguity.SubjectUniqueIds);
        Assert.Contains("無法判定它是建築物外牆或室內帷幕牆", ambiguity.Message);
        Assert.Contains("Function 改回 Exterior", ambiguity.Message);
    }

    [Fact]
    public void A_boundary_curtain_wall_whose_exposure_is_decided_gets_no_such_row()
    {
        // 反面：判得出來的牆不該多出這一列。CW-right 是外牆、A｜B 線上的那片是室內。
        Assert.DoesNotContain(Resolve().Ambiguities,
            a => a.Kind == CandidateAmbiguityKind.CurtainWallExposureUndecided);
        Assert.DoesNotContain(WithInteriorCurtainWall().Ambiguities,
            a => a.Kind == CandidateAmbiguityKind.CurtainWallExposureUndecided);
    }

    [Fact]
    public void Only_a_curtain_panel_carries_a_panel_kind()
    {
        Assert.Throws<ArgumentException>(() => new OpeningObservation(
            Source("x"), CandidateCategory.Door, null, P(0, 0), curtainPanelKind: CurtainPanelKind.Glazed));
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

    /// <summary>
    /// 關係的種類，加上它掛著的疑義（有的話）。Crossing 也可能掛疑義（C-18），所以問的是
    /// <c>CarriesAmbiguity</c> 而不是 <c>IsAmbiguous</c>——否則 W4-crossing 與 F1-slab 這兩種不同的
    /// Crossing 在這張表上看起來一模一樣。
    /// </summary>
    private static string Describe(ZoneRelation? relation) =>
        relation is null ? None
            : relation.CarriesAmbiguity ? $"{relation.Kind}:{relation.Ambiguity}"
            : relation.Kind.ToString();

    private static string Name(Guid zone) => zone == ZoneA ? "A" : zone == ZoneB ? "B" : zone == ZoneC ? "C" : zone.ToString("D");

    // --- 一般外牆上的門窗（docs/tools/fire-review/rules/boundary-edge-elements.md §3.4）-------------------------
    //
    // W10-left 在 x = 0：A 區在它右側，左側沒有任何區劃。一般牆要「外側無區劃」加上「牆類型 Function
    // 為 Exterior」才推定為外牆；Function 是 Interior 或沒讀到，就照舊當區劃開口。

    private static MemberObservation PlainWall(string uid, double x0, double y0, double x1, double y1, CurtainWallFunctionDeclaration function) =>
        new(Source(uid), CandidateCategory.Wall, new[] { P(x0, y0), P(x1, y1) }, widthFeet: M(0.2),
            typeUniqueId: "type-rc200", typeName: "RC 200", isStructural: true, isCurtainWall: false,
            curtainWallFunction: function);

    private static CandidateSet WithLeftWall(CurtainWallFunctionDeclaration function, params OpeningObservation[] openings)
    {
        var members = Members()
            .Where(m => m.Source.ElementUniqueId != "W10-left")
            .Append(PlainWall("W10-left", 0, 0, 0, 10, function))
            .ToList();

        return CandidateResolver.Resolve(Observations(members: members, openings: openings));
    }

    [Fact]
    public void A_window_in_a_plain_exterior_wall_with_nothing_beyond_is_a_facade()
    {
        var set = WithLeftWall(CurtainWallFunctionDeclaration.Exterior, Door("WN-left", "W10-left", 0, 5, CandidateCategory.Window));

        var relation = Relation(set, "WN-left", ZoneA)!;
        Assert.Equal(ZoneRelationKind.Facade, relation.Kind);
        Assert.Contains("Function 為 Exterior", relation.Message);
        Assert.Contains("為建築物外牆而非區劃分隔", relation.Message);
    }

    [Theory]
    [InlineData(CurtainWallFunctionDeclaration.Interior)]
    [InlineData(CurtainWallFunctionDeclaration.NotRead)]
    [InlineData(CurtainWallFunctionDeclaration.Other)]
    public void A_plain_wall_not_declared_exterior_stays_a_boundary_even_with_nothing_beyond(CurtainWallFunctionDeclaration function)
    {
        // 開向未建 Area 走廊的內牆門，只看幾何會被誤推成外牆門而漏掉防火門；所以沒有 Exterior 宣告就不推定。
        var set = WithLeftWall(function, Door("D-left", "W10-left", 0, 5));

        Assert.Equal(ZoneRelationKind.Boundary, Relation(set, "D-left", ZoneA)!.Kind);
    }

    [Fact]
    public void A_plain_wall_declared_exterior_between_two_zones_is_still_a_boundary()
    {
        // Function 只是必要條件：兩側都有區劃的牆就是區劃分隔，宣告錯了也不會因此免掉防火門窗。
        var members = Members()
            .Where(m => m.Source.ElementUniqueId != "W2-shared")
            .Append(PlainWall("W2-shared", 10, 0, 10, 10, CurtainWallFunctionDeclaration.Exterior))
            .ToList();
        var set = CandidateResolver.Resolve(Observations(members: members));

        Assert.Equal(ZoneRelationKind.Boundary, Relation(set, "D1-shared", ZoneA)!.Kind);
        Assert.Equal(ZoneRelationKind.Boundary, Relation(set, "D1-shared", ZoneB)!.Kind);
    }
}
