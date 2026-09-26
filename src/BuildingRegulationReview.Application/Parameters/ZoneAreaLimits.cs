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
    private ZoneAreaLimit(double? squareMeters, string? clause, ZoneAreaLimitGap gaps, bool finishUnrecognised, bool exempt)
    {
        SquareMeters = squareMeters;
        Clause = clause;
        Gaps = gaps;
        FinishUnrecognised = finishUnrecognised;
        IsExempt = exempt;
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

    /// <summary>
    /// True when 區劃用途 is one of the 第79條之2 垂直區劃 both area rules exempt: no limit applies,
    /// and the review will report 免適用 rather than measure the area.
    /// </summary>
    public bool IsExempt { get; }

    public bool IsKnown => Gaps == ZoneAreaLimitGap.None;

    /// <summary>
    /// What the limit cell shows: the article and its limit, the exemption, or the boxes still to fill.
    /// </summary>
    public string Description
    {
        get
        {
            if (IsExempt) return $"{Clause} 免適用（第79條之2 垂直區劃）";
            if (Gaps != ZoneAreaLimitGap.None) return "未填" + string.Join("、", Labels(Gaps)) + "，無法判定上限";

            var limit = SquareMeters!.Value.ToString("0.##", CultureInfo.InvariantCulture);
            var note = FinishUnrecognised ? "（裝修等級非放寬條件）" : "";
            return $"{Clause} 上限 {limit} m²{note}";
        }
    }

    /// <summary>
    /// The limit for one zone. 第83條 decides from the eleventh storey up and 第79條 below it, which
    /// is the two rules' priority order: every 第83條 tier, even doubled by 第四款, is stricter than
    /// 第79條's 一、五○○平方公尺. Whichever decides, a 第79條之2 垂直區劃 is exempt from it.
    /// </summary>
    public static ZoneAreaLimit For(
        int? floorNumber, bool? sprinklered, string? interiorFinish, string? buildingUse, string? use = null)
    {
        // 樓層序 is the rule's applicability, so until it is filled neither article is known to apply.
        if (floorNumber is null) return Unknown(ZoneAreaLimitGap.FloorNumber);

        var clause = floorNumber >= 11 ? "第83條" : "第79條";

        // The engine decides an exemption before the requirement, so an exempt 區劃 needs none of
        // the boxes the limit would otherwise wait on.
        if (ZoneUses.IsVerticalCompartment(use)) return new ZoneAreaLimit(null, clause, ZoneAreaLimitGap.None, false, true);

        return floorNumber >= 11
            ? Article83(sprinklered, interiorFinish, buildingUse)
            : Article79(sprinklered);
    }

    /// <summary>
    /// The limit for a 區劃 as the panel currently holds it — the typed values, not the stored ones,
    /// so each box shows its consequence as it is filled.
    /// </summary>
    public static ZoneAreaLimit ForZone(int? floorNumber, bool? sprinklered, string? buildingUse, string? use)
    {
        // The grade is derived during review from the modelled walls and ceilings. The batch panel
        // no longer pretends an Area carries that fact, so it can only show the Article 79 limit —
        // unless the 用途 exempts the 區劃, which needs no grade at all.
        return floorNumber >= 11 && !ZoneUses.IsVerticalCompartment(use)
            ? Unknown(ZoneAreaLimitGap.InteriorFinish)
            : For(floorNumber, sprinklered, null, buildingUse, use);
    }

    /// <summary>The limit for a zone as it currently stands in the model.</summary>
    public static ZoneAreaLimit For(FireReviewZoneRow zone, string? buildingUse)
    {
        if (zone is null) throw new ArgumentNullException(nameof(zone));
        return ForZone(zone.FloorNumber, zone.Sprinklered, buildingUse, zone.Use);
    }

    private static ZoneAreaLimit Article79(bool? sprinklered) =>
        sprinklered is null
            ? Unknown(ZoneAreaLimitGap.Sprinklered)
            : new ZoneAreaLimit(sprinklered == true ? 3000 : 1500, "第79條", ZoneAreaLimitGap.None, false, false);

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

        // 第三款 is the one tier that gets here without a 用途類組, because it never reads one. The
        // spelling is read exactly as ReviewInputAssembler reads it before handing it to the rule,
        // so 「Ｈ－２組」 raises the limit in the panel and in the review alike.
        var h2 = BuildingUseGroups.IsH2(buildingUse);
        var baseLimit = substrate
            ? 500
            : string.Equals(finish, InteriorFinishGrades.ClassOne, StringComparison.Ordinal)
                ? (h2 ? 400 : 200)
                : (h2 ? 200 : 100);

        return new ZoneAreaLimit(
            (sprinklered == true ? 2 : 1) * baseLimit,
            "第83條",
            ZoneAreaLimitGap.None,
            !InteriorFinishGrades.IsKnown(finish),
            false);
    }

    private static ZoneAreaLimit Unknown(ZoneAreaLimitGap gaps) => new(null, null, gaps, false, false);

    private static IEnumerable<string> Labels(ZoneAreaLimitGap gaps)
    {
        if ((gaps & ZoneAreaLimitGap.FloorNumber) != 0) yield return "樓層序";
        if ((gaps & ZoneAreaLimitGap.InteriorFinish) != 0) yield return "模型牆面／天花板耐燃等級";
        if ((gaps & ZoneAreaLimitGap.BuildingUse) != 0) yield return "用途類組";
        if ((gaps & ZoneAreaLimitGap.Sprinklered) != 0) yield return "滅火設備";
    }

    public bool Equals(ZoneAreaLimit other) =>
        Nullable.Equals(SquareMeters, other.SquareMeters) &&
        string.Equals(Clause, other.Clause, StringComparison.Ordinal) &&
        Gaps == other.Gaps &&
        FinishUnrecognised == other.FinishUnrecognised &&
        IsExempt == other.IsExempt;

    public override bool Equals(object? obj) => obj is ZoneAreaLimit other && Equals(other);

    public override int GetHashCode()
    {
        unchecked
        {
            var hash = SquareMeters?.GetHashCode() ?? 0;
            hash = (hash * 397) ^ (Clause?.GetHashCode() ?? 0);
            hash = (hash * 397) ^ (int)Gaps;
            hash = (hash * 397) ^ (FinishUnrecognised ? 1 : 0);
            return (hash * 397) ^ (IsExempt ? 1 : 0);
        }
    }

    public static bool operator ==(ZoneAreaLimit left, ZoneAreaLimit right) => left.Equals(right);

    public static bool operator !=(ZoneAreaLimit left, ZoneAreaLimit right) => !left.Equals(right);

    public override string ToString() => Description;
}
