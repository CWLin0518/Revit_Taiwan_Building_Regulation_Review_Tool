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
/// The log one write-back run leaves behind (spec 10.5: 局部錯誤可由使用者選擇略過，但須寫入日誌).
/// </summary>
public class ApplyResultTests
{
    private static readonly Guid PackageId = Guid.Parse("11111111-2222-3333-4444-555555555555");
    private static readonly Guid ZoneId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
    private static readonly GeometryTolerance Tolerance = GeometryTolerance.Default;
    private static readonly DateTime SolvedAt = new DateTime(2026, 9, 21, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void CountsWhatItDidAndSaysSoInOneLine()
    {
        var plan = Plan();
        var log = new ApplyResult.Builder(plan);
        foreach (var step in plan.Steps) log.Created(step, "new-" + step.Key.Ordinal);

        var result = log.Complete();

        Assert.Equal(6, result.CreatedCount);
        Assert.Equal(0, result.FailedCount);
        Assert.True(result.IsComplete);
        Assert.False(result.IsRolledBack);
        Assert.Equal("已建立 6 個、更新 0 個、刪除 0 個元素。", result.Summary);
    }

    [Fact]
    public void AnElementRevitRefusedLeavesTheDraftsUnappliedAndLandsOnTheLog()
    {
        var plan = Plan();
        var log = new ApplyResult.Builder(plan);
        var first = plan.Steps[0];
        log.Failed(first, "邊界線太短。");
        foreach (var step in plan.Steps.Skip(1)) log.Created(step, "new-" + step.Key.Ordinal);

        var result = log.Complete();

        Assert.Equal(1, result.FailedCount);
        Assert.False(result.IsComplete);
        Assert.False(result.IsRolledBack);
        Assert.Contains("另有 1 個失敗", result.Summary, StringComparison.Ordinal);
        Assert.Contains(result.Log, line => line.Contains("邊界線太短。", StringComparison.Ordinal));
        Assert.Equal(first.Key, Assert.Single(result.Problems).Key);
    }

    [Fact]
    public void ASkippedElementIsReportedWithoutFailingTheRun()
    {
        var plan = Plan();
        var log = new ApplyResult.Builder(plan);
        log.Skipped(plan.Steps[0], "這個元素沒有本套件的擁有權標記，不會刪除。");

        var result = log.Complete();

        Assert.Equal(1, result.SkippedCount);
        Assert.Equal(0, result.FailedCount);
        Assert.True(result.IsComplete);
        Assert.Contains("已略過", Assert.Single(result.Items).Text, StringComparison.Ordinal);
    }

    [Fact]
    public void ARolledBackRunReportsNoChangesAtAll()
    {
        var plan = Plan();
        var log = new ApplyResult.Builder(plan);
        foreach (var step in plan.Steps) log.Created(step, "new-" + step.Key.Ordinal);

        var result = log.RolledBack("Revit 無法提交「建立與更新面積邊界線」。");

        // The elements the builder had already collected never survived the rollback, so reporting
        // them would tell the user the model holds things it does not.
        Assert.Empty(result.Items);
        Assert.Equal(0, result.CreatedCount);
        Assert.True(result.IsRolledBack);
        Assert.False(result.IsComplete);
        Assert.Contains("模型沒有任何變更", result.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public void CarriesWhatWasDeferredAndWhatTheDraftsWarnedAboutIntoTheLog()
    {
        var result = new ApplyResult.Builder(Plan()).Complete();

        Assert.Contains(result.Notes, note => note.Contains("單線圖細部線", StringComparison.Ordinal));
        Assert.Contains(result.Log, line => line.Contains("單線圖細部線", StringComparison.Ordinal));
    }

    [Fact]
    public void ARollbackStillKeepsTheNotesBecauseTheyExplainWhy()
    {
        var log = new ApplyResult.Builder(Plan());
        log.Note("區劃「A」的面積沒有落在封閉的邊界內。");

        var result = log.RolledBack("Revit 無法提交。");

        Assert.Contains(result.Notes, note => note.Contains("沒有落在封閉的邊界內", StringComparison.Ordinal));
    }

    [Fact]
    public void ARunWithNothingToDoIsNotAFailure()
    {
        var result = ApplyResult.Nothing(PackageId);

        Assert.True(result.IsComplete);
        Assert.False(result.IsRolledBack);
        Assert.Empty(result.Items);
        Assert.Empty(result.ManualActions);
    }

    // ---- what the run hands back to the user (spec 10.5 item 3) ---------------------------------

    [Fact]
    public void AManualItemIsNotAFailureButStillShowsUpInTheSummaryAndTheLog()
    {
        var plan = Plan();
        var log = new ApplyResult.Builder(plan);
        foreach (var step in plan.Steps) log.Created(step, "new-" + step.Key.Ordinal);
        log.Manual("色彩項目「A」", "Revit 不允許刪除使用中的色彩項目", "請先確認沒有面積在用這個名稱");

        var result = log.Complete();

        // The boundaries went in; only the colour entry needs a person. Calling that a failure would
        // leave the Editor claiming the drafts were never applied.
        Assert.True(result.IsComplete);
        Assert.Equal(0, result.FailedCount);
        Assert.Contains("還有 1 項需要在 Revit 中人工處理。", result.Summary, StringComparison.Ordinal);
        Assert.Contains(result.Log, line => line.StartsWith("需人工處理：", StringComparison.Ordinal));
    }

    [Fact]
    public void AManualItemSaysWhatItIsWhyAndWhatToDo()
    {
        var action = new ManualAction("色彩項目「A」", "Revit 不允許刪除使用中的色彩項目", "請先確認沒有面積在用這個名稱");

        Assert.Equal(
            "需人工處理：色彩項目「A」——Revit 不允許刪除使用中的色彩項目。建議：請先確認沒有面積在用這個名稱",
            action.Text);
    }

    [Theory]
    [InlineData("", "why", "what")]
    [InlineData("subject", " ", "what")]
    [InlineData("subject", "why", null)]
    public void AManualItemWithoutAllThreePartsIsNotOne(string subject, string reason, string? suggestion)
    {
        // An item that only says 不支援 is a dead end; the user has to be told what to click.
        Assert.Throws<ArgumentException>(() => new ManualAction(subject, reason, suggestion!));
    }

    [Fact]
    public void ManualItemsSurviveARollbackBecauseTheReasonStillHolds()
    {
        var log = new ApplyResult.Builder(Plan());
        log.Manual("面積色彩配置", "這個專案沒有可複製的色彩配置", "請先新增一個色彩配置");

        var result = log.RolledBack("Revit 無法提交。");

        Assert.Single(result.ManualActions);
        Assert.Empty(result.Items);
    }

    // ---- helpers ------------------------------------------------------------------------------

    private static ApplyPlan Plan()
    {
        var map = Solve(Rectangle(0, 0, 10, 10, "OUTER"));
        var zones = Succeeds(ZoneDraftSet.Empty.Add(new ZoneDraft(
            ZoneId,
            "A",
            ZoneColorPalette.At(0),
            new[] { map.FaceAt(new Point2D(5, 5))!.Id })));
        return ApplyPlan.Build(ApplyPreview.Build(PackageId, map, zones));
    }

    private static PlanRegionMap Solve(IEnumerable<Segment2D> segments)
    {
        var snapshot = new PlanGeometrySnapshot(PackageId, "host-doc", "level-1", segments, Tolerance);
        var network = Succeeds(new LineNetworkRepairer().Repair(snapshot, SolvedAt));
        return Succeeds(new RegionSolver().Solve(network, SolvedAt));
    }

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

/// <summary>
/// Spec 10.6's area check: the drafts and Revit have to agree about how big a 區劃 is, and when they
/// do not the reason is nearly always a boundary that did not close.
/// </summary>
public class AreaAgreementTests
{
    [Fact]
    public void SaysNothingWhenTheTwoNumbersAreTheSameRoomMeasuredTwice()
    {
        Assert.Null(AreaAgreement.Describe("A", 100.0, 100.4));
    }

    [Fact]
    public void CallsOutAnAreaThatNeverLandedInsideAClosedRing()
    {
        var note = AreaAgreement.Describe("A", 100.0, 0.0);

        Assert.NotNull(note);
        Assert.Contains("沒有落在封閉的邊界內", note!, StringComparison.Ordinal);
        Assert.Contains("區劃「A」", note, StringComparison.Ordinal);
    }

    [Fact]
    public void ReportsADifferenceBiggerThanTheTolerance()
    {
        var note = AreaAgreement.Describe("A", 100.0, 80.0);

        Assert.NotNull(note);
        Assert.Contains("相差 20%", note!, StringComparison.Ordinal);
    }

    [Fact]
    public void ADifferenceTheOtherWayCountsJustAsMuch()
    {
        Assert.NotNull(AreaAgreement.Describe("A", 100.0, 130.0));
    }

    [Fact]
    public void ADraftWithNoAreaOfItsOwnHasNothingToDisagreeWith()
    {
        Assert.Null(AreaAgreement.Describe("A", 0.0, 12.0));
    }
}
