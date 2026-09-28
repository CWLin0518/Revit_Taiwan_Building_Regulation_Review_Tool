using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using BuildingRegulationReview.Application.Checks;
using BuildingRegulationReview.Domain.Reviews;
using BuildingRegulationReview.Domain.Rules;

namespace BuildingRegulationReview.Application.Reviews;

/// <summary>
/// A Revit UniqueId read back as the element id a user can actually use: the number 管理 → 依 ID 選取
/// takes, and the number Revit itself shows in a tooltip. Nothing in the review stores an element id
/// — a UniqueId is the only identity that survives a reopened model — so the number is worked out
/// again whenever the 檢討面板 shows one.
/// </summary>
/// <remarks>
/// A UniqueId is the episode GUID followed by the element id in hexadecimal
/// (<c>xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx-45095</c> is element 282773). The shape is checked in
/// full before the tail is read, so a plain GUID — a zone id, a document id — is never mistaken for
/// one and is shown unchanged.
/// </remarks>
public static class ReviewElementReference
{
    /// <summary>How many ids a list prints before it only says how many more there are.</summary>
    public const int ListLimit = 12;

    /// <summary>What a list of no ids reads as.</summary>
    public const string None = "—";

    private const char ListSeparator = '、';

    private static readonly Regex UniqueIdPattern = new Regex(
        @"\b[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}-[0-9a-fA-F]{1,16}\b",
        RegexOptions.CultureInvariant);

    private static readonly int[] SegmentLengths = { 8, 4, 4, 4, 12 };

    /// <summary>The element id of a UniqueId, or null when the text is not one.</summary>
    public static string? ElementIdOf(string? uniqueId)
    {
        if (uniqueId is null) return null;
        var text = uniqueId.Trim();
        if (!IsUniqueId(text)) return null;

        var tail = text.Substring(text.LastIndexOf('-') + 1);
        long id = 0;
        foreach (var digit in tail) id = (id * 16) + HexValue(digit);
        return id.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>The element id, or the text itself when it is not a UniqueId.</summary>
    public static string Describe(string? uniqueId) =>
        string.IsNullOrWhiteSpace(uniqueId) ? None : ElementIdOf(uniqueId) ?? uniqueId!.Trim();

    /// <summary>
    /// A list of ids, long lists cut short. The count is always stated, so a row that names 166 嵌板
    /// still says 166 rather than quietly showing the first few.
    /// </summary>
    public static string DescribeMany(IEnumerable<string>? uniqueIds)
    {
        var ids = (uniqueIds ?? Enumerable.Empty<string>())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(Describe)
            .ToList();

        if (ids.Count == 0) return None;
        if (ids.Count <= ListLimit) return string.Join(ListSeparator.ToString(), ids);
        return string.Join(ListSeparator.ToString(), ids.Take(ListLimit)) +
               string.Format(CultureInfo.InvariantCulture, "…（共 {0} 個）", ids.Count);
    }

    /// <summary>The same for a comma separated list, which is how evidence stores one.</summary>
    public static string DescribeList(string? commaSeparated) =>
        string.IsNullOrWhiteSpace(commaSeparated)
            ? None
            : DescribeMany(commaSeparated!.Split(',', ListSeparator));

    /// <summary>Every UniqueId inside a sentence replaced by its element id; everything else untouched.</summary>
    public static string InText(string? text) =>
        string.IsNullOrEmpty(text) ? string.Empty : UniqueIdPattern.Replace(text, m => ElementIdOf(m.Value) ?? m.Value);

    /// <summary>
    /// True when the text is nothing but a separated list of UniqueIds — the shape evidence stores
    /// 嵌板、實體外牆 and 相關元素 in, which reads as a list of ids rather than as a sentence.
    /// </summary>
    public static bool IsIdList(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;

        var any = false;
        foreach (var part in text!.Split(',', ListSeparator))
        {
            var token = part.Trim();
            if (token.Length == 0) continue;
            if (!IsUniqueId(token)) return false;
            any = true;
        }
        return any;
    }

    private static bool IsUniqueId(string text)
    {
        var parts = text.Split('-');
        if (parts.Length != SegmentLengths.Length + 1) return false;
        for (var i = 0; i < SegmentLengths.Length; i++)
            if (parts[i].Length != SegmentLengths[i] || !IsHex(parts[i])) return false;

        var tail = parts[SegmentLengths.Length];
        return tail.Length >= 1 && tail.Length <= 16 && IsHex(tail);
    }

    private static bool IsHex(string text)
    {
        foreach (var c in text)
            if (!Uri.IsHexDigit(c)) return false;
        return true;
    }

    private static int HexValue(char digit) =>
        digit <= '9' ? digit - '0' : (char.ToUpperInvariant(digit) - 'A') + 10;
}

/// <summary>
/// One value of a result written the way the 檢討表 should read it: a rounded number with its unit
/// in the units of 第79條 and 第83條, 是／否 for a flag, and no floating point noise.
/// </summary>
/// <remarks>
/// <see cref="ReviewValue.ToString"/> is what the stored run and the tests compare, so it stays as
/// it is — round-trip text, not a label. This is the reader's side of the same value.
/// </remarks>
public static class ReviewValueText
{
    /// <summary>What an absent value reads as.</summary>
    public const string None = "—";

    /// <summary>What an empty text reads as: the parameter exists and holds nothing.</summary>
    public const string Blank = "（空白）";

    /// <summary>
    /// Four decimals: enough to show a 0.1 mm difference against a 900 mm 但書, and short enough to
    /// swallow the 0.30000000000000004 a double arrives as.
    /// </summary>
    private const string NumberFormat = "#,##0.####";

    public static string Format(ReviewValue? value)
    {
        if (value is null) return None;
        return value.Kind switch
        {
            ReviewValueKind.Quantity => Number(value.Number) + UnitSuffix(value.Unit),
            ReviewValueKind.Text => value.Text.Length == 0 ? Blank : ReviewElementReference.InText(value.Text),
            _ => value.Flag ? "是" : "否"
        };
    }

    public static string Number(double number) => number.ToString(NumberFormat, CultureInfo.InvariantCulture);

    public static string Unit(ReviewUnit unit) => unit switch
    {
        ReviewUnit.SquareMeter => "㎡",
        ReviewUnit.Meter => "m",
        ReviewUnit.Minute => "分鐘",
        ReviewUnit.Count => "個",
        _ => string.Empty
    };

    private static string UnitSuffix(ReviewUnit unit)
    {
        var text = Unit(unit);
        return text.Length == 0 ? string.Empty : " " + text;
    }
}

/// <summary>
/// The names and the values of 證據 in the words of the 檢討 rather than of the code: every field a
/// check or a rule records gets a Chinese label, and every value that is an enum name, a field name
/// or a UniqueId is turned into something a reviewer can act on.
/// </summary>
/// <remarks>
/// <para>
/// The rule fields already describe themselves — <see cref="RuleFieldCatalog.Default"/> carries a
/// Chinese description for each one — so those are read from the catalog and never repeated here.
/// What is listed below is only what the checks record on their own account (<c>source.*</c>,
/// <c>provided.*</c>, <c>area.*</c>, the 帷幕牆 and 垂直區劃 bookkeeping).
/// </para>
/// <para>
/// A field nobody knows is shown as it is. That is deliberate: a new check's evidence must stay
/// visible in the panel the day it is written, unlabelled rather than hidden.
/// </para>
/// </remarks>
public static class ReviewFieldText
{
    private const string SourceSuffix = "（來源）";
    private const char TokenSeparator = '、';

    private static readonly Regex AreaPartPattern = new Regex(
        @"^area\.part\[(?<uid>[^\]]+)\]\.(?<what>revit|geometric)$", RegexOptions.CultureInvariant);

    private static readonly Regex FieldNamePattern = new Regex(
        @"[A-Za-z_][A-Za-z0-9_]*(?:\.[A-Za-z0-9_]+)+", RegexOptions.CultureInvariant);

    /// <summary>The fields the checks record themselves, in the words the 檢討表 uses for them.</summary>
    private static readonly IReadOnlyDictionary<string, string> Labels = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["zone.id"] = "區劃編號",
        ["zone.name"] = "區劃名稱",
        ["zone.problems"] = "區劃資料問題",

        ["area.revit"] = "Revit 標示面積",
        ["area.geometric"] = "邊界量得面積",
        ["area.crossCheck"] = "兩種面積核對結果",
        ["area.crossCheckTolerance"] = "面積核對容許誤差",
        ["area.relativeDifference"] = "兩種面積相對差",
        ["area.partCount"] = "組成區劃的 Area 數",

        ["article79_1.clause"] = "第79條之1 適用款次",
        ["article79_1.gaps"] = "第79條之1 尚缺的資料",
        ["atrium.clause"] = "挑空免除適用款次",
        ["atrium.gaps"] = "挑空免除尚缺的資料",

        ["candidate.ambiguity"] = "無法判定空間關係的原因",
        ["candidate.areaCount"] = "區劃內的 Area 數",
        ["candidate.enclosedAreaCount"] = "封閉的 Area 數",
        ["candidate.otherZoneId"] = "另一個區劃編號",
        ["candidate.relation"] = "與區劃的空間關係",
        ["candidate.zoneId"] = "區劃編號",
        ["candidate.zoneName"] = "區劃名稱",

        ["junction.id"] = "交接處編號",
        ["junction.panelCount"] = "交接帶內嵌板數",
        ["junction.panels"] = "交接帶內嵌板",
        ["junction.facadeWallUniqueIds"] = "交接處實體外牆",
        ["junction.placement"] = "交接處位置",
        ["junction.doubt"] = "無法判定的情形",
        ["junction.doubtReason"] = "無法判定的原因",
        ["junction.doubtSubjects"] = "相關元素",
        ["junction.transferredTo"] = "改依哪一條檢討",

        ["option.junctionSearchTolerance"] = "交接搜尋容許距離",
        ["option.samplingInterval"] = "取樣間距",

        ["protection.kind"] = "設計防火保護讀取結果",
        ["protection.parameter"] = "讀取的參數",
        ["protection.raw"] = "參數原始內容",
        ["protection.reason"] = "讀取結果說明",

        ["provided.field"] = "對應的檢討欄位",
        ["provided.kind"] = "設計值讀取結果",
        ["provided.parameter"] = "讀取的參數",
        ["provided.raw"] = "參數原始內容",
        ["provided.reason"] = "讀取結果說明",
        ["provided.scope"] = "參數所在層級",

        ["rule.gaps"] = "規則尚缺的資料",

        ["shaft.requirementLabel"] = "受檢要求",

        ["source.category"] = "元素類別",
        ["source.documentUniqueId"] = "所在模型",
        ["source.elementUniqueId"] = "元素",
        ["source.hostIsCurtainWall"] = "依附主體是否為帷幕牆",
        ["source.hostUniqueId"] = "依附主體",
        ["source.linkInstanceUniqueId"] = "連結模型",
        ["source.typeName"] = "類型名稱",
        ["source.typeUniqueId"] = "類型"
    };

    /// <summary>Enum names as they are stored in evidence, per field, in the words of the 條文.</summary>
    private static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> TokenMaps = BuildTokenMaps();

    /// <summary>The label of one evidence field.</summary>
    public static string Label(string? field)
    {
        if (string.IsNullOrWhiteSpace(field)) return string.Empty;
        var name = field!.Trim();

        // source[zone.use] — which parameter, project setting or Area the field was read from.
        if (name.StartsWith("source[", StringComparison.Ordinal) && name.EndsWith("]", StringComparison.Ordinal))
            return Label(name.Substring("source[".Length, name.Length - "source[".Length - 1)) + SourceSuffix;

        var part = AreaPartPattern.Match(name);
        if (part.Success)
            return string.Format(CultureInfo.InvariantCulture, "Area {0} 的{1}",
                ReviewElementReference.Describe(part.Groups["uid"].Value),
                string.Equals(part.Groups["what"].Value, "revit", StringComparison.Ordinal) ? "Revit 標示面積" : "邊界量得面積");

        if (Labels.TryGetValue(name, out var label)) return label;

        var definition = RuleFieldCatalog.Default.Find(name);
        if (definition is not null && definition.Description.Length > 0) return Shorten(definition.Description);

        return name;
    }

    /// <summary>True when the field is one the panel can put a Chinese name to.</summary>
    public static bool IsKnown(string? field) =>
        !string.IsNullOrWhiteSpace(field) &&
        (Labels.ContainsKey(field!.Trim()) || RuleFieldCatalog.Default.Find(field.Trim()) is not null);

    /// <summary>One evidence value, with its enum names, field names and UniqueIds turned into words.</summary>
    public static string Value(string? field, ReviewValue? value)
    {
        if (value is null) return ReviewValueText.None;
        if (value.Kind != ReviewValueKind.Text) return ReviewValueText.Format(value);

        var text = value.Text;
        if (text.Length == 0) return ReviewValueText.Blank;

        var name = field?.Trim() ?? string.Empty;
        if (string.Equals(name, "junction.placement", StringComparison.Ordinal)) return Placement(text);
        if (string.Equals(name, "rule.gaps", StringComparison.Ordinal)) return Humanize(text);
        if (TokenMaps.TryGetValue(name, out var tokens)) return Translate(text, tokens);

        // 交接帶內的 166 片嵌板 is a list, not a sentence: it reads as ids and says how many there are.
        if (ReviewElementReference.IsIdList(text)) return ReviewElementReference.DescribeList(text);

        return ReviewElementReference.InText(text);
    }

    /// <summary>
    /// A sentence written for the log or for a rule — a 說明, a gap list — with the two things a
    /// reader cannot decode replaced: UniqueIds by element ids, rule field names by their Chinese
    /// description. A name nobody knows is left alone rather than guessed at.
    /// </summary>
    public static string Humanize(string? text) =>
        string.IsNullOrEmpty(text)
            ? string.Empty
            : FieldNamePattern.Replace(ReviewElementReference.InText(text), m => IsKnown(m.Value) ? Label(m.Value) : m.Value);

    /// <summary>The six numbers of <c>junction.placement</c> as a position a person can find.</summary>
    private static string Placement(string text)
    {
        if (!CurtainWallJunctionPlacement.TryParseEvidence(text, out var placement) || placement is null)
            return text;

        var bottom = ReviewValueText.Number(placement.BottomElevationMm / 1000.0);
        var top = ReviewValueText.Number(placement.TopElevationMm / 1000.0);
        var height = string.Format(CultureInfo.InvariantCulture, "，高程 {0}～{1} m", bottom, top);

        var start = string.Format(CultureInfo.InvariantCulture, "({0}, {1})",
            ReviewValueText.Number(placement.StartMm.X / 1000.0), ReviewValueText.Number(placement.StartMm.Y / 1000.0));
        if (placement.IsPoint) return "交點 " + start + height;

        var end = string.Format(CultureInfo.InvariantCulture, "({0}, {1})",
            ReviewValueText.Number(placement.EndMm.X / 1000.0), ReviewValueText.Number(placement.EndMm.Y / 1000.0));
        return "平面 " + start + "～" + end + height;
    }

    private static string Translate(string text, IReadOnlyDictionary<string, string> tokens)
    {
        var parts = text.Split(',', TokenSeparator)
            .Select(x => x.Trim())
            .Where(x => x.Length > 0)
            .Select(x => tokens.TryGetValue(x, out var word) ? word : x)
            .ToList();
        return parts.Count == 0 ? text : string.Join(TokenSeparator.ToString(), parts);
    }

    /// <summary>A catalog description without its parenthetical list of code words.</summary>
    private static string Shorten(string description)
    {
        var at = description.IndexOf('（');
        return at <= 0 ? description : description.Substring(0, at).Trim();
    }

    private static IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> BuildTokenMaps()
    {
        var rating = Map(
            ("Rated", "讀到防火時效"),
            ("Missing", "參數未填寫"),
            ("Unreadable", "參數內容無法判讀"),
            ("Undeterminable", "無單一時效可判定"),
            ("Yes", "是"),
            ("No", "否"));

        var protection = Map(
            ("Yes", "是"),
            ("No", "否"),
            ("Missing", "參數未填寫"),
            ("Unreadable", "參數內容無法判讀"));

        var category = Map(
            ("Walls", "牆"),
            ("Columns", "柱"),
            ("StructuralFraming", "梁"),
            ("Floors", "樓板"),
            ("Door", "門"),
            ("Window", "窗"),
            ("CurtainPanel", "帷幕嵌板"));

        var clause = Map(
            ("None", "不適用任一款"),
            ("FirstClause", "第一款"),
            ("SecondClause", "第二款"));

        return new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal)
        {
            ["provided.kind"] = rating,
            ["protection.kind"] = protection,
            ["provided.scope"] = Map(("Instance", "元素本身"), ("Type", "類型")),

            ["source.category"] = category,
            ["element.category"] = category,
            ["opening.kind"] = category,

            ["junction.kind"] = Map(
                ("WallToCurtainWall", "區劃牆與帷幕牆交接（水平）"),
                ("FloorToCurtainWall", "區劃樓地板與帷幕牆交接（層間）"),
                ("CurtainPanelOther", "帷幕牆其他部分")),
            ["junction.panelKind"] = Map(("Solid", "實心嵌板"), ("Glazed", "玻璃嵌板")),
            ["junction.doubt"] = Map(
                ("NonPlanarCurtainWall", "帷幕牆非平面（曲面、傾斜或雙曲面）"),
                ("UnresolvedIntersection", "交點無法唯一解出"),
                ("SplitByGridLine", "層間帶被 grid line 分割"),
                ("FacadeWallOverlapsPanel", "實體外牆與帷幕嵌板重疊"),
                ("VerticalCompartmentSpace", "連跨複數樓層之挑空帷幕牆")),

            ["shaft.requirement"] = Map(
                ("HoistwaySmokeSeal", "昇降機道防火設備之遮煙性能"),
                ("ShaftDoorRating", "管道間維修門之防火時效"),
                ("ShaftDoorSmokeSeal", "管道間維修門之遮煙性能"),
                ("AtriumExemption", "挑空得不受第1項限制")),

            ["area.crossCheck"] = Map(
                ("Agrees", "兩種面積相符"),
                ("Differs", "兩種面積不符"),
                ("NotComparable", "缺一邊，無法核對")),

            ["candidate.relation"] = Map(
                ("Boundary", "構成區劃邊界"),
                ("Crossing", "跨越區劃邊界"),
                ("Inside", "位於區劃內"),
                ("Ambiguous", "無法判定")),
            ["candidate.ambiguity"] = Map(
                ("BoundaryOffCenterline", "區劃邊界落在構件厚度內但不在中心線上"),
                ("BoundaryAlongOutline", "區劃邊界沿柱面而非穿過柱心"),
                ("NoPlanGeometry", "元素沒有可量測的平面幾何"),
                ("HostRelationAmbiguous", "開口的主體牆本身無法判定"),
                ("HostNotResolved", "開口的主體未讀到或不是牆"),
                ("CurtainWallOpening", "帷幕牆上的開口，改由人工覆核"),
                ("NonHostedOpening", "沒有主體的開口，改由人工覆核"),
                ("OpeningLocationUnknown", "開口沒有平面位置"),
                ("LinkedElement", "連結模型的元素，改由人工覆核"),
                ("ZoneNotEnclosed", "區劃的 Area 未封閉"),
                ("ZonesOverlap", "區劃的 Area 與另一區劃重疊")),

            ["article79_1.clause"] = clause,
            ["atrium.clause"] = clause,
            ["article79_1.gaps"] = Map(
                ("None", "無"),
                ("CannotBeSubdivided", "無法區劃分隔（防火檢討_無法區劃分隔）"),
                ("FireResistiveConstruction", "防火構造建築物"),
                ("BuildingUse", "建築物使用類組")),
            ["atrium.gaps"] = Map(
                ("None", "無"),
                ("FireResistiveConstruction", "防火構造建築物"),
                ("RefugeFloorLink", "避難層通達直上層或直下層"),
                ("InteriorFinish", "室內裝修耐燃等級"),
                ("SpannedFloors", "連跨樓層數"),
                ("CompartmentArea", "樓地板面積"))
        };
    }

    private static IReadOnlyDictionary<string, string> Map(params (string Token, string Word)[] entries)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var entry in entries) map[entry.Token] = entry.Word;
        return map;
    }
}
