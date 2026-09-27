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
using BuildingRegulationReview.Revit.Reviews;
using BuildingRegulationReview.Revit.WriteBack;

namespace BuildingRegulationReview.Revit.Geometry;

/// <summary>
/// P3 步驟 4: reads the curtain walls of one package's storey — their location line, outward face,
/// panels and grid lines — plus the 區劃牆 and 區劃樓地板 the caller named, and hands it all over as
/// plain data (docs/regulations/curtain-wall-fire-compartment.md §4.1–§4.3). Read-only: no
/// transaction, no change to the model.
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
        // storey it passes through, but only the panels of this one are handed over.
        var margin = PlanUnits.MillimetersToFeet(request.Options.MinFireRatedRunMm);
        var storey = (Bottom: level.Elevation - margin, Top: NextLevelElevation(level) + margin);

        var curtainWalls = new List<CurtainWallObservation>();
        foreach (var wall in Collect<Wall>(BuiltInCategory.OST_Walls).Where(w => w.CurtainGrid is not null))
        {
            var observation = ReadCurtainWall(wall, request, storey, warnings);
            if (observation is not null) curtainWalls.Add(observation);
        }

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

        var levels = new FilteredElementCollector(_document)
            .OfClass(typeof(Level))
            .Cast<Level>()
            .Select(l => PlanUnits.FeetToMillimeters(l.Elevation))
            .ToList();

        return Result.Success(new CurtainWallObservationSet(
            request.PackageId,
            level.UniqueId,
            level.Name,
            PlanUnits.FeetToMillimeters(level.Elevation),
            zones,
            curtainWalls,
            compartmentWalls,
            compartmentFloors,
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

    private CurtainWallObservation? ReadCurtainWall(
        Wall wall,
        CurtainWallReadRequest request,
        (double Bottom, double Top) storey,
        List<string> warnings)
    {
        if (!(wall.Location is LocationCurve location) || location.Curve is null)
        {
            warnings.Add($"帷幕牆（Id {wall.Id}）沒有可讀的定位線，已略過。");
            return null;
        }

        var box = wall.get_BoundingBox(null);
        if (box is null)
        {
            warnings.Add($"帷幕牆（Id {wall.Id}）沒有可讀的範圍，已略過。");
            return null;
        }

        if (box.Max.Z <= storey.Bottom || box.Min.Z >= storey.Top) return null;

        // Only a straight wall can be described by one line and one normal; anything else goes to
        // 人工覆核 rather than being flattened into a measurement of the wrong thing (docs §9).
        var curve = location.Curve;
        var reason = curve is Line ? null : "為弧形或非直線帷幕牆，本工具只支援平面帷幕牆";
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
            warnings);

        if (observation is null || reason is not null) return observation;

        return new CurtainWallObservation(
            observation.UniqueId, observation.Start, observation.End, observation.ExteriorNormal,
            observation.ExteriorOffsetMm, observation.BaseElevationMm, observation.TopElevationMm,
            ReadPanels(wall.CurtainGrid, observation, request, storey, warnings),
            ReadGridLines(wall.CurtainGrid, observation),
            null,
            observation.TypeName);
    }

    private double NextLevelElevation(Level level) =>
        new FilteredElementCollector(_document)
            .OfClass(typeof(Level))
            .Cast<Level>()
            .Select(l => l.Elevation)
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
        List<string> warnings)
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
                typeName: typeName);
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
        List<string> warnings)
    {
        var panels = new List<CurtainPanelObservation>();
        foreach (var id in grid.GetPanelIds())
        {
            var element = _document.GetElement(id);
            if (element is null) continue;

            var box = element.get_BoundingBox(null);
            if (box is not null && (box.Max.Z <= storey.Bottom || box.Min.Z >= storey.Top)) continue;

            // 豎框不判定：a mullion carries no rating of its own, so it is never read (docs §4.1).
            if (element.Category?.BuiltInCategory == BuiltInCategory.OST_CurtainWallMullions) continue;

            var extent = Extent(element, wall);
            if (extent is null)
            {
                warnings.Add($"帷幕嵌板（Id {element.Id}）的尺寸無法讀取，該處的交接帶將判為資料不足。");
                continue;
            }

            var type = _document.GetElement(element.GetTypeId());
            var rating = ReviewInputAssembler.Rating(
                RevitReviewParameterReader.ReadingOf(type?.LookupParameter(FireRatingParameters.Provided)),
                request.BareNumberUnit);

            var isOpening = element.Category?.BuiltInCategory is BuiltInCategory.OST_Doors or BuiltInCategory.OST_Windows;
            var protection = isOpening
                ? ReviewInputAssembler.Protection(
                    RevitReviewParameterReader.ReadingOf(type?.LookupParameter(FireProtectionParameters.Provided)))
                : null;

            panels.Add(new CurtainPanelObservation(
                element.UniqueId,
                extent.Value.StartMm, extent.Value.EndMm, extent.Value.BottomMm, extent.Value.TopMm,
                rating, isOpening, protection, type?.UniqueId, type?.Name));
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
    private List<CurtainGridLineObservation> ReadGridLines(CurtainGrid grid, CurtainWallObservation wall)
    {
        var lines = new List<CurtainGridLineObservation>();
        foreach (var id in grid.GetUGridLineIds().Concat(grid.GetVGridLineIds()))
        {
            if (!(_document.GetElement(id) is CurtainGridLine line) || line.FullCurve is null) continue;

            var from = line.FullCurve.GetEndPoint(0);
            var to = line.FullCurve.GetEndPoint(1);
            var rise = Math.Abs(to.Z - from.Z);
            var run = new XYZ(to.X - from.X, to.Y - from.Y, 0).GetLength();

            lines.Add(rise > run
                ? new CurtainGridLineObservation(line.UniqueId, CurtainGridLineDirection.Vertical, wall.ParameterOf(ToMillimeters(from)))
                : new CurtainGridLineObservation(line.UniqueId, CurtainGridLineDirection.Horizontal, PlanUnits.FeetToMillimeters(from.Z)));
        }

        return lines;
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

        // 型別的設計防火時效：只有這道牆本身就是上下帷幕牆之間那道實體牆防火帶時才用得到（決議 7）。
        var type = wall.WallType as ElementType;
        var providedRating = ReviewInputAssembler.Rating(
            RevitReviewParameterReader.ReadingOf(type?.LookupParameter(FireRatingParameters.Provided)),
            request.BareNumberUnit);

        return new CompartmentWallObservation(
            wall.UniqueId,
            ToMillimeters(line.GetEndPoint(0)),
            ToMillimeters(line.GetEndPoint(1)),
            PlanUnits.FeetToMillimeters(box.Min.Z),
            PlanUnits.FeetToMillimeters(box.Max.Z),
            request.LegalReferenceOf(wall.UniqueId),
            request.RequiredRatingOf(wall.UniqueId),
            type?.Name,
            providedRating);
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
