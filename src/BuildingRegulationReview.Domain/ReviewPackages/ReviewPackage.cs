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

    private static string? Normalize(string? value) => value is null || string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static IReadOnlyList<string> NormalizeIds(IEnumerable<string>? values) =>
        new ReadOnlyCollection<string>((values ?? Array.Empty<string>())
            .Select(Normalize).Where(x => x is not null).Select(x => x!)
            .Distinct(StringComparer.Ordinal).ToList());
}
