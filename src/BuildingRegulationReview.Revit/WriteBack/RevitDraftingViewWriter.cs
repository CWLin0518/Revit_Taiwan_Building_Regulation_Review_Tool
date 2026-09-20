using System;
using System.Linq;
using Autodesk.Revit.DB;
using BuildingRegulationReview.Application.WriteBack;
using BuildingRegulationReview.Domain.ReviewPackages;
using RevitApplicationException = Autodesk.Revit.Exceptions.ApplicationException;

namespace BuildingRegulationReview.Revit.WriteBack;

/// <summary>What looking for the package's Drafting View turned up.</summary>
internal sealed class DraftingViewLookup
{
    private DraftingViewLookup(ViewDrafting? view, bool created, ManualAction? manualAction)
    {
        View = view;
        Created = created;
        ManualAction = manualAction;
    }

    public ViewDrafting? View { get; }

    /// <summary>True when this run made it, which is when the package has to record its UniqueId.</summary>
    public bool Created { get; }

    /// <summary>Why there is no view, when there is none.</summary>
    public ManualAction? ManualAction { get; }

    public static DraftingViewLookup Found(ViewDrafting view, bool created) =>
        new DraftingViewLookup(view, created, null);

    public static DraftingViewLookup Unavailable(string reason, string suggestion) =>
        new DraftingViewLookup(null, false, new ManualAction(
            ManagedOutputKey.Describe(ManagedOutputKind.DraftingView),
            reason,
            suggestion));
}

/// <summary>
/// Finds, or creates, the Drafting View the 單線圖 copies live in (spec 10.5 items 4 and 5).
/// </summary>
/// <remarks>
/// The view is found by the UniqueId on the package, never by its name. That is what spec 10.5
/// item 5's 用唯一識別防止名稱變更造成失聯 asks for, and it decides the other half too: a view that
/// is already there keeps whatever the user has renamed it to, and the default
/// <c>{AreaScheme}_{SourceFloorPlan}_防火區劃</c> is only ever used at the moment of creation.
/// </remarks>
internal sealed class RevitDraftingViewWriter
{
    private readonly Document _document;

    public RevitDraftingViewWriter(Document document) =>
        _document = document ?? throw new ArgumentNullException(nameof(document));

    /// <summary>Requires an open transaction, because it may have to create the view.</summary>
    public DraftingViewLookup Ensure(ReviewPackage package, string defaultName)
    {
        if (package is null) throw new ArgumentNullException(nameof(package));

        var existing = Resolve(package.DraftingViewUniqueId);
        if (existing is not null)
        {
            // The mark is refreshed rather than assumed: a view the package points at but that lost
            // its mark is still the package's view, and the mark is what the next run reads.
            ManagedElementMark.Write(existing, Key(package.PackageId), existing.Name);
            return DraftingViewLookup.Found(existing, false);
        }

        var viewFamilyType = new FilteredElementCollector(_document)
            .OfClass(typeof(ViewFamilyType))
            .Cast<ViewFamilyType>()
            .FirstOrDefault(type => type.ViewFamily == ViewFamily.Drafting);

        if (viewFamilyType is null)
        {
            return DraftingViewLookup.Unavailable(
                "這個專案沒有任何繪圖視圖（Drafting View）類型，Revit 無法建立單線圖視圖",
                "請先在專案中建立一個繪圖視圖類型，或從樣板載入，再重新套用");
        }

        try
        {
            var created = ViewDrafting.Create(_document, viewFamilyType.Id);
            created.Name = ReviewOutputNaming.MakeUnique(defaultName, IsViewNameTaken);
            ManagedElementMark.Write(created, Key(package.PackageId), created.Name);
            return DraftingViewLookup.Found(created, true);
        }
        catch (RevitApplicationException exception)
        {
            return DraftingViewLookup.Unavailable(
                "Revit 拒絕建立單線圖視圖：" + exception.Message,
                "請確認目前的視圖類型與工作集可以新增視圖，再重新套用");
        }
    }

    private ViewDrafting? Resolve(string? uniqueId)
    {
        if (string.IsNullOrWhiteSpace(uniqueId)) return null;
        var view = _document.GetElement(uniqueId) as ViewDrafting;
        return view is not null && !view.IsTemplate ? view : null;
    }

    private static ManagedOutputKey Key(Guid packageId) =>
        new ManagedOutputKey(packageId, ManagedOutputKind.DraftingView);

    private bool IsViewNameTaken(string name) => new FilteredElementCollector(_document)
        .OfClass(typeof(View))
        .Cast<View>()
        .Any(view => string.Equals(view.Name, name, StringComparison.OrdinalIgnoreCase));
}
