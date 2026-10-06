using System.Linq;
using BuildingRegulationReview.Application.Reviews;
using Xunit;

namespace BuildingRegulationReview.Core.Tests.Reviews;

/// <summary>
/// 依據條文 as a reader of the law reads it: the citation apart from what it is cited for, 暫定 said
/// in words, and the notes into the tool's own documents kept out of the citation.
/// </summary>
public sealed class ReviewLegalReferenceTests
{
    private static string Line(ReviewLegalReference reference, string label) =>
        reference.Lines().Single(l => l.Label == label).Value;

    [Fact]
    public void A_bare_clause_is_the_code_and_the_clause()
    {
        var reference = ReviewLegalReference.Parse("建築技術規則建築設計施工編第79條之3");

        Assert.Equal("建築技術規則建築設計施工編", Line(reference, "法規"));
        Assert.Equal("第79條之3", Line(reference, "條文"));
        Assert.False(reference.IsProvisional);
        Assert.Equal(new[] { "法規", "條文" }, reference.Lines().Select(l => l.Label).ToArray());
    }

    [Fact]
    public void The_bracket_is_what_the_clause_is_cited_for()
    {
        var reference = ReviewLegalReference.Parse("建築技術規則建築設計施工編第70條（主要構造之柱，依自頂層起算之層位）");

        Assert.Equal("第70條", Line(reference, "條文"));
        Assert.Equal("主要構造之柱，依自頂層起算之層位", Line(reference, "檢討重點"));
    }

    [Theory]
    [InlineData("建築技術規則建築設計施工編第79條第1項（暫定）", null)]
    [InlineData("建築技術規則建築設計施工編第79條第1項（暫定：區劃牆壁一小時以上防火時效）", "區劃牆壁一小時以上防火時效")]
    [InlineData("建築技術規則建築設計施工編第83條第1款至第4款（十一層以上部分之區劃面積，暫定）", "十一層以上部分之區劃面積")]
    public void Provisional_is_said_in_words_and_left_out_of_the_gist(string text, string? gist)
    {
        var reference = ReviewLegalReference.Parse(text);

        Assert.True(reference.IsProvisional);
        Assert.Equal(ReviewLegalReference.ProvisionalNote, Line(reference, "備註"));
        Assert.Equal(gist, reference.Lines().SingleOrDefault(l => l.Label == "檢討重點")?.Value);
        Assert.DoesNotContain(reference.Lines(), l => l.Value.Contains("暫定：") || l.Value == "暫定");
    }

    [Fact]
    public void A_letter_is_cited_on_its_own_line()
    {
        var reference = ReviewLegalReference.Parse(
            "建築技術規則建築設計施工編第83條第1款至第4款；內政部106年9月27日內授營建管字第1060814830號函第4點" +
            "（依第79條之2第3項免除區劃之挑空，所跨樓層含第十一層以上者，其連通區劃仍應依第83條區劃，暫定）");

        Assert.Equal("第83條第1款至第4款", Line(reference, "條文"));
        Assert.Equal("內政部 106年9月27日 內授營建管字第1060814830號函 第4點", Line(reference, "函釋"));
        Assert.Equal("依第79條之2第3項免除區劃之挑空，所跨樓層含第十一層以上者，其連通區劃仍應依第83條區劃",
            Line(reference, "檢討重點"));
        Assert.True(reference.IsProvisional);
    }

    [Fact]
    public void A_note_into_the_tools_own_documents_is_not_part_of_the_citation()
    {
        var reference = ReviewLegalReference.Parse(
            "建築技術規則建築設計施工編第79條之4（玻璃嵌板以認可之防火設備作答，解釋選擇見帷幕牆規格決議 16）");

        Assert.Equal("玻璃嵌板以認可之防火設備作答", Line(reference, "檢討重點"));
        Assert.Equal(new[] { "解釋選擇見帷幕牆規格決議 16" }, reference.InternalNotes.ToArray());
        Assert.DoesNotContain(reference.Lines(), l => l.Value.Contains("決議"));
    }

    [Fact]
    public void What_is_neither_clause_nor_letter_is_a_remark()
    {
        var reference = ReviewLegalReference.Parse(
            "建築技術規則建築設計施工編第79條第1項（區劃分隔）；外牆依第79條第3項、第79條之3、第79條之4檢討");

        Assert.Equal("第79條第1項", Line(reference, "條文"));
        Assert.Equal("區劃分隔", Line(reference, "檢討重點"));
        Assert.Equal("外牆依第79條第3項、第79條之3、第79條之4檢討", Line(reference, "補充說明"));
    }

    [Fact]
    public void Nothing_is_empty()
    {
        Assert.True(ReviewLegalReference.Parse(null).IsEmpty);
        Assert.True(ReviewLegalReference.Parse("  ").IsEmpty);
    }
}
