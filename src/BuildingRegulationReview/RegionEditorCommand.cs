using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Interop;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using BuildingRegulationReview.Application.Geometry;
using BuildingRegulationReview.Application.Diagnostics;
using BuildingRegulationReview.Application.RegionEditing;
using BuildingRegulationReview.Application.ReviewPackages;
using BuildingRegulationReview.Application.WriteBack;
using BuildingRegulationReview.Domain.Geometry;
using BuildingRegulationReview.RegionEditor;
using BuildingRegulationReview.Revit.Geometry;
using BuildingRegulationReview.Revit.ReviewPackages;
using BuildingRegulationReview.Revit.WriteBack;

namespace BuildingRegulationReview
{
    /// <summary>
    /// Opens the Region Editor for a review package (spec 10.3, P2-T05): it runs extraction, line
    /// network repair and region solving, then hands the solved map to the Editor window.
    /// </summary>
    /// <remarks>
    /// Extraction, repair and solving open no transaction. The command is nonetheless Manual rather
    /// than ReadOnly, because of one write it does up front: spec 13.1 says a result whose model has
    /// changed underneath it is Stale, and a status the tool works out and then forgets would let
    /// the next session read a Ready package that is not ready. Writing boundaries and Areas back is
    /// a different matter — the Editor is modeless, so that runs on Revit's own thread through an
    /// ExternalEvent that owns its own transactions.
    /// </remarks>
    [Transaction(TransactionMode.Manual)]
    public sealed class RegionEditorCommand : IExternalCommand
    {
        private const string DialogTitle = "防火區劃編輯器";

        private static RegionEditorWindow _window;
        private static ExternalEvent _externalEvent;
        private static SelectSourcesHandler _handler;
        private static ExternalEvent _applyEvent;
        private static ApplyHandler _applyHandler;

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
            => Run(commandData.Application, ref message);

        internal static Result Run(UIApplication application, ref string message)
        {
            var document = application.ActiveUIDocument?.Document;
            if (document == null)
            {
                message = "請先開啟 Revit 專案。";
                return Result.Failed;
            }

            if (_window != null)
            {
                _window.Activate();
                return Result.Succeeded;
            }

            var choice = ChoosePackage(application, document);
            if (choice == null) return Result.Cancelled;

            if (!CheckForStaleResults(document, choice.PackageId)) return Result.Cancelled;

            var session = BuildSession(document, choice);
            if (session == null) return Result.Cancelled;

            _window = new RegionEditorWindow(session, (document.GetElement(choice.AreaPlanUniqueId) as View)?.Name);
            new WindowInteropHelper(_window).Owner = application.MainWindowHandle;

            // Read at preview time rather than at start-up: the model may have been edited while the
            // modeless Editor was open, and the preview has to diff against what is there now. The
            // package is re-read too, because the Drafting View it points at may not have existed
            // when the Editor opened — the first write-back is what creates it.
            _window.ReadExistingElements = () => new RevitManagedElementInventory(document)
                .Read(choice.AreaPlanUniqueId, LoadPackage(document, choice.PackageId)?.DraftingViewUniqueId);

            _handler = new SelectSourcesHandler();
            _externalEvent = ExternalEvent.Create(_handler);
            _window.ShowSourceElements = sources =>
            {
                _handler.Sources = sources;
                _externalEvent.Raise();
            };

            _applyHandler = new ApplyHandler(choice.PackageId);
            _applyEvent = ExternalEvent.Create(_applyHandler);
            _window.RequestApply = (plan, policy) =>
            {
                _applyHandler.Plan = plan;
                _applyHandler.Policy = policy;
                _applyEvent.Raise();
            };

            _window.Closed += (_, __) =>
            {
                _externalEvent?.Dispose();
                _externalEvent = null;
                _handler = null;
                _applyEvent?.Dispose();
                _applyEvent = null;
                _applyHandler = null;
                _window = null;
            };
            _window.Show();
            return Result.Succeeded;
        }

        private static PackageChoice ChoosePackage(UIApplication application, Document document)
        {
            // A package outlives the Area Plan it points at (it is a DataStorage), so one whose plan
            // the user deleted would otherwise be listed with its PackageId for a label — which is
            // the "一串代碼" the user reported. It stays in the model, recoverable by re-running
            // 防火區劃設定; it just does not belong in a picker.
            var selection = ReviewPackageAvailability.Partition(
                new RevitReviewPackageRepository(document).GetAll(),
                new RevitAreaPlanProbe(document).IsLiveAreaPlan);

            var choices = selection.Available
                .Select(package => new PackageChoice(
                    package.PackageId,
                    package.AreaPlanUniqueId,
                    (document.GetElement(package.AreaPlanUniqueId) as View)?.Name ?? package.PackageId.ToString(),
                    package.DraftingViewUniqueId))
                .OrderBy(choice => choice.Label, StringComparer.CurrentCulture)
                .ToList();

            if (choices.Count == 0)
            {
                TaskDialog.Show(DialogTitle, PackagePickerMessages.WithNotice(
                    "這個專案還沒有建立 Area Plan 的檢討套件，請先執行「防火區劃設定」。",
                    selection.HiddenNotice));
                return null;
            }

            if (choices.Count == 1) return choices[0];

            var picker = new PackagePickerWindow(choices, selection.HiddenNotice);
            new WindowInteropHelper(picker).Owner = application.MainWindowHandle;
            return picker.ShowDialog() == true ? picker.Selected : null;
        }

        /// <summary>
        /// Spec 13.1: asks whether what this package last wrote still describes the model, records
        /// the answer on the package and tells the user. Returns false only when the user decides
        /// not to carry on.
        /// </summary>
        /// <remarks>
        /// The check runs before extraction rather than after, because a stale result is exactly the
        /// case where the user is about to look at a canvas that disagrees with the model and needs
        /// to know which of the two is out of date before they start editing.
        /// </remarks>
        private static bool CheckForStaleResults(Document document, Guid packageId)
        {
            var package = LoadPackage(document, packageId);
            if (package == null) return true;

            StalenessVerdict verdict;
            try
            {
                verdict = ReviewStaleness.Evaluate(package, new RevitReviewStalenessProbe(document).Observe(package));
            }
            catch (Exception)
            {
                // A probe that cannot read the model says nothing about the package; the Editor is
                // still worth opening, and extraction will fail with a better message if it must.
                return true;
            }

            if (verdict.Changed) SaveStatus(document, verdict.Package);
            if (verdict.Reasons.Count == 0) return true;

            var dialog = new TaskDialog(DialogTitle)
            {
                MainInstruction = verdict.Message,
                MainContent = string.Join(Environment.NewLine, verdict.Reasons),
                CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No,
                DefaultButton = TaskDialogResult.Yes
            };
            dialog.FooterText = "繼續會以模型現在的幾何重新求解區劃，原本寫入的結果要重新套用才會一致。";
            return dialog.Show() != TaskDialogResult.No;
        }

        /// <summary>Stores a status the tool worked out. Its own transaction, and never fatal.</summary>
        private static void SaveStatus(Document document, Domain.ReviewPackages.ReviewPackage package)
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
                // A status that could not be stored is a status that will be worked out again on the
                // next run. It is not worth interrupting the user for.
            }
        }

        private static RegionEditorSession BuildSession(Document document, PackageChoice choice)
        {
            var snapshot = new RevitPlanGeometryExtractor(document)
                .Extract(new PlanGeometryExtractionRequest(choice.PackageId, choice.AreaPlanUniqueId));
            if (!Succeeded(snapshot, "讀取平面幾何")) return null;

            if (snapshot.Value.IsEmpty)
            {
                TaskDialog.Show(DialogTitle, "這個 Area Plan 的範圍內沒有可用的牆、柱、房間分隔線或輔助線，無法建立區劃。");
                return null;
            }

            var network = new LineNetworkRepairer().Repair(snapshot.Value);
            if (!Succeeded(network, "修復線網")) return null;

            var map = new RegionSolver().Solve(network.Value);
            if (!Succeeded(map, "求解區劃範圍")) return null;

            if (map.Value.IsEmpty)
            {
                TaskDialog.Show(DialogTitle, "線網中沒有任何封閉範圍，請先補足缺口或加入防火區劃輔助線後再試。");
                return null;
            }

            foreach (var warning in snapshot.Value.Warnings)
            {
                // Extraction warnings have no place on the canvas: they are about what was read, not
                // about a boundary, so they are shown once before the Editor opens.
                TaskDialog.Show(DialogTitle, warning);
            }

            var restored = RestoreWrittenZones(document, choice, map.Value);
            return new RegionEditorSession(map.Value, new ScreenSize(1024, 720), network.Value.Issues, restored);
        }

        /// <summary>
        /// The zones an earlier write-back left in the Area Plan, put back onto the freshly solved
        /// faces, so reopening the Editor edits what is there instead of starting over. A model the
        /// tool has not written to yet gives an empty set, which is what a first run should see.
        /// </summary>
        private static Domain.Regions.ZoneDraftSet RestoreWrittenZones(Document document, PackageChoice choice, PlanRegionMap map)
        {
            ZoneRestoration restoration;
            try
            {
                var written = new RevitWrittenZoneReader(document).Read(choice.PackageId, choice.AreaPlanUniqueId);
                restoration = WrittenZoneRestorer.Restore(map, written);
            }
            catch (Exception exception)
            {
                // Losing the restore costs the user their earlier zones on the canvas, not the model:
                // what is written stays written, and the preview will still show it.
                TaskDialog.Show(DialogTitle, "無法讀取已建立的區劃，編輯器將以空白草稿開啟：" + exception.Message);
                return null;
            }

            if (restoration.Warnings.Count > 0)
            {
                TaskDialog.Show(DialogTitle, string.Join(Environment.NewLine, restoration.Warnings));
            }

            return restoration.Zones;
        }

        /// <summary>
        /// The package as the model has it now. Re-read on every preview and every write-back,
        /// because the Editor is modeless and the last run may have recorded a Drafting View on it.
        /// </summary>
        private static Domain.ReviewPackages.ReviewPackage LoadPackage(Document document, Guid packageId)
        {
            try
            {
                return new RevitReviewPackageRepository(document).Get(packageId);
            }
            catch (Exception)
            {
                // A package that cannot be read is a package with no Drafting View yet, which is the
                // same thing a first run sees; nothing here is worth interrupting the Editor for.
                return null;
            }
        }

        /// <summary>
        /// Spec 10.5 item 5's <c>{AreaScheme}_{SourceFloorPlan}_防火區劃</c>, used only when the
        /// write-back has to create an output. Anything already there keeps its own name.
        /// </summary>
        private static string DefaultOutputName(Document document, Domain.ReviewPackages.ReviewPackage package) =>
            ReviewOutputNaming.Default(
                (document.GetElement(package.AreaSchemeUniqueId) as AreaScheme)?.Name,
                (document.GetElement(package.SourceFloorPlanUniqueId) as View)?.Name);

        private static bool Succeeded<T>(Domain.Common.Result<T> result, string step)
        {
            if (result.IsSuccess) return true;

            var dialog = new TaskDialog(DialogTitle)
            {
                MainInstruction = step + "失敗。",
                MainContent = result.Error.Message,
                ExpandedContent = result.Error.TechnicalDetail
            };
            dialog.Show();
            return false;
        }

        /// <summary>
        /// Selects the elements a face came from back in the Revit view. The Editor is modeless, so
        /// this has to run on Revit's own thread through an ExternalEvent.
        /// </summary>
        private sealed class SelectSourcesHandler : IExternalEventHandler
        {
            public IReadOnlyList<SourceRef> Sources { get; set; }

            public void Execute(UIApplication application)
            {
                var uiDocument = application.ActiveUIDocument;
                if (uiDocument == null || Sources == null) return;

                var document = uiDocument.Document;
                var ids = new List<ElementId>();
                var missing = 0;

                foreach (var source in Sources)
                {
                    // A linked element cannot be selected in the host document, and the write-back
                    // always happens in the host, so those are reported rather than silently dropped.
                    var element = source.IsFromLink ? null : document.GetElement(source.ElementUniqueId);
                    if (element == null) missing++;
                    else ids.Add(element.Id);
                }

                if (ids.Count > 0) uiDocument.Selection.SetElementIds(ids);
                if (missing > 0)
                {
                    TaskDialog.Show(
                        DialogTitle,
                        $"已選取 {ids.Count} 個來源元素。另有 {missing} 個來自連結模型或已不在主模型中，無法選取。");
                }
            }

            public string GetName() => "防火區劃編輯器：選取來源元素";
        }

        /// <summary>
        /// Writes the approved plan back to the model (spec 10.5). The Editor is modeless, so this
        /// is where the write gets a legal Revit context; the result is handed back to the window,
        /// which owns what it means for the drafts.
        /// </summary>
        private sealed class ApplyHandler : IExternalEventHandler
        {
            private readonly Guid _packageId;

            public ApplyHandler(Guid packageId) => _packageId = packageId;

            public ApplyPlan Plan { get; set; }
            public ApplyFailurePolicy Policy { get; set; }

            public void Execute(UIApplication application)
            {
                var window = _window;
                var document = application.ActiveUIDocument?.Document;
                if (window == null || Plan == null) return;

                if (document == null)
                {
                    TaskDialog.Show(DialogTitle, "找不到作用中的 Revit 專案，這次沒有寫入任何東西。");
                    Plan = null;
                    window.Dispatcher.Invoke(() => window.ReportApplied(null));
                    return;
                }

                // The package is loaded now rather than kept from start-up: the last run may have
                // recorded the Drafting View on it, and this run has to find that view rather than
                // make a second one.
                var package = LoadPackage(document, _packageId);
                if (package == null || string.IsNullOrWhiteSpace(package.AreaPlanUniqueId))
                {
                    TaskDialog.Show(DialogTitle, "這個檢討套件已經不在模型中，或它的 Area Plan 已被刪除，這次沒有寫入任何東西。");
                    Plan = null;
                    window.Dispatcher.Invoke(() => window.ReportApplied(null));
                    return;
                }

                ApplyResult result;
                try
                {
                    result = new RevitZoneWriteBack(document).Apply(
                        Plan,
                        new ZoneWriteBackRequest(package, DefaultOutputName(document, package), Policy));
                }
                catch (Exception exception)
                {
                    // The write-back rolls its own group back before it rethrows, so the model is
                    // unchanged; what is left is telling the user why.
                    TaskDialog.Show(DialogTitle, "寫回失敗，模型沒有變更：" + exception.Message);
                    window.Dispatcher.Invoke(() => window.ReportApplied(null));
                    return;
                }
                finally
                {
                    Plan = null;
                }

                // Re-read before judging: the run itself may have recorded the Drafting View it
                // created on the package, and building the new state from the copy loaded before the
                // run would save that record straight back out again.
                var written = LoadPackage(document, _packageId) ?? package;
                var report = ReviewRunReport.For(written, result);
                if (report.PackageChanged) SaveStatus(document, report.Package);

                window.Dispatcher.Invoke(() => window.ReportApplied(report));
            }

            public string GetName() => "防火區劃編輯器：寫回 Area Plan";
        }
    }
}
