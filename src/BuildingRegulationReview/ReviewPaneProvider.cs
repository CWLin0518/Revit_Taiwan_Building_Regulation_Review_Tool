using Autodesk.Revit.UI;
using BuildingRegulationReview.ExternalEvents;
using BuildingRegulationReview.Features;

namespace BuildingRegulationReview
{
    public sealed class ReviewPaneProvider : IDockablePaneProvider
    {
        private readonly ReviewPaneControl _control;
        internal ReviewPaneProvider(ReviewFeatureRegistry featureRegistry, ReviewExternalEventDispatcher eventDispatcher)
        {
            _control = new ReviewPaneControl(featureRegistry, eventDispatcher);
        }
        public void SetupDockablePane(DockablePaneProviderData data)
        {
            data.FrameworkElement = _control;
            data.InitialState = new DockablePaneState { DockPosition = DockPosition.Left };
        }
    }
}
