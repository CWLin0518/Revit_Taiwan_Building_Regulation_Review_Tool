using Autodesk.Revit.UI;

namespace BuildingRegulationReview
{
    public sealed class ReviewPaneProvider : IDockablePaneProvider
    {
        private readonly ReviewPaneControl _control;
        public ReviewPaneProvider(ExternalEvent article164Event, ExternalEvent article164DrawingEvent)
        {
            _control = new ReviewPaneControl(article164Event, article164DrawingEvent);
        }
        public void SetupDockablePane(DockablePaneProviderData data)
        {
            data.FrameworkElement = _control;
            data.InitialState = new DockablePaneState { DockPosition = DockPosition.Left };
        }
    }
}
