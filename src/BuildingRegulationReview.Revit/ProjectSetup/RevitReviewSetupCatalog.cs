using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace BuildingRegulationReview.Revit.ProjectSetup;

public sealed class RevitSetupOption
{
    public RevitSetupOption(string uniqueId, string name, string relatedUniqueId = "")
    { UniqueId = uniqueId; Name = name; RelatedUniqueId = relatedUniqueId; }
    public string UniqueId { get; }
    public string Name { get; }
    public string RelatedUniqueId { get; }
}

public sealed class RevitReviewSetupCatalog
{
    public IReadOnlyList<RevitSetupOption> FloorPlans { get; }
    public IReadOnlyList<RevitSetupOption> AreaSchemes { get; }
    public IReadOnlyList<RevitSetupOption> AreaPlanTemplates { get; }
    public IReadOnlyList<RevitSetupOption> ScopeBoxes { get; }

    private RevitReviewSetupCatalog(
        IReadOnlyList<RevitSetupOption> floorPlans,
        IReadOnlyList<RevitSetupOption> areaSchemes,
        IReadOnlyList<RevitSetupOption> templates,
        IReadOnlyList<RevitSetupOption> scopeBoxes)
    {
        FloorPlans = floorPlans;
        AreaSchemes = areaSchemes;
        AreaPlanTemplates = templates;
        ScopeBoxes = scopeBoxes;
    }

    public static RevitReviewSetupCatalog Read(Document document)
    {
        if (document is null) throw new ArgumentNullException(nameof(document));
        var plans = new FilteredElementCollector(document).OfClass(typeof(ViewPlan)).Cast<ViewPlan>()
            .Where(x => !x.IsTemplate && x.ViewType == ViewType.FloorPlan && x.GenLevel != null)
            .OrderBy(x => x.Name).Select(x => new RevitSetupOption(x.UniqueId, x.Name, x.GenLevel.UniqueId)).ToList();
        var schemes = new FilteredElementCollector(document).OfClass(typeof(AreaScheme)).Cast<AreaScheme>()
            .OrderBy(x => x.Name).Select(x => new RevitSetupOption(x.UniqueId, x.Name)).ToList();
        var templates = new FilteredElementCollector(document).OfClass(typeof(ViewPlan)).Cast<ViewPlan>()
            .Where(x => x.IsTemplate && x.ViewType == ViewType.AreaPlan)
            .OrderBy(x => x.Name).Select(x => new RevitSetupOption(x.UniqueId, x.Name)).ToList();
        var scopeBoxes = new FilteredElementCollector(document).OfCategory(BuiltInCategory.OST_VolumeOfInterest)
            .WhereElementIsNotElementType().OrderBy(x => x.Name)
            .Select(x => new RevitSetupOption(x.UniqueId, x.Name)).ToList();
        return new RevitReviewSetupCatalog(plans, schemes, templates, scopeBoxes);
    }
}
