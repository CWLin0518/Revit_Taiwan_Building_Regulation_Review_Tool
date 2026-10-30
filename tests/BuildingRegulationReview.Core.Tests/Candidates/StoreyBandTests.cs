using BuildingRegulationReview.Application.Candidates;
using Xunit;
using static BuildingRegulationReview.Core.Tests.Candidates.CandidateModel;

namespace BuildingRegulationReview.Core.Tests.Candidates;

/// <summary>
/// 驗證清單 C-17：一片沒有通過樓層 bbox 篩選的帷幕嵌板，該不該從宿主帷幕牆的格子接回這一層。
/// 這是「哪些嵌板該入選」那個述詞 —— 讀取層只負責問 Revit 要格子的高程，判斷在這裡。
/// </summary>
public sealed class StoreyBandTests
{
    // 1F：0 m 到 3.5 m（兩端已收過 slack）。
    private static StoreyBand Storey() => new StoreyBand(M(0), M(3.5));

    [Fact]
    public void The_band_is_normalised_so_a_top_below_the_bottom_still_reads_low_to_high()
    {
        var band = new StoreyBand(M(3.5), M(0));

        Assert.Equal(M(0), band.BottomFeet, 9);
        Assert.Equal(M(3.5), band.TopFeet, 9);
    }

    [Theory]
    [InlineData(1.75, true)]
    [InlineData(0.0, true)]
    [InlineData(3.5, true)]
    [InlineData(-0.01, false)]
    [InlineData(3.51, false)]
    public void Contains_asks_whether_one_elevation_is_in_the_band(double meters, bool expected) =>
        Assert.Equal(expected, Storey().Contains(M(meters)));

    [Theory]
    [InlineData(0.9, 2.4, true)]    // 整格都在本層
    [InlineData(-2.0, 0.5, true)]   // 跨樓板：下半在樓下，本層仍算它（與 bbox 篩選同語意）
    [InlineData(3.0, 6.0, true)]    // 跨上方樓板
    [InlineData(3.5, 7.0, true)]    // 只碰到上緣
    [InlineData(-3.0, 0.0, true)]   // 只碰到下緣
    [InlineData(3.6, 7.0, false)]   // 整格在樓上
    [InlineData(-4.0, -0.01, false)]// 整格在樓下
    public void A_cell_counts_for_this_storey_when_it_overlaps_the_band(double lowMeters, double highMeters, bool expected)
    {
        Assert.Equal(expected, Storey().OverlapsVertically(M(lowMeters), M(highMeters)));
        // 上下顛倒傳入也要同一個答案。
        Assert.Equal(expected, Storey().OverlapsVertically(M(highMeters), M(lowMeters)));
    }

    /// <summary>
    /// Critical：宿主牆是以 bbox 相交收的，一道 1F 通到 RF 的帷幕牆因此出現在每一層的牆清單裡，而
    /// <c>GetPanelIds()</c> 回的是整面牆的嵌板。少了這道閘門，1F 會收到 5 層的嵌板，而且它們投影到
    /// 平面後位置看起來完全合理，於是每一片都產生正式的檢討列。
    /// </summary>
    [Theory]
    [InlineData(0.9, 2.4, true)]
    [InlineData(4.0, 6.4, false)]   // 2F 的那一格，不該出現在 1F
    [InlineData(14.0, 16.4, false)] // 5F 的那一格
    public void A_cell_on_another_storey_is_not_kept_even_though_its_host_wall_was_collected(
        double lowMeters, double highMeters, bool expected) =>
        Assert.Equal(expected, Storey().KeepsCurtainPanel(M(lowMeters), M(highMeters), panelHasBoundingBox: false));

    [Fact]
    public void A_panel_with_a_bounding_box_but_no_cell_is_not_let_in_through_the_back_door()
    {
        // 它已經被拿去問過樓層篩選而沒過（沒過才會走到這條路），所以它確實不在範圍內。
        Assert.False(Storey().KeepsCurtainPanel(null, null, panelHasBoundingBox: true));
    }

    [Fact]
    public void A_panel_with_neither_a_cell_nor_a_bounding_box_is_kept_so_it_can_be_seen()
    {
        // C-17 的佔位嵌板：兩種高程來源都沒有。寧可讓它進來而後因為沒有平面位置被列為人工覆核
        // （看得見），也不要在收集階段靜默消失（看不見）。
        Assert.True(Storey().KeepsCurtainPanel(null, null, panelHasBoundingBox: false));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_half_known_cell_counts_as_unknown(bool hasBox)
    {
        Assert.Equal(!hasBox, Storey().KeepsCurtainPanel(M(0.9), null, hasBox));
        Assert.Equal(!hasBox, Storey().KeepsCurtainPanel(null, M(2.4), hasBox));
    }

    [Fact]
    public void The_top_storey_band_collapsed_by_slack_still_keeps_a_cell_that_touches_it()
    {
        // 最頂層的帶可能被 slack 收成上下顛倒；正規化後仍是一個可用的點帶。
        var collapsed = new StoreyBand(M(20.05), M(19.95));

        Assert.True(collapsed.KeepsCurtainPanel(M(18), M(21), panelHasBoundingBox: false));
        Assert.False(collapsed.KeepsCurtainPanel(M(10), M(15), panelHasBoundingBox: false));
    }
}
