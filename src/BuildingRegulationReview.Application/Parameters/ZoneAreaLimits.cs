using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace BuildingRegulationReview.Application.Parameters;

/// <summary>
/// The 室內裝修 grades 第83條 reads its three tiers off, spelled exactly as <c>tw-bcr-83-area</c>
/// compares them.
/// </summary>
/// <remarks>
/// Offering these as a closed list is the whole point of putting the field in the panel: the rule
/// treats any other text as "not the 放寬 第二款／第三款 name" and falls back to 第一款's
/// 一○○平方公尺, so a plausible-looking 「耐燃二級」 would quietly be judged against the strictest
/// limit rather than reported as a typo.
/// </remarks>
public static class InteriorFinishGrades
{
    /// <summary>第一款 — no 耐燃一級 claimed.</summary>
    public const string None = "無";

    /// <summary>第二款 — 自地板面起一‧二公尺以上之室內牆面及天花板為耐燃一級.</summary>
    public const string ClassOne = "耐燃一級";

    /// <summary>第三款 — 室內牆面及天花板（包括底材）均為耐燃一級.</summary>
    public const string ClassOneWithSubstrate = "耐燃一級含底材";

    /// <summary>Every value the panel offers, from the strictest limit to the widest.</summary>
    public static IReadOnlyList<string> All { get; } = new[] { None, ClassOne, ClassOneWithSubstrate };

    /// <summary>True when the rule would read this text as one of the three tiers.</summary>
    public static bool IsKnown(string? value) =>
        value is not null && All.Contains(value.Trim(), StringComparer.Ordinal);
}

/// <summary>Which input the panel is still waiting for before it can name a 區劃's area limit.</summary>
[Flags]
public enum ZoneAreaLimitGap
{
    None = 0,

    /// <summary>所在樓層序 — without it neither 第79條 nor 第83條 is known to be the one deciding.</summary>
    FloorNumber = 1,

    /// <summary>室內裝修等級 — 第83條's three tiers.</summary>
    InteriorFinish = 2,

    /// <summary>建築物用途類組 — the Ｈ－２組 proviso in 第83條第一款、第二款.</summary>
    BuildingUse = 4,

    /// <summary>自動滅火設備 — doubles the limit in both articles.</summary>
    Sprinklered = 8
}

/// <summary>
/// The 區劃面積 limit a zone would be judged against, worked out in the panel so the consequence of
/// each box is visible before the review is run.
/// </summary>
/// <remarks>
/// This mirrors the shipped <c>tw-bcr-79-area</c> and <c>tw-bcr-83-area</c> — including which field
/// each one leaves unread, so that what the panel calls 未填 is exactly what the engine would report
/// as 資料不足. The rules stay the authority; <c>ZoneAreaLimitTests</c> evaluates them and asserts
/// this agrees, so a change to either rule is caught rather than silently diverging.
/// </remarks>
public readonly struct ZoneAreaLimit : IEquatable<ZoneAreaLimit>
{
    private ZoneAreaLimit(double? squareMeters, string? clause, ZoneAreaLimitGap gaps, bool finishUnrecognised)
    {
        SquareMeters = squareMeters;
        Clause = clause;
        Gaps = gaps;
        FinishUnrecognised = finishUnrecognised;
    }

    /// <summary>The limit in square metres, or null while <see cref="Gaps"/> names something unfilled.</summary>
    public double? SquareMeters { get; }

    /// <summary>「第79條」 or 「第83條」, or null when nothing is known yet.</summary>
    public string? Clause { get; }

    public ZoneAreaLimitGap Gaps { get; }

    /// <summary>
    /// True when 裝修等級 holds text the rule does not know: not 資料不足 but a 放寬 that was not
    /// earned, so the limit falls back to 第一款 and the panel says why.
    /// </summary>
    public bool FinishUnrecognised { get; }

    public bool IsKnown => Gaps == ZoneAreaLimitGap.None;

    /// <summary>
    /// What the limit cell shows: the article and its limit, or the boxes still to fill.
    /// </summary>
    public string Description
    {
        get
        {
            if (Gaps != ZoneAreaLimitGap.None) return "未填" + string.Join("、", Labels(Gaps)) + "，無法判定上限";

            var limit = SquareMeters!.Value.ToString("0.##", CultureInfo.InvariantCulture);
            var note = FinishUnrecognised ? "（裝修等級非放寬條件）" : "";
            return $"{Clause} 上限 {limit} m²{note}";
        }
    }

    /// <summary>
    /// The limit for one zone. 第83條 decides from the eleventh storey up and 第79條 below it, which
    /// is the two rules' priority order: every 第83條 tier, even doubled by 第四款, is stricter than
    /// 第79條's 一、五○○平方公尺.
    /// </summary>
    public static ZoneAreaLimit For(int? floorNumber, bool? sprinklered, string? interiorFinish, string? buildingUse)
    {
        // 樓層序 is the rule's applicability, so until it is filled neither article is known to apply.
        if (floorNumber is null) return Unknown(ZoneAreaLimitGap.FloorNumber);

        return floorNumber >= 11
            ? Article83(sprinklered, interiorFinish, buildingUse)
            : Article79(sprinklered);
    }

    /// <summary>The limit for a zone as it currently stands in the model.</summary>
    public static ZoneAreaLimit For(FireReviewZoneRow zone, string? buildingUse)
    {
        if (zone is null) throw new ArgumentNullException(nameof(zone));
        return For(zone.FloorNumber, zone.Sprinklered, zone.InteriorFinish, buildingUse);
    }

    private static ZoneAreaLimit Article79(bool? sprinklered) =>
        sprinklered is null
            ? Unknown(ZoneAreaLimitGap.Sprinklered)
            : new ZoneAreaLimit(sprinklered == true ? 3000 : 1500, "第79條", ZoneAreaLimitGap.None, false);

    private static ZoneAreaLimit Article83(bool? sprinklered, string? interiorFinish, string? buildingUse)
    {
        var finish = string.IsNullOrWhiteSpace(interiorFinish) ? null : interiorFinish!.Trim();
        var substrate = string.Equals(finish, InteriorFinishGrades.ClassOneWithSubstrate, StringComparison.Ordinal);

        var gaps = ZoneAreaLimitGap.None;
        if (finish is null) gaps |= ZoneAreaLimitGap.InteriorFinish;
        // 第三款 names no use group, so Ｈ－２組 changes nothing there and the rule never reads it.
        if (!substrate && string.IsNullOrWhiteSpace(buildingUse)) gaps |= ZoneAreaLimitGap.BuildingUse;
        if (sprinklered is null) gaps |= ZoneAreaLimitGap.Sprinklered;
        if (gaps != ZoneAreaLimitGap.None) return Unknown(gaps);

        // 第三款 is the one tier that gets here without a 用途類組, because it never reads one.
        var h2 = string.Equals(buildingUse?.Trim(), "H-2", StringComparison.Ordinal);
        var baseLimit = substrate
            ? 500
            : string.Equals(finish, InteriorFinishGrades.ClassOne, StringComparison.Ordinal)
                ? (h2 ? 400 : 200)
                : (h2 ? 200 : 100);

        return new ZoneAreaLimit(
            (sprinklered == true ? 2 : 1) * baseLimit,
            "第83條",
            ZoneAreaLimitGap.None,
            !InteriorFinishGrades.IsKnown(finish));
    }

    private static ZoneAreaLimit Unknown(ZoneAreaLimitGap gaps) => new(null, null, gaps, false);

    private static IEnumerable<string> Labels(ZoneAreaLimitGap gaps)
    {
        if ((gaps & ZoneAreaLimitGap.FloorNumber) != 0) yield return "樓層序";
        if ((gaps & ZoneAreaLimitGap.InteriorFinish) != 0) yield return "裝修等級";
        if ((gaps & ZoneAreaLimitGap.BuildingUse) != 0) yield return "用途類組";
        if ((gaps & ZoneAreaLimitGap.Sprinklered) != 0) yield return "滅火設備";
    }

    public bool Equals(ZoneAreaLimit other) =>
        Nullable.Equals(SquareMeters, other.SquareMeters) &&
        string.Equals(Clause, other.Clause, StringComparison.Ordinal) &&
        Gaps == other.Gaps &&
        FinishUnrecognised == other.FinishUnrecognised;

    public override bool Equals(object? obj) => obj is ZoneAreaLimit other && Equals(other);

    public override int GetHashCode()
    {
        unchecked
        {
            var hash = SquareMeters?.GetHashCode() ?? 0;
            hash = (hash * 397) ^ (Clause?.GetHashCode() ?? 0);
            hash = (hash * 397) ^ (int)Gaps;
            return (hash * 397) ^ (FinishUnrecognised ? 1 : 0);
        }
    }

    public static bool operator ==(ZoneAreaLimit left, ZoneAreaLimit right) => left.Equals(right);

    public static bool operator !=(ZoneAreaLimit left, ZoneAreaLimit right) => !left.Equals(right);

    public override string ToString() => Description;
}
