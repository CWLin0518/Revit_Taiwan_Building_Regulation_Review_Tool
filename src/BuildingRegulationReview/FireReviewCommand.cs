using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Interop;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using BuildingRegulationReview.Application.ReviewPackages;
using BuildingRegulationReview.FireReview;
using BuildingRegulationReview.RegionEditor;
using BuildingRegulationReview.Revit.ReviewPackages;

namespace BuildingRegulationReview
{
    /// <summary>
    /// 開始檢討 (spec 7 step 3, P3-T09): picks a review package and opens the review window. Every
    /// read and write of the model happens in the window's requests, which run here, in Revit's API
    /// context, through one ExternalEvent.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public sealed class FireReviewCommand : IExternalCommand
    {
        private const string DialogTitle = "防火區劃檢討";

        private static FireReviewWindow _window;
        private static ExternalEvent _event;
        private static RequestHandler _handler;

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var application = commandData.Application;
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

            _handler = new RequestHandler();
            _event = ExternalEvent.Create(_handler);
            _window = new FireReviewWindow(choice.PackageId)
            {
                Post = action =>
                {
                    _handler.Enqueue(action);
                    _event?.Raise();
                }
            };
            new WindowInteropHelper(_window).Owner = application.MainWindowHandle;
            _window.Closed += (_, __) =>
            {
                _event?.Dispose();
                _event = null;
                _handler = null;
                _window = null;
            };
            _window.Show();
            _window.RequestScan();
            return Result.Succeeded;
        }

        private static PackageChoice ChoosePackage(UIApplication application, Document document)
        {
            // Same reason as the Editor's picker: a package whose Area Plan was deleted is still in
            // the model and would be listed under its PackageId, so it is left out and explained.
            var selection = ReviewPackageAvailability.Partition(
                new RevitReviewPackageRepository(document).GetAll(),
                new RevitAreaPlanProbe(document).IsLiveAreaPlan);

            var choices = selection.Available
                .Select(package => new PackageChoice(package.PackageId, package.AreaPlanUniqueId,
                    FireReviewModel.LabelOf(document, package), package.DraftingViewUniqueId))
                .OrderBy(choice => choice.Label, StringComparer.CurrentCulture)
                .ToList();

            if (choices.Count == 0)
            {
                TaskDialog.Show(DialogTitle, PackagePickerMessages.WithNotice(
                    "這個專案還沒有建立 Area Plan 的檢討套件，請先執行「防火區劃設定」與「防火區劃編輯器」。",
                    selection.HiddenNotice));
                return null;
            }

            if (choices.Count == 1) return choices[0];

            var picker = new PackagePickerWindow(choices, selection.HiddenNotice);
            new WindowInteropHelper(picker).Owner = application.MainWindowHandle;
            return picker.ShowDialog() == true ? picker.Selected : null;
        }

        /// <summary>Runs the window's requests in order, in Revit's API context.</summary>
        private sealed class RequestHandler : IExternalEventHandler
        {
            private readonly Queue<Action<UIApplication>> _queue = new Queue<Action<UIApplication>>();
            private readonly object _gate = new object();

            public void Enqueue(Action<UIApplication> action)
            {
                lock (_gate) _queue.Enqueue(action);
            }

            public void Execute(UIApplication application)
            {
                while (true)
                {
                    Action<UIApplication> next;
                    lock (_gate)
                    {
                        if (_queue.Count == 0) return;
                        next = _queue.Dequeue();
                    }

                    if (application.ActiveUIDocument?.Document == null)
                    {
                        TaskDialog.Show(DialogTitle, "找不到作用中的 Revit 專案，這個操作沒有執行。");
                        continue;
                    }

                    try
                    {
                        next(application);
                    }
                    catch (Exception exception)
                    {
                        TaskDialog.Show(DialogTitle, "操作失敗：" + exception.Message);
                    }
                }
            }

            public string GetName() => "防火區劃檢討";
        }
    }
}
