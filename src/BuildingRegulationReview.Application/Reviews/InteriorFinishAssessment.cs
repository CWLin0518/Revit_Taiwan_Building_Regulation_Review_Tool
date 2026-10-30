using System;
using System.Collections.Generic;
using System.Linq;
using BuildingRegulationReview.Application.Parameters;

namespace BuildingRegulationReview.Application.Reviews;

/// <summary>One wall or ceiling finish system that actually bounds a fire compartment.</summary>
public sealed class InteriorFinishSurface
{
    public InteriorFinishSurface(string elementUniqueId, string category, string? grade)
    {
        if (string.IsNullOrWhiteSpace(elementUniqueId)) throw new ArgumentException("Element UniqueId is required.", nameof(elementUniqueId));
        ElementUniqueId = elementUniqueId.Trim();
        Category = string.IsNullOrWhiteSpace(category) ? "構件" : category.Trim();
        Grade = string.IsNullOrWhiteSpace(grade) ? null : grade!.Trim();
    }

    public string ElementUniqueId { get; }
    public string Category { get; }
    public string? Grade { get; }
}

/// <summary>
/// Derives the single fact consumed by Article 83 from modelled wall and ceiling finish systems.
/// The weakest system governs. Missing or unrecognised data is not guessed and produces no grade,
/// so the rule engine reports InsufficientData.
/// </summary>
public static class InteriorFinishAssessment
{
    public static string? Derive(IEnumerable<InteriorFinishSurface>? surfaces)
    {
        var list = (surfaces ?? Array.Empty<InteriorFinishSurface>()).ToList();
        if (list.Count == 0 || list.Any(x => !InteriorFinishGrades.IsKnown(x.Grade))) return null;
        if (list.Any(x => string.Equals(x.Grade, InteriorFinishGrades.None, StringComparison.Ordinal)))
            return InteriorFinishGrades.None;
        if (list.Any(x => string.Equals(x.Grade, InteriorFinishGrades.ClassOne, StringComparison.Ordinal)))
            return InteriorFinishGrades.ClassOne;
        return InteriorFinishGrades.ClassOneWithSubstrate;
    }
}
