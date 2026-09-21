using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Text;
using BuildingRegulationReview.Application.WriteBack;

namespace BuildingRegulationReview.Application.Diagnostics;

/// <summary>
/// The log of one pass through the review (spec 14). It is a value, not a sink: the run collects
/// entries and hands the whole thing back, so the window can show the user lines and the host can
/// write the technical ones to a file without either of them holding a logger.
/// </summary>
/// <remarks>
/// There is no central logging facility in the project yet. Keeping the log a value is what lets one
/// be added later — a file writer, a Revit journal appender — without every stage having to be
/// rewired, and it is what makes the spec 14 field set assertable with no document open.
/// </remarks>
public sealed class ReviewLog
{
    private ReviewLog(Guid packageId, IEnumerable<ReviewLogEntry> entries)
    {
        PackageId = packageId;
        Entries = new ReadOnlyCollection<ReviewLogEntry>(entries.ToList());
    }

    public Guid PackageId { get; }

    public IReadOnlyList<ReviewLogEntry> Entries { get; }

    public bool IsEmpty => Entries.Count == 0;

    public bool HasErrors => Entries.Any(e => e.Severity == ReviewSeverity.Error);

    public int CountOf(ReviewSeverity severity) => Entries.Count(e => e.Severity == severity);

    public IReadOnlyList<ReviewLogEntry> Of(ReviewStage stage) =>
        new ReadOnlyCollection<ReviewLogEntry>(Entries.Where(e => e.Stage == stage).ToList());

    /// <summary>The lines the UI may show: no technical detail (spec 14).</summary>
    public IReadOnlyList<string> UserMessages =>
        new ReadOnlyCollection<string>(Entries.Select(e => e.UserText).ToList());

    /// <summary>The whole log as text, ready to be written to a local file.</summary>
    public string ToText()
    {
        var text = new StringBuilder();
        text.Append("# 防火區劃檢討日誌　Package ID ")
            .Append(PackageId.ToString("D", CultureInfo.InvariantCulture))
            .AppendLine();
        text.AppendLine("# 時間(UTC)\t嚴重度\t錯誤碼\t階段\tPackage ID\t元素 UniqueId\t訊息");
        foreach (var entry in Entries) text.AppendLine(entry.ToLogLine());
        return text.ToString();
    }

    public static ReviewLog Empty(Guid packageId) => new ReviewLog(packageId, Array.Empty<ReviewLogEntry>());

    /// <summary>
    /// Turns a finished write-back into log entries (spec 14). Every failure, skip, manual item and
    /// area disagreement becomes one entry carrying its own code and the element it is about;
    /// successful writes become one summary line rather than one line each, because a log that lists
    /// two hundred created boundary lines hides the three that failed.
    /// </summary>
    public static ReviewLog FromApplyResult(
        ApplyResult result,
        ReviewStage stage = ReviewStage.WriteBack,
        DateTime? timestampUtc = null)
    {
        if (result is null) throw new ArgumentNullException(nameof(result));

        var at = timestampUtc ?? DateTime.UtcNow;
        var builder = new Builder(result.PackageId, at);

        if (result.IsRolledBack)
        {
            builder.Add(
                ReviewErrorCode.WriteBackRolledBack,
                stage,
                ReviewSeverity.Error,
                "寫回失敗，已全部復原，模型沒有任何變更。",
                technicalDetail: result.FatalError,
                suggestion: "請依訊息修正後重新套用；模型目前與套用前完全一致。");
        }
        else
        {
            builder.Add(
                ReviewErrorCode.WriteBackCompleted,
                stage,
                result.FailedCount > 0 ? ReviewSeverity.Warning : ReviewSeverity.Info,
                result.Summary);
        }

        foreach (var item in result.Items.Where(i => i.Outcome == ApplyOutcome.Failed))
        {
            builder.Add(
                ReviewErrorCode.WriteBackElementFailed,
                stage,
                ReviewSeverity.Error,
                item.Description + " 沒有寫入模型。",
                elementUniqueId: item.ElementUniqueId,
                technicalDetail: item.Message,
                suggestion: "請在 Revit 中檢查這個元素所在的視圖與 Workset，修正後重新套用。");
        }

        foreach (var item in result.Items.Where(i => i.Outcome == ApplyOutcome.Skipped))
        {
            builder.Add(
                ReviewErrorCode.WriteBackElementSkipped,
                stage,
                ReviewSeverity.Warning,
                item.Description + " 已略過。",
                elementUniqueId: item.ElementUniqueId,
                technicalDetail: item.Message,
                suggestion: "略過通常表示元素已不在模型中或不屬於本套件，可直接重新套用確認。");
        }

        foreach (var action in result.ManualActions)
        {
            builder.Add(
                ReviewErrorCode.WriteBackManualAction,
                stage,
                ReviewSeverity.Warning,
                action.Subject + "——" + action.Reason,
                suggestion: action.Suggestion);
        }

        foreach (var finding in result.AreaFindings.Where(f => f.BlocksReady))
        {
            builder.Add(
                finding.Kind == AreaAgreementKind.NotEnclosed
                    ? ReviewErrorCode.AreaNotEnclosed
                    : ReviewErrorCode.AreaDisagrees,
                stage,
                ReviewSeverity.Error,
                finding.Message!,
                elementUniqueId: finding.ElementUniqueId,
                technicalDetail: string.Format(
                    CultureInfo.InvariantCulture,
                    "ZoneId={0:D} 草算={1:0.####} m² Revit={2:0.####} m²",
                    finding.ZoneId,
                    finding.DraftSquareMeters,
                    finding.RevitSquareMeters),
                suggestion: "請回到 Area Plan 檢查這個區劃的邊界是否閉合，補線後重新套用。");
        }

        foreach (var note in result.Notes)
        {
            builder.Add(ReviewErrorCode.WriteBackCompleted, stage, ReviewSeverity.Info, note);
        }

        return builder.Build();
    }

    /// <summary>Collects entries for one run. Nothing here touches Revit or the file system.</summary>
    public sealed class Builder
    {
        private readonly List<ReviewLogEntry> _entries = new List<ReviewLogEntry>();
        private readonly Guid _packageId;
        private readonly DateTime _defaultTimestampUtc;

        public Builder(Guid packageId, DateTime? defaultTimestampUtc = null)
        {
            _packageId = packageId;
            _defaultTimestampUtc = (defaultTimestampUtc ?? DateTime.UtcNow).ToUniversalTime();
        }

        public int Count => _entries.Count;

        public Builder Add(
            string code,
            ReviewStage stage,
            ReviewSeverity severity,
            string userMessage,
            string? elementUniqueId = null,
            string? technicalDetail = null,
            string? suggestion = null,
            DateTime? timestampUtc = null)
        {
            _entries.Add(new ReviewLogEntry(
                code, stage, severity, _packageId, userMessage,
                elementUniqueId, technicalDetail, suggestion,
                timestampUtc ?? _defaultTimestampUtc));
            return this;
        }

        public Builder Add(ReviewLogEntry entry)
        {
            if (entry is not null) _entries.Add(entry);
            return this;
        }

        public Builder AddRange(ReviewLog log)
        {
            if (log is not null) _entries.AddRange(log.Entries);
            return this;
        }

        public ReviewLog Build() => new ReviewLog(_packageId, _entries);
    }
}
