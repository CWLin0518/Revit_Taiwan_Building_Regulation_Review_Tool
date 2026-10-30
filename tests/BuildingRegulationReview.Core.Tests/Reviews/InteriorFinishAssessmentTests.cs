using BuildingRegulationReview.Application.Parameters;
using BuildingRegulationReview.Application.Reviews;
using Xunit;

namespace BuildingRegulationReview.Core.Tests.Reviews;

public sealed class InteriorFinishAssessmentTests
{
    [Fact]
    public void No_modelled_surfaces_is_insufficient_data() =>
        Assert.Null(InteriorFinishAssessment.Derive(null));

    [Fact]
    public void One_missing_or_unrecognised_type_value_is_insufficient_data()
    {
        Assert.Null(InteriorFinishAssessment.Derive(new[]
        {
            Surface("wall", InteriorFinishGrades.ClassOne),
            Surface("ceiling", null)
        }));
        Assert.Null(InteriorFinishAssessment.Derive(new[] { Surface("wall", "耐燃二級") }));
    }

    [Fact]
    public void The_weakest_wall_or_ceiling_system_governs_the_zone()
    {
        Assert.Equal(InteriorFinishGrades.None, InteriorFinishAssessment.Derive(new[]
        {
            Surface("wall", InteriorFinishGrades.ClassOneWithSubstrate),
            Surface("ceiling", InteriorFinishGrades.None)
        }));
        Assert.Equal(InteriorFinishGrades.ClassOne, InteriorFinishAssessment.Derive(new[]
        {
            Surface("wall", InteriorFinishGrades.ClassOneWithSubstrate),
            Surface("ceiling", InteriorFinishGrades.ClassOne)
        }));
    }

    [Fact]
    public void Substrate_grade_requires_every_surface_system_to_include_the_substrate()
    {
        Assert.Equal(InteriorFinishGrades.ClassOneWithSubstrate, InteriorFinishAssessment.Derive(new[]
        {
            Surface("wall-1", InteriorFinishGrades.ClassOneWithSubstrate),
            Surface("wall-2", InteriorFinishGrades.ClassOneWithSubstrate),
            Surface("ceiling", InteriorFinishGrades.ClassOneWithSubstrate)
        }));
    }

    private static InteriorFinishSurface Surface(string id, string? grade) =>
        new(id, id.StartsWith("ceiling") ? "天花板" : "牆", grade);
}
