using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using BuildingRegulationReview.Application.ProjectSetup;
using BuildingRegulationReview.Revit.ProjectSetup;
using BuildingRegulationReview.Revit.ReviewPackages;

namespace BuildingRegulationReview
{
    [Transaction(TransactionMode.Manual)]
    public sealed class FireReviewSetupCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var document = commandData.Application.ActiveUIDocument?.Document;
            if (document == null) { message = "請先開啟 Revit 專案。"; return Result.Failed; }
            var catalog = RevitReviewSetupCatalog.Read(document);
            if (catalog.FloorPlans.Count == 0 || catalog.AreaSchemes.Count == 0)
            {
                TaskDialog.Show("防火區劃設定", "專案必須至少有一個樓層平面與一個面積配置。Revit 2024 API 不提供建立 Area Scheme 的公開方法，請先由「建築 > 房間及面積 > 面積配置」建立後再執行設定。");
                return Result.Cancelled;
            }
            var window = new FireReviewSetupWindow(catalog);
            if (window.ShowDialog() != true) return Result.Cancelled;
            var package = ReviewPackageSetup.Create(window.Selection);
            using (var transaction = new Transaction(document, "建立防火區劃檢討設定"))
            {
                transaction.Start();
                new RevitReviewPackageRepository(document).Save(package);
                new RevitReviewSetupOptionsRepository(document).Save(package.PackageId, window.Selection);
                transaction.Commit();
            }
            TaskDialog.Show("防火區劃設定", $"設定已儲存。\nPackage ID: {package.PackageId:D}\nArea Plan 將於下一步建立。");
            return Result.Succeeded;
        }
    }
}
