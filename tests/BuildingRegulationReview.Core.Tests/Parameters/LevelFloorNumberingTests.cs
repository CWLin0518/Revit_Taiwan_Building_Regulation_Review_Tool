using System;
using System.Linq;
using BuildingRegulationReview.Application.Parameters;
using Xunit;

namespace BuildingRegulationReview.Core.Tests.Parameters;

/// <summary>
/// 防火檢討_所在樓層序 derived from the levels' elevations: 1 is the lowest at or above ground,
/// −1 the first basement. 第70條 counts a storey's position from the top with this and 地上層數.
/// </summary>
public sealed class LevelFloorNumberingTests
{
    private static BuildingLevel L(string name, double elevation) => new(name, name, elevation);

    [Fact]
    public void The_lowest_level_at_or_above_ground_is_the_first_floor()
    {
        var numbering = LevelFloorNumbering.From(new[] { L("1F", 0), L("2F", 3.5), L("3F", 7) });

        Assert.Equal(1, numbering.For("1F"));
        Assert.Equal(2, numbering.For("2F"));
        Assert.Equal(3, numbering.For("3F"));
        Assert.Equal(3, numbering.FloorsAboveGround);
    }

    [Fact]
    public void Basements_count_back_from_the_ground_as_negative_numbers()
    {
        var numbering = LevelFloorNumbering.From(new[]
        {
            L("B2", -7), L("B1", -3.5), L("1F", 0), L("2F", 3.5)
        });

        Assert.Equal(-2, numbering.For("B2"));
        Assert.Equal(-1, numbering.For("B1"));
        Assert.Equal(1, numbering.For("1F"));
        Assert.Equal(2, numbering.For("2F"));
        // 地上層數 counts only what is above ground, which is what 第70條's table needs.
        Assert.Equal(2, numbering.FloorsAboveGround);
    }

    [Fact]
    public void The_order_the_levels_arrive_in_does_not_matter()
    {
        var shuffled = LevelFloorNumbering.From(new[] { L("3F", 7), L("B1", -3.5), L("2F", 3.5), L("1F", 0) });

        Assert.Equal(-1, shuffled.For("B1"));
        Assert.Equal(1, shuffled.For("1F"));
        Assert.Equal(3, shuffled.For("3F"));
    }

    /// <summary>A structural and an architectural level per floor are one storey, not two.</summary>
    [Fact]
    public void Levels_at_the_same_elevation_share_one_storey_number()
    {
        var numbering = LevelFloorNumbering.From(new[]
        {
            L("2F", 3.5), L("2F 結構", 3.5), L("1F", 0)
        });

        Assert.Equal(2, numbering.For("2F"));
        Assert.Equal(2, numbering.For("2F 結構"));
        Assert.Equal(2, numbering.FloorsAboveGround);
        Assert.Contains(numbering.Warnings, w => w.Contains("高程相同", StringComparison.Ordinal));
    }

    [Fact]
    public void A_level_a_millimetre_apart_is_still_the_same_storey_but_a_metre_apart_is_not()
    {
        var same = LevelFloorNumbering.From(new[] { L("1F", 0), L("1F'", 0.0005) });
        Assert.Equal(1, same.FloorsAboveGround);

        var apart = LevelFloorNumbering.From(new[] { L("1F", 0), L("夾層", 1.0) });
        Assert.Equal(2, apart.FloorsAboveGround);
    }

    /// <summary>Ground is the project's datum, not 基地地面 — so the assumption is always stated.</summary>
    [Fact]
    public void The_numbering_always_says_it_assumed_ground_is_elevation_zero()
    {
        var numbering = LevelFloorNumbering.From(new[] { L("1F", 0) });

        Assert.Contains(numbering.Warnings, w => w.Contains("高程 0 為地面", StringComparison.Ordinal));
    }

    /// <summary>
    /// A parapet or a top-of-steel is a Level too and nothing distinguishes it, so the count is
    /// reported for the user to check rather than quietly trusted.
    /// </summary>
    [Fact]
    public void The_numbering_reports_the_counts_so_an_inflated_storey_count_is_visible()
    {
        var numbering = LevelFloorNumbering.From(new[]
        {
            L("1F", 0), L("2F", 3.5), L("屋突", 7), L("女兒牆", 8.2)
        });

        Assert.Equal(4, numbering.FloorsAboveGround);
        Assert.Contains(numbering.Warnings, w => w.Contains("地上 4 層", StringComparison.Ordinal));
        Assert.Contains(numbering.Warnings, w => w.Contains("非樓層", StringComparison.Ordinal));
    }

    [Fact]
    public void A_model_with_only_basements_says_the_storey_position_cannot_be_computed()
    {
        var numbering = LevelFloorNumbering.From(new[] { L("B1", -3.5), L("B2", -7) });

        Assert.Equal(0, numbering.FloorsAboveGround);
        Assert.Contains(numbering.Warnings, w => w.Contains("無從計算", StringComparison.Ordinal));
    }

    [Fact]
    public void A_model_with_no_levels_derives_nothing_and_says_so()
    {
        var numbering = LevelFloorNumbering.From(Array.Empty<BuildingLevel>());

        Assert.True(numbering.IsEmpty);
        Assert.Null(numbering.For("anything"));
        Assert.Contains(numbering.Warnings, w => w.Contains("沒有樓層", StringComparison.Ordinal));
    }

    [Fact]
    public void An_unknown_level_has_no_number_rather_than_a_default()
    {
        var numbering = LevelFloorNumbering.From(new[] { L("1F", 0) });

        Assert.Null(numbering.For("FL99"));
        Assert.Null(numbering.For(null));
    }

    /// <summary>
    /// The numbering and 第70條 have to agree: a storey's position from the top is
    /// 地上層數 − 樓層序 + 1 above ground, and 地上層數 − 樓層序 below it.
    /// </summary>
    [Theory]
    [InlineData("4F", 1)]
    [InlineData("3F", 2)]
    [InlineData("1F", 4)]
    [InlineData("B1", 5)]
    [InlineData("B2", 6)]
    public void The_numbering_feeds_article_70s_count_from_the_top(string level, int expectedFromTop)
    {
        var numbering = LevelFloorNumbering.From(new[]
        {
            L("B2", -7), L("B1", -3.5), L("1F", 0), L("2F", 3.5), L("3F", 7), L("4F", 10.5)
        });

        var f = numbering.For(level)!.Value;
        var fromTop = f > 0
            ? numbering.FloorsAboveGround - f + 1
            : numbering.FloorsAboveGround - f;

        Assert.Equal(expectedFromTop, fromTop);
    }

    [Fact]
    public void A_level_rejects_a_blank_id_or_an_impossible_elevation()
    {
        Assert.Throws<ArgumentException>(() => new BuildingLevel(" ", "1F", 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new BuildingLevel("1F", "1F", double.NaN));
    }
}
