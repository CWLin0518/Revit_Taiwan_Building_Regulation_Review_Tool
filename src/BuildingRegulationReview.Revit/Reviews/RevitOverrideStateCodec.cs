using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.Revit.DB;

namespace BuildingRegulationReview.Revit.Reviews;

/// <summary>
/// Turns an element's view override into a text snapshot and back (spec 11.5 item 6: 保存原視圖狀態).
/// The snapshot is what <c>RecordedElementOverride</c> keeps as the original and the applied state;
/// comparing two snapshots is how the tool knows whether the user has changed an element since.
/// </summary>
/// <remarks>
/// Every property <see cref="OverrideGraphicSettings"/> exposes is written, in a fixed order, so the
/// same override always gives the same text. Line and fill patterns are written by UniqueId rather
/// than ElementId, which is what a saved model keeps stable. A pattern that has since been deleted
/// restores as "no override" for that property rather than failing the whole restore.
/// </remarks>
internal static class RevitOverrideStateCodec
{
    private const string Version = "ogs1";
    private const string None = "-";

    public static string Write(Document document, OverrideGraphicSettings settings)
    {
        if (document is null) throw new ArgumentNullException(nameof(document));
        if (settings is null) throw new ArgumentNullException(nameof(settings));

        var fields = new List<string>
        {
            Version,
            Pair("pl.c", Color(settings.ProjectionLineColor)),
            Pair("pl.p", Id(document, settings.ProjectionLinePatternId)),
            Pair("pl.w", Number(settings.ProjectionLineWeight)),
            Pair("cl.c", Color(settings.CutLineColor)),
            Pair("cl.p", Id(document, settings.CutLinePatternId)),
            Pair("cl.w", Number(settings.CutLineWeight)),
            Pair("sf.c", Color(settings.SurfaceForegroundPatternColor)),
            Pair("sf.p", Id(document, settings.SurfaceForegroundPatternId)),
            Pair("sf.v", Flag(settings.IsSurfaceForegroundPatternVisible)),
            Pair("sb.c", Color(settings.SurfaceBackgroundPatternColor)),
            Pair("sb.p", Id(document, settings.SurfaceBackgroundPatternId)),
            Pair("sb.v", Flag(settings.IsSurfaceBackgroundPatternVisible)),
            Pair("cf.c", Color(settings.CutForegroundPatternColor)),
            Pair("cf.p", Id(document, settings.CutForegroundPatternId)),
            Pair("cf.v", Flag(settings.IsCutForegroundPatternVisible)),
            Pair("cb.c", Color(settings.CutBackgroundPatternColor)),
            Pair("cb.p", Id(document, settings.CutBackgroundPatternId)),
            Pair("cb.v", Flag(settings.IsCutBackgroundPatternVisible)),
            Pair("tr", Number(settings.Transparency)),
            Pair("ht", Flag(settings.Halftone)),
            Pair("dl", ((int)settings.DetailLevel).ToString(CultureInfo.InvariantCulture))
        };
        return string.Join(";", fields);
    }

    /// <summary>The override a snapshot describes; an empty or unreadable snapshot is "no override at all".</summary>
    public static OverrideGraphicSettings Read(Document document, string? snapshot)
    {
        if (document is null) throw new ArgumentNullException(nameof(document));

        var settings = new OverrideGraphicSettings();
        if (string.IsNullOrWhiteSpace(snapshot)) return settings;

        var parts = snapshot!.Split(';');
        if (parts.Length == 0 || !string.Equals(parts[0], Version, StringComparison.Ordinal)) return settings;

        var values = parts.Skip(1)
            .Select(p => p.Split(new[] { '=' }, 2))
            .Where(p => p.Length == 2)
            .GroupBy(p => p[0], StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First()[1], StringComparer.Ordinal);

        if (TryColor(values, "pl.c", out var color)) settings.SetProjectionLineColor(color);
        if (TryId(document, values, "pl.p", out var id)) settings.SetProjectionLinePatternId(id);
        if (TryNumber(values, "pl.w", out var number)) settings.SetProjectionLineWeight(number);
        if (TryColor(values, "cl.c", out color)) settings.SetCutLineColor(color);
        if (TryId(document, values, "cl.p", out id)) settings.SetCutLinePatternId(id);
        if (TryNumber(values, "cl.w", out number)) settings.SetCutLineWeight(number);
        if (TryColor(values, "sf.c", out color)) settings.SetSurfaceForegroundPatternColor(color);
        if (TryId(document, values, "sf.p", out id)) settings.SetSurfaceForegroundPatternId(id);
        if (TryFlag(values, "sf.v", out var flag)) settings.SetSurfaceForegroundPatternVisible(flag);
        if (TryColor(values, "sb.c", out color)) settings.SetSurfaceBackgroundPatternColor(color);
        if (TryId(document, values, "sb.p", out id)) settings.SetSurfaceBackgroundPatternId(id);
        if (TryFlag(values, "sb.v", out flag)) settings.SetSurfaceBackgroundPatternVisible(flag);
        if (TryColor(values, "cf.c", out color)) settings.SetCutForegroundPatternColor(color);
        if (TryId(document, values, "cf.p", out id)) settings.SetCutForegroundPatternId(id);
        if (TryFlag(values, "cf.v", out flag)) settings.SetCutForegroundPatternVisible(flag);
        if (TryColor(values, "cb.c", out color)) settings.SetCutBackgroundPatternColor(color);
        if (TryId(document, values, "cb.p", out id)) settings.SetCutBackgroundPatternId(id);
        if (TryFlag(values, "cb.v", out flag)) settings.SetCutBackgroundPatternVisible(flag);
        if (TryNumber(values, "tr", out number) && number >= 0 && number <= 100) settings.SetSurfaceTransparency(number);
        if (TryFlag(values, "ht", out flag)) settings.SetHalftone(flag);
        if (TryNumber(values, "dl", out number) && Enum.IsDefined(typeof(ViewDetailLevel), number))
            settings.SetDetailLevel((ViewDetailLevel)number);
        return settings;
    }

    private static string Pair(string key, string value) => key + "=" + value;

    private static string Color(Color color) => color is not null && color.IsValid
        ? string.Join(",", color.Red.ToString(CultureInfo.InvariantCulture), color.Green.ToString(CultureInfo.InvariantCulture), color.Blue.ToString(CultureInfo.InvariantCulture))
        : None;

    private static string Id(Document document, ElementId id)
    {
        if (id is null || id == ElementId.InvalidElementId) return None;
        return document.GetElement(id)?.UniqueId ?? None;
    }

    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Flag(bool value) => value ? "1" : "0";

    private static bool TryColor(IReadOnlyDictionary<string, string> values, string key, out Color color)
    {
        color = Autodesk.Revit.DB.Color.InvalidColorValue;
        if (!values.TryGetValue(key, out var text) || text == None) return false;

        var rgb = text.Split(',');
        if (rgb.Length != 3) return false;
        if (!byte.TryParse(rgb[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var r)) return false;
        if (!byte.TryParse(rgb[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var g)) return false;
        if (!byte.TryParse(rgb[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var b)) return false;
        color = new Color(r, g, b);
        return true;
    }

    private static bool TryId(Document document, IReadOnlyDictionary<string, string> values, string key, out ElementId id)
    {
        id = ElementId.InvalidElementId;
        if (!values.TryGetValue(key, out var text) || text == None) return false;

        var element = document.GetElement(text);
        if (element is null) return false;
        id = element.Id;
        return true;
    }

    private static bool TryNumber(IReadOnlyDictionary<string, string> values, string key, out int number)
    {
        number = 0;
        return values.TryGetValue(key, out var text) && int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out number);
    }

    private static bool TryFlag(IReadOnlyDictionary<string, string> values, string key, out bool flag)
    {
        flag = false;
        if (!values.TryGetValue(key, out var text)) return false;
        if (text == "1") { flag = true; return true; }
        return text == "0";
    }
}
