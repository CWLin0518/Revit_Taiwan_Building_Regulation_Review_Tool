using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace BuildingRegulationReview.Domain.ReviewPackages;

public enum ReviewPackageStatus
{
    Setup,
    BoundaryDraft,
    Ready,
    Reviewed,
    Documented,
    Stale,
    Error
}

public sealed class ReviewPackage
{
    public const string CurrentSchemaVersion = "1.0";

    public ReviewPackage(
        Guid packageId,
        string sourceFloorPlanUniqueId,
        string levelUniqueId,
        string areaSchemeUniqueId,
        string? areaPlanUniqueId = null,
        string? draftingViewUniqueId = null,
        IEnumerable<string>? legendViewUniqueIds = null,
        string? sheetUniqueId = null,
        IEnumerable<string>? generatedElementUniqueIds = null,
        int boundaryRevision = 0,
        string? ruleSetId = null,
        string? ruleSetVersion = null,
        string? lastReviewRunId = null,
        ReviewPackageStatus status = ReviewPackageStatus.Setup,
        DateTime? updatedAtUtc = null)
    {
        if (packageId == Guid.Empty) throw new ArgumentException("Package ID cannot be empty.", nameof(packageId));
        if (string.IsNullOrWhiteSpace(sourceFloorPlanUniqueId)) throw new ArgumentException("Source floor plan UniqueId is required.", nameof(sourceFloorPlanUniqueId));
        if (string.IsNullOrWhiteSpace(levelUniqueId)) throw new ArgumentException("Level UniqueId is required.", nameof(levelUniqueId));
        if (string.IsNullOrWhiteSpace(areaSchemeUniqueId)) throw new ArgumentException("Area scheme UniqueId is required.", nameof(areaSchemeUniqueId));
        if (boundaryRevision < 0) throw new ArgumentOutOfRangeException(nameof(boundaryRevision));

        PackageId = packageId;
        SourceFloorPlanUniqueId = sourceFloorPlanUniqueId.Trim();
        LevelUniqueId = levelUniqueId.Trim();
        AreaSchemeUniqueId = areaSchemeUniqueId.Trim();
        AreaPlanUniqueId = Normalize(areaPlanUniqueId);
        DraftingViewUniqueId = Normalize(draftingViewUniqueId);
        LegendViewUniqueIds = NormalizeIds(legendViewUniqueIds);
        SheetUniqueId = Normalize(sheetUniqueId);
        GeneratedElementUniqueIds = NormalizeIds(generatedElementUniqueIds);
        BoundaryRevision = boundaryRevision;
        RuleSetId = Normalize(ruleSetId);
        RuleSetVersion = Normalize(ruleSetVersion);
        LastReviewRunId = Normalize(lastReviewRunId);
        Status = status;
        UpdatedAtUtc = (updatedAtUtc ?? DateTime.UtcNow).ToUniversalTime();
    }

    public Guid PackageId { get; }
    public string SchemaVersion => CurrentSchemaVersion;
    public string SourceFloorPlanUniqueId { get; }
    public string LevelUniqueId { get; }
    public string AreaSchemeUniqueId { get; }
    public string? AreaPlanUniqueId { get; }
    public string? DraftingViewUniqueId { get; }
    public IReadOnlyList<string> LegendViewUniqueIds { get; }
    public string? SheetUniqueId { get; }
    public IReadOnlyList<string> GeneratedElementUniqueIds { get; }
    public int BoundaryRevision { get; }
    public string? RuleSetId { get; }
    public string? RuleSetVersion { get; }
    public string? LastReviewRunId { get; }
    public ReviewPackageStatus Status { get; }
    public DateTime UpdatedAtUtc { get; }

    public ReviewPackage WithAreaPlan(string areaPlanUniqueId, DateTime? updatedAtUtc = null) => new ReviewPackage(
        PackageId, SourceFloorPlanUniqueId, LevelUniqueId, AreaSchemeUniqueId,
        areaPlanUniqueId, DraftingViewUniqueId, LegendViewUniqueIds, SheetUniqueId,
        GeneratedElementUniqueIds, BoundaryRevision, RuleSetId, RuleSetVersion,
        LastReviewRunId, Status, updatedAtUtc);

    /// <summary>
    /// Records the Drafting View holding the 單線圖 copies (spec 10.5 item 5). The UniqueId is the
    /// 唯一識別 that clause asks for: the view is found by it on every later run, so a user who
    /// renames the view keeps the name and the package keeps the view.
    /// </summary>
    public ReviewPackage WithDraftingView(string draftingViewUniqueId, DateTime? updatedAtUtc = null)
    {
        if (string.IsNullOrWhiteSpace(draftingViewUniqueId))
            throw new ArgumentException("Drafting view UniqueId is required.", nameof(draftingViewUniqueId));

        return new ReviewPackage(
            PackageId, SourceFloorPlanUniqueId, LevelUniqueId, AreaSchemeUniqueId,
            AreaPlanUniqueId, draftingViewUniqueId, LegendViewUniqueIds, SheetUniqueId,
            GeneratedElementUniqueIds, BoundaryRevision, RuleSetId, RuleSetVersion,
            LastReviewRunId, Status, updatedAtUtc);
    }

    /// <summary>
    /// Moves the package to the state one pass through the review left it in (spec 13). The status
    /// and the boundary revision travel together because they answer one question between them:
    /// what the package is, and which version of its boundaries that answer is about.
    /// </summary>
    /// <remarks>
    /// Nothing here decides which status is right — <c>ReviewPackageProgress</c> and
    /// <c>ReviewStaleness</c> in the Application layer do, because the rule depends on what a
    /// write-back reported and the domain object has no way to know that.
    /// </remarks>
    public ReviewPackage WithProgress(
        ReviewPackageStatus status,
        int? boundaryRevision = null,
        DateTime? updatedAtUtc = null)
    {
        if (!Enum.IsDefined(typeof(ReviewPackageStatus), status))
            throw new ArgumentOutOfRangeException(nameof(status));
        if (boundaryRevision is not null && boundaryRevision.Value < BoundaryRevision)
            throw new ArgumentOutOfRangeException(
                nameof(boundaryRevision),
                "A boundary revision only ever moves forward.");

        return new ReviewPackage(
            PackageId, SourceFloorPlanUniqueId, LevelUniqueId, AreaSchemeUniqueId,
            AreaPlanUniqueId, DraftingViewUniqueId, LegendViewUniqueIds, SheetUniqueId,
            GeneratedElementUniqueIds, boundaryRevision ?? BoundaryRevision, RuleSetId, RuleSetVersion,
            LastReviewRunId, status, updatedAtUtc);
    }

    /// <summary>The same package with its boundary revision advanced by one (spec 13.2).</summary>
    public ReviewPackage WithNextBoundaryRevision(
        ReviewPackageStatus status,
        DateTime? updatedAtUtc = null) =>
        WithProgress(status, BoundaryRevision + 1, updatedAtUtc);

    private static string? Normalize(string? value) => value is null || string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static IReadOnlyList<string> NormalizeIds(IEnumerable<string>? values) =>
        new ReadOnlyCollection<string>((values ?? Array.Empty<string>())
            .Select(Normalize).Where(x => x is not null).Select(x => x!)
            .Distinct(StringComparer.Ordinal).ToList());
}
