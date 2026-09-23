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
/// A row is a Type, not an element: 防火檢討_設計防火時效, 結構材料 and 防火被覆厚度 are Type
/// parameters, so one edit reaches every instance of that Type in the project — not only the ones
/// visible in the view the list was collected from. <see cref="InstanceCount"/> is what the view
/// showed; <see cref="ProjectInstanceCount"/> is what the edit actually affects.
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
        string? providedProtection = null,
        FireReviewTypeParameters present = FireReviewTypeParameters.None,
        IEnumerable<string>? instanceUniqueIds = null)
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
        ProvidedProtection = string.IsNullOrWhiteSpace(providedProtection) ? null : providedProtection!.Trim();
        Present = present;
        InstanceUniqueIds = new ReadOnlyCollection<string>(
            (instanceUniqueIds ?? Array.Empty<string>())
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToList());
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

    /// <summary>防火檢討_設計防火保護 exactly as the parameter holds it (openings only).</summary>
    public string? ProvidedProtection { get; }

    /// <summary>Which of the review parameters this Type actually carries.</summary>
    public FireReviewTypeParameters Present { get; }

    /// <summary>
    /// The instances of this Type the collected view showed. Filled for openings only, because
    /// 防火檢討_設計防火保護 is bound per instance — a Revit shared parameter has one binding, so it
    /// cannot be both — and 「同一種門可能有的是防火門、有的不是」 is exactly why. Editing an opening
    /// row therefore writes to these instances, not to the Type, and so reaches only what the view
    /// showed.
    /// </summary>
    public IReadOnlyList<string> InstanceUniqueIds { get; }

    public bool IsOpening => CandidateCategories.IsOpening(Category);

    public StructuralMaterial? ParsedMaterial => StructuralMaterialText.Parse(Material);

    public string CategoryLabel => CandidateCategories.Label(Category);

    public string DisplayName => FamilyName is null ? TypeName : $"{FamilyName}：{TypeName}";

    /// <summary>What the clauses derive for this row; openings and 梁 derive nothing.</summary>
    public FireRatingDerivation Derivation =>
        FireRatingDeriver.Derive(Category, ParsedMaterial, DimensionMeters, CoverMeters);

    /// <summary>The derived rating differs from what the Type already holds, so applying it would change something.</summary>
    public bool WouldChangeRating
    {
        get
        {
            var derived = Derivation;
            if (!derived.HasRating) return false;
            var current = ReviewInputAssembler.Rating(
                ProvidedRating is null ? ParameterReading.Empty : ParameterReading.OfText(ProvidedRating));
            return current.Kind != ProvidedFireRatingKind.Rated || current.Minutes != derived.Minutes;
        }
    }

    /// <summary>The edit that writes the derived rating, or null when there is nothing to write.</summary>
    public FireReviewParameterEdit? RatingEdit() =>
        Derivation.ParameterText is string text
            ? FireReviewParameterEdit.OfText(TypeUniqueId, FireRatingParameters.Provided, text)
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
    Protection = 8
}

/// <summary>One parameter value to write to one Type. Text and length are kept apart so the adapter never guesses.</summary>
public sealed class FireReviewParameterEdit
{
    private FireReviewParameterEdit(string elementUniqueId, string parameterName, string? text, double? lengthMeters)
    {
        if (string.IsNullOrWhiteSpace(elementUniqueId)) throw new ArgumentException("Element UniqueId is required.", nameof(elementUniqueId));
        if (string.IsNullOrWhiteSpace(parameterName)) throw new ArgumentException("Parameter name is required.", nameof(parameterName));

        ElementUniqueId = elementUniqueId.Trim();
        ParameterName = parameterName.Trim();
        Text = text;
        LengthMeters = lengthMeters;
    }

    public static FireReviewParameterEdit OfText(string elementUniqueId, string parameterName, string? value)
    {
        if (FireRatingParameters.IsRequiredParameter(parameterName))
            throw new ArgumentException($"{FireRatingParameters.Required} 是規則回寫用的參數，面板不得寫入。", nameof(parameterName));
        return new FireReviewParameterEdit(elementUniqueId, parameterName, value ?? string.Empty, null);
    }

    public static FireReviewParameterEdit OfLength(string elementUniqueId, string parameterName, double? meters)
    {
        if (meters is double m && (double.IsNaN(m) || double.IsInfinity(m) || m < 0))
            throw new ArgumentOutOfRangeException(nameof(meters), "長度不能是負數或非有限值。");
        return new FireReviewParameterEdit(elementUniqueId, parameterName, null, meters);
    }

    public string ElementUniqueId { get; }
    public string ParameterName { get; }

    /// <summary>The text to write; null when this edit is a length. An empty string clears the parameter.</summary>
    public string? Text { get; }

    /// <summary>The length in metres; null when this edit is a text.</summary>
    public double? LengthMeters { get; }

    public bool IsLength => Text is null;

    public override string ToString() =>
        $"{ParameterName} = {(IsLength ? LengthMeters?.ToString("0.###", CultureInfo.InvariantCulture) + " m" ?? "（清除）" : Text)}";
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
        Rows.Where(r => r.Derivation.Kind == FireRatingDerivationKind.CoverMissing).ToList();

    /// <summary>Rows whose 結構材料 is blank or unrecognised.</summary>
    public IReadOnlyList<FireReviewTypeRow> AwaitingMaterial =>
        Rows.Where(r => r.Derivation.Kind == FireRatingDerivationKind.MaterialMissing).ToList();
}
