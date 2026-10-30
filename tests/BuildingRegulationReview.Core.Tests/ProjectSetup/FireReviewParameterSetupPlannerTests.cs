using System;
using System.Collections.Generic;
using System.Linq;
using BuildingRegulationReview.Application.Checks;
using BuildingRegulationReview.Application.ProjectSetup;
using BuildingRegulationReview.Application.Reviews;
using BuildingRegulationReview.Domain.ProjectSetup;
using Xunit;

namespace BuildingRegulationReview.Core.Tests.ProjectSetup;

/// <summary>
/// The diff 「一鍵建立」 shows before it touches anything (spec 8.2): what is missing, what only needs a
/// category added, and what must not be overwritten.
/// </summary>
public sealed class FireReviewParameterSetupPlannerTests
{
    /// <summary>A project that has none of them: every parameter is created, and nothing is a conflict.</summary>
    [Fact]
    public void An_empty_project_creates_every_parameter()
    {
        var plan = FireReviewParameterSetupPlanner.Inspect(Array.Empty<FireReviewParameterBindingObservation>());

        Assert.Equal(FireReviewParameterCatalog.All.Count, plan.ToCreate.Count);
        Assert.Empty(plan.Conflicts);
        Assert.Empty(plan.ToExtend);
        Assert.True(plan.HasWork);
        foreach (var action in plan.ToCreate)
            Assert.Equal(action.Definition.Hosts, action.MissingHosts);
    }

    /// <summary>A project that already has everything has nothing to do, and the button has to say so.</summary>
    [Fact]
    public void A_project_that_already_has_them_all_has_no_work()
    {
        var plan = FireReviewParameterSetupPlanner.Inspect(FireReviewParameterCatalog.All.Select(Bound));

        Assert.Equal(FireReviewParameterCatalog.All.Count, plan.AlreadyBound.Count);
        Assert.False(plan.HasWork);
        Assert.Empty(plan.Conflicts);
    }

    /// <summary>
    /// 設計防火時效 bound to the 主要構造 but not to 門: 第79條之2第1項 reads it on a 管道間維修門, so the
    /// missing category is added rather than the parameter reported as absent. Adding a category does
    /// not touch any value, which is why this is work the tool may do on its own.
    /// </summary>
    [Fact]
    public void A_parameter_missing_only_a_category_gets_the_category_added()
    {
        var definition = Definition(FireRatingParameters.Provided);
        var partial = Bound(definition, definition.Hosts.Where(x => x != ReviewParameterHost.Doors));

        var action = Action(FireReviewParameterSetupPlanner.Inspect(new[] { partial }), definition.Name);

        Assert.Equal(FireReviewParameterActionKind.AddCategories, action.Kind);
        Assert.Equal(new[] { ReviewParameterHost.Doors }, action.MissingHosts);
        Assert.Contains(ReviewParameterHost.Walls, action.BoundHosts);
        Assert.Contains("既有的值不會被改動", action.DetailText);
    }

    /// <summary>
    /// A different GUID is the case the master file's header records: 設計防火保護 went from …0009 to
    /// …000d. Revit will not hold two project parameters of the same name, so the old one has to go
    /// first and its values go with it — the tool says so instead of doing it.
    /// </summary>
    [Fact]
    public void A_parameter_with_another_guid_is_never_overwritten()
    {
        var definition = Definition(FireProtectionParameters.Provided);
        var other = new Guid("bcf10001-0000-4a00-9b00-000000000009");

        var action = Action(FireReviewParameterSetupPlanner.Inspect(new[]
        {
            new FireReviewParameterBindingObservation(definition.Name, other, definition.ValueType, definition.Level, definition.Hosts)
        }), definition.Name);

        Assert.Equal(FireReviewParameterActionKind.Conflict, action.Kind);
        Assert.Contains(other.ToString("D"), action.Problem!);
        Assert.Contains("移除", action.Fix!);
        Assert.Contains("值消失", action.Fix!);
    }

    /// <summary>
    /// The same name on a parameter that is not a shared parameter at all: there is no GUID to compare,
    /// and a locally defined project parameter of the same name is exactly what the fixed GUIDs exist
    /// to prevent (it stops matching the moment the model moves to another machine).
    /// </summary>
    [Fact]
    public void A_same_named_parameter_that_is_not_a_shared_parameter_is_a_conflict()
    {
        var definition = Definition(ReviewInputSources.Sprinklered);

        var action = Action(FireReviewParameterSetupPlanner.Inspect(new[]
        {
            new FireReviewParameterBindingObservation(definition.Name, null, definition.ValueType, definition.Level, definition.Hosts)
        }), definition.Name);

        Assert.Equal(FireReviewParameterActionKind.Conflict, action.Kind);
        Assert.Contains("不是共用參數", action.Problem!);
    }

    /// <summary>
    /// The 設計防火保護 migration also changed the data type (TEXT → YESNO). A text box where the review
    /// expects a tick reads as 資料不足 on every opening, so it is a conflict, not something to extend.
    /// </summary>
    [Fact]
    public void A_parameter_of_another_data_type_is_a_conflict()
    {
        var definition = Definition(FireProtectionParameters.Provided);

        var action = Action(FireReviewParameterSetupPlanner.Inspect(new[]
        {
            new FireReviewParameterBindingObservation(definition.Name, definition.Guid,
                SharedParameterValueType.Text, definition.Level, definition.Hosts)
        }), definition.Name);

        Assert.Equal(FireReviewParameterActionKind.Conflict, action.Kind);
        Assert.Contains("文字", action.Problem!);
        Assert.Contains("是非", action.Problem!);
    }

    /// <summary>
    /// Instance where the review reads a Type, which is the other half of that migration: the review
    /// reads 設計防火保護 off the Type, so an instance binding is never read, however carefully it is
    /// filled in. Revit cannot convert one into the other, so this is a conflict too.
    /// </summary>
    [Fact]
    public void A_parameter_bound_to_the_wrong_level_is_a_conflict()
    {
        var definition = Definition(FireProtectionParameters.Provided);

        var action = Action(FireReviewParameterSetupPlanner.Inspect(new[]
        {
            new FireReviewParameterBindingObservation(definition.Name, definition.Guid, definition.ValueType,
                ReviewParameterLevel.Instance, definition.Hosts)
        }), definition.Name);

        Assert.Equal(FireReviewParameterActionKind.Conflict, action.Kind);
        Assert.Contains("實體參數", action.Problem!);
        Assert.Contains("類型參數", action.Problem!);
    }

    /// <summary>
    /// A conflict is not work: pressing the button with nothing else to do would change nothing, so the
    /// button has to be able to tell the two apart. The other parameters are still created.
    /// </summary>
    [Fact]
    public void A_conflict_alone_is_not_work_but_does_not_stop_the_others()
    {
        var definition = Definition(FireProtectionParameters.Provided);
        var plan = FireReviewParameterSetupPlanner.Inspect(new[]
        {
            new FireReviewParameterBindingObservation(definition.Name, Guid.NewGuid(), definition.ValueType,
                definition.Level, definition.Hosts)
        });

        Assert.Single(plan.Conflicts);
        Assert.True(plan.HasWork);
        Assert.Equal(FireReviewParameterCatalog.All.Count - 1, plan.ToCreate.Count);
        Assert.DoesNotContain(plan.ToCreate, x => x.Definition.Name == definition.Name);
    }

    /// <summary>
    /// Two project parameters of the same name: picking one to judge by hides the other, and which one
    /// comes first is Revit's enumeration order, not a decision. Even the matching one is left alone —
    /// the tool cannot know which one the values were typed into.
    /// </summary>
    [Fact]
    public void Two_parameters_of_the_same_name_are_a_conflict_even_when_one_of_them_matches()
    {
        var definition = Definition(FireProtectionParameters.Provided);

        var action = Action(FireReviewParameterSetupPlanner.Inspect(new[]
        {
            Bound(definition),
            new FireReviewParameterBindingObservation(definition.Name, Guid.NewGuid(), definition.ValueType,
                ReviewParameterLevel.Instance, definition.Hosts)
        }), definition.Name);

        Assert.Equal(FireReviewParameterActionKind.Conflict, action.Kind);
        Assert.Contains("2 筆", action.Problem!);
        Assert.Contains("只留一筆", action.Fix!);
    }

    /// <summary>The reader seam the Revit side uses, so the planner is reached the same way in both.</summary>
    [Fact]
    public void A_reader_is_inspected_the_same_way_as_a_list()
    {
        var plan = FireReviewParameterSetupPlanner.Inspect(new StubReader(FireReviewParameterCatalog.All.Select(Bound)));

        Assert.False(plan.HasWork);
    }

    /// <summary>
    /// Extra categories the plug-in does not ask for are somebody else's business — a project may bind
    /// 結構材料 to more than the fire review needs — so they are not reported as anything to fix.
    /// </summary>
    [Fact]
    public void Categories_beyond_what_the_review_needs_are_left_alone()
    {
        var definition = Definition(StructuralMaterialParameters.Material);
        var wider = definition.Hosts.Concat(new[] { ReviewParameterHost.Ceilings });

        var action = Action(FireReviewParameterSetupPlanner.Inspect(new[] { Bound(definition, wider) }), definition.Name);

        Assert.Equal(FireReviewParameterActionKind.AlreadyBound, action.Kind);
    }

    /// <summary>The summary is what the status line shows, so it has to count all four states.</summary>
    [Fact]
    public void The_summary_counts_what_will_happen()
    {
        var plan = FireReviewParameterSetupPlanner.Inspect(Array.Empty<FireReviewParameterBindingObservation>());

        Assert.Contains($"將建立 {FireReviewParameterCatalog.All.Count} 個參數", plan.Summary);
    }

    private static FireReviewParameterDefinition Definition(string name)
    {
        var definition = FireReviewParameterCatalog.For(name);
        Assert.NotNull(definition);
        return definition!;
    }

    private static FireReviewParameterAction Action(FireReviewParameterSetupPlan plan, string name) =>
        Assert.Single(plan.Actions, x => string.Equals(x.Definition.Name, name, StringComparison.Ordinal));

    private static FireReviewParameterBindingObservation Bound(FireReviewParameterDefinition definition) =>
        Bound(definition, definition.Hosts);

    private static FireReviewParameterBindingObservation Bound(
        FireReviewParameterDefinition definition,
        IEnumerable<ReviewParameterHost> hosts) =>
        new FireReviewParameterBindingObservation(definition.Name, definition.Guid, definition.ValueType,
            definition.Level, hosts);

    private sealed class StubReader : IFireReviewParameterBindingReader
    {
        private readonly IReadOnlyList<FireReviewParameterBindingObservation> _observations;

        public StubReader(IEnumerable<FireReviewParameterBindingObservation> observations) =>
            _observations = observations.ToList();

        public IReadOnlyList<FireReviewParameterBindingObservation> Read() => _observations;
    }
}
