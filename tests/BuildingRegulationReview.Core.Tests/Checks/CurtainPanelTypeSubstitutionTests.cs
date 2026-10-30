using System;
using BuildingRegulationReview.Application.Candidates;
using BuildingRegulationReview.Application.Checks;
using BuildingRegulationReview.Application.Parameters;
using Xunit;

namespace BuildingRegulationReview.Core.Tests.Checks;

/// <summary>
/// 決議 16、步驟 16h: 佔位嵌板型別（Revit 的 <c>System Panel : Wall</c>）改讀帷幕牆型別的
/// <c>Curtain Panel</c> 所指的牆型別。判斷放在 Application 層，所以不必進 Revit 就測得到——讀取層
/// 只交兩個事實進來：型別參數是不是唯讀的，以及那個帷幕牆型別指到的是不是一個牆型別。
/// </summary>
public sealed class CurtainPanelTypeSubstitutionTests
{
    private static CurtainPanelSourceType Source(
        double? thicknessCm = 12,
        string? material = "RC",
        string? rating = "120分") =>
        new("W-RC12", "Basic Wall：RC 牆 12cm",
            thicknessMeters: thicknessCm is double cm ? cm / 100 : (double?)null,
            material: material,
            providedRating: rating);

    // --- Resolve（什麼時候才接手）---------------------------------------------------------------

    /// <summary>唯讀＋指得到牆型別，兩個條件都成立才接手。</summary>
    [Fact]
    public void A_read_only_panel_type_is_answered_by_the_wall_type_the_curtain_wall_type_names()
    {
        var source = Source();

        Assert.Same(source, CurtainPanelTypeSubstitution.Resolve(panelTypeParametersAreReadOnly: true, source));
    }

    /// <summary>
    /// 填得進去的型別不接手，即使它的帷幕牆型別剛好也指著一個牆型別。使用者在嵌板型別上填的字才是
    /// 這一列的答案——借來的值會把它蓋掉。
    /// </summary>
    [Fact]
    public void A_writable_panel_type_answers_for_itself_even_when_a_wall_type_is_available()
    {
        Assert.Null(CurtainPanelTypeSubstitution.Resolve(panelTypeParametersAreReadOnly: false, Source()));
    }

    /// <summary>
    /// 唯讀但指不到牆型別（帷幕系統的嵌板、或 Curtain Panel 指的是系統嵌板族）時回 null：那是
    /// 「問不出來」，照現行路徑走會答資料不足——不猜一個。
    /// </summary>
    [Fact]
    public void A_read_only_panel_type_with_no_wall_type_is_left_as_insufficient_data()
    {
        Assert.Null(CurtainPanelTypeSubstitution.Resolve(panelTypeParametersAreReadOnly: true, null));
    }

    // --- Kind（接手之後種類是什麼）--------------------------------------------------------------

    /// <summary>接手的那一片是實心：模型自己說了那些格子是一道牆。</summary>
    [Fact]
    public void A_substituted_panel_is_solid()
    {
        Assert.Equal(CurtainPanelKind.Solid, CurtainPanelTypeSubstitution.Kind(
            isOpening: false, isPanelAsWall: false, declaredText: null, isSubstituted: true));
    }

    /// <summary>沒有接手時，述詞與 <see cref="CurtainPanelKinds.Classify"/> 逐項相同——16h 不動舊路。</summary>
    [Theory]
    [InlineData(false, false, null)]
    [InlineData(false, false, "玻璃")]
    [InlineData(false, false, "實心")]
    [InlineData(false, true, null)]
    [InlineData(true, false, "實心")]
    [InlineData(true, true, null)]
    public void Without_a_substitution_the_predicate_is_the_old_one(bool isOpening, bool isPanelAsWall, string? declared)
    {
        Assert.Equal(
            CurtainPanelKinds.Classify(isOpening, isPanelAsWall, declared),
            CurtainPanelTypeSubstitution.Kind(isOpening, isPanelAsWall, declared, isSubstituted: false));
    }

    /// <summary>
    /// 明寫的宣告仍然最大。保留型別填不進字，所以實務上碰不到；真碰到了，使用者寫的那句話比
    /// 從帷幕牆型別推來的可靠——與 <see cref="CurtainPanelKinds.Classify"/> 同一個原則。
    /// </summary>
    [Fact]
    public void A_declaration_still_beats_the_wall_type_it_was_borrowed_from()
    {
        Assert.Equal(CurtainPanelKind.Glazed, CurtainPanelTypeSubstitution.Kind(
            isOpening: false, isPanelAsWall: false, declaredText: "玻璃", isSubstituted: true));
    }

    /// <summary>帷幕牆上的門窗是防火設備，接手與否都改變不了它的類別。</summary>
    [Fact]
    public void An_opening_stays_an_opening_even_when_a_wall_type_is_borrowed()
    {
        Assert.Equal(CurtainPanelKind.Opening, CurtainPanelTypeSubstitution.Kind(
            isOpening: true, isPanelAsWall: false, declaredText: null, isSubstituted: true));
    }

    // --- 面板那一列 ------------------------------------------------------------------------------

    private static FireReviewTypeRow PanelRow(CurtainPanelSourceType? substitutedFrom, string? panelKind = null) =>
        new("P-1", CandidateCategory.CurtainPanel, "Wall", familyName: "System Panel",
            instanceCount: 11, projectInstanceCount: 11,
            // 佔位型別自己報的厚度是 1 cm，與構造無關；接手之後這一格不該再出現。
            dimensionMeters: 0.01,
            material: null, coverMeters: null, providedRating: null,
            providedProtection: null, providedSmokeProtection: null,
            present: FireReviewTypeParameters.Rating | FireReviewTypeParameters.Material | FireReviewTypeParameters.PanelKind,
            panelKind: panelKind, proposedPanelKind: null, substitutedFrom: substitutedFrom);

    /// <summary>三個欄位顯示的是來源牆型別的值，不是佔位型別自己的。</summary>
    [Fact]
    public void A_substituted_row_shows_the_wall_types_thickness_material_and_rating()
    {
        var row = PanelRow(Source());

        Assert.True(row.IsSubstituted);
        Assert.Equal(0.12, row.DimensionMeters!.Value, 6);
        Assert.Equal("RC", row.Material);
        Assert.Equal("120分", row.ProvidedRating);
        Assert.Equal("Basic Wall：RC 牆 12cm", row.SubstitutedFrom!.DisplayName);
    }

    /// <summary>
    /// 接手的那一列不再是「待宣告」：種類是事實，不是等使用者填的空格。面板的待辦數字與
    /// <see cref="FireReviewTypeTable.AwaitingPanelKind"/> 都不該再點到它。
    /// </summary>
    [Fact]
    public void A_substituted_row_is_no_longer_waiting_for_a_panel_kind()
    {
        var row = PanelRow(Source());

        Assert.Equal(CurtainPanelKind.Solid, row.ParsedPanelKind);
        Assert.False(row.AwaitsPanelKind);
        Assert.Null(row.PanelKindProposal);
        Assert.Empty(new FireReviewTypeTable(new[] { row }).AwaitingPanelKind);
    }

    /// <summary>
    /// 一片實心嵌板以時效作答、不答防火門窗——接手來的實心與使用者宣告的實心走同一條路。
    /// </summary>
    [Fact]
    public void A_substituted_row_answers_by_rating_like_any_other_solid_panel()
    {
        var row = PanelRow(Source());

        Assert.True(row.CarriesRating);
        Assert.False(row.CarriesProtection);
    }

    /// <summary>
    /// 推定值一個字都寫不回去（型別參數唯讀），所以這一列不列進「可推定」，批次套用也不會試著寫它。
    /// </summary>
    [Fact]
    public void A_substituted_row_is_never_offered_for_derivation_or_written_back()
    {
        var row = PanelRow(Source());

        Assert.False(row.SupportsDerivation);
        Assert.False(row.WouldChangeRating);
        Assert.Null(row.RatingEdit());
        Assert.Null(row.PanelKindEdit(CurtainPanelKind.Solid));
        Assert.Empty(new FireReviewTypeTable(new[] { row }).Derivable);
    }

    /// <summary>
    /// 沒有來源時這一列完全照舊：讀自己的 1 cm、等著使用者宣告種類。16h 只多一條路，不改舊的。
    /// </summary>
    [Fact]
    public void Without_a_source_the_panel_row_is_exactly_what_it_was_before()
    {
        var row = PanelRow(null);

        Assert.False(row.IsSubstituted);
        Assert.Equal(0.01, row.DimensionMeters!.Value, 6);
        Assert.True(row.AwaitsPanelKind);
        Assert.Null(row.ParsedPanelKind);
    }

    /// <summary>
    /// 借來的值只給帷幕嵌板。別的類別帶了來源也不採用——那會讓一列牆的厚度悄悄變成另一個型別的。
    /// </summary>
    [Fact]
    public void Only_a_curtain_panel_row_borrows_from_a_wall_type()
    {
        var wall = new FireReviewTypeRow("W-1", CandidateCategory.Wall, "RC15",
            dimensionMeters: 0.15, substitutedFrom: Source());

        Assert.False(wall.IsSubstituted);
        Assert.Equal(0.15, wall.DimensionMeters!.Value, 6);
    }

    /// <summary>來源的名稱一定講得出來，說明文字才指得出「要改請改哪一列」。</summary>
    [Fact]
    public void The_note_names_the_wall_type_the_user_has_to_edit_instead()
    {
        Assert.Contains("Basic Wall：RC 牆 12cm", CurtainPanelTypeSubstitution.Note(Source()));
        Assert.Throws<ArgumentNullException>(() => CurtainPanelTypeSubstitution.Note(null!));
    }
}
