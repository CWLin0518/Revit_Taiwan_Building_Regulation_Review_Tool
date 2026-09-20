using System;
using System.Collections.Generic;
using BuildingRegulationReview.Application.WriteBack;
using Xunit;

namespace BuildingRegulationReview.Core.Tests.WriteBack;

/// <summary>
/// Spec 10.5 item 5: the Drafting View is called <c>{AreaScheme}_{SourceFloorPlan}_防火區劃</c> by
/// default, and a name is only ever the starting point — identity is the UniqueId on the package.
/// </summary>
public class ReviewOutputNamingTests
{
    [Fact]
    public void UsesTheSchemeThePlanAndTheSuffixInThatOrder()
    {
        Assert.Equal("防火區劃面積_1F 平面圖_防火區劃", ReviewOutputNaming.Default("防火區劃面積", "1F 平面圖"));
    }

    [Fact]
    public void TrimsEachPartSoASloppyViewNameDoesNotLeaveDoubleSpaces()
    {
        Assert.Equal("面積_1F_防火區劃", ReviewOutputNaming.Default("  面積 ", " 1F "));
    }

    [Fact]
    public void ReplacesTheSeparatorInsideAPartSoTheNameStillReadsAsThreePieces()
    {
        // A floor plan really can be called "1F_東棟"; left alone the name would look like it had
        // four parts and nobody could tell where the plan's name ended.
        Assert.Equal("面積_1F-東棟_防火區劃", ReviewOutputNaming.Default("面積", "1F_東棟"));
    }

    [Theory]
    [InlineData("1F|東棟", "面積_1F-東棟_防火區劃")]
    [InlineData("1F:東棟", "面積_1F-東棟_防火區劃")]
    [InlineData("{1F}", "面積_-1F-_防火區劃")]
    public void ReplacesCharactersRevitRefusesInAViewName(string plan, string expected)
    {
        Assert.Equal(expected, ReviewOutputNaming.Default("面積", plan));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void NamesTheMissingPartRatherThanCollapsingTheName(string? missing)
    {
        Assert.Equal("未命名_1F_防火區劃", ReviewOutputNaming.Default(missing, "1F"));
    }

    [Fact]
    public void KeepsTheNameWhenNothingElseHasIt()
    {
        Assert.Equal("面積_1F_防火區劃", ReviewOutputNaming.MakeUnique("面積_1F_防火區劃", _ => false));
    }

    [Fact]
    public void CountsUpUntilTheNameIsFree()
    {
        var taken = new HashSet<string>(new[] { "單線圖", "單線圖 (2)" }, StringComparer.Ordinal);

        Assert.Equal("單線圖 (3)", ReviewOutputNaming.MakeUnique("單線圖", taken.Contains));
    }

    [Fact]
    public void ANameThatCanNeverBeFreeStillReturnsSomethingUsable()
    {
        // Better an ugly name than a loop that never ends inside a Revit transaction.
        var name = ReviewOutputNaming.MakeUnique("單線圖", _ => true);

        Assert.StartsWith("單線圖 (", name, StringComparison.Ordinal);
        Assert.NotEqual("單線圖", name);
    }

    [Fact]
    public void RejectsABaseNameThatIsNotOne()
    {
        Assert.Throws<ArgumentException>(() => ReviewOutputNaming.MakeUnique("  ", _ => false));
    }
}
