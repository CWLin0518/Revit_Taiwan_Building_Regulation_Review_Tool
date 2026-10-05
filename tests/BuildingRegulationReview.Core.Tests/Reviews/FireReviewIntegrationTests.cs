using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using BuildingRegulationReview.Application.Abstractions;
using BuildingRegulationReview.Application.Candidates;
using BuildingRegulationReview.Application.Checks;
using BuildingRegulationReview.Application.Diagnostics;
using BuildingRegulationReview.Application.Parameters;
using BuildingRegulationReview.Application.Reviews;
using BuildingRegulationReview.Application.Rules;
using BuildingRegulationReview.Domain.Common;
using BuildingRegulationReview.Domain.Geometry;
using BuildingRegulationReview.Domain.ReviewPackages;
using BuildingRegulationReview.Domain.Reviews;
using BuildingRegulationReview.Domain.Rules;
using Xunit;
using static BuildingRegulationReview.Core.Tests.Candidates.CandidateModel;

namespace BuildingRegulationReview.Core.Tests.Reviews;

/// <summary>
/// P3-T09: the spec 11.1 pre-review check, the parameters the review reads its inputs from, the
/// three checks run as one review with cancellation at safe points, overrides moving between runs,
/// a stored run judged again after the model or the rules moved, and the spec 15 performance
/// diagnosis — all on the add-in's own shipped rule file.
/// </summary>
public sealed class FireReviewIntegrationTests
{
    private const string RuleSetId = "tw-bcr-fire";
    private const string ShippedVersion = "2026.9-provisional";

    [Fact]
    public void Interior_finish_is_a_model_derived_wall_and_ceiling_type_fact_not_an_area_input()
    {
        var source = ReviewInputSources.For("zone.interiorFinish")!;

        Assert.Equal(ReviewParameterLevel.Type, source.Level);
        Assert.Equal("室內裝修耐燃等級", source.Label);
        Assert.Equal(new[] { ReviewParameterHost.Walls, ReviewParameterHost.Ceilings }, source.Hosts);
        Assert.DoesNotContain(ReviewParameterHost.Areas, source.Hosts);
    }
    private static readonly DateTime Now = new(2026, 9, 22, 9, 0, 0, DateTimeKind.Utc);
    private static readonly RuleEvaluationContext Today = new(new DateTime(2026, 9, 22), "TW");

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow
    };

    // --- fixtures -------------------------------------------------------------------------------

    private static Result<CompiledRuleSet> Shipped()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Rules", "BuiltIn", "fire-review-rules.json");
        return RuleSetCompiler.Load(JsonSerializer.Deserialize<RuleSetDocument>(File.ReadAllText(path), Json));
    }

    private static CompiledRuleSet Rules() => Shipped().Value;

    private static ReviewPackage Package(
        ReviewPackageStatus status = ReviewPackageStatus.Ready,
        string? ruleSetId = null,
        string? ruleSetVersion = null,
        string? areaPlan = "area-plan",
        int boundaryRevision = 1) =>
        new(PackageId, "floor-plan", "level-1F", "scheme-fire", areaPlan, boundaryRevision: boundaryRevision,
            ruleSetId: ruleSetId, ruleSetVersion: ruleSetVersion, status: status, updatedAtUtc: Now);

    private static CandidateSet Set(
        IEnumerable<MemberObservation>? members = null,
        IEnumerable<OpeningObservation>? openings = null) =>
        CandidateResolver.Resolve(Observations(Zones().Take(2), members ?? Members(), openings ?? Openings()));

    /// <summary>The fixed model plus a 帷幕嵌板 whose Type carries 設計防火時效 (docs §10 案例 20).</summary>
    private static IReadOnlyList<OpeningObservation> WithPanelType() => Openings()
        .Concat(new[]
        {
            new OpeningObservation(Source("P2-panel"), CandidateCategory.CurtainPanel, "CW-right", P(20, 6),
                M(1.5), M(3.0), "type-panel", "帷幕嵌板 1500")
        })
        .ToList();

    private static IEnumerable<KeyValuePair<string, ReviewParameterHost>> AllBindings() =>
        ReviewInputSources.All.SelectMany(s => s.Hosts.Select(h => new KeyValuePair<string, ReviewParameterHost>(s.ParameterName, h)));

    private sealed class Parameters
    {
        public List<KeyValuePair<string, ReviewParameterHost>> Bindings { get; set; } = AllBindings().ToList();
        public Dictionary<string, ParameterReading> Project { get; } = new()
        {
            [ReviewInputSources.FireResistiveConstruction] = ParameterReading.OfYesNo(1),
            // 第70條 decides a 主要構造's required rating from its storey counted from the top:
            // 1F of a 10-storey building is the 10th from the top, so 柱／樑 need 2hr, 樓地板 2hr
            // and 承重牆壁 1hr.
            [ReviewInputSources.FloorsAboveGround] = ParameterReading.OfInteger(10)
        };
        public Dictionary<string, Dictionary<string, ParameterReading>> Elements { get; } = new()
        {
            ["area-a"] = new()
            {
                [ReviewInputSources.Sprinklered] = ParameterReading.OfYesNo(0),
                [ReviewInputSources.ZoneUse] = ParameterReading.OfText("辦公"),
                [ReviewInputSources.FloorNumber] = ParameterReading.OfInteger(1)
            },
            ["area-b"] = new()
            {
                [ReviewInputSources.Sprinklered] = ParameterReading.OfYesNo(1),
                [ReviewInputSources.FloorNumber] = ParameterReading.OfInteger(1)
            },
            ["type-rc200"] = new() { [FireRatingParameters.Provided] = ParameterReading.OfText("2 小時") },
            ["D1-shared"] = new()
            {
                [FireProtectionParameters.Provided] = ParameterReading.OfYesNo(1),
                // 第79條第1項之阻熱性 (垂直區劃規格決議 38): a 防火設備 on the boundary owes it too.
                [InsulationParameters.Provided] = ParameterReading.OfYesNo(1)
            },
            ["WN1-bottom"] = new() { [FireProtectionParameters.Provided] = ParameterReading.OfText("否") }
        };

        public ReviewParameterSnapshot Snapshot() => new(Bindings, Project,
            Elements.ToDictionary(x => x.Key, x => (IReadOnlyDictionary<string, ParameterReading>)x.Value));
    }

    private static ReviewEnvironment Environment(string phase = "新建") => ReviewEnvironment.Of(
        (ReviewEnvironment.SourceViewUniqueId, "floor-plan"),
        (ReviewEnvironment.LevelUniqueId, "level-1F"),
        (ReviewEnvironment.AreaSchemeUniqueId, "scheme-fire"),
        (ReviewEnvironment.Phase, phase),
        (ReviewEnvironment.DesignOption, "主要"),
        (ReviewEnvironment.GeometryTolerance, "50mm"),
        (ReviewEnvironment.ProjectUnits, "m/m2"));

    private static Func<Guid> Ids(int prefix)
    {
        var n = 0;
        return () => Guid.Parse($"{prefix:D8}-0000-0000-0000-{++n:D12}");
    }

    private static FireReviewRequest Request(
        ReviewPackage? package = null,
        Parameters? parameters = null,
        ReviewRun? previous = null,
        CompiledRuleSet? rules = null,
        IEnumerable<MemberObservation>? members = null,
        IEnumerable<OpeningObservation>? openings = null,
        ICurtainWallGeometryReader? curtainWalls = null,
        ReviewModelFacts? model = null)
    {
        var set = Set(members, openings);
        var inputs = ReviewInputAssembler.Assemble(set, (parameters ?? new Parameters()).Snapshot(), model: model);
        return new FireReviewRequest(package ?? Package(), rules ?? Rules(), Today, set, inputs, Environment(), previous,
            TimeSpan.FromSeconds(1), curtainWallReader: curtainWalls);
    }

    private static FireReviewOutcome Run(FireReviewRequest request, int prefix = 1, CancellationToken token = default,
        IProgress<FireReviewProgress>? progress = null) =>
        FireReviewRunner.Run(request, token, progress, () => Now, Ids(prefix));

    private static ReviewReadinessReport Readiness(
        ReviewPackage? package = null,
        IEnumerable<string>? staleReasons = null,
        Result<CompiledRuleSet>? rules = null,
        ReviewModelConditions? conditions = null,
        Parameters? parameters = null,
        Result<CandidateSet>? candidates = null,
        bool acceptUpdate = false) =>
        ReviewReadiness.Evaluate(new ReviewReadinessInput(
            package ?? Package(), staleReasons, rules ?? Shipped(), conditions,
            (parameters ?? new Parameters()).Snapshot(), candidates ?? Result.Success(Set()), acceptUpdate));

    private static ReviewResult ResultOf(ReviewRun run, string checkType, string subject, Guid? zone = null) =>
        run.Results.Single(r => r.CheckType == checkType && r.SubjectUniqueIds.Contains(subject) &&
                                (zone is null || r.ZoneId == zone.Value.ToString("D")));

    private sealed class Recorder : IProgress<FireReviewProgress>
    {
        private readonly Action<FireReviewProgress>? _onReport;
        public Recorder(Action<FireReviewProgress>? onReport = null) => _onReport = onReport;
        public List<FireReviewProgress> Reports { get; } = new();
        public void Report(FireReviewProgress value)
        {
            Reports.Add(value);
            _onReport?.Invoke(value);
        }
    }

    // --- the shipped rule file ------------------------------------------------------------------

    [Fact]
    public void Shipped_rule_file_compiles_and_covers_all_three_checks()
    {
        var loaded = Shipped();
        Assert.True(loaded.IsSuccess, loaded.IsFailure ? loaded.Error.TechnicalDetail : null);
        Assert.Equal(RuleSetId, loaded.Value.RuleSet.RuleSetId);
        Assert.Equal(ShippedVersion, loaded.Value.RuleSet.Version);
        Assert.NotEmpty(loaded.Value.OfCategory(RuleCategory.CompartmentArea));
        Assert.NotEmpty(loaded.Value.OfCategory(RuleCategory.FireResistance));
        Assert.NotEmpty(loaded.Value.OfCategory(RuleCategory.OpeningProtection));
        Assert.Contains("暫定", loaded.Value.RuleSet.Title);
    }

    [Fact]
    public void Rule_set_needs_the_parameters_its_conditions_and_requirements_read()
    {
        var fields = ReviewInputSources.FieldsUsedBy(Rules()).Select(f => f.Name).ToList();
        Assert.Contains("building.fireResistiveConstruction", fields);
        Assert.Contains("zone.sprinklered", fields);
        Assert.Contains("zone.use", fields);
        Assert.Contains("element.providedFireRating", fields);
        Assert.Contains("opening.providedFireProtection", fields);
        Assert.Contains("opening.providedInsulation", fields); // 第79條第1項之阻熱性（決議 38）
        // 第70條 decides a column's required rating from where its storey sits, counted from the top.
        Assert.Contains("building.floorsAboveGround", fields);
        Assert.Contains("zone.floorNumber", fields);
        // 第83條 reads the 用途類組 for its H-2 但書 and the 裝修等級 for its 100／200／500 ㎡ tiers.
        Assert.Contains("building.use", fields);
        Assert.Contains("zone.interiorFinish", fields);
        Assert.DoesNotContain("element.typeName", fields); // evidence only

        // 第79條之2第1項: the 遮煙性能 of a 昇降機道's 防火設備 and the rating of a 管道間之維修門.
        Assert.Contains("shaft.requirement", fields);
        Assert.Contains("shaft.providedSmokeProtection", fields);
        Assert.Contains("shaft.providedFireRating", fields);
        Assert.Contains("shaft.elevatorLobbyProtected", fields); // an exemption is read too
        Assert.DoesNotContain("shaft.elementUniqueId", fields);  // evidence only

        var needed = ReviewInputSources.NeededBy(Rules()).Select(s => s.ParameterName).ToList();
        Assert.Equal(new[]
        {
            ReviewInputSources.FireResistiveConstruction, ReviewInputSources.FloorsAboveGround,
            ReviewInputSources.BuildingUse, FireRatingParameters.Provided,
            // 決議 16：junction.panelKind 是 第79條之4 兩路作答的前提，沒綁定就判不出適用哪一條。
            CurtainPanelKindParameters.Provided,
            FireProtectionParameters.Provided,
            // 第79條第1項之阻熱性（垂直區劃規格決議 38）。
            InsulationParameters.Provided,
            // 設計防火時效 twice: element.providedFireRating for a 主要構造, shaft.providedFireRating for
            // a 管道間之維修門. Two fields with different categories, so the pre-review check needs both.
            FireRatingParameters.Provided, SmokeProtectionParameters.Provided,
            ReviewInputSources.FloorNumber, ReviewInputSources.InteriorFinish, ReviewInputSources.Sprinklered,
            ReviewInputSources.ZoneUse
        }, needed);
        Assert.DoesNotContain(needed, n => n == FireRatingParameters.Required);

        // shaft.requirement and shaft.elevatorLobbyProtected are not typed into a parameter: the first
        // is what the check itself puts in, the second is a spatial relation (垂直區劃文件 §9 第 2 項).
        Assert.Null(ReviewInputSources.For("shaft.requirement"));
        Assert.Null(ReviewInputSources.For("shaft.elevatorLobbyProtected"));
    }

    /// <summary>
    /// 建築技術規則建築設計施工編第70條, for 柱: 自頂層起算不超過第四層 → 一小時、超過第四層至第十四層
    /// → 二小時、第十五層以上 → 三小時。本表所指之層數包括地下層數，所以地下層繼續往下數。
    /// </summary>
    [Theory]
    // 20 層樓：頂層 20F 起算第 1 層，1F 起算第 20 層。
    [InlineData(20, 20, 60)]   // 自頂層第 1 層
    [InlineData(20, 17, 60)]   // 自頂層第 4 層，邊界
    [InlineData(20, 16, 120)]  // 自頂層第 5 層，邊界
    [InlineData(20, 7, 120)]   // 自頂層第 14 層，邊界
    [InlineData(20, 6, 180)]   // 自頂層第 15 層，邊界
    [InlineData(20, 1, 180)]   // 自頂層第 20 層
    // 地下層繼續往下數：4 層樓的 B1 是自頂層起算第 5 層。
    [InlineData(4, 4, 60)]
    [InlineData(4, 1, 60)]     // 自頂層第 4 層
    [InlineData(4, -1, 120)]   // 自頂層第 5 層
    [InlineData(4, -10, 120)]  // 自頂層第 14 層，仍在「超過第四層至第十四層」內
    [InlineData(4, -11, 180)]  // 自頂層第 15 層
    public void A_columns_required_rating_follows_article_70_by_storey_from_the_top(
        int floorsAboveGround, int floorNumber, double expectedMinutes)
    {
        var outcome = ColumnOutcome(floorsAboveGround, floorNumber, providedMinutes: expectedMinutes);

        Assert.Equal(ReviewStatus.Pass, outcome.Status);
        Assert.Equal(ReviewValue.Quantity(expectedMinutes, ReviewUnit.Minute), outcome.RequiredValue);
        Assert.Contains("第70條", outcome.LegalReference);
    }

    [Fact]
    public void A_column_one_minute_short_of_what_article_70_requires_fails()
    {
        // 自頂層起算第 5 層需要二小時。
        var outcome = ColumnOutcome(20, 16, providedMinutes: 119);

        Assert.Equal(ReviewStatus.Fail, outcome.Status);
        Assert.Equal(ReviewValue.Quantity(120, ReviewUnit.Minute), outcome.RequiredValue);
    }

    /// <summary>第70條 is about 主要構造, so a column that is not structural is not its subject.</summary>
    [Fact]
    public void A_column_that_is_not_structural_is_not_the_subject_of_article_70()
    {
        var outcome = ColumnOutcome(20, 16, providedMinutes: 60, structural: false);

        Assert.Equal(ReviewStatus.NotApplicable, outcome.Status);
    }

    /// <summary>Without the storey the clause counts from, the answer is 資料不足 — never a fail.</summary>
    [Fact]
    public void A_column_whose_storey_is_unknown_is_insufficient_data_rather_than_a_fail()
    {
        var facts = ColumnFacts(providedMinutes: 60, structural: true);
        facts.Set("building.fireResistiveConstruction", true);
        // zone.floorNumber and building.floorsAboveGround deliberately left out.

        var outcome = new RuleEngine(Rules()).Evaluate(RuleCategory.FireResistance, facts, Today);

        Assert.Equal(ReviewStatus.InsufficientData, outcome.Status);
    }

    /// <summary>
    /// 第70條 sits above 第79條 on purpose. Where both reach the same element — a 承重 區劃牆壁, a
    /// 區劃 樓地板 — 第70條's requirement is never the lower of the two, so letting it decide alone
    /// cannot lose the 第79條 requirement. This pins the numbers that claim rests on.
    /// </summary>
    [Theory]
    // 承重牆壁：第70條 自頂層≤14層 → 1hr、≥15層 → 2hr；第79條 區劃牆壁 → 1hr。
    [InlineData("Walls", 20, 20, 60)]
    [InlineData("Walls", 20, 6, 120)]
    // 樓地板：第70條 自頂層≤4層 → 1hr、≥5層 → 2hr；第79條 區劃樓地板 → 1hr。
    [InlineData("Floors", 20, 20, 60)]
    [InlineData("Floors", 20, 16, 120)]
    public void Article_70_governs_a_member_that_article_79_also_reaches_and_never_asks_for_less(
        string category, int floorsAboveGround, int floorNumber, double expectedMinutes)
    {
        var facts = new RuleFacts(Rules().Catalog);
        facts.Set("element.category", category);
        facts.Set("element.isStructural", true);
        facts.Set("element.isCompartmentBoundary", true);
        facts.Set("element.providedFireRating", expectedMinutes, ReviewUnit.Minute);
        facts.Set("building.fireResistiveConstruction", true);
        facts.Set("building.floorsAboveGround", floorsAboveGround, ReviewUnit.None);
        facts.Set("zone.floorNumber", floorNumber, ReviewUnit.None);

        var outcome = new RuleEngine(Rules()).Evaluate(RuleCategory.FireResistance, facts, Today);

        Assert.Equal(ReviewStatus.Pass, outcome.Status);
        Assert.Equal(ReviewValue.Quantity(expectedMinutes, ReviewUnit.Minute), outcome.RequiredValue);
        Assert.Contains("第70條", outcome.LegalReference);
        // 第79條 asks for 60 min, so 第70條 deciding alone never lowers the bar.
        Assert.True(expectedMinutes >= 60);
    }

    /// <summary>
    /// A 區劃牆壁 that carries no load is not 主要構造, so 第70條 does not reach it and 第79條 —
    /// which does — decides at the lower priority instead of the element falling through unchecked.
    /// </summary>
    [Fact]
    public void A_non_bearing_compartment_wall_falls_through_to_article_79()
    {
        var facts = new RuleFacts(Rules().Catalog);
        facts.Set("element.category", "Walls");
        facts.Set("element.isStructural", false);
        facts.Set("element.isCompartmentBoundary", true);
        // 第79條's 區劃牆壁 requirement asks this since version 2 — an ordinary RC wall, not a 帷幕牆
        // (see CurtainWallBoundaryRatingTests). CandidateFacts sets it for every member, so the real
        // pipeline always answers it; a hand-built fact set has to say so too or the rule is undecided.
        facts.Set("element.isCurtainWall", false);
        facts.Set("element.providedFireRating", 60, ReviewUnit.Minute);
        facts.Set("building.fireResistiveConstruction", true);

        var outcome = new RuleEngine(Rules()).Evaluate(RuleCategory.FireResistance, facts, Today);

        Assert.Equal(ReviewStatus.Pass, outcome.Status);
        Assert.Contains("第79條", outcome.LegalReference);
        Assert.Equal(ReviewValue.Quantity(60, ReviewUnit.Minute), outcome.RequiredValue);
    }

    private static RuleFacts ColumnFacts(double providedMinutes, bool structural)
    {
        var facts = new RuleFacts(Rules().Catalog);
        facts.Set("element.category", "Columns");
        facts.Set("element.isStructural", structural);
        facts.Set("element.providedFireRating", providedMinutes, ReviewUnit.Minute);
        return facts;
    }

    private static RuleOutcome ColumnOutcome(
        int floorsAboveGround, int floorNumber, double providedMinutes, bool structural = true)
    {
        var facts = ColumnFacts(providedMinutes, structural);
        facts.Set("building.fireResistiveConstruction", true);
        facts.Set("building.floorsAboveGround", floorsAboveGround, ReviewUnit.None);
        facts.Set("zone.floorNumber", floorNumber, ReviewUnit.None);

        return new RuleEngine(Rules()).Evaluate(RuleCategory.FireResistance, facts, Today);
    }

    // --- spec 11.1 pre-review check -------------------------------------------------------------

    [Fact]
    public void Ready_package_with_parameters_and_rules_can_run_and_locks_the_rule_set_the_first_time()
    {
        var report = Readiness();

        Assert.True(report.CanRun, string.Join("\n", report.Items));
        Assert.True(report.RuleSetChanges);
        Assert.Empty(report.Blocking);
        Assert.Contains(report.Items, i => i.Condition == ReadinessCondition.RuleSet && i.Message.Contains("首次檢討") && i.Message.Contains(ShippedVersion));
        Assert.Contains(report.Items, i => i.Code == ReviewErrorCode.CandidateAmbiguous && i.Severity == ReadinessSeverity.Info);
        Assert.StartsWith("前置檢查通過", report.Message);
    }

    [Theory]
    [InlineData(ReviewPackageStatus.Setup)]
    [InlineData(ReviewPackageStatus.BoundaryDraft)]
    [InlineData(ReviewPackageStatus.Error)]
    public void Package_that_is_not_ready_blocks_with_a_fix(ReviewPackageStatus status)
    {
        var report = Readiness(Package(status));

        Assert.False(report.CanRun);
        var item = Assert.Single(report.Blocking);
        Assert.Equal(ReviewErrorCode.ReviewNotReady, item.Code);
        Assert.Equal(ReadinessCondition.Package, item.Condition);
        Assert.False(string.IsNullOrWhiteSpace(item.Fix));
    }

    [Fact]
    public void Package_without_area_plan_blocks()
    {
        var report = Readiness(Package(areaPlan: null));
        Assert.Contains(report.Blocking, i => i.Code == ReviewErrorCode.ReviewNotReady && i.Fix!.Contains("防火區劃設定"));
    }

    [Fact]
    public void Stale_boundaries_block_every_status_and_each_reason_is_listed()
    {
        var report = Readiness(Package(ReviewPackageStatus.Reviewed),
            staleReasons: new[] { "有 2 個面積邊界或面積已被修改，與這個套件寫入時不同。", "這個套件的單線圖視圖已不在模型中。" });

        Assert.False(report.CanRun);
        Assert.Equal(2, report.Blocking.Count(i => i.Code == ReviewErrorCode.StatusStale));
        Assert.All(report.Blocking, i => Assert.Contains("防火區劃編輯器", i.Fix));
    }

    [Fact]
    public void Stale_results_on_fresh_boundaries_can_be_reviewed_again()
    {
        var report = Readiness(Package(ReviewPackageStatus.Stale, RuleSetId, ShippedVersion));

        Assert.True(report.CanRun);
        Assert.False(report.RuleSetChanges);
        Assert.Contains(report.Items, i => i.Code == ReviewErrorCode.StatusStale && i.Severity == ReadinessSeverity.Info);
    }

    [Fact]
    public void Rule_set_that_cannot_load_blocks()
    {
        var failed = Domain.Common.Result.Failure<CompiledRuleSet>(new Error(ReviewErrorCode.RuleSchemaInvalid, "規則檔格式錯誤", "rules[0].category"));
        var report = Readiness(rules: failed);

        Assert.False(report.CanRun);
        Assert.Null(report.RuleSet);
        var item = Assert.Single(report.Blocking);
        Assert.Equal(ReviewErrorCode.RuleSchemaInvalid, item.Code);
        Assert.Equal("rules[0].category", item.Detail);
    }

    [Fact]
    public void Locked_version_that_differs_blocks_until_the_user_accepts_the_update()
    {
        var locked = Package(ReviewPackageStatus.Reviewed, RuleSetId, "2025.9");

        var refused = Readiness(locked);
        Assert.False(refused.CanRun);
        Assert.True(refused.NeedsRuleSetConfirmation);
        Assert.Contains(refused.Blocking, i => i.Code == ReviewErrorCode.RuleVersionMismatch && i.Message.Contains("2025.9") && i.Message.Contains(ShippedVersion));

        var accepted = Readiness(locked, acceptUpdate: true);
        Assert.True(accepted.CanRun);
        Assert.True(accepted.RuleSetChanges);
        Assert.Contains(accepted.Warnings, i => i.Code == ReviewErrorCode.RuleVersionMismatch);
    }

    [Fact]
    public void Same_locked_version_needs_no_confirmation()
    {
        var report = Readiness(Package(ReviewPackageStatus.Reviewed, RuleSetId, ShippedVersion));
        Assert.True(report.CanRun);
        Assert.False(report.RuleSetChanges);
        Assert.DoesNotContain(report.Items, i => i.Code == ReviewErrorCode.RuleVersionMismatch);
    }

    [Fact]
    public void Parameter_the_rules_need_but_the_project_lacks_blocks_with_how_to_add_it()
    {
        var parameters = new Parameters();
        parameters.Bindings.RemoveAll(b => b.Key == FireRatingParameters.Provided);

        var report = Readiness(parameters: parameters);

        Assert.False(report.CanRun);
        // Two fields read 設計防火時效 — the 主要構造's own rating and a 管道間維修門's — and they need
        // different categories, so each is reported with the categories it needs (垂直區劃文件 §6).
        var item = Assert.Single(report.Blocking, i => i.Message.Contains("element.providedFireRating"));
        Assert.Equal(ReviewErrorCode.ParameterMissing, item.Code);
        Assert.Contains(FireRatingParameters.Provided, item.Message);
        Assert.Contains("專案沒有參數", item.Message);
        Assert.Contains("類型參數", item.Fix);
        Assert.Contains("牆", item.Fix);

        var door = Assert.Single(report.Blocking, i => i.Message.Contains("shaft.providedFireRating"));
        Assert.Contains("管道間維修門", door.Message);
        Assert.Contains("門", door.Fix);
    }

    /// <summary>
    /// 垂直區劃文件 §6: 設計防火時效 answers two fields, so a project that has the parameter on 牆柱樓板
    /// but not on 門 is told the binding is missing — not that the parameter does not exist.
    /// </summary>
    [Fact]
    public void A_parameter_the_project_has_but_not_on_the_categories_a_field_needs_says_so()
    {
        var parameters = new Parameters();
        parameters.Bindings.RemoveAll(b =>
            b.Key == FireRatingParameters.Provided && b.Value == ReviewParameterHost.Doors);

        var item = Assert.Single(Readiness(parameters: parameters).Blocking);

        Assert.Equal(ReviewErrorCode.ParameterMissing, item.Code);
        Assert.Contains("沒有綁定到 門", item.Message);
        Assert.DoesNotContain("專案沒有參數", item.Message);
        Assert.Contains("類別加上 門", item.Fix);
    }

    /// <summary>遮煙性能 is a required parameter of its own: without it 第79條之2 can never be answered.</summary>
    [Fact]
    public void The_smoke_seal_parameter_blocks_the_review_when_the_project_has_none()
    {
        var parameters = new Parameters();
        parameters.Bindings.RemoveAll(b => b.Key == SmokeProtectionParameters.Provided);

        var item = Assert.Single(Readiness(parameters: parameters).Blocking);

        Assert.Equal(ReviewErrorCode.ParameterMissing, item.Code);
        Assert.Contains(SmokeProtectionParameters.Provided, item.Message);
        Assert.Contains("類型參數", item.Fix);
        foreach (var host in new[] { "門", "窗", "帷幕嵌板" }) Assert.Contains(host, item.Fix);
    }

    [Fact]
    public void Parameter_bound_to_some_candidate_categories_only_warns_about_the_rest()
    {
        var parameters = new Parameters();
        // 門 keep the binding: without it the 管道間維修門 field has no category at all, which is a
        // blocker rather than the partial-binding warning this test is about.
        parameters.Bindings.RemoveAll(b => b.Key == FireRatingParameters.Provided &&
                                          b.Value != ReviewParameterHost.Walls && b.Value != ReviewParameterHost.Doors);

        var report = Readiness(parameters: parameters);

        Assert.True(report.CanRun);
        var warning = Assert.Single(report.Warnings, i => i.Code == ReviewErrorCode.ParameterMissing);
        Assert.Contains("柱", warning.Message);
        Assert.Contains("樓板", warning.Message);
        Assert.DoesNotContain("牆", warning.Message.Split('到')[1].Split('，')[0]);
        // 梁 are 主要構造 under 第70條, so an unbound 結構構架 is a gap like 柱 and 樓板.
        Assert.Contains("結構構架", warning.Message);
    }

    // --- 帷幕嵌板的綁定（帷幕牆規格 §6、§12 步驟 5）-----------------------------------------------

    /// <summary>
    /// 帷幕嵌板 carry 設計防火時效 like the 主要構造 do — not because 第70條 reaches them, but because
    /// 第79條第4項 and 第79條之3第2項 measure the 交接帶 by the panels' own rating. 豎框 carry no rating
    /// of their own and are no host at all.
    /// </summary>
    [Fact]
    public void Curtain_panels_carry_the_rating_parameter_so_the_junction_band_can_be_measured()
    {
        var source = ReviewInputSources.For("element.providedFireRating")!;

        Assert.Contains(ReviewParameterHost.CurtainPanels, source.Hosts);
        Assert.Equal(ReviewParameterLevel.Type, source.Level);
        foreach (var host in ReviewInputSources.MemberHosts) Assert.Contains(host, source.Hosts);
        Assert.DoesNotContain(ReviewParameterHost.Doors, source.Hosts);
        Assert.DoesNotContain(ReviewParameterHost.Windows, source.Hosts);
    }

    /// <summary>The required-binding list the setup flow shows names 帷幕嵌板 too (帷幕牆規格 §6).</summary>
    [Fact]
    public void The_missing_rating_parameter_is_reported_as_needed_on_curtain_panels_as_well()
    {
        var parameters = new Parameters();
        parameters.Bindings.RemoveAll(b => b.Key == FireRatingParameters.Provided);

        var item = Assert.Single(Readiness(parameters: parameters).Blocking,
            i => i.Message.Contains("element.providedFireRating"));

        Assert.Equal(ReviewErrorCode.ParameterMissing, item.Code);
        Assert.Contains("帷幕嵌板", item.Fix);
    }

    /// <summary>
    /// 案例 15: with the category unbound, the 90 cm 但書 cannot be measured at all, so the pre-review
    /// check says so in as many words instead of leaving it as one more name in a category list.
    /// </summary>
    [Fact]
    public void An_unbound_curtain_panel_category_says_the_spandrel_junction_cannot_be_judged()
    {
        var parameters = new Parameters();
        parameters.Bindings.RemoveAll(b =>
            b.Key == FireRatingParameters.Provided && b.Value == ReviewParameterHost.CurtainPanels);

        var report = Readiness(parameters: parameters);

        Assert.True(report.CanRun);
        var warning = Assert.Single(report.Warnings, i => i.Code == ReviewErrorCode.ParameterMissing);
        Assert.Contains("帷幕嵌板", warning.Message);
        Assert.Contains("帷幕嵌板未綁定設計防火時效，層間交接無法判定。", warning.Message);
        Assert.Contains("帷幕嵌板", warning.Fix);
    }

    /// <summary>The note belongs to 設計防火時效 alone; an unbound 柱 is a plain gap.</summary>
    [Fact]
    public void The_spandrel_note_is_not_added_to_every_unbound_category()
    {
        var parameters = new Parameters();
        parameters.Bindings.RemoveAll(b =>
            b.Key == FireRatingParameters.Provided && b.Value == ReviewParameterHost.Columns);

        var warning = Assert.Single(Readiness(parameters: parameters).Warnings, i => i.Code == ReviewErrorCode.ParameterMissing);

        Assert.Contains("柱", warning.Message);
        Assert.DoesNotContain("層間交接無法判定", warning.Message);
    }

    /// <summary>
    /// 設計防火保護 keeps its own hosts: an opening is a 門、窗 or 可開啟嵌板, and the two lists overlap
    /// on 帷幕嵌板 without either swallowing the other.
    /// </summary>
    [Fact]
    public void The_protection_parameter_still_belongs_to_the_openings_only()
    {
        var protection = ReviewInputSources.For("opening.providedFireProtection")!;

        Assert.Equal(ReviewInputSources.OpeningHosts, protection.Hosts);
        Assert.Contains(ReviewParameterHost.CurtainPanels, protection.Hosts);
        Assert.DoesNotContain(ReviewParameterHost.Walls, protection.Hosts);
    }

    /// <summary>
    /// A field no rule reads needs no parameter, and there is no parameter to bind for it either:
    /// 構件 Type 名稱, 區劃 Zone ID and Host 牆 UniqueId are carried as evidence and come from the model.
    /// </summary>
    [Fact]
    public void Evidence_only_fields_require_neither_a_rule_nor_a_parameter()
    {
        var fields = ReviewInputSources.FieldsUsedBy(Rules()).Select(f => f.Name).ToList();

        foreach (var name in new[] { "element.typeName", "zone.id", "opening.hostUniqueId" })
        {
            Assert.NotNull(RuleFieldCatalog.Default.Find(name));
            Assert.DoesNotContain(name, fields);
            Assert.Null(ReviewInputSources.For(name));
        }
    }

    /// <summary>
    /// 建築物用途類組 used to be evidence only. 第83條第一款但書 raises the 區劃 limit to 二○○平方公尺
    /// for Ｈ－２組, so the rules read it now and the project has to carry it — 所在樓層序 went the same
    /// way when 第70條 started counting storeys from the top.
    /// </summary>
    [Fact]
    public void Building_use_is_a_required_parameter_now_that_a_rule_reads_it()
    {
        var parameters = new Parameters();
        parameters.Bindings.RemoveAll(b => b.Key == ReviewInputSources.BuildingUse);

        var report = Readiness(parameters: parameters);

        Assert.False(report.CanRun);
        var item = Assert.Single(report.Blocking);
        Assert.Equal(ReviewErrorCode.ParameterMissing, item.Code);
        Assert.Contains(ReviewInputSources.BuildingUse, item.Message);
        Assert.Contains("專案資訊", item.Fix);
    }

    /// <summary>建築物高度 is measured, not typed: no Project Information parameter supplies it.</summary>
    [Fact]
    public void Building_height_comes_from_the_model_and_not_from_a_parameter()
    {
        Assert.Null(ReviewInputSources.For("building.height"));
        Assert.DoesNotContain("建築物高度", ReviewInputSources.ParameterNames);

        var measured = ReviewInputAssembler.Assemble(Set(), new Parameters().Snapshot(),
            model: new ReviewModelFacts(31.5, "模型量測：最高構件頂端至最低樓層"));

        var height = Assert.Single(measured.Area.Building, i => i.Field == "building.height");
        Assert.Equal(ReviewValue.Quantity(31.5, ReviewUnit.Meter), height.Value);
        Assert.Contains("模型量測", height.Source);
    }

    /// <summary>An unmeasurable model leaves the field missing — never a zero height.</summary>
    [Fact]
    public void An_unmeasured_model_supplies_no_height_at_all()
    {
        var assembled = ReviewInputAssembler.Assemble(Set(), new Parameters().Snapshot(), model: ReviewModelFacts.None);

        Assert.DoesNotContain(assembled.Area.Building, i => i.Field == "building.height");
    }

    /// <summary>
    /// 第79條之2 (垂直區劃規格 §12 步驟 4): every candidate opening Type is assembled into the 垂直區劃
    /// inputs, with 遮煙性能 and 設計防火時效 read from that Type — the two parameters the check hands
    /// the rules as <c>shaft.providedSmokeProtection</c> and <c>shaft.providedFireRating</c>.
    /// </summary>
    [Fact]
    public void Every_opening_type_is_assembled_into_the_vertical_compartment_inputs()
    {
        var parameters = new Parameters();
        parameters.Elements["type-fd"] = new Dictionary<string, ParameterReading>
        {
            [SmokeProtectionParameters.Provided] = ParameterReading.OfYesNo(1),
            [FireRatingParameters.Provided] = ParameterReading.OfText("1 小時")
        };
        var openings = Openings()
            .Concat(new[]
            {
                new OpeningObservation(Source("D9-shaft"), CandidateCategory.Door, "W2-shared", P(10, 7),
                    M(0.9), M(2.1), "type-fd", "維修門")
            })
            .ToList();

        var assembled = ReviewInputAssembler.Assemble(Set(openings: openings), parameters.Snapshot());
        var device = assembled.VerticalCompartment.ForType("type-fd");

        Assert.NotNull(device);
        Assert.Equal(ProvidedFireProtectionKind.Yes, device!.SmokeProtection.Kind);
        Assert.Equal(60, device.FireRating.Minutes);
        Assert.Same(assembled.Area, assembled.VerticalCompartment.Context);
    }

    /// <summary>
    /// 遮煙性能 and 設計防火保護 are two questions (決議 6), so a Type that carries neither must say so
    /// twice over, each time naming its own parameter. The reason text is what the panel shows the
    /// user, and pointing at the wrong checkbox is worse than saying nothing.
    /// </summary>
    [Fact]
    public void An_unbound_smoke_seal_names_its_own_parameter_and_not_the_protection_one()
    {
        var assembled = ReviewInputAssembler.Assemble(Set(openings: WithPanelType()), new Parameters().Snapshot());
        var device = assembled.VerticalCompartment.ForType("type-panel");

        Assert.NotNull(device);
        Assert.Equal(ProvidedFireProtectionKind.Missing, device!.SmokeProtection.Kind);
        Assert.Contains(SmokeProtectionParameters.Provided, device.SmokeProtection.Reason!, StringComparison.Ordinal);
        Assert.DoesNotContain(FireProtectionParameters.Provided, device.SmokeProtection.Reason!, StringComparison.Ordinal);

        // And 設計防火保護 keeps answering in its own name.
        Assert.Contains(FireProtectionParameters.Provided,
            ReviewInputAssembler.Protection(ParameterReading.Absent).Reason!, StringComparison.Ordinal);
    }

    /// <summary>The two building facts a project already records keep their plain names.</summary>
    [Fact]
    public void The_shared_building_parameters_carry_no_review_prefix()
    {
        Assert.Equal("建築物用途類組", ReviewInputSources.BuildingUse);
        Assert.Equal("地上層數", ReviewInputSources.FloorsAboveGround);
        Assert.DoesNotContain(ReviewInputSources.ParameterNames,
            name => name.StartsWith("防火檢討_建築物", StringComparison.Ordinal) ||
                    name.StartsWith("防火檢討_地上層數", StringComparison.Ordinal));
    }

    /// <summary>
    /// 樑 are 主要構造 and 第70條 states a required rating for them, so they carry 設計防火時效 like
    /// 柱、承重牆壁 and 樓地板. What they have no answer for is deriving that design value from the
    /// Type's size — 第71～73條 give 樑 no dimensional threshold — which is the panel's business,
    /// not the review's.
    /// </summary>
    [Fact]
    public void Beams_carry_the_rating_parameter_because_article_70_requires_one_of_them()
    {
        Assert.Contains(ReviewParameterHost.StructuralFraming, ReviewInputSources.MemberHosts);

        var source = ReviewInputSources.For("element.providedFireRating")!;
        Assert.Contains(ReviewParameterHost.StructuralFraming, source.Hosts);

        Assert.False(FireRatingDeriver.IsDerivable(CandidateCategory.StructuralFraming));
    }

    [Fact]
    public void Candidates_that_cannot_be_read_or_have_no_enclosed_zone_block()
    {
        var unreadable = Readiness(candidates: Domain.Common.Result.Failure<CandidateSet>(new Error("candidates.areaPlanMissing", "找不到 Area Plan")));
        Assert.Contains(unreadable.Blocking, i => i.Condition == ReadinessCondition.SpatialRelations && i.Code == ReviewErrorCode.CandidateZoneUnusable);

        var unenclosed = Readiness(candidates: Domain.Common.Result.Success(CandidateResolver.Resolve(Observations())));
        Assert.Contains(unenclosed.Blocking, i => i.Code == ReviewErrorCode.CandidateZoneUnusable && i.Message.Contains("C 區"));

        var empty = Readiness(candidates: Domain.Common.Result.Success(CandidateResolver.Resolve(Observations(Array.Empty<ZoneObservation>()))));
        Assert.Contains(empty.Blocking, i => i.Message.Contains("沒有任何區劃"));
    }

    [Fact]
    public void Design_options_and_links_are_warnings_and_project_facts_are_reported()
    {
        var report = Readiness(conditions: new ReviewModelConditions("新建", hasDesignOptions: true, linkedModelCount: 2, lengthUnit: "公釐", areaUnit: "平方公尺"));

        Assert.True(report.CanRun);
        Assert.Equal(2, report.Warnings.Count(i => i.Code == ReviewErrorCode.EnvironmentLimited));
        Assert.Contains(report.Warnings, i => i.Message.Contains("2 個連結模型"));
        Assert.Contains(report.Items, i => i.Condition == ReadinessCondition.ProjectSettings && i.Message.Contains("Phase「新建」") && i.Message.Contains("公釐"));
    }

    [Fact]
    public void Readiness_log_carries_code_stage_and_fix_and_ends_with_the_verdict()
    {
        var log = Readiness(Package(ReviewPackageStatus.BoundaryDraft)).ToLog(Now);

        Assert.All(log.Entries, e => Assert.Equal(ReviewStage.Review, e.Stage));
        Assert.All(log.Entries, e => Assert.Equal(PackageId, e.PackageId));
        Assert.Contains(log.Entries, e => e.Code == ReviewErrorCode.ReviewNotReady && e.Severity == ReviewSeverity.Error && e.Suggestion is not null);
        Assert.Equal(ReviewErrorCode.ReviewNotReady, log.Entries.Last().Code);
        Assert.True(ReviewErrorCode.IsKnown(ReviewErrorCode.ReviewReady));
        Assert.True(ReviewErrorCode.IsKnown(ReviewErrorCode.PerformanceExceeded));
    }

    // --- parameters → inputs --------------------------------------------------------------------

    private static RuleFieldDefinition Field(string name) => RuleFieldCatalog.Default.Find(name)!;

    [Theory]
    [InlineData(ParameterReadingKind.YesNo, 1, null, true)]
    [InlineData(ParameterReadingKind.YesNo, 0, null, false)]
    [InlineData(ParameterReadingKind.Integer, 1, null, true)]
    [InlineData(ParameterReadingKind.Text, 0, "是", true)]
    [InlineData(ParameterReadingKind.Text, 0, "ＮＯ", false)]
    [InlineData(ParameterReadingKind.Text, 0, "無", false)]
    public void Boolean_inputs_accept_yes_no_integers_and_clear_words(ParameterReadingKind kind, int number, string? text, bool expected)
    {
        var reading = kind switch
        {
            ParameterReadingKind.YesNo => ParameterReading.OfYesNo(number),
            ParameterReadingKind.Integer => ParameterReading.OfInteger(number),
            _ => ParameterReading.OfText(text)
        };

        var input = ReviewInputAssembler.Convert(Field("zone.sprinklered"), reading, "面積：防火檢討_自動滅火設備")!;

        Assert.False(input.IsUnreadable);
        Assert.Equal(expected, input.Value!.Flag);
        Assert.Equal("面積：防火檢討_自動滅火設備", input.Source);
    }

    [Fact]
    public void Unclear_values_are_unreadable_never_false_or_zero()
    {
        var partly = ReviewInputAssembler.Convert(Field("zone.sprinklered"), ParameterReading.OfText("部分"), "s")!;
        Assert.True(partly.IsUnreadable);
        Assert.Contains("部分", partly.UnreadableReason);

        Assert.True(ReviewInputAssembler.Convert(Field("zone.sprinklered"), ParameterReading.OfYesNo(2), "s")!.IsUnreadable);
        Assert.True(ReviewInputAssembler.Convert(Field("building.floorsAboveGround"), ParameterReading.OfText("十層"), "s")!.IsUnreadable);
        Assert.True(ReviewInputAssembler.Convert(Field("building.floorsAboveGround"), ParameterReading.OfYesNo(1), "s")!.IsUnreadable);
        Assert.True(ReviewInputAssembler.Convert(Field("building.use"), ParameterReading.OfYesNo(1), "s")!.IsUnreadable);
    }

    [Fact]
    public void Absent_or_empty_parameters_supply_nothing()
    {
        Assert.Null(ReviewInputAssembler.Convert(Field("zone.use"), ParameterReading.Absent, "s"));
        Assert.Null(ReviewInputAssembler.Convert(Field("zone.use"), ParameterReading.Empty, "s"));
        Assert.Null(ReviewInputAssembler.Convert(Field("zone.use"), ParameterReading.OfText("   "), "s"));
    }

    [Fact]
    public void Numbers_and_lengths_keep_their_unit()
    {
        var floors = ReviewInputAssembler.Convert(Field("building.floorsAboveGround"), ParameterReading.OfText("１２"), "s")!;
        Assert.Equal(12, floors.Value!.Number);
        Assert.Equal(ReviewUnit.None, floors.Value.Unit);

        var height = ReviewInputAssembler.Convert(Field("building.height"), ParameterReading.OfLength(45.5), "s")!;
        Assert.Equal(ReviewValue.Quantity(45.5, ReviewUnit.Meter), height.Value);
        Assert.Equal(45.5, ReviewInputAssembler.Convert(Field("building.height"), ParameterReading.OfText("45.5 m"), "s")!.Value!.Number);
        Assert.Equal(45.5, ReviewInputAssembler.Convert(Field("building.height"), ParameterReading.OfText("45.5公尺"), "s")!.Value!.Number);
    }

    [Fact]
    public void Ratings_and_protection_are_read_without_guessing()
    {
        Assert.Equal(120, ReviewInputAssembler.Rating(ParameterReading.OfText("2 小時")).Minutes);
        Assert.Equal(90, ReviewInputAssembler.Rating(ParameterReading.OfInteger(90)).Minutes);
        Assert.Equal(90, ReviewInputAssembler.Rating(ParameterReading.OfNumber(1.5), FireRatingUnit.Hour).Minutes);
        Assert.Equal(ProvidedFireRatingKind.Missing, ReviewInputAssembler.Rating(ParameterReading.Absent).Kind);
        Assert.Equal(ProvidedFireRatingKind.Missing, ReviewInputAssembler.Rating(ParameterReading.Empty).Kind);
        Assert.Equal(ProvidedFireRatingKind.Unreadable, ReviewInputAssembler.Rating(ParameterReading.OfYesNo(1)).Kind);
        Assert.Equal(ProvidedFireRatingKind.Undeterminable, ReviewInputAssembler.Rating(ParameterReading.OfText("1hr/2hr")).Kind);

        Assert.Equal(ProvidedFireProtectionKind.Yes, ReviewInputAssembler.Protection(ParameterReading.OfYesNo(1)).Kind);
        Assert.Equal(ProvidedFireProtectionKind.No, ReviewInputAssembler.Protection(ParameterReading.OfText("否")).Kind);
        Assert.Equal(ProvidedFireProtectionKind.Unreadable, ReviewInputAssembler.Protection(ParameterReading.OfText("甲種防火門")).Kind);
        Assert.Equal(ProvidedFireProtectionKind.Unreadable, ReviewInputAssembler.Protection(ParameterReading.OfNumber(1)).Kind);
    }

    /// <summary>
    /// 防火檢討_設計防火保護 is a Yes/No Type parameter, and Revit draws an unticked box and a
    /// never-touched box identically. So a bound-but-unset box is 否 — otherwise no model could ever
    /// state「這個型號不是防火門窗」and every opening would stay 待確認 forever. An unbound parameter
    /// stays 資料不足, because that is a setup problem and not an answer.
    /// </summary>
    [Fact]
    public void An_unticked_checkbox_is_否_but_an_unbound_parameter_is_still_資料不足()
    {
        Assert.Equal(ProvidedFireProtectionKind.No, ReviewInputAssembler.Protection(ParameterReading.Empty).Kind);
        Assert.Equal("未勾選", ReviewInputAssembler.Protection(ParameterReading.Empty).RawText);
        Assert.Equal(ProvidedFireProtection.NoText, ReviewInputAssembler.Protection(ParameterReading.Empty).RuleText);
        Assert.Equal(ProvidedFireProtectionKind.Missing, ReviewInputAssembler.Protection(ParameterReading.Absent).Kind);
    }

    /// <summary>
    /// The Type carries the box but nobody ticked it, so the assembler must still take the Type —
    /// skipping it would hand the check nothing and turn 未符合 back into 待確認.
    /// </summary>
    [Fact]
    public void A_type_whose_checkbox_was_never_ticked_still_reaches_the_check_as_否()
    {
        var door = new OpeningObservation(Source("D-untouched"), CandidateCategory.Door, "W2-shared", P(10, 6), M(1), M(2.1), "type-plain", "一般門");
        var set = CandidateResolver.Resolve(Observations(Zones().Take(2), Members(), new[] { door }));
        var snapshot = new ReviewParameterSnapshot(AllBindings(), null, new Dictionary<string, IReadOnlyDictionary<string, ParameterReading>>
        {
            ["type-plain"] = new Dictionary<string, ParameterReading> { [FireProtectionParameters.Provided] = ParameterReading.Empty }
        });

        var protections = ReviewInputAssembler.Assemble(set, snapshot).Protection;

        Assert.Equal(ProvidedFireProtectionKind.No, protections.For(door)!.Protection.Kind);
        Assert.Equal(FireProtectionScope.Type, protections.For(door)!.Scope);
    }

    /// <summary>A Type with no such parameter bound at all is the one case that stays 資料不足.</summary>
    [Fact]
    public void A_type_without_the_parameter_bound_supplies_nothing()
    {
        var door = new OpeningObservation(Source("D-unbound"), CandidateCategory.Door, "W2-shared", P(10, 6), M(1), M(2.1), "type-none", "一般門");
        var set = CandidateResolver.Resolve(Observations(Zones().Take(2), Members(), new[] { door }));

        var protections = ReviewInputAssembler.Assemble(set, new ReviewParameterSnapshot(AllBindings(), null, null)).Protection;

        Assert.Null(protections.For(door));
    }

    [Fact]
    public void Assembly_fills_building_zone_type_and_opening_inputs_from_the_snapshot()
    {
        var assembly = ReviewInputAssembler.Assemble(Set(), new Parameters().Snapshot());

        Assert.Equal(new[] { "building.fireResistiveConstruction", "building.floorsAboveGround" },
            assembly.Area.Building.Select(i => i.Field));
        var building = assembly.Area.Building.Single(i => i.Field == "building.fireResistiveConstruction");
        Assert.Equal("專案資訊：" + ReviewInputSources.FireResistiveConstruction, building.Source);
        Assert.Equal(new[] { "zone.floorNumber", "zone.sprinklered", "zone.use" },
            assembly.Area.ForZone(ZoneA).Select(i => i.Field));
        Assert.Equal(new[] { "zone.floorNumber", "zone.sprinklered" },
            assembly.Area.ForZone(ZoneB).Select(i => i.Field));
        Assert.Same(assembly.Area, assembly.Rating.Context);
        Assert.Same(assembly.Area, assembly.Protection.Context);

        var rc200 = assembly.Rating.TypeRatings.Single(t => t.TypeUniqueId == "type-rc200");
        Assert.Equal(120, rc200.Rating.Minutes);
        Assert.Equal(FireRatingParameters.Provided, rc200.Source);
        Assert.Equal(ProvidedFireRatingKind.Missing, assembly.Rating.TypeRatings.Single(t => t.TypeUniqueId == "type-cw").Rating.Kind);

        Assert.Contains(assembly.Protection.Protections, p => p.UniqueId == "D1-shared" && p.Protection.Kind == ProvidedFireProtectionKind.Yes);
        Assert.Contains(assembly.Protection.Protections, p => p.UniqueId == "WN1-bottom" && p.Protection.Kind == ProvidedFireProtectionKind.No);
    }

    [Fact]
    public void Zone_parts_that_disagree_make_the_zone_input_unreadable()
    {
        var zone = new ZoneObservation(ZoneA, "A 區", new[]
        {
            new ZonePartObservation("area-a1", new[] { Rect(0, 0, 5, 10) }, Domain.Geometry.PlanUnits.SquareMetersToSquareFeet(50)),
            new ZonePartObservation("area-a2", new[] { Rect(5, 0, 10, 10) }, Domain.Geometry.PlanUnits.SquareMetersToSquareFeet(50))
        });
        var set = CandidateResolver.Resolve(Observations(new[] { zone }, Array.Empty<MemberObservation>(), Array.Empty<OpeningObservation>()));

        ReviewInput? Sprinklered(ParameterReading a1, ParameterReading a2)
        {
            var snapshot = new ReviewParameterSnapshot(AllBindings(), null, new Dictionary<string, IReadOnlyDictionary<string, ParameterReading>>
            {
                ["area-a1"] = new Dictionary<string, ParameterReading> { [ReviewInputSources.Sprinklered] = a1 },
                ["area-a2"] = new Dictionary<string, ParameterReading> { [ReviewInputSources.Sprinklered] = a2 }
            });
            return ReviewInputAssembler.Assemble(set, snapshot).Area.ForZone(ZoneA).SingleOrDefault(i => i.Field == "zone.sprinklered");
        }

        Assert.True(Sprinklered(ParameterReading.OfYesNo(1), ParameterReading.OfText("是"))!.Value!.Flag);
        var disagree = Sprinklered(ParameterReading.OfYesNo(1), ParameterReading.OfYesNo(0))!;
        Assert.True(disagree.IsUnreadable);
        Assert.Contains("不一致", disagree.UnreadableReason);
        Assert.True(Sprinklered(ParameterReading.OfYesNo(1), ParameterReading.Empty)!.IsUnreadable);
        Assert.Null(Sprinklered(ParameterReading.Empty, ParameterReading.Absent));
    }

    [Fact]
    public void Instance_protection_wins_and_type_protection_fills_the_rest()
    {
        var door = new OpeningObservation(Source("D-typed"), CandidateCategory.Door, "W2-shared", P(10, 6), M(1), M(2.1), "type-fd", "FD");
        var other = new OpeningObservation(Source("D-own"), CandidateCategory.Door, "W2-shared", P(10, 8), M(1), M(2.1), "type-fd", "FD");
        var set = CandidateResolver.Resolve(Observations(Zones().Take(2), Members(), new[] { door, other }));
        var snapshot = new ReviewParameterSnapshot(AllBindings(), null, new Dictionary<string, IReadOnlyDictionary<string, ParameterReading>>
        {
            ["type-fd"] = new Dictionary<string, ParameterReading> { [FireProtectionParameters.Provided] = ParameterReading.OfYesNo(1) },
            ["D-own"] = new Dictionary<string, ParameterReading> { [FireProtectionParameters.Provided] = ParameterReading.OfYesNo(0) }
        });

        var protections = ReviewInputAssembler.Assemble(set, snapshot).Protection.Protections;

        Assert.Equal(2, protections.Count);
        Assert.Contains(protections, p => p.Scope == FireProtectionScope.Type && p.UniqueId == "type-fd");
        Assert.Contains(protections, p => p.Scope == FireProtectionScope.Instance && p.UniqueId == "D-own" && p.Protection.Kind == ProvidedFireProtectionKind.No);
    }

    // --- one review, three checks ---------------------------------------------------------------

    [Fact]
    public void Review_runs_every_check_and_leaves_the_package_reviewed_with_the_rule_set_locked()
    {
        var progress = new Recorder();
        var outcome = Run(Request(), progress: progress);

        Assert.True(outcome.IsCompleted, outcome.Message);
        var run = outcome.Run!;
        Assert.Equal(ReviewRunState.Completed, run.State);
        Assert.Equal(RuleSetId, run.RuleSetId);
        Assert.Equal(ShippedVersion, run.RuleSetVersion);
        Assert.True(run.Baseline.IsRecorded);
        Assert.All(new[] { ReviewCheckTypes.CompartmentArea, ReviewCheckTypes.FireResistance, ReviewCheckTypes.OpeningProtection },
            type => Assert.Contains(run.Results, r => r.CheckType == type));
        // 區劃面積免除 joins them: this storey's 區劃用途 are 辦公 and none at all, and 第79條之1 reaches
        // neither, so that row is 未檢討 too (第79條之1文件 §3.2、決議 10).
        var empty = new[]
        {
            ReviewCheckTypes.AreaExemption, ReviewCheckTypes.CompartmentContinuity, ReviewCheckTypes.VerticalCompartment
        };
        Assert.All(outcome.Table!.Sections.Where(s => !empty.Contains(s.CheckType)),
            s => Assert.NotEqual(ReviewStatus.NotRun, s.Status));
        Assert.Empty(outcome.Table.OtherEntries);

        // No 帷幕牆 geometry reader: the fourth row is 未檢討, and the log says why (帷幕牆規格 §8).
        Assert.Equal(ReviewStatus.NotRun, outcome.Table.Section(ReviewCheckTypes.CompartmentContinuity).Status);
        Assert.Contains(outcome.Log.Entries, e => e.UserMessage.Contains("帷幕牆區劃交接未檢討"));

        // 垂直區劃 is 未檢討 for the same kind of reason: this storey's 區劃用途 are 辦公 and none at
        // all, so 第79條之2 holds no 防火設備 of it to anything (垂直區劃規格 §3.2).
        Assert.Equal(ReviewStatus.NotRun, outcome.Table.Section(ReviewCheckTypes.VerticalCompartment).Status);
        Assert.DoesNotContain(run.Results, r => r.CheckType == ReviewCheckTypes.VerticalCompartment);

        Assert.Equal(ReviewPackageStatus.Reviewed, outcome.Package.Status);
        Assert.Equal(RuleSetId, outcome.Package.RuleSetId);
        Assert.Equal(ShippedVersion, outcome.Package.RuleSetVersion);
        Assert.Equal(run.RunId.ToString("D"), outcome.Package.LastReviewRunId);
        Assert.Equal(1, outcome.Package.BoundaryRevision);

        Assert.Equal(new[]
            {
                FireReviewStep.CompartmentArea, FireReviewStep.AreaExemption, FireReviewStep.FireResistance,
                FireReviewStep.OpeningProtection, FireReviewStep.CurtainWallJunction,
                FireReviewStep.VerticalCompartment, FireReviewStep.Evidence
            },
            progress.Reports.Select(p => p.Step));
        Assert.Equal("垂直區劃（6/7）", progress.Reports[5].Message);
        Assert.Contains(outcome.Log.Entries, e => e.Code == ReviewErrorCode.ReviewCompleted && e.UserMessage.StartsWith("檢討完成"));
    }

    // --- 第79條第1項之阻熱性（垂直區劃規格決議 38）---------------------------------------------------

    /// <summary>
    /// A 防火設備 on a 區劃 boundary owes 一小時以上之阻熱性. Asked only once the opening is a 防火設備:
    /// tw-bcr-79-opening-insulation sits above tw-bcr-79-opening, so a 否 is that rule's 未符合 and an
    /// unstated one is 資料不足; an opening that is no 防火設備 at all still fails 防火門窗 itself.
    /// </summary>
    [Theory]
    [InlineData(1, 1, ReviewStatus.Pass, "tw-bcr-79-opening-insulation")]
    [InlineData(1, 0, ReviewStatus.Fail, "tw-bcr-79-opening-insulation")]
    [InlineData(1, null, ReviewStatus.InsufficientData, "tw-bcr-79-opening-insulation")]
    [InlineData(0, 1, ReviewStatus.Fail, "tw-bcr-79-opening")]
    public void A_boundary_fire_door_is_held_to_one_hour_of_insulation(int protection, int? insulation, ReviewStatus expected, string ruleId)
    {
        var parameters = new Parameters();
        parameters.Elements["D1-shared"][FireProtectionParameters.Provided] = ParameterReading.OfYesNo(protection);
        if (insulation is int ticked)
            parameters.Elements["D1-shared"][InsulationParameters.Provided] = ParameterReading.OfYesNo(ticked);
        else
            parameters.Elements["D1-shared"].Remove(InsulationParameters.Provided);

        var door = ResultOf(Run(Request(parameters: parameters)).Run!, ReviewCheckTypes.OpeningProtection, "D1-shared", ZoneA);

        Assert.Equal(expected, door.Status);
        Assert.Equal(ruleId, door.RuleId);
    }

    /// <summary>
    /// Code review: a 防火門窗 nobody filled in is a 防火門窗 question, not an 阻熱性 one. The insulation
    /// rule cannot tell whether it applies without 設計防火保護, so 第79條第1項's 防火門窗 rule answers:
    /// 資料不足, requiring 「是」, still a 防火設備 the 區劃 needs.
    /// </summary>
    [Fact]
    public void A_door_with_no_fire_protection_stated_is_filed_under_the_fire_door_rule()
    {
        var parameters = new Parameters();
        parameters.Elements["D1-shared"].Remove(FireProtectionParameters.Provided);

        var door = ResultOf(Run(Request(parameters: parameters)).Run!, ReviewCheckTypes.OpeningProtection, "D1-shared", ZoneA);

        Assert.Equal(ReviewStatus.InsufficientData, door.Status);
        Assert.Equal("tw-bcr-79-opening", door.RuleId);
        Assert.Equal(ReviewValue.OfText("是"), door.RequiredValue);
    }

    /// <summary>Ticking 阻熱性 makes a stored run 需更新, like ticking 防火門窗 does (spec 13.1).</summary>
    [Fact]
    public void Changing_a_doors_insulation_makes_the_stored_run_need_an_update()
    {
        var first = Run(Request());
        var now = new Parameters();
        now.Elements["D1-shared"][InsulationParameters.Provided] = ParameterReading.OfYesNo(0);

        var inspection = StoredRunInspection.Inspect(first.Package, first.Run!, CurrentBaseline(now), RuleSetId, ShippedVersion, Now);

        Assert.True(inspection.Freshness.IsStale);
    }

    [Fact]
    public void Review_verdicts_follow_the_model_parameters()
    {
        var outcome = Run(Request());
        var run = outcome.Run!;

        // 外牆推定的帷幕嵌板：逐件記錄，另有一行總數提醒抽查
        Assert.Contains(outcome.Log.Entries, e => e.Code == ReviewErrorCode.CandidateFacadeInferred && e.ElementUniqueId == "P1-panel");
        Assert.Contains(outcome.Log.Entries, e => e.Code == ReviewErrorCode.CandidateFacadeInferred &&
                                                  e.Severity == ReviewSeverity.Warning && e.UserMessage.Contains("1 件"));

        // 區劃面積：100 m² ≤ 1500 m²（A 區無灑水）與 3000 m²（B 區有灑水）
        Assert.Equal(ReviewStatus.Pass, run.Results.Single(r => r.CheckType == ReviewCheckTypes.CompartmentArea && r.ZoneId == ZoneA.ToString("D")).Status);
        Assert.Equal(ReviewStatus.Pass, run.Results.Single(r => r.CheckType == ReviewCheckTypes.CompartmentArea && r.ZoneId == ZoneB.ToString("D")).Status);

        // 承重牆 RC200 = 2 小時 ≥ 第70條在自頂層第 10 層要求的 1 小時
        Assert.Equal(ReviewStatus.Pass, ResultOf(run, ReviewCheckTypes.FireResistance, "W1-bottom", ZoneA).Status);
        // 樓板沒有 Type 時效 → 資料不足（spec 16.3 情境 6：缺值不是未符合）
        Assert.Equal(ReviewStatus.InsufficientData, ResultOf(run, ReviewCheckTypes.FireResistance, "F1-slab", ZoneA).Status);
        // 梁自第70條起受檢，但這個 fixture 的梁沒有 Type，讀不到設計時效 → 資料不足，不是未符合
        Assert.Equal(ReviewStatus.InsufficientData, ResultOf(run, ReviewCheckTypes.FireResistance, "B1-shared", ZoneA).Status);

        // 門窗：是 → 符合；否 → 未符合；外牆帷幕嵌板 → 不適用（第79條之3、之4 接手）；非 Hosted → 人工覆核（spec 16.3 情境 7）
        Assert.Equal(ReviewStatus.Pass, ResultOf(run, ReviewCheckTypes.OpeningProtection, "D1-shared", ZoneA).Status);
        Assert.Equal(ReviewStatus.Fail, ResultOf(run, ReviewCheckTypes.OpeningProtection, "WN1-bottom", ZoneB).Status);
        Assert.Equal(ReviewStatus.NotApplicable, ResultOf(run, ReviewCheckTypes.OpeningProtection, "P1-panel").Status);
        Assert.Equal(ReviewStatus.ManualReview, ResultOf(run, ReviewCheckTypes.OpeningProtection, "N1-unhosted").Status);

        var table = ReviewTable.Build(run);
        Assert.Equal(ReviewVerdict.Fail, table.Verdict);
        Assert.All(run.Results, r => Assert.False(string.IsNullOrWhiteSpace(r.RuleId)));
        Assert.All(run.Results.Where(r => r.Status is ReviewStatus.Pass or ReviewStatus.Fail),
            r => Assert.False(string.IsNullOrWhiteSpace(r.LegalReference)));
    }

    [Fact]
    public void Missing_building_input_is_insufficient_data_everywhere_and_never_a_fail()
    {
        var parameters = new Parameters();
        parameters.Project.Clear();

        var run = Run(Request(parameters: parameters)).Run!;

        Assert.DoesNotContain(run.Results, r => r.Status is ReviewStatus.Pass or ReviewStatus.Fail);
        Assert.Contains(run.Results, r => r.Status == ReviewStatus.InsufficientData && r.CheckType == ReviewCheckTypes.CompartmentArea);
    }

    [Fact]
    public void Area_exactly_at_the_limit_passes_and_one_over_fails()
    {
        CandidateSet SetOf(double squareMeters)
        {
            var zone = new ZoneObservation(ZoneA, "A 區", new[]
            {
                new ZonePartObservation("area-a", new[] { Rect(0, 0, 50, 30) }, Domain.Geometry.PlanUnits.SquareMetersToSquareFeet(squareMeters))
            });
            return CandidateResolver.Resolve(Observations(new[] { zone }, Array.Empty<MemberObservation>(), Array.Empty<OpeningObservation>()));
        }

        ReviewStatus Status(double squareMeters)
        {
            var set = SetOf(squareMeters);
            var inputs = ReviewInputAssembler.Assemble(set, new Parameters().Snapshot());
            var outcome = FireReviewRunner.Run(new FireReviewRequest(Package(), Rules(), Today, set, inputs), clock: () => Now, newId: Ids(3));
            return outcome.Run!.Results.Single(r => r.CheckType == ReviewCheckTypes.CompartmentArea).Status;
        }

        Assert.Equal(ReviewStatus.Pass, Status(1500));
        Assert.Equal(ReviewStatus.Fail, Status(1500.5));
    }

    [Fact]
    public void Running_three_times_gives_the_same_results()
    {
        string Signature(ReviewRun run) => string.Join("\n", run.Results
            .Select(r => $"{r.CheckType}|{r.ZoneId}|{string.Join(",", r.SubjectUniqueIds)}|{r.Status}|{r.RuleId}|{r.ActualValue}|{r.RequiredValue}")
            .OrderBy(x => x, StringComparer.Ordinal));

        var runs = Enumerable.Range(1, 3).Select(i => Run(Request(), prefix: i).Run!).ToList();

        Assert.Equal(Signature(runs[0]), Signature(runs[1]));
        Assert.Equal(Signature(runs[0]), Signature(runs[2]));
        Assert.Equal(runs[0].Results.Count, runs[2].Results.Count);
        Assert.Equal(runs[0].Baseline.ContextFingerprint, runs[2].Baseline.ContextFingerprint);
    }

    [Fact]
    public void Cancelling_before_the_first_check_stores_nothing()
    {
        using var source = new CancellationTokenSource();
        source.Cancel();
        var request = Request();

        var outcome = Run(request, token: source.Token);

        Assert.Equal(FireReviewOutcomeKind.Cancelled, outcome.Kind);
        Assert.Null(outcome.Run);
        Assert.Null(outcome.Table);
        Assert.Same(request.Package, outcome.Package);
        Assert.Contains(outcome.Log.Entries, e => e.Code == ReviewErrorCode.ReviewCancelled);
        Assert.Contains("沒有變更", outcome.Message);
    }

    [Fact]
    public void Cancelling_at_a_safe_point_between_checks_stops_there()
    {
        using var source = new CancellationTokenSource();
        var progress = new Recorder(p => { if (p.Step == FireReviewStep.FireResistance) source.Cancel(); });
        var request = Request();

        var outcome = Run(request, token: source.Token, progress: progress);

        Assert.Equal(FireReviewOutcomeKind.Cancelled, outcome.Kind);
        Assert.Null(outcome.Run);
        Assert.Same(request.Package, outcome.Package);
        Assert.Equal(
            new[] { FireReviewStep.CompartmentArea, FireReviewStep.AreaExemption, FireReviewStep.FireResistance },
            progress.Reports.Select(p => p.Step));
        Assert.Equal(3, outcome.Performance.Stages.Count);
    }

    [Fact]
    public void A_check_that_refuses_fails_the_whole_run_without_a_partial_one()
    {
        var set = CandidateResolver.Resolve(Observations());   // C 區 has no enclosed Area
        var inputs = ReviewInputAssembler.Assemble(set, new Parameters().Snapshot());
        var request = new FireReviewRequest(Package(), Rules(), Today, set, inputs);

        var outcome = FireReviewRunner.Run(request, clock: () => Now, newId: Ids(4));

        Assert.Equal(FireReviewOutcomeKind.Failed, outcome.Kind);
        Assert.Null(outcome.Run);
        Assert.Same(request.Package, outcome.Package);
        Assert.Equal(ReviewErrorCode.CandidateZoneUnusable, outcome.Error!.Code);
        Assert.Contains(outcome.Log.Entries, e => e.Severity == ReviewSeverity.Error && e.Suggestion is not null);
    }

    [Fact]
    public void Request_refuses_candidates_or_previous_run_of_another_package()
    {
        var set = Set();
        var inputs = ReviewInputAssembler.Assemble(set, new Parameters().Snapshot());
        var other = new ReviewPackage(Guid.NewGuid(), "floor-plan", "level-1F", "scheme-fire", "area-plan", status: ReviewPackageStatus.Ready);

        Assert.Throws<ArgumentException>(() => new FireReviewRequest(other, Rules(), Today, set, inputs));
        var foreign = new ReviewRun(Guid.NewGuid(), other.PackageId, RuleSetId, ShippedVersion, 1, Now);
        Assert.Throws<ArgumentException>(() => new FireReviewRequest(Package(), Rules(), Today, set, inputs, previousRun: foreign));
    }

    // --- overrides between runs (spec 11.8) -----------------------------------------------------

    private static Parameters WithWallRating(string rating)
    {
        var parameters = new Parameters();
        parameters.Elements["type-rc200"][FireRatingParameters.Provided] = ParameterReading.OfText(rating);
        return parameters;
    }

    [Fact]
    public void Override_is_kept_by_an_identical_rerun_and_needs_reconfirmation_when_its_evidence_moves()
    {
        var first = Run(Request(parameters: WithWallRating("30 min")), prefix: 1).Run!;
        var wall = ResultOf(first, ReviewCheckTypes.FireResistance, "W1-bottom", ZoneA);
        Assert.Equal(ReviewStatus.Fail, wall.Status);
        var overridden = ReviewOverrides.Apply(first, wall.ResultId, ReviewStatus.ManualReview, "外包防火被覆待技師簽證", "王技師", Now,
            newId: () => Guid.Parse("99999999-0000-0000-0000-000000000001")).Value;

        var same = Run(Request(parameters: WithWallRating("30 min"), previous: overridden), prefix: 2);
        Assert.Equal(1, same.CarryOver!.Count(OverrideCarryOverOutcome.Kept));
        var kept = ResultOf(same.Run!, ReviewCheckTypes.FireResistance, "W1-bottom", ZoneA);
        Assert.Equal(ReviewStatus.ManualReview, same.Run!.EffectiveStatus(kept));

        var moved = Run(Request(parameters: WithWallRating("45 min"), previous: same.Run), prefix: 3);
        Assert.Equal(1, moved.CarryOver!.Count(OverrideCarryOverOutcome.NeedsReconfirmation));
        var awaiting = ResultOf(moved.Run!, ReviewCheckTypes.FireResistance, "W1-bottom", ZoneA);
        Assert.Equal(ReviewStatus.Fail, moved.Run!.EffectiveStatus(awaiting));
        Assert.True(moved.Run.CurrentOverrideFor(awaiting.ResultId)!.Standing == ReviewOverrideStanding.NeedsReconfirmation);
        Assert.Contains(moved.Log.Entries, e => e.Code == ReviewErrorCode.OverrideNeedsReconfirmation);
    }

    // --- 帷幕牆區劃交接（帷幕牆規格 §8、§10 案例 19、20）----------------------------------------

    /// <summary>
    /// Stands in for the Revit 帷幕牆 reader (帷幕牆規格 §4.1): it keeps the request the run built and
    /// hands back one straight 12 m curtain wall whose outside face looks south, with a 區劃牆 reaching
    /// it at 5 m and stopping flush with that face — so the 但書 decides, not the 50 cm projection.
    /// The 交接帶 panel's rating is what its Type carries, so changing that Type changes what the
    /// junctions measure, as it does in the model.
    /// </summary>
    private sealed class CurtainWalls : ICurtainWallGeometryReader
    {
        private const double WallMm = 12000;
        private const double StoreyMm = 3600;
        private const double OffsetMm = 75;

        private readonly double _bandMinutes;
        private readonly Error? _failure;
        private readonly bool _split;
        private readonly bool _throughLevel;

        public CurtainWalls(double bandMinutes = 60, Error? failure = null, bool split = false, bool throughLevel = false)
        {
            _bandMinutes = bandMinutes;
            _failure = failure;
            _split = split;
            _throughLevel = throughLevel;
        }

        /// <summary>What the run asked for: the hosts, their required ratings and their clauses.</summary>
        public CurtainWallReadRequest? Asked { get; private set; }

        public Result<CurtainWallObservationSet> Read(CurtainWallReadRequest request)
        {
            Asked = request;
            if (_failure is not null) return Result.Failure<CurtainWallObservationSet>(_failure);

            var zone = new CurtainWallZoneObservation(ZoneA, "A 區",
                new[] { Loop(-2000, 1, WallMm + 2000, 20000) });

            if (_split) return Result.Success(Split(request, zone));
            if (_throughLevel) return Result.Success(ThroughLevel(request, zone));

            // 交接帶（交點 5000 左右各 900 mm）那一段立面不鋪嵌板，改以一道實體外牆表達——決議 13
            // 起 CW-H 的但書長度只由它供給（帷幕牆規格 §4.2「建模要求」）。4000–4500 留一片實板，
            // 它落在帶內：帶內嵌板要從 第79條之4 扣掉，也要在未符合時被標示（§7.1）。
            var wall = new CurtainWallObservation("CW-right", new Point2D(0, 0), new Point2D(WallMm, 0),
                new Point2D(0, -1), OffsetMm, 0, StoreyMm,
                new[]
                {
                    Panel("P-glass-left", 0, 4000, 30),
                    Panel("P2-panel", 4000, 4500, _bandMinutes, CurtainPanelKind.Solid),
                    Panel("P-glass-right", 6000, WallMm, 30)
                },
                typeName: "帷幕牆");

            var host = new CompartmentWallObservation("W1-bottom",
                new Point2D(5000, 3000), new Point2D(5000, -OffsetMm), 0, StoreyMm,
                request.LegalReferenceOf("W1-bottom"), request.RequiredRatingOf("W1-bottom"));

            var facade = new FacadeWallObservation("W-facade", new Point2D(4500, 0), new Point2D(6000, 0),
                0, StoreyMm, "RC 牆 15cm",
                ProvidedFireRating.Rated(_bandMinutes, _bandMinutes.ToString("0")));

            return Result.Success(new CurtainWallObservationSet(request.PackageId, "level-1F", "1F", 0,
                new[] { zone }, new[] { wall }, new[] { host }, facadeWalls: new[] { facade },
                levelElevationsMm: new[] { 0.0, StoreyMm }));
        }

        /// <summary>
        /// §4.2「建模要求」的立面（決議 14）：防火帶以一道 900 mm 的實體外牆取代該段帷幕牆，帷幕牆
        /// 因此是兩片，交點 5000 落在兩片之間。兩片都求得到這個交點，檢討表只能有一列。
        /// </summary>
        private CurtainWallObservationSet Split(CurtainWallReadRequest request, CurtainWallZoneObservation zone)
        {
            var left = new CurtainWallObservation("CW-split-left", new Point2D(4550, 0), new Point2D(0, 0),
                new Point2D(0, -1), OffsetMm, 0, StoreyMm, new[] { Panel("P-glass-left", 0, 4550, 30) }, typeName: "帷幕牆");

            var right = new CurtainWallObservation("CW-split-right", new Point2D(5450, 0), new Point2D(WallMm, 0),
                new Point2D(0, -1), OffsetMm, 0, StoreyMm, new[] { Panel("P-glass-right", 0, WallMm - 5450, 30) },
                typeName: "帷幕牆");

            var host = new CompartmentWallObservation("W1-bottom",
                new Point2D(5000, 3000), new Point2D(5000, -OffsetMm), 0, StoreyMm,
                request.LegalReferenceOf("W1-bottom"), request.RequiredRatingOf("W1-bottom"));

            var facade = new FacadeWallObservation("W-facade", new Point2D(4550, 0), new Point2D(5450, 0),
                0, StoreyMm, "RC 牆 15cm",
                ProvidedFireRating.Rated(_bandMinutes, _bandMinutes.ToString("0")));

            return new CurtainWallObservationSet(request.PackageId, "level-1F", "1F", 0,
                new[] { zone }, new[] { left, right }, new[] { host }, facadeWalls: new[] { facade },
                levelElevationsMm: new[] { 0.0, StoreyMm });
        }

        /// <summary>
        /// 一道自下一層起建、穿過本層標高的玻璃帷幕牆，本層沒有任何區劃樓地板與它交接（docs §3.4）。
        /// 是挑空還是樓板沒建，只有後方區劃的用途說得出來。
        /// </summary>
        private static CurtainWallObservationSet ThroughLevel(CurtainWallReadRequest request, CurtainWallZoneObservation zone)
        {
            var wall = new CurtainWallObservation("CW-through", new Point2D(0, 0), new Point2D(WallMm, 0),
                new Point2D(0, -1), OffsetMm, -StoreyMm, StoreyMm,
                new[]
                {
                    new CurtainPanelObservation("P-through", 0, WallMm, -StoreyMm, StoreyMm,
                        ProvidedFireRating.Missing("玻璃嵌板不填構造時效"), false, ProvidedFireProtection.Yes("是"),
                        kind: CurtainPanelKind.Glazed)
                },
                typeName: "帷幕牆");

            return new CurtainWallObservationSet(request.PackageId, "level-1F", "1F", 0,
                new[] { zone }, new[] { wall },
                levelElevationsMm: new[] { -StoreyMm, 0.0, StoreyMm });
        }

        /// <summary>
        /// 決議 16：玻璃嵌板宣告 <c>玻璃</c> 並以 <c>防火檢討_設計防火保護</c> 作答（認可之防火玻璃），
        /// 實板宣告 <c>實心</c> 並以設計防火時效作答。兩種都在這個 fixture 裡出現過。
        /// </summary>
        private static CurtainPanelObservation Panel(
            string uniqueId,
            double startMm,
            double endMm,
            double minutes,
            CurtainPanelKind kind = CurtainPanelKind.Glazed) =>
            new(uniqueId, startMm, endMm, 0, StoreyMm, ProvidedFireRating.Rated(minutes, minutes.ToString("0")),
                false,
                kind == CurtainPanelKind.Glazed ? ProvidedFireProtection.Yes("是") : null,
                kind: kind);

        private static IReadOnlyList<Point2D> Loop(double x0, double y0, double x1, double y1) =>
            new[] { new Point2D(x0, y0), new Point2D(x1, y0), new Point2D(x1, y1), new Point2D(x0, y1) };
    }

    [Fact]
    public void The_curtain_wall_read_names_every_compartment_boundary_with_what_the_rules_required_of_it()
    {
        var reader = new CurtainWalls();
        Run(Request(curtainWalls: reader));

        var asked = reader.Asked!;
        Assert.Equal("area-plan", asked.AreaPlanUniqueId);

        // 第70條 requires 1 hr of a 承重牆壁 on this storey and 2 hr of the 樓地板; the junction is
        // measured against the same numbers, and against the most onerous one where a host bounds two 區劃.
        Assert.Equal(60, asked.RequiredRatingOf("W1-bottom"));
        Assert.Equal(120, asked.RequiredRatingOf("F1-slab"));

        // Naming a host is what has it read as a 區劃 boundary, so both dictionaries say which clause.
        Assert.Equal(CurtainWallJunctionReferences.Article79, asked.LegalReferenceOf("W1-bottom"));
        Assert.Equal(CurtainWallJunctionReferences.Article79_3, asked.LegalReferences["F1-slab"]);

        // A wall inside a 區劃 is no boundary and is not read as one (docs §3.1).
        Assert.DoesNotContain("W3-partition", asked.LegalReferences.Keys);
        Assert.DoesNotContain("W3-partition", asked.RequiredFireRatingMinutes.Keys);
    }

    [Theory]
    [InlineData(ZoneUses.Atrium, ReviewStatus.NotApplicable)]
    [InlineData(ZoneUses.Stairwell, ReviewStatus.NotApplicable)]
    [InlineData("辦公", ReviewStatus.ManualReview)]
    [InlineData(null, ReviewStatus.ManualReview)]
    public void A_curtain_wall_running_through_a_level_with_no_floor_is_handed_to_article_79_2_only_behind_a_vertical_compartment(
        string? use, ReviewStatus expected)
    {
        // 複審 C-1：「不適用」只給有正面證據的情況——後方區劃的用途標示為垂直區劃。用途是一般用途或沒標示時，
        // 同一道牆可能只是樓板沒建，交人工覆核。
        var parameters = new Parameters();
        if (use is not null) parameters.Elements["area-a"][ReviewInputSources.ZoneUse] = ParameterReading.OfText(use);

        var outcome = Run(Request(parameters: parameters, curtainWalls: new CurtainWalls(throughLevel: true)));
        var section = outcome.Table!.Section(ReviewCheckTypes.CompartmentContinuity);

        var spandrel = Assert.Single(section.Entries, e => e.JunctionKind == CurtainWallJunctionKind.FloorToCurtainWall);
        Assert.Equal(expected, spandrel.EffectiveStatus);
        Assert.Equal(
            ReviewValue.OfText(expected == ReviewStatus.NotApplicable ? "VerticalCompartmentSpace" : "FloorNotMeetingCurtainWall"),
            spandrel.Result.Evidence.Find("junction.doubt"));
    }

    [Fact]
    public void Curtain_wall_junctions_are_the_fourth_row_of_the_review_table()
    {
        var outcome = Run(Request(curtainWalls: new CurtainWalls()));
        var section = outcome.Table!.Section(ReviewCheckTypes.CompartmentContinuity);

        // 交接帶 900 mm 以上且達要求時效 → 得免突出（案例 3）; the remaining glazing is 第79條之4's 其他部分.
        var wall = Assert.Single(section.Entries, e => e.JunctionKind == CurtainWallJunctionKind.WallToCurtainWall);
        Assert.Equal(ReviewStatus.NotApplicable, wall.EffectiveStatus);
        Assert.Equal(CurtainWallJunctionReferences.Article79, wall.JunctionLegalReference);
        Assert.Equal("帷幕牆區劃交接（水平）", wall.CategoryLabel);

        // 決議 16：帶外剩下的是玻璃嵌板，它以認可之防火設備作答，不是以分鐘數。
        var other = Assert.Single(section.Entries, e => e.JunctionKind == CurtainWallJunctionKind.CurtainPanelOther);
        Assert.Equal(ReviewStatus.Pass, other.EffectiveStatus);
        Assert.Equal("tw-bcr-79-4-curtain-wall-other-glazed", other.RuleId);
        Assert.Equal(ReviewStatus.Pass, section.Status);
        Assert.Empty(outcome.Table.OtherEntries);
        Assert.Equal(new[] { "帷幕牆區劃交接（水平）：第79條" },
            section.GroupsBy(ReviewTableGrouping.JunctionLegalReference).Select(g => g.Label));
    }

    [Fact]
    public void Case26_a_fire_band_built_as_a_solid_wall_between_two_curtain_walls_is_one_exempt_row()
    {
        // 決議 14：交點落在兩片帷幕牆之間的實體外牆上。兩片都求得到它，檢討表只能有一列，而且它是
        // 「得免突出」而不是人工覆核——實機在決議 14 之前兩片各出一列 ManualReview。
        var outcome = Run(Request(curtainWalls: new CurtainWalls(split: true)));
        var section = outcome.Table!.Section(ReviewCheckTypes.CompartmentContinuity);

        var wall = Assert.Single(section.Entries, e => e.JunctionKind == CurtainWallJunctionKind.WallToCurtainWall);
        Assert.Equal(ReviewStatus.NotApplicable, wall.EffectiveStatus);

        var evidence = outcome.Run!.Results.Single(r => r.ResultId == wall.ResultId).Evidence;
        Assert.Equal(ReviewValue.Quantity(0.9, ReviewUnit.Meter), evidence.Find("junction.continuousFireRatedLength"));
        Assert.Equal(ReviewValue.OfText("W-facade"), evidence.Find("junction.facadeWallUniqueIds"));
        Assert.Equal(ReviewValue.OfText("CW-H:CW-split-left:W1-bottom"), evidence.Find("junction.id"));
    }

    [Fact]
    public void A_curtain_wall_read_that_fails_stops_the_whole_run()
    {
        var error = new Error("curtainWall.areaPlanMissing", "找不到此檢討封包的 Area Plan。", "areaPlanUniqueId=area-plan");
        var outcome = Run(Request(curtainWalls: new CurtainWalls(failure: error)));

        Assert.Equal(FireReviewOutcomeKind.Failed, outcome.Kind);
        Assert.Null(outcome.Run);
        Assert.Equal(error.Code, outcome.Error!.Code);
        Assert.Contains(outcome.Log.Entries, e => e.UserMessage.StartsWith("帷幕牆區劃交接無法檢討"));
    }

    [Fact]
    public void Changing_a_panel_types_rating_makes_the_stored_run_stale_and_the_next_one_say_the_new_value()
    {
        // 案例 20。The panel Type's 設計防火時效 is no check's input — the 帷幕牆 reader reads it — but the
        // evidence baseline records it, so the stored run knows the model moved under it (spec 13.1).
        var before = new Parameters();
        before.Elements["type-panel"] = new Dictionary<string, ParameterReading>
        {
            [FireRatingParameters.Provided] = ParameterReading.OfText("60 min")
        };
        var first = Run(Request(parameters: before, openings: WithPanelType(), curtainWalls: new CurtainWalls()));
        var junction = first.Table!.Section(ReviewCheckTypes.CompartmentContinuity).Entries
            .Single(e => e.JunctionKind == CurtainWallJunctionKind.WallToCurtainWall);
        Assert.Equal(ReviewStatus.NotApplicable, junction.EffectiveStatus);

        var after = new Parameters();
        after.Elements["type-panel"] = new Dictionary<string, ParameterReading>
        {
            [FireRatingParameters.Provided] = ParameterReading.OfText("20 min")
        };
        var inspection = StoredRunInspection.Inspect(first.Package, first.Run!,
            CurrentBaseline(after, openings: WithPanelType()), RuleSetId, ShippedVersion, Now);

        Assert.True(inspection.Freshness.IsStale);
        Assert.Contains("P2-panel", inspection.Freshness.ChangedSubjects);
        Assert.Contains(inspection.Freshness.StaleResultIds, id => id == junction.ResultId);
        Assert.Equal(ReviewPackageStatus.Stale, inspection.Package.Status);
        Assert.Equal(ReviewVerdict.NeedsUpdate, inspection.Table.Verdict);

        // 20 min 達不到區劃牆要求的 60 min，交接帶湊不到 900 mm，且樓板不突出 → 未符合，反映新值。
        var second = Run(Request(parameters: after, openings: WithPanelType(), previous: first.Run,
            curtainWalls: new CurtainWalls(bandMinutes: 20)), prefix: 2);
        var again = second.Table!.Section(ReviewCheckTypes.CompartmentContinuity).Entries
            .Single(e => e.JunctionKind == CurtainWallJunctionKind.WallToCurtainWall);
        Assert.Equal(ReviewStatus.Fail, again.EffectiveStatus);
    }

    // --- 垂直區劃 (垂直區劃規格 §12 步驟 5) -------------------------------------------------------

    private const string ShaftDoorType = "type-shaft-door";

    /// <summary>
    /// A 管道間's 維修門 on the boundary of A 區. The fixture's whole opening list is replaced rather
    /// than added to: 第79條之2 holds every 防火設備 of the 區劃 to its requirements, so the other ten
    /// openings would bury the two results these tests are about.
    /// </summary>
    private static IReadOnlyList<OpeningObservation> ShaftDoorOnly() => new[]
    {
        new OpeningObservation(Source("D9-shaft"), CandidateCategory.Door, "W2-shared", P(10, 7),
            M(0.9), M(2.1), ShaftDoorType, "維修門")
    };

    /// <summary>A 區 is 管道間, and its 維修門 Type declares 遮煙性能 and 設計防火時效.</summary>
    private static Parameters ShaftParameters(int smokeSeal = 1, string rating = "1 小時")
    {
        var parameters = new Parameters();
        parameters.Elements["area-a"][ReviewInputSources.ZoneUse] = ParameterReading.OfText(ZoneUses.Shaft);
        parameters.Elements[ShaftDoorType] = new Dictionary<string, ParameterReading>
        {
            [SmokeProtectionParameters.Provided] = ParameterReading.OfYesNo(smokeSeal),
            [FireRatingParameters.Provided] = ParameterReading.OfText(rating)
        };
        return parameters;
    }

    /// <summary>
    /// The check is wired into the run: one 維修門 of a 管道間 yields the two results 第79條之2第1項第3句
    /// asks of it, counted in the fifth row of the 檢討表 as two of its three requirement lines.
    /// </summary>
    [Fact]
    public void A_shaft_maintenance_door_is_two_results_of_the_fifth_review_table_row()
    {
        var outcome = Run(Request(parameters: ShaftParameters(), openings: ShaftDoorOnly()));

        Assert.True(outcome.IsCompleted, outcome.Message);
        var section = outcome.Table!.Section(ReviewCheckTypes.VerticalCompartment);
        Assert.Equal(2, section.Entries.Count);
        Assert.All(section.Entries, e => Assert.Equal(ReviewStatus.Pass, e.EffectiveStatus));
        Assert.All(section.Entries, e => Assert.Equal(new[] { "D9-shaft" }, e.LocateUniqueIds));
        Assert.All(section.Entries, e => Assert.Contains("第79條之2", e.LegalReference, StringComparison.Ordinal));
        Assert.Equal(ReviewStatus.Pass, section.Status);
        Assert.Empty(outcome.Table.OtherEntries);

        Assert.Equal(new[] { "管道間維修門防火時效", "管道間維修門遮煙性能" },
            section.GroupsBy(ReviewTableGrouping.ShaftRequirement).Select(g => g.Label));

        // 昇降機道 is not this storey's business, nor is 挑空, so their rows count nothing — and the log
        // still says so, because an empty row and an unreviewed one read alike without the count.
        Assert.Contains(outcome.Log.Entries, e =>
            e.UserMessage.Contains("垂直區劃（第79條之2）") &&
            e.UserMessage.Contains("昇降機道防火設備遮煙性能 0 件未檢討") &&
            e.UserMessage.Contains("管道間維修門防火時效 1 件符合") &&
            e.UserMessage.Contains("挑空免除（第3項） 0 件未檢討"));
    }

    /// <summary>
    /// The two requirements are answered apart: a 維修門 that seals against smoke but falls short of the
    /// hour fails the 時效 row alone (決議 12 — one subject carries one field).
    /// </summary>
    [Fact]
    public void A_maintenance_door_short_of_an_hour_fails_only_the_rating_row()
    {
        var outcome = Run(Request(parameters: ShaftParameters(rating: "30 min"), openings: ShaftDoorOnly()));
        var section = outcome.Table!.Section(ReviewCheckTypes.VerticalCompartment);

        Assert.Equal(ReviewStatus.Fail,
            section.Entries.Single(e => e.ShaftRequirement == VerticalCompartmentRequirement.ShaftDoorRating).EffectiveStatus);
        Assert.Equal(ReviewStatus.Pass,
            section.Entries.Single(e => e.ShaftRequirement == VerticalCompartmentRequirement.ShaftDoorSmokeSeal).EffectiveStatus);
        Assert.Equal(ReviewVerdict.Fail, outcome.Table.Verdict);
    }

    /// <summary>
    /// 垂直區劃規格 §9 第9項: 遮煙性能 is a Type parameter no other check reads, so the evidence baseline
    /// has to record it itself — otherwise unticking the box would leave the stored 檢討 reading 有效.
    /// </summary>
    [Fact]
    public void Unticking_a_types_smoke_seal_makes_the_stored_run_need_an_update()
    {
        var first = Run(Request(parameters: ShaftParameters(), openings: ShaftDoorOnly()));
        var smokeSeal = first.Table!.Section(ReviewCheckTypes.VerticalCompartment).Entries
            .Single(e => e.ShaftRequirement == VerticalCompartmentRequirement.ShaftDoorSmokeSeal);
        Assert.Equal(ReviewStatus.Pass, smokeSeal.EffectiveStatus);

        var inspection = StoredRunInspection.Inspect(first.Package, first.Run!,
            CurrentBaseline(ShaftParameters(smokeSeal: 0), openings: ShaftDoorOnly()), RuleSetId, ShippedVersion, Now);

        Assert.True(inspection.Freshness.IsStale);
        Assert.Equal(new[] { "D9-shaft" }, inspection.Freshness.ChangedSubjects);
        Assert.Contains(inspection.Freshness.StaleResultIds, id => id == smokeSeal.ResultId);
        Assert.Equal(ReviewVerdict.NeedsUpdate, inspection.Table.Verdict);

        // And the next run says the new value: 遮煙性能 否 with no answer about a 昇降機間 is 未符合 for a
        // 管道間's 維修門, whose 但書 belongs to 昇降機道 alone.
        var second = Run(Request(parameters: ShaftParameters(smokeSeal: 0), openings: ShaftDoorOnly(), previous: first.Run), prefix: 2);
        Assert.Equal(ReviewStatus.Fail, second.Table!.Section(ReviewCheckTypes.VerticalCompartment).Entries
            .Single(e => e.ShaftRequirement == VerticalCompartmentRequirement.ShaftDoorSmokeSeal).EffectiveStatus);
    }

    /// <summary>
    /// 垂直區劃規格 §9 第9項 again, for the other half of the pair: a 門's 設計防火時效 is read by no
    /// member check either — a 維修門 is an opening — so the baseline has to record it here as well.
    /// </summary>
    [Fact]
    public void Changing_a_maintenance_doors_rating_makes_the_stored_run_need_an_update()
    {
        var first = Run(Request(parameters: ShaftParameters(), openings: ShaftDoorOnly()));

        var inspection = StoredRunInspection.Inspect(first.Package, first.Run!,
            CurrentBaseline(ShaftParameters(rating: "30 min"), openings: ShaftDoorOnly()), RuleSetId, ShippedVersion, Now);

        Assert.True(inspection.Freshness.IsStale);
        Assert.Equal(new[] { "D9-shaft" }, inspection.Freshness.ChangedSubjects);
    }

    /// <summary>
    /// 垂直區劃規格 §9 第10項: a 區劃 whose 用途 says nothing 第79條之2 names produces no subject at all,
    /// so the row stays 未檢討 and the run is not held up by the 遮煙性能 of doors nobody asked about.
    /// </summary>
    [Fact]
    public void A_zone_use_outside_the_vocabulary_produces_no_vertical_compartment_subject()
    {
        var parameters = ShaftParameters();
        parameters.Elements["area-a"][ReviewInputSources.ZoneUse] = ParameterReading.OfText("機房");

        var outcome = Run(Request(parameters: parameters, openings: ShaftDoorOnly()));

        Assert.Equal(ReviewStatus.NotRun, outcome.Table!.Section(ReviewCheckTypes.VerticalCompartment).Status);
        Assert.DoesNotContain(outcome.Log.Entries, e => e.UserMessage.Contains("垂直區劃（第79條之2）"));
    }

    // --- 第79條之2第3項 的事實進來 (垂直區劃規格 §3.8、§6，決議 35) ------------------------------

    private static readonly Guid LowerPackageId = Guid.Parse("22222222-0000-0000-0000-000000000001");
    private static readonly Guid LowerZoneId = Guid.Parse("22222222-0000-0000-0000-0000000000a1");

    /// <summary>A 區 is the 挑空 on 2F (the package's storey); 避難層通達 is the one fact typed.</summary>
    private static Parameters AtriumParameters(int linksRefugeFloor = 1)
    {
        var parameters = new Parameters();
        parameters.Elements["area-a"][ReviewInputSources.ZoneUse] = ParameterReading.OfText(ZoneUses.Atrium);
        parameters.Elements["area-a"][ReviewInputSources.FloorNumber] = ParameterReading.OfInteger(2);
        parameters.Elements["area-a"][ReviewInputSources.LinksRefugeFloor] = ParameterReading.OfYesNo(linksRefugeFloor);
        return parameters;
    }

    /// <summary>
    /// The storeys around it: on 2F the 挑空 A 區 and B 區 beside it, its 所在區劃; on 1F one 區劃
    /// under both. 連跨 2 層, 起始樓層 1F, 連通區劃面積 = B 區 + 1F 區劃.
    /// </summary>
    private static ReviewModelFacts AtriumStoreys(double surroundingSquareMeters = 300, double belowSquareMeters = 600) =>
        ReviewModelFacts.None.WithStoreys(new StoreyZoneMap(new[]
        {
            new StoreyZone(PackageId, ZoneA, "level-1F", 10, "2F", "A 區", ZoneUses.Atrium, 2,
                PlanUnits.SquareMetersToSquareFeet(100), new[] { Rect(0, 0, 10, 10) }, new[] { "area-a" }),
            new StoreyZone(PackageId, ZoneB, "level-1F", 10, "2F", "B 區", "辦公", 2,
                PlanUnits.SquareMetersToSquareFeet(surroundingSquareMeters), new[] { Rect(10, 0, 20, 10) }, new[] { "area-b" }),
            new StoreyZone(LowerPackageId, LowerZoneId, "level-below", 0, "1F", "大廳", "辦公", 1,
                PlanUnits.SquareMetersToSquareFeet(belowSquareMeters), new[] { Rect(0, 0, 20, 10) }, new[] { "area-1f" })
        }));

    /// <summary>The zone inputs assembled for area-a, which is the 挑空 in these fixtures.</summary>
    private static IReadOnlyList<ReviewInput> AtriumZoneInputs(Parameters parameters, ReviewModelFacts? model = null)
    {
        var set = Set();
        var inputs = ReviewInputAssembler.Assemble(set, parameters.Snapshot(), model: model);
        var zone = set.Zones.Single(z => z.AreaUniqueIds.Contains("area-a"));
        return inputs.Area.ForZone(zone.ZoneId).ToList();
    }

    /// <summary>
    /// 決議 35: 連跨樓層數, 起始樓層序 and 連通區劃面積 are traced through the storeys and reach the
    /// 區劃's inputs like any other zone fact, each naming where it came from; 避難層通達 is still read
    /// off the Area.
    /// </summary>
    [Fact]
    public void The_traced_facts_and_the_refuge_box_reach_the_zone_inputs()
    {
        var inputs = AtriumZoneInputs(AtriumParameters(linksRefugeFloor: 1), AtriumStoreys());

        var spanned = inputs.Single(i => i.Field == "zone.spannedFloors");
        Assert.Equal(ReviewValue.Quantity(2, ReviewUnit.None), spanned.Value);
        Assert.StartsWith("由跨樓層區劃推得", spanned.Source, StringComparison.Ordinal);
        Assert.Contains("1F「大廳」 600 ㎡", spanned.Source, StringComparison.Ordinal);
        Assert.Contains("2F「B 區」 300 ㎡", spanned.Source, StringComparison.Ordinal);

        Assert.Equal(ReviewValue.Quantity(1, ReviewUnit.None), inputs.Single(i => i.Field == "zone.atriumBaseFloor").Value);
        Assert.Equal(900, inputs.Single(i => i.Field == "zone.connectedArea").Value!.Number, 6);

        var links = inputs.Single(i => i.Field == "zone.linksRefugeFloor");
        Assert.Equal(ReviewValue.OfBoolean(true), links.Value);
        Assert.Equal("面積：" + ReviewInputSources.LinksRefugeFloor, links.Source);
    }

    /// <summary>Without the other storeys nothing is traced, and nothing is guessed in its place.</summary>
    [Fact]
    public void Without_the_other_storeys_the_traced_facts_are_simply_missing()
    {
        var inputs = AtriumZoneInputs(AtriumParameters());

        Assert.DoesNotContain(inputs, i => i.Field == "zone.spannedFloors");
        Assert.DoesNotContain(inputs, i => i.Field == "zone.connectedArea");
    }

    /// <summary>
    /// Storeys that could not be read are a reason, not a blank: the three traced facts are 資料不足
    /// that says why, so nobody goes looking for a parameter that no longer exists.
    /// </summary>
    [Fact]
    public void Storeys_that_could_not_be_read_leave_a_reason_rather_than_a_blank()
    {
        var inputs = AtriumZoneInputs(AtriumParameters(), ReviewModelFacts.None.WithStoreysUnavailable("找不到此工作包的 Area Plan"));

        foreach (var field in new[] { "zone.spannedFloors", "zone.atriumBaseFloor", "zone.connectedArea" })
        {
            var input = inputs.Single(i => i.Field == field);
            Assert.True(input.IsUnreadable);
            Assert.Contains("Area Plan", input.UnreadableReason, StringComparison.Ordinal);
        }
    }

    /// <summary>Only a 挑空 is traced: B 區 gets none of the three facts.</summary>
    [Fact]
    public void Only_an_atrium_is_traced()
    {
        var set = Set();
        var inputs = ReviewInputAssembler.Assemble(set, AtriumParameters().Snapshot(), model: AtriumStoreys());

        Assert.DoesNotContain(inputs.Area.ForZone(ZoneB), i => i.Field == "zone.spannedFloors");
    }

    /// <summary>
    /// The Yes/No half needs no such treatment: 否 is an answer, and an unticked box is the one the
    /// stricter way — it settles 第一款 as not holding instead of leaving the 挑空 exempt.
    /// </summary>
    [Fact]
    public void An_unticked_refuge_floor_box_is_no_rather_than_nothing_stated()
    {
        var links = AtriumZoneInputs(AtriumParameters(linksRefugeFloor: 0))
            .Single(i => i.Field == "zone.linksRefugeFloor");

        Assert.Equal(ReviewValue.OfBoolean(false), links.Value);
    }

    /// <summary>
    /// The evidence baseline records every zone input, and the traced facts are zone inputs — so a
    /// 區劃 changed on <em>another</em> storey moves this 區劃's fingerprint and the stored 檢討 reads
    /// 需更新 (spec 13.1), although nothing on this package's storey moved.
    /// </summary>
    [Fact]
    public void Changing_a_zone_on_another_storey_makes_the_stored_run_need_an_update()
    {
        var first = Run(Request(parameters: AtriumParameters(), model: AtriumStoreys()));

        var inspection = StoredRunInspection.Inspect(first.Package, first.Run!,
            CurrentBaseline(AtriumParameters(), model: AtriumStoreys(belowSquareMeters: 800)), RuleSetId, ShippedVersion, Now);

        Assert.True(inspection.Freshness.IsStale);
        Assert.All(inspection.Freshness.ChangedSubjects, s => Assert.True(ReviewBaselineKeys.IsZone(s)));

        // And the typed half of 第3項's facts.
        Assert.True(StoredRunInspection.Inspect(first.Package, first.Run!,
            CurrentBaseline(AtriumParameters(linksRefugeFloor: 0), model: AtriumStoreys()), RuleSetId, ShippedVersion, Now).Freshness.IsStale);
    }

    // --- 第79條之2第3項 的結果 (垂直區劃規格 §7.3、§12 步驟 7d) ---------------------------------

    /// <summary>
    /// The judgement is wired into the run: one 挑空 is one result, counted in the fourth requirement
    /// row of the fifth 檢討表 row. 免除成立 is 符合 (決議 39): its 面積 goes back to the 區劃面積 rules
    /// (決議 32), its separation to the boundary walls and 防火設備 of its 區劃 (決議 38).
    /// </summary>
    [Fact]
    public void An_exempt_atrium_is_one_pass_in_the_fourth_requirement_row()
    {
        var outcome = Run(Request(parameters: AtriumParameters(), openings: ShaftDoorOnly(), model: AtriumStoreys()));

        Assert.True(outcome.IsCompleted, outcome.Message);
        var section = outcome.Table!.Section(ReviewCheckTypes.VerticalCompartment);
        var entry = Assert.Single(section.Entries);

        Assert.Equal(VerticalCompartmentRequirement.AtriumExemption, entry.ShaftRequirement);
        Assert.Equal("挑空免除（第3項）", entry.ShaftRequirementLabel);
        Assert.Equal(ReviewStatus.Pass, entry.EffectiveStatus);
        Assert.Equal(new[] { "area-a" }, entry.LocateUniqueIds);
        Assert.Equal("建築技術規則建築設計施工編第79條之2第3項（挑空得不受第1項限制）", entry.LegalReference);
        Assert.DoesNotContain("第83條", entry.LegalReference, StringComparison.Ordinal);

        // Its subject is a 區劃, so the table shows no element category and no Type — like 區劃面積.
        Assert.Equal("區劃", entry.CategoryLabel);
        Assert.Null(entry.TypeKey);

        Assert.Equal(new[] { "挑空免除（第3項）" },
            section.GroupsBy(ReviewTableGrouping.ShaftRequirement).Select(g => g.Label));
        Assert.Contains(outcome.Log.Entries, e =>
            e.UserMessage.Contains("垂直區劃（第79條之2）") &&
            e.UserMessage.Contains("挑空免除（第3項） 1 件符合") &&
            e.UserMessage.Contains("管道間維修門防火時效 0 件未檢討"));
    }

    /// <summary>
    /// 決議 32, end to end: the same run hands the exempt 挑空's 面積 back to the 區劃面積 row — the
    /// 連通區劃面積 traced through the storeys, held to 第79條 — instead of the unconditional use
    /// exemption. A 區劃 on 1F growing then makes the stored 檢討 需更新, like any other zone input.
    /// </summary>
    [Fact]
    public void An_exempt_atriums_connected_total_goes_back_to_the_area_row()
    {
        var outcome = Run(Request(parameters: AtriumParameters(), openings: ShaftDoorOnly(), model: AtriumStoreys()));

        Assert.True(outcome.IsCompleted, outcome.Message);
        var area = ResultOf(outcome.Run!, ReviewCheckTypes.CompartmentArea, "area-a", ZoneA);
        Assert.Equal("tw-bcr-79-area-atrium", area.RuleId);
        Assert.Equal(ReviewValue.Quantity(900, ReviewUnit.SquareMeter), area.ActualValue);
        Assert.NotEqual(ReviewStatus.NotApplicable, area.Status);

        var inspection = StoredRunInspection.Inspect(outcome.Package, outcome.Run!,
            CurrentBaseline(AtriumParameters(), model: AtriumStoreys(belowSquareMeters: 1100)), RuleSetId, ShippedVersion, Now);
        Assert.True(inspection.Freshness.IsStale);
    }

    /// <summary>
    /// 決議 37: once 第3項 holds, A 區 (the 挑空) and B 區 are one 連通區劃, so W2-shared between them is
    /// no 區劃牆 and its door D1-shared need be no 防火設備. W1-bottom only runs along both on the
    /// outside, and stays a boundary — so does the window in it.
    /// </summary>
    [Fact]
    public void The_line_between_an_exempt_atrium_and_its_connected_zone_is_no_compartment_boundary()
    {
        var outcome = Run(Request(parameters: AtriumParameters(), model: AtriumStoreys()));
        Assert.True(outcome.IsCompleted, outcome.Message);

        var door = ResultOf(outcome.Run!, ReviewCheckTypes.OpeningProtection, "D1-shared", ZoneA);
        Assert.Equal(ReviewStatus.NotApplicable, door.Status);
        Assert.Contains(MergedAtriums.Note, door.Message, StringComparison.Ordinal);
        Assert.Equal(ReviewValue.OfBoolean(true), door.Evidence.Find(MergedAtriums.EvidenceField));
        Assert.Equal(ReviewStatus.NotApplicable, ResultOf(outcome.Run!, ReviewCheckTypes.OpeningProtection, "D1-shared", ZoneB).Status);

        var wall = ResultOf(outcome.Run!, ReviewCheckTypes.FireResistance, "W2-shared", ZoneA);
        Assert.Equal(ReviewValue.OfBoolean(true), wall.Evidence.Find(MergedAtriums.EvidenceField));

        var outside = ResultOf(outcome.Run!, ReviewCheckTypes.FireResistance, "W1-bottom", ZoneA);
        Assert.Null(outside.Evidence.Find(MergedAtriums.EvidenceField));
        Assert.NotEqual(ReviewStatus.NotApplicable, ResultOf(outcome.Run!, ReviewCheckTypes.OpeningProtection, "WN1-bottom", ZoneB).Status);
    }

    /// <summary>
    /// A 樓梯間 is 單獨區劃分隔 under 第1項 whatever the 挑空 beside it does, so the line between them
    /// stays a boundary even when the 挑空 is exempt.
    /// </summary>
    [Fact]
    public void The_line_between_an_exempt_atrium_and_a_stairwell_stays_a_compartment_boundary()
    {
        var parameters = AtriumParameters();
        parameters.Elements["area-b"][ReviewInputSources.ZoneUse] = ParameterReading.OfText(ZoneUses.Stairwell);

        var outcome = Run(Request(parameters: parameters, model: AtriumStoreys()));
        Assert.True(outcome.IsCompleted, outcome.Message);

        var door = ResultOf(outcome.Run!, ReviewCheckTypes.OpeningProtection, "D1-shared", ZoneA);
        Assert.Null(door.Evidence.Find(MergedAtriums.EvidenceField));
        Assert.NotEqual(ReviewStatus.NotApplicable, door.Status);
    }

    /// <summary>
    /// When 第3項 does not hold, 第1項 requires exactly that line: the 挑空 is 單獨區劃分隔, and its door
    /// must be a 防火設備.
    /// </summary>
    [Fact]
    public void The_line_around_an_atrium_that_is_not_exempt_stays_a_compartment_boundary()
    {
        var outcome = Run(Request(parameters: AtriumParameters(linksRefugeFloor: 0), model: AtriumStoreys(belowSquareMeters: 4000)));
        Assert.True(outcome.IsCompleted, outcome.Message);

        var door = ResultOf(outcome.Run!, ReviewCheckTypes.OpeningProtection, "D1-shared", ZoneA);
        Assert.NotEqual(ReviewStatus.NotApplicable, door.Status);
        Assert.Null(door.Evidence.Find(MergedAtriums.EvidenceField));
    }

    /// <summary>
    /// 決議 23: the 挑空's own 防火設備 are held to none of 第1項's three requirements, so the 維修門 in
    /// this fixture — which would be two results were A 區 a 管道間 — yields nothing but 第3項's result.
    /// </summary>
    [Fact]
    public void An_atriums_openings_are_held_to_none_of_the_first_paragraphs_requirements()
    {
        var outcome = Run(Request(parameters: AtriumParameters(), openings: ShaftDoorOnly(), model: AtriumStoreys()));

        Assert.DoesNotContain(outcome.Run!.Results,
            r => r.CheckType == ReviewCheckTypes.VerticalCompartment && r.SubjectUniqueIds.Contains("D9-shaft"));
    }

    /// <summary>
    /// 第3項 has no 未符合 to report, so it can never paint an element red (§3.6、決議 20) and can never
    /// turn the run's verdict into 未符合 by itself. (Its 免除成立 is 符合 since 決議 39.)
    /// </summary>
    [Fact]
    public void The_third_paragraph_never_fails_and_so_is_never_marked()
    {
        foreach (var model in new[] { AtriumStoreys(), AtriumStoreys(belowSquareMeters: 4000) })
        {
            var request = Request(parameters: AtriumParameters(linksRefugeFloor: 0),
                openings: ShaftDoorOnly(), model: model);
            var outcome = Run(request);
            var entry = Assert.Single(outcome.Table!.Section(ReviewCheckTypes.VerticalCompartment).Entries);

            Assert.NotEqual(ReviewStatus.Fail, entry.EffectiveStatus);

            var plan = ReviewMarkupPlan.Build(outcome.Table!, request.Candidates.Zones);
            Assert.True(plan.IsSuccess, plan.IsSuccess ? string.Empty : plan.Error.ToString());
            Assert.DoesNotContain(plan.Value.Overrides, o => o.ResultIds.Contains(entry.ResultId));
            Assert.DoesNotContain(plan.Value.Overrides,
                o => o.ShaftRequirements.Contains("AtriumExemption", StringComparer.Ordinal));
        }
    }

    /// <summary>
    /// 決議 27: 避難層通達 is not a required parameter. A project with no 挑空 must be able to start a
    /// review without binding it. The traced facts have no parameter at all (決議 35).
    /// </summary>
    [Fact]
    public void The_third_paragraphs_parameters_have_a_source_but_never_hold_a_review_up()
    {
        var source = ReviewInputSources.For("zone.linksRefugeFloor")!;
        Assert.NotNull(source);
        Assert.Equal(ReviewParameterLevel.Instance, source.Level);
        Assert.Equal(new[] { ReviewParameterHost.Areas }, source.Hosts);
        Assert.DoesNotContain("zone.linksRefugeFloor", ReviewInputSources.NeededBy(Rules()).Select(s => s.Field));

        foreach (var traced in new[] { "zone.spannedFloors", "zone.connectedArea", "zone.atriumBaseFloor" })
            Assert.Null(ReviewInputSources.For(traced));

        var parameters = AtriumParameters();
        parameters.Bindings.RemoveAll(b => b.Key == ReviewInputSources.LinksRefugeFloor);

        Assert.True(Readiness(parameters: parameters).CanRun);
    }
    // --- 第79條之1 的結果（第79條之1文件 §7.1、§12 步驟 3）---------------------------------------

    /// <summary>The fixture with A 區 declared an 觀眾席 that cannot be subdivided, in an Ａ－１組 building.</summary>
    private static Parameters AuditoriumParameters(int? cannotBeSubdivided = 1, string? buildingUse = "A-1")
    {
        var parameters = new Parameters();
        if (buildingUse is not null) parameters.Project[ReviewInputSources.BuildingUse] = ParameterReading.OfText(buildingUse);
        parameters.Elements["area-a"][ReviewInputSources.ZoneUse] = ParameterReading.OfText(ZoneUses.Auditorium);
        if (cannotBeSubdivided is int declared)
            parameters.Elements["area-a"][ReviewInputSources.CannotBeSubdivided] = ParameterReading.OfYesNo(declared);
        return parameters;
    }

    /// <summary>
    /// 第79條之1 wired end to end: 防火檢討_區劃用途＝觀眾席 with 防火檢討_無法區劃分隔＝是 in an Ａ－１組
    /// building gives A 區 a second row of its own — 人工覆核, naming what （丁） leaves to a person — on
    /// the same Areas the 區劃面積 row carries. B 區 has no 區劃用途 at all, so the article reaches it not
    /// at all and it gets no row (決議 10).
    /// </summary>
    [Fact]
    public void An_auditorium_that_cannot_be_subdivided_is_one_manual_review_beside_the_area_row()
    {
        var outcome = Run(Request(parameters: AuditoriumParameters()));

        Assert.True(outcome.IsCompleted, outcome.Message);
        var entry = Assert.Single(outcome.Table!.Section(ReviewCheckTypes.AreaExemption).Entries);

        Assert.Equal(ReviewStatus.ManualReview, entry.EffectiveStatus);
        Assert.Equal(new[] { "area-a" }, entry.LocateUniqueIds);
        Assert.Equal(Article79_1ExemptionCheck.LegalReference, entry.LegalReference);
        Assert.DoesNotContain("第83條", entry.LegalReference, StringComparison.Ordinal);
        Assert.Contains("符合第一款", entry.Message, StringComparison.Ordinal);
        Assert.Contains(Article79_1Exemption.PersonMustConfirm, entry.Message, StringComparison.Ordinal);

        // A 區劃 is no candidate category, so the row names the 區劃 and shows no Type (§5.1).
        Assert.Equal("區劃", entry.CategoryLabel);
        Assert.Equal("A 區", entry.ZoneName);
        Assert.Null(entry.TypeKey);
        Assert.Equal(new[] { "A 區" },
            outcome.Table.Section(ReviewCheckTypes.AreaExemption).GroupsBy(ReviewTableGrouping.Zone).Select(g => g.Label));

        // The same Areas as the 區劃面積 row, so both rows expand onto the same place in the plan.
        var area = ResultOf(outcome.Run!, ReviewCheckTypes.CompartmentArea, "area-a", ZoneA);
        Assert.Equal(area.SubjectUniqueIds, entry.Result.SubjectUniqueIds);
    }

    /// <summary>
    /// 決議 12, end to end: the 區劃面積 row of the very same run reads word for word as it does when
    /// A 區 is an 辦公 區劃. 第79條之1 adds a row; releasing the area is 人工覆寫 and nothing else (§3.7).
    /// </summary>
    [Fact]
    public void The_area_row_reads_the_same_whether_or_not_the_exemption_holds()
    {
        var withExemption = ResultOf(Run(Request(parameters: AuditoriumParameters())).Run!,
            ReviewCheckTypes.CompartmentArea, "area-a", ZoneA);
        var plain = ResultOf(Run(Request(), prefix: 2).Run!, ReviewCheckTypes.CompartmentArea, "area-a", ZoneA);

        Assert.Equal(plain.Status, withExemption.Status);
        Assert.Equal(plain.Message, withExemption.Message);
        Assert.Equal(plain.RuleId, withExemption.RuleId);
        Assert.Equal(plain.RuleVersion, withExemption.RuleVersion);
        Assert.Equal(plain.LegalReference, withExemption.LegalReference);
        Assert.Equal(plain.ActualValue, withExemption.ActualValue);
        Assert.Equal(plain.RequiredValue, withExemption.RequiredValue);
        Assert.DoesNotContain("第79條之1", withExemption.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The declaration is read from 防火檢討_無法區劃分隔 and from nowhere else: unbound or unticked, the
    /// exemption does not hold, and 未勾選 reads as 否 — 不適用, the strict direction (§9 第4項).
    /// </summary>
    [Fact]
    public void The_declaration_decides_the_exemption_and_a_missing_one_is_insufficient_data()
    {
        var unfilled = Assert.Single(Run(Request(parameters: AuditoriumParameters(cannotBeSubdivided: null)))
            .Table!.Section(ReviewCheckTypes.AreaExemption).Entries);
        Assert.Equal(ReviewStatus.InsufficientData, unfilled.EffectiveStatus);
        Assert.Contains("無法區劃分隔", unfilled.Message, StringComparison.Ordinal);

        var unticked = Assert.Single(Run(Request(parameters: AuditoriumParameters(cannotBeSubdivided: 0)), prefix: 2)
            .Table!.Section(ReviewCheckTypes.AreaExemption).Entries);
        Assert.Equal(ReviewStatus.NotApplicable, unticked.EffectiveStatus);
        Assert.Contains("第79條第1項照常適用", unticked.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// 決議 9: 防火檢討_無法區劃分隔 is not a required binding. No rule reads it — 第79條之1 does not go
    /// through the engine — so a project with no 觀眾席 must be able to start a review without it.
    /// </summary>
    [Fact]
    public void The_declaration_has_a_source_but_never_holds_a_review_up()
    {
        var source = ReviewInputSources.For("zone.cannotBeSubdivided")!;
        Assert.NotNull(source);
        Assert.Equal(ReviewParameterLevel.Instance, source.Level);
        Assert.Equal(new[] { ReviewParameterHost.Areas }, source.Hosts);
        Assert.DoesNotContain("zone.cannotBeSubdivided", ReviewInputSources.NeededBy(Rules()).Select(s => s.Field));

        var parameters = AuditoriumParameters();
        parameters.Bindings.RemoveAll(b => b.Key == ReviewInputSources.CannotBeSubdivided);

        Assert.True(Readiness(parameters: parameters).CanRun);
    }

    /// <summary>
    /// 第79條之1 has no 未符合 (§3.5), so it never paints anything and never issues a drawing number
    /// (§7.2). The 區劃 may well be painted red — by the 區劃面積 row, which is where that belongs.
    /// </summary>
    [Fact]
    public void The_exemption_is_never_marked_in_the_review_view()
    {
        foreach (var declared in new int?[] { 1, 0, null })
        {
            var request = Request(parameters: AuditoriumParameters(cannotBeSubdivided: declared));
            var outcome = Run(request);
            var entry = Assert.Single(outcome.Table!.Section(ReviewCheckTypes.AreaExemption).Entries);

            Assert.NotEqual(ReviewStatus.Fail, entry.EffectiveStatus);
            Assert.NotEqual(ReviewStatus.Pass, entry.EffectiveStatus);

            var plan = ReviewMarkupPlan.Build(outcome.Table!, request.Candidates.Zones);
            Assert.True(plan.IsSuccess, plan.IsSuccess ? string.Empty : plan.Error.ToString());
            Assert.DoesNotContain(plan.Value.Regions, r => r.ResultId == entry.ResultId);
            Assert.DoesNotContain(plan.Value.Overrides, o => o.ResultIds.Contains(entry.ResultId));
            Assert.False(plan.Value.Numbers.ContainsKey(entry.ResultId));
        }
    }

    // --- a stored run judged again (spec 13.1, 16.3 情境 8) -------------------------------------

    private static ReviewBaseline CurrentBaseline(
        Parameters parameters, string phase = "新建", IEnumerable<OpeningObservation>? openings = null,
        ReviewModelFacts? model = null)
    {
        var set = Set(openings: openings);
        var inputs = ReviewInputAssembler.Assemble(set, parameters.Snapshot(), model: model);

        // The same overload the run uses: a baseline built from less than the whole assembly gives a
        // different fingerprint and reads as 需更新 although nothing moved (ReviewBaselineBuilder.Build).
        return ReviewBaselineBuilder.Build(set, Environment(phase), inputs);
    }

    [Fact]
    public void Reopened_model_that_did_not_change_keeps_the_run_fresh()
    {
        var outcome = Run(Request());

        var inspection = StoredRunInspection.Inspect(outcome.Package, outcome.Run!, CurrentBaseline(new Parameters()), RuleSetId, ShippedVersion, Now);

        Assert.False(inspection.Freshness.IsStale);
        Assert.False(inspection.NeedsSaving);
        Assert.Equal(ReviewPackageStatus.Reviewed, inspection.Package.Status);
        Assert.NotEqual(ReviewVerdict.NeedsUpdate, inspection.Table.Verdict);
        Assert.True(inspection.Log.IsEmpty);
    }

    [Fact]
    public void Changed_parameter_makes_the_touched_results_stale_and_suspends_their_overrides()
    {
        var outcome = Run(Request());
        var door = ResultOf(outcome.Run!, ReviewCheckTypes.OpeningProtection, "WN1-bottom", ZoneB);
        var overridden = ReviewOverrides.Apply(outcome.Run!, door.ResultId, ReviewStatus.Pass, "現場已改為防火窗", "林建築師", Now).Value;

        var now = new Parameters();
        now.Elements["WN1-bottom"][FireProtectionParameters.Provided] = ParameterReading.OfText("是");
        var inspection = StoredRunInspection.Inspect(outcome.Package, overridden, CurrentBaseline(now), RuleSetId, ShippedVersion, Now);

        Assert.True(inspection.Freshness.IsStale);
        Assert.False(inspection.Freshness.InvalidatesAll);
        Assert.True(inspection.Freshness.IsResultStale(door.ResultId));
        Assert.True(inspection.RunChanged);
        Assert.Equal(ReviewOverrideStanding.NeedsReconfirmation, inspection.Run.CurrentOverrideFor(door.ResultId)!.Standing);
        Assert.True(inspection.PackageChanged);
        Assert.Equal(ReviewPackageStatus.Stale, inspection.Package.Status);
        Assert.Equal(ReviewVerdict.NeedsUpdate, inspection.Table.Verdict);
        Assert.Contains(inspection.Log.Entries, e => e.Code == ReviewErrorCode.OverrideNeedsReconfirmation);
    }

    [Fact]
    public void Rule_version_update_makes_every_old_result_stale()
    {
        var outcome = Run(Request());

        var inspection = StoredRunInspection.Inspect(outcome.Package, outcome.Run!, CurrentBaseline(new Parameters()), RuleSetId, "2027.0", Now);

        Assert.True(inspection.Freshness.InvalidatesAll);
        Assert.Equal(outcome.Run!.Results.Count, inspection.Freshness.StaleResultIds.Count);
        Assert.Contains(inspection.Freshness.Reasons, r => r.Contains("規則版本") && r.Contains("2027.0"));
        Assert.Equal(ReviewVerdict.NeedsUpdate, inspection.Table.Verdict);
        Assert.Equal(ReviewPackageStatus.Stale, inspection.Package.Status);
    }

    [Fact]
    public void Project_phase_change_invalidates_the_whole_run()
    {
        var outcome = Run(Request());
        var inspection = StoredRunInspection.Inspect(outcome.Package, outcome.Run!, CurrentBaseline(new Parameters(), phase: "拆除"), RuleSetId, ShippedVersion, Now);
        Assert.True(inspection.Freshness.InvalidatesAll);
    }

    [Fact]
    public void Rerun_after_accepting_the_new_rule_version_locks_it()
    {
        var locked = Package(ReviewPackageStatus.Stale, RuleSetId, "2025.9");
        var outcome = Run(Request(package: locked));

        Assert.Equal(ShippedVersion, outcome.Package.RuleSetVersion);
        Assert.Equal(ReviewPackageStatus.Reviewed, outcome.Package.Status);
    }

    // --- spec 15 performance --------------------------------------------------------------------

    [Fact]
    public void Performance_within_targets_is_logged_as_information()
    {
        var performance = new ReviewPerformance(1200, TimeSpan.FromSeconds(3),
            new[] { new KeyValuePair<string, TimeSpan>("CompartmentArea", TimeSpan.FromSeconds(2)) });
        var log = new ReviewLog.Builder(PackageId, Now);
        performance.AppendTo(log);

        Assert.False(performance.Exceeded);
        var entry = Assert.Single(log.Build().Entries);
        Assert.Equal(ReviewSeverity.Info, entry.Severity);
        Assert.Contains("prescan=3000ms", entry.TechnicalDetail);
    }

    [Fact]
    public void Performance_over_target_logs_a_diagnosis()
    {
        var slowPrescan = new ReviewPerformance(5000, TimeSpan.FromSeconds(11), null);
        var slowReview = new ReviewPerformance(100, TimeSpan.Zero,
            new[] { new KeyValuePair<string, TimeSpan>("FireResistance", TimeSpan.FromSeconds(31)) });
        Assert.True(slowPrescan.PrescanExceeded);
        Assert.True(slowReview.ReviewExceeded);

        var log = new ReviewLog.Builder(PackageId, Now);
        slowReview.AppendTo(log);
        var entry = Assert.Single(log.Build().Entries);
        Assert.Equal(ReviewErrorCode.PerformanceExceeded, entry.Code);
        Assert.Equal(ReviewSeverity.Warning, entry.Severity);
        Assert.Contains("FireResistance=31000ms", entry.TechnicalDetail);
    }

    [Fact]
    public void Performance_target_scales_above_five_thousand_candidates()
    {
        var large = new ReviewPerformance(10000, TimeSpan.FromSeconds(15),
            new[] { new KeyValuePair<string, TimeSpan>("all", TimeSpan.FromSeconds(50)) });

        Assert.Equal(TimeSpan.FromSeconds(20), large.PrescanLimit);
        Assert.Equal(TimeSpan.FromSeconds(60), large.ReviewLimit);
        Assert.False(large.Exceeded);
    }

    [Fact]
    public void Run_reports_the_candidate_count_and_prescan_time()
    {
        var outcome = Run(Request());
        var set = Set();
        Assert.Equal(set.Members.Count + set.Openings.Count + set.UnrelatedMemberCount + set.UnrelatedOpeningCount, outcome.Performance.CandidateCount);
        Assert.Equal(TimeSpan.FromSeconds(1), outcome.Performance.Prescan);
        Assert.Equal(7, outcome.Performance.Stages.Count);
    }

    // --- domain ---------------------------------------------------------------------------------

    [Fact]
    public void Package_records_the_review_without_moving_the_boundary_revision()
    {
        var runId = Guid.NewGuid();
        var package = Package(boundaryRevision: 3).WithReviewRun(RuleSetId, ShippedVersion, runId, ReviewPackageStatus.Reviewed, Now);

        Assert.Equal(3, package.BoundaryRevision);
        Assert.Equal(runId.ToString("D"), package.LastReviewRunId);
        Assert.Equal(ReviewPackageStatus.Reviewed, package.Status);
        Assert.Throws<ArgumentException>(() => Package().WithReviewRun(RuleSetId, ShippedVersion, Guid.Empty, ReviewPackageStatus.Reviewed));
        Assert.Throws<ArgumentException>(() => Package().WithReviewRun(" ", ShippedVersion, runId, ReviewPackageStatus.Reviewed));
    }
}
