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
            return IsLiveAreaPlan(_document.GetElement(uniqueId));
        }
        catch (ArgumentException)
        {
            // A UniqueId Revit refuses to parse points at nothing, which is the same answer as a
            // view that was deleted. The picker is not the place to surface it.
            //
            // Only this one. Catching every exception here would turn a stale document, a call from
            // the wrong API context or any other real fault into 「這個套件的 Area Plan 已被刪除」,
            // which hides the failure behind a sentence blaming the user for something they did not
            // do. Anything else belongs to the command's own try/catch, which reports it as a fault.
            return false;
        }
    }

    /// <summary>
    /// Whether an element is a usable Area Plan — the one identity judgement, shared so that the
    /// picker, the staleness probe and anything else that has to recognise one cannot drift apart.
    /// A view template is not one: it holds settings, not a plan to draw 區劃 on.
    /// </summary>
    public static bool IsLiveAreaPlan(Element? element) =>
        element is ViewPlan view && !view.IsTemplate && view.ViewType == ViewType.AreaPlan;
}
