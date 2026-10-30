using System;
using System.Linq;
using BuildingRegulationReview.Application.Checks;
using BuildingRegulationReview.Application.Reviews;
using BuildingRegulationReview.Domain.Reviews;
using Xunit;
using static BuildingRegulationReview.Core.Tests.Candidates.CandidateModel;
using Model = BuildingRegulationReview.Core.Tests.Reviews.ReviewRunValidityTests.Model;

namespace BuildingRegulationReview.Core.Tests.Reviews;

/// <summary>
/// 檢討面板的篩選 (spec 11.7.2)：四個狀態、單一檢討項目、只看需更新、以及對列的內容做文字搜尋。
/// 篩選只決定哪幾列在畫面上，不重算任何統計，也不動檢討結果本身。
/// </summary>
public sealed class ReviewTableFilterTests
{
    private static ReviewTable Table(double areaLimitM2 = 150) =>
        ReviewTable.Build(ReviewTableTests.ReviewAll(new Model(), areaLimitM2));

    private static ReviewTableFilter Bands(params ReviewStatusBand[] bands) =>
        new ReviewTableFilter(bands, null, null, false);

    private static ReviewTableFilter Find(string search) =>
        new ReviewTableFilter(null, null, search, false);

    private static ReviewRun HandRun(params ReviewResult[] results) =>
        new ReviewRun(ReviewRunValidityTests.RunId, PackageId, ReviewRunValidityTests.RuleSetId, ReviewRunValidityTests.Version, 1,
            ReviewRunValidityTests.Started).Complete(results, ReviewRunValidityTests.Ended);

    private static ReviewResult Hand(string message, params string[] subjects) =>
        new(Guid.NewGuid(), ReviewRunValidityTests.RunId, PackageId, ReviewCheckTypes.FireResistance, subjects,
            ZoneA.ToString("D"), ReviewStatus.Fail, ReviewValue.OfText("x"), ReviewValue.OfText("y"),
            "wall", "1", "建築技術規則建築設計施工編第79條", message, ReviewEvidence.Empty);

    // --- 狀態帶 -------------------------------------------------------------------------------------

    [Theory]
    [InlineData(ReviewStatus.Fail, ReviewStatusBand.Fail)]
    [InlineData(ReviewStatus.Pass, ReviewStatusBand.Pass)]
    [InlineData(ReviewStatus.NotApplicable, ReviewStatusBand.NotApplicable)]
    [InlineData(ReviewStatus.InsufficientData, ReviewStatusBand.Pending)]
    [InlineData(ReviewStatus.ManualReview, ReviewStatusBand.Pending)]
    [InlineData(ReviewStatus.NotRun, ReviewStatusBand.Pending)]
    public void The_six_states_read_as_four(ReviewStatus status, ReviewStatusBand expected) =>
        Assert.Equal(expected, ReviewStatusBands.Of(status));

    [Fact]
    public void The_bands_are_labelled_the_way_the_統計_counts_them()
    {
        Assert.Equal(new[] { "未符合", "待確認", "符合", "不適用" }, ReviewStatusBands.All.Select(ReviewStatusBands.Label));

        // 待確認 in the filter and 待確認 in the 統計 are the same three states, and stay so by construction.
        var counts = new ReviewStatusCounts(new[] { ReviewStatus.InsufficientData, ReviewStatus.ManualReview, ReviewStatus.NotRun });
        Assert.Equal(3, counts.Unknown);
    }

    // --- 不篩選 -------------------------------------------------------------------------------------

    [Fact]
    public void Nothing_filtered_shows_the_whole_table()
    {
        var table = Table();
        var view = ReviewTableFilter.ShowEverything.Apply(table);

        Assert.False(ReviewTableFilter.ShowEverything.IsActive);
        Assert.Equal(table.Entries.Count(), view.TotalCount);
        Assert.Equal(view.TotalCount, view.ShownCount);
        Assert.False(view.IsEmpty);
        Assert.Equal(table.Sections.Count, view.Sections.Count);
        Assert.Equal($"共 {view.TotalCount} 項", view.Summary);
        Assert.All(view.Sections, s => Assert.Equal(string.Empty, s.ShownText));
    }

    [Fact]
    public void Clearing_every_state_shows_everything_rather_than_nothing()
    {
        var filter = new ReviewTableFilter(Array.Empty<ReviewStatusBand>(), null, null, false);

        Assert.False(filter.IsActive);
        Assert.Equal(ReviewStatusBands.All, filter.Bands);
        Assert.Equal(Table().Entries.Count(), filter.Apply(Table()).ShownCount);
    }

    // --- 依狀態篩選 ---------------------------------------------------------------------------------

    [Fact]
    public void Only_the_failures_are_left_when_only_未符合_is_chosen()
    {
        var table = Table(areaLimitM2: 50);
        var filter = Bands(ReviewStatusBand.Fail);
        var view = filter.Apply(table);

        Assert.True(filter.IsActive);
        Assert.NotEmpty(view.Entries);
        Assert.All(view.Entries, e => Assert.Equal(ReviewStatus.Fail, e.EffectiveStatus));
        Assert.Equal(table.Entries.Count(e => e.EffectiveStatus == ReviewStatus.Fail), view.ShownCount);
        Assert.Equal($"顯示 {view.ShownCount}／{view.TotalCount} 項", view.Summary);

        // The two oversized 區劃 are the failures; 構件防火時效 passes throughout and is emptied.
        var area = view.Sections.Single(s => s.Section.CheckType == ReviewCheckTypes.CompartmentArea);
        Assert.Equal(2, area.ShownCount);
        Assert.True(view.Sections.Single(s => s.Section.CheckType == ReviewCheckTypes.FireResistance).IsEmpty);
    }

    [Fact]
    public void An_emptied_row_stays_in_the_view_so_the_panel_decides_whether_to_show_it()
    {
        var view = Bands(ReviewStatusBand.Fail).Apply(Table(areaLimitM2: 50));

        // Every row of spec 11.7 is still here, in order; the panel hides the empty ones only while a
        // filter is on, so an unfiltered table can still read 未檢討 for a check that produced nothing.
        Assert.Equal(ReviewTable.CheckTypes, view.Sections.Select(s => s.Section.CheckType));
        Assert.Contains(view.Sections, s => s.IsEmpty);
        Assert.All(view.Sections.Where(s => s.IsEmpty), s => Assert.Empty(s.Groups));
    }

    [Fact]
    public void Filtering_hides_rows_and_never_re_counts_the_統計()
    {
        var table = Table(areaLimitM2: 50);
        var section = table.Section(ReviewCheckTypes.CompartmentArea);
        var view = Bands(ReviewStatusBand.Pass).Apply(table);
        var filtered = view.Sections.Single(s => s.Section.CheckType == ReviewCheckTypes.CompartmentArea);

        // Both 區劃 fail, so nothing of this row is shown — but the row still counts what the run found.
        Assert.Equal(0, filtered.ShownCount);
        Assert.Equal(2, filtered.TotalCount);
        Assert.Equal(section.Counts.Text, filtered.Section.Counts.Text);
        Assert.Equal(2, filtered.Section.Counts.Fail);
        Assert.Equal(ReviewStatus.Fail, filtered.Section.Status);
    }

    [Fact]
    public void A_heading_that_shows_less_than_it_counts_says_so()
    {
        var model = new Model();
        model.Ratings["type-a"] = 30;
        var table = ReviewTable.Build(ReviewTableTests.ReviewAll(model));
        var ratings = Bands(ReviewStatusBand.Fail).Apply(table).Sections
            .Single(s => s.Section.CheckType == ReviewCheckTypes.FireResistance);

        Assert.Equal(4, ratings.TotalCount);
        Assert.Equal("　顯示 1／4", ratings.ShownText);

        // The 牆/type-a group is whole — its one row is the failing one — so it says nothing extra.
        var group = Assert.Single(ratings.Groups);
        Assert.Equal("牆：type-a", group.Group.Label);
        Assert.Equal(string.Empty, group.ShownText);
        Assert.Equal(1, group.ShownCount);
    }

    // --- 依檢討項目篩選 -----------------------------------------------------------------------------

    [Fact]
    public void One_檢討項目_hides_the_others()
    {
        var table = Table(areaLimitM2: 50);
        var view = new ReviewTableFilter(null, ReviewCheckTypes.CompartmentArea, null, false).Apply(table);

        Assert.All(view.Entries, e => Assert.Equal(ReviewCheckTypes.CompartmentArea, e.CheckType));
        Assert.Equal(2, view.ShownCount);
        Assert.DoesNotContain(view.Sections, s => s.Section.CheckType != ReviewCheckTypes.CompartmentArea && !s.IsEmpty);
    }

    [Fact]
    public void A_filter_nothing_answers_says_so_rather_than_showing_an_empty_table()
    {
        var view = new ReviewTableFilter(null, "沒有這種檢討", null, false).Apply(Table());

        Assert.True(view.IsEmpty);
        Assert.Equal(0, view.ShownCount);
        Assert.Equal("沒有符合篩選條件的項目", view.Summary);
    }

    // --- 搜尋 ---------------------------------------------------------------------------------------

    [Fact]
    public void A_search_matches_the_type_name_whatever_its_case()
    {
        var table = Table();

        var lower = Find("type-a").Apply(table);
        Assert.NotEmpty(lower.Entries);
        Assert.All(lower.Entries, e => Assert.Equal("type-a", e.TypeName));
        Assert.Equal(lower.ShownCount, Find("TYPE-A").Apply(table).ShownCount);
    }

    [Fact]
    public void A_search_matches_the_區劃_a_row_belongs_to()
    {
        // Not only the 區劃's own row: a wall of that 區劃 says so on its line too, which is what makes
        // 「只看這個區劃的東西」 one search rather than one per 檢討項目.
        var view = Find("B 區").Apply(Table());

        Assert.Contains(view.Entries, e => e.CheckType == ReviewCheckTypes.CompartmentArea && e.ZoneName == "B 區");
        Assert.Contains(view.Entries, e => e.CheckType == ReviewCheckTypes.FireResistance && e.ZoneName == "B 區");
        Assert.True(view.ShownCount < view.TotalCount);
    }

    [Fact]
    public void Every_term_of_a_search_must_match()
    {
        var table = Table();

        Assert.NotEmpty(Find("type-a 牆").Apply(table).Entries);
        Assert.True(Find("type-a 門").Apply(table).IsEmpty);
    }

    [Fact]
    public void A_space_narrows_the_search_rather_than_widening_it()
    {
        // Two terms are an and, not an or and not one phrase: no row is both 區劃 area-a and area-b, so
        // asking for both leaves nothing.
        Assert.True(Find("area-a area-b").Apply(Table()).IsEmpty);
        Assert.Equal(1, Find("區劃 area-b").Apply(Table()).ShownCount);
    }

    [Fact]
    public void An_element_is_searchable_even_when_the_line_only_names_the_first_one()
    {
        var table = ReviewTable.Build(HandRun(Hand("原因", "W-left", "W-right")));
        var entry = Assert.Single(table.Entries);

        // The line reads 「（元素 W-left 等 2 個）」 — the second element is nowhere in it.
        Assert.DoesNotContain("W-right", ReviewEntryReport.Headline(entry), StringComparison.Ordinal);
        Assert.Equal(1, Find("W-right").Apply(table).ShownCount);
    }

    [Fact]
    public void The_whole_reason_is_searchable_although_the_line_shortens_it()
    {
        var tail = "只有展開明細才看得到的結尾";
        var table = ReviewTable.Build(HandRun(Hand(new string('原', ReviewEntryReport.MessageLimit) + tail)));

        Assert.DoesNotContain(tail, ReviewEntryReport.Headline(Assert.Single(table.Entries)), StringComparison.Ordinal);
        Assert.Equal(1, Find(tail).Apply(table).ShownCount);
    }

    [Fact]
    public void A_search_matches_the_條文_and_the_rule_id()
    {
        var table = ReviewTable.Build(HandRun(Hand("原因", "W-left")));

        Assert.Equal(1, Find("第79條").Apply(table).ShownCount);
        Assert.Equal(1, Find("wall").Apply(table).ShownCount);
    }

    // --- 只看需更新 ---------------------------------------------------------------------------------

    [Fact]
    public void 只看需更新_leaves_only_the_rows_the_model_moved_under()
    {
        var run = ReviewTableTests.ReviewAll(new Model());
        var moved = new Model();
        moved.Ratings["type-a"] = 30;
        var table = ReviewTable.Build(run, ReviewRunValidity.Evaluate(run, moved.Baseline(),
            ReviewRunValidityTests.RuleSetId, ReviewRunValidityTests.Version, 1));

        var view = new ReviewTableFilter(null, null, null, staleOnly: true).Apply(table);

        Assert.NotEmpty(view.Entries);
        Assert.All(view.Entries, e => Assert.True(e.IsStale));
        Assert.Equal(table.Entries.Count(e => e.IsStale), view.ShownCount);

        // The 區劃面積 rows did not depend on the rating that changed, so they are not in the view.
        Assert.True(view.Sections.Single(s => s.Section.CheckType == ReviewCheckTypes.CompartmentArea).IsEmpty);
    }

    // --- 分組 ---------------------------------------------------------------------------------------

    [Theory]
    [InlineData(ReviewCheckTypes.CompartmentArea, ReviewTableGrouping.Zone)]
    [InlineData(ReviewCheckTypes.AreaExemption, ReviewTableGrouping.Zone)]
    [InlineData(ReviewCheckTypes.FireResistance, ReviewTableGrouping.Type)]
    [InlineData(ReviewCheckTypes.OpeningProtection, ReviewTableGrouping.OpeningKind)]
    [InlineData(ReviewCheckTypes.CompartmentContinuity, ReviewTableGrouping.JunctionKind)]
    [InlineData(ReviewCheckTypes.VerticalCompartment, ReviewTableGrouping.ShaftRequirement)]
    [InlineData("未知檢查", ReviewTableGrouping.OpeningKind)]
    public void Each_row_is_read_by_one_breakdown(string checkType, ReviewTableGrouping expected) =>
        Assert.Equal(expected, ReviewTable.PrimaryGrouping(checkType));

    [Fact]
    public void The_view_groups_a_row_the_way_the_panel_reads_it()
    {
        var view = ReviewTableFilter.ShowEverything.Apply(Table());
        var ratings = view.Sections.Single(s => s.Section.CheckType == ReviewCheckTypes.FireResistance);

        Assert.Equal(ReviewTableGrouping.Type, ratings.Grouping);
        Assert.All(ratings.Groups, g => Assert.Equal(ReviewTableGrouping.Type, g.Group.Grouping));
        Assert.Equal(ratings.ShownCount, ratings.Groups.Sum(g => g.ShownCount));
    }
}
