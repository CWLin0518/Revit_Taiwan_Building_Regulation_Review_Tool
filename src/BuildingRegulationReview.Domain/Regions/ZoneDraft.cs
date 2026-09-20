using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;

namespace BuildingRegulationReview.Domain.Regions;

/// <summary>
/// One 防火區劃 draft: a name, a colour and the solved faces the user has put into it (spec 10.3).
/// Immutable, so the Editor can keep past states for Undo/Redo in P2-T06 without copying defensively.
/// </summary>
public sealed class ZoneDraft
{
    /// <summary>Long enough for a descriptive Chinese name, short enough to survive an Area parameter.</summary>
    public const int MaximumNameLength = 120;

    public ZoneDraft(Guid id, string name, ZoneColor color, IEnumerable<int>? faceIds = null, bool allowsDisjointParts = false)
    {
        if (id == Guid.Empty) throw new ArgumentException("Zone ID cannot be empty.", nameof(id));

        Id = id;
        Name = NormalizeName(name, nameof(name));
        Color = color;
        AllowsDisjointParts = allowsDisjointParts;

        var faces = (faceIds ?? Array.Empty<int>()).Distinct().OrderBy(x => x).ToList();
        if (faces.Any(x => x < 0)) throw new ArgumentOutOfRangeException(nameof(faceIds), "A face ID cannot be negative.");
        FaceIds = new ReadOnlyCollection<int>(faces);
    }

    public Guid Id { get; }
    public string Name { get; }
    public ZoneColor Color { get; }

    /// <summary>Face IDs of <see cref="Geometry.PlanRegionMap"/>, ascending, so a zone has one shape.</summary>
    public IReadOnlyList<int> FaceIds { get; }

    /// <summary>
    /// Whether the user has explicitly confirmed that this zone may hold parts that do not touch
    /// (spec 10.3: 預設禁止不連通區塊合併，除非使用者明確確認). The Editor clears it again once the
    /// zone is contiguous, so a later disjoint merge asks anew rather than inheriting an old answer.
    /// </summary>
    public bool AllowsDisjointParts { get; }

    public int FaceCount => FaceIds.Count;
    public bool IsEmpty => FaceIds.Count == 0;

    public bool Contains(int faceId) => FaceIds.Contains(faceId);

    public ZoneDraft WithName(string name) => new ZoneDraft(Id, name, Color, FaceIds, AllowsDisjointParts);
    public ZoneDraft WithColor(ZoneColor color) => new ZoneDraft(Id, Name, color, FaceIds, AllowsDisjointParts);
    public ZoneDraft WithFaces(IEnumerable<int> faceIds) => new ZoneDraft(Id, Name, Color, faceIds, AllowsDisjointParts);

    public ZoneDraft WithDisjointAllowed(bool allowed) =>
        allowed == AllowsDisjointParts ? this : new ZoneDraft(Id, Name, Color, FaceIds, allowed);

    public ZoneDraft Including(int faceId) =>
        Contains(faceId) ? this : new ZoneDraft(Id, Name, Color, FaceIds.Concat(new[] { faceId }), AllowsDisjointParts);

    public ZoneDraft Excluding(int faceId) =>
        Contains(faceId) ? new ZoneDraft(Id, Name, Color, FaceIds.Where(x => x != faceId), AllowsDisjointParts) : this;

    /// <summary>
    /// A deterministic fingerprint of everything that reaches the model. Two drafts with the same
    /// signature produce the same Area, boundary lines and parameters, which is how Undo/Redo tells
    /// "back where we started" from "changed" and how the apply preview tells Update from Unchanged.
    /// </summary>
    public string Signature() => string.Format(
        CultureInfo.InvariantCulture,
        "{0:N}|{1}|{2}|{3}|{4}",
        Id,
        Name,
        Color.ToHex(),
        AllowsDisjointParts ? "1" : "0",
        string.Join(",", FaceIds));

    public static string NormalizeName(string name, string parameterName = "name")
    {
        if (name is null) throw new ArgumentNullException(parameterName);

        var trimmed = name.Trim();
        if (trimmed.Length == 0) throw new ArgumentException("A zone needs a name.", parameterName);
        if (trimmed.Length > MaximumNameLength)
            throw new ArgumentException($"A zone name cannot exceed {MaximumNameLength} characters.", parameterName);
        return trimmed;
    }

    public override string ToString() => $"{Name} ({FaceCount} faces, {Color})";
}
