using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace BuildingRegulationReview
{
    [Transaction(TransactionMode.Manual)]
    public sealed class ShowReviewPaneCommand : IExternalCommand
    {
        public Result Execute(
            ExternalCommandData commandData,
            ref string message,
            ElementSet elements)
        {
            commandData.Application.GetDockablePane(App.ReviewPaneId).Show();
            return Result.Succeeded;
        }
    }
}
