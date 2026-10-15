using System;
using System.Collections.Generic;
using System.Linq;
using BuildingRegulationReview.Application.ProjectSetup;
using BuildingRegulationReview.Domain.ReviewPackages;
using Xunit;

namespace BuildingRegulationReview.Core.Tests.ProjectSetup;

/// <summary>
/// 驗證清單 C-13 的兩個 code review 發現：設定視窗把一組設定套到**全部**選取的樓層平面，所以
/// (1) 選中的樓層存過不同設定時不能挑一組預選——那會把別的樓層原本的設定換成別人的值；
/// (2) 同一組選取被重複詢問時（視窗每次重新取得焦點都會刷新面積配置、連帶重新觸發選取變更）
/// 必須認得出「這一組已經套用過了」，否則會把使用者手改的選擇蓋回存檔值。
/// </summary>
public sealed class ReviewPackageSetupPreselectionTests
{
    private const string Scheme = "scheme-A";

    private static ReviewPackageSetupFloorPlan Plan(string uniqueId, string name) =>
        new ReviewPackageSetupFloorPlan(uniqueId, name);

    private static ReviewPackageSetupHistory HistoryOf(
        params (string floor, string? template, bool copyCrop, string? scopeBox)[] rows)
    {
        var packages = rows.Select(row => new ReviewPackage(Guid.NewGuid(), row.floor, "level-" + row.floor, Scheme)).ToList();
        var saved = packages.Select((package, index) => new
        {
            package.PackageId,
            Selection = new ReviewPackageSetupSelection(package.SourceFloorPlanUniqueId, package.LevelUniqueId,
                package.AreaSchemeUniqueId, rows[index].template, rows[index].copyCrop, rows[index].scopeBox)
        }).ToDictionary(x => x.PackageId, x => x.Selection);
        return ReviewPackageSetupHistory.From(packages, package => saved[package.PackageId]);
    }

    [Fact]
    public void Nothing_to_preselect_when_no_selected_floor_plan_saved_anything()
    {
        var preselection = ReviewPackageSetupPreselection.For(
            new[] { Plan("floor-9", "FL9") }, Scheme, HistoryOf(("floor-1", "T1", true, null)));

        Assert.Equal(ReviewPackageSetupPreselectionKind.Nothing, preselection.Kind);
        Assert.Empty(preselection.Saved);
        Assert.Null(preselection.Agreed);
    }

    [Fact]
    public void One_saved_floor_plan_is_preselected()
    {
        var preselection = ReviewPackageSetupPreselection.For(
            new[] { Plan("floor-1", "FL1") }, Scheme, HistoryOf(("floor-1", "T1", false, "S1")));

        Assert.Equal(ReviewPackageSetupPreselectionKind.Agreed, preselection.Kind);
        Assert.Equal("T1", preselection.Agreed!.AreaPlanTemplateUniqueId);
        Assert.Equal("S1", preselection.Agreed.ScopeBoxUniqueId);
        Assert.False(preselection.Agreed.CopyCropSettings);
        Assert.Equal("FL1", Assert.Single(preselection.Saved).FloorPlanName);
    }

    [Fact]
    public void Several_saved_floor_plans_that_say_the_same_thing_are_preselected()
    {
        var preselection = ReviewPackageSetupPreselection.For(
            new[] { Plan("floor-1", "FL1"), Plan("floor-2", "FL2") },
            Scheme,
            HistoryOf(("floor-1", "T1", true, "S1"), ("floor-2", "T1", true, "S1")));

        Assert.Equal(ReviewPackageSetupPreselectionKind.Agreed, preselection.Kind);
        Assert.Equal("T1", preselection.Agreed!.AreaPlanTemplateUniqueId);
        Assert.Equal(new[] { "FL1", "FL2" }, preselection.Saved.Select(entry => entry.FloorPlanName));
    }

    [Theory]
    // 任何一個欄位不同就是衝突：預選其中一組，按下建立時另一組就被換掉了。
    [InlineData("T2", true, "S1")]
    [InlineData("T1", false, "S1")]
    [InlineData("T1", true, "S2")]
    public void Disagreeing_saved_floor_plans_are_a_conflict_and_are_not_preselected(
        string template, bool copyCrop, string scopeBox)
    {
        var preselection = ReviewPackageSetupPreselection.For(
            new[] { Plan("floor-1", "FL1"), Plan("floor-2", "FL2") },
            Scheme,
            HistoryOf(("floor-1", "T1", true, "S1"), ("floor-2", template, copyCrop, scopeBox)));

        Assert.Equal(ReviewPackageSetupPreselectionKind.Conflict, preselection.Kind);
        Assert.Null(preselection.Agreed);
        // 兩個樓層都要被點名，提示才說得出衝突的是哪幾個、各自存的是什麼。
        Assert.Equal(new[] { "FL1", "FL2" }, preselection.Saved.Select(entry => entry.FloorPlanName));
    }

    [Fact]
    public void A_saved_and_an_unsaved_floor_plan_together_still_preselect_the_saved_one()
    {
        var preselection = ReviewPackageSetupPreselection.For(
            new[] { Plan("floor-1", "FL1"), Plan("floor-9", "FL9") },
            Scheme,
            HistoryOf(("floor-1", "T1", true, "S1")));

        Assert.Equal(ReviewPackageSetupPreselectionKind.Agreed, preselection.Kind);
        Assert.Equal("FL1", Assert.Single(preselection.Saved).FloorPlanName);
    }

    [Fact]
    public void Null_and_empty_stand_for_the_same_unset_option()
    {
        var preselection = ReviewPackageSetupPreselection.For(
            new[] { Plan("floor-1", "FL1"), Plan("floor-2", "FL2") },
            Scheme,
            HistoryOf(("floor-1", null, true, null), ("floor-2", "", true, "")));

        Assert.Equal(ReviewPackageSetupPreselectionKind.Agreed, preselection.Kind);
    }

    [Fact]
    public void The_key_is_the_same_for_the_same_selection_whatever_order_it_was_clicked_in()
    {
        var history = HistoryOf(("floor-1", "T1", true, "S1"), ("floor-2", "T1", true, "S1"));
        var first = ReviewPackageSetupPreselection.For(
            new[] { Plan("floor-1", "FL1"), Plan("floor-2", "FL2") }, Scheme, history);
        var reversed = ReviewPackageSetupPreselection.For(
            new[] { Plan("floor-2", "FL2"), Plan("floor-1", "FL1") }, Scheme, history);

        Assert.Equal(first.Key, reversed.Key);
    }

    [Fact]
    public void The_key_changes_with_the_floor_plans_and_with_the_area_scheme()
    {
        var history = HistoryOf(("floor-1", "T1", true, "S1"));
        var one = ReviewPackageSetupPreselection.For(new[] { Plan("floor-1", "FL1") }, Scheme, history);
        var two = ReviewPackageSetupPreselection.For(
            new[] { Plan("floor-1", "FL1"), Plan("floor-2", "FL2") }, Scheme, history);
        var otherScheme = ReviewPackageSetupPreselection.For(new[] { Plan("floor-1", "FL1") }, "scheme-B", history);

        Assert.NotEqual(one.Key, two.Key);
        Assert.NotEqual(one.Key, otherScheme.Key);
    }

    [Fact]
    public void Nothing_to_preselect_without_an_area_scheme()
    {
        var preselection = ReviewPackageSetupPreselection.For(
            new[] { Plan("floor-1", "FL1") }, null, HistoryOf(("floor-1", "T1", true, "S1")));

        Assert.Equal(ReviewPackageSetupPreselectionKind.Nothing, preselection.Kind);
    }

    [Fact]
    public void For_rejects_missing_arguments()
    {
        Assert.Equal("selectedFloorPlans", Assert.Throws<ArgumentNullException>(
            () => ReviewPackageSetupPreselection.For(null!, Scheme, ReviewPackageSetupHistory.Empty)).ParamName);
        Assert.Equal("history", Assert.Throws<ArgumentNullException>(
            () => ReviewPackageSetupPreselection.For(Array.Empty<ReviewPackageSetupFloorPlan>(), Scheme, null!)).ParamName);
    }

    [Fact]
    public void A_null_row_in_the_selection_is_skipped_rather_than_dereferenced()
    {
        var preselection = ReviewPackageSetupPreselection.For(
            new ReviewPackageSetupFloorPlan[] { null!, Plan("floor-1", "FL1") },
            Scheme,
            HistoryOf(("floor-1", "T1", true, "S1")));

        Assert.Equal(ReviewPackageSetupPreselectionKind.Agreed, preselection.Kind);
    }
}
