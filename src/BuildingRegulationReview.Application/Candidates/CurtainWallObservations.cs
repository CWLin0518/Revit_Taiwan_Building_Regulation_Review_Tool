using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using BuildingRegulationReview.Application.Checks;
using BuildingRegulationReview.Domain.Geometry;

namespace BuildingRegulationReview.Application.Candidates;

/// <summary>
/// Which way a curtain grid line runs on the wall's own plane. A vertical one sits at a distance
/// along the wall and cuts a horizontal run in two (CW-H); a horizontal one sits at an elevation and
/// cuts a vertical run (CW-V). The tool never accumulates across one
/// (docs/regulations/curtain-wall-fire-compartment.md §4.1).
/// </summary>
public enum CurtainGridLineDirection
{
    Vertical,
    Horizontal
}

/// <summary>One grid line, at its position on the curtain wall's plane. Millimetres, like everything here.</summary>
public sealed class CurtainGridLineObservation
{
    public CurtainGridLineObservation(string uniqueId, CurtainGridLineDirection direction, double positionMm)
    {
        if (string.IsNullOrWhiteSpace(uniqueId)) throw new ArgumentException("Grid line UniqueId is required.", nameof(uniqueId));
        if (!Enum.IsDefined(typeof(CurtainGridLineDirection), direction)) throw new ArgumentOutOfRangeException(nameof(direction));
        if (double.IsNaN(positionMm) || double.IsInfinity(positionMm))
            throw new ArgumentOutOfRangeException(nameof(positionMm), "A position must be a finite number.");

        UniqueId = uniqueId.Trim();
        Direction = direction;
        PositionMm = positionMm;
    }

    public string UniqueId { get; }
    public CurtainGridLineDirection Direction { get; }

    /// <summary>Distance along the wall for a vertical line; elevation for a horizontal one.</summary>
    public double PositionMm { get; }

    public override string ToString() =>
        $"{(Direction == CurtainGridLineDirection.Vertical ? "垂直" : "水平")} grid line {UniqueId} @ {PositionMm:0.#} mm";
}

/// <summary>
/// One curtain panel, placed on the curtain wall's own (u, z) plane: <c>u</c> is the distance along
/// the wall's location line from its start, <c>z</c> is the elevation. Both in millimetres — the
/// Revit boundary converts feet before any of this is built (docs §4.4).
/// </summary>
/// <remarks>
/// 豎框 are not observed at all: no clause gives a mullion its own rating, so the tool neither reads
/// nor judges one (docs §4.1). A panel that is an opening — a curtain wall door or window, or an
/// openable panel — carries 防火檢討_設計防火保護 as well, because an unprotected opening stops a
/// continuous run even when the panels around it are rated (docs §3.1).
/// </remarks>
public sealed class CurtainPanelObservation
{
    /// <summary>Panels this close are touching; a wider gap is a hole in the façade.</summary>
    public const double TouchToleranceMm = 0.5;

    public CurtainPanelObservation(
        string uniqueId,
        double startMm,
        double endMm,
        double bottomMm,
        double topMm,
        ProvidedFireRating rating,
        bool isOpening = false,
        ProvidedFireProtection? protection = null,
        string? typeUniqueId = null,
        string? typeName = null)
    {
        if (string.IsNullOrWhiteSpace(uniqueId)) throw new ArgumentException("Panel UniqueId is required.", nameof(uniqueId));
        Finite(startMm, nameof(startMm));
        Finite(endMm, nameof(endMm));
        Finite(bottomMm, nameof(bottomMm));
        Finite(topMm, nameof(topMm));
        if (endMm - startMm <= TouchToleranceMm)
            throw new ArgumentOutOfRangeException(nameof(endMm), "A panel has to be wider than the touch tolerance.");
        if (topMm - bottomMm <= TouchToleranceMm)
            throw new ArgumentOutOfRangeException(nameof(topMm), "A panel has to be taller than the touch tolerance.");

        UniqueId = uniqueId.Trim();
        StartMm = startMm;
        EndMm = endMm;
        BottomMm = bottomMm;
        TopMm = topMm;
        Rating = rating ?? throw new ArgumentNullException(nameof(rating));
        IsOpening = isOpening;
        Protection = protection;
        TypeUniqueId = string.IsNullOrWhiteSpace(typeUniqueId) ? null : typeUniqueId!.Trim();
        TypeName = string.IsNullOrWhiteSpace(typeName) ? null : typeName!.Trim();
    }

    public string UniqueId { get; }

    /// <summary>Where the panel starts along the wall.</summary>
    public double StartMm { get; }

    /// <summary>Where it ends along the wall.</summary>
    public double EndMm { get; }

    public double BottomMm { get; }
    public double TopMm { get; }

    /// <summary>防火檢討_設計防火時效 of the panel's Type, exactly as it was read.</summary>
    public ProvidedFireRating Rating { get; }

    /// <summary>True for a curtain wall door／window or an openable panel.</summary>
    public bool IsOpening { get; }

    /// <summary>防火檢討_設計防火保護, for an opening; null when the panel is not one.</summary>
    public ProvidedFireProtection? Protection { get; }

    public string? TypeUniqueId { get; }
    public string? TypeName { get; }

    public double WidthMm => EndMm - StartMm;
    public double HeightMm => TopMm - BottomMm;

    /// <summary>An opening not declared a 防火門窗: a continuous run stops here (docs §3.1).</summary>
    public bool IsUnprotectedOpening => IsOpening && Protection?.Kind != ProvidedFireProtectionKind.Yes;

    public bool CoversAlong(double millimeters) =>
        millimeters >= StartMm - TouchToleranceMm && millimeters <= EndMm + TouchToleranceMm;

    public bool CoversElevation(double millimeters) =>
        millimeters >= BottomMm - TouchToleranceMm && millimeters <= TopMm + TouchToleranceMm;

    public bool OverlapsElevations(double bottomMm, double topMm) =>
        topMm > BottomMm + TouchToleranceMm && bottomMm < TopMm - TouchToleranceMm;

    public bool OverlapsAlong(double startMm, double endMm) =>
        endMm > StartMm + TouchToleranceMm && startMm < EndMm - TouchToleranceMm;

    /// <summary>
    /// Whether this panel may be counted towards a continuous run: its design rating has to reach
    /// what the host requires, and it must not be an unprotected opening (docs §5.4). A required
    /// rating of null is no licence to count it — there is nothing to compare against.
    /// </summary>
    public bool Qualifies(double? requiredMinutes) =>
        !IsUnprotectedOpening &&
        Rating.IsRated &&
        requiredMinutes is double required &&
        Rating.Minutes!.Value >= required;

    public override string ToString() =>
        $"{UniqueId}［{StartMm:0.#}–{EndMm:0.#} × {BottomMm:0.#}–{TopMm:0.#} mm］{Rating}";

    private static void Finite(double value, string name)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
            throw new ArgumentOutOfRangeException(name, "A measurement must be a finite number.");
    }
}

/// <summary>
/// One curtain wall (or curtain system) as the adapter read it: a straight plan location line, the
/// outward direction of its exterior face, the elevations it spans, and the panels and grid lines
/// laid out on its plane. Everything in millimetres, in host project coordinates.
/// </summary>
/// <remarks>
/// A curved, sloped or warped wall cannot be described this way, and the adapter says so in
/// <see cref="NonPlanarReason"/> rather than flattening it into a line that would measure the wrong
/// thing (docs §9). The resolver turns that into 人工覆核.
/// </remarks>
public sealed class CurtainWallObservation
{
    public CurtainWallObservation(
        string uniqueId,
        Point2D start,
        Point2D end,
        Point2D exteriorNormal,
        double exteriorOffsetMm,
        double baseElevationMm,
        double topElevationMm,
        IEnumerable<CurtainPanelObservation>? panels = null,
        IEnumerable<CurtainGridLineObservation>? gridLines = null,
        string? nonPlanarReason = null,
        string? typeName = null)
    {
        if (string.IsNullOrWhiteSpace(uniqueId)) throw new ArgumentException("Curtain wall UniqueId is required.", nameof(uniqueId));
        if (exteriorOffsetMm < 0 || double.IsNaN(exteriorOffsetMm) || double.IsInfinity(exteriorOffsetMm))
            throw new ArgumentOutOfRangeException(nameof(exteriorOffsetMm), "The offset to the outside face cannot be negative.");
        if (topElevationMm - baseElevationMm <= CurtainPanelObservation.TouchToleranceMm)
            throw new ArgumentOutOfRangeException(nameof(topElevationMm), "A curtain wall has to span some height.");

        var length = start.DistanceTo(end);
        if (length <= CurtainPanelObservation.TouchToleranceMm)
            throw new ArgumentException("A curtain wall's location line has to have a length.", nameof(end));

        var normalLength = Math.Sqrt((exteriorNormal.X * exteriorNormal.X) + (exteriorNormal.Y * exteriorNormal.Y));
        if (normalLength <= GeometryTolerance.ZeroLengthFeet)
            throw new ArgumentException("The exterior normal has to point somewhere.", nameof(exteriorNormal));

        UniqueId = uniqueId.Trim();
        Start = start;
        End = end;
        LengthMm = length;
        Direction = new Point2D((end.X - start.X) / length, (end.Y - start.Y) / length);
        ExteriorNormal = new Point2D(exteriorNormal.X / normalLength, exteriorNormal.Y / normalLength);
        ExteriorOffsetMm = exteriorOffsetMm;
        BaseElevationMm = baseElevationMm;
        TopElevationMm = topElevationMm;
        NonPlanarReason = string.IsNullOrWhiteSpace(nonPlanarReason) ? null : nonPlanarReason!.Trim();
        TypeName = string.IsNullOrWhiteSpace(typeName) ? null : typeName!.Trim();

        var panelList = (panels ?? Array.Empty<CurtainPanelObservation>()).ToList();
        if (panelList.Any(x => x is null)) throw new ArgumentException("The panels contain a missing entry.", nameof(panels));
        if (panelList.GroupBy(x => x.UniqueId, StringComparer.Ordinal).Any(g => g.Count() > 1))
            throw new ArgumentException("The same panel is listed twice.", nameof(panels));

        var gridList = (gridLines ?? Array.Empty<CurtainGridLineObservation>()).ToList();
        if (gridList.Any(x => x is null)) throw new ArgumentException("The grid lines contain a missing entry.", nameof(gridLines));

        Panels = new ReadOnlyCollection<CurtainPanelObservation>(panelList
            .OrderBy(x => x.BottomMm).ThenBy(x => x.StartMm).ThenBy(x => x.UniqueId, StringComparer.Ordinal).ToList());
        GridLines = new ReadOnlyCollection<CurtainGridLineObservation>(gridList
            .OrderBy(x => (int)x.Direction).ThenBy(x => x.PositionMm).ThenBy(x => x.UniqueId, StringComparer.Ordinal).ToList());
    }

    public string UniqueId { get; }

    /// <summary>Plan location line, in millimetres.</summary>
    public Point2D Start { get; }

    public Point2D End { get; }

    /// <summary>Unit vector from <see cref="Start"/> to <see cref="End"/>; <c>u</c> is measured along it.</summary>
    public Point2D Direction { get; }

    /// <summary>Unit vector pointing out of the building, normal to the wall.</summary>
    public Point2D ExteriorNormal { get; }

    /// <summary>Location line to outside face; what a projection has to clear (docs §4.2 step 4).</summary>
    public double ExteriorOffsetMm { get; }

    public double BaseElevationMm { get; }
    public double TopElevationMm { get; }
    public double LengthMm { get; }
    public string? TypeName { get; }

    /// <summary>Why this wall cannot be measured as a plane; null when it can (docs §9).</summary>
    public string? NonPlanarReason { get; }

    public bool IsPlanar => NonPlanarReason is null;

    /// <summary>Panels in bottom-then-left order, so a fixed model yields a fixed run.</summary>
    public IReadOnlyList<CurtainPanelObservation> Panels { get; }

    public IReadOnlyList<CurtainGridLineObservation> GridLines { get; }

    /// <summary>The plan point at distance <paramref name="millimeters"/> along the location line.</summary>
    public Point2D PointAt(double millimeters) =>
        new(Start.X + (Direction.X * millimeters), Start.Y + (Direction.Y * millimeters));

    /// <summary>How far along the location line a point falls; negative or past the length is off the wall.</summary>
    public double ParameterOf(Point2D point) =>
        ((point.X - Start.X) * Direction.X) + ((point.Y - Start.Y) * Direction.Y);

    /// <summary>How far out from the location line a point lies; negative is inside the building.</summary>
    public double OutwardDistanceOf(Point2D point) =>
        ((point.X - Start.X) * ExteriorNormal.X) + ((point.Y - Start.Y) * ExteriorNormal.Y);

    /// <summary>
    /// How far something at <paramref name="point"/> clears the outside face — the 突出 of 第79條第3項
    /// and 第79條之3第1項. Never negative: not projecting is 0, not missing data (docs §12 輸入契約).
    /// </summary>
    public double ProjectionOf(Point2D point) => Math.Max(0.0, OutwardDistanceOf(point) - ExteriorOffsetMm);

    public bool SpansElevation(double millimeters) =>
        millimeters >= BaseElevationMm - CurtainPanelObservation.TouchToleranceMm &&
        millimeters <= TopElevationMm + CurtainPanelObservation.TouchToleranceMm;

    public override string ToString() => $"{UniqueId}（{TypeName ?? "帷幕牆"}，{LengthMm:0.#} mm）";
}

/// <summary>
/// A 區劃牆 that reaches a curtain wall: its plan location line, the elevations it spans, the clause
/// that put it there and the rating the rules require of it (docs §4.2).
/// </summary>
public sealed class CompartmentWallObservation
{
    public CompartmentWallObservation(
        string uniqueId,
        Point2D start,
        Point2D end,
        double bottomElevationMm,
        double topElevationMm,
        string legalReference = CurtainWallJunctionReferences.Article79,
        double? requiredFireRatingMinutes = null,
        string? typeName = null,
        ProvidedFireRating? providedFireRating = null)
    {
        if (string.IsNullOrWhiteSpace(uniqueId)) throw new ArgumentException("Compartment wall UniqueId is required.", nameof(uniqueId));
        if (string.IsNullOrWhiteSpace(legalReference)) throw new ArgumentException("Name the clause the compartment comes from.", nameof(legalReference));
        if (start.DistanceTo(end) <= CurtainPanelObservation.TouchToleranceMm)
            throw new ArgumentException("A compartment wall's location line has to have a length.", nameof(end));
        if (topElevationMm - bottomElevationMm <= CurtainPanelObservation.TouchToleranceMm)
            throw new ArgumentOutOfRangeException(nameof(topElevationMm), "A compartment wall has to span some height.");
        RequireRating(requiredFireRatingMinutes, nameof(requiredFireRatingMinutes));

        UniqueId = uniqueId.Trim();
        Start = start;
        End = end;
        BottomElevationMm = bottomElevationMm;
        TopElevationMm = topElevationMm;
        LegalReference = legalReference.Trim();
        RequiredFireRatingMinutes = requiredFireRatingMinutes;
        TypeName = string.IsNullOrWhiteSpace(typeName) ? null : typeName!.Trim();
        ProvidedFireRating = providedFireRating;
    }

    public string UniqueId { get; }
    public Point2D Start { get; }
    public Point2D End { get; }
    public double BottomElevationMm { get; }
    public double TopElevationMm { get; }

    /// <summary>第79條 or 第83條 — which clause the compartment comes from (docs §2.5).</summary>
    public string LegalReference { get; }

    /// <summary>What the rules require of this wall; the bar a panel has to clear to count.</summary>
    public double? RequiredFireRatingMinutes { get; }

    public string? TypeName { get; }

    /// <summary>
    /// 該牆型別的 `防火檢討_設計防火時效`。只有在這道牆本身就是上下帷幕牆之間那道實體牆防火帶時才
    /// 用得到（docs §4.2「實體牆防火帶」、決議 7）；一般的區劃牆走嵌板路徑，讀的是嵌板的時效。
    /// </summary>
    public ProvidedFireRating? ProvidedFireRating { get; }

    public override string ToString() => $"{UniqueId}（{LegalReference}）";

    internal static void RequireRating(double? minutes, string name)
    {
        if (minutes is not double value) return;
        if (double.IsNaN(value) || double.IsInfinity(value) || value < 0 || value > ProvidedFireRating.MaximumMinutes)
            throw new ArgumentOutOfRangeException(name, "A rating is between 0 and 24 hours.");
    }
}

/// <summary>
/// A 區劃樓地板 whose edge meets a curtain wall: its plan outline read even-odd, its elevation and
/// the rating the rules require of it (docs §4.3).
/// </summary>
public sealed class CompartmentFloorObservation
{
    public CompartmentFloorObservation(
        string uniqueId,
        IEnumerable<IReadOnlyList<Point2D>>? outlineLoops,
        double elevationMm,
        double? requiredFireRatingMinutes = null,
        string? typeName = null)
    {
        if (string.IsNullOrWhiteSpace(uniqueId)) throw new ArgumentException("Floor UniqueId is required.", nameof(uniqueId));
        if (double.IsNaN(elevationMm) || double.IsInfinity(elevationMm))
            throw new ArgumentOutOfRangeException(nameof(elevationMm), "An elevation must be a finite number.");
        CompartmentWallObservation.RequireRating(requiredFireRatingMinutes, nameof(requiredFireRatingMinutes));

        UniqueId = uniqueId.Trim();
        ElevationMm = elevationMm;
        RequiredFireRatingMinutes = requiredFireRatingMinutes;
        TypeName = string.IsNullOrWhiteSpace(typeName) ? null : typeName!.Trim();
        OutlineLoops = new ReadOnlyCollection<IReadOnlyList<Point2D>>(
            (outlineLoops ?? Array.Empty<IReadOnlyList<Point2D>>())
            .Where(l => l is not null && l.Count >= 3)
            .Select(l => (IReadOnlyList<Point2D>)new ReadOnlyCollection<Point2D>(l.ToList()))
            .ToList());
    }

    public string UniqueId { get; }

    /// <summary>Outer loop and holes, in millimetres, read even-odd.</summary>
    public IReadOnlyList<IReadOnlyList<Point2D>> OutlineLoops { get; }

    /// <summary>Top of slab: the elevation a spandrel band is measured about.</summary>
    public double ElevationMm { get; }

    public double? RequiredFireRatingMinutes { get; }
    public string? TypeName { get; }

    public bool HasOutline => OutlineLoops.Count > 0;

    public bool Contains(Point2D point) =>
        OutlineLoops.Count(l => RingGeometry.ContainsPoint(l, point)) % 2 == 1;

    public override string ToString() => $"{UniqueId} @ {ElevationMm:0.#} mm";
}

/// <summary>One 區劃 as the model holds it, with its extent in millimetres so a junction can be placed in it.</summary>
public sealed class CurtainWallZoneObservation
{
    public CurtainWallZoneObservation(Guid zoneId, string? name, IEnumerable<IReadOnlyList<Point2D>>? boundaryLoops)
    {
        if (zoneId == Guid.Empty) throw new ArgumentException("Zone ID cannot be empty.", nameof(zoneId));

        ZoneId = zoneId;
        Name = string.IsNullOrWhiteSpace(name) ? zoneId.ToString("D") : name!.Trim();
        BoundaryLoops = new ReadOnlyCollection<IReadOnlyList<Point2D>>(
            (boundaryLoops ?? Array.Empty<IReadOnlyList<Point2D>>())
            .Where(l => l is not null && l.Count >= 3)
            .Select(l => (IReadOnlyList<Point2D>)new ReadOnlyCollection<Point2D>(l.ToList()))
            .ToList());
    }

    public Guid ZoneId { get; }
    public string Name { get; }
    public IReadOnlyList<IReadOnlyList<Point2D>> BoundaryLoops { get; }

    /// <summary>Even-odd, so an Area with a hole answers the way Revit draws it.</summary>
    public bool Contains(Point2D point) =>
        BoundaryLoops.Count(l => RingGeometry.ContainsPoint(l, point)) % 2 == 1;

    public override string ToString() => $"{Name}（{ZoneId:D}）";
}

/// <summary>
/// Everything 帷幕牆區劃交接 needs from the model, as plain data (spec 15): the storey's zones, the
/// curtain walls with their panels, the 區劃牆 and 區劃樓地板 that reach them, and the project's level
/// elevations — the last so a curtain wall running past a level with no floor at it can be
/// recognised as a 連跨複數樓層 space (docs §3.4, §10 案例 17).
/// </summary>
/// <remarks>
/// Millimetres throughout. The Revit adapter converts internal feet once, at its own boundary, and
/// nothing downstream converts again (docs §4.4).
/// </remarks>
public sealed class CurtainWallObservationSet
{
    public CurtainWallObservationSet(
        Guid packageId,
        string levelUniqueId,
        string? levelName,
        double levelElevationMm,
        IEnumerable<CurtainWallZoneObservation>? zones = null,
        IEnumerable<CurtainWallObservation>? curtainWalls = null,
        IEnumerable<CompartmentWallObservation>? compartmentWalls = null,
        IEnumerable<CompartmentFloorObservation>? compartmentFloors = null,
        IEnumerable<double>? levelElevationsMm = null,
        IEnumerable<string>? warnings = null)
    {
        if (packageId == Guid.Empty) throw new ArgumentException("Package ID cannot be empty.", nameof(packageId));
        if (string.IsNullOrWhiteSpace(levelUniqueId)) throw new ArgumentException("Level UniqueId is required.", nameof(levelUniqueId));
        if (double.IsNaN(levelElevationMm) || double.IsInfinity(levelElevationMm))
            throw new ArgumentOutOfRangeException(nameof(levelElevationMm), "An elevation must be a finite number.");

        var zoneList = (zones ?? Array.Empty<CurtainWallZoneObservation>()).ToList();
        var wallList = (curtainWalls ?? Array.Empty<CurtainWallObservation>()).ToList();
        var hostWallList = (compartmentWalls ?? Array.Empty<CompartmentWallObservation>()).ToList();
        var floorList = (compartmentFloors ?? Array.Empty<CompartmentFloorObservation>()).ToList();

        if (zoneList.Any(x => x is null)) throw new ArgumentException("Zones cannot contain null.", nameof(zones));
        if (wallList.Any(x => x is null)) throw new ArgumentException("Curtain walls cannot contain null.", nameof(curtainWalls));
        if (hostWallList.Any(x => x is null)) throw new ArgumentException("Compartment walls cannot contain null.", nameof(compartmentWalls));
        if (floorList.Any(x => x is null)) throw new ArgumentException("Compartment floors cannot contain null.", nameof(compartmentFloors));
        if (zoneList.GroupBy(x => x.ZoneId).Any(g => g.Count() > 1))
            throw new ArgumentException("The same zone is listed twice.", nameof(zones));
        if (wallList.GroupBy(x => x.UniqueId, StringComparer.Ordinal).Any(g => g.Count() > 1))
            throw new ArgumentException("The same curtain wall is listed twice.", nameof(curtainWalls));

        PackageId = packageId;
        LevelUniqueId = levelUniqueId.Trim();
        LevelName = string.IsNullOrWhiteSpace(levelName) ? null : levelName!.Trim();
        LevelElevationMm = levelElevationMm;
        Zones = new ReadOnlyCollection<CurtainWallZoneObservation>(zoneList);
        CurtainWalls = new ReadOnlyCollection<CurtainWallObservation>(
            wallList.OrderBy(x => x.UniqueId, StringComparer.Ordinal).ToList());
        CompartmentWalls = new ReadOnlyCollection<CompartmentWallObservation>(
            hostWallList.OrderBy(x => x.UniqueId, StringComparer.Ordinal).ToList());
        CompartmentFloors = new ReadOnlyCollection<CompartmentFloorObservation>(
            floorList.OrderBy(x => x.UniqueId, StringComparer.Ordinal).ToList());
        LevelElevationsMm = new ReadOnlyCollection<double>((levelElevationsMm ?? Array.Empty<double>())
            .Where(x => !double.IsNaN(x) && !double.IsInfinity(x)).Distinct().OrderBy(x => x).ToList());
        Warnings = new ReadOnlyCollection<string>((warnings ?? Array.Empty<string>())
            .Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).Distinct(StringComparer.Ordinal).ToList());
    }

    public Guid PackageId { get; }
    public string LevelUniqueId { get; }
    public string? LevelName { get; }

    /// <summary>The storey's own elevation; a floor at it is this storey's 區劃樓地板.</summary>
    public double LevelElevationMm { get; }

    public IReadOnlyList<CurtainWallZoneObservation> Zones { get; }
    public IReadOnlyList<CurtainWallObservation> CurtainWalls { get; }
    public IReadOnlyList<CompartmentWallObservation> CompartmentWalls { get; }
    public IReadOnlyList<CompartmentFloorObservation> CompartmentFloors { get; }

    /// <summary>Every level in the project, ascending: used to spot a storey a curtain wall runs past.</summary>
    public IReadOnlyList<double> LevelElevationsMm { get; }

    /// <summary>What the adapter skipped or approximated while reading, one sentence each.</summary>
    public IReadOnlyList<string> Warnings { get; }

    public CurtainWallZoneObservation? ZoneAt(Point2D point) => Zones.FirstOrDefault(z => z.Contains(point));
}
