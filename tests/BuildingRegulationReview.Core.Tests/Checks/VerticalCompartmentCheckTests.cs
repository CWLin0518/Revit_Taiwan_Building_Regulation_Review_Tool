using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using BuildingRegulationReview.Application.Candidates;
using BuildingRegulationReview.Application.Checks;
using BuildingRegulationReview.Application.Diagnostics;
using BuildingRegulationReview.Application.Parameters;
using BuildingRegulationReview.Application.Reviews;
using BuildingRegulationReview.Application.Rules;
using BuildingRegulationReview.Domain.Reviews;
using BuildingRegulationReview.Domain.Rules;
using Xunit;
using static BuildingRegulationReview.Core.Tests.Candidates.CandidateModel;

namespace BuildingRegulationReview.Core.Tests.Checks;

/// <summary>
/// 垂直區劃的 Check 層（docs/regulations/vertical-compartment.md §12 步驟 4）。
///
/// These run against the shipped rule set, not a hand-written one: the check adds no judgement of
/// its own. It decides which subjects exist — one (防火設備, 要求) pair per requirement the 區劃用途
/// carries — supplies the one value that answers each, and lets the rules give the verdict. The six
/// states of §3.3 are therefore checked here as they come out of the real rules.
/// </summary>
public sealed class VerticalCompartmentCheckTests
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow
    };

    private static readonly RuleEvaluationContext Today = new(new DateTime(2026, 9, 27), "TW");
    private static readonly Guid RunId = Guid.Parse("99999999-0000-0000-0000-00000000000d");

    private const string DoorType = "type-shaft-door";
    private const string WindowType = "type-shaft-window";
    private const string PanelType = "type-shaft-panel";

    // A 區 x 0–10 是昇降機道，B 區 x 10–20 是管道間；各有一面只屬於自己的邊界牆。
    private static readonly MemberObservation[] Walls =
    {
        Wall("W-a-left", 0, 0, 0, 10),
        Wall("W-shared", 10, 0, 10, 10),
        Wall("W-b-right", 20, 0, 20, 10),
        Wall("CW-top", 10, 10, 20, 10, width: 0.1, curtain: true)
    };

    private static RuleEngine Shipped()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Rules", "BuiltIn", "fire-review-rules.json");
        var loaded = RuleSetCompiler.Load(JsonSerializer.Deserialize<RuleSetDocument>(File.ReadAllText(path), Json));
        Assert.True(loaded.IsSuccess, loaded.IsSuccess ? string.Empty : loaded.Error.ToString());
        return new RuleEngine(loaded.Value);
    }

    private static OpeningObservation Opening(
        string uid, string? host, double x, double y,
        CandidateCategory category = CandidateCategory.Door, string? type = null, string? link = null) =>
        new(Source(uid, link), category, host, P(x, y), M(1.0), M(2.1),
            type ?? category switch
            {
                CandidateCategory.Window => WindowType,
                CandidateCategory.CurtainPanel => PanelType,
                _ => DoorType
            },
            category switch
            {
                CandidateCategory.Window => "SW1",
                CandidateCategory.CurtainPanel => "SP1",
                _ => "SD1"
            });

    private static OpeningObservation HoistwayDoor() => Opening("D-hoistway", "W-a-left", 0, 5);
    private static OpeningObservation ShaftDoor() => Opening("D-shaft", "W-b-right", 20, 5);

    private static CandidateSet Set(params OpeningObservation[] openings) =>
        CandidateResolver.Resolve(Observations(Zones().Take(2).ToList(), Walls, openings));

    /// <summary>A 區 is 昇降機道 and B 區 is 管道間 unless a test says otherwise.</summary>
    private static CompartmentAreaInputs Context(
        bool? fireResistive = true, string? useA = ZoneUses.ElevatorShaft, string? useB = ZoneUses.Shaft)
    {
        var zones = new Dictionary<Guid, IEnumerable<ReviewInput>>();
        if (useA is not null) zones[ZoneA] = new[] { ReviewInput.Known("zone.use", useA, "面積：" + ReviewInputSources.ZoneUse) };
        if (useB is not null) zones[ZoneB] = new[] { ReviewInput.Known("zone.use", useB, "面積：" + ReviewInputSources.ZoneUse) };

        return new CompartmentAreaInputs(
            fireResistive is bool f
                ? new[] { ReviewInput.Known("building.fireResistiveConstruction", f, "專案資訊：" + ReviewInputSources.FireResistiveConstruction) }
                : Array.Empty<ReviewInput>(),
            zones);
    }

    private static ShaftDeviceProperties Device(string typeUniqueId, string? smoke = null, double? ratingMinutes = null) =>
        new(typeUniqueId,
            smoke is null
                ? ProvidedFireProtection.Missing($"沒有參數 {SmokeProtectionParameters.Provided}")
                : FireProtectionText.Parse(smoke),
            ratingMinutes is double minutes
                ? ProvidedFireRating.Rated(minutes, minutes.ToString("0") + " min")
                : ProvidedFireRating.Missing($"此 Type 沒有參數 {FireRatingParameters.Provided}"),
            typeUniqueId);

    private static VerticalCompartmentReview Review(
        CandidateSet set, CompartmentAreaInputs? context = null, params ShaftDeviceProperties[] devices)
    {
        var ids = Enumerable.Range(1, 200).Select(i => Guid.Parse($"00000000-0000-0000-0000-{i:D12}")).GetEnumerator();
        var review = VerticalCompartmentCheck.Review(set,
            new VerticalCompartmentInputs(context ?? Context(), devices),
            Shipped(), Today, RunId, () => { ids.MoveNext(); return ids.Current; });
        Assert.True(review.IsSuccess, review.IsSuccess ? string.Empty : review.Error.ToString());
        return review.Value;
    }

    private static VerticalCompartmentFinding Only(VerticalCompartmentReview review) => Assert.Single(review.Findings);

    // --- 昇降機道之防火設備遮煙性能（第1項第2句） -----------------------------------------------

    [Fact]
    public void A_smoke_sealed_hoistway_door_passes()
    {
        var finding = Only(Review(Set(HoistwayDoor()), Context(useB: null), Device(DoorType, smoke: "是")));

        Assert.Equal(VerticalCompartmentRequirement.HoistwaySmokeSeal, finding.Requirement);
        Assert.Equal(ReviewStatus.Pass, finding.Status);
        Assert.Equal("tw-bcr-79-2-hoistway-smoke-seal", finding.Result.RuleId);
        Assert.Equal(ReviewCheckTypes.VerticalCompartment, finding.Result.CheckType);
        Assert.Equal(ZoneA, finding.ZoneId);
        Assert.Equal("D-hoistway", finding.ElementUniqueId);
        Assert.Contains("昇降機道防火設備遮煙性能", finding.Result.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// 否 is not yet 未符合: 第2項's 昇降機間但書 has no input source at all (文件 §9 第2項), so the
    /// answer is 資料不足 and the 但書 is named as the gap. Reporting 未符合 here would be exactly the
    /// 資料不足誤判為未符合 spec 11.3 forbids.
    /// </summary>
    [Fact]
    public void A_hoistway_door_without_a_smoke_seal_waits_for_the_elevator_lobby_proviso()
    {
        var finding = Only(Review(Set(HoistwayDoor()), Context(useB: null), Device(DoorType, smoke: "否")));

        Assert.Equal(ReviewStatus.InsufficientData, finding.Status);
        Assert.Equal("shaft.elevatorLobbyProtected", Assert.Single(finding.Outcome!.Gaps).Field);
    }

    /// <summary>
    /// 條文 says 「昇降機道裝設之防火設備」, not 門: 出入口 may be a 窗 or a 帷幕嵌板 as well (文件 §4).
    /// </summary>
    [Fact]
    public void A_hoistway_holds_a_window_and_a_curtain_panel_to_the_smoke_seal_too()
    {
        var review = Review(Set(
                HoistwayDoor(),
                Opening("WN-hoistway", "W-a-left", 0, 7, CandidateCategory.Window),
                Opening("P-hoistway", "W-a-left", 0, 9, CandidateCategory.CurtainPanel)),
            Context(useB: null),
            Device(DoorType, smoke: "是"), Device(WindowType, smoke: "是"), Device(PanelType, smoke: "否"));

        Assert.Equal(new[] { "D-hoistway", "P-hoistway", "WN-hoistway" },
            review.Group(VerticalCompartmentRequirement.HoistwaySmokeSeal).ElementUniqueIds);
        Assert.All(review.Findings, f => Assert.Equal(VerticalCompartmentRequirement.HoistwaySmokeSeal, f.Requirement));
    }

    /// <summary>第2項但書成立時免遮煙。No parameter supplies the flag yet, so the input is given here directly.</summary>
    [Fact]
    public void A_protected_elevator_lobby_is_reported_as_exempt()
    {
        // The 但書 is a fact about the 昇降機間, and no source writes it today; when one does, it will
        // arrive as a zone input exactly like this. Until then this case cannot occur in a real run.
        var facts = new RuleFacts(RuleFieldCatalog.Default)
            .Set("building.fireResistiveConstruction", true)
            .Set("zone.id", ZoneA.ToString("D"))
            .Set("zone.use", ZoneUses.ElevatorShaft)
            .Set("shaft.requirement", VerticalCompartmentRequirements.RuleText(VerticalCompartmentRequirement.HoistwaySmokeSeal))
            .Set("shaft.elementUniqueId", "D-hoistway")
            .Set("shaft.providedSmokeProtection", "否")
            .Set("shaft.elevatorLobbyProtected", true);

        var outcome = new RuleEngine(Shipped().RuleSet).Evaluate(RuleCategory.VerticalCompartment, facts, Today);

        Assert.Equal(ReviewStatus.NotApplicable, outcome.Status);
        Assert.Equal(RuleOutcomeReason.Exempt, outcome.Reason);
    }

    // --- 管道間維修門（第1項第3句） -------------------------------------------------------------

    /// <summary>
    /// 文件 §3.1、§9 第8項：一扇維修門同時欠時效與遮煙，兩者是兩個受檢主體，所以同一扇門出現兩次。
    /// </summary>
    [Fact]
    public void One_maintenance_door_answers_two_requirements()
    {
        var review = Review(Set(ShaftDoor()), Context(useA: null), Device(DoorType, smoke: "是", ratingMinutes: 60));

        Assert.Equal(
            new[] { VerticalCompartmentRequirement.ShaftDoorRating, VerticalCompartmentRequirement.ShaftDoorSmokeSeal },
            review.Findings.Select(f => f.Requirement));
        Assert.All(review.Findings, f => Assert.Equal("D-shaft", f.ElementUniqueId));
        Assert.All(review.Findings, f => Assert.Equal(ReviewStatus.Pass, f.Status));

        // 檢討表與標示要分得出兩列，所以要求名要在訊息裡。
        Assert.Equal(2, review.Findings.Select(f => f.Result.Message).Distinct(StringComparer.Ordinal).Count());
        Assert.Contains("管道間維修門防火時效",
            review.For("D-shaft", VerticalCompartmentRequirement.ShaftDoorRating)!.Result.Message, StringComparison.Ordinal);
        Assert.Contains("管道間維修門遮煙性能",
            review.For("D-shaft", VerticalCompartmentRequirement.ShaftDoorSmokeSeal)!.Result.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(60, ReviewStatus.Pass)]
    [InlineData(59, ReviewStatus.Fail)]
    public void A_maintenance_door_needs_a_full_hour(double minutes, ReviewStatus expected)
    {
        var review = Review(Set(ShaftDoor()), Context(useA: null), Device(DoorType, smoke: "是", ratingMinutes: minutes));

        Assert.Equal(expected, review.For("D-shaft", VerticalCompartmentRequirement.ShaftDoorRating)!.Status);
    }

    /// <summary>維修門只會是門：管道間邊界上的窗不是本類別的受檢主體（文件 §4）。</summary>
    [Fact]
    public void A_window_on_a_shaft_boundary_is_no_maintenance_door()
    {
        var review = Review(Set(Opening("WN-shaft", "W-b-right", 20, 3, CandidateCategory.Window)),
            Context(useA: null), Device(WindowType, smoke: "否"));

        Assert.Empty(review.Findings);
    }

    // --- 哪些區劃產生受檢主體 -------------------------------------------------------------------

    [Theory]
    [InlineData(ZoneUses.Atrium)]
    [InlineData(ZoneUses.EscalatorWell)]
    [InlineData(ZoneUses.Stairwell)]
    [InlineData("辦公")]
    [InlineData(null)]
    public void A_use_with_no_extra_requirement_produces_no_subject(string? use)
    {
        var review = Review(Set(HoistwayDoor(), ShaftDoor()), Context(useA: use, useB: use),
            Device(DoorType, smoke: "否"));

        Assert.Empty(review.Findings);
    }

    /// <summary>第79條之2 opens with 「防火構造建築物內之……」, so nothing outside one is asked.</summary>
    [Fact]
    public void A_building_that_is_not_fire_resistive_is_out_of_scope()
    {
        var review = Review(Set(HoistwayDoor(), ShaftDoor()), Context(fireResistive: false),
            Device(DoorType, smoke: "否", ratingMinutes: 30));

        Assert.Equal(3, review.Findings.Count);
        Assert.All(review.Findings, f => Assert.Equal(ReviewStatus.NotApplicable, f.Status));
        Assert.All(review.Findings, f => Assert.Equal(RuleOutcomeReason.NoRuleApplies, f.Outcome!.Reason));
    }

    // --- 資料不足與參數名 -----------------------------------------------------------------------

    /// <summary>
    /// A Type the 遮煙性能 parameter was never bound to is 資料不足, and the evidence has to name that
    /// parameter — 設計防火保護 is a different question, and sending the user to the wrong checkbox
    /// would be worse than saying nothing (決議 6).
    /// </summary>
    [Fact]
    public void An_unbound_smoke_seal_is_insufficient_data_and_names_its_own_parameter()
    {
        var finding = Only(Review(Set(HoistwayDoor()), Context(useB: null), Device(DoorType)));

        Assert.Equal(ReviewStatus.InsufficientData, finding.Status);
        Assert.Equal(ReviewErrorCode.ParameterMissing, finding.ErrorCode);
        Assert.Equal(SmokeProtectionParameters.Provided, Text(finding, "provided.parameter"));
        Assert.Equal("shaft.providedSmokeProtection", Text(finding, "provided.field"));
    }

    [Fact]
    public void An_unbound_maintenance_door_rating_names_the_rating_parameter()
    {
        var review = Review(Set(ShaftDoor()), Context(useA: null), Device(DoorType, smoke: "是"));
        var finding = review.For("D-shaft", VerticalCompartmentRequirement.ShaftDoorRating)!;

        Assert.Equal(ReviewStatus.InsufficientData, finding.Status);
        Assert.Equal(ReviewErrorCode.ParameterMissing, finding.ErrorCode);
        Assert.Equal(FireRatingParameters.Provided, Text(finding, "provided.parameter"));
    }

    /// <summary>
    /// A subject carries only the value its own requirement asks for: the 遮煙 verdict of a 維修門
    /// must not report a gap in the door's 時效, or one missing parameter would be blamed twice.
    /// </summary>
    [Fact]
    public void A_subject_carries_only_the_field_its_requirement_reads()
    {
        var review = Review(Set(ShaftDoor()), Context(useA: null), Device(DoorType, smoke: "是"));
        var smoke = review.For("D-shaft", VerticalCompartmentRequirement.ShaftDoorSmokeSeal)!;

        Assert.Equal(ReviewStatus.Pass, smoke.Status);
        Assert.Empty(smoke.Outcome!.Gaps);
        Assert.Equal("shaft.providedSmokeProtection", Text(smoke, "provided.field"));
    }

    /// <summary>A composite Type is nobody's data gap — somebody has to say which layer answers.</summary>
    [Fact]
    public void A_maintenance_door_with_no_single_rating_is_manual_review()
    {
        var device = new ShaftDeviceProperties(DoorType, FireProtectionText.Parse("是"),
            FireRatingText.Parse("1hr/2hr"), "SD1");
        var review = Review(Set(ShaftDoor()), Context(useA: null), device);
        var finding = review.For("D-shaft", VerticalCompartmentRequirement.ShaftDoorRating)!;

        Assert.Equal(ReviewStatus.ManualReview, finding.Status);
        Assert.Equal(ReviewErrorCode.FireRatingUndetermined, finding.ErrorCode);
    }

    // --- 幾何有疑義 -----------------------------------------------------------------------------

    /// <summary>A 區劃 whose Area is not enclosed gets no verdict, the same line 防火門窗 draws.</summary>
    [Fact]
    public void An_opening_of_a_zone_whose_extent_is_in_doubt_is_manual_review()
    {
        var zones = new[] { Zone(ZoneA, "電梯 1", "area-a"), Zone(ZoneB, "B 區", "area-b", Rect(10, 0, 20, 10)) };
        var set = CandidateResolver.Resolve(Observations(zones, Walls, new[] { HoistwayDoor() }));

        var review = VerticalCompartmentCheck.Review(set,
            new VerticalCompartmentInputs(Context(useB: null), new[] { Device(DoorType, smoke: "是") }),
            Shipped(), Today, RunId);

        // 沒有封閉面積的區劃在前置檢查就被擋下，整個檢討不會開始。
        Assert.True(review.IsFailure);
        Assert.Equal(ReviewErrorCode.CandidateZoneUnusable, review.Error.Code);
    }

    /// <summary>An opening whose relation is ambiguous (here: hosted in a curtain wall) gets 人工覆核.</summary>
    [Fact]
    public void An_ambiguous_opening_relation_is_manual_review()
    {
        var review = Review(Set(Opening("P-curtain", "CW-top", 15, 10, CandidateCategory.CurtainPanel)),
            Context(useA: null, useB: ZoneUses.ElevatorShaft), Device(PanelType, smoke: "是"));

        var finding = Only(review);
        Assert.Equal(ReviewStatus.ManualReview, finding.Status);
        Assert.Equal(ReviewErrorCode.CandidateAmbiguous, finding.ErrorCode);
        Assert.Null(finding.Outcome);
    }

    // --- 檢討表的三列與警告 ---------------------------------------------------------------------

    [Fact]
    public void The_three_rows_are_always_reported_even_when_empty()
    {
        var review = Review(Set(), Context());

        Assert.Equal(VerticalCompartmentRequirements.All, review.Groups.Select(g => g.Requirement));
        Assert.All(review.Groups, g => Assert.Equal(ReviewStatus.NotRun, g.Status));
        Assert.All(review.Groups, g => Assert.Equal(0, g.DeviceCount));
        Assert.Equal(
            new[] { "昇降機道防火設備遮煙性能", "管道間維修門防火時效", "管道間維修門遮煙性能" },
            review.Groups.Select(g => g.Label));
    }

    [Fact]
    public void A_device_of_a_type_the_package_has_no_opening_of_is_reported_as_a_warning()
    {
        var review = Review(Set(HoistwayDoor()), Context(useB: null),
            Device(DoorType, smoke: "是"), Device("type-elsewhere", smoke: "是"));

        Assert.Contains("type-elsewhere", Assert.Single(review.Warnings), StringComparison.Ordinal);
    }

    [Fact]
    public void A_zone_that_is_not_in_the_package_is_reported_as_a_warning()
    {
        var absent = Guid.Parse("aaaaaaaa-0000-0000-0000-0000000000ff");
        var context = new CompartmentAreaInputs(null, new Dictionary<Guid, IEnumerable<ReviewInput>>
        {
            [absent] = new[] { ReviewInput.Known("zone.use", ZoneUses.Shaft, "面積：" + ReviewInputSources.ZoneUse) }
        });

        var review = Review(Set(HoistwayDoor()), context);

        Assert.Contains(absent.ToString("D"), Assert.Single(review.Warnings), StringComparison.Ordinal);
    }

    private static string? Text(VerticalCompartmentFinding finding, string field) =>
        finding.Result.Evidence.Find(field) is { Kind: ReviewValueKind.Text } value ? value.Text : null;
}
