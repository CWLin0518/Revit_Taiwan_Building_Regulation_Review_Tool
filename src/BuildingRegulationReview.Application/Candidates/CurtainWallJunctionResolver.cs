using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BuildingRegulationReview.Application.Checks;
using BuildingRegulationReview.Domain.Geometry;

namespace BuildingRegulationReview.Application.Candidates;

/// <summary>
/// Turns what the adapter read (<see cref="CurtainWallObservationSet"/>) into the junctions the
/// 帷幕牆區劃交接 check reviews (docs/regulations/curtain-wall-fire-compartment.md §4). No Revit type
/// reaches here, so every rule of §4 is testable as a fixture.
/// </summary>
/// <remarks>
/// <para>
/// The two 90 cm 但書 are measured on different things, and that is the shape of this file. CW-H's
/// 交接處之外牆面 comes from the <b>solid exterior walls standing in the curtain wall's plane</b>
/// (<see cref="FacadeRun"/>, docs §4.2, 決議 13): the 但書's subject is 該外牆構造, so a glazed panel —
/// certified or not — and the 區劃牆 that reaches the façade both answer nothing. CW-V's 層間帶 is
/// measured on the panels, because 第79條之3 really is about them.
/// </para>
/// <para>
/// The panel measurement obeys one rule that shapes the rest: <b>a continuous run never crosses a
/// grid line</b> (docs §4.1). A run is therefore measured inside the one panel the sample falls
/// in — the sum of what lies either side of it, never split 450/450 (docs §3.1 案例 7). Only when that
/// falls short does the resolver look across the grid lines that bound it, and only to answer §4.5's
/// question: would deleting them make the band reach 900 mm? If so the grid lines are suspected of
/// being redundant and the junction is 人工覆核 with their ElementIds; if not, the break is real and the
/// measured run stands.
/// </para>
/// <para>
/// The input contract of §12 is enforced here, not left to the caller: 突出 is always supplied (0 when
/// there is none), and a run is supplied only when everything the measurement looked at — panel or
/// solid wall — had a readable design rating; otherwise nothing is supplied and the engine answers
/// 資料不足 rather than 未符合. A façade with no solid wall at the junction is the one case where 0 is
/// the answer and not a gap: the 外牆面 there is glazing, which is a fact, not an unknown.
/// </para>
/// </remarks>
public static class CurtainWallJunctionResolver
{
    /// <summary>How far inside the façade a point is probed to find the 區劃 a junction belongs to.</summary>
    private const double ZoneProbeMm = 300.0;

    /// <summary>Two coordinates closer than this are the same place.</summary>
    private const double SnapMm = 1.0;

    private const double ParallelEpsilon = 1.0e-9;

    /// <summary>The least number of points a spandrel band is sampled at (docs §4.3 step 2).</summary>
    private const int MinimumSamples = 3;

    public static IReadOnlyList<CurtainWallJunction> Resolve(
        CurtainWallObservationSet observations,
        CurtainWallJunctionOptions? options = null)
    {
        if (observations is null) throw new ArgumentNullException(nameof(observations));
        options ??= CurtainWallJunctionOptions.Default;

        var junctions = new List<CurtainWallJunction>();
        foreach (var wall in observations.CurtainWalls)
            junctions.AddRange(ForCurtainWall(observations, wall, options));

        return junctions
            .OrderBy(x => x.ZoneId)
            .ThenBy(x => (int)x.Kind)
            .ThenBy(x => x.JunctionId, StringComparer.Ordinal)
            .ToList();
    }

    private static IEnumerable<CurtainWallJunction> ForCurtainWall(
        CurtainWallObservationSet set,
        CurtainWallObservation wall,
        CurtainWallJunctionOptions options)
    {
        var wallZone = ZoneOf(set, wall, wall.LengthMm / 2.0);

        // A curved, sloped or warped wall is not measured at all: the two clauses that need a plane
        // go to 人工覆核, and only 第79條之4 — which asks nothing of geometry — is still answered.
        if (!wall.IsPlanar)
        {
            if (wallZone is not null)
            {
                foreach (var kind in new[] { CurtainWallJunctionKind.WallToCurtainWall, CurtainWallJunctionKind.FloorToCurtainWall })
                {
                    yield return CurtainWallJunction.Doubtful(
                        JunctionId(kind, wall.UniqueId), kind, wallZone.ZoneId, wall.UniqueId,
                        new CurtainWallJunctionDoubt(
                            CurtainWallJunctionDoubtKind.NonPlanarCurtainWall,
                            $"帷幕牆（Id {wall.UniqueId}）{wall.NonPlanarReason}，無法以平面量測交接處，需人工覆核。",
                            new[] { wall.UniqueId }),
                        panelUniqueIds: wall.Panels.Select(p => p.UniqueId));
                }

                var all = OtherPanels(wallZone, wall, wall.Panels);
                if (all is not null) yield return all;
            }

            yield break;
        }

        var covered = new List<PanelBand>();
        var results = new List<CurtainWallJunction>();

        // 這片帷幕牆立面內的實體外牆，先投影到它自己的軸上：CW-H 的但書長度由這些牆供給（決議 13）。
        var facades = FacadeSegments(set, wall);

        foreach (var host in set.CompartmentWalls)
        {
            var junction = WallJunction(set, wall, host, options, covered, facades);
            if (junction is not null) results.Add(junction);
        }

        foreach (var floor in set.CompartmentFloors.Where(f => IsSameElevation(f.ElevationMm, set.LevelElevationMm)))
        {
            foreach (var junction in Spandrels(set, wall, floor, options, covered))
                results.Add(junction);
        }

        var vertical = VerticalSpace(set, wall);
        if (vertical is not null) results.Add(vertical);

        foreach (var junction in results) yield return junction;

        if (wallZone is null) yield break;
        var rest = wall.Panels.Where(p => !covered.Any(b => b.Covers(p))).ToList();
        var other = OtherPanels(wallZone, wall, rest);
        if (other is not null) yield return other;
    }

    // --- CW-H：區劃牆與帷幕牆之水平交接（docs §4.2）--------------------------------------------

    private static CurtainWallJunction? WallJunction(
        CurtainWallObservationSet set,
        CurtainWallObservation wall,
        CompartmentWallObservation host,
        CurtainWallJunctionOptions options,
        List<PanelBand> covered,
        IReadOnlyList<FacadeSegment> facades)
    {
        // 躺在立面內的區劃牆不產出 CW-H（決議 14）。它不是「與帷幕牆的交接處」，它**就是**那一段
        // 外牆；它的交接處在自己的兩端，由垂直於立面的區劃牆各自產生。少了這一條，凡是與帷幕牆共面
        // 相接的實體外牆都會因兩線平行求不到交點，掉進「最近端點」的退路，回報一句「端點距帷幕牆
        // 0 mm，超過搜尋公差」的自相矛盾訊息。它仍以實體外牆身分供給別人的但書長度。
        if (wall.IsInFacadePlane(host.Start, host.End)) return null;

        // 交接帶的高程是區劃牆與帷幕牆高程的**交集**（決議 14，docs §4.2 步驟 2）。交集之外那一段
        // 外牆面（本文件所據模型是樓板邊緣的 450 mm）是第 79 條之 3 的層間帶，由 CW-V 回答；拿區劃牆
        // 全高當門檻只會要求實體外牆往下長進樓板，那是工具逼建模配合工具。
        var bandBottom = Math.Max(host.BottomElevationMm, wall.BaseElevationMm);
        var bandTop = Math.Min(host.TopElevationMm, wall.TopElevationMm);
        if (bandTop - bandBottom <= CurtainPanelObservation.TouchToleranceMm) return null;

        var crossing = FindCrossing(wall, host, options.JunctionSearchToleranceMm, facades);
        if (crossing is null) return null;

        // 交點落在定位線之外時，連續段兩端的帷幕牆都求得到同一個交點，但一個交接處只能有一列。
        if (crossing.Value.IsOffSegment && !Owns(set, wall, facades, crossing.Value.Along)) return null;

        var id = JunctionId(CurtainWallJunctionKind.WallToCurtainWall, wall.UniqueId, host.UniqueId);
        var at = crossing.Value.Along;
        var zone = ZoneOf(set, wall, at, lateral: true);
        if (zone is null) return null;

        if (!crossing.Value.IsResolved)
        {
            return CurtainWallJunction.Doubtful(
                id, CurtainWallJunctionKind.WallToCurtainWall, zone.ZoneId, wall.UniqueId,
                new CurtainWallJunctionDoubt(
                    CurtainWallJunctionDoubtKind.UnresolvedIntersection,
                    $"區劃牆（Id {host.UniqueId}）的端點距帷幕牆（Id {wall.UniqueId}）" +
                    $"{crossing.Value.GapMm.ToString("0.#", CultureInfo.InvariantCulture)} mm，" +
                    $"超過搜尋公差 {options.JunctionSearchToleranceMm.ToString("0.#", CultureInfo.InvariantCulture)} mm，" +
                    "無法確定交點位置，需人工覆核區劃牆是否確實交接於帷幕牆。",
                    new[] { host.UniqueId, wall.UniqueId }),
                hostUniqueId: host.UniqueId);
        }

        // 交接帶：交點左右各 MinFireRatedRunMm，高程為區劃牆在該處的高程帶。它有兩個用途——CW-O
        // 要扣掉帶內的嵌板，標示層要把帶內的嵌板塗紅。帶長本身**不由這些嵌板供給**（決議 13）。
        var reach = options.MinFireRatedRunMm;
        var band = PanelBand.Horizontal(at, reach, bandBottom, bandTop);
        covered.Add(band);
        var panels = wall.Panels.Where(band.Covers).Select(p => p.UniqueId).ToList();

        // 交接帶附近、且與區劃牆高程重疊的實體外牆。高程只要求「重疊」是為了先問矛盾；要計入帶長
        // 還得「涵蓋」整個高程帶，那是 FacadeRun 的事。
        //
        // 只看交點左右各 900 mm：門檻就是 900 mm，任何達標的連續段必定有一部分落在這個窗裡，不足的
        // 更是整段都在裡面——所以窗不會改變判定，只會讓一道很長的外牆回報的長度停在窗邊。
        var nearby = facades
            .Where(f => f.OverlapsAlong(at - reach, at + reach))
            .Where(f => f.Wall.OverlapsElevations(bandBottom, bandTop))
            .ToList();

        var clash = Clash(wall, nearby);
        if (clash is not null)
        {
            var (panel, facade) = clash.Value;
            return CurtainWallJunction.Doubtful(
                id, CurtainWallJunctionKind.WallToCurtainWall, zone.ZoneId, wall.UniqueId,
                new CurtainWallJunctionDoubt(
                    CurtainWallJunctionDoubtKind.FacadeWallOverlapsPanel,
                    $"帷幕牆（Id {wall.UniqueId}）與區劃牆（Id {host.UniqueId}）的交接帶上，" +
                    $"實體外牆（Id {facade.Wall.UniqueId}）與帷幕嵌板（Id {panel.UniqueId}）重疊，" +
                    "模型對同一片外牆同時說了兩種構造，無法判定交接處之外牆面，需人工覆核：" +
                    "以實體牆達成但書時，該段立面的帷幕嵌板應一併刪除。",
                    new[] { wall.UniqueId, host.UniqueId, facade.Wall.UniqueId, panel.UniqueId }),
                hostUniqueId: host.UniqueId,
                panelUniqueIds: panels,
                facadeWallUniqueIds: new[] { facade.Wall.UniqueId });
        }

        var measurement = FacadeRun(nearby, at, bandBottom, bandTop, host.RequiredFireRatingMinutes, options);

        return CurtainWallJunction.WallJunction(
            id, zone.ZoneId, wall.UniqueId, host.UniqueId,
            Projection(wall, host),
            measurement.RunMm,
            host.RequiredFireRatingMinutes,
            measurement.Rating,
            host.LegalReference,
            // 實體外牆上另開的門窗不讀，所以這個交接處**沒有**開口的事實可交（docs §9）。供 false
            // 會讓證據講出一件工具沒有查的事。
            null,
            panels,
            CurtainWallJunctionPlacement.At(wall.PointAt(at), bandBottom, bandTop),
            measurement.WallUniqueIds);
    }

    // --- CW-H 的但書：立面內的實體外牆（docs §4.2「交接處之外牆面」、決議 13）----------------------

    /// <summary>One solid exterior wall seen on the curtain wall's own axis, as the interval [Low, High].</summary>
    private sealed class FacadeSegment
    {
        public FacadeSegment(FacadeWallObservation wall, double lowMm, double highMm)
        {
            Wall = wall;
            LowMm = lowMm;
            HighMm = highMm;
        }

        public FacadeWallObservation Wall { get; }
        public double LowMm { get; }
        public double HighMm { get; }
        public double LengthMm => HighMm - LowMm;

        public bool CoversAlong(double millimeters) =>
            millimeters >= LowMm - CurtainPanelObservation.TouchToleranceMm &&
            millimeters <= HighMm + CurtainPanelObservation.TouchToleranceMm;

        public bool OverlapsAlong(double startMm, double endMm) =>
            endMm > LowMm + CurtainPanelObservation.TouchToleranceMm &&
            startMm < HighMm - CurtainPanelObservation.TouchToleranceMm;
    }

    /// <summary>
    /// The solid exterior walls lying in this curtain wall's plane, projected onto its axis so a run
    /// can be walked along it. Nothing is clipped to the curtain wall's own extent: 但書 asks about the
    /// 外牆面, and a rated wall continuing past the end of the glazing is exactly that.
    /// </summary>
    private static IReadOnlyList<FacadeSegment> FacadeSegments(CurtainWallObservationSet set, CurtainWallObservation wall) =>
        set.FacadeWalls
            .Where(f => wall.IsInFacadePlane(f.Start, f.End))
            .Select(f => new FacadeSegment(
                f,
                Math.Min(wall.ParameterOf(f.Start), wall.ParameterOf(f.End)),
                Math.Max(wall.ParameterOf(f.Start), wall.ParameterOf(f.End))))
            .Where(s => s.LengthMm > CurtainPanelObservation.TouchToleranceMm)
            .OrderBy(s => s.LowMm)
            .ThenBy(s => s.Wall.UniqueId, StringComparer.Ordinal)
            .ToList();

    /// <summary>What the façade came to at one CW-H junction, and what the evidence has to name.</summary>
    private sealed class FacadeMeasurement
    {
        /// <summary>立面在交點上沒有實體外牆：外牆面是玻璃，但書不成立——這是答案，不是不知道。</summary>
        public static readonly FacadeMeasurement Glazed = new(0.0, null, Array.Empty<string>());

        public FacadeMeasurement(double? runMm, ProvidedFireRating? rating, IReadOnlyList<string> wallUniqueIds)
        {
            RunMm = runMm;
            Rating = rating;
            WallUniqueIds = wallUniqueIds;
        }

        /// <summary>The continuous rated run along the façade; null when it could not be measured.</summary>
        public double? RunMm { get; }

        /// <summary>The lowest reading among the walls the measurement looked at.</summary>
        public ProvidedFireRating? Rating { get; }

        public IReadOnlyList<string> WallUniqueIds { get; }
    }

    /// <summary>
    /// The continuous rated length of 外牆面 at the junction: walk the façade both ways from the
    /// crossing over solid walls that span the whole 交接帶 — the compartment wall's elevations met with
    /// the curtain wall's own (決議 14) — and reach its required rating, and take the total
    /// (docs §4.2, 決議 13). Seams closer than
    /// <see cref="CurtainPanelObservation.TouchToleranceMm"/> are continuous — two walls butted
    /// together are one piece of façade.
    /// </summary>
    private static FacadeMeasurement FacadeRun(
        IReadOnlyList<FacadeSegment> nearby,
        double at,
        double bandBottomMm,
        double bandTopMm,
        double? requiredMinutes,
        CurtainWallJunctionOptions options)
    {
        // 只有高程上涵蓋整個交接帶的牆才計入：只封住其中一段等於留了缺口（docs §4.2 步驟 2）。
        var band = nearby
            .Where(f => f.Wall.CoversElevations(bandBottomMm, bandTopMm))
            .ToList();

        var here = band.FirstOrDefault(f => f.CoversAlong(at));
        if (here is null) return FacadeMeasurement.Glazed;

        // 沒有人說區劃牆要求多少時效，就沒有門檻可比，也就沒有可供給的長度（輸入契約，docs §12）。
        // 供 0 會讓一段沒有量過的外牆面讀起來像「未符合」。
        if (requiredMinutes is null)
            return new FacadeMeasurement(null, here.Wall.ProvidedFireRating, new[] { here.Wall.UniqueId });

        // 交點上那道牆讀不出時效：不得推定，也不得填 0——交由引擎判資料不足。
        if (!here.Wall.HasReadableRating)
            return new FacadeMeasurement(null, here.Wall.ProvidedFireRating, new[] { here.Wall.UniqueId });

        if (!here.Wall.Qualifies(requiredMinutes))
            return new FacadeMeasurement(0.0, here.Wall.ProvidedFireRating, new[] { here.Wall.UniqueId });

        var qualifying = band.Where(f => f.Wall.Qualifies(requiredMinutes)).ToList();
        var chain = Chain(qualifying, at);
        var low = chain.Min(f => f.LowMm);
        var high = chain.Max(f => f.HighMm);
        var run = high - low;
        var walls = chain.Select(f => f.Wall.UniqueId).ToList();
        var ratings = chain.Select(f => f.Wall.ProvidedFireRating!).ToList();

        // 不足 900 mm，而止於一道讀不出時效的外牆：那道牆若有時效就可能夠了。這與嵌板路徑的
        // 輸入契約同一個道理——量得到的是「至少這麼長」，不是「就是這麼長」。
        var unreadable = band
            .Where(f => !f.Wall.HasReadableRating && Adjoins(f, low, high))
            .ToList();
        if (run < options.MinFireRatedRunMm && unreadable.Count > 0)
            return new FacadeMeasurement(
                null,
                Worst(ratings.Concat(unreadable.Select(f => f.Wall.ProvidedFireRating).Where(r => r is not null).Select(r => r!))),
                walls.Concat(unreadable.Select(f => f.Wall.UniqueId)).Distinct(StringComparer.Ordinal).ToList());

        return new FacadeMeasurement(run, Worst(ratings), walls);
    }

    /// <summary>
    /// The maximal run of touching segments that contains <paramref name="at"/>. The caller has
    /// already found a segment covering it, so the answer is never empty.
    /// </summary>
    private static IReadOnlyList<FacadeSegment> Chain(IReadOnlyList<FacadeSegment> segments, double at) =>
        ChainAt(segments, at)!;

    /// <summary>
    /// The maximal run of touching segments that contains <paramref name="at"/>, or null when nothing
    /// covers it. This is the same 串接 the 但書 length is measured on, and 決議 14 asks it a second
    /// question: whether a crossing beyond the curtain wall's own ends still stands on 外牆面.
    /// </summary>
    private static IReadOnlyList<FacadeSegment>? ChainAt(IReadOnlyList<FacadeSegment> segments, double at)
    {
        var touch = CurtainPanelObservation.TouchToleranceMm;
        var groups = new List<List<FacadeSegment>>();

        foreach (var segment in segments.OrderBy(s => s.LowMm).ThenBy(s => s.HighMm))
        {
            var last = groups.Count == 0 ? null : groups[groups.Count - 1];
            if (last is null || segment.LowMm > last.Max(s => s.HighMm) + touch)
                groups.Add(new List<FacadeSegment> { segment });
            else
                last.Add(segment);
        }

        return groups.FirstOrDefault(g => at >= g.Min(s => s.LowMm) - touch && at <= g.Max(s => s.HighMm) + touch);
    }

    /// <summary>
    /// Whether a run of solid exterior walls butts against one end of this curtain wall (docs §4.2,
    /// 決議 14). A seam wider than <see cref="CurtainPanelObservation.TouchToleranceMm"/> is not a
    /// junction: the tool does not bridge a gap in the façade for the user, for the same reason it
    /// never crosses a grid line.
    /// </summary>
    private static bool Adjoins(IReadOnlyList<FacadeSegment> chain, CurtainWallObservation wall)
    {
        var touch = CurtainPanelObservation.TouchToleranceMm;
        return Math.Abs(chain.Max(s => s.HighMm)) <= touch ||
               Math.Abs(chain.Min(s => s.LowMm) - wall.LengthMm) <= touch;
    }

    /// <summary>
    /// 同一個交接處只能有一列（docs §4.2「歸屬」）. A run of solid wall that has a curtain wall at each
    /// end is found by both of them, so the owner is settled by a rule that cannot depend on which way
    /// either wall was drawn or on the order they were read: of the curtain walls coplanar with the run
    /// and touching it, the one whose touching endpoint is lexicographically smallest by
    /// <c>(X, Y)</c> — 沿軸較低側 would not do, since in a real model both walls are drawn away from
    /// the crossing and both parameters are negative.
    /// </summary>
    private static bool Owns(
        CurtainWallObservationSet set,
        CurtainWallObservation wall,
        IReadOnlyList<FacadeSegment> facades,
        double at)
    {
        var chain = ChainAt(facades, at);
        if (chain is null) return false;

        var touch = CurtainPanelObservation.TouchToleranceMm;
        var ends = new[] { wall.PointAt(chain.Min(s => s.LowMm)), wall.PointAt(chain.Max(s => s.HighMm)) };

        var owner = set.CurtainWalls
            .Where(w => w.IsPlanar && w.IsInFacadePlane(ends[0], ends[1]))
            .Select(w => (Wall: w, Touch: TouchingEnd(w, ends, touch)))
            .Where(x => x.Touch is not null)
            .OrderBy(x => x.Touch!.Value.X)
            .ThenBy(x => x.Touch!.Value.Y)
            .ThenBy(x => x.Wall.UniqueId, StringComparer.Ordinal)
            .Select(x => x.Wall.UniqueId)
            .FirstOrDefault();

        return string.Equals(owner, wall.UniqueId, StringComparison.Ordinal);
    }

    /// <summary>Which end of a curtain wall butts against a run of solid wall, if either does.</summary>
    private static Point2D? TouchingEnd(CurtainWallObservation wall, IReadOnlyList<Point2D> runEnds, double toleranceMm)
    {
        var candidates = new[] { wall.Start, wall.End }
            .Where(p => runEnds.Any(e => p.DistanceTo(e) <= toleranceMm))
            .OrderBy(p => p.X)
            .ThenBy(p => p.Y)
            .ToList();

        return candidates.Count == 0 ? (Point2D?)null : candidates[0];
    }

    /// <summary>Whether a segment butts against either end of the run measured so far.</summary>
    private static bool Adjoins(FacadeSegment segment, double lowMm, double highMm) =>
        Math.Abs(segment.HighMm - lowMm) <= CurtainPanelObservation.TouchToleranceMm ||
        Math.Abs(segment.LowMm - highMm) <= CurtainPanelObservation.TouchToleranceMm;

    /// <summary>
    /// A curtain panel and a solid exterior wall on the same stretch of façade, at the same
    /// elevations: the model says two contradictory things about one piece of exterior wall, and the
    /// tool will not pick one for the user (docs §4.2). It is also the detector for the easiest
    /// modelling mistake this design invites — a solid wall laid over glazing nobody deleted.
    /// </summary>
    private static (CurtainPanelObservation Panel, FacadeSegment Wall)? Clash(
        CurtainWallObservation wall,
        IReadOnlyList<FacadeSegment> nearby)
    {
        foreach (var segment in nearby)
        {
            foreach (var panel in wall.Panels)
            {
                if (panel.OverlapsAlong(segment.LowMm, segment.HighMm) &&
                    panel.OverlapsElevations(segment.Wall.BottomElevationMm, segment.Wall.TopElevationMm))
                    return (panel, segment);
            }
        }

        return null;
    }

    // --- CW-V：區劃樓地板與帷幕牆之層間交接（docs §4.3）-----------------------------------------

    private static IEnumerable<CurtainWallJunction> Spandrels(
        CurtainWallObservationSet set,
        CurtainWallObservation wall,
        CompartmentFloorObservation floor,
        CurtainWallJunctionOptions options,
        List<PanelBand> covered)
    {
        if (!floor.HasOutline || !wall.SpansElevation(floor.ElevationMm)) yield break;

        var horizontal = wall.GridLines.Where(g => g.Direction == CurtainGridLineDirection.Horizontal).ToList();
        var index = 0;
        foreach (var span in InsideSpans(wall, floor))
        {
            var samples = Samples(span.Low, span.High, options.SamplingIntervalMm).ToList();
            if (samples.Count == 0) continue;

            var measurements = new List<(RunMeasurement Measurement, double At)>();
            foreach (var at in samples)
            {
                var column = wall.Panels.Where(p => p.CoversAlong(at)).ToList();
                measurements.Add((Measure(
                    Up(column),
                    floor.ElevationMm,
                    floor.RequiredFireRatingMinutes,
                    options.MinFireRatedRunMm,
                    horizontal,
                    wall.BaseElevationMm,
                    wall.TopElevationMm), at));
            }

            var worst = RunMeasurement.Worst(measurements.Select(m => m.Measurement).ToList());
            var worstAt = measurements.First(m => ReferenceEquals(m.Measurement, worst)).At;

            var zone = ZoneOf(set, wall, worstAt);
            if (zone is null) continue;

            var id = JunctionId(CurtainWallJunctionKind.FloorToCurtainWall, wall.UniqueId, floor.UniqueId,
                index.ToString(CultureInfo.InvariantCulture));
            index++;
            covered.Add(PanelBand.Vertical(span.Low, span.High, floor.ElevationMm, options.MinFireRatedRunMm));

            if (worst.Split.Count > 0)
            {
                yield return CurtainWallJunction.Doubtful(
                    id, CurtainWallJunctionKind.FloorToCurtainWall, zone.ZoneId, wall.UniqueId,
                    new CurtainWallJunctionDoubt(
                        CurtainWallJunctionDoubtKind.SplitByGridLine,
                        SplitMessage(wall, worst, options),
                        worst.Split.Select(g => g.UniqueId).Concat(new[] { wall.UniqueId, floor.UniqueId })),
                    hostUniqueId: floor.UniqueId,
                    panelUniqueIds: worst.PanelUniqueIds);
                continue;
            }

            yield return CurtainWallJunction.Spandrel(
                id, zone.ZoneId, wall.UniqueId, floor.UniqueId,
                Projection(wall, floor, worstAt),
                worst.RunMm,
                floor.RequiredFireRatingMinutes,
                worst.MinRating,
                worst.HasUnprotectedOpening,
                worst.PanelUniqueIds,
                SpandrelPlacement(wall, floor, span, options));
        }
    }

    /// <summary>
    /// The 層間帶 as §4.5 defines it — the floor edge, 900 mm above and below the slab — clipped to the
    /// curtain wall itself, which is what the review view draws red (docs §7.1). Null when the wall
    /// leaves no band at this floor, e.g. a slab at its very top.
    /// </summary>
    private static CurtainWallJunctionPlacement? SpandrelPlacement(
        CurtainWallObservation wall,
        CompartmentFloorObservation floor,
        Span span,
        CurtainWallJunctionOptions options)
    {
        var bottom = Math.Max(wall.BaseElevationMm, floor.ElevationMm - options.MinFireRatedRunMm);
        var top = Math.Min(wall.TopElevationMm, floor.ElevationMm + options.MinFireRatedRunMm);
        if (top - bottom <= SnapMm) return null;

        return CurtainWallJunctionPlacement.Band(wall.PointAt(span.Low), wall.PointAt(span.High), bottom, top);
    }

    /// <summary>
    /// 第79條之3第3項：a curtain wall that runs past the storey above with no 區劃樓地板 there is a
    /// 連跨複數樓層 vertical space, and belongs to 第79條之2 rather than to this check (docs §3.4).
    /// </summary>
    private static CurtainWallJunction? VerticalSpace(CurtainWallObservationSet set, CurtainWallObservation wall)
    {
        var next = set.LevelElevationsMm.Where(e => e > set.LevelElevationMm + SnapMm).Cast<double?>().FirstOrDefault();
        if (next is not double above || wall.TopElevationMm <= above + SnapMm) return null;
        if (set.CompartmentFloors.Any(f => IsSameElevation(f.ElevationMm, above) && f.HasOutline && InsideSpans(wall, f).Any()))
            return null;

        var zone = ZoneOf(set, wall, wall.LengthMm / 2.0);
        if (zone is null) return null;

        var storeys = set.LevelElevationsMm.Count(e => e > set.LevelElevationMm + SnapMm && e < wall.TopElevationMm - SnapMm) + 1;
        return CurtainWallJunction.Doubtful(
            JunctionId(CurtainWallJunctionKind.FloorToCurtainWall, wall.UniqueId),
            CurtainWallJunctionKind.FloorToCurtainWall, zone.ZoneId, wall.UniqueId,
            new CurtainWallJunctionDoubt(
                CurtainWallJunctionDoubtKind.VerticalCompartmentSpace,
                $"帷幕牆（Id {wall.UniqueId}）連跨 {storeys} 個樓層且上方樓層無區劃樓地板與其交接，" +
                $"屬無法逐層區劃分隔之垂直空間，改依{CurtainWallJunctionReferences.Article79_2}檢討，本項不適用。",
                new[] { wall.UniqueId }));
    }

    // --- CW-O：其餘嵌板（第79條之4）-------------------------------------------------------------

    private static CurtainWallJunction? OtherPanels(
        CurtainWallZoneObservation zone,
        CurtainWallObservation wall,
        IEnumerable<CurtainPanelObservation> panels)
    {
        var rest = panels.ToList();
        if (rest.Count == 0) return null;

        return CurtainWallJunction.OtherPanels(
            JunctionId(CurtainWallJunctionKind.CurtainPanelOther, wall.UniqueId),
            zone.ZoneId,
            wall.UniqueId,
            Worst(rest.Select(p => p.Rating))!,
            rest.Select(p => p.UniqueId));
    }

    // --- run measurement -------------------------------------------------------------------------

    /// <summary>One panel seen along the axis a run is measured on, as the interval [Low, High].</summary>
    private readonly struct Slab
    {
        public Slab(CurtainPanelObservation panel, double low, double high)
        {
            Panel = panel;
            Low = low;
            High = high;
        }

        public CurtainPanelObservation Panel { get; }
        public double Low { get; }
        public double High { get; }
    }

    /// <summary>The panel column a CW-V run walks up: every panel at the sample's distance along.</summary>
    private static IReadOnlyList<Slab> Up(IReadOnlyList<CurtainPanelObservation> column) =>
        column.OrderBy(p => p.BottomMm).Select(p => new Slab(p, p.BottomMm, p.TopMm)).ToList();

    /// <summary>What one run came to, and everything the verdict needs to explain itself.</summary>
    private sealed class RunMeasurement
    {
        public static readonly RunMeasurement Nothing = new(0.0, null, false, Array.Empty<string>(), Array.Empty<CurtainGridLineObservation>(), 0.0);

        public RunMeasurement(
            double? runMm,
            ProvidedFireRating? minRating,
            bool hasUnprotectedOpening,
            IReadOnlyList<string> panelUniqueIds,
            IReadOnlyList<CurtainGridLineObservation> split,
            double bridgedMm)
        {
            RunMm = runMm;
            MinRating = minRating;
            HasUnprotectedOpening = hasUnprotectedOpening;
            PanelUniqueIds = panelUniqueIds;
            Split = split;
            BridgedMm = bridgedMm;
        }

        /// <summary>The continuous rated run; null when the band could not be measured at all.</summary>
        public double? RunMm { get; }

        public ProvidedFireRating? MinRating { get; }
        public bool HasUnprotectedOpening { get; }
        public IReadOnlyList<string> PanelUniqueIds { get; }

        /// <summary>The grid lines that, if redundant, would let the band reach the 900 mm (docs §4.5).</summary>
        public IReadOnlyList<CurtainGridLineObservation> Split { get; }

        /// <summary>What the run would have been had those grid lines not been there.</summary>
        public double BridgedMm { get; }

        /// <summary>
        /// The most unfavourable of several measurements: a band that could not be measured beats a
        /// measured one (資料不足 is not a licence to pick the panel row that happens to answer), a
        /// suspected redundant grid line beats a plain measurement, and otherwise the shortest run.
        /// </summary>
        public static RunMeasurement Worst(IReadOnlyList<RunMeasurement> measurements) =>
            measurements
                .OrderBy(m => m.RunMm is null ? 0 : m.Split.Count > 0 ? 1 : 2)
                .ThenBy(m => m.RunMm ?? 0.0)
                .First();
    }

    /// <summary>
    /// The continuous rated run at <paramref name="at"/>, measured inside the one panel it falls in
    /// (docs §4.1: the tool never bridges a grid line). <paramref name="low"/> and
    /// <paramref name="high"/> are the curtain wall's own limits on this axis: a run stopped by them
    /// rather than by a break is not supplied, because the rest of the façade was never read.
    /// </summary>
    private static RunMeasurement Measure(
        IReadOnlyList<Slab> slabs,
        double at,
        double? requiredMinutes,
        double minRunMm,
        IReadOnlyList<CurtainGridLineObservation> gridLines,
        double low,
        double high)
    {
        var index = -1;
        for (var i = 0; i < slabs.Count; i++)
        {
            if (at < slabs[i].Low - CurtainPanelObservation.TouchToleranceMm) continue;
            if (at > slabs[i].High + CurtainPanelObservation.TouchToleranceMm) continue;
            index = i;
            break;
        }

        if (index < 0) return RunMeasurement.Nothing;

        var here = slabs[index];

        // Nobody said what the host requires, so there is no bar for a panel to clear and no run to
        // report. Supplying 0 here would read as 未符合 for a band nobody ever measured.
        if (requiredMinutes is null)
            return new RunMeasurement(null, here.Panel.Rating, here.Panel.IsUnprotectedOpening,
                new[] { here.Panel.UniqueId }, Array.Empty<CurtainGridLineObservation>(), 0.0);

        var qualifies = here.Panel.Qualifies(requiredMinutes);
        var looked = new List<CurtainPanelObservation> { here.Panel };
        var run = qualifies ? here.High - here.Low : 0.0;

        // Short of the 900 mm: ask §4.5's question — do the grid lines that bound this panel merely
        // split what is really one continuous band, or is the break real?
        var split = new List<CurtainGridLineObservation>();
        var bridged = run;
        if (qualifies && run < minRunMm)
        {
            bridged += Walk(slabs, index, requiredMinutes, minRunMm - run, gridLines, looked, split, forward: false);
            if (bridged < minRunMm)
                bridged += Walk(slabs, index, requiredMinutes, minRunMm - bridged, gridLines, looked, split, forward: true);
        }

        var worst = Worst(looked.Select(p => p.Rating));
        var panelIds = looked.Select(p => p.UniqueId).ToList();
        var opening = looked.Any(p => p.IsUnprotectedOpening);

        // 輸入契約：any panel the measurement looked at without a readable design rating means no run
        // is supplied at all — never 0, which would read as 未符合 instead of 資料不足 (docs §12).
        if (worst is null || !worst.IsRated)
            return new RunMeasurement(null, worst, opening, panelIds, Array.Empty<CurtainGridLineObservation>(), bridged);

        if (split.Count > 0 && bridged >= minRunMm)
            return new RunMeasurement(run, worst, opening, panelIds, split, bridged);

        // Stopped by the curtain wall's own edge rather than by a break: the façade continues into a
        // wall that was not read, so the run is unknown, not short.
        if (qualifies && run < minRunMm && TouchesLimit(here, low, high))
            return new RunMeasurement(null, worst, opening, panelIds, Array.Empty<CurtainGridLineObservation>(), bridged);

        return new RunMeasurement(run, worst, opening, panelIds, Array.Empty<CurtainGridLineObservation>(), bridged);
    }

    /// <summary>
    /// Walks outwards across touching panels that would qualify, recording the grid line at each
    /// boundary crossed, until <paramref name="needed"/> is covered or a genuine break is met.
    /// </summary>
    private static double Walk(
        IReadOnlyList<Slab> slabs,
        int from,
        double? requiredMinutes,
        double needed,
        IReadOnlyList<CurtainGridLineObservation> gridLines,
        List<CurtainPanelObservation> looked,
        List<CurtainGridLineObservation> split,
        bool forward)
    {
        var gained = 0.0;
        var i = from;
        while (gained < needed)
        {
            var edge = forward ? slabs[i].High : slabs[i].Low;
            var next = forward ? i + 1 : i - 1;
            if (next < 0 || next >= slabs.Count) break;

            var neighbour = slabs[next];
            var touches = forward
                ? Math.Abs(neighbour.Low - edge) <= CurtainPanelObservation.TouchToleranceMm
                : Math.Abs(neighbour.High - edge) <= CurtainPanelObservation.TouchToleranceMm;
            if (!touches) break;

            if (!looked.Contains(neighbour.Panel)) looked.Add(neighbour.Panel);

            // An unreadable rating stops the walk too: whether the break is real cannot be answered,
            // and the panel is now in `looked`, so no run will be supplied either way.
            if (!neighbour.Panel.Rating.IsRated) break;
            if (!neighbour.Panel.Qualifies(requiredMinutes)) break;

            var line = gridLines.FirstOrDefault(g => Math.Abs(g.PositionMm - edge) <= SnapMm);
            if (line is null) break;
            if (!split.Contains(line)) split.Add(line);

            gained += neighbour.High - neighbour.Low;
            i = next;
        }

        return gained;
    }

    private static bool TouchesLimit(Slab slab, double low, double high) =>
        high - low > SnapMm &&
        (Math.Abs(slab.Low - low) <= CurtainPanelObservation.TouchToleranceMm ||
         Math.Abs(slab.High - high) <= CurtainPanelObservation.TouchToleranceMm);

    /// <summary>
    /// The reading a band is reported by: anything unreadable comes first, because that is what a
    /// user has to act on, and 未設定 before a value nobody can interpret. Only then the lowest rating.
    /// </summary>
    private static ProvidedFireRating? Worst(IEnumerable<ProvidedFireRating> ratings) =>
        ratings
            .OrderBy(r => r.Kind switch
            {
                ProvidedFireRatingKind.Missing => 0,
                ProvidedFireRatingKind.Unreadable => 1,
                ProvidedFireRatingKind.Undeterminable => 2,
                _ => 3
            })
            .ThenBy(r => r.Minutes ?? 0.0)
            .FirstOrDefault();

    /// <summary>
    /// §4.5 的提問，只有 CW-V 會走到：CW-H 自決議 13 起不再量嵌板，grid line 切不到它的但書長度。
    /// </summary>
    private static string SplitMessage(
        CurtainWallObservation wall,
        RunMeasurement measurement,
        CurtainWallJunctionOptions options)
    {
        var ids = string.Join("、", measurement.Split.Select(g => $"Id {g.UniqueId}（{g.PositionMm.ToString("0.#", CultureInfo.InvariantCulture)} mm）"));
        return $"帷幕牆（Id {wall.UniqueId}）的層間帶被 grid line {ids} 分割，" +
               $"兩側嵌板的設計防火時效皆足夠，合計 {measurement.BridgedMm.ToString("0.#", CultureInfo.InvariantCulture)} mm " +
               $"已達 {options.MinFireRatedRunMm.ToString("0.#", CultureInfo.InvariantCulture)} mm，" +
               $"但實際連續段僅 {(measurement.RunMm ?? 0.0).ToString("0.#", CultureInfo.InvariantCulture)} mm。" +
               "請確認該 grid line 是否為真實構造斷點；若否，請刪除後重跑。";
    }

    // --- plan geometry ---------------------------------------------------------------------------

    private readonly struct Crossing
    {
        public Crossing(double along, bool isResolved, double gapMm, bool isOffSegment = false)
        {
            Along = along;
            IsResolved = isResolved;
            GapMm = gapMm;
            IsOffSegment = isOffSegment;
        }

        /// <summary>How far along the curtain wall's location line the crossing is; off its own extent when <see cref="IsOffSegment"/>.</summary>
        public double Along { get; }

        public bool IsResolved { get; }
        public double GapMm { get; }

        /// <summary>
        /// The crossing sits on the extension of the location line, on a run of solid exterior wall
        /// that replaced that stretch of curtain wall (決議 14). It is a junction like any other; what
        /// it needs extra is an owner, since the run has a curtain wall at each end.
        /// </summary>
        public bool IsOffSegment { get; }
    }

    /// <summary>
    /// Where a compartment wall meets the curtain wall, after extending it by the search tolerance at
    /// both ends (docs §4.2 step 2). A wall that stops near the curtain wall without reaching it is
    /// returned unresolved: it was plainly meant to meet, and the tool will not guess by how much.
    /// <para>
    /// A crossing beyond the curtain wall's own ends is not dropped (決議 14): correct modelling puts a
    /// solid rated wall in place of that stretch of glazing, which cuts the curtain wall in two and
    /// leaves the crossing between them. It stands as a junction when the run of solid exterior wall
    /// covering it butts against <b>this</b> curtain wall; otherwise there is no junction here at all,
    /// and 人工覆核 is left to the one case it means — an end that nearly reached the façade.
    /// </para>
    /// </summary>
    private static Crossing? FindCrossing(
        CurtainWallObservation wall,
        CompartmentWallObservation host,
        double toleranceMm,
        IReadOnlyList<FacadeSegment> facades)
    {
        var dx = host.End.X - host.Start.X;
        var dy = host.End.Y - host.Start.Y;
        var length = Math.Sqrt((dx * dx) + (dy * dy));
        var ex = dx / length * toleranceMm;
        var ey = dy / length * toleranceMm;
        var start = new Point2D(host.Start.X - ex, host.Start.Y - ey);
        var end = new Point2D(host.End.X + ex, host.End.Y + ey);

        var rx = end.X - start.X;
        var ry = end.Y - start.Y;
        var sx = wall.End.X - wall.Start.X;
        var sy = wall.End.Y - wall.Start.Y;
        var denominator = (rx * sy) - (ry * sx);

        if (Math.Abs(denominator) > ParallelEpsilon)
        {
            var qx = wall.Start.X - start.X;
            var qy = wall.Start.Y - start.Y;
            var t = ((qx * sy) - (qy * sx)) / denominator;
            var u = ((qx * ry) - (qy * rx)) / denominator;

            if (t >= 0 && t <= 1)
            {
                if (u >= -SnapMm / wall.LengthMm && u <= 1 + (SnapMm / wall.LengthMm))
                    return new Crossing(Math.Min(Math.Max(u * wall.LengthMm, 0.0), wall.LengthMm), true, 0.0);

                // 落在延長線上：只有當實體外牆的連續段既蓋住這個位置、又接上這片帷幕牆，那裡才真的
                // 是「交接處之外牆面」（決議 14）。
                var beyond = u * wall.LengthMm;
                var chain = ChainAt(facades, beyond);
                return chain is not null && Adjoins(chain, wall)
                    ? new Crossing(beyond, true, 0.0, isOffSegment: true)
                    : (Crossing?)null;
            }
        }

        // No crossing: was it close enough that the model plainly meant one?
        var nearest = new[] { host.Start, host.End }
            .Select(p => (Point: p, Distance: DistanceToSegment(p, wall.Start, wall.End)))
            .OrderBy(x => x.Distance)
            .First();

        if (nearest.Distance > toleranceMm * 2.0) return null;

        var along = Math.Min(Math.Max(wall.ParameterOf(nearest.Point), 0.0), wall.LengthMm);
        return new Crossing(along, false, nearest.Distance);
    }

    private static double DistanceToSegment(Point2D point, Point2D start, Point2D end)
    {
        var dx = end.X - start.X;
        var dy = end.Y - start.Y;
        var squared = (dx * dx) + (dy * dy);
        var t = squared <= ParallelEpsilon ? 0.0 : (((point.X - start.X) * dx) + ((point.Y - start.Y) * dy)) / squared;
        t = Math.Min(Math.Max(t, 0.0), 1.0);
        return point.DistanceTo(new Point2D(start.X + (dx * t), start.Y + (dy * t)));
    }

    /// <summary>突出量 of a compartment wall: how far its far end clears the curtain wall's outside face.</summary>
    private static double Projection(CurtainWallObservation wall, CompartmentWallObservation host) =>
        Math.Max(wall.ProjectionOf(host.Start), wall.ProjectionOf(host.End));

    /// <summary>
    /// 突出量 of a floor or 防火簷板 at one sample: how far the slab reaches out past the curtain
    /// wall's outside face, found by casting a ray outwards from the location line (docs §4.3 step 4).
    /// </summary>
    private static double Projection(CurtainWallObservation wall, CompartmentFloorObservation floor, double at)
    {
        var origin = wall.PointAt(at);
        var furthest = 0.0;

        foreach (var loop in floor.OutlineLoops)
        {
            for (int i = 0, j = loop.Count - 1; i < loop.Count; j = i++)
            {
                var a = loop[j];
                var b = loop[i];
                var dx = b.X - a.X;
                var dy = b.Y - a.Y;
                var denominator = (dx * wall.ExteriorNormal.Y) - (dy * wall.ExteriorNormal.X);
                if (Math.Abs(denominator) <= ParallelEpsilon) continue;

                var ax = a.X - origin.X;
                var ay = a.Y - origin.Y;
                var s = -((ax * wall.ExteriorNormal.Y) - (ay * wall.ExteriorNormal.X)) / denominator;
                if (s < 0 || s > 1) continue;

                var t = ((ax * dy) - (ay * dx)) / ((wall.ExteriorNormal.X * dy) - (wall.ExteriorNormal.Y * dx));
                if (t > furthest) furthest = t;
            }
        }

        return Math.Max(0.0, furthest - wall.ExteriorOffsetMm);
    }

    private readonly struct Span
    {
        public Span(double low, double high)
        {
            Low = low;
            High = high;
        }

        public double Low { get; }
        public double High { get; }
        public double Length => High - Low;
    }

    /// <summary>The stretches of the curtain wall that run along the floor, as distances along it.</summary>
    private static IEnumerable<Span> InsideSpans(CurtainWallObservation wall, CompartmentFloorObservation floor)
    {
        var cuts = new List<double> { 0.0, wall.LengthMm };
        foreach (var loop in floor.OutlineLoops)
        {
            for (int i = 0, j = loop.Count - 1; i < loop.Count; j = i++)
            {
                var hit = SegmentCut(wall, loop[j], loop[i]);
                if (hit is double u) cuts.Add(u);
            }
        }

        var ordered = cuts.Where(u => u >= 0 && u <= wall.LengthMm).Distinct().OrderBy(u => u).ToList();
        for (var i = 0; i + 1 < ordered.Count; i++)
        {
            var low = ordered[i];
            var high = ordered[i + 1];
            if (high - low <= SnapMm) continue;
            if (floor.Contains(wall.PointAt((low + high) / 2.0))) yield return new Span(low, high);
        }
    }

    private static double? SegmentCut(CurtainWallObservation wall, Point2D a, Point2D b)
    {
        var rx = wall.End.X - wall.Start.X;
        var ry = wall.End.Y - wall.Start.Y;
        var sx = b.X - a.X;
        var sy = b.Y - a.Y;
        var denominator = (rx * sy) - (ry * sx);
        if (Math.Abs(denominator) <= ParallelEpsilon) return null;

        var qx = a.X - wall.Start.X;
        var qy = a.Y - wall.Start.Y;
        var t = ((qx * sy) - (qy * sx)) / denominator;
        var u = ((qx * ry) - (qy * rx)) / denominator;
        return t >= 0 && t <= 1 && u >= 0 && u <= 1 ? t * wall.LengthMm : (double?)null;
    }

    /// <summary>Sample points along a spandrel band: every interval, and never fewer than three (docs §4.3).</summary>
    private static IEnumerable<double> Samples(double low, double high, double intervalMm)
    {
        var length = high - low;
        if (length <= SnapMm) yield break;

        var inset = Math.Min(50.0, length / 10.0);
        var from = low + inset;
        var to = high - inset;
        var count = Math.Max(MinimumSamples, (int)Math.Ceiling(length / intervalMm) + 1);

        for (var i = 0; i < count; i++)
            yield return count == 1 ? (from + to) / 2.0 : from + ((to - from) * i / (count - 1));
    }

    // --- placing a junction in a 區劃 --------------------------------------------------------------

    /// <summary>
    /// The 區劃 a point on the façade belongs to, found by probing just inside the curtain wall. At a
    /// CW-H junction the compartment wall is the boundary between two zones, so both sides are probed
    /// and the lower Zone ID wins — the junction is one place and gets one result, and which zone it
    /// is filed under has to be the same on every run.
    /// <para>
    /// The probe is not clamped to the wall's own extent: a crossing on the extension of the location
    /// line (決議 14) is a real place, and clamping would move both lateral probes to the same side of
    /// it and file the junction under whichever 區劃 happened to be there.
    /// </para>
    /// </summary>
    private static CurtainWallZoneObservation? ZoneOf(
        CurtainWallObservationSet set,
        CurtainWallObservation wall,
        double at,
        bool lateral = false)
    {
        var depth = wall.ExteriorOffsetMm + ZoneProbeMm;
        var offsets = lateral ? new[] { -ZoneProbeMm, ZoneProbeMm } : new[] { 0.0 };

        return offsets
            .Select(o => wall.PointAt(at + o))
            .Select(p => new Point2D(p.X - (wall.ExteriorNormal.X * depth), p.Y - (wall.ExteriorNormal.Y * depth)))
            .Select(set.ZoneAt)
            .Where(z => z is not null)
            .OrderBy(z => z!.ZoneId)
            .FirstOrDefault();
    }

    /// <summary>The stretch of façade one junction already accounts for; what is left over is CW-O.</summary>
    private readonly struct PanelBand
    {
        private PanelBand(double startMm, double endMm, double bottomMm, double topMm)
        {
            StartMm = startMm;
            EndMm = endMm;
            BottomMm = bottomMm;
            TopMm = topMm;
        }

        public double StartMm { get; }
        public double EndMm { get; }
        public double BottomMm { get; }
        public double TopMm { get; }

        public static PanelBand Horizontal(double at, double reachMm, double bottomMm, double topMm) =>
            new(at - reachMm, at + reachMm, bottomMm, topMm);

        public static PanelBand Vertical(double startMm, double endMm, double at, double reachMm) =>
            new(startMm, endMm, at - reachMm, at + reachMm);

        /// <summary>
        /// A panel belongs to the band when its middle is in it, not merely when it grazes it: a
        /// storey-high glazed panel that reaches into a spandrel band is still 其他部分外牆, and
        /// 第79條之4 has to be answered for it (docs §3.3).
        /// </summary>
        public bool Covers(CurtainPanelObservation panel)
        {
            var along = (panel.StartMm + panel.EndMm) / 2.0;
            var elevation = (panel.BottomMm + panel.TopMm) / 2.0;
            return along >= StartMm && along <= EndMm && elevation >= BottomMm && elevation <= TopMm;
        }
    }

    private static bool IsSameElevation(double first, double second) => Math.Abs(first - second) <= SnapMm;

    private static string JunctionId(CurtainWallJunctionKind kind, params string[] parts) =>
        string.Join(":", new[] { Prefix(kind) }.Concat(parts));

    private static string Prefix(CurtainWallJunctionKind kind) => kind switch
    {
        CurtainWallJunctionKind.WallToCurtainWall => "CW-H",
        CurtainWallJunctionKind.FloorToCurtainWall => "CW-V",
        _ => "CW-O"
    };
}
