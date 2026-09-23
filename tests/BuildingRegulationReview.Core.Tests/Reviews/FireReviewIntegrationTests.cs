using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using BuildingRegulationReview.Application.Candidates;
using BuildingRegulationReview.Application.Checks;
using BuildingRegulationReview.Application.Diagnostics;
using BuildingRegulationReview.Application.Reviews;
using BuildingRegulationReview.Application.Rules;
using BuildingRegulationReview.Domain.Common;
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
    private const string ShippedVersion = "2026.0-provisional";
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

    private static CandidateSet Set(IEnumerable<MemberObservation>? members = null) =>
        CandidateResolver.Resolve(Observations(Zones().Take(2), members ?? Members(), Openings()));

    private static IEnumerable<KeyValuePair<string, ReviewParameterHost>> AllBindings() =>
        ReviewInputSources.All.SelectMany(s => s.Hosts.Select(h => new KeyValuePair<string, ReviewParameterHost>(s.ParameterName, h)));

    private sealed class Parameters
    {
        public List<KeyValuePair<string, ReviewParameterHost>> Bindings { get; set; } = AllBindings().ToList();
        public Dictionary<string, ParameterReading> Project { get; } = new()
        {
            [ReviewInputSources.FireResistiveConstruction] = ParameterReading.OfYesNo(1)
        };
        public Dictionary<string, Dictionary<string, ParameterReading>> Elements { get; } = new()
        {
            ["area-a"] = new() { [ReviewInputSources.Sprinklered] = ParameterReading.OfYesNo(0), [ReviewInputSources.ZoneUse] = ParameterReading.OfText("辦公") },
            ["area-b"] = new() { [ReviewInputSources.Sprinklered] = ParameterReading.OfYesNo(1) },
            ["type-rc200"] = new() { [FireRatingParameters.Provided] = ParameterReading.OfText("2 小時") },
            ["D1-shared"] = new() { [FireProtectionParameters.Provided] = ParameterReading.OfYesNo(1) },
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
        IEnumerable<MemberObservation>? members = null)
    {
        var set = Set(members);
        var inputs = ReviewInputAssembler.Assemble(set, (parameters ?? new Parameters()).Snapshot());
        return new FireReviewRequest(package ?? Package(), rules ?? Rules(), Today, set, inputs, Environment(), previous,
            TimeSpan.FromSeconds(1));
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
        Assert.DoesNotContain("element.typeName", fields); // evidence only

        var needed = ReviewInputSources.NeededBy(Rules()).Select(s => s.ParameterName).ToList();
        Assert.Equal(new[]
        {
            ReviewInputSources.FireResistiveConstruction, FireRatingParameters.Provided, FireProtectionParameters.Provided,
            ReviewInputSources.Sprinklered, ReviewInputSources.ZoneUse
        }, needed);
        Assert.DoesNotContain(needed, n => n == FireRatingParameters.Required);
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
        var item = Assert.Single(report.Blocking);
        Assert.Equal(ReviewErrorCode.ParameterMissing, item.Code);
        Assert.Contains(FireRatingParameters.Provided, item.Message);
        Assert.Contains("類型參數", item.Fix);
        Assert.Contains("牆", item.Fix);
    }

    [Fact]
    public void Parameter_bound_to_some_candidate_categories_only_warns_about_the_rest()
    {
        var parameters = new Parameters();
        parameters.Bindings.RemoveAll(b => b.Key == FireRatingParameters.Provided && b.Value != ReviewParameterHost.Walls);

        var report = Readiness(parameters: parameters);

        Assert.True(report.CanRun);
        var warning = Assert.Single(report.Warnings, i => i.Code == ReviewErrorCode.ParameterMissing);
        Assert.Contains("柱", warning.Message);
        Assert.Contains("樓板", warning.Message);
        Assert.DoesNotContain("牆", warning.Message.Split('到')[1].Split('，')[0]);
        // 梁 are candidates but carry no rating parameter, so they are never reported as a gap.
        Assert.DoesNotContain("結構構架", warning.Message);
    }

    [Fact]
    public void Evidence_only_fields_do_not_require_parameters()
    {
        var parameters = new Parameters();
        parameters.Bindings.RemoveAll(b => b.Key == ReviewInputSources.BuildingUse || b.Key == ReviewInputSources.FloorNumber);
        Assert.True(Readiness(parameters: parameters).CanRun);
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

    /// <summary>第71～73條 give 樑 no dimensional threshold, so beams carry no 設計防火時效 parameter.</summary>
    [Fact]
    public void Beams_are_not_asked_to_carry_the_rating_parameter()
    {
        Assert.DoesNotContain(ReviewParameterHost.StructuralFraming, ReviewInputSources.MemberHosts);

        var source = ReviewInputSources.For("element.providedFireRating")!;
        Assert.DoesNotContain(ReviewParameterHost.StructuralFraming, source.Hosts);
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

    [Fact]
    public void Assembly_fills_building_zone_type_and_opening_inputs_from_the_snapshot()
    {
        var assembly = ReviewInputAssembler.Assemble(Set(), new Parameters().Snapshot());

        var building = Assert.Single(assembly.Area.Building);
        Assert.Equal("building.fireResistiveConstruction", building.Field);
        Assert.Equal("專案資訊：" + ReviewInputSources.FireResistiveConstruction, building.Source);
        Assert.Equal(new[] { "zone.sprinklered", "zone.use" }, assembly.Area.ForZone(ZoneA).Select(i => i.Field));
        Assert.Single(assembly.Area.ForZone(ZoneB));
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
    public void Review_runs_all_three_checks_and_leaves_the_package_reviewed_with_the_rule_set_locked()
    {
        var progress = new Recorder();
        var outcome = Run(Request(), progress: progress);

        Assert.True(outcome.IsCompleted, outcome.Message);
        var run = outcome.Run!;
        Assert.Equal(ReviewRunState.Completed, run.State);
        Assert.Equal(RuleSetId, run.RuleSetId);
        Assert.Equal(ShippedVersion, run.RuleSetVersion);
        Assert.True(run.Baseline.IsRecorded);
        Assert.All(ReviewTable.CheckTypes, type => Assert.Contains(run.Results, r => r.CheckType == type));
        Assert.All(outcome.Table!.Sections, s => Assert.NotEqual(ReviewStatus.NotRun, s.Status));
        Assert.Empty(outcome.Table.OtherEntries);

        Assert.Equal(ReviewPackageStatus.Reviewed, outcome.Package.Status);
        Assert.Equal(RuleSetId, outcome.Package.RuleSetId);
        Assert.Equal(ShippedVersion, outcome.Package.RuleSetVersion);
        Assert.Equal(run.RunId.ToString("D"), outcome.Package.LastReviewRunId);
        Assert.Equal(1, outcome.Package.BoundaryRevision);

        Assert.Equal(new[] { FireReviewStep.CompartmentArea, FireReviewStep.FireResistance, FireReviewStep.OpeningProtection, FireReviewStep.Evidence },
            progress.Reports.Select(p => p.Step));
        Assert.Contains(outcome.Log.Entries, e => e.Code == ReviewErrorCode.ReviewCompleted && e.UserMessage.StartsWith("檢討完成"));
    }

    [Fact]
    public void Review_verdicts_follow_the_model_parameters()
    {
        var run = Run(Request()).Run!;

        // 區劃面積：100 m² ≤ 1500 m²（A 區無灑水）與 3000 m²（B 區有灑水）
        Assert.Equal(ReviewStatus.Pass, run.Results.Single(r => r.CheckType == ReviewCheckTypes.CompartmentArea && r.ZoneId == ZoneA.ToString("D")).Status);
        Assert.Equal(ReviewStatus.Pass, run.Results.Single(r => r.CheckType == ReviewCheckTypes.CompartmentArea && r.ZoneId == ZoneB.ToString("D")).Status);

        // 邊界牆 RC200 = 2 小時 ≥ 60 min
        Assert.Equal(ReviewStatus.Pass, ResultOf(run, ReviewCheckTypes.FireResistance, "W1-bottom", ZoneA).Status);
        // 樓板沒有 Type 時效 → 資料不足（spec 16.3 情境 6：缺值不是未符合）
        Assert.Equal(ReviewStatus.InsufficientData, ResultOf(run, ReviewCheckTypes.FireResistance, "F1-slab", ZoneA).Status);
        // 柱、梁沒有規則 → 不適用
        Assert.Equal(ReviewStatus.NotApplicable, ResultOf(run, ReviewCheckTypes.FireResistance, "B1-shared", ZoneA).Status);

        // 門窗：是 → 符合；否 → 未符合；帷幕嵌板與非 Hosted → 人工覆核（spec 16.3 情境 7）
        Assert.Equal(ReviewStatus.Pass, ResultOf(run, ReviewCheckTypes.OpeningProtection, "D1-shared", ZoneA).Status);
        Assert.Equal(ReviewStatus.Fail, ResultOf(run, ReviewCheckTypes.OpeningProtection, "WN1-bottom", ZoneB).Status);
        Assert.Equal(ReviewStatus.ManualReview, ResultOf(run, ReviewCheckTypes.OpeningProtection, "P1-panel").Status);
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
        Assert.Equal(new[] { FireReviewStep.CompartmentArea, FireReviewStep.FireResistance }, progress.Reports.Select(p => p.Step));
        Assert.Equal(2, outcome.Performance.Stages.Count);
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

    // --- a stored run judged again (spec 13.1, 16.3 情境 8) -------------------------------------

    private static ReviewBaseline CurrentBaseline(Parameters parameters, string phase = "新建")
    {
        var set = Set();
        var inputs = ReviewInputAssembler.Assemble(set, parameters.Snapshot());
        return ReviewBaselineBuilder.Build(set, Environment(phase), inputs.Area, inputs.Rating, inputs.Protection);
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
        Assert.Equal(4, outcome.Performance.Stages.Count);
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
