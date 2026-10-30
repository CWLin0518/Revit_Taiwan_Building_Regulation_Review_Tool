using System;
using System.Collections.Generic;
using System.Linq;
using BuildingRegulationReview.Application.Candidates;
using BuildingRegulationReview.Application.Checks;
using BuildingRegulationReview.Domain.Geometry;
using Xunit;

namespace BuildingRegulationReview.Core.Tests.Candidates;

/// <summary>
/// 帷幕牆室內外的判定表（docs/regulations/curtain-wall-fire-compartment.md §4.8）。
///
/// <para>
/// 第79條第3、4項、第79條之3第2項與第79條之4 問的都是**外牆**；室內帷幕牆分隔的是兩個區劃，是
/// 第79條第1項的區劃牆壁。判錯一邊的代價是兩種相反的錯：把室內牆當外牆量突出（要求模型做它不該做
/// 的事），或把外牆當區劃牆要求一小時時效（一片 2.5 cm 的玻璃永遠過不了）。所以第三態存在，而且
/// 任何猜測都要落到第三態。
/// </para>
/// </summary>
public sealed class CurtainWallExposureTests
{
    private static readonly Guid ZoneA = Guid.Parse("aaaaaaaa-0000-0000-0000-00000000000a");
    private static readonly Guid ZoneB = Guid.Parse("aaaaaaaa-0000-0000-0000-00000000000b");

    private static CurtainWallExposureSample Sample(Guid? negative, Guid? positive) => new(negative, positive);

    private static CurtainWallExposureVerdict Classify(
        IEnumerable<CurtainWallExposureSample> samples,
        CurtainWallFunctionDeclaration function = CurtainWallFunctionDeclaration.NotRead) =>
        CurtainWallExposureClassifier.Classify(samples, function);

    private static IEnumerable<CurtainWallExposureSample> Thrice(CurtainWallExposureSample sample) =>
        Enumerable.Repeat(sample, 3);

    // --- 幾何 -----------------------------------------------------------------------------------

    [Fact]
    public void A_zone_on_one_side_only_is_the_buildings_exterior_wall()
    {
        var verdict = Classify(Thrice(Sample(ZoneA, null)));

        Assert.Equal(CurtainWallExposure.Exterior, verdict.Exposure);
        Assert.Equal(CurtainWallExposureReason.ZoneOnOneSide, verdict.Reason);
    }

    [Fact]
    public void Which_side_the_zone_is_on_does_not_matter()
    {
        // 法線正負由 Oriented 決定，與這裡問的事無關：兩種都是「單側有區劃」。
        Assert.Equal(CurtainWallExposure.Exterior, Classify(Thrice(Sample(null, ZoneA))).Exposure);
    }

    [Fact]
    public void Different_zones_on_the_two_sides_make_it_an_interior_curtain_wall()
    {
        var verdict = Classify(Thrice(Sample(ZoneA, ZoneB)));

        Assert.Equal(CurtainWallExposure.Interior, verdict.Exposure);
        Assert.Equal(CurtainWallExposureReason.ZonesOnBothSides, verdict.Reason);
    }

    [Fact]
    public void The_same_zone_on_both_sides_is_a_wall_standing_inside_one_compartment()
    {
        // 探測深度繞過了這道牆：它站在區劃裡面，既不是外牆也不在區劃邊界上。不是外牆這一點就夠了,
        // 它不該被量突出。
        var verdict = Classify(Thrice(Sample(ZoneA, ZoneA)));

        Assert.Equal(CurtainWallExposure.Interior, verdict.Exposure);
        Assert.Equal(CurtainWallExposureReason.InsideOneZone, verdict.Reason);
    }

    [Fact]
    public void No_zone_on_either_side_at_any_station_is_undecided()
    {
        var verdict = Classify(Thrice(Sample(null, null)));

        Assert.Equal(CurtainWallExposure.Unknown, verdict.Exposure);
        Assert.Equal(CurtainWallExposureReason.NoZoneEitherSide, verdict.Reason);
        Assert.NotNull(verdict.Remedy());
    }

    [Fact]
    public void Stations_that_disagree_without_a_majority_are_undecided()
    {
        // 一站單側、一站兩側、一站空白：沒有一種占多數。補一個區劃或刪一個重疊都會讓它定下來,
        // 猜一個不會。
        var verdict = Classify(new[]
        {
            Sample(ZoneA, null),
            Sample(ZoneA, ZoneB),
            Sample(null, null)
        });

        Assert.Equal(CurtainWallExposure.Unknown, verdict.Exposure);
        Assert.Equal(CurtainWallExposureReason.MixedStations, verdict.Reason);
    }

    [Fact]
    public void A_majority_of_two_out_of_three_decides()
    {
        // 門口、未建區劃的缺口、跨兩個區劃的立面都會讓單一測站失準，所以是多數決而不是「任一站」。
        Assert.Equal(CurtainWallExposure.Interior, Classify(new[]
        {
            Sample(ZoneA, ZoneB),
            Sample(ZoneA, ZoneB),
            Sample(ZoneA, null)
        }).Exposure);
    }

    [Fact]
    public void Opposite_single_sides_are_contradictory_rather_than_exterior()
    {
        // 一站只有北側有區劃、一站只有南側有：那不是一面外牆，是模型在這條線上講了兩件事。
        Assert.Equal(CurtainWallExposure.Unknown, Classify(new[]
        {
            Sample(ZoneA, null),
            Sample(null, ZoneB),
            Sample(null, null)
        }).Exposure);
    }

    // --- Function 宣告 --------------------------------------------------------------------------

    [Fact]
    public void Function_interior_decides_a_case_the_geometry_could_not()
    {
        var verdict = Classify(Thrice(Sample(null, null)), CurtainWallFunctionDeclaration.Interior);

        Assert.Equal(CurtainWallExposure.Interior, verdict.Exposure);
        Assert.Equal(CurtainWallExposureReason.DeclaredInterior, verdict.Reason);
    }

    [Fact]
    public void Function_interior_against_a_geometric_facade_is_a_conflict_not_an_override()
    {
        var verdict = Classify(Thrice(Sample(ZoneA, null)), CurtainWallFunctionDeclaration.Interior);

        Assert.Equal(CurtainWallExposure.Unknown, verdict.Exposure);
        Assert.Equal(CurtainWallExposureReason.DeclarationConflict, verdict.Reason);
        Assert.Contains("矛盾", verdict.Describe());
    }

    [Theory]
    [InlineData(CurtainWallFunctionDeclaration.Exterior)]
    [InlineData(CurtainWallFunctionDeclaration.NotRead)]
    [InlineData(CurtainWallFunctionDeclaration.Other)]
    public void Function_other_than_interior_never_overrides_the_geometry(CurtainWallFunctionDeclaration function)
    {
        // Exterior 是 Revit「帷幕牆」系統族的預設值：在室內畫一片而沒去改它是常態，所以它不是宣告。
        Assert.Equal(CurtainWallExposure.Interior, Classify(Thrice(Sample(ZoneA, ZoneB)), function).Exposure);
        Assert.Equal(CurtainWallExposure.Exterior, Classify(Thrice(Sample(ZoneA, null)), function).Exposure);
        Assert.Equal(CurtainWallExposure.Unknown, Classify(Thrice(Sample(null, null)), function).Exposure);
    }

    // --- 弧形帷幕牆的平面段（docs §4.7）---------------------------------------------------------

    [Fact]
    public void One_facet_is_the_whole_walls_verdict()
    {
        var facet = Classify(Thrice(Sample(ZoneA, null)));

        Assert.Same(facet, CurtainWallExposureClassifier.Merge(new[] { facet }));
    }

    [Fact]
    public void Facets_that_agree_keep_the_reason_they_agreed_on()
    {
        var verdict = CurtainWallExposureClassifier.Merge(new[]
        {
            Classify(Thrice(Sample(ZoneA, null))),
            Classify(Thrice(Sample(ZoneA, null)))
        });

        Assert.Equal(CurtainWallExposure.Exterior, verdict.Exposure);
        Assert.Equal(CurtainWallExposureReason.ZoneOnOneSide, verdict.Reason);
    }

    [Fact]
    public void Facets_that_disagree_are_undecided_rather_than_put_to_a_vote()
    {
        // 審查 4-4：段數跟著 grid line 走，與每一段有多少立面無關，所以多數決會把真實存在的少數段
        // 套上錯誤的規則。混合的那一道牆整道交人工覆核，並要求使用者拆成兩道牆建模。
        var verdict = CurtainWallExposureClassifier.Merge(new[]
        {
            Classify(Thrice(Sample(ZoneA, null))),
            Classify(Thrice(Sample(ZoneA, null))),
            Classify(Thrice(Sample(ZoneA, ZoneB)))
        });

        Assert.Equal(CurtainWallExposure.Unknown, verdict.Exposure);
        Assert.Equal(CurtainWallExposureReason.MixedFacets, verdict.Reason);
        Assert.Contains("分成室內與室外兩道牆", verdict.Remedy());
    }

    [Fact]
    public void One_undecided_facet_leaves_the_whole_wall_undecided()
    {
        var verdict = CurtainWallExposureClassifier.Merge(new[]
        {
            Classify(Thrice(Sample(ZoneA, null))),
            Classify(Thrice(Sample(null, null)))
        });

        Assert.Equal(CurtainWallExposure.Unknown, verdict.Exposure);
    }

    [Fact]
    public void Facets_undecided_for_the_same_reason_keep_that_reason()
    {
        // 整道牆都不屬於任何區劃時，訊息要說的是「請建 Area 區劃」，不是「請拆成兩道牆」。
        var verdict = CurtainWallExposureClassifier.Merge(new[]
        {
            Classify(Thrice(Sample(null, null))),
            Classify(Thrice(Sample(null, null)))
        });

        Assert.Equal(CurtainWallExposureReason.NoZoneEitherSide, verdict.Reason);
        Assert.Contains("Area 區劃", verdict.Remedy());
        Assert.DoesNotContain("兩道牆", verdict.Remedy());
    }

    [Fact]
    public void Facets_all_undecided_for_different_reasons_do_not_claim_to_be_contradictory()
    {
        // 一段沒有區劃、一段宣告與幾何矛盾：**沒有任何一段判出結果**，所以這不是 MixedFacets。
        // 判成 MixedFacets 會讓訊息說「各段判出不同的室內外結果」（事實相反），而修法會說「請拆成
        // 兩道牆」——使用者照做是白做，真正該做的是補區劃與改 Function。
        var verdict = CurtainWallExposureClassifier.Merge(new[]
        {
            Classify(Thrice(Sample(null, null))),
            Classify(Thrice(Sample(ZoneA, null)), CurtainWallFunctionDeclaration.Interior)
        });

        Assert.Equal(CurtainWallExposure.Unknown, verdict.Exposure);
        Assert.Equal(CurtainWallExposureReason.UndecidedThroughout, verdict.Reason);

        // 訊息逐段講出各自的原因，修法給兩段修法的聯集。
        Assert.Contains("每一段都判不出室內外", verdict.Describe());
        Assert.Contains("第 1 段：兩側都找不到區劃", verdict.Describe());
        Assert.Contains("第 2 段：幾何判外牆但 Function 宣告 Interior", verdict.Describe());
        Assert.Contains("Area 區劃", verdict.Remedy());
        Assert.Contains("Function 改回 Exterior", verdict.Remedy());
        Assert.DoesNotContain("兩道牆", verdict.Remedy());
    }

    [Fact]
    public void Facets_agreeing_for_different_reasons_do_not_borrow_one_facets_observation()
    {
        // 一段兩側同屬一個區劃、一段由 Function 宣告為室內：結論（Interior）可信，但不能報成
        // 「兩側各有不同區劃」——那是模型裡沒發生過的觀測，而它會進證據。
        var verdict = CurtainWallExposureClassifier.Merge(new[]
        {
            Classify(Thrice(Sample(ZoneA, ZoneA))),
            Classify(Thrice(Sample(null, null)), CurtainWallFunctionDeclaration.Interior)
        });

        Assert.Equal(CurtainWallExposure.Interior, verdict.Exposure);
        Assert.Equal(CurtainWallExposureReason.AgreedAcrossFacets, verdict.Reason);
        Assert.DoesNotContain("兩側各有不同區劃", verdict.Describe());
        Assert.Contains("一致判為室內", verdict.Describe());
        Assert.Contains("第 1 段：兩側落在同一個區劃內", verdict.Describe());

        // 已判定，所以不該要求使用者做任何事。
        Assert.Null(verdict.Remedy());
    }

    [Fact]
    public void A_contradiction_between_a_decided_facet_and_an_undecided_one_is_still_mixed()
    {
        // 一段判出了室內、一段判不出來：這一種才是「拆成兩道牆」的情形——牆上確實有一段被判定了。
        var verdict = CurtainWallExposureClassifier.Merge(new[]
        {
            Classify(Thrice(Sample(ZoneA, ZoneB))),
            Classify(Thrice(Sample(null, null)))
        });

        Assert.Equal(CurtainWallExposureReason.MixedFacets, verdict.Reason);
        Assert.Contains("兩道牆", verdict.Remedy());
    }

    [Fact]
    public void Merging_no_facets_is_a_programming_error_not_an_unknown()
    {
        Assert.Throws<ArgumentException>(() => CurtainWallExposureClassifier.Merge(Array.Empty<CurtainWallExposureVerdict>()));
    }

    // --- 證據 -----------------------------------------------------------------------------------

    [Fact]
    public void The_verdict_records_what_it_was_decided_from()
    {
        var verdict = Classify(Thrice(Sample(ZoneA, ZoneB)), CurtainWallFunctionDeclaration.Exterior);

        Assert.Equal(new[] { ZoneA, ZoneB }.OrderBy(x => x), verdict.ZoneIds);
        Assert.Equal(3, verdict.Samples.Count);
        Assert.Equal("Interior／ZonesOnBothSides／Function=Exterior", verdict.ToEvidenceText());
    }

    [Fact]
    public void A_decided_verdict_asks_the_user_for_nothing()
    {
        Assert.Null(Classify(Thrice(Sample(ZoneA, null))).Remedy());
        Assert.Null(Classify(Thrice(Sample(ZoneA, ZoneB))).Remedy());
    }

    [Fact]
    public void An_empty_zone_id_is_read_as_no_zone()
    {
        // Guid.Empty 不是一個區劃。讓它當成「有區劃」會把沒有區劃的那一側說成有。
        Assert.Equal(CurtainWallExposureReason.NoZoneEitherSide,
            Classify(Thrice(Sample(Guid.Empty, Guid.Empty))).Reason);
    }

    // --- 兩條管線對同一道牆的答案一致（審查 3-B、4-5）-------------------------------------------
    //
    // `element.curtainWallExposure`（驅動 tw-bcr-79-wall-rating 版本 3）由 CandidateResolver 判；
    // CW-H／CW-V／CW-O 的分流由 CurtainWallJunctionResolver 判。兩者的輸入不是同一份——區劃形狀的
    // 來源、重疊的處理、探測深度的算法、取樣的幾何都不同——判定表卻只有一份。這兩條測試把「同一道
    // 牆、兩邊同一個答案」釘在最常見的兩種形狀上，讓任一端的輸入改掉時有東西會紅。
    //
    // 真正保證不會靜默出錯的不是這兩條測試，而是兩個失效方向都收斂到人工覆核：開口那一路只有正面
    // 判定 Interior 才會離開人工覆核；構件那一路判 Unknown 時會自己產出一列
    // `CurtainWallExposureUndecided`，不管帷幕牆區劃交接那一端怎麼判。

    private const double StoreyMm = 3600;
    private const double HalfWidthM = 0.05;

    /// <summary>A 區 x 0–10 m、B 區 x 10–20 m，與 `CandidateModel.Zones()` 的前兩個區劃同一塊地。</summary>
    private static IReadOnlyList<CurtainWallZoneObservation> ZonesInMillimetres() => new[]
    {
        new CurtainWallZoneObservation(CandidateModel.ZoneA, "A 區", new[] { RectangleMm(0, 0, 10000, 10000) }),
        new CurtainWallZoneObservation(CandidateModel.ZoneB, "B 區", new[] { RectangleMm(10000, 0, 20000, 10000) })
    };

    private static IReadOnlyList<Point2D> RectangleMm(double x0, double y0, double x1, double y1) =>
        new[] { new Point2D(x0, y0), new Point2D(x1, y0), new Point2D(x1, y1), new Point2D(x0, y1) };

    /// <summary>One storey-high glazed curtain wall running north along <paramref name="xMetres"/>.</summary>
    private static CurtainWallObservation WallAtMm(double xMetres)
    {
        var x = xMetres * 1000.0;
        return new CurtainWallObservation(
            "CW", new Point2D(x, 0), new Point2D(x, 10000), new Point2D(1, 0),
            HalfWidthM * 1000.0, 0, StoreyMm,
            new[]
            {
                new CurtainPanelObservation("P1", 0, 10000, 0, StoreyMm,
                    ProvidedFireRating.Missing("測試"), kind: CurtainPanelKind.Glazed)
            },
            typeName: "帷幕牆");
    }

    private static CurtainWallExposure MemberSide(double xMetres)
    {
        var wall = CandidateModel.Wall("CW", xMetres, 0, xMetres, 10, width: HalfWidthM * 2, curtain: true);
        var set = CandidateResolver.Resolve(CandidateModel.Observations(
            zones: CandidateModel.Zones().Take(2),
            members: new[] { wall },
            openings: Array.Empty<OpeningObservation>()));

        return set.Members.Single(m => m.Source.ElementUniqueId == "CW").CurtainWallExposure!.Exposure;
    }

    private static IReadOnlyList<CurtainWallJunction> JunctionSide(double xMetres) =>
        CurtainWallJunctionResolver.Resolve(new CurtainWallObservationSet(
            Guid.Parse("11111111-2222-3333-4444-555555555555"), "LVL", "1F", 0,
            ZonesInMillimetres(),
            new[] { WallAtMm(xMetres) },
            levelElevationsMm: new[] { 0.0, StoreyMm }));

    [Fact]
    public void Both_pipelines_call_the_wall_on_the_zone_boundary_interior()
    {
        // x = 10 m 是 A｜B 的分界：兩側各有一個不同的區劃。
        Assert.Equal(CurtainWallExposure.Interior, MemberSide(10));

        // 判室內的牆不產出任何交接處——外牆的三項都不是它的。
        Assert.Empty(JunctionSide(10));
    }

    [Fact]
    public void Both_pipelines_call_the_wall_on_the_outer_edge_exterior()
    {
        // x = 20 m 是 B 區的外緣：室內側有區劃、外側什麼都沒有。
        Assert.Equal(CurtainWallExposure.Exterior, MemberSide(20));

        // 判外牆的牆照舊產出 CW-O（第79條之4）那一列。
        Assert.Contains(JunctionSide(20), j => j.Kind == CurtainWallJunctionKind.CurtainPanelOther);
    }
}
