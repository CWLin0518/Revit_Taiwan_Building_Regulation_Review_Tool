using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using BuildingRegulationReview.Application.Candidates;
using BuildingRegulationReview.Application.Diagnostics;
using BuildingRegulationReview.Application.ReviewPackages;
using BuildingRegulationReview.Application.Reviews;
using BuildingRegulationReview.Application.WriteBack;
using BuildingRegulationReview.Domain.Common;
using BuildingRegulationReview.Domain.ReviewPackages;
using BuildingRegulationReview.Domain.Reviews;
using BuildingRegulationReview.Domain.Rules;
using BuildingRegulationReview.Revit.Candidates;
using BuildingRegulationReview.Revit.ReviewPackages;
using BuildingRegulationReview.Revit.Reviews;
using DomainResult = BuildingRegulationReview.Domain.Common.Result;

namespace BuildingRegulationReview.FireReview
{
    /// <summary>What one pre-scan read from the model: pure data from here on (spec 15).</summary>
    internal sealed class FireReviewScan
    {
        public ReviewPackage Package { get; set; }
        public string PackageLabel { get; set; }
        public Result<CompiledRuleSet> RuleSet { get; set; }
        public CandidateSet Candidates { get; set; }
        public ReviewInputAssembly Inputs { get; set; }
        public ReviewEnvironment Environment { get; set; }
        public ReviewReadinessReport Readiness { get; set; }
        public ReviewRun PreviousRun { get; set; }
        public StoredRunInspection Stored { get; set; }

        /// <summary>The evidence baseline of the model as this scan read it; null when candidates could not be read.</summary>
        public ReviewBaseline CurrentBaseline { get; set; }
        public ReviewTable StoredTable { get; set; }
        public TimeSpan Prescan { get; set; }
        public ReviewLog Log { get; set; }
        public string Failure { get; set; }
    }

    /// <summary>What storing a run (and marking the review view) did.</summary>
    internal sealed class FireReviewSaveResult
    {
        public bool Saved { get; set; }
        public ReviewPackage Package { get; set; }
        public ReviewRun Run { get; set; }
        public ReviewMarkupResult Mark { get; set; }
        public string Error { get; set; }
        public ReviewLog Log { get; set; }
    }

    /// <summary>
    /// Everything the review does inside the Revit API context. Reading opens no transaction except to
    /// store what it learned about staleness; storing a run and marking its view is one
    /// TransactionGroup that rolls back as a whole when anything throws (spec 13.2), so the model
    /// either gains the run and its marks or stays as it was.
    /// </summary>
    internal static class FireReviewModel
    {
        public static FireReviewScan Scan(Document document, Guid packageId, bool acceptRuleSetUpdate)
        {
            var watch = Stopwatch.StartNew();
            var packages = new RevitReviewPackageRepository(document);
            var package = packages.Get(packageId);
            if (package == null) return new FireReviewScan { Failure = "這個檢討套件已經不在模型中，請重新執行「防火區劃設定」。" };

            var log = new ReviewLog.Builder(packageId);
            var now = DateTime.UtcNow;

            // Spec 11.1「Area／Boundary 未過期」: the same probe the Region Editor uses.
            var boundaryReasons = new List<string>();
            try
            {
                var verdict = ReviewStaleness.Evaluate(package, new RevitReviewStalenessProbe(document).Observe(package), now);
                boundaryReasons.AddRange(verdict.Reasons);
                if (verdict.Changed)
                {
                    package = verdict.Package;
                    SavePackage(document, package);
                }
                if (verdict.Reasons.Count > 0) log.AddRange(ReviewStaleness.Explain(verdict, now));
            }
            catch (Exception exception)
            {
                boundaryReasons.Add("無法確認區劃邊界是否仍與模型一致：" + exception.Message);
            }

            var ruleSet = FireReviewRuleSetSource.Load();

            Result<CandidateSet> candidates;
            try
            {
                var observed = package.AreaPlanUniqueId == null
                    ? DomainResult.Failure<CandidateObservationSet>(new Error(ReviewErrorCode.CandidateZoneUnusable, "這個工作包沒有 Area Plan。"))
                    : new RevitCandidateObservationReader(document).Read(packageId, package.AreaPlanUniqueId);
                candidates = observed.IsSuccess
                    ? DomainResult.Success(CandidateResolver.Resolve(observed.Value))
                    : DomainResult.Failure<CandidateSet>(observed.Error);
            }
            catch (Exception exception)
            {
                candidates = DomainResult.Failure<CandidateSet>(new Error(ReviewErrorCode.CandidateZoneUnusable,
                    "讀取候選元素時發生錯誤。", exception.Message));
            }

            var set = candidates.IsSuccess ? candidates.Value : null;
            var parameters = new RevitReviewParameterReader(document).Read(set);
            var environmentReader = new RevitReviewEnvironmentReader(document);
            var environment = environmentReader.ReadEnvironment(package);
            var readiness = ReviewReadiness.Evaluate(new ReviewReadinessInput(
                package, boundaryReasons, ruleSet, environmentReader.ReadConditions(package), parameters, candidates, acceptRuleSetUpdate));
            var inputs = set == null ? null : ReviewInputAssembler.Assemble(set, parameters);
            watch.Stop();

            var scan = new FireReviewScan
            {
                Package = package,
                PackageLabel = LabelOf(document, package),
                RuleSet = ruleSet,
                Candidates = set,
                Inputs = inputs,
                Environment = environment,
                Readiness = readiness,
                Prescan = watch.Elapsed
            };

            ReadStoredRun(document, scan, log, now);
            log.AddRange(readiness.ToLog(now));
            scan.Log = log.Build();
            return scan;
        }

        /// <summary>
        /// The latest run, judged against the model now (spec 13.1). What the judgement changes —
        /// overrides awaiting confirmation, a package gone Stale — is stored at once, so the next
        /// session does not read a stale run as current.
        /// </summary>
        private static void ReadStoredRun(Document document, FireReviewScan scan, ReviewLog.Builder log, DateTime now)
        {
            ReviewRun latest;
            try
            {
                latest = new RevitReviewRunRepository(document).GetLatest(scan.Package.PackageId);
            }
            catch (Exception exception)
            {
                log.Add(ReviewErrorCode.ReviewRunUnreadable, ReviewStage.Review, ReviewSeverity.Warning,
                    "上一次的檢討紀錄無法讀取，將視為沒有紀錄。", technicalDetail: exception.Message,
                    suggestion: "重新執行「開始檢討」會建立新的紀錄。");
                return;
            }

            scan.PreviousRun = latest;
            if (latest == null) return;

            if (scan.Candidates == null || scan.RuleSet.IsFailure)
            {
                scan.StoredTable = ReviewTable.Build(latest);
                return;
            }

            var rules = scan.RuleSet.Value.RuleSet;
            var baseline = ReviewBaselineBuilder.Build(scan.Candidates, scan.Environment,
                scan.Inputs.Area, scan.Inputs.Rating, scan.Inputs.Protection);
            scan.CurrentBaseline = baseline;
            var inspection = StoredRunInspection.Inspect(scan.Package, latest, baseline, rules.RuleSetId, rules.Version, now);
            log.AddRange(inspection.Log);

            if (inspection.NeedsSaving)
            {
                try
                {
                    using (var transaction = new Transaction(document, "更新防火檢討結果狀態"))
                    {
                        transaction.Start();
                        if (inspection.RunChanged) new RevitReviewRunRepository(document).Save(inspection.Run);
                        if (inspection.PackageChanged) new RevitReviewPackageRepository(document).Save(inspection.Package);
                        transaction.Commit();
                    }
                }
                catch (Exception exception)
                {
                    log.Add(ReviewErrorCode.ReviewSaveRolledBack, ReviewStage.Review, ReviewSeverity.Warning,
                        "檢討結果的失效狀態無法寫回模型，下次開啟會重新判定。", technicalDetail: exception.Message);
                }
            }

            scan.Package = inspection.Package;
            scan.PreviousRun = inspection.Run;
            scan.Stored = inspection;
            scan.StoredTable = inspection.Table;
        }

        /// <summary>Stores a completed run and the package, then marks the review view — one undo step.</summary>
        public static FireReviewSaveResult Save(Document document, ReviewPackage package, ReviewRun run, CandidateSet candidates, ReviewRunFreshness freshness)
        {
            var log = new ReviewLog.Builder(package.PackageId);
            using (var group = new TransactionGroup(document, "防火區劃檢討"))
            {
                group.Start();
                try
                {
                    using (var transaction = new Transaction(document, "儲存防火檢討結果"))
                    {
                        transaction.Start();
                        new RevitReviewRunRepository(document).Save(run);
                        new RevitReviewPackageRepository(document).Save(package);
                        transaction.Commit();
                    }

                    ReviewMarkupResult mark = null;
                    var plan = ReviewMarkupPlan.Build(ReviewTable.Build(run, freshness), candidates.Zones);
                    if (plan.IsSuccess)
                    {
                        mark = new RevitReviewViewMarker(document).Mark(package, plan.Value, ReviewViewName(document, package));
                        foreach (var item in mark.Items.Where(i => i.Outcome == ApplyOutcome.Failed || i.Outcome == ApplyOutcome.Skipped))
                        {
                            log.Add(item.Outcome == ApplyOutcome.Failed ? ReviewErrorCode.ReviewMarkRefused : ReviewErrorCode.ReviewMarkSkipped,
                                ReviewStage.Review, ReviewSeverity.Warning, item.Text, item.ElementUniqueId);
                        }
                        log.Add(mark.IsRolledBack ? ReviewErrorCode.ReviewMarkRefused : ReviewErrorCode.ReviewCompleted, ReviewStage.Review,
                            mark.IsRolledBack ? ReviewSeverity.Warning : ReviewSeverity.Info, mark.Summary,
                            suggestion: mark.IsRolledBack ? "檢討結果已儲存；可在檢討表按「重新標示」再試一次。" : null);
                    }
                    else
                    {
                        log.Add(plan.Error.Code, ReviewStage.Review, ReviewSeverity.Warning, plan.Error.Message);
                    }

                    if (group.Assimilate() != TransactionStatus.Committed)
                        return Failed(package, log, "Revit 未能提交檢討結果。");

                    return new FireReviewSaveResult { Saved = true, Package = package, Run = run, Mark = mark, Log = log.Build() };
                }
                catch (Exception exception)
                {
                    if (group.GetStatus() == TransactionStatus.Started) group.RollBack();
                    log.Add(ReviewErrorCode.ReviewSaveRolledBack, ReviewStage.Review, ReviewSeverity.Error,
                        "檢討結果寫入模型時發生錯誤，已整批復原，模型沒有變更。", technicalDetail: exception.ToString(),
                        suggestion: "請確認模型未被鎖定（Worksharing 借用）後重新執行。");
                    return Failed(package, log, exception.Message);
                }
            }
        }

        /// <summary>Selects the entry's elements and shows them, in the review view when there is one.</summary>
        public static string Locate(UIDocument document, Guid packageId, ReviewTableEntry entry)
        {
            if (entry.IsLinked) return "這個項目在連結模型中，無法在主模型選取。";

            var marker = new RevitReviewViewMarker(document.Document);
            var ids = marker.Locate(entry).ToList();
            if (ids.Count == 0) return "這個項目的元素已不在模型中。";

            var view = marker.FindView(packageId);
            if (view != null && document.ActiveView?.Id != view.Id) document.ActiveView = view;
            document.Selection.SetElementIds(ids);
            document.ShowElements(ids);
            return null;
        }

        public static string OperatorName(UIApplication application)
        {
            var name = application?.Application?.Username;
            return string.IsNullOrWhiteSpace(name) ? Environment.UserName : name;
        }

        private static FireReviewSaveResult Failed(ReviewPackage package, ReviewLog.Builder log, string error) =>
            new FireReviewSaveResult { Saved = false, Package = package, Error = error, Log = log.Build() };

        private static void SavePackage(Document document, ReviewPackage package)
        {
            try
            {
                using (var transaction = new Transaction(document, "更新防火區劃檢討狀態"))
                {
                    transaction.Start();
                    new RevitReviewPackageRepository(document).Save(package);
                    transaction.Commit();
                }
            }
            catch (Exception)
            {
                // Worked out again on the next scan; not worth interrupting the user for.
            }
        }

        private static string ReviewViewName(Document document, ReviewPackage package) =>
            ReviewOutputNaming.ReviewView(
                (document.GetElement(package.AreaSchemeUniqueId) as AreaScheme)?.Name,
                (document.GetElement(package.SourceFloorPlanUniqueId) as View)?.Name);

        public static string LabelOf(Document document, ReviewPackage package) =>
            (package.AreaPlanUniqueId == null ? null : (document.GetElement(package.AreaPlanUniqueId) as View)?.Name)
            ?? (document.GetElement(package.SourceFloorPlanUniqueId) as View)?.Name
            ?? package.PackageId.ToString("D");
    }
}
