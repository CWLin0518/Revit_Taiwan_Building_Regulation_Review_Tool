using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using BuildingRegulationReview.Application.Candidates;
using BuildingRegulationReview.Application.Checks;
using BuildingRegulationReview.Application.Reviews;

namespace BuildingRegulationReview.Application.Parameters;

/// <summary>
/// One element Type as the batch panel shows it: what the model already holds, and what the clauses
/// would derive. Pure data (spec 15) — the adapter fills it, the panel edits it, and
/// <see cref="FireReviewParameterEdit"/> carries the result back.
/// </summary>
/// <remarks>
/// A row is a Type, not an element: 防火檢討_設計防火時效, 結構材料, 防火被覆厚度 and the openings'
/// 防火檢討_設計防火保護 are all Type parameters, so one edit reaches every instance of that Type in
/// the project — not only the ones visible in the view the list was collected from.
/// <see cref="InstanceCount"/> is what the view showed; <see cref="ProjectInstanceCount"/> is what
/// the edit actually affects.
/// </remarks>
public sealed class FireReviewTypeRow
{
    public FireReviewTypeRow(
        string typeUniqueId,
        CandidateCategory category,
        string typeName,
        string? familyName = null,
        int instanceCount = 0,
        int projectInstanceCount = 0,
        double? dimensionMeters = null,
        string? material = null,
        double? coverMeters = null,
        string? providedRating = null,
        bool? providedProtection = null,
        bool? providedSmokeProtection = null,
        FireReviewTypeParameters present = FireReviewTypeParameters.None,
        string? panelKind = null)
    {
        if (string.IsNullOrWhiteSpace(typeUniqueId)) throw new ArgumentException("Type UniqueId is required.", nameof(typeUniqueId));
        if (instanceCount < 0) throw new ArgumentOutOfRangeException(nameof(instanceCount));
        if (projectInstanceCount < 0) throw new ArgumentOutOfRangeException(nameof(projectInstanceCount));
        if (dimensionMeters is double d && (double.IsNaN(d) || double.IsInfinity(d) || d < 0))
            throw new ArgumentOutOfRangeException(nameof(dimensionMeters));
        if (coverMeters is double c && (double.IsNaN(c) || double.IsInfinity(c) || c < 0))
            throw new ArgumentOutOfRangeException(nameof(coverMeters));

        TypeUniqueId = typeUniqueId.Trim();
        Category = category;
        TypeName = string.IsNullOrWhiteSpace(typeName) ? "（未命名類型）" : typeName.Trim();
        FamilyName = string.IsNullOrWhiteSpace(familyName) ? null : familyName!.Trim();
        InstanceCount = instanceCount;
        ProjectInstanceCount = Math.Max(projectInstanceCount, instanceCount);
        DimensionMeters = dimensionMeters;
        Material = string.IsNullOrWhiteSpace(material) ? null : material!.Trim();
        CoverMeters = coverMeters;
        ProvidedRating = string.IsNullOrWhiteSpace(providedRating) ? null : providedRating!.Trim();
        ProvidedProtection = providedProtection;
        ProvidedSmokeProtection = providedSmokeProtection;
        Present = present;
        PanelKind = string.IsNullOrWhiteSpace(panelKind) ? null : panelKind!.Trim();
    }

    public string TypeUniqueId { get; }
    public CandidateCategory Category { get; }
    public string TypeName { get; }
    public string? FamilyName { get; }

    /// <summary>How many instances of this Type the collected view showed.</summary>
    public int InstanceCount { get; }

    /// <summary>How many instances the whole project holds — the real reach of an edit.</summary>
    public int ProjectInstanceCount { get; }

    /// <summary>牆厚／板厚／柱短邊 in metres, as the Type reports it.</summary>
    public double? DimensionMeters { get; }

    /// <summary>結構材料 exactly as the parameter holds it.</summary>
    public string? Material { get; }

    /// <summary>防火被覆厚度 in metres.</summary>
    public double? CoverMeters { get; }

    /// <summary>防火檢討_設計防火時效 exactly as the parameter holds it.</summary>
    public string? ProvidedRating { get; }

    /// <summary>
    /// 防火檢討_設計防火保護 as the Type's Yes/No parameter holds it (openings only): true is ticked,
    /// false is a box that exists but is not ticked, and null is a Type that does not carry the
    /// parameter at all. The review reads false as 否, but null must stay null here so an untouched
    /// row writes nothing back.
    /// </summary>
    public bool? ProvidedProtection { get; }

    /// <summary>
    /// 防火檢討_遮煙性能 as the Type's Yes/No parameter holds it (openings only), read the same way as
    /// <see cref="ProvidedProtection"/>: 遮煙性能 is a separate question from 防火門窗 and 第79條之2第1項
    /// asks a 昇降機道's 防火設備 both (垂直區劃文件 §6).
    /// </summary>
    public bool? ProvidedSmokeProtection { get; }

    /// <summary>Which of the review parameters this Type actually carries.</summary>
    public FireReviewTypeParameters Present { get; }

    /// <summary>
    /// 防火檢討_嵌板種類 exactly as the parameter holds it (帷幕嵌板 only). Null is 未宣告, which is not
    /// the same as either kind: the row then shows both gaps rather than guessing (決議 16).
    /// </summary>
    public string? PanelKind { get; }

    /// <summary>實心／玻璃, or null when the declaration is blank or is neither.</summary>
    public CurtainPanelKind? ParsedPanelKind => CurtainPanelKinds.Parse(PanelKind);

    public bool IsOpening => CandidateCategories.IsOpening(Category);

    /// <summary>A 帷幕嵌板 whose 防火檢討_嵌板種類 has not been declared — it answers neither question yet.</summary>
    public bool AwaitsPanelKind => Category == CandidateCategory.CurtainPanel && ParsedPanelKind is null;

    /// <summary>
    /// Whether 結構材料＋尺寸 → 時效 is worth offering on this row: the clauses have to give the
    /// category a threshold, the row has to answer by 時效 at all, and an undeclared 帷幕嵌板 has to
    /// say which kind it is first.
    /// </summary>
    public bool SupportsDerivation => FireRatingDeriver.IsDerivable(Category) && CarriesRating && !AwaitsPanelKind;

    /// <summary>
    /// Whether this Type answers 設計防火時效. Every 主要構造 does; among the openings, 門 does because
    /// 第79條之2第1項 requires a 管道間之維修門 to reach one hour (垂直區劃文件 §6), and a 帷幕嵌板 does
    /// when it is declared 實心, because 第79條第4項 and 第79條之3第2項 measure the 交接帶 by the panels'
    /// own rating (帷幕牆規格 §6、決議 16). A 玻璃 panel answers 防火保護 instead, exactly like a 窗;
    /// 窗 answer 防火門窗 and 遮煙性能 only — no clause states a rating for them.
    /// </summary>
    /// <remarks>
    /// An undeclared 帷幕嵌板 carries both, so the panel shows both as gaps. Guessing either way would
    /// collapse 「實心嵌板漏填時效」(資料不足) into 「玻璃嵌板不是防火設備」(未符合), which is the whole
    /// reason 防火檢討_嵌板種類 exists.
    /// </remarks>
    public bool CarriesRating => Category == CandidateCategory.CurtainPanel
        ? ParsedPanelKind != CurtainPanelKind.Glazed
        : !IsOpening || Category != CandidateCategory.Window;

    /// <summary>
    /// 防火門窗 is asked of the openings, 可開啟嵌板 included — but not of a 帷幕嵌板 declared 實心,
    /// which is a piece of 構造 answering by its own 時效, not a 防火設備 (決議 16).
    /// </summary>
    public bool CarriesProtection => Category == CandidateCategory.CurtainPanel
        ? ParsedPanelKind != CurtainPanelKind.Solid
        : IsOpening;

    /// <summary>
    /// 遮煙性能 is asked of every opening: a 昇降機道 出入口 may be a 門, a 窗 or a 帷幕嵌板, and a
    /// 管道間 維修門 is a 門 (垂直區劃文件 §4).
    /// </summary>
    public bool CarriesSmokeProtection => IsOpening;

    public StructuralMaterial? ParsedMaterial => StructuralMaterialText.Parse(Material);

    public string CategoryLabel => CandidateCategories.Label(Category);

    public string DisplayName => FamilyName is null ? TypeName : $"{FamilyName}：{TypeName}";

    /// <summary>
    /// What the clauses derive for this row. 門、窗 and 梁 derive nothing; a 帷幕嵌板 derives on the
    /// 牆壁 thresholds (決議 16). A 玻璃 panel goes through the same call and comes back with nothing,
    /// because glass is not RC／SRC／SC and does not reach 第73條's 7cm either.
    /// </summary>
    public FireRatingDerivation Derivation =>
        FireRatingDeriver.Derive(Category, ParsedMaterial, DimensionMeters, CoverMeters);

    /// <summary>The derived rating differs from what the Type already holds, so applying it would change something.</summary>
    public bool WouldChangeRating
    {
        get
        {
            // A 玻璃 panel does not answer by 時效 at all, and an undeclared one has not said yet, so a
            // rating derived for either is never applied — the batch 「套用推定值」 must not put a number
            // into a parameter the review will not read.
            if (!SupportsDerivation) return false;
            var derived = Derivation;
            if (!derived.HasRating) return false;
            var current = ReviewInputAssembler.Rating(
                ProvidedRating is null ? ParameterReading.Empty : ParameterReading.OfText(ProvidedRating));
            return current.Kind != ProvidedFireRatingKind.Rated || current.Minutes != derived.Minutes;
        }
    }

    /// <summary>The edit that writes the derived rating, or null when there is nothing to write.</summary>
    public FireReviewParameterEdit? RatingEdit() =>
        SupportsDerivation && Derivation.ParameterText is string text
            ? FireReviewParameterEdit.OfText(TypeUniqueId, FireRatingParameters.Provided, text)
            : null;

    /// <summary>
    /// The edit that declares this 帷幕嵌板's kind, for the proposal the reader makes from the panel's
    /// material (決議 16). Null for anything that is not a 帷幕嵌板, and for <see cref="CurtainPanelKind.Opening"/>,
    /// which the reader recognises by category and never writes back.
    /// </summary>
    public FireReviewParameterEdit? PanelKindEdit(CurtainPanelKind kind) =>
        Category == CandidateCategory.CurtainPanel && CurtainPanelKinds.ParameterText(kind) is string text
            ? FireReviewParameterEdit.OfText(TypeUniqueId, CurtainPanelKindParameters.Provided, text)
            : null;

    public override string ToString() => $"{CategoryLabel}／{DisplayName}";
}

/// <summary>Which review parameters a Type carries, so the panel can say 「此類型沒有這個參數」.</summary>
[Flags]
public enum FireReviewTypeParameters
{
    None = 0,
    Rating = 1,
    Material = 2,
    Cover = 4,
    Protection = 8,

    /// <summary>防火檢討_遮煙性能 (第79條之2第1項).</summary>
    SmokeSeal = 16,

    /// <summary>防火檢討_嵌板種類 (決議 16); only 帷幕嵌板 are expected to carry it.</summary>
    PanelKind = 32
}

/// <summary>What kind of value one edit carries, so the adapter never has to guess from the text.</summary>
public enum FireReviewEditKind
{
    Text,
    Length,
    YesNo
}

/// <summary>One parameter value to write to one Type. The kinds are kept apart so the adapter never guesses.</summary>
public sealed class FireReviewParameterEdit
{
    private FireReviewParameterEdit(FireReviewEditKind kind, string elementUniqueId, string parameterName, string? text, double? lengthMeters, bool yesNo)
    {
        if (string.IsNullOrWhiteSpace(elementUniqueId)) throw new ArgumentException("Element UniqueId is required.", nameof(elementUniqueId));
        if (string.IsNullOrWhiteSpace(parameterName)) throw new ArgumentException("Parameter name is required.", nameof(parameterName));

        Kind = kind;
        ElementUniqueId = elementUniqueId.Trim();
        ParameterName = parameterName.Trim();
        Text = text;
        LengthMeters = lengthMeters;
        YesNo = yesNo;
    }

    public static FireReviewParameterEdit OfText(string elementUniqueId, string parameterName, string? value)
    {
        if (FireRatingParameters.IsRequiredParameter(parameterName))
            throw new ArgumentException($"{FireRatingParameters.Required} 是規則回寫用的參數，面板不得寫入。", nameof(parameterName));
        return new FireReviewParameterEdit(FireReviewEditKind.Text, elementUniqueId, parameterName, value ?? string.Empty, null, false);
    }

    public static FireReviewParameterEdit OfLength(string elementUniqueId, string parameterName, double? meters)
    {
        if (meters is double m && (double.IsNaN(m) || double.IsInfinity(m) || m < 0))
            throw new ArgumentOutOfRangeException(nameof(meters), "長度不能是負數或非有限值。");
        return new FireReviewParameterEdit(FireReviewEditKind.Length, elementUniqueId, parameterName, null, meters, false);
    }

    /// <summary>A ticked or unticked checkbox. Unticked is written as 0, which the review reads as 否.</summary>
    public static FireReviewParameterEdit OfYesNo(string elementUniqueId, string parameterName, bool value)
    {
        if (FireRatingParameters.IsRequiredParameter(parameterName))
            throw new ArgumentException($"{FireRatingParameters.Required} 是規則回寫用的參數，面板不得寫入。", nameof(parameterName));
        return new FireReviewParameterEdit(FireReviewEditKind.YesNo, elementUniqueId, parameterName, null, null, value);
    }

    public FireReviewEditKind Kind { get; }
    public string ElementUniqueId { get; }
    public string ParameterName { get; }

    /// <summary>The text to write; null unless this edit is a text. An empty string clears the parameter.</summary>
    public string? Text { get; }

    /// <summary>The length in metres; null unless this edit is a length.</summary>
    public double? LengthMeters { get; }

    /// <summary>Whether the box is ticked; meaningful only when <see cref="Kind"/> is Yes/No.</summary>
    public bool YesNo { get; }

    public bool IsLength => Kind == FireReviewEditKind.Length;

    public override string ToString() => Kind switch
    {
        FireReviewEditKind.Length => $"{ParameterName} = {LengthMeters?.ToString("0.###", CultureInfo.InvariantCulture) + " m" ?? "（清除）"}",
        FireReviewEditKind.YesNo => $"{ParameterName} = {(YesNo ? "是" : "否")}",
        _ => $"{ParameterName} = {Text}"
    };
}

/// <summary>The Types one view contributed, grouped and ordered the way the panel lists them.</summary>
public sealed class FireReviewTypeTable
{
    public FireReviewTypeTable(IEnumerable<FireReviewTypeRow>? rows, IEnumerable<string>? warnings = null)
    {
        Rows = new ReadOnlyCollection<FireReviewTypeRow>(
            (rows ?? Array.Empty<FireReviewTypeRow>())
            .GroupBy(r => r.TypeUniqueId, StringComparer.Ordinal)
            .Select(g => g.First())
            .OrderBy(r => CandidateCategories.Members.Contains(r.Category) ? 0 : 1)
            .ThenBy(r => r.Category)
            .ThenBy(r => r.DisplayName, StringComparer.CurrentCulture)
            .ToList());
        Warnings = new ReadOnlyCollection<string>((warnings ?? Array.Empty<string>()).ToList());
    }

    public IReadOnlyList<FireReviewTypeRow> Rows { get; }
    public IReadOnlyList<string> Warnings { get; }

    public IEnumerable<FireReviewTypeRow> Of(CandidateCategory category) => Rows.Where(r => r.Category == category);

    /// <summary>Rows whose derived rating differs from what the Type holds — what「套用推定值」would write.</summary>
    public IReadOnlyList<FireReviewTypeRow> Derivable => Rows.Where(r => r.WouldChangeRating).ToList();

    /// <summary>Rows that need 防火被覆厚度 before SC can be rated at all.</summary>
    public IReadOnlyList<FireReviewTypeRow> AwaitingCover =>
        Rows.Where(r => r.SupportsDerivation && r.Derivation.Kind == FireRatingDerivationKind.CoverMissing).ToList();

    /// <summary>
    /// Rows whose 結構材料 is blank or unrecognised. A 玻璃 panel is not one of them — it never answers
    /// by 時效, so it is not waiting on a material it will never be asked for (決議 16).
    /// </summary>
    public IReadOnlyList<FireReviewTypeRow> AwaitingMaterial =>
        Rows.Where(r => r.SupportsDerivation && r.Derivation.Kind == FireRatingDerivationKind.MaterialMissing).ToList();

    /// <summary>
    /// 帷幕嵌板 that have not declared 實心 or 玻璃 yet (決議 16). Until they do, CW-O cannot decide
    /// which question to ask of them, so the review answers 資料不足 — the panel lists these first.
    /// </summary>
    public IReadOnlyList<FireReviewTypeRow> AwaitingPanelKind => Rows.Where(r => r.AwaitsPanelKind).ToList();
}
