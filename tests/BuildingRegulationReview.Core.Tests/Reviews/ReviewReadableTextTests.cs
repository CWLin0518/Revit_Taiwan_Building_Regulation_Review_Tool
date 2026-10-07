using BuildingRegulationReview.Application.Reviews;
using BuildingRegulationReview.Domain.Reviews;
using BuildingRegulationReview.Domain.Rules;
using Xunit;

namespace BuildingRegulationReview.Core.Tests.Reviews;

/// <summary>
/// 檢討面板可讀性：the panel names an element by the id 依 ID 選取 takes, writes a value in the units of
/// the 條文, and puts a Chinese name to every evidence field — the UniqueIds, enum names and rule
/// field names the checks store are never what the user is asked to read.
/// </summary>
public sealed class ReviewReadableTextTests
{
    /// <summary>Element 282773: the tail of a UniqueId is the element id in hexadecimal (0x45095).</summary>
    private const string CurtainWall = "0a1b2c3d-4e5f-6789-abcd-ef0123456789-00045095";

    /// <summary>Element 282774.</summary>
    private const string Panel = "0a1b2c3d-4e5f-6789-abcd-ef0123456789-00045096";

    [Fact]
    public void A_UniqueId_reads_as_the_element_id_Revit_shows()
    {
        Assert.Equal("282773", ReviewElementReference.ElementIdOf(CurtainWall));
        Assert.Equal("282773", ReviewElementReference.Describe(CurtainWall));
    }

    [Fact]
    public void A_short_tail_is_still_an_element_id()
    {
        Assert.Equal("282773", ReviewElementReference.ElementIdOf("0a1b2c3d-4e5f-6789-abcd-ef0123456789-45095"));
    }

    /// <summary>A zone id is a plain GUID, not a UniqueId: it must never be read as an element.</summary>
    [Fact]
    public void A_plain_guid_is_not_an_element_id()
    {
        const string zone = "0a1b2c3d-4e5f-6789-abcd-ef0123456789";
        Assert.Null(ReviewElementReference.ElementIdOf(zone));
        Assert.Equal(zone, ReviewElementReference.Describe(zone));
        Assert.Equal(zone, ReviewElementReference.InText(zone));
    }

    [Fact]
    public void A_list_of_ids_is_cut_short_but_still_says_how_many_there_are()
    {
        var many = new string[ReviewElementReference.ListLimit + 5];
        for (var i = 0; i < many.Length; i++) many[i] = $"0a1b2c3d-4e5f-6789-abcd-ef0123456789-{i + 1:x8}";

        var text = ReviewElementReference.DescribeMany(many);
        Assert.StartsWith("1、2、3", text);
        Assert.Contains($"共 {many.Length} 個", text);
        Assert.DoesNotContain("0a1b2c3d", text);
    }

    [Fact]
    public void No_ids_reads_as_a_dash() => Assert.Equal("—", ReviewElementReference.DescribeMany(null));

    [Fact]
    public void A_comma_separated_list_reads_as_element_ids() =>
        Assert.Equal("282773、282774", ReviewElementReference.DescribeList(CurtainWall + "," + Panel));

    [Fact]
    public void A_UniqueId_inside_a_sentence_is_replaced_by_its_element_id() =>
        Assert.Equal("區劃牆（Id 282773）不是直線牆",
            ReviewElementReference.InText($"區劃牆（Id {CurtainWall}）不是直線牆"));

    [Fact]
    public void A_quantity_reads_in_the_unit_of_the_clause()
    {
        Assert.Equal("1,500 ㎡", ReviewValueText.Format(ReviewValue.Quantity(1500, ReviewUnit.SquareMeter)));
        Assert.Equal("120 分鐘", ReviewValueText.Format(ReviewValue.Quantity(120, ReviewUnit.Minute)));
        Assert.Equal("0.9 m", ReviewValueText.Format(ReviewValue.Quantity(0.1 + 0.2 + 0.6, ReviewUnit.Meter)));
        Assert.Equal("7 個", ReviewValueText.Format(ReviewValue.Quantity(7, ReviewUnit.Count)));
        Assert.Equal("9", ReviewValueText.Format(ReviewValue.Quantity(9, ReviewUnit.None)));
    }

    /// <summary>
    /// Four decimals, so a length that misses the 900 mm 但書 by a millimetre still reads as a miss
    /// rather than being rounded up into a pass.
    /// </summary>
    [Fact]
    public void A_length_keeps_enough_decimals_to_show_a_near_miss() =>
        Assert.Equal("0.8996 m", ReviewValueText.Format(ReviewValue.Quantity(0.8996, ReviewUnit.Meter)));

    [Fact]
    public void A_flag_and_an_absent_value_read_as_words()
    {
        Assert.Equal("是", ReviewValueText.Format(ReviewValue.OfBoolean(true)));
        Assert.Equal("否", ReviewValueText.Format(ReviewValue.OfBoolean(false)));
        Assert.Equal("—", ReviewValueText.Format(null));
        Assert.Equal("（空白）", ReviewValueText.Format(ReviewValue.OfText(string.Empty)));
    }

    [Fact]
    public void An_evidence_field_the_checks_record_has_a_Chinese_name()
    {
        Assert.Equal("交接帶內嵌板數", ReviewFieldText.Label("junction.panelCount"));
        Assert.Equal("讀取的參數", ReviewFieldText.Label("provided.parameter"));
        Assert.Equal("Revit 標示面積", ReviewFieldText.Label("area.revit"));
    }

    /// <summary>
    /// B-06: 相關元素 shows ElementIds, so no label in that section may promise a UniqueId. The rule
    /// authors' own names still say UniqueId — they are read in <c>RuleFieldCatalog</c>, not here.
    /// </summary>
    [Theory]
    [InlineData("opening.hostUniqueId", "Host 牆")]
    [InlineData("junction.curtainWallUniqueId", "帷幕牆")]
    [InlineData("junction.hostUniqueId", "區劃牆或區劃樓地板")]
    [InlineData("shaft.elementUniqueId", "受檢防火設備（門窗或嵌板）")]
    public void An_element_field_is_labelled_without_the_words_UniqueId(string field, string label)
    {
        Assert.Equal(label, ReviewFieldText.Label(field));
        Assert.DoesNotContain("UniqueId", ReviewFieldText.Label(field));
        Assert.Contains("UniqueId", RuleFieldCatalog.Default.Find(field)!.Description);
    }

    /// <summary>A rule field describes itself in the catalog, so the panel never repeats the wording.</summary>
    [Fact]
    public void A_rule_field_is_named_by_the_catalog()
    {
        Assert.Equal("設計／認證防火時效", ReviewFieldText.Label("element.providedFireRating"));
        Assert.Equal("區劃面積", ReviewFieldText.Label("zone.area"));
    }

    [Fact]
    public void A_source_field_says_which_field_it_answers_for() =>
        Assert.Equal("區劃用途（來源）", ReviewFieldText.Label("source[zone.use]"));

    [Fact]
    public void An_Area_part_names_the_Area_by_its_element_id() =>
        Assert.Equal("Area 282773 的Revit 標示面積", ReviewFieldText.Label($"area.part[{CurtainWall}].revit"));

    /// <summary>A field nobody knows stays visible, unlabelled rather than hidden.</summary>
    [Fact]
    public void An_unknown_field_is_shown_as_it_is()
    {
        Assert.Equal("future.thing", ReviewFieldText.Label("future.thing"));
        Assert.False(ReviewFieldText.IsKnown("future.thing"));
    }

    [Fact]
    public void An_enum_name_stored_as_evidence_reads_as_words()
    {
        Assert.Equal("參數未填寫", ReviewFieldText.Value("provided.kind", ReviewValue.OfText("Missing")));
        Assert.Equal("牆", ReviewFieldText.Value("source.category", ReviewValue.OfText("Walls")));
        Assert.Equal("區劃牆與帷幕牆交接（水平）", ReviewFieldText.Value("junction.kind", ReviewValue.OfText("WallToCurtainWall")));
        Assert.Equal("管道間維修門之防火時效", ReviewFieldText.Value("shaft.requirement", ReviewValue.OfText("ShaftDoorRating")));
    }

    [Fact]
    public void A_flags_enum_reads_as_a_list_of_what_is_missing() =>
        Assert.Equal("無法區劃分隔（防火檢討_無法區劃分隔）、建築物使用類組",
            ReviewFieldText.Value("article79_1.gaps", ReviewValue.OfText("CannotBeSubdivided, BuildingUse")));

    [Fact]
    public void An_evidence_list_of_elements_reads_as_element_ids() =>
        Assert.Equal("282773、282774", ReviewFieldText.Value("junction.panels", ReviewValue.OfText(CurtainWall + "," + Panel)));

    /// <summary>The six numbers of <c>junction.placement</c> are where the junction is, not a CSV row.</summary>
    [Fact]
    public void A_junction_placement_reads_as_a_position()
    {
        Assert.Equal("交點 (12.5, 4.79)，高程 28～31.5 m",
            ReviewFieldText.Value("junction.placement", ReviewValue.OfText("12.5,4.79,12.5,4.79,28,31.5")));
        Assert.Equal("平面 (12.5, 4.79)～(18.5, 4.79)，高程 28～31.5 m",
            ReviewFieldText.Value("junction.placement", ReviewValue.OfText("12.5,4.79,18.5,4.79,28,31.5")));
    }

    /// <summary>A gap list is written in field names; the panel answers in the words of the 條文.</summary>
    [Fact]
    public void A_rule_gap_names_the_field_in_Chinese() =>
        Assert.Equal("設計／認證防火時效 未設定、區劃用途 未設定",
            ReviewFieldText.Value("rule.gaps", ReviewValue.OfText("element.providedFireRating 未設定、zone.use 未設定")));

    [Fact]
    public void Humanize_leaves_a_name_nobody_knows_alone() =>
        Assert.Equal("讀不到 future.thing", ReviewFieldText.Humanize("讀不到 future.thing"));
}
