using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace BuildingRegulationReview.Domain.Reviews;

public enum ReviewRunState
{
    Running,
    Completed,
    Cancelled,
    Failed
}

/// <summary>
/// One pass of the review over one package (spec 11). It pins the rule set version and the
/// boundary revision the results were computed against, which is what later lets a changed model
/// or a newer rule set mark the run stale (spec 13.1).
/// </summary>
public sealed class ReviewRun
{
    public const string CurrentSchemaVersion = "1.0";

    public ReviewRun(
        Guid runId,
        Guid packageId,
        string ruleSetId,
        string ruleSetVersion,
        int boundaryRevision,
        DateTime startedAtUtc,
        ReviewRunState state = ReviewRunState.Running,
        DateTime? completedAtUtc = null,
        IEnumerable<ReviewResult>? results = null)
    {
        if (runId == Guid.Empty) throw new ArgumentException("Run ID cannot be empty.", nameof(runId));
        if (packageId == Guid.Empty) throw new ArgumentException("Package ID cannot be empty.", nameof(packageId));
        if (string.IsNullOrWhiteSpace(ruleSetId)) throw new ArgumentException("Rule set ID is required.", nameof(ruleSetId));
        if (string.IsNullOrWhiteSpace(ruleSetVersion)) throw new ArgumentException("Rule set version is required.", nameof(ruleSetVersion));
        if (boundaryRevision < 0) throw new ArgumentOutOfRangeException(nameof(boundaryRevision));
        if (!Enum.IsDefined(typeof(ReviewRunState), state)) throw new ArgumentOutOfRangeException(nameof(state));

        var started = startedAtUtc.ToUniversalTime();
        var completed = completedAtUtc?.ToUniversalTime();
        if (state == ReviewRunState.Running && completed is not null)
            throw new ArgumentException("A running review has not completed yet.", nameof(completedAtUtc));
        if (state != ReviewRunState.Running && completed is null)
            throw new ArgumentException($"A {state} review records when it ended.", nameof(completedAtUtc));
        if (completed is not null && completed.Value < started)
            throw new ArgumentException("A review cannot end before it started.", nameof(completedAtUtc));

        var list = (results ?? Array.Empty<ReviewResult>()).ToList();
        if (list.Any(x => x is null)) throw new ArgumentException("A run cannot hold a missing result.", nameof(results));
        if (list.Any(x => x.RunId != runId))
            throw new ArgumentException("Every result must belong to this run.", nameof(results));
        if (list.Any(x => x.PackageId != packageId))
            throw new ArgumentException("Every result must belong to this run's package.", nameof(results));
        if (list.Select(x => x.ResultId).Distinct().Count() != list.Count)
            throw new ArgumentException("Result IDs must be unique within a run.", nameof(results));

        RunId = runId;
        PackageId = packageId;
        RuleSetId = ruleSetId.Trim();
        RuleSetVersion = ruleSetVersion.Trim();
        BoundaryRevision = boundaryRevision;
        StartedAtUtc = started;
        State = state;
        CompletedAtUtc = completed;
        Results = new ReadOnlyCollection<ReviewResult>(list);
    }

    public Guid RunId { get; }
    public Guid PackageId { get; }
    public string SchemaVersion => CurrentSchemaVersion;
    public string RuleSetId { get; }
    public string RuleSetVersion { get; }
    public int BoundaryRevision { get; }
    public DateTime StartedAtUtc { get; }
    public ReviewRunState State { get; }
    public DateTime? CompletedAtUtc { get; }
    public IReadOnlyList<ReviewResult> Results { get; }

    public IEnumerable<ReviewResult> WithStatus(ReviewStatus status) => Results.Where(x => x.Status == status);

    /// <summary>Closes the run with the results it produced.</summary>
    public ReviewRun Complete(IEnumerable<ReviewResult> results, DateTime completedAtUtc, ReviewRunState state = ReviewRunState.Completed)
    {
        if (State != ReviewRunState.Running)
            throw new InvalidOperationException($"A {State} review cannot be closed again.");
        if (state == ReviewRunState.Running)
            throw new ArgumentOutOfRangeException(nameof(state), "Closing a run needs a final state.");

        return new ReviewRun(RunId, PackageId, RuleSetId, RuleSetVersion, BoundaryRevision,
            StartedAtUtc, state, completedAtUtc, results);
    }
}
