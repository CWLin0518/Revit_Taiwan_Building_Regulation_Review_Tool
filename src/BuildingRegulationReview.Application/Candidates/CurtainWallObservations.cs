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
        string? typeName = null,
        CurtainPanelKind? kind = null)
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
        Kind = isOpening ? CurtainPanelKind.Opening : kind;
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

    /// <summary>
    /// 防火檢討_設計防火保護 as it was read. Every panel carries it since 決議 16 — a 玻璃嵌板 answers
    /// 第79條之4 with it, not with a rating — so this is no longer an openings-only reading; it is null
    /// only when the adapter did not read it at all.
    /// </summary>
    public ProvidedFireProtection? Protection { get; }

    /// <summary>
    /// 實心／玻璃／門窗, as 防火檢討_嵌板種類 declares it (帷幕牆規格 §3.3, 決議 16); null is 未宣告,
    /// which CW-O answers 資料不足 rather than guessing. An opening is always
    /// <see cref="CurtainPanelKind.Opening"/> — the category says so, the user does not declare it.
    /// </summary>
    public CurtainPanelKind? Kind { get; }

    /// <summary>實心嵌板以設計防火時效作答；玻璃嵌板與門窗讀防火保護；未宣告時兩者都不作答。</summary>
    public bool AnswersByRating => Kind is CurtainPanelKind declared && CurtainPanelKinds.AnswersByRating(declared);

    /// <summary>玻璃嵌板或帷幕牆門窗：第79條之4 由 <see cref="Protection"/> 回答。</summary>
    public bool AnswersByProtection => Kind is CurtainPanelKind declared && !CurtainPanelKinds.AnswersByRating(declared);

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
/// <para>
/// An arc curtain wall is handed over as one observation per <b>facet</b>: Revit lays flat panels
/// between its vertical grid lines, so each stretch between two of them really is a plane, and the
/// chord through it is that plane's location line (docs §4.7). The facets share the wall's
/// <see cref="UniqueId"/> and are told apart by <see cref="FacetIndex"/>; the resolver puts them back
/// together into one wall, so junction ids, CW-O rows and the review elevation still name one element.
/// </para>
/// <para>
/// Any other curved, sloped or warped wall cannot be described this way, and the adapter says so in
/// <see cref="NonPlanarReason"/> rather than flattening it into a line that would measure the wrong
/// thing (docs §9). The resolver turns that into 人工覆核.
/// </para>
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
        string? typeName = null,
        int? facetIndex = null,
        double facetOffsetMm = 0.0)
    {
        if (string.IsNullOrWhiteSpace(uniqueId)) throw new ArgumentException("Curtain wall UniqueId is required.", nameof(uniqueId));
        if (facetIndex < 0) throw new ArgumentOutOfRangeException(nameof(facetIndex), "A facet index cannot be negative.");
        if (facetOffsetMm < 0 || double.IsNaN(facetOffsetMm) || double.IsInfinity(facetOffsetMm))
            throw new ArgumentOutOfRangeException(nameof(facetOffsetMm), "A facet starts somewhere along the wall, never before it.");
        if (facetIndex is null && facetOffsetMm != 0.0)
            throw new ArgumentException("Only a facet has an offset along the wall.", nameof(facetOffsetMm));
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
        FacetIndex = facetIndex;
        FacetOffsetMm = facetOffsetMm;
        if (IsFacet && !IsPlanar)
            throw new ArgumentException("A facet is a plane by construction; a facet that is not is a reading error.", nameof(nonPlanarReason));

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

    /// <summary>
    /// Which flat stretch of an arc curtain wall this is, counted from the wall's start; null for a
    /// straight wall, which is one plane from end to end (docs §4.7).
    /// </summary>
    public int? FacetIndex { get; }

    /// <summary>
    /// How far along the wall — summed over the chords of the facets before it — this facet starts.
    /// It puts every facet's own distances on one axis, so a 交接帶 reaching past a facet's end still
    /// finds the panels of the next one (docs §4.7). Always 0 for a straight wall.
    /// </summary>
    public double FacetOffsetMm { get; }

    public bool IsFacet => FacetIndex is not null;

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

    /// <summary>
    /// Whether a straight wall's location line lies on this curtain wall's own plane — parallel to
    /// within <see cref="FacadeWallObservation.FacadeAngleToleranceDeg"/> and no further off the
    /// location line than <see cref="FacadeWallObservation.FacadePlaneToleranceMm"/> (docs §4.2).
    /// That is what makes a solid wall part of <b>this</b> façade rather than a wall standing behind
    /// it, and it is asked of the same line at both ends of the pipeline: the adapter prefilters the
    /// model with it, the resolver decides which walls a junction may count with it.
    /// </summary>
    public bool IsInFacadePlane(Point2D start, Point2D end)
    {
        var dx = end.X - start.X;
        var dy = end.Y - start.Y;
        var length = Math.Sqrt((dx * dx) + (dy * dy));
        if (length <= CurtainPanelObservation.TouchToleranceMm) return false;

        // |sin| of the angle between the two directions: parallel either way round, since a wall
        // drawn right to left is the same piece of façade as one drawn left to right.
        var sine = Math.Abs(((dx / length) * Direction.Y) - ((dy / length) * Direction.X));
        if (sine > Math.Sin(FacadeWallObservation.FacadeAngleToleranceDeg * Math.PI / 180.0)) return false;

        return Math.Abs(OutwardDistanceOf(start)) <= FacadeWallObservation.FacadePlaneToleranceMm &&
               Math.Abs(OutwardDistanceOf(end)) <= FacadeWallObservation.FacadePlaneToleranceMm;
    }

    /// <summary>
    /// The same wall with <see cref="ExteriorNormal"/> pointing the other way — the one thing the
    /// 外側法線定向 of docs §4.6 (決議 15) is allowed to change. <see cref="Start"/>, <see cref="End"/>,
    /// <see cref="Direction"/>, <see cref="ExteriorOffsetMm"/> and <see cref="LengthMm"/> are carried
    /// over untouched, so every junction keeps its place and its id; the panels and grid lines are
    /// carried over as they are, since they are measured on the wall's own plane, which has not moved.
    /// </summary>
    public CurtainWallObservation WithReversedExteriorNormal() =>
        new(
            UniqueId,
            Start,
            End,
            new Point2D(-ExteriorNormal.X, -ExteriorNormal.Y),
            ExteriorOffsetMm,
            BaseElevationMm,
            TopElevationMm,
            Panels,
            GridLines,
            NonPlanarReason,
            TypeName,
            FacetIndex,
            FacetOffsetMm);

    public override string ToString() => FacetIndex is int facet
        ? $"{UniqueId} 第 {facet + 1} 段（{TypeName ?? "帷幕牆"}，{LengthMm:0.#} mm）"
        : $"{UniqueId}（{TypeName ?? "帷幕牆"}，{LengthMm:0.#} mm）";
}

/// <summary>
/// One solid (non-curtain) exterior wall standing in a curtain wall's own plane: its plan location
/// line, the elevations it spans and its type's 設計防火時效. This is what supplies the 900 mm of
/// 交接處之外牆面 the 但書 of 第79條第3項 asks for (docs §4.2「交接處之外牆面」, 決議 13).
/// </summary>
/// <remarks>
/// <para>
/// The 但書's subject is 「該外牆構造」, so only a real piece of exterior wall answers it — not a
/// curtain panel, and not the 區劃牆 that reaches the façade. A 區劃牆 usually runs perpendicular to
/// the elevation and is therefore never in its plane; one that does lie along the façade is recorded
/// here as well as in <see cref="CompartmentWallObservation"/>, because the two roles do not exclude
/// each other.
/// </para>
/// <para>
/// Millimetres in host project coordinates, like everything else here. Whether this wall belongs to a
/// given curtain wall's plane is <see cref="CurtainWallObservation.IsInFacadePlane"/>'s question, not
/// this type's: the same wall can be in the plane of two curtain walls that meet in line.
/// </para>
/// </remarks>
public sealed class FacadeWallObservation
{
    /// <summary>
    /// How far a wall's location line may sit off the curtain wall's own location line and still be
    /// the same façade. Half a common wall thickness: aligning a 300 mm RC wall by its outside face
    /// to a 150 mm curtain wall already offsets the centre lines by 75 mm, and nothing about that
    /// makes it a different piece of exterior wall. A wall further in than this is a wall standing
    /// behind the façade, and it does not answer the 但書.
    /// </summary>
    public const double FacadePlaneToleranceMm = 150.0;

    /// <summary>How far from parallel a wall may run and still be read as part of the same façade.</summary>
    public const double FacadeAngleToleranceDeg = 5.0;

    public FacadeWallObservation(
        string uniqueId,
        Point2D start,
        Point2D end,
        double bottomElevationMm,
        double topElevationMm,
        string? typeName = null,
        ProvidedFireRating? providedFireRating = null)
    {
        if (string.IsNullOrWhiteSpace(uniqueId)) throw new ArgumentException("Facade wall UniqueId is required.", nameof(uniqueId));
        if (start.DistanceTo(end) <= CurtainPanelObservation.TouchToleranceMm)
            throw new ArgumentException("A facade wall's location line has to have a length.", nameof(end));
        if (topElevationMm - bottomElevationMm <= CurtainPanelObservation.TouchToleranceMm)
            throw new ArgumentOutOfRangeException(nameof(topElevationMm), "A facade wall has to span some height.");

        UniqueId = uniqueId.Trim();
        Start = start;
        End = end;
        BottomElevationMm = bottomElevationMm;
        TopElevationMm = topElevationMm;
        TypeName = string.IsNullOrWhiteSpace(typeName) ? null : typeName!.Trim();
        ProvidedFireRating = providedFireRating;
    }

    public string UniqueId { get; }
    public Point2D Start { get; }
    public Point2D End { get; }
    public double BottomElevationMm { get; }
    public double TopElevationMm { get; }
    public string? TypeName { get; }

    /// <summary>此牆型別的 `防火檢討_設計防火時效`，就照讀到的樣子；null 代表讀取層沒有讀到參數本身。</summary>
    public ProvidedFireRating? ProvidedFireRating { get; }

    /// <summary>
    /// Whether this wall spans the whole of a 區劃牆's elevations at the junction (docs §4.2 step 2).
    /// What the 50 cm 突出 keeps out is flame running round the façade, and it runs round over the
    /// whole height the compartment wall stands there — so a wall closing only part of that height
    /// leaves a gap. Being taller is no objection: a storey-high rated wall is more than the 90 cm.
    /// </summary>
    public bool CoversElevations(double bottomMm, double topMm) =>
        BottomElevationMm <= bottomMm + CurtainPanelObservation.TouchToleranceMm &&
        TopElevationMm >= topMm - CurtainPanelObservation.TouchToleranceMm;

    public bool OverlapsElevations(double bottomMm, double topMm) =>
        topMm > BottomElevationMm + CurtainPanelObservation.TouchToleranceMm &&
        bottomMm < TopElevationMm - CurtainPanelObservation.TouchToleranceMm;

    /// <summary>True when the design rating was read as a number at all — 未填 and 未綁定 are not.</summary>
    public bool HasReadableRating => ProvidedFireRating is { IsRated: true };

    /// <summary>
    /// Whether this wall may be counted towards a continuous run: its design rating has to reach what
    /// the host requires (docs §5.4). A required rating of null is no licence to count it — there is
    /// nothing to compare against.
    /// </summary>
    public bool Qualifies(double? requiredMinutes) =>
        HasReadableRating &&
        requiredMinutes is double required &&
        ProvidedFireRating!.Minutes!.Value >= required;

    public override string ToString() =>
        $"{UniqueId}（{TypeName ?? "實體外牆"}，{BottomElevationMm:0.#}–{TopElevationMm:0.#} mm）";
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
        string? typeName = null)
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

    // 這道牆自己的設計防火時效**刻意不記在這裡**。CW-H 的但書問的是「該外牆構造」的時效，答案只能
    // 來自立面內的實體外牆（FacadeWallObservation）；若容許以區劃牆自身的時效作答，任何具時效的
    // 區劃牆抵上玻璃帷幕牆都會自動合格，第79條第4項即形同虛設（docs §4.2、決議 13）。躺在立面上的
    // 區劃牆會另外被登錄成一道實體外牆，該身分才帶著時效。

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
/// curtain walls with their panels, the 區劃牆 and 區劃樓地板 that reach them, the solid exterior walls
/// standing in those curtain walls' planes, and the project's level elevations — the last so a curtain
/// wall running past a level with no floor at it can be recognised as a 連跨複數樓層 space (docs §3.4,
/// §10 案例 17).
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
        IEnumerable<FacadeWallObservation>? facadeWalls = null,
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
        var facadeList = (facadeWalls ?? Array.Empty<FacadeWallObservation>()).ToList();

        if (zoneList.Any(x => x is null)) throw new ArgumentException("Zones cannot contain null.", nameof(zones));
        if (wallList.Any(x => x is null)) throw new ArgumentException("Curtain walls cannot contain null.", nameof(curtainWalls));
        if (hostWallList.Any(x => x is null)) throw new ArgumentException("Compartment walls cannot contain null.", nameof(compartmentWalls));
        if (floorList.Any(x => x is null)) throw new ArgumentException("Compartment floors cannot contain null.", nameof(compartmentFloors));
        if (facadeList.Any(x => x is null)) throw new ArgumentException("Facade walls cannot contain null.", nameof(facadeWalls));
        if (zoneList.GroupBy(x => x.ZoneId).Any(g => g.Count() > 1))
            throw new ArgumentException("The same zone is listed twice.", nameof(zones));
        foreach (var group in wallList.GroupBy(x => x.UniqueId, StringComparer.Ordinal))
        {
            // 一道牆要嘛整片一筆，要嘛全部以段登錄且段號不重複（docs §4.7）。
            var entries = group.ToList();
            if (entries.Count > 1 && entries.Any(x => !x.IsFacet))
                throw new ArgumentException("The same curtain wall is listed twice.", nameof(curtainWalls));
            if (entries.GroupBy(x => x.FacetIndex).Any(g => g.Count() > 1))
                throw new ArgumentException("The same curtain wall facet is listed twice.", nameof(curtainWalls));
        }
        if (facadeList.GroupBy(x => x.UniqueId, StringComparer.Ordinal).Any(g => g.Count() > 1))
            throw new ArgumentException("The same facade wall is listed twice.", nameof(facadeWalls));

        PackageId = packageId;
        LevelUniqueId = levelUniqueId.Trim();
        LevelName = string.IsNullOrWhiteSpace(levelName) ? null : levelName!.Trim();
        LevelElevationMm = levelElevationMm;
        Zones = new ReadOnlyCollection<CurtainWallZoneObservation>(zoneList);
        CurtainWalls = new ReadOnlyCollection<CurtainWallObservation>(
            wallList.OrderBy(x => x.UniqueId, StringComparer.Ordinal).ThenBy(x => x.FacetIndex ?? 0).ToList());
        CompartmentWalls = new ReadOnlyCollection<CompartmentWallObservation>(
            hostWallList.OrderBy(x => x.UniqueId, StringComparer.Ordinal).ToList());
        CompartmentFloors = new ReadOnlyCollection<CompartmentFloorObservation>(
            floorList.OrderBy(x => x.UniqueId, StringComparer.Ordinal).ToList());
        FacadeWalls = new ReadOnlyCollection<FacadeWallObservation>(
            facadeList.OrderBy(x => x.UniqueId, StringComparer.Ordinal).ToList());
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

    /// <summary>
    /// The solid exterior walls standing in the curtain walls' planes: what supplies the 900 mm of
    /// 交接處之外牆面 at a CW-H junction (docs §4.2, 決議 13). Which curtain wall each one belongs to is
    /// <see cref="CurtainWallObservation.IsInFacadePlane"/>'s question, asked as a junction is measured.
    /// </summary>
    public IReadOnlyList<FacadeWallObservation> FacadeWalls { get; }

    /// <summary>Every level in the project, ascending: used to spot a storey a curtain wall runs past.</summary>
    public IReadOnlyList<double> LevelElevationsMm { get; }

    /// <summary>What the adapter skipped or approximated while reading, one sentence each.</summary>
    public IReadOnlyList<string> Warnings { get; }

    public CurtainWallZoneObservation? ZoneAt(Point2D point) => Zones.FirstOrDefault(z => z.Contains(point));
}
