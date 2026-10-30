using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using BuildingRegulationReview.Application.Candidates;
using BuildingRegulationReview.Application.Checks;
using BuildingRegulationReview.Application.Reviews;

namespace BuildingRegulationReview.Application.Parameters;

/// <summary>
/// One element Type as the batch panel shows it: what the model already holds, and what the clauses
/// would derive. Pure data (spec 15) — the adapter fills it, the panel edits it, and
/// <see cref="FireReviewParameterEdit"/> carries the result back.
/// </summary>
/// <remarks>
/// A row is a Type, not an element: 防火檢討_設計防火時效, 結構材料, 防火被覆厚度 and the openings'
/// 防火檢討_設計防火保護 are all Type parameters, so one edit reaches every instance of that Type in
/// the project — not only the ones visible in the view the list was collected from.
/// <see cref="InstanceCount"/> is what the view showed; <see cref="ProjectInstanceCount"/> is what
/// the edit actually affects.
/// </remarks>
public sealed class FireReviewTypeRow
{
    private readonly double? _dimensionMeters;
    private readonly string? _material;
    private readonly string? _providedRating;

    public FireReviewTypeRow(
        string typeUniqueId,
        CandidateCategory category,
        string typeName,
        string? familyName = null,
        int instanceCount = 0,
        int projectInstanceCount = 0,
        double? dimensionMeters = null,
        string? material = null,
        double? coverMeters = null,
        string? providedRating = null,
        bool? providedProtection = null,
        bool? providedSmokeProtection = null,
        FireReviewTypeParameters present = FireReviewTypeParameters.None,
        string? panelKind = null,
        CurtainPanelKind? proposedPanelKind = null,
        CurtainPanelSourceType? substitutedFrom = null,
        bool? providedInsulation = null)
    {
        if (string.IsNullOrWhiteSpace(typeUniqueId)) throw new ArgumentException("Type UniqueId is required.", nameof(typeUniqueId));
        if (instanceCount < 0) throw new ArgumentOutOfRangeException(nameof(instanceCount));
        if (projectInstanceCount < 0) throw new ArgumentOutOfRangeException(nameof(projectInstanceCount));
        if (dimensionMeters is double d && (double.IsNaN(d) || double.IsInfinity(d) || d < 0))
            throw new ArgumentOutOfRangeException(nameof(dimensionMeters));
        if (coverMeters is double c && (double.IsNaN(c) || double.IsInfinity(c) || c < 0))
            throw new ArgumentOutOfRangeException(nameof(coverMeters));

        TypeUniqueId = typeUniqueId.Trim();
        Category = category;
        TypeName = string.IsNullOrWhiteSpace(typeName) ? "（未命名類型）" : typeName.Trim();
        FamilyName = string.IsNullOrWhiteSpace(familyName) ? null : familyName!.Trim();
        InstanceCount = instanceCount;
        ProjectInstanceCount = Math.Max(projectInstanceCount, instanceCount);
        _dimensionMeters = dimensionMeters;
        _material = string.IsNullOrWhiteSpace(material) ? null : material!.Trim();
        CoverMeters = coverMeters;
        _providedRating = string.IsNullOrWhiteSpace(providedRating) ? null : providedRating!.Trim();
        // 只有帷幕嵌板會遇上 Revit 的保留型別，別的類別帶了來源也不採用——那會讓一列牆的值悄悄
        // 變成另一個型別的值（決議 16、步驟 16h）。
        SubstitutedFrom = category == CandidateCategory.CurtainPanel ? substitutedFrom : null;
        ProvidedProtection = providedProtection;
        ProvidedSmokeProtection = providedSmokeProtection;
        ProvidedInsulation = providedInsulation;
        Present = present;
        PanelKind = string.IsNullOrWhiteSpace(panelKind) ? null : panelKind!.Trim();
        // 門窗的種類由類別認定，不是提案，也不寫回；把它當提案帶進來只會讓面板在門窗列上出現一個
        // 使用者不該碰的下拉值（CurtainPanelKinds.ParameterText 對 Opening 也回 null）。
        ProposedPanelKind = proposedPanelKind == CurtainPanelKind.Opening ? null : proposedPanelKind;
    }

    public string TypeUniqueId { get; }
    public CandidateCategory Category { get; }
    public string TypeName { get; }
    public string? FamilyName { get; }

    /// <summary>How many instances of this Type the collected view showed.</summary>
    public int InstanceCount { get; }

    /// <summary>How many instances the whole project holds — the real reach of an edit.</summary>
    public int ProjectInstanceCount { get; }

    /// <summary>
    /// 牆厚／板厚／柱短邊 in metres, as the Type reports it — or, on a 佔位嵌板列, as the wall Type the
    /// curtain wall Type names reports it (<see cref="SubstitutedFrom"/>、決議 16、步驟 16h).
    /// </summary>
    public double? DimensionMeters => IsSubstituted ? SubstitutedFrom!.ThicknessMeters : _dimensionMeters;

    /// <summary>結構材料 exactly as the parameter holds it (佔位嵌板列讀來源牆型別的).</summary>
    public string? Material => IsSubstituted ? SubstitutedFrom!.Material : _material;

    /// <summary>防火被覆厚度 in metres.</summary>
    public double? CoverMeters { get; }

    /// <summary>防火檢討_設計防火時效 exactly as the parameter holds it (佔位嵌板列讀來源牆型別的).</summary>
    public string? ProvidedRating => IsSubstituted ? SubstitutedFrom!.ProvidedRating : _providedRating;

    /// <summary>
    /// 這一列的厚度／材料／時效是哪一個牆型別供給的，或 null 表示這一列就是自己作答
    /// （<see cref="CurtainPanelTypeSubstitution"/>、決議 16、步驟 16h）。
    /// </summary>
    public CurtainPanelSourceType? SubstitutedFrom { get; }

    /// <summary>
    /// 這一列是 Revit 的保留嵌板型別，參數唯讀：面板顯示來源牆型別的值，但一個字都寫不回去，
    /// 所以 <see cref="RatingEdit"/> 與 <see cref="PanelKindEdit"/> 都不產生編輯，面板那一列也唯讀。
    /// </summary>
    public bool IsSubstituted => SubstitutedFrom is not null;

    /// <summary>
    /// 防火檢討_設計防火保護 as the Type's Yes/No parameter holds it (openings only): true is ticked,
    /// false is a box that exists but is not ticked, and null is a Type that does not carry the
    /// parameter at all. The review reads false as 否, but null must stay null here so an untouched
    /// row writes nothing back.
    /// </summary>
    public bool? ProvidedProtection { get; }

    /// <summary>
    /// 防火檢討_遮煙性能 as the Type's Yes/No parameter holds it (openings only), read the same way as
    /// <see cref="ProvidedProtection"/>: 遮煙性能 is a separate question from 防火門窗 and 第79條之2第1項
    /// asks a 昇降機道's 防火設備 both (垂直區劃文件 §6).
    /// </summary>
    public bool? ProvidedSmokeProtection { get; }

    /// <summary>
    /// 防火檢討_阻熱性 as the Type's Yes/No parameter holds it (openings only): 第79條第1項 asks every
    /// 防火設備 on a 區劃 boundary for 一小時以上之阻熱性 (垂直區劃規格決議 38).
    /// </summary>
    public bool? ProvidedInsulation { get; }

    /// <summary>Which of the review parameters this Type actually carries.</summary>
    public FireReviewTypeParameters Present { get; }

    /// <summary>
    /// 防火檢討_嵌板種類 exactly as the parameter holds it (帷幕嵌板 only). Null is 未宣告, which is not
    /// the same as either kind: the row then shows both gaps rather than guessing (決議 16).
    /// </summary>
    public string? PanelKind { get; }

    /// <summary>
    /// 實心／玻璃, or null when the declaration is blank or is neither. 佔位嵌板列沒有宣告可讀
    /// （參數唯讀），由來源牆型別補成實心——那是事實，不是提案（決議 16、步驟 16h）。
    /// </summary>
    public CurtainPanelKind? ParsedPanelKind =>
        CurtainPanelTypeSubstitution.Kind(isOpening: false, isPanelAsWall: false, PanelKind, IsSubstituted);

    /// <summary>
    /// 種類的提案，由讀取層從嵌板型別的材料算出（<see cref="CurtainPanelKinds.ProposeFrom"/>、決議 16、
    /// 步驟 16c）。這**不是**模型裡的值：<see cref="PanelKind"/> 才是。面板用它當下拉的初值，寫入仍然
    /// 要使用者按下套用，因此 <see cref="AwaitsPanelKind"/> 與 <see cref="Present"/> 都不受它影響。
    /// </summary>
    public CurtainPanelKind? ProposedPanelKind { get; }

    /// <summary>
    /// 真正該拿去當面板初值的提案：只在型別自己還沒宣告時才有意義，已宣告的值不該被提案蓋掉。
    /// </summary>
    public CurtainPanelKind? PanelKindProposal =>
        Category == CandidateCategory.CurtainPanel && ParsedPanelKind is null ? ProposedPanelKind : null;

    public bool IsOpening => CandidateCategories.IsOpening(Category);

    /// <summary>A 帷幕嵌板 whose 防火檢討_嵌板種類 has not been declared — it answers neither question yet.</summary>
    public bool AwaitsPanelKind => Category == CandidateCategory.CurtainPanel && ParsedPanelKind is null;

    /// <summary>
    /// Whether 結構材料＋尺寸 → 時效 is worth offering on this row: the clauses have to give the
    /// category a threshold, the row has to answer by 時效 at all, and an undeclared 帷幕嵌板 has to
    /// say which kind it is first.
    /// </summary>
    /// <remarks>
    /// 佔位嵌板列排除在外：推定值只有寫得回去才有意義，而那個型別的參數是唯讀的——推得出 120 分也
    /// 套用不了，列進「可推定」只會讓批次套用每一次都少寫一列而說不出原因（決議 16、步驟 16h）。
    /// </remarks>
    public bool SupportsDerivation =>
        FireRatingDeriver.IsDerivable(Category) && CarriesRating && !AwaitsPanelKind && !IsSubstituted;

    /// <summary>
    /// Whether this Type answers 設計防火時效. Every 主要構造 does; among the openings, 門 does because
    /// 第79條之2第1項 requires a 管道間之維修門 to reach one hour (垂直區劃文件 §6), and a 帷幕嵌板 does
    /// when it is declared 實心, because 第79條第4項 and 第79條之3第2項 measure the 交接帶 by the panels'
    /// own rating (帷幕牆規格 §6、決議 16). A 玻璃 panel answers 防火保護 instead, exactly like a 窗;
    /// 窗 answer 防火門窗 and 遮煙性能 only — no clause states a rating for them.
    /// </summary>
    /// <remarks>
    /// An undeclared 帷幕嵌板 carries both, so the panel shows both as gaps. Guessing either way would
    /// collapse 「實心嵌板漏填時效」(資料不足) into 「玻璃嵌板不是防火設備」(未符合), which is the whole
    /// reason 防火檢討_嵌板種類 exists.
    /// </remarks>
    public bool CarriesRating => Category == CandidateCategory.CurtainPanel
        ? ParsedPanelKind != CurtainPanelKind.Glazed
        : !IsOpening || Category != CandidateCategory.Window;

    /// <summary>
    /// 防火門窗 is asked of the openings, 可開啟嵌板 included — but not of a 帷幕嵌板 declared 實心,
    /// which is a piece of 構造 answering by its own 時效, not a 防火設備 (決議 16).
    /// </summary>
    public bool CarriesProtection => Category == CandidateCategory.CurtainPanel
        ? ParsedPanelKind != CurtainPanelKind.Solid
        : IsOpening;

    /// <summary>
    /// 遮煙性能 is asked of every opening: a 昇降機道 出入口 may be a 門, a 窗 or a 帷幕嵌板, and a
    /// 管道間 維修門 is a 門 (垂直區劃文件 §4).
    /// </summary>
    public bool CarriesSmokeProtection => IsOpening;

    public StructuralMaterial? ParsedMaterial => StructuralMaterialText.Parse(Material);

    public string CategoryLabel => CandidateCategories.Label(Category);

    public string DisplayName => FamilyName is null ? TypeName : $"{FamilyName}：{TypeName}";

    /// <summary>
    /// What the clauses derive for this row. 門、窗 and 梁 derive nothing; a 帷幕嵌板 derives on the
    /// 牆壁 thresholds (決議 16). A 玻璃 panel goes through the same call and comes back with nothing,
    /// because glass is not RC／SRC／SC and does not reach 第73條's 7cm either.
    /// </summary>
    public FireRatingDerivation Derivation =>
        FireRatingDeriver.Derive(Category, ParsedMaterial, DimensionMeters, CoverMeters);

    /// <summary>
    /// 這一列在等 結構材料 才推得出時效。玻璃嵌板不算——它不由時效作答，不會被問結構材料（決議 16）。
    /// 判斷本身在 <see cref="FireRatingDerivationGaps"/>，面板狀態列與 MCP 讀的是同一個。
    /// </summary>
    public bool AwaitsMaterial => FireRatingDerivationGaps.AwaitsMaterial(SupportsDerivation, Derivation);

    /// <summary>這一列是 SC，在等 防火被覆厚度；同樣不含不由時效作答的列。</summary>
    public bool AwaitsCover => FireRatingDerivationGaps.AwaitsCover(SupportsDerivation, Derivation);

    /// <summary>The derived rating differs from what the Type already holds, so applying it would change something.</summary>
    public bool WouldChangeRating
    {
        get
        {
            // A 玻璃 panel does not answer by 時效 at all, and an undeclared one has not said yet, so a
            // rating derived for either is never applied — the batch 「套用推定值」 must not put a number
            // into a parameter the review will not read.
            if (!SupportsDerivation) return false;
            var derived = Derivation;
            if (!derived.HasRating) return false;
            var current = ReviewInputAssembler.Rating(
                ProvidedRating is null ? ParameterReading.Empty : ParameterReading.OfText(ProvidedRating));
            return current.Kind != ProvidedFireRatingKind.Rated || current.Minutes != derived.Minutes;
        }
    }

    /// <summary>The edit that writes the derived rating, or null when there is nothing to write.</summary>
    public FireReviewParameterEdit? RatingEdit() =>
        SupportsDerivation && Derivation.ParameterText is string text
            ? FireReviewParameterEdit.OfText(TypeUniqueId, FireRatingParameters.Provided, text)
            : null;

    /// <summary>
    /// The edit that declares this 帷幕嵌板's kind, for the proposal the reader makes from the panel's
    /// material (決議 16). Null for anything that is not a 帷幕嵌板, for <see cref="CurtainPanelKind.Opening"/>,
    /// which the reader recognises by category and never writes back, and for a 佔位嵌板列, whose
    /// 嵌板種類 parameter is read-only — its 實心 comes from the wall Type, not from a write (步驟 16h).
    /// </summary>
    public FireReviewParameterEdit? PanelKindEdit(CurtainPanelKind kind) =>
        Category == CandidateCategory.CurtainPanel && !IsSubstituted &&
        CurtainPanelKinds.ParameterText(kind) is string text
            ? FireReviewParameterEdit.OfText(TypeUniqueId, CurtainPanelKindParameters.Provided, text)
            : null;

    public override string ToString() => $"{CategoryLabel}／{DisplayName}";
}

/// <summary>Which review parameters a Type carries, so the panel can say 「此類型沒有這個參數」.</summary>
[Flags]
public enum FireReviewTypeParameters
{
    None = 0,
    Rating = 1,
    Material = 2,
    Cover = 4,
    Protection = 8,

    /// <summary>防火檢討_遮煙性能 (第79條之2第1項).</summary>
    SmokeSeal = 16,

    /// <summary>防火檢討_嵌板種類 (決議 16); only 帷幕嵌板 are expected to carry it.</summary>
    PanelKind = 32,

    /// <summary>防火檢討_阻熱性 (第79條第1項、垂直區劃規格決議 38).</summary>
    Insulation = 64
}

/// <summary>What kind of value one edit carries, so the adapter never has to guess from the text.</summary>
public enum FireReviewEditKind
{
    Text,
    Length,
    YesNo
}

/// <summary>One parameter value to write to one Type. The kinds are kept apart so the adapter never guesses.</summary>
public sealed class FireReviewParameterEdit
{
    private FireReviewParameterEdit(FireReviewEditKind kind, string elementUniqueId, string parameterName, string? text, double? lengthMeters, bool yesNo)
    {
        if (string.IsNullOrWhiteSpace(elementUniqueId)) throw new ArgumentException("Element UniqueId is required.", nameof(elementUniqueId));
        if (string.IsNullOrWhiteSpace(parameterName)) throw new ArgumentException("Parameter name is required.", nameof(parameterName));

        Kind = kind;
        ElementUniqueId = elementUniqueId.Trim();
        ParameterName = parameterName.Trim();
        Text = text;
        LengthMeters = lengthMeters;
        YesNo = yesNo;
    }

    public static FireReviewParameterEdit OfText(string elementUniqueId, string parameterName, string? value)
    {
        if (FireRatingParameters.IsRequiredParameter(parameterName))
            throw new ArgumentException($"{FireRatingParameters.Required} 是規則回寫用的參數，面板不得寫入。", nameof(parameterName));
        return new FireReviewParameterEdit(FireReviewEditKind.Text, elementUniqueId, parameterName, value ?? string.Empty, null, false);
    }

    public static FireReviewParameterEdit OfLength(string elementUniqueId, string parameterName, double? meters)
    {
        if (meters is double m && (double.IsNaN(m) || double.IsInfinity(m) || m < 0))
            throw new ArgumentOutOfRangeException(nameof(meters), "長度不能是負數或非有限值。");
        return new FireReviewParameterEdit(FireReviewEditKind.Length, elementUniqueId, parameterName, null, meters, false);
    }

    /// <summary>A ticked or unticked checkbox. Unticked is written as 0, which the review reads as 否.</summary>
    public static FireReviewParameterEdit OfYesNo(string elementUniqueId, string parameterName, bool value)
    {
        if (FireRatingParameters.IsRequiredParameter(parameterName))
            throw new ArgumentException($"{FireRatingParameters.Required} 是規則回寫用的參數，面板不得寫入。", nameof(parameterName));
        return new FireReviewParameterEdit(FireReviewEditKind.YesNo, elementUniqueId, parameterName, null, null, value);
    }

    public FireReviewEditKind Kind { get; }
    public string ElementUniqueId { get; }
    public string ParameterName { get; }

    /// <summary>The text to write; null unless this edit is a text. An empty string clears the parameter.</summary>
    public string? Text { get; }

    /// <summary>The length in metres; null unless this edit is a length.</summary>
    public double? LengthMeters { get; }

    /// <summary>Whether the box is ticked; meaningful only when <see cref="Kind"/> is Yes/No.</summary>
    public bool YesNo { get; }

    public bool IsLength => Kind == FireReviewEditKind.Length;

    public override string ToString() => Kind switch
    {
        // 驗證清單 C-11：`x?.ToString() + " m" ?? "…"` 永遠不是 null（+ 比 ?? 先結合），所以無值的
        // 長度本來會印成「… =  m」。它是 MCP `edits[].value` 的字樣，照原樣會讓呼叫端讀不出這一筆
        // 其實寫不進去，而「清除」又不是長度參數做得到的事。
        FireReviewEditKind.Length => $"{ParameterName} = " + (LengthMeters is double metres
            ? metres.ToString("0.###", CultureInfo.InvariantCulture) + " m"
            : "（無值；長度參數清不回未填）"),
        FireReviewEditKind.YesNo => $"{ParameterName} = {(YesNo ? "是" : "否")}",
        _ => $"{ParameterName} = {Text}"
    };
}

/// <summary>
/// What Revit can and cannot take back out of a parameter, and how to say so to the user.
/// </summary>
/// <remarks>
/// 驗證清單 C-05：面板與 MCP 都把「清除這一格」表示成一個空字串的文字編輯，但 Revit API 的
/// <c>Parameter</c> 只有 <c>Set</c>、沒有「取消設值」。只有字串類參數設成空字串就等於空白；Integer
/// （YESNO 也是 Integer：Revit 以 0／1 存是否）與長度類都寫不出「未填」這個狀態——一個已經寫入的
/// 值本來就回不到未填。寫入器原本把這件事誤判成「這個值的型別不對」，於是使用者
/// 把「所在樓層序」清空後拿到的是「資料型別（Integer）無法接受這個值。」：原因說錯，也沒給可行的
/// 下一步，而使用者讀起來會以為是自己填錯了。
/// <para>
/// 同一個分支也會被「填了一個非數字的字」觸發（例如樓層序打「三樓」），那種情形「型別不接受」其實
/// 是對的，所以兩者必須分開判、分開講。判斷與措辭放在 Application 而不是放在寫入器裡，面板才能在
/// 產生編輯之前就用同一份規則事先攔下來——使用者該在那一格就知道清不掉，不必按下「寫入模型」才看到
/// 一筆失敗。
/// </para>
/// <para>
/// 驗證清單 C-11：長度類參數也清不回未填，只是失敗的方式更壞。<c>Set(0.0)</c> 成功了，所以舊的寫入器
/// 把一次清除回報成一次寫入（<c>written</c> 多算一筆），而且模型裡被悄悄留下一個使用者沒有宣告過的
/// 0——Revit 的類型屬性、明細表與任何其他讀取端看到的都是「0」。長度格的判斷與整數格並列在這裡，
/// 理由相同：面板要在產生編輯之前就認得出這一格，MCP 的 <c>ignored</c> 也用同一句理由。
/// </para>
/// <para>
/// 寫 0 的真正代價因此是模型被改與回報說謊兩件，不是一個錯的判定。而 0 在推定的語意上確實是一個
/// 「宣告」——SC 被覆未達第71～73條任一被覆門檻（最低 3 公分，第73條第1款第2目）→「無防火時效」——
/// 所以使用者自己打 <c>0</c> 必須仍然寫得進去，不可以被這道攔阻擋掉。
/// </para>
/// <para>
/// 驗證清單 C-20：那個宣告原本無法往返——掃描層只把大於 0 的長度當成有值，存著 0 的被覆重掃後讀回
/// null，格子空白、該列回到「待填被覆」。2026-10-16 先走「甲」（只讓限制可見），2026-10-27 改走
/// **「乙」**：<c>RevitFireReviewTypeScanner.CoverMeters</c> 給被覆一個獨立的讀取述詞，參數
/// <c>HasValue</c> 為真就接受 0。柱短邊與嵌板厚仍走 <c>IsPositiveLength</c> 的 <c>&gt; 0</c>，不受影響。
/// 乙當初沒選，是怕「剛綁定／從未填寫的長度參數是 <c>HasValue == true, AsDouble() == 0</c>」，那會讓
/// 每一條沒填被覆的 SC 列都變成「已宣告 0 公分」。2026-10-27 在實機模型上查過：從未填寫的類型，
/// 被覆參數讀不到值（<c>HasValue</c> 為假），只有真的寫過 0 的類型讀得到 0，所以那個前提在 Revit 2024
/// 以本工具綁定的共用參數上不成立。殘留的風險是 C-11 修好之前（2026-10-15 以前的版本）用面板「清空」
/// 被覆時被悄悄寫進去的 0：那些 0 現在會被讀成宣告。推定那一欄會寫出「SC 被覆 0.0cm…無防火時效」，
/// 審查者看得到；而檢討本身不讀被覆厚度，判定不受影響。
/// </para>
/// <para>
/// <see cref="DeclaresZeroCover"/> 與 <see cref="ZeroCoverNotice"/> 留下來，但講的事變了：不再是
/// 「讀不回來」，而是「0 的意思是無防火時效」——使用者在宣告的那一刻知道後果，面板的確認視窗與
/// MCP 的回應各取用同一句話。
/// </para>
/// </remarks>
public static class FireReviewParameterLimits
{
    /// <summary>How a text edit reads against a parameter Revit stores as an Integer.</summary>
    public enum IntegerTextVerdict
    {
        /// <summary>A whole number that can be written.</summary>
        Number,

        /// <summary>Blank — a request to clear the value, which Revit cannot carry out.</summary>
        NotClearable,

        /// <summary>Not blank, and not a whole number either.</summary>
        NotANumber
    }

    /// <summary>
    /// Reads the text an Integer-storage parameter was handed, telling 「想清空」 apart from
    /// 「打錯了」. Blank includes whitespace: a cell the user emptied may keep a stray space, and
    /// that is still a request to clear rather than a value.
    /// </summary>
    /// <remarks>
    /// Invariant culture, spelled out rather than left to <c>int.TryParse(string, out int)</c>'s
    /// current-culture default. 面板的 <c>IntegerOf</c>／<c>TextOf</c> 讀寫同一格文字時用的就是
    /// invariant，而整個攔阻判斷建立在「面板認得的數字＝寫入器認得的數字」上：兩邊若用不同文化，
    /// 一個在面板過關的值可能在寫入器被判成非數字，或反過來讓面板放過一筆註定失敗的編輯。
    /// </remarks>
    public static IntegerTextVerdict ReadIntegerText(string? text, out int number)
    {
        number = 0;
        if (string.IsNullOrWhiteSpace(text)) return IntegerTextVerdict.NotClearable;
        return int.TryParse(text!.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out number)
            ? IntegerTextVerdict.Number
            : IntegerTextVerdict.NotANumber;
    }

    /// <summary>How a text edit reads against a parameter Revit stores as a Double (a length).</summary>
    /// <remarks>驗證清單 C-11。</remarks>
    public enum LengthTextVerdict
    {
        /// <summary>A finite, non-negative number of centimetres — 0 included, which declares 0 cm.</summary>
        Number,

        /// <summary>Blank — a request to clear the value, which Revit cannot carry out.</summary>
        NotClearable,

        /// <summary>Not blank, and not a length either: not a number, negative, or not finite.</summary>
        NotANumber
    }

    /// <summary>
    /// Reads the centimetres a length cell holds, telling 「想清空」 apart from 「打錯了」 — and keeping
    /// a typed <c>0</c> apart from both, because 0 公分被覆 is a declaration the user is entitled to make.
    /// </summary>
    /// <remarks>
    /// Invariant culture for the same reason <see cref="ReadIntegerText"/> spells it out: the panel's
    /// cell, the MCP tool and the writer all have to agree on which strings are numbers, or a value
    /// that passes in one place is refused in another. Negative is 「打錯了」 rather than a number:
    /// <see cref="FireReviewParameterEdit.OfLength"/> refuses it outright, so letting it through here
    /// would only trade a sentence the user can act on for an exception.
    /// <para>
    /// <paramref name="metres"/> 刻意是 <c>double?</c> 而不是 <c>double</c>：在一個「存在意義就是把 0
    /// 與空白分開」的類別裡，若空白時 <c>out</c> 回 0，日後一句
    /// <c>ReadCentimetresText(t, out var m); write(m);</c> 就把 C-11 原封不動重現一次，而編譯器不會
    /// 講話。回 null 讓那個呼叫端非得先面對「這一格沒有值」才拿得到數字。
    /// </para>
    /// </remarks>
    public static LengthTextVerdict ReadCentimetresText(string? text, out double? metres)
    {
        metres = null;
        if (string.IsNullOrWhiteSpace(text)) return LengthTextVerdict.NotClearable;
        if (!double.TryParse(text!.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var centimetres) ||
            double.IsNaN(centimetres) || double.IsInfinity(centimetres) || centimetres < 0)
            return LengthTextVerdict.NotANumber;

        metres = centimetres / 100;
        return LengthTextVerdict.Number;
    }

    /// <summary>Why a write that would clear an Integer／YESNO parameter cannot be carried out.</summary>
    public static string NotClearableReason(string parameterName) =>
        $"Revit 無法清除整數或是否類參數 {parameterName}：API 只能設值、不能取消設值，已寫入的值回不到未填狀態。" +
        "請改填一個值；若真的要清空，須在 Revit 中直接刪除這個參數的值，或改用其他欄位表達。";

    /// <summary>
    /// Why a write that would clear a length parameter cannot be carried out (驗證清單 C-11).
    /// </summary>
    /// <remarks>
    /// Said separately from <see cref="NotClearableReason(string)"/> because the reason is different:
    /// the integer case fails loudly, while a length would take <c>Set(0.0)</c> and succeed — and 0 is
    /// an answer, not the absence of one. The sentence has to name that, or the user reads it as the
    /// same arbitrary refusal and tries typing 0 themselves.
    /// </remarks>
    public static string NotClearableLengthReason(string parameterName) =>
        $"Revit 無法清除長度參數 {parameterName}：API 只能設值、不能取消設值，而寫入 0 是「宣告 0 公分」" +
        "（SC 列會被推定為無防火時效），不是「未填」。請改填一個值；若真的要清空，須在 Revit 中直接刪除這個參數的值。";

    /// <summary>The not-clearable reason in the words that fit the kind of value the edit carries.</summary>
    public static string NotClearableReason(string parameterName, FireReviewEditKind kind) =>
        kind == FireReviewEditKind.Length
            ? NotClearableLengthReason(parameterName)
            : NotClearableReason(parameterName);

    /// <summary>The same limit said in the panel's own terms, named by the cell's label.</summary>
    public static string CannotClearHint(string label) =>
        $"「{label}」已有值，清空後無法寫回未填狀態（Revit 的整數／是否類參數只能設值、不能取消設值）。" +
        "請填一個值，或在 Revit 中直接刪除這個參數的值。";

    /// <summary>What to say about an Integer cell holding something that is not a whole number.</summary>
    public static string NotANumberHint(string label, string? typed) =>
        $"「{label}」填的「{(typed ?? string.Empty).Trim()}」不是整數，無法寫入；請改填阿拉伯數字（例如 3）。";

    /// <summary>
    /// The length limit in the panel's own terms, named by the cell's label (驗證清單 C-11).
    /// </summary>
    /// <remarks>
    /// 不與 <see cref="CannotClearHint"/> 共用一句話：長度格的使用者看不到任何失敗（舊行為寫 0 就成功了），
    /// 所以這句要說出「清空會被當成宣告 0 公分」，否則他只會把 0 打回去，正好做出那個他不想做的宣告。
    /// </remarks>
    public static string CannotClearLengthHint(string label) =>
        $"「{label}」已有值，清空後無法寫回未填狀態（長度參數只能設值；寫入 0 是宣告 0 公分，不是未填）。" +
        "請填一個值，或在 Revit 中直接刪除這個參數的值。";

    /// <summary>What to say about a length cell holding something that is not a length.</summary>
    public static string NotALengthHint(string label, string? typed) =>
        $"「{label}」填的「{(typed ?? string.Empty).Trim()}」不是有效的公分數值，無法寫入；請改填 0 或正數（例如 4）。";

    /// <summary>
    /// Why a length cell's current content cannot be written, or null when it can.
    /// </summary>
    /// <remarks>
    /// Read exactly like <see cref="IntegerCellProblem"/>: 「打錯了」 and 「想清空」 are two problems and
    /// get two sentences, and a blank cell whose parameter never held a value is not a problem at all —
    /// nothing is being taken back out, so nothing is written and nothing is wrong. A typed <c>0</c> is
    /// never a problem: 被覆 0 公分 is a declaration the clauses can act on (未達第71～73條任一被覆門檻,
    /// 最低 3 公分 第73條第1款第2目 → 無防火時效), and it is the one thing this guard must not make
    /// impossible. 這一格對牆／板／柱／嵌板都問，所以不寫成柱的 4 公分那一條。
    /// </remarks>
    public static string? LengthCellProblem(string label, double? stored, string? typed)
    {
        switch (ReadCentimetresText(typed, out _))
        {
            case LengthTextVerdict.NotANumber:
                return NotALengthHint(label, typed);
            case LengthTextVerdict.NotClearable:
                return stored.HasValue ? CannotClearLengthHint(label) : null;
            default:
                return null;
        }
    }

    /// <summary>
    /// Why a Yes/No cell's current content cannot be written, or null when it can.
    /// </summary>
    /// <remarks>
    /// Only clearing is impossible: 是 and 否 both write a value, and a cell whose parameter never
    /// held one (<paramref name="stored"/> is null) has nothing to take back out — leaving it blank
    /// writes nothing and is not an error. <paramref name="typed"/> is read as the panel reads it:
    /// anything that is neither 是 nor 否 is 未填.
    /// </remarks>
    public static string? YesNoCellProblem(string label, bool? stored, string? typed) =>
        stored.HasValue && !IsYesOrNo(typed) ? CannotClearHint(label) : null;

    /// <summary>
    /// Why an Integer cell's current content cannot be written, or null when it can.
    /// </summary>
    /// <remarks>
    /// 「打錯了」 and 「想清空」 are two different problems and get two different sentences. A blank
    /// cell whose parameter never held a value is not a problem at all; a blank cell over a stored
    /// value is a clear Revit cannot carry out. Text that is not a whole number is always a problem,
    /// stored value or not: the panel normalises what was typed, so a 「三樓」 left in the box would
    /// otherwise reach the writer as a clear nobody asked for.
    /// </remarks>
    public static string? IntegerCellProblem(string label, int? stored, string? typed)
    {
        switch (ReadIntegerText(typed, out _))
        {
            case IntegerTextVerdict.NotANumber:
                return NotANumberHint(label, typed);
            case IntegerTextVerdict.NotClearable:
                return stored.HasValue ? CannotClearHint(label) : null;
            default:
                return null;
        }
    }

    /// <summary>
    /// Whether this edit declares a 被覆 of 0 公分 — which 第71～73條 read as 無防火時效
    /// (驗證清單 C-20).
    /// </summary>
    /// <remarks>
    /// 問的是「這一筆編輯在宣告 0」，不是「這一格空白」：空白是
    /// <see cref="LengthTextVerdict.NotClearable"/>，根本不會變成一筆編輯（C-11）。所以這個述詞只會
    /// 對使用者真的打了 <c>0</c>、或 MCP 真的送了 <c>coverCm: 0</c> 的那一筆為真。
    /// <para>
    /// 刻意**只認 <c>防火被覆厚度</c>**，而不是「任何值為 0 的長度編輯」。一個泛用的述詞配一句泛用的
    /// 文案，就得把「依第71～73條推定為無防火時效」這句拿掉——而那正是這句話唯一能讓審查者行動的部分。今天面板也只有這一個長度格；日後若多出別的長度參數，該為它寫自己
    /// 的述詞與自己的後果，而不是讓它套用被覆的句子。
    /// </para>
    /// </remarks>
    public static bool DeclaresZeroCover(FireReviewParameterEdit? edit) =>
        edit != null && edit.IsLength && edit.LengthMeters.HasValue && edit.LengthMeters.Value == 0 &&
        string.Equals(edit.ParameterName, StructuralMaterialParameters.Cover, StringComparison.Ordinal);

    /// <summary>
    /// 「你在宣告 0 公分，它的意思是無防火時效」這句話（驗證清單 C-20）。
    /// </summary>
    /// <remarks>
    /// 乙方案之後 0 讀得回來，這句話不再講往返限制，只講後果：使用者若只是想「清掉」這一格，打 0
    /// 會得到一個「無防火時效」的推定，而不是空白。
    /// <para>
    /// 文案放在 Application 而不是放在面板或 MCP 裡，理由與 C-05／C-11 相同：兩個出口必須講同一句
    /// 話，而且這句話測得到——面板與 MCP 的接線在 WPF／net48，測試專案碰不到。
    /// </para>
    /// </remarks>
    public static string ZeroCoverDeclaredNotice =>
        $"「{StructuralMaterialParameters.Cover}」寫入 0 是宣告被覆 0 公分：依第71～73條推定為「無防火時效」" +
        "（被覆未達任何門檻）。若這不是您的意思，請改填實際的被覆厚度。";

    /// <summary>
    /// 同一個宣告，但這一批**沒有真的寫進模型**時該說的話（驗證清單 C-20）。
    /// </summary>
    /// <remarks>
    /// MCP 有三條路走這一句：<c>dryRun</c>、「寫入途中失敗、整批復原」，以及「整批 commit 了但這一筆
    /// 被寫入器略過」（例如那個參數沒綁到這個類別）。呼叫端（尤其是自動化的）不可以因為看到宣告的
    /// 說明就以為已經寫好了。
    /// </remarks>
    public static string UnwrittenZeroCoverDeclaredNotice =>
        $"「{StructuralMaterialParameters.Cover}」這一筆是宣告被覆 0 公分（依第71～73條推定為「無防火時效」），" +
        "但本批並未實際寫入模型（試跑，或寫入未成功、整批復原），所以模型裡還沒有這個值。";

    /// <summary>
    /// 這一批編輯裡有「宣告被覆 0 公分」時要附上的那一句話，沒有就回 null（驗證清單 C-20）。
    /// </summary>
    /// <param name="edits">這一批要寫的編輯。</param>
    /// <param name="written">
    /// 這一批是否真的落地。面板的確認視窗是「將寫入…要繼續嗎？」，按下去就會寫，所以用預設的
    /// <c>true</c>（文案本身是未來式的「會寫進模型」）；MCP 要傳
    /// <c>write.Committed &amp;&amp; !dryRun</c>——<c>dryRun</c> 下內層交易照樣 commit，
    /// 只有外層 TransactionGroup 回滾，所以 <c>Committed</c> 仍是 true。
    /// </param>
    /// <remarks>
    /// 回 null 而不是空字串，是為了讓呼叫端非得先面對「這批到底有沒有」才拿得到文字——面板只有在
    /// 真的有這種編輯時才在確認視窗多出一段，MCP 也只有在那時才多一條 notice。
    /// </remarks>
    public static string? ZeroCoverNotice(IEnumerable<FireReviewParameterEdit>? edits, bool written = true) =>
        edits is not null && edits.Any(DeclaresZeroCover)
            ? (written ? ZeroCoverDeclaredNotice : UnwrittenZeroCoverDeclaredNotice)
            : null;

    /// <summary>
    /// Whether the text is one of the two answers a YESNO cell can write. Kept here beside the
    /// predicate that depends on it so the panel's 是／否 and this judgement cannot drift apart.
    /// </summary>
    private static bool IsYesOrNo(string? typed) =>
        string.Equals(typed, YesText, StringComparison.Ordinal) ||
        string.Equals(typed, NoText, StringComparison.Ordinal);

    /// <summary>What the panel's 是 cell holds; the writer turns it into 1.</summary>
    public const string YesText = "是";

    /// <summary>What the panel's 否 cell holds; the writer turns it into 0.</summary>
    public const string NoText = "否";
}

/// <summary>The Types one view contributed, grouped and ordered the way the panel lists them.</summary>
public sealed class FireReviewTypeTable
{
    public FireReviewTypeTable(IEnumerable<FireReviewTypeRow>? rows, IEnumerable<string>? warnings = null)
    {
        Rows = new ReadOnlyCollection<FireReviewTypeRow>(
            (rows ?? Array.Empty<FireReviewTypeRow>())
            .GroupBy(r => r.TypeUniqueId, StringComparer.Ordinal)
            .Select(g => g.First())
            .OrderBy(r => CandidateCategories.Members.Contains(r.Category) ? 0 : 1)
            .ThenBy(r => r.Category)
            .ThenBy(r => r.DisplayName, StringComparer.CurrentCulture)
            .ToList());
        Warnings = new ReadOnlyCollection<string>((warnings ?? Array.Empty<string>()).ToList());
    }

    public IReadOnlyList<FireReviewTypeRow> Rows { get; }
    public IReadOnlyList<string> Warnings { get; }

    public IEnumerable<FireReviewTypeRow> Of(CandidateCategory category) => Rows.Where(r => r.Category == category);

    /// <summary>Rows whose derived rating differs from what the Type holds — what「套用推定值」would write.</summary>
    public IReadOnlyList<FireReviewTypeRow> Derivable => Rows.Where(r => r.WouldChangeRating).ToList();

    /// <summary>Rows that need 防火被覆厚度 before SC can be rated at all.</summary>
    public IReadOnlyList<FireReviewTypeRow> AwaitingCover => Rows.Where(r => r.AwaitsCover).ToList();

    /// <summary>
    /// Rows whose 結構材料 is blank or unrecognised. A 玻璃 panel is not one of them — it never answers
    /// by 時效, so it is not waiting on a material it will never be asked for (決議 16).
    /// </summary>
    public IReadOnlyList<FireReviewTypeRow> AwaitingMaterial => Rows.Where(r => r.AwaitsMaterial).ToList();

    /// <summary>
    /// 帷幕嵌板 that have not declared 實心 or 玻璃 yet (決議 16). Until they do, CW-O cannot decide
    /// which question to ask of them, so the review answers 資料不足 — the panel lists these first.
    /// </summary>
    public IReadOnlyList<FireReviewTypeRow> AwaitingPanelKind => Rows.Where(r => r.AwaitsPanelKind).ToList();
}
