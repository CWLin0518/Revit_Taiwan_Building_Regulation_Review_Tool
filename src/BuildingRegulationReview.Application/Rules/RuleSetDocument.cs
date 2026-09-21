using System.Collections.Generic;

namespace BuildingRegulationReview.Application.Rules;

// The authored shape of a rule set: what a 法規維護者 writes and what a rule file deserializes into.
// Plain properties and strings only, like the other storage records, so any serializer can fill it
// and RuleSetSchemaValidator can report every mistake instead of stopping at the first one.
// Enumerations are spelled by name ("CompartmentArea", "Error") so a rule file reads as text.
public sealed class RuleSetDocument
{
    public string SchemaVersion { get; set; } = string.Empty;
    public string RuleSetId { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public IList<RuleDocument> Rules { get; set; } = new List<RuleDocument>();
}

public sealed class RuleDocument
{
    public string RuleId { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string LegalReference { get; set; } = string.Empty;

    /// <summary>ISO calendar date, <c>yyyy-MM-dd</c>.</summary>
    public string EffectiveDate { get; set; } = string.Empty;

    public string Jurisdiction { get; set; } = string.Empty;
    public int Priority { get; set; }
    public string AppliesWhen { get; set; } = string.Empty;
    public string RequiredValue { get; set; } = string.Empty;
    public IList<string> Exemptions { get; set; } = new List<string>();
    public IList<string> EvidenceFields { get; set; } = new List<string>();
    public string Severity { get; set; } = string.Empty;
}
