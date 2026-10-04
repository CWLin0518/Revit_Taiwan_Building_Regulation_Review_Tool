using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using BuildingRegulationReview.Application.Candidates;

namespace BuildingRegulationReview.Application.Checks;

/// <summary>
/// 第79條第1項：「防火設備並應具有一小時以上之阻熱性」。A Yes/No on the opening Type, read the way
/// 防火檢討_設計防火保護 and 防火檢討_遮煙性能 are: 阻熱性 is a test result on the 型號, never derived
/// from the model (垂直區劃規格決議 38).
/// </summary>
public static class InsulationParameters
{
    public const string Provided = "防火檢討_阻熱性";
}

/// <summary>Whether a fire protection value was read from the opening itself or from its Type.</summary>
public enum FireProtectionScope
{
    Instance,
    Type
}

/// <summary>
/// The design fire protection of one opening or one opening Type, with the parameter it was read
/// from. An instance value overrides its Type's, the way a Revit instance parameter would.
/// </summary>
public sealed class OpeningFireProtection
{
    public OpeningFireProtection(FireProtectionScope scope, string uniqueId, ProvidedFireProtection protection, string source)
    {
        if (!Enum.IsDefined(typeof(FireProtectionScope), scope)) throw new ArgumentOutOfRangeException(nameof(scope));
        if (string.IsNullOrWhiteSpace(uniqueId)) throw new ArgumentException("UniqueId is required.", nameof(uniqueId));
        if (string.IsNullOrWhiteSpace(source)) throw new ArgumentException("Name the parameter the value was read from.", nameof(source));

        Scope = scope;
        UniqueId = uniqueId.Trim();
        Protection = protection ?? throw new ArgumentNullException(nameof(protection));
        Source = source.Trim();
    }

    public static OpeningFireProtection ForInstance(string elementUniqueId, ProvidedFireProtection protection, string source) =>
        new(FireProtectionScope.Instance, elementUniqueId, protection, source);

    public static OpeningFireProtection ForType(string typeUniqueId, ProvidedFireProtection protection, string source) =>
        new(FireProtectionScope.Type, typeUniqueId, protection, source);

    public FireProtectionScope Scope { get; }

    /// <summary>The opening's element UniqueId, or its Type's.</summary>
    public string UniqueId { get; }

    public ProvidedFireProtection Protection { get; }

    /// <summary>The parameter the value was read from, e.g. <c>防火檢討_設計防火保護</c>.</summary>
    public string Source { get; }

    public override string ToString() => $"{Scope} {UniqueId}: {Protection}（{Source}）";
}

/// <summary>
/// What the 防火門窗 check needs besides the candidate set: the building and zone inputs the rules
/// may use (so <c>opening.*</c> can never be supplied as an input) and the design fire protection of
/// each opening or opening Type.
/// </summary>
public sealed class OpeningProtectionInputs
{
    public static readonly OpeningProtectionInputs None = new(null, null);

    private readonly Dictionary<(FireProtectionScope, string), OpeningFireProtection> _values;
    private readonly Dictionary<string, ProvidedFireProtection> _insulation;

    public OpeningProtectionInputs(
        CompartmentAreaInputs? context,
        IEnumerable<OpeningFireProtection>? protections,
        IReadOnlyDictionary<string, ProvidedFireProtection>? insulation = null)
    {
        Context = context ?? CompartmentAreaInputs.None;
        _insulation = new Dictionary<string, ProvidedFireProtection>(
            (IDictionary<string, ProvidedFireProtection>?)insulation?.ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal)
            ?? new Dictionary<string, ProvidedFireProtection>(), StringComparer.Ordinal);

        var list = (protections ?? Array.Empty<OpeningFireProtection>()).ToList();
        if (list.Any(x => x is null)) throw new ArgumentException("The fire protection values contain a missing entry.", nameof(protections));
        var duplicate = list.GroupBy(x => (x.Scope, x.UniqueId)).FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
            throw new ArgumentException($"{duplicate.Key.Scope} '{duplicate.Key.UniqueId}' has more than one fire protection value.", nameof(protections));

        _values = list.ToDictionary(x => (x.Scope, x.UniqueId));
        Protections = new ReadOnlyCollection<OpeningFireProtection>(list
            .OrderBy(x => x.Scope).ThenBy(x => x.UniqueId, StringComparer.Ordinal).ToList());
    }

    /// <summary>Building and zone inputs (構造、用途、灑水…), applied to every opening's facts.</summary>
    public CompartmentAreaInputs Context { get; }

    public IReadOnlyList<OpeningFireProtection> Protections { get; }

    /// <summary>The 阻熱性 read, by the opening's element UniqueId or its Type's.</summary>
    public IReadOnlyDictionary<string, ProvidedFireProtection> Insulation => _insulation;

    /// <summary>
    /// The opening's 阻熱性: its own value when it has one, otherwise its Type's, the way 設計防火保護
    /// is read. Missing — 資料不足, never 否 — when neither was read.
    /// </summary>
    public ProvidedFireProtection InsulationFor(OpeningObservation opening)
    {
        if (opening is null) throw new ArgumentNullException(nameof(opening));
        if (_insulation.TryGetValue(opening.Source.ElementUniqueId, out var own)) return own;
        if (opening.TypeUniqueId is not null && _insulation.TryGetValue(opening.TypeUniqueId, out var type)) return type;
        return ProvidedFireProtection.Missing(opening.TypeUniqueId is null
            ? "開口與其 Type 都未提供 " + InsulationParameters.Provided
            : "Type 未提供 " + InsulationParameters.Provided);
    }

    /// <summary>The opening's own value when it has one, otherwise its Type's; null when neither was read.</summary>
    public OpeningFireProtection? For(OpeningObservation opening)
    {
        if (opening is null) throw new ArgumentNullException(nameof(opening));
        if (_values.TryGetValue((FireProtectionScope.Instance, opening.Source.ElementUniqueId), out var own)) return own;
        return opening.TypeUniqueId is not null && _values.TryGetValue((FireProtectionScope.Type, opening.TypeUniqueId), out var type)
            ? type
            : null;
    }
}
