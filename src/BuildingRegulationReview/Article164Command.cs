using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using Rhino.Geometry;
using DBLine = Autodesk.Revit.DB.Line;
using RCurve = Rhino.Geometry.Curve;

namespace BuildingRegulationReview
{
    [Transaction(TransactionMode.Manual)]
    public sealed class Article164Command : IExternalCommand
    {
        private const double Slope = 3.6;
        private const string SelectionSetName = "164條_外牆與樓板";
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
            => Run(data.Application, ref message);

        internal static Result Run(UIApplication application, ref string message)
        {
            var uiDoc = application.ActiveUIDocument;
            var doc = uiDoc?.Document;
            if (doc == null || !(doc.ActiveView is ViewPlan)) { message = "請先開啟要繪製檢討圖的平面視圖。"; return Result.Failed; }
            try
            {
                var (shadowElements, elementSource) = CollectShadowSourceElements(doc, (ViewPlan)doc.ActiveView);
                if (shadowElements.Count == 0)
                {
                    message = elementSource.StartsWith("Selection Set")
                        ? $"名稱為「{SelectionSetName}」的 Selection Set 內沒有牆或樓板元件。請確認其中已加入要納入檢討的牆／樓板，或先刪除/清空該 Selection Set 以改用自動判斷。"
                        : "找不到外牆或樓板。請確認：牆類型的 Function（Identity Data）設為 Exterior，或自訂參數 Exterior Wall = Yes；" +
                          "也可以先在模型中建立名稱為「" + SelectionSetName + "」的 Selection Set 指定要納入的牆與樓板。" +
                          "若目前視圖沒有裁剪範圍（Crop Region），請先裁剪至基地範圍再執行，以避免抓到不相關的元件。";
                    return Result.Failed;
                }

                var typeGroups = shadowElements
                    .GroupBy(e => e.GetTypeId())
                    .Select(g =>
                    {
                        var sample = g.First();
                        return new Article164ElementTypeGroup
                        {
                            Category = sample is Wall ? "牆" : sample is Floor ? "樓板" : sample.Category?.Name ?? "其他",
                            TypeName = doc.GetElement(g.Key)?.Name ?? "(未命名類型)",
                            TypeId = g.Key,
                            Count = g.Count(),
                        };
                    }).ToList();
                var filterWindow = new Article164ElementFilterWindow(typeGroups, elementSource);
                if (filterWindow.ShowDialog() != true) return Result.Cancelled;
                shadowElements = shadowElements.Where(e => !filterWindow.ExcludedTypeIds.Contains(e.GetTypeId())).ToList();
                if (shadowElements.Count == 0) { message = "已取消勾選所有類型，沒有可運算的牆／樓板。"; return Result.Failed; }

                var lineRef = uiDoc.Selection.PickObject(ObjectType.Element, new StraightCurveFilter(), "選取代表建築線的直線");
                var roadPoint = uiDoc.Selection.PickPoint("在建築線的道路側點一下，以判定投影方向");
                var options = new Article164OptionsWindow();
                if (options.ShowDialog() != true) return Result.Cancelled;

                var curveElement = (CurveElement)doc.GetElement(lineRef);
                var line = curveElement.GeometryCurve as DBLine;
                if (line == null) { message = "建築線必須是直線。"; return Result.Failed; }
                var start = line.GetEndPoint(0); var end = line.GetEndPoint(1);
                var along = (end - start).Normalize();
                var normal = new XYZ(-along.Y, along.X, 0);
                if ((roadPoint - start).DotProduct(normal) < 0) normal = -normal;
                var baseZ = GetViewElevation((ViewPlan)doc.ActiveView);

                var triangles = new List<RCurve>();
                foreach (var element in shadowElements)
                    CollectProjectedTriangles(element.get_Geometry(new Options { DetailLevel = ViewDetailLevel.Fine }),
                        Autodesk.Revit.DB.Transform.Identity, baseZ, normal, triangles);
                if (triangles.Count == 0) { message = "選取物件沒有可運算的實體幾何。"; return Result.Failed; }

                var tolerance = doc.Application.ShortCurveTolerance;
                var shadow = RCurve.CreateBooleanUnion(triangles, tolerance)?.Where(c => c.IsClosed).ToArray() ?? Array.Empty<RCurve>();
                if (shadow.Length == 0) { message = "Rhino 無法建立道路陰影封閉區域。"; return Result.Failed; }

                var footprintTriangles = new List<RCurve>();
                foreach (var element in shadowElements)
                    CollectProjectedTriangles(element.get_Geometry(new Options { DetailLevel = ViewDetailLevel.Fine }),
                        Autodesk.Revit.DB.Transform.Identity, baseZ, XYZ.Zero, footprintTriangles);
                var footprint = RCurve.CreateBooleanUnion(footprintTriangles, tolerance)?.Where(c => c.IsClosed).ToArray() ?? Array.Empty<RCurve>();
                if (footprint.Length == 0) { message = "Rhino 無法建立建物平面投影封閉區域。"; return Result.Failed; }

                var roadWidth = UnitUtils.ConvertToInternalUnits(options.RoadWidthMeters, UnitTypeId.Meters);
                var roadBoundary = Rectangle(start, end, normal, roadWidth);
                var roadCurve = ToRhino(roadBoundary);
                var roadShadow = shadow.SelectMany(c => RCurve.CreateBooleanIntersection(c, roadCurve, tolerance) ?? Array.Empty<RCurve>())
                    .Where(c => c.IsClosed).ToArray();
                var shadowArea = roadShadow.Sum(c => AreaMassProperties.Compute(c)?.Area ?? 0);
                var allowedArea = start.DistanceTo(end) * roadWidth * (options.HasPermanentOpenSpace ? 1.0 : 0.5);
                var exceedsOppositeBoundary = shadow.Any(c => MaxRoadDepth(c, start, normal) > roadWidth + tolerance);
                var compliant = shadowArea <= allowedArea + tolerance * tolerance && !exceedsOppositeBoundary;

                var result = new Article164ReviewSession.Result
                {
                    Compliant = compliant,
                    FootprintSilhouette = footprint,
                    ShadowSilhouette = roadShadow,
                    LineStart = start,
                    LineEnd = end,
                    RoadWidthInternal = roadWidth,
                    HasPermanentOpenSpace = options.HasPermanentOpenSpace,
                    ShadowAreaInternal = shadowArea,
                    AllowedAreaInternal = allowedArea,
                    LevelId = ((ViewPlan)doc.ActiveView).GenLevel?.Id ?? ElementId.InvalidElementId,
                    BaseZ = baseZ,
                    SourceViewId = doc.ActiveView.Id,
                };

                using (var tx = new Transaction(doc, "建築技術規則第164條檢討圖"))
                {
                    tx.Start();
                    var (planViewId, footprintTypeId, shadowTypeId) = Article164PlanViewBuilder.Create(doc, result);
                    result.PlanViewId = planViewId;
                    result.FootprintTypeId = footprintTypeId;
                    result.ShadowTypeId = shadowTypeId;
                    tx.Commit();
                }
                Article164ReviewSession.SetResult(doc, result);

                var status = compliant ? "符合" : "不符合";
                TaskDialog.Show("第164條檢討結果", $"結果：{status}\n元件來源：{elementSource}\n讀取牆／樓板：{shadowElements.Count} 個\n陰影面積 As：{ToSquareMeters(shadowArea):0.##} m²\n容許面積：{ToSquareMeters(allowedArea):0.##} m²\n越過道路對側境界：{(exceedsOppositeBoundary ? "是" : "否")}\n\n已在「{Article164PlanViewBuilder.PlanViewName}」視圖建立本次檢討圖說（已覆蓋先前結果）。");
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex) { message = ex.ToString(); return Result.Failed; }
        }

        private static double GetViewElevation(ViewPlan view) => view.GenLevel?.Elevation ?? view.Origin.Z;
        private static double ToSquareMeters(double squareFeet) => squareFeet * 0.09290304;

        private static (List<Element> Elements, string Source) CollectShadowSourceElements(Document document, ViewPlan activeView)
        {
            var whitelist = new FilteredElementCollector(document)
                .OfClass(typeof(SelectionFilterElement))
                .Cast<SelectionFilterElement>()
                .FirstOrDefault(set => set.Name == SelectionSetName);
            if (whitelist != null)
            {
                var elements = whitelist.GetElementIds().Select(document.GetElement)
                    .Where(e => e is Wall || e is Floor).ToList();
                return (elements, "Selection Set「" + SelectionSetName + "」");
            }

            var outline = activeView.CropBoxActive ? GetPlanOutline(activeView) : null;
            var walls = CollectByCategory<Wall>(document, outline).Where(wall => IsExteriorWall(document, wall));
            var floors = CollectByCategory<Floor>(document, outline);
            var combined = walls.Cast<Element>().Concat(floors.Cast<Element>()).ToList();
            return (combined, "自動判斷");
        }

        private static IEnumerable<T> CollectByCategory<T>(Document document, Outline outline) where T : Element
        {
            var collector = new FilteredElementCollector(document).OfClass(typeof(T)).WhereElementIsNotElementType();
            if (outline != null) collector = collector.WherePasses(new BoundingBoxIntersectsFilter(outline));
            return collector.Cast<T>();
        }

        private static bool IsExteriorWall(Document document, Wall wall)
        {
            var wallType = document.GetElement(wall.GetTypeId());
            var function = wallType?.get_Parameter(BuiltInParameter.FUNCTION_PARAM);
            if (function != null && function.StorageType == StorageType.Integer &&
                function.AsInteger() == (int)WallFunction.Exterior) return true;
            return IsYes(wallType?.LookupParameter("Exterior Wall")) || IsYes(wall.LookupParameter("Exterior Wall"));
        }

        private static Outline GetPlanOutline(ViewPlan view)
        {
            var box = view.CropBox;
            var transform = box.Transform;
            var corners = new[]
            {
                new XYZ(box.Min.X, box.Min.Y, 0), new XYZ(box.Max.X, box.Min.Y, 0),
                new XYZ(box.Min.X, box.Max.Y, 0), new XYZ(box.Max.X, box.Max.Y, 0),
            }.Select(transform.OfPoint).ToList();
            const double unboundedZ = 1e4; // 遠大於任何合理建築高度，但仍在 Outline/BoundingBoxIntersectsFilter 的安全座標範圍內
            var min = new XYZ(corners.Min(p => p.X), corners.Min(p => p.Y), -unboundedZ);
            var max = new XYZ(corners.Max(p => p.X), corners.Max(p => p.Y), unboundedZ);
            return new Outline(min, max);
        }

        private static bool IsYes(Parameter parameter)
        {
            if (parameter == null) return false;
            if (parameter.StorageType == StorageType.Integer) return parameter.AsInteger() == 1;
            var value = (parameter.AsString() ?? parameter.AsValueString() ?? string.Empty).Trim();
            return value.Equals("yes", StringComparison.OrdinalIgnoreCase) ||
                   value.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                   value == "1" || value == "是";
        }
        private static double MaxRoadDepth(RCurve curve, XYZ origin, XYZ normal)
        {
            if (!curve.TryGetPolyline(out var points)) return double.NegativeInfinity;
            return points.Max(p => (p.X - origin.X) * normal.X + (p.Y - origin.Y) * normal.Y);
        }

        private static void CollectProjectedTriangles(GeometryElement geometry, Autodesk.Revit.DB.Transform transform,
            double baseZ, XYZ direction, ICollection<RCurve> result)
        {
            if (geometry == null) return;
            foreach (var obj in geometry)
            {
                if (obj is GeometryInstance instance)
                { CollectProjectedTriangles(instance.GetInstanceGeometry(), transform, baseZ, direction, result); continue; }
                if (!(obj is Solid solid) || solid.Faces.IsEmpty) continue;
                foreach (Face face in solid.Faces)
                {
                    var mesh = face.Triangulate();
                    for (var i = 0; i < mesh.NumTriangles; i++)
                    {
                        var tri = mesh.get_Triangle(i);
                        var points = new List<Point3d>(4);
                        for (var j = 0; j < 3; j++)
                        {
                            var p = transform.OfPoint(tri.get_Vertex(j));
                            var h = Math.Max(0, p.Z - baseZ);
                            points.Add(new Point3d(p.X + direction.X * h / Slope, p.Y + direction.Y * h / Slope, 0));
                        }
                        points.Add(points[0]);
                        var polyline = new Polyline(points);
                        var twiceArea = Math.Abs((points[1].X - points[0].X) * (points[2].Y - points[0].Y) -
                                                 (points[1].Y - points[0].Y) * (points[2].X - points[0].X));
                        if (polyline.IsValid && twiceArea > 1e-10) result.Add(new PolylineCurve(polyline));
                    }
                }
            }
        }

        private static CurveLoop Rectangle(XYZ a, XYZ b, XYZ n, double width)
        {
            var loop = new CurveLoop();
            var a2 = a + n * width; var b2 = b + n * width;
            loop.Append(DBLine.CreateBound(a, b)); loop.Append(DBLine.CreateBound(b, b2));
            loop.Append(DBLine.CreateBound(b2, a2)); loop.Append(DBLine.CreateBound(a2, a)); return loop;
        }
        private static RCurve ToRhino(CurveLoop loop)
        {
            var pts = loop.Select(c => c.GetEndPoint(0)).Select(p => new Point3d(p.X, p.Y, 0)).ToList();
            pts.Add(pts[0]); return new PolylineCurve(pts);
        }
        internal static CurveLoop ToRevitLoop(RCurve curve, double z, double minSegmentLength)
        {
            var poly = curve.ToPolyline(0, 0, 0.01, 0.1, 0, 0, 0, 0, true);
            if (poly == null || poly.PointCount < 4) return null;
            var loop = new CurveLoop();
            for (var i = 1; i < poly.PointCount; i++)
            {
                var a = poly.Point(i - 1); var b = poly.Point(i);
                if (a.DistanceTo(b) > minSegmentLength) loop.Append(DBLine.CreateBound(new XYZ(a.X, a.Y, z), new XYZ(b.X, b.Y, z)));
            }
            return loop;
        }
        private sealed class StraightCurveFilter : ISelectionFilter
        {
            public bool AllowElement(Element element) => element is CurveElement ce && ce.GeometryCurve is DBLine;
            public bool AllowReference(Reference reference, XYZ position) => false;
        }
    }
}
