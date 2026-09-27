using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using BuildingRegulationReview.Application.Candidates;
using BuildingRegulationReview.Application.Checks;
using BuildingRegulationReview.Domain.Reviews;

namespace BuildingRegulationReview.Application.Reviews;

/// <summary>
/// The overall answer of the review table (spec 11.7 總狀態規則). It is deliberately coarser than the
/// six result states: a reader of the table wants to know whether there is a problem, whether
/// something still has to be confirmed, or whether it is clear.
/// </summary>
public enum ReviewVerdict
{
    /// <summary>Nothing to judge: the run did not complete, or it produced no result at all.</summary>
    NotReviewed,

    /// <summary>Every result is Pass or NotApplicable.</summary>
    Pass,

    /// <summary>No Fail, but something is InsufficientData, ManualReview or NotRun.</summary>
    Pending,

    /// <summary>At least one Fail.</summary>
    Fail,

    /// <summary>
    /// The model or the rule set moved since the run (spec 13.1). A stale verdict is not shown as if
    /// it were current, whatever it said (spec 7: 下游狀態必須標示為需更新).
    /// </summary>
    NeedsUpdate
}

public static class ReviewVerdictText
{
    public static string Label(ReviewVerdict verdict) => verdict switch
    {
        ReviewVerdict.NotReviewed => "未檢討",
        ReviewVerdict.Pass => "符合",
        ReviewVerdict.Pending => "待確認",
        ReviewVerdict.Fail => "未符合",
        ReviewVerdict.NeedsUpdate => "需更新",
        _ => throw new ArgumentOutOfRangeException(nameof(verdict))
    };
}

/// <summary>How the table folds many result states into one (spec 11.7).</summary>
public static class ReviewStatusAggregation
{
    /// <summary>
    /// The six-state status of a group of results, the 狀態 column of one table row. The worst state
    /// wins, in the order Fail, InsufficientData, ManualReview, NotRun, Pass, NotApplicable; a row with
    /// nothing in it is NotRun.
    /// </summary>
    /// <remarks>
    /// InsufficientData ranks above ManualReview because it is the one the user can usually fix by
    /// filling in a parameter; both are 待確認 in the verdict. Pass ranks above NotApplicable so a row
    /// with one real pass among exemptions reads 符合 rather than 不適用.
    /// </remarks>
    public static ReviewStatus Row(IEnumerable<ReviewStatus> statuses)
    {
        if (statuses is null) throw new ArgumentNullException(nameof(statuses));

        var present = new HashSet<ReviewStatus>(statuses);
        foreach (var status in RowOrder)
            if (present.Contains(status)) return status;
        return ReviewStatus.NotRun;
    }

    /// <summary>
    /// Spec 11.7 總狀態規則：任何 Fail 則為未符合；無 Fail 但有資料不足或人工覆核則為待確認；其餘檢查
    /// 皆 Pass／NotApplicable 才可顯示符合. A NotRun result is not a pass either, so it keeps the verdict
    /// at 待確認; no result at all is 未檢討.
    /// </summary>
    public static ReviewVerdict Verdict(IEnumerable<ReviewStatus> statuses)
    {
        if (statuses is null) throw new ArgumentNullException(nameof(statuses));

        var list = statuses.ToList();
        if (list.Count == 0) return ReviewVerdict.NotReviewed;
        if (list.Contains(ReviewStatus.Fail)) return ReviewVerdict.Fail;
        return list.All(s => s == ReviewStatus.Pass || s == ReviewStatus.NotApplicable)
            ? ReviewVerdict.Pass
            : ReviewVerdict.Pending;
    }

    private static readonly ReviewStatus[] RowOrder =
    {
        ReviewStatus.Fail,
        ReviewStatus.InsufficientData,
        ReviewStatus.ManualReview,
        ReviewStatus.NotRun,
        ReviewStatus.Pass,
        ReviewStatus.NotApplicable
    };
}

/// <summary>How many results of a group sit in each of the six states, counted on the effective status.</summary>
public sealed class ReviewStatusCounts
{
    private readonly IReadOnlyDictionary<ReviewStatus, int> _counts;

    public ReviewStatusCounts(IEnumerable<ReviewStatus> statuses)
    {
        if (statuses is null) throw new ArgumentNullException(nameof(statuses));

        var counts = Enum.GetValues(typeof(ReviewStatus)).Cast<ReviewStatus>().ToDictionary(s => s, _ => 0);
        foreach (var status in statuses) counts[status]++;
        _counts = new ReadOnlyDictionary<ReviewStatus, int>(counts);
    }

    public int this[ReviewStatus status] => _counts[status];

    public int Total => _counts.Values.Sum();
    public int Pass => this[ReviewStatus.Pass];
    public int Fail => this[ReviewStatus.Fail];

    /// <summary>The 統計 column's Unknown: 資料不足, 人工覆核 and 未檢討 — neither a pass nor a fail.</summary>
    public int Unknown => this[ReviewStatus.InsufficientData] + this[ReviewStatus.ManualReview] + this[ReviewStatus.NotRun];

    public int NotApplicable => this[ReviewStatus.NotApplicable];

    /// <summary><c>符合 3／未符合 1／待確認 2／不適用 0</c>, the one-line 統計 of a row.</summary>
    public string Text =>
        $"{ReviewStatusText.Label(ReviewStatus.Pass)} {Pass}／{ReviewStatusText.Label(ReviewStatus.Fail)} {Fail}／" +
        $"{ReviewVerdictText.Label(ReviewVerdict.Pending)} {Unknown}／{ReviewStatusText.Label(ReviewStatus.NotApplicable)} {NotApplicable}";
}

/// <summary>What a table row breaks its statistics down by (spec 11.7 統計 column).</summary>
public enum ReviewTableGrouping
{
    /// <summary>防火區劃面積: one group per 區劃.</summary>
    Zone,

    /// <summary>構件防火時效: one group per category (牆、柱、梁、樓板).</summary>
    Category,

    /// <summary>構件防火時效: one group per category and Type — the legend of spec 11.5 item 7 folds the same way.</summary>
    Type,

    /// <summary>防火門窗: 門、窗、幕牆.</summary>
    OpeningKind,

    /// <summary>帷幕牆區劃交接: the three rows of 帷幕牆規格 §7.2 — 水平、層間、其他部分.</summary>
    JunctionKind,

    /// <summary>帷幕牆區劃交接: one group per kind and 區劃來源條文, which is how CW-H splits 第79條 from 第83條.</summary>
    JunctionLegalReference,

    /// <summary>
    /// 垂直區劃: the three rows of 第79條之2第1項 (垂直區劃規格 §7), in 條文 order. A 管道間維修門 owes two
    /// of them at once, so grouping by element would show the same door twice with nothing to tell the
    /// lines apart; grouping by requirement is what makes the two results readable.
    /// </summary>
    ShaftRequirement
}

/// <summary>One line of a row's statistics.</summary>
public sealed class ReviewTableGroup
{
    public ReviewTableGroup(ReviewTableGrouping grouping, string key, string label, IEnumerable<ReviewTableEntry> entries)
    {
        if (!Enum.IsDefined(typeof(ReviewTableGrouping), grouping)) throw new ArgumentOutOfRangeException(nameof(grouping));
        if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("A group needs a key.", nameof(key));
        if (entries is null) throw new ArgumentNullException(nameof(entries));

        var list = entries.ToList();
        Grouping = grouping;
        Key = key.Trim();
        Label = string.IsNullOrWhiteSpace(label) ? Key : label.Trim();
        ResultIds = new ReadOnlyCollection<Guid>(list.Select(e => e.ResultId).ToList());
        Counts = new ReviewStatusCounts(list.Select(e => e.EffectiveStatus));
        Status = ReviewStatusAggregation.Row(list.Select(e => e.EffectiveStatus));
        StaleCount = list.Count(e => e.IsStale);
    }

    public ReviewTableGrouping Grouping { get; }

    /// <summary>A stable key: the zone ID, the category, the Type UniqueId or name, the opening kind.</summary>
    public string Key { get; }

    public string Label { get; }
    public IReadOnlyList<Guid> ResultIds { get; }
    public ReviewStatusCounts Counts { get; }
    public ReviewStatus Status { get; }
    public int StaleCount { get; }

    public override string ToString() => $"{Label}：{ReviewStatusText.Label(Status)}（{Counts.Text}）";
}

/// <summary>
/// One result as the table shows it when a row is expanded: what it is about, how to find it, what
/// it was judged against and whether it can still be relied on.
/// </summary>
public sealed class ReviewTableEntry
{
    internal ReviewTableEntry(
        ReviewResult result,
        ReviewStatus effectiveStatus,
        ReviewOverride? currentOverride,
        bool isStale,
        string? zoneName,
        string categoryLabel,
        string? typeKey,
        string? typeName,
        string? openingKind,
        CurtainWallJunctionKind? junctionKind,
        string? junctionLegalReference,
        VerticalCompartmentRequirement? shaftRequirement,
        string? shaftRequirementLabel,
        bool isLinked)
    {
        Result = result;
        EffectiveStatus = effectiveStatus;
        CurrentOverride = currentOverride;
        IsStale = isStale;
        ZoneName = zoneName;
        CategoryLabel = categoryLabel;
        TypeKey = typeKey;
        TypeName = typeName;
        OpeningKind = openingKind;
        JunctionKind = junctionKind;
        JunctionLegalReference = junctionLegalReference;
        ShaftRequirement = shaftRequirement;
        ShaftRequirementLabel = shaftRequirementLabel;
        IsLinked = isLinked;
    }

    public ReviewResult Result { get; }
    public Guid ResultId => Result.ResultId;
    public string CheckType => Result.CheckType;
    public string? ZoneId => Result.ZoneId;
    public string? ZoneName { get; }

    /// <summary>The computed status, before any override.</summary>
    public ReviewStatus ComputedStatus => Result.Status;

    /// <summary>What the table shows: an active override wins (spec 11.8, <see cref="ReviewRun.EffectiveStatus"/>).</summary>
    public ReviewStatus EffectiveStatus { get; }

    /// <summary>The override still attached to the result, active or awaiting confirmation.</summary>
    public ReviewOverride? CurrentOverride { get; }

    public bool IsOverridden => CurrentOverride is not null && CurrentOverride.IsActive;
    public bool OverrideNeedsReconfirmation => CurrentOverride is not null && !CurrentOverride.IsActive;

    /// <summary>The model or the rules this result depends on moved since the run (spec 13.1).</summary>
    public bool IsStale { get; }

    /// <summary>牆、柱、梁、樓板、門、窗、帷幕嵌板, or 區劃 for an area result; 未分類 when the evidence does not say.</summary>
    public string CategoryLabel { get; }

    /// <summary>The Type UniqueId, or its name when that is all the evidence has; null for a zone.</summary>
    public string? TypeKey { get; }

    public string? TypeName { get; }

    /// <summary>門、窗 or 幕牆, for an opening result.</summary>
    public string? OpeningKind { get; }

    /// <summary>Which of the three 帷幕牆 checks this is, for a 帷幕牆區劃交接 result; null otherwise.</summary>
    public CurtainWallJunctionKind? JunctionKind { get; }

    /// <summary>第79條／第83條／第79條之3 — the clause the junction's compartment came from (docs §2.5).</summary>
    public string? JunctionLegalReference { get; }

    /// <summary>
    /// Which of the three 第79條之2 requirements a 垂直區劃 result answers, as its evidence recorded
    /// <c>shaft.requirement</c>; null for every other check type.
    /// </summary>
    public VerticalCompartmentRequirement? ShaftRequirement { get; }

    /// <summary>
    /// The 檢討表 row name of that requirement, from the <c>shaft.requirementLabel</c> evidence the
    /// check stored — the table is rebuilt from the results alone and never reads the model again.
    /// </summary>
    public string? ShaftRequirementLabel { get; }

    /// <summary>The subject lives in a linked model, which the host view cannot select or override by element.</summary>
    public bool IsLinked { get; }

    /// <summary>
    /// What 定位／選取元素 goes to: the elements for a member or an opening, the Areas for a zone. A
    /// linked subject is still listed; the adapter decides what it can show of it.
    /// </summary>
    public IReadOnlyList<string> LocateUniqueIds => Result.SubjectUniqueIds;

    public ReviewValue? ActualValue => Result.ActualValue;
    public ReviewValue? RequiredValue => Result.RequiredValue;
    public string RuleId => Result.RuleId;
    public string RuleVersion => Result.RuleVersion;

    /// <summary>顯示條文: the clause the verdict came from.</summary>
    public string LegalReference => Result.LegalReference;

    public string Message => Result.Message;

    /// <summary>The status column of the expanded line, with what qualifies it.</summary>
    public string StatusText
    {
        get
        {
            var text = ReviewStatusText.Label(EffectiveStatus);
            if (IsOverridden) text += $"（人工覆寫，原為{ReviewStatusText.Label(ComputedStatus)}）";
            else if (OverrideNeedsReconfirmation) text += "（人工覆寫需重新確認）";
            if (IsStale) text += "〔需更新〕";
            return text;
        }
    }

    public override string ToString() => $"{CategoryLabel} {string.Join(",", LocateUniqueIds)}：{StatusText}";
}

/// <summary>One of the three rows of spec 11.7: 防火區劃面積、構件防火時效、防火門窗.</summary>
public sealed class ReviewTableSection
{
    internal ReviewTableSection(string checkType, IEnumerable<ReviewTableEntry> entries, bool runIsStale)
    {
        var list = entries.ToList();
        CheckType = checkType;
        Title = ReviewTable.Title(checkType);
        Entries = new ReadOnlyCollection<ReviewTableEntry>(list);
        Counts = new ReviewStatusCounts(list.Select(e => e.EffectiveStatus));
        Status = ReviewStatusAggregation.Row(list.Select(e => e.EffectiveStatus));
        StaleCount = list.Count(e => e.IsStale);
        Verdict = runIsStale || StaleCount > 0
            ? ReviewVerdict.NeedsUpdate
            : ReviewStatusAggregation.Verdict(list.Select(e => e.EffectiveStatus));
        Groups = new ReadOnlyCollection<ReviewTableGroup>(ReviewTable.GroupsFor(checkType, list).ToList());
    }

    public string CheckType { get; }
    public string Title { get; }

    /// <summary>The six-state 狀態 column.</summary>
    public ReviewStatus Status { get; }

    public ReviewVerdict Verdict { get; }
    public ReviewStatusCounts Counts { get; }
    public int StaleCount { get; }

    /// <summary>The 統計 breakdown: per zone, per category then per Type, or per opening kind.</summary>
    public IReadOnlyList<ReviewTableGroup> Groups { get; }

    /// <summary>What 展開 shows, in the order the run produced the results.</summary>
    public IReadOnlyList<ReviewTableEntry> Entries { get; }

    public IEnumerable<ReviewTableGroup> GroupsBy(ReviewTableGrouping grouping) => Groups.Where(g => g.Grouping == grouping);

    public override string ToString() => $"{Title}：{ReviewStatusText.Label(Status)}（{Counts.Text}）";
}

/// <summary>
/// The review table of spec 11.7, built from a stored run and, when the model has been read again, the
/// freshness of that run. Pure data: the WPF table, the drawings and the log all read the same thing.
/// </summary>
/// <remarks>
/// Every status is the <see cref="ReviewRun.EffectiveStatus"/>, so an active manual override counts
/// the way the reviewer decided and an override awaiting confirmation does not. A stale result keeps
/// its status in the expanded list — the user needs to see what it said — but it is flagged, and any
/// stale result turns the verdict of its row and of the table into 需更新.
/// </remarks>
public sealed class ReviewTable
{
    /// <summary>
    /// The rows, in the order spec 11.7 lists them, with 帷幕牆區劃交接 (帷幕牆規格 §7.2) and 垂直區劃
    /// (垂直區劃規格 §7) after them. They are always present, empty or not: a row with nothing in it
    /// reads 未檢討, which is what a package with no curtain wall — or one whose storey holds no
    /// 昇降機道 or 管道間 — actually is.
    /// </summary>
    /// <remarks>
    /// 區劃面積免除 follows 防火區劃面積 because it is the exception to that row and nothing else: the
    /// two carry the same <c>SubjectUniqueIds</c>, so expanding either points at the same batch of
    /// Areas (第79條之1文件 §7.1).
    /// </remarks>
    public static readonly IReadOnlyList<string> CheckTypes = new ReadOnlyCollection<string>(new[]
    {
        ReviewCheckTypes.CompartmentArea, ReviewCheckTypes.AreaExemption, ReviewCheckTypes.FireResistance,
        ReviewCheckTypes.OpeningProtection, ReviewCheckTypes.CompartmentContinuity,
        ReviewCheckTypes.VerticalCompartment
    });

    internal const string CurtainWallKind = "幕牆";
    private const string Unclassified = "未分類";
    private const string ZoneCategory = "區劃";

    private ReviewTable(ReviewRun run, ReviewRunFreshness? freshness, IEnumerable<ReviewTableSection> sections, IEnumerable<ReviewTableEntry> others)
    {
        Run = run;
        Freshness = freshness;
        Sections = new ReadOnlyCollection<ReviewTableSection>(sections.ToList());
        OtherEntries = new ReadOnlyCollection<ReviewTableEntry>(others.ToList());

        var all = Entries.ToList();
        IsStale = freshness is not null && freshness.IsStale;
        Verdict = run.State != ReviewRunState.Completed
            ? ReviewVerdict.NotReviewed
            : IsStale || all.Any(e => e.IsStale)
                ? ReviewVerdict.NeedsUpdate
                : ReviewStatusAggregation.Verdict(all.Select(e => e.EffectiveStatus));
        Counts = new ReviewStatusCounts(all.Select(e => e.EffectiveStatus));
    }

    public ReviewRun Run { get; }
    public Guid RunId => Run.RunId;
    public Guid PackageId => Run.PackageId;
    public string RuleSetId => Run.RuleSetId;
    public string RuleSetVersion => Run.RuleSetVersion;

    /// <summary>Null when the table was built without reading the model again.</summary>
    public ReviewRunFreshness? Freshness { get; }

    public bool IsStale { get; }

    /// <summary>Why the run is stale, for the banner above the table.</summary>
    public IReadOnlyList<string> StaleReasons => Freshness?.Reasons ?? (IReadOnlyList<string>)Array.Empty<string>();

    /// <summary>The overall 總狀態 of spec 11.7.</summary>
    public ReviewVerdict Verdict { get; }

    public ReviewStatusCounts Counts { get; }
    public IReadOnlyList<ReviewTableSection> Sections { get; }

    /// <summary>Results of a check type this table has no row for. Kept, counted, never dropped silently.</summary>
    public IReadOnlyList<ReviewTableEntry> OtherEntries { get; }

    public IEnumerable<ReviewTableEntry> Entries => Sections.SelectMany(s => s.Entries).Concat(OtherEntries);

    public ReviewTableSection Section(string checkType) =>
        Sections.FirstOrDefault(s => string.Equals(s.CheckType, checkType, StringComparison.Ordinal))
        ?? throw new ArgumentException($"The review table has no row for '{checkType}'.", nameof(checkType));

    public ReviewTableEntry? Entry(Guid resultId) => Entries.FirstOrDefault(e => e.ResultId == resultId);

    /// <summary>
    /// Builds the table. Pass the freshness of the run whenever the model has been read again since:
    /// without it the table can only show the run as it was stored.
    /// </summary>
    public static ReviewTable Build(ReviewRun run, ReviewRunFreshness? freshness = null)
    {
        if (run is null) throw new ArgumentNullException(nameof(run));
        if (freshness is not null && freshness.Run.RunId != run.RunId)
            throw new ArgumentException("The freshness was evaluated for another run.", nameof(freshness));

        var zoneNames = ZoneNames(run);
        var entries = run.Results.Select(r => ToEntry(run, r, freshness, zoneNames)).ToList();
        var runIsStale = freshness is not null && freshness.InvalidatesAll;

        var sections = CheckTypes.Select(type => new ReviewTableSection(
            type, entries.Where(e => string.Equals(e.CheckType, type, StringComparison.Ordinal)), runIsStale));
        var others = entries.Where(e => !CheckTypes.Contains(e.CheckType, StringComparer.Ordinal));
        return new ReviewTable(run, freshness, sections, others);
    }

    public static string Title(string checkType) => checkType switch
    {
        ReviewCheckTypes.CompartmentArea => "防火區劃面積",
        ReviewCheckTypes.AreaExemption => "區劃面積免除（第79條之1）",
        ReviewCheckTypes.FireResistance => "構件防火時效",
        ReviewCheckTypes.OpeningProtection => "防火門窗",
        ReviewCheckTypes.CompartmentContinuity => "帷幕牆區劃交接",
        ReviewCheckTypes.VerticalCompartment => "垂直區劃",
        _ => checkType
    };

    internal static IEnumerable<ReviewTableGroup> GroupsFor(string checkType, IReadOnlyList<ReviewTableEntry> entries)
    {
        switch (checkType)
        {
            // 第79條之1 is counted per 區劃 like the area row it excepts — one 區劃, one result, so the
            // two rows' statistics line up (第79條之1文件 §5.1).
            case ReviewCheckTypes.CompartmentArea:
            case ReviewCheckTypes.AreaExemption:
                return Group(ReviewTableGrouping.Zone, entries, e => e.ZoneId ?? Unclassified, e => e.ZoneName ?? e.ZoneId ?? Unclassified);

            case ReviewCheckTypes.FireResistance:
                return Group(ReviewTableGrouping.Category, entries, e => e.CategoryLabel, e => e.CategoryLabel)
                    .Concat(Group(ReviewTableGrouping.Type, entries,
                        e => e.CategoryLabel + "/" + (e.TypeKey ?? Unclassified),
                        e => e.CategoryLabel + "：" + (e.TypeName ?? e.TypeKey ?? "未知類型")));

            case ReviewCheckTypes.OpeningProtection:
                return Group(ReviewTableGrouping.OpeningKind, entries, e => e.OpeningKind ?? Unclassified, e => e.OpeningKind ?? Unclassified);

            // 帷幕牆規格 §7.2: the three kinds are the rows, and CW-H is counted per 區劃來源條文 so a
            // reviewer can tell a 第79條 junction from a 第83條 one (docs §2.5).
            case ReviewCheckTypes.CompartmentContinuity:
                return Group(ReviewTableGrouping.JunctionKind, entries, e => e.CategoryLabel, e => e.CategoryLabel)
                    .Concat(Group(ReviewTableGrouping.JunctionLegalReference,
                        entries.Where(e => e.JunctionLegalReference is not null).ToList(),
                        e => e.CategoryLabel + "/" + e.JunctionLegalReference,
                        e => e.CategoryLabel + "：" + e.JunctionLegalReference));

            // 垂直區劃規格 §7: the three requirements of 第79條之2第1項 are the rows, always read in
            // 條文 order rather than in the order the run happened to meet them, because a 管道間's two
            // rows and a 昇降機道's one row otherwise interleave differently per storey.
            case ReviewCheckTypes.VerticalCompartment:
                return Group(ReviewTableGrouping.ShaftRequirement, entries,
                        e => e.ShaftRequirement is VerticalCompartmentRequirement r
                            ? VerticalCompartmentRequirements.RuleText(r)
                            : Unclassified,
                        e => e.ShaftRequirementLabel ?? Unclassified)
                    .OrderBy(g => VerticalCompartmentRequirements.Order(g.Key));

            default:
                return Enumerable.Empty<ReviewTableGroup>();
        }
    }

    /// <summary>Groups in order of first appearance, so the statistics read in the order the run did.</summary>
    private static IEnumerable<ReviewTableGroup> Group(
        ReviewTableGrouping grouping,
        IReadOnlyList<ReviewTableEntry> entries,
        Func<ReviewTableEntry, string> key,
        Func<ReviewTableEntry, string> label) =>
        entries.GroupBy(key, StringComparer.Ordinal)
            .Select(g => new ReviewTableGroup(grouping, g.Key, label(g.First()), g));

    /// <summary>
    /// Zone names come from the area results of the same run: every zone the run saw has one, even a
    /// zone that was withheld, and each records <c>zone.name</c>.
    /// </summary>
    private static IReadOnlyDictionary<string, string> ZoneNames(ReviewRun run)
    {
        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var result in run.Results)
        {
            if (result.ZoneId is null || names.ContainsKey(result.ZoneId)) continue;
            var name = result.Evidence.Find("zone.name");
            if (name is not null && name.Kind == ReviewValueKind.Text && name.Text.Length > 0) names[result.ZoneId] = name.Text;
        }
        return names;
    }

    private static ReviewTableEntry ToEntry(ReviewRun run, ReviewResult result, ReviewRunFreshness? freshness, IReadOnlyDictionary<string, string> zoneNames)
    {
        var evidence = result.Evidence;
        var category = CategoryOf(evidence);
        var junctionKind = JunctionKindOf(evidence);
        var shaftRequirement = ShaftRequirementOf(evidence);

        // Whose subject is a 區劃 rather than an element: every 區劃面積 result, every 第79條之1 one, and
        // 第79條之2第3項, whose subject is the 挑空 itself. None of them has a category or a Type to show
        // (垂直區劃 §3.6、第79條之1文件 §5.1).
        var isArea = string.Equals(result.CheckType, ReviewCheckTypes.CompartmentArea, StringComparison.Ordinal) ||
                     string.Equals(result.CheckType, ReviewCheckTypes.AreaExemption, StringComparison.Ordinal) ||
                     shaftRequirement == VerticalCompartmentRequirement.AtriumExemption;

        var typeUniqueId = TextOf(evidence, "source.typeUniqueId");
        var typeName = TextOf(evidence, "source.typeName");
        string? openingKind = null;
        if (category is CandidateCategory c && CandidateCategories.IsOpening(c))
        {
            var onCurtainWall = evidence.Find("source.hostIsCurtainWall") is { Kind: ReviewValueKind.Boolean, Flag: true };
            openingKind = c == CandidateCategory.CurtainPanel || onCurtainWall ? CurtainWallKind : CandidateCategories.Label(c);
        }

        return new ReviewTableEntry(
            result,
            run.EffectiveStatus(result),
            run.CurrentOverrideFor(result.ResultId),
            freshness is not null && (freshness.InvalidatesAll || freshness.IsResultStale(result.ResultId)),
            result.ZoneId is not null && zoneNames.TryGetValue(result.ZoneId, out var zoneName) ? zoneName : null,
            isArea ? ZoneCategory
                : junctionKind is CurtainWallJunctionKind kind ? CurtainWallJunctionKinds.Label(kind)
                : category is CandidateCategory known ? CandidateCategories.Label(known)
                : Unclassified,
            isArea ? null : typeUniqueId ?? typeName,
            isArea ? null : typeName,
            openingKind,
            junctionKind,
            TextOf(evidence, "junction.hostLegalReference"),
            shaftRequirement,
            shaftRequirement is VerticalCompartmentRequirement requirement
                ? TextOf(evidence, "shaft.requirementLabel") ?? VerticalCompartmentRequirements.Label(requirement)
                : TextOf(evidence, "shaft.requirementLabel"),
            evidence.Has("source.linkInstanceUniqueId"));
    }

    /// <summary>
    /// The 第79條之2 requirement a result answers, as its evidence recorded
    /// <see cref="VerticalCompartmentRequirements.RequirementField"/>.
    /// </summary>
    private static VerticalCompartmentRequirement? ShaftRequirementOf(ReviewEvidence evidence)
    {
        var text = TextOf(evidence, VerticalCompartmentRequirements.RequirementField);
        if (text is null) return null;
        foreach (var requirement in VerticalCompartmentRequirements.All)
            if (string.Equals(VerticalCompartmentRequirements.RuleText(requirement), text, StringComparison.Ordinal)) return requirement;
        return null;
    }

    /// <summary>The 帷幕牆 check a result belongs to, as its evidence recorded <c>junction.kind</c>.</summary>
    private static CurtainWallJunctionKind? JunctionKindOf(ReviewEvidence evidence)
    {
        var text = TextOf(evidence, "junction.kind");
        if (text is null) return null;
        foreach (var kind in CurtainWallJunctionKinds.All)
            if (string.Equals(CurtainWallJunctionKinds.RuleText(kind), text, StringComparison.Ordinal)) return kind;
        return null;
    }

    private static CandidateCategory? CategoryOf(ReviewEvidence evidence)
    {
        var text = TextOf(evidence, "source.category");
        if (text is null) return null;
        foreach (CandidateCategory category in Enum.GetValues(typeof(CandidateCategory)))
            if (string.Equals(CandidateCategories.RuleText(category), text, StringComparison.Ordinal)) return category;
        return null;
    }

    private static string? TextOf(ReviewEvidence evidence, string field)
    {
        var value = evidence.Find(field);
        return value is not null && value.Kind == ReviewValueKind.Text && value.Text.Length > 0 ? value.Text : null;
    }
}
