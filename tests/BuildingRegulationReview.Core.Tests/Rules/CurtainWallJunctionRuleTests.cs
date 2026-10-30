using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using BuildingRegulationReview.Application.Rules;
using BuildingRegulationReview.Domain.Reviews;
using BuildingRegulationReview.Domain.Rules;
using Xunit;

namespace BuildingRegulationReview.Core.Tests.Rules;

/// <summary>
/// The shipped CompartmentContinuity rules — 防火區劃與帷幕牆交接
/// (docs/regulations/curtain-wall-fire-compartment.md).
///
/// These rules encode 第79條第3項 the way the statute is written: 「應突出建築物外牆面五十公分以上」
/// is the requirement and 「但與其交接處之外牆面長度有九十公分以上……者，得免突出」is the exemption.
/// That shape matters, because the engine then produces the right six-state answer at every edge
/// without the check having to special-case anything:
///
/// <list type="bullet">
/// <item>突出達標 → Pass, and a missing spandrel measurement cannot spoil it.</item>
/// <item>未突出但但書成立 → NotApplicable（得免突出），which spec 11.7 counts towards 符合.</item>
/// <item>未突出、但書不成立 → Fail.</item>
/// <item>未突出、但書無法判定 → InsufficientData, never Fail (spec 11.3).</item>
/// </list>
/// </summary>
public sealed class CurtainWallJunctionRuleTests
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow
    };

    private static readonly RuleEvaluationContext Today = new(new DateTime(2026, 9, 25), "TW");

    private static CompiledRuleSet Shipped()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Rules", "BuiltIn", "fire-review-rules.json");
        var loaded = RuleSetCompiler.Load(JsonSerializer.Deserialize<RuleSetDocument>(File.ReadAllText(path), Json));
        Assert.True(loaded.IsSuccess, loaded.IsSuccess ? string.Empty : loaded.Error.ToString());
        return loaded.Value;
    }

    private static RuleFacts Junction(string kind) =>
        new RuleFacts(RuleFieldCatalog.Default)
            .Set("building.fireResistiveConstruction", true)
            .Set("junction.kind", kind)
            .Set("junction.zoneId", "zone-1")
            .Set("junction.curtainWallUniqueId", "cw-1")
            .Set("junction.hostUniqueId", "host-1")
            .Set("junction.hostLegalReference", "第79條")
            .Set("junction.hostRequiredFireRating", 60, ReviewUnit.Minute);

    private static ReviewStatus Status(RuleFacts facts) =>
        new RuleEngine(Shipped()).Evaluate(RuleCategory.CompartmentContinuity, facts, Today).Status;

    private static RuleOutcome Outcome(RuleFacts facts) =>
        new RuleEngine(Shipped()).Evaluate(RuleCategory.CompartmentContinuity, facts, Today);

    // --- the rule file itself -------------------------------------------------------------------

    [Fact]
    public void Shipped_rule_set_compiles_the_three_continuity_rules()
    {
        var set = Shipped();
        var rules = set.OfCategory(RuleCategory.CompartmentContinuity).ToList();

        // 第79條之4 自決議 16 起分兩條：實心讀設計防火時效，玻璃與帷幕牆門窗讀設計防火保護。
        Assert.Equal(
            new[]
            {
                "tw-bcr-79-3-curtain-wall-spandrel", "tw-bcr-79-4-curtain-wall-other",
                "tw-bcr-79-4-curtain-wall-other-glazed", "tw-bcr-79-curtain-wall-junction"
            },
            rules.Select(x => x.RuleId).OrderBy(x => x, StringComparer.Ordinal));

        // The junction rules are written against junction.*, which only this category may read.
        Assert.All(rules, rule => Assert.All(
            rule.AppliesWhen.Fields.Concat(rule.EvidenceFields).Concat(new[] { rule.Requirement.Actual }),
            field => Assert.True(field.IsAvailableIn(RuleCategory.CompartmentContinuity))));
    }

    [Fact]
    public void Junction_fields_are_hidden_from_the_other_categories()
    {
        var catalog = RuleFieldCatalog.Default;

        foreach (var name in new[] { "junction.kind", "junction.projectionDepth", "junction.continuousFireRatedLength" })
        {
            var field = catalog.Find(name);
            Assert.NotNull(field);
            Assert.Equal(new[] { RuleCategory.CompartmentContinuity }, field!.AvailableIn);
        }

        // The other way round: a continuity rule cannot reach for an element or an opening.
        Assert.False(catalog.Find("element.providedFireRating")!.IsAvailableIn(RuleCategory.CompartmentContinuity));
        Assert.False(catalog.Find("opening.providedFireProtection")!.IsAvailableIn(RuleCategory.CompartmentContinuity));

        // zone.* and building.* describe the compartment and stay open to every category.
        Assert.True(catalog.Find("zone.floorNumber")!.IsAvailableIn(RuleCategory.CompartmentContinuity));
        Assert.True(catalog.Find("building.fireResistiveConstruction")!.IsAvailableIn(RuleCategory.CompartmentContinuity));
    }

    // --- CW-H 區劃牆 × 帷幕牆（第79條第3、4項） ---------------------------------------------------

    [Fact]
    public void Wall_junction_passes_when_the_compartment_wall_projects_far_enough()
    {
        var facts = Junction("WallToCurtainWall").Set("junction.projectionDepth", 0.6, ReviewUnit.Meter);

        Assert.Equal(ReviewStatus.Pass, Status(facts));
    }

    [Fact]
    public void Wall_junction_needs_no_spandrel_measurement_once_it_projects()
    {
        // 突出達標時，交接帶的長度與嵌板時效都不影響本項判定（規格 §3.2 的反面）。
        var facts = Junction("WallToCurtainWall").Set("junction.projectionDepth", 0.5, ReviewUnit.Meter);

        Assert.Equal(ReviewStatus.Pass, Status(facts));
    }

    [Fact]
    public void Wall_junction_is_exempt_when_the_fire_rated_run_reaches_900mm()
    {
        var facts = Junction("WallToCurtainWall")
            .Set("junction.projectionDepth", 0, ReviewUnit.Meter)
            .Set("junction.continuousFireRatedLength", 0.9, ReviewUnit.Meter)
            .Set("junction.minFireRating", 60, ReviewUnit.Minute);

        var outcome = Outcome(facts);

        // 「得免突出」is an exemption, not a pass: the 50cm requirement stops applying.
        Assert.Equal(ReviewStatus.NotApplicable, outcome.Status);
        Assert.Equal(RuleOutcomeReason.Exempt, outcome.Reason);
    }

    [Fact]
    public void Wall_junction_totals_both_sides_of_the_intersection()
    {
        // 決議 1：900 mm 採總和，不要求兩側各 450 mm。左 900 + 右 0 仍然免突出。
        var facts = Junction("WallToCurtainWall")
            .Set("junction.projectionDepth", 0, ReviewUnit.Meter)
            .Set("junction.continuousFireRatedLength", 0.9, ReviewUnit.Meter);

        Assert.Equal(ReviewStatus.NotApplicable, Status(facts));
    }

    [Fact]
    public void Wall_junction_fails_just_below_either_threshold()
    {
        var shortRun = Junction("WallToCurtainWall")
            .Set("junction.projectionDepth", 0.499, ReviewUnit.Meter)
            .Set("junction.continuousFireRatedLength", 0.899, ReviewUnit.Meter);

        Assert.Equal(ReviewStatus.Fail, Status(shortRun));
    }

    [Fact]
    public void Wall_junction_without_any_fire_rated_run_fails()
    {
        var facts = Junction("WallToCurtainWall")
            .Set("junction.projectionDepth", 0, ReviewUnit.Meter)
            .Set("junction.continuousFireRatedLength", 0, ReviewUnit.Meter);

        Assert.Equal(ReviewStatus.Fail, Status(facts));
    }

    [Fact]
    public void Wall_junction_is_insufficient_when_the_run_cannot_be_measured()
    {
        // 交接帶內有嵌板缺設計防火時效 → 幾何層不供給長度 → 不得誤判為未符合（spec 11.3）。
        var facts = Junction("WallToCurtainWall").Set("junction.projectionDepth", 0, ReviewUnit.Meter);

        var outcome = Outcome(facts);

        Assert.Equal(ReviewStatus.InsufficientData, outcome.Status);
        Assert.Contains("junction.continuousFireRatedLength", outcome.Gaps.Select(x => x.Field));
    }

    // --- CW-V 區劃樓地板 × 帷幕牆（第79條之3） ----------------------------------------------------

    [Fact]
    public void Spandrel_is_exempt_at_900mm_and_fails_below_it()
    {
        var exempt = Junction("FloorToCurtainWall")
            .Set("junction.projectionDepth", 0, ReviewUnit.Meter)
            .Set("junction.continuousFireRatedHeight", 0.9, ReviewUnit.Meter);

        var shortBand = Junction("FloorToCurtainWall")
            .Set("junction.projectionDepth", 0, ReviewUnit.Meter)
            .Set("junction.continuousFireRatedHeight", 0.85, ReviewUnit.Meter);

        Assert.Equal(ReviewStatus.NotApplicable, Status(exempt));
        Assert.Equal(ReviewStatus.Fail, Status(shortBand));
    }

    [Fact]
    public void Spandrel_reads_the_length_of_its_own_rule_only()
    {
        // 層間規則看的是高度；只給長度不能讓它免突出。
        var facts = Junction("FloorToCurtainWall")
            .Set("junction.projectionDepth", 0, ReviewUnit.Meter)
            .Set("junction.continuousFireRatedLength", 5, ReviewUnit.Meter);

        Assert.Equal(ReviewStatus.InsufficientData, Status(facts));
    }

    // --- CW-O 其餘帷幕牆面（第79條之4） -----------------------------------------------------------

    [Fact]
    public void Other_panels_need_half_an_hour()
    {
        var enough = Solid().Set("junction.minFireRating", 30, ReviewUnit.Minute);
        var notEnough = Solid().Set("junction.minFireRating", 29, ReviewUnit.Minute);
        var unknown = Solid();

        Assert.Equal(ReviewStatus.Pass, Status(enough));
        Assert.Equal(ReviewStatus.Fail, Status(notEnough));
        Assert.Equal(ReviewStatus.InsufficientData, Status(unknown));
    }

    /// <summary>決議 16：玻璃嵌板答的是防火設備，不是分鐘數。</summary>
    [Fact]
    public void Glazed_other_panels_need_a_fire_rated_opening_assembly()
    {
        var protected_ = Glazed().Set("junction.minFireProtection", "是");
        var not = Glazed().Set("junction.minFireProtection", "否");
        var unknown = Glazed();

        Assert.Equal(ReviewStatus.Pass, Status(protected_));
        Assert.Equal(ReviewStatus.Fail, Status(not));
        Assert.Equal(ReviewStatus.InsufficientData, Status(unknown));
    }

    /// <summary>
    /// 決議 16：玻璃那一路不讀時效，實心那一路不讀防火保護。填錯欄位不會讓另一路通過——這正是分兩列
    /// 而不是取一個最不利值的理由。
    /// </summary>
    [Fact]
    public void The_two_other_panel_paths_do_not_answer_for_each_other()
    {
        Assert.Equal(ReviewStatus.InsufficientData, Status(Glazed().Set("junction.minFireRating", 120, ReviewUnit.Minute)));
        Assert.Equal(ReviewStatus.InsufficientData, Status(Solid().Set("junction.minFireProtection", "是")));
    }

    /// <summary>
    /// 決議 16：沒宣告 <c>防火檢討_嵌板種類</c> 時，兩條 CW-O 規則的適用條件都算不出來，引擎答資料不足
    /// 並指名缺的欄位——工具不猜是玻璃還是實心。
    /// </summary>
    [Fact]
    public void Other_panels_without_a_declared_kind_are_insufficient_data()
    {
        var outcome = Outcome(Junction("CurtainPanelOther")
            .Set("junction.minFireRating", 120, ReviewUnit.Minute)
            .Set("junction.minFireProtection", "是"));

        Assert.Equal(ReviewStatus.InsufficientData, outcome.Status);
        Assert.Contains(outcome.Gaps, g => g.Field == "junction.panelKind");
    }

    private static RuleFacts Solid() =>
        Junction("CurtainPanelOther").Set("junction.panelKind", "Solid");

    private static RuleFacts Glazed() =>
        Junction("CurtainPanelOther").Set("junction.panelKind", "Glazed");

    // --- 適用性 ------------------------------------------------------------------------------------

    [Fact]
    public void Nothing_applies_to_a_building_that_is_not_fire_resistive()
    {
        var facts = new RuleFacts(RuleFieldCatalog.Default)
            .Set("building.fireResistiveConstruction", false)
            .Set("junction.kind", "WallToCurtainWall")
            .Set("junction.projectionDepth", 0, ReviewUnit.Meter);

        Assert.Equal(ReviewStatus.NotApplicable, Status(facts));
    }

    [Fact]
    public void An_unknown_junction_kind_decides_nothing()
    {
        var facts = Junction("Something else").Set("junction.projectionDepth", 0, ReviewUnit.Meter);

        var outcome = Outcome(facts);

        Assert.Equal(ReviewStatus.NotApplicable, outcome.Status);
        Assert.Equal(RuleOutcomeReason.NoRuleApplies, outcome.Reason);
    }

    [Fact]
    public void The_three_kinds_never_apply_at_the_same_time()
    {
        // 前三條以 junction.kind 互斥、兩條 CW-O 再以 junction.panelKind 互斥；同一交接處不會有兩條
        // 同時適用而落入 Conflict。CW-H、CW-V 不設 panelKind，靠 kind 不符時 `false && unknown` 仍為
        // false，兩條 CW-O 不會因此變成「適用與否不明」。
        foreach (var kind in new[] { "WallToCurtainWall", "FloorToCurtainWall" })
        {
            var outcome = Outcome(Junction(kind)
                .Set("junction.projectionDepth", 0.6, ReviewUnit.Meter)
                .Set("junction.minFireRating", 60, ReviewUnit.Minute));

            Assert.NotEqual(RuleOutcomeReason.Conflict, outcome.Reason);
            Assert.Equal(ReviewStatus.Pass, outcome.Status);
        }

        foreach (var facts in new[]
                 {
                     Solid().Set("junction.minFireRating", 60, ReviewUnit.Minute),
                     Glazed().Set("junction.minFireProtection", "是")
                 })
        {
            var outcome = Outcome(facts);

            Assert.NotEqual(RuleOutcomeReason.Conflict, outcome.Reason);
            Assert.Equal(ReviewStatus.Pass, outcome.Status);
            Assert.Single(outcome.ConsideredRuleIds);
        }
    }
}
