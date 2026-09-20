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
        private static FireReviewSetupWindow _window;
        private static ExternalEvent _externalEvent;
        private static SetupEventHandler _handler;

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
            => Run(commandData.Application, ref message);

        internal static Result Run(UIApplication application, ref string message)
        {
            var document = application.ActiveUIDocument?.Document;
            if (document == null) { message = "請先開啟 Revit 專案。"; return Result.Failed; }
            var catalog = RevitReviewSetupCatalog.Read(document);
            if (catalog.FloorPlans.Count == 0)
            {
                TaskDialog.Show("防火區劃設定", "專案必須至少有一個樓層平面。");
                return Result.Cancelled;
            }
            if (_window != null)
            {
                _window.Activate();
                return Result.Succeeded;
            }
            _window = new FireReviewSetupWindow(catalog);
            new System.Windows.Interop.WindowInteropHelper(_window).Owner = application.MainWindowHandle;
            _handler = new SetupEventHandler(_window);
            _externalEvent = ExternalEvent.Create(_handler);
            _window.OpenAreaComputations = () => { _handler.Request = SetupRequest.OpenComputations; _externalEvent.Raise(); };
            _window.RefreshAreaSchemes = () => { _handler.Request = SetupRequest.RefreshSchemes; _externalEvent.Raise(); };
            _window.CreateAreaPlans = () => { _handler.Request = SetupRequest.CreateAreaPlans; _externalEvent.Raise(); };
            _window.Activated += (_, __) =>
            {
                if (_handler.Request != SetupRequest.None) return;
                _handler.Request = SetupRequest.RefreshSchemes;
                _externalEvent.Raise();
            };
            _window.Closed += (_, __) =>
            {
                _externalEvent.Dispose();
                _externalEvent = null;
                _handler = null;
                _window = null;
            };
            _window.Show();
            return Result.Succeeded;
        }

        private static Result Apply(UIApplication application, System.Collections.Generic.IReadOnlyList<ReviewPackageSetupSelection> selections, ref string message)
        {
            var document = application.ActiveUIDocument?.Document;
            if (document == null) { message = "請先開啟 Revit 專案。"; return Result.Failed; }
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

        private enum SetupRequest { None, OpenComputations, CreateAreaPlans, RefreshSchemes }

        private sealed class SetupEventHandler : IExternalEventHandler
        {
            private readonly FireReviewSetupWindow _setupWindow;
            public SetupRequest Request { get; set; }
            public SetupEventHandler(FireReviewSetupWindow setupWindow) { _setupWindow = setupWindow; }
            public string GetName() => "防火區劃設定";
            public void Execute(UIApplication application)
            {
                var request = Request;
                Request = SetupRequest.None;
                if (request == SetupRequest.OpenComputations)
                {
                    var commandId = RevitCommandId.LookupPostableCommandId(PostableCommand.AreaAndVolumeComputations);
                    if (commandId == null || !application.CanPostCommand(commandId))
                        TaskDialog.Show("防火區劃設定", "目前無法開啟 Revit 的 Area and Volume Computations。請從「建築 > 房間及面積」開啟。");
                    else application.PostCommand(commandId);
                }
                else if (request == SetupRequest.RefreshSchemes)
                {
                    var document = application.ActiveUIDocument?.Document;
                    if (document != null) _setupWindow.RefreshSchemes(RevitReviewSetupCatalog.Read(document));
                }
                else if (request == SetupRequest.CreateAreaPlans)
                {
                    var message = "";
                    if (Apply(application, _setupWindow.PendingSelections, ref message) == Result.Succeeded)
                        _setupWindow.Dispatcher.BeginInvoke(new Action(() => _setupWindow.Close()));
                }
            }
        }
    }
}
