using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using BuildingRegulationReview.Domain.Geometry;

namespace BuildingRegulationReview.Application.Candidates;

/// <summary>The model elements Phase 3 reviews (spec 11.5 構件, 11.6 開口).</summary>
public enum CandidateCategory
{
    Wall,
    Column,
    StructuralFraming,
    Floor,
    Door,
    Window,
    CurtainPanel
}

public static class CandidateCategories
{
    public static IReadOnlyList<CandidateCategory> Members { get; } = new ReadOnlyCollection<CandidateCategory>(new[]
    {
        CandidateCategory.Wall, CandidateCategory.Column, CandidateCategory.StructuralFraming, CandidateCategory.Floor
    });

    public static IReadOnlyList<CandidateCategory> Openings { get; } = new ReadOnlyCollection<CandidateCategory>(new[]
    {
        CandidateCategory.Door, CandidateCategory.Window, CandidateCategory.CurtainPanel
    });

    public static bool IsMember(CandidateCategory category) => Members.Contains(category);
    public static bool IsOpening(CandidateCategory category) => Openings.Contains(category);

    /// <summary>Members measured along a centreline; the others are measured by their plan outline.</summary>
    public static bool IsLinear(CandidateCategory category) =>
        category == CandidateCategory.Wall || category == CandidateCategory.StructuralFraming;

    /// <summary>
    /// The text <c>element.category</c> and <c>opening.kind</c> carry into rule expressions, as the
    /// field whitelist documents it.
    /// </summary>
    public static string RuleText(CandidateCategory category) => category switch
    {
        CandidateCategory.Wall => "Walls",
        CandidateCategory.Column => "Columns",
        CandidateCategory.StructuralFraming => "StructuralFraming",
        CandidateCategory.Floor => "Floors",
        CandidateCategory.Door => "Door",
        CandidateCategory.Window => "Window",
        CandidateCategory.CurtainPanel => "CurtainPanel",
        _ => throw new ArgumentOutOfRangeException(nameof(category))
    };

    public static string Label(CandidateCategory category) => category switch
    {
        CandidateCategory.Wall => "牆",
        CandidateCategory.Column => "柱",
        CandidateCategory.StructuralFraming => "梁",
        CandidateCategory.Floor => "樓板",
        CandidateCategory.Door => "門",
        CandidateCategory.Window => "窗",
        CandidateCategory.CurtainPanel => "帷幕嵌板",
        _ => throw new ArgumentOutOfRangeException(nameof(category))
    };
}

/// <summary>Which model element an observation came from, down to the link instance when there is one.</summary>
public sealed class CandidateSource : IEquatable<CandidateSource>
{
    public CandidateSource(string documentUniqueId, string elementUniqueId, string? linkInstanceUniqueId = null)
    {
        if (string.IsNullOrWhiteSpace(documentUniqueId)) throw new ArgumentException("Document identity is required.", nameof(documentUniqueId));
        if (string.IsNullOrWhiteSpace(elementUniqueId)) throw new ArgumentException("Element UniqueId is required.", nameof(elementUniqueId));

        DocumentUniqueId = documentUniqueId.Trim();
        ElementUniqueId = elementUniqueId.Trim();
        LinkInstanceUniqueId = string.IsNullOrWhiteSpace(linkInstanceUniqueId) ? null : linkInstanceUniqueId!.Trim();
    }

    public string DocumentUniqueId { get; }
    public string ElementUniqueId { get; }
    public string? LinkInstanceUniqueId { get; }
    public bool IsFromLink => LinkInstanceUniqueId is not null;

    public bool Equals(CandidateSource? other) =>
        other is not null &&
        string.Equals(DocumentUniqueId, other.DocumentUniqueId, StringComparison.Ordinal) &&
        string.Equals(ElementUniqueId, other.ElementUniqueId, StringComparison.Ordinal) &&
        string.Equals(LinkInstanceUniqueId, other.LinkInstanceUniqueId, StringComparison.Ordinal);

    public override bool Equals(object? obj) => Equals(obj as CandidateSource);

    public override int GetHashCode()
    {
        unchecked
        {
            var hash = 17;
            hash = (hash * 31) + StringComparer.Ordinal.GetHashCode(DocumentUniqueId);
            hash = (hash * 31) + StringComparer.Ordinal.GetHashCode(ElementUniqueId);
            hash = (hash * 31) + (LinkInstanceUniqueId is null ? 0 : StringComparer.Ordinal.GetHashCode(LinkInstanceUniqueId));
            return hash;
        }
    }

    public override string ToString() =>
        LinkInstanceUniqueId is null ? ElementUniqueId : $"{ElementUniqueId}@{LinkInstanceUniqueId}";

    internal static int Compare(CandidateSource a, CandidateSource b)
    {
        var byElement = string.CompareOrdinal(a.ElementUniqueId, b.ElementUniqueId);
        if (byElement != 0) return byElement;
        var byDocument = string.CompareOrdinal(a.DocumentUniqueId, b.DocumentUniqueId);
        return byDocument != 0 ? byDocument : string.CompareOrdinal(a.LinkInstanceUniqueId ?? string.Empty, b.LinkInstanceUniqueId ?? string.Empty);
    }
}

/// <summary>
/// One wall, column, beam or floor as the Revit adapter read it: plain plan geometry in host feet,
/// plus the identifying data a result has to carry. No Revit type crosses this line.
/// </summary>
/// <remarks>
/// Walls and beams are described by their location centreline and width, because that is what the
/// 區劃 boundary was drawn from (spec 10.1). Columns and floors are described by their plan outline.
/// </remarks>
public sealed class MemberObservation
{
    public MemberObservation(
        CandidateSource source,
        CandidateCategory category,
        IEnumerable<Point2D>? centerline = null,
        IEnumerable<IReadOnlyList<Point2D>>? outlines = null,
        double? widthFeet = null,
        string? typeUniqueId = null,
        string? typeName = null,
        bool? isStructural = null,
        bool isCurtainWall = false)
    {
        Source = source ?? throw new ArgumentNullException(nameof(source));
        if (!CandidateCategories.IsMember(category)) throw new ArgumentOutOfRangeException(nameof(category), "Not a member category.");

        var line = (centerline ?? Array.Empty<Point2D>()).ToList();
        var rings = (outlines ?? Array.Empty<IReadOnlyList<Point2D>>()).Where(r => r is not null).Select(r => r.ToList()).ToList();

        if (CandidateCategories.IsLinear(category))
        {
            if (rings.Count > 0) throw new ArgumentException("A wall or beam is described by its centreline, not an outline.", nameof(outlines));
            if (line.Count == 1) throw new ArgumentException("A centreline needs at least two points.", nameof(centerline));
        }
        else
        {
            if (line.Count > 0) throw new ArgumentException("A column or floor is described by its outline, not a centreline.", nameof(centerline));
            if (rings.Any(r => r.Count < 3)) throw new ArgumentException("An outline ring needs at least three points.", nameof(outlines));
        }

        if (widthFeet.HasValue && (double.IsNaN(widthFeet.Value) || double.IsInfinity(widthFeet.Value) || widthFeet.Value < 0))
            throw new ArgumentOutOfRangeException(nameof(widthFeet), "Width must be a finite, non-negative number.");
        if (isCurtainWall && category != CandidateCategory.Wall)
            throw new ArgumentException("Only a wall can be a curtain wall.", nameof(isCurtainWall));

        Category = category;
        Centerline = new ReadOnlyCollection<Point2D>(line);
        Outlines = new ReadOnlyCollection<IReadOnlyList<Point2D>>(
            rings.Select(r => (IReadOnlyList<Point2D>)new ReadOnlyCollection<Point2D>(r)).ToList());
        WidthFeet = widthFeet;
        TypeUniqueId = string.IsNullOrWhiteSpace(typeUniqueId) ? null : typeUniqueId!.Trim();
        TypeName = string.IsNullOrWhiteSpace(typeName) ? null : typeName!.Trim();
        IsStructural = isStructural;
        IsCurtainWall = isCurtainWall;
    }

    public CandidateSource Source { get; }
    public CandidateCategory Category { get; }

    /// <summary>Plan centreline of a wall or beam; empty when the element had none to read.</summary>
    public IReadOnlyList<Point2D> Centerline { get; }

    /// <summary>Plan outline rings of a column or floor, even-odd; empty when none could be read.</summary>
    public IReadOnlyList<IReadOnlyList<Point2D>> Outlines { get; }

    /// <summary>Wall thickness or beam width, when the type states one.</summary>
    public double? WidthFeet { get; }

    public string? TypeUniqueId { get; }
    public string? TypeName { get; }
    public bool? IsStructural { get; }
    public bool IsCurtainWall { get; }

    public bool HasPlanGeometry => CandidateCategories.IsLinear(Category) ? Centerline.Count >= 2 : Outlines.Count > 0;
}

/// <summary>One door, window or curtain panel as the Revit adapter read it.</summary>
public sealed class OpeningObservation
{
    public OpeningObservation(
        CandidateSource source,
        CandidateCategory category,
        string? hostUniqueId,
        Point2D? location,
        double? widthFeet = null,
        double? heightFeet = null,
        string? typeUniqueId = null,
        string? typeName = null)
    {
        Source = source ?? throw new ArgumentNullException(nameof(source));
        if (!CandidateCategories.IsOpening(category)) throw new ArgumentOutOfRangeException(nameof(category), "Not an opening category.");
        RequireSize(widthFeet, nameof(widthFeet));
        RequireSize(heightFeet, nameof(heightFeet));

        Category = category;
        HostUniqueId = string.IsNullOrWhiteSpace(hostUniqueId) ? null : hostUniqueId!.Trim();
        Location = location;
        WidthFeet = widthFeet;
        HeightFeet = heightFeet;
        TypeUniqueId = string.IsNullOrWhiteSpace(typeUniqueId) ? null : typeUniqueId!.Trim();
        TypeName = string.IsNullOrWhiteSpace(typeName) ? null : typeName!.Trim();
    }

    public CandidateSource Source { get; }
    public CandidateCategory Category { get; }

    /// <summary>The UniqueId of the element hosting the opening, in the same document; null when not hosted.</summary>
    public string? HostUniqueId { get; }

    /// <summary>Plan insertion point. For a hosted opening it lies on the host's location line.</summary>
    public Point2D? Location { get; }

    public double? WidthFeet { get; }
    public double? HeightFeet { get; }
    public string? TypeUniqueId { get; }
    public string? TypeName { get; }

    public bool IsHosted => HostUniqueId is not null;

    private static void RequireSize(double? value, string name)
    {
        if (value.HasValue && (double.IsNaN(value.Value) || double.IsInfinity(value.Value) || value.Value <= 0))
            throw new ArgumentOutOfRangeException(name, "An opening size must be a finite, positive number.");
    }
}

/// <summary>One Area the tool wrote for a 區劃, with the outline and area Revit computed for it.</summary>
public sealed class ZonePartObservation
{
    public ZonePartObservation(
        string areaUniqueId,
        IEnumerable<IReadOnlyList<Point2D>>? boundaryLoops,
        double? revitAreaSquareFeet,
        Point2D? placement = null)
    {
        if (string.IsNullOrWhiteSpace(areaUniqueId)) throw new ArgumentException("Area UniqueId is required.", nameof(areaUniqueId));
        if (revitAreaSquareFeet.HasValue &&
            (double.IsNaN(revitAreaSquareFeet.Value) || double.IsInfinity(revitAreaSquareFeet.Value) || revitAreaSquareFeet.Value < 0))
            throw new ArgumentOutOfRangeException(nameof(revitAreaSquareFeet));

        AreaUniqueId = areaUniqueId.Trim();
        BoundaryLoops = new ReadOnlyCollection<IReadOnlyList<Point2D>>(
            (boundaryLoops ?? Array.Empty<IReadOnlyList<Point2D>>())
                .Where(l => l is not null && l.Count >= 3)
                .Select(l => (IReadOnlyList<Point2D>)new ReadOnlyCollection<Point2D>(l.ToList()))
                .ToList());
        RevitAreaSquareFeet = revitAreaSquareFeet;
        Placement = placement;
    }

    public string AreaUniqueId { get; }

    /// <summary>Outer loop and holes; empty when Revit reports the Area as not enclosed.</summary>
    public IReadOnlyList<IReadOnlyList<Point2D>> BoundaryLoops { get; }

    public double? RevitAreaSquareFeet { get; }
    public Point2D? Placement { get; }
    public bool IsEnclosed => BoundaryLoops.Count > 0;
}

/// <summary>One 區劃 as the model holds it: its Zone ID, name and the Areas written for its parts.</summary>
public sealed class ZoneObservation
{
    public ZoneObservation(Guid zoneId, string name, IEnumerable<ZonePartObservation> parts)
    {
        if (zoneId == Guid.Empty) throw new ArgumentException("Zone ID cannot be empty.", nameof(zoneId));
        if (parts is null) throw new ArgumentNullException(nameof(parts));

        var list = parts.ToList();
        if (list.Count == 0) throw new ArgumentException("A zone needs at least one Area.", nameof(parts));
        if (list.Any(x => x is null)) throw new ArgumentException("A zone cannot hold a missing Area.", nameof(parts));
        if (list.GroupBy(x => x.AreaUniqueId, StringComparer.Ordinal).Any(g => g.Count() > 1))
            throw new ArgumentException("The same Area is listed twice.", nameof(parts));

        ZoneId = zoneId;
        Name = string.IsNullOrWhiteSpace(name) ? zoneId.ToString("D") : name.Trim();
        Parts = new ReadOnlyCollection<ZonePartObservation>(list.OrderBy(x => x.AreaUniqueId, StringComparer.Ordinal).ToList());
    }

    public Guid ZoneId { get; }
    public string Name { get; }
    public IReadOnlyList<ZonePartObservation> Parts { get; }
}

/// <summary>
/// Everything the Revit adapter read for one package's candidate resolution: the zones written in
/// its Area Plan and the members and openings of its storey. Pure data, safe outside the Revit API
/// context (spec 15).
/// </summary>
public sealed class CandidateObservationSet
{
    public CandidateObservationSet(
        Guid packageId,
        string levelUniqueId,
        string? levelName,
        IEnumerable<ZoneObservation> zones,
        IEnumerable<MemberObservation>? members = null,
        IEnumerable<OpeningObservation>? openings = null,
        IEnumerable<string>? warnings = null)
    {
        if (packageId == Guid.Empty) throw new ArgumentException("Package ID cannot be empty.", nameof(packageId));
        if (string.IsNullOrWhiteSpace(levelUniqueId)) throw new ArgumentException("Level UniqueId is required.", nameof(levelUniqueId));
        if (zones is null) throw new ArgumentNullException(nameof(zones));

        var zoneList = zones.ToList();
        var memberList = (members ?? Array.Empty<MemberObservation>()).ToList();
        var openingList = (openings ?? Array.Empty<OpeningObservation>()).ToList();
        if (zoneList.Any(x => x is null)) throw new ArgumentException("Zones cannot contain null.", nameof(zones));
        if (memberList.Any(x => x is null)) throw new ArgumentException("Members cannot contain null.", nameof(members));
        if (openingList.Any(x => x is null)) throw new ArgumentException("Openings cannot contain null.", nameof(openings));

        if (zoneList.GroupBy(x => x.ZoneId).Any(g => g.Count() > 1))
            throw new ArgumentException("The same zone is listed twice.", nameof(zones));

        var areaIds = zoneList.SelectMany(z => z.Parts).Select(p => p.AreaUniqueId).ToList();
        if (areaIds.Distinct(StringComparer.Ordinal).Count() != areaIds.Count)
            throw new ArgumentException("One Area cannot belong to two zones.", nameof(zones));

        var sources = memberList.Select(m => m.Source).Concat(openingList.Select(o => o.Source)).ToList();
        if (sources.Distinct().Count() != sources.Count)
            throw new ArgumentException("The same element is observed twice.", nameof(members));

        PackageId = packageId;
        LevelUniqueId = levelUniqueId.Trim();
        LevelName = string.IsNullOrWhiteSpace(levelName) ? null : levelName!.Trim();
        Zones = new ReadOnlyCollection<ZoneObservation>(zoneList);
        Members = new ReadOnlyCollection<MemberObservation>(memberList);
        Openings = new ReadOnlyCollection<OpeningObservation>(openingList);
        Warnings = new ReadOnlyCollection<string>((warnings ?? Array.Empty<string>())
            .Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).Distinct(StringComparer.Ordinal).ToList());
    }

    public Guid PackageId { get; }
    public string LevelUniqueId { get; }
    public string? LevelName { get; }
    public IReadOnlyList<ZoneObservation> Zones { get; }
    public IReadOnlyList<MemberObservation> Members { get; }
    public IReadOnlyList<OpeningObservation> Openings { get; }

    /// <summary>What the adapter skipped or approximated while reading, one sentence each.</summary>
    public IReadOnlyList<string> Warnings { get; }
}
