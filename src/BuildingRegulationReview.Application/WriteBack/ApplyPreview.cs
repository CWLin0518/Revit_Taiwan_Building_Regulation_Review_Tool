using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using BuildingRegulationReview.Domain.Geometry;
using BuildingRegulationReview.Domain.Regions;

namespace BuildingRegulationReview.Application.WriteBack;

/// <summary>What applying the drafts would do to one element.</summary>
public enum ApplyChangeKind
{
    Add,
    Update,
    Delete,
    Unchanged
}

/// <summary>One line of the preview list (spec 10.4).</summary>
public sealed class ApplyPreviewItem
{
    public ApplyPreviewItem(
        ApplyChangeKind change,
        ManagedElementKey key,
        string description,
        string? elementUniqueId = null,
        string? zoneName = null,
        PlannedElement? planned = null)
    {
        if (!Enum.IsDefined(typeof(ApplyChangeKind), change)) throw new ArgumentOutOfRangeException(nameof(change));
        if (string.IsNullOrWhiteSpace(description)) throw new ArgumentException("A preview item needs a description.", nameof(description));

        Change = change;
        Key = key;
        Description = description.Trim();
        ElementUniqueId = string.IsNullOrWhiteSpace(elementUniqueId) ? null : elementUniqueId!.Trim();
        ZoneName = string.IsNullOrWhiteSpace(zoneName) ? null : zoneName!.Trim();
        Planned = planned;
    }

    public ApplyChangeKind Change { get; }
    public ManagedElementKey Key { get; }
    public ManagedElementKind Kind => Key.Kind;
    public string Description { get; }

    /// <summary>The element that would be updated or deleted; null for something not yet created.</summary>
    public string? ElementUniqueId { get; }

    public string? ZoneName { get; }

    /// <summary>
    /// What the draft says this element should be — geometry, placement, name and colour. Null on a
    /// 刪除 row, which is the one case where the draft no longer plans anything. Carrying it here is
    /// what lets the write-back execute the preview the user approved, instead of re-deriving a second
    /// plan that could differ from the one that was shown.
    /// </summary>
    public PlannedElement? Planned { get; }

    /// <summary>The list line: 新增／更新／刪除／不變 followed by what it is.</summary>
    public string Text => ChangeText(Change) + " " + Description;

    public static string ChangeText(ApplyChangeKind change) => change switch
    {
        ApplyChangeKind.Add => "新增",
        ApplyChangeKind.Update => "更新",
        ApplyChangeKind.Delete => "刪除",
        ApplyChangeKind.Unchanged => "不變",
        _ => throw new ArgumentOutOfRangeException(nameof(change))
    };

    public override string ToString() => Text;
}

/// <summary>
/// The difference between the 區劃 drafts and what this package has already written to the model
/// (spec 10.4). Deletion is confined to elements carrying this package's own ownership token:
/// anything else in the model — hand-drawn boundaries, another package's output, an element whose
/// token no longer parses — is counted as untouched and never appears in <see cref="Deleted"/>.
/// </summary>
public sealed class ApplyPreview
{
    private ApplyPreview(
        Guid packageId,
        IEnumerable<ApplyPreviewItem> items,
        int untouchedElementCount,
        IEnumerable<string> warnings,
        ZoneUseOperations? zoneUses = null)
    {
        PackageId = packageId;
        Items = new ReadOnlyCollection<ApplyPreviewItem>(items.ToList());
        UntouchedElementCount = untouchedElementCount;
        Warnings = new ReadOnlyCollection<string>(warnings.ToList());
        ZoneUses = zoneUses ?? ZoneUseOperations.Empty;

        Added = Filter(ApplyChangeKind.Add);
        Updated = Filter(ApplyChangeKind.Update);
        Deleted = Filter(ApplyChangeKind.Delete);
        Unchanged = Filter(ApplyChangeKind.Unchanged);
    }

    public Guid PackageId { get; }

    /// <summary>Every line, in key order: adds, updates, deletes and the elements left alone.</summary>
    public IReadOnlyList<ApplyPreviewItem> Items { get; }

    public IReadOnlyList<ApplyPreviewItem> Added { get; }
    public IReadOnlyList<ApplyPreviewItem> Updated { get; }
    public IReadOnlyList<ApplyPreviewItem> Deleted { get; }
    public IReadOnlyList<ApplyPreviewItem> Unchanged { get; }

    /// <summary>Elements in the model this package does not own. They are reported, never changed.</summary>
    public int UntouchedElementCount { get; }

    public IReadOnlyList<string> Warnings { get; }

    /// <summary>
    /// The 防火檢討_區劃用途 writes this run would make. They change no element and so appear in no
    /// row above, but they are still changes to the model and count towards <see cref="IsEmpty"/>.
    /// </summary>
    public ZoneUseOperations ZoneUses { get; }

    /// <summary>True when applying would change nothing, which is what a second run should report.</summary>
    public bool IsEmpty =>
        Added.Count == 0 && Updated.Count == 0 && Deleted.Count == 0 && ZoneUses.IsEmpty;

    public int ChangeCount => Added.Count + Updated.Count + Deleted.Count + ZoneUses.Count;

    public IEnumerable<ApplyPreviewItem> Of(ManagedElementKind kind) => Items.Where(i => i.Kind == kind);

    public int CountOf(ManagedElementKind kind, ApplyChangeKind change) =>
        Items.Count(i => i.Kind == kind && i.Change == change);

    /// <summary>One line per element kind: 面積邊界線 新增 12、更新 0、刪除 3。</summary>
    public IReadOnlyList<string> KindSummaries => new ReadOnlyCollection<string>(
        Enum.GetValues(typeof(ManagedElementKind))
            .Cast<ManagedElementKind>()
            .Select(kind => string.Format(
                CultureInfo.InvariantCulture,
                "{0}：新增 {1}、更新 {2}、刪除 {3}、不變 {4}",
                ManagedElementKey.Describe(kind),
                CountOf(kind, ApplyChangeKind.Add),
                CountOf(kind, ApplyChangeKind.Update),
                CountOf(kind, ApplyChangeKind.Delete),
                CountOf(kind, ApplyChangeKind.Unchanged)))
            .ToList());

    public string Summary
    {
        get
        {
            if (IsEmpty)
            {
                return string.Format(
                    CultureInfo.InvariantCulture,
                    "模型已經與草稿一致，套用不會變更任何元素（不變 {0} 個，未受管理 {1} 個）。",
                    Unchanged.Count,
                    UntouchedElementCount);
            }

            // A 用途-only run has no element changes at all, and saying 「新增 0、更新 0、刪除 0」
            // would read as nothing to do right next to an enabled 套用 button.
            var head = Added.Count == 0 && Updated.Count == 0 && Deleted.Count == 0
                ? string.Format(
                    CultureInfo.InvariantCulture,
                    "元素全部不變（{0} 個），未受管理 {1} 個不會被更動。",
                    Unchanged.Count,
                    UntouchedElementCount)
                : string.Format(
                    CultureInfo.InvariantCulture,
                    "將新增 {0} 個、更新 {1} 個、刪除 {2} 個元素；不變 {3} 個，未受管理 {4} 個不會被更動。",
                    Added.Count,
                    Updated.Count,
                    Deleted.Count,
                    Unchanged.Count,
                    UntouchedElementCount);

            return ZoneUses.Summary is string uses ? head + uses : head;
        }
    }

    /// <summary>
    /// Compares the drafts against the model. <paramref name="existing"/> is everything the adapter
    /// found carrying an ownership token; elements of another package are filtered out here, so a
    /// caller that over-reports cannot cause a foreign element to be deleted.
    /// </summary>
    public static ApplyPreview Build(
        Guid packageId,
        PlanRegionMap map,
        ZoneDraftSet zones,
        IEnumerable<ExistingManagedElement>? existing = null,
        GeometryTolerance? tolerance = null)
    {
        if (map is null) throw new ArgumentNullException(nameof(map));
        if (zones is null) throw new ArgumentNullException(nameof(zones));

        var planned = ZoneWritePlan.Build(packageId, map, zones, tolerance);
        var inventory = (existing ?? Array.Empty<ExistingManagedElement>()).Where(x => x is not null).ToList();

        var mine = new Dictionary<ManagedElementKey, ExistingManagedElement>();
        var untouched = 0;
        foreach (var element in inventory)
        {
            // Spec 10.4: only this package's own generated elements are ever in scope for deletion.
            if (!element.BelongsTo(packageId) || mine.ContainsKey(element.Key))
            {
                untouched++;
                continue;
            }

            mine[element.Key] = element;
        }

        var items = new List<ApplyPreviewItem>();
        var plannedKeys = new HashSet<ManagedElementKey>();

        foreach (var element in planned)
        {
            plannedKeys.Add(element.Key);
            if (!mine.TryGetValue(element.Key, out var current))
            {
                items.Add(new ApplyPreviewItem(ApplyChangeKind.Add, element.Key, element.Description, null, element.ZoneName, element));
                continue;
            }

            var change = string.Equals(current.Signature, element.Signature, StringComparison.Ordinal)
                ? ApplyChangeKind.Unchanged
                : ApplyChangeKind.Update;
            items.Add(new ApplyPreviewItem(change, element.Key, element.Description, current.ElementUniqueId, element.ZoneName, element));
        }

        foreach (var orphan in mine.Where(pair => !plannedKeys.Contains(pair.Key)).OrderBy(pair => pair.Key.ToToken(), StringComparer.Ordinal))
        {
            items.Add(new ApplyPreviewItem(
                ApplyChangeKind.Delete,
                orphan.Key,
                orphan.Value.Description,
                orphan.Value.ElementUniqueId));
        }

        return new ApplyPreview(packageId, items, untouched, Warn(map, zones), ZoneUseOperations.Build(planned, mine));
    }

    /// <summary>
    /// The same comparison with every 不變 row turned into 更新, so the write-back redraws all of
    /// this package's elements instead of trusting their stored signatures. It exists for a model
    /// whose elements drifted without their signatures changing — lines dragged by their end joins,
    /// an Area moved by hand — which a plain apply would call consistent and leave where they are.
    /// Additions and deletions are untouched, and so is the ownership rule: only rows that already
    /// name one of this package's elements are rewritten.
    /// </summary>
    public ApplyPreview ForRebuild() => new ApplyPreview(
        PackageId,
        Items.Select(item => item.Change == ApplyChangeKind.Unchanged
            ? new ApplyPreviewItem(ApplyChangeKind.Update, item.Key, item.Description, item.ElementUniqueId, item.ZoneName, item.Planned)
            : item),
        UntouchedElementCount,
        Warnings,
        // A rebuild redraws elements; it does not re-assert 用途. The operations already exclude the
        // Areas that agree with the draft, and rewriting those would make the review stale for nothing.
        ZoneUses);

    private static IEnumerable<string> Warn(PlanRegionMap map, ZoneDraftSet zones)
    {
        foreach (var zone in zones.Zones.Where(z => z.IsEmpty))
        {
            yield return string.Format(CultureInfo.InvariantCulture, "區劃「{0}」還沒有任何範圍，套用時會被略過。", zone.Name);
        }

        foreach (var zone in zones.Zones.Where(z => !z.IsEmpty))
        {
            var parts = map.ContiguousPartsOf(zone.FaceIds).Count;
            if (parts > 1)
            {
                yield return string.Format(
                    CultureInfo.InvariantCulture,
                    "區劃「{0}」分成 {1} 塊不相連，會建立 {1} 個各自獨立的面積。",
                    zone.Name,
                    parts);
            }
        }

        var unassigned = map.Faces.Count - zones.AssignedFaceCount;
        if (unassigned > 0)
        {
            yield return string.Format(CultureInfo.InvariantCulture, "還有 {0} 個範圍未指派給任何區劃，不會寫入模型。", unassigned);
        }

        var errors = map.Issues.Count(i => i.IsError);
        if (errors > 0)
        {
            yield return string.Format(CultureInfo.InvariantCulture, "求解仍有 {0} 則錯誤，建議先修正再套用。", errors);
        }
    }

    private IReadOnlyList<ApplyPreviewItem> Filter(ApplyChangeKind change) =>
        new ReadOnlyCollection<ApplyPreviewItem>(Items.Where(i => i.Change == change).ToList());
}
