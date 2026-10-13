using System;
using System.Linq;
using BuildingRegulationReview.Application.Candidates;
using BuildingRegulationReview.Application.Checks;
using BuildingRegulationReview.Application.Parameters;
using Xunit;

namespace BuildingRegulationReview.Core.Tests.Parameters;

public sealed class FireReviewTypeTableTests
{
    private static FireReviewTypeRow Row(
        string uid = "T-1",
        CandidateCategory category = CandidateCategory.Wall,
        double? dimensionCm = 20,
        string? material = "RC",
        double? coverCm = null,
        string? rating = null,
        bool? protection = null,
        bool? smokeProtection = null,
        int inView = 3,
        int inProject = 9,
        FireReviewTypeParameters present = FireReviewTypeParameters.Rating | FireReviewTypeParameters.Material,
        string? panelKind = null,
        CurtainPanelKind? proposedPanelKind = null) =>
        new(uid, category, "RC20", familyName: null, instanceCount: inView, projectInstanceCount: inProject,
            dimensionMeters: dimensionCm is double d ? d / 100 : (double?)null,
            material: material,
            coverMeters: coverCm is double c ? c / 100 : (double?)null,
            providedRating: rating, providedProtection: protection, providedSmokeProtection: smokeProtection,
            present: present, panelKind: panelKind, proposedPanelKind: proposedPanelKind);

    [Fact]
    public void A_row_derives_the_rating_its_material_and_thickness_give()
    {
        var row = Row(dimensionCm: 8, material: "RC");

        Assert.Equal(60, row.Derivation.Minutes);
        Assert.Equal("60 min", row.RatingEdit()!.Text);
        Assert.Equal(FireRatingParameters.Provided, row.RatingEdit()!.ParameterName);
    }

    [Fact]
    public void A_row_whose_rating_already_matches_would_change_nothing()
    {
        Assert.False(Row(dimensionCm: 8, rating: "60 min").WouldChangeRating);
        Assert.False(Row(dimensionCm: 8, rating: "1hr").WouldChangeRating);
        Assert.True(Row(dimensionCm: 8, rating: "30 min").WouldChangeRating);
        Assert.True(Row(dimensionCm: 8, rating: null).WouldChangeRating);
        Assert.True(Row(dimensionCm: 8, rating: "耐燃").WouldChangeRating);
    }

    [Fact]
    public void A_row_that_derives_nothing_would_change_nothing()
    {
        Assert.False(Row(material: null).WouldChangeRating);
        Assert.False(Row(category: CandidateCategory.StructuralFraming).WouldChangeRating);
        Assert.Null(Row(material: null).RatingEdit());
    }

    /// <summary>The project count is the real reach of a Type edit, so it can never be the smaller number.</summary>
    [Fact]
    public void The_project_count_is_never_less_than_what_the_view_showed()
    {
        Assert.Equal(9, Row(inView: 3, inProject: 9).ProjectInstanceCount);
        Assert.Equal(7, Row(inView: 7, inProject: 2).ProjectInstanceCount);
    }

    [Fact]
    public void The_table_orders_members_before_openings_and_drops_duplicate_types()
    {
        var table = new FireReviewTypeTable(new[]
        {
            Row("T-door", CandidateCategory.Door, dimensionCm: null, material: null),
            Row("T-wall", CandidateCategory.Wall),
            Row("T-wall", CandidateCategory.Wall),
            Row("T-floor", CandidateCategory.Floor)
        });

        Assert.Equal(3, table.Rows.Count);
        Assert.Equal(new[] { CandidateCategory.Wall, CandidateCategory.Floor, CandidateCategory.Door },
            table.Rows.Select(r => r.Category));
    }

    [Fact]
    public void The_table_separates_rows_waiting_on_material_from_rows_waiting_on_cover()
    {
        var table = new FireReviewTypeTable(new[]
        {
            Row("T-1", material: null),
            Row("T-2", material: "SC", coverCm: null),
            Row("T-3", material: "SC", coverCm: 4),
            Row("T-4", material: "RC", dimensionCm: 20)
        });

        Assert.Equal(new[] { "T-1" }, table.AwaitingMaterial.Select(r => r.TypeUniqueId));
        Assert.Equal(new[] { "T-2" }, table.AwaitingCover.Select(r => r.TypeUniqueId));
        Assert.Equal(new[] { "T-3", "T-4" }, table.Derivable.Select(r => r.TypeUniqueId).OrderBy(x => x));
    }

    [Fact]
    public void An_edit_keeps_text_and_length_apart()
    {
        var text = FireReviewParameterEdit.OfText("T-1", StructuralMaterialParameters.Material, "RC");
        Assert.False(text.IsLength);
        Assert.Equal("RC", text.Text);
        Assert.Null(text.LengthMeters);

        var length = FireReviewParameterEdit.OfLength("T-1", StructuralMaterialParameters.Cover, 0.04);
        Assert.True(length.IsLength);
        Assert.Equal(0.04, length.LengthMeters);
        Assert.Null(length.Text);
    }

    /// <summary>The panel must never set the parameter the rules write back to (spec 11.5 step 4).</summary>
    [Fact]
    public void The_required_rating_parameter_cannot_be_edited_by_the_panel()
    {
        var thrown = Assert.Throws<ArgumentException>(() =>
            FireReviewParameterEdit.OfText("T-1", FireRatingParameters.Required, "60 min"));

        Assert.Contains(FireRatingParameters.Required, thrown.Message);
    }

    [Fact]
    public void An_edit_rejects_a_blank_type_or_parameter_and_a_negative_length()
    {
        Assert.Throws<ArgumentException>(() => FireReviewParameterEdit.OfText(" ", "P", "v"));
        Assert.Throws<ArgumentException>(() => FireReviewParameterEdit.OfText("T-1", " ", "v"));
        Assert.Throws<ArgumentOutOfRangeException>(() => FireReviewParameterEdit.OfLength("T-1", "P", -0.01));
    }

    [Fact]
    public void A_row_rejects_impossible_measurements()
    {
        Assert.Throws<ArgumentException>(() => Row(uid: " "));
        Assert.Throws<ArgumentOutOfRangeException>(() => Row(dimensionCm: -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => Row(coverCm: -1));
    }

    [Fact]
    public void An_opening_row_derives_nothing_and_is_recognised_as_an_opening()
    {
        var door = Row("T-door", CandidateCategory.Door, dimensionCm: null, material: null, protection: true);

        Assert.True(door.IsOpening);
        Assert.Equal(FireRatingDerivationKind.NotDerivable, door.Derivation.Kind);
        Assert.True(door.ProvidedProtection);
    }

    /// <summary>
    /// 防火保護 is a Type parameter, so a row keeps 「沒有這個參數」 apart from 「有，但沒勾」: the
    /// first must write nothing, the second is an answer（否）.
    /// </summary>
    [Fact]
    public void An_opening_row_keeps_an_unbound_checkbox_apart_from_an_unticked_one()
    {
        Assert.Null(Row("T-door", CandidateCategory.Door, protection: null).ProvidedProtection);
        Assert.False(Row("T-door", CandidateCategory.Door, protection: false).ProvidedProtection);
    }

    /// <summary>
    /// The panel writes the tick to the Type, and unticking writes 0 rather than clearing — the
    /// review reads 0 as 否, and there is no way to put a Yes/No box back to「沒填」anyway.
    /// </summary>
    [Fact]
    public void A_checkbox_edit_names_the_type_and_carries_the_tick_as_a_yes_no()
    {
        var ticked = FireReviewParameterEdit.OfYesNo("T-door", FireProtectionParameters.Provided, true);

        Assert.Equal(FireReviewEditKind.YesNo, ticked.Kind);
        Assert.Equal("T-door", ticked.ElementUniqueId);
        Assert.True(ticked.YesNo);
        Assert.False(ticked.IsLength);
        Assert.Contains("是", ticked.ToString());

        Assert.False(FireReviewParameterEdit.OfYesNo("T-door", FireProtectionParameters.Provided, false).YesNo);
    }

    [Fact]
    public void A_checkbox_edit_may_not_target_the_write_back_parameter()
    {
        Assert.Throws<ArgumentException>(() =>
            FireReviewParameterEdit.OfYesNo("T-door", FireRatingParameters.Required, true));
    }

    // --- 帷幕嵌板（帷幕牆規格 §6、§12 步驟 5）-----------------------------------------------------

    /// <summary>
    /// An undeclared 帷幕嵌板 shows both gaps. Guessing either way would collapse 「實心嵌板漏填時效」
    /// (資料不足, fixable) into 「玻璃嵌板不是防火設備」(未符合, a design problem), which is the whole
    /// reason 防火檢討_嵌板種類 exists (決議 16).
    /// </summary>
    [Fact]
    public void An_undeclared_curtain_panel_answers_both_the_rating_and_the_protection()
    {
        var panel = Row("T-panel", CandidateCategory.CurtainPanel);

        Assert.True(panel.IsOpening);
        Assert.True(panel.AwaitsPanelKind);
        Assert.Null(panel.ParsedPanelKind);
        Assert.True(panel.CarriesRating);
        Assert.True(panel.CarriesProtection);
    }

    /// <summary>
    /// 垂直區劃文件 §6: a 門 answers 設計防火時效 as well, because a 管道間之維修門 owes one hour under
    /// 第79條之2第1項. A 窗 still answers neither — no clause states a rating for one.
    /// </summary>
    [Fact]
    public void A_door_answers_the_rating_as_well_but_a_window_does_not()
    {
        var door = Row("T-door", CandidateCategory.Door);
        Assert.True(door.CarriesRating);
        Assert.True(door.CarriesProtection);

        var window = Row("T-window", CandidateCategory.Window);
        Assert.False(window.CarriesRating);
        Assert.True(window.CarriesProtection);
    }

    /// <summary>
    /// 遮煙性能 is asked of every opening (a 昇降機道 出入口 may be a 門, 窗 or 帷幕嵌板) and of no
    /// 主要構造 — it is a property of a 防火設備, not of a wall.
    /// </summary>
    [Fact]
    public void Every_opening_answers_the_smoke_seal_and_no_member_does()
    {
        foreach (var category in new[] { CandidateCategory.Door, CandidateCategory.Window, CandidateCategory.CurtainPanel })
            Assert.True(Row("T-" + category, category).CarriesSmokeProtection);

        foreach (var category in CandidateCategories.Members)
            Assert.False(Row("T-" + category, category).CarriesSmokeProtection);
    }

    /// <summary>
    /// 遮煙性能 is read the way 防火門窗 is: ticked, an unticked box, or a Type that does not carry the
    /// parameter at all — and the last one has to stay apart from 否 so an untouched row writes nothing.
    /// </summary>
    [Fact]
    public void The_smoke_seal_keeps_an_unbound_type_apart_from_an_unticked_box()
    {
        Assert.Null(Row("T-door", CandidateCategory.Door).ProvidedSmokeProtection);
        Assert.False(Row("T-door", CandidateCategory.Door, smokeProtection: false).ProvidedSmokeProtection);
        Assert.True(Row("T-door", CandidateCategory.Door, smokeProtection: true).ProvidedSmokeProtection);
    }

    /// <summary>遮煙性能 and 防火門窗 are two questions, so they are two parameters (垂直區劃文件 §6).</summary>
    [Fact]
    public void The_smoke_seal_is_not_the_fire_protection_parameter()
    {
        Assert.NotEqual(FireProtectionParameters.Provided, SmokeProtectionParameters.Provided);
        Assert.Equal("防火檢討_遮煙性能", SmokeProtectionParameters.Provided);
    }

    [Fact]
    public void A_structural_member_answers_the_rating_only()
    {
        foreach (var category in CandidateCategories.Members)
        {
            var row = Row("T-" + category, category);
            Assert.True(row.CarriesRating);
            Assert.False(row.CarriesProtection);
        }
    }

    // --- 嵌板種類（決議 16、帷幕牆規格 §3.3）------------------------------------------------------

    /// <summary>
    /// 實心 answers by 時效 only, 玻璃 by 防火保護 only. One declaration, two different questions —
    /// that is the split 決議 16 makes.
    /// </summary>
    [Fact]
    public void The_declared_kind_decides_which_question_a_curtain_panel_answers()
    {
        var solid = Row("T-solid", CandidateCategory.CurtainPanel, panelKind: CurtainPanelKinds.SolidText);
        Assert.Equal(CurtainPanelKind.Solid, solid.ParsedPanelKind);
        Assert.True(solid.CarriesRating);
        Assert.False(solid.CarriesProtection);
        Assert.False(solid.AwaitsPanelKind);

        var glazed = Row("T-glazed", CandidateCategory.CurtainPanel, panelKind: CurtainPanelKinds.GlazedText);
        Assert.Equal(CurtainPanelKind.Glazed, glazed.ParsedPanelKind);
        Assert.False(glazed.CarriesRating);
        Assert.True(glazed.CarriesProtection);
        Assert.False(glazed.AwaitsPanelKind);
    }

    /// <summary>遮煙性能 is asked of a 帷幕嵌板 whatever its kind: a 昇降機道 出入口 may be one (垂直區劃文件 §4).</summary>
    [Fact]
    public void The_declared_kind_does_not_change_who_answers_the_smoke_seal()
    {
        foreach (var kind in new string?[] { null, CurtainPanelKinds.SolidText, CurtainPanelKinds.GlazedText })
            Assert.True(Row("T-" + kind, CandidateCategory.CurtainPanel, panelKind: kind).CarriesSmokeProtection);
    }

    /// <summary>A kind nobody recognises is 未宣告, not a third kind — the same reading <see cref="CurtainPanelKinds.Parse"/> makes.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("鋁板")]
    public void An_unreadable_kind_leaves_the_panel_undeclared(string kind)
    {
        var panel = Row("T-panel", CandidateCategory.CurtainPanel, panelKind: kind);

        Assert.Null(panel.ParsedPanelKind);
        Assert.True(panel.AwaitsPanelKind);
    }

    /// <summary>Only a 帷幕嵌板 is ever waiting on a kind; a wall has no such question to answer.</summary>
    [Fact]
    public void Nothing_but_a_curtain_panel_waits_on_a_kind()
    {
        var table = new FireReviewTypeTable(new[]
        {
            Row("T-wall", CandidateCategory.Wall),
            Row("T-door", CandidateCategory.Door, dimensionCm: null, material: null),
            Row("T-undeclared", CandidateCategory.CurtainPanel, dimensionCm: null, material: null),
            Row("T-solid", CandidateCategory.CurtainPanel, panelKind: CurtainPanelKinds.SolidText)
        });

        Assert.Equal(new[] { "T-undeclared" }, table.AwaitingPanelKind.Select(r => r.TypeUniqueId));
    }

    /// <summary>
    /// 第72條一(一)／第73條一(一) say 「牆壁」, not 「承重牆壁」, so a 實心嵌板 is derived on the wall's own
    /// thresholds — the whole point of declaring 實心 (決議 16、D5).
    /// </summary>
    [Fact]
    public void A_solid_curtain_panel_derives_its_rating_from_its_thickness()
    {
        var panel = Row("T-solid", CandidateCategory.CurtainPanel, dimensionCm: 10, material: "RC",
            panelKind: CurtainPanelKinds.SolidText);

        Assert.True(panel.SupportsDerivation);
        Assert.Equal(120, panel.Derivation.Minutes);
        Assert.True(panel.WouldChangeRating);
        Assert.Equal("120 min", panel.RatingEdit()!.Text);
        Assert.Equal(FireRatingParameters.Provided, panel.RatingEdit()!.ParameterName);
    }

    /// <summary>
    /// A 玻璃 panel does not answer by 時效, so 「套用推定值」 must not write one into it even if the
    /// numbers happened to add up — and it is not waiting on a 結構材料 it will never be asked for.
    /// </summary>
    [Fact]
    public void A_glazed_curtain_panel_is_never_given_a_derived_rating()
    {
        var thick = Row("T-glazed", CandidateCategory.CurtainPanel, dimensionCm: 12, material: "RC",
            panelKind: CurtainPanelKinds.GlazedText);

        Assert.False(thick.SupportsDerivation);
        Assert.False(thick.WouldChangeRating);
        Assert.Null(thick.RatingEdit());

        var glass = Row("T-glass", CandidateCategory.CurtainPanel, dimensionCm: 1, material: null,
            panelKind: CurtainPanelKinds.GlazedText);
        var table = new FireReviewTypeTable(new[] { glass });

        Assert.Empty(table.AwaitingMaterial);
        Assert.Empty(table.Derivable);
    }

    /// <summary>
    /// 「待填結構材料」是一個述詞，不是一句 <c>Derivation.Kind == MaterialMissing</c>。推定對每一列都跑，
    /// 玻璃嵌板照樣回 MaterialMissing，但它不由時效作答、面板那一格是停用的，算進去就是叫使用者去填一個
    /// 填不到的格子（決議 16、B-07）。面板狀態列、MCP 的 onlyNeedingAttention 與這張表讀的都是這個。
    /// </summary>
    [Fact]
    public void A_glazed_curtain_panel_is_not_waiting_on_a_structural_material()
    {
        var glass = Row("T-glass", CandidateCategory.CurtainPanel, dimensionCm: 1, material: null,
            panelKind: CurtainPanelKinds.GlazedText);

        Assert.Equal(FireRatingDerivationKind.MaterialMissing, glass.Derivation.Kind);
        Assert.False(glass.SupportsDerivation);
        Assert.False(glass.AwaitsMaterial);
        Assert.False(glass.AwaitsCover);

        var wall = Row("T-wall", CandidateCategory.Wall, material: null);
        Assert.True(wall.AwaitsMaterial);
        Assert.False(wall.AwaitsCover);

        var table = new FireReviewTypeTable(new[] { glass, wall });
        Assert.Equal(new[] { "T-wall" }, table.AwaitingMaterial.Select(r => r.TypeUniqueId));
    }

    /// <summary>Same reading for 防火被覆厚度: only a row that will actually be asked for one is waiting on it.</summary>
    [Fact]
    public void Only_a_row_that_is_asked_for_a_cover_waits_on_one()
    {
        var steel = Row("T-sc", CandidateCategory.Wall, material: "SC", coverCm: null);
        Assert.Equal(FireRatingDerivationKind.CoverMissing, steel.Derivation.Kind);
        Assert.True(steel.AwaitsCover);
        Assert.False(steel.AwaitsMaterial);

        var glazedSteel = Row("T-glazed-sc", CandidateCategory.CurtainPanel, material: "SC", coverCm: null,
            panelKind: CurtainPanelKinds.GlazedText);
        Assert.Equal(FireRatingDerivationKind.CoverMissing, glazedSteel.Derivation.Kind);
        Assert.False(glazedSteel.AwaitsCover);

        // 還沒宣告種類的嵌板也不算：要先知道它是構造還是防火設備，才知道它該不該填材料。
        var undeclared = Row("T-panel", CandidateCategory.CurtainPanel, material: null);
        Assert.Equal(FireRatingDerivationKind.MaterialMissing, undeclared.Derivation.Kind);
        Assert.False(undeclared.AwaitsMaterial);

        var table = new FireReviewTypeTable(new[] { steel, glazedSteel, undeclared });
        Assert.Equal(new[] { "T-sc" }, table.AwaitingCover.Select(r => r.TypeUniqueId));
        Assert.Empty(table.AwaitingMaterial);
    }

    /// <summary>
    /// An undeclared panel derives nothing either: it has not said yet whether it is 構造 or 防火設備,
    /// and the kind is the question to answer first.
    /// </summary>
    [Fact]
    public void An_undeclared_curtain_panel_derives_nothing_until_it_declares_a_kind()
    {
        var panel = Row("T-panel", CandidateCategory.CurtainPanel, dimensionCm: 10, material: "RC");

        Assert.False(panel.SupportsDerivation);
        Assert.False(panel.WouldChangeRating);
        Assert.Null(panel.RatingEdit());
    }

    /// <summary>The kind is written back as 實心／玻璃; 帷幕牆門窗 is read from the category and never written.</summary>
    [Fact]
    public void The_kind_edit_writes_the_declared_text_to_the_panel_parameter()
    {
        var panel = Row("T-panel", CandidateCategory.CurtainPanel);

        var solid = panel.PanelKindEdit(CurtainPanelKind.Solid)!;
        Assert.Equal(CurtainPanelKindParameters.Provided, solid.ParameterName);
        Assert.Equal(FireReviewEditKind.Text, solid.Kind);
        Assert.Equal(CurtainPanelKinds.SolidText, solid.Text);

        Assert.Equal(CurtainPanelKinds.GlazedText, panel.PanelKindEdit(CurtainPanelKind.Glazed)!.Text);
        Assert.Null(panel.PanelKindEdit(CurtainPanelKind.Opening));
        Assert.Null(Row("T-wall", CandidateCategory.Wall).PanelKindEdit(CurtainPanelKind.Solid));
    }

    /// <summary>
    /// 步驟 16c: the reader's proposal is not a declaration. The row still reports 未宣告 — it still
    /// waits on a kind, still answers both questions, still derives nothing — because nothing has been
    /// written to the model. Only the panel's dropdown starts out on the proposal.
    /// </summary>
    [Fact]
    public void A_proposed_kind_is_not_a_declaration()
    {
        var panel = Row("T-panel", CandidateCategory.CurtainPanel, dimensionCm: 10, material: "RC",
            proposedPanelKind: CurtainPanelKind.Glazed);

        Assert.Equal(CurtainPanelKind.Glazed, panel.ProposedPanelKind);
        Assert.Equal(CurtainPanelKind.Glazed, panel.PanelKindProposal);
        Assert.Null(panel.PanelKind);
        Assert.Null(panel.ParsedPanelKind);
        Assert.True(panel.AwaitsPanelKind);
        Assert.True(panel.CarriesRating);
        Assert.True(panel.CarriesProtection);
        Assert.False(panel.SupportsDerivation);
        Assert.False(panel.WouldChangeRating);
        Assert.Equal(FireReviewTypeParameters.None, panel.Present & FireReviewTypeParameters.PanelKind);
    }

    /// <summary>
    /// A declaration outranks the proposal: the model has spoken, and a reading of the panel's material
    /// must not put the dropdown back on something the designer already decided against.
    /// </summary>
    [Fact]
    public void A_declared_kind_leaves_no_proposal_to_offer()
    {
        var declared = Row("T-solid", CandidateCategory.CurtainPanel,
            panelKind: CurtainPanelKinds.SolidText, proposedPanelKind: CurtainPanelKind.Glazed);

        Assert.Equal(CurtainPanelKind.Solid, declared.ParsedPanelKind);
        Assert.Null(declared.PanelKindProposal);
        Assert.Equal(CurtainPanelKind.Glazed, declared.ProposedPanelKind);
    }

    /// <summary>
    /// 帷幕牆門窗 is read from the category and never written (<see cref="CurtainPanelKinds.ParameterText"/>
    /// returns null for it), so it is not a proposal either — offering it would put a value in the
    /// dropdown that no edit could ever carry.
    /// </summary>
    [Fact]
    public void An_opening_is_never_proposed_as_a_kind()
    {
        Assert.Null(Row("T-panel", CandidateCategory.CurtainPanel, proposedPanelKind: CurtainPanelKind.Opening)
            .ProposedPanelKind);
        Assert.Null(Row("T-wall", CandidateCategory.Wall, proposedPanelKind: CurtainPanelKind.Glazed)
            .PanelKindProposal);
    }

    /// <summary>A 主要構造 is untouched by 決議 16: it never waits on a kind and derives exactly as before.</summary>
    [Fact]
    public void The_members_are_unaffected_by_the_panel_kind()
    {
        foreach (var category in new[] { CandidateCategory.Wall, CandidateCategory.Column, CandidateCategory.Floor })
        {
            var row = Row("T-" + category, category);
            Assert.False(row.AwaitsPanelKind);
            Assert.True(row.SupportsDerivation);
        }

        Assert.False(Row("T-beam", CandidateCategory.StructuralFraming).SupportsDerivation);
    }
}

/// <summary>
/// C-05：面板與 MCP 都把「清除這一格」表示成空字串的文字編輯，而 Revit API 的 Parameter 只有 Set、
/// 沒有「取消設值」。這組測試釘住的是「清不掉」與「值放不進這個型別」兩件事不可以共用一句訊息。
/// </summary>
public sealed class FireReviewParameterLimitsTests
{
    /// <summary>空字串（含只剩空白）是想清除，不是打錯——使用者該拿到「清不掉」而不是「型別不接受」。</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Blank_text_on_an_integer_parameter_is_a_clear_that_cannot_be_done(string? text)
    {
        Assert.Equal(FireReviewParameterLimits.IntegerTextVerdict.NotClearable,
            FireReviewParameterLimits.ReadIntegerText(text, out var number));
        Assert.Equal(0, number);

        var reason = FireReviewParameterLimits.NotClearableReason("防火檢討_所在樓層序");
        Assert.Contains("無法清除", reason);
        Assert.Contains("不能取消設值", reason);

        // 舊訊息把原因怪到值的型別上，使用者只會一直重試同一個空白格。
        Assert.DoesNotContain("資料型別", reason);
        Assert.DoesNotContain("無法接受這個值", reason);
    }

    /// <summary>非空而不是整數（例如樓層序打「三樓」）才是這個值放不進這個型別，舊訊息對這種情形是對的。</summary>
    [Theory]
    [InlineData("三樓")]
    [InlineData("1.5")]
    [InlineData("3F")]
    public void Text_that_is_not_a_whole_number_is_a_different_problem(string text)
    {
        Assert.Equal(FireReviewParameterLimits.IntegerTextVerdict.NotANumber,
            FireReviewParameterLimits.ReadIntegerText(text, out _));

        var hint = FireReviewParameterLimits.NotANumberHint("所在樓層序", text);
        Assert.Contains("不是整數", hint);
        Assert.DoesNotContain("無法清除", hint);
    }

    [Theory]
    [InlineData("3", 3)]
    [InlineData("-2", -2)]
    [InlineData(" 11 ", 11)]
    public void A_whole_number_is_written_as_before(string text, int expected)
    {
        Assert.Equal(FireReviewParameterLimits.IntegerTextVerdict.Number,
            FireReviewParameterLimits.ReadIntegerText(text, out var number));
        Assert.Equal(expected, number);
    }

    /// <summary>面板端的措辭要說出「這一格清不掉」與可行的下一步，而不是只說失敗。</summary>
    [Fact]
    public void The_panel_hint_names_the_cell_and_says_what_to_do_instead()
    {
        var hint = FireReviewParameterLimits.CannotClearHint("自動滅火設備");

        Assert.Contains("自動滅火設備", hint);
        Assert.Contains("無法寫回未填狀態", hint);
        Assert.Contains("請填一個值", hint);
    }
}

/// <summary>
/// C-05：面板用來事先攔下「做不到的編輯」的那兩個 predicate。它們下沉到 Application 才測得到——
/// 測試專案只 reference Domain／Application／Mcp，碰不到 ViewModel、面板與寫入器。攔阻規則錯一邊
/// 都是災難：攔過頭會讓使用者改不了值而沒有任何說明，攔不住則會送出一筆註定失敗的編輯。
/// </summary>
public sealed class FireReviewCellProblemTests
{
    // --- 合法的寫入不可以被攔 ---------------------------------------------------------------------

    [Theory]
    [InlineData("是")]
    [InlineData("否")]
    public void An_answered_yes_no_cell_is_never_blocked(string typed)
    {
        // 不論模型裡原本是 是、否 還是未填，填了一個答案就是一筆正常的寫入。
        Assert.Null(FireReviewParameterLimits.YesNoCellProblem("自動滅火設備", true, typed));
        Assert.Null(FireReviewParameterLimits.YesNoCellProblem("自動滅火設備", false, typed));
        Assert.Null(FireReviewParameterLimits.YesNoCellProblem("自動滅火設備", null, typed));
    }

    [Theory]
    [InlineData("3")]
    [InlineData("-2")]
    [InlineData(" 11 ")]
    public void An_integer_cell_holding_a_whole_number_is_never_blocked(string typed)
    {
        Assert.Null(FireReviewParameterLimits.IntegerCellProblem("所在樓層序", 5, typed));
        Assert.Null(FireReviewParameterLimits.IntegerCellProblem("所在樓層序", null, typed));
    }

    /// <summary>
    /// 沒被碰過的列不可以誤報：參數本來就是未填、使用者也沒填，留白是現狀而不是一個做不到的清除。
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_cell_whose_parameter_never_held_a_value_is_not_a_problem(string? typed)
    {
        Assert.Null(FireReviewParameterLimits.YesNoCellProblem("避難層通達", null, typed));
        Assert.Null(FireReviewParameterLimits.IntegerCellProblem("地上層數", null, typed));
    }

    // --- 清不掉 -----------------------------------------------------------------------------------

    /// <summary>
    /// 把一個已有值的是否類／整數欄位清空，是 Revit 做不到的事（API 只能設值）。要拿到「清不掉」
    /// 那種理由，不是「型別不接受這個值」。
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Clearing_a_stored_value_is_blocked_as_something_that_cannot_be_cleared(string? typed)
    {
        var yesNo = FireReviewParameterLimits.YesNoCellProblem("自動滅火設備", true, typed);
        Assert.NotNull(yesNo);
        Assert.Contains("自動滅火設備", yesNo!);
        Assert.Contains("無法寫回未填狀態", yesNo);
        Assert.DoesNotContain("不是整數", yesNo);

        var integer = FireReviewParameterLimits.IntegerCellProblem("所在樓層序", 3, typed);
        Assert.NotNull(integer);
        Assert.Contains("所在樓層序", integer!);
        Assert.Contains("無法寫回未填狀態", integer);
        Assert.DoesNotContain("不是整數", integer);
    }

    /// <summary>填 否 也清不掉 是——但那是一筆正常的寫入，不是清除，所以不該被攔。</summary>
    [Fact]
    public void Answering_否_over_a_stored_是_is_a_write_and_not_a_clear()
    {
        Assert.Null(FireReviewParameterLimits.YesNoCellProblem("無法區劃分隔", true, "否"));
    }

    // --- 不是整數 ---------------------------------------------------------------------------------

    /// <summary>
    /// 整數欄位填了非數字是另一種問題，要拿到「不是整數」那種理由。面板會把打錯的字正規化成空字串，
    /// 所以這裡不攔的話，「三樓」會以一筆沒人要求的清除抵達寫入器。
    /// </summary>
    [Theory]
    [InlineData("三樓")]
    [InlineData("3F")]
    [InlineData("1.5")]
    public void An_integer_cell_holding_something_that_is_not_a_number_is_blocked_as_such(string typed)
    {
        foreach (var stored in new int?[] { 3, null })
        {
            var problem = FireReviewParameterLimits.IntegerCellProblem("所在樓層序", stored, typed);
            Assert.NotNull(problem);
            Assert.Contains("不是整數", problem!);
            Assert.Contains(typed, problem);
            Assert.DoesNotContain("無法寫回未填狀態", problem);
        }
    }
}
