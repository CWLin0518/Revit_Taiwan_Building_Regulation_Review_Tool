using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using DBLine = Autodesk.Revit.DB.Line;
using RCurve = Rhino.Geometry.Curve;

namespace BuildingRegulationReview
{
    internal static class Article164PlanViewBuilder
    {
        public const string PlanViewName = "164條圖說－結果";

        public static (ElementId PlanViewId, ElementId FootprintTypeId, ElementId ShadowTypeId) Create(
            Document document, Article164ReviewSession.Result run)
        {
            DeleteExistingViewByName<ViewPlan>(document, PlanViewName);

            var footprintTypeId = Article164FilledRegionWriter.GetOrCreateFilledRegionType(
                document, "164條－本案新建建物", new Color(160, 160, 160));
            var shadowTypeId = Article164FilledRegionWriter.GetOrCreateFilledRegionType(
                document, run.Compliant ? "164條－符合" : "164條－不符合",
                run.Compliant ? new Color(70, 180, 90) : new Color(225, 65, 65));

            var levelId = run.LevelId != ElementId.InvalidElementId
                ? run.LevelId
                : new FilteredElementCollector(document).OfClass(typeof(Level)).FirstElementId();
            var viewFamilyTypeId = new FilteredElementCollector(document).OfClass(typeof(ViewFamilyType))
                .Cast<ViewFamilyType>().First(v => v.ViewFamily == ViewFamily.FloorPlan).Id;
            var view = ViewPlan.Create(document, viewFamilyTypeId, levelId);
            view.Name = PlanViewName;

            HideModelCategories(document, view);

            var minSegmentLength = document.Application.ShortCurveTolerance;
            var footprintLoops = ToLoops(run.FootprintSilhouette, run.BaseZ, minSegmentLength);
            var shadowLoops = ToLoops(run.ShadowSilhouette, run.BaseZ, minSegmentLength);
            CreateFilledRegions(document, view.Id, footprintLoops, footprintTypeId);
            CreateFilledRegions(document, view.Id, shadowLoops, shadowTypeId);
            document.Create.NewDetailCurve(view, DBLine.CreateBound(run.LineStart, run.LineEnd));

            ApplyCropBox(view, footprintLoops.Concat(shadowLoops), run);
            return (view.Id, footprintTypeId, shadowTypeId);
        }

        internal static void DeleteExistingViewByName<T>(Document document, string name) where T : View
        {
            var existing = new FilteredElementCollector(document).OfClass(typeof(T)).Cast<T>()
                .Where(v => !v.IsTemplate && v.Name == name).Select(v => v.Id).ToList();
            if (existing.Count > 0) document.Delete(existing);
        }

        private static void ApplyCropBox(ViewPlan view, IEnumerable<CurveLoop> loops, Article164ReviewSession.Result run)
        {
            var points = loops.SelectMany(l => l).SelectMany(c => new[] { c.GetEndPoint(0), c.GetEndPoint(1) })
                .Concat(new[] { run.LineStart, run.LineEnd }).ToList();
            if (points.Count == 0) return;
            const double margin = 5.0;
            var min = new XYZ(points.Min(p => p.X) - margin, points.Min(p => p.Y) - margin, run.BaseZ - 1);
            var max = new XYZ(points.Max(p => p.X) + margin, points.Max(p => p.Y) + margin, run.BaseZ + 1);
            view.CropBoxActive = true;
            view.CropBoxVisible = false;
            view.CropBox = new BoundingBoxXYZ { Min = min, Max = max };
        }

        private static void HideModelCategories(Document document, View view)
        {
            foreach (Category category in document.Settings.Categories)
            {
                if (category.CategoryType != CategoryType.Model) continue;
                if (!view.CanCategoryBeHidden(category.Id)) continue;
                view.SetCategoryHidden(category.Id, true);
            }
        }

        private static List<CurveLoop> ToLoops(IEnumerable<RCurve> curves, double z, double minSegmentLength) =>
            curves.Select(c => Article164Command.ToRevitLoop(c, z, minSegmentLength)).Where(l => l != null).ToList();

        private static void CreateFilledRegions(Document document, ElementId viewId, IEnumerable<CurveLoop> loops, ElementId typeId)
        {
            foreach (var loop in loops)
                FilledRegion.Create(document, typeId, viewId, new List<CurveLoop> { loop });
        }
    }
}
