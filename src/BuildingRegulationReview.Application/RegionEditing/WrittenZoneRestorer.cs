using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using BuildingRegulationReview.Domain.Geometry;
using BuildingRegulationReview.Domain.Regions;

namespace BuildingRegulationReview.Application.RegionEditing;

/// <summary>
/// One Area the tool wrote for a 區劃 part, as the model has it now: which zone it carries, what
/// that zone was called and coloured, and the outline Revit computed for it.
/// </summary>
public sealed class WrittenZoneArea
{
    public WrittenZoneArea(
        Guid zoneId,
        string zoneName,
        ZoneColor color,
        IEnumerable<IReadOnlyList<Point2D>>? boundaryLoops,
        Point2D? placement,
        string? use = null)
    {
        if (zoneId == Guid.Empty) throw new ArgumentException("Zone ID cannot be empty.", nameof(zoneId));

        ZoneId = zoneId;
        ZoneName = zoneName ?? throw new ArgumentNullException(nameof(zoneName));
        Color = color;
        Use = use?.Trim();
        BoundaryLoops = new ReadOnlyCollection<IReadOnlyList<Point2D>>(
            (boundaryLoops ?? Array.Empty<IReadOnlyList<Point2D>>()).Where(l => l is not null && l.Count >= 3).ToList());
        Placement = placement;
    }

    public Guid ZoneId { get; }
    public string ZoneName { get; }
    public ZoneColor Color { get; }

    /// <summary>
    /// 防火檢討_區劃用途 as this Area carries it <em>now</em>, not as the signature records it; null
    /// when the parameter could not be read at all, which is not the same as an unfilled one.
    /// </summary>
    /// <remarks>
    /// The name and colour come from the signature because they are the tool's own record and a hand
    /// edit of them is drift to report. 用途 is the opposite: it is the user's value, and the 批次設定
    /// 面板 owns it just as much as the Editor does. Reading it from the signature would mean a use
    /// set in that panel never reaches the Editor's drafts — and the next 套用 would then write the
    /// draft's blank over it, silently taking a 管道間 back to a 一般區劃 and with it its 第79條之2
    /// results.
    /// </remarks>
    public string? Use { get; }

    /// <summary>Outer loop and holes together; empty when Revit reports the Area as not enclosed.</summary>
    public IReadOnlyList<IReadOnlyList<Point2D>> BoundaryLoops { get; }

    /// <summary>Where the Area sits, the fallback when it has no enclosed outline.</summary>
    public Point2D? Placement { get; }

    /// <summary>Even-odd over every loop, so a hole in the Area is outside it.</summary>
    public bool Contains(Point2D point) =>
        BoundaryLoops.Count(loop => RingGeometry.ContainsPoint(loop, point)) % 2 == 1;
}

/// <summary>What reopening the Editor recovered from the model.</summary>
public sealed class ZoneRestoration
{
    public ZoneRestoration(ZoneDraftSet zones, IEnumerable<string> warnings)
    {
        Zones = zones ?? throw new ArgumentNullException(nameof(zones));
        Warnings = new ReadOnlyCollection<string>((warnings ?? Array.Empty<string>()).ToList());
    }

    public ZoneDraftSet Zones { get; }

    /// <summary>Zones that could not be put back, each in a sentence the user can act on.</summary>
    public IReadOnlyList<string> Warnings { get; }
}

/// <summary>
/// Rebuilds the 區劃 drafts from the Areas an earlier write-back left in the Area Plan, so the Editor
/// reopens on the zones the model already holds instead of an empty canvas.
/// </summary>
/// <remarks>
/// A face joins a zone when its representative point lies inside one of that zone's Areas. The point
/// is strictly inside the face and the Area was drawn from faces of the same kind, so the test needs
/// no tolerance, and it still works after the geometry has been edited and re-solved: a face the
/// Area no longer covers simply stays unassigned. The zone keeps the ID it was written with, which
/// is what lets the next write-back update its elements rather than add a second set.
/// </remarks>
public static class WrittenZoneRestorer
{
    public static ZoneRestoration Restore(PlanRegionMap map, IEnumerable<WrittenZoneArea> areas)
    {
        if (map is null) throw new ArgumentNullException(nameof(map));
        if (areas is null) throw new ArgumentNullException(nameof(areas));

        var zones = ZoneDraftSet.Empty;
        var warnings = new List<string>();
        var claimed = new HashSet<int>();

        foreach (var group in areas.Where(a => a is not null).GroupBy(a => a.ZoneId).OrderBy(g => g.Key))
        {
            var first = group.First();
            var faceIds = map.Faces
                .Where(face => group.Any(area => area.Contains(face.RepresentativePoint)))
                .Select(face => face.Id)
                .ToList();

            foreach (var area in group.Where(a => a.BoundaryLoops.Count == 0 && a.Placement.HasValue))
            {
                var placed = map.FaceAt(area.Placement!.Value);
                if (placed is not null) faceIds.Add(placed.Id);
            }

            // One face, one zone (spec 10.3). Areas of two zones overlapping means the model was
            // edited by hand; the zone read first keeps the face and the user sees it unassigned
            // from the other.
            faceIds = faceIds.Distinct().Where(id => !claimed.Contains(id)).OrderBy(id => id).ToList();
            if (faceIds.Count == 0)
            {
                warnings.Add(string.Format(
                    CultureInfo.CurrentCulture,
                    "區劃「{0}」的面積已找不到對應的範圍（可能是邊界已變更或面積未封閉），未載入編輯器。",
                    first.ZoneName));
                continue;
            }

            var use = AgreedUse(group, out var mixed);
            if (mixed)
            {
                warnings.Add(string.Format(
                    CultureInfo.CurrentCulture,
                    "區劃「{0}」的各個面積填了不一致的區劃用途，編輯器顯示「多種用途」並原樣保留；" +
                    "要統一它們，請在編輯器裡明確選一個用途。",
                    first.ZoneName));
            }

            var disjoint = map.ContiguousPartsOf(faceIds).Count > 1;
            var draft = new ZoneDraft(group.Key, UniqueName(zones, first.ZoneName), first.Color, faceIds, disjoint, use);
            var added = zones.Add(draft);
            if (added.IsFailure)
            {
                warnings.Add(string.Format(CultureInfo.CurrentCulture, "區劃「{0}」無法載入：{1}", first.ZoneName, added.Error.Message));
                continue;
            }

            zones = added.Value;
            foreach (var id in faceIds) claimed.Add(id);
        }

        return new ZoneRestoration(zones, warnings);
    }

    /// <summary>
    /// The 區劃用途 a zone's Areas agree on, or null for 不變更 — the same reading
    /// <c>RevitStoreyZoneReader</c> applies to a 區劃 spread over several Areas, so a zone whose parts
    /// were edited apart never has one part's answer picked for it.
    /// </summary>
    /// <remarks>
    /// Null comes about two ways and only one is worth a warning. <paramref name="mixed"/> is the
    /// zone whose Areas carry different uses: a real disagreement, and one the user can settle. An
    /// Area the parameter could not be read from is the other, and it is silent — it means the model
    /// has not had 防火檢討參數設定 run on it yet, which is every zone at once and is the write-back's
    /// message to deliver, not a line per zone here. Both end as 不變更, so neither can lose data.
    /// </remarks>
    private static string? AgreedUse(IEnumerable<WrittenZoneArea> areas, out bool mixed)
    {
        mixed = false;
        var uses = areas.Select(a => a.Use).ToList();
        if (uses.Any(u => u is null)) return null;

        var distinct = uses.Distinct(StringComparer.Ordinal).ToList();
        if (distinct.Count == 1) return distinct[0];

        mixed = true;
        return null;
    }

    // Names are unique ignoring case, and one renamed by hand in Revit could now clash.
    private static string UniqueName(ZoneDraftSet zones, string name)
    {
        var baseName = string.IsNullOrWhiteSpace(name) ? "區劃" : name.Trim();
        var candidate = baseName;
        for (var n = 2; zones.Zones.Any(z => string.Equals(z.Name, candidate, StringComparison.OrdinalIgnoreCase)); n++)
        {
            candidate = string.Format(CultureInfo.InvariantCulture, "{0} ({1})", baseName, n);
        }

        return candidate;
    }
}
