using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Text;
using BuildingRegulationReview.Application.Checks;
using BuildingRegulationReview.Domain.Reviews;

namespace BuildingRegulationReview.Application.Reviews;

/// <summary>One labelled line of the detail pane. A line with no label is a paragraph of its own.</summary>
public sealed class ReviewDetailLine
{
    public ReviewDetailLine(string? label, string? value, bool emphasis = false)
    {
        Label = label?.Trim() ?? string.Empty;
        Value = value?.Trim() ?? string.Empty;
        Emphasis = emphasis;
    }

    public string Label { get; }
    public string Value { get; }

    /// <summary>The one line of its section a reader should see first: the 狀態, the 說明.</summary>
    public bool Emphasis { get; }

    public override string ToString() => Label.Length == 0 ? Value : Label + "：" + Value;
}

/// <summary>A titled block of the detail pane.</summary>
public sealed class ReviewDetailSection
{
    public ReviewDetailSection(string title, IEnumerable<ReviewDetailLine> lines)
    {
        Title = title?.Trim() ?? string.Empty;
        Lines = new ReadOnlyCollection<ReviewDetailLine>((lines ?? Enumerable.Empty<ReviewDetailLine>()).ToList());
    }

    public string Title { get; }
    public IReadOnlyList<ReviewDetailLine> Lines { get; }
    public bool IsEmpty => Lines.Count == 0;

    public override string ToString() => Title;
}

/// <summary>
/// What the 檢討面板 shows about one row of the 檢討表, arranged so the questions a reviewer actually
/// asks come first: what is being reviewed, what the answer was and why, and only then which rule
/// and which version produced it.
/// </summary>
/// <remarks>
/// Pure text built from the stored run, like <see cref="ReviewTable"/> itself — the panel renders it
/// and nothing more, and the same sections can be copied to the clipboard through
/// <see cref="ToText"/>. Every element is named by its element id rather than by its UniqueId, and
/// every evidence field by its Chinese name (<see cref="ReviewFieldText"/>).
/// </remarks>
public static class ReviewEntryReport
{
    /// <summary>How much of a 說明 one line of the 檢討表 carries before it is cut short.</summary>
    public const int MessageLimit = 60;

    /// <summary>Fields the header lines already say in full, so the evidence does not repeat them.</summary>
    private static readonly HashSet<string> Covered = new HashSet<string>(StringComparer.Ordinal)
    {
        "zone.id", "zone.name", "source.category", "source.typeName", "junction.kind",
        VerticalCompartmentRequirements.RequirementField, "shaft.requirementLabel"
    };

    /// <summary>
    /// The one line the 檢討表 shows for a result before it is opened: what it said, which 檢討圖號 it
    /// carries, what it is about — by element id, never by UniqueId — and the beginning of why. The
    /// 說明 is cut short on purpose: the line is for finding the row, and the row's own 原因說明 is
    /// where the whole sentence is read.
    /// </summary>
    public static string Headline(ReviewTableEntry entry, string? markNumber = null, int messageLimit = MessageLimit)
    {
        if (entry is null) throw new ArgumentNullException(nameof(entry));

        // 第79條之1 is a 區劃 too, so it reads as the 區劃's name rather than as an empty 類別
        // （第79條之1文件 §5.1）.
        var isArea = string.Equals(entry.CheckType, ReviewCheckTypes.CompartmentArea, StringComparison.Ordinal) ||
                     string.Equals(entry.CheckType, ReviewCheckTypes.AreaExemption, StringComparison.Ordinal);
        var subject = isArea
            ? entry.ZoneName ?? entry.ZoneId ?? entry.CategoryLabel
            : entry.CategoryLabel +
              (entry.TypeName is null ? string.Empty : "「" + entry.TypeName + "」") +
              Elements(entry) +
              (entry.ZoneName is null ? string.Empty : "＠" + entry.ZoneName);

        var mark = string.IsNullOrWhiteSpace(markNumber) ? string.Empty : markNumber!.Trim() + "　";
        var message = Summarize(ReviewFieldText.Humanize(entry.Message), messageLimit);
        return entry.StatusText + "　" + mark + subject + (message.Length == 0 ? string.Empty : "　" + message);
    }

    /// <summary>「（元素 282773）」, or 「（元素 282773 等 7 個）」 when one line covers several.</summary>
    private static string Elements(ReviewTableEntry entry)
    {
        var first = entry.LocateUniqueIds.FirstOrDefault();
        if (first is null) return string.Empty;
        var more = entry.LocateUniqueIds.Count > 1
            ? string.Format(CultureInfo.InvariantCulture, " 等 {0} 個", entry.LocateUniqueIds.Count)
            : string.Empty;
        return "（元素 " + ReviewElementReference.Describe(first) + more + "）";
    }

    private static string Summarize(string message, int limit)
    {
        var text = (message ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ').Trim();
        return text.Length <= limit ? text : text.Substring(0, limit).TrimEnd() + "…";
    }

    public static IReadOnlyList<ReviewDetailSection> Describe(ReviewTable table, ReviewTableEntry entry, string? markNumber = null)
    {
        if (table is null) throw new ArgumentNullException(nameof(table));
        if (entry is null) throw new ArgumentNullException(nameof(entry));

        var sections = new List<ReviewDetailSection>
        {
            new ReviewDetailSection("檢討對象", Subject(entry, markNumber)),
            new ReviewDetailSection("檢討結果", Verdict(entry))
        };

        var current = Override(entry);
        if (current.Count > 0) sections.Add(new ReviewDetailSection("人工覆寫", current));

        var history = History(table, entry);
        if (history.Count > 0) sections.Add(new ReviewDetailSection("過去的覆寫紀錄", history));

        var evidence = entry.Result.Evidence.Items.Where(x => !Covered.Contains(x.Field)).ToList();
        Add(sections, "判定依據", evidence.Where(x => Bucket(x.Field) == EvidenceBucket.Reading));
        Add(sections, "相關元素（Revit 元素編號）", evidence.Where(x => Bucket(x.Field) == EvidenceBucket.Element));
        Add(sections, "量測設定", evidence.Where(x => Bucket(x.Field) == EvidenceBucket.Option));

        sections.Add(new ReviewDetailSection("規則來源", Rule(table, entry)));
        return new ReadOnlyCollection<ReviewDetailSection>(sections);
    }

    /// <summary>The same for a 檢討表 row, which is what a section header in the tree stands for.</summary>
    public static IReadOnlyList<ReviewDetailSection> Describe(ReviewTableSection section)
    {
        if (section is null) throw new ArgumentNullException(nameof(section));

        var summary = new List<ReviewDetailLine>
        {
            new ReviewDetailLine("狀態", ReviewStatusText.Label(section.Status), emphasis: true),
            new ReviewDetailLine("總狀態", ReviewVerdictText.Label(section.Verdict)),
            new ReviewDetailLine("統計", section.Counts.Text),
            new ReviewDetailLine("項目數", Count(section.Entries.Count))
        };
        if (section.StaleCount > 0)
            summary.Add(new ReviewDetailLine("需更新", Count(section.StaleCount)));

        var sections = new List<ReviewDetailSection> { new ReviewDetailSection(section.Title, summary) };
        Add(sections, "分項統計", section.Groups.Select(g =>
            new ReviewDetailLine(g.Label, ReviewStatusText.Label(g.Status) + "（" + g.Counts.Text + "）")));
        return new ReadOnlyCollection<ReviewDetailSection>(sections);
    }

    /// <summary>The same for one line of a row's statistics.</summary>
    public static IReadOnlyList<ReviewDetailSection> Describe(ReviewTableGroup group)
    {
        if (group is null) throw new ArgumentNullException(nameof(group));

        return new ReadOnlyCollection<ReviewDetailSection>(new[]
        {
            new ReviewDetailSection(group.Label, new[]
            {
                new ReviewDetailLine("狀態", ReviewStatusText.Label(group.Status), emphasis: true),
                new ReviewDetailLine("統計", group.Counts.Text),
                new ReviewDetailLine("項目數", Count(group.ResultIds.Count)),
                new ReviewDetailLine("需更新", Count(group.StaleCount))
            })
        });
    }

    /// <summary>The sections as plain text, for 複製 and for the log.</summary>
    public static string ToText(IEnumerable<ReviewDetailSection> sections)
    {
        if (sections is null) throw new ArgumentNullException(nameof(sections));

        var text = new StringBuilder();
        foreach (var section in sections.Where(s => !s.IsEmpty))
        {
            if (text.Length > 0) text.AppendLine();
            text.AppendLine("【" + section.Title + "】");
            foreach (var line in section.Lines) text.AppendLine("　" + line);
        }
        return text.ToString();
    }

    private static IEnumerable<ReviewDetailLine> Subject(ReviewTableEntry entry, string? markNumber)
    {
        yield return new ReviewDetailLine("檢討項目", ReviewTable.Title(entry.CheckType), emphasis: true);

        if (!string.IsNullOrWhiteSpace(markNumber))
            yield return new ReviewDetailLine("檢討圖號", markNumber + (entry.JunctionKind == CurtainWallJunctionKind.FloorToCurtainWall
                ? "（同名的層間帶立面視圖）"
                : "（標註於檢討平面）"));

        if (entry.ShaftRequirementLabel is string requirement)
            yield return new ReviewDetailLine("受檢要求", requirement);

        if ((entry.ZoneName ?? entry.ZoneId) is string zone)
            yield return new ReviewDetailLine("所屬區劃", zone);

        yield return new ReviewDetailLine("元素類別", entry.CategoryLabel);
        if (entry.TypeName is string typeName) yield return new ReviewDetailLine("元素類型", typeName);

        yield return new ReviewDetailLine("Revit 元素編號", ReviewElementReference.DescribeMany(entry.LocateUniqueIds) +
                                                       (entry.IsLinked ? "（位於連結模型，無法在本模型選取）" : string.Empty));
    }

    private static IEnumerable<ReviewDetailLine> Verdict(ReviewTableEntry entry)
    {
        yield return new ReviewDetailLine("狀態", entry.StatusText, emphasis: true);
        yield return new ReviewDetailLine("原因說明", ReviewFieldText.Humanize(entry.Message), emphasis: true);
        yield return new ReviewDetailLine("模型實際值", ReviewValueText.Format(entry.ActualValue));
        yield return new ReviewDetailLine("法規要求值", ReviewValueText.Format(entry.RequiredValue));
        yield return new ReviewDetailLine("依據條文",
            string.IsNullOrWhiteSpace(entry.LegalReference) ? ReviewValueText.None : entry.LegalReference);
    }

    private static IReadOnlyList<ReviewDetailLine> Override(ReviewTableEntry entry)
    {
        if (entry.CurrentOverride is not ReviewOverride current) return Array.Empty<ReviewDetailLine>();

        var lines = new List<ReviewDetailLine>
        {
            new ReviewDetailLine("覆寫狀態", current.IsActive ? "生效中" : "需重新確認，目前未生效", emphasis: true),
            new ReviewDetailLine("判定改為",
                ReviewStatusText.Label(current.OriginalStatus) + " → " + ReviewStatusText.Label(current.OverriddenStatus)),
            new ReviewDetailLine("覆寫原因", current.Reason, emphasis: true),
            new ReviewDetailLine("操作者", current.OverriddenBy),
            new ReviewDetailLine("覆寫時間", Time(current.OverriddenAtUtc))
        };
        if (current.Comment is string comment) lines.Add(new ReviewDetailLine("註解", comment));
        if (current.StandingReason is string standing) lines.Add(new ReviewDetailLine("沿用情形", standing));
        return lines;
    }

    private static IReadOnlyList<ReviewDetailLine> History(ReviewTable table, ReviewTableEntry entry) =>
        table.Run.Overrides
            .Where(o => o.ResultId == entry.ResultId && !o.IsCurrent)
            .Select(o => new ReviewDetailLine(Time(o.OverriddenAtUtc) + "　" + o.OverriddenBy,
                ReviewStatusText.Label(o.OverriddenStatus) + "（" + o.Reason + "）" +
                (string.IsNullOrWhiteSpace(o.StandingReason) ? string.Empty : "　" + o.StandingReason)))
            .ToList();

    private static IEnumerable<ReviewDetailLine> Rule(ReviewTable table, ReviewTableEntry entry)
    {
        yield return new ReviewDetailLine("規則", entry.RuleId + "　版本 " + entry.RuleVersion);
        yield return new ReviewDetailLine("規則集", table.RuleSetId + " " + table.RuleSetVersion);
        yield return new ReviewDetailLine("檢討時間", Time(table.Run.StartedAtUtc));
    }

    private static void Add(List<ReviewDetailSection> sections, string title, IEnumerable<ReviewEvidenceItem> items)
    {
        var lines = items
            .Select(x => new ReviewDetailLine(ReviewFieldText.Label(x.Field), ReviewFieldText.Value(x.Field, x.Value)))
            .ToList();
        if (lines.Count > 0) sections.Add(new ReviewDetailSection(title, lines));
    }

    private static void Add(List<ReviewDetailSection> sections, string title, IEnumerable<ReviewDetailLine> lines)
    {
        var list = lines.ToList();
        if (list.Count > 0) sections.Add(new ReviewDetailSection(title, list));
    }

    private enum EvidenceBucket
    {
        Reading,
        Element,
        Option
    }

    private static EvidenceBucket Bucket(string field)
    {
        if (field.StartsWith("option.", StringComparison.Ordinal) ||
            string.Equals(field, "area.crossCheckTolerance", StringComparison.Ordinal))
            return EvidenceBucket.Option;

        if (field.EndsWith("UniqueId", StringComparison.Ordinal) ||
            field.EndsWith("UniqueIds", StringComparison.Ordinal) ||
            string.Equals(field, "junction.panels", StringComparison.Ordinal) ||
            string.Equals(field, "junction.doubtSubjects", StringComparison.Ordinal))
            return EvidenceBucket.Element;

        return EvidenceBucket.Reading;
    }

    private static string Count(int value) =>
        value.ToString(CultureInfo.InvariantCulture) + " 項";

    private static string Time(DateTime utc) =>
        utc.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
}
