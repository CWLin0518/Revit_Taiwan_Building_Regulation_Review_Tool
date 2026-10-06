using System;
using Autodesk.Revit.DB;

namespace BuildingRegulationReview.Revit.ReviewPackages;

/// <summary>
/// Answers the one question <c>ReviewPackageAvailability</c> asks of the model: is the UniqueId a
/// package recorded still an Area Plan in this document?
/// </summary>
/// <remarks>
/// Deliberately looser than <c>RevitPlanGeometryExtractor</c> and <c>RevitCurtainWallGeometryReader</c>,
/// which also require <c>GenLevel</c>. A plan that exists but has no Level is unusable, not gone, and
/// a package that vanished from the picker for that reason would leave the user with nothing to act
/// on; letting it through means extraction reports the real problem by name instead.
/// </remarks>
public sealed class RevitAreaPlanProbe
{
    private readonly Document _document;

    public RevitAreaPlanProbe(Document document) =>
        _document = document ?? throw new ArgumentNullException(nameof(document));

    /// <summary>True when the UniqueId still resolves to a usable Area Plan. Never throws.</summary>
    public bool IsLiveAreaPlan(string? uniqueId)
    {
        if (string.IsNullOrWhiteSpace(uniqueId)) return false;

        try
        {
            return _document.GetElement(uniqueId) is ViewPlan view &&
                   !view.IsTemplate &&
                   view.ViewType == ViewType.AreaPlan;
        }
        catch (Exception)
        {
            // A UniqueId Revit refuses to parse points at nothing, which is the same answer as a
            // view that was deleted. The picker is not the place to surface it.
            return false;
        }
    }
}
