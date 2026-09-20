using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using BuildingRegulationReview.Application.WriteBack;

namespace BuildingRegulationReview.Revit.WriteBack;

/// <summary>
/// Reads back the elements this tool has already written, so the apply preview can tell 新增 from
/// 更新 from 刪除 (spec 10.4). An element counts as the tool's only when it carries a readable
/// ownership token in <see cref="ManagedElementKey.KeyParameterName"/>; everything else in the view
/// belongs to somebody and is never reported, which is what keeps deletion off hand-drawn work.
/// </summary>
/// <remarks>
/// Until P2-T07 writes those parameters this reader finds nothing, and every planned element shows
/// as an addition — which is the truth about a model the tool has not written to yet. The token and
/// the signature are read as plain text, so the reader works whether the parameters arrive as shared
/// parameters or as project parameters, and simply returns nothing while they are unbound.
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
            var token = Text(element, ManagedElementKey.KeyParameterName);
            if (string.IsNullOrWhiteSpace(token)) continue;

            found.Add(new ExistingManagedElement(
                element.UniqueId,
                token!,
                Text(element, ManagedElementKey.SignatureParameterName) ?? string.Empty,
                Describe(element)));
        }
    }

    private static string? Text(Element element, string parameterName)
    {
        var parameter = element.LookupParameter(parameterName);
        return parameter != null && parameter.HasValue && parameter.StorageType == StorageType.String
            ? parameter.AsString()
            : null;
    }

    private static string? Describe(Element element)
    {
        var name = element.Name;
        var category = element.Category?.Name;
        if (string.IsNullOrWhiteSpace(category)) return string.IsNullOrWhiteSpace(name) ? null : name;
        return string.IsNullOrWhiteSpace(name) ? category : category + "：" + name;
    }
}
