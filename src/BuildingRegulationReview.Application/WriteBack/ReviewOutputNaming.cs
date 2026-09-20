using System;
using System.Globalization;
using System.Linq;

namespace BuildingRegulationReview.Application.WriteBack;

/// <summary>
/// The default name of the views and schemes this tool creates (spec 10.5 item 5:
/// <c>{AreaScheme}_{SourceFloorPlan}_防火區劃</c>).
/// </summary>
/// <remarks>
/// The name is only ever a starting point. What ties a package to its Drafting View is the
/// UniqueId kept on the package, which is why the same spec clause asks for a 唯一識別: a user who
/// renames the view keeps it, and a re-run finds it by identity and leaves the new name alone.
/// Nothing here reads or writes a document, so the sanitising rule and the uniquing rule are both
/// testable without Revit.
/// </remarks>
public static class ReviewOutputNaming
{
    /// <summary>The trailing part of every generated name, so the outputs sort together.</summary>
    public const string Suffix = "防火區劃";

    private const char Separator = '_';
    private const string Unnamed = "未命名";

    /// <summary>
    /// Characters Revit refuses in a view or scheme name. A name is assembled from whatever the
    /// project called its Area Scheme and its floor plan, so it can contain anything at all; a
    /// prohibited character is replaced rather than left to make the creation fail.
    /// </summary>
    private static readonly char[] Prohibited = { '\\', ':', '{', '}', '[', ']', '|', ';', '<', '>', '?', '`', '~' };

    /// <summary>Builds <c>{AreaScheme}_{SourceFloorPlan}_防火區劃</c>.</summary>
    public static string Default(string? areaSchemeName, string? sourceFloorPlanName) => string.Join(
        Separator.ToString(),
        Clean(areaSchemeName),
        Clean(sourceFloorPlanName),
        Suffix);

    /// <summary>
    /// The first name in the <c>name</c>, <c>name (2)</c>, <c>name (3)</c> series that
    /// <paramref name="isTaken"/> does not reject. Used only when creating something new: an
    /// existing output is found by its UniqueId and keeps whatever it is called now.
    /// </summary>
    public static string MakeUnique(string baseName, Func<string, bool> isTaken)
    {
        if (string.IsNullOrWhiteSpace(baseName)) throw new ArgumentException("A base name is required.", nameof(baseName));
        if (isTaken is null) throw new ArgumentNullException(nameof(isTaken));

        var trimmed = baseName.Trim();
        if (!isTaken(trimmed)) return trimmed;

        for (var suffix = 2; suffix < 1000; suffix++)
        {
            var candidate = string.Format(CultureInfo.InvariantCulture, "{0} ({1})", trimmed, suffix);
            if (!isTaken(candidate)) return candidate;
        }

        // A thousand views of the same name is not a naming problem any more, so the caller gets a
        // name that cannot collide instead of a loop that never ends.
        return string.Format(CultureInfo.InvariantCulture, "{0} ({1:N})", trimmed, Guid.NewGuid());
    }

    /// <summary>
    /// One part of a name: trimmed, with prohibited characters and the separator itself replaced, so
    /// a floor plan called "1F|東棟" cannot break the name into pieces that no longer parse by eye.
    /// </summary>
    private static string Clean(string? part)
    {
        if (string.IsNullOrWhiteSpace(part)) return Unnamed;

        var cleaned = new string(part!.Trim()
            .Select(c => Prohibited.Contains(c) || c == Separator ? '-' : c)
            .ToArray())
            .Trim();

        return cleaned.Length == 0 ? Unnamed : cleaned;
    }
}
