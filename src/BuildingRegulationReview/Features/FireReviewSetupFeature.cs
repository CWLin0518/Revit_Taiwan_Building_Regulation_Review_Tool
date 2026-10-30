using Autodesk.Revit.UI;

namespace BuildingRegulationReview.Features
{
    internal sealed class FireReviewSetupFeature : IReviewFeature
    {
        public const string FeatureId = "fire-review-setup";
        public string Id => FeatureId;
        public string ReviewDialogTitle => "建築防火檢討";
        public string DrawingDialogTitle => "建築防火檢討";
        public bool SupportsDrawing => false;
        public Result RunReview(UIApplication application, ref string message) =>
            FireReviewSetupCommand.Run(application, ref message);
        public Result RunDrawing(UIApplication application, ref string message) => Result.Cancelled;
    }
}
