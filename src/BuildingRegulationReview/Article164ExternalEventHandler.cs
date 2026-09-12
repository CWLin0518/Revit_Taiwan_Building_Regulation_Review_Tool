using Autodesk.Revit.UI;

namespace BuildingRegulationReview
{
    internal sealed class Article164ExternalEventHandler : IExternalEventHandler
    {
        public void Execute(UIApplication application)
        {
            var message = string.Empty;
            var result = Article164Command.Run(application, ref message);
            if (result == Result.Failed)
                TaskDialog.Show("第164條檢討", string.IsNullOrWhiteSpace(message) ? "檢討執行失敗。" : message);
        }

        public string GetName() => "建築技術規則第164條道路陰影檢討";
    }
}
