using System;
using System.Collections.Generic;
using BuildingRegulationReview.Domain.ProjectSetup;

namespace BuildingRegulationReview.Application.ProjectSetup;

public sealed class SharedParameterObservation
{
    public SharedParameterObservation(
        string name,
        Guid? guid,
        SharedParameterValueType? valueType,
        SharedParameterBindingKind bindingKind,
        IEnumerable<string> categoryKeys)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Guid = guid;
        ValueType = valueType;
        BindingKind = bindingKind;
        CategoryKeys = new List<string>(categoryKeys ?? throw new ArgumentNullException(nameof(categoryKeys)));
    }

    public string Name { get; }
    public Guid? Guid { get; }
    public SharedParameterValueType? ValueType { get; }
    public SharedParameterBindingKind BindingKind { get; }
    public IReadOnlyList<string> CategoryKeys { get; }
}

public interface ISharedParameterInventoryReader
{
    IReadOnlyList<SharedParameterObservation> Read();
}
