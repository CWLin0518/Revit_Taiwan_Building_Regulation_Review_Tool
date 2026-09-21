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
/// P2-T07's exit criteria (spec 10.5 and 10.6): running the same drafts three times in a row must
/// not add elements, and nothing the tool did not create may ever be removed.
/// </summary>
/// <remarks>
/// The model here is a dictionary, not a Revit document — it does exactly what the Revit adapter is
/// told to do by <see cref="ApplyPlan"/> and nothing else. That is the point: if the plan says to
/// create the same boundary line three times, no amount of care in the adapter would save the
/// model, so the property belongs to the plan and is asserted where it lives.
/// </remarks>
public class ApplyIdempotencyTests
{
    private static readonly Guid PackageId = Guid.Parse("11111111-2222-3333-4444-555555555555");
    private static readonly Guid ZoneId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
    private static readonly Guid SecondZoneId = Guid.Parse("bbbbbbbb-cccc-dddd-eeee-ffffffffffff");
    private static readonly GeometryTolerance Tolerance = GeometryTolerance.Default;
    private static readonly DateTime SolvedAt = new DateTime(2026, 9, 21, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void ThreeRunsOfTheSameDraftLeaveTheModelTheSizeTheFirstOneMadeIt()
    {
        var (map, zones) = TwoRoomsOneZone();
        var model = new FakeModel();

        var first = model.Apply(Plan(map, zones, model));
        var second = model.Apply(Plan(map, zones, model));
        var third = model.Apply(Plan(map, zones, model));

        // Six boundary segments, because the divider wall splits the outer walls into two each,
        // plus the one Area the merged 區劃 gets and its tag.
        Assert.Equal(8, first.Created);
        Assert.Equal(8, model.Count);
        Assert.Equal(0, second.Created + second.Updated + second.Deleted);
        Assert.Equal(0, third.Created + third.Updated + third.Deleted);
        Assert.Equal(8, model.Count);
    }

    [Fact]
    public void ASecondRunAfterAnEditUpdatesInPlaceInsteadOfAddingASecondSet()
    {
        var (map, zones) = TwoRoomsOneZone();
        var model = new FakeModel();
        model.Apply(Plan(map, zones, model));
        var before = model.Count;

        var renamed = Succeeds(zones.Rename(ZoneId, "改過的名字"));
        var run = model.Apply(Plan(map, renamed, model));

        Assert.Equal(0, run.Created);
        Assert.Equal(2, run.Updated); // the Area and its tag carry the name; the walls did not move
        Assert.Equal(before, model.Count);
    }

    [Fact]
    public void ShrinkingAZoneRemovesTheBoundariesItNoLongerNeedsAndNothingElse()
    {
        var (map, both) = TwoRoomsOneZone();
        var model = new FakeModel();
        model.Apply(Plan(map, both, model));

        var oneRoom = Assign(ZoneId, "A", map, FaceAt(map, 5, 5));
        var run = model.Apply(Plan(map, oneRoom, model));

        Assert.Equal(2, run.Deleted);
        Assert.Equal(4, model.CountOf(ManagedElementKind.AreaBoundaryLine));
        Assert.Equal(1, model.CountOf(ManagedElementKind.Area));
        Assert.Equal(1, model.CountOf(ManagedElementKind.AreaTag));
    }

    [Fact]
    public void DeletingEveryDraftClearsThisPackageAndStopsThere()
    {
        var (map, zones) = TwoRoomsOneZone();
        var model = new FakeModel();
        model.Apply(Plan(map, zones, model));

        var run = model.Apply(Plan(map, ZoneDraftSet.Empty, model));

        Assert.Equal(8, run.Deleted);
        Assert.Equal(0, model.Count);
    }

    [Fact]
    public void NeverTouchesHandDrawnWorkOrAnotherPackagesElements()
    {
        var (map, zones) = TwoRoomsOneZone();
        var model = new FakeModel();
        model.AddForeign("hand-drawn", string.Empty);
        model.AddForeign(
            "other-package",
            new ManagedElementKey(Guid.NewGuid(), Guid.NewGuid(), ManagedElementKind.Area, 0, 0).ToToken());

        model.Apply(Plan(map, zones, model));
        model.Apply(Plan(map, ZoneDraftSet.Empty, model));

        Assert.True(model.Holds("hand-drawn"));
        Assert.True(model.Holds("other-package"));
        Assert.Equal(2, model.Count);
    }

    [Fact]
    public void TwoPackagesInOneModelDoNotDeleteEachOther()
    {
        var (map, zones) = TwoRoomsOneZone();
        var otherPackage = Guid.Parse("22222222-3333-4444-5555-666666666666");
        var model = new FakeModel();

        model.Apply(model.Plan(PackageId, map, zones));
        var otherZones = Assign(SecondZoneId, "B", map, FaceAt(map, 15, 5));
        model.Apply(model.Plan(otherPackage, map, otherZones));
        var afterBoth = model.Count;

        // The second package now re-runs with nothing drafted: only its own elements may go.
        var run = model.Apply(model.Plan(otherPackage, map, ZoneDraftSet.Empty));

        Assert.Equal(8, model.Count);
        Assert.Equal(afterBoth - 8, run.Deleted);
        Assert.Equal(8, model.CountOwnedBy(PackageId));
    }

    // ---- the same criteria once the Drafting View is written too (P2-T08) -----------------------

    [Fact]
    public void ThreeRunsThatAlsoWriteTheDraftingViewStillLeaveTheModelTheSizeTheFirstOneMadeIt()
    {
        var (map, zones) = TwoRoomsOneZone();
        var model = new FakeModel();

        var first = model.Apply(FullPlan(map, zones, model));
        var second = model.Apply(FullPlan(map, zones, model));
        var third = model.Apply(FullPlan(map, zones, model));

        // The eight Area Plan elements plus one 單線圖 copy of each of the six boundary segments and
        // the one area label of the zone's single part.
        Assert.Equal(15, first.Created);
        Assert.Equal(15, model.Count);
        Assert.Equal(6, model.CountOf(ManagedElementKind.DetailCurve));
        Assert.Equal(1, model.CountOf(ManagedElementKind.DetailLabel));
        Assert.Equal(0, second.Created + second.Updated + second.Deleted);
        Assert.Equal(0, third.Created + third.Updated + third.Deleted);
        Assert.Equal(15, model.Count);
    }

    [Fact]
    public void ShrinkingAZoneClearsTheDraftingViewCopiesItNoLongerNeeds()
    {
        var (map, both) = TwoRoomsOneZone();
        var model = new FakeModel();
        model.Apply(FullPlan(map, both, model));

        var oneRoom = Assign(ZoneId, "A", map, FaceAt(map, 5, 5));
        model.Apply(FullPlan(map, oneRoom, model));

        // The 單線圖 is a copy of the boundary, so it shrinks with it rather than keeping a line
        // that no longer fences anything.
        Assert.Equal(4, model.CountOf(ManagedElementKind.AreaBoundaryLine));
        Assert.Equal(4, model.CountOf(ManagedElementKind.DetailCurve));
    }

    [Fact]
    public void DeletingEveryDraftClearsTheDraftingViewCopiesToo()
    {
        var (map, zones) = TwoRoomsOneZone();
        var model = new FakeModel();
        model.AddForeign("hand-drawn", string.Empty);
        model.Apply(FullPlan(map, zones, model));

        var run = model.Apply(FullPlan(map, ZoneDraftSet.Empty, model));

        Assert.Equal(15, run.Deleted);
        Assert.Equal(1, model.Count);
        Assert.True(model.Holds("hand-drawn"));
    }

    [Fact]
    public void ARunThatCannotReachTheDraftingViewLeavesThoseCopiesForNextTimeInsteadOfLosingThem()
    {
        var (map, zones) = TwoRoomsOneZone();
        var model = new FakeModel();

        // The Area Plan alone, as a host with no Drafting View would schedule it.
        model.Apply(Plan(map, zones, model));
        Assert.Equal(0, model.CountOf(ManagedElementKind.DetailCurve));

        // And the same drafts once the Drafting View is reachable: only the copies are new.
        var run = model.Apply(FullPlan(map, zones, model));

        Assert.Equal(7, run.Created);
        Assert.Equal(0, run.Updated + run.Deleted);
        Assert.Equal(15, model.Count);
    }

    // ---- the model the plan is executed against -------------------------------------------------

    /// <summary>What a run did, in the only three numbers the exit criteria care about.</summary>
    private sealed class RunTally
    {
        public int Created { get; set; }
        public int Updated { get; set; }
        public int Deleted { get; set; }
    }

    /// <summary>
    /// A stand-in for the Area Plan: a bag of elements, each with the ownership token and signature
    /// the tool wrote on it. It carries out a plan literally, including the ownership re-check the
    /// Revit adapter does before every deletion.
    /// </summary>
    private sealed class FakeModel
    {
        private readonly Dictionary<string, ExistingManagedElement> _elements =
            new Dictionary<string, ExistingManagedElement>(StringComparer.Ordinal);
        private int _nextId;

        public int Count => _elements.Count;

        public bool Holds(string uniqueId) => _elements.ContainsKey(uniqueId);

        public int CountOf(ManagedElementKind kind) => _elements.Values.Count(e => e.HasKey && e.Key.Kind == kind);

        public int CountOwnedBy(Guid packageId) => _elements.Values.Count(e => e.BelongsTo(packageId));

        public void AddForeign(string uniqueId, string token) =>
            _elements[uniqueId] = new ExistingManagedElement(uniqueId, string.IsNullOrEmpty(token) ? "人工繪製" : token, string.Empty);

        public ApplyPlan Plan(Guid packageId, PlanRegionMap map, ZoneDraftSet zones) =>
            Plan(packageId, map, zones, ApplyPlan.AreaPlanKinds);

        public ApplyPlan Plan(
            Guid packageId,
            PlanRegionMap map,
            ZoneDraftSet zones,
            IReadOnlyList<ManagedElementKind> writableKinds) =>
            ApplyPlan.Build(
                ApplyPreview.Build(packageId, map, zones, _elements.Values.ToList()),
                writableKinds);

        public RunTally Apply(ApplyPlan plan)
        {
            var tally = new RunTally();

            foreach (var step in plan.Steps)
            {
                switch (step.Change)
                {
                    case ApplyChangeKind.Delete:
                        // The adapter asks the element itself, not the preview, before removing it.
                        if (!_elements.TryGetValue(step.ElementUniqueId!, out var doomed) ||
                            !doomed.BelongsTo(plan.PackageId))
                        {
                            continue;
                        }

                        _elements.Remove(step.ElementUniqueId!);
                        tally.Deleted++;
                        break;

                    case ApplyChangeKind.Update:
                        _elements[step.ElementUniqueId!] = new ExistingManagedElement(
                            step.ElementUniqueId!,
                            step.Key.ToToken(),
                            step.Planned!.Signature,
                            step.Description);
                        tally.Updated++;
                        break;

                    default:
                        var uniqueId = "element-" + _nextId++;
                        _elements[uniqueId] = new ExistingManagedElement(
                            uniqueId,
                            step.Key.ToToken(),
                            step.Planned!.Signature,
                            step.Description);
                        tally.Created++;
                        break;
                }
            }

            return tally;
        }
    }

    // ---- helpers ------------------------------------------------------------------------------

    private static ApplyPlan Plan(PlanRegionMap map, ZoneDraftSet zones, FakeModel model) =>
        model.Plan(PackageId, map, zones);

    /// <summary>The Area Plan and the Drafting View together, which is what the Editor now asks for.</summary>
    private static ApplyPlan FullPlan(PlanRegionMap map, ZoneDraftSet zones, FakeModel model) =>
        model.Plan(PackageId, map, zones, ApplyPlan.AllKinds);

    /// <summary>Two rooms, both in one 區劃: four outer walls, one Area, one tag.</summary>
    private static (PlanRegionMap Map, ZoneDraftSet Zones) TwoRoomsOneZone()
    {
        var map = Solve(Rectangle(0, 0, 20, 10, "OUTER").Concat(new[] { Seg(10, 0, 10, 10, "DIVIDER") }));
        return (map, Assign(ZoneId, "A", map, FaceAt(map, 5, 5), FaceAt(map, 15, 5)));
    }

    private static ZoneDraftSet Assign(Guid zoneId, string name, PlanRegionMap map, params int[] faceIds) =>
        Succeeds(ZoneDraftSet.Empty.Add(new ZoneDraft(zoneId, name, ZoneColorPalette.At(0), faceIds)));

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
