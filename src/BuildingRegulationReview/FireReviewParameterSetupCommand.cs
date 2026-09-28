using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using BuildingRegulationReview.FireReview;
using BuildingRegulationReview.Revit.ProjectSetup;

namespace BuildingRegulationReview
{
    /// <summary>
    /// 「防火參數一鍵建立」：把防火區劃檢討要讀的共用參數一次加進目前的專案並綁好類別（spec 8.2）。
    /// </summary>
    /// <remarks>
    /// 視窗是模式對話框，所以建立就在這個指令的 API context 裡發生——一個交易，要嘛整批落地要嘛整批
    /// 回滾（<see cref="RevitFireReviewParameterInstaller"/>）。這樣做不必外接 ExternalEvent，也不會有
    /// 「視窗還開著、模型已經被改一半」的中間狀態。
    /// </remarks>
    [Transaction(TransactionMode.Manual)]
    public sealed class FireReviewParameterSetupCommand : IExternalCommand
    {
        private const string DialogTitle = "防火檢討參數一鍵建立";

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var document = commandData?.Application?.ActiveUIDocument?.Document;
            if (document == null)
            {
                message = "請先開啟 Revit 專案。";
                return Result.Failed;
            }

            if (document.IsFamilyDocument)
            {
                TaskDialog.Show(DialogTitle, "這個功能建立的是專案參數，族群檔沒有專案參數。請先開啟專案檔。");
                return Result.Cancelled;
            }

            try
            {
                var installer = new RevitFireReviewParameterInstaller(document);
                var window = new FireReviewParameterSetupWindow(installer.Inspect());
                new System.Windows.Interop.WindowInteropHelper(window).Owner = commandData.Application.MainWindowHandle;

                window.Reinspect = () => installer.Inspect();
                window.Create = plan =>
                {
                    var result = installer.Apply(plan);
                    window.ReportResult(result.Summary, result.Failures, result.Warnings);

                    var detail = string.Empty;
                    if (result.Warnings.Count > 0) detail += "\n\n提醒：\n- " + string.Join("\n- ", result.Warnings);
                    if (result.Failures.Count > 0) detail += "\n\n未能處理：\n- " + string.Join("\n- ", result.Failures);
                    TaskDialog.Show(DialogTitle, result.Summary + detail);

                    // 全部處理完就沒事可做了；還有沒處理完的（衝突或失敗）就讓視窗留著繼續看。
                    // ReportResult 已經重讀過專案，HasWork 就是寫入後的現況，不必再掃一次。
                    if (result.Committed && result.Failures.Count == 0 && !window.HasWork)
                        window.DialogResult = true;
                };

                var applied = window.ShowDialog() == true;
                return applied ? Result.Succeeded : Result.Cancelled;
            }
            catch (Exception exception) when (exception is Autodesk.Revit.Exceptions.ApplicationException ||
                                              exception is InvalidOperationException)
            {
                message = "建立防火檢討參數失敗：" + exception.Message;
                TaskDialog.Show(DialogTitle, message);
                return Result.Failed;
            }
        }
    }
}
