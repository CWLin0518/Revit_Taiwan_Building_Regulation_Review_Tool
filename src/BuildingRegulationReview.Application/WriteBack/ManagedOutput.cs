using System;
using System.Globalization;

namespace BuildingRegulationReview.Application.WriteBack;

/// <summary>
/// The containers the tool creates once per package, as opposed to the per-區劃 elements inside
/// them (spec 10.5 items 3 and 4).
/// </summary>
public enum ManagedOutputKind
{
    /// <summary>The Drafting View holding the 單線圖 copies.</summary>
    DraftingView,

    /// <summary>The Area Color Scheme colouring the 區劃 on the Area Plan.</summary>
    ColorFillScheme,

    /// <summary>
    /// The dedicated review view the red Filled Regions and element overrides go in (spec 11.4 item 4,
    /// 11.5 item 6). It is a duplicate of the source floor plan, so marking it never touches a view the
    /// user draws in.
    /// </summary>
    ReviewView
}

/// <summary>
/// The ownership mark for a container. It is the same idea as <see cref="ManagedElementKey"/> but
/// without a 區劃: there is one Drafting View and one colour scheme per package, not one per zone.
/// </summary>
/// <remarks>
/// A container needs a mark for the same reason an element does. The Drafting View has the package
/// to point at it — <c>ReviewPackage.DraftingViewUniqueId</c> is the 唯一識別 spec 10.5 item 5 asks
/// for — but a colour scheme has nowhere to be recorded, and editing one this tool did not create
/// would be overwriting somebody's project settings. With the mark the tool can tell its own scheme
/// from the project's own and leave the latter alone.
/// </remarks>
public readonly struct ManagedOutputKey : IEquatable<ManagedOutputKey>
{
    /// <summary>
    /// Distinct from <see cref="ManagedElementKey.Prefix"/>, so a token is never read as the wrong
    /// shape — a container has no 區劃 and a zone element always has one.
    /// </summary>
    public const string Prefix = "BCROUT";

    private const char Separator = '/';

    public ManagedOutputKey(Guid packageId, ManagedOutputKind kind)
    {
        if (packageId == Guid.Empty) throw new ArgumentException("Package ID cannot be empty.", nameof(packageId));
        if (!Enum.IsDefined(typeof(ManagedOutputKind), kind)) throw new ArgumentOutOfRangeException(nameof(kind));

        PackageId = packageId;
        Kind = kind;
    }

    public Guid PackageId { get; }
    public ManagedOutputKind Kind { get; }

    public string ToToken() => string.Join(
        Separator.ToString(),
        Prefix,
        PackageId.ToString("N", CultureInfo.InvariantCulture),
        KindToken(Kind));

    public static bool TryParse(string? token, out ManagedOutputKey key)
    {
        key = default;
        if (string.IsNullOrWhiteSpace(token)) return false;

        var parts = token!.Trim().Split(Separator);
        if (parts.Length != 3) return false;
        if (!string.Equals(parts[0], Prefix, StringComparison.Ordinal)) return false;
        if (!Guid.TryParseExact(parts[1], "N", out var packageId) || packageId == Guid.Empty) return false;
        if (!TryParseKind(parts[2], out var kind)) return false;

        key = new ManagedOutputKey(packageId, kind);
        return true;
    }

    public bool Equals(ManagedOutputKey other) => PackageId == other.PackageId && Kind == other.Kind;
    public override bool Equals(object? obj) => obj is ManagedOutputKey other && Equals(other);

    public override int GetHashCode()
    {
        unchecked
        {
            return (PackageId.GetHashCode() * 31) + (int)Kind;
        }
    }

    public static bool operator ==(ManagedOutputKey left, ManagedOutputKey right) => left.Equals(right);
    public static bool operator !=(ManagedOutputKey left, ManagedOutputKey right) => !left.Equals(right);

    public override string ToString() => ToToken();

    public static string Describe(ManagedOutputKind kind) => kind switch
    {
        ManagedOutputKind.DraftingView => "單線圖視圖",
        ManagedOutputKind.ColorFillScheme => "面積色彩配置",
        ManagedOutputKind.ReviewView => "防火檢討視圖",
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    private static string KindToken(ManagedOutputKind kind) => kind switch
    {
        ManagedOutputKind.DraftingView => "draftingview",
        ManagedOutputKind.ColorFillScheme => "colorscheme",
        ManagedOutputKind.ReviewView => "reviewview",
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    private static bool TryParseKind(string token, out ManagedOutputKind kind)
    {
        switch (token)
        {
            case "draftingview": kind = ManagedOutputKind.DraftingView; return true;
            case "colorscheme": kind = ManagedOutputKind.ColorFillScheme; return true;
            case "reviewview": kind = ManagedOutputKind.ReviewView; return true;
            default: kind = default; return false;
        }
    }
}

/// <summary>
/// The one question every deletion and every update has to answer: is this thing ours? It reads
/// either token shape, so a caller does not have to know whether it is holding a 區劃 element or one
/// of the containers.
/// </summary>
public static class ManagedOwnership
{
    /// <summary>The package that wrote this token, or false when nothing readable is there.</summary>
    public static bool TryReadPackageId(string? token, out Guid packageId)
    {
        if (ManagedElementKey.TryParse(token, out var elementKey))
        {
            packageId = elementKey.PackageId;
            return true;
        }

        if (ManagedOutputKey.TryParse(token, out var outputKey))
        {
            packageId = outputKey.PackageId;
            return true;
        }

        if (Reviews.ReviewMarkKey.TryParse(token, out var markKey))
        {
            packageId = markKey.PackageId;
            return true;
        }

        packageId = Guid.Empty;
        return false;
    }

    /// <summary>Whether <paramref name="packageId"/> may touch whatever carries this token.</summary>
    public static bool BelongsTo(string? token, Guid packageId) =>
        packageId != Guid.Empty && TryReadPackageId(token, out var owner) && owner == packageId;
}
