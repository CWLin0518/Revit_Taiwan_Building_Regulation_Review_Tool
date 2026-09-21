using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using BuildingRegulationReview.Application.WriteBack;

namespace BuildingRegulationReview.Application.Checks;

/// <summary>
/// What the 區劃面積 check needs besides the model: facts about the building (構造、用途、層數、高度)
/// and about each zone (用途、灑水、樓層序) — spec 11.4 step 1. The zone's area, ID and level come
/// from the candidate set and cannot be supplied here, so an input can never overrule the model.
/// </summary>
public sealed class CompartmentAreaInputs
{
    /// <summary>Fields the check derives from the model itself.</summary>
    public static readonly IReadOnlyCollection<string> ModelOwnedFields =
        new ReadOnlyCollection<string>(new[] { "zone.id", "zone.area", "zone.levelName" });

    public static readonly CompartmentAreaInputs None = new(null, null);

    private readonly Dictionary<Guid, IReadOnlyList<ReviewInput>> _zones;

    public CompartmentAreaInputs(IEnumerable<ReviewInput>? building, IReadOnlyDictionary<Guid, IEnumerable<ReviewInput>>? zones)
    {
        Building = Checked(building, "building.", "building");
        _zones = new Dictionary<Guid, IReadOnlyList<ReviewInput>>();
        foreach (var pair in zones ?? new Dictionary<Guid, IEnumerable<ReviewInput>>())
        {
            if (pair.Key == Guid.Empty) throw new ArgumentException("Zone ID cannot be empty.", nameof(zones));
            _zones[pair.Key] = Checked(pair.Value, "zone.", $"zone {pair.Key:D}");
        }
    }

    public IReadOnlyList<ReviewInput> Building { get; }

    public IEnumerable<Guid> ZoneIds => _zones.Keys.OrderBy(x => x);

    public IReadOnlyList<ReviewInput> ForZone(Guid zoneId) =>
        _zones.TryGetValue(zoneId, out var inputs) ? inputs : Array.Empty<ReviewInput>();

    private static IReadOnlyList<ReviewInput> Checked(IEnumerable<ReviewInput>? inputs, string scope, string owner)
    {
        var list = (inputs ?? Array.Empty<ReviewInput>()).ToList();
        if (list.Any(x => x is null)) throw new ArgumentException($"The {owner} inputs contain a missing input.");

        var wrongScope = list.FirstOrDefault(x => !x.Field.StartsWith(scope, StringComparison.Ordinal));
        if (wrongScope is not null)
            throw new ArgumentException($"The {owner} inputs can only fill {scope}* fields, not '{wrongScope.Field}'.");

        var owned = list.FirstOrDefault(x => ModelOwnedFields.Contains(x.Field, StringComparer.Ordinal));
        if (owned is not null)
            throw new ArgumentException($"'{owned.Field}' is read from the model and cannot be supplied as an input.");

        var duplicate = list.GroupBy(x => x.Field, StringComparer.Ordinal).FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
            throw new ArgumentException($"The {owner} inputs supply '{duplicate.Key}' more than once.");

        return new ReadOnlyCollection<ReviewInput>(list.OrderBy(x => x.Field, StringComparer.Ordinal).ToList());
    }
}

/// <summary>Settings of the 區劃面積 check.</summary>
public sealed class CompartmentAreaOptions
{
    public static readonly CompartmentAreaOptions Default = new();

    public CompartmentAreaOptions(double crossCheckRelativeTolerance = AreaAgreement.DefaultRelativeTolerance)
    {
        if (double.IsNaN(crossCheckRelativeTolerance) || double.IsInfinity(crossCheckRelativeTolerance) || crossCheckRelativeTolerance < 0)
            throw new ArgumentOutOfRangeException(nameof(crossCheckRelativeTolerance));

        CrossCheckRelativeTolerance = crossCheckRelativeTolerance;
    }

    /// <summary>
    /// How far Revit's Area and the measured boundary may differ, relative to the measured boundary,
    /// before a Pass or Fail is withheld. Defaults to the same 1% write-back uses (spec 10.6).
    /// </summary>
    public double CrossCheckRelativeTolerance { get; }
}
