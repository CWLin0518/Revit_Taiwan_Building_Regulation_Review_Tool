using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using BuildingRegulationReview.Application.Candidates;
using BuildingRegulationReview.Application.Reviews;
using BuildingRegulationReview.Domain.Geometry;
using BuildingRegulationReview.Revit.WriteBack;

namespace BuildingRegulationReview.Revit.Reviews;

/// <summary>
/// P3-T09: reads the review parameters (<see cref="ReviewInputSources"/>) — which categories the
/// project binds them to, and their values on Project Information and on every Area, member Type,
/// opening and opening Type the candidate set names. Read-only: no transaction. Only Revit's
/// internal length unit is converted here; what a value means is decided by
/// <see cref="ReviewInputAssembler"/>.
/// </summary>
public sealed class RevitReviewParameterReader
{
    private static readonly IReadOnlyDictionary<BuiltInCategory, ReviewParameterHost> Hosts =
        new Dictionary<BuiltInCategory, ReviewParameterHost>
        {
            { BuiltInCategory.OST_ProjectInformation, ReviewParameterHost.ProjectInformation },
            { BuiltInCategory.OST_Areas, ReviewParameterHost.Areas },
            { BuiltInCategory.OST_Walls, ReviewParameterHost.Walls },
            { BuiltInCategory.OST_Columns, ReviewParameterHost.Columns },
            { BuiltInCategory.OST_StructuralColumns, ReviewParameterHost.Columns },
            { BuiltInCategory.OST_StructuralFraming, ReviewParameterHost.StructuralFraming },
            { BuiltInCategory.OST_Floors, ReviewParameterHost.Floors },
            { BuiltInCategory.OST_Ceilings, ReviewParameterHost.Ceilings },
            { BuiltInCategory.OST_Doors, ReviewParameterHost.Doors },
            { BuiltInCategory.OST_Windows, ReviewParameterHost.Windows },
            { BuiltInCategory.OST_CurtainWallPanels, ReviewParameterHost.CurtainPanels }
        };

    private readonly Document _document;

    public RevitReviewParameterReader(Document document) =>
        _document = document ?? throw new ArgumentNullException(nameof(document));

    /// <summary>Bindings and project values only, for the pre-review check before candidates are read.</summary>
    public ReviewParameterSnapshot Read(CandidateSet? candidates)
    {
        var names = new HashSet<string>(ReviewInputSources.ParameterNames, StringComparer.Ordinal);

        var project = new Dictionary<string, ParameterReading>(StringComparer.Ordinal);
        var info = _document.ProjectInformation;
        foreach (var source in ReviewInputSources.All.Where(s => s.Hosts.Contains(ReviewParameterHost.ProjectInformation)))
            project[source.ParameterName] = info is null ? ParameterReading.Absent : ReadingOf(info.LookupParameter(source.ParameterName));

        var elements = new Dictionary<string, IReadOnlyDictionary<string, ParameterReading>>(StringComparer.Ordinal);
        if (candidates is not null)
        {
            var zoneNames = Names(ReviewParameterHost.Areas);
            foreach (var uid in candidates.Zones.SelectMany(z => z.AreaUniqueIds))
                Add(elements, uid, zoneNames);
            AddDerivedInteriorFinishes(elements, candidates);

            var memberNames = Names(ReviewParameterHost.Walls);
            foreach (var uid in candidates.Members.Select(m => m.Observation.TypeUniqueId).Where(x => x is not null))
                Add(elements, uid!, memberNames);

            // Openings do not all read the same parameters any more: a 帷幕嵌板 carries 設計防火時效
            // as well as 設計防火保護 (帷幕牆規格 §6), so the names follow each opening's own category.
            var openingNames = new Dictionary<ReviewParameterHost, IReadOnlyList<string>>();
            foreach (var opening in candidates.Openings.Select(o => o.Observation))
            {
                var host = ReviewInputSources.HostOf(opening.Category);
                if (!openingNames.TryGetValue(host, out var forHost)) openingNames[host] = forHost = Names(host);
                Add(elements, opening.Source.ElementUniqueId, forHost);
                if (opening.TypeUniqueId is not null) Add(elements, opening.TypeUniqueId, forHost);
            }
        }

        return new ReviewParameterSnapshot(Bindings(names), project, elements);
    }

    private void AddDerivedInteriorFinishes(
        Dictionary<string, IReadOnlyDictionary<string, ParameterReading>> elements,
        CandidateSet candidates)
    {
        foreach (var zone in candidates.Zones)
        {
            var surfaces = new List<InteriorFinishSurface>();
            foreach (var wall in candidates.MembersOf(zone.ZoneId)
                         .Where(x => x.Observation.Category == CandidateCategory.Wall))
            {
                var element = _document.GetElement(wall.Observation.Source.ElementUniqueId);
                var type = element is null ? null : _document.GetElement(element.GetTypeId());
                surfaces.Add(new InteriorFinishSurface(
                    wall.Observation.Source.ElementUniqueId,
                    "牆",
                    TextOf(type?.LookupParameter(ReviewInputSources.InteriorFinish))));
            }

            foreach (var uid in zone.AreaUniqueIds)
            {
                var area = _document.GetElement(uid) as Area;
                if (area is null) continue;
                foreach (var ceiling in CeilingsInside(area))
                {
                    var type = _document.GetElement(ceiling.GetTypeId());
                    surfaces.Add(new InteriorFinishSurface(
                        ceiling.UniqueId,
                        "天花板",
                        TextOf(type?.LookupParameter(ReviewInputSources.InteriorFinish))));
                }
            }

            var derived = InteriorFinishAssessment.Derive(surfaces);
            var reading = derived is null ? ParameterReading.Empty : ParameterReading.OfText(derived);
            foreach (var uid in zone.AreaUniqueIds)
            {
                var values = new Dictionary<string, ParameterReading>(StringComparer.Ordinal);
                if (elements.TryGetValue(uid, out var existing))
                    foreach (var pair in existing) values[pair.Key] = pair.Value;
                values[ReviewInputSources.InteriorFinish] = reading;
                elements[uid] = values;
            }
        }
    }

    private IEnumerable<Ceiling> CeilingsInside(Area area)
    {
        var loops = RevitWrittenZoneReader.ReadLoops(area, new SpatialElementBoundaryOptions());
        if (loops.Count == 0) yield break;
        foreach (var ceiling in new FilteredElementCollector(_document)
                     .OfCategory(BuiltInCategory.OST_Ceilings)
                     .WhereElementIsNotElementType()
                     .Cast<Ceiling>())
        {
            if (ceiling.LevelId != area.LevelId) continue;
            var box = ceiling.get_BoundingBox(null);
            if (box is null) continue;
            var point = (box.Min + box.Max) * 0.5;
            var plan = new Point2D(point.X, point.Y);
            if (loops.Count(loop => RingGeometry.ContainsPoint(loop, plan)) % 2 == 1)
                yield return ceiling;
        }
    }

    private static string? TextOf(Parameter? parameter)
    {
        if (parameter is null || !parameter.HasValue) return null;
        return parameter.StorageType == StorageType.String
            ? parameter.AsString()
            : parameter.AsValueString();
    }

    /// <summary>
    /// The parameters one host carries, each named once: 設計防火時效 answers two fields
    /// (<c>element.providedFireRating</c> and <c>shaft.providedFireRating</c>), and a name read twice
    /// would be the same reading twice — the fields are told apart when the checks are assembled.
    /// </summary>
    private static IReadOnlyList<string> Names(ReviewParameterHost host) =>
        ReviewInputSources.All.Where(s => s.Hosts.Contains(host)).Select(s => s.ParameterName)
            .Distinct(StringComparer.Ordinal).ToList();

    private void Add(Dictionary<string, IReadOnlyDictionary<string, ParameterReading>> elements, string uniqueId, IReadOnlyList<string> names)
    {
        if (elements.ContainsKey(uniqueId)) return;
        var element = _document.GetElement(uniqueId);
        if (element is null) return;
        elements[uniqueId] = names.ToDictionary(n => n, n => ReadingOf(element.LookupParameter(n)), StringComparer.Ordinal);
    }

    private IEnumerable<KeyValuePair<string, ReviewParameterHost>> Bindings(HashSet<string> names)
    {
        var result = new List<KeyValuePair<string, ReviewParameterHost>>();
        var iterator = _document.ParameterBindings.ForwardIterator();
        iterator.Reset();
        while (iterator.MoveNext())
        {
            if (!(iterator.Key is Definition definition) || !names.Contains(definition.Name)) continue;
            if (!(iterator.Current is ElementBinding binding) || binding.Categories is null) continue;

            foreach (Category category in binding.Categories)
            {
                if (Hosts.TryGetValue(category.BuiltInCategory, out var host))
                    result.Add(new KeyValuePair<string, ReviewParameterHost>(definition.Name, host));
            }
        }

        return result.Distinct().ToList();
    }

    /// <summary>A parameter as plain data. Lengths are converted to metres; nothing else is interpreted.</summary>
    public static ParameterReading ReadingOf(Parameter? parameter)
    {
        if (parameter is null) return ParameterReading.Absent;
        if (!parameter.HasValue) return ParameterReading.Empty;

        switch (parameter.StorageType)
        {
            case StorageType.String:
                return ParameterReading.OfText(parameter.AsString());
            case StorageType.Integer:
                return parameter.Definition?.GetDataType() == SpecTypeId.Boolean.YesNo
                    ? ParameterReading.OfYesNo(parameter.AsInteger())
                    : ParameterReading.OfInteger(parameter.AsInteger());
            case StorageType.Double:
                var value = parameter.AsDouble();
                return parameter.Definition?.GetDataType() == SpecTypeId.Length
                    ? ParameterReading.OfLength(UnitUtils.ConvertFromInternalUnits(value, UnitTypeId.Meters))
                    : ParameterReading.OfNumber(value);
            case StorageType.ElementId:
                return ParameterReading.OfText(parameter.AsValueString());
            default:
                return ParameterReading.Empty;
        }
    }
}
