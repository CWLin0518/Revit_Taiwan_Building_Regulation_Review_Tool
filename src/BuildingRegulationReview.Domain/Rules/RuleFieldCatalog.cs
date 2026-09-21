using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using BuildingRegulationReview.Domain.Reviews;
using BuildingRegulationReview.Domain.Rules.Expressions;

namespace BuildingRegulationReview.Domain.Rules;

/// <summary>
/// One field a rule expression may read. A field is the only way data enters an expression: there
/// are no variables, calls or member access beyond this whitelist (spec 11.2 "白名單欄位").
/// </summary>
public sealed class RuleFieldDefinition
{
    public RuleFieldDefinition(string name, RuleValueType type, string description, IEnumerable<RuleCategory> availableIn)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Field name is required.", nameof(name));
        var trimmed = name.Trim();
        if (!IsWellFormedName(trimmed))
            throw new ArgumentException($"Field name '{trimmed}' must be scope.name made of letters, digits and underscores.", nameof(name));
        if (availableIn is null) throw new ArgumentNullException(nameof(availableIn));

        var categories = availableIn.Distinct().ToList();
        if (categories.Count == 0) throw new ArgumentException("A field must be available to at least one rule category.", nameof(availableIn));

        Name = trimmed;
        Type = type;
        Description = description?.Trim() ?? string.Empty;
        AvailableIn = new ReadOnlyCollection<RuleCategory>(categories);
    }

    public string Name { get; }
    public RuleValueType Type { get; }
    public string Description { get; }

    /// <summary>The rule categories whose subjects can supply this field.</summary>
    public IReadOnlyList<RuleCategory> AvailableIn { get; }

    public bool IsAvailableIn(RuleCategory category) => AvailableIn.Contains(category);

    private static bool IsWellFormedName(string name)
    {
        var parts = name.Split('.');
        return parts.Length >= 2 && parts.All(part =>
            part.Length > 0 &&
            (char.IsLetter(part[0]) || part[0] == '_') &&
            part.All(c => char.IsLetterOrDigit(c) || c == '_'));
    }
}

/// <summary>The whitelist of fields rule expressions may read.</summary>
public sealed class RuleFieldCatalog
{
    private readonly Dictionary<string, RuleFieldDefinition> _byName;

    public RuleFieldCatalog(IEnumerable<RuleFieldDefinition> fields)
    {
        if (fields is null) throw new ArgumentNullException(nameof(fields));

        var list = fields.ToList();
        if (list.Any(x => x is null)) throw new ArgumentException("The catalog cannot hold a missing field.", nameof(fields));
        var duplicate = list.GroupBy(x => x.Name, StringComparer.Ordinal).FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
            throw new ArgumentException($"Field '{duplicate.Key}' is defined more than once.", nameof(fields));

        Fields = new ReadOnlyCollection<RuleFieldDefinition>(list);
        _byName = list.ToDictionary(x => x.Name, StringComparer.Ordinal);
    }

    public IReadOnlyList<RuleFieldDefinition> Fields { get; }

    public RuleFieldDefinition? Find(string name) =>
        name is not null && _byName.TryGetValue(name.Trim(), out var field) ? field : null;

    /// <summary>
    /// The fields the review can supply today. <c>building.*</c> and <c>zone.*</c> describe the
    /// compartment and are open to every category; <c>element.*</c> only exists for fire-resistance
    /// subjects and <c>opening.*</c> only for opening-protection subjects.
    /// </summary>
    public static RuleFieldCatalog Default { get; } = CreateDefault();

    private static RuleFieldCatalog CreateDefault()
    {
        var all = new[] { RuleCategory.CompartmentArea, RuleCategory.FireResistance, RuleCategory.OpeningProtection };
        var element = new[] { RuleCategory.FireResistance };
        var opening = new[] { RuleCategory.OpeningProtection };
        var boolean = RuleValueType.Boolean;
        var text = RuleValueType.Text;
        var number = RuleValueType.Quantity(ReviewUnit.None);

        return new RuleFieldCatalog(new[]
        {
            new RuleFieldDefinition("building.fireResistiveConstruction", boolean, "是否為防火構造建築物", all),
            new RuleFieldDefinition("building.use", text, "建築物用途類組", all),
            new RuleFieldDefinition("building.floorsAboveGround", number, "地上層數", all),
            new RuleFieldDefinition("building.height", RuleValueType.Quantity(ReviewUnit.Meter), "建築物高度", all),

            new RuleFieldDefinition("zone.id", text, "區劃 Zone ID", all),
            new RuleFieldDefinition("zone.use", text, "區劃用途", all),
            new RuleFieldDefinition("zone.area", RuleValueType.Quantity(ReviewUnit.SquareMeter), "區劃面積（Revit Area）", all),
            new RuleFieldDefinition("zone.sprinklered", boolean, "是否設有自動滅火設備", all),
            new RuleFieldDefinition("zone.levelName", text, "所在樓層名稱", all),
            new RuleFieldDefinition("zone.floorNumber", number, "所在樓層序（地上為正、地下為負）", all),

            new RuleFieldDefinition("element.category", text, "構件類別（Walls／Columns／StructuralFraming／Floors）", element),
            new RuleFieldDefinition("element.typeName", text, "構件 Type 名稱", element),
            new RuleFieldDefinition("element.isCompartmentBoundary", boolean, "是否構成區劃邊界", element),
            new RuleFieldDefinition("element.isStructural", boolean, "是否為結構構件", element),
            new RuleFieldDefinition("element.providedFireRating", RuleValueType.Quantity(ReviewUnit.Minute), "設計／認證防火時效", element),

            new RuleFieldDefinition("opening.kind", text, "開口種類（Door／Window／CurtainPanel）", opening),
            new RuleFieldDefinition("opening.isHosted", boolean, "是否為 Hosted 開口", opening),
            new RuleFieldDefinition("opening.hostUniqueId", text, "Host 牆 UniqueId", opening),
            new RuleFieldDefinition("opening.hostIsCompartmentBoundary", boolean, "Host 牆是否為區劃邊界", opening),
            new RuleFieldDefinition("opening.area", RuleValueType.Quantity(ReviewUnit.SquareMeter), "開口面積", opening),
            new RuleFieldDefinition("opening.providedFireProtection", text, "設計防火保護（是／否）", opening)
        });
    }
}
