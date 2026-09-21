using System;
using System.Collections.Generic;
using System.Linq;
using BuildingRegulationReview.Application.Rules;
using BuildingRegulationReview.Domain.Reviews;

namespace BuildingRegulationReview.Application.Reviews;

// Flat, serializer-agnostic shape of a review run, mirroring ReviewPackageStorageRecord. States,
// statuses, kinds and units are stored by name rather than number, so reordering an enumeration
// can never silently turn a stored 符合 into something else.
public sealed class ReviewRunStorageRecord
{
    public string SchemaVersion { get; set; } = ReviewRun.CurrentSchemaVersion;
    public string RunId { get; set; } = string.Empty;
    public string PackageId { get; set; } = string.Empty;
    public string RuleSetId { get; set; } = string.Empty;
    public string RuleSetVersion { get; set; } = string.Empty;
    public int BoundaryRevision { get; set; }
    public long StartedAtUtcTicks { get; set; }
    public string State { get; set; } = string.Empty;
    public long CompletedAtUtcTicks { get; set; }
    public IList<ReviewResultRecord> Results { get; set; } = new List<ReviewResultRecord>();

    /// <summary>Empty when the run recorded no evidence (every 1.0 record).</summary>
    public string ContextFingerprint { get; set; } = string.Empty;

    public IList<ReviewFingerprintRecord> SubjectFingerprints { get; set; } = new List<ReviewFingerprintRecord>();
    public IList<ReviewOverrideRecord> Overrides { get; set; } = new List<ReviewOverrideRecord>();
}

public sealed class ReviewFingerprintRecord
{
    public string SubjectId { get; set; } = string.Empty;
    public string Fingerprint { get; set; } = string.Empty;
}

public sealed class ReviewOverrideRecord
{
    public string OverrideId { get; set; } = string.Empty;
    public string ResultId { get; set; } = string.Empty;
    public string OriginalStatus { get; set; } = string.Empty;
    public string OverriddenStatus { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public string Comment { get; set; } = string.Empty;
    public string OverriddenBy { get; set; } = string.Empty;
    public long OverriddenAtUtcTicks { get; set; }
    public string RuleVersion { get; set; } = string.Empty;
    public string DependencyFingerprint { get; set; } = string.Empty;
    public string Standing { get; set; } = string.Empty;
    public string StandingReason { get; set; } = string.Empty;
    public string PreviousOverrideId { get; set; } = string.Empty;
}

public sealed class ReviewResultRecord
{
    public string ResultId { get; set; } = string.Empty;
    public string CheckType { get; set; } = string.Empty;
    public IList<string> SubjectUniqueIds { get; set; } = new List<string>();
    public string ZoneId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public ReviewValueRecord? ActualValue { get; set; }
    public ReviewValueRecord? RequiredValue { get; set; }
    public string RuleId { get; set; } = string.Empty;
    public string RuleVersion { get; set; } = string.Empty;
    public string LegalReference { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public IList<ReviewEvidenceRecord> Evidence { get; set; } = new List<ReviewEvidenceRecord>();
    public string ReviewedBy { get; set; } = string.Empty;
    public long ReviewedAtUtcTicks { get; set; }
}

public sealed class ReviewValueRecord
{
    public string Kind { get; set; } = string.Empty;
    public double Number { get; set; }
    public string Unit { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public bool Flag { get; set; }
}

public sealed class ReviewEvidenceRecord
{
    public string Field { get; set; } = string.Empty;
    public ReviewValueRecord Value { get; set; } = new ReviewValueRecord();
}

/// <summary>
/// Maps a <see cref="ReviewRun"/> to its storage record and back. Like the package mapper, a record
/// the tool cannot read is an error, never a guess: an unknown schema version is refused and a bad
/// field throws, because a silently repaired result would be a verdict nobody made.
/// </summary>
public static class ReviewRunStorageMapper
{
    /// <summary>The first released shape: no evidence and no overrides. Read as a run that cannot be shown to hold.</summary>
    public const string LegacySchemaVersion = "1.0";

    public static ReviewRunStorageRecord ToRecord(ReviewRun run)
    {
        if (run is null) throw new ArgumentNullException(nameof(run));

        return new ReviewRunStorageRecord
        {
            SchemaVersion = ReviewRun.CurrentSchemaVersion,
            RunId = run.RunId.ToString("D"),
            PackageId = run.PackageId.ToString("D"),
            RuleSetId = run.RuleSetId,
            RuleSetVersion = run.RuleSetVersion,
            BoundaryRevision = run.BoundaryRevision,
            StartedAtUtcTicks = run.StartedAtUtc.Ticks,
            State = run.State.ToString(),
            CompletedAtUtcTicks = run.CompletedAtUtc?.Ticks ?? 0,
            Results = run.Results.Select(ToRecord).ToList(),
            ContextFingerprint = run.Baseline.ContextFingerprint ?? string.Empty,
            SubjectFingerprints = run.Baseline.SubjectIds
                .Select(x => new ReviewFingerprintRecord { SubjectId = x, Fingerprint = run.Baseline.Subjects[x] })
                .ToList(),
            Overrides = run.Overrides.Select(ToRecord).ToList()
        };
    }

    public static ReviewRun FromRecord(ReviewRunStorageRecord record)
    {
        if (record is null) throw new ArgumentNullException(nameof(record));
        var legacy = string.Equals(record.SchemaVersion, LegacySchemaVersion, StringComparison.Ordinal);
        if (!legacy && !string.Equals(record.SchemaVersion, ReviewRun.CurrentSchemaVersion, StringComparison.Ordinal))
            throw new NotSupportedException($"Review run schema version '{record.SchemaVersion}' is not supported.");

        var runId = ParseId(record.RunId, "RunId");
        var packageId = ParseId(record.PackageId, "PackageId");
        var state = ParseName<ReviewRunState>(record.State, "state");

        return new ReviewRun(
            runId,
            packageId,
            record.RuleSetId,
            record.RuleSetVersion,
            record.BoundaryRevision,
            new DateTime(record.StartedAtUtcTicks, DateTimeKind.Utc),
            state,
            record.CompletedAtUtcTicks == 0 ? null : new DateTime(record.CompletedAtUtcTicks, DateTimeKind.Utc),
            (record.Results ?? new List<ReviewResultRecord>()).Select(x => FromRecord(x, runId, packageId)),
            legacy ? ReviewBaseline.None : BaselineOf(record),
            legacy ? null : (record.Overrides ?? new List<ReviewOverrideRecord>()).Select(FromRecord));
    }

    private static ReviewBaseline BaselineOf(ReviewRunStorageRecord record) => new ReviewBaseline(
        record.ContextFingerprint,
        (record.SubjectFingerprints ?? new List<ReviewFingerprintRecord>()).Select(x =>
            x is null
                ? throw new InvalidOperationException("Stored review run has a missing fingerprint.")
                : new KeyValuePair<string, string>(x.SubjectId, x.Fingerprint)));

    private static ReviewOverrideRecord ToRecord(ReviewOverride entry) => new ReviewOverrideRecord
    {
        OverrideId = entry.OverrideId.ToString("D"),
        ResultId = entry.ResultId.ToString("D"),
        OriginalStatus = entry.OriginalStatus.ToString(),
        OverriddenStatus = entry.OverriddenStatus.ToString(),
        Reason = entry.Reason,
        Comment = entry.Comment ?? string.Empty,
        OverriddenBy = entry.OverriddenBy,
        OverriddenAtUtcTicks = entry.OverriddenAtUtc.Ticks,
        RuleVersion = entry.RuleVersion,
        DependencyFingerprint = entry.DependencyFingerprint ?? string.Empty,
        Standing = entry.Standing.ToString(),
        StandingReason = entry.StandingReason ?? string.Empty,
        PreviousOverrideId = entry.PreviousOverrideId?.ToString("D") ?? string.Empty
    };

    private static ReviewOverride FromRecord(ReviewOverrideRecord? record)
    {
        if (record is null) throw new InvalidOperationException("Stored review run has a missing override.");

        return new ReviewOverride(
            ParseId(record.OverrideId, "OverrideId"),
            ParseId(record.ResultId, "override ResultId"),
            ParseName<ReviewStatus>(record.OriginalStatus, "original status"),
            ParseName<ReviewStatus>(record.OverriddenStatus, "overridden status"),
            record.Reason,
            record.Comment,
            record.OverriddenBy,
            new DateTime(record.OverriddenAtUtcTicks, DateTimeKind.Utc),
            record.RuleVersion,
            record.DependencyFingerprint,
            ParseName<ReviewOverrideStanding>(record.Standing, "override standing"),
            record.StandingReason,
            string.IsNullOrEmpty(record.PreviousOverrideId) ? null : ParseId(record.PreviousOverrideId, "PreviousOverrideId"));
    }

    private static ReviewResultRecord ToRecord(ReviewResult result) => new ReviewResultRecord
    {
        ResultId = result.ResultId.ToString("D"),
        CheckType = result.CheckType,
        SubjectUniqueIds = result.SubjectUniqueIds.ToList(),
        ZoneId = result.ZoneId ?? string.Empty,
        Status = result.Status.ToString(),
        ActualValue = result.ActualValue is null ? null : ToRecord(result.ActualValue),
        RequiredValue = result.RequiredValue is null ? null : ToRecord(result.RequiredValue),
        RuleId = result.RuleId,
        RuleVersion = result.RuleVersion,
        LegalReference = result.LegalReference,
        Message = result.Message,
        Evidence = result.Evidence.Items
            .Select(x => new ReviewEvidenceRecord { Field = x.Field, Value = ToRecord(x.Value) })
            .ToList(),
        ReviewedBy = result.ReviewedBy ?? string.Empty,
        ReviewedAtUtcTicks = result.ReviewedAtUtc?.Ticks ?? 0
    };

    private static ReviewResult FromRecord(ReviewResultRecord? record, Guid runId, Guid packageId)
    {
        if (record is null) throw new InvalidOperationException("Stored review run has a missing result.");

        var evidence = new ReviewEvidence((record.Evidence ?? new List<ReviewEvidenceRecord>()).Select(x =>
        {
            if (x is null) throw new InvalidOperationException("Stored review result has a missing evidence entry.");
            return new ReviewEvidenceItem(x.Field, FromRecord(x.Value));
        }));

        return new ReviewResult(
            ParseId(record.ResultId, "ResultId"),
            runId,
            packageId,
            record.CheckType,
            record.SubjectUniqueIds,
            record.ZoneId,
            ParseName<ReviewStatus>(record.Status, "status"),
            record.ActualValue is null ? null : FromRecord(record.ActualValue),
            record.RequiredValue is null ? null : FromRecord(record.RequiredValue),
            record.RuleId,
            record.RuleVersion,
            record.LegalReference,
            record.Message,
            evidence,
            record.ReviewedBy,
            record.ReviewedAtUtcTicks == 0 ? null : new DateTime(record.ReviewedAtUtcTicks, DateTimeKind.Utc));
    }

    private static ReviewValueRecord ToRecord(ReviewValue value) => new ReviewValueRecord
    {
        Kind = value.Kind.ToString(),
        Number = value.Number,
        Unit = value.Unit.ToString(),
        Text = value.Text,
        Flag = value.Flag
    };

    private static ReviewValue FromRecord(ReviewValueRecord? record)
    {
        if (record is null) throw new InvalidOperationException("Stored review value is missing.");

        return ParseName<ReviewValueKind>(record.Kind, "value kind") switch
        {
            ReviewValueKind.Quantity => ReviewValue.Quantity(record.Number, ParseName<ReviewUnit>(record.Unit, "unit")),
            ReviewValueKind.Text => ReviewValue.OfText(record.Text ?? string.Empty),
            _ => ReviewValue.OfBoolean(record.Flag)
        };
    }

    private static Guid ParseId(string? text, string name) =>
        Guid.TryParse(text, out var id) && id != Guid.Empty
            ? id
            : throw new InvalidOperationException($"Stored review run has an invalid {name}.");

    private static TEnum ParseName<TEnum>(string? text, string name) where TEnum : struct =>
        RuleSetSchemaValidator.TryParseName<TEnum>(text, out var value)
            ? value
            : throw new InvalidOperationException($"Stored review run has an invalid {name} '{text}'.");
}
