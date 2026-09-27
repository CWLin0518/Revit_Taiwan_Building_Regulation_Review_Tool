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
            var rating = ReviewInputAssembler.Rating(
                RevitReviewParameterReader.ReadingOf(type?.LookupParameter(FireRatingParameters.Provided)),
                request.BareNumberUnit);

            var isOpening = element.Category?.BuiltInCategory is BuiltInCategory.OST_Doors or BuiltInCategory.OST_Windows;

            // 防火保護每一片嵌板都讀，不再是門窗專屬（決議 16）：一片玻璃嵌板就是以 防火檢討_設計防火保護
            // 回答第79條之4，不讀它等於讓每一片玻璃嵌板永遠資料不足。門窗的 IsUnprotectedOpening 仍然
            // 只看門窗，所以 CW-H、CW-V 的連續段判定不受這一行影響。
            var protection = ReviewInputAssembler.Protection(
                RevitReviewParameterReader.ReadingOf(type?.LookupParameter(FireProtectionParameters.Provided)));

            // 種類的三個事實交給 Application 層判：類別是不是門窗、這片嵌板本身是不是一道牆、型別參數
            // 寫了什麼。述詞只有一份，讀取層不自己另訂一套（決議 16、步驟 16c）。
            var kind = CurtainPanelKinds.Classify(
                isOpening,
                element is Wall,
                Text(type, CurtainPanelKindParameters.Provided));

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

        // 「嵌板為牆」的嵌板本身就是一片 Wall，沒有 CurtainGrid，而且必然躺在帷幕牆的定位面內——
        // 不排掉它，它會同時以嵌板和實體外牆兩個身分出現，每個交接處都自己跟自己重疊而判人工覆核。
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
