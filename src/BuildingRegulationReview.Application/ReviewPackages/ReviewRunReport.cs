using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using BuildingRegulationReview.Application.Diagnostics;
using BuildingRegulationReview.Application.WriteBack;
using BuildingRegulationReview.Domain.ReviewPackages;

namespace BuildingRegulationReview.Application.ReviewPackages;

/// <summary>
/// One whole pass through the review, as one value: what the write-back did, what that made of the
/// package's state, and the log both of them produced (spec 10.5, 10.6, 13 and 14).
/// </summary>
/// <remarks>
/// This is the seam P2-T09 was about. Before it the three answers lived in three places — the Revit
/// adapter had the result, nobody had the state, and the log existed only as strings on the result —
/// so the window had to reassemble them and the rule about Ready had nowhere to live. Building the
/// report is pure: the host runs the write-back, hands the result here, and gets back the package it
/// should save and the log it should write.
/// </remarks>
public sealed class ReviewRunReport
{
    private ReviewRunReport(
        ApplyResult result,
        ReviewPackageProgressOutcome progress,
        ReviewLog log)
    {
        Result = result;
        Progress = progress;
        Log = log;
    }

    public ApplyResult Result { get; }

    public ReviewPackageProgressOutcome Progress { get; }

    /// <summary>Everything worth recording about the run (spec 14), write-back and state together.</summary>
    public ReviewLog Log { get; }

    public Guid PackageId => Result.PackageId;

    /// <summary>The package as it should now be stored.</summary>
    public ReviewPackage Package => Progress.Package;

    /// <summary>True when the host has a package worth saving.</summary>
    public bool PackageChanged => Progress.Changed;

    public ReviewPackageStatus Status => Progress.Status;

    public bool IsReady => Progress.IsReady;

    /// <summary>Why the package is not Ready, in the user's words (spec 10.6).</summary>
    public IReadOnlyList<string> Blockers => Progress.Blockers;

    /// <summary>The two lines the status bar shows: what was written, and what the package is now.</summary>
    public string Summary => Result.Summary + " " + Progress.Message;

    /// <summary>The problem lines on their own, which is what the user has to act on.</summary>
    public IReadOnlyList<string> UserProblems => new ReadOnlyCollection<string>(
        Log.Entries
            .Where(entry => entry.Severity != ReviewSeverity.Info)
            .Select(entry => entry.UserText)
            .ToList());

    /// <summary>
    /// Assembles the report for a finished run. The package that comes back is the one to save; the
    /// caller does the saving, because only it knows whether it holds a writable transaction.
    /// </summary>
    public static ReviewRunReport For(
        ReviewPackage package,
        ApplyResult result,
        DateTime? nowUtc = null)
    {
        if (package is null) throw new ArgumentNullException(nameof(package));
        if (result is null) throw new ArgumentNullException(nameof(result));

        var at = nowUtc ?? DateTime.UtcNow;
        var progress = ReviewPackageProgress.After(package, result, at);

        var log = new ReviewLog.Builder(package.PackageId, at)
            .AddRange(ReviewLog.FromApplyResult(result, ReviewStage.WriteBack, at))
            .AddRange(ReviewPackageProgress.Explain(progress, at))
            .Build();

        return new ReviewRunReport(result, progress, log);
    }

    public override string ToString() => Summary;
}
