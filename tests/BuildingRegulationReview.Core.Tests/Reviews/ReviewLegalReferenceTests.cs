using System.Linq;
using BuildingRegulationReview.Application.Reviews;
using BuildingRegulationReview.Domain.Rules;
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

    /// <summary>
    /// B-01: a 疑義／人工覆核 row cites no 條文, and what it says instead must read as the one sentence
    /// it is — not as a 函釋 or a 檢討重點 the splitting would invent.
    /// </summary>
    [Fact]
    public void The_rule_set_fallback_reads_as_one_line()
    {
        var reference = ReviewLegalReference.Parse(
            RuleSet.FallbackLegalReferenceOf("tw-bcr-fire", "2026.10-provisional"));

        var line = Assert.Single(reference.Lines());
        Assert.Equal("補充說明", line.Label);
        Assert.Equal("依規則集 tw-bcr-fire 2026.10-provisional 判定（本列無個別條文）", line.Value);
        Assert.DoesNotContain(reference.Lines(), l => l.Label is "函釋" or "檢討重點" or "法規" or "條文");
        Assert.Empty(reference.Clauses);
        Assert.Empty(reference.Letters);
        Assert.Empty(reference.Gists);
        Assert.False(reference.IsProvisional);
    }

    /// <summary>
    /// B-01, the other half: the rule set's own title is a design note hundreds of words long that
    /// happens to name 條文 and a 函釋. Dropped in here it must stay one 補充說明 rather than be carved
    /// into a citation it never was.
    /// </summary>
    [Fact]
    public void A_long_explanation_is_not_carved_into_a_citation()
    {
        const string title =
            "建築技術規則建築設計施工編 防火構造與防火區劃（暫定示意規則：條文、版本與生效日期待確認）。" +
            "第70條主要構造規則列於優先序 20、第79條防火區劃規則列於優先序 10；" +
            "「其他類似部分」無法逐一列舉，清單外的用字一律不豁免。挑空另有例外：" +
            "依內政部106年9月27日內授營建管字第1060814830號函，其連通區劃之合計樓地板面積回到第79條檢討" +
            "（`tw-bcr-83-area-atrium`，優先序 30）；詳見 docs/regulations/vertical-compartment.md 決議 31～33。";

        var reference = ReviewLegalReference.Parse(title);

        Assert.Empty(reference.Letters);
        Assert.Empty(reference.Gists);
        Assert.Empty(reference.Clauses);
        Assert.Null(reference.Code);
        Assert.Equal(new[] { title }, reference.Remarks.ToArray());
        Assert.Equal(new[] { "補充說明" }, reference.Lines().Select(l => l.Label).ToArray());
    }

    [Fact]
    public void Nothing_is_empty()
    {
        Assert.True(ReviewLegalReference.Parse(null).IsEmpty);
        Assert.True(ReviewLegalReference.Parse("  ").IsEmpty);
    }
}
