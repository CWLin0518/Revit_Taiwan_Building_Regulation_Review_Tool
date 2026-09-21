using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using BuildingRegulationReview.Application.Candidates;
using BuildingRegulationReview.Application.Checks;
using BuildingRegulationReview.Application.Diagnostics;
using BuildingRegulationReview.Application.WriteBack;
using BuildingRegulationReview.Domain.Common;
using BuildingRegulationReview.Domain.Geometry;
using BuildingRegulationReview.Domain.Reviews;

namespace BuildingRegulationReview.Application.Reviews;

/// <summary>
/// The ownership mark of a red Filled Region in the review view (spec 11.4 item 4: 標註元素須含 Package
/// ID、Run ID、Zone ID). The token carries all three; what a later run matches on is the
/// <see cref="Slot"/> — package, zone and part — so a new run takes over the region the last run drew
/// for the same part of the same 區劃 instead of drawing a second one (spec 13.2).
/// </summary>
public readonly struct ReviewMarkKey : IEquatable<ReviewMarkKey>
{
    /// <summary>Distinct from the P2 prefixes, so a review mark is never read as a boundary element.</summary>
    public const string Prefix = "BCRRV";

    private const char Separator = '/';

    public ReviewMarkKey(Guid packageId, Guid runId, Guid zoneId, int partIndex)
    {
        if (packageId == Guid.Empty) throw new ArgumentException("Package ID cannot be empty.", nameof(packageId));
        if (runId == Guid.Empty) throw new ArgumentException("Run ID cannot be empty.", nameof(runId));
        if (zoneId == Guid.Empty) throw new ArgumentException("Zone ID cannot be empty.", nameof(zoneId));
        if (partIndex < 0) throw new ArgumentOutOfRangeException(nameof(partIndex));

        PackageId = packageId;
        RunId = runId;
        ZoneId = zoneId;
        PartIndex = partIndex;
    }

    public Guid PackageId { get; }
    public Guid RunId { get; }
    public Guid ZoneId { get; }

    /// <summary>Which enclosed Area of the 區劃, in the order the candidate zone lists them.</summary>
    public int PartIndex { get; }

    /// <summary>What identifies the region across runs: everything but the run.</summary>
    public string Slot => string.Join(Separator.ToString(),
        PackageId.ToString("N", CultureInfo.InvariantCulture),
        ZoneId.ToString("N", CultureInfo.InvariantCulture),
        PartIndex.ToString(CultureInfo.InvariantCulture));

    public string ToToken() => string.Join(Separator.ToString(),
        Prefix,
        PackageId.ToString("N", CultureInfo.InvariantCulture),
        RunId.ToString("N", CultureInfo.InvariantCulture),
        ZoneId.ToString("N", CultureInfo.InvariantCulture),
        PartIndex.ToString(CultureInfo.InvariantCulture));

    /// <summary>The readable form the adapter also writes to the region's Comments, for a user looking at it in Revit.</summary>
    public string ToLabel() =>
        $"Package {PackageId:D} / Run {RunId:D} / Zone {ZoneId:D} / Part {PartIndex.ToString(CultureInfo.InvariantCulture)}";

    public static bool TryParse(string? token, out ReviewMarkKey key)
    {
        key = default;
        if (string.IsNullOrWhiteSpace(token)) return false;

        var parts = token!.Trim().Split(Separator);
        if (parts.Length != 5) return false;
        if (!string.Equals(parts[0], Prefix, StringComparison.Ordinal)) return false;
        if (!Guid.TryParseExact(parts[1], "N", out var packageId) || packageId == Guid.Empty) return false;
        if (!Guid.TryParseExact(parts[2], "N", out var runId) || runId == Guid.Empty) return false;
        if (!Guid.TryParseExact(parts[3], "N", out var zoneId) || zoneId == Guid.Empty) return false;
        if (!int.TryParse(parts[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out var partIndex) || partIndex < 0) return false;

        key = new ReviewMarkKey(packageId, runId, zoneId, partIndex);
        return true;
    }

    public bool Equals(ReviewMarkKey other) =>
        PackageId == other.PackageId && RunId == other.RunId && ZoneId == other.ZoneId && PartIndex == other.PartIndex;

    public override bool Equals(object? obj) => obj is ReviewMarkKey other && Equals(other);

    public override int GetHashCode()
    {
        unchecked
        {
            var hash = 17;
            hash = (hash * 31) + PackageId.GetHashCode();
            hash = (hash * 31) + RunId.GetHashCode();
            hash = (hash * 31) + ZoneId.GetHashCode();
            hash = (hash * 31) + PartIndex;
            return hash;
        }
    }

    public static bool operator ==(ReviewMarkKey left, ReviewMarkKey right) => left.Equals(right);
    public static bool operator !=(ReviewMarkKey left, ReviewMarkKey right) => !left.Equals(right);

    public override string ToString() => ToToken();
}

/// <summary>A red Filled Region the current run says should be in the review view: one per enclosed Area of a failing 區劃.</summary>
public sealed class PlannedReviewRegion
{
    internal PlannedReviewRegion(ReviewMarkKey key, Guid resultId, string zoneName, IEnumerable<IReadOnlyList<Point2D>> loops)
    {
        Key = key;
        ResultId = resultId;
        ZoneName = zoneName;
        Loops = new ReadOnlyCollection<IReadOnlyList<Point2D>>(loops
            .Select(l => (IReadOnlyList<Point2D>)new ReadOnlyCollection<Point2D>(l.ToList())).ToList());
        Signature = ReviewMarkupPlan.RegionSignature(zoneName, Loops);
    }

    public ReviewMarkKey Key { get; }
    public Guid ResultId { get; }
    public string ZoneName { get; }

    /// <summary>The outer loop first, then its holes, in model feet — one Filled Region holds them all.</summary>
    public IReadOnlyList<IReadOnlyList<Point2D>> Loops { get; }

    /// <summary>What reaches the model apart from the mark. Same signature and same run: nothing to do.</summary>
    public string Signature { get; }

    public string Description => $"未符合區劃「{ZoneName}」紅色填滿區域（第 {Key.PartIndex + 1} 部分）";

    public override string ToString() => Description;
}

/// <summary>A model element the review view should show red: it failed at least one element check.</summary>
public sealed class PlannedElementOverride
{
    internal PlannedElementOverride(string elementUniqueId, IEnumerable<ReviewTableEntry> entries)
    {
        var list = entries.ToList();
        ElementUniqueId = elementUniqueId;
        ResultIds = new ReadOnlyCollection<Guid>(list.Select(e => e.ResultId).Distinct().ToList());
        CheckTypes = new ReadOnlyCollection<string>(list.Select(e => e.CheckType).Distinct(StringComparer.Ordinal).ToList());
        var first = list[0];
        Description = first.TypeName is null
            ? $"未符合{first.CategoryLabel} {elementUniqueId}"
            : $"未符合{first.CategoryLabel}「{first.TypeName}」 {elementUniqueId}";
    }

    public string ElementUniqueId { get; }
    public IReadOnlyList<Guid> ResultIds { get; }
    public IReadOnlyList<string> CheckTypes { get; }
    public string Description { get; }

    public override string ToString() => Description;
}

/// <summary>A failing result the run could not mark in the view, and why — never dropped without a word.</summary>
public sealed class SkippedReviewMark
{
    public SkippedReviewMark(Guid? resultId, string subject, string reason)
    {
        if (string.IsNullOrWhiteSpace(subject)) throw new ArgumentException("A skipped mark needs a subject.", nameof(subject));
        if (string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("A skipped mark needs a reason.", nameof(reason));

        ResultId = resultId;
        Subject = subject.Trim();
        Reason = reason.Trim();
    }

    public Guid? ResultId { get; }
    public string Subject { get; }
    public string Reason { get; }
    public string Text => $"未標示 {Subject}：{Reason}";

    public override string ToString() => Text;
}

/// <summary>
/// What the review view should show for one run (spec 11.4 item 4, 11.5 item 6, 11.6 item 4): a red
/// Filled Region over every 區劃 whose area fails, and a red By Element Override on every member and
/// opening that fails. Pure data: the Revit adapter draws it, the diff decides what that means for
/// what is already there.
/// </summary>
/// <remarks>
/// The plan reads the <see cref="ReviewRun.EffectiveStatus"/>, so a failure a reviewer has overridden
/// is not painted red, and a pass a reviewer has overridden to Fail is. A stale result is never
/// painted: showing what an out-of-date run said as if it were the model's current state is exactly
/// what spec 13.1 forbids; it is listed as skipped instead.
/// </remarks>
public sealed class ReviewMarkupPlan
{
    private const double SignatureQuantumFeet = 1e-4;

    private ReviewMarkupPlan(
        Guid packageId,
        Guid runId,
        IEnumerable<PlannedReviewRegion> regions,
        IEnumerable<PlannedElementOverride> overrides,
        IEnumerable<SkippedReviewMark> skipped)
    {
        PackageId = packageId;
        RunId = runId;
        Regions = new ReadOnlyCollection<PlannedReviewRegion>(regions.ToList());
        Overrides = new ReadOnlyCollection<PlannedElementOverride>(overrides.ToList());
        Skipped = new ReadOnlyCollection<SkippedReviewMark>(skipped.ToList());
    }

    public Guid PackageId { get; }
    public Guid RunId { get; }
    public IReadOnlyList<PlannedReviewRegion> Regions { get; }
    public IReadOnlyList<PlannedElementOverride> Overrides { get; }
    public IReadOnlyList<SkippedReviewMark> Skipped { get; }

    /// <summary>
    /// Builds the plan from the run's table and the zones as the candidate set resolved them — the same
    /// zones the run was computed from, which is what the geometry of the red regions has to follow.
    /// </summary>
    public static Result<ReviewMarkupPlan> Build(ReviewTable table, IEnumerable<CandidateZone> zones)
    {
        if (table is null) throw new ArgumentNullException(nameof(table));
        if (zones is null) throw new ArgumentNullException(nameof(zones));

        if (table.Run.State != ReviewRunState.Completed)
            return Result.Failure<ReviewMarkupPlan>(new Error(ReviewErrorCode.ReviewMarkRefused,
                $"檢討紀錄 {table.RunId:D} 的狀態是 {table.Run.State}，只有完成的檢討可以標示在檢討視圖。",
                "Only a completed review run can be marked in the review view."));

        var byZone = zones.GroupBy(z => z.ZoneIdText, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var regions = new List<PlannedReviewRegion>();
        var skipped = new List<SkippedReviewMark>();
        var failing = new List<(string ElementUniqueId, ReviewTableEntry Entry)>();

        foreach (var entry in table.Entries.Where(e => e.EffectiveStatus == ReviewStatus.Fail))
        {
            var isArea = string.Equals(entry.CheckType, ReviewCheckTypes.CompartmentArea, StringComparison.Ordinal);
            if (entry.IsStale)
            {
                skipped.Add(new SkippedReviewMark(entry.ResultId, Subject(entry, isArea),
                    "結果已過期（模型或規則已變更），請重新檢討後再標示"));
                continue;
            }

            if (isArea)
            {
                PlanRegions(table, entry, byZone, regions, skipped);
                continue;
            }

            if (entry.IsLinked)
            {
                skipped.Add(new SkippedReviewMark(entry.ResultId, Subject(entry, false),
                    "元素位於連結模型，主模型的檢討視圖無法逐元素覆寫，請在連結模型中確認"));
                continue;
            }

            foreach (var subject in entry.LocateUniqueIds)
                failing.Add((subject, entry));
        }

        var overrides = failing
            .GroupBy(x => x.ElementUniqueId, StringComparer.Ordinal)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => new PlannedElementOverride(g.Key, g.Select(x => x.Entry)));

        return Result.Success(new ReviewMarkupPlan(table.PackageId, table.RunId,
            regions.OrderBy(r => r.Key.Slot, StringComparer.Ordinal), overrides, skipped));
    }

    internal static string RegionSignature(string zoneName, IReadOnlyList<IReadOnlyList<Point2D>> loops) =>
        "region|" + zoneName + "|" + string.Join("|", loops.Select(loop =>
            string.Join(";", loop.Select(p => PlannedElementSignature.ForPoint(p, SignatureQuantumFeet)))));

    private static void PlanRegions(
        ReviewTable table,
        ReviewTableEntry entry,
        IReadOnlyDictionary<string, CandidateZone> zones,
        List<PlannedReviewRegion> regions,
        List<SkippedReviewMark> skipped)
    {
        if (entry.ZoneId is null || !zones.TryGetValue(entry.ZoneId, out var zone))
        {
            skipped.Add(new SkippedReviewMark(entry.ResultId, Subject(entry, true),
                "找不到這個區劃目前的範圍，請重新讀取區劃後再標示"));
            return;
        }

        var drawn = 0;
        for (var index = 0; index < zone.Parts.Count; index++)
        {
            var part = zone.Parts[index];
            if (!part.IsEnclosed) continue;
            regions.Add(new PlannedReviewRegion(
                new ReviewMarkKey(table.PackageId, table.RunId, zone.ZoneId, index), entry.ResultId, zone.Name,
                part.Observation.BoundaryLoops));
            drawn++;
        }

        if (drawn == 0)
            skipped.Add(new SkippedReviewMark(entry.ResultId, Subject(entry, true),
                "區劃沒有封閉的面積，無法建立填滿區域"));
    }

    private static string Subject(ReviewTableEntry entry, bool isArea) => isArea
        ? $"區劃「{entry.ZoneName ?? entry.ZoneId ?? "?"}」"
        : $"{entry.CategoryLabel} {string.Join(",", entry.LocateUniqueIds)}";
}

/// <summary>A red region already in the review view, as the adapter read its mark.</summary>
public sealed class ExistingReviewMark
{
    public ExistingReviewMark(string elementUniqueId, string keyToken, string signature)
    {
        if (string.IsNullOrWhiteSpace(elementUniqueId)) throw new ArgumentException("An element UniqueId is required.", nameof(elementUniqueId));

        ElementUniqueId = elementUniqueId.Trim();
        KeyToken = (keyToken ?? string.Empty).Trim();
        Signature = (signature ?? string.Empty).Trim();
        HasKey = ReviewMarkKey.TryParse(KeyToken, out var key);
        Key = key;
    }

    public string ElementUniqueId { get; }
    public string KeyToken { get; }
    public string Signature { get; }
    public bool HasKey { get; }
    public ReviewMarkKey Key { get; }

    public bool BelongsTo(Guid packageId) => HasKey && Key.PackageId == packageId;
}

/// <summary>
/// The view state the tool changed on one element, and what it changed it to (spec 11.5 item 6: 保存原
/// 視圖狀態與本工具覆寫的元素集合). The states are opaque snapshots the adapter writes and reads; the
/// Application only needs to compare them.
/// </summary>
public sealed class RecordedElementOverride
{
    public RecordedElementOverride(string elementUniqueId, Guid runId, string originalState, string appliedState)
    {
        if (string.IsNullOrWhiteSpace(elementUniqueId)) throw new ArgumentException("An element UniqueId is required.", nameof(elementUniqueId));
        if (runId == Guid.Empty) throw new ArgumentException("Run ID cannot be empty.", nameof(runId));

        ElementUniqueId = elementUniqueId.Trim();
        RunId = runId;
        OriginalState = originalState ?? string.Empty;
        AppliedState = appliedState ?? string.Empty;
    }

    public string ElementUniqueId { get; }

    /// <summary>The run that last painted the element.</summary>
    public Guid RunId { get; }

    /// <summary>The element's override in the view before the tool first touched it.</summary>
    public string OriginalState { get; }

    /// <summary>What the tool set; if the element no longer shows this, somebody changed it since.</summary>
    public string AppliedState { get; }

    public RecordedElementOverride ForRun(Guid runId, string appliedState) =>
        new RecordedElementOverride(ElementUniqueId, runId, OriginalState, appliedState);
}

public enum ReviewMarkAction
{
    /// <summary>A region to draw, or an element to paint for the first time.</summary>
    Create,

    /// <summary>A region whose outline or run changed, or an element already painted by an earlier run.</summary>
    Update,

    /// <summary>Already exactly as planned, by this run.</summary>
    Unchanged,

    /// <summary>A region of this package no longer needed, or an element to give its original look back.</summary>
    Remove
}

public sealed class ReviewRegionChange
{
    internal ReviewRegionChange(ReviewMarkAction action, PlannedReviewRegion? planned, ExistingReviewMark? existing, string reason)
    {
        Action = action;
        Planned = planned;
        Existing = existing;
        Reason = reason;
    }

    public ReviewMarkAction Action { get; }
    public PlannedReviewRegion? Planned { get; }
    public ExistingReviewMark? Existing { get; }
    public string Reason { get; }
}

public sealed class ReviewOverrideChange
{
    internal ReviewOverrideChange(ReviewMarkAction action, string elementUniqueId, PlannedElementOverride? planned, RecordedElementOverride? recorded, string reason)
    {
        Action = action;
        ElementUniqueId = elementUniqueId;
        Planned = planned;
        Recorded = recorded;
        Reason = reason;
    }

    public ReviewMarkAction Action { get; }
    public string ElementUniqueId { get; }
    public PlannedElementOverride? Planned { get; }
    public RecordedElementOverride? Recorded { get; }
    public string Reason { get; }
}

/// <summary>
/// What marking one run in the review view will do to what is already there (spec 13.2): update the
/// package's own regions, draw the missing ones, delete those no longer needed; paint the failing
/// elements, and give every element the tool painted before but that no longer fails its original look.
/// </summary>
/// <remarks>
/// Only the current package's marks and the elements this tool recorded are ever touched. A region of
/// another package, a region whose mark does not parse, and an override the user set on an element the
/// tool never recorded are all outside the diff — that is what 只更新目前 Run 管理的元素 means in a view
/// the user can still draw in.
/// </remarks>
public sealed class ReviewMarkupDiff
{
    private ReviewMarkupDiff(ReviewMarkupPlan plan, IEnumerable<ReviewRegionChange> regions, IEnumerable<ReviewOverrideChange> overrides, int foreignMarks)
    {
        Plan = plan;
        Regions = new ReadOnlyCollection<ReviewRegionChange>(regions.ToList());
        Overrides = new ReadOnlyCollection<ReviewOverrideChange>(overrides.ToList());
        ForeignMarks = foreignMarks;
    }

    public ReviewMarkupPlan Plan { get; }
    public IReadOnlyList<ReviewRegionChange> Regions { get; }
    public IReadOnlyList<ReviewOverrideChange> Overrides { get; }

    /// <summary>Marks in the view that are not this package's and were left alone.</summary>
    public int ForeignMarks { get; }

    public int Count(ReviewMarkAction action) =>
        Regions.Count(r => r.Action == action) + Overrides.Count(o => o.Action == action);

    public bool HasChanges =>
        Regions.Any(r => r.Action != ReviewMarkAction.Unchanged) || Overrides.Any(o => o.Action != ReviewMarkAction.Unchanged);

    /// <summary>The scope summary spec 15 asks for before any automatic change.</summary>
    public string Summary =>
        $"檢討視圖標示：新增 {Count(ReviewMarkAction.Create)}、更新 {Count(ReviewMarkAction.Update)}、" +
        $"移除 {Count(ReviewMarkAction.Remove)}、不變 {Count(ReviewMarkAction.Unchanged)}、略過 {Plan.Skipped.Count}";

    public static ReviewMarkupDiff Compute(
        ReviewMarkupPlan plan,
        IEnumerable<ExistingReviewMark> existingMarks,
        IEnumerable<RecordedElementOverride> recordedOverrides)
    {
        if (plan is null) throw new ArgumentNullException(nameof(plan));
        if (existingMarks is null) throw new ArgumentNullException(nameof(existingMarks));
        if (recordedOverrides is null) throw new ArgumentNullException(nameof(recordedOverrides));

        var marks = existingMarks.ToList();
        var ours = marks.Where(m => m.BelongsTo(plan.PackageId)).ToList();
        var foreign = marks.Count - ours.Count;

        var regionChanges = new List<ReviewRegionChange>();
        var bySlot = ours.GroupBy(m => m.Key.Slot, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.OrderBy(m => m.ElementUniqueId, StringComparer.Ordinal).ToList(), StringComparer.Ordinal);
        var planned = new HashSet<string>(StringComparer.Ordinal);

        foreach (var region in plan.Regions)
        {
            planned.Add(region.Key.Slot);
            if (!bySlot.TryGetValue(region.Key.Slot, out var existing))
            {
                regionChanges.Add(new ReviewRegionChange(ReviewMarkAction.Create, region, null, "未符合區劃尚無標示"));
                continue;
            }

            var keep = existing[0];
            if (keep.Key.RunId == plan.RunId && string.Equals(keep.Signature, region.Signature, StringComparison.Ordinal))
                regionChanges.Add(new ReviewRegionChange(ReviewMarkAction.Unchanged, region, keep, "與本次檢討相同"));
            else
                regionChanges.Add(new ReviewRegionChange(ReviewMarkAction.Update, region, keep,
                    keep.Key.RunId != plan.RunId ? "沿用前次檢討的標示，改為本次檢討" : "區劃範圍已變更"));

            // Two regions in one slot can only come from a copy or an interrupted run; one is enough.
            foreach (var duplicate in existing.Skip(1))
                regionChanges.Add(new ReviewRegionChange(ReviewMarkAction.Remove, null, duplicate, "重複的標示"));
        }

        foreach (var pair in bySlot.Where(p => !planned.Contains(p.Key)).OrderBy(p => p.Key, StringComparer.Ordinal))
            foreach (var stale in pair.Value)
                regionChanges.Add(new ReviewRegionChange(ReviewMarkAction.Remove, null, stale, "區劃已不再未符合"));

        var overrideChanges = new List<ReviewOverrideChange>();
        var recorded = recordedOverrides
            .GroupBy(r => r.ElementUniqueId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        foreach (var paint in plan.Overrides)
        {
            if (!recorded.TryGetValue(paint.ElementUniqueId, out var record))
                overrideChanges.Add(new ReviewOverrideChange(ReviewMarkAction.Create, paint.ElementUniqueId, paint, null, "未符合元素尚未標示"));
            else if (record.RunId == plan.RunId)
                overrideChanges.Add(new ReviewOverrideChange(ReviewMarkAction.Unchanged, paint.ElementUniqueId, paint, record, "已由本次檢討標示"));
            else
                overrideChanges.Add(new ReviewOverrideChange(ReviewMarkAction.Update, paint.ElementUniqueId, paint, record, "沿用前次檢討的標示，改為本次檢討"));
        }

        var painted = new HashSet<string>(plan.Overrides.Select(o => o.ElementUniqueId), StringComparer.Ordinal);
        foreach (var record in recorded.Values.Where(r => !painted.Contains(r.ElementUniqueId)).OrderBy(r => r.ElementUniqueId, StringComparer.Ordinal))
            overrideChanges.Add(new ReviewOverrideChange(ReviewMarkAction.Remove, record.ElementUniqueId, null, record, "元素已不再未符合，恢復原顯示"));

        return new ReviewMarkupDiff(plan, regionChanges, overrideChanges, foreign);
    }
}

/// <summary>What to do with an element the tool painted once, when it is time to give it back.</summary>
public enum OverrideRestoreDecision
{
    /// <summary>The element still shows what the tool set: put the original back.</summary>
    RestoreOriginal,

    /// <summary>Somebody changed it since; their change is kept and the record is dropped.</summary>
    KeepUserChange,

    /// <summary>The element is not in the model any more; only the record goes.</summary>
    ForgetMissing
}

public static class ReviewOverrideRestore
{
    /// <summary>
    /// Decided on the element as it is at the moment of restoring, not when the diff was built: the
    /// view is live and the user may have edited it in between.
    /// </summary>
    public static OverrideRestoreDecision Decide(RecordedElementOverride record, string? currentState)
    {
        if (record is null) throw new ArgumentNullException(nameof(record));
        if (currentState is null) return OverrideRestoreDecision.ForgetMissing;
        return string.Equals(currentState, record.AppliedState, StringComparison.Ordinal)
            ? OverrideRestoreDecision.RestoreOriginal
            : OverrideRestoreDecision.KeepUserChange;
    }
}

/// <summary>One line of what marking the view did.</summary>
public sealed class ReviewMarkupItem
{
    public ReviewMarkupItem(ApplyOutcome outcome, string description, string? elementUniqueId = null, string? message = null)
    {
        if (!Enum.IsDefined(typeof(ApplyOutcome), outcome)) throw new ArgumentOutOfRangeException(nameof(outcome));
        if (string.IsNullOrWhiteSpace(description)) throw new ArgumentException("A result line needs a description.", nameof(description));

        Outcome = outcome;
        Description = description.Trim();
        ElementUniqueId = string.IsNullOrWhiteSpace(elementUniqueId) ? null : elementUniqueId!.Trim();
        Message = string.IsNullOrWhiteSpace(message) ? null : message!.Trim();
    }

    public ApplyOutcome Outcome { get; }
    public string Description { get; }
    public string? ElementUniqueId { get; }
    public string? Message { get; }

    public string Text => Message is null
        ? ApplyResultItem.OutcomeText(Outcome) + " " + Description
        : ApplyResultItem.OutcomeText(Outcome) + " " + Description + "：" + Message;

    public override string ToString() => Text;
}

/// <summary>
/// What marking the view did (spec 15: 完成後提供新增／更新／刪除／略過數量). Unchanged lines are not
/// listed; the plan's skipped results are counted as skipped.
/// </summary>
public sealed class ReviewMarkupResult
{
    public ReviewMarkupResult(Guid packageId, Guid runId, string? viewUniqueId, IEnumerable<ReviewMarkupItem> items, string? fatalError = null)
    {
        PackageId = packageId;
        RunId = runId;
        ViewUniqueId = string.IsNullOrWhiteSpace(viewUniqueId) ? null : viewUniqueId!.Trim();
        Items = new ReadOnlyCollection<ReviewMarkupItem>((items ?? throw new ArgumentNullException(nameof(items))).ToList());
        FatalError = string.IsNullOrWhiteSpace(fatalError) ? null : fatalError!.Trim();
    }

    public Guid PackageId { get; }
    public Guid RunId { get; }
    public string? ViewUniqueId { get; }
    public IReadOnlyList<ReviewMarkupItem> Items { get; }

    /// <summary>Set when the whole marking was rolled back; the view is then exactly as it was.</summary>
    public string? FatalError { get; }

    public bool IsRolledBack => FatalError is not null;

    public int Count(ApplyOutcome outcome) => Items.Count(i => i.Outcome == outcome);

    public string Summary => IsRolledBack
        ? "檢討視圖標示已整批復原：" + FatalError
        : $"檢討視圖標示完成：新增 {Count(ApplyOutcome.Created)}、更新 {Count(ApplyOutcome.Updated)}、" +
          $"刪除 {Count(ApplyOutcome.Deleted)}、略過 {Count(ApplyOutcome.Skipped)}、失敗 {Count(ApplyOutcome.Failed)}";
}
