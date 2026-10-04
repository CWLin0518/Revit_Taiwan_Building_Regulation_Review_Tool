using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using BuildingRegulationReview.Application.Parameters;
using BuildingRegulationReview.Domain.Geometry;

namespace BuildingRegulationReview.Application.Candidates;

/// <summary>
/// One 防火區劃 of any storey, as the cross-storey 挑空 judgement needs it: where it is, what it is
/// used for, its storey and its Revit area. The review of one package only sees its own storey; this
/// is the thin slice of every other storey that 第79條之2第3項 has to look at (垂直區劃規格 §3.8).
/// </summary>
public sealed class StoreyZone
{
    public StoreyZone(
        Guid packageId,
        Guid zoneId,
        string levelUniqueId,
        double levelElevationFeet,
        string? levelName,
        string? name,
        string? use,
        int? floorNumber,
        double? revitAreaSquareFeet,
        IEnumerable<IReadOnlyList<Point2D>> boundaryLoops,
        IEnumerable<string>? areaUniqueIds = null,
        string? problem = null)
    {
        if (packageId == Guid.Empty) throw new ArgumentException("Package ID cannot be empty.", nameof(packageId));
        if (zoneId == Guid.Empty) throw new ArgumentException("Zone ID cannot be empty.", nameof(zoneId));
        if (string.IsNullOrWhiteSpace(levelUniqueId)) throw new ArgumentException("Level UniqueId is required.", nameof(levelUniqueId));
        if (boundaryLoops is null) throw new ArgumentNullException(nameof(boundaryLoops));

        PackageId = packageId;
        ZoneId = zoneId;
        LevelUniqueId = levelUniqueId.Trim();
        LevelElevationFeet = levelElevationFeet;
        LevelName = string.IsNullOrWhiteSpace(levelName) ? null : levelName!.Trim();
        Name = string.IsNullOrWhiteSpace(name) ? "（未命名區劃）" : name!.Trim();
        Use = string.IsNullOrWhiteSpace(use) ? null : use!.Trim();
        FloorNumber = floorNumber;
        RevitAreaSquareFeet = revitAreaSquareFeet is double a && a > 0 ? a : (double?)null;
        BoundaryLoops = new ReadOnlyCollection<IReadOnlyList<Point2D>>(boundaryLoops.Where(l => l is not null && l.Count >= 3).ToList());
        AreaUniqueIds = new ReadOnlyCollection<string>((areaUniqueIds ?? Array.Empty<string>()).OrderBy(x => x, StringComparer.Ordinal).ToList());
        Problem = string.IsNullOrWhiteSpace(problem) ? null : problem!.Trim();
        Shape = new PlanShape(BoundaryLoops);
    }

    public Guid PackageId { get; }
    public Guid ZoneId { get; }
    public string LevelUniqueId { get; }
    public double LevelElevationFeet { get; }
    public string? LevelName { get; }
    public string Name { get; }

    /// <summary>防火檢討_區劃用途, or null when blank or its Areas disagree.</summary>
    public string? Use { get; }

    /// <summary>防火檢討_所在樓層序, or null when blank, 0, or its Areas disagree.</summary>
    public int? FloorNumber { get; }

    /// <summary>The sum of its Areas' Revit areas, or null when any of them is unplaced or unenclosed.</summary>
    public double? RevitAreaSquareFeet { get; }

    public IReadOnlyList<IReadOnlyList<Point2D>> BoundaryLoops { get; }
    public IReadOnlyList<string> AreaUniqueIds { get; }

    /// <summary>
    /// Why this 區劃 cannot be relied on — e.g. its mark turns up on more than one level, the trace of
    /// an Area copied to another storey — or null. A 挑空 that touches such a 區劃 is not traced.
    /// </summary>
    public string? Problem { get; }

    public bool IsAtrium => string.Equals(Use, ZoneUses.Atrium, StringComparison.Ordinal);

    /// <summary>
    /// A 第79條之2 垂直區劃 of any kind, 挑空 included. A 樓梯間 or 昇降機道 is 單獨區劃分隔 under 第1項,
    /// so it is never part of a 挑空's 連通範圍 — counting it would also let a stair stacked through
    /// every storey stitch two unrelated 挑空 into one (垂直區劃規格 決議 36).
    /// </summary>
    public bool IsVerticalCompartment => ZoneUses.IsVerticalCompartment(Use);

    internal PlanShape Shape { get; }

    /// <summary>How the zone is named in a message: its storey and its name.</summary>
    public string Label => $"{LevelName ?? "（未命名樓層）"}「{Name}」";

    public override string ToString() => Label;
}

/// <summary>Every 防火區劃 of one Area Scheme, across its storeys.</summary>
public sealed class StoreyZoneMap
{
    public StoreyZoneMap(IEnumerable<StoreyZone> zones)
    {
        if (zones is null) throw new ArgumentNullException(nameof(zones));
        var list = zones.ToList();
        if (list.Any(z => z is null)) throw new ArgumentException("Zones cannot contain null.", nameof(zones));
        if (list.GroupBy(z => (z.PackageId, z.ZoneId, z.LevelUniqueId)).Any(g => g.Count() > 1))
            throw new ArgumentException("The same zone is listed twice on one level.", nameof(zones));

        Zones = new ReadOnlyCollection<StoreyZone>(list);

        // Storeys in elevation order, ties broken by UniqueId so the order never depends on what the
        // model happened to return first. A storey is a level that carries at least one zone.
        Storeys = new ReadOnlyCollection<string>(list
            .GroupBy(z => z.LevelUniqueId, StringComparer.Ordinal)
            .OrderBy(g => g.Min(z => z.LevelElevationFeet))
            .ThenBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => g.Key)
            .ToList());
    }

    public IReadOnlyList<StoreyZone> Zones { get; }

    /// <summary>The storeys' level UniqueIds, lowest first.</summary>
    public IReadOnlyList<string> Storeys { get; }

    /// <summary>The zone on the given level, or — with no level — the only one with that identity.</summary>
    public StoreyZone? Find(Guid packageId, Guid zoneId, string? levelUniqueId = null)
    {
        var matches = Zones.Where(z => z.PackageId == packageId && z.ZoneId == zoneId).ToList();
        if (levelUniqueId is not null)
            return matches.FirstOrDefault(z => string.Equals(z.LevelUniqueId, levelUniqueId, StringComparison.Ordinal));
        return matches.Count == 1 ? matches[0] : null;
    }

    internal int StoreyIndex(StoreyZone zone) => IndexOf(zone.LevelUniqueId);

    internal IEnumerable<StoreyZone> On(int storeyIndex) =>
        storeyIndex < 0 || storeyIndex >= Storeys.Count
            ? Enumerable.Empty<StoreyZone>()
            : Zones.Where(z => string.Equals(z.LevelUniqueId, Storeys[storeyIndex], StringComparison.Ordinal));

    private int IndexOf(string levelUniqueId)
    {
        for (var i = 0; i < Storeys.Count; i++)
            if (string.Equals(Storeys[i], levelUniqueId, StringComparison.Ordinal)) return i;
        return -1;
    }
}

/// <summary>Why a 區劃 is counted in a 挑空's 連通區劃面積.</summary>
public enum AtriumContributorRole
{
    /// <summary>On a storey the 挑空 opens through, sharing a boundary with one of its 挑空 Areas.</summary>
    Surrounding,

    /// <summary>On the 起始樓層 below the lowest opening, under a surrounding 區劃 or the opening in plan.</summary>
    Below
}

/// <summary>One 區劃 counted in the 連通區劃面積, and why.</summary>
public sealed class AtriumContributor
{
    internal AtriumContributor(StoreyZone zone, AtriumContributorRole role)
    {
        Zone = zone;
        Role = role;
    }

    public StoreyZone Zone { get; }
    public AtriumContributorRole Role { get; }

    public double? AreaSquareMeters =>
        Zone.RevitAreaSquareFeet is double feet ? PlanUnits.SquareFeetToSquareMeters(feet) : (double?)null;
}

/// <summary>
/// One 挑空 traced through the storeys it spans: the 挑空 Areas that belong together, the 起始樓層,
/// the 連跨樓層數 and the 連通區劃面積 — the facts 第79條之2第3項 and the 區劃面積 rules read, worked
/// out from the model rather than typed (垂直區劃規格 §3.8、決議 35).
/// </summary>
public sealed class AtriumStack
{
    internal AtriumStack(
        IReadOnlyList<StoreyZone> atriums,
        int? spannedFloors,
        int? baseFloorNumber,
        int? topFloorNumber,
        IReadOnlyList<AtriumContributor> contributors,
        string? spanProblem,
        string? areaProblem)
    {
        Atriums = atriums;
        SpannedFloors = spanProblem is null ? spannedFloors : null;
        BaseFloorNumber = spanProblem is null ? baseFloorNumber : null;
        TopFloorNumber = spanProblem is null ? topFloorNumber : null;
        Contributors = contributors;
        SpanProblem = spanProblem;
        AreaProblem = spanProblem ?? areaProblem;
    }

    /// <summary>The 挑空 Areas' zones that make up this one 挑空, lowest storey first.</summary>
    public IReadOnlyList<StoreyZone> Atriums { get; }

    /// <summary>連跨樓層數: the 起始樓層 and every storey the 挑空 opens through. Null with <see cref="SpanProblem"/>.</summary>
    public int? SpannedFloors { get; }

    /// <summary>起始樓層's 樓層序, or null when it cannot be told.</summary>
    public int? BaseFloorNumber { get; }

    /// <summary>The highest storey's 樓層序, or null when it cannot be told.</summary>
    public int? TopFloorNumber { get; }

    /// <summary>The 區劃 the 連通區劃面積 adds up, in storey order.</summary>
    public IReadOnlyList<AtriumContributor> Contributors { get; }

    /// <summary>Why the span cannot be told, or null.</summary>
    public string? SpanProblem { get; }

    /// <summary>Why the 連通區劃面積 cannot be told, or null. A span problem spoils the area too.</summary>
    public string? AreaProblem { get; }

    /// <summary>連通區劃面積 in m², or null with <see cref="AreaProblem"/>.</summary>
    public double? ConnectedAreaSquareMeters =>
        AreaProblem is null ? Contributors.Sum(c => c.AreaSquareMeters!.Value) : (double?)null;

    /// <summary>What the figures were worked out from, for the inputs' evidence source.</summary>
    public string Explanation
    {
        get
        {
            var storeys = string.Join("、", Atriums.Select(a => a.Label));
            var parts = string.Join("、", Contributors.Select(c =>
                c.Zone.Label + (c.AreaSquareMeters is double m2 ? " " + m2.ToString("0.##", CultureInfo.InvariantCulture) + " ㎡" : "（無面積）")));
            return $"由跨樓層區劃推得：挑空 {storeys}；連通區劃 {(parts.Length == 0 ? "無" : parts)}";
        }
    }
}

/// <summary>
/// Traces one 挑空 through the storeys (垂直區劃規格 §3.8、決議 35、36). The steps:
/// <list type="number">
/// <item>A 挑空 Area's <em>所在區劃</em> are the 區劃 of its storey that share a boundary with it,
/// other than 垂直區劃 (樓梯間、昇降機道… are 單獨區劃分隔 and never part of the 連通範圍).</item>
/// <item>A 挑空 on the storey above or below belongs to the same 挑空 when its 所在區劃 overlap this
/// one's in plan — the two openings need not overlap each other.</item>
/// <item>起始樓層 is the storey under the lowest opening; 連跨樓層數 runs from it to the highest
/// storey that opens. When the 樓層序 are known they must run on without a gap: a storey nobody
/// has drawn 區劃 on yet would otherwise drop out of the count unnoticed.</item>
/// <item>連通區劃面積 adds every 所在區劃 of every opening storey and, on the 起始樓層, every 區劃 under
/// a 所在區劃 or under the opening itself. The 挑空 Areas themselves are not counted.</item>
/// </list>
/// Nothing is guessed: whatever spoils a figure leaves it unknown, with the reason.
/// </summary>
public static class AtriumStackResolver
{
    /// <summary>How far apart two boundaries may lie and still be one shared boundary.</summary>
    public static readonly double TouchToleranceFeet = PlanUnits.MillimetersToFeet(50.0);

    /// <summary>How long a shared boundary must be: two 區劃 meeting at a corner do not surround anything.</summary>
    public static readonly double MinimumSharedLengthFeet = PlanUnits.MillimetersToFeet(300.0);

    /// <summary>The 挑空 the given zone belongs to, or null when the zone is not a 挑空 in the map.</summary>
    public static AtriumStack? Resolve(StoreyZoneMap map, Guid packageId, Guid zoneId, string? levelUniqueId = null)
    {
        if (map is null) throw new ArgumentNullException(nameof(map));
        var start = map.Find(packageId, zoneId, levelUniqueId);
        if (start is null || !start.IsAtrium) return null;

        var surrounding = new Dictionary<StoreyZone, IReadOnlyList<StoreyZone>>();
        IReadOnlyList<StoreyZone> SurroundingOf(StoreyZone atrium)
        {
            if (!surrounding.TryGetValue(atrium, out var found))
            {
                found = Ordered(map.On(map.StoreyIndex(atrium))
                    .Where(z => !z.IsVerticalCompartment && SharesBoundary(atrium.Shape, z.Shape)));
                surrounding[atrium] = found;
            }
            return found;
        }

        // Breadth-first over the storeys, one storey up or down at a time.
        var group = new List<StoreyZone> { start };
        var queue = new Queue<StoreyZone>(group);
        while (queue.Count > 0)
        {
            var atrium = queue.Dequeue();
            var index = map.StoreyIndex(atrium);
            var hosts = SurroundingOf(atrium);
            foreach (var neighbourIndex in new[] { index - 1, index + 1 })
            {
                foreach (var other in Ordered(map.On(neighbourIndex).Where(z => z.IsAtrium && !group.Contains(z))))
                {
                    if (!SurroundingOf(other).Any(o => hosts.Any(h => Overlap(h.Shape, o.Shape)))) continue;
                    group.Add(other);
                    queue.Enqueue(other);
                }
            }
        }

        var atriums = group.OrderBy(map.StoreyIndex).ThenBy(a => a.Name, StringComparer.Ordinal)
            .ThenBy(a => a.ZoneId).ToList();
        var lowest = atriums.Min(map.StoreyIndex);
        var highest = atriums.Max(map.StoreyIndex);

        var contributors = atriums.SelectMany(SurroundingOf).Distinct()
            .OrderBy(map.StoreyIndex).ThenBy(z => z.Name, StringComparer.Ordinal).ThenBy(z => z.ZoneId)
            .Select(z => new AtriumContributor(z, AtriumContributorRole.Surrounding))
            .ToList();

        var spanProblem = SpanProblem(map, atriums, lowest, SurroundingOf);
        string? areaProblem = null;
        List<StoreyZone> below = new List<StoreyZone>();
        if (spanProblem is null)
        {
            var lowestAtriums = atriums.Where(a => map.StoreyIndex(a) == lowest).ToList();
            var lowestHosts = lowestAtriums.SelectMany(SurroundingOf).Distinct().ToList();

            // Under a 所在區劃, or under the opening itself: a 1F 大廳 shaped exactly like the 2F
            // opening lies inside the 所在區劃's hole, not its interior, and must still be counted.
            below = Ordered(map.On(lowest - 1).Where(z =>
                !z.IsVerticalCompartment &&
                (lowestHosts.Any(h => Overlap(h.Shape, z.Shape)) || lowestAtriums.Any(a => Overlap(a.Shape, z.Shape)))));
            if (below.Count == 0)
                areaProblem = $"起始樓層沒有位於 {string.Join("、", lowestHosts.Select(h => h.Label))} 正下方的區劃";
            contributors.InsertRange(0, below.Select(z => new AtriumContributor(z, AtriumContributorRole.Below)));

            spanProblem = Problems(below) ?? FloorGap(map, atriums, below, lowest, highest);
        }

        var unmeasured = contributors.Where(c => c.AreaSquareMeters is null).ToList();
        if (areaProblem is null && unmeasured.Count > 0)
            areaProblem = $"區劃 {string.Join("、", unmeasured.Select(c => c.Zone.Label))} 沒有 Revit 面積（未放置或未封閉）";

        var top = CommonFloor(atriums.Where(a => map.StoreyIndex(a) == highest));
        var baseFloor = CommonFloor(below) ?? Below(CommonFloor(atriums.Where(a => map.StoreyIndex(a) == lowest)));

        return new AtriumStack(atriums, highest - lowest + 2, baseFloor, top, contributors, spanProblem, areaProblem);
    }

    /// <summary>What stops the 挑空 being traced at all, before the 起始樓層 is looked at.</summary>
    private static string? SpanProblem(
        StoreyZoneMap map, IReadOnlyList<StoreyZone> atriums, int lowest, Func<StoreyZone, IReadOnlyList<StoreyZone>> surroundingOf)
    {
        var problem = Problems(atriums) ?? Problems(atriums.SelectMany(surroundingOf));
        if (problem is not null) return problem;

        var lonely = atriums.Where(a => surroundingOf(a).Count == 0).ToList();
        if (lonely.Count > 0)
            return $"挑空 {string.Join("、", lonely.Select(a => a.Label))} 周圍沒有相接的區劃（樓梯間等垂直區劃不算），無法判斷它開向哪個區劃";
        if (lowest == 0)
            return $"最低的挑空 {atriums[0].Label} 已在最低一個有區劃的樓層，下方沒有起始樓層的區劃";
        return null;
    }

    private static string? Problems(IEnumerable<StoreyZone> zones) =>
        zones.Select(z => z.Problem).FirstOrDefault(p => p is not null);

    /// <summary>
    /// The storeys must follow one another. Storeys are the levels that carry 區劃, so one nobody has
    /// drawn 區劃 on yet is simply skipped — 2F and 4F would read as neighbours, the 挑空 would be one
    /// storey short and 第二款's 「三層以下」 could hold when it should not. When every storey's
    /// 樓層序 is known, a jump between them is reported instead of counted around.
    /// </summary>
    private static string? FloorGap(StoreyZoneMap map, IReadOnlyList<StoreyZone> atriums, IReadOnlyList<StoreyZone> below, int lowest, int highest)
    {
        var floors = new List<(int Index, int? Floor, string Label)>
        {
            (lowest - 1, CommonFloor(below), below.FirstOrDefault()?.LevelName ?? "起始樓層")
        };
        for (var index = lowest; index <= highest; index++)
        {
            var onStorey = atriums.Where(a => map.StoreyIndex(a) == index).ToList();
            floors.Add((index, CommonFloor(onStorey), onStorey.FirstOrDefault()?.LevelName ?? "（無挑空的樓層）"));
        }

        for (var i = 0; i + 1 < floors.Count; i++)
        {
            var (_, lower, lowerLabel) = floors[i];
            var (_, upper, upperLabel) = floors[i + 1];
            if (lower is int l && upper is int u && Above(l) != u)
            {
                return $"{lowerLabel}（樓層序 {l}）與 {upperLabel}（樓層序 {u}）之間的樓層沒有區劃，或樓層序填錯，" +
                       "無法確認挑空連跨的樓層；請先為中間樓層建立區劃，或核對各樓層區劃的防火檢討_所在樓層序";
            }
        }

        return null;
    }

    private static List<StoreyZone> Ordered(IEnumerable<StoreyZone> zones) =>
        zones.OrderBy(z => z.Name, StringComparer.Ordinal).ThenBy(z => z.ZoneId).ToList();

    /// <summary>The 樓層序 every zone agrees on, or null when none is stated or they disagree.</summary>
    private static int? CommonFloor(IEnumerable<StoreyZone> zones)
    {
        var floors = zones.Select(z => z.FloorNumber).ToList();
        return floors.Count > 0 && floors.All(f => f is not null) && floors.Distinct().Count() == 1 ? floors[0] : null;
    }

    /// <summary>The storey under a 樓層序. 樓層序 skips 0: the storey under 1F is B1.</summary>
    private static int? Below(int? floor) => floor switch
    {
        null => null,
        1 => -1,
        _ => floor - 1
    };

    /// <summary>The storey over a 樓層序, skipping 0.</summary>
    private static int Above(int floor) => floor == -1 ? 1 : floor + 1;

    /// <summary>Whether two shapes share a stretch of boundary at least <see cref="MinimumSharedLengthFeet"/> long.</summary>
    internal static bool SharesBoundary(PlanShape first, PlanShape second)
    {
        if (first.IsEmpty || second.IsEmpty) return false;
        if (!first.Overlaps(second.MinX, second.MinY, second.MaxX, second.MaxY, TouchToleranceFeet)) return false;

        var shared = 0.0;
        foreach (var edge in first.Edges)
        {
            var ranges = new List<ParameterRange>();
            foreach (var other in second.Edges)
            {
                if (!edge.IsNear(other, TouchToleranceFeet)) continue;
                if (CandidateGeometry.TryCapsuleRange(edge.Start, edge.End, other.Start, other.End, TouchToleranceFeet, out var range))
                    ranges.Add(range);
            }

            shared += CandidateGeometry.Union(ranges).Sum(r => r.Span) * edge.Length;
            if (shared >= MinimumSharedLengthFeet) return true;
        }

        return false;
    }

    /// <summary>
    /// Whether two shapes overlap in plan — share interior, not merely a boundary. The same test
    /// <see cref="CandidateResolver"/> uses for overlapping 區劃 on one storey.
    /// </summary>
    internal static bool Overlap(PlanShape first, PlanShape second)
    {
        if (first.IsEmpty || second.IsEmpty) return false;
        if (!first.Overlaps(second.MinX, second.MinY, second.MaxX, second.MaxY, 0)) return false;

        var tolerance = TouchToleranceFeet;
        return CandidateGeometry.LengthThrough(first.Edges, second, tolerance) > 0 ||
               CandidateGeometry.LengthThrough(second.Edges, first, tolerance) > 0 ||
               first.InteriorPoints().Any(p => second.ContainsWithClearance(p, tolerance)) ||
               second.InteriorPoints().Any(p => first.ContainsWithClearance(p, tolerance));
    }
}
