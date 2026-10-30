using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace BuildingRegulationReview.Application.Parameters;

/// <summary>
/// 建築物使用類組 (<c>zone.useGroup</c>, filled per 區劃 on the Areas) as the rules compare it: the
/// half-width code 第88條's own table prints, e.g. <c>H-2</c>.
/// </summary>
/// <remarks>
/// <para>
/// 第83條第一款、第二款 double their 區劃 area limit 「供建築物使用類組Ｈ–２組使用者」, and the rule
/// expression compares <c>zone.useGroup</c> against one literal. The code itself spells one group at
/// least four ways — 「Ｈ–２組」 (第83條, full-width letter and an en dash), 「H-2 組」 (第86條),
/// 「H-2」 (第88條's table) and 「Ｈ類第二組」 (第310條) — so a project that types any of them is
/// typing the group the article names. Matching one spelling and quietly falling back to the
/// stricter 一○○／二○○平方公尺 for the rest is safe but unusable.
/// </para>
/// <para>
/// The spelling is therefore canonicalised here, at the one place a parameter becomes a rule input,
/// rather than by piling <c>||</c> into the rule (which would have to be repeated in every rule that
/// ever reads a use group) or by teaching the DSL a use-group type. Anything this class does not
/// recognise is passed through untouched: an unrecognised text is compared literally, exactly as
/// before, so the direction stays safe.
/// </para>
/// <para>
/// The group list is 第3-3條's. Only <see cref="H2"/> changes a judgement today — no rule reads any
/// other group — so the rest of the list decides only what the panel offers and which spellings are
/// tidied, never a review result.
/// </para>
/// </remarks>
public static class BuildingUseGroups
{
    /// <summary>Ｈ－２組 — the one group a rule reads (第83條第一款、第二款).</summary>
    public const string H2 = "H-2";

    /// <summary>
    /// The 二十四 codes of 第3-3條, in the article's order: Ａ類 to Ｉ類, and within a class by group
    /// number. Ｅ類 and Ｉ類 have no group, so they are the bare letter.
    /// </summary>
    public static IReadOnlyList<string> All { get; } = new[]
    {
        "A-1", "A-2",
        "B-1", "B-2", "B-3", "B-4",
        "C-1", "C-2",
        "D-1", "D-2", "D-3", "D-4", "D-5",
        "E",
        "F-1", "F-2", "F-3", "F-4",
        "G-1", "G-2", "G-3",
        "H-1", "H-2",
        "I"
    };

    /// <summary>
    /// The canonical code this text names, or null when it names none. Recognises the full-width
    /// letters and digits, every dash the code prints, the 「組」／「類」 suffixes and the
    /// 「Ｘ類第Ｎ組」 form; the comparison is otherwise exact, so 「H-2 及 G-2」 or 「住宿類」 name no
    /// single group and come back null.
    /// </summary>
    public static string? Canonical(string? text)
    {
        var folded = Fold(text);
        if (folded.Length == 0) return null;

        var letter = folded[0];
        if (letter < 'A' || letter > 'I') return null;

        string code;
        if (folded.Length == 1) code = folded;
        else if (folded.Length == 2 && folded[1] >= '1' && folded[1] <= '5') code = $"{letter}-{folded[1]}";
        else if (folded.Length == 3 && folded[1] == '-' && folded[2] >= '1' && folded[2] <= '5') code = folded;
        else return null;

        return All.Contains(code, StringComparer.Ordinal) ? code : null;
    }

    /// <summary>
    /// The text as the review should compare it: the canonical code when the text names a group,
    /// otherwise the text trimmed and otherwise untouched. Null only when nothing was supplied.
    /// </summary>
    public static string? Normalize(string? text) =>
        Canonical(text) ?? (string.IsNullOrWhiteSpace(text) ? null : text!.Trim());

    /// <summary>True when the text names one of 第3-3條's groups, however it is spelled.</summary>
    public static bool IsKnown(string? text) => Canonical(text) is not null;

    /// <summary>
    /// True when the text names Ｈ－２組 — the 第83條第一款、第二款 proviso, however it is spelled.
    /// </summary>
    public static bool IsH2(string? text) => string.Equals(Canonical(text), H2, StringComparison.Ordinal);

    /// <summary>
    /// True when <see cref="Normalize"/> would report something other than what was typed, i.e. the
    /// review reads this text as a group the user spelled differently. Worth saying out loud, in the
    /// panel and in the evidence, so the canonicalisation is never a silent rewrite.
    /// </summary>
    public static bool IsRespelled(string? text) =>
        Canonical(text) is string code && !string.Equals(code, text?.Trim(), StringComparison.Ordinal);

    /// <summary>
    /// What a 區劃 whose Areas name more than one group reads as, e.g. <c>混合（B-2、H-2）</c>. It is
    /// deliberately no group code, so a rule comparing <c>zone.useGroup == "H-2"</c> finds it false and
    /// the stricter limit applies — 「如同一空間有多種使用類組者，應同時符合各使用類組之規定」
    /// （內政部國土管理署115.05.15國署建管字第1150047679號函）.
    /// </summary>
    public const string MixedPrefix = "混合（";

    /// <summary>The value a 區劃 of several groups reads as.</summary>
    public static string Mixed(IEnumerable<string> groups) =>
        MixedPrefix + string.Join("、", groups) + "）";

    /// <summary>
    /// The groups a <see cref="Mixed"/> value names, in its order; empty for anything else. Lets a check
    /// that only sees the rule input ask whether every group of the mix meets its condition.
    /// </summary>
    public static IReadOnlyList<string> GroupsOf(string? mixed) =>
        IsMixed(mixed)
            ? mixed!.Substring(MixedPrefix.Length, mixed.Length - MixedPrefix.Length - 1)
                .Split(new[] { '、' }, StringSplitOptions.RemoveEmptyEntries)
            : Array.Empty<string>();

    /// <summary>True for a value produced by <see cref="Mixed"/>.</summary>
    public static bool IsMixed(string? text) =>
        text is not null && text.StartsWith(MixedPrefix, StringComparison.Ordinal) && text.EndsWith("）", StringComparison.Ordinal);

    /// <summary>
    /// The 用途類組 of one 區劃 from what each of its Areas holds (docs/regulations/building-use-groups.md §6).
    /// Spellings of one group count as that group; text that names no group is kept as typed.
    /// </summary>
    public static ZoneUseGroup Combine(IEnumerable<string?> perArea)
    {
        if (perArea is null) throw new ArgumentNullException(nameof(perArea));

        var typed = perArea.Select(t => string.IsNullOrWhiteSpace(t) ? null : t!.Trim()).ToList();
        if (typed.Count == 0 || typed.All(t => t is null)) return ZoneUseGroup.Blank;
        if (typed.Any(t => t is null))
            return ZoneUseGroup.Partial(typed.Count(t => t is null), typed.Count);

        var groups = typed
            .Select(t => Normalize(t)!)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(Order)
            .ThenBy(g => g, StringComparer.Ordinal)
            .ToList();
        return groups.Count == 1 ? ZoneUseGroup.Single(groups[0]) : ZoneUseGroup.OfMixed(groups);
    }

    /// <summary>第3-3條's order for a known code; anything else after all of them.</summary>
    private static int Order(string group)
    {
        for (var i = 0; i < All.Count; i++)
            if (string.Equals(All[i], group, StringComparison.Ordinal)) return i;
        return All.Count;
    }

    /// <summary>
    /// The text stripped down to letter, optional dash and digit: full-width mapped to half-width,
    /// every dash the code prints mapped to <c>-</c>, Chinese numerals mapped to digits, whitespace
    /// and the 「類」「第」「組」 of 「Ｈ類第二組」 removed, and the letter upper-cased.
    /// </summary>
    private static string Fold(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";

        var folded = new StringBuilder(text!.Length);
        foreach (var raw in text)
        {
            // Full-width ASCII (Ｈ, ２, －) sits one fixed offset above its half-width twin.
            var c = raw >= '！' && raw <= '～' ? (char)(raw - 0xFEE0) : raw;
            if (char.IsWhiteSpace(c) || Dropped.IndexOf(c) >= 0) continue;

            var digit = ChineseDigits.IndexOf(c);
            if (digit >= 0) c = (char)('1' + digit);
            else if (Dashes.IndexOf(c) >= 0) c = '-';

            folded.Append(char.ToUpperInvariant(c));
        }

        return folded.ToString();
    }

    /// <summary>Every dash the code prints between a class letter and a group number.</summary>
    private const string Dashes = "-‐‑‒–—―−ー﹘﹣";

    /// <summary>一 to 五 — 「Ｈ類第二組」 writes the group number out (第310條).</summary>
    private const string ChineseDigits = "一二三四五";

    /// <summary>The scaffolding of 「Ｈ類第二組」 and 「H-2 組」, which carries no information.</summary>
    private const string Dropped = "類第組";
}

/// <summary>How the Areas of one 區劃 answer 防火檢討_用途類組.</summary>
public enum ZoneUseGroupKind
{
    /// <summary>Every Area left it blank: nothing was supplied.</summary>
    Blank,

    /// <summary>Some Areas filled it and some did not, so the 區劃's groups cannot be told.</summary>
    Partial,

    /// <summary>Every Area names the same group (or the same text).</summary>
    Single,

    /// <summary>The Areas name more than one group; every one of them has to be satisfied.</summary>
    Mixed
}

/// <summary>The 用途類組 one 區劃 reads as — see <see cref="BuildingUseGroups.Combine"/>.</summary>
public sealed class ZoneUseGroup
{
    private ZoneUseGroup(ZoneUseGroupKind kind, string? value, IReadOnlyList<string> groups, string? problem, int blankCount = 0)
    {
        Kind = kind;
        Value = value;
        Groups = groups;
        Problem = problem;
        BlankCount = blankCount;
    }

    public static ZoneUseGroup Blank { get; } = new(ZoneUseGroupKind.Blank, null, Array.Empty<string>(), null);

    internal static ZoneUseGroup Partial(int blank, int total) =>
        new(ZoneUseGroupKind.Partial, null, Array.Empty<string>(),
            $"此區劃的 {total} 個面積中有 {blank} 個未填用途類組，無法判定此區劃有哪些使用類組", blank);

    internal static ZoneUseGroup Single(string group) =>
        new(ZoneUseGroupKind.Single, group, new[] { group }, null);

    internal static ZoneUseGroup OfMixed(IReadOnlyList<string> groups) =>
        new(ZoneUseGroupKind.Mixed, BuildingUseGroups.Mixed(groups), groups, null);

    public ZoneUseGroupKind Kind { get; }

    /// <summary>What the rules compare: the group, <c>混合（…）</c>, or null when it cannot be told.</summary>
    public string? Value { get; }

    /// <summary>Every group the 區劃 holds, in 第3-3條's order; empty when blank or partial.</summary>
    public IReadOnlyList<string> Groups { get; }

    /// <summary>Why <see cref="Value"/> is null for a <see cref="ZoneUseGroupKind.Partial"/> 區劃.</summary>
    public string? Problem { get; }

    /// <summary>How many Areas left it blank; nonzero only for <see cref="ZoneUseGroupKind.Partial"/>.</summary>
    public int BlankCount { get; }

    /// <summary>
    /// What a panel row says about the whole 區劃 it belongs to, or null when the 區劃 is this one Area
    /// (the row's own reading already says it all) or nothing is filled. A row alone can only say what
    /// its own box reads as, which misleads exactly when another Area of the 區劃 disagrees or is blank.
    /// </summary>
    public string? PanelNote(int areaCount)
    {
        if (areaCount <= 1) return null;
        return Kind switch
        {
            ZoneUseGroupKind.Partial =>
                $"本區劃共 {areaCount} 塊面積，其中 {BlankCount} 塊未填用途類組，檢討會判「資料不足」。",
            ZoneUseGroupKind.Mixed =>
                $"本區劃共 {areaCount} 塊面積，整個區劃讀作 {Value}：各類組之規定均應符合，不適用Ｈ－２組的放寬。",
            ZoneUseGroupKind.Single => $"本區劃共 {areaCount} 塊面積，整個區劃讀作 {Value}。",
            _ => null
        };
    }

    public override string ToString() => Value ?? Problem ?? "（未填）";
}
