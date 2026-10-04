using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using BuildingRegulationReview.Application.Checks;
using BuildingRegulationReview.Application.Parameters;
using BuildingRegulationReview.Application.Reviews;
using BuildingRegulationReview.Application.Rules;
using BuildingRegulationReview.Domain.Rules;
using Xunit;

namespace BuildingRegulationReview.Core.Tests.Checks;

/// <summary>
/// 第79條之2第3項's two 款 (docs/regulations/vertical-compartment.md §3.6).
///
/// 第3項 is a classification, not a requirement: it says whether an 挑空 has to be 單獨區劃分隔 under
/// 第1項 at all. So there is no 符合 and no 未符合 here — only 免除成立 (which the review reports as
/// 人工覆核), 兩款均不成立 (不適用) and 資料不足. What follows walks the §3.6 state table row by row,
/// then both sides of every threshold.
/// </summary>
public sealed class AtriumExemptionTests
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow
    };

    // --- §3.6 狀態表 ----------------------------------------------------------------------------

    /// <summary>第1項 only reaches a 防火構造建築物, so there is nothing for 第3項 to exempt.</summary>
    [Fact]
    public void A_building_that_is_not_fire_resistive_has_no_exemption_to_claim()
    {
        var exemption = AtriumExemption.For(false, linksRefugeFloor: true, InteriorFinishGrades.ClassOne, 2, 320);

        Assert.True(exemption.IsInapplicable);
        Assert.False(exemption.Holds);
        Assert.Equal(AtriumExemptionClause.None, exemption.Clause);
        Assert.Equal(AtriumExemptionGap.None, exemption.Gaps);
        Assert.Contains("非防火構造", exemption.Description, StringComparison.Ordinal);
    }

    /// <summary>Without 防火構造 it is not even known whether 第1項 applies, let alone 第3項.</summary>
    [Fact]
    public void An_unfilled_construction_leaves_the_exemption_undecided()
    {
        var exemption = AtriumExemption.For(null, linksRefugeFloor: true, InteriorFinishGrades.ClassOne, 2, 320);

        Assert.True(exemption.IsUndecided);
        Assert.Equal(AtriumExemptionGap.FireResistiveConstruction, exemption.Gaps);
        Assert.Contains("建築物防火構造", exemption.Description, StringComparison.Ordinal);
    }

    [Fact]
    public void Two_storeys_and_three_hundred_square_metres_hold_the_second_clause()
    {
        var exemption = AtriumExemption.For(true, linksRefugeFloor: null, interiorFinish: null, 2, 320.5);

        Assert.True(exemption.Holds);
        Assert.Equal(AtriumExemptionClause.SecondClause, exemption.Clause);
        Assert.Equal("符合第二款（連跨 2 層、連通區劃合計樓地板面積 320.5 ㎡）", exemption.Description);
    }

    [Fact]
    public void A_refuge_floor_link_with_class_one_finish_holds_the_first_clause()
    {
        var exemption = AtriumExemption.For(true, linksRefugeFloor: true, InteriorFinishGrades.ClassOne, 8, 4000);

        Assert.True(exemption.Holds);
        Assert.Equal(AtriumExemptionClause.FirstClause, exemption.Clause);
        Assert.Contains("符合第一款", exemption.Description, StringComparison.Ordinal);
        Assert.Contains(InteriorFinishGrades.ClassOne, exemption.Description, StringComparison.Ordinal);
    }

    /// <summary>
    /// A 款 that holds makes every other gap irrelevant — the same reading the engine gives an
    /// exemption that holds while a requirement's input is still blank (§3.3).
    /// </summary>
    [Fact]
    public void A_clause_that_holds_leaves_no_gap_even_when_the_other_clause_lacks_its_facts()
    {
        var second = AtriumExemption.For(true, linksRefugeFloor: null, interiorFinish: null, 3, 900);
        var first = AtriumExemption.For(true, linksRefugeFloor: true, InteriorFinishGrades.ClassOne, null, null);

        Assert.Equal(AtriumExemptionClause.SecondClause, second.Clause);
        Assert.Equal(AtriumExemptionGap.None, second.Gaps);
        Assert.Equal(AtriumExemptionClause.FirstClause, first.Clause);
        Assert.Equal(AtriumExemptionGap.None, first.Gaps);
    }

    [Fact]
    public void Five_storeys_and_no_refuge_floor_link_leave_the_atrium_under_article_1()
    {
        var exemption = AtriumExemption.For(true, linksRefugeFloor: false, InteriorFinishGrades.ClassOne, 5, 320);

        Assert.True(exemption.IsInapplicable);
        Assert.Equal("兩款均不成立（避難層通達＝否、連跨 5 層）", exemption.Description);
    }

    /// <summary>
    /// 裝修等級＝無 is a filled answer, not a gap: 第一款 is settled as not holding even though the
    /// 挑空 does reach a 避難層.
    /// </summary>
    [Fact]
    public void A_declared_absence_of_class_one_finish_settles_the_first_clause()
    {
        var exemption = AtriumExemption.For(true, linksRefugeFloor: true, InteriorFinishGrades.None, 5, 320);

        Assert.True(exemption.IsInapplicable);
        Assert.Equal(AtriumExemptionGap.None, exemption.Gaps);
        Assert.Equal($"兩款均不成立（室內裝修等級＝{InteriorFinishGrades.None}、連跨 5 層）", exemption.Description);
    }

    /// <summary>第一款 cannot be decided and 第二款 does not hold — the only shape that is 資料不足.</summary>
    [Fact]
    public void An_unfilled_refuge_floor_link_is_the_gap_when_the_second_clause_fails()
    {
        var exemption = AtriumExemption.For(true, linksRefugeFloor: null, InteriorFinishGrades.ClassOne, 5, 320);

        Assert.True(exemption.IsUndecided);
        Assert.Equal(AtriumExemptionGap.RefugeFloorLink, exemption.Gaps);
        Assert.Equal("缺避難層通達，無法判定兩款是否成立", exemption.Description);
    }

    [Fact]
    public void An_unfilled_spanned_floor_count_is_the_gap_when_the_first_clause_fails()
    {
        var exemption = AtriumExemption.For(true, linksRefugeFloor: false, InteriorFinishGrades.ClassOne, null, 320);

        Assert.True(exemption.IsUndecided);
        Assert.Equal(AtriumExemptionGap.SpannedFloors, exemption.Gaps);
        Assert.Equal("缺連跨樓層數，無法判定兩款是否成立", exemption.Description);
    }

    /// <summary>
    /// A finish grade that cannot be read is simply absent: <c>InteriorFinishAssessment.Derive</c>
    /// returns null when the 區劃's walls and ceilings do not answer, so there is no third value to
    /// tell apart from 未填.
    /// </summary>
    [Fact]
    public void An_unreadable_finish_grade_is_the_gap_when_the_second_clause_fails()
    {
        var exemption = AtriumExemption.For(true, linksRefugeFloor: true, interiorFinish: null, 5, 320);

        Assert.True(exemption.IsUndecided);
        Assert.Equal(AtriumExemptionGap.InteriorFinish, exemption.Gaps);
        Assert.Contains("模型牆面／天花板耐燃等級", exemption.Description, StringComparison.Ordinal);
    }

    /// <summary>
    /// A 款 already settled as not holding is never asked for the facts it still lacks: 二千平方公尺
    /// puts 第二款 out of reach whatever the 連跨樓層數 turns out to be.
    /// </summary>
    [Fact]
    public void An_area_over_the_limit_settles_the_second_clause_without_the_storey_count()
    {
        var exemption = AtriumExemption.For(true, linksRefugeFloor: false, InteriorFinishGrades.ClassOne, null, 2000);

        Assert.True(exemption.IsInapplicable);
        Assert.Equal(AtriumExemptionGap.None, exemption.Gaps);
        Assert.Equal("兩款均不成立（避難層通達＝否、連通區劃合計樓地板面積 2000 ㎡）", exemption.Description);
    }

    // --- 門檻兩側 -------------------------------------------------------------------------------

    /// <summary>「三層以下」 includes three.</summary>
    [Theory]
    [InlineData(1, true)]
    [InlineData(3, true)]
    [InlineData(4, false)]
    public void The_second_clause_counts_three_spanned_storeys_as_within_the_limit(int spannedFloors, bool holds)
    {
        var exemption = AtriumExemption.For(true, linksRefugeFloor: false, InteriorFinishGrades.ClassOne, spannedFloors, 900);

        Assert.Equal(holds, exemption.Holds);
        Assert.True(holds || exemption.IsInapplicable);
    }

    /// <summary>「一千五百平方公尺以下」 includes 一千五百.</summary>
    [Theory]
    [InlineData(1500.0, true)]
    [InlineData(1500.1, false)]
    public void The_second_clause_counts_fifteen_hundred_square_metres_as_within_the_limit(double area, bool holds)
    {
        var exemption = AtriumExemption.For(true, linksRefugeFloor: false, InteriorFinishGrades.ClassOne, 2, area);

        Assert.Equal(holds, exemption.Holds);
        Assert.True(holds || exemption.IsInapplicable);
    }

    /// <summary>
    /// 第一款 asks for 耐燃一級 and has none of 第83條第三款's 「包括底材」 wording, so a 區劃 finished
    /// to the stricter grade satisfies it as well. Anything else is not the 款's grade.
    /// </summary>
    [Theory]
    [InlineData(InteriorFinishGrades.ClassOne, true)]
    [InlineData(InteriorFinishGrades.ClassOneWithSubstrate, true)]
    [InlineData(InteriorFinishGrades.None, false)]
    [InlineData("耐燃二級", false)]
    public void The_first_clause_accepts_either_class_one_grade(string interiorFinish, bool holds)
    {
        var exemption = AtriumExemption.For(true, linksRefugeFloor: true, interiorFinish, 9, 4000);

        Assert.Equal(holds, exemption.Holds);
        Assert.Equal(holds ? AtriumExemptionClause.FirstClause : AtriumExemptionClause.None, exemption.Clause);
    }

    /// <summary>
    /// A 連通區劃面積 nobody stated is a gap like any other, not an area of zero — and the gap names
    /// the 合計, not the 挑空's own area (決議 31).
    /// </summary>
    [Fact]
    public void A_connected_area_nobody_stated_leaves_the_second_clause_undecided()
    {
        var exemption = AtriumExemption.For(true, linksRefugeFloor: false, InteriorFinishGrades.ClassOne, 2, null);

        Assert.True(exemption.IsUndecided);
        Assert.Equal(AtriumExemptionGap.ConnectedArea, exemption.Gaps);
        Assert.Equal("缺連通區劃面積，無法判定兩款是否成立", exemption.Description);
    }

    /// <summary>
    /// The 函 example table (防火區劃與挑空規則 §3): three storeys of 500 ㎡ is 1,500 ㎡ and holds; three
    /// of 600 ㎡ is 1,800 ㎡ and does not, although each storey alone is far below 1,500 ㎡ — the limit
    /// is on the 合計, never on a storey; four storeys fails on the count whatever the area.
    /// </summary>
    [Theory]
    [InlineData(3, 500 + 500 + 500, true)]
    [InlineData(3, 600 + 600 + 600, false)]
    [InlineData(4, 300 + 300 + 300 + 300, false)]
    public void The_second_clause_reads_the_total_over_the_connected_storeys(int spannedFloors, double total, bool holds)
    {
        var exemption = AtriumExemption.For(true, linksRefugeFloor: false, InteriorFinishGrades.ClassOne, spannedFloors, total);

        Assert.Equal(holds, exemption.Holds);
        Assert.True(holds || exemption.IsInapplicable);
    }

    /// <summary>
    /// A Revit Number parameter is never blank either, so 0 ㎡ is 未填 rather than 「一千五百平方公尺
    /// 以下」 satisfied unasked.
    /// </summary>
    [Theory]
    [InlineData(null, null)]
    [InlineData(0.0, null)]
    [InlineData(-5.0, null)]
    [InlineData(double.NaN, null)]
    [InlineData(0.5, 0.5)]
    [InlineData(1500.0, 1500.0)]
    public void A_connected_area_of_zero_or_less_is_nothing_stated(double? read, double? stated)
    {
        Assert.Equal(stated, AtriumExemption.StatedConnectedArea(read));
    }

    /// <summary>樓層序 has no 0: 1F is 1 and B1 is −1, so 0 names no storey.</summary>
    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(-2, true)]
    [InlineData(12, true)]
    public void A_floor_number_of_zero_names_no_storey(int read, bool stated)
    {
        Assert.Equal(stated, AtriumExemption.IsStatedFloorNumber(read));
    }

    /// <summary>
    /// Both checks read the five facts through <see cref="AtriumExemptionFacts"/>: an unreadable input
    /// is a gap, 0 floors and 0 ㎡ are 未填, and the exemption is the one <see cref="AtriumExemption.For"/>
    /// would give for what is left.
    /// </summary>
    [Fact]
    public void The_facts_reader_turns_inputs_into_the_exemption_both_checks_share()
    {
        var facts = AtriumExemptionFacts.Read(new[]
        {
            ReviewInput.Known("building.fireResistiveConstruction", true),
            ReviewInput.Known("zone.linksRefugeFloor", false),
            ReviewInput.Known("zone.spannedFloors", 3, Domain.Reviews.ReviewUnit.None),
            ReviewInput.Known("zone.connectedArea", 1200, Domain.Reviews.ReviewUnit.SquareMeter)
        });

        Assert.Equal(3, facts.SpannedFloors);
        Assert.Equal(1200, facts.ConnectedAreaSquareMeters);
        Assert.Equal(AtriumExemptionClause.SecondClause, facts.Exemption.Clause);
        Assert.Contains(facts.Evidence(), x => x.Field == "zone.connectedArea");

        var unstated = AtriumExemptionFacts.Read(new[]
        {
            ReviewInput.Known("building.fireResistiveConstruction", true),
            ReviewInput.Known("zone.linksRefugeFloor", false),
            ReviewInput.Known("zone.spannedFloors", 0, Domain.Reviews.ReviewUnit.None),
            ReviewInput.Unreadable("zone.connectedArea", "此區劃的 2 個面積填寫不一致（800、900）")
        });

        Assert.Null(unstated.SpannedFloors);
        Assert.Null(unstated.ConnectedAreaSquareMeters);
        Assert.Equal(AtriumExemptionGap.SpannedFloors | AtriumExemptionGap.ConnectedArea, unstated.Exemption.Gaps);
    }

    /// <summary>
    /// The three states are exclusive, whatever the facts: an answer is 免除成立, 不適用 or 資料不足,
    /// and never 符合 or 未符合. This is what keeps 第3項 out of the markup, which only paints Fail.
    /// </summary>
    [Fact]
    public void Every_combination_of_facts_lands_in_exactly_one_of_the_three_states()
    {
        var booleans = new bool?[] { null, true, false };
        var finishes = new[]
        {
            null, InteriorFinishGrades.None, InteriorFinishGrades.ClassOne,
            InteriorFinishGrades.ClassOneWithSubstrate, "耐燃二級"
        };
        var floors = new int?[] { null, 1, 3, 4, 9 };
        var areas = new double?[] { null, 320, 1500, 1500.1, 4000 };

        foreach (var fireResistive in booleans)
        foreach (var refuge in booleans)
        foreach (var finish in finishes)
        foreach (var spanned in floors)
        foreach (var area in areas)
        {
            var exemption = AtriumExemption.For(fireResistive, refuge, finish, spanned, area);
            var states = new[] { exemption.Holds, exemption.IsInapplicable, exemption.IsUndecided };

            Assert.Single(states, x => x);
            Assert.Equal(exemption.Holds, exemption.Clause != AtriumExemptionClause.None);
            Assert.False(exemption.Holds && exemption.Gaps != AtriumExemptionGap.None);
            Assert.False(string.IsNullOrWhiteSpace(exemption.Description));
        }
    }

    // --- 白名單與規則的關係 ----------------------------------------------------------------------

    /// <summary>
    /// The three facts are 區劃 facts like 所在樓層序, so they are open to every category — but no
    /// rule reads them, because 第3項 has no comparable requiredValue to be written as one (決議 24).
    /// The two atrium area rules read the derived <c>zone.atrium*</c> fields instead (決議 32). That is
    /// also why they never become a 前置檢查 blocker: NeededBy is rule-driven (決議 27).
    /// </summary>
    [Fact]
    public void The_third_paragraphs_two_facts_are_whitelisted_but_read_by_no_rule()
    {
        var catalog = RuleFieldCatalog.Default;
        var read = ReviewInputSources.FieldsUsedBy(Shipped()).Select(f => f.Name).ToList();
        var needed = ReviewInputSources.NeededBy(Shipped()).Select(s => s.Field).ToList();

        foreach (var name in new[] { "zone.spannedFloors", "zone.linksRefugeFloor", "zone.connectedArea", "zone.atriumBaseFloor" })
        {
            var field = catalog.Find(name);
            Assert.NotNull(field);
            Assert.True(field!.IsAvailableIn(RuleCategory.VerticalCompartment));
            Assert.True(field.IsAvailableIn(RuleCategory.CompartmentArea));
            Assert.DoesNotContain(name, read);
            Assert.DoesNotContain(name, needed);

            // Only 避難層通達 is typed; the other three are traced through the storeys and have no
            // parameter to come from at all (決議 35).
            var source = ReviewInputSources.For(name);
            if (name != "zone.linksRefugeFloor")
            {
                Assert.Null(source);
                continue;
            }

            Assert.NotNull(source);
            Assert.Equal(ReviewParameterLevel.Instance, source!.Level);
            Assert.Equal(new[] { ReviewParameterHost.Areas }, source.Hosts);
        }
    }

    /// <summary>
    /// What counts as a 連跨樓層數 at all: 「連跨 0 層」 taken at face value would satisfy 第二款's
    /// 「三層以下」 and exempt the 挑空 unasked, so anything below one storey is no answer.
    /// </summary>
    [Theory]
    [InlineData(0, false)]
    [InlineData(-1, false)]
    [InlineData(1, true)]
    [InlineData(3, true)]
    [InlineData(12, true)]
    public void A_span_below_one_storey_is_nothing_stated(int read, bool stated)
    {
        Assert.Equal(stated, AtriumExemption.IsStatedSpannedFloors(read));
    }

    private static CompiledRuleSet Shipped()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Rules", "BuiltIn", "fire-review-rules.json");
        var loaded = RuleSetCompiler.Load(JsonSerializer.Deserialize<RuleSetDocument>(File.ReadAllText(path), Json));
        Assert.True(loaded.IsSuccess, loaded.IsSuccess ? string.Empty : loaded.Error.ToString());
        return loaded.Value;
    }
}
