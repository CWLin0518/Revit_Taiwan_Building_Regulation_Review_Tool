using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using BuildingRegulationReview.Application.Candidates;
using BuildingRegulationReview.Application.Checks;
using BuildingRegulationReview.Domain.Rules;

namespace BuildingRegulationReview.Application.Reviews;

/// <summary>The Revit elements a review parameter can be bound to (spec 11.1「必要參數存在且可讀」).</summary>
public enum ReviewParameterHost
{
    ProjectInformation,
    Areas,
    Walls,
    Columns,
    StructuralFraming,
    Floors,
    Ceilings,
    Doors,
    Windows,
    CurtainPanels
}

/// <summary>Whether a parameter is read from the element itself, its Type, or the element first and then its Type.</summary>
public enum ReviewParameterLevel
{
    Instance,
    Type,
    InstanceOrType
}

/// <summary>
/// Where one rule field comes from when the model does not derive it: the parameter that holds it,
/// whether that is an instance or a Type parameter, and which elements carry it.
/// </summary>
public sealed class ReviewInputSource
{
    internal ReviewInputSource(string field, string parameterName, ReviewParameterLevel level, string label, params ReviewParameterHost[] hosts)
    {
        Field = field;
        ParameterName = parameterName;
        Level = level;
        Label = label;
        Hosts = new ReadOnlyCollection<ReviewParameterHost>(hosts);
    }

    /// <summary>The whitelisted rule field, e.g. <c>zone.sprinklered</c>.</summary>
    public string Field { get; }

    public string ParameterName { get; }
    public ReviewParameterLevel Level { get; }

    /// <summary>The Chinese name of what the parameter holds, for messages.</summary>
    public string Label { get; }

    public IReadOnlyList<ReviewParameterHost> Hosts { get; }

    public override string ToString() => $"{Field} ← {ParameterName}";
}

/// <summary>
/// The parameters the review reads its inputs from. Spec 19 items 4–6 (the company's shared
/// parameter GUIDs, the rating's unit and type, where zone and building inputs come from) are still
/// open, so the parameters are found by name — the same names the checks already use — and any
/// data type is accepted as long as the value can be understood: a Yes/No, an integer or a text.
/// </summary>
/// <remarks>
/// Building inputs are Project Information parameters; zone inputs are Area parameters, because the
/// Areas are what the tool wrote for each 區劃 and what the user sees in the Area Plan. Fields the
/// model itself provides (<c>zone.area</c>, <c>element.category</c>, <c>opening.kind</c>…) have no
/// source here: they can never be typed in (spec 11.4 step 2: Revit Area is the actual value).
/// <c>building.height</c> is one of those: the model's own extent is the height, so it comes from
/// <see cref="ReviewModelFacts"/> and not from a Project Information parameter. 梁 carry
/// 設計防火時效 like the other 主要構造, because 第70條 states a required rating for them; what 梁
/// have no answer for is <em>deriving</em> that design value from the Type's size, since 第71～73條
/// give 樑 no dimensional threshold. The batch panel therefore lists 梁 but leaves their 推定時效
/// empty for the user to fill in.
/// </remarks>
public static class ReviewInputSources
{
    public const string FireResistiveConstruction = "防火檢討_防火構造建築物";
    public const string BuildingUse = "建築物用途類組";
    public const string FloorsAboveGround = "地上層數";
    public const string ZoneUse = "防火檢討_區劃用途";
    public const string Sprinklered = "防火檢討_自動滅火設備";
    public const string FloorNumber = "防火檢討_所在樓層序";

    /// <summary>
    /// 牆／天花板類型的室內裝修耐燃等級。檢討時依區劃內實際構件彙總成
    /// zone.interiorFinish；不再由 Area 實體人工宣告。
    /// </summary>
    public const string InteriorFinish = "防火檢討_室內裝修等級";

    /// <summary>
    /// 挑空是否避難層通達其直上層或直下層（第79條之2第3項第一款）。只有挑空需要填，沒有任何規則讀它——
    /// 判定寫在 <see cref="Checks.AtriumExemption"/>（垂直區劃規格 §3.6、決議 24、27）。第3項的另外
    /// 兩個事實（連跨樓層數、連通區劃面積）由各樓層的區劃推得，沒有參數（決議 35）。
    /// </summary>
    public const string LinksRefugeFloor = "防火檢討_避難層通達";

    /// <summary>
    /// 本區劃是否為建築物構造或設備上無法區劃分隔之部分（第79條之1）。同上，沒有任何規則讀它——
    /// 判定寫在 <see cref="Checks.Article79_1Exemption"/>（第79條之1規格 §6、決議 1、9）。只有
    /// 觀眾席、生產線、教室、體育館、零售市場、停車空間需要填。
    /// </summary>
    public const string CannotBeSubdivided = "防火檢討_無法區劃分隔";

    public static readonly IReadOnlyList<ReviewParameterHost> MemberHosts = new ReadOnlyCollection<ReviewParameterHost>(new[]
    {
        ReviewParameterHost.Walls, ReviewParameterHost.Columns, ReviewParameterHost.StructuralFraming,
        ReviewParameterHost.Floors
    });

    public static readonly IReadOnlyList<ReviewParameterHost> OpeningHosts = new ReadOnlyCollection<ReviewParameterHost>(new[]
    {
        ReviewParameterHost.Doors, ReviewParameterHost.Windows, ReviewParameterHost.CurtainPanels
    });

    /// <summary>
    /// Who answers 設計防火時效: the 主要構造, and 帷幕嵌板 as well. A panel is no 主要構造 — 第70條 does
    /// not reach it — but 第79條第4項 and 第79條之3第2項 measure the 交接帶 by the rating of the panels
    /// themselves, so the parameter has to be bound to Curtain Panels for a curtain wall to be
    /// reviewable at all (帷幕牆規格 §6). Curtain Wall Mullions carry no rating of their own and are
    /// deliberately left out.
    /// </summary>
    public static readonly IReadOnlyList<ReviewParameterHost> FireRatingHosts =
        new ReadOnlyCollection<ReviewParameterHost>(
            MemberHosts.Concat(new[] { ReviewParameterHost.CurtainPanels }).ToArray());

    public static IReadOnlyList<ReviewInputSource> All { get; } = new ReadOnlyCollection<ReviewInputSource>(new[]
    {
        new ReviewInputSource("building.fireResistiveConstruction", FireResistiveConstruction, ReviewParameterLevel.Instance, "是否為防火構造建築物", ReviewParameterHost.ProjectInformation),
        new ReviewInputSource("building.use", BuildingUse, ReviewParameterLevel.Instance, "建築物用途類組", ReviewParameterHost.ProjectInformation),
        new ReviewInputSource("building.floorsAboveGround", FloorsAboveGround, ReviewParameterLevel.Instance, "地上層數", ReviewParameterHost.ProjectInformation),
        new ReviewInputSource("zone.use", ZoneUse, ReviewParameterLevel.Instance, "區劃用途", ReviewParameterHost.Areas),
        new ReviewInputSource("zone.sprinklered", Sprinklered, ReviewParameterLevel.Instance, "是否設有自動滅火設備", ReviewParameterHost.Areas),
        new ReviewInputSource("zone.floorNumber", FloorNumber, ReviewParameterLevel.Instance, "所在樓層序", ReviewParameterHost.Areas),
        new ReviewInputSource("zone.interiorFinish", InteriorFinish, ReviewParameterLevel.Type, "室內裝修耐燃等級", ReviewParameterHost.Walls, ReviewParameterHost.Ceilings),
        new ReviewInputSource("element.providedFireRating", FireRatingParameters.Provided, ReviewParameterLevel.Type, "設計／認證防火時效", FireRatingHosts.ToArray()),
        new ReviewInputSource("opening.providedFireProtection", FireProtectionParameters.Provided, ReviewParameterLevel.Type, "設計防火保護", OpeningHosts.ToArray()),

        // 第79條第1項之阻熱性 (垂直區劃規格決議 38). Read by tw-bcr-79-opening-insulation, so it is a
        // required parameter like 設計防火保護: every 區劃 boundary's 防火設備 owes it.
        new ReviewInputSource("opening.providedInsulation", InsulationParameters.Provided, ReviewParameterLevel.Type, "防火設備一小時以上阻熱性", OpeningHosts.ToArray()),

        // 第79條之4 的兩路作答（帷幕牆規格 §3.3、決議 16）。只有種類需要自己的參數：玻璃那一路讀的
        // junction.minFireProtection 就是上面那個 設計防火保護，同一個參數答兩個欄位，前置檢查看的是
        // 參數有沒有綁，所以不必再登錄一次。種類沒綁時 CW-O 兩條規則都判不出適用與否，這是必要參數。
        new ReviewInputSource("junction.panelKind", CurtainPanelKindParameters.Provided, ReviewParameterLevel.Type, "帷幕嵌板種類（實心／玻璃）", ReviewParameterHost.CurtainPanels),

        // 第79條之2 (垂直區劃文件 §6). Two fields, and 設計防火時效 therefore appears twice in this list:
        // the same parameter answers 第70條 for a 主要構造 and 第79條之2第1項 for a 管道間維修門, but they
        // are different fields with different categories, so neither entry can stand for the other.
        // 維修門 are doors and nothing else (垂直區劃文件 §4), which is why this one is bound to 門 alone
        // while 遮煙性能 follows every opening — 昇降機道出入口 may be a 門, a 窗 or a 帷幕嵌板.
        new ReviewInputSource("shaft.providedFireRating", FireRatingParameters.Provided, ReviewParameterLevel.Type, "管道間維修門之設計防火時效", ReviewParameterHost.Doors),
        new ReviewInputSource("shaft.providedSmokeProtection", SmokeProtectionParameters.Provided, ReviewParameterLevel.Type, "遮煙性能", OpeningHosts.ToArray()),

        // 第79條之2第3項第一款 (垂直區劃文件 §3.6、§6). An Area instance fact like 所在樓層序, because
        // only the designer can state it. No rule reads it — 第3項 is a classification, not a
        // requirement — so NeededBy, which is rule-driven, leaves it out of the pre-review check on
        // purpose: a project with no 挑空 must still be able to start a review without binding it
        // (決議 27). 連跨樓層數 and 連通區劃面積 have no parameter: they are traced through the storeys
        // (ReviewInputAssembler.AtriumInputs, 決議 35).
        new ReviewInputSource("zone.linksRefugeFloor", LinksRefugeFloor, ReviewParameterLevel.Instance, "挑空是否避難層通達其直上層或直下層", ReviewParameterHost.Areas),

        // 第79條之1 之無法區劃分隔部分 (第79條之1規格 §6). Same shape and same reason as the two
        // above: an Area instance fact only the designer can state, read by no rule, and therefore
        // left out of NeededBy so a project with none of the six 區劃用途 can still start a review
        // without binding it (決議 9).
        new ReviewInputSource("zone.cannotBeSubdivided", CannotBeSubdivided, ReviewParameterLevel.Instance, "是否為無法區劃分隔之部分", ReviewParameterHost.Areas)
    });

    public static ReviewInputSource? For(string field) =>
        All.FirstOrDefault(x => string.Equals(x.Field, field, StringComparison.Ordinal));

    /// <summary>
    /// Every parameter name the review may read, for the adapter. Distinct: one parameter can answer
    /// more than one field (設計防火時效 answers both <c>element.providedFireRating</c> and
    /// <c>shaft.providedFireRating</c>), and the adapter looks a name up once.
    /// </summary>
    public static IEnumerable<string> ParameterNames =>
        All.Select(x => x.ParameterName).Distinct(StringComparer.Ordinal);

    public static ReviewParameterHost HostOf(CandidateCategory category) => category switch
    {
        CandidateCategory.Wall => ReviewParameterHost.Walls,
        CandidateCategory.Column => ReviewParameterHost.Columns,
        CandidateCategory.StructuralFraming => ReviewParameterHost.StructuralFraming,
        CandidateCategory.Floor => ReviewParameterHost.Floors,
        CandidateCategory.Door => ReviewParameterHost.Doors,
        CandidateCategory.Window => ReviewParameterHost.Windows,
        CandidateCategory.CurtainPanel => ReviewParameterHost.CurtainPanels,
        _ => throw new ArgumentOutOfRangeException(nameof(category))
    };

    public static string Label(ReviewParameterHost host) => host switch
    {
        ReviewParameterHost.ProjectInformation => "專案資訊",
        ReviewParameterHost.Areas => "面積",
        ReviewParameterHost.Walls => "牆",
        ReviewParameterHost.Columns => "柱",
        ReviewParameterHost.StructuralFraming => "結構構架（梁）",
        ReviewParameterHost.Floors => "樓板",
        ReviewParameterHost.Ceilings => "天花板",
        ReviewParameterHost.Doors => "門",
        ReviewParameterHost.Windows => "窗",
        ReviewParameterHost.CurtainPanels => "帷幕嵌板",
        _ => host.ToString()
    };

    public static string Label(ReviewParameterLevel level) => level switch
    {
        ReviewParameterLevel.Instance => "實體參數",
        ReviewParameterLevel.Type => "類型參數",
        _ => "實體或類型參數"
    };

    /// <summary>
    /// The fields a rule set reads: its applicability conditions, the actual and required sides of
    /// its requirements and its exemptions. Evidence-only fields are left out — a rule does not stop
    /// working because a field it merely reports is empty.
    /// </summary>
    public static IReadOnlyList<RuleFieldDefinition> FieldsUsedBy(CompiledRuleSet ruleSet)
    {
        if (ruleSet is null) throw new ArgumentNullException(nameof(ruleSet));

        return ruleSet.Rules
            .SelectMany(rule => rule.AppliesWhen.Fields
                .Concat(new[] { rule.Requirement.Actual })
                .Concat(rule.Requirement.Required.Fields)
                .Concat(rule.Exemptions.SelectMany(x => x.Fields)))
            .GroupBy(x => x.Name, StringComparer.Ordinal)
            .Select(g => g.First())
            .OrderBy(x => x.Name, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>The parameters the rule set needs the model to carry, in field order.</summary>
    public static IReadOnlyList<ReviewInputSource> NeededBy(CompiledRuleSet ruleSet) =>
        FieldsUsedBy(ruleSet).Select(f => For(f.Name)).Where(x => x is not null).Select(x => x!).ToList();
}
