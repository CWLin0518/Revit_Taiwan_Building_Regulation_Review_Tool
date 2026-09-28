using System;

namespace BuildingRegulationReview.Application.Checks;

/// <summary>
/// 一片佔位嵌板真正的構造由哪一個牆型別宣告（決議 16、步驟 16h）。
/// </summary>
/// <remarks>
/// 帷幕牆型別把 <c>Curtain Panel</c> 指到一個 <c>Basic Wall</c> 型別時，Revit 不會在格子裡放那道牆，
/// 而是放一片保留型別的嵌板（<c>System Panel : Wall</c>）：它沒有任何實體量體（實測 <c>SolidCount</c> 0、
/// 讀不到 BoundingBox、連 <c>Volume</c> 參數都不存在），型別上的每一個參數都是唯讀的，使用者連
/// <c>防火檢討_嵌板種類</c> 都填不進去。厚度／材料／時效因此只能回到那個牆型別去問——那是模型裡
/// 唯一一句關於這些格子是什麼構造的宣告。
/// <para>
/// 這是**相信型別參數的宣告**，不是量幾何：格子裡確實沒有 12 cm 的量體。使用者在知情的情況下選了
/// 這一條路（決議 16、步驟 16h）。
/// </para>
/// </remarks>
public sealed class CurtainPanelSourceType
{
    public CurtainPanelSourceType(
        string typeUniqueId,
        string displayName,
        double? thicknessMeters = null,
        string? material = null,
        string? providedRating = null)
    {
        if (string.IsNullOrWhiteSpace(typeUniqueId))
            throw new ArgumentException("Source Type UniqueId is required.", nameof(typeUniqueId));
        if (thicknessMeters is double t && (double.IsNaN(t) || double.IsInfinity(t) || t < 0))
            throw new ArgumentOutOfRangeException(nameof(thicknessMeters));

        TypeUniqueId = typeUniqueId.Trim();
        DisplayName = string.IsNullOrWhiteSpace(displayName) ? "（未命名類型）" : displayName.Trim();
        ThicknessMeters = thicknessMeters;
        Material = string.IsNullOrWhiteSpace(material) ? null : material!.Trim();
        ProvidedRating = string.IsNullOrWhiteSpace(providedRating) ? null : providedRating!.Trim();
    }

    /// <summary>供給這些格子的牆型別，例如 <c>Basic Wall : RC 牆 12cm</c>。</summary>
    public string TypeUniqueId { get; }

    /// <summary>面板要顯示的來源名稱，讓使用者看得出「要改請改那一列」。</summary>
    public string DisplayName { get; }

    /// <summary>牆型別的厚度（公尺）。</summary>
    public double? ThicknessMeters { get; }

    /// <summary>牆型別的 <c>結構材料</c>，照原樣。</summary>
    public string? Material { get; }

    /// <summary>牆型別的 <c>防火檢討_設計防火時效</c>，照原樣。</summary>
    public string? ProvidedRating { get; }

    public override string ToString() => DisplayName;
}

/// <summary>
/// 佔位嵌板型別改讀來源牆型別的述詞（決議 16、步驟 16h）。判斷寫在這裡而不是 Revit 讀取層，是為了讓
/// 它測得到：讀取層只負責把兩個事實交進來——型別參數是不是唯讀的，以及 host 帷幕牆型別的
/// <c>Curtain Panel</c> 指到的牆型別（指不到牆型別就是 null）。
/// </summary>
public static class CurtainPanelTypeSubstitution
{
    /// <summary>
    /// 這片嵌板該不該改讀 <paramref name="hostCurtainPanelWallType"/>。
    /// </summary>
    /// <param name="panelTypeParametersAreReadOnly">
    /// 嵌板型別上檢討要用的參數是唯讀的——Revit 保留型別的識別方式。**不以型別名稱比對**：
    /// 名稱是可以改的，唯讀與否是 Revit 自己說的（反射實測 <c>PanelType</c> 沒有任何欄位可問這件事）。
    /// </param>
    /// <param name="hostCurtainPanelWallType">
    /// host 帷幕牆型別的 <c>Curtain Panel</c> 所指的牆型別；指到的不是牆型別、或根本取不到 host 時是 null。
    /// </param>
    /// <returns>要改讀的來源，或 null 表示照現行路徑走（讀不到就是資料不足，不猜）。</returns>
    public static CurtainPanelSourceType? Resolve(
        bool panelTypeParametersAreReadOnly,
        CurtainPanelSourceType? hostCurtainPanelWallType) =>
        panelTypeParametersAreReadOnly ? hostCurtainPanelWallType : null;

    /// <summary>
    /// 佔位嵌板的種類。<see cref="CurtainPanelKinds.Classify"/> 先問——使用者明寫的宣告仍然最大——
    /// 它答未宣告時，來源牆型別讓這片嵌板成為**實心**。
    /// </summary>
    /// <remarks>
    /// 這是事實不是提案：模型自己說了這些格子是一道牆，所以種類不進
    /// <c>ProposeFrom</c>／<c>AwaitsPanelKind</c> 那條路，也不需要使用者按套用
    /// （<c>A_proposed_kind_is_not_a_declaration</c> 守門的是提案，不是這裡）。
    /// </remarks>
    public static CurtainPanelKind? Kind(bool isOpening, bool isPanelAsWall, string? declaredText, bool isSubstituted)
    {
        var classified = CurtainPanelKinds.Classify(isOpening, isPanelAsWall, declaredText);
        if (classified.HasValue) return classified;

        return isSubstituted ? CurtainPanelKind.Solid : (CurtainPanelKind?)null;
    }

    /// <summary>面板上那一列的說明：值是哪裡來的，以及要改該去改哪裡。</summary>
    public static string Note(CurtainPanelSourceType source)
    {
        if (source is null) throw new ArgumentNullException(nameof(source));

        return $"此嵌板型別是 Revit 的保留型別，參數唯讀，填不進去。" +
               $"厚度、結構材料與設計防火時效改由帷幕牆型別的 Curtain Panel 所指的「{source.DisplayName}」供給，" +
               $"種類視為實心。要改請改「{source.DisplayName}」那一列——那一列的變更會同時影響用到該牆型別的一般牆。";
    }
}
