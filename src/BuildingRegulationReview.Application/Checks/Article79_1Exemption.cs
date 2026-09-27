using System;
using System.Collections.Generic;
using System.Linq;
using BuildingRegulationReview.Application.Parameters;

namespace BuildingRegulationReview.Application.Checks;

/// <summary>
/// Which 款 of 第79條之1 lets a 區劃 out of 第79條第1項's area limit
/// (docs/regulations/article-79-1-area-exemption.md §3.5).
/// </summary>
public enum Article79_1Clause
{
    /// <summary>Neither 款 holds, so 第79條第1項 applies to this 區劃 as usual.</summary>
    None = 0,

    /// <summary>第一款：建築物使用類組為Ａ－１組或Ｄ－２組之觀眾席部分.</summary>
    FirstClause = 1,

    /// <summary>
    /// 第二款：Ｃ類之生產線部分、Ｄ－３組或Ｄ－４組之教室、體育館、零售市場、停車空間及其他類似用途
    /// 建築物.
    /// </summary>
    SecondClause = 2
}

/// <summary>
/// Which fact 第79條之1 is still waiting for before either 款 can be decided. Declared in the order
/// <see cref="Article79_1Exemption.For"/> reads them — （丙）、（甲）、（乙） — so the message names what
/// is missing in the order the judgement asked for it.
/// </summary>
[Flags]
public enum Article79_1Gap
{
    None = 0,

    /// <summary>（丙）無法區劃分隔 — 防火檢討_無法區劃分隔, which only a designer can state.</summary>
    CannotBeSubdivided = 1,

    /// <summary>（甲）防火構造建築物 — without it, whether 第79條第1項 applies at all is unknown.</summary>
    FireResistiveConstruction = 2,

    /// <summary>（乙）建築物使用類組 — asked for only by the three uses that carry a group condition.</summary>
    BuildingUse = 4
}

/// <summary>
/// Whether 第79條之1 exempts one 區劃 from 第79條第1項's 一、五○○平方公尺, worked out as a plain
/// calculation.
/// </summary>
/// <remarks>
/// <para>
/// 第79條之1 is a classification, not a requirement — 「這個區劃到不到得不受第79條第1項之限制」 — so it
/// does not go through the rule engine: every rule needs a comparable <c>requiredValue</c> and this
/// has none (§3.1, the same reason as <see cref="AtriumExemption"/>). It is shaped like that class
/// too: a 款, the gaps, and one sentence for a person to read.
/// </para>
/// <para>
/// Of the article's four elements only three are in the model. （丁）「自成一個區劃」 and 第2項's
/// 一小時以上之阻熱性 have no field at all, so they are never decided here — which is why an exemption
/// that holds is reported as 人工覆核 and the 區劃面積 result stays 未符合. The tool can say that the
/// 區劃 looks like one of the two 款 and that the designer declared it 無法區劃分隔; it cannot say that
/// the 區劃 is therefore lawful. Releasing it is 人工覆寫 (spec §11.8), and <see cref="Description"/>
/// names the two things the person overriding it has to confirm (§3.7、§7.1).
/// </para>
/// <para>
/// There is consequently no 符合 and no 未符合 here. Failing 第79條之1 is not a violation; it only
/// means 第79條第1項 applies as usual and <c>tw-bcr-79-area</c> reviews the area exactly as it always
/// did. Neither area rule carries any of these six uses as an exemption, and neither may gain one
/// (決議 2).
/// </para>
/// </remarks>
public sealed class Article79_1Exemption
{
    /// <summary>
    /// The two facts （丁） leaves to a person, spelled out in the result so whoever overrides the area
    /// judgement does not have to go back to the article for them (§7.1).
    /// </summary>
    public const string PersonMustConfirm =
        "得不受第79條第1項面積限制，惟須人工確認：" +
        "(1) 本區劃是否以一小時以上防火時效之牆壁、防火門窗等防火設備與該處防火構造之樓地板自成一個區劃；" +
        "(2) 該防火設備是否具有一小時以上之阻熱性（第2項）。確認後以人工覆寫放行面積結果。";

    private Article79_1Exemption(Article79_1Clause clause, Article79_1Gap gaps, string description)
    {
        Clause = clause;
        Gaps = gaps;
        Description = description;
    }

    /// <summary>The 款 that holds, or <see cref="Article79_1Clause.None"/> when none does.</summary>
    public Article79_1Clause Clause { get; }

    /// <summary>The facts still missing, and empty unless the answer waits on one of them.</summary>
    public Article79_1Gap Gaps { get; }

    /// <summary>True when the exemption holds — 人工覆核, never 符合 (§3.5).</summary>
    public bool Holds => Clause != Article79_1Clause.None;

    /// <summary>True when a fact is missing and the answer is therefore 資料不足.</summary>
    public bool IsUndecided => Gaps != Article79_1Gap.None;

    /// <summary>True when the exemption is settled as not holding — 不適用, not 未符合.</summary>
    public bool IsInapplicable => !Holds && !IsUndecided;

    /// <summary>What the result says: the 款 that holds, why none does, or what is missing.</summary>
    public string Description { get; }

    /// <summary>
    /// The exemption for one 區劃. Takes the facts, not a candidate or a <see cref="ReviewInput"/>:
    /// the caller turns 有值／不可讀／沒有 into a value or null, because 「同一區劃各 Area 填得不一致」
    /// is the same kind of gap as 「未填」 and neither picks a value.
    /// </summary>
    /// <param name="fireResistive">（甲）<c>building.fireResistiveConstruction</c>.</param>
    /// <param name="buildingUse">（乙）<c>building.use</c>, in any spelling 第3-3條 prints.</param>
    /// <param name="zoneUse">（乙）<c>zone.use</c>, compared exactly against the six agreed words.</param>
    /// <param name="cannotBeSubdivided">（丙）<c>zone.cannotBeSubdivided</c>.</param>
    public static Article79_1Exemption For(
        bool? fireResistive,
        string? buildingUse,
        string? zoneUse,
        bool? cannotBeSubdivided)
    {
        // （丙） is read first because one Boolean settles the whole article on its own: a 區劃 whose
        // designer says it can be subdivided claims nothing here, whatever its 用途 or 類組.
        if (cannotBeSubdivided == false) return Inapplicable("無法區劃分隔＝否");

        // （甲） next: 第79條之1 only lifts 第79條第1項, and 第1項 only reaches a 防火構造建築物, so the
        // construction settles whether there is anything to be exempt from before the 款 are read.
        if (fireResistive == false) return Settled("非防火構造建築物，第79條第1項本不適用，無免除可言。");

        var clause = ReadClauses(buildingUse, zoneUse);
        if (clause.IsFailed) return Inapplicable(clause.Reason);

        var gaps = clause.Gaps;
        if (cannotBeSubdivided is null) gaps |= Article79_1Gap.CannotBeSubdivided;
        if (fireResistive is null) gaps |= Article79_1Gap.FireResistiveConstruction;
        if (gaps != Article79_1Gap.None) return Undecided(gaps);

        return new Article79_1Exemption(
            clause.Clause,
            Article79_1Gap.None,
            $"符合{Name(clause.Clause)}（{clause.Reason}、設計者宣告無法區劃分隔）。{PersonMustConfirm}");
    }

    public override string ToString() => Description;

    /// <summary>
    /// （乙）: whether 第一款 or 第二款 reaches this 區劃. A 用途 names at most one of them, so there is
    /// nothing to order here — unlike <see cref="AtriumExemption"/>, whose two 款 can both apply to
    /// the same 挑空.
    /// </summary>
    /// <remarks>
    /// A 用途 outside the six agreed words is settled as not holding rather than left undecided: the
    /// field was read and it does not name a use the article covers. 「其他類似用途建築物」 is the
    /// 裁量 that would cover such a case, and that belongs in 人工覆寫, not here (決議 6). The check
    /// layer never asks about such a 區劃 at all (決議 10); this answer is for the panel, which shows
    /// every 區劃.
    /// </remarks>
    private static ClauseReading ReadClauses(string? buildingUse, string? zoneUse)
    {
        var use = string.IsNullOrWhiteSpace(zoneUse) ? null : zoneUse!.Trim();
        if (use is null) return ClauseReading.Failed("區劃用途未填");
        if (!ZoneUses.IsArticle79_1Use(use)) return ClauseReading.Failed($"區劃用途「{use}」不在第79條之1兩款之列");

        var clause = string.Equals(use, ZoneUses.Auditorium, StringComparison.Ordinal)
            ? Article79_1Clause.FirstClause
            : Article79_1Clause.SecondClause;

        // 體育館、零售市場、停車空間 carry no 類組 condition, so the 款 holds on the 用途 alone and the
        // 類組 is never a gap for them (決議 5).
        var groups = ZoneUses.GroupsFor(use);
        if (groups.Count == 0) return ClauseReading.Held(clause, use);

        // A 類組 that was filled in but names no group of 第3-3條 reads as 「不是這幾組」, not as a gap —
        // the same reading ZoneAreaLimit gives 第83條's Ｈ－２組 proviso, so one unrecognised spelling
        // is judged the same way everywhere. Only 未填 leaves the 款 undecided.
        var group = BuildingUseGroups.Canonical(buildingUse);
        if (group is null && string.IsNullOrWhiteSpace(buildingUse)) return ClauseReading.Waiting(Article79_1Gap.BuildingUse);

        if (group is null || !groups.Contains(group, StringComparer.Ordinal))
            return ClauseReading.Failed($"{use}，用途類組 {group ?? buildingUse!.Trim()} 非 {string.Join("、", groups)}");

        return ClauseReading.Held(clause, $"{use}、用途類組 {group}");
    }

    private static string Name(Article79_1Clause clause) =>
        clause == Article79_1Clause.FirstClause ? "第一款" : "第二款";

    private static Article79_1Exemption Undecided(Article79_1Gap gaps) =>
        new Article79_1Exemption(
            Article79_1Clause.None, gaps, "缺" + string.Join("、", Labels(gaps)) + "，無法判定是否符合第79條之1。");

    /// <summary>不適用, said as 「不符合第79條之1（理由）」.</summary>
    private static Article79_1Exemption Inapplicable(string reason) =>
        Settled($"不符合第79條之1（{reason}），第79條第1項照常適用。");

    /// <summary>
    /// 不適用 with the sentence given whole — for the one reason that is not a way of failing
    /// 第79條之1: when 第79條第1項 does not apply, there is no exemption to fail.
    /// </summary>
    private static Article79_1Exemption Settled(string description) =>
        new Article79_1Exemption(Article79_1Clause.None, Article79_1Gap.None, description);

    private static IEnumerable<string> Labels(Article79_1Gap gaps)
    {
        if ((gaps & Article79_1Gap.CannotBeSubdivided) != 0) yield return "無法區劃分隔";
        if ((gaps & Article79_1Gap.FireResistiveConstruction) != 0) yield return "建築物防火構造";
        if ((gaps & Article79_1Gap.BuildingUse) != 0) yield return "建築物用途類組";
    }

    /// <summary>The 款 as they read: one held, settled as not held with a reason, or waiting on a fact.</summary>
    private readonly struct ClauseReading
    {
        private ClauseReading(Article79_1Clause clause, bool failed, Article79_1Gap gaps, string reason)
        {
            Clause = clause;
            IsFailed = failed;
            Gaps = gaps;
            Reason = reason;
        }

        public Article79_1Clause Clause { get; }

        /// <summary>True when neither 款 reaches this 區劃 and no further fact would change that.</summary>
        public bool IsFailed { get; }

        /// <summary>Non-empty only while a 款 cannot be decided.</summary>
        public Article79_1Gap Gaps { get; }

        public string Reason { get; }

        public static ClauseReading Held(Article79_1Clause clause, string reason) =>
            new ClauseReading(clause, false, Article79_1Gap.None, reason);

        public static ClauseReading Failed(string reason) =>
            new ClauseReading(Article79_1Clause.None, true, Article79_1Gap.None, reason);

        public static ClauseReading Waiting(Article79_1Gap gaps) =>
            new ClauseReading(Article79_1Clause.None, false, gaps, string.Empty);
    }
}
