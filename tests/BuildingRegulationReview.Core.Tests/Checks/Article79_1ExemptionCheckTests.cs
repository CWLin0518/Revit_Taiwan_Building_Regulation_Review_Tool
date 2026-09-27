using System;
using System.Collections.Generic;
using System.Linq;
using BuildingRegulationReview.Application.Candidates;
using BuildingRegulationReview.Application.Checks;
using BuildingRegulationReview.Application.Diagnostics;
using BuildingRegulationReview.Application.Parameters;
using BuildingRegulationReview.Application.Reviews;
using BuildingRegulationReview.Application.Rules;
using BuildingRegulationReview.Domain.Geometry;
using BuildingRegulationReview.Domain.Reviews;
using BuildingRegulationReview.Domain.Rules;
using Xunit;
using static BuildingRegulationReview.Core.Tests.Candidates.CandidateModel;

namespace BuildingRegulationReview.Core.Tests.Checks;

/// <summary>
/// The check layer of 第79條之1 (docs/regulations/article-79-1-area-exemption.md §3.2、§3.5、§7).
/// <see cref="Article79_1ExemptionTests"/> covers the judgement itself; what is asserted here is what
/// the judgement is turned into — which 區劃 gets a subject at all, what the result says, and above
/// all what the check must leave alone: the 區劃面積 row, the markup plan and the rule set.
/// </summary>
public sealed class Article79_1ExemptionCheckTests
{
    private static readonly RuleEvaluationContext Today = new(new DateTime(2026, 9, 27), "TW");
    private static readonly Guid RunId = Guid.Parse("79100000-0000-0000-0000-000000000001");

    /// <summary>第79條第1項's area limit, as the shipped rule words it.</summary>
    private static Rule AreaRule() =>
        new("tw-bcr-79-area", "2", RuleCategory.CompartmentArea, "建築技術規則建築設計施工編第79條第1項",
            new DateTime(2024, 1, 1), "TW", 10,
            new RuleExpression("building.fireResistiveConstruction == true"),
            new RuleExpression("zone.area <= (zone.sprinklered ? 3000 m2 : 1500 m2)"),
            ZoneUses.VerticalCompartments.Select(x => new RuleExpression(ZoneUses.ExemptionSource(x))).ToArray(),
            new[] { "zone.area", "zone.sprinklered", "zone.use", "building.fireResistiveConstruction" });

    private static RuleEngine Engine()
    {
        var compiled = RuleSetCompiler.Compile(new RuleSet("tw-bcr-fire", "2026.6", "防火區劃", new[] { AreaRule() }));
        Assert.True(compiled.IsSuccess, compiled.Error.TechnicalDetail);
        return new RuleEngine(compiled.Value);
    }

    /// <summary>A zone of one rectangular Area whose Revit value and boundary agree.</summary>
    private static ZoneObservation RectZone(Guid id, string name, string areaUid, double areaM2, double x0 = 0) =>
        new(id, name, new[]
        {
            new ZonePartObservation(areaUid, new[] { Rect(x0, 0, x0 + areaM2 / 30.0, 30) },
                PlanUnits.SquareMetersToSquareFeet(areaM2))
        });

    private static CandidateSet Set(params ZoneObservation[] zones) =>
        CandidateResolver.Resolve(new CandidateObservationSet(PackageId, "level-1F", "1F", zones,
            Array.Empty<MemberObservation>(), Array.Empty<OpeningObservation>()));

    private static CandidateSet OneZone(double areaM2 = 3000) => Set(RectZone(ZoneA, "A 區", "area-a", areaM2));

    /// <summary>
    /// The facts of one 區劃. <c>null</c> everywhere means 未填, which is what an unbound parameter and a
    /// 區劃 whose Areas disagree both arrive as (§3.5).
    /// </summary>
    private static CompartmentAreaInputs Inputs(
        string? use = ZoneUses.Auditorium,
        bool? cannotBeSubdivided = true,
        bool? fireResistive = true,
        string? buildingUse = "A-1",
        int? floorNumber = 9,
        bool? sprinklered = false,
        Guid? zone = null)
    {
        var building = new List<ReviewInput>();
        if (fireResistive is bool f) building.Add(ReviewInput.Known("building.fireResistiveConstruction", f, "專案資訊"));
        if (buildingUse is not null) building.Add(ReviewInput.Known("building.use", buildingUse, "專案資訊"));

        var zoneInputs = new List<ReviewInput>();
        if (use is not null) zoneInputs.Add(ReviewInput.Known("zone.use", use, ReviewInputSources.ZoneUse));
        if (cannotBeSubdivided is bool c)
            zoneInputs.Add(ReviewInput.Known("zone.cannotBeSubdivided", c, ReviewInputSources.CannotBeSubdivided));
        if (floorNumber is int n)
            zoneInputs.Add(ReviewInput.Known("zone.floorNumber", n, ReviewUnit.None, ReviewInputSources.FloorNumber));
        if (sprinklered is bool s) zoneInputs.Add(ReviewInput.Known("zone.sprinklered", s, ReviewInputSources.Sprinklered));

        return new CompartmentAreaInputs(building,
            new Dictionary<Guid, IEnumerable<ReviewInput>> { [zone ?? ZoneA] = zoneInputs });
    }

    private static Func<Guid> Ids(int prefix)
    {
        var n = 0;
        return () => Guid.Parse($"{prefix:D8}-0000-0000-0000-{++n:D12}");
    }

    private static Article79_1ExemptionReview Review(CandidateSet set, CompartmentAreaInputs inputs)
    {
        var review = Article79_1ExemptionCheck.Review(set, inputs, Engine().RuleSet, RunId, Ids(1));
        Assert.True(review.IsSuccess, review.IsSuccess ? string.Empty : review.Error.ToString());
        return review.Value;
    }

    private static CompartmentAreaReview Area(CandidateSet set, CompartmentAreaInputs inputs)
    {
        var review = CompartmentAreaCheck.Review(set, inputs, Engine(), Today, RunId, null, Ids(2));
        Assert.True(review.IsSuccess, review.IsSuccess ? string.Empty : review.Error.ToString());
        return review.Value;
    }

    private static Article79_1ExemptionFinding Single(Article79_1ExemptionReview review) => Assert.Single(review.Findings);

    // --- 誰有主體、誰沒有（§3.2、決議 10）--------------------------------------------------------

    /// <summary>
    /// 決議 10: the judgement answers 不適用 for a 用途 outside the article — that answer is for the
    /// panel — but the check produces no subject at all. Almost every 區劃 in a project is one of these,
    /// and a row of empty 不適用 would bury the 觀眾席 this check exists to surface. A near miss
    /// (觀眾廳、看台、停車場) is filtered out for the same reason it is not folded into the vocabulary
    /// (決議 7): it is not one of the six words.
    /// </summary>
    [Theory]
    [InlineData("辦公")]
    [InlineData("觀眾廳")]
    [InlineData("看台")]
    [InlineData("停車場")]
    [InlineData("")]
    [InlineData(null)]
    public void A_use_outside_the_list_gets_no_subject(string? use)
    {
        Assert.Empty(Review(OneZone(), Inputs(use: use)).Findings);

        // And the judgement itself does answer for it, so the panel still has something to show.
        Assert.True(Article79_1Exemption.For(true, "A-1", use, true).IsInapplicable);
    }

    /// <summary>
    /// §2.3: 第79條之1 lifts 「前條第一項」 and nothing else. From the eleventh storey up 第83條 decides the
    /// area — a 觀眾席 there is still bound by 一○○／二○○平方公尺 — so the article does not reach it and
    /// the check says nothing about it, however the declaration is filled in.
    /// </summary>
    [Theory]
    [InlineData(11)]
    [InlineData(12)]
    [InlineData(30)]
    public void Article_79_1_does_not_reach_the_eleventh_storey(int floorNumber)
    {
        Assert.Empty(Review(OneZone(), Inputs(floorNumber: floorNumber)).Findings);
        Assert.Empty(Review(OneZone(), Inputs(floorNumber: floorNumber, cannotBeSubdivided: false)).Findings);
        Assert.Empty(Review(OneZone(), Inputs(floorNumber: floorNumber, cannotBeSubdivided: null)).Findings);

        // The tenth storey is 第79條's, so there the article does reach the 區劃.
        Assert.Single(Review(OneZone(), Inputs(floorNumber: 10)).Findings);
    }

    /// <summary>
    /// A 樓層序 nobody filled in is not the 第83條 branch: which article limits the area is unknown, so
    /// whether an exemption from 第79條第1項 is worth anything is unknown too. It is asked for last and
    /// only while the exemption might hold — a 區劃 that claims nothing is 不適用 whichever article
    /// applies to it, which is §3.5's 「已確定不成立的要素讓其他缺口不必再問」 one level up.
    /// </summary>
    [Fact]
    public void A_missing_storey_leaves_the_exemption_undecided_unless_it_already_failed()
    {
        var undecided = Single(Review(OneZone(), Inputs(floorNumber: null)));
        Assert.Equal(ReviewStatus.InsufficientData, undecided.Status);
        Assert.Contains("缺樓層序", undecided.Result.Message, StringComparison.Ordinal);
        Assert.Contains("第83條", undecided.Result.Message, StringComparison.Ordinal);
        Assert.Equal(ReviewErrorCode.ParameterMissing, undecided.ErrorCode);

        // 無法區劃分隔＝否 settles it, so the storey is never asked for.
        var settled = Single(Review(OneZone(), Inputs(floorNumber: null, cannotBeSubdivided: false)));
        Assert.Equal(ReviewStatus.NotApplicable, settled.Status);
        Assert.DoesNotContain("樓層序", settled.Result.Message, StringComparison.Ordinal);
    }

    // --- 三態與結果內容（§3.5、§7.1）------------------------------------------------------------

    /// <summary>
    /// The holding case, whole: 人工覆核, the 款 named, and the two things （丁） leaves to a person
    /// quoted from <see cref="Article79_1Exemption.PersonMustConfirm"/> rather than retyped. The subject
    /// is the 區劃's Areas — the same batch the 區劃面積 row carries — so both rows expand onto the same
    /// place in the plan (§3.2、§7.1).
    /// </summary>
    [Fact]
    public void A_holding_exemption_is_manual_review_and_names_what_a_person_must_confirm()
    {
        var finding = Single(Review(OneZone(), Inputs()));
        var result = finding.Result;

        Assert.Equal(ReviewStatus.ManualReview, result.Status);
        Assert.Equal(Article79_1Clause.FirstClause, finding.Exemption!.Clause);
        Assert.Null(finding.ErrorCode);

        Assert.Equal(ReviewCheckTypes.AreaExemption, result.CheckType);
        Assert.Equal(ZoneA.ToString("D"), result.ZoneId);
        Assert.Equal(new[] { "area-a" }, result.SubjectUniqueIds);
        Assert.Equal(PackageId, result.PackageId);
        Assert.Equal(RunId, result.RunId);
        Assert.Equal("tw-bcr-fire", result.RuleId);
        Assert.Equal("2026.6", result.RuleVersion);
        Assert.Equal(Article79_1ExemptionCheck.LegalReference, result.LegalReference);
        Assert.DoesNotContain("第83條", result.LegalReference, StringComparison.Ordinal);

        Assert.StartsWith("區劃「A 區」：", result.Message);
        Assert.Contains(finding.Exemption.Description, result.Message, StringComparison.Ordinal);
        Assert.Contains(Article79_1Exemption.PersonMustConfirm, result.Message, StringComparison.Ordinal);
        Assert.Contains("人工覆寫", result.Message, StringComparison.Ordinal);

        // No comparison was made, so there is nothing to compare — the row carries no 實際值／要求值.
        Assert.Null(result.ActualValue);
        Assert.Null(result.RequiredValue);
    }

    [Fact]
    public void A_declaration_of_no_is_inapplicable_and_a_missing_one_is_insufficient_data()
    {
        var no = Single(Review(OneZone(), Inputs(cannotBeSubdivided: false)));
        Assert.Equal(ReviewStatus.NotApplicable, no.Status);
        Assert.Contains("第79條第1項照常適用", no.Result.Message, StringComparison.Ordinal);
        Assert.Null(no.ErrorCode);

        var missing = Single(Review(OneZone(), Inputs(cannotBeSubdivided: null)));
        Assert.Equal(ReviewStatus.InsufficientData, missing.Status);
        Assert.Contains("無法區劃分隔", missing.Result.Message, StringComparison.Ordinal);
        Assert.Equal(ReviewErrorCode.ParameterMissing, missing.ErrorCode);
    }

    /// <summary>
    /// §3.5: there is no 符合 and no 未符合 here, in any filling of the four facts. Failing 第79條之1 is
    /// not a violation — it only means 第79條第1項 applies as usual, which the area row already says.
    /// </summary>
    [Fact]
    public void The_exemption_never_passes_and_never_fails()
    {
        var uses = ZoneUses.Article79_1Uses;
        var groups = new string?[] { "A-1", "D-2", "C-2", "D-3", "B-2", "H-2", null };
        var flags = new bool?[] { true, false, null };

        foreach (var use in uses)
        foreach (var group in groups)
        foreach (var declared in flags)
        foreach (var construction in flags)
        {
            var review = Review(OneZone(), Inputs(use: use, cannotBeSubdivided: declared,
                fireResistive: construction, buildingUse: group));
            foreach (var finding in review.Findings)
            {
                Assert.NotEqual(ReviewStatus.Pass, finding.Status);
                Assert.NotEqual(ReviewStatus.Fail, finding.Status);
            }
        }
    }

    /// <summary>
    /// §3.5 first row, §4: a 區劃 whose extent is in doubt is not judged at all — the same line the
    /// 區劃面積 row draws. No fact was read, so the finding carries no judgement.
    /// </summary>
    [Fact]
    public void A_zone_whose_extent_is_in_doubt_is_manual_review_and_nothing_is_judged()
    {
        var set = Set(
            RectZone(ZoneA, "A 區", "area-a", 900),
            RectZone(ZoneB, "B 區", "area-b", 900, x0: 20));

        var finding = Review(set, Inputs()).For(ZoneA)!;

        Assert.Equal(ReviewStatus.ManualReview, finding.Status);
        Assert.Null(finding.Exemption);
        Assert.Equal(ReviewErrorCode.CandidateZoneUnusable, finding.ErrorCode);
        Assert.Contains("不檢討第79條之1之免除", finding.Result.Message, StringComparison.Ordinal);
        Assert.Equal(ReviewValue.OfText("ZonesOverlap"), finding.Result.Evidence.Find("zone.problems"));
    }

    /// <summary>§3.2: every fact the judgement read, and where it came from.</summary>
    [Fact]
    public void The_result_keeps_every_fact_it_read_and_where_it_came_from()
    {
        var evidence = Single(Review(OneZone(), Inputs())).Result.Evidence;

        Assert.Equal(ReviewValue.OfText(ZoneA.ToString("D")), evidence.Find("zone.id"));
        Assert.Equal(ReviewValue.OfText("A 區"), evidence.Find("zone.name"));
        Assert.Equal(ReviewValue.OfText(ZoneUses.Auditorium), evidence.Find("zone.use"));
        Assert.Equal(ReviewValue.OfText("A-1"), evidence.Find("building.use"));
        Assert.Equal(ReviewValue.OfBoolean(true), evidence.Find("building.fireResistiveConstruction"));
        Assert.Equal(ReviewValue.OfBoolean(true), evidence.Find("zone.cannotBeSubdivided"));
        Assert.Equal(9, Assert.IsType<ReviewValue>(evidence.Find("zone.floorNumber")).Number);
        Assert.Equal(ReviewValue.OfText(nameof(Article79_1Clause.FirstClause)), evidence.Find("article79_1.clause"));
        Assert.Equal(ReviewValue.OfText(nameof(Article79_1Gap.None)), evidence.Find("article79_1.gaps"));

        Assert.Equal(ReviewValue.OfText(ReviewInputSources.CannotBeSubdivided), evidence.Find("source[zone.cannotBeSubdivided]"));
        Assert.Equal(ReviewValue.OfText(ReviewInputSources.ZoneUse), evidence.Find("source[zone.use]"));

        // 滅火設備 belongs to the area row; naming it here would suggest 第79條之1 weighed it.
        Assert.Null(evidence.Find("zone.sprinklered"));
        Assert.Null(evidence.Find("source[zone.sprinklered]"));
    }

    // --- 既有判定不動（決議 12）------------------------------------------------------------------

    /// <summary>
    /// The most important test of the feature: in the same run, the 區劃面積 result for a 三○○○平方公尺
    /// 觀眾席 that satisfies every element 第79條之1 can see reads <em>word for word</em> as it does for
    /// an 辦公 區劃 of the same size. 第79條之1 adds a row; it changes nothing about the row it excepts
    /// (§3.7、決議 12). Releasing the area is 人工覆寫 and nothing else.
    /// </summary>
    [Fact]
    public void The_area_result_is_untouched_by_the_exemption()
    {
        var auditorium = Inputs();
        var office = Inputs(use: "辦公", cannotBeSubdivided: null);

        var withExemption = Assert.Single(Area(OneZone(), auditorium).Findings);
        var without = Assert.Single(Area(OneZone(), office).Findings);

        Assert.Equal(ReviewStatus.Fail, withExemption.Status);
        Assert.Equal(without.Status, withExemption.Status);
        Assert.Equal(without.Result.Message, withExemption.Result.Message);
        Assert.Equal(without.Result.RuleId, withExemption.Result.RuleId);
        Assert.Equal(without.Result.RuleVersion, withExemption.Result.RuleVersion);
        Assert.Equal(without.Result.LegalReference, withExemption.Result.LegalReference);
        Assert.Equal(without.Result.ActualValue, withExemption.Result.ActualValue);
        Assert.Equal(without.Result.RequiredValue, withExemption.Result.RequiredValue);
        Assert.DoesNotContain("第79條之1", withExemption.Result.Message, StringComparison.Ordinal);

        // The exemption is its own row on the same subject, beside the area one.
        var exemption = Single(Review(OneZone(), auditorium));
        Assert.Equal(ReviewStatus.ManualReview, exemption.Status);
        Assert.Equal(withExemption.Result.SubjectUniqueIds, exemption.Result.SubjectUniqueIds);
        Assert.NotEqual(withExemption.Result.CheckType, exemption.Result.CheckType);
    }

    /// <summary>
    /// §7.2: nothing is marked and no drawing number is issued. 第79條之1 has no 未符合 at all and
    /// <see cref="ReviewMarkupPlan"/> only paints 未符合, so this holds by construction — which is
    /// exactly why it needs a test: the 區劃 <em>is</em> painted red, by the area row, and painting it
    /// twice would say the same thing twice.
    /// </summary>
    [Fact]
    public void No_marking_is_planned_for_an_exemption()
    {
        var set = OneZone();
        var inputs = Inputs();
        var results = Area(set, inputs).Results.Concat(Review(set, inputs).Results).ToList();
        var run = new ReviewRun(RunId, PackageId, "tw-bcr-fire", "2026.6", 1, new DateTime(2026, 9, 27, 8, 0, 0, DateTimeKind.Utc))
            .Complete(results, new DateTime(2026, 9, 27, 8, 1, 0, DateTimeKind.Utc));

        var plan = ReviewMarkupPlan.Build(ReviewTable.Build(run), set.Zones);
        Assert.True(plan.IsSuccess, plan.IsSuccess ? string.Empty : plan.Error.ToString());

        var exemptionIds = results.Where(r => r.CheckType == ReviewCheckTypes.AreaExemption).Select(r => r.ResultId).ToList();
        Assert.Single(exemptionIds);

        // The area row is 未符合, so the 區劃 is painted once — and for that row's result, not this one.
        var region = Assert.Single(plan.Value.Regions);
        Assert.DoesNotContain(region.ResultId, exemptionIds);
        Assert.Empty(plan.Value.Notes);
        Assert.Empty(plan.Value.Bands);
        Assert.Empty(plan.Value.Skipped);
        Assert.All(exemptionIds, id => Assert.False(plan.Value.Numbers.ContainsKey(id)));
    }

    /// <summary>
    /// §5.1 last paragraph: the subject of this row is a 區劃, so the table names the 區劃 and shows no
    /// category and no Type. Without this the row would read as an empty 類別「」 — the 第79條之1 result
    /// has neither, because a 區劃 is no candidate category.
    /// </summary>
    [Fact]
    public void The_table_names_the_zone_not_a_category()
    {
        var set = OneZone();
        var inputs = Inputs();
        var run = new ReviewRun(RunId, PackageId, "tw-bcr-fire", "2026.6", 1, new DateTime(2026, 9, 27, 8, 0, 0, DateTimeKind.Utc))
            .Complete(Area(set, inputs).Results.Concat(Review(set, inputs).Results),
                new DateTime(2026, 9, 27, 8, 1, 0, DateTimeKind.Utc));

        var table = ReviewTable.Build(run);
        var section = table.Section(ReviewCheckTypes.AreaExemption);

        Assert.Equal("區劃面積免除（第79條之1）", ReviewTable.Title(ReviewCheckTypes.AreaExemption));
        Assert.Equal(ReviewStatus.ManualReview, section.Status);

        var entry = Assert.Single(section.Entries);
        Assert.Equal("A 區", entry.ZoneName);
        Assert.Equal("區劃", entry.CategoryLabel);
        Assert.Null(entry.TypeName);
        Assert.Null(entry.TypeKey);
        Assert.DoesNotContain(section.Groups, g => g.Grouping != ReviewTableGrouping.Zone);
        Assert.Equal("A 區", Assert.Single(section.Groups).Label);

        // The row sits immediately after 防火區劃面積, whose exception it is.
        var titles = table.Sections.Select(s => s.CheckType).ToList();
        Assert.Equal(titles.IndexOf(ReviewCheckTypes.CompartmentArea) + 1, titles.IndexOf(ReviewCheckTypes.AreaExemption));
    }
}
