using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Text;
using BuildingRegulationReview.Application.Checks;
using BuildingRegulationReview.Domain.Reviews;

namespace BuildingRegulationReview.Application.Reviews;

/// <summary>
/// The four states the 檢討表 actually reads in: the same four the 統計 column counts. Six states are
/// what the rules produce; four are what a reviewer filters by, because 資料不足、人工覆核 and 未檢討 are
/// all "someone still has to look at this".
/// </summary>
public enum ReviewStatusBand
{
    /// <summary>未符合 — at least something to fix.</summary>
    Fail,

    /// <summary>待確認: 資料不足、人工覆核、未檢討.</summary>
    Pending,

    /// <summary>符合.</summary>
    Pass,

    /// <summary>不適用.</summary>
    NotApplicable
}

public static class ReviewStatusBands
{
    /// <summary>The bands in the order the filter shows them: what needs work first.</summary>
    public static readonly IReadOnlyList<ReviewStatusBand> All = new ReadOnlyCollection<ReviewStatusBand>(new[]
    {
        ReviewStatusBand.Fail, ReviewStatusBand.Pending, ReviewStatusBand.Pass, ReviewStatusBand.NotApplicable
    });

    /// <summary>Which band one of the six states falls in (spec 11.7 統計).</summary>
    public static ReviewStatusBand Of(ReviewStatus status) => status switch
    {
        ReviewStatus.Fail => ReviewStatusBand.Fail,
        ReviewStatus.Pass => ReviewStatusBand.Pass,
        ReviewStatus.NotApplicable => ReviewStatusBand.NotApplicable,
        _ => ReviewStatusBand.Pending
    };

    public static string Label(ReviewStatusBand band) => band switch
    {
        ReviewStatusBand.Fail => ReviewStatusText.Label(ReviewStatus.Fail),
        ReviewStatusBand.Pending => ReviewVerdictText.Label(ReviewVerdict.Pending),
        ReviewStatusBand.Pass => ReviewStatusText.Label(ReviewStatus.Pass),
        ReviewStatusBand.NotApplicable => ReviewStatusText.Label(ReviewStatus.NotApplicable),
        _ => throw new ArgumentOutOfRangeException(nameof(band))
    };
}

/// <summary>
/// Which rows of the 檢討表 the panel shows (spec 11.7.2). A run of a real storey is hundreds of rows;
/// the reviewer works through one kind of problem at a time, so the panel filters by state, by 檢討項目,
/// by whether the row needs updating, and by free text over what the row actually says.
/// </summary>
/// <remarks>
/// Pure, like <see cref="ReviewTable"/> and <see cref="ReviewEntryReport"/>: it decides nothing about
/// the review itself, only which of its rows are on screen. Filtering never rebuilds the run and never
/// changes a 統計 — <see cref="ReviewTableSection.Counts"/> keeps counting every row of the section, and
/// the view says separately how many of them are shown.
/// </remarks>
public sealed class ReviewTableFilter
{
    /// <summary>The filter the panel opens with: every row, nothing typed.</summary>
    public static readonly ReviewTableFilter ShowEverything = new ReviewTableFilter(null, null, null, false);

    private readonly HashSet<ReviewStatusBand> _bands;
    private readonly IReadOnlyList<string> _terms;

    /// <param name="bands">The states to show; null or empty means every state.</param>
    /// <param name="checkType">One <see cref="ReviewCheckTypes"/> value, or null for every 檢討項目.</param>
    /// <param name="search">Free text; whitespace separates terms and every term must match.</param>
    /// <param name="staleOnly">Only rows the model or the rules moved under (spec 13.1).</param>
    public ReviewTableFilter(
        IEnumerable<ReviewStatusBand>? bands,
        string? checkType,
        string? search,
        bool staleOnly)
    {
        var chosen = bands is null ? new HashSet<ReviewStatusBand>() : new HashSet<ReviewStatusBand>(bands);
        _bands = chosen.Count == 0 ? new HashSet<ReviewStatusBand>(ReviewStatusBands.All) : chosen;
        CheckType = string.IsNullOrWhiteSpace(checkType) ? null : checkType!.Trim();
        Search = search?.Trim() ?? string.Empty;
        StaleOnly = staleOnly;
        _terms = new ReadOnlyCollection<string>(Search.Split(new[] { ' ', '\t', '　' }, StringSplitOptions.RemoveEmptyEntries));
    }

    /// <summary>The states on screen. Never empty: clearing every box shows everything, not nothing.</summary>
    public IEnumerable<ReviewStatusBand> Bands => ReviewStatusBands.All.Where(_bands.Contains);

    public string? CheckType { get; }
    public string Search { get; }
    public bool StaleOnly { get; }

    /// <summary>Whether anything is being hidden on purpose — what tells the panel to say so.</summary>
    public bool IsActive =>
        _bands.Count < ReviewStatusBands.All.Count || CheckType is not null || StaleOnly || _terms.Count > 0;

    public bool Shows(ReviewStatusBand band) => _bands.Contains(band);

    /// <summary>
    /// Whether one row is on screen. The cheap tests come first: the text of a row is only built when
    /// something was typed, because this runs over every row on every keystroke.
    /// </summary>
    public bool Matches(ReviewTableEntry entry, string? markNumber = null)
    {
        if (entry is null) throw new ArgumentNullException(nameof(entry));

        if (!_bands.Contains(ReviewStatusBands.Of(entry.EffectiveStatus))) return false;
        if (CheckType is not null && !string.Equals(entry.CheckType, CheckType, StringComparison.Ordinal)) return false;
        if (StaleOnly && !entry.IsStale) return false;
        if (_terms.Count == 0) return true;

        var text = SearchText(entry, markNumber);
        foreach (var term in _terms)
            if (text.IndexOf(term, StringComparison.OrdinalIgnoreCase) < 0) return false;
        return true;
    }

    /// <summary>The table as the panel should draw it: the same sections and groups, minus what is hidden.</summary>
    public ReviewTableView Apply(ReviewTable table, IReadOnlyDictionary<Guid, string>? markNumbers = null)
    {
        if (table is null) throw new ArgumentNullException(nameof(table));

        var marks = markNumbers ?? new Dictionary<Guid, string>();
        var shown = new HashSet<Guid>(table.Entries.Where(e => Matches(e, CurtainWallMarkNumbers.Of(marks, e.ResultId))).Select(e => e.ResultId));
        var sections = table.Sections
            .Select(section => new ReviewTableSectionView(section, ReviewTable.PrimaryGrouping(section.CheckType), shown))
            .ToList();
        var others = table.OtherEntries.Where(e => shown.Contains(e.ResultId));
        return new ReviewTableView(this, table, sections, others);
    }

    /// <summary>
    /// What a row is searched over: the line the tree shows — with the whole 說明, not the shortened one
    /// — plus everything a reviewer is likely to type that the line leaves out. Every element is
    /// searchable both by its element id and by its UniqueId, and by all of them, not just the first one
    /// the line names.
    /// </summary>
    private static string SearchText(ReviewTableEntry entry, string? markNumber)
    {
        var text = new StringBuilder(ReviewEntryReport.Headline(entry, markNumber, int.MaxValue));
        Add(text, ReviewTable.Title(entry.CheckType));
        Add(text, entry.CategoryLabel);
        Add(text, entry.TypeName);
        Add(text, entry.TypeKey);
        Add(text, entry.ZoneName);
        Add(text, entry.ZoneId);
        Add(text, entry.OpeningKind);
        Add(text, entry.JunctionLegalReference);
        Add(text, entry.ShaftRequirementLabel);
        Add(text, entry.LegalReference);
        Add(text, entry.RuleId);
        foreach (var uniqueId in entry.LocateUniqueIds)
        {
            Add(text, ReviewElementReference.Describe(uniqueId));
            Add(text, uniqueId);
        }
        return text.ToString();
    }

    private static void Add(StringBuilder text, string? part)
    {
        if (string.IsNullOrWhiteSpace(part)) return;
        text.Append(' ').Append(part);
    }
}

/// <summary>One group of a section with only the rows the filter shows.</summary>
public sealed class ReviewTableGroupView
{
    internal ReviewTableGroupView(ReviewTableGroup group, IEnumerable<ReviewTableEntry> entries)
    {
        Group = group;
        Entries = new ReadOnlyCollection<ReviewTableEntry>(entries.ToList());
    }

    public ReviewTableGroup Group { get; }

    /// <summary>The rows on screen, in the order the run produced them.</summary>
    public IReadOnlyList<ReviewTableEntry> Entries { get; }

    public int ShownCount => Entries.Count;
    public int TotalCount => Group.ResultIds.Count;
    public bool IsEmpty => Entries.Count == 0;

    /// <summary><c>顯示 2／5</c>, or nothing when the group is whole.</summary>
    public string ShownText => ReviewTableView.ShownSuffix(ShownCount, TotalCount);
}

/// <summary>One row of the 檢討表 with only the groups and rows the filter shows.</summary>
public sealed class ReviewTableSectionView
{
    internal ReviewTableSectionView(ReviewTableSection section, ReviewTableGrouping grouping, ICollection<Guid> shown)
    {
        Section = section;
        Grouping = grouping;
        var groups = new List<ReviewTableGroupView>();
        foreach (var group in section.GroupsBy(grouping))
        {
            var ids = new HashSet<Guid>(group.ResultIds);
            var entries = section.Entries.Where(e => ids.Contains(e.ResultId) && shown.Contains(e.ResultId)).ToList();
            if (entries.Count > 0) groups.Add(new ReviewTableGroupView(group, entries));
        }
        Groups = new ReadOnlyCollection<ReviewTableGroupView>(groups);
        ShownCount = section.Entries.Count(e => shown.Contains(e.ResultId));
    }

    public ReviewTableSection Section { get; }

    /// <summary>Which breakdown of the section's statistics the panel is showing it by.</summary>
    public ReviewTableGrouping Grouping { get; }

    public IReadOnlyList<ReviewTableGroupView> Groups { get; }

    /// <summary>
    /// How many of the section's rows the filter shows. It can exceed what the groups hold: a result
    /// whose evidence never said which group it belongs to is counted by the section and by nothing else.
    /// </summary>
    public int ShownCount { get; }

    public int TotalCount => Section.Entries.Count;
    public bool IsEmpty => ShownCount == 0;

    public string ShownText => ReviewTableView.ShownSuffix(ShownCount, TotalCount);
}

/// <summary>The whole table as one filter leaves it.</summary>
public sealed class ReviewTableView
{
    internal ReviewTableView(ReviewTableFilter filter, ReviewTable table, IEnumerable<ReviewTableSectionView> sections, IEnumerable<ReviewTableEntry> others)
    {
        Filter = filter;
        Table = table;
        Sections = new ReadOnlyCollection<ReviewTableSectionView>(sections.ToList());
        OtherEntries = new ReadOnlyCollection<ReviewTableEntry>(others.ToList());
        ShownCount = Sections.Sum(s => s.ShownCount) + OtherEntries.Count;
        TotalCount = table.Entries.Count();
    }

    public ReviewTableFilter Filter { get; }
    public ReviewTable Table { get; }

    /// <summary>
    /// Every row of the table, in spec 11.7 order, whether the filter left anything in it or not. A row
    /// the filter emptied is still here — the panel hides it, and only while a filter is on, so that an
    /// unfiltered table still reads 未檢討 for a check that produced nothing.
    /// </summary>
    public IReadOnlyList<ReviewTableSectionView> Sections { get; }

    public IReadOnlyList<ReviewTableEntry> OtherEntries { get; }

    public int ShownCount { get; }
    public int TotalCount { get; }
    public bool IsEmpty => ShownCount == 0;

    public IEnumerable<ReviewTableEntry> Entries =>
        Sections.SelectMany(s => s.Groups).SelectMany(g => g.Entries).Concat(OtherEntries);

    /// <summary>The one line beside the filter: how much of the table is on screen.</summary>
    public string Summary =>
        !Filter.IsActive
            ? string.Format(CultureInfo.InvariantCulture, "共 {0} 項", TotalCount)
            : ShownCount == 0
                ? "沒有符合篩選條件的項目"
                : string.Format(CultureInfo.InvariantCulture, "顯示 {0}／{1} 項", ShownCount, TotalCount);

    /// <summary><c>　顯示 2／5</c> when a heading covers more than it shows, and nothing when it does not.</summary>
    internal static string ShownSuffix(int shown, int total) =>
        shown >= total ? string.Empty : string.Format(CultureInfo.InvariantCulture, "　顯示 {0}／{1}", shown, total);
}
