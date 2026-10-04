using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using BuildingRegulationReview.Application.Candidates;
using BuildingRegulationReview.Application.Checks;
using BuildingRegulationReview.Application.Parameters;
using BuildingRegulationReview.Application.Reviews;
using BuildingRegulationReview.Domain.Geometry;

namespace BuildingRegulationReview.Revit.Parameters;

/// <summary>
/// Collects the element Types one view shows, with the review parameters they carry and the
/// dimension 建築技術規則第71～73條 measures. Read-only: no transaction.
/// </summary>
/// <remarks>
/// The view orders the list and fills the 視圖 count; it does not decide which Types are listed. The
/// parameters are Type parameters, so one edit reaches every instance in the project, and the review
/// reads the storey band of the whole document rather than what a view happens to show — a Type the
/// view hides still gets reviewed, so it still gets a row (its 數量 reads 0 / 專案數).
/// 梁（結構構架）are listed like the rest, because 第70條 states a required rating for them and the
/// review reads their 設計防火時效. What they have no answer for is deriving that design value from
/// the Type's size — 第71～73條 give 樑 no dimensional threshold — so their 結構材料 column is
/// disabled and the 推定時效 column says why. The value is typed in instead.
/// </remarks>
public sealed class RevitFireReviewTypeScanner
{
    private static readonly IReadOnlyDictionary<BuiltInCategory, CandidateCategory> Categories =
        new Dictionary<BuiltInCategory, CandidateCategory>
        {
            { BuiltInCategory.OST_Walls, CandidateCategory.Wall },
            { BuiltInCategory.OST_Columns, CandidateCategory.Column },
            { BuiltInCategory.OST_StructuralColumns, CandidateCategory.Column },
            { BuiltInCategory.OST_StructuralFraming, CandidateCategory.StructuralFraming },
            { BuiltInCategory.OST_Floors, CandidateCategory.Floor },
            { BuiltInCategory.OST_Doors, CandidateCategory.Door },
            { BuiltInCategory.OST_Windows, CandidateCategory.Window },
            { BuiltInCategory.OST_CurtainWallPanels, CandidateCategory.CurtainPanel }
        };

    private readonly Document _document;

    public RevitFireReviewTypeScanner(Document document) =>
        _document = document ?? throw new ArgumentNullException(nameof(document));

    /// <summary>
    /// Everything the batch panel edits: the Types the project uses, plus the 區劃 and the project facts.
    /// </summary>
    /// <remarks>
    /// Nothing here is scoped to the view — it only orders the Types and counts how many of each it
    /// shows. The 區劃 and the project facts are instance parameters on a handful of elements the user
    /// has to fill in whatever view they happen to be in, and an Area is not visible in a floor plan
    /// at all; the Types are read project-wide because the review is too, so anything it reviews can
    /// be declared here.
    /// </remarks>
    public FireReviewParameterSet ScanAll(View? view) =>
        new(Scan(view), Zones(), ProjectRow(), Floors());

    /// <summary>
    /// Every Type the panel edits: the ones <paramref name="view"/> shows first, then the ones the
    /// project uses but this view does not show. <paramref name="view"/> only orders the list and
    /// fills the 視圖 count; it does not decide membership.
    /// </summary>
    public FireReviewTypeTable Scan(View? view)
    {
        var warnings = new List<string>();
        var inView = new Dictionary<ElementId, List<Element>>();
        var inProject = new Dictionary<ElementId, List<Element>>();

        foreach (var category in Categories.Keys)
        {
            Gather(Collect(category, view), inView);
            if (view is not null) Gather(Collect(category, null), inProject);
        }

        // 專案全體的實體留著而不是只留一個數：佔位嵌板列要從實體回頭問 host 帷幕牆型別，才知道那些
        // 格子的構造是哪一個牆型別宣告的（決議 16、步驟 16h）。收集本來就已經把它們都取出來了，
        // 這裡只是多留一份參考。
        if (view is null) inProject = inView;

        var rows = new List<FireReviewTypeRow>();
        foreach (var pair in inView)
        {
            var all = inProject.TryGetValue(pair.Key, out var list) ? list : pair.Value;
            var row = Row(pair.Key, pair.Value, all, warnings);
            if (row is not null) rows.Add(row);
        }

        // 型別參數的一次編輯到得了整個專案，所以視圖沒顯示的型別也必須列得出來。檢討讀的是樓層帶內的
        // 全文件構件（RevitCandidateObservationReader 的 Collect(storey, …)），不是視圖看得見的那些：
        // 平面圖由視圖範圍決定顯示什麼，整段落在剪切面下方的帷幕嵌板因此一列都不會產生——而
        // 防火檢討_嵌板種類 是必要參數，列不出來就等於宣告不了，那些嵌板在 CW-O 只能答資料不足。
        // 這些列的「數量」欄顯示 0 / 專案數，與視圖裡有實體的列一眼分得開。
        foreach (var pair in inProject)
        {
            if (inView.ContainsKey(pair.Key)) continue;

            var row = Row(pair.Key, Array.Empty<Element>(), pair.Value, warnings);
            if (row is not null) rows.Add(row);
        }

        if (rows.Count == 0)
            warnings.Add("專案中找不到牆、柱、樑、樓板、門、窗或帷幕嵌板。");

        return new FireReviewTypeTable(rows, warnings);
    }

    /// <summary>One row, or null when the Type is gone or is not a category the panel edits.</summary>
    private FireReviewTypeRow? Row(
        ElementId typeId,
        IReadOnlyList<Element> instances,
        IReadOnlyList<Element> projectInstances,
        List<string> warnings)
    {
        if (_document.GetElement(typeId) is not ElementType type) return null;
        if (type.Category is null || !Categories.TryGetValue(type.Category.BuiltInCategory, out var candidate))
            return null;

        return Row(type, candidate, instances, projectInstances, warnings);
    }

    private IEnumerable<Element> Collect(BuiltInCategory category, View? view)
    {
        var collector = view is null
            ? new FilteredElementCollector(_document)
            : new FilteredElementCollector(_document, view.Id);

        return collector.OfCategory(category).WhereElementIsNotElementType().ToElements();
    }

    /// <summary>Groups the view's elements by Type, which is what a row is and how it is counted.</summary>
    private static void Gather(IEnumerable<Element> elements, Dictionary<ElementId, List<Element>> byType)
    {
        foreach (var element in elements)
        {
            var typeId = TypeIdOf(element);
            if (typeId is null) continue;

            if (!byType.TryGetValue(typeId, out var list)) byType[typeId] = list = new List<Element>();
            list.Add(element);
        }
    }

    private static ElementId? TypeIdOf(Element element)
    {
        // A stacked wall reports its members as walls too; only the members carry a WallType.
        if (element is Wall wall && wall.IsStackedWallMember) return null;

        var typeId = element.GetTypeId();
        return typeId is null || typeId == ElementId.InvalidElementId ? null : typeId;
    }

    private FireReviewTypeRow Row(
        ElementType type,
        CandidateCategory category,
        IReadOnlyList<Element> instances,
        IReadOnlyList<Element> projectInstances,
        List<string> warnings)
    {
        var opening = CandidateCategories.IsOpening(category);
        var panel = category == CandidateCategory.CurtainPanel;
        var substitutedFrom = panel ? PanelSource(type, projectInstances, warnings) : null;

        var present = FireReviewTypeParameters.None;
        if (Find(type, FireRatingParameters.Provided) is not null) present |= FireReviewTypeParameters.Rating;
        if (Find(type, StructuralMaterialParameters.Material) is not null) present |= FireReviewTypeParameters.Material;
        if (Find(type, StructuralMaterialParameters.Cover) is not null) present |= FireReviewTypeParameters.Cover;
        if (opening && Find(type, FireProtectionParameters.Provided) is not null)
            present |= FireReviewTypeParameters.Protection;
        if (opening && Find(type, SmokeProtectionParameters.Provided) is not null)
            present |= FireReviewTypeParameters.SmokeSeal;
        // 只有帷幕嵌板該帶種類。這份共享參數檔綁 Curtain Panels 時會連門窗一起綁到（同一個檔），所以
        // 旗標由類別把關，而不是「型別上找得到這個參數」（決議 16、步驟 16c）。
        if (panel && Find(type, CurtainPanelKindParameters.Provided) is not null)
            present |= FireReviewTypeParameters.PanelKind;

        return new FireReviewTypeRow(
            type.UniqueId,
            category,
            type.Name,
            familyName: string.IsNullOrWhiteSpace(type.FamilyName) ? null : type.FamilyName,
            instanceCount: instances.Count,
            projectInstanceCount: projectInstances.Count,
            dimensionMeters: Dimension(type, category),
            material: Text(type, StructuralMaterialParameters.Material),
            coverMeters: Meters(type, StructuralMaterialParameters.Cover),
            providedRating: Text(type, FireRatingParameters.Provided),
            providedProtection: opening ? Ticked(type, FireProtectionParameters.Provided) : null,
            providedSmokeProtection: opening ? Ticked(type, SmokeProtectionParameters.Provided) : null,
            present: present,
            panelKind: panel ? Text(type, CurtainPanelKindParameters.Provided) : null,
            // 佔位嵌板不提案：種類是事實（模型自己說了那些格子是一道牆），不是由材料猜的提案，
            // 所以不走 ProposeFrom／AwaitsPanelKind 那條路（決議 16、步驟 16h）。
            proposedPanelKind: panel && substitutedFrom is null ? ProposedPanelKind(type) : null,
            substitutedFrom: substitutedFrom);
    }

    /// <summary>
    /// 佔位嵌板型別（<c>System Panel : Wall</c>）真正的構造來源，或 null 表示這一列自己作答
    /// （決議 16、步驟 16h）。
    /// </summary>
    /// <remarks>
    /// 一列就是一個型別，所以來源必須是唯一的：同一個保留型別若被兩個帷幕牆型別用、而兩者的
    /// <c>Curtain Panel</c> 指到不同的牆型別，這一列代表不了其中任何一個，寧可不顯示也不挑一個。
    /// 檢討本身不受影響——<c>RevitCurtainWallGeometryReader</c> 是逐片解析的。
    /// </remarks>
    private CurtainPanelSourceType? PanelSource(ElementType type, IReadOnlyList<Element> instances, List<string> warnings)
    {
        if (!RevitReservedPanelType.IsReserved(type)) return null;

        var sources = instances
            .Select(element => RevitReservedPanelType.SourceWallTypeOf(_document, element, type))
            .Where(wallType => wallType is not null)
            .Select(wallType => wallType!.Id)
            .Distinct()
            .ToList();

        if (sources.Count == 1 && _document.GetElement(sources[0]) is WallType single)
            return RevitReservedPanelType.Describe(single);

        warnings.Add(sources.Count == 0
            ? $"帷幕嵌板類型「{type.Name}」是 Revit 的保留類型，參數唯讀且無法從帷幕牆類型的 Curtain Panel 解析出來源牆類型；這些嵌板在檢討中會答資料不足。"
            : $"帷幕嵌板類型「{type.Name}」是 Revit 的保留類型，但不同實體指向 {sources.Count} 種牆類型，面板無法以單一來源顯示；檢討仍逐片解析，不受影響。");

        return null;
    }

    /// <summary>
    /// 由嵌板型別的材料替使用者提案一個種類（決議 16、步驟 16c）。這**不是**寫入：值只進面板的下拉，
    /// 由使用者留下或改掉，寫入是按下套用才發生的事，與 結構材料＋推定時效 同一套模式。
    /// </summary>
    private CurtainPanelKind? ProposedPanelKind(ElementType type)
    {
        var material = PanelMaterial(type);
        return CurtainPanelKinds.ProposeFrom(material?.MaterialClass, material?.Name, type.Name);
    }

    /// <summary>
    /// The material a panel Type names, which is the 材料 parameter — a system panel Type carries no
    /// compound structure to read layers from (see <see cref="PanelThickness"/>), and a custom panel
    /// family publishes the same parameter. Null when the Type names none (`&lt;By Category&gt;`
    /// included), which proposes nothing rather than guessing.
    /// </summary>
    private Material? PanelMaterial(ElementType type)
    {
        var id = type.get_Parameter(BuiltInParameter.MATERIAL_ID_PARAM)?.AsElementId();
        return id is null || id == ElementId.InvalidElementId ? null : _document.GetElement(id) as Material;
    }

    /// <summary>
    /// A Yes/No Type parameter: true when ticked, false when the box is there but not ticked, and
    /// null when the Type does not carry the parameter — so an untouched row writes nothing back.
    /// </summary>
    private static bool? Ticked(ElementType type, string name)
    {
        var parameter = Find(type, name);
        if (parameter is null || parameter.StorageType != StorageType.Integer) return null;
        return parameter.HasValue && parameter.AsInteger() == 1;
    }

    /// <summary>牆厚、板厚、柱短邊 or 嵌板厚 in metres — the value the clause compares its threshold against.</summary>
    private double? Dimension(ElementType type, CandidateCategory category) => category switch
    {
        CandidateCategory.Wall => WallThickness(type),
        CandidateCategory.Floor => FloorThickness(type),
        CandidateCategory.Column => ColumnShortSide(type),
        CandidateCategory.CurtainPanel => PanelThickness(type),
        _ => null
    };

    private static double? WallThickness(ElementType type) =>
        type is WallType wall && wall.Width > 0 ? Meters(wall.Width) : Structure(type);

    private static double? FloorThickness(ElementType type) => Structure(type);

    /// <summary>
    /// 嵌板厚 — 一片實心嵌板的時效比照牆體由厚度推定（決議 16），所以這一欄要讀得出來。
    /// </summary>
    /// <remarks>
    /// 系統嵌板型別（<c>PanelType</c>）繼承的是 <c>FamilySymbol</c>，**不是** <c>HostObjAttributes</c>
    /// （反射實測 Revit 2024：<c>PanelType → FamilySymbol → InsertableObject → ElementType</c>）——
    /// 它沒有複合構造可讀，厚度是型別參數 Thickness（<c>CURTAIN_WALL_SYSPANEL_THICKNESS</c>）。
    /// 步驟 16a～16c 誤以為它是 <c>HostObjAttributes</c>，於是每一個系統嵌板型別的尺寸都讀成 null，
    /// 面板顯示「（尺寸讀不到）」、實心那一路推不出時效（步驟 16g）。內建參數的查詢與介面語言無關，
    /// 中文版 Revit 也照樣讀得到。嵌板為牆者類別是 OST_Walls、走 <see cref="WallThickness"/>，不到這裡；
    /// 訂製嵌板族沒有這個參數，其型別若自帶複合構造仍由 <see cref="Structure"/> 補上，兩者都讀不到才報 null。
    /// </remarks>
    private static double? PanelThickness(ElementType type)
    {
        var feet = PositiveLength(type, BuiltInParameter.CURTAIN_WALL_SYSPANEL_THICKNESS);
        return feet is null ? Structure(type) : Meters(feet.Value);
    }

    /// <summary>The total thickness of a layered construction, which is how 第72／73條 measure a 牆壁／樓地板.</summary>
    private static double? Structure(ElementType type)
    {
        CompoundStructure? structure = null;
        try
        {
            structure = (type as HostObjAttributes)?.GetCompoundStructure();
        }
        catch (Autodesk.Revit.Exceptions.ApplicationException)
        {
            return null;
        }

        var width = structure?.GetWidth() ?? 0;
        return width > 0 ? Meters(width) : (double?)null;
    }

    /// <summary>
    /// The shorter of the section's two sides — 第71條二 and 第72條二 both measure 短邊. A column whose
    /// family does not publish b／h reports nothing rather than a guess from its bounding box.
    /// </summary>
    private static double? ColumnShortSide(ElementType type)
    {
        // Chinese-localised column families publish 柱寬／柱深 rather than the b／h a structural
        // section family carries, so both spellings are tried before giving up.
        var width = PositiveLength(type, BuiltInParameter.STRUCTURAL_SECTION_COMMON_WIDTH, "b", "柱寬", "寬度", "Width");
        var depth = PositiveLength(type, BuiltInParameter.STRUCTURAL_SECTION_COMMON_HEIGHT, "h", "柱深", "深度", "Depth", "Height");

        if (width is null && depth is null) return null;
        var shorter = Math.Min(width ?? double.MaxValue, depth ?? double.MaxValue);
        return Meters(shorter);
    }

    private static double? PositiveLength(Element type, BuiltInParameter builtIn, params string[] fallbackNames)
    {
        var parameter = type.get_Parameter(builtIn);
        if (!IsPositiveLength(parameter))
            parameter = fallbackNames.Select(type.LookupParameter).FirstOrDefault(IsPositiveLength);

        return IsPositiveLength(parameter) ? parameter!.AsDouble() : (double?)null;
    }

    private static bool IsPositiveLength(Parameter? parameter) =>
        parameter is not null && parameter.StorageType == StorageType.Double && parameter.HasValue && parameter.AsDouble() > 0;

    /// <summary>
    /// The storey number of every Level, proposed from their elevations so 所在樓層序 and 地上層數
    /// do not have to be typed one Area at a time.
    /// </summary>
    private FloorNumbering Floors() =>
        LevelFloorNumbering.From(new FilteredElementCollector(_document)
            .OfClass(typeof(Level))
            .Cast<Level>()
            .Select(level => new BuildingLevel(
                level.UniqueId,
                level.Name,
                UnitUtils.ConvertFromInternalUnits(level.Elevation, UnitTypeId.Meters))));

    /// <summary>Every 防火區劃 Area in the document, with the zone parameters the review reads.</summary>
    private IEnumerable<FireReviewZoneRow> Zones()
    {
        foreach (var element in new FilteredElementCollector(_document)
                     .OfCategory(BuiltInCategory.OST_Areas)
                     .WhereElementIsNotElementType()
                     .ToElements())
        {
            if (!(element is Autodesk.Revit.DB.Area area)) continue;

            var present = FireReviewZoneParameters.None;
            if (Find(area, ReviewInputSources.ZoneUse) is not null) present |= FireReviewZoneParameters.Use;
            if (Find(area, ReviewInputSources.Sprinklered) is not null) present |= FireReviewZoneParameters.Sprinklered;
            if (Find(area, ReviewInputSources.FloorNumber) is not null) present |= FireReviewZoneParameters.FloorNumber;
            if (Find(area, ReviewInputSources.LinksRefugeFloor) is not null) present |= FireReviewZoneParameters.LinksRefugeFloor;
            if (Find(area, ReviewInputSources.CannotBeSubdivided) is not null) present |= FireReviewZoneParameters.CannotBeSubdivided;

            yield return new FireReviewZoneRow(
                area.UniqueId,
                Text(area, BuiltInParameter.ROOM_NAME) ?? area.Name,
                number: Text(area, BuiltInParameter.ROOM_NUMBER),
                levelName: area.Level?.Name,
                levelId: area.Level?.UniqueId,
                areaSchemeName: SchemeOf(area),
                // Revit reports an unplaced Area as zero; the review's own cross-check judges that,
                // so the panel simply shows nothing rather than a misleading 0 m².
                areaSquareMeters: area.Area > 0 ? PlanUnits.SquareFeetToSquareMeters(area.Area) : (double?)null,
                use: Text(area, ReviewInputSources.ZoneUse),
                sprinklered: YesNo(area, ReviewInputSources.Sprinklered),
                floorNumber: Integer(area, ReviewInputSources.FloorNumber),
                present: present,
                // 第79條之2第3項第一款 (垂直區劃規格 §6). Only a 挑空 fills it; 連跨樓層數 and 連通區劃面積
                // are traced through the storeys, not typed (決議 35).
                linksRefugeFloor: YesNo(area, ReviewInputSources.LinksRefugeFloor),
                // 第79條之1 (第79條之1規格 §6). Not a required parameter either, so an Area that does
                // not carry it reads as 未填 and the panel says so only for the six uses that need it.
                cannotBeSubdivided: YesNo(area, ReviewInputSources.CannotBeSubdivided));
        }
    }

    private string? SchemeOf(Autodesk.Revit.DB.Area area)
    {
        try
        {
            var id = area.get_Parameter(BuiltInParameter.AREA_SCHEME_ID)?.AsElementId();
            return id is null ? null : (_document.GetElement(id) as AreaScheme)?.Name;
        }
        catch (Autodesk.Revit.Exceptions.ApplicationException)
        {
            return null;
        }
    }

    /// <summary>The Project Information facts, or null when the document exposes none.</summary>
    private FireReviewProjectRow? ProjectRow()
    {
        var info = _document.ProjectInformation;
        if (info is null) return null;

        var present = FireReviewProjectParameters.None;
        if (Find(info, ReviewInputSources.FireResistiveConstruction) is not null) present |= FireReviewProjectParameters.FireResistive;
        if (Find(info, ReviewInputSources.BuildingUse) is not null) present |= FireReviewProjectParameters.BuildingUse;
        if (Find(info, ReviewInputSources.FloorsAboveGround) is not null) present |= FireReviewProjectParameters.FloorsAboveGround;

        return new FireReviewProjectRow(
            info.UniqueId,
            fireResistiveConstruction: YesNo(info, ReviewInputSources.FireResistiveConstruction),
            buildingUse: Text(info, ReviewInputSources.BuildingUse),
            floorsAboveGround: Integer(info, ReviewInputSources.FloorsAboveGround),
            present: present);
    }

    private static string? Text(Element element, BuiltInParameter parameter)
    {
        var value = element.get_Parameter(parameter);
        return value is not null && value.HasValue ? value.AsString() : null;
    }

    /// <summary>A Yes/No as a tri-state: null means the parameter holds no value, not "no".</summary>
    private static bool? YesNo(Element element, string name)
    {
        var parameter = element.LookupParameter(name);
        if (parameter is null || !parameter.HasValue || parameter.StorageType != StorageType.Integer) return null;

        var value = parameter.AsInteger();
        return value == 1 ? true : value == 0 ? false : (bool?)null;
    }

    private static int? Integer(Element element, string name)
    {
        var parameter = element.LookupParameter(name);
        if (parameter is null || !parameter.HasValue) return null;

        if (parameter.StorageType == StorageType.Integer) return parameter.AsInteger();
        return parameter.StorageType == StorageType.String &&
               int.TryParse(parameter.AsString(), out var parsed)
            ? parsed
            : (int?)null;
    }

    /// <summary>The parameter if the Type carries it at all — presence, not whether it holds a value.</summary>
    private static Parameter? Find(Element element, string name) => element.LookupParameter(name);

    private static string? Text(Element element, string name)
    {
        var parameter = element.LookupParameter(name);
        if (parameter is null || !parameter.HasValue) return null;

        return parameter.StorageType switch
        {
            StorageType.String => parameter.AsString(),
            StorageType.Integer => parameter.AsValueString() ?? parameter.AsInteger().ToString(),
            StorageType.Double => parameter.AsValueString(),
            StorageType.ElementId => parameter.AsValueString(),
            _ => null
        };
    }

    private static double? Meters(Element element, string name)
    {
        var parameter = element.LookupParameter(name);
        return IsPositiveLength(parameter) ? Meters(parameter!.AsDouble()) : (double?)null;
    }

    private static double Meters(double feet) => UnitUtils.ConvertFromInternalUnits(feet, UnitTypeId.Meters);
}
