using System;
using System.Globalization;

namespace BuildingRegulationReview.Application.WriteBack;

/// <summary>
/// Compares the area the drafts computed with the area Revit reports for the Area that was written
/// (spec 10.6: 面積與 Revit Area 的差異超過容許值時，禁止進入 Ready，並指出可能的邊界問題).
/// </summary>
/// <remarks>
/// Write-back only reports the disagreement; gating the package's state on it is P2-T09. The two
/// numbers are computed from different things — the draft sums the solved faces, Revit measures the
/// enclosure the boundary lines actually formed — so a disagreement is nearly always a boundary that
/// did not close where the solver thought it did.
/// </remarks>
public static class AreaAgreement
{
    /// <summary>One percent. Below this the two numbers are the same room measured twice.</summary>
    public const double DefaultRelativeTolerance = 0.01;

    /// <summary>
    /// What to put on the log, or null when the two agree. An Area that Revit reports as zero is
    /// called out separately: that is not a rounding difference, it is an Area that never landed
    /// inside a closed ring.
    /// </summary>
    public static string? Describe(
        string zoneName,
        double draftSquareMeters,
        double revitSquareMeters,
        double relativeTolerance = DefaultRelativeTolerance)
    {
        if (relativeTolerance < 0) throw new ArgumentOutOfRangeException(nameof(relativeTolerance));

        var label = string.IsNullOrWhiteSpace(zoneName) ? "區劃" : "區劃「" + zoneName.Trim() + "」";

        if (revitSquareMeters <= 0)
        {
            return label + "的面積沒有落在封閉的邊界內，Revit 量到 0 m²，請檢查邊界線是否閉合。";
        }

        if (draftSquareMeters <= 0) return null;

        var difference = Math.Abs(draftSquareMeters - revitSquareMeters);
        if (difference <= draftSquareMeters * relativeTolerance) return null;

        return string.Format(
            CultureInfo.InvariantCulture,
            "{0}的草算面積 {1:0.##} m² 與 Revit 量到的 {2:0.##} m² 相差 {3:0.#}%，可能有邊界沒有閉合。",
            label,
            draftSquareMeters,
            revitSquareMeters,
            difference / draftSquareMeters * 100.0);
    }
}
