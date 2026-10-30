using System;
using System.Collections.Generic;
using BuildingRegulationReview.Domain.ReviewPackages;

namespace BuildingRegulationReview.Application.ProjectSetup;

public sealed class AreaPlanProvisioningResult
{
    public AreaPlanProvisioningResult(ReviewPackage package, bool created, IReadOnlyList<string>? warnings = null)
    {
        Package = package ?? throw new ArgumentNullException(nameof(package));
        Created = created;
        Warnings = warnings ?? Array.Empty<string>();
    }

    public ReviewPackage Package { get; }
    public bool Created { get; }
    public IReadOnlyList<string> Warnings { get; }
}

public static class AreaPlanProvisioning
{
    public static bool CanReuse(ReviewPackage package, Func<string, bool> isUsableAreaPlan)
    {
        if (package is null) throw new ArgumentNullException(nameof(package));
        if (isUsableAreaPlan is null) throw new ArgumentNullException(nameof(isUsableAreaPlan));
        return package.AreaPlanUniqueId is not null && isUsableAreaPlan(package.AreaPlanUniqueId);
    }

    public static ReviewPackage Complete(ReviewPackage package, string areaPlanUniqueId, DateTime? updatedAtUtc = null)
    {
        if (package is null) throw new ArgumentNullException(nameof(package));
        if (string.IsNullOrWhiteSpace(areaPlanUniqueId))
            throw new ArgumentException("Area Plan UniqueId is required.", nameof(areaPlanUniqueId));
        return package.WithAreaPlan(areaPlanUniqueId, updatedAtUtc);
    }
}
