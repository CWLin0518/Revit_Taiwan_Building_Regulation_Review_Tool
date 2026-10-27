using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using BuildingRegulationReview.Application.Reviews;

namespace BuildingRegulationReview.Application.WriteBack;

/// <summary>
/// 區劃層級、但存在每一塊 Area 上的參數（用途以外的那四個）。檢討把同一區劃的各 Area 讀成一個值，
/// 填得不一致就是讀不懂（<c>ReviewParameterSnapshot.ZoneInput</c>），所以編輯器新建一塊 Area 時不能讓它空著。
/// </summary>
public static class ZoneAreaParameters
{
    /// <summary>The four, in the order the preview lists them. All are Integer or Yes/No in Revit.</summary>
    public static IReadOnlyList<string> All { get; } = new ReadOnlyCollection<string>(new[]
    {
        ReviewInputSources.FloorNumber,
        ReviewInputSources.Sprinklered,
        ReviewInputSources.LinksRefugeFloor,
        ReviewInputSources.CannotBeSubdivided
    });

    /// <summary>How a stored integer reads to a person: 樓層序 as a number, the rest as 是／否.</summary>
    public static string Display(string parameter, int value) =>
        parameter == ReviewInputSources.FloorNumber
            ? value.ToString(CultureInfo.InvariantCulture)
            : value != 0 ? "是" : "否";

    /// <summary>The parameter name without the 防火檢討_ prefix, for messages.</summary>
    public static string Label(string parameter) =>
        parameter.StartsWith("防火檢討_", StringComparison.Ordinal) ? parameter.Substring("防火檢討_".Length) : parameter;
}

/// <summary>One value a new Area takes over from the Areas its 區劃 already has.</summary>
public sealed class InheritedZoneValue
{
    public InheritedZoneValue(string parameter, int value)
    {
        Parameter = parameter ?? throw new ArgumentNullException(nameof(parameter));
        Value = value;
    }

    public string Parameter { get; }
    public int Value { get; }
    public string Text => ZoneAreaParameters.Label(Parameter) + "＝" + ZoneAreaParameters.Display(Parameter, Value);
}

/// <summary>
/// 驗證清單 C-22：一塊這次要新建的 Area，從同區劃既有的 Area 沿用哪些參數，以及哪些沿用不了。
/// </summary>
public sealed class ZoneParameterInheritanceOperation
{
    public ZoneParameterInheritanceOperation(
        ManagedElementKey key,
        string? zoneName,
        IEnumerable<InheritedZoneValue> values,
        IEnumerable<string> unresolved)
    {
        if (key.Kind != ManagedElementKind.Area)
            throw new ArgumentException("Zone parameters live on an Area.", nameof(key));

        Key = key;
        ZoneName = string.IsNullOrWhiteSpace(zoneName) ? "區劃" : zoneName!.Trim();
        Values = new ReadOnlyCollection<InheritedZoneValue>((values ?? throw new ArgumentNullException(nameof(values))).ToList());
        Unresolved = new ReadOnlyCollection<string>((unresolved ?? throw new ArgumentNullException(nameof(unresolved))).ToList());
    }

    public ManagedElementKey Key { get; }
    public string ZoneName { get; }

    /// <summary>「區劃名」, or 「區劃名」第 n 塊 from the second part on — the name messages use.</summary>
    public string QuotedName => ZonePartName.Quoted(ZoneName, Key.PartIndex);

    /// <summary>Values every existing Area of the 區劃 agrees on, to be written onto the new one.</summary>
    public IReadOnlyList<InheritedZoneValue> Values { get; }

    /// <summary>
    /// Parameters the existing Areas do not agree on (or only some of them fill). Nothing is written
    /// for these — picking one would be the tool choosing a fact — and the user is told to fill them.
    /// </summary>
    public IReadOnlyList<string> Unresolved { get; }

    public string Text
    {
        get
        {
            var parts = new List<string>();
            if (Values.Count > 0)
                parts.Add(string.Format(CultureInfo.InvariantCulture,
                    "{0}新建的面積沿用同區劃既有面積的 {1}",
                    QuotedName, string.Join("、", Values.Select(v => v.Text))));
            if (Unresolved.Count > 0)
                parts.Add(string.Format(CultureInfo.InvariantCulture,
                    "{0}：同區劃既有面積的 {1} 填得不一致或有未填，新建的面積不填，請在「防火參數批次設定」補填",
                    QuotedName, string.Join("、", Unresolved.Select(ZoneAreaParameters.Label))));
            return string.Join("；", parts);
        }
    }

    public override string ToString() => Text;
}

/// <summary>
/// Every new Area of one run that has something to take over, worked out once from the drafts and
/// the model so the preview and the write-back say the same thing (C-22).
/// </summary>
public sealed class ZoneParameterInheritance : IReadOnlyList<ZoneParameterInheritanceOperation>
{
    public static readonly ZoneParameterInheritance Empty = new ZoneParameterInheritance(Array.Empty<ZoneParameterInheritanceOperation>());

    private readonly ReadOnlyCollection<ZoneParameterInheritanceOperation> _items;

    private ZoneParameterInheritance(IEnumerable<ZoneParameterInheritanceOperation> items) =>
        _items = new ReadOnlyCollection<ZoneParameterInheritanceOperation>(items.ToList());

    public int Count => _items.Count;
    public ZoneParameterInheritanceOperation this[int index] => _items[index];
    public bool IsEmpty => _items.Count == 0;
    public IEnumerator<ZoneParameterInheritanceOperation> GetEnumerator() => _items.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <summary>
    /// A new Area of a 區劃 that already has Areas takes over each parameter all of them agree on.
    /// A 區劃 with no existing Area is a new 區劃: nothing to take over, its values are the user's to
    /// fill. Existing Areas this run is about to delete still count — when a 區劃 is re-placed, they
    /// are the only record of what the user filled.
    /// </summary>
    public static ZoneParameterInheritance Build(
        IEnumerable<PlannedElement> planned,
        IReadOnlyDictionary<ManagedElementKey, ExistingManagedElement> existing)
    {
        if (planned is null) throw new ArgumentNullException(nameof(planned));
        if (existing is null) throw new ArgumentNullException(nameof(existing));

        var areasByZone = existing.Values
            .Where(e => e.HasKey && e.Key.Kind == ManagedElementKind.Area)
            .GroupBy(e => (e.Key.PackageId, e.Key.ZoneId))
            .ToDictionary(g => g.Key, g => g.ToList());

        var operations = new List<ZoneParameterInheritanceOperation>();

        foreach (var element in planned.Where(p => p.Key.Kind == ManagedElementKind.Area && !existing.ContainsKey(p.Key)))
        {
            if (!areasByZone.TryGetValue((element.Key.PackageId, element.Key.ZoneId), out var siblings)) continue;

            var values = new List<InheritedZoneValue>();
            var unresolved = new List<string>();

            foreach (var parameter in ZoneAreaParameters.All)
            {
                var readings = siblings.Select(s => s.ZoneParameter(parameter)).ToList();
                if (readings.All(r => r is null)) continue;

                var distinct = readings.Distinct().ToList();
                if (distinct.Count == 1 && distinct[0] is int agreed)
                    values.Add(new InheritedZoneValue(parameter, agreed));
                else
                    unresolved.Add(parameter);
            }

            if (values.Count == 0 && unresolved.Count == 0) continue;
            operations.Add(new ZoneParameterInheritanceOperation(element.Key, element.ZoneName, values, unresolved));
        }

        return operations.Count == 0
            ? Empty
            : new ZoneParameterInheritance(operations.OrderBy(op => op.Key.ToToken(), StringComparer.Ordinal));
    }
}
