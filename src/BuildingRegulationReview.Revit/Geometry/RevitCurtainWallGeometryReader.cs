using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using BuildingRegulationReview.Application.Abstractions;
using BuildingRegulationReview.Application.Candidates;
using BuildingRegulationReview.Application.Checks;
using BuildingRegulationReview.Application.Reviews;
using BuildingRegulationReview.Domain.Common;
using BuildingRegulationReview.Domain.Geometry;
using BuildingRegulationReview.Revit.Parameters;
using BuildingRegulationReview.Revit.Reviews;
using BuildingRegulationReview.Revit.WriteBack;

namespace BuildingRegulationReview.Revit.Geometry;

/// <summary>
/// P3 步驟 4: reads the curtain walls of one package's storey — their location line, outward face,
/// panels and grid lines — plus the 區劃牆 and 區劃樓地板 the caller named and the solid exterior walls
/// standing in those curtain walls' planes, and hands it all over as plain data
/// (docs/regulations/curtain-wall-fire-compartment.md §4.1–§4.3). Read-only: no transaction, no change
/// to the model.
/// </summary>
/// <remarks>
/// It converts and nothing else. Every judgement — where a compartment wall crosses, how far a run
/// reaches, whether a grid line is redundant — belongs to <see cref="CurtainWallJunctionResolver"/>,
/// which the core tests cover without Revit. The one conversion that happens here and nowhere else
/// is Revit's internal feet to the millimetres the domain works in (docs §4.4).
/// </remarks>
public sealed class RevitCurtainWallGeometryReader : ICurtainWallGeometryReader
{
    // A panel whose edge is this close to a grid line sits on it.
    private static readonly double SnapFeet = PlanUnits.MillimetersToFeet(1.0);

    private readonly Document _document;

    public RevitCurtainWallGeometryReader(Document document) =>
        _document = document ?? throw new ArgumentNullException(nameof(document));

    public Result<CurtainWallObservationSet> Read(CurtainWallReadRequest request)
    {
        if (request is null) throw new ArgumentNullException(nameof(request));

        if (!(_document.GetElement(request.AreaPlanUniqueId) is ViewPlan view) ||
            view.ViewType != ViewType.AreaPlan || view.GenLevel is null)
        {
            return Result.Failure<CurtainWallObservationSet>(new Error(
                "curtainWall.areaPlanMissing",
                "找不到此檢討封包的 Area Plan 或其樓層，無法解析帷幕牆交接處，請重新執行專案設定。",
                "areaPlanUniqueId=" + request.AreaPlanUniqueId));
        }

        var level = view.GenLevel;
        var warnings = new List<string>();
        var zones = ReadZones(request.PackageId, request.AreaPlanUniqueId);
        var hosts = request.RequiredFireRatingMinutes.Keys
            .Concat(request.LegalReferences.Keys)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (hosts.Count == 0)
            warnings.Add("呼叫端未指定任何區劃牆或區劃樓地板，本次僅讀取帷幕牆本身，未產生交接處。");

        // The storey, widened by the reach of a 90 cm band, so a spandrel is never cut short by where
        // the read stopped instead of by the façade. A full-height curtain wall is read in every
        // storey it passes through, and the panels within the widened range are handed over — the
        // top row of the storey below and the bottom row of the one above included. Those are there
        // for the bands to measure; the resolver answers 第79條之4 only for this storey's own panels.
        var margin = PlanUnits.MillimetersToFeet(request.Options.MinFireRatedRunMm);
        var storeyElevations = StoreyElevations(level);
        var storey = (Bottom: level.Elevation - margin, Top: NextLevelElevation(level, storeyElevations) + margin);

        var curtainWalls = new List<CurtainWallObservation>();
        foreach (var wall in Collect<Wall>(BuiltInCategory.OST_Walls).Where(w => w.CurtainGrid is not null))
            curtainWalls.AddRange(ReadCurtainWall(wall, request, storey, warnings));

        foreach (var system in CollectOfClass<CurtainSystem>())
        {
            var observation = ReadCurtainSystem(system, storey, warnings);
            if (observation is not null) curtainWalls.Add(observation);
        }

        var compartmentWalls = new List<CompartmentWallObservation>();
        var compartmentFloors = new List<CompartmentFloorObservation>();
        foreach (var uniqueId in hosts)
        {
            switch (_document.GetElement(uniqueId))
            {
                case Wall wall when wall.CurtainGrid is null:
                    var read = ReadCompartmentWall(wall, request, warnings);
                    if (read is not null) compartmentWalls.Add(read);
                    break;
                case Element floor when floor.Category?.BuiltInCategory == BuiltInCategory.OST_Floors:
                    compartmentFloors.Add(ReadCompartmentFloor(floor, request, warnings));
                    break;
                case null:
                    warnings.Add($"呼叫端指定的區劃元素（UniqueId {uniqueId}）已不在模型中，已略過。");
                    break;
                default:
                    warnings.Add($"呼叫端指定的區劃元素（UniqueId {uniqueId}）不是牆或樓板，已略過。");
                    break;
            }
        }

        var levels = storeyElevations.Select(PlanUnits.FeetToMillimeters).ToList();

        return Result.Success(new CurtainWallObservationSet(
            request.PackageId,
            level.UniqueId,
            level.Name,
            PlanUnits.FeetToMillimeters(level.Elevation),
            zones,
            curtainWalls,
            compartmentWalls,
            compartmentFloors,
            ReadFacadeWalls(curtainWalls, request, storey, warnings),
            levels,
            warnings));
    }

    // --- zones -------------------------------------------------------------------------------

    private List<CurtainWallZoneObservation> ReadZones(Guid packageId, string areaPlanUniqueId) =>
        new RevitWrittenZoneReader(_document)
            .Read(packageId, areaPlanUniqueId)
            .GroupBy(a => a.ZoneId)
            .Select(g => new CurtainWallZoneObservation(
                g.Key,
                g.Select(a => a.ZoneName).FirstOrDefault(n => !string.IsNullOrWhiteSpace(n)),
                g.SelectMany(a => a.BoundaryLoops).Select(ToMillimeters)))
            .ToList();

    // --- curtain walls -----------------------------------------------------------------------

    private IEnumerable<CurtainWallObservation> ReadCurtainWall(
        Wall wall,
        CurtainWallReadRequest request,
        (double Bottom, double Top) storey,
        List<string> warnings)
    {
        if (!(wall.Location is LocationCurve location) || location.Curve is null)
        {
            warnings.Add($"帷幕牆（Id {wall.Id}）沒有可讀的定位線，已略過。");
            return Array.Empty<CurtainWallObservation>();
        }

        var box = wall.get_BoundingBox(null);
        if (box is null)
        {
            warnings.Add($"帷幕牆（Id {wall.Id}）沒有可讀的範圍，已略過。");
            return Array.Empty<CurtainWallObservation>();
        }

        if (box.Max.Z <= storey.Bottom || box.Min.Z >= storey.Top) return Array.Empty<CurtainWallObservation>();

        // An arc wall is flat between its vertical grid lines, so it is read as those planes (docs §4.7).
        if (location.Curve is Arc arc) return ReadArcCurtainWall(wall, arc, box, request, storey, warnings);

        var single = ReadStraightCurtainWall(wall, location.Curve, box, request, storey, warnings);
        return single is null ? Array.Empty<CurtainWallObservation>() : new[] { single };
    }

    private CurtainWallObservation? ReadStraightCurtainWall(
        Wall wall,
        Curve curve,
        BoundingBoxXYZ box,
        CurtainWallReadRequest request,
        (double Bottom, double Top) storey,
        List<string> warnings)
    {
        // Only a straight wall can be described by one line and one normal, and only an arc by the
        // planes between its grid lines; anything else goes to 人工覆核 rather than being flattened
        // into a measurement of the wrong thing (docs §9).
        var reason = curve is Line ? null : "為非直線、非圓弧的帷幕牆（例如橢圓或不規則曲線），本工具只支援直線與圓弧帷幕牆";
        var start = curve.GetEndPoint(0);
        var end = curve.GetEndPoint(1);

        var observation = Build(
            wall.UniqueId,
            start,
            end,
            wall.Orientation,
            wall.Width / 2.0,
            box,
            reason,
            (wall.WallType as ElementType)?.Name,
            warnings,
            RevitWallFunctionReader.Of(wall.WallType));

        if (observation is null || reason is not null) return observation;

        return new CurtainWallObservation(
            observation.UniqueId, observation.Start, observation.End, observation.ExteriorNormal,
            observation.ExteriorOffsetMm, observation.BaseElevationMm, observation.TopElevationMm,
            ReadPanels(wall.CurtainGrid, observation, request, storey, warnings),
            ReadGridLines(wall.CurtainGrid, observation),
            null,
            observation.TypeName,
            functionDeclaration: observation.FunctionDeclaration);
    }

    /// <summary>
    /// An arc curtain wall as the flat facets Revit actually builds it from: one between each pair of
    /// neighbouring vertical grid lines (and the wall's two ends), each with the chord through it as
    /// its location line and only the panels standing on it (docs §4.7). The facets share the wall's
    /// UniqueId; the resolver puts them back together.
    /// </summary>
    /// <remarks>
    /// The outward normal of a facet is its chord turned −90° about Z — the same rule Revit's
    /// <c>Wall.Orientation</c> follows for a straight wall, and as little to be trusted: the resolver
    /// re-orients every facet from the side its 區劃 lies on (docs §4.6).
    /// </remarks>
    private IEnumerable<CurtainWallObservation> ReadArcCurtainWall(
        Wall wall,
        Arc arc,
        BoundingBoxXYZ box,
        CurtainWallReadRequest request,
        (double Bottom, double Top) storey,
        List<string> warnings)
    {
        var grid = wall.CurtainGrid;
        var sweep = Sweep(arc);

        // 段的分界：牆的兩端，加上每一條直向 grid line 在定位線上的位置。方向由線本身判斷，不看 U／V。
        var cuts = new List<double> { 0.0, sweep };
        foreach (var id in grid.GetUGridLineIds().Concat(grid.GetVGridLineIds()))
        {
            if (!(_document.GetElement(id) is CurtainGridLine line) || line.FullCurve is null || !IsVertical(line.FullCurve)) continue;
            var angle = AngleAlong(arc, line.FullCurve.GetEndPoint(0));
            if (angle > SnapAngle(arc) && angle < sweep - SnapAngle(arc)) cuts.Add(angle);
        }

        var angles = cuts.OrderBy(a => a).ToList();

        // 嵌板一律依中心落在哪一段歸屬；落不到任何一段的不會被讀到，所以要講出來，不默默丟掉。
        var stray = grid.GetPanelIds()
            .Select(_document.GetElement)
            .Where(e => e is not null && e.Category?.BuiltInCategory != BuiltInCategory.OST_CurtainWallMullions)
            .Count(e => !IsOnFacet(arc, e!, angles[0] - SnapAngle(arc), angles[angles.Count - 1]));
        if (stray > 0)
            warnings.Add($"弧形帷幕牆（Id {wall.Id}）有 {stray} 片嵌板的中心不在任何一段上，未列入檢討，請確認該牆的嵌板。");

        var facets = new List<CurtainWallObservation>();
        var offsetFeet = 0.0;

        for (var i = 0; i + 1 < angles.Count; i++)
        {
            var low = angles[i];
            var high = angles[i + 1];
            if (high - low <= SnapAngle(arc)) continue;

            var start = PointAt(arc, low);
            var end = PointAt(arc, high);
            var direction = (end - start).Normalize();
            var index = facets.Count;

            CurtainWallObservation facet;
            try
            {
                facet = new CurtainWallObservation(
                    wall.UniqueId,
                    ToMillimeters(start),
                    ToMillimeters(end),
                    new Point2D(direction.Y, -direction.X),
                    PlanUnits.FeetToMillimeters(wall.Width / 2.0),
                    PlanUnits.FeetToMillimeters(box.Min.Z),
                    PlanUnits.FeetToMillimeters(box.Max.Z),
                    typeName: (wall.WallType as ElementType)?.Name,
                    facetIndex: index,
                    facetOffsetMm: PlanUnits.FeetToMillimeters(offsetFeet),
                    functionDeclaration: RevitWallFunctionReader.Of(wall.WallType));
            }
            catch (Exception exception) when (exception is ArgumentException || exception is ArgumentOutOfRangeException)
            {
                warnings.Add($"弧形帷幕牆（Id {wall.Id}）第 {index + 1} 段的幾何無法解析（{exception.Message}），整道牆已略過。");
                return Array.Empty<CurtainWallObservation>();
            }

            offsetFeet += start.DistanceTo(end);
            facets.Add(new CurtainWallObservation(
                facet.UniqueId, facet.Start, facet.End, facet.ExteriorNormal,
                facet.ExteriorOffsetMm, facet.BaseElevationMm, facet.TopElevationMm,
                ReadPanels(grid, facet, request, storey, warnings, e => IsOnFacet(arc, e, low, high)),
                ReadGridLines(grid, facet, p => IsOnFacet(arc, p, low, high)),
                null,
                facet.TypeName,
                facet.FacetIndex,
                facet.FacetOffsetMm,
                facet.FunctionDeclaration));
        }

        return facets;
    }

    /// <summary>How far round the arc, in radians from its start, the plan point <paramref name="point"/> lies; in [0, 2π).</summary>
    private static double AngleAlong(Arc arc, XYZ point)
    {
        var v = new XYZ(point.X - arc.Center.X, point.Y - arc.Center.Y, 0.0);
        var start = arc.GetEndPoint(0) - arc.Center;
        var angle = Math.Atan2(v.DotProduct(arc.YDirection), v.DotProduct(arc.XDirection)) -
                    Math.Atan2(start.DotProduct(arc.YDirection), start.DotProduct(arc.XDirection));
        while (angle < 0) angle += 2.0 * Math.PI;
        while (angle >= 2.0 * Math.PI) angle -= 2.0 * Math.PI;
        return angle;
    }

    private static double Sweep(Arc arc)
    {
        var sweep = AngleAlong(arc, arc.GetEndPoint(1));
        return sweep <= 0.0 ? 2.0 * Math.PI : sweep;
    }

    /// <summary>The plan point <paramref name="angle"/> radians round the arc from its start.</summary>
    private static XYZ PointAt(Arc arc, double angle)
    {
        var start = arc.GetEndPoint(0) - arc.Center;
        var origin = Math.Atan2(start.DotProduct(arc.YDirection), start.DotProduct(arc.XDirection)) + angle;
        return arc.Center + (arc.XDirection * (arc.Radius * Math.Cos(origin))) + (arc.YDirection * (arc.Radius * Math.Sin(origin)));
    }

    /// <summary>The angle a <see cref="SnapFeet"/> step along the arc subtends.</summary>
    private static double SnapAngle(Arc arc) => SnapFeet / arc.Radius;

    private static bool IsOnFacet(Arc arc, XYZ point, double from, double to)
    {
        var angle = AngleAlong(arc, point);
        return angle >= from - SnapAngle(arc) && angle <= to + SnapAngle(arc);
    }

    /// <summary>A panel stands on the facet its centre falls on — a flat panel's centre is on its chord.</summary>
    private static bool IsOnFacet(Arc arc, Element element, double from, double to)
    {
        var box = element.get_BoundingBox(null);
        var centre = element.Location is LocationPoint point
            ? point.Point
            : box is null ? null : (box.Min + box.Max) / 2.0;
        if (centre is null) return false;

        var angle = AngleAlong(arc, centre);
        return angle > from && angle <= to;
    }

    private static bool IsVertical(Curve curve)
    {
        var from = curve.GetEndPoint(0);
        var to = curve.GetEndPoint(1);
        return Math.Abs(to.Z - from.Z) > new XYZ(to.X - from.X, to.Y - from.Y, 0).GetLength();
    }

    /// <summary>
    /// The elevations of the levels that are building storeys (Revit 的「建築樓層」), in feet, plus this
    /// package's own level whatever it is flagged. A reference level — 結構 SL, 天花, 女兒牆頂 — is not a
    /// storey and has no package: counted as one, the panels between it and the storey above would
    /// belong to neither package and 第79條之4 would never be answered for them.
    /// </summary>
    private IReadOnlyList<double> StoreyElevations(Level level) =>
        new FilteredElementCollector(_document)
            .OfClass(typeof(Level))
            .Cast<Level>()
            .Where(l => l.Id == level.Id || IsBuildingStorey(l))
            .Select(l => l.Elevation)
            .ToList();

    private static bool IsBuildingStorey(Level level) =>
        level.get_Parameter(BuiltInParameter.LEVEL_IS_BUILDING_STORY)?.AsInteger() != 0;

    private static double NextLevelElevation(Level level, IReadOnlyList<double> storeyElevations) =>
        storeyElevations
            .Where(e => e > level.Elevation + SnapFeet)
            .DefaultIfEmpty(level.Elevation + PlanUnits.MillimetersToFeet(4000.0))
            .Min();

    /// <summary>
    /// A Curtain System is a curtain wall too (docs §4.1), but it is placed on faces and has no
    /// location line, so this version cannot measure one. It is still reported — as a wall whose
    /// plane could not be read — so it reaches 人工覆核 instead of disappearing from the review.
    /// </summary>
    private CurtainWallObservation? ReadCurtainSystem(CurtainSystem system, (double Bottom, double Top) storey, List<string> warnings)
    {
        var box = system.get_BoundingBox(null);
        if (box is null) return null;
        if (box.Max.Z <= storey.Bottom || box.Min.Z >= storey.Top) return null;

        var start = new XYZ(box.Min.X, box.Min.Y, box.Min.Z);
        var end = new XYZ(box.Max.X, box.Max.Y, box.Min.Z);
        if (start.DistanceTo(end) <= SnapFeet) return null;

        var direction = (end - start).Normalize();
        return Build(
            system.UniqueId,
            start,
            end,
            new XYZ(direction.Y, -direction.X, 0),
            0.0,
            box,
            "為帷幕系統（Curtain System），本版無法解析其定位面",
            _document.GetElement(system.GetTypeId())?.Name,
            warnings);
    }

    private static CurtainWallObservation? Build(
        string uniqueId,
        XYZ start,
        XYZ end,
        XYZ orientation,
        double offsetFeet,
        BoundingBoxXYZ box,
        string? nonPlanarReason,
        string? typeName,
        List<string> warnings,
        CurtainWallFunctionDeclaration functionDeclaration = CurtainWallFunctionDeclaration.NotRead)
    {
        try
        {
            return new CurtainWallObservation(
                uniqueId,
                ToMillimeters(start),
                ToMillimeters(end),
                new Point2D(orientation.X, orientation.Y),
                PlanUnits.FeetToMillimeters(offsetFeet),
                PlanUnits.FeetToMillimeters(box.Min.Z),
                PlanUnits.FeetToMillimeters(box.Max.Z),
                nonPlanarReason: nonPlanarReason,
                typeName: typeName,
                functionDeclaration: functionDeclaration);
        }
        catch (Exception exception) when (exception is ArgumentException || exception is ArgumentOutOfRangeException)
        {
            warnings.Add($"帷幕牆（UniqueId {uniqueId}）的幾何無法解析（{exception.Message}），已略過。");
            return null;
        }
    }

    // --- panels ------------------------------------------------------------------------------

    private List<CurtainPanelObservation> ReadPanels(
        CurtainGrid grid,
        CurtainWallObservation wall,
        CurtainWallReadRequest request,
        (double Bottom, double Top) storey,
        List<string> warnings,
        Func<Element, bool>? belongs = null)
    {
        var panels = new List<CurtainPanelObservation>();
        foreach (var id in grid.GetPanelIds())
        {
            var element = _document.GetElement(id);
            if (element is null) continue;

            // 弧形帷幕牆的一段只收站在它上面的嵌板（docs §4.7）。
            if (belongs is not null && !belongs(element)) continue;

            // 貼在讀取範圍邊界上的嵌板要留下來，因為它就是收邊的那一片。外擴量是 MinFireRatedRunMm
            // （900 mm），與 90 cm 帶的高度同一個數：一道貼齊樓層底面的 900 mm 帶，其下方收邊嵌板的
            // 頂端必然正好落在 storey.Bottom 上。沒有公差的話這是一個浮點數等值比較，層間帶看不看得見
            // 取決於進位。
            var box = element.get_BoundingBox(null);
            if (box is not null &&
                (box.Max.Z <= storey.Bottom - SnapFeet || box.Min.Z >= storey.Top + SnapFeet)) continue;

            // 豎框不判定：a mullion carries no rating of its own, so it is never read (docs §4.1).
            if (element.Category?.BuiltInCategory == BuiltInCategory.OST_CurtainWallMullions) continue;

            var extent = Extent(element, wall);
            if (extent is null)
            {
                warnings.Add($"帷幕嵌板（Id {element.Id}）的尺寸無法讀取，該處的交接帶將判為資料不足。");
                continue;
            }

            var type = _document.GetElement(element.GetTypeId());

            // 佔位嵌板（System Panel : Wall）：Revit 把型別參數全設成唯讀，格子裡也沒有任何量體，
            // 所以構造只能問帷幕牆型別的 Curtain Panel 指到的那個牆型別——模型裡唯一一句關於這些
            // 格子是什麼構造的宣告（決議 16、步驟 16h）。逐格判斷：被個別改過型別的那一格走自己的路。
            var source = RevitReservedPanelType.SourceWallTypeOf(_document, element, type as ElementType);
            var declaring = source ?? type;

            var rating = ReviewInputAssembler.Rating(
                RevitReviewParameterReader.ReadingOf(declaring?.LookupParameter(FireRatingParameters.Provided)),
                request.BareNumberUnit);

            var isOpening = element.Category?.BuiltInCategory is BuiltInCategory.OST_Doors or BuiltInCategory.OST_Windows;

            // 防火保護每一片嵌板都讀，不再是門窗專屬（決議 16）：一片玻璃嵌板就是以 防火檢討_設計防火保護
            // 回答第79條之4，不讀它等於讓每一片玻璃嵌板永遠資料不足。門窗的 IsUnprotectedOpening 仍然
            // 只看門窗，所以 CW-H、CW-V 的連續段判定不受這一行影響。
            var protection = ReviewInputAssembler.Protection(
                RevitReviewParameterReader.ReadingOf(type?.LookupParameter(FireProtectionParameters.Provided)));

            // 種類的四個事實交給 Application 層判：類別是不是門窗、這片嵌板本身是不是一道牆、型別參數
            // 寫了什麼、有沒有來源牆型別接手。述詞只有一份，讀取層不自己另訂一套（決議 16、步驟 16c、16h）。
            var kind = CurtainPanelTypeSubstitution.Kind(
                isOpening,
                element is Wall,
                Text(type, CurtainPanelKindParameters.Provided),
                source is not null);

            panels.Add(new CurtainPanelObservation(
                element.UniqueId,
                extent.Value.StartMm, extent.Value.EndMm, extent.Value.BottomMm, extent.Value.TopMm,
                rating, isOpening, protection, type?.UniqueId, type?.Name, kind));
        }

        return panels;
    }

    /// <summary>
    /// Where a panel sits on the curtain wall's own plane. A panel that is a wall is measured from
    /// its location line, everything else from its width and height about its centre — the bounding
    /// box would overstate both on a wall that does not run along an axis.
    /// </summary>
    private (double StartMm, double EndMm, double BottomMm, double TopMm)? Extent(Element element, CurtainWallObservation wall)
    {
        var box = element.get_BoundingBox(null);

        if (element is Wall panelWall && panelWall.Location is LocationCurve location && location.Curve is not null && box is not null)
        {
            var from = wall.ParameterOf(ToMillimeters(location.Curve.GetEndPoint(0)));
            var to = wall.ParameterOf(ToMillimeters(location.Curve.GetEndPoint(1)));
            return Ordered(from, to, PlanUnits.FeetToMillimeters(box.Min.Z), PlanUnits.FeetToMillimeters(box.Max.Z));
        }

        var centre = element.Location is LocationPoint point
            ? point.Point
            : box is null ? null : (box.Min + box.Max) / 2.0;
        if (centre is null) return null;

        var width = Size(element, BuiltInParameter.CURTAIN_WALL_PANELS_WIDTH, BuiltInParameter.FAMILY_WIDTH_PARAM, BuiltInParameter.DOOR_WIDTH, BuiltInParameter.WINDOW_WIDTH);
        var height = Size(element, BuiltInParameter.CURTAIN_WALL_PANELS_HEIGHT, BuiltInParameter.FAMILY_HEIGHT_PARAM, BuiltInParameter.DOOR_HEIGHT, BuiltInParameter.WINDOW_HEIGHT);
        if (width is null || height is null)
        {
            if (box is null) return null;
            var lowerLeft = wall.ParameterOf(ToMillimeters(box.Min));
            var upperRight = wall.ParameterOf(ToMillimeters(box.Max));
            return Ordered(lowerLeft, upperRight, PlanUnits.FeetToMillimeters(box.Min.Z), PlanUnits.FeetToMillimeters(box.Max.Z));
        }

        var along = wall.ParameterOf(ToMillimeters(centre));
        var elevation = PlanUnits.FeetToMillimeters(centre.Z);
        var halfWidth = PlanUnits.FeetToMillimeters(width.Value) / 2.0;
        var halfHeight = PlanUnits.FeetToMillimeters(height.Value) / 2.0;
        return Ordered(along - halfWidth, along + halfWidth, elevation - halfHeight, elevation + halfHeight);
    }

    private static (double StartMm, double EndMm, double BottomMm, double TopMm)? Ordered(
        double first, double second, double bottom, double top)
    {
        var startMm = Math.Min(first, second);
        var endMm = Math.Max(first, second);
        return endMm - startMm <= CurtainPanelObservation.TouchToleranceMm ||
               top - bottom <= CurtainPanelObservation.TouchToleranceMm
            ? (ValueTuple<double, double, double, double>?)null
            : (startMm, endMm, bottom, top);
    }

    /// <summary>
    /// A text Type parameter as it stands, or null when the Type does not carry it or holds nothing.
    /// 「沒有綁這個參數」與「綁了但空白」對種類是同一件事——都是未宣告，所以兩者不必分開。
    /// </summary>
    private static string? Text(Element? element, string name)
    {
        var parameter = element?.LookupParameter(name);
        return parameter is not null && parameter.HasValue && parameter.StorageType == StorageType.String
            ? parameter.AsString()
            : null;
    }

    private double? Size(Element element, params BuiltInParameter[] parameters)
    {
        var type = _document.GetElement(element.GetTypeId());
        foreach (var name in parameters)
        {
            foreach (var owner in new[] { element, type })
            {
                var parameter = owner?.get_Parameter(name);
                if (parameter is not null && parameter.StorageType == StorageType.Double &&
                    parameter.HasValue && parameter.AsDouble() > 0)
                    return parameter.AsDouble();
            }
        }

        return null;
    }

    // --- grid lines --------------------------------------------------------------------------

    /// <summary>
    /// The grid lines, placed on the wall's plane. Direction comes from the line itself rather than
    /// from whether Revit calls it U or V, so a wall built either way reads the same.
    /// </summary>
    /// <remarks>
    /// <paramref name="keepVertical"/> is asked of a vertical line's foot: an arc wall's facet keeps only
    /// the two at its own ends, a horizontal line belongs to every facet (docs §4.7).
    /// </remarks>
    private List<CurtainGridLineObservation> ReadGridLines(CurtainGrid grid, CurtainWallObservation wall, Func<XYZ, bool>? keepVertical = null)
    {
        var lines = new List<CurtainGridLineObservation>();
        foreach (var id in grid.GetUGridLineIds().Concat(grid.GetVGridLineIds()))
        {
            if (!(_document.GetElement(id) is CurtainGridLine line) || line.FullCurve is null) continue;

            var from = line.FullCurve.GetEndPoint(0);
            var to = line.FullCurve.GetEndPoint(1);
            var rise = Math.Abs(to.Z - from.Z);
            var run = new XYZ(to.X - from.X, to.Y - from.Y, 0).GetLength();
            if (rise > run && keepVertical is not null && !keepVertical(from)) continue;

            lines.Add(rise > run
                ? new CurtainGridLineObservation(line.UniqueId, CurtainGridLineDirection.Vertical, wall.ParameterOf(ToMillimeters(from)))
                : new CurtainGridLineObservation(line.UniqueId, CurtainGridLineDirection.Horizontal, PlanUnits.FeetToMillimeters(from.Z)));
        }

        return lines;
    }

    // --- 立面內的實體外牆（docs §4.2「交接處之外牆面」、決議 13）---------------------------------

    /// <summary>
    /// The solid exterior walls standing in the curtain walls' own planes: what supplies the 900 mm of
    /// 交接處之外牆面 the 但書 of 第79條第3項 asks for. A non-curtain straight wall whose location line
    /// lies in some curtain wall's plane and whose elevations reach into the read range, with its
    /// type's 設計防火時效 as it reads.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A wall that is both a 區劃牆 and a solid exterior wall is recorded in both lists: the two roles
    /// do not exclude each other, and a 區劃牆 lying along the façade really is a piece of it. The
    /// ordinary 區劃牆 runs perpendicular to the elevation and therefore never gets in here — which is
    /// the point of 決議 13, because answering the 但書 with the compartment wall's own rating would let
    /// any rated wall butted against glazing exempt itself and hollow out 第79條第4項.
    /// </para>
    /// <para>
    /// <see cref="CurtainWallObservation.IsInFacadePlane"/> is the prefilter here and the association
    /// in the resolver — one predicate, so the adapter cannot hand over a wall the resolver then
    /// refuses to look at, or drop one it would have counted.
    /// </para>
    /// </remarks>
    private List<FacadeWallObservation> ReadFacadeWalls(
        IReadOnlyList<CurtainWallObservation> curtainWalls,
        CurtainWallReadRequest request,
        (double Bottom, double Top) storey,
        List<string> warnings)
    {
        var facades = new List<FacadeWallObservation>();
        var planes = curtainWalls.Where(c => c.IsPlanar).ToList();
        if (planes.Count == 0) return facades;

        // 帷幕牆型別把 Curtain Panel 指到一個牆型別時，Revit 有兩種放法，兩種都要排掉：格子裡放一片
        // 真的 Wall（「嵌板為牆」，沒有 CurtainGrid，必然躺在帷幕牆的定位面內），或放一片沒有量體的
        // 保留型別嵌板（System Panel : Wall，步驟 16h）。不排掉，前者會同時以嵌板和實體外牆兩個身分
        // 出現，每個交接處都自己跟自己重疊而判人工覆核。下面用 UniqueId 集合排除，所以兩種都涵蓋——
        // 保留型別那一種本來就不是 Wall，走不到這個迴圈，集合比對仍然成立。
        var panelWalls = CurtainPanelUniqueIds();

        foreach (var wall in Collect<Wall>(BuiltInCategory.OST_Walls).Where(w => w.CurtainGrid is null))
        {
            if (panelWalls.Contains(wall.UniqueId)) continue;
            if (!(wall.Location is LocationCurve location) || !(location.Curve is Line line)) continue;

            var box = wall.get_BoundingBox(null);
            if (box is null) continue;
            if (box.Max.Z <= storey.Bottom || box.Min.Z >= storey.Top) continue;

            var start = ToMillimeters(line.GetEndPoint(0));
            var end = ToMillimeters(line.GetEndPoint(1));
            if (!planes.Any(c => c.IsInFacadePlane(start, end))) continue;

            var type = wall.WallType as ElementType;
            var rating = ReviewInputAssembler.Rating(
                RevitReviewParameterReader.ReadingOf(type?.LookupParameter(FireRatingParameters.Provided)),
                request.BareNumberUnit);

            try
            {
                facades.Add(new FacadeWallObservation(
                    wall.UniqueId,
                    start,
                    end,
                    PlanUnits.FeetToMillimeters(box.Min.Z),
                    PlanUnits.FeetToMillimeters(box.Max.Z),
                    type?.Name,
                    rating));
            }
            catch (ArgumentException exception)
            {
                warnings.Add($"立面內的實體外牆（Id {wall.Id}）的幾何無法解析（{exception.Message}），已略過，該處交接帶將不計入其長度。");
            }
        }

        return facades;
    }

    /// <summary>
    /// Every element that is a panel of some curtain grid, whatever storey it is on: the set a wall
    /// has to stay out of to count as a solid exterior wall of its own. Read from the grids rather
    /// than from what this run observed, because a panel skipped for any reason — outside the read
    /// range, unreadable size — is still a panel and must not come back as a façade wall.
    /// </summary>
    private HashSet<string> CurtainPanelUniqueIds()
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);

        foreach (var grid in Collect<Wall>(BuiltInCategory.OST_Walls)
                     .Select(w => w.CurtainGrid)
                     .Where(g => g is not null)
                     .Concat(CollectOfClass<CurtainSystem>().SelectMany(Grids)))
        {
            foreach (var id in grid!.GetPanelIds())
            {
                var element = _document.GetElement(id);
                if (element is not null) ids.Add(element.UniqueId);
            }
        }

        return ids;
    }

    private static IEnumerable<CurtainGrid> Grids(CurtainSystem system)
    {
        foreach (CurtainGrid grid in system.CurtainGrids)
            if (grid is not null) yield return grid;
    }

    // --- compartment boundaries ----------------------------------------------------------------

    private CompartmentWallObservation? ReadCompartmentWall(Wall wall, CurtainWallReadRequest request, List<string> warnings)
    {
        if (!(wall.Location is LocationCurve location) || !(location.Curve is Line line))
        {
            warnings.Add($"區劃牆（Id {wall.Id}）不是直線牆，無法求其與帷幕牆的交點，已略過。");
            return null;
        }

        var box = wall.get_BoundingBox(null);
        if (box is null)
        {
            warnings.Add($"區劃牆（Id {wall.Id}）沒有可讀的範圍，已略過。");
            return null;
        }

        // 這道牆自己的設計防火時效不讀：CW-H 的但書只認立面內的實體外牆（決議 13）。躺在立面上的
        // 區劃牆會由 ReadFacadeWalls 另外登錄一次，時效隨那個身分讀。
        return new CompartmentWallObservation(
            wall.UniqueId,
            ToMillimeters(line.GetEndPoint(0)),
            ToMillimeters(line.GetEndPoint(1)),
            PlanUnits.FeetToMillimeters(box.Min.Z),
            PlanUnits.FeetToMillimeters(box.Max.Z),
            request.LegalReferenceOf(wall.UniqueId),
            request.RequiredRatingOf(wall.UniqueId),
            (wall.WallType as ElementType)?.Name);
    }

    private CompartmentFloorObservation ReadCompartmentFloor(Element floor, CurtainWallReadRequest request, List<string> warnings)
    {
        var box = floor.get_BoundingBox(null);
        var elevationFeet = box?.Max.Z ?? 0.0;
        IReadOnlyList<IReadOnlyList<Point2D>> outlines;

        try
        {
            outlines = RevitPlanShapeReader.ReadPlanOutlines(floor, box?.Min.Z ?? 0.0);
        }
        catch (Exception exception) when (exception is Autodesk.Revit.Exceptions.ApplicationException ||
                                          exception is InvalidOperationException)
        {
            outlines = Array.Empty<IReadOnlyList<Point2D>>();
        }

        if (outlines.Count == 0)
        {
            var rectangle = RevitPlanShapeReader.ReadBoundingRectangle(floor);
            if (rectangle is not null)
            {
                outlines = new[] { rectangle };
                warnings.Add($"區劃樓地板（Id {floor.Id}）的平面輪廓無法分析，已改用外接矩形量測層間帶，請確認突出量。");
            }
        }

        return new CompartmentFloorObservation(
            floor.UniqueId,
            outlines.Select(ToMillimeters),
            PlanUnits.FeetToMillimeters(elevationFeet),
            request.RequiredRatingOf(floor.UniqueId),
            _document.GetElement(floor.GetTypeId())?.Name);
    }

    // --- plumbing ----------------------------------------------------------------------------

    private IEnumerable<T> Collect<T>(BuiltInCategory category) where T : Element =>
        new FilteredElementCollector(_document)
            .OfCategory(category)
            .WhereElementIsNotElementType()
            .OfType<T>()
            .Where(IsInPrimaryDesign);

    private IEnumerable<T> CollectOfClass<T>() where T : Element =>
        new FilteredElementCollector(_document)
            .OfClass(typeof(T))
            .WhereElementIsNotElementType()
            .OfType<T>()
            .Where(IsInPrimaryDesign);

    // Secondary design options are alternatives, not the building under review (spec 11.1).
    private static bool IsInPrimaryDesign(Element element)
    {
        var option = element.DesignOption;
        return option is null || option.IsPrimary;
    }

    private static Point2D ToMillimeters(XYZ point) =>
        new(PlanUnits.FeetToMillimeters(point.X), PlanUnits.FeetToMillimeters(point.Y));

    private static IReadOnlyList<Point2D> ToMillimeters(IReadOnlyList<Point2D> loop) =>
        loop.Select(p => new Point2D(PlanUnits.FeetToMillimeters(p.X), PlanUnits.FeetToMillimeters(p.Y))).ToList();
}
