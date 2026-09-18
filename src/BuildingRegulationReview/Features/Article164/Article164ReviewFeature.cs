using Autodesk.Revit.UI;

namespace BuildingRegulationReview.Features.Article164
{
    internal sealed class Article164ReviewFeature : IReviewFeature
    {
        public const string FeatureId = "article-164-road-shadow";

        public string Id => FeatureId;
        public string ReviewDialogTitle => "第164條檢討";
        public string DrawingDialogTitle => "第164條圖說製作";
        public bool SupportsDrawing => true;

        public Result RunReview(UIApplication application, ref string message) =>
            Article164Command.Run(application, ref message);

        public Result RunDrawing(UIApplication application, ref string message) =>
            Article164DrawingCommand.Run(application, ref message);
    }
}
