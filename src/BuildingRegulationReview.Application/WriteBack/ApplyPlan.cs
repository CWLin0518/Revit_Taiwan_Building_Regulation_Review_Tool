using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;

namespace BuildingRegulationReview.Application.WriteBack;

/// <summary>
/// The order write-back has to happen in (spec 10.5). The stages exist because Revit's own model
/// imposes a dependency chain, not because the code prefers to batch: a tag needs its Area, an Area
/// needs a closed ring of boundary lines around its placement point, and an Area that loses that
/// ring becomes unplaced and does not come back on its own.
/// </summary>
public enum ApplyStage
{
    /// <summary>Tags first, then the Areas they annotate, then anything else no longer planned.</summary>
    Remove,

    /// <summary>
    /// Every boundary change, deletions included, in one go. Splitting the old ring from the new one
    /// across two commits would leave surviving Areas momentarily unenclosed.
    /// </summary>
    Boundaries,

    /// <summary>Areas, once the boundaries that enclose them exist.</summary>
    Areas,

    /// <summary>Tags, once the Areas they point at exist.</summary>
    Tags,

    /// <summary>
    /// The 單線圖 copies and their area labels in the Drafting View, which depend on nothing in the
    /// Area Plan and so come last. A caller whose adapter cannot reach a Drafting View leaves
    /// <see cref="ManagedElementKind.DetailCurve"/> and <see cref="ManagedElementKind.DetailLabel"/>
    /// out of its writable kinds, and these rows are then reported as deferred instead of half-done.
    /// </summary>
    DetailCurves
}

/// <summary>What a run does when Revit refuses one individual element (spec 10.5).</summary>
public enum ApplyFailurePolicy
{
    /// <summary>Log it, leave that element alone and carry on. The user decides what to do next.</summary>
    SkipAndLog,

    /// <summary>Treat any refusal as fatal and roll the whole group back.</summary>
    RollBackEverything
}

/// <summary>One thing write-back does, taken straight from the preview row the user approved.</summary>
public sealed class ApplyStep
{
    public ApplyStep(ApplyStage stage, ApplyPreviewItem item)
    {
        if (!Enum.IsDefined(typeof(ApplyStage), stage)) throw new ArgumentOutOfRangeException(nameof(stage));
        if (item is null) throw new ArgumentNullException(nameof(item));
        if (item.Change == ApplyChangeKind.Unchanged)
            throw new ArgumentException("An unchanged element is not a step.", nameof(item));
        if (item.Change != ApplyChangeKind.Delete && item.Planned is null)
            throw new ArgumentException("A step that writes needs the planned element.", nameof(item));
        if (item.Change != ApplyChangeKind.Add && item.ElementUniqueId is null)
            throw new ArgumentException("A step that touches an existing element needs its UniqueId.", nameof(item));

        Stage = stage;
        Item = item;
    }

    public ApplyStage Stage { get; }
    public ApplyPreviewItem Item { get; }

    public ApplyChangeKind Change => Item.Change;
    public ManagedElementKey Key => Item.Key;
    public ManagedElementKind Kind => Item.Kind;
    public string Description => Item.Description;

    /// <summary>What to write. Null only on a deletion.</summary>
    public PlannedElement? Planned => Item.Planned;

    /// <summary>The element to update or delete. Null only on an addition.</summary>
    public string? ElementUniqueId => Item.ElementUniqueId;

    public override string ToString() => Item.Text;
}

/// <summary>
/// The approved preview turned into an ordered list of steps, plus the rows this write-back cannot
/// carry out yet (spec 10.5 steps 3 to 5 — Color Scheme and Drafting View — belong to P2-T08). Pure
/// data: the Revit adapter walks the stages and does the API work, so the ordering and the rule
/// about what may be deleted are both testable with no document open.
/// </summary>
/// <remarks>
/// Deletion is deliberately not filtered by the writable kinds. Removing an element this package
/// owns needs no knowledge of what kind it is, and refusing to remove a kind the writer cannot
/// create would leave the model accumulating orphans that nothing would ever clean up.
/// </remarks>
public sealed class ApplyPlan
{
    /// <summary>
    /// What lives in the Area Plan. The default, because a caller that says nothing about its
    /// adapter is taken to reach no further than the plan it named.
    /// </summary>
    public static readonly IReadOnlyList<ManagedElementKind> AreaPlanKinds = new ReadOnlyCollection<ManagedElementKind>(
        new[] { ManagedElementKind.AreaBoundaryLine, ManagedElementKind.Area, ManagedElementKind.AreaTag });

    /// <summary>
    /// Everything the tool writes, the Drafting View copies included (spec 10.5 items 1 to 4). What
    /// the Editor passes, now that the write-back can reach the Drafting View too.
    /// </summary>
    public static readonly IReadOnlyList<ManagedElementKind> AllKinds = new ReadOnlyCollection<ManagedElementKind>(
        (ManagedElementKind[])Enum.GetValues(typeof(ManagedElementKind)));

    private ApplyPlan(
        Guid packageId,
        IEnumerable<ApplyStep> steps,
        IEnumerable<ApplyPreviewItem> deferred,
        IEnumerable<string> warnings,
        IDictionary<ManagedElementKey, string> existingElementIds,
        ColorSchemeEntries colorEntries,
        IEnumerable<ApplyPreviewItem> unchanged,
        int untouchedElementCount)
    {
        PackageId = packageId;
        Steps = new ReadOnlyCollection<ApplyStep>(steps.ToList());
        Deferred = new ReadOnlyCollection<ApplyPreviewItem>(deferred.ToList());
        Warnings = new ReadOnlyCollection<string>(warnings.ToList());
        ExistingElementIds = new ReadOnlyDictionary<ManagedElementKey, string>(existingElementIds);
        ColorEntries = colorEntries;
        Unchanged = new ReadOnlyCollection<ApplyPreviewItem>(unchanged.ToList());
        UntouchedElementCount = untouchedElementCount;
    }

    public Guid PackageId { get; }

    /// <summary>Every step, already in the order they have to run.</summary>
    public IReadOnlyList<ApplyStep> Steps { get; }

    /// <summary>
    /// Rows the preview listed that this write-back leaves alone, because creating that kind is a
    /// later task. They are reported rather than dropped, so the numbers on screen still add up.
    /// </summary>
    public IReadOnlyList<ApplyPreviewItem> Deferred { get; }

    public IReadOnlyList<string> Warnings { get; }

    /// <summary>
    /// The colour every 區劃 that reaches the model is drawn with (spec 10.5 item 3), taken from the
    /// same approved preview as the steps. It covers the unchanged rows too, because a run that
    /// changes no Area still has to say what all of the scheme's entries are.
    /// </summary>
    public ColorSchemeEntries ColorEntries { get; }

    /// <summary>
    /// Where this package's elements are in the model right now, keyed the way the plan is. It
    /// covers the rows that survive the run — unchanged and updated — so a step can find something
    /// it depends on without re-reading the model: a tag has to be given the Area it annotates, and
    /// that Area is very often one nothing is changing.
    /// </summary>
    public IReadOnlyDictionary<ManagedElementKey, string> ExistingElementIds { get; }

    /// <summary>
    /// Rows the draft and the model already agree on, so no step writes them. They are carried
    /// rather than counted away because a run that writes nothing still has to be able to prove the
    /// boundaries are sound: spec 10.6's area cross-check is what decides whether the package may
    /// reach Ready, and on a model that is already correct these are the only Areas there are to
    /// measure. Without them such a run reports no evidence at all and the package can never leave
    /// 區劃草稿, however right the model is.
    /// </summary>
    public IReadOnlyList<ApplyPreviewItem> Unchanged { get; }

    public int UnchangedCount => Unchanged.Count;

    /// <summary>The unchanged Areas, which is what the area cross-check can still measure.</summary>
    public IEnumerable<ApplyPreviewItem> UnchangedAreas =>
        Unchanged.Where(item => item.Kind == ManagedElementKind.Area && item.Planned is not null &&
                                item.ElementUniqueId is not null);

    /// <summary>Elements in the views this package does not own, which will not be touched.</summary>
    public int UntouchedElementCount { get; }

    public bool IsEmpty => Steps.Count == 0;

    public IReadOnlyList<ApplyStep> StepsOf(ApplyStage stage) =>
        new ReadOnlyCollection<ApplyStep>(Steps.Where(s => s.Stage == stage).ToList());

    public int CountOf(ApplyChangeKind change) => Steps.Count(s => s.Change == change);

    /// <summary>One line naming what is deferred, per kind, for the confirmation dialog.</summary>
    public IReadOnlyList<string> DeferredNotes => new ReadOnlyCollection<string>(
        Deferred
            .GroupBy(item => item.Kind)
            .OrderBy(group => (int)group.Key)
            .Select(group => string.Format(
                CultureInfo.InvariantCulture,
                "{0} {1} 個尚未寫入，會在單線圖輸出階段建立。",
                ManagedElementKey.Describe(group.Key),
                group.Count()))
            .ToList());

    public string Summary => IsEmpty
        ? "沒有需要寫入模型的變更。"
        : string.Format(
            CultureInfo.InvariantCulture,
            "將建立 {0} 個、更新 {1} 個、刪除 {2} 個元素；不變 {3} 個，未受管理 {4} 個不會被更動。",
            CountOf(ApplyChangeKind.Add),
            CountOf(ApplyChangeKind.Update),
            CountOf(ApplyChangeKind.Delete),
            UnchangedCount,
            UntouchedElementCount);

    /// <summary>
    /// Schedules the preview. <paramref name="writableKinds"/> is what the caller's adapter can
    /// actually create; anything else it would add or update is deferred instead of attempted.
    /// </summary>
    public static ApplyPlan Build(ApplyPreview preview, IEnumerable<ManagedElementKind>? writableKinds = null)
    {
        if (preview is null) throw new ArgumentNullException(nameof(preview));

        var writable = new HashSet<ManagedElementKind>(writableKinds ?? AreaPlanKinds);
        var steps = new List<ApplyStep>();
        var deferred = new List<ApplyPreviewItem>();
        var existing = new Dictionary<ManagedElementKey, string>();

        foreach (var item in preview.Items)
        {
            if (item.Change != ApplyChangeKind.Delete && item.ElementUniqueId is not null)
                existing[item.Key] = item.ElementUniqueId;

            if (item.Change == ApplyChangeKind.Unchanged) continue;

            if (item.Change == ApplyChangeKind.Delete)
            {
                steps.Add(new ApplyStep(StageOfDelete(item.Kind), item));
                continue;
            }

            if (!writable.Contains(item.Kind))
            {
                deferred.Add(item);
                continue;
            }

            steps.Add(new ApplyStep(StageOfWrite(item.Kind), item));
        }

        return new ApplyPlan(
            preview.PackageId,
            steps.OrderBy(Rank).ThenBy(s => s.Key.ToToken(), StringComparer.Ordinal),
            deferred,
            preview.Warnings,
            existing,
            ColorSchemeEntries.From(preview),
            preview.Unchanged,
            preview.UntouchedElementCount);
    }

    private static ApplyStage StageOfDelete(ManagedElementKind kind) =>
        kind == ManagedElementKind.AreaBoundaryLine ? ApplyStage.Boundaries : ApplyStage.Remove;

    private static ApplyStage StageOfWrite(ManagedElementKind kind) => kind switch
    {
        ManagedElementKind.AreaBoundaryLine => ApplyStage.Boundaries,
        ManagedElementKind.Area => ApplyStage.Areas,
        ManagedElementKind.AreaTag => ApplyStage.Tags,
        ManagedElementKind.DetailCurve => ApplyStage.DetailCurves,
        ManagedElementKind.DetailLabel => ApplyStage.DetailCurves,
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    /// <summary>
    /// Sorts the steps. The stage comes first; inside the removal stage a tag goes before the Area it
    /// annotates, and inside the boundary stage the old lines go before the new ones, so the ring is
    /// rebuilt rather than doubled.
    /// </summary>
    private static int Rank(ApplyStep step) => ((int)step.Stage * 100)
        + (step.Change == ApplyChangeKind.Delete ? DeleteRank(step.Kind) : 10);

    private static int DeleteRank(ManagedElementKind kind) => kind switch
    {
        ManagedElementKind.AreaTag => 0,
        ManagedElementKind.Area => 1,
        _ => 2
    };
}
