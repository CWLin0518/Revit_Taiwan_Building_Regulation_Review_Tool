using System;
using System.Linq;
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
            => Run(commandData.Application, ref message);

        internal static Result Run(UIApplication application, ref string message)
        {
            var document = application.ActiveUIDocument?.Document;
            if (document == null) { message = "請先開啟 Revit 專案。"; return Result.Failed; }
            var catalog = RevitReviewSetupCatalog.Read(document);
            if (catalog.FloorPlans.Count == 0 || catalog.AreaSchemes.Count == 0)
            {
                TaskDialog.Show("防火區劃設定", "專案必須至少有一個樓層平面與一個面積配置。Revit 2024 API 不提供建立 Area Scheme 的公開方法，請先由「建築 > 房間及面積 > 面積配置」建立後再執行設定。");
                return Result.Cancelled;
            }
            var window = new FireReviewSetupWindow(catalog);
            if (window.ShowDialog() != true) return Result.Cancelled;
            var selections = window.Selections;
            var repository = new RevitReviewPackageRepository(document);
            var existing = repository.GetAll();
            foreach (var selection in selections)
            {
                if (existing.Count(x => x.SourceFloorPlanUniqueId == selection.SourceFloorPlanUniqueId &&
                    x.AreaSchemeUniqueId == selection.AreaSchemeUniqueId) <= 1) continue;
                message = "所選樓層平面與面積配置已有多個檢討套件，請先清理重複資料。";
                TaskDialog.Show("防火區劃設定", message);
                return Result.Failed;
            }
            try
            {
                var created = 0; var reused = 0;
                var warnings = new System.Collections.Generic.List<string>();
                using (var group = new TransactionGroup(document, "建立防火區劃檢討視圖"))
                {
                    group.Start();
                    foreach (var selection in selections)
                    {
                        var package = existing.FirstOrDefault(x => x.SourceFloorPlanUniqueId == selection.SourceFloorPlanUniqueId &&
                            x.AreaSchemeUniqueId == selection.AreaSchemeUniqueId) ?? ReviewPackageSetup.Create(selection);
                        using (var transaction = new Transaction(document, "儲存防火區劃檢討設定"))
                        {
                            transaction.Start();
                            repository.Save(package);
                            new RevitReviewSetupOptionsRepository(document).Save(package.PackageId, selection);
                            if (transaction.Commit() != TransactionStatus.Committed)
                                throw new InvalidOperationException("設定交易未能提交。");
                        }
                        AreaPlanProvisioningResult provisioned = new RevitAreaPlanProvisioner(document).Provision(package, selection);
                        if (provisioned.Created) created++;
                        else { reused++; warnings.Add("既有 Area Plan 的樣板、裁切與 Scope Box 設定未重新套用。"); }
                        warnings.AddRange(provisioned.Warnings);
                    }
                    if (group.Assimilate() != TransactionStatus.Committed)
                        throw new InvalidOperationException("防火區劃檢討視圖交易未能提交。");
                }
                var warningText = warnings.Count == 0 ? "" : "\n警告：\n- " + string.Join("\n- ", warnings.Distinct());
                TaskDialog.Show("防火區劃設定", $"已處理 {selections.Count} 個樓層平面：建立 {created} 個 Area Plan，重用 {reused} 個。{warningText}");
                return Result.Succeeded;
            }
            catch (Exception exception)
            {
                message = "建立 Area Plan 失敗：" + exception.Message;
                TaskDialog.Show("防火區劃設定", message);
                return Result.Failed;
            }
        }
    }
}
