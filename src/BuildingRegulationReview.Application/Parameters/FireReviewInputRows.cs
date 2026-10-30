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
        bool? linksRefugeFloor = null,
        bool? cannotBeSubdivided = null,
        string? useGroup = null,
        string? zoneKey = null)
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
        LinksRefugeFloor = linksRefugeFloor;
        CannotBeSubdivided = cannotBeSubdivided;
        UseGroup = Clean(useGroup);
        ZoneKey = Clean(zoneKey);
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

    /// <summary>
    /// 防火檢討_用途類組 — this Area's 建築物使用類組 (第3條之3), read by 第83條's Ｈ－２組 proviso and
    /// 第79條之1. Another Area of the same 區劃 may hold another group; the review then reads the 區劃 as
    /// 混合 (docs/regulations/building-use-groups.md §6).
    /// </summary>
    public string? UseGroup { get; }

    /// <summary>
    /// Which 防火區劃 this Area is a part of — the same for every Area of one 區劃, from the tool's own
    /// mark (package and zone). Null for an Area the tool did not write, which then stands alone. The
    /// panel groups by it so a row can say what the whole 區劃 reads as, not only this Area.
    /// </summary>
    public string? ZoneKey { get; }

    /// <summary>防火檢討_自動滅火設備; null when the parameter holds no value.</summary>
    public bool? Sprinklered { get; }

    /// <summary>防火檢討_所在樓層序.</summary>
    public int? FloorNumber { get; }

    /// <summary>防火檢討_避難層通達 — 第79條之2第3項第一款; null when the parameter holds no value.</summary>
    public bool? LinksRefugeFloor { get; }

    /// <summary>
    /// 防火檢討_無法區劃分隔 — （丙）of 第79條之1, which only a designer can state; null when the
    /// parameter holds no value. A Revit YESNO cannot tell 未勾選 from 從未設定, so a bound-but-blank
    /// box reads as 否 — the strict direction (第79條之1規格 §9).
    /// </summary>
    public bool? CannotBeSubdivided { get; }

    public FireReviewZoneParameters Present { get; }

    public string DisplayName => Number is null ? Name : $"{Number} {Name}";

    public string AreaText => AreaSquareMeters is double a
        ? a.ToString("0.##", CultureInfo.InvariantCulture) + " m²"
        : "—";

    /// <summary>True when this Area's 區劃用途 is 挑空, the only use 第79條之2第3項 is written for.</summary>
    public bool IsAtrium => string.Equals(Use, ZoneUses.Atrium, StringComparison.Ordinal);

    /// <summary>
    /// True when this Area's 區劃用途 is one of the six 第79條之1 names, the only ones for which
    /// 無法區劃分隔 means anything.
    /// </summary>
    public bool IsArticle79_1Use => ZoneUses.IsArticle79_1Use(Use);

    /// <summary>
    /// Which of the zone parameters this Area does not carry, so an edit cannot land.
    /// </summary>
    /// <remarks>
    /// 防火檢討_避難層通達 is reported only for a 挑空 — the one 第3項 fact only a designer can state;
    /// 連跨樓層數 and 連通區劃面積 are traced through the storeys (決議 35). It is not a required
    /// parameter (垂直區劃規格 §6、決議 27) — a project with no 挑空 reviews perfectly well without them — so listing them on
    /// every Area would turn the 提醒 column red across a whole model over something nobody needs.
    /// 防火檢討_無法區劃分隔 is reported the same way and for the same reason, only for the six uses
    /// 第79條之1 names (第79條之1規格 §6、決議 9).
    /// </remarks>
    public IReadOnlyList<string> MissingParameters
    {
        get
        {
            var missing = new List<string>();
            if ((Present & FireReviewZoneParameters.Use) == 0) missing.Add(ReviewInputSources.ZoneUse);
            if ((Present & FireReviewZoneParameters.Sprinklered) == 0) missing.Add(ReviewInputSources.Sprinklered);
            if ((Present & FireReviewZoneParameters.FloorNumber) == 0) missing.Add(ReviewInputSources.FloorNumber);
            if ((Present & FireReviewZoneParameters.UseGroup) == 0) missing.Add(ReviewInputSources.UseGroup);
            if (IsAtrium && (Present & FireReviewZoneParameters.LinksRefugeFloor) == 0)
                missing.Add(ReviewInputSources.LinksRefugeFloor);

            if (IsArticle79_1Use && (Present & FireReviewZoneParameters.CannotBeSubdivided) == 0)
                missing.Add(ReviewInputSources.CannotBeSubdivided);

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

    /// <summary>防火檢討_避難層通達 (第79條之2第3項第一款); only a 挑空 needs it.</summary>
    LinksRefugeFloor = 16,

    /// <summary>
    /// 防火檢討_無法區劃分隔 (第79條之1); only the six uses that article names need it.
    /// </summary>
    CannotBeSubdivided = 32,

    /// <summary>防火檢討_用途類組 — 第83條's Ｈ－２組 proviso and 第79條之1 read it.</summary>
    UseGroup = 64
}

/// <summary>
/// What 防火檢討_防火構造建築物 does to the whole review, said in the three states the parameter
/// actually has.
/// </summary>
/// <remarks>
/// 驗證清單 C-07：面板原本只要值不是 true 就說「整份檢討會判『資料不足』」，不分「否」與「未填」。
/// 四條規則的 <c>appliesWhen</c> 都以 <c>building.fireResistiveConstruction == true</c> 開頭，所以
/// 明確填「否」時那個條件是「已知為假」——規則一律不適用，不是資料不足（2026-10-13 實測：寫成
/// false 後跑 FL3 得到「符合 0／未符合 0／待確認 2／不適用 104」，一筆資料不足都沒有）。只有未填
/// 才是資料不足。
/// <para>
/// 「不適用」在檢討表上讀起來像「沒有要檢討的事」，與這句紅字想傳達的「這份檢討作不了數」正好相反，
/// 所以填「否」那一句必須點明不適用不代表合格。措辭放在 Application 而不是只寫在 ViewModel 裡，
/// MCP 的工具說明與面板才講得出同一件事。
/// </para>
/// </remarks>
public static class FireResistiveConstructionNotice
{
    /// <summary>The warning to show beside the box, or an empty string when there is nothing to warn about.</summary>
    public static string For(bool? fireResistiveConstruction)
    {
        if (fireResistiveConstruction == true) return "";
        return fireResistiveConstruction == false
            ? "填「否」時，四類規則的適用條件都是已知為假，整份檢討每一列都會判「不適用」——" +
              "「不適用」不代表合格，只表示本工具找不到可據以檢討的條文，這份檢討作不了數。"
            : "未填時，四類規則的適用條件都無法判斷，整份檢討會判「資料不足」。";
    }
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
        string? legacyBuildingUse = null,
        int? floorsAboveGround = null,
        FireReviewProjectParameters present = FireReviewProjectParameters.None)
    {
        if (string.IsNullOrWhiteSpace(elementUniqueId)) throw new ArgumentException("Element UniqueId is required.", nameof(elementUniqueId));

        ElementUniqueId = elementUniqueId.Trim();
        FireResistiveConstruction = fireResistiveConstruction;
        LegacyBuildingUse = string.IsNullOrWhiteSpace(legacyBuildingUse) ? null : legacyBuildingUse!.Trim();
        FloorsAboveGround = floorsAboveGround;
        Present = present;
    }

    public string ElementUniqueId { get; }

    /// <summary>防火檢討_防火構造建築物 — the switch every rule's applicability hangs on.</summary>
    public bool? FireResistiveConstruction { get; }

    /// <summary>
    /// The old Project Information 建築物用途類組, if the model still carries one. The review no longer
    /// reads it — one value for the whole building let a 區劃 nobody filled in borrow another's group —
    /// so it is shown only to offer 「填入所有未填的區劃」 once (docs/regulations/building-use-groups.md §6).
    /// </summary>
    public string? LegacyBuildingUse { get; }

    /// <summary>地上層數, which 第70條 counts storeys from the top with.</summary>
    public int? FloorsAboveGround { get; }

    public FireReviewProjectParameters Present { get; }

    public IReadOnlyList<string> MissingParameters
    {
        get
        {
            var missing = new List<string>();
            if ((Present & FireReviewProjectParameters.FireResistive) == 0) missing.Add(ReviewInputSources.FireResistiveConstruction);
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
