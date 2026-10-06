using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;

namespace BuildingRegulationReview.Domain.Regions;

/// <summary>
/// One 防火區劃 draft: a name, a colour, a 區劃用途 and the solved faces the user has put into it
/// (spec 10.3).
/// Immutable, so the Editor can keep past states for Undo/Redo in P2-T06 without copying defensively.
/// </summary>
public sealed class ZoneDraft
{
    /// <summary>Long enough for a descriptive Chinese name, short enough to survive an Area parameter.</summary>
    public const int MaximumNameLength = 120;

    public ZoneDraft(
        Guid id,
        string name,
        ZoneColor color,
        IEnumerable<int>? faceIds = null,
        bool allowsDisjointParts = false,
        string? use = null)
    {
        if (id == Guid.Empty) throw new ArgumentException("Zone ID cannot be empty.", nameof(id));

        Id = id;
        Name = NormalizeName(name, nameof(name));
        Color = color;
        AllowsDisjointParts = allowsDisjointParts;
        Use = NormalizeUse(use, nameof(use));

        var faces = (faceIds ?? Array.Empty<int>()).Distinct().OrderBy(x => x).ToList();
        if (faces.Any(x => x < 0)) throw new ArgumentOutOfRangeException(nameof(faceIds), "A face ID cannot be negative.");
        FaceIds = new ReadOnlyCollection<int>(faces);
    }

    public Guid Id { get; }
    public string Name { get; }
    public ZoneColor Color { get; }

    /// <summary>
    /// 防火檢討_區劃用途 as this draft would write it — three states, not two.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>null</c> is 不變更: either the Areas behind this zone disagree about their use, or the
    /// parameter could not be read at all, or the user simply has not touched the field. Empty is the
    /// opposite — the user asking for a 一般區劃, which clears the parameter. A non-empty string is
    /// that use.
    /// </para>
    /// <para>
    /// The distinction is what stops a reopened Editor destroying data: a zone whose two parts were
    /// given different uses by hand reads back as <c>null</c> and the next 套用 leaves both alone,
    /// where a single blank string would have cleared them and taken their 第79條之2 results with it.
    /// Free text on purpose — the vocabulary that changes an answer is closed (<c>ZoneUses</c> in the
    /// Application layer) but the field is read for evidence as much as for a decision, so the Domain
    /// stores whatever the user settled on and leaves the comparing to the rules.
    /// </para>
    /// </remarks>
    public string? Use { get; }

    /// <summary>True when this draft has something to say about 區劃用途 at all.</summary>
    public bool HasUse => Use is not null;

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

    public ZoneDraft WithName(string name) => new ZoneDraft(Id, name, Color, FaceIds, AllowsDisjointParts, Use);
    public ZoneDraft WithColor(ZoneColor color) => new ZoneDraft(Id, Name, color, FaceIds, AllowsDisjointParts, Use);
    public ZoneDraft WithFaces(IEnumerable<int> faceIds) => new ZoneDraft(Id, Name, Color, faceIds, AllowsDisjointParts, Use);

    /// <summary>
    /// The same zone under another 區劃用途. Empty is the explicit 一般區劃 that clears the parameter;
    /// null is 不變更, which is how a mixed or unreadable zone is put back untouched.
    /// </summary>
    public ZoneDraft WithUse(string? use) =>
        string.Equals(NormalizeUse(use, nameof(use)), Use, StringComparison.Ordinal)
            ? this
            : new ZoneDraft(Id, Name, Color, FaceIds, AllowsDisjointParts, use);

    public ZoneDraft WithDisjointAllowed(bool allowed) =>
        allowed == AllowsDisjointParts ? this : new ZoneDraft(Id, Name, Color, FaceIds, allowed, Use);

    public ZoneDraft Including(int faceId) =>
        Contains(faceId) ? this : new ZoneDraft(Id, Name, Color, FaceIds.Concat(new[] { faceId }), AllowsDisjointParts, Use);

    public ZoneDraft Excluding(int faceId) =>
        Contains(faceId) ? new ZoneDraft(Id, Name, Color, FaceIds.Where(x => x != faceId), AllowsDisjointParts, Use) : this;

    /// <summary>
    /// A deterministic fingerprint of everything that reaches the model. Two drafts with the same
    /// signature produce the same Area, boundary lines and parameters, which is how Undo/Redo tells
    /// "back where we started" from "changed" and how the apply preview tells Update from Unchanged.
    /// </summary>
    public string Signature() => string.Format(
        CultureInfo.InvariantCulture,
        "{0:N}|{1}|{2}|{3}|{4}|{5}{6}",
        Id,
        Name,
        Color.ToHex(),
        AllowsDisjointParts ? "1" : "0",
        string.Join(",", FaceIds),
        // The flag keeps 不變更 apart from 清除成一般區劃: both would otherwise end the signature
        // the same way, and Undo would not notice the user clearing a use.
        HasUse ? "=" : "~",
        Use);

    public static string NormalizeName(string name, string parameterName = "name")
    {
        if (name is null) throw new ArgumentNullException(parameterName);

        var trimmed = name.Trim();
        if (trimmed.Length == 0) throw new ArgumentException("A zone needs a name.", parameterName);
        if (trimmed.Length > MaximumNameLength)
            throw new ArgumentException($"A zone name cannot exceed {MaximumNameLength} characters.", parameterName);
        return trimmed;
    }

    /// <summary>
    /// Trims the 區劃用途, keeping null as null. Null and empty are different answers here — see
    /// <see cref="Use"/> — so this must not fold one into the other. It shares the name's length
    /// limit, because it reaches the same Area parameter.
    /// </summary>
    public static string? NormalizeUse(string? use, string parameterName = "use")
    {
        if (use is null) return null;

        var trimmed = use.Trim();
        if (trimmed.Length > MaximumNameLength)
            throw new ArgumentException($"A zone use cannot exceed {MaximumNameLength} characters.", parameterName);
        return trimmed;
    }

    public override string ToString() => $"{Name} ({FaceCount} faces, {Color})";
}
