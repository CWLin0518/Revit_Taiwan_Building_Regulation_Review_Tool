using System;
using BuildingRegulationReview.Application.WriteBack;
using Xunit;

namespace BuildingRegulationReview.Core.Tests.WriteBack;

/// <summary>
/// The ownership mark on the containers the tool creates once per package — the Drafting View and
/// the Area Color Scheme (spec 10.5 items 3 and 4). Without it, editing a colour scheme would mean
/// editing whatever scheme happened to be there, which is the overwrite spec 18 item 6 forbids.
/// </summary>
public class ManagedOutputTests
{
    private static readonly Guid PackageId = Guid.Parse("11111111-2222-3333-4444-555555555555");
    private static readonly Guid OtherPackageId = Guid.Parse("22222222-3333-4444-5555-666666666666");
    private static readonly Guid ZoneId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
    private const string CurtainWallUniqueId = "7f3a1c9e-0b42-4d18-9c5a-2e6d8f4b1a70-000a1b2c";
    private const string OtherCurtainWallUniqueId = "7f3a1c9e-0b42-4d18-9c5a-2e6d8f4b1a70-000a1b2d";

    [Theory]
    [InlineData(ManagedOutputKind.DraftingView)]
    [InlineData(ManagedOutputKind.ColorFillScheme)]
    [InlineData(ManagedOutputKind.ReviewView)]
    public void ATokenSurvivesTheRoundTrip(ManagedOutputKind kind)
    {
        var key = new ManagedOutputKey(PackageId, kind);

        Assert.True(ManagedOutputKey.TryParse(key.ToToken(), out var parsed));
        Assert.Equal(key, parsed);
    }

    [Fact]
    public void AnElevationIsIdentifiedByTheCurtainWallItLooksAt()
    {
        // 帷幕牆規格 §7.1: a package has one review view but an elevation per curtain wall, so the
        // subject is what keeps two walls' elevations apart when the next run comes looking for them.
        var key = new ManagedOutputKey(PackageId, ManagedOutputKind.CurtainWallElevation, CurtainWallUniqueId);

        Assert.True(ManagedOutputKey.TryParse(key.ToToken(), out var parsed));
        Assert.Equal(key, parsed);
        Assert.Equal(CurtainWallUniqueId, parsed.Subject);
        Assert.NotEqual(key, new ManagedOutputKey(PackageId, ManagedOutputKind.CurtainWallElevation, OtherCurtainWallUniqueId));
    }

    [Fact]
    public void TheContainersThereIsOneOfPerPackageKeepTheTokenTheyAlwaysHad()
    {
        // An output already marked in a model has a three-part token; adding a fourth part must not
        // stop it parsing, or the tool would make a second Drafting View beside the one it owns.
        Assert.True(ManagedOutputKey.TryParse("BCROUT/11111111222233334444555555555555/draftingview", out var parsed));
        Assert.Equal(ManagedOutputKind.DraftingView, parsed.Kind);
        Assert.Equal(string.Empty, parsed.Subject);
    }

    [Fact]
    public void AnElevationWithoutACurtainWallIsNotAKeyAtAll()
    {
        Assert.Throws<ArgumentException>(() => new ManagedOutputKey(PackageId, ManagedOutputKind.CurtainWallElevation));
        Assert.Throws<ArgumentException>(() => new ManagedOutputKey(PackageId, ManagedOutputKind.CurtainWallElevation, "  "));
        Assert.False(ManagedOutputKey.TryParse("BCROUT/11111111222233334444555555555555/curtainwallelevation", out _));
    }

    [Fact]
    public void AContainerThereIsOnlyOneOfCannotBeGivenASubject()
    {
        // Otherwise two keys could both claim to be the package's one colour scheme.
        Assert.Throws<ArgumentException>(() => new ManagedOutputKey(PackageId, ManagedOutputKind.ColorFillScheme, "something"));
        Assert.False(ManagedOutputKey.TryParse("BCROUT/11111111222233334444555555555555/colorscheme/something", out _));
    }

    [Fact]
    public void ASubjectCannotCarryTheSeparatorThatWouldSplitTheTokenApart()
    {
        Assert.Throws<ArgumentException>(() =>
            new ManagedOutputKey(PackageId, ManagedOutputKind.CurtainWallElevation, "a/b"));
    }

    [Fact]
    public void OwnershipReadsAnElevationTooSoItsViewCanBeTakenOver()
    {
        var elevation = new ManagedOutputKey(PackageId, ManagedOutputKind.CurtainWallElevation, CurtainWallUniqueId).ToToken();

        Assert.True(ManagedOwnership.BelongsTo(elevation, PackageId));
        Assert.False(ManagedOwnership.BelongsTo(elevation, OtherPackageId));
    }

    [Fact]
    public void AContainerTokenIsNotReadAsAZoneElement()
    {
        // The two shapes have to stay apart: a zone element always has a 區劃 and a container never
        // does, so a token read as the wrong shape would give an element an invented zone.
        var token = new ManagedOutputKey(PackageId, ManagedOutputKind.DraftingView).ToToken();

        Assert.False(ManagedElementKey.TryParse(token, out _));
    }

    [Fact]
    public void AZoneElementTokenIsNotReadAsAContainer()
    {
        var token = new ManagedElementKey(PackageId, ZoneId, ManagedElementKind.Area, 0, 0).ToToken();

        Assert.False(ManagedOutputKey.TryParse(token, out _));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("BCROUT/not-a-guid/draftingview")]
    [InlineData("BCROUT/11111111222233334444555555555555/somethingelse")]
    [InlineData("OTHER/11111111222233334444555555555555/draftingview")]
    [InlineData("人工繪製")]
    public void ATokenThatIsNotOursDoesNotParse(string? token)
    {
        Assert.False(ManagedOutputKey.TryParse(token, out _));
    }

    [Fact]
    public void OwnershipReadsEitherShape()
    {
        var element = new ManagedElementKey(PackageId, ZoneId, ManagedElementKind.DetailCurve, 0, 3).ToToken();
        var container = new ManagedOutputKey(PackageId, ManagedOutputKind.ColorFillScheme).ToToken();

        Assert.True(ManagedOwnership.BelongsTo(element, PackageId));
        Assert.True(ManagedOwnership.BelongsTo(container, PackageId));
    }

    [Fact]
    public void OwnershipStopsAtThePackageBoundary()
    {
        var container = new ManagedOutputKey(OtherPackageId, ManagedOutputKind.ColorFillScheme).ToToken();

        Assert.False(ManagedOwnership.BelongsTo(container, PackageId));
    }

    [Fact]
    public void NothingUnreadableBelongsToAnybody()
    {
        Assert.False(ManagedOwnership.BelongsTo("人工繪製", PackageId));
        Assert.False(ManagedOwnership.TryReadPackageId("人工繪製", out _));
    }

    [Fact]
    public void AnEmptyPackageIdOwnsNothingEvenIfTheTokenReads()
    {
        // Guid.Empty is what a caller with no package looks like; it must not match anything.
        var container = new ManagedOutputKey(PackageId, ManagedOutputKind.DraftingView).ToToken();

        Assert.False(ManagedOwnership.BelongsTo(container, Guid.Empty));
    }

    [Fact]
    public void RejectsAKeyWithNoPackage()
    {
        Assert.Throws<ArgumentException>(() => new ManagedOutputKey(Guid.Empty, ManagedOutputKind.DraftingView));
    }

    [Theory]
    [InlineData(ManagedOutputKind.DraftingView, "單線圖視圖")]
    [InlineData(ManagedOutputKind.ColorFillScheme, "面積色彩配置")]
    [InlineData(ManagedOutputKind.ReviewView, "防火檢討視圖")]
    [InlineData(ManagedOutputKind.CurtainWallElevation, "帷幕牆檢討立面")]
    public void EachKindHasANameTheUserWouldRecognise(ManagedOutputKind kind, string expected)
    {
        Assert.Equal(expected, ManagedOutputKey.Describe(kind));
    }
}
