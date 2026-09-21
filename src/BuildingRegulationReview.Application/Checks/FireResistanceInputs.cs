using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace BuildingRegulationReview.Application.Checks;

/// <summary>
/// The design rating one Type carries, read from the selected Type parameter (spec 11.5 step 3). The
/// parameter name is kept as the source of the value, and it can never be the required-rating
/// parameter: a requirement must not become a design value (spec 11.5 step 3「規則要求值不得直接覆寫設計值」).
/// </summary>
public sealed class TypeFireRating
{
    public TypeFireRating(string typeUniqueId, ProvidedFireRating rating, string source, string? typeName = null)
    {
        if (string.IsNullOrWhiteSpace(typeUniqueId)) throw new ArgumentException("Type UniqueId is required.", nameof(typeUniqueId));
        if (string.IsNullOrWhiteSpace(source)) throw new ArgumentException("Name the parameter the rating was read from.", nameof(source));
        if (FireRatingParameters.IsRequiredParameter(source))
            throw new ArgumentException(
                $"'{FireRatingParameters.Required}' holds what the rules require; it cannot be read as the design rating.", nameof(source));

        TypeUniqueId = typeUniqueId.Trim();
        Rating = rating ?? throw new ArgumentNullException(nameof(rating));
        Source = source.Trim();
        TypeName = string.IsNullOrWhiteSpace(typeName) ? null : typeName!.Trim();
    }

    public string TypeUniqueId { get; }
    public ProvidedFireRating Rating { get; }

    /// <summary>The parameter the rating was read from, e.g. <c>BCR_ProvidedFireRating</c> or <c>Fire Rating</c>.</summary>
    public string Source { get; }

    public string? TypeName { get; }

    public override string ToString() => $"{TypeName ?? TypeUniqueId}: {Rating}（{Source}）";
}

/// <summary>
/// What the 構件防火時效 check needs besides the candidate set: the building and zone inputs the rules
/// may use (the same ones the 區劃面積 check takes, so <c>element.*</c> can never be supplied as an
/// input) and the design rating of each Type.
/// </summary>
public sealed class FireResistanceInputs
{
    public static readonly FireResistanceInputs None = new(null, null);

    private readonly Dictionary<string, TypeFireRating> _types;

    public FireResistanceInputs(CompartmentAreaInputs? context, IEnumerable<TypeFireRating>? typeRatings)
    {
        Context = context ?? CompartmentAreaInputs.None;

        var list = (typeRatings ?? Array.Empty<TypeFireRating>()).ToList();
        if (list.Any(x => x is null)) throw new ArgumentException("The type ratings contain a missing entry.", nameof(typeRatings));
        var duplicate = list.GroupBy(x => x.TypeUniqueId, StringComparer.Ordinal).FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
            throw new ArgumentException($"Type '{duplicate.Key}' has more than one design rating.", nameof(typeRatings));

        _types = list.ToDictionary(x => x.TypeUniqueId, StringComparer.Ordinal);
        TypeRatings = new ReadOnlyCollection<TypeFireRating>(list.OrderBy(x => x.TypeUniqueId, StringComparer.Ordinal).ToList());
    }

    /// <summary>Building and zone inputs (構造、用途、灑水…), applied to every member's facts.</summary>
    public CompartmentAreaInputs Context { get; }

    public IReadOnlyList<TypeFireRating> TypeRatings { get; }

    public TypeFireRating? ForType(string? typeUniqueId) =>
        typeUniqueId is not null && _types.TryGetValue(typeUniqueId, out var rating) ? rating : null;
}
