using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace BuildingRegulationReview
{
    internal static class Article164DrawingCommand
    {
        internal static Result Run(UIApplication application, ref string message)
        {
            var doc = application.ActiveUIDocument?.Document;
            if (doc == null) { message = "找不到目前的 Revit 文件。"; return Result.Failed; }

            var run = Article164ReviewSession.GetResult(doc);
            if (run == null)
            {
                message = "請先在本次 Revit 工作階段對目前模型執行「開始檢討」，通過後才能製作圖說。";
                return Result.Failed;
            }
            if (!run.Compliant)
            {
                message = "上一次「開始檢討」結果為不符合，無法製作圖說；請先調整設計並重新檢討至符合為止。";
                return Result.Failed;
            }
            if (run.FootprintSilhouette == null || run.FootprintSilhouette.Count == 0)
            {
                message = "上一次「開始檢討」沒有成功建立建物平面投影輪廓，無法製作圖說；請重新執行開始檢討並確認牆／樓板幾何正常。";
                return Result.Failed;
            }

            var titleBlockWindow = new Article164TitleBlockWindow(doc);
            if (titleBlockWindow.ShowDialog() != true) return Result.Cancelled;

            try
            {
                var builder = new Article164DrawingBuilder(doc, run);

                ViewSheet sheet;
                using (var tx = new Transaction(doc, "第164條圖說製作－建立圖紙"))
                {
                    tx.Start();
                    sheet = builder.CreateSheetShell(titleBlockWindow.SelectedTitleBlockTypeId);
                    tx.Commit();
                }

                var textSizeFeet = UnitUtils.ConvertToInternalUnits(titleBlockWindow.TextSizeMillimeters, UnitTypeId.Millimeters);

                var paperBounds = builder.GetTitleBlockBounds(sheet);
                var paperWidth = paperBounds.Max.X - paperBounds.Min.X;
                var paperHeight = paperBounds.Max.Y - paperBounds.Min.Y;
                var (planWidth, planHeight) = Article164DrawingBuilder.ComputePlanBoxSize(paperWidth, paperHeight);
                var (legendWidth, legendHeight) = builder.EstimateLegendBoxSize(textSizeFeet);

                var previewWindow = new Article164LayoutPreviewWindow(paperWidth, paperHeight, planWidth, planHeight, legendWidth, legendHeight);
                if (previewWindow.ShowDialog() != true) return Result.Cancelled;

                double actualLegendWidth, actualLegendHeight;
                using (var tx = new Transaction(doc, "第164條圖說製作－排版"))
                {
                    tx.Start();
                    var scale = builder.ComputePlanViewScale(planWidth, planHeight);
                    builder.ApplyPlanViewScale(scale);
                    var (legendView, actualWidth, actualHeight) = builder.CreateLegendView(textSizeFeet);
                    actualLegendWidth = actualWidth;
                    actualLegendHeight = actualHeight;
                    var planView = builder.GetPlanView();

                    var planCenter = new XYZ(paperBounds.Min.X + previewWindow.PlanCenter.X, paperBounds.Min.Y + previewWindow.PlanCenter.Y, 0);
                    var legendCenter = new XYZ(paperBounds.Min.X + previewWindow.LegendCenter.X, paperBounds.Min.Y + previewWindow.LegendCenter.Y, 0);
                    Viewport.Create(doc, sheet.Id, planView.Id, planCenter);
                    Viewport.Create(doc, sheet.Id, legendView.Id, legendCenter);
                    tx.Commit();
                }

                var sizeMismatch = actualLegendWidth > legendWidth * 1.15 || actualLegendHeight > legendHeight * 1.15;
                var overflowNote = sizeMismatch
                    ? "\n\n提醒：圖例實際大小比排版預覽時的估計大，可能與平面圖重疊，建議重新執行圖說製作並在預覽視窗預留更大的圖例空間。"
                    : string.Empty;
                TaskDialog.Show("圖說製作", "已建立第164條檢討圖說（平面視圖＋圖例＋圖紙）。" + overflowNote);
                return Result.Succeeded;
            }
            catch (System.Exception ex)
            {
                message = ex.Message;
                return Result.Failed;
            }
        }
    }
}
