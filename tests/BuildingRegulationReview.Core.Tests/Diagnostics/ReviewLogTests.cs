using System;
using System.Collections.Generic;
using System.Linq;
using BuildingRegulationReview.Application.Diagnostics;
using BuildingRegulationReview.Application.Geometry;
using BuildingRegulationReview.Application.WriteBack;
using BuildingRegulationReview.Domain.Common;
using BuildingRegulationReview.Domain.Geometry;
using BuildingRegulationReview.Domain.Regions;
using Xunit;

namespace BuildingRegulationReview.Core.Tests.Diagnostics;

/// <summary>
/// Spec 14: 錯誤至少包含錯誤碼、階段、Package ID、元素 UniqueId、使用者訊息、技術細節、處理建議與
/// 時間。UI 顯示使用者可理解的訊息，技術堆疊僅寫入本機日誌。不得在錯誤紀錄中保存機密路徑或憑證。
/// </summary>
public class ReviewLogEntryTests
{
    private static readonly Guid PackageId = Guid.Parse("11111111-2222-3333-4444-555555555555");
    private static readonly DateTime At = new DateTime(2026, 9, 21, 8, 30, 15, DateTimeKind.Utc);

    [Fact]
    public void CarriesEveryFieldTheSpecAsksFor()
    {
        var entry = Entry(
            technicalDetail: "Autodesk.Revit.Exceptions.InvalidOperationException",
            suggestion: "請改用另一個視圖再試一次。");

        Assert.Equal(ReviewErrorCode.WriteBackElementFailed, entry.Code);
        Assert.Equal(ReviewStage.WriteBack, entry.Stage);
        Assert.Equal(PackageId, entry.PackageId);
        Assert.Equal("area-42", entry.ElementUniqueId);
        Assert.Equal("面積邊界線 #3 沒有寫入模型。", entry.UserMessage);
        Assert.Equal("Autodesk.Revit.Exceptions.InvalidOperationException", entry.TechnicalDetail);
        Assert.Equal("請改用另一個視圖再試一次。", entry.Suggestion);
        Assert.Equal(At, entry.TimestampUtc);
    }

    [Fact]
    public void TheScreenGetsTheMessageAndTheSuggestionButNeverTheTechnicalDetail()
    {
        var entry = Entry(
            technicalDetail: "at Autodesk.Revit.DB.Document.Delete(ElementId id)",
            suggestion: "請先解除群組。");

        Assert.Contains("請先解除群組。", entry.UserText, StringComparison.Ordinal);
        Assert.DoesNotContain("Autodesk.Revit.DB.Document.Delete", entry.UserText, StringComparison.Ordinal);
        Assert.Contains("Autodesk.Revit.DB.Document.Delete", entry.ToLogLine(), StringComparison.Ordinal);
    }

    [Fact]
    public void TheLogLineNamesTheStageTheCodeThePackageAndTheElement()
    {
        var line = Entry().ToLogLine();

        Assert.Contains("2026-09-21 08:30:15Z", line, StringComparison.Ordinal);
        Assert.Contains(ReviewErrorCode.WriteBackElementFailed, line, StringComparison.Ordinal);
        Assert.Contains("寫回模型", line, StringComparison.Ordinal);
        Assert.Contains(PackageId.ToString("D"), line, StringComparison.Ordinal);
        Assert.Contains("area-42", line, StringComparison.Ordinal);
    }

    [Fact]
    public void AnEntryAboutTheRunAsAWholeStillHasAColumnForTheElementItIsNotAbout()
    {
        var entry = new ReviewLogEntry(
            ReviewErrorCode.WriteBackCompleted, ReviewStage.WriteBack, ReviewSeverity.Info,
            PackageId, "已建立 6 個元素。", timestampUtc: At);

        Assert.Null(entry.ElementUniqueId);
        Assert.Contains("\t-\t", entry.ToLogLine(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(@"無法開啟 C:\Users\wei\Documents\案件\建築防火檢討1.rvt。", "建築防火檢討1.rvt")]
    [InlineData(@"讀取 \\nas01\projects\2026\taipei.rvt 失敗。", "taipei.rvt")]
    [InlineData("cannot read /home/wei/secret/plan.rvt", "plan.rvt")]
    public void RedactsThePathAndKeepsOnlyTheFileNameTheUserWouldRecognise(string detail, string kept)
    {
        var entry = Entry(technicalDetail: detail);

        Assert.Contains(kept, entry.TechnicalDetail!, StringComparison.Ordinal);
        Assert.Contains("（已隱藏路徑）", entry.TechnicalDetail!, StringComparison.Ordinal);
        Assert.DoesNotContain(@"C:\Users", entry.TechnicalDetail!, StringComparison.Ordinal);
        Assert.DoesNotContain("nas01", entry.TechnicalDetail!, StringComparison.Ordinal);
        Assert.DoesNotContain("/home/wei", entry.TechnicalDetail!, StringComparison.Ordinal);
    }

    [Fact]
    public void LeavesATechnicalDetailWithNoPathInItAlone()
    {
        var entry = Entry(technicalDetail: "ElementId 318842 is not a ModelCurve.");

        Assert.Equal("ElementId 318842 is not a ModelCurve.", entry.TechnicalDetail);
    }

    [Fact]
    public void ARedactedNetworkShareLosesTheServerName()
    {
        // The share name is the part that identifies a client, so it goes even though the file
        // name after it is what the user recognises.
        var entry = Entry(technicalDetail: @"\\client-fileserver\案件\A.rvt");

        Assert.DoesNotContain("client-fileserver", entry.TechnicalDetail!, StringComparison.Ordinal);
        Assert.Contains("A.rvt", entry.TechnicalDetail!, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void RefusesAnEntryWithoutACode(string code)
    {
        Assert.Throws<ArgumentException>(() => new ReviewLogEntry(
            code, ReviewStage.WriteBack, ReviewSeverity.Error, PackageId, "訊息"));
    }

    [Fact]
    public void RefusesAnEntryWithoutAUserMessage()
    {
        Assert.Throws<ArgumentException>(() => new ReviewLogEntry(
            ReviewErrorCode.WriteBackElementFailed, ReviewStage.WriteBack, ReviewSeverity.Error, PackageId, " "));
    }

    [Fact]
    public void EveryCodeTheCatalogueOffersHasAChineseName()
    {
        Assert.NotEmpty(ReviewErrorCode.All);
        foreach (var code in ReviewErrorCode.All)
        {
            Assert.True(ReviewErrorCode.IsKnown(code));
            Assert.NotEqual(code, ReviewErrorCode.Describe(code));
        }
    }

    [Fact]
    public void ACodeFromSomewhereElseIsReportedAsItself()
    {
        Assert.Equal("XYZ-1", ReviewErrorCode.Describe("XYZ-1"));
        Assert.False(ReviewErrorCode.IsKnown("XYZ-1"));
    }

    private static ReviewLogEntry Entry(string? technicalDetail = null, string? suggestion = null) =>
        new ReviewLogEntry(
            ReviewErrorCode.WriteBackElementFailed,
            ReviewStage.WriteBack,
            ReviewSeverity.Error,
            PackageId,
            "面積邊界線 #3 沒有寫入模型。",
            "area-42",
            technicalDetail,
            suggestion,
            At);
}

/// <summary>
/// The log of one write-back (spec 14), built from what the run reported rather than written as the
/// run goes, so what the user sees and what the file holds cannot drift apart.
/// </summary>
public class ReviewLogTests
{
    private static readonly Guid PackageId = Guid.Parse("11111111-2222-3333-4444-555555555555");
    private static readonly Guid ZoneId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
    private static readonly GeometryTolerance Tolerance = GeometryTolerance.Default;
    private static readonly DateTime SolvedAt = new DateTime(2026, 9, 21, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime At = new DateTime(2026, 9, 21, 8, 30, 15, DateTimeKind.Utc);

    [Fact]
    public void ARunThatWorkedIsOneInformationLineAndNoErrors()
    {
        var plan = Plan();
        var builder = new ApplyResult.Builder(plan);
        foreach (var step in plan.Steps) builder.Created(step, "new-" + step.Key.Ordinal);

        var log = ReviewLog.FromApplyResult(builder.Complete(), timestampUtc: At);

        Assert.False(log.HasErrors);
        Assert.Equal(1, log.CountOf(ReviewSeverity.Info));
        Assert.Equal(ReviewErrorCode.WriteBackCompleted, Assert.Single(log.Entries).Code);
    }

    [Fact]
    public void EveryFailureBecomesItsOwnEntryCarryingTheElementItIsAbout()
    {
        var plan = Plan();
        var builder = new ApplyResult.Builder(plan);
        builder.Failed(plan.Steps[0], "邊界線太短。");
        foreach (var step in plan.Steps.Skip(1)) builder.Created(step, "new-" + step.Key.Ordinal);

        var log = ReviewLog.FromApplyResult(builder.Complete(), timestampUtc: At);
        var failure = log.Entries.Single(e => e.Code == ReviewErrorCode.WriteBackElementFailed);

        Assert.True(log.HasErrors);
        Assert.Equal(ReviewSeverity.Error, failure.Severity);
        Assert.Equal("邊界線太短。", failure.TechnicalDetail);
        Assert.NotNull(failure.Suggestion);
    }

    [Fact]
    public void ARollbackIsOneErrorSayingTheModelDidNotChange()
    {
        var log = ReviewLog.FromApplyResult(
            new ApplyResult.Builder(Plan()).RolledBack("Revit 無法提交。"),
            timestampUtc: At);

        var entry = Assert.Single(log.Entries, e => e.Code == ReviewErrorCode.WriteBackRolledBack);
        Assert.Equal(ReviewSeverity.Error, entry.Severity);
        Assert.Contains("模型沒有任何變更", entry.UserMessage, StringComparison.Ordinal);
        Assert.Equal("Revit 無法提交。", entry.TechnicalDetail);
    }

    [Fact]
    public void AManualItemKeepsItsSuggestionSoTheUserKnowsWhatToClick()
    {
        var builder = new ApplyResult.Builder(Plan());
        builder.Manual("面積色彩配置", "這個專案沒有可複製的色彩配置", "請先在 Area Plan 新增一個色彩配置");

        var log = ReviewLog.FromApplyResult(builder.Complete(), timestampUtc: At);
        var entry = Assert.Single(log.Entries, e => e.Code == ReviewErrorCode.WriteBackManualAction);

        Assert.Equal(ReviewSeverity.Warning, entry.Severity);
        Assert.Equal("請先在 Area Plan 新增一個色彩配置", entry.Suggestion);
    }

    [Fact]
    public void AnAreaThatDisagreesIsAnErrorWithItsZoneIdInTheTechnicalDetail()
    {
        var builder = new ApplyResult.Builder(Plan());
        builder.Area(AreaAgreement.Compare(AreaKey(), "A", 100.0, 80.0, elementUniqueId: "area-9"));

        var log = ReviewLog.FromApplyResult(builder.Complete(), timestampUtc: At);
        var entry = Assert.Single(log.Entries, e => e.Code == ReviewErrorCode.AreaDisagrees);

        Assert.Equal(ReviewSeverity.Error, entry.Severity);
        Assert.Equal("area-9", entry.ElementUniqueId);
        Assert.Contains(ZoneId.ToString("D"), entry.TechnicalDetail!, StringComparison.Ordinal);
    }

    [Fact]
    public void AnAreaRevitMeasuredAsZeroGetsItsOwnCodeBecauseItIsADifferentProblem()
    {
        var builder = new ApplyResult.Builder(Plan());
        builder.Area(AreaAgreement.Compare(AreaKey(), "A", 100.0, 0.0));

        var log = ReviewLog.FromApplyResult(builder.Complete(), timestampUtc: At);

        Assert.Single(log.Entries, e => e.Code == ReviewErrorCode.AreaNotEnclosed);
    }

    [Fact]
    public void AnAreaThatAgreesPutsNothingOnTheLog()
    {
        var builder = new ApplyResult.Builder(Plan());
        builder.Area(AreaAgreement.Compare(AreaKey(), "A", 100.0, 100.2));

        var log = ReviewLog.FromApplyResult(builder.Complete(), timestampUtc: At);

        Assert.DoesNotContain(log.Entries, e => e.Code == ReviewErrorCode.AreaDisagrees);
        Assert.False(log.HasErrors);
    }

    [Fact]
    public void TheTextFileHasAHeaderAndOneLinePerEntry()
    {
        var plan = Plan();
        var builder = new ApplyResult.Builder(plan);
        builder.Failed(plan.Steps[0], "邊界線太短。");

        var text = ReviewLog.FromApplyResult(builder.Complete(), timestampUtc: At).ToText();
        var lines = text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);

        Assert.StartsWith("# 防火區劃檢討日誌", lines[0], StringComparison.Ordinal);
        Assert.Contains(PackageId.ToString("D"), lines[0], StringComparison.Ordinal);
        Assert.Equal(4, lines.Length); // two header lines plus the summary and the failure
    }

    [Fact]
    public void AnEmptyLogStillNamesThePackageItIsAbout()
    {
        var log = ReviewLog.Empty(PackageId);

        Assert.True(log.IsEmpty);
        Assert.False(log.HasErrors);
        Assert.Contains(PackageId.ToString("D"), log.ToText(), StringComparison.Ordinal);
    }

    // ---- helpers ------------------------------------------------------------------------------

    private static ManagedElementKey AreaKey() =>
        new ManagedElementKey(PackageId, ZoneId, ManagedElementKind.Area, 0, 0);

    private static ApplyPlan Plan()
    {
        var map = Solve(Rectangle(0, 0, 10, 10, "OUTER"));
        var zones = Succeeds(ZoneDraftSet.Empty.Add(new ZoneDraft(
            ZoneId, "A", ZoneColorPalette.At(0), new[] { map.FaceAt(new Point2D(5, 5))!.Id })));
        // Everything the Editor now schedules, so nothing is deferred and the log lines under
        // test are the ones the run itself produced.
        return ApplyPlan.Build(ApplyPreview.Build(PackageId, map, zones), ApplyPlan.AllKinds);
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
