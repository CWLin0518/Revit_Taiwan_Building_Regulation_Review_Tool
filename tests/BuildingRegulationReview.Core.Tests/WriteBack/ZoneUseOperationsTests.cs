using System;
using System.Collections.Generic;
using System.Linq;
using BuildingRegulationReview.Application.Geometry;
using BuildingRegulationReview.Application.WriteBack;
using BuildingRegulationReview.Domain.Common;
using BuildingRegulationReview.Domain.Geometry;
using BuildingRegulationReview.Domain.Regions;
using Xunit;

namespace BuildingRegulationReview.Core.Tests.WriteBack;

/// <summary>
/// 防火檢討_區劃用途 on the way into the model (任務 3). A 用途 change alters no geometry and no
/// signature, so the Area carrying it is 不變 as far as the element comparison goes — these tests
/// pin the separate plan that makes such a run happen at all, and the cases where it must
/// deliberately write nothing.
/// </summary>
public class ZoneUseOperationsTests
{
    private static readonly Guid PackageId = Guid.Parse("11111111-2222-3333-4444-555555555555");
    private static readonly Guid ZoneId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
    private static readonly GeometryTolerance Tolerance = GeometryTolerance.Default;
    private static readonly DateTime SolvedAt = new DateTime(2026, 10, 6, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void ChangingOnlyTheUseIsAPlanThatRunsAlthoughNoElementChanges()
    {
        var (map, zones) = OneRoom("管道間");
        var model = AsWritten(map, OneRoom(null).Zones, currentUse: string.Empty);

        var preview = ApplyPreview.Build(PackageId, map, zones, model);

        // Every element matches; the whole change is the one parameter.
        Assert.Empty(preview.Added);
        Assert.Empty(preview.Updated);
        Assert.Empty(preview.Deleted);
        Assert.False(preview.IsEmpty);

        var operation = Assert.Single(preview.ZoneUses);
        Assert.Equal("管道間", operation.TargetUse);
        Assert.Equal(string.Empty, operation.CurrentUse);
        Assert.False(ApplyPlan.Build(preview, ApplyPlan.AllKinds).IsEmpty);
    }

    [Fact]
    public void ADraftThatSaysNothingAboutItsUseWritesNothing()
    {
        var (map, zones) = OneRoom(null);
        var model = AsWritten(map, zones, currentUse: "管道間");

        var preview = ApplyPreview.Build(PackageId, map, zones, model);

        // The case the whole tri-state exists for: a zone restored from Areas that disagreed, or
        // from a model with no such parameter, must not take 管道間 away from the 批次設定面板.
        Assert.Empty(preview.ZoneUses);
        Assert.True(preview.IsEmpty);
    }

    [Fact]
    public void AnAreaThatAlreadyCarriesTheTargetUseIsNotRewritten()
    {
        var (map, zones) = OneRoom("挑空");
        var model = AsWritten(map, zones, currentUse: "挑空");

        var preview = ApplyPreview.Build(PackageId, map, zones, model);

        Assert.Empty(preview.ZoneUses);
        Assert.True(preview.IsEmpty);
    }

    [Fact]
    public void ClearingIsAnOperationWhenTheAreaHasAUseToLose()
    {
        var (map, zones) = OneRoom(string.Empty);
        var model = AsWritten(map, zones, currentUse: "樓梯間");

        var operation = Assert.Single(ApplyPreview.Build(PackageId, map, zones, model).ZoneUses);

        Assert.True(operation.IsClear);
        Assert.Equal("樓梯間", operation.CurrentUse);
        Assert.Contains("樓梯間", operation.Text);
    }

    [Fact]
    public void ClearingAnAreaWhoseParameterCannotBeReadIsNotAFailedWriteWaitingToHappen()
    {
        var (map, zones) = OneRoom(string.Empty);
        var model = AsWritten(map, zones, currentUse: null);

        // Nothing to clear on a parameter that is not bound; reporting it as a refused write would
        // blame the user for a field they never filled.
        Assert.Empty(ApplyPreview.Build(PackageId, map, zones, model).ZoneUses);
    }

    [Fact]
    public void SettingAUseOnAnAreaWhoseParameterCannotBeReadIsStillAttempted()
    {
        var (map, zones) = OneRoom("昇降機道");
        var model = AsWritten(map, zones, currentUse: null);

        // The user asked for it outright, so the run tries and reports what Revit said, rather than
        // silently dropping it and calling the apply complete.
        var operation = Assert.Single(ApplyPreview.Build(PackageId, map, zones, model).ZoneUses);
        Assert.Equal("昇降機道", operation.TargetUse);
        Assert.Null(operation.CurrentUse);
    }

    [Fact]
    public void AnAreaThisRunCreatesGetsItsUseWritten()
    {
        var (map, zones) = OneRoom("管道間");

        var operation = Assert.Single(ApplyPreview.Build(PackageId, map, zones).ZoneUses);

        Assert.Null(operation.ElementUniqueId);
        Assert.Equal("管道間", operation.TargetUse);
    }

    [Fact]
    public void AnAreaThisRunCreatesAsAGeneralZoneNeedsNoWrite()
    {
        var (map, zones) = OneRoom(string.Empty);

        // A new Area starts blank, so clearing it is work for nothing.
        Assert.Empty(ApplyPreview.Build(PackageId, map, zones).ZoneUses);
    }

    [Fact]
    public void EachPartOfADisjointZoneGetsItsOwnOperation()
    {
        // Two rooms that do not touch, so the write-back makes one Revit Area for each.
        var map = Solve(Rectangle(0, 0, 10, 10, "LEFT").Concat(Rectangle(40, 0, 10, 10, "RIGHT")));
        var zones = Succeeds(ZoneDraftSet.Empty.Add(new ZoneDraft(
            ZoneId,
            "A",
            ZoneColorPalette.At(0),
            new[] { FaceAt(map, 5, 5), FaceAt(map, 45, 5) },
            allowsDisjointParts: true,
            use: "管道間")));

        Assert.Equal(2, ApplyPreview.Build(PackageId, map, zones).ZoneUses.Count);
    }

    [Fact]
    public void TheSummarySaysWhatAUseOnlyRunWillDo()
    {
        var (map, zones) = OneRoom("管道間");
        var model = AsWritten(map, OneRoom(null).Zones, currentUse: string.Empty);

        var summary = ApplyPreview.Build(PackageId, map, zones, model).Summary;

        Assert.Contains("元素全部不變", summary);
        Assert.Contains("區劃用途", summary);
    }

    [Fact]
    public void SettingAUseSurvivesEveryWayADraftIsCopied()
    {
        var zone = new ZoneDraft(ZoneId, "A", ZoneColorPalette.At(0), new[] { 1, 2 }, use: "管道間");

        Assert.Equal("管道間", zone.WithName("B").Use);
        Assert.Equal("管道間", zone.WithColor(ZoneColorPalette.At(1)).Use);
        Assert.Equal("管道間", zone.WithFaces(new[] { 3 }).Use);
        Assert.Equal("管道間", zone.WithDisjointAllowed(true).Use);
        Assert.Equal("管道間", zone.Including(9).Use);
        Assert.Equal("管道間", zone.Excluding(1).Use);
    }

    [Fact]
    public void NotChangingAUseAndClearingItAreDifferentSignatures()
    {
        var kept = new ZoneDraft(ZoneId, "A", ZoneColorPalette.At(0), new[] { 1 }, use: null);
        var cleared = kept.WithUse(string.Empty);

        // Otherwise Undo would not notice the user asking for a 一般區劃.
        Assert.NotEqual(kept.Signature(), cleared.Signature());
        Assert.False(kept.HasUse);
        Assert.True(cleared.HasUse);
    }

    // ---- helpers ---------------------------------------------------------------------------------

    private static (PlanRegionMap Map, ZoneDraftSet Zones) OneRoom(string? use)
    {
        var map = Solve(Rectangle(0, 0, 10, 10, "OUTER"));
        var zones = Succeeds(ZoneDraftSet.Empty.Add(new ZoneDraft(
            ZoneId,
            "A",
            ZoneColorPalette.At(0),
            new[] { FaceAt(map, 5, 5) },
            use: use)));
        return (map, zones);
    }

    /// <summary>The model as an earlier run left it: every planned element, with the Areas' current use.</summary>
    private static IReadOnlyList<ExistingManagedElement> AsWritten(PlanRegionMap map, ZoneDraftSet zones, string? currentUse) =>
        ZoneWritePlan.Build(PackageId, map, zones, Tolerance)
            .Select(planned => new ExistingManagedElement(
                "uid-" + planned.Key.ToToken(),
                planned.Key.ToToken(),
                planned.Signature,
                planned.Description,
                planned.Key.Kind == ManagedElementKind.Area ? currentUse : null))
            .ToList();

    private static PlanRegionMap Solve(IEnumerable<Segment2D> segments)
    {
        var snapshot = new PlanGeometrySnapshot(PackageId, "host-doc", "level-1", segments, Tolerance);
        var network = Succeeds(new LineNetworkRepairer().Repair(snapshot, SolvedAt));
        return Succeeds(new RegionSolver().Solve(network, SolvedAt));
    }

    private static int FaceAt(PlanRegionMap map, double x, double y) => map.FaceAt(new Point2D(x, y))!.Id;

    private static Segment2D[] Rectangle(double x, double y, double width, double height, string prefix) => new[]
    {
        Seg(x, y, x + width, y, prefix + "-S"),
        Seg(x + width, y, x + width, y + height, prefix + "-E"),
        Seg(x + width, y + height, x, y + height, prefix + "-N"),
        Seg(x, y + height, x, y, prefix + "-W")
    };

    private static Segment2D Seg(double x1, double y1, double x2, double y2, string element) =>
        new Segment2D(new Point2D(x1, y1), new Point2D(x2, y2), new SourceRef("doc", element, GeometrySourceKind.WallCenterline));

    private static T Succeeds<T>(Result<T> result)
    {
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.ToString() : string.Empty);
        return result.Value;
    }
}
