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

    /// <param name="observations">What the reader read for one package.</param>
    /// <param name="options">The constants of docs §4.4.</param>
    /// <param name="verticalCompartmentZoneIds">
    /// The 區劃 of this storey whose 用途 is a 第79條之2 垂直區劃 (挑空、樓梯間……). A curtain wall running
    /// through this level with no floor meeting it is handed on to 第79條之2 only when the 區劃 behind
    /// it is one of these; otherwise it is 人工覆核 (docs §3.4).
    /// </param>
    public static IReadOnlyList<CurtainWallJunction> Resolve(
        CurtainWallObservationSet observations,
        CurtainWallJunctionOptions? options = null,
        IEnumerable<Guid>? verticalCompartmentZoneIds = null)
    {
        if (observations is null) throw new ArgumentNullException(nameof(observations));
        options ??= CurtainWallJunctionOptions.Default;
        var verticalZones = new HashSet<Guid>(verticalCompartmentZoneIds ?? Array.Empty<Guid>());

        // 弧形帷幕牆以段登錄（docs §4.7），同一個 UniqueId 的各段在這裡合回一道牆。
        var junctions = new List<CurtainWallJunction>();
        foreach (var wall in observations.CurtainWalls.GroupBy(w => w.UniqueId, StringComparer.Ordinal))
            junctions.AddRange(ForCurtainWall(observations, wall.ToList(), options, verticalZones));

        return junctions
            .OrderBy(x => x.ZoneId)
            .ThenBy(x => (int)x.Kind)
            .ThenBy(x => x.JunctionId, StringComparer.Ordinal)
            .ToList();
    }

    private static IEnumerable<CurtainWallJunction> ForCurtainWall(
        CurtainWallObservationSet set,
        IReadOnlyList<CurtainWallObservation> facets,
        CurtainWallJunctionOptions options,
        ISet<Guid> verticalCompartmentZoneIds)
    {
        // Revit's Wall.Orientation is the location line turned −90° about Z and says nothing about
        // which side the building is on, so the side the 區劃 lies on decides it (docs §4.6, 決議 15).
        // Each facet of an arc wall is oriented on its own: it is its own plane.
        var elevation = new Elevation(facets.Select(f => Oriented(set, f)).ToList());
        var wall = elevation.First;

        var wallZone = elevation.ZoneAtMiddle(set);

        // 室內外判定（docs §4.8）。每一個平面段各自判，再合回整道牆：第79條第3、4項、第79條之3、
        // 第79條之4 問的都是「外牆」，不是外牆就不該用這三項量它。
        var exposure = CurtainWallExposureClassifier.Merge(
            elevation.Facets.Select(f => ExposureOf(set, f)).ToList());

        if (!exposure.IsExterior)
        {
            foreach (var junction in NonExteriorJunctions(set, elevation, wallZone, options, verticalCompartmentZoneIds, exposure))
                yield return junction;

            yield break;
        }

        // A curved, sloped or warped wall is not measured at all: the two clauses that need a plane
        // go to 人工覆核, and only 第79條之4 — which asks nothing of geometry — is still answered.
        if (!wall.IsPlanar)
        {
            // Unmeasured, the storey below's or above's wall would put the same two 人工覆核 rows in
            // this package as in its own; it is answered there.
            if (wallZone is not null && StandsInThisStorey(set, wall, options.JunctionSearchToleranceMm))
            {
                var own = OfThisStorey(set, wall.Panels).ToList();
                foreach (var kind in new[] { CurtainWallJunctionKind.WallToCurtainWall, CurtainWallJunctionKind.FloorToCurtainWall })
                {
                    yield return CurtainWallJunction.Doubtful(
                        JunctionId(kind, wall.UniqueId), kind, wallZone.ZoneId, wall.UniqueId,
                        new CurtainWallJunctionDoubt(
                            CurtainWallJunctionDoubtKind.NonPlanarCurtainWall,
                            $"帷幕牆（Id {wall.UniqueId}）{wall.NonPlanarReason}，無法以平面量測交接處，需人工覆核。",
                            new[] { wall.UniqueId }),
                        panelUniqueIds: own.Select(p => p.UniqueId));
                }

                foreach (var all in OtherPanelJunctions(wallZone, wall, own)) yield return all;
            }

            yield break;
        }

        var results = new List<CurtainWallJunction>();

        foreach (var host in set.CompartmentWalls)
        {
            results.AddRange(WallJunctions(set, elevation, host, options));
        }

        foreach (var floor in set.CompartmentFloors.Where(f => IsFloorOfThisLevel(set, f, options)))
        {
            foreach (var junction in Spandrels(set, elevation, floor, options))
                results.Add(junction);
        }

        var noFloor = NoFloorAtThisLevel(set, elevation, wallZone, options, verticalCompartmentZoneIds);
        if (noFloor is not null) results.Add(noFloor);

        foreach (var junction in results) yield return junction;

        if (wallZone is null) yield break;
        foreach (var other in OtherPanelJunctions(wallZone, wall, OfThisStorey(set, elevation.Uncovered()))) yield return other;
    }

    /// <summary>
    /// One curtain wall as the resolver walks it: a straight wall is one facet, an arc wall is the flat
    /// stretches between its vertical grid lines, in order (docs §4.7). Each facet is measured as the
    /// plane it is; what the facets share — the stretches of façade the junctions already account for,
    /// and the panels a 交接帶 reaching past one facet's end finds on the next — is kept here, on the
    /// one axis <see cref="CurtainWallObservation.FacetOffsetMm"/> lays them all out on.
    /// </summary>
    private sealed class Elevation
    {
        private readonly List<PanelBand> _covered = new();

        public Elevation(IReadOnlyList<CurtainWallObservation> facets)
        {
            Facets = facets;
        }

        public IReadOnlyList<CurtainWallObservation> Facets { get; }

        public CurtainWallObservation First => Facets[0];

        public double LengthMm => Facets[Facets.Count - 1].FacetOffsetMm + Facets[Facets.Count - 1].LengthMm;

        /// <summary>
        /// The 區劃 the wall as a whole belongs to: probed at the middle of its length, on whichever
        /// facet that falls — for a straight wall exactly <c>ZoneOf(wall, LengthMm / 2)</c>.
        /// </summary>
        public CurtainWallZoneObservation? ZoneAtMiddle(CurtainWallObservationSet set)
        {
            var middle = LengthMm / 2.0;
            var facet = Facets.LastOrDefault(f => f.FacetOffsetMm <= middle) ?? First;
            return ZoneOf(set, facet, middle - facet.FacetOffsetMm);
        }

        /// <summary>Records a stretch one junction accounts for; <paramref name="band"/> is in <paramref name="facet"/>'s own distances.</summary>
        public void Cover(CurtainWallObservation facet, PanelBand band) => _covered.Add(band.Shifted(facet.FacetOffsetMm));

        /// <summary>The panels of every facet whose middle falls in the band, in facet then panel order.</summary>
        public IReadOnlyList<string> PanelsIn(CurtainWallObservation facet, PanelBand band)
        {
            var shared = band.Shifted(facet.FacetOffsetMm);
            return Facets
                .SelectMany(f => f.Panels.Where(p => shared.Covers(p, f.FacetOffsetMm)))
                .Select(p => p.UniqueId)
                .ToList();
        }

        /// <summary>What no junction accounts for: the panels CW-O answers 第79條之4 for.</summary>
        public IReadOnlyList<CurtainPanelObservation> Uncovered() =>
            Facets
                .SelectMany(f => f.Panels.Where(p => !_covered.Any(b => b.Covers(p, f.FacetOffsetMm))))
                .ToList();
    }

    // --- CW-H：區劃牆與帷幕牆之水平交接（docs §4.2）--------------------------------------------

    /// <summary>
    /// The CW-H junctions of one 區劃牆 with the wall. A straight wall has at most one; on an arc wall
    /// every facet is asked (docs §4.7), and:
    /// <list type="bullet">
    /// <item>a crossing found on any facet beats an end that merely came near another — a 區劃牆 meeting
    /// the arc at facet 5 always stops within the search tolerance of facets 4 and 6 as well, and those
    /// near misses are not junctions;</item>
    /// <item>crossings found on two facets at the same place along the wall — the 區劃牆 meets the arc
    /// right at a vertical grid line — are one junction;</item>
    /// <item>crossings at different places are different junctions: a straight 區劃牆 can cut an arc
    /// twice (a round tower split along a diameter), and each crossing is reviewed. The first keeps the
    /// plain id, so a straight wall's id never changes; later ones are numbered from 2.</item>
    /// </list>
    /// </summary>
    private static IEnumerable<CurtainWallJunction> WallJunctions(
        CurtainWallObservationSet set,
        Elevation elevation,
        CompartmentWallObservation host,
        CurtainWallJunctionOptions options)
    {
        var baseId = JunctionId(CurtainWallJunctionKind.WallToCurtainWall, elevation.First.UniqueId, host.UniqueId);
        var places = new List<double>();
        var crossed = false;
        CurtainWallJunction? nearMiss = null;

        string? IdAt(double alongWallMm)
        {
            if (places.Any(p => Math.Abs(p - alongWallMm) <= SnapMm)) return null;
            places.Add(alongWallMm);
            return places.Count == 1 ? baseId : baseId + ":" + places.Count.ToString(CultureInfo.InvariantCulture);
        }

        var found = new List<CurtainWallJunction>();
        foreach (var facet in elevation.Facets)
        {
            // 這片帷幕牆立面內的實體外牆，先投影到它自己的軸上：CW-H 的但書長度由這些牆供給（決議 13）。
            var attempt = WallJunction(set, elevation, facet, host, options, FacadeSegments(set, facet), baseId, IdAt);
            crossed |= attempt.Crossed;
            if (attempt.Junction is null) continue;
            if (attempt.Junction.Doubt?.Kind == CurtainWallJunctionDoubtKind.UnresolvedIntersection)
                nearMiss ??= attempt.Junction;
            else
                found.Add(attempt.Junction);
        }

        // 某一段真的有交點（即使它因為不屬任何區劃、或交接處歸別片帷幕牆而沒有產出）時，鄰段的「端點
        // 靠近」就只是同一個交點的影子，不是一筆人工覆核。
        if (found.Count == 0 && !crossed && nearMiss is not null) found.Add(nearMiss);
        return found;
    }

    /// <summary>What asking one facet came to: the junction it produced, if any, and whether the 區劃牆 really crosses it.</summary>
    private readonly struct WallAttempt
    {
        public static readonly WallAttempt None = new(null, false);

        public WallAttempt(CurtainWallJunction? junction, bool crossed)
        {
            Junction = junction;
            Crossed = crossed;
        }

        public CurtainWallJunction? Junction { get; }
        public bool Crossed { get; }
    }

    private static WallAttempt WallJunction(
        CurtainWallObservationSet set,
        Elevation elevation,
        CurtainWallObservation wall,
        CompartmentWallObservation host,
        CurtainWallJunctionOptions options,
        IReadOnlyList<FacadeSegment> facades,
        string nearMissId,
        Func<double, string?> idAt)
    {
        // 躺在立面內的區劃牆不產出 CW-H（決議 14）。它不是「與帷幕牆的交接處」，它**就是**那一段
        // 外牆；它的交接處在自己的兩端，由垂直於立面的區劃牆各自產生。少了這一條，凡是與帷幕牆共面
        // 相接的實體外牆都會因兩線平行求不到交點，掉進「最近端點」的退路，回報一句「端點距帷幕牆
        // 0 mm，超過搜尋公差」的自相矛盾訊息。它仍以實體外牆身分供給別人的但書長度。
        if (wall.IsInFacadePlane(host.Start, host.End)) return WallAttempt.None;

        // 交接帶的高程是區劃牆與帷幕牆高程的**交集**（決議 14，docs §4.2 步驟 2）。交集之外那一段
        // 外牆面（本文件所據模型是樓板邊緣的 450 mm）是第 79 條之 3 的層間帶，由 CW-V 回答；拿區劃牆
        // 全高當門檻只會要求實體外牆往下長進樓板，那是工具逼建模配合工具。
        var bandBottom = Math.Max(host.BottomElevationMm, wall.BaseElevationMm);
        var bandTop = Math.Min(host.TopElevationMm, wall.TopElevationMm);
        if (bandTop - bandBottom <= CurtainPanelObservation.TouchToleranceMm) return WallAttempt.None;

        var crossing = FindCrossing(wall, host, options.JunctionSearchToleranceMm, facades);
        if (crossing is null) return WallAttempt.None;
        var crossed = crossing.Value.IsResolved;

        // 交點落在定位線之外時，連續段兩端的帷幕牆都求得到同一個交點，但一個交接處只能有一列。
        if (crossing.Value.IsOffSegment && !Owns(set, wall, facades, crossing.Value.Along)) return new WallAttempt(null, crossed);

        var at = crossing.Value.Along;
        var zone = ZoneOf(set, wall, at, lateral: true);
        if (zone is null) return new WallAttempt(null, crossed);

        if (!crossing.Value.IsResolved)
        {
            return new WallAttempt(CurtainWallJunction.Doubtful(
                nearMissId, CurtainWallJunctionKind.WallToCurtainWall, zone.ZoneId, wall.UniqueId,
                new CurtainWallJunctionDoubt(
                    CurtainWallJunctionDoubtKind.UnresolvedIntersection,
                    $"區劃牆（Id {host.UniqueId}）的端點距帷幕牆（Id {wall.UniqueId}）" +
                    $"{crossing.Value.GapMm.ToString("0.#", CultureInfo.InvariantCulture)} mm，" +
                    $"超過搜尋公差 {options.JunctionSearchToleranceMm.ToString("0.#", CultureInfo.InvariantCulture)} mm，" +
                    "無法確定交點位置，需人工覆核區劃牆是否確實交接於帷幕牆。",
                    new[] { host.UniqueId, wall.UniqueId }),
                hostUniqueId: host.UniqueId), false);
        }

        // 同一個地方已由前一段產出（交點正好在直向 grid line 上）：不重複出列，也不重複覆蓋。
        var id = idAt(wall.FacetOffsetMm + at);
        if (id is null) return new WallAttempt(null, true);

        // 交接帶：交點左右各 MinFireRatedRunMm，高程為區劃牆在該處的高程帶。它有兩個用途——CW-O
        // 要扣掉帶內的嵌板，標示層要把帶內的嵌板塗紅。帶長本身**不由這些嵌板供給**（決議 13）。
        var reach = options.MinFireRatedRunMm;
        var band = PanelBand.Horizontal(at, reach, bandBottom, bandTop);
        elevation.Cover(wall, band);
        var panels = elevation.PanelsIn(wall, band);

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
            return new WallAttempt(CurtainWallJunction.Doubtful(
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
                facadeWallUniqueIds: new[] { facade.Wall.UniqueId }), true);
        }

        var measurement = FacadeRun(nearby, at, bandBottom, bandTop, host.RequiredFireRatingMinutes, options);

        return new WallAttempt(CurtainWallJunction.WallJunction(
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
            measurement.WallUniqueIds), true);
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

    /// <summary>One stretch of a facet that runs along the floor, with what each of its samples measured.</summary>
    private sealed class SpandrelPart
    {
        public SpandrelPart(CurtainWallObservation facet, Span span, IReadOnlyList<(RunMeasurement Measurement, double At)> measurements)
        {
            Facet = facet;
            Span = span;
            Measurements = measurements;
        }

        public CurtainWallObservation Facet { get; }
        public Span Span { get; }
        public IReadOnlyList<(RunMeasurement Measurement, double At)> Measurements { get; }

        public bool ReachesEnd => Math.Abs(Span.High - Facet.LengthMm) <= SnapMm;
        public bool ReachesStart => Math.Abs(Span.Low) <= SnapMm;
    }

    private static IEnumerable<CurtainWallJunction> Spandrels(
        CurtainWallObservationSet set,
        Elevation elevation,
        CompartmentFloorObservation floor,
        CurtainWallJunctionOptions options)
    {
        if (!floor.HasOutline) yield break;

        var parts = new List<SpandrelPart>();
        foreach (var facet in elevation.Facets)
        {
            if (!facet.SpansElevation(floor.ElevationMm)) continue;

            var horizontal = facet.GridLines.Where(g => g.Direction == CurtainGridLineDirection.Horizontal).ToList();
            foreach (var span in InsideSpans(facet, floor, options.JunctionSearchToleranceMm))
            {
                var samples = Samples(span.Low, span.High, options.SamplingIntervalMm).ToList();
                if (samples.Count == 0) continue;

                var measurements = new List<(RunMeasurement Measurement, double At)>();
                foreach (var at in samples)
                {
                    var column = facet.Panels.Where(p => p.CoversAlong(at)).ToList();
                    measurements.Add((Measure(
                        Up(column),
                        floor.ElevationMm,
                        floor.RequiredFireRatingMinutes,
                        options.MinFireRatedRunMm,
                        horizontal,
                        facet.BaseElevationMm,
                        facet.TopElevationMm), at));
                }

                parts.Add(new SpandrelPart(facet, span, measurements));
            }
        }

        var index = 0;
        foreach (var run in Contiguous(parts))
        {
            var all = run.SelectMany(p => p.Measurements.Select(m => (Part: p, m.Measurement, m.At))).ToList();
            var worst = RunMeasurement.Worst(all.Select(m => m.Measurement).ToList());
            var picked = all.First(m => ReferenceEquals(m.Measurement, worst));
            var wall = picked.Part.Facet;
            var worstAt = picked.At;

            var zone = ZoneOf(set, wall, worstAt);
            if (zone is null) continue;

            var id = JunctionId(CurtainWallJunctionKind.FloorToCurtainWall, wall.UniqueId, floor.UniqueId,
                index.ToString(CultureInfo.InvariantCulture));
            index++;
            foreach (var part in run)
                elevation.Cover(part.Facet, PanelBand.Vertical(part.Span.Low, part.Span.High, floor.ElevationMm, options.MinFireRatedRunMm));

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
                SpandrelPlacement(run[0], run[run.Count - 1], floor, options));
        }
    }

    /// <summary>
    /// The spandrel stretches grouped into bands: on an arc wall a slab edge running past a vertical
    /// grid line is still one 層間帶, so a stretch reaching the end of its facet joins the one starting
    /// the next facet (docs §4.7). Stretches inside one facet are never joined — that keeps a straight
    /// wall exactly as it was.
    /// </summary>
    private static IEnumerable<IReadOnlyList<SpandrelPart>> Contiguous(IReadOnlyList<SpandrelPart> parts)
    {
        var run = new List<SpandrelPart>();
        foreach (var part in parts)
        {
            var last = run.Count == 0 ? null : run[run.Count - 1];
            if (last is not null &&
                !(part.Facet.IsFacet && last.Facet.IsFacet &&
                  part.Facet.FacetIndex == last.Facet.FacetIndex + 1 && last.ReachesEnd && part.ReachesStart))
            {
                yield return run;
                run = new List<SpandrelPart>();
            }

            run.Add(part);
        }

        if (run.Count > 0) yield return run;
    }

    /// <summary>
    /// The 層間帶 as §4.5 defines it — the floor edge, 900 mm above and below the slab — clipped to the
    /// curtain wall itself, which is what the review view draws red (docs §7.1). Null when the wall
    /// leaves no band at this floor, e.g. a slab at its very top.
    /// </summary>
    /// <remarks>
    /// A band over several facets of an arc wall is placed on the chord from its first point to its
    /// last: the review elevation looks along one direction, so that is the plane the band is drawn on
    /// (docs §4.7).
    /// </remarks>
    private static CurtainWallJunctionPlacement? SpandrelPlacement(
        SpandrelPart first,
        SpandrelPart last,
        CompartmentFloorObservation floor,
        CurtainWallJunctionOptions options)
    {
        var wall = first.Facet;
        var bottom = Math.Max(wall.BaseElevationMm, floor.ElevationMm - options.MinFireRatedRunMm);
        var top = Math.Min(wall.TopElevationMm, floor.ElevationMm + options.MinFireRatedRunMm);
        if (top - bottom <= SnapMm) return null;

        return CurtainWallJunctionPlacement.Band(first.Facet.PointAt(first.Span.Low), last.Facet.PointAt(last.Span.High), bottom, top);
    }

    /// <summary>
    /// A curtain wall that runs up through this storey's level with no 區劃樓地板 meeting it there
    /// (docs §3.4). Two answers, and only one of them says 不適用:
    /// <list type="bullet">
    /// <item>The 區劃 behind it is a 垂直區劃 by its 用途 — a 挑空, a 樓梯間: a 連跨複數樓層 space
    /// (第79條之3第3項), handed on to 第79條之2. That is positive evidence the clause is another one's.</item>
    /// <item>Anything else — a floor set back further than the search tolerance, no floor modelled, a
    /// roof drawn as a Roof, or a 挑空 whose 用途 nobody marked — is 人工覆核, with the distance to the
    /// nearest floor of this level when there is one. 不適用 would read as nothing wrong.</item>
    /// </list>
    /// <para>
    /// Only this storey's own level is asked about, because only this storey's floors are in the
    /// package: the floor of the level above belongs to the next package, so its absence here says
    /// nothing. Each level a curtain wall runs past without a floor is answered by the package of that
    /// level. A wall standing on this level (or on the one above, which the 900 mm read margin also
    /// hands over) does not run through it; the search tolerance absorbs a base offset laid a little
    /// below the slab to cover its edge.
    /// </para>
    /// </summary>
    private static CurtainWallJunction? NoFloorAtThisLevel(
        CurtainWallObservationSet set,
        Elevation elevation,
        CurtainWallZoneObservation? zone,
        CurtainWallJunctionOptions options,
        ISet<Guid> verticalCompartmentZoneIds)
    {
        var wall = elevation.First;
        var level = set.LevelElevationMm;
        var tolerance = options.JunctionSearchToleranceMm;
        if (wall.BaseElevationMm >= level - tolerance || wall.TopElevationMm <= level + tolerance) return null;

        var floors = set.CompartmentFloors.Where(f => IsFloorOfThisLevel(set, f, options) && f.HasOutline).ToList();
        if (floors.Any(f => elevation.Facets.Any(facet => InsideSpans(facet, f, tolerance).Any()))) return null;

        if (zone is null) return null;

        var storeys = set.LevelElevationsMm.Count(e => e > wall.BaseElevationMm + tolerance && e < wall.TopElevationMm - tolerance) + 1;
        var levelName = string.IsNullOrWhiteSpace(set.LevelName) ? "本層" : $"本層（{set.LevelName}）";
        var fact = $"帷幕牆（Id {wall.UniqueId}）連跨 {storeys} 個樓層且於{levelName}標高無區劃樓地板與其交接";
        var id = JunctionId(CurtainWallJunctionKind.FloorToCurtainWall, wall.UniqueId);

        // 不適用要有正面證據，而且要涵蓋整道牆：沿牆每一處後方的區劃都得是垂直區劃。一段面對挑空、
        // 一段面對沒建樓板的辦公室，後者不能跟著前者一起轉出去。
        var behind = Behind(set, elevation, options);
        var ordinary = behind.Where(z => !verticalCompartmentZoneIds.Contains(z.ZoneId)).ToList();
        if (behind.Count > 0 && ordinary.Count == 0)
        {
            return CurtainWallJunction.Doubtful(
                id, CurtainWallJunctionKind.FloorToCurtainWall, zone.ZoneId, wall.UniqueId,
                new CurtainWallJunctionDoubt(
                    CurtainWallJunctionDoubtKind.VerticalCompartmentSpace,
                    $"{fact}，其後之區劃「{string.Join("、", behind.Select(z => z.Name))}」為垂直區劃，" +
                    $"屬無法逐層區劃分隔之垂直空間，改依{CurtainWallJunctionReferences.Article79_2}檢討，本項不適用。",
                    new[] { wall.UniqueId }));
        }

        var (why, nearest) = WhyNoFloor(set, elevation, options);
        var unmarked = ordinary.Count > 0 ? ordinary : new List<CurtainWallZoneObservation> { zone };
        return CurtainWallJunction.Doubtful(
            id, CurtainWallJunctionKind.FloorToCurtainWall, zone.ZoneId, wall.UniqueId,
            new CurtainWallJunctionDoubt(
                CurtainWallJunctionDoubtKind.FloorNotMeetingCurtainWall,
                $"{fact}：{why}。無法確認層間交接是否符合{CurtainWallJunctionReferences.Article79_3}，需人工覆核；" +
                $"若此處為挑空等垂直區劃，請將區劃「{string.Join("、", unmarked.Select(z => z.Name))}」的用途標示為垂直區劃後重新檢討。",
                nearest is null ? new[] { wall.UniqueId } : new[] { wall.UniqueId, nearest }),
            hostUniqueId: nearest);
    }

    /// <summary>Every 區劃 found behind the wall, probed along it at the sampling interval.</summary>
    private static IReadOnlyList<CurtainWallZoneObservation> Behind(
        CurtainWallObservationSet set,
        Elevation elevation,
        CurtainWallJunctionOptions options) =>
        elevation.Facets
            .SelectMany(facet => Samples(0, facet.LengthMm, options.SamplingIntervalMm).Select(at => ZoneOf(set, facet, at)))
            .Where(z => z is not null)
            .Select(z => z!)
            .GroupBy(z => z.ZoneId)
            .Select(g => g.First())
            .OrderBy(z => z.ZoneId)
            .ToList();

    /// <summary>
    /// What the user is told about the floors of this storey, and the floor named as the junction's
    /// host: a floor of this level standing back further than the tolerance, failing that one whose
    /// elevation is too far off the level, failing that a floor that does not run along the wall at
    /// all, and failing everything the plain fact that there is none.
    /// </summary>
    private static (string Why, string? Floor) WhyNoFloor(
        CurtainWallObservationSet set,
        Elevation elevation,
        CurtainWallJunctionOptions options)
    {
        var tol = options.JunctionSearchToleranceMm.ToString("0.#", CultureInfo.InvariantCulture);
        var outlined = set.CompartmentFloors.Where(f => f.HasOutline).ToList();
        var level = outlined.Where(f => IsFloorOfThisLevel(set, f, options)).ToList();

        var setBack = level
            .Select(f => (Floor: f, Depth: elevation.Facets.Select(facet => SetBackOf(facet, f)).Where(d => d is not null).Select(d => d!.Value).DefaultIfEmpty(double.NaN).Min()))
            .Where(x => !double.IsNaN(x.Depth) && x.Depth > options.JunctionSearchToleranceMm)
            .OrderBy(x => x.Depth)
            .FirstOrDefault();
        if (setBack.Floor is not null)
            return ($"本層的區劃樓地板（Id {setBack.Floor.UniqueId}）邊緣退在帷幕牆定位線內側 " +
                    $"{setBack.Depth.ToString("0", CultureInfo.InvariantCulture)} mm，超過搜尋公差 {tol} mm", setBack.Floor.UniqueId);

        var offLevel = outlined
            .Where(f => !IsFloorOfThisLevel(set, f, options))
            .OrderBy(f => Math.Abs(f.ElevationMm - set.LevelElevationMm))
            .FirstOrDefault();
        if (offLevel is not null)
            return ($"本層有區劃樓地板（Id {offLevel.UniqueId}），但其高程與本層標高相差 " +
                    $"{Math.Abs(offLevel.ElevationMm - set.LevelElevationMm).ToString("0", CultureInfo.InvariantCulture)} mm，" +
                    $"超過公差 {tol} mm", offLevel.UniqueId);

        if (level.Count > 0)
            return ($"本層的區劃樓地板（Id {level[0].UniqueId}）未沿帷幕牆延伸（僅觸及其端點或位於其外側）", level[0].UniqueId);

        return ("本層沒有任何區劃樓地板（未建模、以屋頂建模，或樓板不在本層）", null);
    }

    /// <summary>
    /// How far behind this facet's location line the floor's edge stands, measured only on the 區劃
    /// side and only along the facet's own length — a floor that merely touches the wall's end or lies
    /// outside it has no setback, and null says so. Exact: each outline edge is clipped to that strip
    /// and the depth is linear along it, so the least is at a clipped end.
    /// </summary>
    private static double? SetBackOf(CurtainWallObservation wall, CompartmentFloorObservation floor)
    {
        double? least = null;
        foreach (var loop in floor.OutlineLoops)
        {
            for (int i = 0, j = loop.Count - 1; i < loop.Count; j = i++)
            {
                var (u0, d0) = Local(wall, loop[j]);
                var (u1, d1) = Local(wall, loop[i]);
                var low = 0.0;
                var high = 1.0;
                if (!Clip(u0, u1, SnapMm, wall.LengthMm - SnapMm, ref low, ref high)) continue;
                if (!Clip(d0, d1, 0.0, double.PositiveInfinity, ref low, ref high)) continue;

                var depth = Math.Min(d0 + ((d1 - d0) * low), d0 + ((d1 - d0) * high));
                if (least is null || depth < least) least = depth;
            }
        }

        return least;
    }

    /// <summary>A plan point as (distance along the wall, distance behind its location line).</summary>
    private static (double Along, double Behind) Local(CurtainWallObservation wall, Point2D point)
    {
        var dx = point.X - wall.Start.X;
        var dy = point.Y - wall.Start.Y;
        return ((dx * wall.Direction.X) + (dy * wall.Direction.Y),
                -((dx * wall.ExteriorNormal.X) + (dy * wall.ExteriorNormal.Y)));
    }

    /// <summary>Narrows [low, high] of a segment's parameter to where a linear value v0 → v1 lies in [min, max].</summary>
    private static bool Clip(double v0, double v1, double min, double max, ref double low, ref double high)
    {
        var dv = v1 - v0;
        if (Math.Abs(dv) <= ParallelEpsilon)
            return v0 >= min && v0 <= max && low <= high;

        var tMin = (min - v0) / dv;
        var tMax = double.IsPositiveInfinity(max) ? (dv > 0 ? double.PositiveInfinity : double.NegativeInfinity) : (max - v0) / dv;
        if (tMin > tMax) (tMin, tMax) = (tMax, tMin);
        low = Math.Max(low, tMin);
        high = Math.Min(high, tMax);
        return low <= high;
    }

    // --- CW-O：其餘嵌板（第79條之4）-------------------------------------------------------------

    /// <summary>
    /// The panels this storey answers 第79條之4 for. The read reaches 900 mm past the storey on either
    /// side so a band is never cut short (CW-H, CW-V), which hands over the top row of the curtain wall
    /// below and the bottom row of the one above as well; each of those is the other storey's to
    /// answer. A panel belongs to the storey its middle is in — from this level up to, not including,
    /// the next — so every panel is answered by exactly one package.
    /// </summary>
    private static IEnumerable<CurtainPanelObservation> OfThisStorey(
        CurtainWallObservationSet set,
        IEnumerable<CurtainPanelObservation> panels)
    {
        var next = NextLevelAbove(set);
        return panels.Where(p =>
        {
            var middle = (p.BottomMm + p.TopMm) / 2.0;
            return middle >= set.LevelElevationMm - SnapMm && (next is not double above || middle < above - SnapMm);
        });
    }

    /// <summary>
    /// 其餘嵌板依作答方式分成最多三列（決議 16）：實心讀設計防火時效，玻璃與帷幕牆門窗讀設計防火保護，
    /// 沒宣告種類的自成一列讓引擎判資料不足。不取一個最不利值合成一列，是因為分鐘數與是非題湊不成
    /// 同一個門檻；三列同為 <see cref="CurtainWallJunctionKind.CurtainPanelOther"/>，所以 §7.2 的檢討表
    /// 仍是三列。
    /// </summary>
    private static IEnumerable<CurtainWallJunction> OtherPanelJunctions(
        CurtainWallZoneObservation zone,
        CurtainWallObservation wall,
        IEnumerable<CurtainPanelObservation> panels)
    {
        var rest = panels.ToList();
        if (rest.Count == 0) yield break;

        var solid = rest.Where(p => p.AnswersByRating).ToList();
        if (solid.Count > 0)
            yield return CurtainWallJunction.OtherPanels(
                JunctionId(CurtainWallJunctionKind.CurtainPanelOther, wall.UniqueId, SolidSuffix),
                zone.ZoneId,
                wall.UniqueId,
                Worst(solid.Select(p => p.Rating))!,
                solid.Select(p => p.UniqueId));

        var glazed = rest.Where(p => p.AnswersByProtection).ToList();
        if (glazed.Count > 0)
            yield return CurtainWallJunction.OtherGlazedPanels(
                JunctionId(CurtainWallJunctionKind.CurtainPanelOther, wall.UniqueId, GlazedSuffix),
                zone.ZoneId,
                wall.UniqueId,
                Worst(glazed.Select(p => p.Protection)),
                glazed.Select(p => p.UniqueId));

        var undeclared = rest.Where(p => p.Kind is null).ToList();
        if (undeclared.Count > 0)
            yield return CurtainWallJunction.OtherUndeclaredPanels(
                JunctionId(CurtainWallJunctionKind.CurtainPanelOther, wall.UniqueId, UndeclaredSuffix),
                zone.ZoneId,
                wall.UniqueId,
                undeclared.Select(p => p.UniqueId));
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
        var covering = Enumerable.Range(0, slabs.Count)
            .Where(i => at >= slabs[i].Low - CurtainPanelObservation.TouchToleranceMm &&
                        at <= slabs[i].High + CurtainPanelObservation.TouchToleranceMm)
            .ToList();
        if (covering.Count == 0) return RunMeasurement.Nothing;

        // A horizontal grid line right at the slab puts two panels on it. The band may start on either
        // side, so a qualifying one is taken over one that is not — a 玻璃 window below a solid
        // spandrel above is measured from the spandrel, not reported as 0. When both qualify the lower
        // is kept and the walk asks §4.5's question about the line between them.
        var index = covering.Where(i => slabs[i].Panel.Qualifies(requiredMinutes)).DefaultIfEmpty(covering[0]).First();
        var here = slabs[index];

        // Nobody said what the host requires, so there is no bar for a panel to clear and no run to
        // report. Supplying 0 here would read as 未符合 for a band nobody ever measured. A glazed
        // panel's blank rating is not reported either: it is not what the user has to fill in.
        if (requiredMinutes is null)
            return new RunMeasurement(null, here.Panel.IsGlazed ? null : here.Panel.Rating, here.Panel.IsUnprotectedOpening,
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

        // A declared 玻璃嵌板 has no construction rating by design (決議 16): its blank rating is not a
        // gap the user could fill but a panel that never counts, so it is left out of the reading.
        var worst = Worst(looked.Where(p => !p.IsGlazed).Select(p => p.Rating));
        var panelIds = looked.Select(p => p.UniqueId).ToList();
        var opening = looked.Any(p => p.IsUnprotectedOpening);

        // 輸入契約：any panel the measurement looked at without a readable design rating means no run
        // is supplied at all — never 0, which would read as 未符合 instead of 資料不足 (docs §12).
        // Null here means every panel looked at was glass: a run that is known, not unknown.
        if (worst is not null && !worst.IsRated)
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
            // and the panel is now in `looked`, so no run will be supplied either way. A glazed
            // neighbour stops it as a real break: Measure leaves it out of the reading.
            if (neighbour.Panel.IsGlazed || !neighbour.Panel.Rating.IsRated) break;
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
    /// 玻璃那一路的讀值，順序與 <see cref="Worst(IEnumerable{ProvidedFireRating})"/> 同一套理由：
    /// 未設定最先（那是使用者要補的），再是讀不出來的值，然後才是「否」——一片沒填、一片填否時，
    /// 先講沒填的那一片，因為補了它答案才可能改變。整組都沒讀到值時回 <c>Missing</c>，不回 null：
    /// CW-O 一律供值，未綁定是資料不足而不是通過。
    /// </summary>
    private static ProvidedFireProtection Worst(IEnumerable<ProvidedFireProtection?> protections) =>
        protections
            .Select(p => p ?? ProvidedFireProtection.Missing("讀取層未提供防火保護讀值"))
            .OrderBy(p => p.Kind switch
            {
                ProvidedFireProtectionKind.Missing => 0,
                ProvidedFireProtectionKind.Unreadable => 1,
                ProvidedFireProtectionKind.No => 2,
                _ => 3
            })
            .First();

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

    /// <summary>
    /// The stretches of the curtain wall that run along the floor, as distances along it.
    /// <para>
    /// A slab rarely reaches the location line itself: it usually stops a little behind the curtain
    /// wall, with a gap the firestop fills. So the floor is asked twice — at the location line and
    /// <paramref name="toleranceMm"/> behind it, towards the 區劃 — and a stretch the floor covers at
    /// either counts. That is the same search tolerance CW-H extends a 區劃牆 by; a slab stopping
    /// further back than that does not meet this curtain wall. How far the slab reaches is
    /// <see cref="Projection(CurtainWallObservation, CompartmentFloorObservation, double)"/>'s
    /// question, which answers 0 for one stopping behind the face.
    /// </para>
    /// </summary>
    private static IEnumerable<Span> InsideSpans(CurtainWallObservation wall, CompartmentFloorObservation floor, double toleranceMm)
    {
        var behind = new Point2D(-wall.ExteriorNormal.X * toleranceMm, -wall.ExteriorNormal.Y * toleranceMm);
        var cuts = new List<double> { 0.0, wall.LengthMm };
        foreach (var loop in floor.OutlineLoops)
        {
            for (int i = 0, j = loop.Count - 1; i < loop.Count; j = i++)
            {
                if (SegmentCut(wall, loop[j], loop[i], wall.Start) is double u) cuts.Add(u);
                if (SegmentCut(wall, loop[j], loop[i], Shifted(wall.Start, behind.X, behind.Y)) is double v) cuts.Add(v);
            }
        }

        var ordered = cuts.Where(u => u >= 0 && u <= wall.LengthMm).Distinct().OrderBy(u => u).ToList();
        for (var i = 0; i + 1 < ordered.Count; i++)
        {
            var low = ordered[i];
            var high = ordered[i + 1];
            if (high - low <= SnapMm) continue;

            var middle = wall.PointAt((low + high) / 2.0);
            if (floor.Contains(middle) || floor.Contains(Shifted(middle, behind.X, behind.Y)))
                yield return new Span(low, high);
        }
    }

    private static Point2D Shifted(Point2D point, double dx, double dy) => new(point.X + dx, point.Y + dy);

    /// <summary>Where segment <paramref name="a"/>–<paramref name="b"/> crosses the wall's location line moved to start at <paramref name="start"/>.</summary>
    private static double? SegmentCut(CurtainWallObservation wall, Point2D a, Point2D b, Point2D start)
    {
        var rx = wall.End.X - wall.Start.X;
        var ry = wall.End.Y - wall.Start.Y;
        var sx = b.X - a.X;
        var sy = b.Y - a.Y;
        var denominator = (rx * sy) - (ry * sx);
        if (Math.Abs(denominator) <= ParallelEpsilon) return null;

        var qx = a.X - start.X;
        var qy = a.Y - start.Y;
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
    /// <summary>
    /// The wall with its 外側法線 pointing away from the 區劃 it belongs to (docs §4.6, 決議 15).
    /// <para>
    /// Three stations along the location line — a quarter, half and three quarters of the way — are
    /// probed to <see cref="ZoneProbeMm"/> past the outside face on each side, and the normal is
    /// reversed only when the <c>+n</c> side finds a 區劃 at <b>strictly more</b> stations than the
    /// <c>−n</c> side. A tie keeps the normal as read: both sides answering means the wall stands
    /// inside the building or the probe reached a neighbouring 區劃, and neither side answering means
    /// the wall belongs to no 區劃 at all (docs §9) — reversing on either would be a coin toss.
    /// </para>
    /// <para>
    /// Three stations rather than the midpoint alone, because a single one is led astray by a doorway,
    /// a gap where no 區劃 was modelled, or an elevation that spans two of them; a majority rather
    /// than "any station", because the two ends of an elevation can touch different 區劃.
    /// </para>
    /// </summary>
    private static CurtainWallObservation Oriented(CurtainWallObservationSet set, CurtainWallObservation wall)
    {
        var samples = Probe(set, wall);
        var inward = samples.Count(s => s.NegativeSideZoneId is not null);
        var outward = samples.Count(s => s.PositiveSideZoneId is not null);

        return outward > inward ? wall.WithReversedExteriorNormal() : wall;
    }

    /// <summary>
    /// The 區劃 on each side of one facet, at the same three stations 外側法線定向 uses. One probe for
    /// both questions — which way is out, and whether this is an 外牆 at all — so the two can never
    /// disagree about what was found where (docs §4.6, §4.8).
    /// </summary>
    private static IReadOnlyList<CurtainWallExposureSample> Probe(
        CurtainWallObservationSet set,
        CurtainWallObservation wall)
    {
        var depth = wall.ExteriorOffsetMm + ZoneProbeMm;
        var samples = new List<CurtainWallExposureSample>();

        foreach (var fraction in new[] { 0.25, 0.5, 0.75 })
        {
            var p = wall.PointAt(wall.LengthMm * fraction);
            var negative = set.ZoneAt(new Point2D(p.X - (wall.ExteriorNormal.X * depth), p.Y - (wall.ExteriorNormal.Y * depth)));
            var positive = set.ZoneAt(new Point2D(p.X + (wall.ExteriorNormal.X * depth), p.Y + (wall.ExteriorNormal.Y * depth)));
            samples.Add(new CurtainWallExposureSample(negative?.ZoneId, positive?.ZoneId));
        }

        return samples;
    }

    /// <summary>Whether one facet is 外牆 or 室內, from its own probe and its Wall Type's Function (docs §4.8).</summary>
    private static CurtainWallExposureVerdict ExposureOf(CurtainWallObservationSet set, CurtainWallObservation wall) =>
        CurtainWallExposureClassifier.Classify(Probe(set, wall), wall.FunctionDeclaration);

    /// <summary>
    /// What a curtain wall that is <b>not</b> the building's 外牆 still owes this review (docs §4.8).
    /// </summary>
    /// <remarks>
    /// <para>
    /// No CW-H, no CW-V, no CW-O: 第79條第3、4項、第79條之3第2項 and 第79條之4 all speak of 外牆, and an
    /// interior curtain wall is not one. Its openings are reviewed instead as any 區劃 boundary's are —
    /// 第79條第1項之防火門窗 — which <see cref="CandidateResolver"/> routes and
    /// <c>OpeningProtectionCheck</c> answers.
    /// </para>
    /// <para>
    /// Two things survive the handover, because neither of them is about 外牆:
    /// </para>
    /// <list type="bullet">
    /// <item>第79條之2's 連跨複數樓層 question. A curtain wall running past this storey with no
    /// 區劃樓地板 meeting it is a 垂直空間 whether it stands inside the building or on its face, and a
    /// 管道間 or 挑空 enclosed in glass is exactly the case this must not stop asking about.</item>
    /// <item>An <see cref="CurtainWallExposure.Unknown"/> verdict, which is a 人工覆核 of its own: the
    /// tool will not route half a wall on a guess, and it will not silently drop it either.</item>
    /// </list>
    /// <para>
    /// A wall no probe found any 區劃 beside, on either side, still produces nothing — a junction has
    /// to be filed under a 區劃 and there is none. That is the pre-existing 「不屬於任何區劃的帷幕牆」
    /// limitation of docs §9, narrowed here to walls with no 區劃 anywhere along them rather than every
    /// wall the tool could not classify.
    /// </para>
    /// </remarks>
    private static IEnumerable<CurtainWallJunction> NonExteriorJunctions(
        CurtainWallObservationSet set,
        Elevation elevation,
        CurtainWallZoneObservation? wallZone,
        CurtainWallJunctionOptions options,
        ISet<Guid> verticalCompartmentZoneIds,
        CurtainWallExposureVerdict exposure)
    {
        var wall = elevation.First;

        // 連跨只需要定位線的高程與沿牆的區劃，不需要平面可量測，所以非平面牆也照問。
        var noFloor = NoFloorAtThisLevel(set, elevation, wallZone, options, verticalCompartmentZoneIds);
        if (noFloor is not null) yield return noFloor;

        // 整道牆沿線找到的任何一個區劃都算：少數測站找到的也算，否則「各處答案不一致」這一種
        // 就正好沒有區劃可歸屬，而那是最需要被看到的一種。
        var zoneId = wallZone?.ZoneId ?? exposure.ZoneIds.FirstOrDefault();
        if (zoneId == Guid.Empty) yield break;

        var panels = OfThisStorey(set, elevation.Facets.SelectMany(f => f.Panels)).Select(p => p.UniqueId).ToList();

        if (exposure.IsUnknown)
        {
            var remedy = exposure.Remedy();
            yield return CurtainWallJunction.Doubtful(
                JunctionId(CurtainWallJunctionKind.WallToCurtainWall, wall.UniqueId) + ":exposure",
                CurtainWallJunctionKind.WallToCurtainWall,
                zoneId,
                wall.UniqueId,
                new CurtainWallJunctionDoubt(
                    CurtainWallJunctionDoubtKind.ExposureUndecided,
                    $"無法判定帷幕牆（Id {wall.UniqueId}）是建築物外牆或室內帷幕牆：{exposure.Describe()}。" +
                    $"第79條第3、4項、{CurtainWallJunctionReferences.Article79_3}與{CurtainWallJunctionReferences.Article79_4}" +
                    "問的都是外牆，室內外未定前這三項皆無法判定，需人工覆核" +
                    (remedy is null ? "。" : $"；{remedy}。"),
                    new[] { wall.UniqueId }),
                panelUniqueIds: panels);

            yield break;
        }

        // 室內、但量不出平面：弧形以外的曲面、傾斜面，以及沒有定位面的 Curtain System。開口那一條路
        // 只收得到 host 是一道 Wall 的帷幕嵌板，所以 Curtain System 的嵌板兩條路都走不到——這一列就是
        // 為了不讓它靜默消失。決議 18 之前它由非平面那一支答兩列（CW-H、CW-V），現在只需要一列：
        // 外牆的那兩項對室內牆本來就不適用，要說的是「這道牆的嵌板沒有被接手」。
        if (!wall.IsPlanar)
        {
            yield return CurtainWallJunction.Doubtful(
                JunctionId(CurtainWallJunctionKind.WallToCurtainWall, wall.UniqueId) + ":interior",
                CurtainWallJunctionKind.WallToCurtainWall,
                zoneId,
                wall.UniqueId,
                new CurtainWallJunctionDoubt(
                    CurtainWallJunctionDoubtKind.NonPlanarCurtainWall,
                    $"帷幕牆（Id {wall.UniqueId}）判定為室內帷幕牆（{exposure.Describe()}），" +
                    $"外牆之交接處規定不適用；但它{wall.NonPlanarReason}，" +
                    "工具無法確認其嵌板是否已由區劃邊緣的防火門窗檢討涵蓋，需人工覆核。",
                    new[] { wall.UniqueId }),
                panelUniqueIds: panels);
        }
    }

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

        /// <summary>The same band moved <paramref name="offsetMm"/> along the wall: a facet's own distances onto the wall's (docs §4.7).</summary>
        public PanelBand Shifted(double offsetMm) => new(StartMm + offsetMm, EndMm + offsetMm, BottomMm, TopMm);

        /// <summary>
        /// A panel belongs to the band when its middle is in it, not merely when it grazes it: a
        /// storey-high glazed panel that reaches into a spandrel band is still 其他部分外牆, and
        /// 第79條之4 has to be answered for it (docs §3.3).
        /// </summary>
        public bool Covers(CurtainPanelObservation panel) => Covers(panel, 0.0);

        /// <summary><see cref="Covers(CurtainPanelObservation)"/> for a panel of a facet starting <paramref name="offsetMm"/> along the wall.</summary>
        public bool Covers(CurtainPanelObservation panel, double offsetMm)
        {
            var along = offsetMm + ((panel.StartMm + panel.EndMm) / 2.0);
            var elevation = (panel.BottomMm + panel.TopMm) / 2.0;
            return along >= StartMm && along <= EndMm && elevation >= BottomMm && elevation <= TopMm;
        }
    }

    /// <summary>
    /// Whether a 區劃樓地板 is this storey's: within the search tolerance of its level, not exactly on
    /// it. Every floor in the package is already one hosted on this level; a slab offset −50 mm for the
    /// finish, or whose top is the structural level, is still this storey's floor, and losing it would
    /// lose the CW-V row and hand the curtain wall to 人工覆核 for want of a floor.
    /// </summary>
    private static bool IsFloorOfThisLevel(CurtainWallObservationSet set, CompartmentFloorObservation floor, CurtainWallJunctionOptions options) =>
        Math.Abs(floor.ElevationMm - set.LevelElevationMm) <= options.JunctionSearchToleranceMm;

    /// <summary>The next level up from this storey's, or null for the top storey.</summary>
    private static double? NextLevelAbove(CurtainWallObservationSet set) =>
        set.LevelElevationsMm.Where(e => e > set.LevelElevationMm + SnapMm).Cast<double?>().FirstOrDefault();

    /// <summary>
    /// Whether a curtain wall stands in this storey by more than the search tolerance, rather than
    /// being the storey below's or above's that the 900 mm read margin handed over as well.
    /// </summary>
    private static bool StandsInThisStorey(CurtainWallObservationSet set, CurtainWallObservation wall, double toleranceMm) =>
        wall.TopElevationMm > set.LevelElevationMm + toleranceMm &&
        (NextLevelAbove(set) is not double above || wall.BaseElevationMm < above - toleranceMm);


    /// <summary>CW-O 三路的 <c>junction.id</c> 尾碼（決議 16）；同一片帷幕牆最多三列，彼此不會撞號。</summary>
    private const string SolidSuffix = "solid";

    private const string GlazedSuffix = "glazed";

    private const string UndeclaredSuffix = "undeclared";

    private static string JunctionId(CurtainWallJunctionKind kind, params string[] parts) =>
        string.Join(":", new[] { Prefix(kind) }.Concat(parts));

    private static string Prefix(CurtainWallJunctionKind kind) => kind switch
    {
        CurtainWallJunctionKind.WallToCurtainWall => "CW-H",
        CurtainWallJunctionKind.FloorToCurtainWall => "CW-V",
        _ => "CW-O"
    };
}
