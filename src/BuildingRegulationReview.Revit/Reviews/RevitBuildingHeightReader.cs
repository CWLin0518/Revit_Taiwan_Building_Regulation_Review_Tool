using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.Revit.DB;
using BuildingRegulationReview.Application.Reviews;

namespace BuildingRegulationReview.Revit.Reviews;

/// <summary>
/// Measures 建築物高度 from the model instead of reading it from a Project Information parameter:
/// the model's own extent is the height, so there is nothing to type in and nothing to keep in sync.
/// Read-only: no transaction.
/// </summary>
/// <remarks>
/// 建築技術規則建築設計施工編第1條第9款 measures from 基地地面 to the building's highest point. Revit
/// has no 基地地面, so the datum used here is the lowest Level in the document and the top is the
/// highest point of the building fabric — walls, floors, roofs, columns and structural framing.
/// Topography and linked models are left out: they are the site and its neighbours, not this
/// building. The datum is named in <see cref="ReviewModelFacts.BuildingHeightSource"/> so a project
/// whose 基地地面 is not the lowest Level can see what was measured rather than trust a bare number.
/// </remarks>
public sealed class RevitBuildingHeightReader
{
    private static readonly BuiltInCategory[] Fabric =
    {
        BuiltInCategory.OST_Walls,
        BuiltInCategory.OST_Floors,
        BuiltInCategory.OST_Roofs,
        BuiltInCategory.OST_Columns,
        BuiltInCategory.OST_StructuralColumns,
        BuiltInCategory.OST_StructuralFraming,
        BuiltInCategory.OST_Stairs
    };

    private readonly Document _document;

    public RevitBuildingHeightReader(Document document) =>
        _document = document ?? throw new ArgumentNullException(nameof(document));

    /// <summary>The measured height, or <see cref="ReviewModelFacts.None"/> when the model shows nothing to measure.</summary>
    public ReviewModelFacts Read()
    {
        var baseElevation = LowestLevelElevation();
        var top = HighestFabricPoint();

        if (baseElevation is not double datum || top is not double highest) return ReviewModelFacts.None;

        var feet = highest - datum;
        if (feet <= 0) return ReviewModelFacts.None;

        var meters = UnitUtils.ConvertFromInternalUnits(feet, UnitTypeId.Meters);
        var datumMeters = UnitUtils.ConvertFromInternalUnits(datum, UnitTypeId.Meters);

        return new ReviewModelFacts(meters,
            $"模型量測：最高構件頂端至最低樓層（高程 {datumMeters.ToString("0.###", CultureInfo.InvariantCulture)} m）");
    }

    private double? LowestLevelElevation()
    {
        var levels = new FilteredElementCollector(_document)
            .OfClass(typeof(Level))
            .Cast<Level>()
            .Select(level => level.Elevation)
            .ToList();

        return levels.Count > 0 ? levels.Min() : (double?)null;
    }

    private double? HighestFabricPoint()
    {
        double? top = null;
        foreach (var category in Fabric)
        {
            foreach (var element in Collect(category))
            {
                var box = BoundingBox(element);
                if (box is null) continue;
                if (top is null || box.Max.Z > top) top = box.Max.Z;
            }
        }

        return top;
    }

    private IEnumerable<Element> Collect(BuiltInCategory category) =>
        new FilteredElementCollector(_document)
            .OfCategory(category)
            .WhereElementIsNotElementType()
            .ToElements();

    private static BoundingBoxXYZ? BoundingBox(Element element)
    {
        try
        {
            return element.get_BoundingBox(null);
        }
        catch (Autodesk.Revit.Exceptions.ApplicationException)
        {
            return null;
        }
    }
}
