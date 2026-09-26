using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using BuildingRegulationReview.Application.Checks;
using BuildingRegulationReview.Application.Reviews;

namespace BuildingRegulationReview.Application.Parameters;

/// <summary>
/// One Revit Area — one 防火區劃 — as the batch panel edits it. These are instance parameters, so a
/// row writes to that Area alone; nothing here reaches another zone.
/// </summary>
/// <remarks>
/// <see cref="AreaSquareMeters"/> and the level are shown but never written: the Area's extent is
/// what the review measures (spec 11.4 step 2), so it is read-only here by construction — the row
/// offers no edit for it rather than relying on anyone remembering not to.
/// </remarks>
public sealed class FireReviewZoneRow
{
    public FireReviewZoneRow(
        string elementUniqueId,
        string name,
        string? number = null,
        string? levelName = null,
        string? areaSchemeName = null,
        double? areaSquareMeters = null,
        string? use = null,
        bool? sprinklered = null,
        int? floorNumber = null,
        FireReviewZoneParameters present = FireReviewZoneParameters.None,
        string? levelId = null,
        int? spannedFloors = null,
        bool? linksRefugeFloor = null)
    {
        if (string.IsNullOrWhiteSpace(elementUniqueId)) throw new ArgumentException("Element UniqueId is required.", nameof(elementUniqueId));
        if (areaSquareMeters is double a && (double.IsNaN(a) || double.IsInfinity(a) || a < 0))
            throw new ArgumentOutOfRangeException(nameof(areaSquareMeters));

        ElementUniqueId = elementUniqueId.Trim();
        LevelId = Clean(levelId);
        Name = string.IsNullOrWhiteSpace(name) ? "（未命名區劃）" : name.Trim();
        Number = Clean(number);
        LevelName = Clean(levelName);
        AreaSchemeName = Clean(areaSchemeName);
        AreaSquareMeters = areaSquareMeters;
        Use = Clean(use);
        Sprinklered = sprinklered;
        FloorNumber = floorNumber;
        SpannedFloors = AtriumExemption.StatedSpannedFloors(spannedFloors);
        LinksRefugeFloor = linksRefugeFloor;
        Present = present;
    }

    public string ElementUniqueId { get; }
    public string Name { get; }
    public string? Number { get; }
    public string? LevelName { get; }

    /// <summary>The level this Area sits on, which is what the storey numbering is keyed by.</summary>
    public string? LevelId { get; }

    public string? AreaSchemeName { get; }

    /// <summary>Revit's own measurement, shown for context; never written from here.</summary>
    public double? AreaSquareMeters { get; }

    /// <summary>防火檢討_區劃用途.</summary>
    public string? Use { get; }

    /// <summary>防火檢討_自動滅火設備; null when the parameter holds no value.</summary>
    public bool? Sprinklered { get; }

    /// <summary>防火檢討_所在樓層序.</summary>
    public int? FloorNumber { get; }

    /// <summary>
    /// 防火檢討_連跨樓層數 — 第79條之2第3項第二款, and only a 挑空 has one. Null when nothing was
    /// stated, which a Revit Integer parameter spells 0 (see
    /// <see cref="AtriumExemption.StatedSpannedFloors"/>).
    /// </summary>
    public int? SpannedFloors { get; }

    /// <summary>防火檢討_避難層通達 — 第79條之2第3項第一款; null when the parameter holds no value.</summary>
    public bool? LinksRefugeFloor { get; }

    public FireReviewZoneParameters Present { get; }

    public string DisplayName => Number is null ? Name : $"{Number} {Name}";

    public string AreaText => AreaSquareMeters is double a
        ? a.ToString("0.##", CultureInfo.InvariantCulture) + " m²"
        : "—";

    /// <summary>True when this Area's 區劃用途 is 挑空, the only use 第79條之2第3項 is written for.</summary>
    public bool IsAtrium => string.Equals(Use, ZoneUses.Atrium, StringComparison.Ordinal);

    /// <summary>
    /// Which of the zone parameters this Area does not carry, so an edit cannot land.
    /// </summary>
    /// <remarks>
    /// The 第3項 pair is reported only for a 挑空. They are not required parameters (垂直區劃規格
    /// §6、決議 27) — a project with no 挑空 reviews perfectly well without them — so listing them on
    /// every Area would turn the 提醒 column red across a whole model over something nobody needs.
    /// </remarks>
    public IReadOnlyList<string> MissingParameters
    {
        get
        {
            var missing = new List<string>();
            if ((Present & FireReviewZoneParameters.Use) == 0) missing.Add(ReviewInputSources.ZoneUse);
            if ((Present & FireReviewZoneParameters.Sprinklered) == 0) missing.Add(ReviewInputSources.Sprinklered);
            if ((Present & FireReviewZoneParameters.FloorNumber) == 0) missing.Add(ReviewInputSources.FloorNumber);
            if (!IsAtrium) return missing;
            if ((Present & FireReviewZoneParameters.SpannedFloors) == 0) missing.Add(ReviewInputSources.SpannedFloors);
            if ((Present & FireReviewZoneParameters.LinksRefugeFloor) == 0) missing.Add(ReviewInputSources.LinksRefugeFloor);
            return missing;
        }
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value!.Trim();

    public override string ToString() => DisplayName;
}

[Flags]
public enum FireReviewZoneParameters
{
    None = 0,
    Use = 1,
    Sprinklered = 2,
    FloorNumber = 4,

    /// <summary>防火檢討_連跨樓層數 (第79條之2第3項第二款); only a 挑空 needs it.</summary>
    SpannedFloors = 8,

    /// <summary>防火檢討_避難層通達 (第79條之2第3項第一款); only a 挑空 needs it.</summary>
    LinksRefugeFloor = 16
}

/// <summary>
/// The Project Information facts the review reads. One element, so one row; it is rendered as a
/// form rather than a grid.
/// </summary>
/// <remarks>
/// 建築物高度 is deliberately absent: the model's own extent is the height (see
/// <see cref="ReviewModelFacts"/>), so there is nothing here to type and nothing to keep in sync.
/// </remarks>
public sealed class FireReviewProjectRow
{
    public FireReviewProjectRow(
        string elementUniqueId,
        bool? fireResistiveConstruction = null,
        string? buildingUse = null,
        int? floorsAboveGround = null,
        FireReviewProjectParameters present = FireReviewProjectParameters.None)
    {
        if (string.IsNullOrWhiteSpace(elementUniqueId)) throw new ArgumentException("Element UniqueId is required.", nameof(elementUniqueId));

        ElementUniqueId = elementUniqueId.Trim();
        FireResistiveConstruction = fireResistiveConstruction;
        BuildingUse = string.IsNullOrWhiteSpace(buildingUse) ? null : buildingUse!.Trim();
        FloorsAboveGround = floorsAboveGround;
        Present = present;
    }

    public string ElementUniqueId { get; }

    /// <summary>防火檢討_防火構造建築物 — the switch every rule's applicability hangs on.</summary>
    public bool? FireResistiveConstruction { get; }

    /// <summary>建築物用途類組.</summary>
    public string? BuildingUse { get; }

    /// <summary>地上層數, which 第70條 counts storeys from the top with.</summary>
    public int? FloorsAboveGround { get; }

    public FireReviewProjectParameters Present { get; }

    public IReadOnlyList<string> MissingParameters
    {
        get
        {
            var missing = new List<string>();
            if ((Present & FireReviewProjectParameters.FireResistive) == 0) missing.Add(ReviewInputSources.FireResistiveConstruction);
            if ((Present & FireReviewProjectParameters.BuildingUse) == 0) missing.Add(ReviewInputSources.BuildingUse);
            if ((Present & FireReviewProjectParameters.FloorsAboveGround) == 0) missing.Add(ReviewInputSources.FloorsAboveGround);
            return missing;
        }
    }
}

[Flags]
public enum FireReviewProjectParameters
{
    None = 0,
    FireResistive = 1,
    BuildingUse = 2,
    FloorsAboveGround = 4
}

/// <summary>
/// Everything one scan collected for the batch panel: the element Types, the 區劃 and the project
/// facts. Grouped in one object because the panel writes them in a single transaction.
/// </summary>
public sealed class FireReviewParameterSet
{
    public FireReviewParameterSet(
        FireReviewTypeTable types,
        IEnumerable<FireReviewZoneRow>? zones = null,
        FireReviewProjectRow? project = null,
        FloorNumbering? floors = null)
    {
        Types = types ?? throw new ArgumentNullException(nameof(types));
        Floors = floors ?? FloorNumbering.Empty;
        Zones = new ReadOnlyCollection<FireReviewZoneRow>(
            (zones ?? Array.Empty<FireReviewZoneRow>())
            .GroupBy(z => z.ElementUniqueId, StringComparer.Ordinal)
            .Select(g => g.First())
            .OrderBy(z => z.LevelName, StringComparer.CurrentCulture)
            .ThenBy(z => z.DisplayName, StringComparer.CurrentCulture)
            .ToList());
        Project = project;
    }

    public FireReviewTypeTable Types { get; }
    public IReadOnlyList<FireReviewZoneRow> Zones { get; }

    /// <summary>Null when the document has no Project Information element to read.</summary>
    public FireReviewProjectRow? Project { get; }

    /// <summary>
    /// What the model's levels say each storey's number is, so 所在樓層序 and 地上層數 can be
    /// proposed instead of typed. A proposal only: it is shown in the panel and written when the
    /// user accepts it, because ground is assumed to be elevation zero and not every Level is a
    /// storey (see <see cref="LevelFloorNumbering"/>).
    /// </summary>
    public FloorNumbering Floors { get; }

    /// <summary>
    /// The one fact that decides whether any rule runs at all: with 防火構造建築物 unticked every
    /// rule's applicability fails and the whole review reports 資料不足 (spec 11.3).
    /// </summary>
    public bool FireResistiveConstructionIsSet => Project?.FireResistiveConstruction == true;

    public IEnumerable<FireReviewZoneRow> ZonesMissingSprinklers =>
        Zones.Where(z => z.Sprinklered is null);

}
