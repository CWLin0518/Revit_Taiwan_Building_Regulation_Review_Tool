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
/// Spec 10.5 item 3: the Area Color Scheme is updated or created to match the 區劃, and whatever the
/// API will not do is reported as a manual item instead of being half-done or guessed at.
/// </summary>
public class ColorSchemeEntriesTests
{
    [Fact]
    public void OneEntryPerZoneInNameOrder()
    {
        var (map, zones) = TwoZones();

        var entries = ColorSchemeEntries.From(Preview(map, zones));

        Assert.Equal(new[] { "乙區", "甲區" }, entries.Entries.Select(entry => entry.Value));
    }

    [Fact]
    public void TheColourIsTheOneTheEditorDraws()
    {
        var (map, zones) = TwoZones();

        var entries = ColorSchemeEntries.From(Preview(map, zones));

        Assert.Equal(ZoneColorPalette.At(1), Entry(entries, "乙區").Color);
        Assert.Equal(ZoneColorPalette.At(0), Entry(entries, "甲區").Color);
    }

    [Fact]
    public void AZoneWithNoRangesGetsNoEntry()
    {
        var map = Solve(Rectangle(0, 0, 20, 10, "OUTER").Concat(new[] { Seg(10, 0, 10, 10, "DIVIDER") }));
        var zones = Succeeds(Assign(map, "甲區", ZoneColorPalette.At(0), FaceAt(map, 5, 5))
            .Add(new ZoneDraft(Guid.NewGuid(), "空區", ZoneColorPalette.At(3))));

        var entries = ColorSchemeEntries.From(Preview(map, zones));

        // An empty zone writes no Area, so colouring its name would put a row in the legend for
        // something that is not on the plan.
        Assert.Equal(new[] { "甲區" }, entries.Entries.Select(entry => entry.Value));
    }

    [Fact]
    public void AZoneInSeveralPiecesStillGetsOneEntry()
    {
        // Two rooms that do not touch, both in one 區劃: two Areas, one name, one colour.
        var map = Solve(Rectangle(0, 0, 10, 10, "A").Concat(Rectangle(20, 0, 10, 10, "B")));
        var zones = Succeeds(ZoneDraftSet.Empty.Add(new ZoneDraft(
            Guid.NewGuid(),
            "甲區",
            ZoneColorPalette.At(0),
            new[] { FaceAt(map, 5, 5), FaceAt(map, 25, 5) },
            allowsDisjointParts: true)));

        var entries = ColorSchemeEntries.From(Preview(map, zones));

        Assert.Single(entries.Entries);
        Assert.Equal("甲區", entries.Entries[0].Value);
    }

    [Fact]
    public void TwoZonesCannotShareANameSoAnEntryCanNeverBeAmbiguous()
    {
        var map = Solve(Rectangle(0, 0, 20, 10, "OUTER").Concat(new[] { Seg(10, 0, 10, 10, "DIVIDER") }));
        var zones = Assign(map, "同名", ZoneColorPalette.At(0), FaceAt(map, 5, 5));

        // The scheme keys on the 區劃 name, so two zones of that name in different colours would
        // have no right answer. The rule lives in ZoneDraftSet, where it can be said to the user
        // while they are typing rather than surfacing as a colour nobody asked for.
        var second = zones.Add(new ZoneDraft(Guid.NewGuid(), "同名", ZoneColorPalette.At(4), new[] { FaceAt(map, 15, 5) }));

        Assert.True(second.IsFailure);
        Assert.Equal("regions.zone.duplicateName", second.Error.Code);
    }

    [Fact]
    public void ADeletedZoneContributesNoColour()
    {
        var (map, zones) = TwoZones();
        var written = Written(map, zones);

        var entries = ColorSchemeEntries.From(ApplyPreview.Build(PackageId, map, ZoneDraftSet.Empty, written));

        Assert.True(entries.IsEmpty);
    }

    // ---- helpers ------------------------------------------------------------------------------

    internal static readonly Guid PackageId = Guid.Parse("11111111-2222-3333-4444-555555555555");
    private static readonly GeometryTolerance Tolerance = GeometryTolerance.Default;
    private static readonly DateTime SolvedAt = new DateTime(2026, 9, 21, 0, 0, 0, DateTimeKind.Utc);

    private static ColorSchemeEntryPlan Entry(ColorSchemeEntries entries, string value) =>
        entries.Entries.Single(entry => entry.Value == value);

    internal static ApplyPreview Preview(PlanRegionMap map, ZoneDraftSet zones) =>
        ApplyPreview.Build(PackageId, map, zones);

    /// <summary>The model after one run of these drafts, so a second preview reports 不變.</summary>
    internal static IReadOnlyList<ExistingManagedElement> Written(PlanRegionMap map, ZoneDraftSet zones)
    {
        var id = 0;
        return ZoneWritePlan.Build(PackageId, map, zones)
            .Select(element => new ExistingManagedElement(
                "element-" + id++,
                element.Key.ToToken(),
                element.Signature,
                element.Description))
            .ToList();
    }

    internal static (PlanRegionMap Map, ZoneDraftSet Zones) TwoZones()
    {
        var map = Solve(Rectangle(0, 0, 20, 10, "OUTER").Concat(new[] { Seg(10, 0, 10, 10, "DIVIDER") }));
        var zones = Succeeds(Assign(map, "甲區", ZoneColorPalette.At(0), FaceAt(map, 5, 5))
            .Add(new ZoneDraft(Guid.NewGuid(), "乙區", ZoneColorPalette.At(1), new[] { FaceAt(map, 15, 5) })));
        return (map, zones);
    }

    private static ZoneDraftSet Assign(PlanRegionMap map, string name, ZoneColor color, params int[] faceIds) =>
        Succeeds(ZoneDraftSet.Empty.Add(new ZoneDraft(Guid.NewGuid(), name, color, faceIds)));

    internal static PlanRegionMap Solve(IEnumerable<Segment2D> segments)
    {
        var snapshot = new PlanGeometrySnapshot(PackageId, "host-doc", "level-1", segments, Tolerance);
        var network = Succeeds(new LineNetworkRepairer().Repair(snapshot, SolvedAt));
        return Succeeds(new RegionSolver().Solve(network, SolvedAt));
    }

    internal static int FaceAt(PlanRegionMap map, double x, double y) => map.FaceAt(new Point2D(x, y))!.Id;

    internal static Segment2D[] Rectangle(double x, double y, double width, double height, string prefix) => new[]
    {
        Seg(x, y, x + width, y, prefix + "-S"),
        Seg(x + width, y, x + width, y + height, prefix + "-E"),
        Seg(x + width, y + height, x, y + height, prefix + "-N"),
        Seg(x, y + height, x, y, prefix + "-W")
    };

    internal static Segment2D Seg(double x1, double y1, double x2, double y2, string element) =>
        new Segment2D(new Point2D(x1, y1), new Point2D(x2, y2), new SourceRef("doc", element, GeometrySourceKind.WallCenterline));

    internal static T Succeeds<T>(Result<T> result)
    {
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.ToString() : string.Empty);
        return result.Value;
    }
}

/// <summary>
/// The other half of spec 10.5 item 3: what actually changes in a scheme that is already there, and
/// what the tool refuses to touch.
/// </summary>
public class ColorSchemePlanTests
{
    [Fact]
    public void AnUntouchedSchemeGetsEveryEntryAsAnAddition()
    {
        var plan = ColorSchemePlan.Build(Entries());

        Assert.Equal(2, plan.CountOf(ColorSchemeChange.Add));
        Assert.False(plan.IsEmpty);
    }

    [Fact]
    public void ASecondRunChangesNothing()
    {
        var entries = Entries();
        var plan = ColorSchemePlan.Build(entries, Mirror(entries));

        Assert.True(plan.IsEmpty);
        Assert.Equal(2, plan.CountOf(ColorSchemeChange.Unchanged));
        Assert.Equal("面積色彩配置已經與區劃一致，不需要變更。", plan.Summary);
    }

    [Fact]
    public void ARecolouredZoneIsUpdatedNotAddedAgain()
    {
        var entries = Entries();
        var current = Mirror(entries)
            .Select(entry => entry.Value == "甲區"
                ? new ExistingColorSchemeEntry("甲區", ZoneColor.FromHex("#000000"))
                : entry)
            .ToList();

        var plan = ColorSchemePlan.Build(entries, current);

        var step = Assert.Single(plan.StepsOf(ColorSchemeChange.Update));
        Assert.Equal("甲區", step.Value);
        Assert.Equal(ZoneColorPalette.At(0), step.Color);
        Assert.Equal(0, plan.CountOf(ColorSchemeChange.Add));
    }

    [Fact]
    public void AnEntryWhoseColourCouldNotBeReadIsRewrittenRatherThanComparedAgainstAGuess()
    {
        var entries = Entries();
        var current = Mirror(entries)
            .Select(entry => new ExistingColorSchemeEntry(entry.Value, entry.Color, false, isColorKnown: false))
            .ToList();

        var plan = ColorSchemePlan.Build(entries, current);

        Assert.Equal(2, plan.CountOf(ColorSchemeChange.Update));
    }

    [Fact]
    public void AnEntryWithNoZoneLeftAndNobodyUsingItIsRemoved()
    {
        var entries = Entries();
        var current = Mirror(entries)
            .Append(new ExistingColorSchemeEntry("舊區劃", ZoneColor.FromHex("#123456")))
            .ToList();

        var plan = ColorSchemePlan.Build(entries, current);

        var step = Assert.Single(plan.StepsOf(ColorSchemeChange.Remove));
        Assert.Equal("舊區劃", step.Value);
        Assert.Empty(plan.ManualActions);
    }

    [Fact]
    public void AnEntryStillInUseIsReportedInsteadOfRemoved()
    {
        var entries = Entries();
        var current = Mirror(entries)
            .Append(new ExistingColorSchemeEntry("別人的面積", ZoneColor.FromHex("#123456"), isInUse: true))
            .ToList();

        var plan = ColorSchemePlan.Build(entries, current);

        // Revit refuses to remove it, and an entry in use is usually somebody else's Area — so the
        // run says what it is and what to do, and removes nothing.
        Assert.Empty(plan.StepsOf(ColorSchemeChange.Remove));
        var action = Assert.Single(plan.ManualActions);
        Assert.Contains("別人的面積", action.Subject, StringComparison.Ordinal);
    }

    [Fact]
    public void ClearingEveryDraftRemovesThisPackagesEntriesAndStopsThere()
    {
        var current = Mirror(Entries())
            .Append(new ExistingColorSchemeEntry("別人的面積", ZoneColor.FromHex("#123456"), isInUse: true))
            .ToList();

        var plan = ColorSchemePlan.Build(ColorSchemeEntries.None, current);

        Assert.Equal(new[] { "乙區", "甲區" }, plan.StepsOf(ColorSchemeChange.Remove).Select(step => step.Value));
        Assert.Single(plan.ManualActions);
    }

    [Fact]
    public void TheSummaryCountsEveryKindOfChange()
    {
        var entries = Entries();
        var current = new[]
        {
            new ExistingColorSchemeEntry("甲區", ZoneColorPalette.At(0)),
            new ExistingColorSchemeEntry("乙區", ZoneColor.FromHex("#000000")),
            new ExistingColorSchemeEntry("舊區劃", ZoneColor.FromHex("#123456"))
        };

        var plan = ColorSchemePlan.Build(entries, current);

        Assert.Equal("面積色彩配置新增 0 項、更新 1 項、刪除 1 項，不變 1 項。", plan.Summary);
    }

    [Fact]
    public void RejectsBeingBuiltWithoutEntries()
    {
        Assert.Throws<ArgumentNullException>(() => ColorSchemePlan.Build(null!));
    }

    private static ColorSchemeEntries Entries()
    {
        var (map, zones) = ColorSchemeEntriesTests.TwoZones();
        return ColorSchemeEntries.From(ColorSchemeEntriesTests.Preview(map, zones));
    }

    /// <summary>The scheme as it stands after those entries have been written once.</summary>
    private static List<ExistingColorSchemeEntry> Mirror(ColorSchemeEntries entries) => entries.Entries
        .Select(entry => new ExistingColorSchemeEntry(entry.Value, entry.Color))
        .ToList();
}
