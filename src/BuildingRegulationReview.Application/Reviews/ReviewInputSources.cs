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

    public static readonly IReadOnlyList<ReviewParameterHost> MemberHosts = new ReadOnlyCollection<ReviewParameterHost>(new[]
    {
        ReviewParameterHost.Walls, ReviewParameterHost.Columns, ReviewParameterHost.StructuralFraming,
        ReviewParameterHost.Floors
    });

    public static readonly IReadOnlyList<ReviewParameterHost> OpeningHosts = new ReadOnlyCollection<ReviewParameterHost>(new[]
    {
        ReviewParameterHost.Doors, ReviewParameterHost.Windows, ReviewParameterHost.CurtainPanels
    });

    public static IReadOnlyList<ReviewInputSource> All { get; } = new ReadOnlyCollection<ReviewInputSource>(new[]
    {
        new ReviewInputSource("building.fireResistiveConstruction", FireResistiveConstruction, ReviewParameterLevel.Instance, "是否為防火構造建築物", ReviewParameterHost.ProjectInformation),
        new ReviewInputSource("building.use", BuildingUse, ReviewParameterLevel.Instance, "建築物用途類組", ReviewParameterHost.ProjectInformation),
        new ReviewInputSource("building.floorsAboveGround", FloorsAboveGround, ReviewParameterLevel.Instance, "地上層數", ReviewParameterHost.ProjectInformation),
        new ReviewInputSource("zone.use", ZoneUse, ReviewParameterLevel.Instance, "區劃用途", ReviewParameterHost.Areas),
        new ReviewInputSource("zone.sprinklered", Sprinklered, ReviewParameterLevel.Instance, "是否設有自動滅火設備", ReviewParameterHost.Areas),
        new ReviewInputSource("zone.floorNumber", FloorNumber, ReviewParameterLevel.Instance, "所在樓層序", ReviewParameterHost.Areas),
        new ReviewInputSource("element.providedFireRating", FireRatingParameters.Provided, ReviewParameterLevel.Type, "設計／認證防火時效", MemberHosts.ToArray()),
        new ReviewInputSource("opening.providedFireProtection", FireProtectionParameters.Provided, ReviewParameterLevel.Type, "設計防火保護", OpeningHosts.ToArray())
    });

    public static ReviewInputSource? For(string field) =>
        All.FirstOrDefault(x => string.Equals(x.Field, field, StringComparison.Ordinal));

    /// <summary>Every parameter name the review may read, for the adapter.</summary>
    public static IEnumerable<string> ParameterNames => All.Select(x => x.ParameterName);

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
