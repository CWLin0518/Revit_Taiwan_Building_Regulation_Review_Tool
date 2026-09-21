using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace BuildingRegulationReview.Domain.Reviews;

/// <summary>
/// What the model looked like when a run was computed (spec 13.1): one fingerprint for everything a
/// run shares — view, Level, Area Scheme, Phase, Design Option, tolerance, building inputs — and one
/// per subject the results name (element UniqueIds and Zone IDs). A later look at the model produces
/// another baseline; the keys whose fingerprints moved say exactly which results no longer hold.
/// </summary>
/// <remarks>
/// A fingerprint is an opaque, stable digest of what the check read. Nothing here knows how it was
/// computed, so the Application layer can widen what it covers without touching stored runs.
/// </remarks>
public sealed class ReviewBaseline
{
    /// <summary>A run that recorded no evidence; it can never be shown to still hold.</summary>
    public static readonly ReviewBaseline None = new ReviewBaseline(null, null);

    public ReviewBaseline(string? contextFingerprint, IEnumerable<KeyValuePair<string, string>>? subjects)
    {
        var context = string.IsNullOrWhiteSpace(contextFingerprint) ? null : contextFingerprint!.Trim();
        var list = (subjects ?? Array.Empty<KeyValuePair<string, string>>()).ToList();

        if (context is null && list.Count > 0)
            throw new ArgumentException("Subject fingerprints need the context they were taken in.", nameof(subjects));
        if (list.Any(x => string.IsNullOrWhiteSpace(x.Key)))
            throw new ArgumentException("A subject fingerprint needs the subject it is about.", nameof(subjects));
        if (list.Any(x => string.IsNullOrWhiteSpace(x.Value)))
            throw new ArgumentException("A subject fingerprint cannot be empty.", nameof(subjects));

        var duplicate = list.GroupBy(x => x.Key.Trim(), StringComparer.Ordinal).FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
            throw new ArgumentException($"Subject '{duplicate.Key}' has more than one fingerprint.", nameof(subjects));

        ContextFingerprint = context;
        Subjects = new ReadOnlyDictionary<string, string>(list.ToDictionary(
            x => x.Key.Trim(), x => x.Value.Trim(), StringComparer.Ordinal));
        SubjectIds = new ReadOnlyCollection<string>(Subjects.Keys.OrderBy(x => x, StringComparer.Ordinal).ToList());
    }

    /// <summary>Null when the run recorded nothing to compare against.</summary>
    public string? ContextFingerprint { get; }

    public IReadOnlyDictionary<string, string> Subjects { get; }

    /// <summary>Subject keys in ordinal order, for storage and display.</summary>
    public IReadOnlyList<string> SubjectIds { get; }

    public bool IsRecorded => ContextFingerprint is not null;

    public string? FingerprintOf(string? subjectId) =>
        subjectId is not null && Subjects.TryGetValue(subjectId, out var fingerprint) ? fingerprint : null;
}
