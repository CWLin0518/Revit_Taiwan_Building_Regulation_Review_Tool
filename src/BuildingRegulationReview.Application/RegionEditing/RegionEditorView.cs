using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using BuildingRegulationReview.Domain.Geometry;
using BuildingRegulationReview.Domain.Regions;

namespace BuildingRegulationReview.Application.RegionEditing;

/// <summary>Where an issue shown in the Editor came from: line network repair, or region solving.</summary>
public enum EditorIssueOrigin
{
    Network,
    Region
}

/// <summary>
/// One line of the issue list (spec 10.3, 顯示…未閉合錯誤). Repair issues and solve issues are shown
/// as one list because to the reviewer they are the same question — what is stopping a boundary from
/// closing — and both carry the source elements to select in Revit.
/// </summary>
public sealed class EditorIssue
{
    public EditorIssue(
        EditorIssueOrigin origin,
        NetworkIssueSeverity severity,
        string kind,
        Point2D location,
        string message,
        IEnumerable<SourceRef>? sources = null,
        int? faceId = null)
    {
        if (!Enum.IsDefined(typeof(EditorIssueOrigin), origin)) throw new ArgumentOutOfRangeException(nameof(origin));
        if (!Enum.IsDefined(typeof(NetworkIssueSeverity), severity)) throw new ArgumentOutOfRangeException(nameof(severity));
        if (string.IsNullOrWhiteSpace(kind)) throw new ArgumentException("An issue needs a kind.", nameof(kind));
        if (string.IsNullOrWhiteSpace(message)) throw new ArgumentException("An issue needs a message.", nameof(message));

        Origin = origin;
        Severity = severity;
        Kind = kind.Trim();
        Location = location;
        Message = message.Trim();
        Sources = new ReadOnlyCollection<SourceRef>((sources ?? Array.Empty<SourceRef>()).Where(x => x is not null).Distinct().ToList());
        FaceId = faceId;
    }

    public EditorIssueOrigin Origin { get; }
    public NetworkIssueSeverity Severity { get; }
    public string Kind { get; }
    public Point2D Location { get; }
    public string Message { get; }
    public IReadOnlyList<SourceRef> Sources { get; }
    public int? FaceId { get; }
    public bool IsError => Severity == NetworkIssueSeverity.Error;

    public static EditorIssue From(NetworkIssue issue)
    {
        if (issue is null) throw new ArgumentNullException(nameof(issue));
        return new EditorIssue(EditorIssueOrigin.Network, issue.Severity, issue.Kind.ToString(), issue.Location, issue.Message, issue.Sources);
    }

    public static EditorIssue From(RegionIssue issue)
    {
        if (issue is null) throw new ArgumentNullException(nameof(issue));
        return new EditorIssue(EditorIssueOrigin.Region, issue.Severity, issue.Kind.ToString(), issue.Location, issue.Message, issue.Sources, issue.FaceId);
    }

    public override string ToString() => $"[{Severity}] {Kind}: {Message}";
}

/// <summary>One solved face ready to draw: rings in screen pixels, how it is filled, how it is selected.</summary>
public sealed class FaceVisual
{
    public FaceVisual(
        int faceId,
        IEnumerable<IReadOnlyList<ScreenPoint>> rings,
        ScreenPoint labelAnchor,
        double netAreaSquareMeters,
        int holeCount,
        Guid? zoneId,
        ZoneColor? fill,
        bool isSelected,
        bool isInActiveZone)
    {
        if (faceId < 0) throw new ArgumentOutOfRangeException(nameof(faceId));
        if (rings is null) throw new ArgumentNullException(nameof(rings));
        if (holeCount < 0) throw new ArgumentOutOfRangeException(nameof(holeCount));

        var copy = rings.ToList();
        if (copy.Count == 0) throw new ArgumentException("A face needs at least its outer ring.", nameof(rings));

        FaceId = faceId;
        Rings = new ReadOnlyCollection<IReadOnlyList<ScreenPoint>>(copy);
        LabelAnchor = labelAnchor;
        NetAreaSquareMeters = netAreaSquareMeters;
        HoleCount = holeCount;
        ZoneId = zoneId;
        Fill = fill;
        IsSelected = isSelected;
        IsInActiveZone = isInActiveZone;
    }

    public int FaceId { get; }

    /// <summary>Ring 0 is the outer boundary; the rest are holes.</summary>
    public IReadOnlyList<IReadOnlyList<ScreenPoint>> Rings { get; }

    public IReadOnlyList<ScreenPoint> OuterRing => Rings[0];
    public IEnumerable<IReadOnlyList<ScreenPoint>> HoleRings => Rings.Skip(1);

    public ScreenPoint LabelAnchor { get; }
    public double NetAreaSquareMeters { get; }
    public int HoleCount { get; }

    /// <summary>The zone this face belongs to, or null while it is still unassigned.</summary>
    public Guid? ZoneId { get; }

    /// <summary>The zone colour, or null when the face belongs to no zone and is drawn plain.</summary>
    public ZoneColor? Fill { get; }

    public bool IsSelected { get; }
    public bool IsInActiveZone { get; }
    public bool IsAssigned => ZoneId.HasValue;
}

/// <summary>One 區劃 as the Editor lists and labels it, including whether its parts hang together.</summary>
public sealed class ZoneVisual
{
    public ZoneVisual(
        Guid zoneId,
        string name,
        ZoneColor color,
        int faceCount,
        double netAreaSquareMeters,
        int holeCount,
        int contiguousPartCount,
        bool isActive,
        ScreenPoint? labelAnchor,
        bool allowsDisjointParts = false,
        string? use = null)
    {
        if (zoneId == Guid.Empty) throw new ArgumentException("Zone ID cannot be empty.", nameof(zoneId));
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("A zone needs a name.", nameof(name));
        if (faceCount < 0) throw new ArgumentOutOfRangeException(nameof(faceCount));
        if (holeCount < 0) throw new ArgumentOutOfRangeException(nameof(holeCount));
        if (contiguousPartCount < 0) throw new ArgumentOutOfRangeException(nameof(contiguousPartCount));

        ZoneId = zoneId;
        Name = name.Trim();
        Color = color;
        FaceCount = faceCount;
        NetAreaSquareMeters = netAreaSquareMeters;
        HoleCount = holeCount;
        ContiguousPartCount = contiguousPartCount;
        IsActive = isActive;
        LabelAnchor = labelAnchor;
        AllowsDisjointParts = allowsDisjointParts;
        Use = use;
    }

    public Guid ZoneId { get; }
    public string Name { get; }
    public ZoneColor Color { get; }
    public int FaceCount { get; }

    /// <summary>Draft area. The Area element Revit reports after write-back is the authoritative one.</summary>
    public double NetAreaSquareMeters { get; }

    public int HoleCount { get; }
    public int ContiguousPartCount { get; }
    public bool IsContiguous => ContiguousPartCount <= 1;
    public bool IsActive { get; }
    public bool IsEmpty => FaceCount == 0;

    /// <summary>
    /// True once the user has confirmed this zone may hold blocks that do not touch (spec 10.3), so
    /// the label can say the split was intended rather than leaving it looking like a mistake.
    /// </summary>
    public bool AllowsDisjointParts { get; }

    /// <summary>
    /// 防火檢討_區劃用途 this draft would write: empty for the explicit 一般區劃, null for 不變更 —
    /// the zone whose Areas disagree, or whose parameter could not be read. See <c>ZoneDraft.Use</c>.
    /// </summary>
    public string? Use { get; }

    /// <summary>
    /// What the 用途 column reads. The three states have to be told apart on screen, because the
    /// user's next click decides whether their Areas keep what they carry.
    /// </summary>
    public string UseText => Use switch
    {
        null => "多種用途",
        "" => "一般區劃",
        _ => Use
    };

    /// <summary>True while the 用途 column is showing a state rather than a use, so it can be dimmed.</summary>
    public bool HasUse => !string.IsNullOrEmpty(Use);

    /// <summary>Where to draw the zone label, or null when the zone holds no face yet.</summary>
    public ScreenPoint? LabelAnchor { get; }

    /// <summary>Name and draft area on one line each, plus holes and disjoint parts when there are any.</summary>
    public string Label
    {
        get
        {
            var text = Name + "\n" + AreaText;
            if (HoleCount > 0) text += string.Format(CultureInfo.InvariantCulture, "\n孔洞 {0}", HoleCount);
            if (!IsContiguous)
            {
                text += string.Format(
                    CultureInfo.InvariantCulture,
                    "\n{0} 塊不相連{1}",
                    ContiguousPartCount,
                    AllowsDisjointParts ? "（已確認）" : string.Empty);
            }

            return text;
        }
    }

    public string AreaText => string.Format(CultureInfo.InvariantCulture, "{0:0.##} m²", NetAreaSquareMeters);

    public override string ToString() => $"{Name} ({FaceCount} faces, {AreaText})";
}

/// <summary>
/// Everything the canvas needs for one redraw, already in screen coordinates. The Editor builds this
/// so the WPF layer holds no geometry or policy of its own and the whole display can be asserted in
/// tests without a window.
/// </summary>
public sealed class RegionEditorView
{
    public RegionEditorView(
        EditorViewport viewport,
        IEnumerable<FaceVisual> faces,
        IEnumerable<ZoneVisual> zones,
        IEnumerable<EditorIssue> issues,
        Func<Point2D, ScreenPoint> toScreen,
        Guid? activeZoneId,
        IEnumerable<int> selectedFaceIds,
        bool canUndo = false,
        bool canRedo = false,
        bool hasUnappliedChanges = false)
    {
        if (toScreen is null) throw new ArgumentNullException(nameof(toScreen));

        Viewport = viewport ?? throw new ArgumentNullException(nameof(viewport));
        Faces = new ReadOnlyCollection<FaceVisual>((faces ?? throw new ArgumentNullException(nameof(faces))).ToList());
        Zones = new ReadOnlyCollection<ZoneVisual>((zones ?? throw new ArgumentNullException(nameof(zones))).ToList());
        var issueList = (issues ?? throw new ArgumentNullException(nameof(issues))).ToList();
        Issues = new ReadOnlyCollection<EditorIssue>(issueList);
        IssueAnchors = new ReadOnlyCollection<ScreenPoint>(issueList.Select(i => toScreen(i.Location)).ToList());
        ActiveZoneId = activeZoneId;
        SelectedFaceIds = new ReadOnlyCollection<int>((selectedFaceIds ?? Array.Empty<int>()).Distinct().OrderBy(x => x).ToList());
        CanUndo = canUndo;
        CanRedo = canRedo;
        HasUnappliedChanges = hasUnappliedChanges;
    }

    public EditorViewport Viewport { get; }
    public IReadOnlyList<FaceVisual> Faces { get; }
    public IReadOnlyList<ZoneVisual> Zones { get; }
    public IReadOnlyList<EditorIssue> Issues { get; }

    /// <summary>Screen positions of <see cref="Issues"/>, index for index.</summary>
    public IReadOnlyList<ScreenPoint> IssueAnchors { get; }

    public Guid? ActiveZoneId { get; }
    public IReadOnlyList<int> SelectedFaceIds { get; }

    /// <summary>Whether the Undo and Redo commands have anything to do, for enabling the buttons.</summary>
    public bool CanUndo { get; }

    public bool CanRedo { get; }

    /// <summary>Whether the drafts differ from the model, which the title bar marks with an asterisk.</summary>
    public bool HasUnappliedChanges { get; }

    /// <summary>Zones the user confirmed may hold blocks that do not touch (spec 10.3).</summary>
    public IEnumerable<ZoneVisual> DisjointZones => Zones.Where(z => !z.IsContiguous);

    public int AssignedFaceCount => Faces.Count(f => f.IsAssigned);
    public int UnassignedFaceCount => Faces.Count - AssignedFaceCount;
    public double TotalNetAreaSquareMeters => Faces.Sum(f => f.NetAreaSquareMeters);
    public double AssignedNetAreaSquareMeters => Faces.Where(f => f.IsAssigned).Sum(f => f.NetAreaSquareMeters);
    public int ErrorCount => Issues.Count(i => i.IsError);
    public bool HasErrors => ErrorCount > 0;

    /// <summary>The one-line summary the Editor shows under the canvas.</summary>
    public string Summary => string.Format(
        CultureInfo.InvariantCulture,
        "{0} 個範圍，已指派 {1} 個（{2:0.##} m²），未指派 {3} 個；區劃 {4} 個；問題 {5} 則（錯誤 {6} 則）",
        Faces.Count,
        AssignedFaceCount,
        AssignedNetAreaSquareMeters,
        UnassignedFaceCount,
        Zones.Count,
        Issues.Count,
        ErrorCount);

    /// <summary>
    /// The draft state line next to the summary: whether anything is waiting to be applied, and how
    /// many zones are in pieces the user confirmed.
    /// </summary>
    public string DraftState
    {
        get
        {
            var text = HasUnappliedChanges ? "草稿尚未套用到模型" : "草稿與模型一致";
            var disjoint = DisjointZones.Count();
            if (disjoint > 0)
            {
                text += string.Format(CultureInfo.InvariantCulture, "；{0} 個區劃不相連", disjoint);
            }

            return text;
        }
    }
}
