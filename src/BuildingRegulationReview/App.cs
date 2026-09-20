using System;
using System.Reflection;
using Autodesk.Revit.UI;
using BuildingRegulationReview.ExternalEvents;
using BuildingRegulationReview.Features;
using BuildingRegulationReview.Features.Article164;

namespace BuildingRegulationReview
{
    public sealed class App : IExternalApplication
    {
        public static readonly DockablePaneId ReviewPaneId = new DockablePaneId(new Guid("AE47CF0C-CFE8-4803-A97C-381AF4F1E760"));
        private ReviewExternalEventDispatcher _eventDispatcher;

        public Result OnStartup(UIControlledApplication application)
        {
            const string tabName = "建築法規檢討";
            try { application.CreateRibbonTab(tabName); } catch (Autodesk.Revit.Exceptions.ArgumentException) { }
            var panel = application.CreateRibbonPanel(tabName, "法規檢討");
            var path = Assembly.GetExecutingAssembly().Location;
            panel.AddItem(new PushButtonData("OpenBuildingRegulationReview", "開啟檢討\n面板", path, typeof(ShowReviewPaneCommand).FullName));
            var button = (PushButton)panel.AddItem(new PushButtonData("ReviewArticle164", "第164條\n道路陰影", path, typeof(Article164Command).FullName));
            button.ToolTip = "以 RhinoCommon 計算 3.6:1 道路陰影，並在目前平面視圖建立 FilledRegion。";
            var setupButton = (PushButton)panel.AddItem(new PushButtonData("FireReviewSetup", "防火區劃\n設定", path, typeof(FireReviewSetupCommand).FullName));
            setupButton.ToolTip = "選擇來源樓層平面、面積配置與 Area Plan 選項，建立檢討套件。";

            var featureRegistry = new ReviewFeatureRegistry(new IReviewFeature[]
            {
                new Article164ReviewFeature(),
                new FireReviewSetupFeature(),
            });
            _eventDispatcher = new ReviewExternalEventDispatcher(featureRegistry);
            _eventDispatcher.Initialize();
            application.RegisterDockablePane(ReviewPaneId, "建築技術規則檢討",
                new ReviewPaneProvider(featureRegistry, _eventDispatcher));
            return Result.Succeeded;
        }

        public Result OnShutdown(UIControlledApplication application)
        {
            _eventDispatcher?.Dispose();
            _eventDispatcher = null;
            return Result.Succeeded;
        }
    }
}
