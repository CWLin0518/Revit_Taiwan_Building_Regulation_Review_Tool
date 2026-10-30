using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using BuildingRegulationReview.Domain.Regions;

namespace BuildingRegulationReview.Application.WriteBack;

/// <summary>What syncing the Area Color Scheme does to one of its entries.</summary>
public enum ColorSchemeChange
{
    /// <summary>The drafts have a 區劃 the scheme has no entry for.</summary>
    Add,

    /// <summary>The entry exists but is not the colour the Editor shows.</summary>
    Update,

    /// <summary>The entry is left over from a 區劃 that is gone and nothing uses it.</summary>
    Remove,

    /// <summary>The entry already says what the drafts say.</summary>
    Unchanged
}

/// <summary>
/// One colour entry as the drafts want it: the Area name the entry keys on, and the colour the
/// 區劃 is drawn with in the Editor.
/// </summary>
/// <remarks>
/// The scheme keys on the Area's name because that is the one parameter write-back already sets
/// (see <see cref="ZoneWritePlan"/>), so the colours on the plan and the colours in the Editor come
/// from one value instead of from two that have to be kept in step.
/// </remarks>
public sealed class ColorSchemeEntryPlan
{
    public ColorSchemeEntryPlan(string value, ZoneColor color)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("A colour entry needs a value.", nameof(value));

        Value = value.Trim();
        Color = color;
    }

    /// <summary>The Area name this entry colours.</summary>
    public string Value { get; }

    public ZoneColor Color { get; }

    public override string ToString() => Value + " " + Color.ToHex();
}

/// <summary>An entry the scheme holds right now, as the adapter read it.</summary>
public sealed class ExistingColorSchemeEntry
{
    public ExistingColorSchemeEntry(string value, ZoneColor color, bool isInUse = false, bool isColorKnown = true)
    {
        Value = (value ?? string.Empty).Trim();
        Color = color;
        IsInUse = isInUse;
        IsColorKnown = isColorKnown;
    }

    public string Value { get; }
    public ZoneColor Color { get; }

    /// <summary>
    /// False when the adapter could not read the colour — Revit hands back an invalid colour for an
    /// entry it has not resolved yet. An unreadable colour is never the colour the drafts want, so
    /// the entry is rewritten rather than compared against a made-up value.
    /// </summary>
    public bool IsColorKnown { get; }

    /// <summary>
    /// Whether an Area in the model still carries this value. Revit refuses to remove an entry that
    /// is in use, and one that is in use is very often somebody else's Area rather than a leftover.
    /// </summary>
    public bool IsInUse { get; }

    public override string ToString() => Value + " " + Color.ToHex() + (IsInUse ? "（使用中）" : string.Empty);
}

/// <summary>
/// The colours the current drafts call for, taken from the very preview the user approved rather
/// than recomputed, so the Area Plan and the colour scheme cannot disagree about what a 區劃 is.
/// </summary>
/// <remarks>
/// Every Area row counts, including the 不變 ones. A second run changes nothing in the Area Plan but
/// the scheme still has to say what all of its entries are — building this from the changes alone
/// would make an unchanged run look like a scheme with no zones left in it.
/// </remarks>
public sealed class ColorSchemeEntries
{
    private ColorSchemeEntries(IEnumerable<ColorSchemeEntryPlan> entries) =>
        Entries = new ReadOnlyCollection<ColorSchemeEntryPlan>(entries.ToList());

    public static readonly ColorSchemeEntries None = new ColorSchemeEntries(Array.Empty<ColorSchemeEntryPlan>());

    /// <summary>One entry per distinct 區劃 name, in name order so a re-run matches last time.</summary>
    public IReadOnlyList<ColorSchemeEntryPlan> Entries { get; }

    public bool IsEmpty => Entries.Count == 0;

    /// <summary>
    /// Reads the colours out of the preview's Area rows.
    /// </summary>
    /// <remarks>
    /// The rows are grouped by name because one 區劃 in several disconnected pieces gets one Area per
    /// piece, all carrying the same name and colour. Two different 區劃 cannot collide here:
    /// <see cref="ZoneDraftSet"/> refuses a duplicate name when the zone is created or renamed, which
    /// is the right place for that rule — it can say so to the user's face instead of surfacing much
    /// later as a colour nobody asked for.
    /// </remarks>
    public static ColorSchemeEntries From(ApplyPreview preview)
    {
        if (preview is null) throw new ArgumentNullException(nameof(preview));

        var entries = preview.Items
            .Where(item => item.Kind == ManagedElementKind.Area && item.Change != ApplyChangeKind.Delete)
            .Select(item => item.Planned)
            .Where(element => element is not null && !string.IsNullOrWhiteSpace(element!.ZoneName) && element.Color.HasValue)
            .Select(element => new { Name = element!.ZoneName!.Trim(), Color = element.Color!.Value })
            .GroupBy(row => row.Name, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => new ColorSchemeEntryPlan(group.Key, group.First().Color));

        return new ColorSchemeEntries(entries);
    }
}

/// <summary>One thing the sync does, ready for the adapter to carry out.</summary>
public sealed class ColorSchemeStep
{
    public ColorSchemeStep(ColorSchemeChange change, string value, ZoneColor color)
    {
        if (!Enum.IsDefined(typeof(ColorSchemeChange), change)) throw new ArgumentOutOfRangeException(nameof(change));
        if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("A colour step needs a value.", nameof(value));

        Change = change;
        Value = value.Trim();
        Color = color;
    }

    public ColorSchemeChange Change { get; }
    public string Value { get; }
    public ZoneColor Color { get; }

    public string Description => string.Format(
        CultureInfo.InvariantCulture,
        "色彩項目「{0}」（{1}）",
        Value,
        Color.ToHex());

    public string Text => ChangeText(Change) + " " + Description;

    public static string ChangeText(ColorSchemeChange change) => change switch
    {
        ColorSchemeChange.Add => "新增",
        ColorSchemeChange.Update => "更新",
        ColorSchemeChange.Remove => "刪除",
        ColorSchemeChange.Unchanged => "不變",
        _ => throw new ArgumentOutOfRangeException(nameof(change))
    };

    public override string ToString() => Text;
}

/// <summary>
/// The difference between the colours the drafts call for and the Area Color Scheme as it is now
/// (spec 10.5 item 3). Pure data: the adapter turns the steps into <c>AddEntry</c>／
/// <c>UpdateEntry</c>／<c>RemoveEntry</c> calls, so both the colours and the rule about what may be
/// removed can be asserted with no document open.
/// </summary>
/// <remarks>
/// An entry still in use is never removed. Revit refuses it, and an entry in use normally belongs to
/// an Area somebody else placed — the tool reports it instead, which is the same answer spec 10.5
/// item 3 asks for when the API will not do the whole job.
/// </remarks>
public sealed class ColorSchemePlan
{
    private ColorSchemePlan(
        IEnumerable<ColorSchemeEntryPlan> entries,
        IEnumerable<ColorSchemeStep> steps,
        IEnumerable<ManualAction> manualActions)
    {
        Entries = new ReadOnlyCollection<ColorSchemeEntryPlan>(entries.ToList());
        Steps = new ReadOnlyCollection<ColorSchemeStep>(steps.ToList());
        ManualActions = new ReadOnlyCollection<ManualAction>(manualActions.ToList());
    }

    /// <summary>Every entry the drafts call for, unchanged ones included.</summary>
    public IReadOnlyList<ColorSchemeEntryPlan> Entries { get; }

    /// <summary>The changes, with the unchanged entries kept so a run can report what it skipped.</summary>
    public IReadOnlyList<ColorSchemeStep> Steps { get; }

    public IReadOnlyList<ManualAction> ManualActions { get; }

    /// <summary>True when the scheme already says what the drafts say — what a second run reports.</summary>
    public bool IsEmpty => Steps.All(step => step.Change == ColorSchemeChange.Unchanged);

    public IReadOnlyList<ColorSchemeStep> StepsOf(ColorSchemeChange change) =>
        new ReadOnlyCollection<ColorSchemeStep>(Steps.Where(step => step.Change == change).ToList());

    public int CountOf(ColorSchemeChange change) => Steps.Count(step => step.Change == change);

    public string Summary => IsEmpty
        ? "面積色彩配置已經與區劃一致，不需要變更。"
        : string.Format(
            CultureInfo.InvariantCulture,
            "面積色彩配置新增 {0} 項、更新 {1} 項、刪除 {2} 項，不變 {3} 項。",
            CountOf(ColorSchemeChange.Add),
            CountOf(ColorSchemeChange.Update),
            CountOf(ColorSchemeChange.Remove),
            CountOf(ColorSchemeChange.Unchanged));

    /// <summary>
    /// Works out what has to change. <paramref name="existing"/> is what the adapter read back from
    /// the scheme; with nothing there every entry is an addition, which is the truth about a scheme
    /// the tool has not written to yet.
    /// </summary>
    public static ColorSchemePlan Build(
        ColorSchemeEntries entries,
        IEnumerable<ExistingColorSchemeEntry>? existing = null)
    {
        if (entries is null) throw new ArgumentNullException(nameof(entries));

        var manual = new List<ManualAction>();
        var current = new Dictionary<string, ExistingColorSchemeEntry>(StringComparer.Ordinal);

        foreach (var entry in (existing ?? Array.Empty<ExistingColorSchemeEntry>()).Where(e => e is not null))
        {
            if (entry.Value.Length == 0) continue;
            current[entry.Value] = entry;
        }

        var steps = new List<ColorSchemeStep>();
        var plannedValues = new HashSet<string>(entries.Entries.Select(entry => entry.Value), StringComparer.Ordinal);

        foreach (var entry in entries.Entries)
        {
            if (!current.TryGetValue(entry.Value, out var now))
            {
                steps.Add(new ColorSchemeStep(ColorSchemeChange.Add, entry.Value, entry.Color));
                continue;
            }

            steps.Add(new ColorSchemeStep(
                now.IsColorKnown && now.Color == entry.Color ? ColorSchemeChange.Unchanged : ColorSchemeChange.Update,
                entry.Value,
                entry.Color));
        }

        foreach (var orphan in current.Values
                     .Where(entry => !plannedValues.Contains(entry.Value))
                     .OrderBy(entry => entry.Value, StringComparer.Ordinal))
        {
            if (orphan.IsInUse)
            {
                manual.Add(new ManualAction(
                    string.Format(CultureInfo.InvariantCulture, "色彩項目「{0}」", orphan.Value),
                    "已經沒有對應的區劃，但模型裡還有面積在用這個名稱，Revit 不允許刪除使用中的色彩項目",
                    "請先確認那些面積是否應該改名或刪除，再到「色彩配置」對話框移除這個項目"));
                continue;
            }

            steps.Add(new ColorSchemeStep(ColorSchemeChange.Remove, orphan.Value, orphan.Color));
        }

        return new ColorSchemePlan(entries.Entries, steps, manual);
    }
}
