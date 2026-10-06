using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;

namespace BuildingRegulationReview.Application.WriteBack;

/// <summary>
/// One Area whose 防火檢討_區劃用途 the drafts would change: which Area, what it says now and what it
/// should say.
/// </summary>
/// <remarks>
/// A 用途 change is not an element change. It alters no geometry and no signature, so the Area it
/// sits on is 不變 as far as <see cref="ApplyPreview"/> is concerned — and a run built only from
/// element steps would call the whole plan empty and write nothing at all. These operations are
/// therefore carried beside the steps rather than inside them, and <see cref="ApplyPlan.IsEmpty"/>
/// counts them, which is what makes 「只改用途」 a run that happens.
/// </remarks>
public sealed class ZoneUseOperation
{
    public ZoneUseOperation(
        ManagedElementKey key,
        string? zoneName,
        string? currentUse,
        string targetUse,
        string? elementUniqueId)
    {
        if (key.Kind != ManagedElementKind.Area)
            throw new ArgumentException("區劃用途 is only written onto an Area.", nameof(key));

        Key = key;
        ZoneName = string.IsNullOrWhiteSpace(zoneName) ? "區劃" : zoneName!.Trim();
        CurrentUse = currentUse;
        TargetUse = targetUse ?? throw new ArgumentNullException(nameof(targetUse));
        ElementUniqueId = string.IsNullOrWhiteSpace(elementUniqueId) ? null : elementUniqueId!.Trim();
    }

    public ManagedElementKey Key { get; }

    public string ZoneName { get; }

    /// <summary>
    /// What the Area carries now; empty when the field is blank, and null when the Area does not
    /// exist yet or its parameter could not be read.
    /// </summary>
    public string? CurrentUse { get; }

    /// <summary>What to write. Empty is the explicit 一般區劃 that clears the field.</summary>
    public string TargetUse { get; }

    /// <summary>The Area to write on, or null when this run is creating it.</summary>
    public string? ElementUniqueId { get; }

    public bool IsClear => TargetUse.Length == 0;

    /// <summary>The preview line, naming both ends so the user can see what is being replaced.</summary>
    public string Text => IsClear
        ? string.Format(
            CultureInfo.InvariantCulture,
            "清除「{0}」的區劃用途（原為「{1}」，改為一般區劃）",
            ZoneName,
            CurrentUse)
        : string.Format(
            CultureInfo.InvariantCulture,
            "將「{0}」的區劃用途設為「{1}」{2}",
            ZoneName,
            TargetUse,
            string.IsNullOrEmpty(CurrentUse) ? string.Empty : string.Format(
                CultureInfo.InvariantCulture, "（原為「{0}」）", CurrentUse));

    public override string ToString() => Text;
}

/// <summary>
/// Every 區劃用途 write one run would make, worked out once from the drafts and the model so the
/// preview, the plan and the write-back all describe the same thing.
/// </summary>
public sealed class ZoneUseOperations : IReadOnlyList<ZoneUseOperation>
{
    public static readonly ZoneUseOperations Empty = new ZoneUseOperations(Array.Empty<ZoneUseOperation>());

    private readonly ReadOnlyCollection<ZoneUseOperation> _items;

    private ZoneUseOperations(IEnumerable<ZoneUseOperation> items) =>
        _items = new ReadOnlyCollection<ZoneUseOperation>(items.ToList());

    public int Count => _items.Count;
    public ZoneUseOperation this[int index] => _items[index];
    public bool IsEmpty => _items.Count == 0;

    public IEnumerator<ZoneUseOperation> GetEnumerator() => _items.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <summary>One line for the preview, or null when there is nothing to say.</summary>
    public string? Summary => IsEmpty
        ? null
        : string.Format(
            CultureInfo.InvariantCulture,
            "另外會設定 {0} 個面積的區劃用途（其中清除 {1} 個）。",
            Count,
            _items.Count(op => op.IsClear));

    /// <summary>
    /// Works out which Areas need writing. A draft that says nothing about 用途 — the mixed zone, the
    /// zone whose parameter could not be read, the zone nobody touched — produces no operation at
    /// all, which is the whole protection against a reopened Editor clearing what the 批次設定面板
    /// set. An Area that already carries the target value produces none either: 同值不重寫, because a
    /// pointless write is still a model change and still makes the review stale.
    /// </summary>
    public static ZoneUseOperations Build(
        IEnumerable<PlannedElement> planned,
        IReadOnlyDictionary<ManagedElementKey, ExistingManagedElement> existing)
    {
        if (planned is null) throw new ArgumentNullException(nameof(planned));
        if (existing is null) throw new ArgumentNullException(nameof(existing));

        var operations = new List<ZoneUseOperation>();

        foreach (var element in planned.Where(p => p.Key.Kind == ManagedElementKind.Area))
        {
            var target = element.ZoneUse;
            if (target is null) continue;

            if (!existing.TryGetValue(element.Key, out var current))
            {
                // An Area this run creates starts blank, so only a use worth writing is an operation.
                if (target.Length > 0)
                    operations.Add(new ZoneUseOperation(element.Key, element.ZoneName, null, target, null));
                continue;
            }

            // Nothing to clear on an Area whose parameter cannot be read: reporting that as a failed
            // write would blame the user for a field they never filled.
            if (current.ZoneUse is null && target.Length == 0) continue;
            if (string.Equals(current.ZoneUse, target, StringComparison.Ordinal)) continue;

            operations.Add(new ZoneUseOperation(
                element.Key,
                element.ZoneName,
                current.ZoneUse,
                target,
                current.ElementUniqueId));
        }

        return operations.Count == 0
            ? Empty
            : new ZoneUseOperations(operations.OrderBy(op => op.Key.ToToken(), StringComparer.Ordinal));
    }
}
