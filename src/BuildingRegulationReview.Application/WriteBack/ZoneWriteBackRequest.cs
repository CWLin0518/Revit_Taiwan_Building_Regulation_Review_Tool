using System;
using BuildingRegulationReview.Domain.ReviewPackages;

namespace BuildingRegulationReview.Application.WriteBack;

/// <summary>
/// Everything one write-back run needs to know beyond the plan itself (spec 10.5): which package it
/// belongs to, and what to call the outputs it may have to create.
/// </summary>
/// <remarks>
/// The package travels whole rather than as a handful of UniqueIds because the run may have to
/// record something on it — the Drafting View it created — and doing that inside the same
/// transaction group is what keeps the identity and the view either both there or both not.
/// </remarks>
public sealed class ZoneWriteBackRequest
{
    public ZoneWriteBackRequest(
        ReviewPackage package,
        string defaultOutputName,
        ApplyFailurePolicy failurePolicy = ApplyFailurePolicy.SkipAndLog)
    {
        if (package is null) throw new ArgumentNullException(nameof(package));
        if (string.IsNullOrWhiteSpace(package.AreaPlanUniqueId))
            throw new ArgumentException("A package without an Area Plan has nothing to write to.", nameof(package));
        if (string.IsNullOrWhiteSpace(defaultOutputName))
            throw new ArgumentException("A default output name is required.", nameof(defaultOutputName));

        Package = package;
        DefaultOutputName = defaultOutputName.Trim();
        FailurePolicy = failurePolicy;
    }

    public ReviewPackage Package { get; }

    public Guid PackageId => Package.PackageId;

    public string AreaPlanUniqueId => Package.AreaPlanUniqueId!;

    /// <summary>
    /// What a newly created Drafting View or colour scheme is called: spec 10.5 item 5's
    /// <c>{AreaScheme}_{SourceFloorPlan}_防火區劃</c>, built by <see cref="ReviewOutputNaming"/>.
    /// An output that already exists keeps whatever it is called now.
    /// </summary>
    public string DefaultOutputName { get; }

    public ApplyFailurePolicy FailurePolicy { get; }
}
