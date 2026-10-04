using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using BuildingRegulationReview.Application.Candidates;
using BuildingRegulationReview.Application.Checks;
using BuildingRegulationReview.Application.Parameters;
using BuildingRegulationReview.Application.Rules;
using BuildingRegulationReview.Domain.Reviews;
using BuildingRegulationReview.Domain.Rules;
using Xunit;
using static BuildingRegulationReview.Core.Tests.Candidates.CandidateModel;

namespace BuildingRegulationReview.Core.Tests.Checks;

/// <summary>
/// 決議 37: the line between an exempt 挑空 and its 連通區劃 is no 區劃邊界 — and nothing else is.
/// Plans in metres; A 區 is the 挑空 unless a test says otherwise.
/// </summary>
public sealed class MergedAtriumsTests
{
    private static readonly Guid ZoneP = Guid.Parse("aaaaaaaa-0000-0000-0000-0000000000ff");
    private static readonly RuleEvaluationContext Today = new(new DateTime(2025, 6, 1), "TW");

    [Fact]
    public void The_wall_between_an_exempt_atrium_and_its_neighbour_is_interior()
    {
        var set = Resolve(new[] { Zone(ZoneA, "A 區", "area-a", Rect(0, 0, 10, 10)), Zone(ZoneB, "B 區", "area-b", Rect(10, 0, 20, 10)) },
            Partition("W-shared", 10, 0, 10, 10));

        Assert.True(MergedAtriums.Of(set, Inputs()).IsInterior(set.Members.Single()));
    }

    /// <summary>
    /// Code review 第 1 項: a wall that runs mostly between the 挑空 and B 區 but for its last metre
    /// faces a 管道間 still separates the 管道間, however short that stretch is.
    /// </summary>
    [Fact]
    public void A_short_stretch_along_a_shaft_keeps_a_long_wall_a_boundary()
    {
        var set = Resolve(new[]
            {
                Zone(ZoneA, "A 區", "area-a", Rect(0, 0, 10, 30)),
                Zone(ZoneB, "B 區", "area-b", Rect(10, 0, 20, 29)),
                Zone(ZoneP, "管道間", "area-p", Rect(10, 29, 20, 30))
            },
            Partition("W-long", 10, 0, 10, 30));

        var inputs = Inputs(extra: (ZoneP, ZoneUses.Shaft));
        Assert.False(MergedAtriums.Of(set, inputs).IsInterior(set.Members.Single()));
    }

    /// <summary>Two exempt 挑空 side by side each have their own 連通區劃: the line between them stays.</summary>
    [Fact]
    public void The_line_between_two_exempt_atriums_stays_a_boundary()
    {
        var set = Resolve(new[] { Zone(ZoneA, "A 區", "area-a", Rect(0, 0, 10, 10)), Zone(ZoneB, "B 區", "area-b", Rect(10, 0, 20, 10)) },
            Partition("W-shared", 10, 0, 10, 10));

        var inputs = Inputs(atriums: new[] { ZoneA, ZoneB });
        Assert.False(MergedAtriums.Of(set, inputs).IsInterior(set.Members.Single()));
    }

    /// <summary>
    /// A neighbour whose 用途 cannot be read — its Areas disagree — might be a 樓梯間, so the line stays
    /// a boundary. (One left blank is an ordinary 區劃, the convention for every 區劃 not special.)
    /// </summary>
    [Fact]
    public void A_neighbour_whose_use_cannot_be_read_keeps_the_line_a_boundary()
    {
        var set = Resolve(new[] { Zone(ZoneA, "A 區", "area-a", Rect(0, 0, 10, 10)), Zone(ZoneB, "B 區", "area-b", Rect(10, 0, 20, 10)) },
            Partition("W-shared", 10, 0, 10, 10));
        var inputs = Inputs();
        var unreadable = new CompartmentAreaInputs(inputs.Building, new Dictionary<Guid, IEnumerable<ReviewInput>>
        {
            [ZoneA] = inputs.ForZone(ZoneA),
            [ZoneB] = new[] { ReviewInput.Unreadable("zone.use", "此區劃的 2 個面積填寫不一致（辦公、樓梯間）") }
        });

        Assert.False(MergedAtriums.Of(set, unreadable).IsInterior(set.Members.Single()));
    }

    /// <summary>A 挑空 whose 第3項 does not hold is 單獨區劃分隔 under 第1項: its line stays.</summary>
    [Fact]
    public void Nothing_is_interior_around_an_atrium_that_is_not_exempt()
    {
        var set = Resolve(new[] { Zone(ZoneA, "A 區", "area-a", Rect(0, 0, 10, 10)), Zone(ZoneB, "B 區", "area-b", Rect(10, 0, 20, 10)) },
            Partition("W-shared", 10, 0, 10, 10));

        Assert.False(MergedAtriums.Of(set, Inputs(connectedArea: 4000)).IsInterior(set.Members.Single()));
    }

    /// <summary>
    /// Code review 第 2 項: the 挑空 closed with a line on the railing's face rather than its
    /// centreline. The relation is in doubt, yet between an exempt 挑空 and B 區 the railing is
    /// interior either way, so the 構件防火時效 result says so instead of asking a person.
    /// </summary>
    [Fact]
    public void A_line_on_the_face_of_the_railing_is_interior_too()
    {
        var set = Resolve(new[] { Zone(ZoneA, "A 區", "area-a", Rect(0, 0, 10.1, 10)), Zone(ZoneB, "B 區", "area-b", Rect(10.1, 0, 20, 10)) },
            Partition("W-railing", 10, 0, 10, 10, width: 0.3));
        var railing = set.Members.Single();
        Assert.Contains(railing.Relations, r => r.IsAmbiguous && r.Ambiguity == CandidateAmbiguityKind.BoundaryOffCenterline);

        var inputs = Inputs();
        Assert.True(MergedAtriums.Of(set, inputs).IsInterior(railing));

        var review = FireResistanceCheck.Review(set, new FireResistanceInputs(inputs, null), new RuleEngine(Shipped()), Today,
            Guid.Parse("99999999-0000-0000-0000-000000000003"));
        Assert.True(review.IsSuccess, review.IsSuccess ? string.Empty : review.Error.ToString());

        var findings = review.Value.For("W-railing").ToList();
        Assert.NotEmpty(findings);
        Assert.All(findings, f =>
        {
            Assert.Equal(ReviewStatus.NotApplicable, f.Status);
            Assert.Equal(ReviewValue.OfBoolean(true), f.Result.Evidence.Find(MergedAtriums.EvidenceField));
        });
    }

    // --- helpers ------------------------------------------------------------------------------------

    private static CandidateSet Resolve(IEnumerable<ZoneObservation> zones, params MemberObservation[] members) =>
        CandidateResolver.Resolve(Observations(zones, members, Array.Empty<OpeningObservation>()));

    /// <summary>A non-structural partition — a railing or glass wall — so 第70條 has nothing to say about it.</summary>
    private static MemberObservation Partition(string uid, double x0, double y0, double x1, double y1, double width = 0.1) =>
        new(Source(uid), CandidateCategory.Wall, new[] { P(x0, y0), P(x1, y1) }, widthFeet: M(width),
            typeUniqueId: "type-rail", typeName: "欄杆", isStructural: false, isCurtainWall: false);

    /// <summary>
    /// A 區 a 挑空 meeting 第二款 (連跨 2 層、合計 900 ㎡) unless <paramref name="connectedArea"/> says
    /// otherwise; the zones in <paramref name="atriums"/> all get the same facts.
    /// </summary>
    private static CompartmentAreaInputs Inputs(double connectedArea = 900, Guid[]? atriums = null, (Guid Zone, string Use)? extra = null)
    {
        var zones = new Dictionary<Guid, IEnumerable<ReviewInput>>();
        foreach (var atrium in atriums ?? new[] { ZoneA })
        {
            zones[atrium] = new[]
            {
                ReviewInput.Known("zone.use", ZoneUses.Atrium),
                ReviewInput.Known("zone.linksRefugeFloor", false),
                ReviewInput.Known("zone.spannedFloors", 2, ReviewUnit.None),
                ReviewInput.Known("zone.connectedArea", connectedArea, ReviewUnit.SquareMeter)
            };
        }

        if (extra is { } e) zones[e.Zone] = new[] { ReviewInput.Known("zone.use", e.Use) };
        return new CompartmentAreaInputs(new[] { ReviewInput.Known("building.fireResistiveConstruction", true) }, zones);
    }

    private static CompiledRuleSet Shipped()
    {
        var json = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow
        };
        var path = Path.Combine(AppContext.BaseDirectory, "Rules", "BuiltIn", "fire-review-rules.json");
        var loaded = RuleSetCompiler.Load(JsonSerializer.Deserialize<RuleSetDocument>(File.ReadAllText(path), json));
        Assert.True(loaded.IsSuccess, loaded.IsSuccess ? string.Empty : loaded.Error.ToString());
        return loaded.Value;
    }
}
