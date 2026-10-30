using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using BuildingRegulationReview.Domain.Rules;

namespace BuildingRegulationReview.Application.Rules;

/// <summary>One thing wrong with a rule file, located by a JSON-style path such as <c>rules[2].category</c>.</summary>
public sealed class RuleSchemaIssue
{
    public RuleSchemaIssue(string path, string code, string message)
    {
        Path = path ?? throw new ArgumentNullException(nameof(path));
        Code = code ?? throw new ArgumentNullException(nameof(code));
        Message = message ?? throw new ArgumentNullException(nameof(message));
    }

    public string Path { get; }
    public string Code { get; }
    public string Message { get; }

    public override string ToString() => $"{Path}: {Message} ({Code})";
}

/// <summary>Stable identifiers of the schema checks, so tests and the UI can match on them.</summary>
public static class RuleSchemaIssueCode
{
    public const string Missing = "missing";
    public const string UnsupportedSchemaVersion = "unsupported-schema-version";
    public const string UnknownValue = "unknown-value";
    public const string InvalidDate = "invalid-date";
    public const string OutOfRange = "out-of-range";
    public const string Duplicate = "duplicate";
    public const string Empty = "empty";
}

public sealed class RuleSetValidationReport
{
    public RuleSetValidationReport(IEnumerable<RuleSchemaIssue> issues)
    {
        Issues = new ReadOnlyCollection<RuleSchemaIssue>((issues ?? throw new ArgumentNullException(nameof(issues))).ToList());
    }

    public IReadOnlyList<RuleSchemaIssue> Issues { get; }
    public bool IsValid => Issues.Count == 0;

    public string ToText() => string.Join(Environment.NewLine, Issues.Select(x => x.ToString()));
}

/// <summary>
/// Checks the shape of a rule file before any of it reaches the domain (spec 11.2). It checks
/// structure only — required fields, known names, dates, uniqueness. Whether an expression parses,
/// uses whitelisted fields or conflicts with another rule is the rule engine's question (P3-T02).
/// </summary>
public static class RuleSetSchemaValidator
{
    public const string DateFormat = "yyyy-MM-dd";

    public static RuleSetValidationReport Validate(RuleSetDocument? document)
    {
        var issues = new List<RuleSchemaIssue>();
        if (document is null)
        {
            issues.Add(new RuleSchemaIssue("$", RuleSchemaIssueCode.Missing, "規則檔是空的。"));
            return new RuleSetValidationReport(issues);
        }

        if (string.IsNullOrWhiteSpace(document.SchemaVersion))
            issues.Add(new RuleSchemaIssue("schemaVersion", RuleSchemaIssueCode.Missing, "缺少 schemaVersion。"));
        else if (!string.Equals(document.SchemaVersion.Trim(), RuleSet.CurrentSchemaVersion, StringComparison.Ordinal))
            issues.Add(new RuleSchemaIssue("schemaVersion", RuleSchemaIssueCode.UnsupportedSchemaVersion,
                $"不支援的 schemaVersion「{document.SchemaVersion.Trim()}」，目前只支援 {RuleSet.CurrentSchemaVersion}。"));

        RequireText(issues, "ruleSetId", document.RuleSetId);
        RequireText(issues, "version", document.Version);

        var rules = document.Rules ?? new List<RuleDocument>();
        if (rules.Count == 0)
            issues.Add(new RuleSchemaIssue("rules", RuleSchemaIssueCode.Empty, "規則集至少要有一條規則。"));

        for (var i = 0; i < rules.Count; i++)
            ValidateRule(issues, $"rules[{i}]", rules[i]);

        var duplicateIds = rules
            .Select((rule, index) => (id: rule?.RuleId?.Trim() ?? string.Empty, index))
            .Where(x => x.id.Length > 0)
            .GroupBy(x => x.id, StringComparer.Ordinal)
            .Where(g => g.Count() > 1);
        foreach (var group in duplicateIds)
            foreach (var (_, index) in group.Skip(1))
                issues.Add(new RuleSchemaIssue($"rules[{index}].ruleId", RuleSchemaIssueCode.Duplicate,
                    $"ruleId「{group.Key}」在規則集中重複；一個規則集只能鎖定一條規則的一個版本。"));

        return new RuleSetValidationReport(issues);
    }

    private static void ValidateRule(List<RuleSchemaIssue> issues, string path, RuleDocument? rule)
    {
        if (rule is null)
        {
            issues.Add(new RuleSchemaIssue(path, RuleSchemaIssueCode.Missing, "規則是空的。"));
            return;
        }

        RequireText(issues, $"{path}.ruleId", rule.RuleId);
        RequireText(issues, $"{path}.version", rule.Version);
        RequireEnum<RuleCategory>(issues, $"{path}.category", rule.Category);
        RequireText(issues, $"{path}.legalReference", rule.LegalReference);
        RequireText(issues, $"{path}.jurisdiction", rule.Jurisdiction);
        RequireText(issues, $"{path}.appliesWhen", rule.AppliesWhen);
        RequireText(issues, $"{path}.requiredValue", rule.RequiredValue);
        RequireEnum<RuleSeverity>(issues, $"{path}.severity", rule.Severity);

        if (string.IsNullOrWhiteSpace(rule.EffectiveDate))
            issues.Add(new RuleSchemaIssue($"{path}.effectiveDate", RuleSchemaIssueCode.Missing, "缺少 effectiveDate。"));
        else if (!TryParseDate(rule.EffectiveDate, out _))
            issues.Add(new RuleSchemaIssue($"{path}.effectiveDate", RuleSchemaIssueCode.InvalidDate,
                $"effectiveDate「{rule.EffectiveDate.Trim()}」不是 {DateFormat} 格式的日期。"));

        if (rule.Priority < 0)
            issues.Add(new RuleSchemaIssue($"{path}.priority", RuleSchemaIssueCode.OutOfRange, "priority 不可為負數。"));

        var exemptions = rule.Exemptions ?? new List<string>();
        for (var i = 0; i < exemptions.Count; i++)
            if (string.IsNullOrWhiteSpace(exemptions[i]))
                issues.Add(new RuleSchemaIssue($"{path}.exemptions[{i}]", RuleSchemaIssueCode.Empty, "豁免條件不可空白。"));

        var fields = rule.EvidenceFields ?? new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < fields.Count; i++)
        {
            if (string.IsNullOrWhiteSpace(fields[i]))
                issues.Add(new RuleSchemaIssue($"{path}.evidenceFields[{i}]", RuleSchemaIssueCode.Empty, "證據欄位名稱不可空白。"));
            else if (!seen.Add(fields[i].Trim()))
                issues.Add(new RuleSchemaIssue($"{path}.evidenceFields[{i}]", RuleSchemaIssueCode.Duplicate,
                    $"證據欄位「{fields[i].Trim()}」重複。"));
        }
    }

    internal static bool TryParseDate(string text, out DateTime date) =>
        DateTime.TryParseExact(text.Trim(), DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out date);

    // Enum.TryParse would also accept "1" or "Error, Info"; a rule file must spell the one name out.
    internal static bool TryParseName<TEnum>(string? text, out TEnum value) where TEnum : struct
    {
        value = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var name = Enum.GetNames(typeof(TEnum)).FirstOrDefault(x => string.Equals(x, text!.Trim(), StringComparison.Ordinal));
        if (name is null) return false;
        value = (TEnum)Enum.Parse(typeof(TEnum), name);
        return true;
    }

    private static void RequireText(List<RuleSchemaIssue> issues, string path, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            issues.Add(new RuleSchemaIssue(path, RuleSchemaIssueCode.Missing, $"缺少 {LastSegment(path)}。"));
    }

    private static void RequireEnum<TEnum>(List<RuleSchemaIssue> issues, string path, string? value) where TEnum : struct
    {
        if (string.IsNullOrWhiteSpace(value))
            issues.Add(new RuleSchemaIssue(path, RuleSchemaIssueCode.Missing, $"缺少 {LastSegment(path)}。"));
        else if (!TryParseName<TEnum>(value, out _))
            issues.Add(new RuleSchemaIssue(path, RuleSchemaIssueCode.UnknownValue,
                $"{LastSegment(path)}「{value!.Trim()}」不是可用的值，只能是：{string.Join("、", Enum.GetNames(typeof(TEnum)))}。"));
    }

    private static string LastSegment(string path)
    {
        var dot = path.LastIndexOf('.');
        return dot < 0 ? path : path.Substring(dot + 1);
    }
}
