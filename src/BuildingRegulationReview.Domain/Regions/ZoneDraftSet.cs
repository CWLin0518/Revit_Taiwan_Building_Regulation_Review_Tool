using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using BuildingRegulationReview.Domain.Common;

namespace BuildingRegulationReview.Domain.Regions;

/// <summary>What one assignment did: the new set, and the zone the face came from if it had one.</summary>
public sealed class ZoneAssignment
{
    public ZoneAssignment(ZoneDraftSet zones, int faceId, Guid? zoneId, Guid? previousZoneId)
    {
        if (faceId < 0) throw new ArgumentOutOfRangeException(nameof(faceId));

        Zones = zones ?? throw new ArgumentNullException(nameof(zones));
        FaceId = faceId;
        ZoneId = zoneId;
        PreviousZoneId = previousZoneId;
    }

    public ZoneDraftSet Zones { get; }
    public int FaceId { get; }

    /// <summary>The zone the face belongs to now, or null when it was taken out of every zone.</summary>
    public Guid? ZoneId { get; }

    /// <summary>The zone that lost the face, or null when it belonged to none.</summary>
    public Guid? PreviousZoneId { get; }

    /// <summary>True when the face was taken from another zone rather than added from nothing.</summary>
    public bool MovedFromAnotherZone => PreviousZoneId.HasValue && PreviousZoneId != ZoneId;
}

/// <summary>
/// Every 區劃 draft of one package, holding the rule spec 10.3 states outright: a solved face belongs
/// to at most one zone at a time. Assigning a face another zone owns moves it and says so, rather
/// than refusing it or quietly leaving a copy behind, so the Editor can report the move.
/// </summary>
/// <remarks>
/// Immutable: every operation returns a new set, which is what P2-T06 replays for Undo/Redo. Zone
/// order is creation order so the list in the Editor does not jump around; names are unique ignoring
/// case, because a zone name reaches the Area parameters where a duplicate cannot be told apart.
/// </remarks>
public sealed class ZoneDraftSet
{
    public static readonly ZoneDraftSet Empty = new ZoneDraftSet(Array.Empty<ZoneDraft>());

    private readonly Dictionary<Guid, ZoneDraft> _byId;
    private readonly Dictionary<int, Guid> _zoneByFace;

    private ZoneDraftSet(IEnumerable<ZoneDraft> zones)
    {
        var list = zones.ToList();
        Zones = new ReadOnlyCollection<ZoneDraft>(list);
        _byId = list.ToDictionary(z => z.Id);
        _zoneByFace = new Dictionary<int, Guid>();
        foreach (var zone in list)
        {
            foreach (var faceId in zone.FaceIds) _zoneByFace[faceId] = zone.Id;
        }
    }

    /// <summary>In creation order.</summary>
    public IReadOnlyList<ZoneDraft> Zones { get; }

    public int Count => Zones.Count;
    public bool IsEmpty => Zones.Count == 0;
    public int AssignedFaceCount => _zoneByFace.Count;
    public IEnumerable<int> AssignedFaceIds => _zoneByFace.Keys.OrderBy(x => x);
    public IEnumerable<ZoneColor> UsedColors => Zones.Select(z => z.Color);

    public bool Contains(Guid zoneId) => _byId.ContainsKey(zoneId);

    public ZoneDraft? Zone(Guid zoneId) => _byId.TryGetValue(zoneId, out var zone) ? zone : null;

    /// <summary>The zone owning a face, or null when nobody claimed it yet.</summary>
    public ZoneDraft? ZoneOf(int faceId) => _zoneByFace.TryGetValue(faceId, out var id) ? _byId[id] : null;

    public bool IsAssigned(int faceId) => _zoneByFace.ContainsKey(faceId);

    public Result<ZoneDraftSet> Add(ZoneDraft zone)
    {
        if (zone is null) throw new ArgumentNullException(nameof(zone));

        if (_byId.ContainsKey(zone.Id))
        {
            return Result.Failure<ZoneDraftSet>(new Error(
                "regions.zone.duplicateId", "這個區劃已經存在。", "zoneId=" + zone.Id));
        }

        if (HasName(zone.Name, null))
        {
            return Result.Failure<ZoneDraftSet>(new Error(
                "regions.zone.duplicateName", $"已經有名為「{zone.Name}」的區劃，請換一個名稱。", "name=" + zone.Name));
        }

        var taken = zone.FaceIds.Where(IsAssigned).ToList();
        if (taken.Count > 0)
        {
            return Result.Failure<ZoneDraftSet>(new Error(
                "regions.zone.faceAlreadyAssigned",
                "要加入的範圍已經屬於其他區劃，請先將它移出。",
                "faceIds=" + string.Join(",", taken)));
        }

        return Result.Success(new ZoneDraftSet(Zones.Concat(new[] { zone })));
    }

    public Result<ZoneDraftSet> Remove(Guid zoneId)
    {
        if (!_byId.ContainsKey(zoneId)) return UnknownZone<ZoneDraftSet>(zoneId);
        return Result.Success(new ZoneDraftSet(Zones.Where(z => z.Id != zoneId)));
    }

    public Result<ZoneDraftSet> Rename(Guid zoneId, string name)
    {
        if (!_byId.TryGetValue(zoneId, out var zone)) return UnknownZone<ZoneDraftSet>(zoneId);

        string normalized;
        try
        {
            normalized = ZoneDraft.NormalizeName(name);
        }
        catch (ArgumentException exception)
        {
            return Result.Failure<ZoneDraftSet>(new Error(
                "regions.zone.invalidName",
                $"區劃名稱不可空白，長度也不可超過 {ZoneDraft.MaximumNameLength} 個字。",
                exception.Message));
        }

        if (HasName(normalized, zoneId))
        {
            return Result.Failure<ZoneDraftSet>(new Error(
                "regions.zone.duplicateName", $"已經有名為「{normalized}」的區劃，請換一個名稱。", "name=" + normalized));
        }

        return Result.Success(Replace(zone.WithName(normalized)));
    }

    public Result<ZoneDraftSet> Recolor(Guid zoneId, ZoneColor color)
    {
        if (!_byId.TryGetValue(zoneId, out var zone)) return UnknownZone<ZoneDraftSet>(zoneId);
        return Result.Success(Replace(zone.WithColor(color)));
    }

    /// <summary>
    /// Records, or withdraws, the user's explicit confirmation that a zone may hold parts that do
    /// not touch (spec 10.3). The set only stores the answer; deciding when to ask is the Editor's.
    /// </summary>
    public Result<ZoneDraftSet> SetDisjointAllowed(Guid zoneId, bool allowed)
    {
        if (!_byId.TryGetValue(zoneId, out var zone)) return UnknownZone<ZoneDraftSet>(zoneId);
        return Result.Success(Replace(zone.WithDisjointAllowed(allowed)));
    }

    /// <summary>Puts a face into a zone, taking it out of whichever zone held it before.</summary>
    public Result<ZoneAssignment> Assign(int faceId, Guid zoneId)
    {
        if (faceId < 0) throw new ArgumentOutOfRangeException(nameof(faceId));
        if (!_byId.ContainsKey(zoneId)) return UnknownZone<ZoneAssignment>(zoneId);

        var previous = _zoneByFace.TryGetValue(faceId, out var owner) ? owner : (Guid?)null;
        if (previous == zoneId)
        {
            return Result.Failure<ZoneAssignment>(new Error(
                "regions.zone.faceAlreadyInZone",
                "這個範圍已經屬於目前的區劃。",
                $"faceId={faceId};zoneId={zoneId}"));
        }

        var updated = Zones
            .Select(z => z.Id == zoneId ? z.Including(faceId) : z.Excluding(faceId))
            .ToList();

        return Result.Success(new ZoneAssignment(new ZoneDraftSet(updated), faceId, zoneId, previous));
    }

    /// <summary>Takes a face out of the zone that owns it.</summary>
    public Result<ZoneAssignment> Unassign(int faceId)
    {
        if (faceId < 0) throw new ArgumentOutOfRangeException(nameof(faceId));

        if (!_zoneByFace.TryGetValue(faceId, out var owner))
        {
            return Result.Failure<ZoneAssignment>(new Error(
                "regions.zone.faceNotAssigned", "這個範圍不屬於任何區劃。", "faceId=" + faceId));
        }

        var updated = Zones.Select(z => z.Id == owner ? z.Excluding(faceId) : z).ToList();
        return Result.Success(new ZoneAssignment(new ZoneDraftSet(updated), faceId, null, owner));
    }

    /// <summary>Drops assignments to faces a later solve no longer produces.</summary>
    public ZoneDraftSet RetainFaces(Func<int, bool> stillExists)
    {
        if (stillExists is null) throw new ArgumentNullException(nameof(stillExists));

        var updated = Zones.Select(z => z.WithFaces(z.FaceIds.Where(stillExists))).ToList();
        var unchanged = updated.Zip(Zones, (a, b) => a.FaceCount == b.FaceCount).All(same => same);
        return unchanged ? this : new ZoneDraftSet(updated);
    }

    /// <summary>
    /// A deterministic fingerprint of the whole set, in zone order. Equal signatures mean the drafts
    /// would write the same thing back to the model, which is what tells an Editor with unapplied
    /// changes from one the user has undone back to where it started.
    /// </summary>
    public string Signature() => string.Join(";", Zones.Select(z => z.Signature()));

    private ZoneDraftSet Replace(ZoneDraft zone) =>
        new ZoneDraftSet(Zones.Select(z => z.Id == zone.Id ? zone : z));

    private bool HasName(string name, Guid? exceptZoneId) =>
        Zones.Any(z => (exceptZoneId is null || z.Id != exceptZoneId) &&
                       string.Equals(z.Name, name, StringComparison.OrdinalIgnoreCase));

    private static Result<T> UnknownZone<T>(Guid zoneId) => Result.Failure<T>(new Error(
        "regions.zone.unknownZone", "找不到這個區劃，它可能已經被刪除。", "zoneId=" + zoneId));
}
