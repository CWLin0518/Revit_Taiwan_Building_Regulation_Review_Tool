using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BuildingRegulationReview.Application.Candidates;

namespace BuildingRegulationReview.Application.Checks;

/// <summary>The structural material a Type is built of, as 建築技術規則第71～73條 divides constructions.</summary>
public enum StructuralMaterial
{
    /// <summary>RC — 鋼筋混凝土造.</summary>
    ReinforcedConcrete,

    /// <summary>SRC — 鋼骨鋼筋混凝土造.</summary>
    SteelReinforcedConcrete,

    /// <summary>SC — 鋼骨造. Rated by its 防火被覆 thickness, never by the member's own section.</summary>
    Steel
}

/// <summary>The parameters the derivation reads besides the Type's own dimensions.</summary>
public static class StructuralMaterialParameters
{
    /// <summary>RC／SRC／SC on the Type.</summary>
    public const string Material = "結構材料";

    /// <summary>
    /// 防火被覆單面厚度 on the Type, needed only by <see cref="StructuralMaterial.Steel"/>: a steel
    /// member's rating comes from what covers it, not from its section.
    /// </summary>
    public const string Cover = "防火被覆厚度";
}

/// <summary>Reads 結構材料 as the user may have typed it.</summary>
public static class StructuralMaterialText
{
    private static readonly IReadOnlyDictionary<string, StructuralMaterial> Known =
        new Dictionary<string, StructuralMaterial>(StringComparer.OrdinalIgnoreCase)
        {
            ["RC"] = StructuralMaterial.ReinforcedConcrete,
            ["鋼筋混凝土造"] = StructuralMaterial.ReinforcedConcrete,
            ["鋼筋混凝土"] = StructuralMaterial.ReinforcedConcrete,
            ["SRC"] = StructuralMaterial.SteelReinforcedConcrete,
            ["鋼骨鋼筋混凝土造"] = StructuralMaterial.SteelReinforcedConcrete,
            ["鋼骨鋼筋混凝土"] = StructuralMaterial.SteelReinforcedConcrete,
            ["SC"] = StructuralMaterial.Steel,
            ["鋼骨造"] = StructuralMaterial.Steel,
            ["鋼骨"] = StructuralMaterial.Steel,
            ["鋼構造"] = StructuralMaterial.Steel
        };

    /// <summary>Every value the panel offers, in the order it offers them.</summary>
    public static IReadOnlyList<StructuralMaterial> All { get; } = new[]
    {
        StructuralMaterial.ReinforcedConcrete, StructuralMaterial.SteelReinforcedConcrete, StructuralMaterial.Steel
    };

    /// <summary>The short code written to the parameter.</summary>
    public static string Code(StructuralMaterial material) => material switch
    {
        StructuralMaterial.ReinforcedConcrete => "RC",
        StructuralMaterial.SteelReinforcedConcrete => "SRC",
        StructuralMaterial.Steel => "SC",
        _ => throw new ArgumentOutOfRangeException(nameof(material))
    };

    public static string Label(StructuralMaterial material) => material switch
    {
        StructuralMaterial.ReinforcedConcrete => "RC（鋼筋混凝土造）",
        StructuralMaterial.SteelReinforcedConcrete => "SRC（鋼骨鋼筋混凝土造）",
        StructuralMaterial.Steel => "SC（鋼骨造）",
        _ => throw new ArgumentOutOfRangeException(nameof(material))
    };

    /// <summary>The material, or null when the text is blank or is not one of the three.</summary>
    public static StructuralMaterial? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        return Known.TryGetValue(text!.Trim(), out var material) ? material : (StructuralMaterial?)null;
    }
}

public enum FireRatingDerivationKind
{
    /// <summary>A rating follows from the material and the dimension.</summary>
    Derived,

    /// <summary>The construction is below every threshold: it has no 防火時效 to write.</summary>
    NotRated,

    /// <summary>結構材料 is blank or not RC／SRC／SC.</summary>
    MaterialMissing,

    /// <summary>The Type does not report the dimension the clause measures.</summary>
    DimensionMissing,

    /// <summary>SC without 防火被覆厚度: the section says nothing about the rating.</summary>
    CoverMissing,

    /// <summary>A category the clauses give no dimensional threshold for, e.g. 樑.</summary>
    NotDerivable
}

/// <summary>
/// One Type's 防火時效 as the clauses give it, with the number and the clause it came from. This is a
/// proposal for the 設計防火時效 parameter, not a review verdict: the review still reads the parameter
/// afterwards, so what the user accepted is what gets checked (spec 11.5 step 3).
/// </summary>
public sealed class FireRatingDerivation
{
    private FireRatingDerivation(FireRatingDerivationKind kind, double? minutes, string? legalReference, string explanation)
    {
        Kind = kind;
        Minutes = minutes;
        LegalReference = legalReference;
        Explanation = explanation;
    }

    public FireRatingDerivationKind Kind { get; }

    /// <summary>The derived rating in minutes; null unless <see cref="Kind"/> is Derived.</summary>
    public double? Minutes { get; }

    /// <summary>The clause the threshold came from; null when nothing was derived.</summary>
    public string? LegalReference { get; }

    /// <summary>Why this rating, or why none — shown in the panel's 依據 column.</summary>
    public string Explanation { get; }

    public bool HasRating => Kind == FireRatingDerivationKind.Derived;

    /// <summary>The text written to 防火檢討_設計防火時效, e.g. "60 min".</summary>
    public string? ParameterText => HasRating
        ? Minutes!.Value.ToString("0.###", CultureInfo.InvariantCulture) + " min"
        : null;

    internal static FireRatingDerivation Rated(double minutes, string legalReference, string explanation) =>
        new(FireRatingDerivationKind.Derived, minutes, legalReference, explanation);

    internal static FireRatingDerivation None(FireRatingDerivationKind kind, string explanation) =>
        new(kind, null, null, explanation);

    public override string ToString() => HasRating ? $"{ParameterText}（{LegalReference}）" : Explanation;
}

/// <summary>
/// 結構材料＋斷面尺寸 → 防火時效, strictly as 建築技術規則建築設計施工編第三章第三節 writes it. Every
/// threshold below carries the clause it came from; nothing is interpolated between them.
/// </summary>
/// <remarks>
/// <para>RC／SRC, by clause:</para>
/// <list type="bullet">
/// <item>牆壁 — 第72條一(一) 厚10cm以上→2hr；第73條一(一) 厚7cm以上→1hr.</item>
/// <item>樓地板 — 第72條四(一) 厚10cm以上→2hr；第73條四(一) 厚7cm以上→1hr.</item>
/// <item>柱 — 第71條二 短邊40cm以上→3hr；第72條二 短邊25cm以上→2hr；第73條二(一) 無尺寸下限→1hr.</item>
/// </list>
/// <para>
/// SC is rated by 防火被覆單面厚度 (鐵絲網水泥粉刷), never by its own section: 第72條一(二) 4cm→2hr、
/// 第73條一(二) 3cm→1hr for 牆壁; 第72條四(二) 5cm→2hr、第73條四(二) 4cm→1hr for 樓地板;
/// 第71條二(三) 9cm＋短邊40cm→3hr、第72條二(二) 5cm＋短邊25cm→2hr、第73條二(二) 4cm→1hr for 柱.
/// 被覆以磚、石或空心磚 has its own, larger thresholds; the cover parameter is read as 鐵絲網水泥粉刷,
/// which is the conservative reading, and the explanation says so.
/// </para>
/// <para>
/// 樑 is deliberately absent: 第71條三、第72條三、第73條三 all state the 樑 requirement as the bare
/// construction type with no dimensional threshold, so no rating follows from a beam Type's size.
/// </para>
/// </remarks>
public static class FireRatingDeriver
{
    /// <summary>The categories a rating can be derived for.</summary>
    public static IReadOnlyList<CandidateCategory> DerivableCategories { get; } = new[]
    {
        CandidateCategory.Wall, CandidateCategory.Column, CandidateCategory.Floor
    };

    public static bool IsDerivable(CandidateCategory category) => DerivableCategories.Contains(category);

    /// <summary>What the clause measures for this category, for the panel's column heading.</summary>
    public static string DimensionLabel(CandidateCategory category) => category switch
    {
        CandidateCategory.Wall => "牆厚",
        CandidateCategory.Floor => "板厚",
        CandidateCategory.Column => "柱短邊",
        _ => "尺寸"
    };

    /// <summary>
    /// The rating the clauses give, or why they give none.
    /// </summary>
    /// <param name="category">Wall、Column or Floor; anything else is <see cref="FireRatingDerivationKind.NotDerivable"/>.</param>
    /// <param name="material">結構材料; null when the parameter is blank or unrecognised.</param>
    /// <param name="dimensionMeters">牆厚／板厚／柱短邊, in metres.</param>
    /// <param name="coverMeters">防火被覆單面厚度 in metres; only SC uses it.</param>
    public static FireRatingDerivation Derive(
        CandidateCategory category,
        StructuralMaterial? material,
        double? dimensionMeters,
        double? coverMeters = null)
    {
        if (!IsDerivable(category))
            return FireRatingDerivation.None(FireRatingDerivationKind.NotDerivable,
                category == CandidateCategory.StructuralFraming
                    ? "第71～73條的「樑」款未設尺寸門檻，無法由斷面尺寸推定防火時效。"
                    : $"{CandidateCategories.Label(category)}不適用尺寸推定。");

        if (material is null)
            return FireRatingDerivation.None(FireRatingDerivationKind.MaterialMissing,
                $"未填 {StructuralMaterialParameters.Material}（RC／SRC／SC），無法判定適用條文。");

        return material.Value == StructuralMaterial.Steel
            ? Steel(category, dimensionMeters, coverMeters)
            : Concrete(category, material.Value, dimensionMeters);
    }

    private static FireRatingDerivation Concrete(CandidateCategory category, StructuralMaterial material, double? dimensionMeters)
    {
        var code = StructuralMaterialText.Code(material);

        if (category == CandidateCategory.Column)
        {
            // 第73條二(一) sets no minimum, so an RC／SRC column is rated whatever its section;
            // the section only decides whether it reaches 2 or 3 hours.
            if (dimensionMeters is not double shortSide)
                return FireRatingDerivation.None(FireRatingDerivationKind.DimensionMissing,
                    "無法取得柱短邊尺寸，第71條二與第72條二的短邊門檻無從判定。");

            if (shortSide >= 0.40)
                return FireRatingDerivation.Rated(180, "建築技術規則建築設計施工編第71條第2款",
                    $"{code} 柱短邊 {Cm(shortSide)}，達 40cm 以上 → 三小時。");
            if (shortSide >= 0.25)
                return FireRatingDerivation.Rated(120, "建築技術規則建築設計施工編第72條第2款",
                    $"{code} 柱短邊 {Cm(shortSide)}，達 25cm 以上 → 二小時。");
            return FireRatingDerivation.Rated(60, "建築技術規則建築設計施工編第73條第2款第1目",
                $"{code} 柱短邊 {Cm(shortSide)}，未達 25cm；第73條二(一) 未設尺寸下限 → 一小時。");
        }

        if (dimensionMeters is not double thickness)
            return FireRatingDerivation.None(FireRatingDerivationKind.DimensionMissing,
                $"無法取得{DimensionLabel(category)}，無法比對第72／73條的厚度門檻。");

        var (two, one) = category == CandidateCategory.Wall
            ? ("建築技術規則建築設計施工編第72條第1款第1目", "建築技術規則建築設計施工編第73條第1款第1目")
            : ("建築技術規則建築設計施工編第72條第4款第1目", "建築技術規則建築設計施工編第73條第4款第1目");

        if (thickness >= 0.10)
            return FireRatingDerivation.Rated(120, two, $"{code} {DimensionLabel(category)} {Cm(thickness)}，達 10cm 以上 → 二小時。");
        if (thickness >= 0.07)
            return FireRatingDerivation.Rated(60, one, $"{code} {DimensionLabel(category)} {Cm(thickness)}，達 7cm 以上 → 一小時。");

        return FireRatingDerivation.None(FireRatingDerivationKind.NotRated,
            $"{code} {DimensionLabel(category)} {Cm(thickness)}，未達第73條的 7cm 門檻 → 無防火時效。");
    }

    private static FireRatingDerivation Steel(CandidateCategory category, double? dimensionMeters, double? coverMeters)
    {
        if (coverMeters is not double cover)
            return FireRatingDerivation.None(FireRatingDerivationKind.CoverMissing,
                $"SC（鋼骨造）的防火時效取決於{StructuralMaterialParameters.Cover}，" +
                "第71～73條不以鋼骨本身斷面計算，請填被覆單面厚度。");

        const string Medium = "（以鐵絲網水泥粉刷計；覆磚、石或空心磚者門檻較高）";

        if (category == CandidateCategory.Column)
        {
            var shortSide = dimensionMeters;
            if (cover >= 0.09 && shortSide is double s3 && s3 >= 0.40)
                return FireRatingDerivation.Rated(180, "建築技術規則建築設計施工編第71條第2款第3目",
                    $"SC 被覆 {Cm(cover)}、柱短邊 {Cm(s3)}，達 9cm 與 40cm 以上 → 三小時{Medium}。");
            if (cover >= 0.05 && shortSide is double s2 && s2 >= 0.25)
                return FireRatingDerivation.Rated(120, "建築技術規則建築設計施工編第72條第2款第2目",
                    $"SC 被覆 {Cm(cover)}、柱短邊 {Cm(s2)}，達 5cm 與 25cm 以上 → 二小時。");
            if (cover >= 0.04)
                return FireRatingDerivation.Rated(60, "建築技術規則建築設計施工編第73條第2款第2目",
                    $"SC 被覆 {Cm(cover)}，達 4cm 以上 → 一小時{Medium}。");
            return FireRatingDerivation.None(FireRatingDerivationKind.NotRated,
                $"SC 被覆 {Cm(cover)}，未達第73條二(二) 的 4cm 門檻 → 無防火時效。");
        }

        var (twoCm, oneCm, twoRef, oneRef) = category == CandidateCategory.Wall
            ? (0.04, 0.03, "建築技術規則建築設計施工編第72條第1款第2目", "建築技術規則建築設計施工編第73條第1款第2目")
            : (0.05, 0.04, "建築技術規則建築設計施工編第72條第4款第2目", "建築技術規則建築設計施工編第73條第4款第2目");

        if (cover >= twoCm)
            return FireRatingDerivation.Rated(120, twoRef,
                $"SC {DimensionLabel(category)}不計；被覆單面 {Cm(cover)}，達 {Cm(twoCm)} 以上 → 二小時{Medium}。");
        if (cover >= oneCm)
            return FireRatingDerivation.Rated(60, oneRef,
                $"SC {DimensionLabel(category)}不計；被覆單面 {Cm(cover)}，達 {Cm(oneCm)} 以上 → 一小時{Medium}。");

        return FireRatingDerivation.None(FireRatingDerivationKind.NotRated,
            $"SC 被覆單面 {Cm(cover)}，未達 {Cm(oneCm)} 門檻 → 無防火時效。");
    }

    private static string Cm(double meters) =>
        (meters * 100).ToString("0.##", CultureInfo.InvariantCulture) + "cm";
}
