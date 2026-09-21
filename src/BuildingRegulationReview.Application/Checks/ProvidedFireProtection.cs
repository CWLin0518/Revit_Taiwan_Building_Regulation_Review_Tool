using System;
using System.Globalization;
using System.Linq;
using System.Text;

namespace BuildingRegulationReview.Application.Checks;

/// <summary>
/// The parameter an opening's design fire protection is read from by default (spec 11.6 step 2
/// <c>ProvidedFireProtection</c>). The review reads it and never writes it.
/// </summary>
public static class FireProtectionParameters
{
    public const string Provided = "BCR_ProvidedFireProtection";
}

public enum ProvidedFireProtectionKind
{
    /// <summary>是: the opening is a fire door／window.</summary>
    Yes,

    /// <summary>否: the opening is declared not to be one.</summary>
    No,

    /// <summary>未設定: the parameter is empty or absent. 資料不足.</summary>
    Missing,

    /// <summary>A value that is not a yes／no, e.g. "甲種" or "F60". 資料不足.</summary>
    Unreadable
}

/// <summary>
/// An opening's design fire protection as it was read (spec 11.6 step 3: 是／否／未設定). It keeps
/// the raw text, so evidence shows exactly what the model said.
/// </summary>
public sealed class ProvidedFireProtection
{
    /// <summary>The texts <c>opening.providedFireProtection</c> carries into rule expressions.</summary>
    public const string YesText = "是";
    public const string NoText = "否";

    private ProvidedFireProtection(ProvidedFireProtectionKind kind, string? rawText, string? reason)
    {
        Kind = kind;
        RawText = string.IsNullOrWhiteSpace(rawText) ? null : rawText!.Trim();
        Reason = string.IsNullOrWhiteSpace(reason) ? null : reason!.Trim();
    }

    public static ProvidedFireProtection Yes(string? rawText = null) => new(ProvidedFireProtectionKind.Yes, rawText, null);

    public static ProvidedFireProtection No(string? rawText = null) => new(ProvidedFireProtectionKind.No, rawText, null);

    public static ProvidedFireProtection Missing(string? reason = null) => new(ProvidedFireProtectionKind.Missing, null, reason);

    public static ProvidedFireProtection Unreadable(string? rawText, string reason)
    {
        if (string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("Say why the value could not be read.", nameof(reason));
        return new ProvidedFireProtection(ProvidedFireProtectionKind.Unreadable, rawText, reason);
    }

    /// <summary>A Revit Yes/No parameter.</summary>
    public static ProvidedFireProtection FromBoolean(bool value) => value ? Yes(value.ToString()) : No(value.ToString());

    /// <summary>
    /// A Revit Yes/No parameter stored as an integer: 1 is 是, 0 is 否, anything else cannot be read.
    /// </summary>
    public static ProvidedFireProtection FromInteger(int value) => value switch
    {
        1 => Yes("1"),
        0 => No("0"),
        _ => Unreadable(value.ToString(CultureInfo.InvariantCulture), "是非參數只能是 1 或 0")
    };

    public ProvidedFireProtectionKind Kind { get; }

    /// <summary>What the parameter held, trimmed; null when nothing was read.</summary>
    public string? RawText { get; }

    /// <summary>Why a value is missing or unreadable.</summary>
    public string? Reason { get; }

    public bool IsKnown => Kind == ProvidedFireProtectionKind.Yes || Kind == ProvidedFireProtectionKind.No;

    /// <summary>是 or 否 for the rules; null when the value is not known.</summary>
    public string? RuleText => Kind switch
    {
        ProvidedFireProtectionKind.Yes => YesText,
        ProvidedFireProtectionKind.No => NoText,
        _ => null
    };

    public override string ToString() => Kind switch
    {
        ProvidedFireProtectionKind.Yes => YesText,
        ProvidedFireProtectionKind.No => NoText,
        ProvidedFireProtectionKind.Missing => "未設定" + (Reason is null ? string.Empty : $"（{Reason}）"),
        _ => $"「{RawText}」{Reason}"
    };
}

/// <summary>
/// Reads a text parameter as 是／否. Only unmistakable yes／no words are accepted; a grade such as
/// 甲種 or a rating such as F60 is not guessed at — it is unreadable and so 資料不足.
/// </summary>
public static class FireProtectionText
{
    private static readonly string[] YesWords = { "是", "有", "yes", "y", "true", "1" };
    private static readonly string[] NoWords = { "否", "無", "无", "沒有", "没有", "no", "n", "false", "0" };

    public static ProvidedFireProtection Parse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return ProvidedFireProtection.Missing("參數未填寫");

        var text = raw!.Trim();
        var word = text.Normalize(NormalizationForm.FormKC).Trim().ToLowerInvariant();
        if (YesWords.Contains(word, StringComparer.Ordinal)) return ProvidedFireProtection.Yes(text);
        if (NoWords.Contains(word, StringComparer.Ordinal)) return ProvidedFireProtection.No(text);
        return ProvidedFireProtection.Unreadable(text, "不是「是／否」的值");
    }
}
