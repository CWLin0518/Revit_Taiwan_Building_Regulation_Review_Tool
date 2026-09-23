using System;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using BuildingRegulationReview.FireReview;
using BuildingRegulationReview.Revit.Parameters;

namespace BuildingRegulationReview
{
    /// <summary>
    /// Opens the batch panel for the active view's element Types and writes what the user accepted.
    /// </summary>
    /// <remarks>
    /// The panel is modal, so the write happens inside this command's API context — one transaction
    /// that either lands whole or rolls back (see <see cref="RevitFireReviewParameterWriter"/>). The
    /// panel is reopened after a write so the rows show what the model now holds.
    /// </remarks>
    [Transaction(TransactionMode.Manual)]
    public sealed class FireReviewParameterPanelCommand : IExternalCommand
    {
        private const string DialogTitle = "防火檢討參數批次設定";

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var uiDocument = commandData?.Application?.ActiveUIDocument;
            var document = uiDocument?.Document;
            if (document == null)
            {
                message = "請先開啟 Revit 專案。";
                return Result.Failed;
            }

            var view = uiDocument.ActiveGraphicalView ?? document.ActiveView;
            if (view == null)
            {
                message = "請先開啟一個視圖。";
                return Result.Failed;
            }

            if (view.IsTemplate)
            {
                TaskDialog.Show(DialogTitle, "目前是視圖樣板，沒有可收集的構件。請切換到平面、立面或 3D 視圖。");
                return Result.Cancelled;
            }

            try
            {
                var table = new RevitFireReviewTypeScanner(document).Scan(view);
                if (table.Rows.Count == 0)
                {
                    TaskDialog.Show(DialogTitle, string.Join("\n", table.Warnings));
                    return Result.Cancelled;
                }

                var window = new FireReviewParameterPanelWindow(table, view.Name);
                new System.Windows.Interop.WindowInteropHelper(window).Owner = commandData.Application.MainWindowHandle;

                window.Write = edits =>
                {
                    var result = new RevitFireReviewParameterWriter(document).Apply(edits.ToList());
                    var detail = result.Failures.Count == 0
                        ? string.Empty
                        : "\n\n未能寫入：\n- " + string.Join("\n- ", result.Failures.Select(f => f.ToString()));

                    TaskDialog.Show(DialogTitle, result.Summary + detail);
                    window.ReportWritten(result.Summary);

                    // Rows that could not be written are still worth fixing, so the panel stays open
                    // for them; a clean write has nothing left to do.
                    if (result.Committed && result.Failures.Count == 0) window.DialogResult = true;
                };

                var accepted = window.ShowDialog() == true;
                return accepted ? Result.Succeeded : Result.Cancelled;
            }
            catch (Exception exception) when (exception is Autodesk.Revit.Exceptions.ApplicationException ||
                                              exception is InvalidOperationException)
            {
                message = "讀取或寫入防火檢討參數失敗：" + exception.Message;
                TaskDialog.Show(DialogTitle, message);
                return Result.Failed;
            }
        }
    }
}
