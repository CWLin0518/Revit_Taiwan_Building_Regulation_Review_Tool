using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using BuildingRegulationReview.Application.WriteBack;
using BuildingRegulationReview.Domain.Common;
using BuildingRegulationReview.Domain.Geometry;
using BuildingRegulationReview.Domain.Regions;

namespace BuildingRegulationReview.Application.RegionEditing;

/// <summary>How a new pick combines with what is already selected.</summary>
public enum SelectionMode
{
    /// <summary>The pick becomes the selection.</summary>
    Replace,

    /// <summary>The pick joins the selection.</summary>
    Add,

    /// <summary>Faces in the pick that were selected are dropped, the rest are added.</summary>
    Toggle
}

/// <summary>A face that changed hands, and the zone it came from.</summary>
public sealed class FaceMove
{
    public FaceMove(int faceId, Guid fromZoneId, string fromZoneName)
    {
        if (faceId < 0) throw new ArgumentOutOfRangeException(nameof(faceId));
        if (fromZoneId == Guid.Empty) throw new ArgumentException("Zone ID cannot be empty.", nameof(fromZoneId));

        FaceId = faceId;
        FromZoneId = fromZoneId;
        FromZoneName = fromZoneName ?? throw new ArgumentNullException(nameof(fromZoneName));
    }

    public int FaceId { get; }
    public Guid FromZoneId { get; }
    public string FromZoneName { get; }
}

/// <summary>
/// What one membership edit did. The moves are reported rather than silently applied: spec 10.3 lets
/// a face belong to only one 區劃, so adding it somewhere else takes it away from where it was, and
/// the user has to be able to see that happen.
/// </summary>
public sealed class ZoneMembershipChange
{
    public ZoneMembershipChange(
        Guid? zoneId,
        IEnumerable<int> faceIds,
        IEnumerable<FaceMove>? moves,
        string message,
        string? warning = null)
    {
        if (string.IsNullOrWhiteSpace(message)) throw new ArgumentException("A change needs a message.", nameof(message));

        ZoneId = zoneId;
        FaceIds = new ReadOnlyCollection<int>((faceIds ?? throw new ArgumentNullException(nameof(faceIds))).Distinct().OrderBy(x => x).ToList());
        MovedFrom = new ReadOnlyCollection<FaceMove>((moves ?? Array.Empty<FaceMove>()).ToList());
        Message = message.Trim();
        Warning = string.IsNullOrWhiteSpace(warning) ? null : warning!.Trim();
    }

    /// <summary>The zone the faces are in now, or null when they were taken out of every zone.</summary>
    public Guid? ZoneId { get; }

    public IReadOnlyList<int> FaceIds { get; }
    public IReadOnlyList<FaceMove> MovedFrom { get; }
    public string Message { get; }

    /// <summary>Something the user should know but that did not stop the edit, such as a split zone.</summary>
    public string? Warning { get; }

    /// <summary>Message plus warning, which is what the status line shows.</summary>
    public string FullMessage => Warning is null ? Message : Message + Warning;

    public override string ToString() => FullMessage;
}

/// <summary>
/// The Region Editor itself (spec 10.3, P2-T05): it holds the solved map, the 區劃 drafts and what is
/// selected, turns canvas gestures into edits, and builds the picture to draw. It knows nothing about
/// WPF or Revit, so every interaction rule here is testable without a window.
/// </summary>
/// <remarks>
/// The session is deliberately the only mutable object: the map is fixed for its lifetime and the
/// zone set is immutable, so Undo/Redo keeps past <see cref="Zones"/> values without snapshotting
/// anything else. Nothing here writes to Revit; write-back is P2-T07.
/// <para>
/// Zoom and pan are not edits and are not part of that history. Neither is selection, which follows
/// the picture rather than the draft.
/// </para>
/// </remarks>
public sealed class RegionEditorSession
{
    /// <summary>A drag shorter than this is a click, not a rubber band.</summary>
    public const double ClickThresholdPixels = 4.0;

    /// <summary>One wheel notch. Twelve notches roughly double or halve the scale.</summary>
    public const double WheelZoomFactor = 1.1;

    /// <summary>
    /// Returned when an edit would leave a 區劃 in pieces that do not touch. Spec 10.3 forbids that
    /// merge unless the user confirms it, so the UI matches this code, asks, and calls the same
    /// operation again with the confirmation.
    /// </summary>
    public const string DisjointNotConfirmedCode = "regions.zone.disjointNotConfirmed";

    private readonly SortedSet<int> _selected = new SortedSet<int>();
    private readonly List<EditorIssue> _issues;
    private readonly EditorHistory _history = new EditorHistory();

    private string _appliedSignature;

    /// <param name="initialZones">
    /// The zones the model already holds for this package, restored by <see cref="WrittenZoneRestorer"/>.
    /// They open as applied: they are what the last write-back left, so there is nothing to lose yet.
    /// </param>
    public RegionEditorSession(
        PlanRegionMap map,
        ScreenSize canvasSize,
        IEnumerable<NetworkIssue>? networkIssues = null,
        ZoneDraftSet? initialZones = null)
    {
        Map = map ?? throw new ArgumentNullException(nameof(map));

        ModelExtent = map.Extent ?? MeasureExtent(map);
        Viewport = EditorViewport.FitTo(ModelExtent, canvasSize);
        Zones = initialZones ?? ZoneDraftSet.Empty;
        if (Zones.AssignedFaceIds.Any(id => id >= map.Faces.Count))
            throw new ArgumentException("The initial zones refer to faces this map does not have.", nameof(initialZones));
        _appliedSignature = Zones.Signature();

        _issues = (networkIssues ?? Array.Empty<NetworkIssue>())
            .Where(x => x is not null)
            .Select(EditorIssue.From)
            .Concat(map.Issues.Select(EditorIssue.From))
            .OrderByDescending(i => i.Severity)
            .ToList();
    }

    public PlanRegionMap Map { get; }
    public ZoneDraftSet Zones { get; private set; }
    public EditorViewport Viewport { get; private set; }

    /// <summary>The plan the viewport was fitted to: the solved extent, or the geometry it measured.</summary>
    public PlanExtent2D ModelExtent { get; }

    public IReadOnlyList<EditorIssue> Issues => _issues;

    /// <summary>Ascending face IDs, so a selection reads the same however it was picked.</summary>
    public IReadOnlyList<int> SelectedFaceIds => _selected.ToList();

    public bool HasSelection => _selected.Count > 0;
    public Guid? ActiveZoneId { get; private set; }
    public ZoneDraft? ActiveZone => ActiveZoneId.HasValue ? Zones.Zone(ActiveZoneId.Value) : null;

    // ---- history and applied state --------------------------------------------------------

    public bool CanUndo => _history.CanUndo;
    public bool CanRedo => _history.CanRedo;

    /// <summary>The edit Undo would take back, for the button's tooltip; null when there is none.</summary>
    public string? UndoLabel => _history.UndoLabel;

    public string? RedoLabel => _history.RedoLabel;

    /// <summary>
    /// Whether the drafts differ from what was last written to the model (spec 10.3, 離開前提示未
    /// 套用變更). It compares content, not history, so undoing back to the applied state correctly
    /// reports nothing to apply.
    /// </summary>
    public bool HasUnappliedChanges => !string.Equals(Zones.Signature(), _appliedSignature, StringComparison.Ordinal);

    /// <summary>The question to ask before closing, or null when there is nothing unapplied.</summary>
    public string? UnappliedChangesPrompt
    {
        get
        {
            if (!HasUnappliedChanges) return null;

            var assigned = Zones.AssignedFaceCount;
            return string.Format(
                CultureInfo.InvariantCulture,
                "有 {0} 個區劃草稿（共 {1} 個範圍）尚未套用到模型，關閉後會遺失。仍要關閉嗎？",
                Zones.Count,
                assigned);
        }
    }

    /// <summary>
    /// Steps back one edit. The view, the selection and the zoom stay where they are: only the
    /// drafts and which zone is active go back.
    /// </summary>
    public Result<string> Undo()
    {
        var entry = _history.Undo(Snapshot());
        if (entry is null)
        {
            return Result.Failure<string>(new Error("regions.editor.nothingToUndo", "沒有可以復原的動作。"));
        }

        Restore(entry.State);
        return Result.Success(string.Format(CultureInfo.InvariantCulture, "已復原「{0}」。", entry.Label));
    }

    public Result<string> Redo()
    {
        var entry = _history.Redo(Snapshot());
        if (entry is null)
        {
            return Result.Failure<string>(new Error("regions.editor.nothingToRedo", "沒有可以重做的動作。"));
        }

        Restore(entry.State);
        return Result.Success(string.Format(CultureInfo.InvariantCulture, "已重做「{0}」。", entry.Label));
    }

    /// <summary>
    /// Records that the current drafts now match the model. P2-T07 calls this once write-back has
    /// committed; until then every Editor reports its drafts as unapplied.
    /// </summary>
    public void MarkApplied() => _appliedSignature = Zones.Signature();

    /// <summary>
    /// What applying the drafts would add, update and delete (spec 10.4). <paramref name="existing"/>
    /// is what the model already holds for this package; with nothing passed every planned element
    /// reads as an addition.
    /// </summary>
    public ApplyPreview BuildPreview(IEnumerable<ExistingManagedElement>? existing = null) =>
        ApplyPreview.Build(Map.PackageId, Map, Zones, existing);

    // ---- view -----------------------------------------------------------------------------

    public void Resize(ScreenSize size) => Viewport = Viewport.Resize(size);

    public void PanByPixels(double deltaX, double deltaY) => Viewport = Viewport.PanByPixels(deltaX, deltaY);

    public void ZoomAt(ScreenPoint anchor, double factor) => Viewport = Viewport.ZoomAt(anchor, factor);

    /// <param name="notches">Wheel notches: positive zooms in, negative out.</param>
    public void ZoomByWheel(ScreenPoint anchor, int notches) =>
        Viewport = Viewport.ZoomAt(anchor, Math.Pow(WheelZoomFactor, notches));

    public void ZoomToFit() => Viewport = EditorViewport.FitTo(ModelExtent, Viewport.Size);

    /// <summary>Frames one zone, or the whole plan when the zone holds no face.</summary>
    public Result ZoomToZone(Guid zoneId)
    {
        var zone = Zones.Zone(zoneId);
        if (zone is null) return Result.Failure(UnknownZone(zoneId));

        var faces = zone.FaceIds.Select(id => Map.Face(id)).ToList();
        if (faces.Count == 0)
        {
            ZoomToFit();
            return Result.Success();
        }

        Viewport = EditorViewport.FitTo(ExtentOf(faces), Viewport.Size);
        return Result.Success();
    }

    public RegionEditorView BuildView()
    {
        var viewport = Viewport;
        var faces = Map.Faces.Select(face =>
        {
            var zone = Zones.ZoneOf(face.Id);
            return new FaceVisual(
                face.Id,
                face.AllBoundaries.Select(b => (IReadOnlyList<ScreenPoint>)b.Vertices.Select(viewport.ToScreen).ToList()),
                viewport.ToScreen(face.RepresentativePoint),
                face.NetAreaSquareMeters,
                face.Holes.Count,
                zone?.Id,
                zone?.Color,
                _selected.Contains(face.Id),
                zone is not null && zone.Id == ActiveZoneId);
        });

        return new RegionEditorView(
            viewport,
            faces,
            BuildZoneVisuals(viewport),
            _issues,
            viewport.ToScreen,
            ActiveZoneId,
            _selected,
            CanUndo,
            CanRedo,
            HasUnappliedChanges);
    }

    // ---- selection ------------------------------------------------------------------------

    public PlanFace? FaceAt(ScreenPoint point) => Map.FaceAt(Viewport.ToModel(point));

    public void ClearSelection() => _selected.Clear();

    /// <summary>Picks the face under the cursor. Returns the faces selected afterwards.</summary>
    public IReadOnlyList<int> SelectAt(ScreenPoint point, SelectionMode mode = SelectionMode.Replace)
    {
        var face = FaceAt(point);
        Apply(face is null ? Array.Empty<int>() : new[] { face.Id }, mode);
        return SelectedFaceIds;
    }

    /// <summary>
    /// Rubber-band selection (spec 10.3, 框選). A face is caught when the band holds its
    /// representative point or any boundary vertex, or when the band lies inside the face — so a
    /// small band dragged inside one large room still picks that room.
    /// </summary>
    public IReadOnlyList<int> SelectInBox(ScreenPoint first, ScreenPoint second, SelectionMode mode = SelectionMode.Replace)
    {
        var box = new ScreenRect(first, second);
        var center = Viewport.ToModel(box.Center);

        var hits = Map.Faces.Where(face =>
                box.Contains(Viewport.ToScreen(face.RepresentativePoint)) ||
                face.AllBoundaries.SelectMany(b => b.Vertices).Any(v => box.Contains(Viewport.ToScreen(v))) ||
                face.Contains(center))
            .Select(face => face.Id)
            .ToList();

        Apply(hits, mode);
        return SelectedFaceIds;
    }

    public void SelectAll() => Apply(Map.Faces.Select(f => f.Id).ToList(), SelectionMode.Replace);

    /// <summary>Selects every face of a zone, which is how clicking the zone list highlights it.</summary>
    public Result SelectZoneFaces(Guid zoneId, SelectionMode mode = SelectionMode.Replace)
    {
        var zone = Zones.Zone(zoneId);
        if (zone is null) return Result.Failure(UnknownZone(zoneId));

        Apply(zone.FaceIds, mode);
        return Result.Success();
    }

    // ---- zones ----------------------------------------------------------------------------

    /// <summary>Creates a 區劃 draft and makes it the active one. A blank name gets the next 區劃 n.</summary>
    public Result<ZoneDraft> CreateZone(string? name = null, ZoneColor? color = null)
    {
        var zoneName = string.IsNullOrWhiteSpace(name) ? NextZoneName() : name!;
        ZoneDraft zone;
        try
        {
            zone = new ZoneDraft(Guid.NewGuid(), zoneName, color ?? ZoneColorPalette.NextUnused(Zones.UsedColors));
        }
        catch (ArgumentException exception)
        {
            return Result.Failure<ZoneDraft>(new Error(
                "regions.zone.invalidName",
                $"區劃名稱不可空白，長度也不可超過 {ZoneDraft.MaximumNameLength} 個字。",
                exception.Message));
        }

        var added = Zones.Add(zone);
        if (added.IsFailure) return Result.Failure<ZoneDraft>(added.Error);

        var before = Snapshot();
        Zones = added.Value;
        ActiveZoneId = zone.Id;
        _history.Record(before, string.Format(CultureInfo.InvariantCulture, "建立區劃「{0}」", zone.Name));
        return Result.Success(zone);
    }

    public Result SetActiveZone(Guid? zoneId)
    {
        if (zoneId is null)
        {
            ActiveZoneId = null;
            return Result.Success();
        }

        if (!Zones.Contains(zoneId.Value)) return Result.Failure(UnknownZone(zoneId.Value));

        ActiveZoneId = zoneId;
        return Result.Success();
    }

    public Result RenameZone(Guid zoneId, string name)
    {
        var previous = Zones.Zone(zoneId);
        var renamed = Zones.Rename(zoneId, name);
        if (renamed.IsFailure) return Result.Failure(renamed.Error);

        var before = Snapshot();
        Zones = renamed.Value;
        _history.Record(before, string.Format(
            CultureInfo.InvariantCulture,
            "將「{0}」改名為「{1}」",
            previous!.Name,
            Zones.Zone(zoneId)!.Name));
        return Result.Success();
    }

    public Result RecolorZone(Guid zoneId, ZoneColor color)
    {
        var recolored = Zones.Recolor(zoneId, color);
        if (recolored.IsFailure) return Result.Failure(recolored.Error);

        var before = Snapshot();
        Zones = recolored.Value;
        _history.Record(before, string.Format(
            CultureInfo.InvariantCulture,
            "變更「{0}」的顏色",
            Zones.Zone(zoneId)!.Name));
        return Result.Success();
    }

    /// <summary>
    /// Sets a zone's 防火檢討_區劃用途, which the next 套用 writes onto its Areas. Blank clears it, and
    /// setting the use a zone already carries is not an edit, so it records no history step.
    /// </summary>
    public Result SetZoneUse(Guid zoneId, string? use)
    {
        var previous = Zones.Zone(zoneId);
        if (previous is null) return Result.Failure(UnknownZone(zoneId));

        var updated = Zones.SetUse(zoneId, use);
        if (updated.IsFailure) return Result.Failure(updated.Error);
        if (ReferenceEquals(updated.Value.Zone(zoneId), previous)) return Result.Success();

        var before = Snapshot();
        Zones = updated.Value;
        var now = Zones.Zone(zoneId)!.Use;
        var label = now switch
        {
            null => "不變更「{0}」的區劃用途",
            "" => "將「{0}」設為一般區劃（清除區劃用途）",
            _ => "將「{0}」的區劃用途設為「{1}」"
        };

        _history.Record(before, string.Format(CultureInfo.InvariantCulture, label, previous.Name, now));
        return Result.Success();
    }

    /// <summary>
    /// Deletes a 區劃 draft. Its faces go back to being unassigned; nothing in the model is touched,
    /// because the draft has not been written yet.
    /// </summary>
    public Result<ZoneDraft> DeleteZone(Guid zoneId)
    {
        var zone = Zones.Zone(zoneId);
        if (zone is null) return Result.Failure<ZoneDraft>(UnknownZone(zoneId));

        var index = IndexOf(zoneId);
        var removed = Zones.Remove(zoneId);
        if (removed.IsFailure) return Result.Failure<ZoneDraft>(removed.Error);

        var before = Snapshot();
        Zones = removed.Value;
        _history.Record(before, string.Format(CultureInfo.InvariantCulture, "刪除區劃「{0}」", zone.Name));
        if (ActiveZoneId == zoneId)
        {
            // Keep the list position rather than the identity: after deleting one zone the neighbour
            // that slid into its place is the one the user is looking at.
            ActiveZoneId = Zones.Count == 0
                ? (Guid?)null
                : Zones.Zones[Math.Min(index, Zones.Count - 1)].Id;
        }

        return Result.Success(zone);
    }

    // ---- membership -----------------------------------------------------------------------

    /// <summary>Left click: puts the face under the cursor into the active 區劃 and selects it.</summary>
    /// <param name="confirmDisjoint">
    /// The user's answer to the question spec 10.3 requires before a 區劃 may fall into parts that do
    /// not touch. It is passed on a second call, after the first came back with
    /// <see cref="DisjointNotConfirmedCode"/>.
    /// </param>
    public Result<ZoneMembershipChange> AddFaceAt(ScreenPoint point, bool confirmDisjoint = false)
    {
        var face = FaceAt(point);
        if (face is null) return Result.Failure<ZoneMembershipChange>(NoFaceHere());

        Apply(new[] { face.Id }, SelectionMode.Replace);
        return AddFaces(new[] { face.Id }, confirmDisjoint);
    }

    /// <summary>Right click: takes the face under the cursor out of whichever 區劃 holds it.</summary>
    public Result<ZoneMembershipChange> RemoveFaceAt(ScreenPoint point)
    {
        var face = FaceAt(point);
        if (face is null) return Result.Failure<ZoneMembershipChange>(NoFaceHere());

        return RemoveFaces(new[] { face.Id });
    }

    public Result<ZoneMembershipChange> AddSelectionToActiveZone(bool confirmDisjoint = false) =>
        AddFaces(_selected.ToList(), confirmDisjoint);

    public Result<ZoneMembershipChange> RemoveSelectionFromZones() => RemoveFaces(_selected.ToList());

    /// <summary>Puts faces into the active 區劃, moving any that another zone already held.</summary>
    /// <param name="confirmDisjoint">See <see cref="AddFaceAt"/>.</param>
    public Result<ZoneMembershipChange> AddFaces(IEnumerable<int> faceIds, bool confirmDisjoint = false)
    {
        if (faceIds is null) throw new ArgumentNullException(nameof(faceIds));

        var zone = ActiveZone;
        if (zone is null)
        {
            return Result.Failure<ZoneMembershipChange>(new Error(
                "regions.editor.noActiveZone", "請先建立或選取一個區劃，再把範圍加進去。"));
        }

        var requested = Normalize(faceIds, out var invalid);
        if (invalid is not null) return Result.Failure<ZoneMembershipChange>(invalid);
        if (requested.Count == 0) return Result.Failure<ZoneMembershipChange>(NothingChosen());

        var block = CheckDisjoint(zone, requested, confirmDisjoint);
        if (block is not null) return Result.Failure<ZoneMembershipChange>(block);

        var before = Snapshot();
        var added = new List<int>();
        var moves = new List<FaceMove>();
        foreach (var faceId in requested)
        {
            var assignment = Zones.Assign(faceId, zone.Id);
            if (assignment.IsFailure) continue; // Already in this zone; adding it again changes nothing.

            var previousId = assignment.Value.PreviousZoneId;
            if (previousId.HasValue)
            {
                var previous = Zones.Zone(previousId.Value);
                if (previous is not null) moves.Add(new FaceMove(faceId, previous.Id, previous.Name));
            }

            Zones = assignment.Value.Zones;
            added.Add(faceId);
        }

        if (added.Count == 0)
        {
            Restore(before);
            return Result.Failure<ZoneMembershipChange>(new Error(
                "regions.zone.faceAlreadyInZone",
                $"選取的範圍已經屬於「{zone.Name}」。",
                "faceIds=" + string.Join(",", requested)));
        }

        SettleDisjointConfirmation(zone.Id, confirmDisjoint);
        foreach (var source in moves.Select(m => m.FromZoneId).Distinct()) SettleDisjointConfirmation(source, false);

        var message = string.Format(CultureInfo.InvariantCulture, "已將 {0} 個範圍加入「{1}」。", added.Count, zone.Name);
        if (moves.Count > 0)
        {
            message += string.Format(
                CultureInfo.InvariantCulture,
                "其中 {0} 個原屬於{1}，已改為此區劃。",
                moves.Count,
                string.Join("、", moves.Select(m => "「" + m.FromZoneName + "」").Distinct()));
        }

        _history.Record(before, string.Format(
            CultureInfo.InvariantCulture, "將 {0} 個範圍加入「{1}」", added.Count, zone.Name));

        return Result.Success(new ZoneMembershipChange(
            zone.Id, added, moves, message, SplitWarning(moves.Select(m => m.FromZoneId).Concat(new[] { zone.Id }))));
    }

    /// <summary>Takes faces out of the 區劃 that holds them, leaving them unassigned.</summary>
    public Result<ZoneMembershipChange> RemoveFaces(IEnumerable<int> faceIds)
    {
        if (faceIds is null) throw new ArgumentNullException(nameof(faceIds));

        var requested = Normalize(faceIds, out var invalid);
        if (invalid is not null) return Result.Failure<ZoneMembershipChange>(invalid);
        if (requested.Count == 0) return Result.Failure<ZoneMembershipChange>(NothingChosen());

        var before = Snapshot();
        var removed = new List<int>();
        var moves = new List<FaceMove>();
        foreach (var faceId in requested)
        {
            var owner = Zones.ZoneOf(faceId);
            var unassigned = Zones.Unassign(faceId);
            if (unassigned.IsFailure) continue; // Belongs to nothing; removing it changes nothing.

            Zones = unassigned.Value.Zones;
            removed.Add(faceId);
            if (owner is not null) moves.Add(new FaceMove(faceId, owner.Id, owner.Name));
        }

        if (removed.Count == 0)
        {
            Restore(before);
            return Result.Failure<ZoneMembershipChange>(new Error(
                "regions.zone.faceNotAssigned",
                "選取的範圍不屬於任何區劃。",
                "faceIds=" + string.Join(",", requested)));
        }

        foreach (var source in moves.Select(m => m.FromZoneId).Distinct()) SettleDisjointConfirmation(source, false);

        var names = moves.Select(m => "「" + m.FromZoneName + "」").Distinct().ToList();
        var message = string.Format(
            CultureInfo.InvariantCulture,
            "已將 {0} 個範圍移出{1}。",
            removed.Count,
            names.Count > 0 ? string.Join("、", names) : "區劃");

        _history.Record(before, string.Format(
            CultureInfo.InvariantCulture, "將 {0} 個範圍移出區劃", removed.Count));

        return Result.Success(new ZoneMembershipChange(
            null, removed, moves, message, SplitWarning(moves.Select(m => m.FromZoneId))));
    }

    // ---- source elements ------------------------------------------------------------------

    /// <summary>The model elements the boundary of one face came from (spec 10.3, 顯示來源元素).</summary>
    public IReadOnlyList<SourceRef> SourcesOfFace(int faceId)
    {
        if (faceId < 0 || faceId >= Map.Faces.Count) throw new ArgumentOutOfRangeException(nameof(faceId));
        return Map.Face(faceId).DistinctSources.ToList();
    }

    public IReadOnlyList<SourceRef> SourcesAt(ScreenPoint point)
    {
        var face = FaceAt(point);
        return face is null ? Array.Empty<SourceRef>() : SourcesOfFace(face.Id);
    }

    public IReadOnlyList<SourceRef> SourcesOfSelection() =>
        _selected.SelectMany(SourcesOfFace).Distinct().ToList();

    /// <summary>The elements behind one zone, for selecting them back in the Revit view.</summary>
    public Result<IReadOnlyList<SourceRef>> SourcesOfZone(Guid zoneId)
    {
        var zone = Zones.Zone(zoneId);
        if (zone is null) return Result.Failure<IReadOnlyList<SourceRef>>(UnknownZone(zoneId));

        return Result.Success<IReadOnlyList<SourceRef>>(zone.FaceIds.SelectMany(SourcesOfFace).Distinct().ToList());
    }

    // ---- internals ------------------------------------------------------------------------

    private EditorState Snapshot() => new EditorState(Zones, ActiveZoneId);

    private void Restore(EditorState state)
    {
        Zones = state.Zones;
        ActiveZoneId = state.ActiveZoneId.HasValue && Zones.Contains(state.ActiveZoneId.Value)
            ? state.ActiveZoneId
            : null;

        // The drafts may no longer hold faces the selection still names; drop those so the canvas
        // never highlights a face that belongs nowhere the user can see.
        _selected.RemoveWhere(id => id < 0 || id >= Map.Faces.Count);
    }

    /// <summary>
    /// Spec 10.3 forbids merging blocks that do not touch unless the user says so. The question is
    /// asked only when an edit actually breaks the zone into more pieces than it already had, so
    /// adding a neighbour to a zone that is already in two pieces does not ask again.
    /// </summary>
    private Error? CheckDisjoint(ZoneDraft zone, IReadOnlyList<int> requested, bool confirmed)
    {
        if (confirmed || zone.AllowsDisjointParts) return null;

        var prospective = zone.FaceIds.Concat(requested).Distinct().ToList();
        var after = Map.ContiguousPartsOf(prospective).Count;
        var current = zone.IsEmpty ? 1 : Map.ContiguousPartsOf(zone.FaceIds).Count;
        if (after <= Math.Max(current, 1)) return null;

        return new Error(
            DisjointNotConfirmedCode,
            string.Format(
                CultureInfo.InvariantCulture,
                "加入後「{0}」會分成 {1} 塊互不相連的區塊，將各自產生一個面積。確定要合併嗎？",
                zone.Name,
                after),
            string.Format(CultureInfo.InvariantCulture, "zoneId={0};parts={1}", zone.Id, after));
    }

    /// <summary>
    /// Keeps the stored confirmation honest: it is set only when the user has just confirmed a
    /// disjoint merge, and cleared the moment the zone is in one piece again, so a later split has
    /// to be confirmed on its own.
    /// </summary>
    private void SettleDisjointConfirmation(Guid zoneId, bool confirmed)
    {
        var zone = Zones.Zone(zoneId);
        if (zone is null) return;

        var parts = zone.IsEmpty ? 0 : Map.ContiguousPartsOf(zone.FaceIds).Count;
        var allowed = parts > 1 && (confirmed || zone.AllowsDisjointParts);
        if (allowed == zone.AllowsDisjointParts) return;

        var updated = Zones.SetDisjointAllowed(zoneId, allowed);
        if (updated.IsSuccess) Zones = updated.Value;
    }

    /// <summary>Names the zones an edit left in pieces, which the status line appends to its message.</summary>
    private string? SplitWarning(IEnumerable<Guid> zoneIds)
    {
        var split = zoneIds
            .Distinct()
            .Select(id => Zones.Zone(id))
            .Where(z => z is not null && !z.IsEmpty && Map.ContiguousPartsOf(z.FaceIds).Count > 1)
            .Select(z => string.Format(
                CultureInfo.InvariantCulture,
                "「{0}」{1} 塊不相連",
                z!.Name,
                Map.ContiguousPartsOf(z.FaceIds).Count))
            .ToList();

        return split.Count == 0 ? null : "注意：" + string.Join("、", split) + "，套用時會各自建立一個面積。";
    }

    private IEnumerable<ZoneVisual> BuildZoneVisuals(EditorViewport viewport)
    {
        foreach (var zone in Zones.Zones)
        {
            var region = Map.Combine(zone.FaceIds);
            var anchorFace = region.Faces.OrderByDescending(f => f.NetAreaSquareFeet).FirstOrDefault();

            yield return new ZoneVisual(
                zone.Id,
                zone.Name,
                zone.Color,
                region.Faces.Count,
                region.NetAreaSquareMeters,
                region.Faces.Sum(f => f.Holes.Count),
                region.ContiguousPartCount,
                zone.Id == ActiveZoneId,
                anchorFace is null ? (ScreenPoint?)null : viewport.ToScreen(anchorFace.RepresentativePoint),
                zone.AllowsDisjointParts,
                zone.Use);
        }
    }

    private void Apply(IEnumerable<int> faceIds, SelectionMode mode)
    {
        var picked = faceIds.Where(id => id >= 0 && id < Map.Faces.Count).Distinct().ToList();
        switch (mode)
        {
            case SelectionMode.Replace:
                _selected.Clear();
                foreach (var id in picked) _selected.Add(id);
                break;
            case SelectionMode.Add:
                foreach (var id in picked) _selected.Add(id);
                break;
            case SelectionMode.Toggle:
                foreach (var id in picked)
                {
                    if (!_selected.Remove(id)) _selected.Add(id);
                }

                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(mode));
        }
    }

    private List<int> Normalize(IEnumerable<int> faceIds, out Error? error)
    {
        var requested = faceIds.Distinct().OrderBy(x => x).ToList();
        var unknown = requested.Where(id => id < 0 || id >= Map.Faces.Count).ToList();
        if (unknown.Count > 0)
        {
            error = new Error(
                "regions.editor.unknownFace",
                "選取的範圍已經不在目前的求解結果中，請重新求解後再試。",
                "faceIds=" + string.Join(",", unknown));
            return requested;
        }

        error = null;
        return requested;
    }

    private int IndexOf(Guid zoneId)
    {
        for (var i = 0; i < Zones.Count; i++)
        {
            if (Zones.Zones[i].Id == zoneId) return i;
        }

        return 0;
    }

    private string NextZoneName()
    {
        var index = Zones.Count + 1;
        while (Zones.Zones.Any(z => string.Equals(z.Name, ZoneName(index), StringComparison.OrdinalIgnoreCase))) index++;
        return ZoneName(index);
    }

    private static string ZoneName(int index) => string.Format(CultureInfo.InvariantCulture, "區劃 {0}", index);

    private static Error NoFaceHere() => new Error(
        "regions.editor.noFaceHere", "這裡沒有封閉範圍，請點在已經圍起來的區域內。");

    private static Error NothingChosen() => new Error(
        "regions.editor.nothingChosen", "請先選取至少一個範圍。");

    private static Error UnknownZone(Guid zoneId) => new Error(
        "regions.zone.unknownZone", "找不到這個區劃，它可能已經被刪除。", "zoneId=" + zoneId);

    private static PlanExtent2D ExtentOf(IEnumerable<PlanFace> faces)
    {
        var points = faces.SelectMany(f => f.AllBoundaries).SelectMany(b => b.Vertices).ToList();
        return new PlanExtent2D(
            new Point2D(points.Min(p => p.X), points.Min(p => p.Y)),
            new Point2D(points.Max(p => p.X), points.Max(p => p.Y)));
    }

    private static PlanExtent2D MeasureExtent(PlanRegionMap map)
    {
        if (map.IsEmpty)
        {
            throw new ArgumentException(
                "A region map with no face has nothing to edit; solve the network first.", nameof(map));
        }

        return ExtentOf(map.Faces);
    }
}
