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
/// than the whole model. The one exception is the curtain panels: an element with no BoundingBox
/// cannot pass a <see cref="BoundingBoxIntersectsFilter"/> at all, so those are also gathered from
/// the curtain walls that did pass (驗證清單 C-17, <see cref="CollectCurtainPanels"/>).
/// Linked models are not read: the MVP reviews the host model only (spec 17.1).
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
        var walls = Collect(storey, BuiltInCategory.OST_Walls).OfType<Wall>().ToList();
        members.AddRange(walls.Select(w => ReadWall(w, documentId, warnings)).Where(m => m is not null).Select(m => m!));
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
        foreach (var category in new[] { BuiltInCategory.OST_Doors, BuiltInCategory.OST_Windows })
            openings.AddRange(Collect(storey, category)
                .Select(o => ReadOpening(o, documentId, category, warnings))
                .Where(o => o is not null).Select(o => o!));

        openings.AddRange(CollectCurtainPanels(storey, walls, new StoreyBand(bottom, top), warnings)
            .Select(o => ReadOpening(o, documentId, BuiltInCategory.OST_CurtainWallPanels, warnings))
            .Where(o => o is not null).Select(o => o!));

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

    /// <summary>
    /// 樓層範圍內的帷幕嵌板：通過樓層篩選的，**或** 宿主帷幕牆已入選、而它所在那一格的高程也落在
    /// 這一層的（驗證清單 C-17）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 樓層篩選是 <see cref="BoundingBoxIntersectsFilter"/>，而佔位嵌板（帷幕牆型別的 Curtain Panel
    /// 指到一個基本牆型別時 Revit 放進格子的保留型別嵌板）**沒有任何量體、讀不到 BoundingBox**
    /// （見 <see cref="RevitReservedPanelType"/>），所以它永遠過不了那道篩選——連
    /// <c>UnrelatedOpeningCount</c> 都計不到它，檢討表、日誌、統計三邊都看不出有東西消失。
    /// 所以樓層篩選不能是嵌板能否入選的唯一閘門，要從 <c>CurtainGrid</c> 的格子把它接回來。
    /// </para>
    /// <para>
    /// 但宿主牆入選**不代表**它的每一片嵌板都在這一層：宿主牆是以 bbox 相交收的，一道 1F 通到 RF 的
    /// 帷幕牆會出現在每一層的牆清單裡，而 <c>GetPanelIds()</c> 回的是整面牆的嵌板。所以每一片都要
    /// 再問一次高程，判準在 <see cref="StoreyBand.KeepsCurtainPanel"/>（Application 層，有測試）。
    /// </para>
    /// <para>
    /// 只收帷幕嵌板類別：格子裡換成門窗的那幾格屬於 <c>OST_Doors</c>／<c>OST_Windows</c>，有自己的
    /// 量體與各自的收集，從這裡再收一次會變成同一個元素被觀察兩次。
    /// </para>
    /// </remarks>
    private IEnumerable<Element> CollectCurtainPanels(
        ElementFilter storey,
        IReadOnlyList<Wall> walls,
        StoreyBand band,
        List<string> warnings)
    {
        var found = new Dictionary<ElementId, Element>();
        foreach (var panel in Collect(storey, BuiltInCategory.OST_CurtainWallPanels))
            found[panel.Id] = panel;

        foreach (var wall in walls)
        {
            var grid = wall.CurtainGrid;
            if (grid is null) continue;

            foreach (var id in grid.GetPanelIds())
            {
                if (found.ContainsKey(id)) continue;

                var element = _document.GetElement(id);
                if (element is null || element.Category?.BuiltInCategory != BuiltInCategory.OST_CurtainWallPanels) continue;
                if (!IsInPrimaryDesign(element)) continue;

                var cell = CurtainCellPlacement(element, wall);
                var hasBox = element.get_BoundingBox(null) is not null;
                if (!band.KeepsCurtainPanel(cell?.LowFeet, cell?.HighFeet, hasBox)) continue;

                if (cell is null && !hasBox)
                {
                    warnings.Add($"帷幕嵌板（Id {element.Id}）既讀不到 BoundingBox，也問不出它所在的格子" +
                                 $"（宿主帷幕牆 Id {wall.Id}），無法確認它屬於哪一層，已列入本層待人工覆核。");
                }

                found[id] = element;
            }
        }

        // 讀取順序不影響 CandidateResolver 的正規排序，但固定下來讓重讀同一個模型連中間狀態都一樣。
        return found.OrderBy(pair => pair.Key.Value).Select(pair => pair.Value).ToList();
    }

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

    private OpeningObservation? ReadOpening(Element element, string documentId, BuiltInCategory category, List<string> warnings)
    {
        var host = element switch
        {
            Panel panel => panel.Host,
            FamilyInstance instance => instance.Host,
            _ => null
        };

        // 佔位嵌板的 Host 兩條現成路都問不到（沒有量體，Panel.Host／FamilyInstance.Host 都是 null），
        // 要靠 CURTAIN_WALL_PANEL_HOST_ID 才接得回宿主帷幕牆（驗證清單 C-17）。先問型別化的那兩個是為了
        // 不改變一般嵌板與門窗的既有行為。
        if (host is null && category == BuiltInCategory.OST_CurtainWallPanels)
            host = RevitReservedPanelType.HostWallOf(_document, element);

        // A curtain panel only matters as an opening in a wall; panels of curtain systems and roofs are not.
        if (category == BuiltInCategory.OST_CurtainWallPanels && !(host is Wall)) return null;

        var type = _document.GetElement(element.GetTypeId());
        var location = ReadOpeningLocation(element, host as Wall, warnings);

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

    /// <summary>
    /// 一個開口的平面位置，依序問四個來源（中間兩個的先後取決於它是嵌板還是門窗，見內文註解），
    /// 全部問不到時回 null（驗證清單 C-16、C-17）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 一般牆上的門窗有 <c>LocationPoint</c>，那是 Revit 給的插入點，一個字都不動。帷幕牆上的門窗與
    /// 嵌板都沒有（實測），於是原本一路回退到 <c>get_BoundingBox</c> 的中心——而外接矩形**含門扇**：
    /// 一扇以 90 度開啟的帷幕牆門，矩形中心被拉離牆面 502 mm，超出 <c>CandidateResolver</c> 的半牆厚
    /// ＋容許值（175 mm），於是這扇就在區劃邊界牆上的門被判成「離邊界太遠」而從檢討表上消失（C-16）。
    /// 佔位嵌板更徹底：連外接矩形都沒有（C-17）。
    /// </para>
    /// <para>
    /// 所以兩種開口各有自己的**放置**來源，都與量體伸到哪裡無關。門窗問
    /// <see cref="FamilyInstance.GetTotalTransform"/> 的原點（家族的插入點就落在牆面上）；嵌板問它
    /// 所在那一格的 <see cref="CurtainCell"/> 中心（由 <see cref="Panel.GetRefGridLines"/> 取回它自己
    /// 的兩條格線，不靠格子與嵌板的順序對應）。取到的點一律投影回宿主牆的位置線
    /// ——<see cref="OpeningObservation.Location"/> 的契約就是「落在 host 的位置線上」，而那條線正是
    /// 判邊界用的那條帶的軸。
    /// </para>
    /// <para>
    /// **嵌板不以門窗的 transform 原點頂替**：佔位嵌板沒有量體，它的 transform 原點是什麼沒有驗過，
    /// 而投影會把一個錯的點修成「落在正確那道牆上、沿牆位置卻錯」的樣子——在橫跨兩個區劃的長牆上
    /// 那會把嵌板判給錯的區劃或錯的邊界段，而且看起來是正常候選，沒有警告也沒有疑義。那比讀不到
    /// 位置更糟：讀不到至少會進 <c>OpeningLocationUnknown</c> 人工覆核。
    /// </para>
    /// <para>
    /// 來源退到外接矩形、或全部問不到時都往 <paramref name="warnings"/> 留一句，說明是哪一件、
    /// 退到了哪一段。全部問不到就回 null，讓 <c>CandidateResolver</c> 登一筆
    /// <c>OpeningLocationUnknown</c> 人工覆核，而不是靜默消失。
    /// </para>
    /// </remarks>
    private Point2D? ReadOpeningLocation(Element element, Wall? host, List<string> warnings)
    {
        if (element.Location is LocationPoint point) return RevitPlanShapeReader.ToPlan(point.Point);

        var placed = element is Panel
            ? CurtainCellPlacement(element, host)?.Centre
            : PlacementOrigin(element);
        if (placed is not null) return OnHostCenterline(RevitPlanShapeReader.ToPlan(placed), host);

        var source = element is Panel ? "所在格子的中心" : "放置點";
        var box = Centre(element.get_BoundingBox(null));
        if (box is null)
        {
            warnings.Add($"開口（Id {element.Id}）的{source}與外接矩形都讀不到，無法判定它屬於哪個區劃，已列為人工覆核。");
            return null;
        }

        warnings.Add($"開口（Id {element.Id}）的{source}讀不到，已改用外接矩形中心投影到 Host 牆位置線上判定，請確認。");
        return OnHostCenterline(box.Value, host);
    }

    /// <summary>宿主牆的位置線上最近的一點；沒有宿主牆或讀不到位置線時原樣回傳。</summary>
    private static Point2D OnHostCenterline(Point2D located, Wall? host)
    {
        IReadOnlyList<Point2D>? centerline = null;
        try
        {
            if (host?.Location is LocationCurve location && location.Curve is not null)
                centerline = RevitPlanShapeReader.Flatten(location.Curve);
        }
        catch (Autodesk.Revit.Exceptions.ApplicationException)
        {
            // 讀不到位置線就用原來的點；牆自己的位置線讀取失敗已由 ReadWall 留了一筆警告。
        }

        return OpeningPlacement.OnCenterline(located, centerline);
    }

    /// <summary>實例的放置點，與它有沒有量體無關。</summary>
    private static XYZ? PlacementOrigin(Element element)
    {
        try
        {
            return element is FamilyInstance instance ? instance.GetTotalTransform()?.Origin : null;
        }
        catch (Autodesk.Revit.Exceptions.ApplicationException)
        {
            return null;
        }
    }

    /// <summary>一片嵌板所在那一格：中心（判區劃用）與垂直範圍（判樓層用）。</summary>
    private readonly struct CellPlacement
    {
        public CellPlacement(XYZ centre, double lowFeet, double highFeet)
        {
            Centre = centre;
            LowFeet = lowFeet;
            HighFeet = highFeet;
        }

        public XYZ Centre { get; }
        public double LowFeet { get; }
        public double HighFeet { get; }
    }

    /// <summary>
    /// 一片嵌板所在那一格，由嵌板自己指名的兩條格線取回（<see cref="Panel.GetRefGridLines"/>），
    /// 所以不依賴 <c>GetPanelIds</c> 與 <c>GetCurtainCells</c> 的順序對應，也不依賴嵌板有沒有量體。
    /// </summary>
    /// <remarks>
    /// <paramref name="host"/> 由呼叫端傳進來（收集時是正在走格子的那道牆，讀取時是
    /// <c>ReadOpening</c> 已經解出的 host），不在這裡重解一次：兩處一旦解出不同的牆會錯得很難查。
    /// 取不到格子時回 null —— 位於格線邊緣、整面只有一格、非平面格子都問得出 null 或擲例外，
    /// 那是「問不出來」，由呼叫端決定要不要退到別的來源。
    /// </remarks>
    private CellPlacement? CurtainCellPlacement(Element element, Wall? host)
    {
        if (!(element is Panel panel)) return null;

        var grid = host?.CurtainGrid;
        if (grid is null) return null;

        try
        {
            ElementId u = ElementId.InvalidElementId, v = ElementId.InvalidElementId;
            panel.GetRefGridLines(ref u, ref v);

            var cell = grid.GetCell(u, v);
            return cell is null ? null : Outline(cell.CurveLoops);
        }
        catch (Autodesk.Revit.Exceptions.ApplicationException)
        {
            return null;
        }
    }

    /// <summary>
    /// 格子外輪廓頂點的平均（矩形格即其中心）與它的上下緣；一個頂點都沒有時回 null。
    /// </summary>
    /// <remarks>
    /// 只取第一個 loop：那是外輪廓，其餘是洞。把多個 loop 的頂點混在一起平均會把中心拉向洞的那一側。
    /// </remarks>
    private static CellPlacement? Outline(CurveArrArray? loops)
    {
        if (loops is null || loops.IsEmpty) return null;

        double x = 0, y = 0, z = 0, low = double.PositiveInfinity, high = double.NegativeInfinity;
        var count = 0;
        foreach (Curve curve in loops.get_Item(0))
        {
            if (!curve.IsBound) continue;

            var end = curve.GetEndPoint(0);
            x += end.X;
            y += end.Y;
            z += end.Z;
            if (end.Z < low) low = end.Z;
            if (end.Z > high) high = end.Z;
            count++;
        }

        return count == 0 ? null : new CellPlacement(new XYZ(x / count, y / count, z / count), low, high);
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
