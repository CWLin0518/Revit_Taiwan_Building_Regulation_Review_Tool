using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using BuildingRegulationReview.Application.Candidates;
using BuildingRegulationReview.Application.Checks;
using BuildingRegulationReview.Application.WriteBack;
using BuildingRegulationReview.Domain.Common;
using BuildingRegulationReview.Domain.Geometry;
using BuildingRegulationReview.Revit.Geometry;
using BuildingRegulationReview.Revit.Parameters;
using BuildingRegulationReview.Revit.WriteBack;
using RevitArea = Autodesk.Revit.DB.Area;

namespace BuildingRegulationReview.Revit.Candidates;

/// <summary>
/// P3-T03: reads what candidate resolution needs from the model — the package's written Areas and
/// the walls, columns, beams, floors and openings of its storey — and hands it over as plain data
/// (<see cref="CandidateObservationSet"/>). Read-only: no transaction, no change to the model.
/// </summary>
/// <remarks>
/// All deciding happens in <see cref="CandidateResolver"/>, which the core tests cover; this class
/// only converts. The storey is the band between the Area Plan's level and the next level up, and
/// the plan search is limited to the zones' extent, so the read scales with the reviewed area rather
/// than the whole model. Linked models are not read: the MVP reviews the host model only (spec 17.1).
/// </remarks>
public sealed class RevitCandidateObservationReader
{
    // Keeps an element that merely stops at a level, such as the wall of the storey below, out of the band.
    private static readonly double StoreySlackFeet = PlanUnits.MillimetersToFeet(50.0);
    private static readonly double SearchMarginFeet = PlanUnits.MillimetersToFeet(1000.0);

    // Used for the storey above the top level, where there is no next level to stop at.
    private static readonly double DefaultStoreyHeightFeet = PlanUnits.MillimetersToFeet(4000.0);

    private static readonly BuiltInCategory[] ColumnCategories =
    {
        BuiltInCategory.OST_Columns,
        BuiltInCategory.OST_StructuralColumns
    };

    private readonly Document _document;

    public RevitCandidateObservationReader(Document document) =>
        _document = document ?? throw new ArgumentNullException(nameof(document));

    public Result<CandidateObservationSet> Read(Guid packageId, string areaPlanUniqueId)
    {
        if (packageId == Guid.Empty) throw new ArgumentException("Package ID cannot be empty.", nameof(packageId));

        if (!(_document.GetElement(areaPlanUniqueId) is ViewPlan view) || view.ViewType != ViewType.AreaPlan || view.GenLevel is null)
        {
            return Result.Failure<CandidateObservationSet>(new Error(
                "candidates.areaPlanMissing",
                "找不到此檢討封包的 Area Plan 或其樓層，無法解析候選元素，請重新執行專案設定。",
                "areaPlanUniqueId=" + areaPlanUniqueId));
        }

        var level = view.GenLevel;
        var warnings = new List<string>();
        var zones = ReadZones(packageId, view);
        var documentId = RevitDocumentIdentity.Of(_document);

        var loops = zones.SelectMany(z => z.Parts).SelectMany(p => p.BoundaryLoops).SelectMany(l => l).ToList();
        if (loops.Count == 0)
        {
            warnings.Add("此 Area Plan 沒有可用的封閉區劃面積，未讀取任何構件；請先完成「建立區劃範圍」。");
            return Result.Success(new CandidateObservationSet(packageId, level.UniqueId, level.Name, zones, warnings: warnings));
        }

        var bottom = level.Elevation + StoreySlackFeet;
        var top = NextLevelElevation(level) - StoreySlackFeet;
        var storey = new BoundingBoxIntersectsFilter(new Outline(
            new XYZ(loops.Min(p => p.X) - SearchMarginFeet, loops.Min(p => p.Y) - SearchMarginFeet, bottom),
            new XYZ(loops.Max(p => p.X) + SearchMarginFeet, loops.Max(p => p.Y) + SearchMarginFeet, Math.Max(top, bottom))));

        var members = new List<MemberObservation>();
        members.AddRange(Collect(storey, BuiltInCategory.OST_Walls).OfType<Wall>().Select(w => ReadWall(w, documentId, warnings)).Where(m => m is not null).Select(m => m!));
        foreach (var category in ColumnCategories)
            members.AddRange(Collect(storey, category).Select(c => ReadColumn(c, documentId, level.Elevation, warnings)));
        members.AddRange(Collect(storey, BuiltInCategory.OST_StructuralFraming).Select(b => ReadBeam(b, documentId)));

        // A floor belongs to the level it is hosted on; its slab sits at, not inside, the storey band.
        members.AddRange(new FilteredElementCollector(_document)
            .OfCategory(BuiltInCategory.OST_Floors)
            .WhereElementIsNotElementType()
            .WherePasses(new ElementLevelFilter(level.Id))
            .Where(IsInPrimaryDesign)
            .Select(f => ReadFloor(f, documentId, warnings)));

        var openings = new List<OpeningObservation>();
        foreach (var category in new[] { BuiltInCategory.OST_Doors, BuiltInCategory.OST_Windows, BuiltInCategory.OST_CurtainWallPanels })
            openings.AddRange(Collect(storey, category).Select(o => ReadOpening(o, documentId, category)).Where(o => o is not null).Select(o => o!));

        return Result.Success(new CandidateObservationSet(packageId, level.UniqueId, level.Name, zones, members, openings, warnings));
    }

    // --- zones -------------------------------------------------------------------------------

    private List<ZoneObservation> ReadZones(Guid packageId, View view)
    {
        var options = new SpatialElementBoundaryOptions();
        var parts = new List<(Guid ZoneId, string Name, ZonePartObservation Part)>();

        foreach (var area in new FilteredElementCollector(_document, view.Id)
                     .OfCategory(BuiltInCategory.OST_Areas)
                     .WhereElementIsNotElementType()
                     .OfType<RevitArea>())
        {
            if (!ManagedElementMark.TryRead(area, out var token, out var signature)) continue;
            if (!ManagedElementKey.TryParse(token, out var key)) continue;
            if (key.PackageId != packageId || key.Kind != ManagedElementKind.Area) continue;
            PlannedElementSignature.TryReadArea(signature, out var name, out _);

            parts.Add((key.ZoneId, name, new ZonePartObservation(
                area.UniqueId,
                RevitWrittenZoneReader.ReadLoops(area, options),
                area.Area > 0 ? area.Area : (double?)null,
                area.Location is LocationPoint location ? RevitPlanShapeReader.ToPlan(location.Point) : (Point2D?)null)));
        }

        return parts
            .GroupBy(p => p.ZoneId)
            .Select(g => new ZoneObservation(g.Key, g.Select(p => p.Name).FirstOrDefault(n => !string.IsNullOrWhiteSpace(n)) ?? string.Empty, g.Select(p => p.Part)))
            .ToList();
    }

    private double NextLevelElevation(Level level)
    {
        var above = new FilteredElementCollector(_document)
            .OfClass(typeof(Level))
            .Cast<Level>()
            .Where(l => l.Elevation > level.Elevation + StoreySlackFeet)
            .Select(l => l.Elevation)
            .DefaultIfEmpty(level.Elevation + DefaultStoreyHeightFeet)
            .Min();
        return above;
    }

    // --- members -----------------------------------------------------------------------------

    private IEnumerable<Element> Collect(ElementFilter storey, BuiltInCategory category) =>
        new FilteredElementCollector(_document)
            .OfCategory(category)
            .WhereElementIsNotElementType()
            .WherePasses(storey)
            .Where(IsInPrimaryDesign);

    // Secondary design options are alternatives, not the building under review (spec 11.1 Design Option).
    private static bool IsInPrimaryDesign(Element element)
    {
        var option = element.DesignOption;
        return option is null || option.IsPrimary;
    }

    private static MemberObservation? ReadWall(Wall wall, string documentId, List<string> warnings)
    {
        // Stacked-wall members report themselves as walls too; the stack itself carries the location.
        if (wall.IsStackedWallMember) return null;

        IReadOnlyList<Point2D> centerline = Array.Empty<Point2D>();
        try
        {
            if (wall.Location is LocationCurve location && location.Curve is not null)
                centerline = RevitPlanShapeReader.Flatten(location.Curve);
        }
        catch (Autodesk.Revit.Exceptions.ApplicationException exception)
        {
            warnings.Add($"牆（Id {wall.Id}）的位置線無法讀取：{exception.Message}");
        }

        var type = wall.WallType;
        return new MemberObservation(
            new CandidateSource(documentId, wall.UniqueId),
            CandidateCategory.Wall,
            centerline: centerline,
            widthFeet: wall.Width > 0 ? wall.Width : (double?)null,
            typeUniqueId: type?.UniqueId,
            typeName: type?.Name,
            isStructural: ReadFlag(wall, BuiltInParameter.WALL_STRUCTURAL_SIGNIFICANT),
            isCurtainWall: type?.Kind == WallKind.Curtain,
            // 只有帷幕牆需要室內外判定，一般牆讀了也沒人問；讀了反而會讓基準指紋因無關的參數變動而過期。
            curtainWallFunction: type?.Kind == WallKind.Curtain
                ? RevitWallFunctionReader.Of(type)
                : CurtainWallFunctionDeclaration.NotRead);
    }

    private MemberObservation ReadColumn(Element column, string documentId, double planeElevationFeet, List<string> warnings)
    {
        var type = _document.GetElement(column.GetTypeId());
        return new MemberObservation(
            new CandidateSource(documentId, column.UniqueId),
            CandidateCategory.Column,
            outlines: ReadOutlines(column, planeElevationFeet, "柱", warnings),
            typeUniqueId: type?.UniqueId,
            typeName: type?.Name,
            isStructural: column.Category?.BuiltInCategory == BuiltInCategory.OST_StructuralColumns);
    }

    private MemberObservation ReadBeam(Element beam, string documentId)
    {
        var type = _document.GetElement(beam.GetTypeId());
        IReadOnlyList<Point2D> centerline = Array.Empty<Point2D>();
        if (beam.Location is LocationCurve location && location.Curve is not null)
            centerline = RevitPlanShapeReader.Flatten(location.Curve);

        return new MemberObservation(
            new CandidateSource(documentId, beam.UniqueId),
            CandidateCategory.StructuralFraming,
            centerline: centerline.Count >= 2 && centerline[0].DistanceTo(centerline[centerline.Count - 1]) > GeometryTolerance.ZeroLengthFeet
                ? centerline
                : Array.Empty<Point2D>(),
            widthFeet: ReadPositiveLength(type, BuiltInParameter.STRUCTURAL_SECTION_COMMON_WIDTH, "b"),
            typeUniqueId: type?.UniqueId,
            typeName: type?.Name,
            isStructural: true);
    }

    private MemberObservation ReadFloor(Element floor, string documentId, List<string> warnings)
    {
        var type = _document.GetElement(floor.GetTypeId());
        var elevation = floor.get_BoundingBox(null)?.Min.Z ?? 0;
        return new MemberObservation(
            new CandidateSource(documentId, floor.UniqueId),
            CandidateCategory.Floor,
            outlines: ReadOutlines(floor, elevation, "樓板", warnings),
            typeUniqueId: type?.UniqueId,
            typeName: type?.Name,
            isStructural: ReadFlag(floor, BuiltInParameter.FLOOR_PARAM_IS_STRUCTURAL));
    }

    private static IReadOnlyList<IReadOnlyList<Point2D>> ReadOutlines(Element element, double planeElevationFeet, string label, List<string> warnings)
    {
        try
        {
            var outlines = RevitPlanShapeReader.ReadPlanOutlines(element, planeElevationFeet);
            if (outlines.Count > 0) return outlines;
        }
        catch (Exception exception) when (exception is Autodesk.Revit.Exceptions.ApplicationException ||
                                          exception is InvalidOperationException)
        {
            // Fall through to the bounding rectangle and say so.
        }

        var rectangle = RevitPlanShapeReader.ReadBoundingRectangle(element);
        if (rectangle is null) return Array.Empty<IReadOnlyList<Point2D>>();

        warnings.Add($"{label}（Id {element.Id}）的平面輪廓無法分析，已改用外接矩形判定空間關係，請確認。");
        return new[] { rectangle };
    }

    // --- openings ----------------------------------------------------------------------------

    private OpeningObservation? ReadOpening(Element element, string documentId, BuiltInCategory category)
    {
        var host = element switch
        {
            Panel panel => panel.Host,
            FamilyInstance instance => instance.Host,
            _ => null
        };

        // A curtain panel only matters as an opening in a wall; panels of curtain systems and roofs are not.
        if (category == BuiltInCategory.OST_CurtainWallPanels && !(host is Wall)) return null;

        var type = _document.GetElement(element.GetTypeId());
        Point2D? location = element.Location is LocationPoint point
            ? RevitPlanShapeReader.ToPlan(point.Point)
            : Centre(element.get_BoundingBox(null));

        return new OpeningObservation(
            new CandidateSource(documentId, element.UniqueId),
            category switch
            {
                BuiltInCategory.OST_Doors => CandidateCategory.Door,
                BuiltInCategory.OST_Windows => CandidateCategory.Window,
                _ => CandidateCategory.CurtainPanel
            },
            host?.UniqueId,
            location,
            widthFeet: ReadOpeningSize(element, type, BuiltInParameter.FAMILY_WIDTH_PARAM, BuiltInParameter.DOOR_WIDTH, BuiltInParameter.WINDOW_WIDTH, BuiltInParameter.CURTAIN_WALL_PANELS_WIDTH),
            heightFeet: ReadOpeningSize(element, type, BuiltInParameter.FAMILY_HEIGHT_PARAM, BuiltInParameter.DOOR_HEIGHT, BuiltInParameter.WINDOW_HEIGHT, BuiltInParameter.CURTAIN_WALL_PANELS_HEIGHT),
            typeUniqueId: type?.UniqueId,
            typeName: type?.Name,
            curtainPanelKind: category == BuiltInCategory.OST_CurtainWallPanels
                ? PanelKindOf(element, type)
                : null);
    }

    /// <summary>
    /// 一片帷幕嵌板的種類，與 <c>RevitCurtainWallGeometryReader.ReadPanels</c> 讀的是同一套述詞與同一組
    /// 輸入（<see cref="CurtainPanelTypeSubstitution.Kind"/>、決議 16、步驟 16h）。室內帷幕牆上的區劃
    /// 邊緣開口要靠它分路：門窗與玻璃答防火保護，實心是構造、開口規則回答不了它（docs §4.8）。
    /// </summary>
    /// <remarks>
    /// 三個輸入都要與幾何讀取層一致，否則同一片嵌板在兩條路上會有兩種種類：
    /// <list type="bullet">
    /// <item><c>isOpening</c> 恆為 false——這個分支只收 <c>OST_CurtainWallPanels</c>，門窗走各自的類別。</item>
    /// <item>宣告讀的是**嵌板自己的型別**，不是來源牆型別。被取代時來源牆型別上不會有
    /// <c>防火檢討_嵌板種類</c>（那個參數只綁 Curtain Panels），而使用者若真在嵌板型別上明寫了種類，
    /// 明寫的那句話比推論可靠（<see cref="CurtainPanelKinds.Classify"/> 的註解）。</item>
    /// <item>佔位嵌板（保留型別、參數唯讀、指到一個牆型別）視為實心。少了這一步，它會被判成
    /// 「未宣告種類」，而訊息會叫使用者去填一個**填不進去**的參數。</item>
    /// </list>
    /// </remarks>
    private CurtainPanelKind? PanelKindOf(Element element, Element? type)
    {
        var source = RevitReservedPanelType.SourceWallTypeOf(_document, element, type as ElementType);
        var declared = type?.LookupParameter(CurtainPanelKindParameters.Provided)?.AsString();

        return CurtainPanelTypeSubstitution.Kind(false, element is Wall, declared, source is not null);
    }

    private static Point2D? Centre(BoundingBoxXYZ? box) =>
        box is null ? (Point2D?)null : new Point2D((box.Min.X + box.Max.X) / 2.0, (box.Min.Y + box.Max.Y) / 2.0);

    // Instance values win: a door family may carry its size on either the instance or the type.
    private static double? ReadOpeningSize(Element instance, Element? type, params BuiltInParameter[] parameters)
    {
        foreach (var parameter in parameters)
        {
            foreach (var owner in new[] { instance, type })
            {
                var value = owner?.get_Parameter(parameter);
                if (value is not null && value.StorageType == StorageType.Double && value.HasValue && value.AsDouble() > 0)
                    return value.AsDouble();
            }
        }

        return null;
    }

    private static double? ReadPositiveLength(Element? owner, BuiltInParameter parameter, string fallbackName)
    {
        if (owner is null) return null;
        var value = owner.get_Parameter(parameter) ?? owner.LookupParameter(fallbackName);
        return value is not null && value.StorageType == StorageType.Double && value.HasValue && value.AsDouble() > 0
            ? value.AsDouble()
            : (double?)null;
    }

    private static bool? ReadFlag(Element element, BuiltInParameter parameter)
    {
        var value = element.get_Parameter(parameter);
        return value is not null && value.StorageType == StorageType.Integer && value.HasValue ? value.AsInteger() != 0 : (bool?)null;
    }
}
