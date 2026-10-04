using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using BuildingRegulationReview.Application.Candidates;
using BuildingRegulationReview.Application.Reviews;
using BuildingRegulationReview.Application.WriteBack;
using BuildingRegulationReview.Revit.WriteBack;
using RevitArea = Autodesk.Revit.DB.Area;

namespace BuildingRegulationReview.Revit.Candidates;

/// <summary>
/// Reads the 防火區劃 of every storey of a package's Area Scheme, so a 挑空 can be traced through
/// the storeys it opens (垂直區劃規格 §3.8、決議 35). Read-only: no transaction, no change.
/// </summary>
/// <remarks>
/// A 區劃 here is what the tool wrote — Areas carrying an ownership mark, grouped by package and
/// zone — on any level, as long as they belong to the same Area Scheme as the package under
/// review: a scheme is one way of cutting the building into 區劃, and two schemes must not mix.
/// The 用途 and 所在樓層序 come from the same Area parameters the review reads for its own storey;
/// Areas of one 區劃 that disagree leave the fact blank rather than picking one. A mark found on more
/// than one level — an Area copied to another storey carries its ownership mark with it — is listed
/// once per level, each with the problem, so no 挑空 is traced through it silently.
/// </remarks>
public sealed class RevitStoreyZoneReader
{
    private readonly Document _document;

    public RevitStoreyZoneReader(Document document) =>
        _document = document ?? throw new ArgumentNullException(nameof(document));

    /// <summary>The map, or the reason it cannot be read.</summary>
    public (StoreyZoneMap? Map, string? Problem) Read(string? areaPlanUniqueId)
    {
        if (string.IsNullOrWhiteSpace(areaPlanUniqueId) || !(_document.GetElement(areaPlanUniqueId) is ViewPlan view))
            return (null, "找不到此工作包的 Area Plan，無法讀取其他樓層的區劃");
        var scheme = view.AreaScheme;
        if (scheme is null) return (null, "此工作包的 Area Plan 沒有 Area Scheme，無法讀取其他樓層的區劃");

        var options = new SpatialElementBoundaryOptions();
        var parts = new List<Part>();
        foreach (var area in new FilteredElementCollector(_document)
                     .OfCategory(BuiltInCategory.OST_Areas)
                     .WhereElementIsNotElementType()
                     .OfType<RevitArea>())
        {
            if (area.AreaScheme?.Id != scheme.Id || area.Level is null) continue;
            if (!ManagedElementMark.TryRead(area, out var token, out var signature)) continue;
            if (!ManagedElementKey.TryParse(token, out var key) || key.Kind != ManagedElementKind.Area) continue;
            PlannedElementSignature.TryReadArea(signature, out var name, out _);

            parts.Add(new Part(key.PackageId, key.ZoneId, area, name, RevitWrittenZoneReader.ReadLoops(area, options)));
        }

        var zones = new List<StoreyZone>();
        foreach (var zone in parts.GroupBy(p => (p.PackageId, p.ZoneId)))
        {
            var levels = zone.GroupBy(p => p.Area.Level!.UniqueId, StringComparer.Ordinal).ToList();
            var problem = levels.Count > 1
                ? $"區劃「{zone.Select(p => p.Name).FirstOrDefault(n => !string.IsNullOrWhiteSpace(n)) ?? "（未命名區劃）"}」同時出現在 " +
                  string.Join("、", levels.Select(l => l.First().Area.Level!.Name)) + "，可能是複製到其他樓層的 Area；請刪除複製品或重新建立區劃範圍"
                : null;

            foreach (var onLevel in levels)
            {
                var level = onLevel.First().Area.Level!;
                var areas = onLevel.Select(p => p.Area.Area).ToList();
                zones.Add(new StoreyZone(
                    zone.Key.PackageId,
                    zone.Key.ZoneId,
                    level.UniqueId,
                    // ProjectElevation, not Elevation: the latter is measured from whatever base the
                    // level type uses, and two types in one project would sort the storeys wrongly.
                    level.ProjectElevation,
                    level.Name,
                    onLevel.Select(p => p.Name).FirstOrDefault(n => !string.IsNullOrWhiteSpace(n)),
                    Agreed(onLevel.Select(p => Text(p.Area, ReviewInputSources.ZoneUse))),
                    Agreed(onLevel.Select(p => Integer(p.Area, ReviewInputSources.FloorNumber))),
                    areas.All(a => a > 0) ? areas.Sum() : (double?)null,
                    onLevel.SelectMany(p => p.Loops),
                    onLevel.Select(p => p.Area.UniqueId),
                    problem));
            }
        }

        return (new StoreyZoneMap(zones), null);
    }

    private static T? Agreed<T>(IEnumerable<T?> values) where T : class
    {
        var list = values.ToList();
        return list.Count > 0 && list.All(v => v is not null) && list.Distinct().Count() == 1 ? list[0] : null;
    }

    private static int? Agreed(IEnumerable<int?> values)
    {
        var list = values.ToList();
        return list.Count > 0 && list.All(v => v is not null) && list.Distinct().Count() == 1 ? list[0] : null;
    }

    private static string? Text(Element element, string name)
    {
        var parameter = element.LookupParameter(name);
        if (parameter is null || !parameter.HasValue || parameter.StorageType != StorageType.String) return null;
        var text = parameter.AsString();
        return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    }

    /// <summary>樓層序 has no 0, and an Integer parameter reads 0 until filled, so 0 is 未填.</summary>
    private static int? Integer(Element element, string name)
    {
        var parameter = element.LookupParameter(name);
        if (parameter is null || !parameter.HasValue) return null;
        int value;
        if (parameter.StorageType == StorageType.Integer) value = parameter.AsInteger();
        else if (parameter.StorageType != StorageType.String || !int.TryParse(parameter.AsString(), out value)) return null;
        return value == 0 ? (int?)null : value;
    }

    private sealed class Part
    {
        public Part(Guid packageId, Guid zoneId, RevitArea area, string name, IReadOnlyList<IReadOnlyList<Domain.Geometry.Point2D>> loops)
        {
            PackageId = packageId;
            ZoneId = zoneId;
            Area = area;
            Name = name;
            Loops = loops;
        }

        public Guid PackageId { get; }
        public Guid ZoneId { get; }
        public RevitArea Area { get; }
        public string Name { get; }
        public IReadOnlyList<IReadOnlyList<Domain.Geometry.Point2D>> Loops { get; }
    }
}
