using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using BuildingRegulationReview.Application.Candidates;
using BuildingRegulationReview.Application.Diagnostics;
using BuildingRegulationReview.Application.Parameters;
using BuildingRegulationReview.Application.ReviewPackages;
using BuildingRegulationReview.Application.Reviews;
using BuildingRegulationReview.Application.WriteBack;
using BuildingRegulationReview.Domain.Common;
using BuildingRegulationReview.Domain.ReviewPackages;
using BuildingRegulationReview.Domain.Reviews;
using BuildingRegulationReview.Domain.Rules;
using BuildingRegulationReview.RegionEditor;
using BuildingRegulationReview.Revit.Candidates;
using BuildingRegulationReview.Revit.Geometry;
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
    /// The packages a review can start on, as the picker lists them, and what was left out and why.
    /// </summary>
    internal sealed class FireReviewPackageList
    {
        public FireReviewPackageList(IReadOnlyList<PackageChoice> choices, ReviewPackageSelection selection)
        {
            Choices = choices;
            Selection = selection;
        }

        /// <summary>The packages whose Area Plan is still in the model, sorted by label.</summary>
        public IReadOnlyList<PackageChoice> Choices { get; }

        public ReviewPackageSelection Selection { get; }

        /// <summary>Why some packages are not offered (their Area Plan was deleted); null when none are hidden.</summary>
        public string HiddenNotice => Selection.HiddenNotice;

        public ReviewPackage Package(Guid packageId) =>
            Selection.Available.FirstOrDefault(package => package.PackageId == packageId);
    }

    /// <summary>
    /// What 開始檢討 did: the outcome of the run and, when it completed, of storing it. Exactly one
    /// of <see cref="Failure"/> and <see cref="Outcome"/> is set.
    /// </summary>
    internal sealed class FireReviewRunResult
    {
        /// <summary>Why the run never produced an outcome: the pre-check did not pass, or the run threw.</summary>
        public string Failure { get; set; }

        public FireReviewOutcome Outcome { get; set; }

        /// <summary>Set only when the run completed; says whether the run reached the model.</summary>
        public FireReviewSaveResult Saved { get; set; }
    }

    /// <summary>
    /// Everything the review does inside the Revit API context. Reading opens no transaction except to
    /// store what it learned about staleness; storing a run and marking its view is one
    /// TransactionGroup that rolls back as a whole when anything throws (spec 13.2), so the model
    /// either gains the run and its marks or stays as it was.
    /// </summary>
    /// <remarks>
    /// The review window and the MCP tools call the same methods here, in the same order — list the
    /// packages, scan, run and store — so what an agent verifies is what a user's button does
    /// (docs/adr/0004).
    /// </remarks>
    internal static class FireReviewModel
    {
        /// <summary>The packages the review can be started on — what「防火區劃檢討」offers in its picker.</summary>
        public static FireReviewPackageList AvailablePackages(Document document)
        {
            // Same reason as the Editor's picker: a package whose Area Plan was deleted is still in
            // the model and would be listed under its PackageId, so it is left out and explained.
            var selection = ReviewPackageAvailability.Partition(
                new RevitReviewPackageRepository(document).GetAll(),
                new RevitAreaPlanProbe(document).IsLiveAreaPlan);

            var choices = selection.Available
                .Select(package => new PackageChoice(package.PackageId, package.AreaPlanUniqueId,
                    LabelOf(document, package), package.DraftingViewUniqueId))
                .OrderBy(choice => choice.Label, StringComparer.CurrentCulture)
                .ToList();

            return new FireReviewPackageList(choices, selection);
        }

        /// <summary><see cref="Scan"/>, with an unexpected exception turned into a scan that says what failed.</summary>
        public static FireReviewScan TryScan(Document document, Guid packageId, bool acceptRuleSetUpdate)
        {
            try
            {
                return Scan(document, packageId, acceptRuleSetUpdate);
            }
            catch (Exception exception)
            {
                return new FireReviewScan { Failure = "讀取模型時發生錯誤：" + exception.Message };
            }
        }

        /// <summary>
        /// The package's latest stored run, as it was stored: no pre-scan, so nothing says whether the
        /// model has moved since. <see cref="Scan"/> is what judges that (需更新).
        /// </summary>
        public static ReviewRun LatestRun(Document document, Guid packageId) =>
            new RevitReviewRunRepository(document).GetLatest(packageId);

        /// <summary>Whether <see cref="RunAndSave"/> can start on this scan: the pre-check passed and the candidates were read.</summary>
        public static bool CanRun(FireReviewScan scan) =>
            scan?.Readiness != null && scan.Readiness.CanRun && scan.Candidates != null && scan.Inputs != null;

        /// <summary>
        /// 開始檢討: runs the checks over what the scan read, then stores a completed run and marks the
        /// review view. A cancelled or failed run stores nothing.
        /// </summary>
        /// <param name="beforeSave">Called with a completed outcome just before it is stored, so a caller can report progress.</param>
        public static FireReviewRunResult RunAndSave(Document document, FireReviewScan scan, CancellationToken cancellation,
            IProgress<FireReviewProgress> progress = null, Action<FireReviewOutcome> beforeSave = null)
        {
            if (!CanRun(scan))
                return new FireReviewRunResult { Failure = "前置檢查尚未通過，無法開始檢討；請先依前置檢查的修正方式處理。" };

            FireReviewOutcome outcome;
            try
            {
                var request = new FireReviewRequest(scan.Package, scan.Readiness.RuleSet,
                    new RuleEvaluationContext(DateTime.Today, FireReviewRuleSetSource.Jurisdiction),
                    scan.Candidates, scan.Inputs, scan.Environment, scan.PreviousRun, scan.Prescan,
                    curtainWallReader: new RevitCurtainWallGeometryReader(document));
                outcome = FireReviewRunner.Run(request, cancellation, progress);
            }
            catch (Exception exception)
            {
                return new FireReviewRunResult { Failure = "檢討發生錯誤：" + exception.Message };
            }

            if (!outcome.IsCompleted) return new FireReviewRunResult { Outcome = outcome };

            beforeSave?.Invoke(outcome);
            FireReviewSaveResult saved;
            try
            {
                saved = Save(document, outcome.Package, outcome.Run, scan.Candidates, null);
            }
            catch (Exception exception)
            {
                saved = new FireReviewSaveResult { Saved = false, Error = exception.Message };
            }

            return new FireReviewRunResult { Outcome = outcome, Saved = saved };
        }

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
            var modelFacts = new RevitBuildingHeightReader(document).Read();
            var inputs = set == null ? null : ReviewInputAssembler.Assemble(set, parameters, model: modelFacts);

            // 挑空's 連跨樓層數 and 連通區劃面積 are traced through every storey of the scheme
            // (垂直區劃規格 §3.8). Only a package that has a 挑空 pays for reading every Area of the
            // scheme. The traced facts enter the zone inputs, so the baseline below sees them too, and
            // a 區劃 changed on another storey makes this package's stored run 需更新.
            if (set != null && inputs != null && HasAtrium(set, inputs))
            {
                try
                {
                    var (storeys, problem) = new RevitStoreyZoneReader(document).Read(package.AreaPlanUniqueId);
                    modelFacts = storeys != null ? modelFacts.WithStoreys(storeys) : modelFacts.WithStoreysUnavailable(problem);
                }
                catch (Exception exception)
                {
                    modelFacts = modelFacts.WithStoreysUnavailable("讀取其他樓層的區劃時發生錯誤：" + exception.Message);
                }
                inputs = ReviewInputAssembler.Assemble(set, parameters, model: modelFacts);
            }
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

        private static bool HasAtrium(CandidateSet set, ReviewInputAssembly inputs) =>
            set.Zones.Any(zone => inputs.Area.ForZone(zone.ZoneId).Any(input =>
                input.Field == "zone.use" && !input.IsUnreadable &&
                input.Value.Kind == ReviewValueKind.Text &&
                string.Equals(input.Value.Text.Trim(), ZoneUses.Atrium, StringComparison.Ordinal)));

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
            var baseline = ReviewBaselineBuilder.Build(scan.Candidates, scan.Environment, scan.Inputs);
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
                        log.Add(mark.IsRolledBack ? ReviewErrorCode.ReviewMarkRefused : ReviewErrorCode.ReviewMarkCompleted, ReviewStage.Review,
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
