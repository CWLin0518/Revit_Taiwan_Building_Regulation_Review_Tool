using System.Collections.Generic;
using BuildingRegulationReview.Application.Geometry;
using BuildingRegulationReview.Domain.Geometry;

namespace BuildingRegulationReview.Application.Candidates;

/// <summary>
/// 一個開口的平面位置與它 host 牆位置線的關係（驗證清單 C-16）。純平面幾何，單位是英尺。
/// </summary>
/// <remarks>
/// <para>
/// <see cref="OpeningObservation.Location"/> 的契約是「落在 host 的位置線上」，而
/// <see cref="CandidateResolver"/> 判一個開口在不在區劃邊界上，用的是以 host 半牆厚加容許值為寬的
/// 一條帶。位置一旦偏離那條線，量到的距離就不再是「開口距邊界多遠」，而是「門扇伸出牆面多遠」
/// ——一扇就在區劃邊界牆上的門會被判成「離邊界太遠」而整扇消失。
/// </para>
/// <para>
/// 同一件事在兩層各需要一次，所以述詞只有這一份：Revit 讀取層拿不到 <c>LocationPoint</c> 時
/// （帷幕牆上的門窗與嵌板都是）以 <see cref="OnCenterline"/> 把來源點投影回位置線；
/// <see cref="CandidateResolver"/> 則以 <see cref="IsOffCenterline"/> 在位置仍然偏離時擋下靜默排除
/// ——解析層不自己再寫一次「距離大於 band」，兩邊的門檻因此不可能走樣。
/// </para>
/// </remarks>
public static class OpeningPlacement
{
    /// <summary>
    /// <paramref name="point"/> 投影到 <paramref name="centerline"/> 上最近的一點。折線不可用
    /// （不到兩點，或每一段都是零長）時原樣回傳——沒有線可投影時，原來的點已經是最好的答案。
    /// </summary>
    public static Point2D OnCenterline(Point2D point, IReadOnlyList<Point2D>? centerline) =>
        TryMeasure(point, centerline, out _, out var foot) ? foot : point;

    /// <summary>
    /// <paramref name="point"/> 到 <paramref name="centerline"/> 的距離（英尺），或 null 表示折線不可用
    /// ——那是「量不出來」，不是零。
    /// </summary>
    public static double? DistanceToCenterline(Point2D point, IReadOnlyList<Point2D>? centerline) =>
        TryMeasure(point, centerline, out var distance, out _) ? distance : (double?)null;

    /// <summary>
    /// 這個位置是不是離 host 的位置線遠到不能拿來判斷它與區劃邊界的關係：偏離量超過
    /// <paramref name="bandFeet"/>（半牆厚＋容許值，與 <see cref="CandidateResolver"/> 判邊界用的同一條帶）。
    /// 量不出來時回 false——沒有位置線可比，就沒有「偏離」這回事，那是另一種疑義。
    /// </summary>
    public static bool IsOffCenterline(Point2D point, IReadOnlyList<Point2D>? centerline, double bandFeet) =>
        DistanceToCenterline(point, centerline) is double distance && distance > bandFeet;

    private static bool TryMeasure(Point2D point, IReadOnlyList<Point2D>? centerline, out double distance, out Point2D foot)
    {
        distance = 0;
        foot = point;
        if (centerline is null || centerline.Count < 2) return false;

        var found = false;
        for (var i = 0; i + 1 < centerline.Count; i++)
        {
            if (centerline[i].DistanceTo(centerline[i + 1]) <= GeometryTolerance.ZeroLengthFeet) continue;

            var measured = SegmentGeometry.DistanceToSegment(point, centerline[i], centerline[i + 1], out _, out var closest);
            if (found && measured >= distance) continue;

            found = true;
            distance = measured;
            foot = closest;
        }

        return found;
    }
}
