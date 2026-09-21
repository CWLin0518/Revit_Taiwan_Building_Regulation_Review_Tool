using System;
using System.Collections.Generic;
using System.Linq;
using BuildingRegulationReview.Application.Diagnostics;
using BuildingRegulationReview.Application.Geometry;
using BuildingRegulationReview.Application.RegionEditing;
using BuildingRegulationReview.Application.ReviewPackages;
using BuildingRegulationReview.Application.WriteBack;
using BuildingRegulationReview.Domain.Common;
using BuildingRegulationReview.Domain.Geometry;
using BuildingRegulationReview.Domain.Regions;
using BuildingRegulationReview.Domain.ReviewPackages;
using Xunit;

namespace BuildingRegulationReview.Core.Tests.WriteBack;

/// <summary>
/// Phase 2's acceptance (spec 10.6) and the parts of the 驗收模型情境 (spec 16.3) Phase 2 answers,
/// driven through the whole chain in one go: extraction's output, line network repair, region
/// solving, the Editor, the preview, the plan, a model that carries the plan out, and the package
/// state and log that come back.
/// </summary>
/// <remarks>
/// The model here is a dictionary, not a Revit document, and that is the point: every property spec
/// 10.6 asks for is a property of what the tool decides, not of how Revit was driven. A plan that
/// says to create the same boundary line three times would defeat any adapter, so the criterion
/// belongs where the deciding happens. What this cannot cover — that Revit accepts the API calls at
/// all — is what the manual checklists in the task documents are for.
/// </remarks>
public class PhaseTwoAcceptanceTests
{
    private static readonly Guid PackageId = Guid.Parse("11111111-2222-3333-4444-555555555555");
    private static readonly GeometryTolerance Tolerance = GeometryTolerance.Default;
    private static readonly DateTime SolvedAt = new DateTime(2026, 9, 21, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime At = new DateTime(2026, 9, 21, 10, 0, 0, DateTimeKind.Utc);

    // ---- spec 10.6 第 1 項：同一份草稿重複套用不會累加線、Area 或 Drafting View ------------------

    [Fact]
    public void Acceptance_ApplyingTheSameDraftThreeTimesAddsNothingTheSecondOrThirdTime()
    {
        var run = Pipeline(Rectangle(0, 0, 20, 10, "OUTER"), (session, map) =>
            AssignAll(session, map));

        var first = run.Apply();
        var second = run.Apply();
        var third = run.Apply();

        Assert.Equal(run.Model.Count, first.Result.CreatedCount);
        Assert.True(first.Result.CreatedCount > 0);
        Assert.Equal(0, second.Result.CreatedCount + second.Result.UpdatedCount + second.Result.DeletedCount);
        Assert.Equal(0, third.Result.CreatedCount + third.Result.UpdatedCount + third.Result.DeletedCount);
        Assert.Equal(first.Result.CreatedCount, run.Model.Count);
    }

    [Fact]
    public void Acceptance_TheDraftingViewCopiesDoNotAccumulateEither()
    {
        var run = Pipeline(Rectangle(0, 0, 20, 10, "OUTER"), (session, map) => AssignAll(session, map));

        run.Apply();
        var lines = run.Model.CountOf(ManagedElementKind.DetailCurve);
        run.Apply();
        run.Apply();

        Assert.True(lines > 0);
        Assert.Equal(lines, run.Model.CountOf(ManagedElementKind.DetailCurve));
        Assert.Equal(1, run.Model.CountOf(ManagedElementKind.Area));
        Assert.Equal(1, run.Model.CountOf(ManagedElementKind.AreaTag));
    }

    // ---- spec 10.6 第 2 項：工具不刪除未受管理的元素 ---------------------------------------------

    [Fact]
    public void Acceptance_NeitherHandDrawnWorkNorAnotherPackagesElementsAreEverRemoved()
    {
        var run = Pipeline(Rectangle(0, 0, 20, 10, "OUTER"), (session, map) => AssignAll(session, map));
        run.Model.AddForeign("hand-drawn", string.Empty);
        run.Model.AddForeign("other-package", new ManagedElementKey(
            Guid.NewGuid(), Guid.NewGuid(), ManagedElementKind.Area, 0, 0).ToToken());

        run.Apply();
        run.ClearEveryZone();
        var cleared = run.Apply();

        Assert.True(cleared.Result.DeletedCount > 0);
        Assert.Equal(2, run.Model.Count);
        Assert.True(run.Model.Holds("hand-drawn"));
        Assert.True(run.Model.Holds("other-package"));
    }

    [Fact]
    public void Acceptance_AnElementThatLostItsOwnershipMarkIsLeftAloneRatherThanTidiedAway()
    {
        var run = Pipeline(Rectangle(0, 0, 20, 10, "OUTER"), (session, map) => AssignAll(session, map));
        run.Apply();
        var orphaned = run.Model.StripMarkFromOne(ManagedElementKind.AreaBoundaryLine);

        run.ClearEveryZone();
        run.Apply();

        Assert.True(run.Model.Holds(orphaned));
        Assert.Equal(1, run.Model.Count);
    }

    // ---- spec 10.6 第 3 項：面積差異超過容許值時禁止進入 Ready -----------------------------------

    [Fact]
    public void Acceptance_AnAreaRevitMeasuresDifferentlyFromTheDraftBlocksReadyAndSaysWhy()
    {
        var run = Pipeline(Rectangle(0, 0, 20, 10, "OUTER"), (session, map) => AssignAll(session, map));

        // The model measures what the boundary actually enclosed. A ring that did not close measures
        // less than the solver's arithmetic said, which is the case this refusal exists for.
        run.Model.MeasureAreasAs(ratio => ratio * 0.7);
        var report = run.Apply();

        Assert.False(report.IsReady);
        Assert.Equal(ReviewPackageStatus.BoundaryDraft, report.Status);
        Assert.Contains(report.Blockers, b => b.Contains("可能有邊界沒有閉合", StringComparison.Ordinal));
        Assert.Contains(report.Log.Entries, e => e.Code == ReviewErrorCode.AreaDisagrees);
    }

    [Fact]
    public void Acceptance_TheSamePackageReachesReadyOnceTheTwoMeasurementsAgree()
    {
        var run = Pipeline(Rectangle(0, 0, 20, 10, "OUTER"), (session, map) => AssignAll(session, map));
        run.Model.MeasureAreasAs(ratio => ratio * 0.7);
        var blocked = run.Apply();

        // The user fixed the boundary in Revit; the next run writes nothing new but re-measures,
        // which is what lets the package advance.
        run.Model.MeasureAreasAs(ratio => ratio);
        run.RenameZone("修正後的區劃");
        var advanced = run.Apply();

        Assert.False(blocked.IsReady);
        Assert.True(advanced.IsReady);
        Assert.Equal(ReviewPackageStatus.Ready, advanced.Status);
        Assert.Equal(blocked.Package.BoundaryRevision + 1, advanced.Package.BoundaryRevision);
    }

    [Fact]
    public void Acceptance_AnAreaThatNeverLandedInAClosedRingBlocksReadyToo()
    {
        var run = Pipeline(Rectangle(0, 0, 20, 10, "OUTER"), (session, map) => AssignAll(session, map));
        run.Model.MeasureAreasAs(_ => 0.0);

        var report = run.Apply();

        Assert.False(report.IsReady);
        Assert.Contains(report.Log.Entries, e => e.Code == ReviewErrorCode.AreaNotEnclosed);
    }

    // ---- spec 10.6 第 4 項：所有區劃均可由 Package ID 與 Zone ID 追溯 -----------------------------

    [Fact]
    public void Acceptance_EveryElementInTheModelNamesItsPackageAndItsZone()
    {
        var run = Pipeline(
            Rectangle(0, 0, 20, 10, "OUTER").Concat(new[] { Seg(10, 0, 10, 10, "DIVIDER") }),
            TwoZonesOneRoomEach);

        run.Apply();

        var zoneIds = run.ZoneIds().ToHashSet();
        Assert.Equal(2, zoneIds.Count);
        Assert.NotEmpty(run.Model.Elements);
        foreach (var element in run.Model.Elements)
        {
            Assert.True(element.HasKey);
            Assert.Equal(PackageId, element.Key.PackageId);
            Assert.Contains(element.Key.ZoneId, zoneIds);
        }
    }

    [Fact]
    public void Acceptance_ElementsOfOneZoneCanBeFoundAgainByThatZonesIdAlone()
    {
        var run = Pipeline(
            Rectangle(0, 0, 20, 10, "OUTER").Concat(new[] { Seg(10, 0, 10, 10, "DIVIDER") }),
            TwoZonesOneRoomEach);
        run.Apply();

        var first = run.ZoneIds().First();
        var mine = run.Model.Elements.Where(e => e.Key.ZoneId == first).ToList();

        Assert.NotEmpty(mine);
        Assert.Single(mine, e => e.Key.Kind == ManagedElementKind.Area);
        Assert.Single(mine, e => e.Key.Kind == ManagedElementKind.AreaTag);
    }

    // ---- spec 16.3 情境 1：正常矩形單區劃 -------------------------------------------------------

    [Fact]
    public void Scenario01_APlainRectangleBecomesOneZoneWithOneAreaAndItsTag()
    {
        var run = Pipeline(Rectangle(0, 0, 20, 10, "OUTER"), (session, map) => AssignAll(session, map));

        var report = run.Apply();

        Assert.Equal(4, run.Model.CountOf(ManagedElementKind.AreaBoundaryLine));
        Assert.Equal(1, run.Model.CountOf(ManagedElementKind.Area));
        Assert.Equal(1, run.Model.CountOf(ManagedElementKind.AreaTag));
        Assert.True(report.IsReady);
        Assert.Equal(PlanUnits.SquareFeetToSquareMeters(200.0), run.DraftAreaSquareMeters(), 3);
    }

    // ---- spec 16.3 情境 2：柱造成短缺口，但在容差内可修復 ---------------------------------------

    [Fact]
    public void Scenario02_AGapSmallerThanTheToleranceIsRepairedAndTheRoomStillCloses()
    {
        // 0.05 ft is wider than the 10 mm snap tolerance and narrower than the 50 mm extension one,
        // which is the gap a column leaves where it interrupts a wall.
        var walls = new[]
        {
            Seg(0, 0, 9.975, 0, "S-1"),
            Seg(10.025, 0, 20, 0, "S-2"),
            Seg(20, 0, 20, 10, "E"),
            Seg(20, 10, 0, 10, "N"),
            Seg(0, 10, 0, 0, "W")
        };

        var run = Pipeline(walls, (session, map) => AssignAll(session, map));
        var report = run.Apply();

        Assert.Equal(1, run.Model.CountOf(ManagedElementKind.Area));
        Assert.True(report.IsReady);

        // Spec 10.2: the repair is kept with its kind and its distance, so the reviewer can see what
        // the tool closed and how far it reached to do it.
        var repair = Assert.Single(run.Network.RepairsOfKind(NetworkRepairKind.GapExtended));
        Assert.InRange(repair.DistanceMillimeters, 0.0, Tolerance.GapExtensionFeet * 304.8);
        Assert.Empty(run.Network.Errors);
    }

    // ---- spec 16.3 情境 3：缺口超過容差，必須人工修正 -------------------------------------------

    [Fact]
    public void Scenario03_AGapWiderThanTheToleranceLeavesNoClosedRegionAndSaysSoInsteadOfGuessing()
    {
        var walls = new[]
        {
            Seg(0, 0, 9.0, 0, "S-1"),
            Seg(11.0, 0, 20, 0, "S-2"),
            Seg(20, 0, 20, 10, "E"),
            Seg(20, 10, 0, 10, "N"),
            Seg(0, 10, 0, 0, "W")
        };

        var network = Repair(walls);
        var solved = new RegionSolver().Solve(network, SolvedAt);

        // The tool never invents the missing two feet. Solving fails outright rather than handing
        // back an empty map that a caller might mistake for "there is nothing here", and the repair
        // pass has already named the ends that do not meet.
        Assert.True(solved.IsFailure);
        Assert.Equal("geometry.regions.no-closed-loop", solved.Error.Code);
        Assert.Contains(network.Issues, issue =>
            issue.Kind == NetworkIssueKind.DanglingEnd || issue.Kind == NetworkIssueKind.GapBeyondTolerance);
    }

    // ---- spec 16.3 情境 4：一個區劃含孔洞或多個 Region -------------------------------------------

    [Fact]
    public void Scenario04_TwoRoomsMergedIntoOneZoneLoseTheWallBetweenThemAndKeepOneArea()
    {
        var run = Pipeline(
            Rectangle(0, 0, 20, 10, "OUTER").Concat(new[] { Seg(10, 0, 10, 10, "DIVIDER") }),
            (session, map) => AssignAll(session, map));

        run.Apply();

        // Six outer segments, because the divider splits the north and south walls in two; the
        // divider itself is interior to the merged zone and is not fenced.
        Assert.Equal(6, run.Model.CountOf(ManagedElementKind.AreaBoundaryLine));
        Assert.Equal(1, run.Model.CountOf(ManagedElementKind.Area));
        Assert.Equal(PlanUnits.SquareFeetToSquareMeters(200.0), run.DraftAreaSquareMeters(), 3);
    }

    [Fact]
    public void Scenario04_TwoRoomsThatDoNotTouchNeedOneAreaEachAndSayThatBeforeTheyAreMerged()
    {
        var walls = Rectangle(0, 0, 10, 10, "LEFT").Concat(Rectangle(20, 0, 10, 10, "RIGHT"));
        var run = Pipeline(walls, (session, map) =>
        {
            var zone = Succeeds(session.CreateZone("分離的區劃"));
            session.SetActiveZone(zone.Id);
            Succeeds(session.AddFaces(map.Faces.Select(f => f.Id), confirmDisjoint: true));
        });

        var preview = run.Preview();
        run.Apply();

        Assert.Contains(preview.Warnings, w => w.Contains("分成 2 塊不相連", StringComparison.Ordinal));
        Assert.Equal(2, run.Model.CountOf(ManagedElementKind.Area));
        Assert.Equal(2, run.Model.CountOf(ManagedElementKind.AreaTag));
        Assert.Equal(8, run.Model.CountOf(ManagedElementKind.AreaBoundaryLine));
    }

    // ---- spec 16.3 情境 5：區劃面積剛好等於法規上限 ---------------------------------------------

    [Fact]
    public void Scenario05_AnAreaSittingExactlyOnALimitIsNotNudgedByTheSignatureRounding()
    {
        // The rule that reads the limit is Phase 3. What Phase 2 owes it is a number that does not
        // drift: the same drafts re-solved have to produce the same area and the same signatures, or
        // a 區劃 sitting on the limit would cross it on a re-run for no reason the user can see.
        var walls = Rectangle(0, 0, 32.808398950131235, 32.808398950131235, "OUTER"); // 10 m × 10 m
        var run = Pipeline(walls, (session, map) => AssignAll(session, map));

        var first = run.DraftAreaSquareMeters();
        run.Apply();
        var second = Pipeline(walls, (session, map) => AssignAll(session, map)).DraftAreaSquareMeters();
        var rerun = run.Apply();

        Assert.Equal(100.0, first, 6);
        Assert.Equal(first, second, 9);
        Assert.Equal(0, rerun.Result.UpdatedCount);
    }

    // ---- spec 16.3 情境 9：使用者改名或移動視埠後仍可正確更新 -----------------------------------

    [Fact]
    public void Scenario09_PanningAndZoomingTheEditorChangesNothingAboutWhatWouldBeWritten()
    {
        var run = Pipeline(Rectangle(0, 0, 20, 10, "OUTER"), (session, map) => AssignAll(session, map));
        run.Apply();

        run.Session.PanByPixels(140, -85);
        run.Session.ZoomByWheel(new ScreenPoint(300, 200), 4);
        var afterMoving = run.Apply();

        Assert.True(run.Preview().IsEmpty);
        Assert.Equal(0, afterMoving.Result.CreatedCount + afterMoving.Result.UpdatedCount + afterMoving.Result.DeletedCount);
    }

    [Fact]
    public void Scenario09_RenamingAZoneUpdatesTheAreaAndItsTagAndLeavesTheBoundaryAlone()
    {
        var run = Pipeline(Rectangle(0, 0, 20, 10, "OUTER"), (session, map) => AssignAll(session, map));
        run.Apply();
        var before = run.Model.Count;

        run.RenameZone("改過的名字");
        var renamed = run.Apply();

        // The walls did not move, so only the two elements that carry the name are rewritten, and
        // they are found again by their key rather than by what they were called.
        Assert.Equal(0, renamed.Result.CreatedCount);
        Assert.Equal(2, renamed.Result.UpdatedCount);
        Assert.Equal(before, run.Model.Count);
    }

    [Fact]
    public void Scenario09_APackageWhoseUserRenamedTheOutputsStillFindsThemByIdentity()
    {
        // Spec 10.5 item 5: 用唯一識別防止名稱變更造成失聯. The Drafting View travels on the package
        // as a UniqueId, so a view the user renamed is still this package's view.
        var package = new ReviewPackage(
            PackageId, "floor-plan", "level-1", "area-scheme", "area-plan",
            draftingViewUniqueId: "drafting-1", boundaryRevision: 1, status: ReviewPackageStatus.Ready);

        var verdict = ReviewStaleness.Evaluate(
            package,
            new ReviewModelObservation(
                true, true, true, true,
                draftingViewFound: true,
                areaPlanLevelUniqueId: "level-1",
                areaPlanAreaSchemeUniqueId: "area-scheme",
                managedElementCount: 8),
            At);

        Assert.False(verdict.IsStale);
        Assert.Empty(verdict.Reasons);
    }

    // ---- spec 16.3 情境 10：重跑三次後元素數量不增加 ---------------------------------------------

    [Fact]
    public void Scenario10_ThreeRunsOverTwoZonesLeaveTheModelExactlyTheSizeTheFirstOneMadeIt()
    {
        var run = Pipeline(
            Rectangle(0, 0, 20, 10, "OUTER").Concat(new[] { Seg(10, 0, 10, 10, "DIVIDER") }),
            TwoZonesOneRoomEach);

        run.Apply();
        var size = run.Model.Count;
        run.Apply();
        run.Apply();

        Assert.Equal(size, run.Model.Count);
        Assert.Equal(2, run.Model.CountOf(ManagedElementKind.Area));
    }

    [Fact]
    public void Scenario10_ThePackageDoesNotKeepAdvancingItsRevisionOnRunsThatChangedNothing()
    {
        var run = Pipeline(Rectangle(0, 0, 20, 10, "OUTER"), (session, map) => AssignAll(session, map));

        var first = run.Apply();
        var second = run.Apply();
        var third = run.Apply();

        Assert.Equal(1, first.Package.BoundaryRevision);
        Assert.Equal(1, second.Package.BoundaryRevision);
        Assert.Equal(1, third.Package.BoundaryRevision);
        Assert.False(second.PackageChanged);
    }

    // ---- the chain the tests above drive --------------------------------------------------------

    /// <summary>
    /// Extraction's output, repaired, solved, edited, previewed, planned, carried out, and judged —
    /// the whole Phase 2 chain behind one object, so a test reads as what a user does.
    /// </summary>
    private sealed class Pipeline_
    {
        private ReviewPackage _package;

        public Pipeline_(RegionEditorSession session, FakeModel model, PlanLineNetwork network)
        {
            Session = session;
            Model = model;
            Network = network;
            _package = new ReviewPackage(PackageId, "floor-plan", "level-1", "area-scheme", "area-plan");
        }

        public RegionEditorSession Session { get; }
        public FakeModel Model { get; }

        /// <summary>The repaired line network, with what spec 10.2 says every repair must keep.</summary>
        public PlanLineNetwork Network { get; }

        public ApplyPreview Preview() => Session.BuildPreview(Model.Elements);

        /// <summary>One press of 套用: preview, plan, write, and work out what the package now is.</summary>
        public ReviewRunReport Apply()
        {
            var plan = ApplyPlan.Build(Preview(), ApplyPlan.AllKinds);
            var report = ReviewRunReport.For(_package, Model.Carry(plan, Session), At);
            _package = report.Package;
            if (report.Result.IsComplete) Session.MarkApplied();
            return report;
        }

        public IEnumerable<Guid> ZoneIds() => Session.Zones.Zones.Select(z => z.Id);

        public double DraftAreaSquareMeters() => Session.Zones.Zones
            .SelectMany(z => z.FaceIds)
            .Select(id => Session.Map.Face(id).NetAreaSquareFeet)
            .Sum() * PlanUnits.SquareFeetToSquareMeters(1.0);

        public void RenameZone(string name) =>
            Succeeds(Session.RenameZone(Session.Zones.Zones.First().Id, name));

        public void ClearEveryZone()
        {
            foreach (var zone in Session.Zones.Zones.ToList()) Succeeds(Session.DeleteZone(zone.Id));
        }
    }

    /// <summary>
    /// A stand-in for the Area Plan and the Drafting View: a bag of elements, each with the
    /// ownership token and signature the tool wrote on it. It carries a plan out literally,
    /// including the ownership re-check the Revit adapter does before every deletion, and it
    /// measures the Areas it holds the way the adapter reads them back from Revit.
    /// </summary>
    private sealed class FakeModel
    {
        private readonly Dictionary<string, ExistingManagedElement> _elements =
            new Dictionary<string, ExistingManagedElement>(StringComparer.Ordinal);
        private Func<double, double> _measure = draft => draft;
        private int _nextId;

        public int Count => _elements.Count;

        public IReadOnlyList<ExistingManagedElement> Elements => _elements.Values.ToList();

        public bool Holds(string uniqueId) => _elements.ContainsKey(uniqueId);

        public int CountOf(ManagedElementKind kind) => _elements.Values.Count(e => e.HasKey && e.Key.Kind == kind);

        public void AddForeign(string uniqueId, string token) => _elements[uniqueId] =
            new ExistingManagedElement(uniqueId, string.IsNullOrEmpty(token) ? "人工繪製" : token, string.Empty);

        /// <summary>What Revit reports for an Area, as a function of what the draft computed.</summary>
        public void MeasureAreasAs(Func<double, double> measure) => _measure = measure;

        /// <summary>Takes the mark off one element, the way a user who rebuilt it by hand would.</summary>
        public string StripMarkFromOne(ManagedElementKind kind)
        {
            var victim = _elements.Values.First(e => e.HasKey && e.Key.Kind == kind);
            _elements[victim.ElementUniqueId] =
                new ExistingManagedElement(victim.ElementUniqueId, "人工繪製", string.Empty);
            return victim.ElementUniqueId;
        }

        public ApplyResult Carry(ApplyPlan plan, RegionEditorSession session)
        {
            var log = new ApplyResult.Builder(plan);
            if (plan.IsEmpty) return log.Complete();

            var written = new List<PlannedElement>();

            foreach (var step in plan.Steps)
            {
                switch (step.Change)
                {
                    case ApplyChangeKind.Delete:
                        // The adapter asks the element itself, not the preview, before removing it.
                        if (!_elements.TryGetValue(step.ElementUniqueId!, out var doomed) ||
                            !doomed.BelongsTo(plan.PackageId))
                        {
                            log.Skipped(step, "這個元素沒有本套件的擁有權標記，不會刪除。");
                            continue;
                        }

                        _elements.Remove(step.ElementUniqueId!);
                        log.Deleted(step);
                        break;

                    case ApplyChangeKind.Update:
                        Store(step.ElementUniqueId!, step);
                        if (step.Kind == ManagedElementKind.Area) written.Add(step.Planned!);
                        log.Updated(step, step.ElementUniqueId);
                        break;

                    default:
                        var uniqueId = "element-" + _nextId++;
                        Store(uniqueId, step);
                        if (step.Kind == ManagedElementKind.Area) written.Add(step.Planned!);
                        log.Created(step, uniqueId);
                        break;
                }
            }

            // Spec 10.6: every Area that reached the model is measured again and compared, exactly
            // where the Revit adapter does it — after the stages have run and the model regenerated.
            foreach (var area in written)
            {
                log.Area(AreaAgreement.Compare(
                    area.Key,
                    area.ZoneName,
                    area.NetAreaSquareMeters,
                    _measure(area.NetAreaSquareMeters)));
            }

            return log.Complete();
        }

        private void Store(string uniqueId, ApplyStep step) => _elements[uniqueId] = new ExistingManagedElement(
            uniqueId, step.Key.ToToken(), step.Planned!.Signature, step.Description);
    }

    // ---- helpers ------------------------------------------------------------------------------

    private static Pipeline_ Pipeline(
        IEnumerable<Segment2D> walls,
        Action<RegionEditorSession, PlanRegionMap> edit)
    {
        var segments = walls.ToList();
        var network = Repair(segments);
        var map = Succeeds(new RegionSolver().Solve(network, SolvedAt));
        var session = new RegionEditorSession(map, new ScreenSize(1024, 720), network.Issues);
        edit(session, map);
        return new Pipeline_(session, new FakeModel(), network);
    }

    /// <summary>The whole plan in one 區劃, which is what a single-compartment floor looks like.</summary>
    private static void AssignAll(RegionEditorSession session, PlanRegionMap map)
    {
        var zone = Succeeds(session.CreateZone("A"));
        session.SetActiveZone(zone.Id);
        Succeeds(session.AddFaces(map.Faces.Select(f => f.Id), confirmDisjoint: true));
    }

    /// <summary>One 區劃 per room, which is what two compartments either side of a wall look like.</summary>
    private static void TwoZonesOneRoomEach(RegionEditorSession session, PlanRegionMap map)
    {
        var faces = map.Faces.OrderBy(f => f.RepresentativePoint.X).ToList();
        var names = new[] { "A", "B" };

        for (var i = 0; i < Math.Min(2, faces.Count); i++)
        {
            var zone = Succeeds(session.CreateZone(names[i]));
            session.SetActiveZone(zone.Id);
            Succeeds(session.AddFaces(new[] { faces[i].Id }));
        }
    }

    private static PlanLineNetwork Repair(IEnumerable<Segment2D> segments) => Succeeds(
        new LineNetworkRepairer().Repair(
            new PlanGeometrySnapshot(PackageId, "host-doc", "level-1", segments, Tolerance),
            SolvedAt));

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

    private static void Succeeds(Result result) =>
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.ToString() : string.Empty);
}
