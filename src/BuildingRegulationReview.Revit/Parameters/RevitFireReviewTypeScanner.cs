using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using BuildingRegulationReview.Application.Candidates;
using BuildingRegulationReview.Application.Checks;
using BuildingRegulationReview.Application.Parameters;

namespace BuildingRegulationReview.Revit.Parameters;

/// <summary>
/// Collects the element Types one view shows, with the review parameters they carry and the
/// dimension 建築技術規則第71～73條 measures. Read-only: no transaction.
/// </summary>
/// <remarks>
/// The view only decides which Types are listed. The parameters are Type parameters, so the counts
/// report both what the view showed and what the project holds — an edit reaches all of the latter.
/// 梁（結構構架）are not collected: they carry no 設計防火時效 parameter, because the 樑 clauses set
/// no dimensional threshold.
/// </remarks>
public sealed class RevitFireReviewTypeScanner
{
    private static readonly IReadOnlyDictionary<BuiltInCategory, CandidateCategory> Categories =
        new Dictionary<BuiltInCategory, CandidateCategory>
        {
            { BuiltInCategory.OST_Walls, CandidateCategory.Wall },
            { BuiltInCategory.OST_Columns, CandidateCategory.Column },
            { BuiltInCategory.OST_StructuralColumns, CandidateCategory.Column },
            { BuiltInCategory.OST_Floors, CandidateCategory.Floor },
            { BuiltInCategory.OST_Doors, CandidateCategory.Door },
            { BuiltInCategory.OST_Windows, CandidateCategory.Window },
            { BuiltInCategory.OST_CurtainWallPanels, CandidateCategory.CurtainPanel }
        };

    private readonly Document _document;

    public RevitFireReviewTypeScanner(Document document) =>
        _document = document ?? throw new ArgumentNullException(nameof(document));

    /// <summary>The Types visible in <paramref name="view"/>, or in the whole project when it is null.</summary>
    public FireReviewTypeTable Scan(View? view)
    {
        var warnings = new List<string>();
        var inView = new Dictionary<ElementId, List<Element>>();
        var projectCounts = new Dictionary<ElementId, int>();

        foreach (var category in Categories.Keys)
        {
            Gather(Collect(category, view), inView);
            if (view is not null) Count(Collect(category, null), projectCounts);
        }

        if (view is null)
            projectCounts = inView.ToDictionary(x => x.Key, x => x.Value.Count);

        var rows = new List<FireReviewTypeRow>();
        foreach (var pair in inView)
        {
            var type = _document.GetElement(pair.Key) as ElementType;
            if (type is null) continue;
            if (type.Category is null || !Categories.TryGetValue(type.Category.BuiltInCategory, out var candidate)) continue;

            rows.Add(Row(type, candidate, pair.Value,
                projectCounts.TryGetValue(pair.Key, out var all) ? all : pair.Value.Count));
        }

        if (rows.Count == 0)
            warnings.Add(view is null
                ? "專案中找不到牆、柱、樓板、門、窗或帷幕嵌板。"
                : $"視圖「{view.Name}」中找不到牆、柱、樓板、門、窗或帷幕嵌板；請切換到含這些構件的視圖。");

        return new FireReviewTypeTable(rows, warnings);
    }

    private IEnumerable<Element> Collect(BuiltInCategory category, View? view)
    {
        var collector = view is null
            ? new FilteredElementCollector(_document)
            : new FilteredElementCollector(_document, view.Id);

        return collector.OfCategory(category).WhereElementIsNotElementType().ToElements();
    }

    private static void Count(IEnumerable<Element> elements, Dictionary<ElementId, int> counts)
    {
        foreach (var typeId in TypeIds(elements))
            counts[typeId] = counts.TryGetValue(typeId, out var current) ? current + 1 : 1;
    }

    /// <summary>Keeps the instances themselves, because an opening's 防火保護 is written per instance.</summary>
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

    private static IEnumerable<ElementId> TypeIds(IEnumerable<Element> elements) =>
        elements.Select(TypeIdOf).Where(id => id is not null).Select(id => id!);

    private static ElementId? TypeIdOf(Element element)
    {
        // A stacked wall reports its members as walls too; only the members carry a WallType.
        if (element is Wall wall && wall.IsStackedWallMember) return null;

        var typeId = element.GetTypeId();
        return typeId is null || typeId == ElementId.InvalidElementId ? null : typeId;
    }

    private FireReviewTypeRow Row(ElementType type, CandidateCategory category, IReadOnlyList<Element> instances, int inProject)
    {
        var opening = CandidateCategories.IsOpening(category);

        var present = FireReviewTypeParameters.None;
        if (Find(type, FireRatingParameters.Provided) is not null) present |= FireReviewTypeParameters.Rating;
        if (Find(type, StructuralMaterialParameters.Material) is not null) present |= FireReviewTypeParameters.Material;
        if (Find(type, StructuralMaterialParameters.Cover) is not null) present |= FireReviewTypeParameters.Cover;
        if (opening && instances.Any(i => Find(i, FireProtectionParameters.Provided) is not null))
            present |= FireReviewTypeParameters.Protection;

        return new FireReviewTypeRow(
            type.UniqueId,
            category,
            type.Name,
            familyName: string.IsNullOrWhiteSpace(type.FamilyName) ? null : type.FamilyName,
            instanceCount: instances.Count,
            // An opening row writes to the view's instances, so its reach is what the view showed.
            projectInstanceCount: opening ? instances.Count : inProject,
            dimensionMeters: Dimension(type, category),
            material: Text(type, StructuralMaterialParameters.Material),
            coverMeters: Meters(type, StructuralMaterialParameters.Cover),
            providedRating: Text(type, FireRatingParameters.Provided),
            providedProtection: opening ? SharedProtection(instances) : null,
            present: present,
            instanceUniqueIds: opening ? instances.Select(i => i.UniqueId) : null);
    }

    /// <summary>
    /// The 防火保護 every instance of this Type agrees on, or null when they differ — the panel must
    /// not show one door's value as if it were the whole Type's, and an untouched row writes nothing.
    /// </summary>
    private static string? SharedProtection(IReadOnlyList<Element> instances)
    {
        var values = instances.Select(i => Text(i, FireProtectionParameters.Provided) ?? string.Empty)
            .Distinct(StringComparer.Ordinal).ToList();

        return values.Count == 1 ? values[0] : null;
    }

    /// <summary>牆厚、板厚 or 柱短邊 in metres — the value the clause compares its threshold against.</summary>
    private double? Dimension(ElementType type, CandidateCategory category) => category switch
    {
        CandidateCategory.Wall => WallThickness(type),
        CandidateCategory.Floor => FloorThickness(type),
        CandidateCategory.Column => ColumnShortSide(type),
        _ => null
    };

    private static double? WallThickness(ElementType type) =>
        type is WallType wall && wall.Width > 0 ? Meters(wall.Width) : Structure(type);

    private static double? FloorThickness(ElementType type) => Structure(type);

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
