using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using BuildingRegulationReview.Application.Diagnostics;
using BuildingRegulationReview.Domain.Geometry;
using BuildingRegulationReview.Domain.Reviews;

namespace BuildingRegulationReview.Application.Checks;

/// <summary>
/// The three 帷幕牆 checks, and the value each one puts in <c>junction.kind</c>
/// (docs/regulations/curtain-wall-fire-compartment.md §3). The rules key off this, and they are
/// mutually exclusive: one junction is only ever one of them.
/// </summary>
public enum CurtainWallJunctionKind
{
    /// <summary>CW-H：區劃牆與帷幕牆之水平交接（第79條第3、4項，區劃來源含第83條）.</summary>
    WallToCurtainWall,

    /// <summary>CW-V：區劃樓地板與帷幕牆之層間交接（第79條之3）.</summary>
    FloorToCurtainWall,

    /// <summary>CW-O：不落在任一 90 cm 帶內的其餘帷幕嵌板（第79條之4）.</summary>
    CurtainPanelOther
}

public static class CurtainWallJunctionKinds
{
    public static IReadOnlyList<CurtainWallJunctionKind> All { get; } =
        new ReadOnlyCollection<CurtainWallJunctionKind>(new[]
        {
            CurtainWallJunctionKind.WallToCurtainWall,
            CurtainWallJunctionKind.FloorToCurtainWall,
            CurtainWallJunctionKind.CurtainPanelOther
        });

    /// <summary>The text the rules compare <c>junction.kind</c> against.</summary>
    public static string RuleText(CurtainWallJunctionKind kind) => kind switch
    {
        CurtainWallJunctionKind.WallToCurtainWall => "WallToCurtainWall",
        CurtainWallJunctionKind.FloorToCurtainWall => "FloorToCurtainWall",
        CurtainWallJunctionKind.CurtainPanelOther => "CurtainPanelOther",
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    /// <summary>The row of the 檢討表 this kind is counted in (docs §7.2).</summary>
    public static string Label(CurtainWallJunctionKind kind) => kind switch
    {
        CurtainWallJunctionKind.WallToCurtainWall => "帷幕牆區劃交接（水平）",
        CurtainWallJunctionKind.FloorToCurtainWall => "帷幕牆區劃交接（層間）",
        CurtainWallJunctionKind.CurtainPanelOther => "帷幕牆其他部分時效",
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };
}

/// <summary>
/// The clause a junction's compartment comes from, recorded as <c>junction.hostLegalReference</c>.
/// CW-H covers both 第79條 and 第83條 (docs §2.5: 第83條 is an interpretation, so the evidence has to
/// say which one a junction came from).
/// </summary>
public static class CurtainWallJunctionReferences
{
    public const string Article79 = "第79條";
    public const string Article83 = "第83條";
    public const string Article79_3 = "第79條之3";

    /// <summary>第79條之2：where a 連跨複數樓層 space is reviewed instead (docs §3.4).</summary>
    public const string Article79_2 = "建築技術規則建築設計施工編第79條之2";
}

/// <summary>
/// Why the geometry could not hand a junction to the rules (docs §3.4). These are observations, not
/// verdicts the rules withheld: nothing was measured, so nothing is compared.
/// </summary>
public enum CurtainWallJunctionDoubtKind
{
    /// <summary>曲面、傾斜面、雙曲面，或嵌板非平面（docs §9）.</summary>
    NonPlanarCurtainWall,

    /// <summary>交點解析出兩組以上候選，或區劃牆端點與帷幕牆距離超過搜尋公差.</summary>
    UnresolvedIntersection,

    /// <summary>層間帶被 grid line 分割，且兩側嵌板時效皆足夠——疑為多餘 grid line（docs §4.5）.</summary>
    SplitByGridLine,

    /// <summary>
    /// 交接處的立面上，實體外牆與帷幕嵌板重疊：模型對同一片外牆講了兩件互相矛盾的事（docs §4.2）.
    /// </summary>
    FacadeWallOverlapsPanel,

    /// <summary>連跨複數樓層之挑空帷幕牆：改依第79條之2垂直區劃檢討，不是本項的未符合.</summary>
    VerticalCompartmentSpace
}

/// <summary>One such observation, with the elements a user has to look at.</summary>
public sealed class CurtainWallJunctionDoubt
{
    public CurtainWallJunctionDoubt(CurtainWallJunctionDoubtKind kind, string message, IEnumerable<string>? subjectUniqueIds = null)
    {
        if (!Enum.IsDefined(typeof(CurtainWallJunctionDoubtKind), kind)) throw new ArgumentOutOfRangeException(nameof(kind));
        if (string.IsNullOrWhiteSpace(message)) throw new ArgumentException("A doubt must say why.", nameof(message));

        Kind = kind;
        Message = message.Trim();
        SubjectUniqueIds = new ReadOnlyCollection<string>((subjectUniqueIds ?? Array.Empty<string>())
            .Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim())
            .Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToList());
    }

    public CurtainWallJunctionDoubtKind Kind { get; }

    /// <summary>What the user is told, e.g. the ElementId of the grid line that split the band.</summary>
    public string Message { get; }

    /// <summary>The elements the message is about — a grid line, a panel — beyond the junction's own.</summary>
    public IReadOnlyList<string> SubjectUniqueIds { get; }

    /// <summary>
    /// 人工覆核 for everything the tool cannot measure, but 不適用 for a 連跨複數樓層 space: that one is
    /// not in doubt at all, it belongs to another clause (docs §3.4).
    /// </summary>
    public ReviewStatus Status => Kind == CurtainWallJunctionDoubtKind.VerticalCompartmentSpace
        ? ReviewStatus.NotApplicable
        : ReviewStatus.ManualReview;

    public string ErrorCode => Kind switch
    {
        CurtainWallJunctionDoubtKind.NonPlanarCurtainWall => ReviewErrorCode.CurtainWallNotPlanar,
        CurtainWallJunctionDoubtKind.UnresolvedIntersection => ReviewErrorCode.CurtainWallJunctionUnresolved,
        CurtainWallJunctionDoubtKind.SplitByGridLine => ReviewErrorCode.CurtainWallJunctionSplitByGridLine,
        CurtainWallJunctionDoubtKind.FacadeWallOverlapsPanel => ReviewErrorCode.CurtainWallFacadeOverlapsPanel,
        _ => ReviewErrorCode.CurtainWallVerticalSpace
    };

    public override string ToString() => $"{Kind}: {Message}";
}

/// <summary>
/// Where a junction sits in the model, so the review view can mark it there (docs §7.1): a segment of
/// the curtain wall's plan location line and the elevations the junction spans. CW-H is a point — the
/// intersection, where the measurements are annotated — so its start and end are the same; CW-V is the
/// 層間帶, across the floor edge and 900 mm above and below the slab.
/// <para>
/// Millimetres in host project coordinates, like everything the geometry layer produces (docs §4.4);
/// the Revit adapter converts once, at its own boundary. The 檢討表 and the markup plan are both
/// rebuilt from the stored results alone, so this travels with them through
/// <see cref="ToEvidenceText"/> — in metres, the unit every other length in the evidence is in.
/// </para>
/// </summary>
public sealed class CurtainWallJunctionPlacement
{
    /// <summary>Two plan points this close are the same point: a junction with no width.</summary>
    private const double CoincidentToleranceMm = 0.5;

    private const double MillimetersPerMeter = 1000.0;
    private const string NumberFormat = "0.######";
    private const char Separator = ',';

    public CurtainWallJunctionPlacement(Point2D startMm, Point2D endMm, double bottomElevationMm, double topElevationMm)
    {
        Finite(startMm.X, nameof(startMm));
        Finite(startMm.Y, nameof(startMm));
        Finite(endMm.X, nameof(endMm));
        Finite(endMm.Y, nameof(endMm));
        Finite(bottomElevationMm, nameof(bottomElevationMm));
        Finite(topElevationMm, nameof(topElevationMm));
        if (topElevationMm <= bottomElevationMm)
            throw new ArgumentOutOfRangeException(nameof(topElevationMm), "A placement has to span some height.");

        StartMm = startMm;
        EndMm = endMm;
        BottomElevationMm = bottomElevationMm;
        TopElevationMm = topElevationMm;
    }

    /// <summary>CW-H：the intersection point, where §7.1 asks for the measurements to be annotated.</summary>
    public static CurtainWallJunctionPlacement At(Point2D pointMm, double bottomElevationMm, double topElevationMm) =>
        new(pointMm, pointMm, bottomElevationMm, topElevationMm);

    /// <summary>CW-V：the 層間帶, as wide as the floor edge and as tall as §4.5 defines it.</summary>
    public static CurtainWallJunctionPlacement Band(Point2D startMm, Point2D endMm, double bottomElevationMm, double topElevationMm) =>
        new(startMm, endMm, bottomElevationMm, topElevationMm);

    public Point2D StartMm { get; }
    public Point2D EndMm { get; }
    public double BottomElevationMm { get; }
    public double TopElevationMm { get; }

    /// <summary>True for a junction that is one point in plan: nothing to draw a band across.</summary>
    public bool IsPoint => StartMm.DistanceTo(EndMm) <= CoincidentToleranceMm;

    public double LengthMm => StartMm.DistanceTo(EndMm);
    public double HeightMm => TopElevationMm - BottomElevationMm;

    public Point2D MidpointMm => new((StartMm.X + EndMm.X) / 2.0, (StartMm.Y + EndMm.Y) / 2.0);

    /// <summary>
    /// The placement as the closed rectangle on the curtain wall's plane that the 層間帶 Filled Region
    /// is drawn from (docs §7.1): bottom start, bottom end, top end, top start. The order walks the
    /// rectangle once, so the adapter can turn it straight into a boundary without sorting anything;
    /// the caller closes it. Degenerate for a <see cref="IsPoint"/> placement, which is CW-H and gets
    /// a note rather than a band.
    /// </summary>
    public IReadOnlyList<PlacementCorner> Corners() => new ReadOnlyCollection<PlacementCorner>(new[]
    {
        new PlacementCorner(StartMm, BottomElevationMm),
        new PlacementCorner(EndMm, BottomElevationMm),
        new PlacementCorner(EndMm, TopElevationMm),
        new PlacementCorner(StartMm, TopElevationMm)
    });

    /// <summary>
    /// The six numbers of the placement in metres, comma separated: start X, start Y, end X, end Y,
    /// bottom, top. One evidence field rather than six, the way <c>junction.panels</c> is one field.
    /// </summary>
    public string ToEvidenceText() => string.Join(Separator.ToString(), new[]
    {
        StartMm.X, StartMm.Y, EndMm.X, EndMm.Y, BottomElevationMm, TopElevationMm
    }.Select(x => (x / MillimetersPerMeter).ToString(NumberFormat, CultureInfo.InvariantCulture)));

    /// <summary>Reads back what <see cref="ToEvidenceText"/> wrote; false for anything else.</summary>
    public static bool TryParseEvidence(string? text, out CurtainWallJunctionPlacement? placement)
    {
        placement = null;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var parts = text!.Split(Separator);
        if (parts.Length != 6) return false;

        var numbers = new double[6];
        for (var i = 0; i < 6; i++)
            if (!double.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out numbers[i]) ||
                double.IsNaN(numbers[i]) || double.IsInfinity(numbers[i]))
                return false;

        for (var i = 0; i < 6; i++) numbers[i] *= MillimetersPerMeter;
        if (numbers[5] <= numbers[4]) return false;

        placement = new CurtainWallJunctionPlacement(
            new Point2D(numbers[0], numbers[1]), new Point2D(numbers[2], numbers[3]), numbers[4], numbers[5]);
        return true;
    }

    public override string ToString() => string.Format(CultureInfo.InvariantCulture,
        "({0:0.#}, {1:0.#})–({2:0.#}, {3:0.#}) @ {4:0.#}–{5:0.#} mm",
        StartMm.X, StartMm.Y, EndMm.X, EndMm.Y, BottomElevationMm, TopElevationMm);

    private static void Finite(double value, string name)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
            throw new ArgumentOutOfRangeException(name, "A placement must be made of finite numbers.");
    }
}

/// <summary>
/// One corner of a <see cref="CurtainWallJunctionPlacement"/>: where it is in plan and how high it is,
/// both in millimetres in host project coordinates.
/// </summary>
public readonly struct PlacementCorner : IEquatable<PlacementCorner>
{
    public PlacementCorner(Point2D planMm, double elevationMm)
    {
        PlanMm = planMm;
        ElevationMm = elevationMm;
    }

    public Point2D PlanMm { get; }
    public double ElevationMm { get; }

    public bool Equals(PlacementCorner other) =>
        PlanMm.Equals(other.PlanMm) && ElevationMm.Equals(other.ElevationMm);

    public override bool Equals(object? obj) => obj is PlacementCorner other && Equals(other);

    public override int GetHashCode()
    {
        unchecked
        {
            return (PlanMm.GetHashCode() * 31) + ElevationMm.GetHashCode();
        }
    }

    public static bool operator ==(PlacementCorner left, PlacementCorner right) => left.Equals(right);
    public static bool operator !=(PlacementCorner left, PlacementCorner right) => !left.Equals(right);

    public override string ToString() => string.Format(
        CultureInfo.InvariantCulture, "({0:0.#}, {1:0.#}) @ {2:0.#} mm", PlanMm.X, PlanMm.Y, ElevationMm);
}

/// <summary>
/// One junction as the geometry layer measured it (docs §12「步驟 3 的輸入契約」). Lengths are in
/// millimetres, the unit the geometry layer works in; the check converts them to the metres the rule
/// fields declare.
/// <para>
/// The contract the factories enforce is the whole point of this type:
/// <list type="bullet">
/// <item><see cref="ProjectionDepthMm"/> is always supplied — no projection is 0, never absent.
/// Omitting it would turn 本文 into missing data and hide a genuine Fail.</item>
/// <item>A continuous run is supplied only when it could be measured end to end. Any panel in the
/// band without a design rating means no run is supplied at all — never 0 — so the engine answers
/// 資料不足 instead of 未符合 (spec 11.3).</item>
/// <item>A run only ever counts panels whose 設計防火時效 reaches
/// <see cref="HostRequiredFireRatingMinutes"/>, and stops at a grid line, an unprotected opening or an
/// under-rated panel (docs §5.4). That is why 「具同等以上防火時效」 needs no second test.</item>
/// </list>
/// </para>
/// </summary>
public sealed class CurtainWallJunction
{
    private CurtainWallJunction(
        string junctionId,
        CurtainWallJunctionKind kind,
        Guid zoneId,
        string curtainWallUniqueId,
        string? hostUniqueId,
        string? hostLegalReference,
        double? hostRequiredFireRatingMinutes,
        ProvidedFireRating? minFireRating,
        double? projectionDepthMm,
        double? continuousFireRatedLengthMm,
        double? continuousFireRatedHeightMm,
        bool? hasUnprotectedOpening,
        IEnumerable<string>? panelUniqueIds,
        IEnumerable<string>? facadeWallUniqueIds,
        CurtainWallJunctionPlacement? placement,
        CurtainWallJunctionDoubt? doubt)
    {
        if (string.IsNullOrWhiteSpace(junctionId)) throw new ArgumentException("Junction ID is required.", nameof(junctionId));
        if (!Enum.IsDefined(typeof(CurtainWallJunctionKind), kind)) throw new ArgumentOutOfRangeException(nameof(kind));
        if (zoneId == Guid.Empty) throw new ArgumentException("Zone ID cannot be empty.", nameof(zoneId));
        if (string.IsNullOrWhiteSpace(curtainWallUniqueId)) throw new ArgumentException("Curtain wall UniqueId is required.", nameof(curtainWallUniqueId));

        JunctionId = junctionId.Trim();
        Kind = kind;
        ZoneId = zoneId;
        CurtainWallUniqueId = curtainWallUniqueId.Trim();
        HostUniqueId = Trimmed(hostUniqueId);
        HostLegalReference = Trimmed(hostLegalReference);
        HostRequiredFireRatingMinutes = Rating(hostRequiredFireRatingMinutes, nameof(hostRequiredFireRatingMinutes));
        MinFireRating = minFireRating;
        ProjectionDepthMm = Length(projectionDepthMm, nameof(projectionDepthMm));
        ContinuousFireRatedLengthMm = Length(continuousFireRatedLengthMm, nameof(continuousFireRatedLengthMm));
        ContinuousFireRatedHeightMm = Length(continuousFireRatedHeightMm, nameof(continuousFireRatedHeightMm));
        HasUnprotectedOpening = hasUnprotectedOpening;
        PanelUniqueIds = new ReadOnlyCollection<string>((panelUniqueIds ?? Array.Empty<string>())
            .Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim())
            .Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToList());
        FacadeWallUniqueIds = new ReadOnlyCollection<string>((facadeWallUniqueIds ?? Array.Empty<string>())
            .Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim())
            .Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToList());
        Placement = placement;
        Doubt = doubt;
    }

    /// <summary>CW-H：一個交點。Every measurement is supplied except a run that could not be measured.</summary>
    public static CurtainWallJunction WallJunction(
        string junctionId,
        Guid zoneId,
        string curtainWallUniqueId,
        string hostUniqueId,
        double projectionDepthMm,
        double? continuousFireRatedLengthMm = null,
        double? hostRequiredFireRatingMinutes = null,
        ProvidedFireRating? minFireRating = null,
        string hostLegalReference = CurtainWallJunctionReferences.Article79,
        bool? hasUnprotectedOpening = null,
        IEnumerable<string>? panelUniqueIds = null,
        CurtainWallJunctionPlacement? placement = null,
        IEnumerable<string>? facadeWallUniqueIds = null)
    {
        if (string.IsNullOrWhiteSpace(hostUniqueId)) throw new ArgumentException("The compartment wall's UniqueId is required.", nameof(hostUniqueId));
        if (string.IsNullOrWhiteSpace(hostLegalReference)) throw new ArgumentException("Name the clause the compartment comes from.", nameof(hostLegalReference));

        return new CurtainWallJunction(junctionId, CurtainWallJunctionKind.WallToCurtainWall, zoneId, curtainWallUniqueId,
            hostUniqueId, hostLegalReference, hostRequiredFireRatingMinutes, minFireRating,
            projectionDepthMm, continuousFireRatedLengthMm, null, hasUnprotectedOpening, panelUniqueIds,
            facadeWallUniqueIds, placement, null);
    }

    /// <summary>CW-V：一個層間帶的最不利取樣點（docs §4.3）.</summary>
    public static CurtainWallJunction Spandrel(
        string junctionId,
        Guid zoneId,
        string curtainWallUniqueId,
        string hostUniqueId,
        double projectionDepthMm,
        double? continuousFireRatedHeightMm = null,
        double? hostRequiredFireRatingMinutes = null,
        ProvidedFireRating? minFireRating = null,
        bool? hasUnprotectedOpening = null,
        IEnumerable<string>? panelUniqueIds = null,
        CurtainWallJunctionPlacement? placement = null)
    {
        if (string.IsNullOrWhiteSpace(hostUniqueId)) throw new ArgumentException("The compartment floor's UniqueId is required.", nameof(hostUniqueId));

        return new CurtainWallJunction(junctionId, CurtainWallJunctionKind.FloorToCurtainWall, zoneId, curtainWallUniqueId,
            hostUniqueId, CurtainWallJunctionReferences.Article79_3, hostRequiredFireRatingMinutes, minFireRating,
            projectionDepthMm, null, continuousFireRatedHeightMm, hasUnprotectedOpening, panelUniqueIds,
            null, placement, null);
    }

    /// <summary>
    /// CW-O：帷幕牆上不落在任一 90 cm 帶內的嵌板，統一以其最小設計時效判定。The rating is always
    /// supplied, even as <see cref="ProvidedFireRating.Missing"/>: 未綁定參數 is 資料不足, not a pass.
    /// </summary>
    public static CurtainWallJunction OtherPanels(
        string junctionId,
        Guid zoneId,
        string curtainWallUniqueId,
        ProvidedFireRating minFireRating,
        IEnumerable<string>? panelUniqueIds = null) =>
        new(junctionId, CurtainWallJunctionKind.CurtainPanelOther, zoneId, curtainWallUniqueId,
            null, null, null,
            minFireRating ?? throw new ArgumentNullException(nameof(minFireRating)),
            null, null, null, null, panelUniqueIds, null, null, null);

    /// <summary>A junction the geometry could not measure, or one that belongs to another clause (docs §3.4).</summary>
    public static CurtainWallJunction Doubtful(
        string junctionId,
        CurtainWallJunctionKind kind,
        Guid zoneId,
        string curtainWallUniqueId,
        CurtainWallJunctionDoubt doubt,
        string? hostUniqueId = null,
        IEnumerable<string>? panelUniqueIds = null,
        IEnumerable<string>? facadeWallUniqueIds = null) =>
        new(junctionId, kind, zoneId, curtainWallUniqueId, hostUniqueId, null, null, null, null, null, null, null,
            panelUniqueIds, facadeWallUniqueIds, null, doubt ?? throw new ArgumentNullException(nameof(doubt)));

    /// <summary>Identifies this junction within the run; results and repeat runs key off it.</summary>
    public string JunctionId { get; }

    public CurtainWallJunctionKind Kind { get; }
    public Guid ZoneId { get; }
    public string CurtainWallUniqueId { get; }

    /// <summary>The compartment wall or floor; null for CW-O, which has no host.</summary>
    public string? HostUniqueId { get; }

    /// <summary>第79條／第83條／第79條之3 — which clause put the compartment there.</summary>
    public string? HostLegalReference { get; }

    /// <summary>What the host must achieve; the threshold a panel has to reach to count towards a run.</summary>
    public double? HostRequiredFireRatingMinutes { get; }

    /// <summary>The lowest 設計防火時效 in the band, as it was read.</summary>
    public ProvidedFireRating? MinFireRating { get; }

    /// <summary>How far the host projects past the curtain wall; 0 when it does not project at all.</summary>
    public double? ProjectionDepthMm { get; }

    /// <summary>CW-H：交點兩側連續具時效長度的總和，量不到時為 null.</summary>
    public double? ContinuousFireRatedLengthMm { get; }

    /// <summary>CW-V：樓板上下連續具時效高度的總和，量不到時為 null.</summary>
    public double? ContinuousFireRatedHeightMm { get; }

    /// <summary>Whether an unprotected opening sits in the band; the run already stops there.</summary>
    public bool? HasUnprotectedOpening { get; }

    /// <summary>
    /// The curtain panels the junction covers — what a failed result marks red (docs §7.1). For CW-H
    /// these are the panels the 交接帶 covers, which is also what CW-O deducts; they no longer supply
    /// the run itself (決議 13).
    /// </summary>
    public IReadOnlyList<string> PanelUniqueIds { get; }

    /// <summary>
    /// CW-H：the solid exterior walls the run was measured on (docs §4.2, 決議 13); empty for the other
    /// kinds and for a junction where the façade carries none.
    /// </summary>
    public IReadOnlyList<string> FacadeWallUniqueIds { get; }

    /// <summary>
    /// Where the junction is, for the review view's annotation (CW-H) and 層間帶 (CW-V); null for
    /// CW-O, which is a set of panels rather than a place, and for a junction nothing was measured at.
    /// </summary>
    public CurtainWallJunctionPlacement? Placement { get; }

    /// <summary>Set when the geometry could not produce facts; then no rule runs on this junction.</summary>
    public CurtainWallJunctionDoubt? Doubt { get; }

    public bool IsDoubtful => Doubt is not null;

    /// <summary>The curtain wall, its host, its panels and its façade walls: everything a result is about.</summary>
    public IEnumerable<string> SubjectUniqueIds =>
        new[] { CurtainWallUniqueId }
            .Concat(HostUniqueId is null ? Array.Empty<string>() : new[] { HostUniqueId })
            .Concat(PanelUniqueIds)
            .Concat(FacadeWallUniqueIds)
            .Concat(Doubt?.SubjectUniqueIds ?? (IEnumerable<string>)Array.Empty<string>());

    public override string ToString() =>
        $"{JunctionId}（{CurtainWallJunctionKinds.Label(Kind)}）" +
        (Doubt is null ? string.Empty : $"：{Doubt.Kind}");

    private static string? Trimmed(string? text) => string.IsNullOrWhiteSpace(text) ? null : text!.Trim();

    private static double? Length(double? millimeters, string name)
    {
        if (millimeters is not double value) return null;
        if (double.IsNaN(value) || double.IsInfinity(value)) throw new ArgumentOutOfRangeException(name, "A measurement must be a finite number.");
        if (value < 0) throw new ArgumentOutOfRangeException(name, "A measurement cannot be negative.");
        return value;
    }

    private static double? Rating(double? minutes, string name)
    {
        if (minutes is not double value) return null;
        if (double.IsNaN(value) || double.IsInfinity(value) || value < 0 || value > ProvidedFireRating.MaximumMinutes)
            throw new ArgumentOutOfRangeException(name, "A rating is between 0 and 24 hours.");
        return value;
    }
}

/// <summary>
/// What the 帷幕牆區劃交接 check needs besides the model: the building and zone inputs the rules may
/// use — the same ones the 區劃面積 check takes, so <c>junction.*</c> can never be supplied as an
/// input — and every junction the geometry layer resolved.
/// </summary>
public sealed class CurtainWallJunctionInputs
{
    public static readonly CurtainWallJunctionInputs None = new(null, null);

    private readonly Dictionary<string, CurtainWallJunction> _byId;

    public CurtainWallJunctionInputs(CompartmentAreaInputs? context, IEnumerable<CurtainWallJunction>? junctions)
    {
        Context = context ?? CompartmentAreaInputs.None;

        var list = (junctions ?? Array.Empty<CurtainWallJunction>()).ToList();
        if (list.Any(x => x is null)) throw new ArgumentException("The junctions contain a missing entry.", nameof(junctions));
        var duplicate = list.GroupBy(x => x.JunctionId, StringComparer.Ordinal).FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
            throw new ArgumentException($"Junction '{duplicate.Key}' is supplied more than once.", nameof(junctions));

        _byId = list.ToDictionary(x => x.JunctionId, StringComparer.Ordinal);
        Junctions = new ReadOnlyCollection<CurtainWallJunction>(list
            .OrderBy(x => x.ZoneId)
            .ThenBy(x => (int)x.Kind)
            .ThenBy(x => x.JunctionId, StringComparer.Ordinal)
            .ToList());
    }

    /// <summary>Building and zone inputs (構造、用途、灑水…), applied to every junction's facts.</summary>
    public CompartmentAreaInputs Context { get; }

    /// <summary>Every junction, in zone, kind and ID order, so a fixed model yields a fixed run.</summary>
    public IReadOnlyList<CurtainWallJunction> Junctions { get; }

    public CurtainWallJunction? For(string junctionId) =>
        junctionId is not null && _byId.TryGetValue(junctionId, out var junction) ? junction : null;

    public IEnumerable<CurtainWallJunction> ForZone(Guid zoneId) => Junctions.Where(x => x.ZoneId == zoneId);
}

/// <summary>
/// The constants of 帷幕牆區劃交接, kept in one place instead of scattered through the geometry
/// (docs §4.4). <see cref="MinProjectionMm"/>, <see cref="MinFireRatedRunMm"/> and
/// <see cref="OtherWallRequiredMinutes"/> restate what the rule set requires — the rules remain the
/// only judge; the geometry layer needs them to know how far to walk when it builds a band.
/// <see cref="JunctionSearchToleranceMm"/> and <see cref="SamplingIntervalMm"/> are the tool's own
/// settings, and the check records them so a measurement can be traced to how it was taken.
/// </summary>
public sealed class CurtainWallJunctionOptions
{
    public static readonly CurtainWallJunctionOptions Default = new();

    public CurtainWallJunctionOptions(
        double minProjectionMm = 500,
        double minFireRatedRunMm = 900,
        double otherWallRequiredMinutes = 30,
        double junctionSearchToleranceMm = 300,
        double samplingIntervalMm = 600)
    {
        MinProjectionMm = Positive(minProjectionMm, nameof(minProjectionMm));
        MinFireRatedRunMm = Positive(minFireRatedRunMm, nameof(minFireRatedRunMm));
        OtherWallRequiredMinutes = Positive(otherWallRequiredMinutes, nameof(otherWallRequiredMinutes));
        JunctionSearchToleranceMm = Positive(junctionSearchToleranceMm, nameof(junctionSearchToleranceMm));
        SamplingIntervalMm = Positive(samplingIntervalMm, nameof(samplingIntervalMm));
    }

    /// <summary>第79條第3項、第79條之3第1項：應突出五十公分以上.</summary>
    public double MinProjectionMm { get; }

    /// <summary>同上但書：交接處之外牆面長度／高度有九十公分以上.</summary>
    public double MinFireRatedRunMm { get; }

    /// <summary>第79條之4：其他部分外牆應具有半小時以上防火時效.</summary>
    public double OtherWallRequiredMinutes { get; }

    /// <summary>How far past its end a compartment wall is extended when looking for the curtain wall.</summary>
    public double JunctionSearchToleranceMm { get; }

    /// <summary>How closely a spandrel band is sampled along the floor edge (docs §4.3).</summary>
    public double SamplingIntervalMm { get; }

    public override string ToString() => string.Format(CultureInfo.InvariantCulture,
        "突出 {0:0.##} mm／連續 {1:0.##} mm／其他部分 {2:0.##} min", MinProjectionMm, MinFireRatedRunMm, OtherWallRequiredMinutes);

    private static double Positive(double value, string name)
    {
        if (double.IsNaN(value) || double.IsInfinity(value) || value <= 0)
            throw new ArgumentOutOfRangeException(name, "The setting must be a positive, finite number.");
        return value;
    }
}
