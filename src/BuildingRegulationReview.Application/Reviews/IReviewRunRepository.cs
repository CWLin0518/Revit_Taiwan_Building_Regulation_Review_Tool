using System;
using System.Collections.Generic;
using System.Linq;
using BuildingRegulationReview.Domain.Reviews;

namespace BuildingRegulationReview.Application.Reviews;

/// <summary>
/// Where review runs live between sessions (spec 17.1 DataStorage 持久化). A run is saved whole —
/// results, evidence baseline and override audit trail — so reopening the model gives back exactly
/// what was reviewed and by whom.
/// </summary>
public interface IReviewRunRepository
{
    ReviewRun? Get(Guid runId);

    /// <summary>Every run of the package, oldest first.</summary>
    IReadOnlyList<ReviewRun> GetForPackage(Guid packageId);

    void Save(ReviewRun run);

    bool Delete(Guid runId);
}

public static class ReviewRunRepositoryExtensions
{
    /// <summary>The package's most recently started run, or null when it has none.</summary>
    public static ReviewRun? GetLatest(this IReviewRunRepository repository, Guid packageId)
    {
        if (repository is null) throw new ArgumentNullException(nameof(repository));
        return repository.GetForPackage(packageId)
            .OrderBy(x => x.StartedAtUtc).ThenBy(x => x.RunId)
            .LastOrDefault();
    }
}
