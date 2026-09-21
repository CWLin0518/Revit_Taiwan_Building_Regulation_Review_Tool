using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace BuildingRegulationReview.Domain.Reviews;

public sealed class ReviewEvidenceItem
{
    public ReviewEvidenceItem(string field, ReviewValue value)
    {
        if (string.IsNullOrWhiteSpace(field)) throw new ArgumentException("Evidence field is required.", nameof(field));

        Field = field.Trim();
        Value = value ?? throw new ArgumentNullException(nameof(value));
    }

    public string Field { get; }
    public ReviewValue Value { get; }
}

/// <summary>
/// The named values a result was decided on (spec 11.3 <c>evidence</c>). Fields are unique and
/// keep the order they were recorded in, so the review table reads them the way the check did.
/// </summary>
public sealed class ReviewEvidence
{
    public static readonly ReviewEvidence Empty = new ReviewEvidence(Array.Empty<ReviewEvidenceItem>());

    private readonly Dictionary<string, ReviewValue> _byField;

    public ReviewEvidence(IEnumerable<ReviewEvidenceItem> items)
    {
        if (items is null) throw new ArgumentNullException(nameof(items));

        var list = items.ToList();
        if (list.Any(x => x is null)) throw new ArgumentException("Evidence cannot hold a missing item.", nameof(items));

        var duplicate = list.GroupBy(x => x.Field, StringComparer.Ordinal).FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
            throw new ArgumentException($"Evidence field '{duplicate.Key}' is recorded more than once.", nameof(items));

        Items = new ReadOnlyCollection<ReviewEvidenceItem>(list);
        _byField = list.ToDictionary(x => x.Field, x => x.Value, StringComparer.Ordinal);
    }

    public IReadOnlyList<ReviewEvidenceItem> Items { get; }
    public int Count => Items.Count;

    public ReviewValue? Find(string field) =>
        field is not null && _byField.TryGetValue(field.Trim(), out var value) ? value : null;

    public bool Has(string field) => Find(field) is not null;
}
