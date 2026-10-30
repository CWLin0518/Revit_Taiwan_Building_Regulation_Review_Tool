using System;
using System.Collections.Generic;
using BuildingRegulationReview.Application.ProjectSetup;
using BuildingRegulationReview.Domain.ProjectSetup;
using Xunit;

namespace BuildingRegulationReview.Core.Tests.ProjectSetup;

public sealed class SharedParameterDryRunServiceTests
{
    private static readonly Guid FixtureGuid = Guid.Parse("11111111-2222-4333-8444-555555555555");

    [Fact]
    public void Inspect_reports_missing_parameter_without_writing()
    {
        var reader = new StubReader(Array.Empty<SharedParameterObservation>());

        var report = new SharedParameterDryRunService(reader).Inspect(Configuration());

        var difference = Assert.Single(report.Differences);
        Assert.Equal(SharedParameterDifferenceKind.Missing, difference.Kind);
        Assert.Equal(1, reader.ReadCount);
    }

    [Fact]
    public void Inspect_reports_same_name_with_different_guid()
    {
        var observed = Observation(guid: Guid.Parse("aaaaaaaa-bbbb-4ccc-8ddd-eeeeeeeeeeee"));

        var report = new SharedParameterDryRunService(new StubReader(observed)).Inspect(Configuration());

        Assert.Contains(report.Differences, x => x.Kind == SharedParameterDifferenceKind.GuidMismatch);
    }

    [Fact]
    public void Inspect_reports_wrong_value_type()
    {
        var observed = Observation(valueType: SharedParameterValueType.Text);

        var report = new SharedParameterDryRunService(new StubReader(observed)).Inspect(Configuration());

        Assert.Contains(report.Differences, x => x.Kind == SharedParameterDifferenceKind.ValueTypeMismatch);
    }

    [Fact]
    public void Inspect_reports_binding_and_category_differences()
    {
        var observed = Observation(
            bindingKind: SharedParameterBindingKind.Type,
            categories: new[] { "BIC:-2000023", "BIC:-2000014" });

        var report = new SharedParameterDryRunService(new StubReader(observed)).Inspect(Configuration());

        Assert.Contains(report.Differences, x => x.Kind == SharedParameterDifferenceKind.BindingKindMismatch);
        Assert.Contains(report.Differences, x => x.Kind == SharedParameterDifferenceKind.MissingCategoryBinding);
        Assert.Contains(report.Differences, x => x.Kind == SharedParameterDifferenceKind.UnexpectedCategoryBinding);
    }

    [Fact]
    public void Inspect_returns_compliant_for_exact_match()
    {
        var report = new SharedParameterDryRunService(new StubReader(Observation())).Inspect(Configuration());

        Assert.True(report.IsCompliant);
        Assert.Empty(report.Differences);
    }

    [Fact]
    public void Configuration_rejects_duplicate_names()
    {
        var first = Requirement();
        var second = new SharedParameterRequirement(first.Name, Guid.NewGuid(), first.ValueType, first.BindingKind, first.CategoryKeys);

        Assert.Throws<ArgumentException>(() => new ProjectSetupConfiguration(new[] { first, second }));
    }

    private static ProjectSetupConfiguration Configuration() => new(new[] { Requirement() });

    private static SharedParameterRequirement Requirement() => new(
        "BCR_RequiredFireRating",
        FixtureGuid,
        SharedParameterValueType.Integer,
        SharedParameterBindingKind.Instance,
        new[] { "BIC:-2000011", "BIC:-2000023" });

    private static SharedParameterObservation Observation(
        Guid? guid = null,
        SharedParameterValueType valueType = SharedParameterValueType.Integer,
        SharedParameterBindingKind bindingKind = SharedParameterBindingKind.Instance,
        IEnumerable<string>? categories = null) => new(
            "BCR_RequiredFireRating",
            guid ?? FixtureGuid,
            valueType,
            bindingKind,
            categories ?? new[] { "BIC:-2000011", "BIC:-2000023" });

    private sealed class StubReader : ISharedParameterInventoryReader
    {
        private readonly IReadOnlyList<SharedParameterObservation> _items;

        public StubReader(params SharedParameterObservation[] items) => _items = items;
        public int ReadCount { get; private set; }

        public IReadOnlyList<SharedParameterObservation> Read()
        {
            ReadCount++;
            return _items;
        }
    }
}
