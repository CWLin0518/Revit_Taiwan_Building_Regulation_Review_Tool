using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Text;
using BuildingRegulationReview.Application.Candidates;
using BuildingRegulationReview.Application.Checks;
using BuildingRegulationReview.Domain.Reviews;
using BuildingRegulationReview.Domain.Rules;
using BuildingRegulationReview.Domain.Rules.Expressions;

namespace BuildingRegulationReview.Application.Reviews;

public enum ParameterReadingKind
{
    /// <summary>The element has no parameter of that name.</summary>
    Absent,

    /// <summary>The parameter exists but holds no value.</summary>
    Empty,

    Text,
    Integer,
    YesNo,

    /// <summary>A plain number with no unit.</summary>
    Number,

    /// <summary>A length, already converted to metres by the adapter.</summary>
    Length
}

/// <summary>
/// One parameter value exactly as the adapter found it, before anyone decided what it means. The
/// adapter only converts Revit's internal length unit; interpreting 是／否, 60 min or 部分 is done
/// here, where it is tested.
/// </summary>
public sealed class ParameterReading
{
    private ParameterReading(ParameterReadingKind kind, string? text, double number)
    {
        Kind = kind;
        Text = text;
        Number = number;
    }

    public static readonly ParameterReading Absent = new(ParameterReadingKind.Absent, null, 0);
    public static readonly ParameterReading Empty = new(ParameterReadingKind.Empty, null, 0);

    public static ParameterReading OfText(string? text) =>
        string.IsNullOrWhiteSpace(text) ? Empty : new ParameterReading(ParameterReadingKind.Text, text!.Trim(), 0);

    public static ParameterReading OfInteger(int value) => new(ParameterReadingKind.Integer, null, value);
    public static ParameterReading OfYesNo(int value) => new(ParameterReadingKind.YesNo, null, value);

    public static ParameterReading OfNumber(double value) =>
        IsFinite(value) ? new ParameterReading(ParameterReadingKind.Number, null, value) : throw new ArgumentOutOfRangeException(nameof(value));

    public static ParameterReading OfLength(double meters) =>
        IsFinite(meters) ? new ParameterReading(ParameterReadingKind.Length, null, meters) : throw new ArgumentOutOfRangeException(nameof(meters));

    public ParameterReadingKind Kind { get; }
    public string? Text { get; }
    public double Number { get; }

    public bool HasValue => Kind != ParameterReadingKind.Absent && Kind != ParameterReadingKind.Empty;

    /// <summary>The value as the user typed it, for messages and evidence.</summary>
    public string Raw => Kind switch
    {
        ParameterReadingKind.Absent => "（無此參數）",
        ParameterReadingKind.Empty => "（未填）",
        ParameterReadingKind.Text => Text!,
        ParameterReadingKind.YesNo => Number == 1 ? "是" : Number == 0 ? "否" : Number.ToString(CultureInfo.InvariantCulture),
        ParameterReadingKind.Length => Number.ToString("0.###", CultureInfo.InvariantCulture) + " m",
        _ => Number.ToString("0.###", CultureInfo.InvariantCulture)
    };

    public override string ToString() => Raw;

    private static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
}

/// <summary>
/// What the adapter read for one review: which review parameters the project binds to which
/// categories, and the value of each on Project Information and on every element the review looks
/// at — Areas, member Types, openings and opening Types — keyed by UniqueId. Pure data (spec 15).
/// </summary>
public sealed class ReviewParameterSnapshot
{
    public static readonly ReviewParameterSnapshot Empty = new(null, null, null);

    private readonly HashSet<(string Name, ReviewParameterHost Host)> _bindings;
    private readonly Dictionary<string, ParameterReading> _project;
    private readonly Dictionary<string, Dictionary<string, ParameterReading>> _elements;

    public ReviewParameterSnapshot(
        IEnumerable<KeyValuePair<string, ReviewParameterHost>>? bindings,
        IReadOnlyDictionary<string, ParameterReading>? project,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, ParameterReading>>? elements)
    {
        _bindings = new HashSet<(string, ReviewParameterHost)>(
            (bindings ?? Array.Empty<KeyValuePair<string, ReviewParameterHost>>()).Select(x => (x.Key.Trim(), x.Value)));
        _project = new Dictionary<string, ParameterReading>(StringComparer.Ordinal);
        foreach (var pair in project ?? new Dictionary<string, ParameterReading>())
            _project[pair.Key.Trim()] = pair.Value ?? ParameterReading.Absent;
        _elements = new Dictionary<string, Dictionary<string, ParameterReading>>(StringComparer.Ordinal);
        foreach (var element in elements ?? new Dictionary<string, IReadOnlyDictionary<string, ParameterReading>>())
        {
            var values = new Dictionary<string, ParameterReading>(StringComparer.Ordinal);
            foreach (var pair in element.Value) values[pair.Key.Trim()] = pair.Value ?? ParameterReading.Absent;
            _elements[element.Key.Trim()] = values;
        }
    }

    public int ElementCount => _elements.Count;

    public bool IsBound(string parameterName, ReviewParameterHost host) => _bindings.Contains((parameterName, host));

    public IReadOnlyList<ReviewParameterHost> HostsOf(string parameterName) =>
        _bindings.Where(x => string.Equals(x.Name, parameterName, StringComparison.Ordinal)).Select(x => x.Host).OrderBy(x => x).ToList();

    public ParameterReading Project(string parameterName) =>
        _project.TryGetValue(parameterName, out var reading) ? reading : ParameterReading.Absent;

    public ParameterReading Element(string? uniqueId, string parameterName) =>
        uniqueId is not null && _elements.TryGetValue(uniqueId, out var values) && values.TryGetValue(parameterName, out var reading)
            ? reading
            : ParameterReading.Absent;
}

/// <summary>The check inputs one review runs on, built from the model's parameters.</summary>
public sealed class ReviewInputAssembly
{
    private readonly Dictionary<string, TypeFireRating> _panels;

    internal ReviewInputAssembly(
        CompartmentAreaInputs area,
        FireResistanceInputs rating,
        OpeningProtectionInputs protection,
        IEnumerable<TypeFireRating>? panelRatings = null)
    {
        Area = area;
        Rating = rating;
        Protection = protection;
        PanelRatings = new ReadOnlyCollection<TypeFireRating>((panelRatings ?? Array.Empty<TypeFireRating>()).ToList());
        _panels = PanelRatings.ToDictionary(x => x.TypeUniqueId, StringComparer.Ordinal);
    }

    public CompartmentAreaInputs Area { get; }
    public FireResistanceInputs Rating { get; }
    public OpeningProtectionInputs Protection { get; }

    /// <summary>
    /// 設計防火時效 as read on every 帷幕嵌板 Type of the package. No check reads it — the 帷幕牆 geometry
    /// reader reads the panels' ratings itself (帷幕牆規格 §12 步驟 5 決策 2) — but the evidence baseline
    /// has to know it, or changing a panel Type's rating would not make the stored run 需更新
    /// (spec 13.1, docs §10 案例 20).
    /// </summary>
    public IReadOnlyList<TypeFireRating> PanelRatings { get; }

    public TypeFireRating? PanelRating(string? typeUniqueId) =>
        typeUniqueId is not null && _panels.TryGetValue(typeUniqueId, out var rating) ? rating : null;
}

/// <summary>
/// Turns a <see cref="ReviewParameterSnapshot"/> into the three checks' inputs. Nothing is guessed:
/// an absent or empty parameter is simply not supplied (so the field is missing and the rule engine
/// says 資料不足), and a value that cannot be understood is <see cref="ReviewInput.Unreadable"/> with
/// the reason, never false or zero (spec 11.3, spec 18 item 5).
/// </summary>
public static class ReviewInputAssembler
{
    public static ReviewInputAssembly Assemble(
        CandidateSet set,
        ReviewParameterSnapshot snapshot,
        FireRatingUnit bareNumberUnit = FireRatingUnit.Minute,
        RuleFieldCatalog? catalog = null,
        ReviewModelFacts? model = null)
    {
        if (set is null) throw new ArgumentNullException(nameof(set));
        if (snapshot is null) throw new ArgumentNullException(nameof(snapshot));
        catalog ??= RuleFieldCatalog.Default;
        model ??= ReviewModelFacts.None;

        var building = new List<ReviewInput>();
        if (model.BuildingHeightMeters is double height)
        {
            var field = catalog.Find("building.height");
            if (field is not null)
                building.Add(ReviewInput.Known(field.Name, height, field.Type.Unit, model.BuildingHeightSource));
        }

        var zoneSources = new List<ReviewInputSource>();
        foreach (var source in ReviewInputSources.All)
        {
            var definition = catalog.Find(source.Field);
            if (definition is null) continue;
            if (source.Hosts.Contains(ReviewParameterHost.ProjectInformation))
            {
                var input = Convert(definition, snapshot.Project(source.ParameterName), "專案資訊：" + source.ParameterName);
                if (input is not null) building.Add(input);
            }
            else if (source.Hosts.Contains(ReviewParameterHost.Areas) ||
                     string.Equals(source.Field, "zone.interiorFinish", StringComparison.Ordinal))
            {
                zoneSources.Add(source);
            }
        }

        var zones = new Dictionary<Guid, IEnumerable<ReviewInput>>();
        foreach (var zone in set.Zones)
        {
            var inputs = zoneSources
                .Select(source => ZoneInput(catalog.Find(source.Field)!, source, zone, snapshot))
                .Where(x => x is not null).Select(x => x!).ToList();
            if (inputs.Count > 0) zones[zone.ZoneId] = inputs;
        }

        var context = new CompartmentAreaInputs(building, zones);

        var ratings = set.Members
            .Select(m => m.Observation)
            .Where(o => o.TypeUniqueId is not null)
            .GroupBy(o => o.TypeUniqueId!, StringComparer.Ordinal)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => new TypeFireRating(
                g.Key,
                Rating(snapshot.Element(g.Key, FireRatingParameters.Provided), bareNumberUnit),
                FireRatingParameters.Provided,
                g.First().TypeName))
            .ToList();

        var protections = new List<OpeningFireProtection>();
        var typesDone = new HashSet<string>(StringComparer.Ordinal);
        foreach (var opening in set.Openings.Select(o => o.Observation)
                     .OrderBy(o => o.Source.ElementUniqueId, StringComparer.Ordinal))
        {
            var own = snapshot.Element(opening.Source.ElementUniqueId, FireProtectionParameters.Provided);
            if (own.HasValue && !protections.Any(p => p.Scope == FireProtectionScope.Instance &&
                                                      string.Equals(p.UniqueId, opening.Source.ElementUniqueId, StringComparison.Ordinal)))
            {
                protections.Add(OpeningFireProtection.ForInstance(
                    opening.Source.ElementUniqueId, Protection(own), FireProtectionParameters.Provided + "（實體）"));
            }

            if (opening.TypeUniqueId is null || !typesDone.Add(opening.TypeUniqueId)) continue;
            var type = snapshot.Element(opening.TypeUniqueId, FireProtectionParameters.Provided);

            // An unticked checkbox reads as Empty but is an answer (否), so the Type is taken
            // whenever it carries the parameter at all — only Absent means nothing was bound.
            if (type.Kind != ParameterReadingKind.Absent)
            {
                protections.Add(OpeningFireProtection.ForType(
                    opening.TypeUniqueId, Protection(type), FireProtectionParameters.Provided + "（類型）"));
            }
        }

        // Panel ratings stay out of FireResistanceInputs on purpose: a 帷幕嵌板 is no 主要構造, and
        // feeding it to 第70條's check would change what that check reviews (帷幕牆規格 §12 步驟 5 決策 2).
        var panelRatings = set.Openings
            .Select(o => o.Observation)
            .Where(o => o.Category == CandidateCategory.CurtainPanel && o.TypeUniqueId is not null)
            .GroupBy(o => o.TypeUniqueId!, StringComparer.Ordinal)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => new TypeFireRating(
                g.Key,
                Rating(snapshot.Element(g.Key, FireRatingParameters.Provided), bareNumberUnit),
                FireRatingParameters.Provided,
                g.First().TypeName))
            .ToList();

        return new ReviewInputAssembly(
            context,
            new FireResistanceInputs(context, ratings),
            new OpeningProtectionInputs(context, protections),
            panelRatings);
    }

    /// <summary>
    /// A rule input from one parameter reading, or null when nothing was supplied. The reading must
    /// fit the field's type: a Yes/No where a number is expected is unreadable, not zero.
    /// </summary>
    public static ReviewInput? Convert(RuleFieldDefinition field, ParameterReading reading, string source)
    {
        if (field is null) throw new ArgumentNullException(nameof(field));
        if (reading is null) throw new ArgumentNullException(nameof(reading));
        if (!reading.HasValue) return null;

        var type = field.Type;
        if (type.IsBoolean) return Boolean(field.Name, reading, source);
        if (type.IsText) return TextInput(field.Name, reading, source);
        if (type.IsQuantity) return Quantity(field.Name, type.Unit, reading, source);
        return ReviewInput.Unreadable(field.Name, $"不支援的欄位型別 {type}", source);
    }

    public static ProvidedFireRating Rating(ParameterReading reading, FireRatingUnit bareNumberUnit = FireRatingUnit.Minute)
    {
        if (reading is null) throw new ArgumentNullException(nameof(reading));
        return reading.Kind switch
        {
            ParameterReadingKind.Absent => ProvidedFireRating.Missing($"此 Type 沒有參數 {FireRatingParameters.Provided}"),
            ParameterReadingKind.Empty => ProvidedFireRating.Missing($"{FireRatingParameters.Provided} 未填"),
            ParameterReadingKind.Text => FireRatingText.Parse(reading.Text, bareNumberUnit),
            ParameterReadingKind.Integer or ParameterReadingKind.Number => ProvidedFireRating.FromNumber(reading.Number, bareNumberUnit),
            ParameterReadingKind.YesNo => ProvidedFireRating.Unreadable(reading.Raw, "是非參數無法表示防火時效"),
            _ => ProvidedFireRating.Unreadable(reading.Raw, "長度參數無法表示防火時效")
        };
    }

    /// <summary>
    /// 防火檢討_設計防火保護 as read. It is a Yes/No Type parameter, and Revit shows an unticked box
    /// and a never-touched box the same way, so a bound-but-unset parameter is 否, not 資料不足 —
    /// otherwise no model could ever say「這扇門不是防火門」and every opening would stay 待確認.
    /// Only a parameter that is not bound at all is 資料不足, which is a setup problem, not an answer.
    /// </summary>
    public static ProvidedFireProtection Protection(ParameterReading reading)
    {
        if (reading is null) throw new ArgumentNullException(nameof(reading));
        return reading.Kind switch
        {
            ParameterReadingKind.Absent => ProvidedFireProtection.Missing($"沒有參數 {FireProtectionParameters.Provided}"),
            ParameterReadingKind.Empty => ProvidedFireProtection.No("未勾選"),
            ParameterReadingKind.Text => FireProtectionText.Parse(reading.Text),
            ParameterReadingKind.YesNo or ParameterReadingKind.Integer => ProvidedFireProtection.FromInteger((int)reading.Number),
            _ => ProvidedFireProtection.Unreadable(reading.Raw, "數值或長度參數無法表示是否具防火保護")
        };
    }

    private static ReviewInput? ZoneInput(RuleFieldDefinition field, ReviewInputSource source, CandidateZone zone, ReviewParameterSnapshot snapshot)
    {
        var sourceText = "面積：" + source.ParameterName;
        var readings = zone.AreaUniqueIds.Select(uid => snapshot.Element(uid, source.ParameterName)).ToList();
        if (readings.Count == 0 || readings.All(r => !r.HasValue)) return null;

        var inputs = readings.Select(r => Convert(field, r, sourceText)).ToList();
        var first = inputs[0];
        var consistent = inputs.All(i => i is not null && !i.IsUnreadable && first is not null && !first.IsUnreadable &&
                                         Equals(i.Value, first.Value));
        if (consistent) return first;

        // One 區劃 is one set of facts; parts that disagree, or a part left blank, cannot be
        // resolved by picking one — that would be the tool deciding the use or the sprinklers.
        if (readings.Count == 1 || inputs.All(i => i is not null && i.IsUnreadable)) return first;
        return ReviewInput.Unreadable(field.Name,
            $"此區劃的 {readings.Count} 個面積填寫不一致（{string.Join("、", readings.Select(r => r.Raw))}）",
            sourceText);
    }

    private static ReviewInput Boolean(string field, ParameterReading reading, string source)
    {
        switch (reading.Kind)
        {
            case ParameterReadingKind.YesNo:
            case ParameterReadingKind.Integer:
                if (reading.Number == 1) return ReviewInput.Known(field, true, source);
                if (reading.Number == 0) return ReviewInput.Known(field, false, source);
                return ReviewInput.Unreadable(field, $"「{reading.Raw}」不是是／否", source);
            case ParameterReadingKind.Text:
                var parsed = FireProtectionText.Parse(reading.Text);
                return parsed.Kind switch
                {
                    ProvidedFireProtectionKind.Yes => ReviewInput.Known(field, true, source),
                    ProvidedFireProtectionKind.No => ReviewInput.Known(field, false, source),
                    _ => ReviewInput.Unreadable(field, $"「{reading.Raw}」不是明確的是／否", source)
                };
            default:
                return ReviewInput.Unreadable(field, $"「{reading.Raw}」不是是／否", source);
        }
    }

    private static ReviewInput TextInput(string field, ParameterReading reading, string source) => reading.Kind switch
    {
        ParameterReadingKind.Text => ReviewInput.Known(field, reading.Text!, source),
        ParameterReadingKind.Integer => ReviewInput.Known(field, reading.Raw, source),
        _ => ReviewInput.Unreadable(field, $"「{reading.Raw}」不是文字", source)
    };

    private static ReviewInput Quantity(string field, ReviewUnit unit, ParameterReading reading, string source)
    {
        switch (reading.Kind)
        {
            case ParameterReadingKind.Integer:
            case ParameterReadingKind.Number:
                return ReviewInput.Known(field, reading.Number, unit, source);
            case ParameterReadingKind.Length when unit == ReviewUnit.Meter:
                return ReviewInput.Known(field, reading.Number, unit, source);
            case ParameterReadingKind.Text:
                var number = ParseNumber(reading.Text!, unit);
                return number is double value
                    ? ReviewInput.Known(field, value, unit, source)
                    : ReviewInput.Unreadable(field, $"「{reading.Raw}」不是{(unit == ReviewUnit.Meter ? "以公尺表示的長度" : "數字")}", source);
            default:
                return ReviewInput.Unreadable(field, $"「{reading.Raw}」不是{(unit == ReviewUnit.Meter ? "長度" : "數字")}", source);
        }
    }

    /// <summary>A plain number; for metres an optional <c>m</c> suffix. Full-width digits are accepted.</summary>
    private static double? ParseNumber(string text, ReviewUnit unit)
    {
        var normalized = HalfWidth(text).Trim();
        if (unit == ReviewUnit.Meter && normalized.EndsWith("m", StringComparison.OrdinalIgnoreCase))
            normalized = normalized.Substring(0, normalized.Length - 1).TrimEnd();
        if (unit == ReviewUnit.Meter && normalized.EndsWith("公尺", StringComparison.Ordinal))
            normalized = normalized.Substring(0, normalized.Length - 2).TrimEnd();

        return double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) &&
               !double.IsNaN(value) && !double.IsInfinity(value)
            ? value
            : (double?)null;
    }

    private static string HalfWidth(string text)
    {
        var builder = new StringBuilder(text.Length);
        foreach (var c in text)
            builder.Append(c >= '！' && c <= '～' ? (char)(c - 0xFEE0) : c == '　' ? ' ' : c);
        return builder.ToString();
    }
}
