using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using BuildingRegulationReview.Domain.Geometry;
using BuildingRegulationReview.Domain.Regions;

namespace BuildingRegulationReview.Application.WriteBack;

/// <summary>The kinds of element the tool creates for a 區劃 draft (spec 10.4 and 10.5).</summary>
public enum ManagedElementKind
{
    /// <summary>An Area Boundary Line in the Area Plan.</summary>
    AreaBoundaryLine,

    /// <summary>An Area placed inside one closed part of a 區劃.</summary>
    Area,

    /// <summary>The tag annotating that Area.</summary>
    AreaTag,

    /// <summary>A Detail Curve copying the boundary into the Drafting View.</summary>
    DetailCurve,

    /// <summary>
    /// A Text Note in the Drafting View naming one closed part of a 區劃 and its net area, the
    /// 單線圖's counterpart of the Area tag.
    /// </summary>
    DetailLabel
}

/// <summary>
/// The ownership mark that makes write-back idempotent and deletion safe (spec 10.4: 刪除範圍只限
/// 相同 Package ID 的工具生成元素). Every element the tool creates carries this key as a token; an
/// element without a readable token, or with another package's ID, is somebody else's and is never
/// touched.
/// </summary>
/// <remarks>
/// The token is a flat string so one field can hold it, wherever the adapter chooses to keep it.
/// The preview reads it back to tell Add from Update from Delete; the write-back reads it again on
/// the element itself before deleting anything.
/// </remarks>
public readonly struct ManagedElementKey : IEquatable<ManagedElementKey>
{
    /// <summary>Marks a token as this tool's, and lets a foreign string be rejected cheaply.</summary>
    public const string Prefix = "BCR";

    /// <summary>
    /// The text parameter the adapter mirrors <see cref="ToToken"/> into, for a project that binds
    /// it and wants the key where a schedule can see it. The mark itself lives in the element's own
    /// storage, because Area Boundary Lines are not a category a project parameter can reach.
    /// </summary>
    public const string KeyParameterName = "BCR_ManagedKey";

    /// <summary>
    /// The matching mirror of the <see cref="PlannedElement.Signature"/> that was last written, which
    /// is what tells an element that still matches the draft from one that has to be updated.
    /// </summary>
    public const string SignatureParameterName = "BCR_ManagedSignature";

    private const char Separator = '/';

    public ManagedElementKey(Guid packageId, Guid zoneId, ManagedElementKind kind, int partIndex, int ordinal)
    {
        if (packageId == Guid.Empty) throw new ArgumentException("Package ID cannot be empty.", nameof(packageId));
        if (zoneId == Guid.Empty) throw new ArgumentException("Zone ID cannot be empty.", nameof(zoneId));
        if (!Enum.IsDefined(typeof(ManagedElementKind), kind)) throw new ArgumentOutOfRangeException(nameof(kind));
        if (partIndex < 0) throw new ArgumentOutOfRangeException(nameof(partIndex));
        if (ordinal < 0) throw new ArgumentOutOfRangeException(nameof(ordinal));

        PackageId = packageId;
        ZoneId = zoneId;
        Kind = kind;
        PartIndex = partIndex;
        Ordinal = ordinal;
    }

    public Guid PackageId { get; }
    public Guid ZoneId { get; }
    public ManagedElementKind Kind { get; }

    /// <summary>Which contiguous part of the 區劃 the element belongs to.</summary>
    public int PartIndex { get; }

    /// <summary>Position within that part, in the canonical order the plan builder uses.</summary>
    public int Ordinal { get; }

    public string ToToken() => string.Join(
        Separator.ToString(),
        Prefix,
        PackageId.ToString("N", CultureInfo.InvariantCulture),
        ZoneId.ToString("N", CultureInfo.InvariantCulture),
        KindToken(Kind),
        PartIndex.ToString(CultureInfo.InvariantCulture),
        Ordinal.ToString(CultureInfo.InvariantCulture));

    public static bool TryParse(string? token, out ManagedElementKey key)
    {
        key = default;
        if (string.IsNullOrWhiteSpace(token)) return false;

        var parts = token!.Trim().Split(Separator);
        if (parts.Length != 6) return false;
        if (!string.Equals(parts[0], Prefix, StringComparison.Ordinal)) return false;
        if (!Guid.TryParseExact(parts[1], "N", out var packageId) || packageId == Guid.Empty) return false;
        if (!Guid.TryParseExact(parts[2], "N", out var zoneId) || zoneId == Guid.Empty) return false;
        if (!TryParseKind(parts[3], out var kind)) return false;
        if (!int.TryParse(parts[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out var partIndex) || partIndex < 0) return false;
        if (!int.TryParse(parts[5], NumberStyles.Integer, CultureInfo.InvariantCulture, out var ordinal) || ordinal < 0) return false;

        key = new ManagedElementKey(packageId, zoneId, kind, partIndex, ordinal);
        return true;
    }

    public bool Equals(ManagedElementKey other) =>
        PackageId == other.PackageId &&
        ZoneId == other.ZoneId &&
        Kind == other.Kind &&
        PartIndex == other.PartIndex &&
        Ordinal == other.Ordinal;

    public override bool Equals(object? obj) => obj is ManagedElementKey other && Equals(other);

    public override int GetHashCode()
    {
        unchecked
        {
            var hash = 17;
            hash = (hash * 31) + PackageId.GetHashCode();
            hash = (hash * 31) + ZoneId.GetHashCode();
            hash = (hash * 31) + (int)Kind;
            hash = (hash * 31) + PartIndex;
            hash = (hash * 31) + Ordinal;
            return hash;
        }
    }

    public static bool operator ==(ManagedElementKey left, ManagedElementKey right) => left.Equals(right);
    public static bool operator !=(ManagedElementKey left, ManagedElementKey right) => !left.Equals(right);

    public override string ToString() => ToToken();

    /// <summary>The Chinese name of a kind, used in every preview line.</summary>
    public static string Describe(ManagedElementKind kind) => kind switch
    {
        ManagedElementKind.AreaBoundaryLine => "面積邊界線",
        ManagedElementKind.Area => "面積",
        ManagedElementKind.AreaTag => "面積標註",
        ManagedElementKind.DetailCurve => "單線圖細部線",
        ManagedElementKind.DetailLabel => "單線圖面積標註",
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    private static string KindToken(ManagedElementKind kind) => kind switch
    {
        ManagedElementKind.AreaBoundaryLine => "boundary",
        ManagedElementKind.Area => "area",
        ManagedElementKind.AreaTag => "tag",
        ManagedElementKind.DetailCurve => "detail",
        ManagedElementKind.DetailLabel => "label",
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    private static bool TryParseKind(string token, out ManagedElementKind kind)
    {
        switch (token)
        {
            case "boundary": kind = ManagedElementKind.AreaBoundaryLine; return true;
            case "area": kind = ManagedElementKind.Area; return true;
            case "tag": kind = ManagedElementKind.AreaTag; return true;
            case "detail": kind = ManagedElementKind.DetailCurve; return true;
            case "label": kind = ManagedElementKind.DetailLabel; return true;
            default: kind = default; return false;
        }
    }
}

/// <summary>
/// One element the current drafts say should exist, already carrying everything P2-T07 needs to
/// create it and everything the preview needs to compare it with what is in the model.
/// </summary>
public sealed class PlannedElement
{
    public PlannedElement(
        ManagedElementKey key,
        string signature,
        string description,
        IEnumerable<Point2D>? points = null,
        Point2D? placement = null,
        string? zoneName = null,
        ZoneColor? color = null,
        double netAreaSquareMeters = 0.0,
        string? text = null)
    {
        if (string.IsNullOrWhiteSpace(signature)) throw new ArgumentException("A planned element needs a signature.", nameof(signature));
        if (string.IsNullOrWhiteSpace(description)) throw new ArgumentException("A planned element needs a description.", nameof(description));

        Key = key;
        Signature = signature.Trim();
        Description = description.Trim();
        Points = new ReadOnlyCollection<Point2D>((points ?? Array.Empty<Point2D>()).ToList());
        Placement = placement;
        ZoneName = zoneName;
        Color = color;
        NetAreaSquareMeters = netAreaSquareMeters;
        Text = text;
    }

    public ManagedElementKey Key { get; }

    /// <summary>Everything that reaches the model. Same signature, nothing to update.</summary>
    public string Signature { get; }

    public string Description { get; }

    /// <summary>The line to draw, for a boundary line or detail curve; empty otherwise.</summary>
    public IReadOnlyList<Point2D> Points { get; }

    /// <summary>Where an Area, its tag or its 單線圖 label is placed; null for a line.</summary>
    public Point2D? Placement { get; }

    public string? ZoneName { get; }
    public ZoneColor? Color { get; }
    public double NetAreaSquareMeters { get; }

    /// <summary>What a 單線圖 area label shows; null for every other kind.</summary>
    public string? Text { get; }

    public override string ToString() => Description;
}

/// <summary>
/// An element already in the model that carries this tool's ownership token. The Revit adapter reads
/// these; the preview never sees an element the tool did not create.
/// </summary>
public sealed class ExistingManagedElement
{
    public ExistingManagedElement(string elementUniqueId, string keyToken, string signature, string? description = null)
    {
        if (string.IsNullOrWhiteSpace(elementUniqueId)) throw new ArgumentException("An element UniqueId is required.", nameof(elementUniqueId));
        if (string.IsNullOrWhiteSpace(keyToken)) throw new ArgumentException("A managed element needs its key token.", nameof(keyToken));

        ElementUniqueId = elementUniqueId.Trim();
        KeyToken = keyToken.Trim();
        Signature = (signature ?? string.Empty).Trim();
        HasKey = ManagedElementKey.TryParse(KeyToken, out var key);
        Key = key;
        Description = string.IsNullOrWhiteSpace(description)
            ? (HasKey ? ManagedElementKey.Describe(key.Kind) : "無法辨識的工具元素")
            : description!.Trim();
    }

    public string ElementUniqueId { get; }
    public string KeyToken { get; }

    /// <summary>What was written last time, for telling Update from Unchanged.</summary>
    public string Signature { get; }

    public string Description { get; }

    /// <summary>False when the token does not parse, which makes the element untouchable.</summary>
    public bool HasKey { get; }

    public ManagedElementKey Key { get; }

    public bool BelongsTo(Guid packageId) => HasKey && Key.PackageId == packageId;

    public override string ToString() => $"{Description} [{KeyToken}]";
}
