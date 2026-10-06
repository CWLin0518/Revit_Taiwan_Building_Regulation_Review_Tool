using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using BuildingRegulationReview.Application.RegionEditing;
using BuildingRegulationReview.Application.Reviews;
using BuildingRegulationReview.Application.WriteBack;
using BuildingRegulationReview.Domain.Geometry;
using BuildingRegulationReview.Domain.Regions;
using BuildingRegulationReview.Revit.Geometry;
using RevitArea = Autodesk.Revit.DB.Area;

namespace BuildingRegulationReview.Revit.WriteBack;

/// <summary>
/// Reads the Areas this package already wrote into its Area Plan, so the Editor can reopen on the
/// zones they describe (<see cref="WrittenZoneRestorer"/>). Read-only: no transaction, no change.
/// </summary>
/// <remarks>
/// Only an Area carrying this package's ownership mark counts. The zone ID comes from the mark and
/// the name and colour from the signature written beside it, not from the Area's own parameters: a
/// user may have retyped those, but the signature is what the tool wrote and what the next
/// write-back compares against.
/// <para>
/// 防火檢討_區劃用途 is the exception, and is read from the Area itself — see
/// <see cref="WrittenZoneArea.Use"/> for why. In short, the 批次設定面板 owns that field too, and a
/// draft that did not know what it already said would write a blank over it on the next 套用.
/// </para>
/// </remarks>
public sealed class RevitWrittenZoneReader
{
    private readonly Document _document;

    public RevitWrittenZoneReader(Document document) =>
        _document = document ?? throw new ArgumentNullException(nameof(document));

    public IReadOnlyList<WrittenZoneArea> Read(Guid packageId, string areaPlanUniqueId)
    {
        var found = new List<WrittenZoneArea>();
        if (!(_document.GetElement(areaPlanUniqueId) is View view)) return found;

        var options = new SpatialElementBoundaryOptions();
        foreach (var area in new FilteredElementCollector(_document, view.Id)
                     .OfCategory(BuiltInCategory.OST_Areas)
                     .WhereElementIsNotElementType()
                     .OfType<RevitArea>())
        {
            if (!ManagedElementMark.TryRead(area, out var token, out var signature)) continue;
            if (!ManagedElementKey.TryParse(token, out var key)) continue;
            if (key.PackageId != packageId || key.Kind != ManagedElementKind.Area) continue;
            if (!PlannedElementSignature.TryReadArea(signature, out var name, out var hex)) continue;

            ZoneColor color;
            try
            {
                color = ZoneColor.FromHex(hex);
            }
            catch (FormatException)
            {
                color = ZoneColorPalette.At(found.Count);
            }

            found.Add(new WrittenZoneArea(
                key.ZoneId,
                name,
                color,
                ReadLoops(area, options),
                area.Location is LocationPoint location ? RevitPlanShapeReader.ToPlan(location.Point) : (Point2D?)null,
                ZoneUseOf(area)));
        }

        return found;
    }

    /// <summary>
    /// 防火檢討_區劃用途 as the Area carries it, or null when the parameter is not bound to Areas or
    /// does not hold text. Null is 不變更, never 空白: an unbound parameter must not come back looking
    /// like an empty one, or the next 套用 would write that emptiness onto every Area in the package.
    /// </summary>
    private static string? ZoneUseOf(RevitArea area)
    {
        var parameter = area.LookupParameter(ReviewInputSources.ZoneUse);
        if (parameter is null || parameter.StorageType != StorageType.String) return null;
        return parameter.AsString() ?? string.Empty;
    }

    internal static List<IReadOnlyList<Point2D>> ReadLoops(RevitArea area, SpatialElementBoundaryOptions options)
    {
        var rings = new List<IReadOnlyList<Point2D>>();
        IList<IList<BoundarySegment>>? loops;
        try
        {
            loops = area.GetBoundarySegments(options);
        }
        catch (Autodesk.Revit.Exceptions.ApplicationException)
        {
            // An Area Revit cannot bound is treated like an unenclosed one: its placement decides.
            return rings;
        }

        if (loops is null) return rings;

        foreach (var loop in loops)
        {
            var points = new List<Point2D>();
            foreach (var segment in loop)
            {
                var flattened = RevitPlanShapeReader.Flatten(segment.GetCurve());
                // The loop is continuous, so each curve contributes everything but its closing point.
                for (var i = 0; i < flattened.Count - 1; i++) points.Add(flattened[i]);
            }

            if (points.Count >= 3) rings.Add(points);
        }

        return rings;
    }
}
