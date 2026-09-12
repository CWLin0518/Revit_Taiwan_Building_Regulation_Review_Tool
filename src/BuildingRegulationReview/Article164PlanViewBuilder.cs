using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using DBLine = Autodesk.Revit.DB.Line;
using RCurve = Rhino.Geometry.Curve;

namespace BuildingRegulationReview
{
    internal static class Article164PlanViewBuilder
    {
        public const string PlanViewName = "日照陰影檢討圖";

        public static (ElementId PlanViewId, ElementId FootprintTypeId, ElementId ShadowTypeId) Create(
            Document document, Article164ReviewSession.Result run)
        {
            DeleteExistingViewByName<ViewPlan>(document, PlanViewName);

            var footprintTypeId = Article164FilledRegionWriter.GetOrCreateFilledRegionType(
                document, "164條－本案新建建物", new Color(160, 160, 160));
            var shadowTypeId = run.Compliant
                ? Article164FilledRegionWriter.GetOrCreateFilledRegionType(
                    document, "164條－符合", new Color(160, 160, 160), diagonalHatch: true)
                : Article164FilledRegionWriter.GetOrCreateFilledRegionType(
                    document, "164條－不符合", new Color(225, 65, 65));

            var levelId = run.LevelId != ElementId.InvalidElementId
                ? run.LevelId
                : new FilteredElementCollector(document).OfClass(typeof(Level)).FirstElementId();
            var viewFamilyTypeId = new FilteredElementCollector(document).OfClass(typeof(ViewFamilyType))
                .Cast<ViewFamilyType>().First(v => v.ViewFamily == ViewFamily.FloorPlan).Id;
            var view = ViewPlan.Create(document, viewFamilyTypeId, levelId);
            view.Name = PlanViewName;
            var sourceView = run.SourceViewId != null && run.SourceViewId != ElementId.InvalidElementId
                ? document.GetElement(run.SourceViewId) as View
                : null;
            ApplySourceViewProperties(document, view, sourceView);

            HideModelCategories(document, view);

            var minSegmentLength = document.Application.ShortCurveTolerance;
            var footprintLoops = ToLoops(run.FootprintSilhouette, run.BaseZ, minSegmentLength);
            var shadowLoops = ToLoops(run.ShadowSilhouette, run.BaseZ, minSegmentLength);
            CreateFilledRegions(document, view.Id, footprintLoops, footprintTypeId);
            CreateFilledRegions(document, view.Id, shadowLoops, shadowTypeId);
            document.Create.NewDetailCurve(view, DBLine.CreateBound(run.LineStart, run.LineEnd));
            LabelShadowAreas(document, view.Id, shadowLoops);

            ApplyCropBox(view, sourceView, footprintLoops.Concat(shadowLoops), run);
            return (view.Id, footprintTypeId, shadowTypeId);
        }

        internal static void DeleteExistingViewByName<T>(Document document, string name) where T : View
        {
            var existing = new FilteredElementCollector(document).OfClass(typeof(T)).Cast<T>()
                .Where(v => !v.IsTemplate && v.Name == name).Select(v => v.Id).ToList();
            if (existing.Count > 0) document.Delete(existing);
        }

        private static void ApplyCropBox(ViewPlan view, View sourceView, IEnumerable<CurveLoop> loops, Article164ReviewSession.Result run)
        {
            if (sourceView != null && sourceView.CropBoxActive)
            {
                view.CropBoxActive = true;
                view.CropBoxVisible = false;
                view.CropBox = ProjectCropBox(sourceView.CropBox, view.CropBox);
                return;
            }

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

        private static BoundingBoxXYZ ProjectCropBox(BoundingBoxXYZ sourceBox, BoundingBoxXYZ targetDefaultBox)
        {
            var toWorld = sourceBox.Transform;
            var corners = new[]
            {
                new XYZ(sourceBox.Min.X, sourceBox.Min.Y, 0),
                new XYZ(sourceBox.Max.X, sourceBox.Min.Y, 0),
                new XYZ(sourceBox.Min.X, sourceBox.Max.Y, 0),
                new XYZ(sourceBox.Max.X, sourceBox.Max.Y, 0),
            }.Select(toWorld.OfPoint).ToList();

            var toLocal = targetDefaultBox.Transform.Inverse;
            var localPoints = corners.Select(toLocal.OfPoint).ToList();

            var min = new XYZ(localPoints.Min(p => p.X), localPoints.Min(p => p.Y), targetDefaultBox.Min.Z);
            var max = new XYZ(localPoints.Max(p => p.X), localPoints.Max(p => p.Y), targetDefaultBox.Max.Z);
            return new BoundingBoxXYZ { Transform = targetDefaultBox.Transform, Min = min, Max = max };
        }

        private static void ApplySourceViewProperties(Document document, ViewPlan view, View sourceView)
        {
            if (sourceView == null) return;

            var templateId = sourceView.ViewTemplateId;
            if (templateId != null && templateId != ElementId.InvalidElementId &&
                document.GetElement(templateId) is View template && template.IsTemplate)
            {
                try { view.ViewTemplateId = templateId; }
                catch (Autodesk.Revit.Exceptions.ApplicationException) { }
            }

            try { view.Scale = sourceView.Scale; } catch (Autodesk.Revit.Exceptions.ApplicationException) { }
            try { view.DetailLevel = sourceView.DetailLevel; } catch (Autodesk.Revit.Exceptions.ApplicationException) { }
            try { view.DisplayStyle = sourceView.DisplayStyle; } catch (Autodesk.Revit.Exceptions.ApplicationException) { }
            try { view.Discipline = sourceView.Discipline; } catch (Autodesk.Revit.Exceptions.ApplicationException) { }
            CopyParameter(sourceView, view, BuiltInParameter.VIEW_PHASE);
            CopyParameter(sourceView, view, BuiltInParameter.VIEW_PHASE_FILTER);
        }

        private static void CopyParameter(View source, View target, BuiltInParameter parameter)
        {
            var sourceParam = source.get_Parameter(parameter);
            var targetParam = target.get_Parameter(parameter);
            if (sourceParam == null || targetParam == null || targetParam.IsReadOnly) return;
            if (sourceParam.StorageType != targetParam.StorageType) return;
            try
            {
                switch (sourceParam.StorageType)
                {
                    case StorageType.Double: targetParam.Set(sourceParam.AsDouble()); break;
                    case StorageType.Integer: targetParam.Set(sourceParam.AsInteger()); break;
                    case StorageType.ElementId: targetParam.Set(sourceParam.AsElementId()); break;
                    case StorageType.String: targetParam.Set(sourceParam.AsString()); break;
                }
            }
            catch (Autodesk.Revit.Exceptions.ApplicationException) { }
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

        private static void LabelShadowAreas(Document document, ElementId viewId, IReadOnlyList<CurveLoop> shadowLoops)
        {
            if (shadowLoops.Count == 0) return;
            var textTypeId = GetOrCreateAreaLabelTextType(document);
            foreach (var loop in shadowLoops)
            {
                var (area, centroid) = ComputePolygonAreaAndCentroid(loop);
                if (area <= 0) continue;
                var areaSquareMeters = area * 0.09290304;
                var note = TextNote.Create(document, viewId, centroid, $"{areaSquareMeters:0.##} m²", textTypeId);
                note.HorizontalAlignment = HorizontalTextAlignment.Center;
                note.VerticalAlignment = VerticalTextAlignment.Middle;
            }
        }

        private static ElementId GetOrCreateAreaLabelTextType(Document document)
        {
            const string name = "164條－陰影面積標註";
            var types = new FilteredElementCollector(document).OfClass(typeof(TextNoteType)).Cast<TextNoteType>().ToList();
            var type = types.FirstOrDefault(t => t.Name == name) ?? (TextNoteType)types.First().Duplicate(name);
            type.get_Parameter(BuiltInParameter.TEXT_SIZE).Set(UnitUtils.ConvertToInternalUnits(2.5, UnitTypeId.Millimeters));
            return type.Id;
        }

        private static (double Area, XYZ Centroid) ComputePolygonAreaAndCentroid(CurveLoop loop)
        {
            var points = loop.Select(c => c.GetEndPoint(0)).ToList();
            double signedArea = 0, cx = 0, cy = 0;
            for (var i = 0; i < points.Count; i++)
            {
                var p0 = points[i];
                var p1 = points[(i + 1) % points.Count];
                var cross = p0.X * p1.Y - p1.X * p0.Y;
                signedArea += cross;
                cx += (p0.X + p1.X) * cross;
                cy += (p0.Y + p1.Y) * cross;
            }
            signedArea *= 0.5;
            if (Math.Abs(signedArea) < 1e-9)
                return (0, new XYZ(points.Average(p => p.X), points.Average(p => p.Y), points[0].Z));

            cx /= 6 * signedArea;
            cy /= 6 * signedArea;
            return (Math.Abs(signedArea), new XYZ(cx, cy, points[0].Z));
        }
    }
}
