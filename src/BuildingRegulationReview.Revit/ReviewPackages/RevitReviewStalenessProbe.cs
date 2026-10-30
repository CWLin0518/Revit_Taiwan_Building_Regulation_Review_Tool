using System;
using Autodesk.Revit.DB;
using BuildingRegulationReview.Application.ReviewPackages;
using BuildingRegulationReview.Application.WriteBack;
using BuildingRegulationReview.Domain.Geometry;
using BuildingRegulationReview.Domain.ReviewPackages;
using BuildingRegulationReview.Revit.WriteBack;

namespace BuildingRegulationReview.Revit.ReviewPackages;

/// <summary>
/// Looks at the model a package points at and reports what is there (spec 13.1). Read-only: it
/// opens no transaction and decides nothing — <see cref="ReviewStaleness"/> turns the observation
/// into a verdict.
/// </summary>
/// <remarks>
/// The only interesting part is how a changed element is recognised. Every element the tool wrote
/// carries the signature it was written with, and that signature is a quantized spelling of the
/// geometry, so recomputing it from the element as it stands now and comparing the two answers both
/// halves of spec 13.1 at once: a boundary somebody dragged no longer matches, and a project whose
/// geometry tolerance changed no longer matches either, because the tolerance is what sets the
/// quantum.
/// </remarks>
public sealed class RevitReviewStalenessProbe
{
    private readonly Document _document;
    private readonly GeometryTolerance _tolerance;

    public RevitReviewStalenessProbe(Document document, GeometryTolerance? tolerance = null)
    {
        _document = document ?? throw new ArgumentNullException(nameof(document));
        _tolerance = tolerance ?? GeometryTolerance.Default;
    }

    /// <summary>Observes one package. Never throws for a missing element; that is the observation.</summary>
    public ReviewModelObservation Observe(ReviewPackage package, string? ruleSetVersion = null)
    {
        if (package is null) throw new ArgumentNullException(nameof(package));

        // The same identity judgement the picker uses, not a bare `as ViewPlan`: a reference that now
        // resolves to a floor plan or to a view template would otherwise be accepted as this
        // package's Area Plan, and the staleness report would describe somebody else's view.
        var referenced = _document.GetElement(package.AreaPlanUniqueId ?? string.Empty);
        var areaPlan = RevitAreaPlanProbe.IsLiveAreaPlan(referenced) ? (ViewPlan)referenced : null;
        var changed = 0;
        var unreadable = 0;
        var managed = 0;

        if (areaPlan is not null)
        {
            foreach (var element in new RevitManagedElementInventory(_document)
                         .Read(package.AreaPlanUniqueId!, package.DraftingViewUniqueId))
            {
                if (!element.BelongsTo(package.PackageId))
                {
                    // Somebody else's, or a token that no longer parses. Only the second is this
                    // package's problem, and only when the element sits in this package's views.
                    if (!element.HasKey) unreadable++;
                    continue;
                }

                managed++;
                if (HasDrifted(element)) changed++;
            }
        }

        return new ReviewModelObservation(
            sourceFloorPlanFound: Exists(package.SourceFloorPlanUniqueId),
            levelFound: Exists(package.LevelUniqueId),
            areaSchemeFound: Exists(package.AreaSchemeUniqueId),
            areaPlanFound: areaPlan is not null,
            draftingViewFound: package.DraftingViewUniqueId is null
                ? (bool?)null
                : _document.GetElement(package.DraftingViewUniqueId) is ViewDrafting,
            areaPlanLevelUniqueId: UniqueIdOf(areaPlan?.GenLevel),
            areaPlanAreaSchemeUniqueId: UniqueIdOf(AreaSchemeOf(areaPlan)),
            managedElementCount: managed,
            changedManagedElementCount: changed,
            unreadableManagedElementCount: unreadable,
            ruleSetVersion: ruleSetVersion);
    }

    /// <summary>
    /// Whether the element still matches the signature written on it. Only the kinds whose geometry
    /// can be re-read are compared; a tag carries nothing the probe could recompute, so it is left
    /// alone rather than guessed at.
    /// </summary>
    private bool HasDrifted(ExistingManagedElement managed)
    {
        if (string.IsNullOrEmpty(managed.Signature)) return false;

        var element = _document.GetElement(managed.ElementUniqueId);
        if (element is null) return false;

        switch (managed.Key.Kind)
        {
            case ManagedElementKind.AreaBoundaryLine:
            case ManagedElementKind.DetailCurve:
                return CurveHasDrifted(element, managed.Signature);

            case ManagedElementKind.Area:
                // The colour is not on the element, so only the name half is comparable; a renamed
                // Area is exactly the edit this check exists to notice.
                var expected = PlannedElementSignature.ZoneNameOf(managed.Signature);
                if (expected is null) return false;
                var actual = element.get_Parameter(BuiltInParameter.ROOM_NAME)?.AsString();
                return !string.Equals(expected, actual ?? string.Empty, StringComparison.Ordinal);

            default:
                return false;
        }
    }

    private bool CurveHasDrifted(Element element, string signature)
    {
        if (!(element.Location is LocationCurve location) || !(location.Curve is Line line)) return false;

        var start = new Point2D(line.GetEndPoint(0).X, line.GetEndPoint(0).Y);
        var end = new Point2D(line.GetEndPoint(1).X, line.GetEndPoint(1).Y);
        var current = PlannedElementSignature.ForCanonicalSegment(start, end, _tolerance.ClosureFeet);
        return !string.Equals(current, signature, StringComparison.Ordinal);
    }

    private bool Exists(string? uniqueId) =>
        !string.IsNullOrWhiteSpace(uniqueId) && _document.GetElement(uniqueId) is not null;

    private static string? UniqueIdOf(Element? element) => element?.UniqueId;

    private static AreaScheme? AreaSchemeOf(ViewPlan? areaPlan) => areaPlan?.AreaScheme;
}
