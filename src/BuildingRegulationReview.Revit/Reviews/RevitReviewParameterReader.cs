using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using BuildingRegulationReview.Application.Candidates;
using BuildingRegulationReview.Application.Reviews;

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

            var memberNames = Names(ReviewParameterHost.Walls);
            foreach (var uid in candidates.Members.Select(m => m.Observation.TypeUniqueId).Where(x => x is not null))
                Add(elements, uid!, memberNames);

            var openingNames = Names(ReviewParameterHost.Doors);
            foreach (var opening in candidates.Openings.Select(o => o.Observation))
            {
                Add(elements, opening.Source.ElementUniqueId, openingNames);
                if (opening.TypeUniqueId is not null) Add(elements, opening.TypeUniqueId, openingNames);
            }
        }

        return new ReviewParameterSnapshot(Bindings(names), project, elements);
    }

    private static IReadOnlyList<string> Names(ReviewParameterHost host) =>
        ReviewInputSources.All.Where(s => s.Hosts.Contains(host)).Select(s => s.ParameterName).ToList();

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
