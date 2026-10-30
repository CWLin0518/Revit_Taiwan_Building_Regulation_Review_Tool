using System;
using System.Globalization;

namespace BuildingRegulationReview.Application.WriteBack;

/// <summary>
/// The containers the tool creates once per package, as opposed to the per-區劃 elements inside
/// them (spec 10.5 items 3 and 4). One of them — the 帷幕牆檢討立面 — is made once per curtain wall
/// rather than once per package, which is why <see cref="ManagedOutputKey"/> can carry a subject.
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
    ReviewView,

    /// <summary>
    /// The elevation the 層間帶 of one curtain wall is drawn red in (帷幕牆規格 §7.1, CW-V). A 層間帶
    /// is a stretch of a façade, not a plan outline, so it needs a view looking at that façade; there
    /// is one per curtain wall, named by the wall's UniqueId in the key's subject.
    /// </summary>
    CurtainWallElevation
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
        : this(packageId, kind, null)
    {
    }

    public ManagedOutputKey(Guid packageId, ManagedOutputKind kind, string? subject)
    {
        if (packageId == Guid.Empty) throw new ArgumentException("Package ID cannot be empty.", nameof(packageId));
        if (!Enum.IsDefined(typeof(ManagedOutputKind), kind)) throw new ArgumentOutOfRangeException(nameof(kind));

        var trimmed = (subject ?? string.Empty).Trim();
        if (HasSubject(kind))
        {
            if (trimmed.Length == 0) throw new ArgumentException($"A {kind} output has to name what it is for.", nameof(subject));
            if (trimmed.IndexOf(Separator) >= 0) throw new ArgumentException($"A subject cannot contain '{Separator}'.", nameof(subject));
        }
        else if (trimmed.Length > 0)
        {
            throw new ArgumentException($"There is one {kind} per package, so it has no subject.", nameof(subject));
        }

        PackageId = packageId;
        Kind = kind;
        Subject = trimmed;
    }

    public Guid PackageId { get; }
    public ManagedOutputKind Kind { get; }

    /// <summary>
    /// What the container is for when there is more than one of its kind in a package — the curtain
    /// wall's UniqueId for a <see cref="ManagedOutputKind.CurtainWallElevation"/>, empty otherwise.
    /// </summary>
    public string Subject { get; }

    public string ToToken()
    {
        var common = string.Join(
            Separator.ToString(),
            Prefix,
            PackageId.ToString("N", CultureInfo.InvariantCulture),
            KindToken(Kind));

        return Subject.Length == 0 ? common : common + Separator + Subject;
    }

    public static bool TryParse(string? token, out ManagedOutputKey key)
    {
        key = default;
        if (string.IsNullOrWhiteSpace(token)) return false;

        var parts = token!.Trim().Split(Separator);
        if (parts.Length != 3 && parts.Length != 4) return false;
        if (!string.Equals(parts[0], Prefix, StringComparison.Ordinal)) return false;
        if (!Guid.TryParseExact(parts[1], "N", out var packageId) || packageId == Guid.Empty) return false;
        if (!TryParseKind(parts[2], out var kind)) return false;

        var subject = parts.Length == 4 ? parts[3] : null;
        if (HasSubject(kind) != (subject is not null && subject.Length > 0)) return false;

        key = new ManagedOutputKey(packageId, kind, subject);
        return true;
    }

    public bool Equals(ManagedOutputKey other) =>
        PackageId == other.PackageId && Kind == other.Kind &&
        string.Equals(Subject ?? string.Empty, other.Subject ?? string.Empty, StringComparison.Ordinal);

    public override bool Equals(object? obj) => obj is ManagedOutputKey other && Equals(other);

    public override int GetHashCode()
    {
        unchecked
        {
            var hash = (PackageId.GetHashCode() * 31) + (int)Kind;
            return (hash * 31) + (Subject ?? string.Empty).GetHashCode();
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
        ManagedOutputKind.CurtainWallElevation => "帷幕牆檢討立面",
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    /// <summary>Whether a package holds more than one of this kind, so the key needs a subject to tell them apart.</summary>
    public static bool HasSubject(ManagedOutputKind kind) => kind switch
    {
        ManagedOutputKind.DraftingView => false,
        ManagedOutputKind.ColorFillScheme => false,
        ManagedOutputKind.ReviewView => false,
        ManagedOutputKind.CurtainWallElevation => true,
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    private static string KindToken(ManagedOutputKind kind) => kind switch
    {
        ManagedOutputKind.DraftingView => "draftingview",
        ManagedOutputKind.ColorFillScheme => "colorscheme",
        ManagedOutputKind.ReviewView => "reviewview",
        ManagedOutputKind.CurtainWallElevation => "curtainwallelevation",
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    private static bool TryParseKind(string token, out ManagedOutputKind kind)
    {
        switch (token)
        {
            case "draftingview": kind = ManagedOutputKind.DraftingView; return true;
            case "colorscheme": kind = ManagedOutputKind.ColorFillScheme; return true;
            case "reviewview": kind = ManagedOutputKind.ReviewView; return true;
            case "curtainwallelevation": kind = ManagedOutputKind.CurtainWallElevation; return true;
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
