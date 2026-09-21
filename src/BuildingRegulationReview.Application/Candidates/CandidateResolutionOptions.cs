using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using BuildingRegulationReview.Domain.Geometry;

namespace BuildingRegulationReview.Application.Candidates;

/// <summary>
/// Which plan relations make a member a candidate for its category. Spec 11.5 requires the strategy
/// to be configurable per category; an ambiguous relation is always kept, because dropping it would
/// hide exactly the element a reviewer has to look at.
/// </summary>
[Flags]
public enum CandidateRelationKinds
{
    None = 0,
    Boundary = 1,
    Crossing = 2,
    Inside = 4,
    All = Boundary | Crossing | Inside
}

/// <summary>The measuring rules of <see cref="CandidateResolver"/>. Lengths are decimal feet.</summary>
public sealed class CandidateResolutionOptions
{
    public static readonly CandidateResolutionOptions Default = new CandidateResolutionOptions();

    private readonly Dictionary<CandidateCategory, CandidateRelationKinds> _memberRelations;

    /// <param name="boundaryToleranceFeet">
    /// How far a centreline may sit from the Area boundary and still be the line it was drawn from.
    /// The default matches the gap-extension tolerance, the furthest P2 repair can move a boundary.
    /// </param>
    /// <param name="parallelDegrees">Direction difference under which a member runs along a boundary edge.</param>
    /// <param name="minimumRelationLengthFeet">
    /// The shortest length that counts as running along, inside or outside a zone. It keeps a wall
    /// that merely abuts a boundary from reading as a boundary wall.
    /// </param>
    /// <param name="openingSearchFeet">
    /// How close to a boundary an opening without a usable host must be before it is sent to manual
    /// review rather than ignored.
    /// </param>
    /// <param name="memberRelations">Per-category override of <see cref="CandidateRelationKinds.All"/>.</param>
    /// <param name="includeInteriorOpenings">
    /// Whether openings inside a zone, not on its boundary, are candidates too. Off by default: spec
    /// 11.6 takes boundary walls and their openings as the primary candidates.
    /// </param>
    public CandidateResolutionOptions(
        double? boundaryToleranceFeet = null,
        double parallelDegrees = 5.0,
        double? minimumRelationLengthFeet = null,
        double? openingSearchFeet = null,
        IEnumerable<KeyValuePair<CandidateCategory, CandidateRelationKinds>>? memberRelations = null,
        bool includeInteriorOpenings = false)
    {
        BoundaryToleranceFeet = Positive(boundaryToleranceFeet ?? GeometryTolerance.Default.GapExtensionFeet, nameof(boundaryToleranceFeet));
        MinimumRelationLengthFeet = Positive(minimumRelationLengthFeet ?? PlanUnits.MillimetersToFeet(200), nameof(minimumRelationLengthFeet));
        OpeningSearchFeet = Positive(openingSearchFeet ?? PlanUnits.MillimetersToFeet(300), nameof(openingSearchFeet));
        if (double.IsNaN(parallelDegrees) || parallelDegrees <= 0 || parallelDegrees >= 45)
            throw new ArgumentOutOfRangeException(nameof(parallelDegrees), "The parallel tolerance must lie between 0 and 45 degrees.");
        if (OpeningSearchFeet < BoundaryToleranceFeet)
            throw new ArgumentException("The opening search distance cannot be tighter than the boundary tolerance.", nameof(openingSearchFeet));

        ParallelRadians = parallelDegrees * Math.PI / 180.0;
        IncludeInteriorOpenings = includeInteriorOpenings;

        _memberRelations = CandidateCategories.Members.ToDictionary(c => c, _ => CandidateRelationKinds.All);
        foreach (var pair in memberRelations ?? Array.Empty<KeyValuePair<CandidateCategory, CandidateRelationKinds>>())
        {
            if (!CandidateCategories.IsMember(pair.Key))
                throw new ArgumentException($"{pair.Key} is not a member category.", nameof(memberRelations));
            if ((pair.Value & ~CandidateRelationKinds.All) != 0)
                throw new ArgumentOutOfRangeException(nameof(memberRelations), $"Unknown relation flags for {pair.Key}.");
            _memberRelations[pair.Key] = pair.Value;
        }

        MemberRelations = new ReadOnlyDictionary<CandidateCategory, CandidateRelationKinds>(_memberRelations);
    }

    public double BoundaryToleranceFeet { get; }
    public double ParallelRadians { get; }
    public double ParallelDegrees => ParallelRadians * 180.0 / Math.PI;
    public double MinimumRelationLengthFeet { get; }
    public double OpeningSearchFeet { get; }
    public bool IncludeInteriorOpenings { get; }
    public IReadOnlyDictionary<CandidateCategory, CandidateRelationKinds> MemberRelations { get; }

    public CandidateRelationKinds RelationsFor(CandidateCategory category) =>
        _memberRelations.TryGetValue(category, out var kinds)
            ? kinds
            : throw new ArgumentOutOfRangeException(nameof(category), "Not a member category.");

    /// <summary>A category switched off entirely is not read at all, ambiguities included.</summary>
    public bool Reviews(CandidateCategory category) => RelationsFor(category) != CandidateRelationKinds.None;

    private static double Positive(double value, string name)
    {
        if (double.IsNaN(value) || double.IsInfinity(value) || value <= 0)
            throw new ArgumentOutOfRangeException(name, "The distance must be a finite, positive number.");
        return value;
    }
}
