using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using BuildingRegulationReview.Application.Checks;
using BuildingRegulationReview.Domain.Reviews;

namespace BuildingRegulationReview.Application.Reviews;

/// <summary>
/// The drawing number a 未符合 帷幕牆交接 is referred to by — <c>CW-H-01</c> for a horizontal junction,
/// <c>CW-V-01</c> for a 層間帶 (帷幕牆規格 §7.1). It is what ties one row of the 檢討表 to the mark in
/// the plan and, for CW-V, to the section the tool generated for it: the row, the text note and the
/// elevation's name all carry the same number, so a reviewer reading the table can find the drawing.
/// </summary>
/// <remarks>
/// <para>
/// The numbers are assigned from the <see cref="ReviewTable"/> alone, not from the markup plan, so the
/// window can show the same number the marks carry without having to build a plan first — and so two
/// callers looking at the same run always agree. They are a per-run drawing reference, not an identity:
/// the identity of a junction is its <c>junction.id</c>, which is what a re-run matches marks on. A run
/// where one junction has been fixed therefore renumbers the ones after it, exactly as a drawing set
/// renumbers when a detail is dropped.
/// </para>
/// <para>
/// Only CW-H and CW-V are numbered. CW-O paints its panels red and writes no annotation, so a number
/// would have nothing to appear on and nothing to be matched against.
/// </para>
/// </remarks>
public static class CurtainWallMarkNumbers
{
    /// <summary>The prefix of a 區劃牆 × 帷幕牆 horizontal junction's number.</summary>
    public const string HorizontalPrefix = "CW-H";

    /// <summary>The prefix of a 層間帶 number — the one a generated section is named after.</summary>
    public const string SpandrelPrefix = "CW-V";

    /// <summary>How many numbers a generated view's name lists before it says "等 N 處" instead.</summary>
    private const int NamedInViewName = 3;

    private static readonly IReadOnlyDictionary<Guid, string> None =
        new ReadOnlyDictionary<Guid, string>(new Dictionary<Guid, string>());

    /// <summary>The prefix of a kind's numbers, or null for a kind that carries no number.</summary>
    public static string? Prefix(CurtainWallJunctionKind kind) => kind switch
    {
        CurtainWallJunctionKind.WallToCurtainWall => HorizontalPrefix,
        CurtainWallJunctionKind.FloorToCurtainWall => SpandrelPrefix,
        _ => null
    };

    /// <summary>
    /// The number of every 未符合 帷幕牆交接 in the run, by result. Ordered by the junction's own ID so
    /// the junctions of one curtain wall are numbered together — which is what keeps the name of the
    /// elevation they share short — and so the same run always numbers them the same way.
    /// </summary>
    public static IReadOnlyDictionary<Guid, string> Assign(ReviewTable table)
    {
        if (table is null) throw new ArgumentNullException(nameof(table));

        var numbered = new Dictionary<Guid, string>();
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);

        var marked = table.Entries
            .Where(e => e.EffectiveStatus == ReviewStatus.Fail && !e.IsStale && e.JunctionKind is not null)
            .Select(e => (Entry: e, Prefix: Prefix(e.JunctionKind!.Value), Junction: CurtainWallReviewMarks.JunctionId(e.Result.Evidence)))
            .Where(x => x.Prefix is not null && x.Junction is not null)
            .OrderBy(x => x.Prefix, StringComparer.Ordinal)
            .ThenBy(x => x.Junction, StringComparer.Ordinal)
            .ThenBy(x => x.Entry.ZoneId ?? string.Empty, StringComparer.Ordinal);

        foreach (var (entry, prefix, _) in marked)
        {
            counts.TryGetValue(prefix!, out var used);
            counts[prefix!] = ++used;

            // A result that somehow reached the table twice keeps the first number it was given.
            if (!numbered.ContainsKey(entry.ResultId)) numbered.Add(entry.ResultId, Format(prefix!, used));
        }

        return numbered.Count == 0 ? None : new ReadOnlyDictionary<Guid, string>(numbered);
    }

    /// <summary>The number of one result, or null when it has none (a pass, a stale row, CW-O).</summary>
    public static string? Of(IReadOnlyDictionary<Guid, string> numbers, Guid resultId)
    {
        if (numbers is null) throw new ArgumentNullException(nameof(numbers));
        return numbers.TryGetValue(resultId, out var number) ? number : null;
    }

    /// <summary><c>CW-V-01</c>. Two digits up to 99, then as many as the count needs.</summary>
    public static string Format(string prefix, int ordinal)
    {
        if (string.IsNullOrWhiteSpace(prefix)) throw new ArgumentException("A prefix is required.", nameof(prefix));
        if (ordinal < 1) throw new ArgumentOutOfRangeException(nameof(ordinal), ordinal, "A drawing number starts at 1.");

        return prefix + "-" + ordinal.ToString("00", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// The numbers a generated view's name carries. One elevation can hold the 層間帶 of several floors
    /// of the same curtain wall, and a name that listed twelve of them would be unreadable, so past
    /// three it says how many instead of naming them all.
    /// </summary>
    public static string Join(IEnumerable<string> numbers)
    {
        if (numbers is null) throw new ArgumentNullException(nameof(numbers));

        var ordered = numbers
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Select(n => n.Trim())
            .Distinct(StringComparer.Ordinal)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

        if (ordered.Count == 0) return string.Empty;
        if (ordered.Count <= NamedInViewName) return string.Join("、", ordered);

        return string.Format(CultureInfo.InvariantCulture, "{0}等{1}處", ordered[0], ordered.Count);
    }
}
