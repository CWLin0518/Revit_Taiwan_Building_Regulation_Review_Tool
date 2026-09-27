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

    /// <summary>
    /// What the 區劃面積 result says after an exemption holds. The engine's own message names the
    /// condition that held (<c>zone.use == "管道間"</c>) but not who takes over, which reads as if the
    /// 區劃 were simply not reviewed; this names the article that does review it.
    /// </summary>
    public const string VerticalCompartmentHandoff =
        "此區劃屬第79條之2第1項之垂直區劃，不受區劃面積規定拘束，" +
        "改依該項以一小時以上防火時效之牆壁、防火門窗等防火設備與該處防火構造之樓地板單獨區劃分隔。";

    // --- 第79條之1 的六個用字 -------------------------------------------------------------------
    //
    // 第79條之1 lets six uses out of 第79條第1項's 一、五○○平方公尺 — but only when they 無法區劃
    // 分隔 and 自成一個區劃, and the tool can see neither of those (see
    // docs/regulations/article-79-1-area-exemption.md §3.7). So unlike the 垂直區劃 above, these six
    // words exempt nothing on their own: they only decide which 區劃 gets a 人工覆核 asking a person
    // to confirm the rest. They are deliberately NOT in either area rule's exemption list (§3.6).

    /// <summary>Ａ－１組或Ｄ－２組之觀眾席部分 — 第79條之1第一款.</summary>
    public const string Auditorium = "觀眾席";

    /// <summary>Ｃ類之生產線部分 — 第79條之1第二款.</summary>
    public const string ProductionLine = "生產線";

    /// <summary>Ｄ－３組或Ｄ－４組之教室 — 第79條之1第二款.</summary>
    public const string Classroom = "教室";

    /// <summary>體育館 — 第79條之1第二款.</summary>
    public const string Gymnasium = "體育館";

    /// <summary>零售市場 — 第79條之1第二款.</summary>
    public const string RetailMarket = "零售市場";

    /// <summary>停車空間 — 第79條之1第二款.</summary>
    public const string CarPark = "停車空間";

    /// <summary>
    /// The uses 第79條之1 names, in the article's order: 第一款's 觀眾席, then 第二款's five.
    /// 「其他類似用途建築物」 is not here for the same reason 「其他類似部分」 is not above — it cannot
    /// be enumerated, and a use outside this list simply gets no 第79條之1 result (決議 6、10).
    /// </summary>
    public static IReadOnlyList<string> Article79_1Uses { get; } =
        new[] { Auditorium, ProductionLine, Classroom, Gymnasium, RetailMarket, CarPark };

    /// <summary>
    /// The 建築物使用類組 each use is read together with, for the three that 第79條之1 qualifies by
    /// group. 體育館、零售市場、停車空間 are absent on purpose (決議 5): 「Ｄ－３組或Ｄ－４組之」
    /// cannot reach 零售市場 or 停車空間 — neither is a Ｄ類 use — and 體育館 is read the same wide way
    /// because the two mistakes cost differently. Reading it wide adds one 人工覆核; reading it narrow
    /// hides an exemption the designer is entitled to claim, and this judgement never produces a 符合
    /// either way.
    /// </summary>
    private static readonly Dictionary<string, IReadOnlyList<string>> Article79_1Groups =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
        {
            [Auditorium] = new[] { "A-1", "D-2" },
            [ProductionLine] = new[] { "C-1", "C-2" },
            [Classroom] = new[] { "D-3", "D-4" }
        };

    /// <summary>
    /// True when the text is one of the six uses 第79條之1 names. Compared ordinally after trimming,
    /// exactly like <see cref="IsVerticalCompartment"/> and for the same reason: 觀眾廳、看台、停車場
    /// are not folded in, so a near miss is reported as a use outside the article rather than quietly
    /// judged as if it were inside it (決議 7).
    /// </summary>
    public static bool IsArticle79_1Use(string? value) =>
        value is not null && Article79_1Uses.Contains(value.Trim(), StringComparer.Ordinal);

    /// <summary>
    /// The 建築物使用類組 this use must also carry, or an empty list when the use carries no group
    /// condition at all. Empty is therefore the answer for 體育館、零售市場、停車空間 <em>and</em> for
    /// any text outside <see cref="Article79_1Uses"/>, so a caller reads
    /// <see cref="IsArticle79_1Use"/> first — an empty list means 「不比對類組」, never 「不在兩款之列」.
    /// </summary>
    public static IReadOnlyList<string> GroupsFor(string? use) =>
        use is not null && Article79_1Groups.TryGetValue(use.Trim(), out var groups)
            ? groups
            : Array.Empty<string>();
}
