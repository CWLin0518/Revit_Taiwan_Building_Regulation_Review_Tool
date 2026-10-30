using System;
using BuildingRegulationReview.Application.Diagnostics;
using BuildingRegulationReview.Domain.ReviewPackages;
using BuildingRegulationReview.Domain.Reviews;

namespace BuildingRegulationReview.Application.Reviews;

/// <summary>
/// A stored run judged against the model as it is now: what the review table shows when the model
/// is reopened (spec 13.1), and what has to be stored back — overrides on stale results await
/// confirmation (spec 11.8) and a Reviewed package with stale results becomes Stale.
/// </summary>
public sealed class StoredRunInspection
{
    private StoredRunInspection(
        ReviewRun run,
        ReviewRunFreshness freshness,
        ReviewPackage package,
        bool runChanged,
        bool packageChanged,
        ReviewLog log)
    {
        Run = run;
        Freshness = freshness;
        Package = package;
        RunChanged = runChanged;
        PackageChanged = packageChanged;
        Log = log;
        Table = ReviewTable.Build(run, freshness);
    }

    /// <summary>The run to show and, when <see cref="RunChanged"/>, to store.</summary>
    public ReviewRun Run { get; }

    public ReviewRunFreshness Freshness { get; }

    /// <summary>The package to show and, when <see cref="PackageChanged"/>, to store.</summary>
    public ReviewPackage Package { get; }

    public bool RunChanged { get; }
    public bool PackageChanged { get; }
    public bool NeedsSaving => RunChanged || PackageChanged;
    public ReviewTable Table { get; }
    public ReviewLog Log { get; }

    /// <param name="currentBaseline">Built from the model now, with the same readers the run used.</param>
    /// <param name="ruleSetId">The rule set the add-in offers now.</param>
    /// <param name="ruleSetVersion">Its version now: a newer version than the run's makes every result stale.</param>
    public static StoredRunInspection Inspect(
        ReviewPackage package,
        ReviewRun run,
        ReviewBaseline currentBaseline,
        string ruleSetId,
        string ruleSetVersion,
        DateTime? nowUtc = null)
    {
        if (package is null) throw new ArgumentNullException(nameof(package));
        if (run is null) throw new ArgumentNullException(nameof(run));
        if (run.PackageId != package.PackageId) throw new ArgumentException("The run belongs to another package.", nameof(run));

        var freshness = ReviewRunValidity.Evaluate(run, currentBaseline, ruleSetId, ruleSetVersion, package.BoundaryRevision);
        var suspended = ReviewRunValidity.WithOverridesSuspended(freshness);
        var updatedPackage = ReviewRunValidity.ApplyTo(package, freshness, nowUtc);
        var inspected = ReferenceEquals(suspended, run)
            ? freshness
            : ReviewRunValidity.Evaluate(suspended, currentBaseline, ruleSetId, ruleSetVersion, package.BoundaryRevision);

        return new StoredRunInspection(
            suspended,
            inspected,
            updatedPackage,
            !ReferenceEquals(suspended, run),
            updatedPackage.Status != package.Status,
            freshness.IsStale ? ReviewRunValidity.Explain(freshness, nowUtc) : ReviewLog.Empty(package.PackageId));
    }
}
