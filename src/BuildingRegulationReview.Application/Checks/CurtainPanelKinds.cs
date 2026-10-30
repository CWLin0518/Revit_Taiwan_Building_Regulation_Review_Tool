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

    /// <summary>
    /// 讀取層怎麼認一片嵌板的種類（決議 16、步驟 16c）。判斷寫在這裡而不是在 Revit 讀取層，是為了讓它
    /// 測得到——讀取層只負責把 Revit 的三個事實（類別是不是門窗、嵌板是不是一道牆、型別參數的字）交進來。
    /// </summary>
    /// <param name="isOpening">類別是門或窗：它是防火設備，種類由類別認定，不由使用者宣告。</param>
    /// <param name="isPanelAsWall">
    /// 「嵌板為牆」：這片嵌板本身是一道 <c>Wall</c>。它是構造，一律以自己的時效作答，不需宣告
    /// （見 <see cref="CurtainPanelKind.Solid"/>）。
    /// </param>
    /// <param name="declaredText"><c>防火檢討_嵌板種類</c> 讀到的字；空白或不認得的字視為未宣告。</param>
    /// <returns>三種之一，或 null 表示未宣告——那不是實心也不是玻璃，CW-O 會答資料不足。</returns>
    /// <remarks>
    /// 宣告優先於「嵌板為牆」，不是反過來。牆型別本來就不該帶 <c>防火檢討_嵌板種類</c>（那個參數只綁
    /// Curtain Panels），所以實務上讀到的是 null 而落到 <see cref="CurtainPanelKind.Solid"/>；但真有人
    /// 在牆型別上明寫了種類時，明寫的那句話比推論可靠。
    /// </remarks>
    public static CurtainPanelKind? Classify(bool isOpening, bool isPanelAsWall, string? declaredText)
    {
        if (isOpening) return CurtainPanelKind.Opening;
        if (Parse(declaredText) is CurtainPanelKind declared) return declared;
        return isPanelAsWall ? CurtainPanelKind.Solid : (CurtainPanelKind?)null;
    }

    /// <summary>材料名稱裡代表玻璃的字；比對時不分大小寫。</summary>
    private static readonly string[] GlassWords = { "玻璃", "Glass", "Glazing", "Glazed" };

    /// <summary>Revit 材料的 <c>MaterialClass</c> 是玻璃時的值。</summary>
    private const string GlassMaterialClass = "Glass";

    /// <summary>
    /// 由嵌板型別的材料替使用者**提案**一個種類（決議 16、步驟 16c）。只提案玻璃：材料類別是
    /// <c>Glass</c>，或材料名稱裡有玻璃的字。
    /// </summary>
    /// <remarks>
    /// 只往玻璃這一邊提案是刻意的。「不是玻璃」推不出「是實心」——一片 3 mm 的鋁板不是玻璃，但把它宣告
    /// 成實心就等於說它的防火時效由厚度推定，那是工具在替設計者決定法規上的分類。提案也不等於寫入：
    /// 值先進面板的下拉，由使用者留下或改掉，寫入是使用者按下套用才發生的事（決議 16、D1）。
    /// </remarks>
    public static CurtainPanelKind? ProposeFrom(string? materialClass, string? materialName, string? typeName = null)
    {
        if (!string.IsNullOrWhiteSpace(materialClass) &&
            string.Equals(materialClass!.Trim(), GlassMaterialClass, StringComparison.OrdinalIgnoreCase))
            return CurtainPanelKind.Glazed;

        // 型別名稱是最弱的訊號，只在材料兩個欄位都沒話說時才看：系統嵌板常常沒設材料，而型別就叫
        // 「玻璃 1.0cm」。名字錯得起——提案本來就只是下拉的初值。
        return NamesGlass(materialName) || NamesGlass(typeName) ? CurtainPanelKind.Glazed : (CurtainPanelKind?)null;
    }

    private static bool NamesGlass(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;

        foreach (var word in GlassWords)
            if (text!.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;

        return false;
    }

    /// <summary><c>junction.panelKind</c> 的值：兩路而非三種，門窗與玻璃走同一條規則。</summary>
    public static string RuleText(CurtainPanelKind kind) =>
        AnswersByRating(kind) ? SolidRuleText : GlazedRuleText;
}
