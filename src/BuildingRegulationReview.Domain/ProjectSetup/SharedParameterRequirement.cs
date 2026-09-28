using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace BuildingRegulationReview.Domain.ProjectSetup;

public enum SharedParameterValueType
{
    Text,
    YesNo,
    Integer,
    Number,

    /// <summary>A LENGTH parameter — 防火被覆厚度 is one, and a 長度 read as a plain 數值 would be in feet.</summary>
    Length
}

public enum SharedParameterBindingKind
{
    Instance,
    Type
}

public sealed class SharedParameterRequirement
{
    public SharedParameterRequirement(
        string name,
        Guid guid,
        SharedParameterValueType valueType,
        SharedParameterBindingKind bindingKind,
        IEnumerable<string> categoryKeys)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Parameter name is required.", nameof(name));
        if (guid == Guid.Empty) throw new ArgumentException("Parameter GUID cannot be empty.", nameof(guid));
        if (categoryKeys is null) throw new ArgumentNullException(nameof(categoryKeys));

        var categories = categoryKeys
            .Select(x => x?.Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Cast<string>()
            .Distinct(StringComparer.Ordinal)
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToList();

        if (categories.Count == 0) throw new ArgumentException("At least one category is required.", nameof(categoryKeys));

        Name = name.Trim();
        Guid = guid;
        ValueType = valueType;
        BindingKind = bindingKind;
        CategoryKeys = new ReadOnlyCollection<string>(categories);
    }

    public string Name { get; }
    public Guid Guid { get; }
    public SharedParameterValueType ValueType { get; }
    public SharedParameterBindingKind BindingKind { get; }
    public IReadOnlyList<string> CategoryKeys { get; }
}
