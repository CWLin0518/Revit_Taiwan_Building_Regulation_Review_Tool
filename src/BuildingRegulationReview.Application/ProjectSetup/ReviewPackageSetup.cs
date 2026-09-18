using System;
using BuildingRegulationReview.Domain.ReviewPackages;

namespace BuildingRegulationReview.Application.ProjectSetup;

public sealed class ReviewPackageSetupSelection
{
    public ReviewPackageSetupSelection(string sourceFloorPlanUniqueId, string levelUniqueId, string areaSchemeUniqueId,
        string? areaPlanTemplateUniqueId, bool copyCropSettings, string? scopeBoxUniqueId)
    {
        SourceFloorPlanUniqueId = sourceFloorPlanUniqueId;
        LevelUniqueId = levelUniqueId;
        AreaSchemeUniqueId = areaSchemeUniqueId;
        AreaPlanTemplateUniqueId = areaPlanTemplateUniqueId;
        CopyCropSettings = copyCropSettings;
        ScopeBoxUniqueId = scopeBoxUniqueId;
    }
    public string SourceFloorPlanUniqueId { get; }
    public string LevelUniqueId { get; }
    public string AreaSchemeUniqueId { get; }
    public string? AreaPlanTemplateUniqueId { get; }
    public bool CopyCropSettings { get; }
    public string? ScopeBoxUniqueId { get; }
}

public static class ReviewPackageSetup
{
    public static ReviewPackage Create(ReviewPackageSetupSelection selection, Guid? packageId = null, DateTime? updatedAtUtc = null)
    {
        if (selection is null) throw new ArgumentNullException(nameof(selection));
        return new ReviewPackage(
            packageId ?? Guid.NewGuid(),
            selection.SourceFloorPlanUniqueId,
            selection.LevelUniqueId,
            selection.AreaSchemeUniqueId,
            status: ReviewPackageStatus.Setup,
            updatedAtUtc: updatedAtUtc);
    }
}
