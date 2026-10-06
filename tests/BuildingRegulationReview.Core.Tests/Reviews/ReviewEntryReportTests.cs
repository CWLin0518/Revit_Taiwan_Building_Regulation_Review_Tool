using System;
using System.Collections.Generic;
using System.Linq;
using BuildingRegulationReview.Application.Checks;
using BuildingRegulationReview.Application.Reviews;
using BuildingRegulationReview.Domain.Reviews;
using Xunit;

namespace BuildingRegulationReview.Core.Tests.Reviews;

/// <summary>
/// 檢討面板的明細：what one row of the 檢討表 says when it is opened. The order is the order a reviewer
/// asks in — what is being reviewed, what the answer was and why, then the rule — and nothing in it
/// is left in the words the code stores it in.
/// </summary>
public sealed class ReviewEntryReportTests
{
    private static readonly Guid RunId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid PackageId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid ZoneId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly DateTime Started = new DateTime(2026, 9, 28, 1, 0, 0, DateTimeKind.Utc);

    /// <summary>Element 282773.</summary>
    private const string Wall = "0a1b2c3d-4e5f-6789-abcd-ef0123456789-00045095";

    /// <summary>Element 282774.</summary>
    private const string Panel = "0a1b2c3d-4e5f-6789-abcd-ef0123456789-00045096";

    private static ReviewTable Table(ReviewResult result) =>
        ReviewTable.Build(new ReviewRun(RunId, PackageId, "tw-bcr", "1.0.0", 1, Started)
            .Complete(new[] { result }, Started.AddMinutes(2)));

    private static ReviewResult Failing() =>
        new(Guid.Parse("44444444-4444-4444-4444-444444444444"), RunId, PackageId, ReviewCheckTypes.FireResistance,
            new[] { Wall }, ZoneId.ToString("D"), ReviewStatus.Fail,
            ReviewValue.Quantity(30, ReviewUnit.Minute), ReviewValue.Quantity(60, ReviewUnit.Minute),
            "tw-bcr-79-wall-rating", "1", "建築技術規則建築設計施工編第79條",
            $"element.providedFireRating 不足：{Wall} 只有 30 分鐘",
            new ReviewEvidence(new[]
            {
                new ReviewEvidenceItem("source.category", ReviewValue.OfText("Walls")),
                new ReviewEvidenceItem("source.typeName", ReviewValue.OfText("RC 牆 12cm")),
                new ReviewEvidenceItem("zone.name", ReviewValue.OfText("防火區劃 A")),
                new ReviewEvidenceItem("provided.kind", ReviewValue.OfText("Rated")),
                new ReviewEvidenceItem("provided.parameter", ReviewValue.OfText(FireRatingParameters.Provided)),
                new ReviewEvidenceItem("element.providedFireRating", ReviewValue.Quantity(30, ReviewUnit.Minute)),
                new ReviewEvidenceItem("source.typeUniqueId", ReviewValue.OfText(Panel)),
                new ReviewEvidenceItem("option.samplingInterval", ReviewValue.Quantity(0.05, ReviewUnit.Meter))
            }));

    private static IReadOnlyList<ReviewDetailSection> Report(out ReviewTable table)
    {
        var result = Failing();
        table = Table(result);
        return ReviewEntryReport.Describe(table, table.Entry(result.ResultId)!);
    }

    private static string Text() => ReviewEntryReport.ToText(Report(out _));

    private static ReviewDetailSection Section(string title) =>
        Report(out _).Single(s => s.Title == title);

    private static string Line(string sectionTitle, string label) =>
        Section(sectionTitle).Lines.Single(l => l.Label == label).Value;

    [Fact]
    public void The_sections_read_in_the_order_a_reviewer_asks_in()
    {
        Assert.Equal(
            new[] { "檢討對象", "檢討結果", "法規依據", "判定依據", "相關元素（Revit 元素編號）", "量測設定", "規則來源" },
            Report(out _).Select(s => s.Title).ToArray());
    }

    [Fact]
    public void The_subject_is_named_by_its_element_id()
    {
        Assert.Equal("構件防火時效", Line("檢討對象", "檢討項目"));
        Assert.Equal("防火區劃 A", Line("檢討對象", "所屬區劃"));
        Assert.Equal("牆", Line("檢討對象", "元素類別"));
        Assert.Equal("RC 牆 12cm", Line("檢討對象", "元素類型"));
        Assert.Equal("282773", Line("檢討對象", "Revit 元素編號"));
    }

    [Fact]
    public void The_verdict_says_what_was_measured_against_what()
    {
        Assert.Equal("未符合", Line("檢討結果", "狀態"));
        Assert.Equal("30 分鐘", Line("檢討結果", "模型實際值"));
        Assert.Equal("60 分鐘", Line("檢討結果", "法規要求值"));
    }

    /// <summary>The citation is its own section, the 法規 named once and the 條文 on a line of its own.</summary>
    [Fact]
    public void The_clause_reads_as_a_citation()
    {
        Assert.Equal("建築技術規則建築設計施工編", Line("法規依據", "法規"));
        Assert.Equal("第79條", Line("法規依據", "條文"));
    }

    /// <summary>The 原因 is the sentence the user reads first, so it holds neither a field name nor a UniqueId.</summary>
    [Fact]
    public void The_reason_is_written_in_the_words_of_the_clause() =>
        Assert.Equal("設計／認證防火時效 不足：282773 只有 30 分鐘", Line("檢討結果", "原因說明"));

    [Fact]
    public void The_evidence_is_labelled_in_Chinese()
    {
        Assert.Equal("讀到防火時效", Line("判定依據", "設計值讀取結果"));
        Assert.Equal(FireRatingParameters.Provided, Line("判定依據", "讀取的參數"));
        Assert.Equal("30 分鐘", Line("判定依據", "設計／認證防火時效"));
    }

    /// <summary>What the header lines already said is not said again three lines further down.</summary>
    [Fact]
    public void The_evidence_does_not_repeat_the_header()
    {
        var labels = Section("判定依據").Lines.Select(l => l.Label).ToList();
        Assert.DoesNotContain("元素類別", labels);
        Assert.DoesNotContain("類型名稱", labels);
        Assert.DoesNotContain("區劃名稱", labels);
    }

    [Fact]
    public void Elements_and_settings_are_kept_apart_from_the_reading()
    {
        Assert.Equal("282774", Line("相關元素（Revit 元素編號）", "類型"));
        Assert.Equal("0.05 m", Line("量測設定", "取樣間距"));
    }

    [Fact]
    public void The_rule_is_last_because_it_is_asked_about_last()
    {
        Assert.Equal("tw-bcr-79-wall-rating　版本 1", Line("規則來源", "規則"));
        Assert.Equal("tw-bcr 1.0.0", Line("規則來源", "規則集"));
    }

    /// <summary>The whole point: nothing a user is shown is a UniqueId.</summary>
    [Fact]
    public void No_UniqueId_reaches_the_reader()
    {
        var text = Text();
        Assert.DoesNotContain("0a1b2c3d", text);
        Assert.Contains("282773", text);
        Assert.Contains("282774", text);
    }

    [Fact]
    public void A_row_of_the_table_describes_its_own_statistics()
    {
        Report(out var table);
        var sections = ReviewEntryReport.Describe(table.Section(ReviewCheckTypes.FireResistance));
        Assert.Equal("構件防火時效", sections[0].Title);
        Assert.Equal("未符合", sections[0].Lines.Single(l => l.Label == "狀態").Value);
        Assert.Equal("1 項", sections[0].Lines.Single(l => l.Label == "項目數").Value);
        Assert.Contains(sections, s => s.Title == "分項統計");
    }

    [Fact]
    public void The_text_form_titles_every_section() =>
        Assert.StartsWith("【檢討對象】", Text());

    /// <summary>The line of the 檢討表 itself: status, subject by element id, and why — cut short.</summary>
    [Fact]
    public void The_table_line_names_the_element_by_its_id()
    {
        Report(out var table);
        var entry = table.Entries.Single();

        Assert.Equal("未符合　牆「RC 牆 12cm」（元素 282773）＠防火區劃 A　設計／認證防火時效 不足：282773 只有 30 分鐘",
            ReviewEntryReport.Headline(entry));
    }

    [Fact]
    public void The_table_line_carries_the_drawing_number_when_there_is_one()
    {
        Report(out var table);
        Assert.StartsWith("未符合　CW-H1　牆", ReviewEntryReport.Headline(table.Entries.Single(), "CW-H1"));
    }

    [Fact]
    public void A_long_reason_is_cut_short_on_the_line_and_kept_whole_in_the_detail()
    {
        Report(out var table);
        var line = ReviewEntryReport.Headline(table.Entries.Single(), null, messageLimit: 6);

        Assert.EndsWith("　設計／認證防…", line);
        Assert.Contains("只有 30 分鐘", Line("檢討結果", "原因說明"));
    }
}
