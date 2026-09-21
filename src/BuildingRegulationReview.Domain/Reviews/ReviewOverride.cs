using System;

namespace BuildingRegulationReview.Domain.Reviews;

/// <summary>Whether a manual override still decides its result (spec 11.8).</summary>
public enum ReviewOverrideStanding
{
    /// <summary>The override decides the result's effective status.</summary>
    Active,

    /// <summary>
    /// The model or the rule version moved since the override was made, so it no longer decides
    /// anything until somebody confirms it again (spec 11.8「覆寫不得無條件沿用」).
    /// </summary>
    NeedsReconfirmation,

    /// <summary>Replaced by a later entry for the same result; kept only as the audit trail.</summary>
    Superseded
}

/// <summary>
/// One manual override of one result, as the audit trail keeps it (spec 11.8): who, when, why, the
/// computed status it overrode and the status it put in its place. Entries are never edited in
/// place — a reconfirmation or a newer override adds an entry and supersedes this one.
/// </summary>
public sealed class ReviewOverride
{
    public ReviewOverride(
        Guid overrideId,
        Guid resultId,
        ReviewStatus originalStatus,
        ReviewStatus overriddenStatus,
        string reason,
        string? comment,
        string overriddenBy,
        DateTime overriddenAtUtc,
        string ruleVersion,
        string? dependencyFingerprint,
        ReviewOverrideStanding standing = ReviewOverrideStanding.Active,
        string? standingReason = null,
        Guid? previousOverrideId = null)
    {
        if (overrideId == Guid.Empty) throw new ArgumentException("Override ID cannot be empty.", nameof(overrideId));
        if (resultId == Guid.Empty) throw new ArgumentException("Result ID cannot be empty.", nameof(resultId));
        if (!Enum.IsDefined(typeof(ReviewStatus), originalStatus)) throw new ArgumentOutOfRangeException(nameof(originalStatus));
        if (!Enum.IsDefined(typeof(ReviewStatus), overriddenStatus)) throw new ArgumentOutOfRangeException(nameof(overriddenStatus));
        if (!Enum.IsDefined(typeof(ReviewOverrideStanding), standing)) throw new ArgumentOutOfRangeException(nameof(standing));
        if (originalStatus == ReviewStatus.NotRun)
            throw new ArgumentException("A result that was never reviewed has nothing to override.", nameof(originalStatus));
        if (overriddenStatus == ReviewStatus.NotRun)
            throw new ArgumentException("An override cannot turn a result back into 未檢討.", nameof(overriddenStatus));
        if (overriddenStatus == originalStatus)
            throw new ArgumentException("An override must change the status it overrides.", nameof(overriddenStatus));
        if (string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("An override must say why.", nameof(reason));
        if (string.IsNullOrWhiteSpace(overriddenBy)) throw new ArgumentException("An override must say who made it.", nameof(overriddenBy));
        if (string.IsNullOrWhiteSpace(ruleVersion)) throw new ArgumentException("Rule version is required.", nameof(ruleVersion));
        if (standing != ReviewOverrideStanding.Active && string.IsNullOrWhiteSpace(standingReason))
            throw new ArgumentException($"A {standing} override must say why it no longer decides the result.", nameof(standingReason));
        if (previousOverrideId == Guid.Empty || previousOverrideId == overrideId)
            throw new ArgumentException("The previous override must be another entry.", nameof(previousOverrideId));

        OverrideId = overrideId;
        ResultId = resultId;
        OriginalStatus = originalStatus;
        OverriddenStatus = overriddenStatus;
        Reason = reason.Trim();
        Comment = string.IsNullOrWhiteSpace(comment) ? null : comment!.Trim();
        OverriddenBy = overriddenBy.Trim();
        OverriddenAtUtc = overriddenAtUtc.ToUniversalTime();
        RuleVersion = ruleVersion.Trim();
        DependencyFingerprint = string.IsNullOrWhiteSpace(dependencyFingerprint) ? null : dependencyFingerprint!.Trim();
        Standing = standing;
        StandingReason = standing == ReviewOverrideStanding.Active || string.IsNullOrWhiteSpace(standingReason)
            ? null
            : standingReason!.Trim();
        PreviousOverrideId = previousOverrideId;
    }

    public Guid OverrideId { get; }

    /// <summary>The result in the same run this entry is about.</summary>
    public Guid ResultId { get; }

    /// <summary>The status the rules computed when the override was made.</summary>
    public ReviewStatus OriginalStatus { get; }

    /// <summary>The status the reviewer put in its place.</summary>
    public ReviewStatus OverriddenStatus { get; }

    public string Reason { get; }
    public string? Comment { get; }
    public string OverriddenBy { get; }
    public DateTime OverriddenAtUtc { get; }

    /// <summary>The version of the rule that computed <see cref="OriginalStatus"/>.</summary>
    public string RuleVersion { get; }

    /// <summary>
    /// A digest of the evidence the result depended on when the override was made; null when the run
    /// recorded no evidence, which means the override can never be carried to another run unconfirmed.
    /// </summary>
    public string? DependencyFingerprint { get; }

    public ReviewOverrideStanding Standing { get; }

    /// <summary>Why the entry stopped deciding the result; null while it is active.</summary>
    public string? StandingReason { get; }

    /// <summary>The entry this one continues: the one it reconfirms, replaces, or was carried over from.</summary>
    public Guid? PreviousOverrideId { get; }

    public bool IsActive => Standing == ReviewOverrideStanding.Active;

    /// <summary>Whether this entry is the one still attached to its result (active or awaiting confirmation).</summary>
    public bool IsCurrent => Standing != ReviewOverrideStanding.Superseded;

    /// <summary>The same entry, no longer deciding its result.</summary>
    public ReviewOverride WithStanding(ReviewOverrideStanding standing, string reason)
    {
        if (standing == ReviewOverrideStanding.Active)
            throw new ArgumentException("An override becomes active again only through a new, confirmed entry.", nameof(standing));

        return new ReviewOverride(OverrideId, ResultId, OriginalStatus, OverriddenStatus, Reason, Comment,
            OverriddenBy, OverriddenAtUtc, RuleVersion, DependencyFingerprint, standing, reason, PreviousOverrideId);
    }
}
