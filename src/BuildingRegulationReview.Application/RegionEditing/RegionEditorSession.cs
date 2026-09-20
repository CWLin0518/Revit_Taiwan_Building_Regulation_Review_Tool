using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
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
    public ZoneMembershipChange(Guid? zoneId, IEnumerable<int> faceIds, IEnumerable<FaceMove>? moves, string message)
    {
        if (string.IsNullOrWhiteSpace(message)) throw new ArgumentException("A change needs a message.", nameof(message));

        ZoneId = zoneId;
        FaceIds = new ReadOnlyCollection<int>((faceIds ?? throw new ArgumentNullException(nameof(faceIds))).Distinct().OrderBy(x => x).ToList());
        MovedFrom = new ReadOnlyCollection<FaceMove>((moves ?? Array.Empty<FaceMove>()).ToList());
        Message = message.Trim();
    }

    /// <summary>The zone the faces are in now, or null when they were taken out of every zone.</summary>
    public Guid? ZoneId { get; }

    public IReadOnlyList<int> FaceIds { get; }
    public IReadOnlyList<FaceMove> MovedFrom { get; }
    public string Message { get; }

    public override string ToString() => Message;
}

/// <summary>
/// The Region Editor itself (spec 10.3, P2-T05): it holds the solved map, the 區劃 drafts and what is
/// selected, turns canvas gestures into edits, and builds the picture to draw. It knows nothing about
/// WPF or Revit, so every interaction rule here is testable without a window.
/// </summary>
/// <remarks>
/// The session is deliberately the only mutable object: the map is fixed for its lifetime and the
/// zone set is immutable, so P2-T06 can keep past <see cref="Zones"/> values for Undo/Redo without
/// snapshotting anything else. Nothing here writes to Revit; write-back is P2-T07.
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

    private readonly SortedSet<int> _selected = new SortedSet<int>();
    private readonly List<EditorIssue> _issues;

    public RegionEditorSession(PlanRegionMap map, ScreenSize canvasSize, IEnumerable<NetworkIssue>? networkIssues = null)
    {
        Map = map ?? throw new ArgumentNullException(nameof(map));

        ModelExtent = map.Extent ?? MeasureExtent(map);
        Viewport = EditorViewport.FitTo(ModelExtent, canvasSize);
        Zones = ZoneDraftSet.Empty;

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

        return new RegionEditorView(viewport, faces, BuildZoneVisuals(viewport), _issues, viewport.ToScreen, ActiveZoneId, _selected);
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

        Zones = added.Value;
        ActiveZoneId = zone.Id;
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
        var renamed = Zones.Rename(zoneId, name);
        if (renamed.IsFailure) return Result.Failure(renamed.Error);

        Zones = renamed.Value;
        return Result.Success();
    }

    public Result RecolorZone(Guid zoneId, ZoneColor color)
    {
        var recolored = Zones.Recolor(zoneId, color);
        if (recolored.IsFailure) return Result.Failure(recolored.Error);

        Zones = recolored.Value;
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

        Zones = removed.Value;
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
    public Result<ZoneMembershipChange> AddFaceAt(ScreenPoint point)
    {
        var face = FaceAt(point);
        if (face is null) return Result.Failure<ZoneMembershipChange>(NoFaceHere());

        Apply(new[] { face.Id }, SelectionMode.Replace);
        return AddFaces(new[] { face.Id });
    }

    /// <summary>Right click: takes the face under the cursor out of whichever 區劃 holds it.</summary>
    public Result<ZoneMembershipChange> RemoveFaceAt(ScreenPoint point)
    {
        var face = FaceAt(point);
        if (face is null) return Result.Failure<ZoneMembershipChange>(NoFaceHere());

        return RemoveFaces(new[] { face.Id });
    }

    public Result<ZoneMembershipChange> AddSelectionToActiveZone() => AddFaces(_selected.ToList());

    public Result<ZoneMembershipChange> RemoveSelectionFromZones() => RemoveFaces(_selected.ToList());

    /// <summary>Puts faces into the active 區劃, moving any that another zone already held.</summary>
    public Result<ZoneMembershipChange> AddFaces(IEnumerable<int> faceIds)
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
            return Result.Failure<ZoneMembershipChange>(new Error(
                "regions.zone.faceAlreadyInZone",
                $"選取的範圍已經屬於「{zone.Name}」。",
                "faceIds=" + string.Join(",", requested)));
        }

        var message = string.Format(CultureInfo.InvariantCulture, "已將 {0} 個範圍加入「{1}」。", added.Count, zone.Name);
        if (moves.Count > 0)
        {
            message += string.Format(
                CultureInfo.InvariantCulture,
                "其中 {0} 個原屬於{1}，已改為此區劃。",
                moves.Count,
                string.Join("、", moves.Select(m => "「" + m.FromZoneName + "」").Distinct()));
        }

        return Result.Success(new ZoneMembershipChange(zone.Id, added, moves, message));
    }

    /// <summary>Takes faces out of the 區劃 that holds them, leaving them unassigned.</summary>
    public Result<ZoneMembershipChange> RemoveFaces(IEnumerable<int> faceIds)
    {
        if (faceIds is null) throw new ArgumentNullException(nameof(faceIds));

        var requested = Normalize(faceIds, out var invalid);
        if (invalid is not null) return Result.Failure<ZoneMembershipChange>(invalid);
        if (requested.Count == 0) return Result.Failure<ZoneMembershipChange>(NothingChosen());

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
            return Result.Failure<ZoneMembershipChange>(new Error(
                "regions.zone.faceNotAssigned",
                "選取的範圍不屬於任何區劃。",
                "faceIds=" + string.Join(",", requested)));
        }

        var names = moves.Select(m => "「" + m.FromZoneName + "」").Distinct().ToList();
        var message = string.Format(
            CultureInfo.InvariantCulture,
            "已將 {0} 個範圍移出{1}。",
            removed.Count,
            names.Count > 0 ? string.Join("、", names) : "區劃");

        return Result.Success(new ZoneMembershipChange(null, removed, moves, message));
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
                anchorFace is null ? (ScreenPoint?)null : viewport.ToScreen(anchorFace.RepresentativePoint));
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
