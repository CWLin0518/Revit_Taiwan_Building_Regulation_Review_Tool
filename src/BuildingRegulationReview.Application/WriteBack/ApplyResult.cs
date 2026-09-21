using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;

namespace BuildingRegulationReview.Application.WriteBack;

/// <summary>What actually happened to one element when the plan ran.</summary>
public enum ApplyOutcome
{
    Created,
    Updated,
    Deleted,

    /// <summary>Not attempted: a later task creates this kind, or its prerequisite never appeared.</summary>
    Skipped,

    /// <summary>Attempted and refused by Revit. Local, logged, and the run carried on.</summary>
    Failed
}

/// <summary>One line of the write-back log.</summary>
public sealed class ApplyResultItem
{
    public ApplyResultItem(
        ApplyOutcome outcome,
        ManagedElementKey key,
        string description,
        string? message = null,
        string? elementUniqueId = null)
    {
        if (!Enum.IsDefined(typeof(ApplyOutcome), outcome)) throw new ArgumentOutOfRangeException(nameof(outcome));
        if (string.IsNullOrWhiteSpace(description)) throw new ArgumentException("A result line needs a description.", nameof(description));

        Outcome = outcome;
        Key = key;
        Description = description.Trim();
        Message = string.IsNullOrWhiteSpace(message) ? null : message!.Trim();
        ElementUniqueId = string.IsNullOrWhiteSpace(elementUniqueId) ? null : elementUniqueId!.Trim();
    }

    public ApplyOutcome Outcome { get; }
    public ManagedElementKey Key { get; }
    public ManagedElementKind Kind => Key.Kind;
    public string Description { get; }

    /// <summary>Why it was skipped or how it failed; null when it simply worked.</summary>
    public string? Message { get; }

    public string? ElementUniqueId { get; }

    public bool IsProblem => Outcome == ApplyOutcome.Failed || Outcome == ApplyOutcome.Skipped;

    public string Text => Message is null
        ? OutcomeText(Outcome) + " " + Description
        : OutcomeText(Outcome) + " " + Description + "：" + Message;

    public static string OutcomeText(ApplyOutcome outcome) => outcome switch
    {
        ApplyOutcome.Created => "已建立",
        ApplyOutcome.Updated => "已更新",
        ApplyOutcome.Deleted => "已刪除",
        ApplyOutcome.Skipped => "已略過",
        ApplyOutcome.Failed => "失敗",
        _ => throw new ArgumentOutOfRangeException(nameof(outcome))
    };

    public override string ToString() => Text;
}

/// <summary>
/// What one write-back run did (spec 10.5). A run either committed — possibly with individual
/// elements skipped or refused, each of them on the log — or rolled the whole
/// <c>TransactionGroup</c> back, in which case the model is exactly as it was and
/// <see cref="FatalError"/> says why.
/// </summary>
public sealed class ApplyResult
{
    private ApplyResult(
        Guid packageId,
        IEnumerable<ApplyResultItem> items,
        IEnumerable<string> notes,
        IEnumerable<ManualAction> manualActions,
        IEnumerable<AreaAgreementFinding> areaFindings,
        string? fatalError)
    {
        PackageId = packageId;
        Items = new ReadOnlyCollection<ApplyResultItem>(items.ToList());
        Notes = new ReadOnlyCollection<string>(notes.ToList());
        ManualActions = new ReadOnlyCollection<ManualAction>(manualActions.ToList());
        AreaFindings = new ReadOnlyCollection<AreaAgreementFinding>(areaFindings.ToList());
        FatalError = fatalError;
    }

    public Guid PackageId { get; }

    public IReadOnlyList<ApplyResultItem> Items { get; }

    /// <summary>Run-level remarks: what was deferred, what the drafts still warn about.</summary>
    public IReadOnlyList<string> Notes { get; }

    /// <summary>
    /// What the run left for the user to finish in Revit (spec 10.5 item 3). These survive a
    /// rollback: the reason the tool would not do something — an ambiguous draft, a Revit refusal —
    /// is still true once the model is back where it started.
    /// </summary>
    public IReadOnlyList<ManualAction> ManualActions { get; }

    /// <summary>
    /// What Revit measured for every Area this run wrote, against what the drafts computed (spec
    /// 10.6). A rollback drops them: the Areas they describe are not in the model any more, so a
    /// verdict about their size would be a verdict about nothing.
    /// </summary>
    public IReadOnlyList<AreaAgreementFinding> AreaFindings { get; }

    /// <summary>The Areas whose two measurements disagree, which is what refuses Ready.</summary>
    public IReadOnlyList<AreaAgreementFinding> AreaDisagreements =>
        new ReadOnlyCollection<AreaAgreementFinding>(AreaFindings.Where(f => f.BlocksReady).ToList());

    /// <summary>Set only when the whole group was rolled back; the model was left untouched.</summary>
    public string? FatalError { get; }

    public bool IsRolledBack => FatalError is not null;

    public int CreatedCount => CountOf(ApplyOutcome.Created);
    public int UpdatedCount => CountOf(ApplyOutcome.Updated);
    public int DeletedCount => CountOf(ApplyOutcome.Deleted);
    public int SkippedCount => CountOf(ApplyOutcome.Skipped);
    public int FailedCount => CountOf(ApplyOutcome.Failed);

    /// <summary>True when the drafts are now in the model in full, which is what lets the Editor
    /// clear its unapplied-changes mark.</summary>
    public bool IsComplete => !IsRolledBack && FailedCount == 0;

    public int CountOf(ApplyOutcome outcome) => Items.Count(i => i.Outcome == outcome);

    /// <summary>The problem lines on their own, which is what the user has to act on.</summary>
    public IReadOnlyList<ApplyResultItem> Problems =>
        new ReadOnlyCollection<ApplyResultItem>(Items.Where(i => i.IsProblem).ToList());

    public string Summary
    {
        get
        {
            if (IsRolledBack)
            {
                return string.Format(
                    CultureInfo.InvariantCulture,
                    "寫回失敗，已全部復原，模型沒有任何變更：{0}",
                    FatalError);
            }

            var done = string.Format(
                CultureInfo.InvariantCulture,
                "已建立 {0} 個、更新 {1} 個、刪除 {2} 個元素。",
                CreatedCount,
                UpdatedCount,
                DeletedCount);

            if (FailedCount > 0 || SkippedCount > 0)
            {
                done += string.Format(
                    CultureInfo.InvariantCulture,
                    "另有 {0} 個失敗、{1} 個略過，詳見日誌。",
                    FailedCount,
                    SkippedCount);
            }

            if (ManualActions.Count > 0)
            {
                done += string.Format(
                    CultureInfo.InvariantCulture,
                    "還有 {0} 項需要在 Revit 中人工處理。",
                    ManualActions.Count);
            }

            if (AreaDisagreements.Count > 0)
            {
                done += string.Format(
                    CultureInfo.InvariantCulture,
                    "另有 {0} 個區劃的面積與草算不符，需要先修正邊界。",
                    AreaDisagreements.Count);
            }

            return done;
        }
    }

    /// <summary>The whole log as text, ready to be shown or written to a file.</summary>
    public IReadOnlyList<string> Log
    {
        get
        {
            var lines = new List<string> { Summary };
            lines.AddRange(Notes);
            lines.AddRange(ManualActions.Select(action => action.Text));
            lines.AddRange(AreaDisagreements.Select(finding => finding.Message!));
            lines.AddRange(Items.Select(item => item.Text));
            return new ReadOnlyCollection<string>(lines);
        }
    }

    /// <summary>The run that never started, because the plan had nothing to do.</summary>
    public static ApplyResult Nothing(Guid packageId) => new ApplyResult(
        packageId,
        Array.Empty<ApplyResultItem>(),
        Array.Empty<string>(),
        Array.Empty<ManualAction>(),
        Array.Empty<AreaAgreementFinding>(),
        null);

    /// <summary>
    /// Collects what a run does. The Revit adapter owns one of these and hands back
    /// <see cref="Complete"/> or <see cref="RolledBack"/>; nothing here touches a document, so the
    /// counting, the log and the rule that a rollback reports no changes are all testable.
    /// </summary>
    public sealed class Builder
    {
        private readonly List<ApplyResultItem> _items = new List<ApplyResultItem>();
        private readonly List<string> _notes = new List<string>();
        private readonly List<ManualAction> _manualActions = new List<ManualAction>();
        private readonly List<AreaAgreementFinding> _areaFindings = new List<AreaAgreementFinding>();
        private readonly Guid _packageId;

        public Builder(ApplyPlan plan)
        {
            if (plan is null) throw new ArgumentNullException(nameof(plan));

            _packageId = plan.PackageId;
            _notes.AddRange(plan.DeferredNotes);
            _notes.AddRange(plan.Warnings);
        }

        public int FailureCount => _items.Count(i => i.Outcome == ApplyOutcome.Failed);

        public void Note(string line)
        {
            if (!string.IsNullOrWhiteSpace(line)) _notes.Add(line.Trim());
        }

        /// <summary>Records something the run is handing back to the user (spec 10.5 item 3).</summary>
        public void Manual(ManualAction action)
        {
            if (action is not null) _manualActions.Add(action);
        }

        public void Manual(string subject, string reason, string suggestion) =>
            Manual(new ManualAction(subject, reason, suggestion));

        /// <summary>
        /// Records what Revit measured for one Area this run wrote (spec 10.6). Every Area is
        /// recorded, agreeing ones included, so the log shows the comparison was made rather than
        /// leaving the reader to guess whether it was skipped.
        /// </summary>
        public void Area(AreaAgreementFinding finding)
        {
            if (finding is not null) _areaFindings.Add(finding);
        }

        public void Created(ApplyStep step, string elementUniqueId) =>
            Record(ApplyOutcome.Created, step, null, elementUniqueId);

        public void Updated(ApplyStep step, string? elementUniqueId = null) =>
            Record(ApplyOutcome.Updated, step, null, elementUniqueId ?? step.ElementUniqueId);

        public void Deleted(ApplyStep step) =>
            Record(ApplyOutcome.Deleted, step, null, step.ElementUniqueId);

        public void Skipped(ApplyStep step, string reason) =>
            Record(ApplyOutcome.Skipped, step, reason, step.ElementUniqueId);

        public void Failed(ApplyStep step, string reason) =>
            Record(ApplyOutcome.Failed, step, reason, step.ElementUniqueId);

        /// <summary>The run committed. Individual failures are on the log, not in an exception.</summary>
        public ApplyResult Complete() =>
            new ApplyResult(_packageId, _items, _notes, _manualActions, _areaFindings, null);

        /// <summary>
        /// The group was rolled back. Every line collected so far describes something that no longer
        /// happened, so they are dropped and only the log-worthy notes and the cause survive.
        /// </summary>
        public ApplyResult RolledBack(string fatalError)
        {
            if (string.IsNullOrWhiteSpace(fatalError))
                throw new ArgumentException("A rollback has to say why.", nameof(fatalError));

            return new ApplyResult(
                _packageId,
                Array.Empty<ApplyResultItem>(),
                _notes,
                _manualActions,
                Array.Empty<AreaAgreementFinding>(),
                fatalError.Trim());
        }

        private void Record(ApplyOutcome outcome, ApplyStep step, string? message, string? elementUniqueId)
        {
            if (step is null) throw new ArgumentNullException(nameof(step));
            _items.Add(new ApplyResultItem(outcome, step.Key, step.Description, message, elementUniqueId));
        }
    }
}
