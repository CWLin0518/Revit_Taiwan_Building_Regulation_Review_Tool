using Autodesk.Revit.UI;

namespace BuildingRegulationReview.Features
{
    internal interface IReviewFeature
    {
        string Id { get; }
        string ReviewDialogTitle { get; }
        string DrawingDialogTitle { get; }
        bool SupportsDrawing { get; }

        Result RunReview(UIApplication application, ref string message);
        Result RunDrawing(UIApplication application, ref string message);
    }
}
