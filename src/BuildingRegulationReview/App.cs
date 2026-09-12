using System;
using System.Reflection;
using Autodesk.Revit.UI;

namespace BuildingRegulationReview
{
    public sealed class App : IExternalApplication
    {
        public static readonly DockablePaneId ReviewPaneId = new DockablePaneId(new Guid("AE47CF0C-CFE8-4803-A97C-381AF4F1E760"));
        private ExternalEvent _article164Event;
        private ExternalEvent _article164DrawingEvent;

        public Result OnStartup(UIControlledApplication application)
        {
            const string tabName = "建築法規檢討";
            try { application.CreateRibbonTab(tabName); } catch (Autodesk.Revit.Exceptions.ArgumentException) { }
            var panel = application.CreateRibbonPanel(tabName, "法規檢討");
            var path = Assembly.GetExecutingAssembly().Location;
            panel.AddItem(new PushButtonData("OpenBuildingRegulationReview", "開啟檢討\n面板", path, typeof(ShowReviewPaneCommand).FullName));
            var button = (PushButton)panel.AddItem(new PushButtonData("ReviewArticle164", "第164條\n道路陰影", path, typeof(Article164Command).FullName));
            button.ToolTip = "以 RhinoCommon 計算 3.6:1 道路陰影，並在目前平面視圖建立 FilledRegion。";

            _article164Event = ExternalEvent.Create(new Article164ExternalEventHandler());
            _article164DrawingEvent = ExternalEvent.Create(new Article164DrawingExternalEventHandler());
            application.RegisterDockablePane(ReviewPaneId, "建築技術規則檢討",
                new ReviewPaneProvider(_article164Event, _article164DrawingEvent));
            return Result.Succeeded;
        }

        public Result OnShutdown(UIControlledApplication application)
        {
            _article164Event?.Dispose();
            _article164Event = null;
            _article164DrawingEvent?.Dispose();
            _article164DrawingEvent = null;
            return Result.Succeeded;
        }
    }
}
