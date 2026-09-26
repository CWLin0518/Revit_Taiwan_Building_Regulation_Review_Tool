using System;
using System.Collections.Generic;
using System.Linq;

namespace BuildingRegulationReview.Application.Parameters;

/// <summary>
/// The agreed spellings for 防火檢討_區劃用途 (<c>zone.use</c>) — spec 19 item 6, settled for the
/// part of the field that changes a judgement.
/// </summary>
/// <remarks>
/// <para>
/// 區劃用途 stays free text: no list can name every use a 區劃 might have, and the review reads the
/// field for evidence as much as for a decision. What <em>is</em> closed is the part that changes an
/// answer — the 垂直區劃 of 第79條之2第1項, which both area rules exempt. Those are offered as a list
/// for the same reason <see cref="InteriorFinishGrades"/> is: a rule compares the text exactly, so a
/// plausible-looking 「梯間」 or 「電梯井」 would quietly be judged against the area limit rather than
/// reported as a typo.
/// </para>
/// <para>
/// 第79條之2第1項 names 挑空部分、昇降階梯間、安全梯之樓梯間、昇降機道、垂直貫穿樓板之管道間
/// 及其他類似部分. 「其他類似部分」 cannot be enumerated, so anything outside this list is simply not
/// exempt — the safe direction, because it reviews more rather than less.
/// </para>
/// </remarks>
public static class ZoneUses
{
    /// <summary>挑空部分.</summary>
    public const string Atrium = "挑空";

    /// <summary>昇降階梯間 — the well an escalator runs in.</summary>
    public const string EscalatorWell = "昇降階梯間";

    /// <summary>安全梯之樓梯間. Kept as the plain 樓梯間 the models already carry.</summary>
    public const string Stairwell = "樓梯間";

    /// <summary>昇降機道.</summary>
    public const string ElevatorShaft = "昇降機道";

    /// <summary>垂直貫穿樓板之管道間.</summary>
    public const string Shaft = "管道間";

    /// <summary>
    /// The 垂直區劃 of 第79條之2第1項, in the order both area rules list them as exemptions.
    /// </summary>
    public static IReadOnlyList<string> VerticalCompartments { get; } =
        new[] { Atrium, EscalatorWell, Stairwell, ElevatorShaft, Shaft };

    /// <summary>
    /// True when the text is one the area rules exempt. Compared ordinally after trimming, exactly
    /// as the rule expression compares it — 「Ｈ－２」-style full-width variants are a separate
    /// question and are deliberately not normalised here.
    /// </summary>
    public static bool IsVerticalCompartment(string? value) =>
        value is not null && VerticalCompartments.Contains(value.Trim(), StringComparer.Ordinal);

    /// <summary>The exemption expression a rule carries for one of these uses.</summary>
    public static string ExemptionSource(string use) => $"zone.use == \"{use}\"";
}
