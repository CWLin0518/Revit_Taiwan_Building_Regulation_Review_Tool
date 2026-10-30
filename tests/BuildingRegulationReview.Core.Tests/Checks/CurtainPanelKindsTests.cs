using BuildingRegulationReview.Application.Checks;
using Xunit;

namespace BuildingRegulationReview.Core.Tests.Checks;

/// <summary>
/// 決議 16、步驟 16c: the two decisions the Revit reading layer makes about a panel's kind, kept here
/// rather than in the adapter so they are covered without Revit. The adapter's whole job is to hand
/// these three facts over — category, whether the panel is a wall, what the Type parameter says — and
/// to turn the proposal into a dropdown value.
/// </summary>
public sealed class CurtainPanelKindsTests
{
    // --- 分類（讀取層怎麼認一片嵌板）-------------------------------------------------------------

    /// <summary>
    /// A 帷幕牆門窗 is 防火設備 by its category. Its kind is never declared and never written, so a
    /// declaration on its Type — however it got there — must not turn it into 構造.
    /// </summary>
    [Fact]
    public void An_opening_is_an_opening_whatever_its_type_parameter_says()
    {
        Assert.Equal(CurtainPanelKind.Opening, CurtainPanelKinds.Classify(isOpening: true, isPanelAsWall: false, declaredText: null));
        Assert.Equal(CurtainPanelKind.Opening, CurtainPanelKinds.Classify(true, false, CurtainPanelKinds.SolidText));
        Assert.Equal(CurtainPanelKind.Opening, CurtainPanelKinds.Classify(true, true, CurtainPanelKinds.GlazedText));
    }

    /// <summary>
    /// 嵌板為牆 is 構造 and needs no declaration (see <see cref="CurtainPanelKind.Solid"/>): it is a
    /// <c>Wall</c> with a thickness and a 設計防火時效 of its own, which is exactly what 實心 means.
    /// </summary>
    [Fact]
    public void A_panel_that_is_a_wall_is_solid_without_being_declared()
    {
        Assert.Equal(CurtainPanelKind.Solid, CurtainPanelKinds.Classify(isOpening: false, isPanelAsWall: true, declaredText: null));
        Assert.Equal(CurtainPanelKind.Solid, CurtainPanelKinds.Classify(false, true, "   "));
        Assert.Equal(CurtainPanelKind.Solid, CurtainPanelKinds.Classify(false, true, "說不上來的字"));
    }

    /// <summary>
    /// 防火檢討_嵌板種類 is only bound to Curtain Panels, so a 牆型別 reads null in practice — but a
    /// declaration that is actually there outranks the inference, because it is the designer speaking.
    /// </summary>
    [Fact]
    public void A_declaration_outranks_the_inference_from_being_a_wall()
    {
        Assert.Equal(CurtainPanelKind.Glazed, CurtainPanelKinds.Classify(false, true, CurtainPanelKinds.GlazedText));
        Assert.Equal(CurtainPanelKind.Solid, CurtainPanelKinds.Classify(false, true, CurtainPanelKinds.SolidText));
    }

    /// <summary>
    /// The one case 決議 16 exists for: an ordinary panel that has declared nothing is 未宣告, not a
    /// kind the tool picked. CW-O then answers 資料不足 and the panel lists the row under 待宣告.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("半實心")]
    public void An_undeclared_panel_is_neither_kind(string? declared)
    {
        Assert.Null(CurtainPanelKinds.Classify(isOpening: false, isPanelAsWall: false, declaredText: declared));
    }

    /// <summary>Every spelling <see cref="CurtainPanelKinds.Parse"/> knows reaches the classifier too.</summary>
    [Theory]
    [InlineData("實心", CurtainPanelKind.Solid)]
    [InlineData("實心嵌板", CurtainPanelKind.Solid)]
    [InlineData("Solid", CurtainPanelKind.Solid)]
    [InlineData("玻璃", CurtainPanelKind.Glazed)]
    [InlineData("玻璃帷幕", CurtainPanelKind.Glazed)]
    [InlineData("Glass", CurtainPanelKind.Glazed)]
    public void A_declared_panel_answers_with_the_kind_it_declared(string declared, CurtainPanelKind expected)
    {
        Assert.Equal(expected, CurtainPanelKinds.Classify(false, false, declared));
    }

    // --- 提案（由材料替使用者填下拉的初值）--------------------------------------------------------

    /// <summary>Revit's own material classification is the strongest signal there is.</summary>
    [Theory]
    [InlineData("Glass")]
    [InlineData("glass")]
    [InlineData(" Glass ")]
    public void A_glass_material_class_proposes_glazed(string materialClass)
    {
        Assert.Equal(CurtainPanelKind.Glazed, CurtainPanelKinds.ProposeFrom(materialClass, materialName: null));
    }

    /// <summary>
    /// A material or Type named for glass proposes 玻璃 as well. The Type name is the weakest of the
    /// three and is only there because a system panel often carries no material at all while its Type
    /// is called 玻璃 1.0cm — which is the row that started 決議 16.
    /// </summary>
    [Theory]
    [InlineData(null, "強化玻璃", null)]
    [InlineData(null, "Low-E Glazing", null)]
    [InlineData("Generic", null, "玻璃 1.0cm")]
    [InlineData("Generic", "鋁", "玻璃 1.0cm")]
    public void A_name_that_says_glass_proposes_glazed(string? materialClass, string? materialName, string? typeName)
    {
        Assert.Equal(CurtainPanelKind.Glazed, CurtainPanelKinds.ProposeFrom(materialClass, materialName, typeName));
    }

    /// <summary>
    /// Nothing proposes 實心. 「不是玻璃」推不出「是實心」——宣告實心等於說這片嵌板的時效由厚度推定，
    /// 那是法規上的分類，不是工具可以替設計者決定的事（決議 16）。
    /// </summary>
    [Theory]
    [InlineData(null, null, null)]
    [InlineData("", "", "")]
    [InlineData("Metal", "鋁複合板", "鋁板 3mm")]
    [InlineData("Concrete", "預鑄混凝土", "PC 版 12cm")]
    [InlineData("Stone", "花崗石", "石材乾式")]
    public void Nothing_ever_proposes_solid(string? materialClass, string? materialName, string? typeName)
    {
        Assert.Null(CurtainPanelKinds.ProposeFrom(materialClass, materialName, typeName));
    }

    /// <summary>
    /// The proposal is one of the two the user may declare, never <see cref="CurtainPanelKind.Opening"/>:
    /// that one has no <see cref="CurtainPanelKinds.ParameterText"/>, so no edit could carry it.
    /// </summary>
    [Fact]
    public void A_proposal_is_always_a_kind_the_user_could_have_declared()
    {
        var proposed = CurtainPanelKinds.ProposeFrom("Glass", null);

        Assert.NotNull(proposed);
        Assert.Contains(proposed!.Value, CurtainPanelKinds.Declarable);
        Assert.NotNull(CurtainPanelKinds.ParameterText(proposed.Value));
    }
}
