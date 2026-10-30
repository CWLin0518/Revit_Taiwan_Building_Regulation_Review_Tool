using System;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using BuildingRegulationReview.Domain.Reviews;

namespace BuildingRegulationReview.Application.Checks;

/// <summary>
/// The two parameters spec 11.5 step 4 separates. The review reads the design value from the selected
/// Type parameter and never writes to it; a computed requirement, if it is ever written back, goes to
/// its own parameter.
/// </summary>
public static class FireRatingParameters
{
    /// <summary>The design／certified rating of a Type (設計／認證防火時效).</summary>
    public const string Provided = "防火檢討_設計防火時效";

    /// <summary>The rating the rules require; reserved for write-back, never a source of the design value.</summary>
    public const string Required = "防火檢討_法規要求防火時效";

    /// <summary>True when the parameter is the required-rating one, so it may not be read as a design value.</summary>
    public static bool IsRequiredParameter(string? name) =>
        name is not null && string.Equals(name.Trim(), Required, StringComparison.OrdinalIgnoreCase);
}

/// <summary>What a bare number in a rating parameter means (spec 19 item 5 is still open).</summary>
public enum FireRatingUnit
{
    Minute,
    Hour
}

public enum ProvidedFireRatingKind
{
    /// <summary>One rating in minutes.</summary>
    Rated,

    /// <summary>No value: the parameter is empty, absent, or the element has no Type to read.</summary>
    Missing,

    /// <summary>A value that is not a rating, e.g. "耐燃" or "-30". 資料不足.</summary>
    Unreadable,

    /// <summary>A composite construction with no single rating, e.g. "1hr/2hr". 人工覆核.</summary>
    Undeterminable
}

/// <summary>
/// A Type's design／certified fire rating as it was read (spec 11.5 step 3). It keeps the raw text it
/// came from, so evidence shows exactly what the model said, and it is never replaced by what the
/// rules require.
/// </summary>
public sealed class ProvidedFireRating
{
    /// <summary>A rating above a day is a typing error, not a construction.</summary>
    public const double MaximumMinutes = 24 * 60;

    private ProvidedFireRating(ProvidedFireRatingKind kind, double? minutes, string? rawText, string? reason)
    {
        Kind = kind;
        Minutes = minutes;
        RawText = string.IsNullOrWhiteSpace(rawText) ? null : rawText!.Trim();
        Reason = string.IsNullOrWhiteSpace(reason) ? null : reason!.Trim();
    }

    public static ProvidedFireRating Rated(double minutes, string? rawText = null)
    {
        if (double.IsNaN(minutes) || double.IsInfinity(minutes) || minutes < 0 || minutes > MaximumMinutes)
            throw new ArgumentOutOfRangeException(nameof(minutes), "A rating is between 0 and 24 hours.");
        return new ProvidedFireRating(ProvidedFireRatingKind.Rated, minutes, rawText, null);
    }

    public static ProvidedFireRating Missing(string? reason = null) =>
        new(ProvidedFireRatingKind.Missing, null, null, reason);

    public static ProvidedFireRating Unreadable(string? rawText, string reason)
    {
        if (string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("Say why the rating could not be read.", nameof(reason));
        return new ProvidedFireRating(ProvidedFireRatingKind.Unreadable, null, rawText, reason);
    }

    public static ProvidedFireRating Undeterminable(string? rawText, string reason)
    {
        if (string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("Say why no single rating applies.", nameof(reason));
        return new ProvidedFireRating(ProvidedFireRatingKind.Undeterminable, null, rawText, reason);
    }

    /// <summary>A numeric parameter (Integer／Number storage) in the unit the project declared.</summary>
    public static ProvidedFireRating FromNumber(double value, FireRatingUnit unit)
    {
        var raw = value.ToString("0.###", CultureInfo.InvariantCulture);
        if (double.IsNaN(value) || double.IsInfinity(value)) return Unreadable(raw, "數值無效");
        if (value < 0) return Unreadable(raw, "防火時效不可為負值");

        var minutes = unit == FireRatingUnit.Hour ? value * 60.0 : value;
        return minutes > MaximumMinutes
            ? Unreadable(raw, "防火時效超過 24 小時，應為輸入錯誤")
            : Rated(minutes, raw);
    }

    public ProvidedFireRatingKind Kind { get; }

    /// <summary>The rating in minutes when <see cref="Kind"/> is <see cref="ProvidedFireRatingKind.Rated"/>.</summary>
    public double? Minutes { get; }

    /// <summary>The parameter value as the model holds it, when there was one.</summary>
    public string? RawText { get; }

    /// <summary>Why there is no rating, for the non-rated kinds.</summary>
    public string? Reason { get; }

    public bool IsRated => Kind == ProvidedFireRatingKind.Rated;

    public ReviewValue? Value => Minutes is double m ? ReviewValue.Quantity(m, ReviewUnit.Minute) : null;

    public override string ToString() => Kind switch
    {
        ProvidedFireRatingKind.Rated => FireRatingText.Format(Minutes!.Value),
        ProvidedFireRatingKind.Missing => Reason is null ? "未設定" : $"未設定（{Reason}）",
        ProvidedFireRatingKind.Unreadable => $"格式無法判讀「{RawText}」（{Reason}）",
        _ => $"無法判定「{RawText}」（{Reason}）"
    };
}

/// <summary>
/// Reads a rating typed into a text parameter. Accepts one number with an optional unit — minutes
/// (<c>60</c>, <c>60 min</c>, <c>60分</c>) or hours (<c>1 hr</c>, <c>1.5h</c>, <c>2小時</c>, <c>一小時半</c>,
/// <c>半小時</c>). Several ratings in one value (<c>1hr/2hr</c>, <c>60~120</c>) describe a composite
/// construction and are <see cref="ProvidedFireRatingKind.Undeterminable"/>; anything else is
/// <see cref="ProvidedFireRatingKind.Unreadable"/>. Nothing is guessed.
/// </summary>
public static class FireRatingText
{
    private const string MinuteUnits = "minutes|minute|mins|min|分鐘|分";
    private const string HourUnits = "hours|hour|hrs|hr|h|小時|時";

    private static readonly Regex Single = new(
        $@"^(?<n>\d+(?:\.\d+)?|[一二兩三四])(?<u>{MinuteUnits}|{HourUnits})?(?<half>半)?$",
        RegexOptions.CultureInvariant);

    private static readonly Regex HalfHour = new(@"^半(?:小時|時)$", RegexOptions.CultureInvariant);

    private static readonly Regex Rating = new(
        $@"(?:\d+(?:\.\d+)?|[一二兩三四半])\s*(?:{MinuteUnits}|{HourUnits})|\d+(?:\.\d+)?",
        RegexOptions.CultureInvariant);

    public static ProvidedFireRating Parse(string? raw, FireRatingUnit bareNumberUnit = FireRatingUnit.Minute)
    {
        if (string.IsNullOrWhiteSpace(raw)) return ProvidedFireRating.Missing("參數值為空白");

        var original = raw!.Trim();
        var text = Normalize(original);
        var compact = text.Replace(" ", string.Empty);

        if (HalfHour.IsMatch(compact)) return ProvidedFireRating.Rated(30, original);

        var match = Single.Match(compact);
        if (match.Success)
        {
            var number = Number(match.Groups["n"].Value);
            var unit = match.Groups["u"].Value;
            var half = match.Groups["half"].Success;
            var isHour = unit.Length == 0 ? bareNumberUnit == FireRatingUnit.Hour : IsHourUnit(unit);

            if (half && !(unit.Length > 0 && IsHourUnit(unit)))
                return ProvidedFireRating.Unreadable(original, "「半」只能接在小時之後");
            if (unit.Length == 0 && !char.IsDigit(match.Groups["n"].Value[0]))
                return ProvidedFireRating.Unreadable(original, "中文數字需寫明「小時」");

            var minutes = (isHour ? number * 60.0 : number) + (half ? 30.0 : 0.0);
            return minutes > ProvidedFireRating.MaximumMinutes
                ? ProvidedFireRating.Unreadable(original, "防火時效超過 24 小時，應為輸入錯誤")
                : ProvidedFireRating.Rated(minutes, original);
        }

        var ratings = Rating.Matches(text).Count;
        if (ratings >= 2)
            return ProvidedFireRating.Undeterminable(original, $"含 {ratings} 個防火時效，應為複合構造，無法判定單一時效");

        return ProvidedFireRating.Unreadable(original, text.Contains("-")
            ? "防火時效不可為負值"
            : "不是可辨識的防火時效（例如 60 min、1 hr、2小時）");
    }

    /// <summary>A rating the way results and legends print it: <c>90 min（1.5 小時）</c>.</summary>
    public static string Format(double minutes)
    {
        var text = minutes.ToString("0.##", CultureInfo.InvariantCulture) + " min";
        return minutes >= 60 ? $"{text}（{(minutes / 60.0).ToString("0.##", CultureInfo.InvariantCulture)} 小時）" : text;
    }

    private static bool IsHourUnit(string unit) => HourUnits.Split('|').Contains(unit, StringComparer.Ordinal);

    private static double Number(string text) => text switch
    {
        "一" => 1,
        "二" or "兩" => 2,
        "三" => 3,
        "四" => 4,
        _ => double.Parse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture)
    };

    /// <summary>Full-width digits and punctuation to ASCII, lower case, runs of spaces to one.</summary>
    private static string Normalize(string text)
    {
        var builder = new StringBuilder(text.Length);
        foreach (var c in text.Normalize(NormalizationForm.FormKC))
            builder.Append(char.IsWhiteSpace(c) ? ' ' : char.ToLowerInvariant(c));
        return Regex.Replace(builder.ToString(), " +", " ").Trim();
    }
}
