using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using BuildingRegulationReview.Application.Reviews;
using BuildingRegulationReview.Application.WriteBack;

namespace BuildingRegulationReview.Revit.WriteBack;

/// <summary>
/// Reads back the elements this tool has already written, so the apply preview can tell 新增 from
/// 更新 from 刪除 (spec 10.4). An element counts as the tool's only when it carries a readable
/// ownership mark; everything else in the view
/// belongs to somebody and is never reported, which is what keeps deletion off hand-drawn work.
/// </summary>
/// <remarks>
/// The mark itself is <see cref="ManagedElementMark"/>: Extensible Storage on the element, with the
/// text parameters as a fallback for a project that binds them. Before anything has been written the
/// reader finds nothing and every planned element shows as an addition, which is the truth about a
/// model the tool has not touched yet.
/// </remarks>
public sealed class RevitManagedElementInventory
{
    private static readonly BuiltInCategory[] AreaPlanCategories =
    {
        BuiltInCategory.OST_Areas,
        BuiltInCategory.OST_AreaSchemeLines,
        BuiltInCategory.OST_AreaTags
    };

    private static readonly BuiltInCategory[] DraftingViewCategories =
    {
        BuiltInCategory.OST_Lines,
        BuiltInCategory.OST_GenericAnnotation,
        BuiltInCategory.OST_TextNotes
    };

    private readonly Document _document;

    public RevitManagedElementInventory(Document document) =>
        _document = document ?? throw new ArgumentNullException(nameof(document));

    /// <summary>
    /// Everything carrying an ownership token in the package's Area Plan and Drafting View. Views
    /// that are missing are skipped rather than failing: a package whose Drafting View has not been
    /// created yet simply has nothing there to compare against.
    /// </summary>
    public IReadOnlyList<ExistingManagedElement> Read(string areaPlanUniqueId, string? draftingViewUniqueId = null)
    {
        var found = new List<ExistingManagedElement>();
        Collect(found, areaPlanUniqueId, AreaPlanCategories);
        Collect(found, draftingViewUniqueId, DraftingViewCategories);
        return found;
    }

    private void Collect(List<ExistingManagedElement> found, string? viewUniqueId, BuiltInCategory[] categories)
    {
        if (string.IsNullOrWhiteSpace(viewUniqueId)) return;
        if (!(_document.GetElement(viewUniqueId) is View view)) return;

        var filter = new ElementMulticategoryFilter(categories);
        foreach (var element in new FilteredElementCollector(_document, view.Id)
                     .WhereElementIsNotElementType()
                     .WherePasses(filter))
        {
            if (!ManagedElementMark.TryRead(element, out var token, out var signature)) continue;

            found.Add(new ExistingManagedElement(
                element.UniqueId,
                token,
                signature,
                Describe(element),
                ZoneUseOf(element)));
        }
    }

    /// <summary>
    /// 防火檢討_區劃用途 as an Area carries it, or null for anything that is not an Area and for one
    /// whose parameter is unbound or does not hold text. Null is 讀不到, never 空白: the preview turns
    /// a blank into 「清除用途」 and must not be handed one the model never had.
    /// </summary>
    private static string? ZoneUseOf(Element element)
    {
        if (!(element is Autodesk.Revit.DB.Area)) return null;

        var parameter = element.LookupParameter(ReviewInputSources.ZoneUse);
        if (parameter is null || parameter.StorageType != StorageType.String) return null;
        return parameter.AsString() ?? string.Empty;
    }

    private static string? Describe(Element element)
    {
        var name = element.Name;
        var category = element.Category?.Name;
        if (string.IsNullOrWhiteSpace(category)) return string.IsNullOrWhiteSpace(name) ? null : name;
        return string.IsNullOrWhiteSpace(name) ? category : category + "：" + name;
    }
}
