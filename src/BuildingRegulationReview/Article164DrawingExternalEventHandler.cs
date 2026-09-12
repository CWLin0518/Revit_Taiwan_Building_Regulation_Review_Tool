using Autodesk.Revit.UI;

namespace BuildingRegulationReview
{
    internal sealed class Article164DrawingExternalEventHandler : IExternalEventHandler
    {
        public void Execute(UIApplication application)
        {
            var message = string.Empty;
            var result = Article164DrawingCommand.Run(application, ref message);
            if (result == Result.Failed)
                TaskDialog.Show("第164條圖說製作", string.IsNullOrWhiteSpace(message) ? "圖說製作失敗。" : message);
        }

        public string GetName() => "建築技術規則第164條圖說製作";
    }
}
