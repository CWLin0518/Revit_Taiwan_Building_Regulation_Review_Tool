using System;
using System.Collections.Generic;
using System.Linq;
using BuildingRegulationReview.Domain.ProjectSetup;

namespace BuildingRegulationReview.Application.ProjectSetup;

public enum SharedParameterDifferenceKind
{
    Missing,
    GuidMismatch,
    ValueTypeMismatch,
    BindingKindMismatch,
    MissingCategoryBinding,
    UnexpectedCategoryBinding
}

public sealed class SharedParameterDifference
{
    public SharedParameterDifference(string parameterName, SharedParameterDifferenceKind kind, string expected, string actual)
    {
        ParameterName = parameterName;
        Kind = kind;
        Expected = expected;
        Actual = actual;
    }

    public string ParameterName { get; }
    public SharedParameterDifferenceKind Kind { get; }
    public string Expected { get; }
    public string Actual { get; }
}

public sealed class SharedParameterDryRunReport
{
    public SharedParameterDryRunReport(IReadOnlyList<SharedParameterDifference> differences) => Differences = differences;
    public IReadOnlyList<SharedParameterDifference> Differences { get; }
    public bool IsCompliant => Differences.Count == 0;
}

public sealed class SharedParameterDryRunService
{
    private readonly ISharedParameterInventoryReader _reader;

    public SharedParameterDryRunService(ISharedParameterInventoryReader reader) =>
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));

    public SharedParameterDryRunReport Inspect(ProjectSetupConfiguration configuration)
    {
        if (configuration is null) throw new ArgumentNullException(nameof(configuration));
        var observed = _reader.Read();
        var differences = new List<SharedParameterDifference>();

        foreach (var expected in configuration.SharedParameters)
        {
            var actual = observed.FirstOrDefault(x => string.Equals(x.Name, expected.Name, StringComparison.Ordinal));
            if (actual is null)
            {
                differences.Add(new SharedParameterDifference(expected.Name, SharedParameterDifferenceKind.Missing, expected.Guid.ToString("D"), "not present"));
                continue;
            }

            AddIfDifferent(differences, expected.Name, SharedParameterDifferenceKind.GuidMismatch,
                expected.Guid.ToString("D"), actual.Guid?.ToString("D") ?? "not a shared parameter");
            AddIfDifferent(differences, expected.Name, SharedParameterDifferenceKind.ValueTypeMismatch,
                expected.ValueType.ToString(), actual.ValueType?.ToString() ?? "unsupported");
            AddIfDifferent(differences, expected.Name, SharedParameterDifferenceKind.BindingKindMismatch,
                expected.BindingKind.ToString(), actual.BindingKind.ToString());

            var expectedCategories = new HashSet<string>(expected.CategoryKeys, StringComparer.Ordinal);
            var actualCategories = new HashSet<string>(actual.CategoryKeys, StringComparer.Ordinal);
            foreach (var missing in expectedCategories.Except(actualCategories).OrderBy(x => x, StringComparer.Ordinal))
                differences.Add(new SharedParameterDifference(expected.Name, SharedParameterDifferenceKind.MissingCategoryBinding, missing, "not bound"));
            foreach (var extra in actualCategories.Except(expectedCategories).OrderBy(x => x, StringComparer.Ordinal))
                differences.Add(new SharedParameterDifference(expected.Name, SharedParameterDifferenceKind.UnexpectedCategoryBinding, "not bound", extra));
        }

        return new SharedParameterDryRunReport(differences);
    }

    private static void AddIfDifferent(List<SharedParameterDifference> differences, string name,
        SharedParameterDifferenceKind kind, string expected, string actual)
    {
        if (!string.Equals(expected, actual, StringComparison.Ordinal))
            differences.Add(new SharedParameterDifference(name, kind, expected, actual));
    }
}
