using System;

namespace BuildingRegulationReview.Application.Candidates;

/// <summary>
/// 一個樓層的高程帶（Area Plan 的樓層到上一層，兩端各收一點 slack），以及「這一片帷幕嵌板算不算
/// 這一層的」那個述詞（驗證清單 C-17）。單位是英尺。
/// </summary>
/// <remarks>
/// <para>
/// 讀取層用高程帶建的是一個 <c>BoundingBoxIntersectsFilter</c>，而帷幕嵌板有兩種：一般嵌板有量體、
/// 過得了那道篩選；佔位嵌板（帷幕牆型別的 Curtain Panel 指到一個基本牆型別時 Revit 放進格子的保留
/// 型別嵌板）**沒有任何量體、讀不到 BoundingBox**，永遠過不了，所以要從宿主帷幕牆的格子把它接回來。
/// </para>
/// <para>
/// 接回來的那條路**必須自己再問一次高程**：宿主牆是以「bbox 與樓層盒相交」收的，一道 1F 通到 RF 的
/// 帷幕牆因此出現在**每一層**的牆清單裡，而 <c>CurtainGrid.GetPanelIds()</c> 回的是整面牆的嵌板。
/// 少了這道閘門，1F 會收到 5 層的嵌板，而且它們投影到平面後位置看起來完全合理，於是每一片都產生
/// 正式的檢討列——檢討表上無聲多出一堆不屬於這一層的列，還會被當成法規合規依據。
/// 「宿主牆在範圍內」不等於「宿主牆的每一片嵌板都在範圍內」。
/// </para>
/// <para>
/// 判準刻意與 <c>BoundingBoxIntersectsFilter</c> 同語意（**垂直範圍相交**，不是中心點落在帶內）：
/// 一格跨過樓板而樓板處沒有格線時，那一格對上下兩層都算，與一般嵌板走 bbox 篩選的結果一致。
/// </para>
/// </remarks>
public sealed class StoreyBand
{
    public StoreyBand(double bottomFeet, double topFeet)
    {
        if (double.IsNaN(bottomFeet) || double.IsInfinity(bottomFeet)) throw new ArgumentOutOfRangeException(nameof(bottomFeet));
        if (double.IsNaN(topFeet) || double.IsInfinity(topFeet)) throw new ArgumentOutOfRangeException(nameof(topFeet));

        // 最頂層的帶可能被 slack 收成上下顛倒（spec 的 Math.Max(top, bottom) 同一個處置）。
        BottomFeet = Math.Min(bottomFeet, topFeet);
        TopFeet = Math.Max(bottomFeet, topFeet);
    }

    public double BottomFeet { get; }
    public double TopFeet { get; }

    /// <summary>一個高程落在這一帶之內。</summary>
    public bool Contains(double elevationFeet) => elevationFeet >= BottomFeet && elevationFeet <= TopFeet;

    /// <summary>一段垂直範圍與這一帶相交，與 <c>BoundingBoxIntersectsFilter</c> 同語意。</summary>
    public bool OverlapsVertically(double lowFeet, double highFeet) =>
        Math.Max(lowFeet, highFeet) >= BottomFeet && Math.Min(lowFeet, highFeet) <= TopFeet;

    /// <summary>
    /// 一片**沒有通過樓層 bbox 篩選**的帷幕嵌板該不該接回這一層。
    /// </summary>
    /// <param name="cellLowFeet">它所在那一格的下緣高程，或 null 表示連格子都問不到。</param>
    /// <param name="cellTopFeet">它所在那一格的上緣高程，或 null 表示連格子都問不到。</param>
    /// <param name="panelHasBoundingBox">這片嵌板自己讀得到 BoundingBox。</param>
    /// <remarks>
    /// 三種情形三種答案：
    /// <list type="bullet">
    /// <item>格子問得到 → 以格子的垂直範圍與這一帶相交為準。這是佔位嵌板唯一可靠的高程來源。</item>
    /// <item>格子問不到、但嵌板**有** BoundingBox → 不接。它本來就被拿去問過樓層篩選而沒過，
    /// 代表它確實不在範圍內；從格子這條後門再放它進來，就是把篩選結果推翻。</item>
    /// <item>格子問不到、嵌板也**沒有** BoundingBox → 接。這是 C-17 那種「兩種高程來源都沒有」的
    /// 嵌板，寧可讓它進來而後因為沒有平面位置被列為人工覆核（看得見），也不要在收集階段靜默消失
    /// （看不見）。寧可多問一句，不要少掉一片。</item>
    /// </list>
    /// </remarks>
    public bool KeepsCurtainPanel(double? cellLowFeet, double? cellTopFeet, bool panelHasBoundingBox) =>
        cellLowFeet is double low && cellTopFeet is double high
            ? OverlapsVertically(low, high)
            : !panelHasBoundingBox;
}
