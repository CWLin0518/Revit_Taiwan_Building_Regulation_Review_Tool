using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Interop;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using BuildingRegulationReview.Application.Geometry;
using BuildingRegulationReview.Application.RegionEditing;
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
    /// Read-only from end to end. Nothing here opens a transaction, and the Editor only drafts;
    /// writing boundaries and Areas back is P2-T07.
    /// </remarks>
    [Transaction(TransactionMode.ReadOnly)]
    public sealed class RegionEditorCommand : IExternalCommand
    {
        private const string DialogTitle = "防火區劃編輯器";

        private static RegionEditorWindow _window;
        private static ExternalEvent _externalEvent;
        private static SelectSourcesHandler _handler;

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

            var session = BuildSession(document, choice);
            if (session == null) return Result.Cancelled;

            _window = new RegionEditorWindow(session, (document.GetElement(choice.AreaPlanUniqueId) as View)?.Name);
            new WindowInteropHelper(_window).Owner = application.MainWindowHandle;

            // Read at preview time rather than at start-up: the model may have been edited while the
            // modeless Editor was open, and the preview has to diff against what is there now.
            _window.ReadExistingElements = () => new RevitManagedElementInventory(document)
                .Read(choice.AreaPlanUniqueId, choice.DraftingViewUniqueId);

            _handler = new SelectSourcesHandler();
            _externalEvent = ExternalEvent.Create(_handler);
            _window.ShowSourceElements = sources =>
            {
                _handler.Sources = sources;
                _externalEvent.Raise();
            };
            _window.Closed += (_, __) =>
            {
                _externalEvent?.Dispose();
                _externalEvent = null;
                _handler = null;
                _window = null;
            };
            _window.Show();
            return Result.Succeeded;
        }

        private static PackageChoice ChoosePackage(UIApplication application, Document document)
        {
            var choices = new RevitReviewPackageRepository(document).GetAll()
                .Where(package => !string.IsNullOrWhiteSpace(package.AreaPlanUniqueId))
                .Select(package => new PackageChoice(
                    package.PackageId,
                    package.AreaPlanUniqueId,
                    (document.GetElement(package.AreaPlanUniqueId) as View)?.Name ?? package.PackageId.ToString(),
                    package.DraftingViewUniqueId))
                .OrderBy(choice => choice.Label, StringComparer.CurrentCulture)
                .ToList();

            if (choices.Count == 0)
            {
                TaskDialog.Show(DialogTitle, "這個專案還沒有建立 Area Plan 的檢討套件，請先執行「防火區劃設定」。");
                return null;
            }

            if (choices.Count == 1) return choices[0];

            var picker = new PackagePickerWindow(choices);
            new WindowInteropHelper(picker).Owner = application.MainWindowHandle;
            return picker.ShowDialog() == true ? picker.Selected : null;
        }

        private static RegionEditorSession BuildSession(Document document, PackageChoice choice)
        {
            var snapshot = new RevitPlanGeometryExtractor(document)
                .Extract(new PlanGeometryExtractionRequest(choice.PackageId, choice.AreaPlanUniqueId));
            if (!Succeeded(snapshot, "讀取平面幾何")) return null;

            if (snapshot.Value.IsEmpty)
            {
                TaskDialog.Show(DialogTitle, "這個 Area Plan 的範圍內沒有可用的牆、柱或輔助線，無法建立區劃。");
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

            return new RegionEditorSession(map.Value, new ScreenSize(1024, 720), network.Value.Issues);
        }

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
    }
}
