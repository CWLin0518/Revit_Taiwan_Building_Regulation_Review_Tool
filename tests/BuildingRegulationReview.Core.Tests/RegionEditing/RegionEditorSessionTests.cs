using System;
using System.Collections.Generic;
using System.Linq;
using BuildingRegulationReview.Application.Geometry;
using BuildingRegulationReview.Application.RegionEditing;
using BuildingRegulationReview.Application.WriteBack;
using BuildingRegulationReview.Domain.Common;
using BuildingRegulationReview.Domain.Geometry;
using BuildingRegulationReview.Domain.Regions;
using Xunit;

namespace BuildingRegulationReview.Core.Tests.RegionEditing;

public class RegionEditorSessionTests
{
    private static readonly Guid PackageId = Guid.Parse("11111111-2222-3333-4444-555555555555");
    private static readonly GeometryTolerance Tolerance = GeometryTolerance.Default;
    private static readonly DateTime SolvedAt = new DateTime(2026, 9, 21, 0, 0, 0, DateTimeKind.Utc);
    private static readonly ScreenSize Canvas = new ScreenSize(800, 600);

    // ---- display --------------------------------------------------------------------------

    [Fact]
    public void OpensWithTheWholePlanInView()
    {
        var session = TwoRooms();

        Assert.True(session.Viewport.VisibleExtent.Contains(new Point2D(0, 0)));
        Assert.True(session.Viewport.VisibleExtent.Contains(new Point2D(20, 10)));
        Assert.Equal(0.0, session.ModelExtent.Minimum.X, 9);
        Assert.Equal(20.0, session.ModelExtent.Maximum.X, 9);
    }

    [Fact]
    public void DrawsEveryFaceWithItsBoundaryAndLabelAnchor()
    {
        var view = TwoRooms().BuildView();

        Assert.Equal(2, view.Faces.Count);
        foreach (var face in view.Faces)
        {
            Assert.Equal(4, face.OuterRing.Count);
            Assert.Empty(face.HoleRings);
            Assert.Null(face.Fill);
            Assert.False(face.IsAssigned);
            Assert.True(view.Viewport.VisibleExtent.Contains(view.Viewport.ToModel(face.LabelAnchor)));
        }

        Assert.Equal(2, view.UnassignedFaceCount);
        Assert.Equal(0, view.AssignedFaceCount);
    }

    [Fact]
    public void DrawsAHoleAsItsOwnRing()
    {
        var view = RoomWithIsland().BuildView();

        var outer = view.Faces.Single(f => f.HoleCount == 1);
        Assert.Equal(2, outer.Rings.Count);
        Assert.Equal(4, outer.HoleRings.Single().Count);
    }

    [Fact]
    public void FillsAFaceWithTheColourOfTheZoneThatOwnsIt()
    {
        var session = TwoRooms();
        var zone = Succeeds(session.CreateZone("A", ZoneColor.FromHex("#123456")));
        Succeeds(session.AddFaceAt(At(session, 5, 5)));

        var view = session.BuildView();

        var filled = view.Faces.Single(f => f.IsAssigned);
        Assert.Equal(ZoneColor.FromHex("#123456"), filled.Fill);
        Assert.Equal(zone.Id, filled.ZoneId);
        Assert.True(filled.IsInActiveZone);
        Assert.Equal(1, view.AssignedFaceCount);
    }

    [Fact]
    public void LabelsAZoneWithItsNameAndDraftArea()
    {
        var session = TwoRooms();
        Succeeds(session.CreateZone("防火區劃 A"));
        Succeeds(session.AddFaceAt(At(session, 5, 5)));

        var zone = session.BuildView().Zones.Single();

        Assert.Equal(1, zone.FaceCount);
        Assert.Equal(PlanUnits.SquareFeetToSquareMeters(100), zone.NetAreaSquareMeters, 6);
        Assert.Equal("9.29 m²", zone.AreaText);
        Assert.Equal("防火區劃 A\n9.29 m²", zone.Label);
        Assert.True(zone.IsContiguous);
        Assert.NotNull(zone.LabelAnchor);
    }

    [Fact]
    public void SaysInTheLabelWhenAZoneFallsIntoPartsThatDoNotTouch()
    {
        var session = TwoDetachedRooms();
        Succeeds(session.CreateZone("A"));
        Succeeds(session.AddFaceAt(At(session, 5, 5)));
        Succeeds(session.AddFaceAt(At(session, 55, 5), confirmDisjoint: true));

        var zone = session.BuildView().Zones.Single();

        Assert.Equal(2, zone.ContiguousPartCount);
        Assert.False(zone.IsContiguous);
        Assert.Contains("2 塊不相連", zone.Label);
    }

    [Fact]
    public void CountsTheHolesOfAZoneInItsLabel()
    {
        var session = RoomWithIsland();
        Succeeds(session.CreateZone("A"));
        Succeeds(session.AddFaceAt(At(session, 1, 1)));

        var zone = session.BuildView().Zones.Single();

        Assert.Equal(1, zone.HoleCount);
        Assert.Contains("孔洞 1", zone.Label);
    }

    [Fact]
    public void ReportsAnEmptyZoneWithoutAPlaceToPutItsLabel()
    {
        var session = TwoRooms();
        Succeeds(session.CreateZone("A"));

        var zone = session.BuildView().Zones.Single();

        Assert.True(zone.IsEmpty);
        Assert.Null(zone.LabelAnchor);
        Assert.Equal(0, zone.ContiguousPartCount);
    }

    [Fact]
    public void ShowsRepairAndSolveProblemsAsOneListWithErrorsFirst()
    {
        var network = RepairedNetwork(WithDanglingWall());
        var map = Succeeds(new RegionSolver().Solve(network, SolvedAt));
        var session = new RegionEditorSession(map, Canvas, network.Issues);

        Assert.Contains(session.Issues, i => i.Origin == EditorIssueOrigin.Region && i.Kind == "DanglingEdgePruned");
        Assert.Equal(
            session.Issues.OrderByDescending(i => i.Severity).Select(i => i.Message),
            session.Issues.Select(i => i.Message));
    }

    [Fact]
    public void PutsAnIssueWhereTheReviewerHasToLook()
    {
        var network = RepairedNetwork(WithDanglingWall());
        var session = new RegionEditorSession(Succeeds(new RegionSolver().Solve(network, SolvedAt)), Canvas, network.Issues);

        var view = session.BuildView();

        Assert.Equal(view.Issues.Count, view.IssueAnchors.Count);
        Assert.Equal(view.Viewport.ToScreen(view.Issues[0].Location).X, view.IssueAnchors[0].X, 9);
    }

    [Fact]
    public void SummarizesWhatIsDoneAndWhatIsLeft()
    {
        var session = TwoRooms();
        Succeeds(session.CreateZone("A"));
        Succeeds(session.AddFaceAt(At(session, 5, 5)));

        Assert.Equal("2 個範圍，已指派 1 個（9.29 m²），未指派 1 個；區劃 1 個；問題 0 則（錯誤 0 則）", session.BuildView().Summary);
    }

    // ---- zoom and pan ---------------------------------------------------------------------

    [Fact]
    public void ZoomsAroundTheCursorAndBackToTheWholePlan()
    {
        var session = TwoRooms();
        var fitted = session.Viewport.PixelsPerFoot;

        session.ZoomByWheel(new ScreenPoint(100, 100), 3);
        Assert.True(session.Viewport.PixelsPerFoot > fitted);

        session.ZoomToFit();
        Assert.Equal(fitted, session.Viewport.PixelsPerFoot, 9);
    }

    [Fact]
    public void ZoomsToOneZone()
    {
        var session = TwoRooms();
        var zone = Succeeds(session.CreateZone("A"));
        Succeeds(session.AddFaceAt(At(session, 5, 5)));

        Assert.True(session.ZoomToZone(zone.Id).IsSuccess);

        var visible = session.Viewport.VisibleExtent;
        Assert.True(visible.Contains(new Point2D(0, 0)));
        Assert.True(visible.Contains(new Point2D(10, 10)));
        Assert.Equal(5.0, session.Viewport.ModelCenter.X, 9);
    }

    [Fact]
    public void ZoomingToAnEmptyZoneShowsTheWholePlan()
    {
        var session = TwoRooms();
        var zone = Succeeds(session.CreateZone("A"));
        session.ZoomByWheel(new ScreenPoint(10, 10), 5);

        Assert.True(session.ZoomToZone(zone.Id).IsSuccess);

        Assert.Equal(10.0, session.Viewport.ModelCenter.X, 9);
    }

    [Fact]
    public void PanningMovesTheModelUnderTheCanvas()
    {
        var session = TwoRooms();
        var before = session.Viewport.ModelCenter.X;

        session.PanByPixels(40, 0);

        Assert.True(session.Viewport.ModelCenter.X < before);
    }

    // ---- selection ------------------------------------------------------------------------

    [Fact]
    public void SelectsTheFaceUnderTheCursor()
    {
        var session = TwoRooms();

        var selected = session.SelectAt(At(session, 15, 5));

        Assert.Equal(new[] { FaceAt(session, 15, 5) }, selected);
    }

    [Fact]
    public void ClickingOutsideEveryFaceClearsTheSelection()
    {
        var session = TwoRooms();
        session.SelectAt(At(session, 5, 5));

        session.SelectAt(At(session, -5, -5));

        Assert.False(session.HasSelection);
    }

    [Fact]
    public void RubberBandTakesEveryFaceItTouches()
    {
        var session = TwoRooms();

        var selected = session.SelectInBox(At(session, -1, -1), At(session, 21, 11));

        Assert.Equal(new[] { 0, 1 }, selected);
    }

    [Fact]
    public void ASmallBandInsideOneRoomStillPicksThatRoom()
    {
        var session = TwoRooms();

        var selected = session.SelectInBox(At(session, 4, 4), At(session, 6, 6));

        Assert.Equal(new[] { FaceAt(session, 5, 5) }, selected);
    }

    [Fact]
    public void BandsCanAddToAndToggleTheSelection()
    {
        var session = TwoRooms();
        var left = FaceAt(session, 5, 5);
        var right = FaceAt(session, 15, 5);

        session.SelectInBox(At(session, 4, 4), At(session, 6, 6));
        session.SelectInBox(At(session, 14, 4), At(session, 16, 6), SelectionMode.Add);
        Assert.Equal(new[] { left, right }.OrderBy(x => x), session.SelectedFaceIds);

        session.SelectInBox(At(session, 4, 4), At(session, 6, 6), SelectionMode.Toggle);
        Assert.Equal(new[] { right }, session.SelectedFaceIds);
    }

    [Fact]
    public void SelectsEveryFaceOfAZoneFromTheZoneList()
    {
        var session = TwoRooms();
        var zone = Succeeds(session.CreateZone("A"));
        Succeeds(session.AddFaces(new[] { 0, 1 }));
        session.ClearSelection();

        Assert.True(session.SelectZoneFaces(zone.Id).IsSuccess);
        Assert.Equal(new[] { 0, 1 }, session.SelectedFaceIds);
    }

    // ---- zones ----------------------------------------------------------------------------

    [Fact]
    public void NamesAndColoursANewZoneAndMakesItActive()
    {
        var session = TwoRooms();

        var first = Succeeds(session.CreateZone());
        var second = Succeeds(session.CreateZone());

        Assert.Equal("區劃 1", first.Name);
        Assert.Equal("區劃 2", second.Name);
        Assert.NotEqual(first.Color, second.Color);
        Assert.Equal(second.Id, session.ActiveZoneId);
    }

    [Fact]
    public void SkipsAnAutomaticNameTheUserAlreadyTook()
    {
        var session = TwoRooms();
        Succeeds(session.CreateZone("區劃 2"));

        Assert.Equal("區劃 3", Succeeds(session.CreateZone()).Name);
    }

    [Fact]
    public void RefusesASecondZoneWithTheSameName()
    {
        var session = TwoRooms();
        Succeeds(session.CreateZone("A"));

        var result = session.CreateZone("A");

        Assert.True(result.IsFailure);
        Assert.Equal("regions.zone.duplicateName", result.Error.Code);
        Assert.Single(session.Zones.Zones);
    }

    [Fact]
    public void RenamesAndRecoloursAZone()
    {
        var session = TwoRooms();
        var zone = Succeeds(session.CreateZone("A"));

        Assert.True(session.RenameZone(zone.Id, "廚房").IsSuccess);
        Assert.True(session.RecolorZone(zone.Id, ZoneColor.FromHex("#ABCDEF")).IsSuccess);

        var updated = session.Zones.Zone(zone.Id)!;
        Assert.Equal("廚房", updated.Name);
        Assert.Equal("#ABCDEF", updated.Color.ToHex());
    }

    [Fact]
    public void SetsAZoneUseAndTakesItBackWithUndo()
    {
        var session = TwoRooms();
        var zone = Succeeds(session.CreateZone("A"));

        Assert.True(session.SetZoneUse(zone.Id, "管道間").IsSuccess);
        Assert.Equal("管道間", session.Zones.Zone(zone.Id)!.Use);

        Assert.True(session.Undo().IsSuccess);
        Assert.Null(session.Zones.Zone(zone.Id)!.Use);
    }

    [Fact]
    public void ClearingAZoneUseIsItsOwnUndoableStepAndNotTheSameAsNotChangingIt()
    {
        var session = TwoRooms();
        var zone = Succeeds(session.CreateZone("A"));
        Assert.True(session.SetZoneUse(zone.Id, "挑空").IsSuccess);

        Assert.True(session.SetZoneUse(zone.Id, string.Empty).IsSuccess);

        // Empty is 一般區劃, which the write-back clears; null would have been 不變更.
        Assert.Equal(string.Empty, session.Zones.Zone(zone.Id)!.Use);
        Assert.True(session.Undo().IsSuccess);
        Assert.Equal("挑空", session.Zones.Zone(zone.Id)!.Use);
    }

    [Fact]
    public void SettingTheUseAZoneAlreadyCarriesRecordsNoHistoryStep()
    {
        var session = TwoRooms();
        var zone = Succeeds(session.CreateZone("A"));
        Assert.True(session.SetZoneUse(zone.Id, "樓梯間").IsSuccess);

        Assert.True(session.SetZoneUse(zone.Id, " 樓梯間 ").IsSuccess);

        // One Undo, not two: the second call changed nothing, so it is not a step to walk back over.
        Assert.True(session.Undo().IsSuccess);
        Assert.Null(session.Zones.Zone(zone.Id)!.Use);
    }

    [Fact]
    public void DeletingAZoneReleasesItsFacesAndKeepsTheListPosition()
    {
        var session = TwoRooms();
        var first = Succeeds(session.CreateZone("A"));
        var second = Succeeds(session.CreateZone("B"));
        Assert.True(session.SetActiveZone(first.Id).IsSuccess);
        Succeeds(session.AddFaceAt(At(session, 5, 5)));

        var deleted = Succeeds(session.DeleteZone(first.Id));

        Assert.Equal("A", deleted.Name);
        Assert.Single(session.Zones.Zones);
        Assert.Equal(second.Id, session.ActiveZoneId);
        Assert.DoesNotContain(session.BuildView().Faces, f => f.IsAssigned);
    }

    [Fact]
    public void DeletingTheLastZoneLeavesNoneActive()
    {
        var session = TwoRooms();
        var zone = Succeeds(session.CreateZone("A"));

        Succeeds(session.DeleteZone(zone.Id));

        Assert.Null(session.ActiveZoneId);
        Assert.Null(session.ActiveZone);
    }

    [Fact]
    public void ReportsAZoneThatIsNoLongerThere()
    {
        var session = TwoRooms();

        Assert.Equal("regions.zone.unknownZone", session.SetActiveZone(Guid.NewGuid()).Error.Code);
        Assert.Equal("regions.zone.unknownZone", session.DeleteZone(Guid.NewGuid()).Error.Code);
        Assert.Equal("regions.zone.unknownZone", session.ZoomToZone(Guid.NewGuid()).Error.Code);
    }

    // ---- membership -----------------------------------------------------------------------

    [Fact]
    public void LeftClickPutsTheFaceIntoTheActiveZoneAndSelectsIt()
    {
        var session = TwoRooms();
        var zone = Succeeds(session.CreateZone("A"));

        var change = Succeeds(session.AddFaceAt(At(session, 5, 5)));

        var faceId = FaceAt(session, 5, 5);
        Assert.Equal(new[] { faceId }, change.FaceIds);
        Assert.Equal(zone.Id, change.ZoneId);
        Assert.Empty(change.MovedFrom);
        Assert.Equal("已將 1 個範圍加入「A」。", change.Message);
        Assert.Equal(new[] { faceId }, session.SelectedFaceIds);
    }

    [Fact]
    public void RefusesToAddBeforeThereIsAZoneToAddTo()
    {
        var session = TwoRooms();

        var result = session.AddFaceAt(At(session, 5, 5));

        Assert.True(result.IsFailure);
        Assert.Equal("regions.editor.noActiveZone", result.Error.Code);
    }

    [Fact]
    public void SaysThereIsNothingToAddWhereNothingIsEnclosed()
    {
        var session = TwoRooms();
        Succeeds(session.CreateZone("A"));

        Assert.Equal("regions.editor.noFaceHere", session.AddFaceAt(At(session, -5, -5)).Error.Code);
        Assert.Equal("regions.editor.noFaceHere", session.RemoveFaceAt(At(session, -5, -5)).Error.Code);
    }

    [Fact]
    public void AddingTheSameFaceTwiceChangesNothing()
    {
        var session = TwoRooms();
        Succeeds(session.CreateZone("A"));
        Succeeds(session.AddFaceAt(At(session, 5, 5)));

        var result = session.AddFaceAt(At(session, 5, 5));

        Assert.True(result.IsFailure);
        Assert.Equal("regions.zone.faceAlreadyInZone", result.Error.Code);
        Assert.Single(session.Zones.Zone(session.ActiveZoneId!.Value)!.FaceIds);
    }

    [Fact]
    public void AFaceAddedToASecondZoneLeavesTheFirstAndTheMoveIsReported()
    {
        var session = TwoRooms();
        var first = Succeeds(session.CreateZone("A"));
        Succeeds(session.AddFaceAt(At(session, 5, 5)));
        var second = Succeeds(session.CreateZone("B"));

        var change = Succeeds(session.AddFaceAt(At(session, 5, 5)));

        Assert.Equal(second.Id, change.ZoneId);
        Assert.Equal(first.Id, change.MovedFrom.Single().FromZoneId);
        Assert.Contains("原屬於「A」", change.Message);
        Assert.Empty(session.Zones.Zone(first.Id)!.FaceIds);
        Assert.Single(session.Zones.Zone(second.Id)!.FaceIds);
    }

    [Fact]
    public void RightClickTakesTheFaceOutOfItsZone()
    {
        var session = TwoRooms();
        Succeeds(session.CreateZone("A"));
        Succeeds(session.AddFaceAt(At(session, 5, 5)));

        var change = Succeeds(session.RemoveFaceAt(At(session, 5, 5)));

        Assert.Null(change.ZoneId);
        Assert.Equal("已將 1 個範圍移出「A」。", change.Message);
        Assert.False(session.Zones.IsAssigned(FaceAt(session, 5, 5)));
    }

    [Fact]
    public void RightClickOnAFaceInNoZoneSaysSo()
    {
        var session = TwoRooms();
        Succeeds(session.CreateZone("A"));

        Assert.Equal("regions.zone.faceNotAssigned", session.RemoveFaceAt(At(session, 5, 5)).Error.Code);
    }

    [Fact]
    public void AddsAndRemovesAWholeSelection()
    {
        var session = TwoRooms();
        Succeeds(session.CreateZone("A"));
        session.SelectAll();

        var added = Succeeds(session.AddSelectionToActiveZone());
        Assert.Equal(new[] { 0, 1 }, added.FaceIds);
        Assert.Equal(2, session.Zones.AssignedFaceCount);

        var removed = Succeeds(session.RemoveSelectionFromZones());
        Assert.Equal(new[] { 0, 1 }, removed.FaceIds);
        Assert.Equal(0, session.Zones.AssignedFaceCount);
    }

    [Fact]
    public void AddingWithNothingSelectedSaysSo()
    {
        var session = TwoRooms();
        Succeeds(session.CreateZone("A"));

        Assert.Equal("regions.editor.nothingChosen", session.AddSelectionToActiveZone().Error.Code);
        Assert.Equal("regions.editor.nothingChosen", session.RemoveSelectionFromZones().Error.Code);
    }

    [Fact]
    public void RefusesAFaceThatIsNotInThisSolve()
    {
        var session = TwoRooms();
        Succeeds(session.CreateZone("A"));

        var result = session.AddFaces(new[] { 99 });

        Assert.True(result.IsFailure);
        Assert.Equal("regions.editor.unknownFace", result.Error.Code);
    }

    // ---- source elements ------------------------------------------------------------------

    [Fact]
    public void TracesAFaceBackToTheElementsItsBoundaryCameFrom()
    {
        var session = TwoRooms();

        var sources = session.SourcesAt(At(session, 5, 5));

        Assert.NotEmpty(sources);
        Assert.All(sources, s => Assert.Equal(GeometrySourceKind.WallCenterline, s.Kind));
        Assert.Contains(sources, s => s.ElementUniqueId == "DIVIDER");
    }

    [Fact]
    public void ListsTheElementsOfASelectionOnceEach()
    {
        var session = TwoRooms();
        session.SelectAll();

        var sources = session.SourcesOfSelection();

        Assert.Equal(sources.Count, sources.Distinct().Count());
        Assert.Contains(sources, s => s.ElementUniqueId == "DIVIDER");
    }

    [Fact]
    public void ListsTheElementsOfAZone()
    {
        var session = TwoRooms();
        var zone = Succeeds(session.CreateZone("A"));
        Succeeds(session.AddFaceAt(At(session, 5, 5)));

        var sources = Succeeds(session.SourcesOfZone(zone.Id));

        Assert.Equal(session.SourcesOfFace(FaceAt(session, 5, 5)).Count, sources.Count);
    }

    [Fact]
    public void RefusesAMapWithNothingToEdit()
    {
        Assert.Throws<ArgumentNullException>(() => new RegionEditorSession(null!, Canvas));
    }

    // ---- undo and redo (P2-T06) -------------------------------------------------------------

    [Fact]
    public void UndoTakesBackTheLastEditAndRedoPutsItBack()
    {
        var session = TwoRooms();
        Succeeds(session.CreateZone("A"));
        Succeeds(session.AddFaceAt(At(session, 5, 5)));

        Assert.True(session.CanUndo);
        Succeeds(session.Undo());

        Assert.Empty(session.Zones.Zones.Single().FaceIds);
        Assert.True(session.CanRedo);

        Succeeds(session.Redo());
        Assert.Single(session.Zones.Zones.Single().FaceIds);
    }

    [Fact]
    public void UndoWalksBackThroughEveryEditToTheEmptyDraft()
    {
        var session = TwoRooms();
        Succeeds(session.CreateZone("A"));
        Succeeds(session.AddFaceAt(At(session, 5, 5)));
        Succeeds(session.AddFaceAt(At(session, 15, 5)));

        while (session.CanUndo) Succeeds(session.Undo());

        Assert.True(session.Zones.IsEmpty);
        Assert.Null(session.ActiveZoneId);
        Assert.False(session.HasUnappliedChanges);
    }

    [Fact]
    public void UndoNamesTheEditItTakesBack()
    {
        var session = TwoRooms();
        Succeeds(session.CreateZone("A"));

        Assert.Equal("建立區劃「A」", session.UndoLabel);
        Assert.Equal("已復原「建立區劃「A」」。", Succeeds(session.Undo()));
        Assert.Equal("建立區劃「A」", session.RedoLabel);
    }

    [Fact]
    public void ANewEditDropsWhatWasUndone()
    {
        var session = TwoRooms();
        Succeeds(session.CreateZone("A"));
        Succeeds(session.AddFaceAt(At(session, 5, 5)));
        Succeeds(session.Undo());

        Assert.True(session.CanRedo);
        Succeeds(session.AddFaceAt(At(session, 15, 5)));

        Assert.False(session.CanRedo);
    }

    [Fact]
    public void RefusesToUndoOrRedoWhenThereIsNothingToStepThrough()
    {
        var session = TwoRooms();

        Assert.False(session.CanUndo);
        Assert.False(session.CanRedo);
        Assert.Equal("regions.editor.nothingToUndo", session.Undo().Error.Code);
        Assert.Equal("regions.editor.nothingToRedo", session.Redo().Error.Code);
    }

    [Fact]
    public void AFailedEditIsNotRecordedInTheHistory()
    {
        var session = TwoRooms();
        var zone = Succeeds(session.CreateZone("A"));
        Succeeds(session.CreateZone("B"));

        Assert.True(session.RenameZone(zone.Id, "B").IsFailure);
        Assert.Equal("建立區劃「B」", session.UndoLabel);

        // The refused edit also left the drafts alone.
        Assert.Equal("A", session.Zones.Zone(zone.Id)!.Name);
    }

    [Fact]
    public void UndoBringsBackADeletedZoneAsTheActiveOne()
    {
        var session = TwoRooms();
        var zone = Succeeds(session.CreateZone("A"));
        Succeeds(session.AddFaceAt(At(session, 5, 5)));
        Succeeds(session.DeleteZone(zone.Id));

        Succeeds(session.Undo());

        Assert.True(session.Zones.Contains(zone.Id));
        Assert.Equal(zone.Id, session.ActiveZoneId);
        Assert.Single(session.Zones.Zone(zone.Id)!.FaceIds);
    }

    [Fact]
    public void ZoomingAndSelectingAreNotEdits()
    {
        var session = TwoRooms();
        session.ZoomByWheel(At(session, 5, 5), 3);
        session.SelectAll();
        session.PanByPixels(20, 20);
        Assert.True(session.SetActiveZone(null).IsSuccess);

        Assert.False(session.CanUndo);
    }

    [Fact]
    public void UndoKeepsTheViewWhereTheUserLeftIt()
    {
        var session = TwoRooms();
        Succeeds(session.CreateZone("A"));
        session.ZoomByWheel(At(session, 5, 5), 4);
        var scale = session.Viewport.PixelsPerFoot;

        Succeeds(session.Undo());

        Assert.Equal(scale, session.Viewport.PixelsPerFoot, 9);
    }

    // ---- the explicit confirmation a disjoint merge needs (spec 10.3) -------------------------

    [Fact]
    public void RefusesToMergeBlocksThatDoNotTouchUntilTheUserConfirms()
    {
        var session = TwoDetachedRooms();
        Succeeds(session.CreateZone("A"));
        Succeeds(session.AddFaceAt(At(session, 5, 5)));

        var blocked = session.AddFaceAt(At(session, 55, 5));

        Assert.True(blocked.IsFailure);
        Assert.Equal(RegionEditorSession.DisjointNotConfirmedCode, blocked.Error.Code);
        Assert.Contains("2 塊互不相連", blocked.Error.Message);
        Assert.Single(session.Zones.Zones.Single().FaceIds);
        Assert.False(session.CanUndo is false && session.Zones.Count == 0);
    }

    [Fact]
    public void MergesBlocksThatDoNotTouchOnceTheUserConfirms()
    {
        var session = TwoDetachedRooms();
        Succeeds(session.CreateZone("A"));
        Succeeds(session.AddFaceAt(At(session, 5, 5)));

        var merged = Succeeds(session.AddFaceAt(At(session, 55, 5), confirmDisjoint: true));

        Assert.True(session.Zones.Zones.Single().AllowsDisjointParts);
        Assert.Contains("2 塊不相連", merged.Warning);
        Assert.Contains("（已確認）", session.BuildView().Zones.Single().Label);
    }

    [Fact]
    public void DoesNotAskAgainWhileTheZoneStaysInPieces()
    {
        var session = ThreeDetachedRooms();
        Succeeds(session.CreateZone("A"));
        Succeeds(session.AddFaceAt(At(session, 5, 5)));
        Succeeds(session.AddFaceAt(At(session, 55, 5), confirmDisjoint: true));

        Succeeds(session.AddFaceAt(At(session, 105, 5)));

        Assert.Equal(3, session.BuildView().Zones.Single().ContiguousPartCount);
    }

    [Fact]
    public void AsksAgainOnceTheZoneIsWholeAgain()
    {
        var session = TwoDetachedRooms();
        Succeeds(session.CreateZone("A"));
        Succeeds(session.AddFaceAt(At(session, 5, 5)));
        Succeeds(session.AddFaceAt(At(session, 55, 5), confirmDisjoint: true));

        Succeeds(session.RemoveFaceAt(At(session, 55, 5)));
        Assert.False(session.Zones.Zones.Single().AllowsDisjointParts);

        Assert.Equal(
            RegionEditorSession.DisjointNotConfirmedCode,
            session.AddFaceAt(At(session, 55, 5)).Error.Code);
    }

    [Fact]
    public void AddingATouchingRoomNeverAsks()
    {
        var session = ThreeRooms();
        Succeeds(session.CreateZone("A"));
        Succeeds(session.AddFaceAt(At(session, 5, 5)));

        Succeeds(session.AddFaceAt(At(session, 15, 5)));

        Assert.True(session.BuildView().Zones.Single().IsContiguous);
    }

    [Fact]
    public void SaysSoWhenTakingARoomOutSplitsAZone()
    {
        var session = ThreeRooms();
        Succeeds(session.CreateZone("A"));
        session.SelectAll();
        Succeeds(session.AddSelectionToActiveZone());

        var change = Succeeds(session.RemoveFaceAt(At(session, 15, 5)));

        Assert.Contains("「A」2 塊不相連", change.Warning);
        Assert.Contains("2 塊不相連", change.FullMessage);
    }

    // ---- unapplied changes (spec 10.3, 離開前提示) --------------------------------------------

    [Fact]
    public void HasNothingToApplyBeforeAnyEdit()
    {
        var session = TwoRooms();

        Assert.False(session.HasUnappliedChanges);
        Assert.Null(session.UnappliedChangesPrompt);
    }

    [Fact]
    public void AsksBeforeClosingOnceSomethingWasDrafted()
    {
        var session = TwoRooms();
        Succeeds(session.CreateZone("A"));
        Succeeds(session.AddFaceAt(At(session, 5, 5)));

        Assert.True(session.HasUnappliedChanges);
        Assert.Contains("尚未套用", session.UnappliedChangesPrompt);
        Assert.Contains("草稿尚未套用到模型", session.BuildView().DraftState);
    }

    [Fact]
    public void UndoingBackToTheAppliedStateLeavesNothingToApply()
    {
        var session = TwoRooms();
        Succeeds(session.CreateZone("A"));
        Succeeds(session.AddFaceAt(At(session, 5, 5)));
        session.MarkApplied();

        Succeeds(session.AddFaceAt(At(session, 15, 5)));
        Assert.True(session.HasUnappliedChanges);

        Succeeds(session.Undo());

        Assert.False(session.HasUnappliedChanges);
        Assert.Equal("草稿與模型一致", session.BuildView().DraftState);
    }

    [Fact]
    public void RenamingAZoneCountsAsSomethingToApply()
    {
        var session = TwoRooms();
        var zone = Succeeds(session.CreateZone("A"));
        session.MarkApplied();

        Assert.True(session.RenameZone(zone.Id, "B").IsSuccess);

        Assert.True(session.HasUnappliedChanges);
    }

    // ---- apply preview (spec 10.4) ------------------------------------------------------------

    [Fact]
    public void PreviewsEveryPlannedElementAsAnAdditionAgainstAnEmptyModel()
    {
        var session = TwoRooms();
        Succeeds(session.CreateZone("A"));
        Succeeds(session.AddFaceAt(At(session, 5, 5)));

        var preview = session.BuildPreview();

        Assert.Empty(preview.Updated);
        Assert.Empty(preview.Deleted);
        Assert.Equal(1, preview.CountOf(ManagedElementKind.Area, ApplyChangeKind.Add));
        Assert.Equal(1, preview.CountOf(ManagedElementKind.AreaTag, ApplyChangeKind.Add));
        Assert.Equal(4, preview.CountOf(ManagedElementKind.AreaBoundaryLine, ApplyChangeKind.Add));
        Assert.Contains("未指派", string.Join("\n", preview.Warnings));
    }

    // ---- helpers --------------------------------------------------------------------------

    private static RegionEditorSession TwoRooms() => Session(
        Rectangle(0, 0, 20, 10, "OUTER").Concat(new[] { Seg(10, 0, 10, 10, "DIVIDER") }));

    private static RegionEditorSession ThreeRooms() => Session(
        Rectangle(0, 0, 30, 10, "OUTER").Concat(new[]
        {
            Seg(10, 0, 10, 10, "DIVIDER-1"),
            Seg(20, 0, 20, 10, "DIVIDER-2")
        }));

    private static RegionEditorSession ThreeDetachedRooms() => Session(
        Rectangle(0, 0, 10, 10, "LEFT")
            .Concat(Rectangle(50, 0, 10, 10, "MIDDLE"))
            .Concat(Rectangle(100, 0, 10, 10, "RIGHT")));

    private static RegionEditorSession TwoDetachedRooms() => Session(
        Rectangle(0, 0, 10, 10, "LEFT").Concat(Rectangle(50, 0, 10, 10, "RIGHT")));

    private static RegionEditorSession RoomWithIsland() => Session(
        Rectangle(0, 0, 20, 20, "OUTER").Concat(Rectangle(5, 5, 5, 5, "ISLAND")));

    private static IEnumerable<Segment2D> WithDanglingWall() =>
        Rectangle(0, 0, 10, 10, "OUTER").Concat(new[] { Seg(5, 10, 5, 14, "STUB") });

    private static RegionEditorSession Session(IEnumerable<Segment2D> segments)
    {
        var network = RepairedNetwork(segments);
        return new RegionEditorSession(Succeeds(new RegionSolver().Solve(network, SolvedAt)), Canvas, network.Issues);
    }

    private static PlanLineNetwork RepairedNetwork(IEnumerable<Segment2D> segments)
    {
        var snapshot = new PlanGeometrySnapshot(PackageId, "host-doc", "level-1", segments, Tolerance);
        return Succeeds(new LineNetworkRepairer().Repair(snapshot, SolvedAt));
    }

    private static ScreenPoint At(RegionEditorSession session, double x, double y) =>
        session.Viewport.ToScreen(new Point2D(x, y));

    private static int FaceAt(RegionEditorSession session, double x, double y) =>
        session.Map.FaceAt(new Point2D(x, y))!.Id;

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
