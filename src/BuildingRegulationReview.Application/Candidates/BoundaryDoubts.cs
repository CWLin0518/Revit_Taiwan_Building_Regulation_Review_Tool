namespace BuildingRegulationReview.Application.Candidates;

/// <summary>
/// 「只是不確定它**是不是**區劃邊界」的那幾種疑義——全專案唯一的一份定義（驗證清單 C-19 第二層）。
/// </summary>
/// <remarks>
/// 原本這個判準在 <c>FireResistanceCheck.IsBoundaryDoubt</c>、<c>MergedAtriums.Counts</c> 與
/// <c>FireReviewRunner.BoundaryDoubtLabel</c> 各寫一份，三份都是「列出認得的 kind，其餘忽略」，新增
/// 一種邊界疑義時任何一份漏改都會回到無聲：漏改構件時效那份是 C-18 復發（靜默免除），漏改 runner
/// 那份是 C-19 復發（交接處靜默消失）。現在三處都問這裡。
/// <para>
/// 不收的那些是「對資料本身的疑義」：連結模型、沒有平面幾何、區劃本身未封閉或重疊……它們不是
/// 「這道牆算不算邊界」的問題，兩種假設也評不出來。
/// </para>
/// </remarks>
public static class BoundaryDoubts
{
    /// <summary>給審查者看的名字；不是邊界疑義時為 null。</summary>
    /// <remarks>
    /// 沿輪廓（<see cref="CandidateAmbiguityKind.BoundaryAlongOutline"/>）今天只有柱會產生——牆走中心線
    /// 那條路——但仍列出來：牆若哪天以輪廓量測，它不該又變回無聲。
    /// </remarks>
    public static string? Label(CandidateAmbiguityKind? kind) => kind switch
    {
        CandidateAmbiguityKind.BoundaryCrossed => "跨越邊界",
        CandidateAmbiguityKind.BoundaryOffCenterline => "偏離中心線",
        CandidateAmbiguityKind.BoundaryAlongOutline => "沿輪廓",
        _ => null
    };

    public static bool Is(CandidateAmbiguityKind kind) => Label(kind) is not null;

    /// <summary>
    /// 這個關係掛著一筆邊界疑義。問的是 <see cref="ZoneRelation.Ambiguity"/> 而不是
    /// <see cref="ZoneRelation.IsAmbiguous"/>：跨越邊界的牆關係是 <see cref="ZoneRelationKind.Crossing"/>、
    /// 疑義掛在 <c>BoundaryCrossed</c> 上（驗證清單 C-18），問窄的那一個會漏掉它。
    /// </summary>
    public static bool Carries(ZoneRelation relation) => Label(relation.Ambiguity) is not null;
}
