using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using BuildingRegulationReview.Application.ProjectSetup;
using BuildingRegulationReview.Domain.ProjectSetup;

namespace BuildingRegulationReview.Revit.ProjectSetup;

public sealed class RevitSharedParameterInventoryReader : ISharedParameterInventoryReader
{
    private readonly Document _document;

    public RevitSharedParameterInventoryReader(Document document) =>
        _document = document ?? throw new ArgumentNullException(nameof(document));

    public IReadOnlyList<SharedParameterObservation> Read()
    {
        var observations = new List<SharedParameterObservation>();
        var iterator = _document.ParameterBindings.ForwardIterator();
        iterator.Reset();

        while (iterator.MoveNext())
        {
            var definition = iterator.Key;
            var binding = iterator.Current as ElementBinding;
            if (definition is null || binding is null) continue;

            Guid? guid = null;
            if (definition is InternalDefinition internalDefinition &&
                _document.GetElement(internalDefinition.Id) is SharedParameterElement sharedParameter)
            {
                guid = sharedParameter.GuidValue;
            }

            var categoryKeys = new List<string>();
            foreach (Category category in binding.Categories)
                categoryKeys.Add(ToCategoryKey(category));

            observations.Add(new SharedParameterObservation(
                definition.Name,
                guid,
                ToValueType(definition.GetDataType()),
                binding is TypeBinding ? SharedParameterBindingKind.Type : SharedParameterBindingKind.Instance,
                categoryKeys));
        }

        return observations;
    }

    public static string ToCategoryKey(Category category)
    {
        if (category is null) throw new ArgumentNullException(nameof(category));
        return $"BIC:{category.Id.Value}";
    }

    /// <summary>建立參數時也用同一份對應，兩邊才會對同一個型別有同一個答案。</summary>
    public static SharedParameterValueType? ToValueType(ForgeTypeId dataType)
    {
        if (dataType == SpecTypeId.String.Text) return SharedParameterValueType.Text;
        if (dataType == SpecTypeId.Boolean.YesNo) return SharedParameterValueType.YesNo;
        if (dataType == SpecTypeId.Int.Integer) return SharedParameterValueType.Integer;
        if (dataType == SpecTypeId.Number) return SharedParameterValueType.Number;
        if (dataType == SpecTypeId.Length) return SharedParameterValueType.Length;
        return null;
    }
}
