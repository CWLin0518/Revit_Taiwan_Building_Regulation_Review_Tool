using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace BuildingRegulationReview.Application.Checks;

/// <summary>
/// 帷幕嵌板以哪一種構造回答第79條之4（帷幕牆規格 §3.3、決議 16）。同一片帷幕牆上兩種都有時，CW-O
/// 產出兩列，因為兩路量的不是同一件事：一路是分鐘數，一路是是非題。
/// </summary>
public enum CurtainPanelKind
{
    /// <summary>
    /// 實心嵌板：以 <c>防火檢討_設計防火時效</c> 作答，時效比照牆體由模型尺寸推定
    /// （<see cref="FireRatingDeriver"/>）。嵌板為牆者一律是這一種，不需宣告。
    /// </summary>
    Solid,

    /// <summary>玻璃嵌板：以 <c>防火檢討_設計防火保護</c> 作答（同玻璃窗）。</summary>
    Glazed,

    /// <summary>
    /// 帷幕牆上的門或窗：它們是防火設備，答的是防火保護，沒有自己的構造時效可讀，因此與玻璃嵌板
    /// 同一路（決議 16）。這一種不是使用者宣告的，而是讀取層由類別認出來的。
    /// </summary>
    Opening
}

/// <summary>宣告嵌板種類的型別參數。</summary>
public static class CurtainPanelKindParameters
{
    /// <summary>
    /// <c>防火檢討_嵌板種類</c>（Type、TEXT、共享參數 GUID <c>…0013</c>，只綁 Curtain Panels）。
    /// 工具可以由材料類別提案，但寫入參數的是使用者——沒有這個宣告就分不出「實心嵌板漏填時效」
    /// （資料不足，補得起來）與「玻璃嵌板宣告不是防火設備」（未符合，是設計本身的問題）。
    /// </summary>
    public const string Provided = "防火檢討_嵌板種類";
}

/// <summary>嵌板種類的用字、標籤與規則欄位文字。</summary>
public static class CurtainPanelKinds
{
    /// <summary>寫入 <c>防火檢討_嵌板種類</c> 的兩個值。</summary>
    public const string SolidText = "實心";

    public const string GlazedText = "玻璃";

    /// <summary><c>junction.panelKind</c> 帶進規則運算式的文字。</summary>
    public const string SolidRuleText = "Solid";

    public const string GlazedRuleText = "Glazed";

    /// <summary>使用者可以宣告的兩種，依面板呈現的順序。</summary>
    public static IReadOnlyList<CurtainPanelKind> Declarable { get; } =
        new ReadOnlyCollection<CurtainPanelKind>(new[] { CurtainPanelKind.Solid, CurtainPanelKind.Glazed });

    private static readonly IReadOnlyDictionary<string, CurtainPanelKind> Known =
        new Dictionary<string, CurtainPanelKind>(StringComparer.OrdinalIgnoreCase)
        {
            [SolidText] = CurtainPanelKind.Solid,
            ["實心嵌板"] = CurtainPanelKind.Solid,
            ["實板"] = CurtainPanelKind.Solid,
            ["Solid"] = CurtainPanelKind.Solid,
            [GlazedText] = CurtainPanelKind.Glazed,
            ["玻璃嵌板"] = CurtainPanelKind.Glazed,
            ["玻璃帷幕"] = CurtainPanelKind.Glazed,
            ["Glazed"] = CurtainPanelKind.Glazed,
            ["Glass"] = CurtainPanelKind.Glazed
        };

    public static string Label(CurtainPanelKind kind) => kind switch
    {
        CurtainPanelKind.Solid => "實心嵌板",
        CurtainPanelKind.Glazed => "玻璃嵌板",
        CurtainPanelKind.Opening => "帷幕牆門窗",
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    /// <summary>
    /// 寫回參數的文字；<see cref="CurtainPanelKind.Opening"/> 回 null，門窗的種類是讀取層認出來的，
    /// 不由使用者宣告，也不寫回。
    /// </summary>
    public static string? ParameterText(CurtainPanelKind kind) => kind switch
    {
        CurtainPanelKind.Solid => SolidText,
        CurtainPanelKind.Glazed => GlazedText,
        CurtainPanelKind.Opening => null,
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    /// <summary>參數讀到的種類；空白或不認得的字回 null，那是「未宣告」，不是實心也不是玻璃。</summary>
    public static CurtainPanelKind? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        return Known.TryGetValue(text!.Trim(), out var kind) ? kind : (CurtainPanelKind?)null;
    }

    /// <summary>只有實心嵌板以設計防火時效作答；玻璃與門窗讀防火保護。</summary>
    public static bool AnswersByRating(CurtainPanelKind kind) => kind == CurtainPanelKind.Solid;

    /// <summary><c>junction.panelKind</c> 的值：兩路而非三種，門窗與玻璃走同一條規則。</summary>
    public static string RuleText(CurtainPanelKind kind) =>
        AnswersByRating(kind) ? SolidRuleText : GlazedRuleText;
}
