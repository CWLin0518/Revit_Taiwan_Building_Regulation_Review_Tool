using System;
using System.Collections.Generic;
using System.Globalization;
using BuildingRegulationReview.Application.Parameters;

namespace BuildingRegulationReview.Application.Checks;

/// <summary>
/// Which 款 of 第79條之2第3項 lets an 挑空 out of 第1項's 單獨區劃分隔
/// (docs/regulations/vertical-compartment.md §3.6).
/// </summary>
public enum AtriumExemptionClause
{
    /// <summary>Neither 款 holds, so 第1項 applies to this 挑空 as usual.</summary>
    None = 0,

    /// <summary>第一款：避難層通達其直上層或直下層，且室內牆面與天花板以耐燃一級材料裝修.</summary>
    FirstClause = 1,

    /// <summary>第二款：連跨樓層數在三層以下，且樓地板面積在一千五百平方公尺以下.</summary>
    SecondClause = 2
}

/// <summary>Which fact 第79條之2第3項 is still waiting for before either 款 can be decided.</summary>
[Flags]
public enum AtriumExemptionGap
{
    None = 0,

    /// <summary>防火構造建築物 — without it, whether 第1項 applies at all is unknown.</summary>
    FireResistiveConstruction = 1,

    /// <summary>第一款's 避難層通達其直上層或直下層 — 防火檢討_避難層通達.</summary>
    RefugeFloorLink = 2,

    /// <summary>第一款's 耐燃一級裝修 — derived from the modelled walls and ceilings.</summary>
    InteriorFinish = 4,

    /// <summary>第二款's 連跨樓層數 — 防火檢討_連跨樓層數.</summary>
    SpannedFloors = 8,

    /// <summary>第二款's 樓地板面積 — the Revit Area, which an unplaced or unenclosed Area leaves blank.</summary>
    CompartmentArea = 16
}

/// <summary>
/// Whether 第79條之2第3項 exempts one 挑空 from 第1項, worked out as a plain calculation.
/// </summary>
/// <remarks>
/// <para>
/// 第3項 is the one part of 第79條之2 that can lift the whole 區劃 obligation, and it is written only
/// for 挑空. It is not a requirement but a classification — 「這個挑空到不到得依第1項單獨區劃分隔」 —
/// so it does not go through the rule engine: every rule needs a comparable <c>requiredValue</c> and
/// 第3項 has none (決議 24). The judgement lives here instead, shaped like
/// <see cref="Parameters.ZoneAreaLimit"/>: a 款, the gaps, and one sentence for a person to read.
/// </para>
/// <para>
/// Three rules decide the gaps, the same shape as the engine's six states: a 款 that holds makes
/// every other gap irrelevant; a 款 already settled as not holding is never asked for the facts it
/// still lacks; only 「這一款無法判定、另一款不成立」 is 資料不足. There is therefore no 符合 and no
/// 未符合 — failing 第3項 is not a violation, it only means 第1項 applies as usual and the existing
/// boundary rules review it (§3.4、決議 3).
/// </para>
/// </remarks>
public sealed class AtriumExemption
{
    private AtriumExemption(AtriumExemptionClause clause, AtriumExemptionGap gaps, string description)
    {
        Clause = clause;
        Gaps = gaps;
        Description = description;
    }

    /// <summary>The 款 that holds, or <see cref="AtriumExemptionClause.None"/> when none does.</summary>
    public AtriumExemptionClause Clause { get; }

    /// <summary>The facts still missing, and empty unless the answer waits on one of them.</summary>
    public AtriumExemptionGap Gaps { get; }

    /// <summary>True when the exemption holds — 人工覆核, never 符合 (§3.6).</summary>
    public bool Holds => Clause != AtriumExemptionClause.None;

    /// <summary>True when a fact is missing and the answer is therefore 資料不足.</summary>
    public bool IsUndecided => Gaps != AtriumExemptionGap.None;

    /// <summary>True when the exemption is settled as not holding — 不適用, not 未符合.</summary>
    public bool IsInapplicable => !Holds && !IsUndecided;

    /// <summary>What the result says: the 款 that holds, why neither does, or what is missing.</summary>
    public string Description { get; }

    /// <summary>
    /// The exemption for one 挑空. Takes the facts, not a candidate or a
    /// <see cref="ReviewInput"/>: the caller turns 有值／不可讀／沒有 into a value or null, because
    /// 「同一區劃各 Area 填得不一致」 is the same kind of gap as 「未填」 and neither picks a value.
    /// </summary>
    /// <param name="fireResistive"><c>building.fireResistiveConstruction</c>.</param>
    /// <param name="linksRefugeFloor"><c>zone.linksRefugeFloor</c>.</param>
    /// <param name="interiorFinish"><c>zone.interiorFinish</c>.</param>
    /// <param name="spannedFloors"><c>zone.spannedFloors</c>.</param>
    /// <param name="areaSquareMeters"><c>zone.area</c>, in square metres.</param>
    public static AtriumExemption For(
        bool? fireResistive,
        bool? linksRefugeFloor,
        string? interiorFinish,
        int? spannedFloors,
        double? areaSquareMeters)
    {
        // 第3項 only lifts 第1項, and 第1項 only reaches a 防火構造建築物, so the construction settles
        // whether there is anything to be exempt from before either 款 is read.
        if (fireResistive is null) return Undecided(AtriumExemptionGap.FireResistiveConstruction);
        if (fireResistive == false) return Inapplicable("非防火構造建築物，第1項本不適用，無免除可言");

        var first = ReadFirstClause(linksRefugeFloor, interiorFinish);
        var second = ReadSecondClause(spannedFloors, areaSquareMeters);

        // A 款 that holds makes every other gap irrelevant, 第一款 first because it is the 款 the
        // article states first — either one alone is enough, so the order only decides what is said.
        if (first.Holds) return new AtriumExemption(AtriumExemptionClause.FirstClause, AtriumExemptionGap.None, $"符合第一款（{first.Reason}）");
        if (second.Holds) return new AtriumExemption(AtriumExemptionClause.SecondClause, AtriumExemptionGap.None, $"符合第二款（{second.Reason}）");

        var gaps = first.Gaps | second.Gaps;
        if (gaps != AtriumExemptionGap.None) return Undecided(gaps);

        return Inapplicable($"兩款均不成立（{first.Reason}、{second.Reason}）");
    }

    public override string ToString() => Description;

    /// <summary>
    /// 第一款：避難層通達其直上層或直下層，且室內牆面與天花板以耐燃一級材料裝修。第一款 asks only for
    /// 耐燃一級 and has none of 第83條第三款's 「包括底材」 wording, so 耐燃一級含底材 satisfies it too —
    /// it is stricter than the 款 requires.
    /// </summary>
    private static ClauseReading ReadFirstClause(bool? linksRefugeFloor, string? interiorFinish)
    {
        var finish = string.IsNullOrWhiteSpace(interiorFinish) ? null : interiorFinish!.Trim();
        var classOne = finish is not null && (
            string.Equals(finish, InteriorFinishGrades.ClassOne, StringComparison.Ordinal) ||
            string.Equals(finish, InteriorFinishGrades.ClassOneWithSubstrate, StringComparison.Ordinal));

        if (linksRefugeFloor == false) return ClauseReading.Failed("避難層通達＝否");
        if (finish is not null && !classOne) return ClauseReading.Failed($"室內裝修等級＝{finish}");

        var gaps = AtriumExemptionGap.None;
        if (linksRefugeFloor is null) gaps |= AtriumExemptionGap.RefugeFloorLink;
        if (finish is null) gaps |= AtriumExemptionGap.InteriorFinish;
        if (gaps != AtriumExemptionGap.None) return ClauseReading.Waiting(gaps);

        return ClauseReading.Held($"避難層通達其直上層或直下層、室內裝修{finish}");
    }

    /// <summary>
    /// 第二款：連跨樓層數在三層以下，且樓地板面積在一千五百平方公尺以下。Both 以下 include the
    /// threshold. The area is this 挑空's own <c>zone.area</c> (決議 28).
    /// </summary>
    private static ClauseReading ReadSecondClause(int? spannedFloors, double? areaSquareMeters)
    {
        // Either measure being over the threshold settles the 款 on its own, so the other one is not
        // asked for — 面積 2000 ㎡ needs no 連跨樓層數 to know 第二款 does not hold.
        if (spannedFloors is int over && over > 3) return ClauseReading.Failed($"連跨 {over.ToString(CultureInfo.InvariantCulture)} 層");
        if (areaSquareMeters is double large && large > 1500) return ClauseReading.Failed($"樓地板面積 {SquareMeters(large)} ㎡");

        var gaps = AtriumExemptionGap.None;
        if (spannedFloors is null) gaps |= AtriumExemptionGap.SpannedFloors;
        if (areaSquareMeters is null) gaps |= AtriumExemptionGap.CompartmentArea;
        if (gaps != AtriumExemptionGap.None) return ClauseReading.Waiting(gaps);

        return ClauseReading.Held(
            $"連跨 {spannedFloors!.Value.ToString(CultureInfo.InvariantCulture)} 層、樓地板面積 {SquareMeters(areaSquareMeters!.Value)} ㎡");
    }

    private static AtriumExemption Undecided(AtriumExemptionGap gaps) =>
        new AtriumExemption(
            AtriumExemptionClause.None, gaps, "缺" + string.Join("、", Labels(gaps)) + "，無法判定兩款是否成立");

    private static AtriumExemption Inapplicable(string reason) =>
        new AtriumExemption(AtriumExemptionClause.None, AtriumExemptionGap.None, reason);

    private static string SquareMeters(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    private static IEnumerable<string> Labels(AtriumExemptionGap gaps)
    {
        if ((gaps & AtriumExemptionGap.FireResistiveConstruction) != 0) yield return "建築物防火構造";
        if ((gaps & AtriumExemptionGap.RefugeFloorLink) != 0) yield return "避難層通達";
        if ((gaps & AtriumExemptionGap.InteriorFinish) != 0) yield return "模型牆面／天花板耐燃等級";
        if ((gaps & AtriumExemptionGap.SpannedFloors) != 0) yield return "連跨樓層數";
        if ((gaps & AtriumExemptionGap.CompartmentArea) != 0) yield return "區劃面積";
    }

    /// <summary>One 款 as it reads: held, settled as not held with a reason, or waiting on a fact.</summary>
    private readonly struct ClauseReading
    {
        private ClauseReading(bool holds, AtriumExemptionGap gaps, string reason)
        {
            Holds = holds;
            Gaps = gaps;
            Reason = reason;
        }

        public bool Holds { get; }

        /// <summary>Non-empty only while the 款 cannot be decided.</summary>
        public AtriumExemptionGap Gaps { get; }

        public string Reason { get; }

        public static ClauseReading Held(string reason) => new ClauseReading(true, AtriumExemptionGap.None, reason);

        public static ClauseReading Failed(string reason) => new ClauseReading(false, AtriumExemptionGap.None, reason);

        public static ClauseReading Waiting(AtriumExemptionGap gaps) => new ClauseReading(false, gaps, string.Empty);
    }
}
