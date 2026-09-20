using System;
using System.Collections.Generic;
using System.Linq;
using BuildingRegulationReview.Application.Geometry;
using BuildingRegulationReview.Application.WriteBack;
using BuildingRegulationReview.Domain.Common;
using BuildingRegulationReview.Domain.Geometry;
using BuildingRegulationReview.Domain.Regions;
using Xunit;

namespace BuildingRegulationReview.Core.Tests.WriteBack;

/// <summary>
/// The schedule write-back follows (spec 10.5, P2-T07). Everything the Revit adapter is not allowed
/// to decide for itself is decided here, so the ordering and the deletion rule can be asserted with
/// no document open.
/// </summary>
public class ApplyPlanTests
{
    private static readonly Guid PackageId = Guid.Parse("11111111-2222-3333-4444-555555555555");
    private static readonly Guid ZoneId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
    private static readonly GeometryTolerance Tolerance = GeometryTolerance.Default;
    private static readonly DateTime SolvedAt = new DateTime(2026, 9, 21, 0, 0, 0, DateTimeKind.Utc);

    // ---- what becomes a step ------------------------------------------------------------------

    [Fact]
    public void AnUnchangedElementIsNotAStep()
    {
        var (map, zones) = OneRoom();
        var preview = ApplyPreview.Build(PackageId, map, zones, AsWritten(map, zones));

        var plan = ApplyPlan.Build(preview);

        Assert.True(plan.IsEmpty);
        Assert.Equal(10, plan.UnchangedCount);
        Assert.Equal("沒有需要寫入模型的變更。", plan.Summary);
    }

    [Fact]
    public void EachAreaPlanKindGoesToTheStageItsDependenciesAllow()
    {
        var plan = ApplyPlan.Build(Preview());

        Assert.All(plan.StepsOf(ApplyStage.Boundaries), s => Assert.Equal(ManagedElementKind.AreaBoundaryLine, s.Kind));
        Assert.All(plan.StepsOf(ApplyStage.Areas), s => Assert.Equal(ManagedElementKind.Area, s.Kind));
        Assert.All(plan.StepsOf(ApplyStage.Tags), s => Assert.Equal(ManagedElementKind.AreaTag, s.Kind));
        Assert.Equal(4, plan.StepsOf(ApplyStage.Boundaries).Count);
        Assert.Single(plan.StepsOf(ApplyStage.Areas));
        Assert.Single(plan.StepsOf(ApplyStage.Tags));
    }

    [Fact]
    public void DefersTheDraftingViewCopiesInsteadOfAttemptingThem()
    {
        var plan = ApplyPlan.Build(Preview());

        Assert.All(plan.Steps, s => Assert.NotEqual(ManagedElementKind.DetailCurve, s.Kind));
        Assert.Equal(4, plan.Deferred.Count);
        Assert.All(plan.Deferred, item => Assert.Equal(ManagedElementKind.DetailCurve, item.Kind));
        Assert.Contains(plan.DeferredNotes, note => note.Contains("單線圖細部線 4 個尚未寫入", StringComparison.Ordinal));
    }

    [Fact]
    public void WritesTheDraftingViewCopiesWhenTheCallerSaysItCan()
    {
        var plan = ApplyPlan.Build(Preview(), ApplyPlan.AllKinds);

        Assert.Empty(plan.Deferred);
        Assert.Equal(4, plan.StepsOf(ApplyStage.DetailCurves).Count);
        Assert.All(plan.StepsOf(ApplyStage.DetailCurves), s => Assert.Equal(ManagedElementKind.DetailCurve, s.Kind));
    }

    [Fact]
    public void EveryKindIsWritableUnderAllKinds()
    {
        // The list is derived from the enum rather than typed out, so a kind added later cannot be
        // silently left out of the one run that is supposed to write everything.
        Assert.Equal(
            Enum.GetValues(typeof(ManagedElementKind)).Cast<ManagedElementKind>().ToList(),
            ApplyPlan.AllKinds.ToList());
    }

    [Fact]
    public void TheDraftingViewCopiesComeAfterEverythingInTheAreaPlan()
    {
        // They depend on nothing the Area Plan holds, and the Drafting View may not exist yet — so
        // they run last, where a failure to make the view cannot strand a half-written Area Plan.
        var plan = ApplyPlan.Build(MixedPreview(), ApplyPlan.AllKinds);
        var stages = plan.Steps.Select(s => s.Stage).ToList();

        var firstDetail = stages.IndexOf(ApplyStage.DetailCurves);
        Assert.True(firstDetail > 0);
        Assert.All(stages.Skip(firstDetail), stage => Assert.Equal(ApplyStage.DetailCurves, stage));
    }

    // ---- the colours that travel with the plan --------------------------------------------------

    [Fact]
    public void CarriesOneColourEntryPerZoneIncludingTheUnchangedOnes()
    {
        var (map, zones) = OneRoom();
        var preview = ApplyPreview.Build(PackageId, map, zones, AsWritten(map, zones));

        var plan = ApplyPlan.Build(preview, ApplyPlan.AllKinds);

        // Nothing changes in the Area Plan on this run, but the colour scheme still has to say what
        // all of its entries are — building it from the steps alone would look like no zones left.
        Assert.True(plan.IsEmpty);
        Assert.Single(plan.ColorEntries.Entries);
        Assert.Equal("A", plan.ColorEntries.Entries[0].Value);
        Assert.Equal(ZoneColorPalette.At(0), plan.ColorEntries.Entries[0].Color);
    }

    [Fact]
    public void CarriesNoColourForAZoneThatIsBeingDeleted()
    {
        var plan = ApplyPlan.Build(PreviewOfDeletions(), ApplyPlan.AllKinds);

        Assert.True(plan.ColorEntries.IsEmpty);
    }

    [Fact]
    public void RemovesAKindItCannotCreate()
    {
        // Nothing writes detail curves yet, but an orphan left behind still has to be cleared out,
        // or the model would accumulate elements no run would ever clean up.
        var plan = ApplyPlan.Build(PreviewOfDeletions());

        var detail = plan.StepsOf(ApplyStage.Remove).Where(s => s.Kind == ManagedElementKind.DetailCurve).ToList();
        Assert.Equal(4, detail.Count);
        Assert.All(detail, s => Assert.Equal(ApplyChangeKind.Delete, s.Change));
        Assert.Empty(plan.Deferred);
    }

    // ---- the order ----------------------------------------------------------------------------

    [Fact]
    public void RunsTheStagesInDependencyOrder()
    {
        var plan = ApplyPlan.Build(MixedPreview());

        var stages = plan.Steps.Select(s => (int)s.Stage).ToList();
        Assert.Equal(stages.OrderBy(x => x).ToList(), stages);
    }

    [Fact]
    public void TakesATagAwayBeforeTheAreaItAnnotates()
    {
        var plan = ApplyPlan.Build(PreviewOfDeletions());
        var removals = plan.StepsOf(ApplyStage.Remove);

        var tag = removals.ToList().FindIndex(s => s.Kind == ManagedElementKind.AreaTag);
        var area = removals.ToList().FindIndex(s => s.Kind == ManagedElementKind.Area);

        Assert.True(tag >= 0 && area >= 0);
        Assert.True(tag < area, "A tag whose Area is deleted first would be deleted with it.");
    }

    [Fact]
    public void ClearsOldBoundariesBeforeRedrawingTheRestInTheSameStage()
    {
        var plan = ApplyPlan.Build(MixedPreview());
        var boundaries = plan.StepsOf(ApplyStage.Boundaries).ToList();

        Assert.Contains(boundaries, s => s.Change == ApplyChangeKind.Delete);
        Assert.Contains(boundaries, s => s.Change != ApplyChangeKind.Delete);
        var lastDelete = boundaries.FindLastIndex(s => s.Change == ApplyChangeKind.Delete);
        var firstWrite = boundaries.FindIndex(s => s.Change != ApplyChangeKind.Delete);
        Assert.True(lastDelete < firstWrite);
    }

    [Fact]
    public void OrdersTheStepsOfOneStageTheSameWayEveryTime()
    {
        var preview = Preview();

        var first = ApplyPlan.Build(preview).Steps.Select(s => s.Key.ToToken()).ToList();
        var second = ApplyPlan.Build(preview).Steps.Select(s => s.Key.ToToken()).ToList();

        Assert.Equal(first, second);
    }

    // ---- what a step carries ------------------------------------------------------------------

    [Fact]
    public void AWritingStepCarriesTheGeometryAndAnExistingOneCarriesItsElement()
    {
        var plan = ApplyPlan.Build(MixedPreview());

        Assert.All(
            plan.Steps.Where(s => s.Change != ApplyChangeKind.Delete),
            s => Assert.NotNull(s.Planned));
        Assert.All(
            plan.Steps.Where(s => s.Change != ApplyChangeKind.Add),
            s => Assert.False(string.IsNullOrWhiteSpace(s.ElementUniqueId)));
        Assert.All(
            plan.StepsOf(ApplyStage.Boundaries).Where(s => s.Change != ApplyChangeKind.Delete),
            s => Assert.Equal(2, s.Planned!.Points.Count));
    }

    [Fact]
    public void KnowsWhereTheElementsThatSurviveTheRunAlreadyAre()
    {
        var (map, zones) = OneRoom();
        var written = AsWritten(map, zones);
        var renamed = Rename(map, zones, "B");
        var preview = ApplyPreview.Build(PackageId, renamed.Map, renamed.Zones, written);

        var plan = ApplyPlan.Build(preview);

        // The Area's tag has to be given its Area, and that Area may be one nothing is changing.
        var areaKey = new ManagedElementKey(PackageId, ZoneId, ManagedElementKind.Area, 0, 0);
        Assert.True(plan.ExistingElementIds.ContainsKey(areaKey));
        Assert.All(plan.Steps.Where(s => s.Change == ApplyChangeKind.Delete),
            s => Assert.False(plan.ExistingElementIds.ContainsKey(s.Key)));
    }

    [Fact]
    public void ARowThatDoesNotKnowWhatToWriteIsNotAllowedToBecomeAStep()
    {
        var key = new ManagedElementKey(PackageId, ZoneId, ManagedElementKind.Area, 0, 0);
        var noGeometry = new ApplyPreviewItem(ApplyChangeKind.Add, key, "「A」的面積");
        var noElement = new ApplyPreviewItem(
            ApplyChangeKind.Update,
            key,
            "「A」的面積",
            planned: new PlannedElement(key, "sig", "「A」的面積"));

        Assert.Throws<ArgumentException>(() => new ApplyStep(ApplyStage.Areas, noGeometry));
        Assert.Throws<ArgumentException>(() => new ApplyStep(ApplyStage.Areas, noElement));
    }

    // ---- the numbers on screen -----------------------------------------------------------------

    [Fact]
    public void CountsWhatItWillDoAndWhatItWillLeaveAlone()
    {
        var foreign = new ExistingManagedElement("other-1", "BCR/" + Guid.NewGuid().ToString("N") + "/" +
            Guid.NewGuid().ToString("N") + "/area/0/0", "sig");
        var (map, zones) = OneRoom();

        var plan = ApplyPlan.Build(ApplyPreview.Build(PackageId, map, zones, new[] { foreign }));

        Assert.Equal(6, plan.CountOf(ApplyChangeKind.Add));
        Assert.Equal(0, plan.CountOf(ApplyChangeKind.Delete));
        Assert.Equal(1, plan.UntouchedElementCount);
        Assert.Contains("未受管理 1 個不會被更動", plan.Summary, StringComparison.Ordinal);
    }

    // ---- helpers ------------------------------------------------------------------------------

    /// <summary>A model the tool has never written to: everything is an addition.</summary>
    private static ApplyPreview Preview()
    {
        var (map, zones) = OneRoom();
        return ApplyPreview.Build(PackageId, map, zones);
    }

    /// <summary>The drafts emptied out, so everything already written has to go.</summary>
    private static ApplyPreview PreviewOfDeletions()
    {
        var (map, zones) = OneRoom();
        return ApplyPreview.Build(PackageId, map, ZoneDraftSet.Empty, AsWritten(map, zones));
    }

    /// <summary>
    /// A zone that shrinks back to one room after both were written: the outer ring is redrawn and
    /// the two segments that only the room being dropped needed have to go.
    /// </summary>
    private static ApplyPreview MixedPreview()
    {
        var map = Solve(Rectangle(0, 0, 20, 10, "OUTER").Concat(new[] { Seg(10, 0, 10, 10, "DIVIDER") }));
        var written = AsWritten(map, Assign(map, FaceAt(map, 5, 5), FaceAt(map, 15, 5)));
        return ApplyPreview.Build(PackageId, map, Assign(map, FaceAt(map, 5, 5)), written);
    }

    private static (PlanRegionMap Map, ZoneDraftSet Zones) Rename(PlanRegionMap map, ZoneDraftSet zones, string name) =>
        (map, Succeeds(zones.Rename(ZoneId, name)));

    private static IReadOnlyList<ExistingManagedElement> AsWritten(PlanRegionMap map, ZoneDraftSet zones) =>
        ZoneWritePlan.Build(PackageId, map, zones)
            .Select((element, index) => new ExistingManagedElement(
                "element-" + index,
                element.Key.ToToken(),
                element.Signature,
                element.Description))
            .ToList();

    private static (PlanRegionMap Map, ZoneDraftSet Zones) OneRoom()
    {
        var map = Solve(Rectangle(0, 0, 20, 10, "OUTER").Concat(new[] { Seg(10, 0, 10, 10, "DIVIDER") }));
        return (map, Assign(map, FaceAt(map, 5, 5)));
    }

    private static ZoneDraftSet Assign(PlanRegionMap map, params int[] faceIds) =>
        Succeeds(ZoneDraftSet.Empty.Add(new ZoneDraft(ZoneId, "A", ZoneColorPalette.At(0), faceIds)));

    private static PlanRegionMap Solve(IEnumerable<Segment2D> segments)
    {
        var snapshot = new PlanGeometrySnapshot(PackageId, "host-doc", "level-1", segments, Tolerance);
        var network = Succeeds(new LineNetworkRepairer().Repair(snapshot, SolvedAt));
        return Succeeds(new RegionSolver().Solve(network, SolvedAt));
    }

    private static int FaceAt(PlanRegionMap map, double x, double y) => map.FaceAt(new Point2D(x, y))!.Id;

    private static Segment2D[] Rectangle(double x, double y, double width, double height, string prefix) => new[]
    {
        Seg(x, y, x + width, y, prefix + "-S"),
        Seg(x + width, y, x + width, y + height, prefix + "-E"),
        Seg(x + width, y + height, x, y + height, prefix + "-N"),
        Seg(x, y + height, x, y, prefix + "-W")
    };

    private static Segment2D Seg(double x1, double y1, double x2, double y2, string element) =>
        new Segment2D(new Point2D(x1, y1), new Point2D(x2, y2), new SourceRef("doc", element, GeometrySourceKind.WallCenterline));

    private static T Succeeds<T>(Result<T> result)
    {
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.ToString() : string.Empty);
        return result.Value;
    }
}
