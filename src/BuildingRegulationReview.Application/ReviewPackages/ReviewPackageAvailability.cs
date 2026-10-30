using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using BuildingRegulationReview.Domain.ReviewPackages;

namespace BuildingRegulationReview.Application.ReviewPackages;

/// <summary>Whether a package still has the Area Plan a panel would have to open.</summary>
public enum ReviewPackageAvailabilityState
{
    /// <summary>No Area Plan recorded yet: setup has saved the package but not provisioned its plan.</summary>
    AwaitingAreaPlan,

    /// <summary>An Area Plan was recorded and is no longer in the model — the user deleted it.</summary>
    AreaPlanDeleted,

    /// <summary>The recorded Area Plan is still there, so the package can be worked on.</summary>
    Available
}

/// <summary>How a list of packages splits into the ones a panel may offer and the ones it may not.</summary>
public sealed class ReviewPackageSelection
{
    internal ReviewPackageSelection(
        IEnumerable<ReviewPackage> available,
        IEnumerable<ReviewPackage> awaitingAreaPlan,
        IEnumerable<ReviewPackage> areaPlanDeleted)
    {
        Available = new ReadOnlyCollection<ReviewPackage>(available.ToList());
        AwaitingAreaPlan = new ReadOnlyCollection<ReviewPackage>(awaitingAreaPlan.ToList());
        AreaPlanDeleted = new ReadOnlyCollection<ReviewPackage>(areaPlanDeleted.ToList());
    }

    /// <summary>The packages a picker lists, in the order they arrived.</summary>
    public IReadOnlyList<ReviewPackage> Available { get; }

    /// <summary>Packages setup has not finished; a picker leaves them out but nothing is wrong.</summary>
    public IReadOnlyList<ReviewPackage> AwaitingAreaPlan { get; }

    /// <summary>
    /// Packages whose Area Plan the user deleted. They stay in the model and stay recoverable:
    /// <c>RevitAreaPlanProvisioner</c> reuses the same package when「防火區劃設定」runs again, so the
    /// 單線圖 view, legends, sheet and review history all reconnect to the new plan.
    /// </summary>
    public IReadOnlyList<ReviewPackage> AreaPlanDeleted { get; }

    /// <summary>
    /// What to tell the user about the packages that were left out, or null when there is nothing to
    /// say. Only a deleted Area Plan earns a line: a package still awaiting one is mid-setup, and
    /// the picker's own「請先執行防火區劃設定」already covers that.
    /// </summary>
    /// <remarks>
    /// It says 「已不存在」, not 「已被刪除」. Deleting the view is much the commonest way to get here,
    /// but the probe cannot tell that from a reference that no longer resolves, and a sentence that
    /// states what the user did is one that will sometimes be wrong about it.
    /// </remarks>
    public string? HiddenNotice => AreaPlanDeleted.Count == 0
        ? null
        : string.Format(
            CultureInfo.InvariantCulture,
            "有 {0} 個檢討套件的 Area Plan 已不存在（通常是被刪除了），已不列出。重新執行「防火區劃設定」會為它們重建 Area Plan，並接回原有的單線圖與檢討紀錄。",
            AreaPlanDeleted.Count);
}

/// <summary>
/// Which packages a panel may offer the user. Deleting an Area Plan does not delete the package that
/// points at it — the package lives in a DataStorage (spec 8「關聯資料應存於 DataStorage +
/// Extensible Storage」) and outlives the view — so every picker has to ask this before listing one.
/// </summary>
/// <remarks>
/// The judgement is here rather than in the repository on purpose. The repository reports what is
/// stored, and「防火區劃設定」<em>needs</em> to see a package whose plan is gone: that is how spec
/// 附錄 9.5「刪除後修復」works, because <c>RevitAreaPlanProvisioner.Provision</c> reuses the package
/// and gives it a new Area Plan. A repository that filtered silently would make setup create a
/// second package for the same floor plan and area scheme, and leave the first one orphaned for good.
/// <para>
/// Whether a plan is still there is the adapter's business, so it arrives as a predicate over the
/// recorded UniqueId. Nothing here touches Revit, which is what makes the rule assertable with no
/// document open.
/// </para>
/// </remarks>
public static class ReviewPackageAvailability
{
    /// <summary>Judges one package. The predicate is asked at most once, and only when there is an id.</summary>
    public static ReviewPackageAvailabilityState Classify(
        ReviewPackage package,
        Func<string, bool> areaPlanIsLive)
    {
        if (package is null) throw new ArgumentNullException(nameof(package));
        if (areaPlanIsLive is null) throw new ArgumentNullException(nameof(areaPlanIsLive));

        var areaPlanUniqueId = package.AreaPlanUniqueId;
        if (string.IsNullOrWhiteSpace(areaPlanUniqueId)) return ReviewPackageAvailabilityState.AwaitingAreaPlan;

        return areaPlanIsLive(areaPlanUniqueId!)
            ? ReviewPackageAvailabilityState.Available
            : ReviewPackageAvailabilityState.AreaPlanDeleted;
    }

    /// <summary>
    /// Splits a stored list into what a picker shows and what it has to explain. One pass, so the
    /// predicate — a Revit element lookup — is paid for once per package.
    /// </summary>
    public static ReviewPackageSelection Partition(
        IEnumerable<ReviewPackage>? packages,
        Func<string, bool> areaPlanIsLive)
    {
        if (areaPlanIsLive is null) throw new ArgumentNullException(nameof(areaPlanIsLive));

        var available = new List<ReviewPackage>();
        var awaiting = new List<ReviewPackage>();
        var deleted = new List<ReviewPackage>();

        foreach (var package in packages ?? Enumerable.Empty<ReviewPackage>())
        {
            if (package is null) continue;
            switch (Classify(package, areaPlanIsLive))
            {
                case ReviewPackageAvailabilityState.Available: available.Add(package); break;
                case ReviewPackageAvailabilityState.AwaitingAreaPlan: awaiting.Add(package); break;
                default: deleted.Add(package); break;
            }
        }

        return new ReviewPackageSelection(available, awaiting, deleted);
    }
}
