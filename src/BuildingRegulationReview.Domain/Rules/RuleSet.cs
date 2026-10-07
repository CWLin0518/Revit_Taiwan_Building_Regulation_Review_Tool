using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace BuildingRegulationReview.Domain.Rules;

/// <summary>
/// A named, versioned collection of rules — the thing a package locks to before a review may run
/// (spec 11.1 "規則集存在且版本已鎖定"). Identity is <see cref="RuleSetId"/> plus
/// <see cref="Version"/>; a set pins exactly one version of each rule it holds.
/// </summary>
public sealed class RuleSet
{
    public const string CurrentSchemaVersion = "1.0";

    private readonly Dictionary<string, Rule> _byId;

    public RuleSet(
        string ruleSetId,
        string version,
        string title,
        IEnumerable<Rule> rules)
    {
        if (string.IsNullOrWhiteSpace(ruleSetId)) throw new ArgumentException("Rule set ID is required.", nameof(ruleSetId));
        if (string.IsNullOrWhiteSpace(version)) throw new ArgumentException("Rule set version is required.", nameof(version));
        if (rules is null) throw new ArgumentNullException(nameof(rules));

        var list = rules.ToList();
        if (list.Count == 0) throw new ArgumentException("A rule set must hold at least one rule.", nameof(rules));
        if (list.Any(x => x is null)) throw new ArgumentException("A rule set cannot hold a missing rule.", nameof(rules));

        var duplicate = list.GroupBy(x => x.RuleId, StringComparer.Ordinal).FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
            throw new ArgumentException($"Rule '{duplicate.Key}' appears more than once in the set.", nameof(rules));

        RuleSetId = ruleSetId.Trim();
        Version = version.Trim();
        Title = title?.Trim() ?? string.Empty;
        Rules = new ReadOnlyCollection<Rule>(list);
        _byId = list.ToDictionary(x => x.RuleId, StringComparer.Ordinal);
    }

    public string RuleSetId { get; }
    public string Version { get; }
    public string Title { get; }
    public string SchemaVersion => CurrentSchemaVersion;
    public IReadOnlyList<Rule> Rules { get; }

    /// <summary>
    /// The 法規依據 of a result no individual rule decided — an ambiguity a check answers itself, or
    /// the engine's NoRule outcome. Such a row cites no 條文, so it says exactly that instead of
    /// standing in the title: <see cref="Title"/> is a design note of several hundred words, and a
    /// reader of 法規依據 taken apart by <c>ReviewLegalReference</c> would see it mislabelled as
    /// 函釋 and 檢討重點 (驗證清單 B-01).
    /// </summary>
    public static string FallbackLegalReferenceOf(string ruleSetId, string version) =>
        $"依規則集 {ruleSetId?.Trim()} {version?.Trim()} 判定（本列無個別條文）";

    /// <summary>This set's <see cref="FallbackLegalReferenceOf"/>.</summary>
    public string FallbackLegalReference => FallbackLegalReferenceOf(RuleSetId, Version);

    public Rule? Find(string ruleId) =>
        ruleId is not null && _byId.TryGetValue(ruleId.Trim(), out var rule) ? rule : null;

    public IEnumerable<Rule> OfCategory(RuleCategory category) => Rules.Where(x => x.Category == category);
}
