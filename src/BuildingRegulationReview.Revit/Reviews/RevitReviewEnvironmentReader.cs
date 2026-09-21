using System;
using System.Globalization;
using System.Linq;
using Autodesk.Revit.DB;
using BuildingRegulationReview.Application.Candidates;
using BuildingRegulationReview.Application.Reviews;
using BuildingRegulationReview.Domain.ReviewPackages;

namespace BuildingRegulationReview.Revit.Reviews;

/// <summary>
/// P3-T09: the project-wide facts a run is computed under (spec 13.1 — 來源視圖／Level、Area Scheme、
/// 專案 Phase／Design Option、幾何容差、專案單位) and the conditions spec 11.1 asks about. Read-only.
/// </summary>
public sealed class RevitReviewEnvironmentReader
{
    private readonly Document _document;

    public RevitReviewEnvironmentReader(Document document) =>
        _document = document ?? throw new ArgumentNullException(nameof(document));

    public ReviewEnvironment ReadEnvironment(ReviewPackage package, CandidateResolutionOptions? options = null)
    {
        if (package is null) throw new ArgumentNullException(nameof(package));
        options ??= CandidateResolutionOptions.Default;

        var view = _document.GetElement(package.SourceFloorPlanUniqueId) as View;
        var areaPlan = package.AreaPlanUniqueId is null ? null : _document.GetElement(package.AreaPlanUniqueId) as ViewPlan;
        var phase = PhaseOf(view);
        var primaryOptions = new FilteredElementCollector(_document).OfClass(typeof(DesignOption)).Cast<DesignOption>()
            .Where(o => o.IsPrimary).Select(o => o.UniqueId).OrderBy(x => x, StringComparer.Ordinal).ToList();
        var units = _document.GetUnits();

        return ReviewEnvironment.Of(
            (ReviewEnvironment.SourceViewUniqueId, view?.UniqueId ?? "missing:" + package.SourceFloorPlanUniqueId),
            (ReviewEnvironment.LevelUniqueId, package.LevelUniqueId),
            (ReviewEnvironment.AreaSchemeUniqueId, areaPlan?.AreaScheme?.UniqueId ?? package.AreaSchemeUniqueId),
            (ReviewEnvironment.Phase, phase?.UniqueId),
            (ReviewEnvironment.DesignOption, primaryOptions.Count == 0 ? null : "primary:" + string.Join(",", primaryOptions)),
            (ReviewEnvironment.GeometryTolerance, string.Format(CultureInfo.InvariantCulture,
                "boundary={0:0.######};parallel={1:0.######};minLength={2:0.######};openingSearch={3:0.######};interior={4}",
                options.BoundaryToleranceFeet, options.ParallelDegrees, options.MinimumRelationLengthFeet,
                options.OpeningSearchFeet, options.IncludeInteriorOpenings)),
            (ReviewEnvironment.ProjectUnits,
                units.GetFormatOptions(SpecTypeId.Length).GetUnitTypeId().TypeId + ";" +
                units.GetFormatOptions(SpecTypeId.Area).GetUnitTypeId().TypeId));
    }

    public ReviewModelConditions ReadConditions(ReviewPackage package)
    {
        if (package is null) throw new ArgumentNullException(nameof(package));

        var units = _document.GetUnits();
        return new ReviewModelConditions(
            PhaseOf(_document.GetElement(package.SourceFloorPlanUniqueId) as View)?.Name,
            new FilteredElementCollector(_document).OfClass(typeof(DesignOption)).GetElementCount() > 0,
            new FilteredElementCollector(_document).OfClass(typeof(RevitLinkInstance)).GetElementCount(),
            LabelUtils.GetLabelForUnit(units.GetFormatOptions(SpecTypeId.Length).GetUnitTypeId()),
            LabelUtils.GetLabelForUnit(units.GetFormatOptions(SpecTypeId.Area).GetUnitTypeId()));
    }

    private Phase? PhaseOf(View? view)
    {
        var id = view?.get_Parameter(BuiltInParameter.VIEW_PHASE)?.AsElementId();
        return id is null || id == ElementId.InvalidElementId ? null : _document.GetElement(id) as Phase;
    }
}
