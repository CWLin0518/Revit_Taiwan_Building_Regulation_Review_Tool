using System;
using System.Collections.Generic;
using System.Linq;
using BuildingRegulationReview.Domain.Regions;

namespace BuildingRegulationReview.Application.RegionEditing;

/// <summary>
/// Everything one Undo step has to put back: the 區劃 drafts and which of them was active. Zoom, pan
/// and selection are deliberately absent — they are not edits, and restoring them would move the
/// view out from under the user on every Undo.
/// </summary>
public sealed class EditorState
{
    public EditorState(ZoneDraftSet zones, Guid? activeZoneId)
    {
        Zones = zones ?? throw new ArgumentNullException(nameof(zones));
        ActiveZoneId = activeZoneId;
    }

    public ZoneDraftSet Zones { get; }
    public Guid? ActiveZoneId { get; }

    public override string ToString() => $"{Zones.Count} zones, active={ActiveZoneId}";
}

/// <summary>A past or future state together with the name of the edit that leads away from it.</summary>
public sealed class EditorHistoryEntry
{
    public EditorHistoryEntry(EditorState state, string label)
    {
        if (string.IsNullOrWhiteSpace(label)) throw new ArgumentException("A history entry needs a label.", nameof(label));

        State = state ?? throw new ArgumentNullException(nameof(state));
        Label = label.Trim();
    }

    public EditorState State { get; }

    /// <summary>What the user did, phrased for the Undo button: 建立區劃「A」.</summary>
    public string Label { get; }

    public override string ToString() => Label;
}

/// <summary>
/// Undo/Redo for the Editor (spec 10.3). Because <see cref="ZoneDraftSet"/> is immutable, a step is
/// simply the previous value: there are no inverse operations to get wrong, and replaying the stack
/// can never produce a state the drafts could not have reached on their own.
/// </summary>
/// <remarks>
/// The stack is bounded so a long editing session cannot grow without limit; the oldest step falls
/// off the bottom, which is the one nobody goes back to. Recording a new edit drops the redo branch,
/// as everywhere else.
/// </remarks>
public sealed class EditorHistory
{
    /// <summary>How many steps back the Editor can go.</summary>
    public const int MaximumDepth = 200;

    private readonly List<EditorHistoryEntry> _undo = new List<EditorHistoryEntry>();
    private readonly List<EditorHistoryEntry> _redo = new List<EditorHistoryEntry>();

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;
    public int UndoCount => _undo.Count;
    public int RedoCount => _redo.Count;

    /// <summary>The edit Undo would take back, or null when there is nothing to undo.</summary>
    public string? UndoLabel => CanUndo ? _undo[_undo.Count - 1].Label : null;

    /// <summary>The edit Redo would put back, or null when there is nothing to redo.</summary>
    public string? RedoLabel => CanRedo ? _redo[_redo.Count - 1].Label : null;

    /// <summary>Records the state an edit is about to leave behind. Call it only once the edit took.</summary>
    public void Record(EditorState before, string label)
    {
        if (before is null) throw new ArgumentNullException(nameof(before));

        _undo.Add(new EditorHistoryEntry(before, label));
        if (_undo.Count > MaximumDepth) _undo.RemoveAt(0);
        _redo.Clear();
    }

    /// <summary>Steps back, handing the current state to the redo stack. Null when nothing to undo.</summary>
    public EditorHistoryEntry? Undo(EditorState current)
    {
        if (current is null) throw new ArgumentNullException(nameof(current));
        if (!CanUndo) return null;

        var entry = _undo[_undo.Count - 1];
        _undo.RemoveAt(_undo.Count - 1);
        _redo.Add(new EditorHistoryEntry(current, entry.Label));
        return entry;
    }

    /// <summary>Steps forward again. Null when nothing was undone.</summary>
    public EditorHistoryEntry? Redo(EditorState current)
    {
        if (current is null) throw new ArgumentNullException(nameof(current));
        if (!CanRedo) return null;

        var entry = _redo[_redo.Count - 1];
        _redo.RemoveAt(_redo.Count - 1);
        _undo.Add(new EditorHistoryEntry(current, entry.Label));
        return entry;
    }

    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
    }

    /// <summary>The recorded edits, oldest first, for showing what Undo would walk back through.</summary>
    public IReadOnlyList<string> UndoLabels => _undo.Select(e => e.Label).ToList();
}
