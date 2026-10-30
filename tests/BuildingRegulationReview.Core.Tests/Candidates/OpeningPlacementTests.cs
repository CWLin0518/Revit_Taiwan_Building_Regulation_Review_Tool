using System;
using System.Collections.Generic;
using BuildingRegulationReview.Application.Candidates;
using BuildingRegulationReview.Domain.Geometry;
using Xunit;
using static BuildingRegulationReview.Core.Tests.Candidates.CandidateModel;

namespace BuildingRegulationReview.Core.Tests.Candidates;

/// <summary>
/// 驗證清單 C-16：開口位置與 host 位置線的關係。這一份述詞是 Revit 讀取層（投影回位置線）與
/// <see cref="CandidateResolver"/>（擋下偏離位置的靜默排除）共用的，所以兩邊的門檻一定一致。
/// </summary>
public sealed class OpeningPlacementTests
{
    private static readonly IReadOnlyList<Point2D> Centerline = new[] { P(10, 0), P(10, 10) };

    [Fact]
    public void A_point_beside_the_centreline_projects_onto_its_foot()
    {
        // 帷幕牆門的外接矩形含 90 度開啟的門扇，中心被拉離牆面半公尺（實機 502 mm）。
        var projected = OpeningPlacement.OnCenterline(P(10.5, 4), Centerline);

        Assert.Equal(M(10), projected.X, 9);
        Assert.Equal(M(4), projected.Y, 9);
    }

    [Fact]
    public void A_point_past_the_end_projects_onto_the_nearer_end_not_the_infinite_line()
    {
        var projected = OpeningPlacement.OnCenterline(P(10.5, 12), Centerline);

        Assert.Equal(M(10), projected.X, 9);
        Assert.Equal(M(10), projected.Y, 9);
    }

    [Fact]
    public void A_point_already_on_the_centreline_is_left_where_it_is()
    {
        var projected = OpeningPlacement.OnCenterline(P(10, 3), Centerline);

        Assert.Equal(M(10), projected.X, 9);
        Assert.Equal(M(3), projected.Y, 9);
        Assert.Equal(0, OpeningPlacement.DistanceToCenterline(P(10, 3), Centerline));
    }

    [Fact]
    public void A_polyline_measures_against_its_nearest_segment()
    {
        var corner = new[] { P(0, 0), P(10, 0), P(10, 10) };

        Assert.Equal(M(0.3), OpeningPlacement.DistanceToCenterline(P(9.7, 6), corner)!.Value, 9);
        Assert.Equal(M(0.2), OpeningPlacement.DistanceToCenterline(P(4, 0.2), corner)!.Value, 9);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(1)]
    public void Without_a_usable_centreline_nothing_is_measured_and_the_point_stands(int? points)
    {
        var line = points is null
            ? null
            : points == 0 ? Array.Empty<Point2D>() : new[] { P(10, 5) };

        Assert.Null(OpeningPlacement.DistanceToCenterline(P(10.5, 4), line));
        // 量不出來不是「偏離」：沒有位置線可比時不得擋下開口，那是另一種疑義。
        Assert.False(OpeningPlacement.IsOffCenterline(P(10.5, 4), line, M(0.175)));
        Assert.Equal(M(10.5), OpeningPlacement.OnCenterline(P(10.5, 4), line).X, 9);
    }

    [Fact]
    public void A_zero_length_segment_is_skipped_rather_than_measured()
    {
        var line = new[] { P(10, 5), P(10, 5) };

        Assert.Null(OpeningPlacement.DistanceToCenterline(P(10.5, 4), line));
    }

    [Theory]
    [InlineData(0.5, true)]    // 實機的帷幕牆門：偏離 0.5 m，band 0.175 m
    [InlineData(0.18, true)]
    [InlineData(0.17, false)]  // 還在 band 內的就不算偏離：門檻與 CandidateResolver 的那條帶同一條
    [InlineData(0.0, false)]
    public void Off_the_centreline_means_further_than_the_band(double offsetMeters, bool expected) =>
        Assert.Equal(expected, OpeningPlacement.IsOffCenterline(P(10 + offsetMeters, 4), Centerline, M(0.175)));
}
