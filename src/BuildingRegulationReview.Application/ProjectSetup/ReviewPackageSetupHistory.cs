using System;
using System.Collections.Generic;
using System.Linq;
using BuildingRegulationReview.Domain.ReviewPackages;

namespace BuildingRegulationReview.Application.ProjectSetup;

/// <summary>
/// What the setup step last saved for each（來源樓層平面, 面積配置）pair, so the setup window can
/// preselect it instead of falling back to「（不指定）」(驗證清單 C-13). Without this the window
/// always opened on its own defaults and the Apply step wrote them straight back over the saved
/// record, which silently replaced the view template and Scope Box of an Area Plan being rebuilt
/// after a delete (附錄 9.5「刪除後修復」).
/// </summary>
/// <remarks>
/// A snapshot rather than a live lookup: the window queries this from WPF selection-changed
/// handlers, which run outside Revit's API context, so every Revit read has to happen before the
/// window opens.
/// </remarks>
public sealed class ReviewPackageSetupHistory
{
    private readonly IReadOnlyList<ReviewPackageSetupSelection> _saved;

    private ReviewPackageSetupHistory(IReadOnlyList<ReviewPackageSetupSelection> saved) => _saved = saved;

    /// <summary>A history that has nothing saved, for the first run of a document.</summary>
    public static ReviewPackageSetupHistory Empty { get; } =
        new ReviewPackageSetupHistory(Array.Empty<ReviewPackageSetupSelection>());

    /// <summary>
    /// Snapshots what <paramref name="savedOptions"/> holds for each of <paramref name="packages"/>.
    /// Packages it answers null for are left out rather than stored as defaults: that is what lets
    /// <see cref="Find"/> tell「沒有存過」apart from「存過的就是預設值」.
    /// </summary>
    public static ReviewPackageSetupHistory From(
        IEnumerable<ReviewPackage> packages,
        Func<ReviewPackage, ReviewPackageSetupSelection?> savedOptions)
    {
        if (packages is null) throw new ArgumentNullException(nameof(packages));
        if (savedOptions is null) throw new ArgumentNullException(nameof(savedOptions));
        var saved = packages
            .Where(package => package is not null)
            .Select(savedOptions)
            .Where(selection => selection is not null)
            .Select(selection => selection!)
            .ToList();
        return saved.Count == 0 ? Empty : new ReviewPackageSetupHistory(saved);
    }

    /// <summary>
    /// What was saved for this floor plan and area scheme, or null when nothing was. That pair is
    /// the key the setup window works in, because it is the pair the Apply step matches a package
    /// by. The first match wins: a second package on the same pair is the duplicate Apply refuses
    /// to process, so there is no answer to choose between them.
    /// </summary>
    public ReviewPackageSetupSelection? Find(string? sourceFloorPlanUniqueId, string? areaSchemeUniqueId)
    {
        if (string.IsNullOrWhiteSpace(sourceFloorPlanUniqueId)) return null;
        if (string.IsNullOrWhiteSpace(areaSchemeUniqueId)) return null;
        return _saved.FirstOrDefault(selection =>
            string.Equals(selection.SourceFloorPlanUniqueId, sourceFloorPlanUniqueId, StringComparison.Ordinal) &&
            string.Equals(selection.AreaSchemeUniqueId, areaSchemeUniqueId, StringComparison.Ordinal));
    }
}
