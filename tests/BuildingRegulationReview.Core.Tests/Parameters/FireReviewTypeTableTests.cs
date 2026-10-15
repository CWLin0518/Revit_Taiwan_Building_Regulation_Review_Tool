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

/// <summary>
/// C-11：清空「被覆厚度 cm」（或 MCP 送 <c>coverCm: null</c>）會靜默地寫入 0。長度參數的 Set 會成功，
/// 所以一次清除被回報成一次寫入，而 0 在判定上是一個**宣告**（第73條門檻不過 → 無防火時效），未填才是
/// 推不出來（CoverMissing、該列列為待填）。這組測試釘住三件事：空白與 0 分得開、清不掉有自己的理由、
/// 而使用者自己打的 0 永遠不可以被攔——否則「真的想宣告 0 公分」就變成做不到的事。
/// </summary>
public sealed class FireReviewLengthCellLimitsTests
{
    private const string Label = "被覆厚度 cm";

    // --- 空白／0／數字分得開 -----------------------------------------------------------------------

    /// <summary>空白（含只剩空白、null）是想清除，不是 0 也不是打錯。</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Blank_text_on_a_length_parameter_is_a_clear_that_cannot_be_done(string? text)
    {
        Assert.Equal(FireReviewParameterLimits.LengthTextVerdict.NotClearable,
            FireReviewParameterLimits.ReadCentimetresText(text, out var metres));
        // null 而不是 0：這個 out 要是在空白時回 0，日後一句 `ReadCentimetresText(t, out var m);
        // write(m);` 就把 C-11 原封不動重現一次，而編譯器不會講話。
        Assert.Null(metres);
    }

    /// <summary>
    /// 不可退讓的一條：使用者自己打 0 是「宣告被覆 0 公分」，照常寫入，與空白分得開。
    /// </summary>
    [Theory]
    [InlineData("0")]
    [InlineData(" 0 ")]
    [InlineData("0.0")]
    public void A_typed_zero_is_a_declaration_and_not_a_clear(string text)
    {
        Assert.Equal(FireReviewParameterLimits.LengthTextVerdict.Number,
            FireReviewParameterLimits.ReadCentimetresText(text, out var metres));
        // 0 而不是 null：打出來的 0 是一個宣告，與空白分得開（空白那條斷言的是 Assert.Null）。
        Assert.Equal(0d, metres);
    }

    [Theory]
    [InlineData("4", 0.04)]
    [InlineData(" 4.5 ", 0.045)]
    [InlineData("0.5", 0.005)]
    public void Centimetres_are_read_as_metres(string text, double expectedMetres)
    {
        Assert.Equal(FireReviewParameterLimits.LengthTextVerdict.Number,
            FireReviewParameterLimits.ReadCentimetresText(text, out var metres));
        Assert.Equal(expectedMetres, metres!.Value, 9);
    }

    /// <summary>非空而讀不成有效的公分數（含負數與非有限值）是另一種問題。</summary>
    [Theory]
    [InlineData("四公分")]
    [InlineData("4cm")]
    [InlineData("-1")]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    public void Text_that_is_not_a_length_is_a_different_problem(string text)
    {
        Assert.Equal(FireReviewParameterLimits.LengthTextVerdict.NotANumber,
            FireReviewParameterLimits.ReadCentimetresText(text, out _));
    }

    // --- 合法的寫入不可以被攔 -----------------------------------------------------------------------

    /// <summary>0 無論模型裡原本有沒有值都是一筆正常的寫入，絕不可以被攔。</summary>
    [Fact]
    public void A_length_cell_holding_zero_is_never_blocked()
    {
        Assert.Null(FireReviewParameterLimits.LengthCellProblem(Label, 0.04, "0"));
        Assert.Null(FireReviewParameterLimits.LengthCellProblem(Label, null, "0"));
    }

    [Theory]
    [InlineData("4")]
    [InlineData(" 4.5 ")]
    public void A_length_cell_holding_a_number_is_never_blocked(string typed)
    {
        Assert.Null(FireReviewParameterLimits.LengthCellProblem(Label, 0.04, typed));
        Assert.Null(FireReviewParameterLimits.LengthCellProblem(Label, null, typed));
    }

    /// <summary>沒被碰過的列不可以誤報：參數本來就是未填、使用者也沒填，留白是現狀而不是一個清除。</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_length_cell_whose_parameter_never_held_a_value_is_not_a_problem(string? typed)
    {
        Assert.Null(FireReviewParameterLimits.LengthCellProblem(Label, null, typed));
    }

    // --- 清不掉 -------------------------------------------------------------------------------------

    /// <summary>
    /// 把一個已有值的長度欄位清空，要拿到「清不掉」那種理由，而且那句話必須說出「寫 0 等於宣告 0 公分」
    /// ——不講的話使用者只會把 0 打回去，正好做出那個他不想做的宣告。
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Clearing_a_stored_length_is_blocked_as_something_that_cannot_be_cleared(string? typed)
    {
        var problem = FireReviewParameterLimits.LengthCellProblem(Label, 0.04, typed);

        Assert.NotNull(problem);
        Assert.Contains(Label, problem!);
        Assert.Contains("無法寫回未填狀態", problem);
        Assert.Contains("寫入 0 是宣告 0 公分", problem);
        Assert.DoesNotContain("不是有效的公分數值", problem);
    }

    // --- 不是公分數 ---------------------------------------------------------------------------------

    [Theory]
    [InlineData("四公分")]
    [InlineData("-1")]
    public void A_length_cell_holding_something_that_is_not_a_length_is_blocked_as_such(string typed)
    {
        foreach (var stored in new double?[] { 0.04, null })
        {
            var problem = FireReviewParameterLimits.LengthCellProblem(Label, stored, typed);
            Assert.NotNull(problem);
            Assert.Contains("不是有效的公分數值", problem!);
            Assert.Contains(typed, problem);
            Assert.DoesNotContain("無法寫回未填狀態", problem);
        }
    }

    // --- 寫入器那一句話 -----------------------------------------------------------------------------

    /// <summary>
    /// 長度與整數清不掉的原因不同：整數是 API 會失敗，長度是 Set(0.0) 會成功而 0 是一個宣告。兩者
    /// 共用一句話就會把「寫 0 的陷阱」藏起來。
    /// </summary>
    [Fact]
    public void The_length_reason_names_the_zero_trap_and_is_not_the_integer_sentence()
    {
        var length = FireReviewParameterLimits.NotClearableReason(
            StructuralMaterialParameters.Cover, FireReviewEditKind.Length);

        Assert.Contains(StructuralMaterialParameters.Cover, length);
        Assert.Contains("宣告 0 公分", length);
        Assert.DoesNotContain("整數或是否類參數", length);
        Assert.NotEqual(FireReviewParameterLimits.NotClearableReason(StructuralMaterialParameters.Cover), length);
    }

    /// <summary>整數／是否類與文字仍舊走原本那句——C-05 的措辭不可以被這次的修改動到。</summary>
    [Theory]
    [InlineData(FireReviewEditKind.Text)]
    [InlineData(FireReviewEditKind.YesNo)]
    public void Every_other_kind_keeps_the_sentence_C_05_left(FireReviewEditKind kind)
    {
        Assert.Equal(
            FireReviewParameterLimits.NotClearableReason("防火檢討_所在樓層序"),
            FireReviewParameterLimits.NotClearableReason("防火檢討_所在樓層序", kind));
    }

    /// <summary>無值的長度編輯在 MCP 的 edits[].value 上要看得出來寫不進去，而不是印成「…＝空白 m」。</summary>
    [Fact]
    public void A_length_edit_with_no_value_says_so_instead_of_printing_an_empty_number()
    {
        var text = FireReviewParameterEdit
            .OfLength("T-1", StructuralMaterialParameters.Cover, null)
            .ToString();

        Assert.Contains("無值", text);
        Assert.DoesNotContain("=  m", text);

        Assert.Equal($"{StructuralMaterialParameters.Cover} = 0.04 m",
            FireReviewParameterEdit.OfLength("T-1", StructuralMaterialParameters.Cover, 0.04).ToString());
    }

    // --- C-20 甲：宣告 0 公分的那一刻要被告知往返限制 ------------------------------------------------

    /// <summary>
    /// 「這一筆編輯在宣告被覆 0 公分」是一個述詞，不是一句文案：面板與 MCP 都問它，所以它必須在
    /// Application 層而且測得到。空白不在它的範圍內——空白是 NotClearable，根本不會變成一筆編輯
    /// （C-11）；別的長度參數也不在，那一句話講的後果（第71～73條的推定、該列回到「待填被覆」）
    /// 只對被覆成立。
    /// </summary>
    [Fact]
    public void A_cover_edit_of_zero_is_recognised_as_a_declaration_of_zero_centimetres()
    {
        Assert.True(FireReviewParameterLimits.DeclaresZeroCover(
            FireReviewParameterEdit.OfLength("T-1", StructuralMaterialParameters.Cover, 0)));

        // 有厚度的被覆不是宣告 0；無值的長度編輯寫不進去，也不是宣告 0。
        Assert.False(FireReviewParameterLimits.DeclaresZeroCover(
            FireReviewParameterEdit.OfLength("T-1", StructuralMaterialParameters.Cover, 0.04)));
        Assert.False(FireReviewParameterLimits.DeclaresZeroCover(
            FireReviewParameterEdit.OfLength("T-1", StructuralMaterialParameters.Cover, null)));

        // 別的長度參數寫 0 不套用被覆的句子：它沒有「待填被覆」那個後果，該為它寫自己的述詞。
        Assert.False(FireReviewParameterLimits.DeclaresZeroCover(
            FireReviewParameterEdit.OfLength("T-1", "測試_另一個長度", 0)));

        // 別的種類不是長度：文字的「0」與沒勾的是否欄位（寫成 0）都不是宣告被覆 0 公分。
        Assert.False(FireReviewParameterLimits.DeclaresZeroCover(
            FireReviewParameterEdit.OfText("T-1", StructuralMaterialParameters.Material, "0")));
        Assert.False(FireReviewParameterLimits.DeclaresZeroCover(
            FireReviewParameterEdit.OfYesNo("T-1", FireProtectionParameters.Provided, false)));
        Assert.False(FireReviewParameterLimits.DeclaresZeroCover(null));
    }

    /// <summary>
    /// 那句話要先說「值確實寫進去了」。使用者看到格子變空白，最容易得到的結論是「寫入失敗」，於是把
    /// 同一個 0 再打一次——C-20 實機看到的就是這個循環。所以這一條同時釘住「說了寫得進去」與
    /// 「沒有說寫入失敗」。
    /// </summary>
    [Fact]
    public void The_zero_cover_notice_says_the_value_was_written_and_why_the_cell_still_goes_blank()
    {
        var notice = FireReviewParameterLimits.ZeroCoverRoundTripNotice;

        Assert.Contains(StructuralMaterialParameters.Cover, notice);
        Assert.Contains("宣告被覆 0 公分", notice);
        Assert.Contains("無防火時效", notice);
        Assert.Contains("值確實會寫進模型", notice);
        Assert.Contains("讀不回 0", notice);
        Assert.Contains("顯示為空白", notice);
        Assert.Contains("待填被覆", notice);
        Assert.Contains("值沒有不見", notice);

        // 不可以讀成一次失敗，也不可以讀成「這個值被攔下來了」——它沒有被攔，它寫進去了。
        Assert.DoesNotContain("寫入失敗", notice);
        Assert.DoesNotContain("不會寫入", notice);
    }

    /// <summary>
    /// Code review M-3：同一句話在「沒有真的落地」時是假話。MCP 的 dryRun（內層 commit、外層
    /// TransactionGroup 回滾）與「寫入失敗整批復原」兩條路，模型裡什麼都沒有，卻會讀到
    /// 「值確實會寫進模型…值沒有不見」；自動化呼叫端因此以為宣告完成，不再真的寫一次。
    /// </summary>
    [Fact]
    public void The_unwritten_zero_cover_notice_never_claims_the_value_is_in_the_model()
    {
        var notice = FireReviewParameterLimits.UnwrittenZeroCoverRoundTripNotice;

        Assert.Contains(StructuralMaterialParameters.Cover, notice);
        Assert.Contains("宣告被覆 0 公分", notice);
        Assert.Contains("並未實際寫入模型", notice);
        Assert.Contains("試跑", notice);
        Assert.Contains("整批復原", notice);
        Assert.Contains("模型裡還沒有這個值", notice);

        // 往返限制本身仍要講，否則呼叫端只知道「這次沒寫」，不知道寫了也讀不回來。
        Assert.Contains("讀不回 0", notice);
        Assert.Contains("待填被覆", notice);

        // 落地那一句的兩個關鍵說法在這裡都是假話。
        Assert.DoesNotContain("值確實會寫進模型", notice);
        Assert.DoesNotContain("值沒有不見", notice);
        Assert.NotEqual(FireReviewParameterLimits.ZeroCoverRoundTripNotice, notice);
    }

    /// <summary>零噪音：沒有人宣告被覆 0 時，面板的確認視窗與 MCP 的回應都不該多出這一段。</summary>
    [Fact]
    public void A_batch_that_declares_no_zero_cover_gets_no_notice_at_all()
    {
        foreach (var written in new[] { true, false })
        {
            Assert.Null(FireReviewParameterLimits.ZeroCoverNotice(null, written));
            Assert.Null(FireReviewParameterLimits.ZeroCoverNotice(Array.Empty<FireReviewParameterEdit>(), written));
            Assert.Null(FireReviewParameterLimits.ZeroCoverNotice(new[]
            {
                FireReviewParameterEdit.OfLength("T-1", StructuralMaterialParameters.Cover, 0.04),
                FireReviewParameterEdit.OfLength("T-2", StructuralMaterialParameters.Cover, null),
                FireReviewParameterEdit.OfLength("T-3", "測試_另一個長度", 0),
                FireReviewParameterEdit.OfText("T-1", StructuralMaterialParameters.Material, "SC")
            }, written));
        }
    }

    /// <summary>
    /// 一筆宣告被覆 0 就要有那一句話，而且是**跟著落地與否**的那一句（code review M-3）。面板的確認
    /// 視窗是「將寫入…要繼續嗎？」，按下去就會寫，所以用預設的 true。
    /// </summary>
    [Fact]
    public void A_batch_that_declares_zero_cover_gets_the_notice_that_matches_whether_it_landed()
    {
        var batch = new[]
        {
            FireReviewParameterEdit.OfText("T-1", StructuralMaterialParameters.Material, "SC"),
            FireReviewParameterEdit.OfLength("T-1", StructuralMaterialParameters.Cover, 0)
        };

        Assert.Equal(FireReviewParameterLimits.ZeroCoverRoundTripNotice,
            FireReviewParameterLimits.ZeroCoverNotice(batch, written: true));
        Assert.Equal(FireReviewParameterLimits.UnwrittenZeroCoverRoundTripNotice,
            FireReviewParameterLimits.ZeroCoverNotice(batch, written: false));

        // 預設是落地那一句：面板的呼叫端不傳這個旗標。
        Assert.Equal(FireReviewParameterLimits.ZeroCoverRoundTripNotice,
            FireReviewParameterLimits.ZeroCoverNotice(batch));

        // 同一個參數在兩個類型上各宣告一次，仍然只是那一句話。
        Assert.Equal(FireReviewParameterLimits.ZeroCoverRoundTripNotice,
            FireReviewParameterLimits.ZeroCoverNotice(new[]
            {
                FireReviewParameterEdit.OfLength("T-1", StructuralMaterialParameters.Cover, 0),
                FireReviewParameterEdit.OfLength("T-2", StructuralMaterialParameters.Cover, 0)
            }));
    }

    // --- 為什麼這件事重要：0 與未填是兩個判定 -------------------------------------------------------

    /// <summary>
    /// 被覆 0 公分是一個**有效的推定**（第73條門檻不過 → 無防火時效），未填才是推不出來、該列列為待填。
    /// 寫入層把空白塌成 0，就是把使用者沒做過的宣告寫進模型；這一條把兩者的差別釘在判定端。
    /// </summary>
    [Fact]
    public void Zero_cover_is_an_answer_while_a_missing_cover_is_a_gap()
    {
        var zero = SteelWall(coverCm: 0);
        var missing = SteelWall(coverCm: null);

        Assert.Equal(FireRatingDerivationKind.NotRated, zero.Derivation.Kind);
        Assert.Contains("無防火時效", zero.Derivation.Explanation);
        Assert.False(zero.AwaitsCover);

        Assert.Equal(FireRatingDerivationKind.CoverMissing, missing.Derivation.Kind);
        Assert.True(missing.AwaitsCover);
    }

    private static FireReviewTypeRow SteelWall(double? coverCm) =>
        new("T-SC", CandidateCategory.Wall, "SC20", familyName: null, instanceCount: 1, projectInstanceCount: 1,
            dimensionMeters: 0.20, material: "SC",
            coverMeters: coverCm is double c ? c / 100 : (double?)null,
            present: FireReviewTypeParameters.Rating | FireReviewTypeParameters.Material | FireReviewTypeParameters.Cover);
}
