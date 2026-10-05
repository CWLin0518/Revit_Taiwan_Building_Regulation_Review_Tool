using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace BuildingRegulationReview.Application.Candidates;

/// <summary>
/// Whether a curtain wall is the building's 外牆 or stands inside it
/// (docs/regulations/curtain-wall-fire-compartment.md §4.8).
/// </summary>
/// <remarks>
/// 第79條第3、4項、第79條之3第2項 and 第79條之4 all speak of 外牆; a curtain wall drawn inside the
/// building is none of those, and measuring 突出 or a 層間帶 on it answers a question nobody asked. It
/// is still a 區劃 boundary when a 區劃 lies on each side of it, so its openings are judged the way any
/// boundary wall's are. Which of the two it is used to be something the tool refused to decide (§9);
/// this decides it.
/// </remarks>
public enum CurtainWallExposure
{
    /// <summary>室內外無法判定：維持人工覆核，絕不當成其中一種（docs §9 的資料不足原則）.</summary>
    Unknown,

    /// <summary>建築物外牆：照第79條第3、4項、第79條之3、第79條之4 檢討交接處.</summary>
    Exterior,

    /// <summary>室內帷幕牆：不是外牆，改以區劃邊緣上的開口是否為防火門窗檢討.</summary>
    Interior
}

/// <summary>Wall Type 的 <c>Function</c>，就照模型宣告的樣子（docs §4.8）.</summary>
/// <remarks>
/// <see cref="Exterior"/> 是 Revit「帷幕牆」系統族的**預設值**，使用者在室內畫一片而沒去改它是常態，
/// 所以它不被當成一句宣告、不能否決幾何判定；<see cref="Interior"/> 是使用者改過的非預設值，才算宣告。
/// <c>CurtainSystem</c> 沒有這個參數，一律 <see cref="NotRead"/>。
/// </remarks>
public enum CurtainWallFunctionDeclaration
{
    /// <summary>沒有這個參數，或讀取層沒有讀它.</summary>
    NotRead,

    /// <summary>Function = Exterior——系統族預設值，不具宣告效力.</summary>
    Exterior,

    /// <summary>Function = Interior——使用者改過的非預設值.</summary>
    Interior,

    /// <summary>Function 是其他值（Foundation、Retaining、Soffit、Core-Shaft）.</summary>
    Other
}

/// <summary>Why <see cref="CurtainWallExposureClassifier"/> answered the way it did.</summary>
public enum CurtainWallExposureReason
{
    /// <summary>多數測站只有一側有區劃，且都是同一側：那一側是室內，另一側是室外.</summary>
    ZoneOnOneSide,

    /// <summary>多數測站兩側各有一個**不同的**區劃：這道牆是區劃分隔，不是外牆.</summary>
    ZonesOnBothSides,

    /// <summary>多數測站兩側都落在**同一個**區劃內：這道牆站在區劃裡面，不是外牆也不是區劃邊界.</summary>
    InsideOneZone,

    /// <summary>幾何答不出來，但 Function 宣告為 Interior.</summary>
    DeclaredInterior,

    /// <summary>幾何判外牆，Function 卻宣告 Interior：模型自相矛盾，不替使用者選一邊.</summary>
    DeclarationConflict,

    /// <summary>每一個測站兩側都找不到區劃：這道牆不屬於任何區劃（docs §9）.</summary>
    NoZoneEitherSide,

    /// <summary>各測站答案不一致，沒有一種占多數.</summary>
    MixedStations,

    /// <summary>
    /// 同一道牆的各平面段判出**互相矛盾**的結果：至少一段判了外牆、至少一段判了室內
    /// （弧形帷幕牆，docs §4.7）。修法是把它拆成兩道牆建模.
    /// </summary>
    MixedFacets,

    /// <summary>
    /// 同一道牆的**每一段都判不出來**，而且各段判不出來的原因不同（例如一段沒有區劃、一段宣告與幾何
    /// 矛盾）。這不是 <see cref="MixedFacets"/>——沒有任何一段判出結果，所以「拆成兩道牆」不是修法；
    /// 要做的是各段自己那件事.
    /// </summary>
    UndecidedThroughout,

    /// <summary>
    /// 各段**一致同意**同一個結果，但同意的理由不同（例如一段兩側各有不同區劃、一段兩側同屬一個區劃）。
    /// 結論可信，但不能借用其中任一段的理由當成整道牆的觀測——那會在證據裡寫下沒發生過的事.
    /// </summary>
    AgreedAcrossFacets
}

/// <summary>
/// One probe station: the 區劃 found on each side of the curtain wall there. Which side is which does
/// not matter to the classification — only the pattern does — so the two are named after the sign of
/// the normal the geometry was read with, not after 室內／室外.
/// </summary>
public readonly struct CurtainWallExposureSample
{
    public CurtainWallExposureSample(Guid? negativeSideZoneId, Guid? positiveSideZoneId)
    {
        NegativeSideZoneId = negativeSideZoneId == Guid.Empty ? null : negativeSideZoneId;
        PositiveSideZoneId = positiveSideZoneId == Guid.Empty ? null : positiveSideZoneId;
    }

    /// <summary>The 區劃 found <c>−n</c> of the wall, or null when none is there.</summary>
    public Guid? NegativeSideZoneId { get; }

    /// <summary>The 區劃 found <c>+n</c> of the wall, or null when none is there.</summary>
    public Guid? PositiveSideZoneId { get; }

    internal bool BothSidesDifferentZones =>
        NegativeSideZoneId is Guid a && PositiveSideZoneId is Guid b && a != b;

    internal bool BothSidesSameZone =>
        NegativeSideZoneId is Guid a && PositiveSideZoneId is Guid b && a == b;

    internal bool NegativeSideOnly => NegativeSideZoneId is not null && PositiveSideZoneId is null;
    internal bool PositiveSideOnly => PositiveSideZoneId is not null && NegativeSideZoneId is null;
    internal bool NeitherSide => NegativeSideZoneId is null && PositiveSideZoneId is null;
}

/// <summary>
/// The classification and everything it was decided from, so a reviewer can tell an answer from a
/// guess (spec §5.7 Evidence Engine). The probe counts are kept rather than just the verdict: the
/// difference between「兩側都沒有區劃」and「各站答案不一致」is what the 人工覆核 message has to say.
/// </summary>
public sealed class CurtainWallExposureVerdict
{
    internal CurtainWallExposureVerdict(
        CurtainWallExposure exposure,
        CurtainWallExposureReason reason,
        CurtainWallFunctionDeclaration declaration,
        IReadOnlyList<CurtainWallExposureSample> samples,
        IReadOnlyList<CurtainWallExposureReason>? facetReasons = null)
    {
        Exposure = exposure;
        Reason = reason;
        Declaration = declaration;
        Samples = samples;
        FacetReasons = facetReasons ?? Array.Empty<CurtainWallExposureReason>();
    }

    public CurtainWallExposure Exposure { get; }
    public CurtainWallExposureReason Reason { get; }

    /// <summary>Wall Type 的 Function，就照讀到的樣子.</summary>
    public CurtainWallFunctionDeclaration Declaration { get; }

    /// <summary>The probe stations the answer was read from, in the order they were probed.</summary>
    public IReadOnlyList<CurtainWallExposureSample> Samples { get; }

    /// <summary>
    /// 各平面段自己的理由，依段序（弧形帷幕牆，docs §4.7）；單段的牆為空。<see cref="Reason"/> 是
    /// <see cref="CurtainWallExposureReason.UndecidedThroughout"/> 或
    /// <see cref="CurtainWallExposureReason.AgreedAcrossFacets"/> 時，要講的話都在這裡——整道牆沒有一個
    /// 自己的觀測可以引用，借用其中一段的會在證據裡寫下沒發生過的事.
    /// </summary>
    public IReadOnlyList<CurtainWallExposureReason> FacetReasons { get; }

    public bool IsExterior => Exposure == CurtainWallExposure.Exterior;
    public bool IsInterior => Exposure == CurtainWallExposure.Interior;
    public bool IsUnknown => Exposure == CurtainWallExposure.Unknown;

    /// <summary>Every distinct 區劃 either probe found, so a message can name them.</summary>
    public IReadOnlyList<Guid> ZoneIds => Samples
        .SelectMany(s => new[] { s.NegativeSideZoneId, s.PositiveSideZoneId })
        .OfType<Guid>()
        .Distinct()
        .OrderBy(x => x)
        .ToList();

    /// <summary>One sentence naming what was read, for a 人工覆核 message and for the evidence.</summary>
    public string Describe() => Reason switch
    {
        CurtainWallExposureReason.ZoneOnOneSide => "沿牆取樣多數只有一側有區劃，判定為建築物外牆",
        CurtainWallExposureReason.ZonesOnBothSides => "沿牆取樣多數兩側各有不同區劃，判定為室內帷幕牆",
        CurtainWallExposureReason.InsideOneZone => "沿牆取樣多數兩側落在同一個區劃內，這道牆站在區劃內部，既非外牆也非區劃邊界",
        CurtainWallExposureReason.DeclaredInterior => "沿牆取樣判不出室內外，但牆型別的 Function 宣告為 Interior",
        CurtainWallExposureReason.DeclarationConflict =>
            "沿牆取樣判定為外牆，但牆型別的 Function 宣告為 Interior，模型對同一道牆講了兩件互相矛盾的事",
        CurtainWallExposureReason.NoZoneEitherSide =>
            "沿牆取樣每一處兩側都找不到區劃，這道牆不屬於任何區劃，且牆型別的 Function 未宣告為 Interior",
        CurtainWallExposureReason.MixedStations => "沿牆取樣各處答案不一致，沒有一種占多數",
        CurtainWallExposureReason.MixedFacets => "同一道弧形帷幕牆的各平面段判出互相矛盾的室內外結果",
        CurtainWallExposureReason.UndecidedThroughout =>
            "同一道弧形帷幕牆的每一段都判不出室內外，各段的原因不同（" + FacetDescriptions() + "）",
        CurtainWallExposureReason.AgreedAcrossFacets =>
            $"同一道弧形帷幕牆的各平面段一致判為{Label(Exposure)}，但各段的依據不同（" + FacetDescriptions() + "）",
        _ => "室內外判定無法取得"
    };

    /// <summary>What the user has to do about an <see cref="CurtainWallExposure.Unknown"/>.</summary>
    public string? Remedy() => Reason switch
    {
        CurtainWallExposureReason.DeclarationConflict =>
            "請確認這道牆是室內還是室外：若為室外，請把牆型別的 Function 改回 Exterior；若為室內，請確認兩側的 Area 區劃是否都已建立",
        CurtainWallExposureReason.NoZoneEitherSide =>
            "請確認這道牆所在的樓層是否已建立 Area 區劃；若為室內帷幕牆，也可將牆型別的 Function 設為 Interior",
        CurtainWallExposureReason.MixedStations =>
            "請確認沿這道牆的 Area 區劃是否完整建立、有無缺漏或重疊",
        CurtainWallExposureReason.MixedFacets =>
            "請把這道弧形帷幕牆分成室內與室外兩道牆分別建模，本版不對同一道牆混合適用外牆與室內規則",

        // 沒有一段判出結果，所以「拆成兩道牆」不是修法——要做的是各段自己那件事。
        CurtainWallExposureReason.UndecidedThroughout => FacetRemedies(),
        _ => null
    };

    /// <summary>各段理由的中文短語，依段序，重複的只講一次.</summary>
    private string FacetDescriptions() => FacetReasons.Count == 0
        ? "各段理由未記錄"
        : string.Join("；", FacetReasons
            .Select((reason, index) => $"第 {index + 1} 段：{Phrase(reason)}"));

    /// <summary>各段修法的聯集；一段也沒有可講的就回 null，讓呼叫端不要印一句空話.</summary>
    private string? FacetRemedies()
    {
        var remedies = FacetReasons
            .Select(Remedy)
            .OfType<string>()
            .Distinct(StringComparer.Ordinal)
            .ToList();

        return remedies.Count == 0 ? null : string.Join("；並且 ", remedies);
    }

    /// <summary>一個理由的短語，不帶「判定為…」的結論——那是整道牆的事，不是一段的事.</summary>
    private static string Phrase(CurtainWallExposureReason reason) => reason switch
    {
        CurtainWallExposureReason.ZoneOnOneSide => "只有一側有區劃",
        CurtainWallExposureReason.ZonesOnBothSides => "兩側各有不同區劃",
        CurtainWallExposureReason.InsideOneZone => "兩側落在同一個區劃內",
        CurtainWallExposureReason.DeclaredInterior => "幾何判不出、由 Function 宣告為 Interior",
        CurtainWallExposureReason.DeclarationConflict => "幾何判外牆但 Function 宣告 Interior，互相矛盾",
        CurtainWallExposureReason.NoZoneEitherSide => "兩側都找不到區劃",
        CurtainWallExposureReason.MixedStations => "各取樣處答案不一致",
        CurtainWallExposureReason.MixedFacets => "與其他段互相矛盾",
        CurtainWallExposureReason.UndecidedThroughout => "判不出室內外",
        CurtainWallExposureReason.AgreedAcrossFacets => "與其他段一致",
        _ => reason.ToString()
    };

    /// <summary>The remedy a single reason asks for, with no merged-wall wording.</summary>
    private static string? Remedy(CurtainWallExposureReason reason) => reason switch
    {
        CurtainWallExposureReason.DeclarationConflict =>
            "請確認這道牆是室內還是室外：若為室外，請把牆型別的 Function 改回 Exterior；若為室內，請確認兩側的 Area 區劃是否都已建立",
        CurtainWallExposureReason.NoZoneEitherSide =>
            "請確認這道牆所在的樓層是否已建立 Area 區劃；若為室內帷幕牆，也可將牆型別的 Function 設為 Interior",
        CurtainWallExposureReason.MixedStations =>
            "請確認沿這道牆的 Area 區劃是否完整建立、有無缺漏或重疊",
        _ => null
    };

    /// <summary>The text the evidence and the baseline record, so a changed judgement invalidates a 覆寫.</summary>
    public string ToEvidenceText() =>
        $"{Text(Exposure)}／{Reason}／Function={Declaration}";

    internal static string Text(CurtainWallExposure exposure) => exposure switch
    {
        CurtainWallExposure.Exterior => "Exterior",
        CurtainWallExposure.Interior => "Interior",
        _ => "Unknown"
    };

    /// <summary>The Chinese label a 檢討表 row and a 人工覆核 message use.</summary>
    public static string Label(CurtainWallExposure exposure) => exposure switch
    {
        CurtainWallExposure.Exterior => "外牆",
        CurtainWallExposure.Interior => "室內",
        _ => "未判定"
    };

    public override string ToString() => $"{Label(Exposure)}（{Describe()}）";
}

/// <summary>
/// Decides whether a curtain wall is 外牆 or 室內 from the 區劃 found on each side of it and from the
/// Wall Type's <c>Function</c> (docs §4.8).
/// </summary>
/// <remarks>
/// <para>
/// One classifier, called from both places that need the answer: the 帷幕牆區劃交接 resolver, which
/// must not measure 突出 on an interior wall, and <see cref="CandidateResolver"/>, which must not send
/// an interior wall's openings to 人工覆核 as if they were on a façade. The two probe different things
/// — a facet's quarter points, an opening's own place on its host — so they cannot share one result
/// without reordering the pipeline, but they share this decision table, and a disagreement can never
/// silently flip an opening to「已判定」: only a positive <see cref="CurtainWallExposure.Interior"/>
/// moves it off 人工覆核.
/// </para>
/// <para>
/// Geometry decides, and <c>Function</c> only ever narrows the answer. <c>Function = Exterior</c> is
/// what Revit's 帷幕牆 system family ships, so it is not a declaration and never overrides geometry;
/// <c>Function = Interior</c> is a non-default value the user had to go and set, so it is one — it
/// settles a case geometry could not, and it contradicts a geometric 外牆 loudly enough to stop the
/// review rather than be overridden. Nothing here infers 室內 from a type name.
/// </para>
/// </remarks>
public static class CurtainWallExposureClassifier
{
    /// <summary>
    /// A pattern has to hold at **more than half** the stations. A single station is led astray by a
    /// doorway, a gap where no 區劃 was modelled, or a probe that reached a neighbouring 區劃 — the same
    /// reason 外側法線定向 samples three of them (docs §4.6). Strictly more than half, not at least
    /// half: on a tie either answer is a coin toss.
    /// </summary>
    private static bool Majority(int count, int total) => count * 2 > total;

    /// <summary>
    /// How few stations still decide. One does: a wall short enough that only its middle could be
    /// probed is still a wall, and refusing to classify it would send a perfectly ordinary 外牆 to
    /// 人工覆核 — the failure this whole reading exists to avoid. The callers probe three where the
    /// geometry allows it (<c>CurtainWallJunctionResolver.Probe</c>, <c>CandidateResolver</c>), so one
    /// or two only happens on a stretch with no length to walk; what protects such a wall is not a
    /// station count but the rest of the table, which answers <see cref="CurtainWallExposure.Unknown"/>
    /// for everything it did not positively see.
    /// </summary>
    public const int MinimumStations = 1;

    public static CurtainWallExposureVerdict Classify(
        IEnumerable<CurtainWallExposureSample> samples,
        CurtainWallFunctionDeclaration declaration = CurtainWallFunctionDeclaration.NotRead)
    {
        var list = new ReadOnlyCollection<CurtainWallExposureSample>(
            (samples ?? throw new ArgumentNullException(nameof(samples))).ToList());

        return Combine(Geometry(list), declaration, list);
    }

    /// <summary>
    /// The verdict for a whole curtain wall from the verdicts of its facets (docs §4.7). Facets that
    /// do not agree are <see cref="CurtainWallExposureReason.MixedFacets"/>: a wall half inside and
    /// half outside is not reviewed by taking a vote and dropping the losing stretches, because the
    /// number of facets follows the grid lines and has nothing to do with how much façade each is.
    /// </summary>
    public static CurtainWallExposureVerdict Merge(IEnumerable<CurtainWallExposureVerdict> facets)
    {
        var list = (facets ?? throw new ArgumentNullException(nameof(facets))).ToList();
        if (list.Count == 0) throw new ArgumentException("A wall has at least one facet.", nameof(facets));
        if (list.Count == 1) return list[0];

        var samples = list.SelectMany(v => v.Samples).ToList();
        var declaration = list[0].Declaration;
        var facetReasons = list.Select(v => v.Reason).ToList();
        var distinctReasons = facetReasons.Distinct().ToList();

        // Unknown is contagious on purpose: a stretch nobody could classify leaves the whole wall's
        // routing undecided, and routing half a wall is worse than asking.
        if (list.Any(v => v.IsUnknown))
        {
            // 三種要分開，因為修法完全不同：全段同因（照那個原因修）、全段皆未定但原因不同（各段
            // 各自修），以及真的有段判出了外牆或室內而與未定的段並存（那才是「拆成兩道牆」的情形）。
            var reason =
                distinctReasons.Count == 1 ? distinctReasons[0]
                : list.All(v => v.IsUnknown) ? CurtainWallExposureReason.UndecidedThroughout
                : CurtainWallExposureReason.MixedFacets;

            return new CurtainWallExposureVerdict(
                CurtainWallExposure.Unknown, reason, declaration, samples, facetReasons);
        }

        var distinct = list.Select(v => v.Exposure).Distinct().ToList();
        if (distinct.Count > 1)
            return new CurtainWallExposureVerdict(
                CurtainWallExposure.Unknown, CurtainWallExposureReason.MixedFacets, declaration, samples, facetReasons);

        // 各段一致同意。理由也一致時沿用它；不一致時**不借用其中任何一段的理由**——借用會在證據與
        // 訊息裡寫下一句模型裡沒發生過的觀測（例如兩段分別是 InsideOneZone 與 DeclaredInterior，
        // 卻報成「兩側各有不同區劃」）。
        return new CurtainWallExposureVerdict(
            distinct[0],
            distinctReasons.Count == 1 ? distinctReasons[0] : CurtainWallExposureReason.AgreedAcrossFacets,
            declaration,
            samples,
            facetReasons);
    }

    private static (CurtainWallExposure Exposure, CurtainWallExposureReason Reason) Geometry(
        IReadOnlyList<CurtainWallExposureSample> samples)
    {
        if (samples.Count == 0) return (CurtainWallExposure.Unknown, CurtainWallExposureReason.NoZoneEitherSide);

        var total = samples.Count;
        if (samples.All(s => s.NeitherSide)) return (CurtainWallExposure.Unknown, CurtainWallExposureReason.NoZoneEitherSide);

        // 兩側各有一個不同的區劃：這道牆就是區劃分隔，第79條第3項的「外牆」不是它。
        if (Majority(samples.Count(s => s.BothSidesDifferentZones), total))
            return (CurtainWallExposure.Interior, CurtainWallExposureReason.ZonesOnBothSides);

        // 兩側落在同一個區劃內：探測深度繞過了這道牆，它站在區劃裡面。不是外牆，也沒有區劃邊界可判。
        if (Majority(samples.Count(s => s.BothSidesSameZone), total))
            return (CurtainWallExposure.Interior, CurtainWallExposureReason.InsideOneZone);

        // 多數測站只有一側有區劃，而且是同一側：另一側是建築物外部。兩個方向分開數，不是加總——
        // 一站只有北側、一站只有南側，那不是一面外牆，是模型在這條線上講了兩件事（下面落 MixedStations）。
        // 「單側有區劃就是外牆」是既有 FacingOutside 已在用、且已於 Revit 驗證過的判讀；改成「單側
        // 不足以證實外牆」會讓每一片真正的外牆帷幕牆都變成人工覆核，等於刪掉一個可用的功能。
        if (Majority(samples.Count(s => s.NegativeSideOnly), total) ||
            Majority(samples.Count(s => s.PositiveSideOnly), total))
            return (CurtainWallExposure.Exterior, CurtainWallExposureReason.ZoneOnOneSide);

        return (CurtainWallExposure.Unknown, CurtainWallExposureReason.MixedStations);
    }

    private static CurtainWallExposureVerdict Combine(
        (CurtainWallExposure Exposure, CurtainWallExposureReason Reason) geometry,
        CurtainWallFunctionDeclaration declaration,
        IReadOnlyList<CurtainWallExposureSample> samples)
    {
        var declaredInterior = declaration == CurtainWallFunctionDeclaration.Interior;

        var (exposure, reason) = geometry.Exposure switch
        {
            // 幾何說室內：Function=Exterior 是系統族預設值，不足以否決它。
            CurtainWallExposure.Interior => geometry,

            // 幾何說外牆、使用者卻把 Function 改成 Interior：兩者不能同時為真，交人工覆核。
            CurtainWallExposure.Exterior when declaredInterior =>
                (CurtainWallExposure.Unknown, CurtainWallExposureReason.DeclarationConflict),

            CurtainWallExposure.Exterior => geometry,

            // 幾何答不出來，宣告就是唯一線索。
            _ when declaredInterior => (CurtainWallExposure.Interior, CurtainWallExposureReason.DeclaredInterior),

            _ => geometry
        };

        return new CurtainWallExposureVerdict(exposure, reason, declaration, samples);
    }
}
