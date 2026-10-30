using System;
using System.Collections.Generic;
using BuildingRegulationReview.Application.ProjectSetup;
using BuildingRegulationReview.Domain.ReviewPackages;
using Xunit;

namespace BuildingRegulationReview.Core.Tests.ProjectSetup;

/// <summary>
/// 驗證清單 C-13：修復一個被刪掉的 Area Plan 時，設定視窗要帶回套件原本存的樣板與 Scope Box。
/// 這裡測的是視窗讀回那份設定的那一段接線（視窗本身在 net48 WPF 專案，無法自動化）。
/// </summary>
public sealed class ReviewPackageSetupHistoryTests
{
    private static ReviewPackage Package(string floor, string scheme, Guid? id = null) =>
        new ReviewPackage(id ?? Guid.NewGuid(), floor, "level-" + floor, scheme);

    private static ReviewPackageSetupSelection Saved(ReviewPackage package, string? template, bool copyCrop, string? scopeBox) =>
        new ReviewPackageSetupSelection(package.SourceFloorPlanUniqueId, package.LevelUniqueId,
            package.AreaSchemeUniqueId, template, copyCrop, scopeBox);

    [Fact]
    public void Find_returns_what_was_saved_for_the_floor_plan_and_scheme_pair()
    {
        var package = Package("floor-1", "scheme-A");
        var history = ReviewPackageSetupHistory.From(
            new[] { package },
            _ => Saved(package, "template-7", copyCrop: false, scopeBox: "scope-3"));

        var found = history.Find("floor-1", "scheme-A");

        Assert.NotNull(found);
        Assert.Equal("template-7", found!.AreaPlanTemplateUniqueId);
        Assert.Equal("scope-3", found.ScopeBoxUniqueId);
        Assert.False(found.CopyCropSettings);
    }

    [Fact]
    public void Find_returns_null_when_the_package_never_saved_its_options()
    {
        var history = ReviewPackageSetupHistory.From(new[] { Package("floor-1", "scheme-A") }, _ => null);

        Assert.Null(history.Find("floor-1", "scheme-A"));
    }

    [Fact]
    public void Find_tells_the_pairs_apart_by_both_halves_of_the_key()
    {
        var first = Package("floor-1", "scheme-A");
        var second = Package("floor-2", "scheme-B");
        var saved = new Dictionary<Guid, ReviewPackageSetupSelection>
        {
            [first.PackageId] = Saved(first, "template-first", copyCrop: true, scopeBox: null),
            [second.PackageId] = Saved(second, "template-second", copyCrop: true, scopeBox: null)
        };
        var history = ReviewPackageSetupHistory.From(new[] { first, second }, package => saved[package.PackageId]);

        Assert.Equal("template-first", history.Find("floor-1", "scheme-A")!.AreaPlanTemplateUniqueId);
        Assert.Equal("template-second", history.Find("floor-2", "scheme-B")!.AreaPlanTemplateUniqueId);
        // 同一個樓層平面換一個面積配置是另一個套件，不能拿別人的設定來預選。
        Assert.Null(history.Find("floor-1", "scheme-B"));
        Assert.Null(history.Find("floor-2", "scheme-A"));
    }

    [Fact]
    public void Find_returns_null_for_a_pair_no_package_covers()
    {
        var package = Package("floor-1", "scheme-A");
        var history = ReviewPackageSetupHistory.From(
            new[] { package },
            _ => Saved(package, "template-7", copyCrop: true, scopeBox: null));

        Assert.Null(history.Find("floor-9", "scheme-A"));
    }

    [Theory]
    [InlineData(null, "scheme-A")]
    [InlineData("floor-1", null)]
    [InlineData("", "scheme-A")]
    [InlineData("floor-1", "  ")]
    public void Find_returns_null_when_either_half_of_the_key_is_missing(string? floor, string? scheme)
    {
        var package = Package("floor-1", "scheme-A");
        var history = ReviewPackageSetupHistory.From(
            new[] { package },
            _ => Saved(package, "template-7", copyCrop: true, scopeBox: null));

        Assert.Null(history.Find(floor, scheme));
    }

    [Fact]
    public void Find_takes_the_first_of_a_duplicated_pair()
    {
        var first = Package("floor-1", "scheme-A");
        var second = Package("floor-1", "scheme-A");
        var saved = new Dictionary<Guid, ReviewPackageSetupSelection>
        {
            [first.PackageId] = Saved(first, "template-first", copyCrop: true, scopeBox: null),
            [second.PackageId] = Saved(second, "template-second", copyCrop: true, scopeBox: null)
        };
        var history = ReviewPackageSetupHistory.From(new[] { first, second }, package => saved[package.PackageId]);

        Assert.Equal("template-first", history.Find("floor-1", "scheme-A")!.AreaPlanTemplateUniqueId);
    }

    [Fact]
    public void From_skips_a_null_package_instead_of_asking_for_its_options()
    {
        // 真正的 savedOptions 是 RevitReviewSetupOptionsRepository.Get，它對 null 會丟
        // ArgumentNullException；這裡用同樣會丟的 selector 釘住「不會把 null 餵給它」。
        var package = Package("floor-1", "scheme-A");
        var history = ReviewPackageSetupHistory.From(
            new ReviewPackage[] { null!, package },
            asked => asked is null
                ? throw new ArgumentNullException(nameof(asked))
                : Saved(package, "template-7", copyCrop: true, scopeBox: null));

        Assert.Equal("template-7", history.Find("floor-1", "scheme-A")!.AreaPlanTemplateUniqueId);
    }

    [Fact]
    public void Empty_finds_nothing()
    {
        Assert.Null(ReviewPackageSetupHistory.Empty.Find("floor-1", "scheme-A"));
    }

    [Fact]
    public void From_rejects_missing_arguments()
    {
        // 參數名一起斷言：少了自己的檢查時 LINQ 也會丟 ArgumentNullException，但丟的是
        // source／selector，測不出這裡真的攔過。
        Assert.Equal("packages",
            Assert.Throws<ArgumentNullException>(() => ReviewPackageSetupHistory.From(null!, _ => null)).ParamName);
        Assert.Equal("savedOptions",
            Assert.Throws<ArgumentNullException>(() => ReviewPackageSetupHistory.From(Array.Empty<ReviewPackage>(), null!)).ParamName);
    }
}
