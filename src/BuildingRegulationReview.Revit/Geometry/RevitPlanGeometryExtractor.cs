using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using BuildingRegulationReview.Application.Geometry;
using BuildingRegulationReview.Domain.Common;
using BuildingRegulationReview.Domain.Geometry;

namespace BuildingRegulationReview.Revit.Geometry;

// P2-T02: the read-only Revit side of IPlanGeometryExtractor (spec 10.1). It opens no transaction
// and changes nothing in the model. Its job is narrow on purpose: resolve the Area Plan's scope,
// read wall centrelines, column outlines and auxiliary lines, and hand plan points plus their
// source element to PlanGeometryBuilder. Repair belongs to P2-T03, not here.
public sealed class RevitPlanGeometryExtractor : IPlanGeometryExtractor
{
    // A link element only counts when the extraction plane actually passes through it. The slack
    // keeps a slab-thickness difference between host and link levels from dropping a whole wall.
    private static readonly double LinkPlaneSlackFeet = PlanUnits.MillimetersToFeet(50.0);

    private static readonly BuiltInCategory[] ColumnCategories =
    {
        BuiltInCategory.OST_Columns,
        BuiltInCategory.OST_StructuralColumns
    };

    private readonly Document _document;
    private readonly HashSet<string> _auxiliaryLineStyleNames;

    /// <param name="auxiliaryLineStyleNames">
    /// Line styles the user reserved for boundary helper lines. Empty means every line visible in
    /// the Area Plan counts, which is the safe default until the setup UI exposes the choice.
    /// </param>
    public RevitPlanGeometryExtractor(Document document, IEnumerable<string>? auxiliaryLineStyleNames = null)
    {
        _document = document ?? throw new ArgumentNullException(nameof(document));
        _auxiliaryLineStyleNames = new HashSet<string>(
            (auxiliaryLineStyleNames ?? Array.Empty<string>())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim()),
            StringComparer.OrdinalIgnoreCase);
    }

    public Result<PlanGeometrySnapshot> Extract(PlanGeometryExtractionRequest request)
    {
        if (request is null) throw new ArgumentNullException(nameof(request));

        if (!(_document.GetElement(request.AreaPlanUniqueId) is ViewPlan view) ||
            view.IsTemplate || view.ViewType != ViewType.AreaPlan)
        {
            return Result.Failure<PlanGeometrySnapshot>(new Error(
                "geometry.extraction.areaPlanMissing",
                "找不到此檢討封包的 Area Plan，或該視圖不是面積平面，請重新執行專案設定。",
                "areaPlanUniqueId=" + request.AreaPlanUniqueId));
        }

        var level = view.GenLevel;
        if (level is null)
        {
            return Result.Failure<PlanGeometrySnapshot>(new Error(
                "geometry.extraction.levelMissing",
                "此 Area Plan 未關聯樓層，無法決定擷取平面。",
                "areaPlan=" + view.Name));
        }

        var options = request.Options;
        var builder = new PlanGeometryBuilder(request, RevitDocumentIdentity.Of(_document), level.UniqueId);
        builder.UseExtent(ResolveExtent(view));
        if (options.RestrictToViewExtent && builder.Extent is null)
            builder.AddWarning("此 Area Plan 未啟用裁切範圍也未指定 Scope Box，已改以整個樓層擷取。");

        var cutElevation = ResolveCutElevation(view, level);

        AddDocumentGeometry(
            new HostSource(this, view, level, options.RestrictToViewExtent, cutElevation),
            options,
            builder,
            PlanTransform2D.Identity);

        AddAuxiliaryLines(view, options, builder);

        if (options.IncludeLinkedModels)
            AddLinkedGeometry(view, options, builder, cutElevation);

        return builder.Build();
    }

    // --- scope -----------------------------------------------------------------------------

    private PlanExtent2D? ResolveExtent(ViewPlan view)
    {
        // A scope box wins over the crop region: it is the shared, named boundary the team agreed on.
        var scopeBoxId = view.get_Parameter(BuiltInParameter.VIEWER_VOLUME_OF_INTEREST_CROP)?.AsElementId();
        if (scopeBoxId is not null && scopeBoxId != ElementId.InvalidElementId)
        {
            var extent = RevitPlanShapeReader.ReadPlanExtent(_document.GetElement(scopeBoxId)?.get_BoundingBox(null));
            if (extent is not null) return extent;
        }

        return view.CropBoxActive ? RevitPlanShapeReader.ReadPlanExtent(view.CropBox) : null;
    }

    private static double ResolveCutElevation(ViewPlan view, Level level)
    {
        try
        {
            var range = view.GetViewRange();
            var cutLevelId = range.GetLevelId(PlanViewPlane.CutPlane);
            var baseElevation = view.Document.GetElement(cutLevelId) is Level cutLevel ? cutLevel.Elevation : level.Elevation;
            return baseElevation + range.GetOffset(PlanViewPlane.CutPlane);
        }
        catch (Exception)
        {
            // An Area Plan need not expose a usable view range; the level plane is the honest fallback.
            return level.Elevation;
        }
    }

    // --- host and link collection ----------------------------------------------------------

    private void AddDocumentGeometry(
        IGeometrySource source,
        PlanGeometryExtractionOptions options,
        PlanGeometryBuilder builder,
        PlanTransform2D transform)
    {
        if (options.IncludeWallCenterlines)
        {
            foreach (var wall in source.Collect(BuiltInCategory.OST_Walls).OfType<Wall>())
                AddWall(wall, source, builder, transform);
        }

        if (!options.IncludeColumnOutlines) return;

        foreach (var category in ColumnCategories)
        {
            foreach (var column in source.Collect(category))
                AddColumn(column, source, builder, transform);
        }
    }

    private static void AddWall(Wall wall, IGeometrySource source, PlanGeometryBuilder builder, PlanTransform2D transform)
    {
        try
        {
            // The location curve is the wall's centreline by definition, whichever location line the
            // wall was drawn with. Offsetting to a face is a repair concern, not an extraction one.
            if (!(wall.Location is LocationCurve location) || location.Curve is null) return;

            builder.AddPolyline(
                RevitPlanShapeReader.Flatten(location.Curve).Select(transform.Apply),
                source.SourceRefFor(wall, GeometrySourceKind.WallCenterline));
        }
        catch (Autodesk.Revit.Exceptions.ApplicationException exception)
        {
            builder.AddWarning($"牆（Id {wall.Id}）的位置線無法讀取，已略過：{exception.Message}");
        }
    }

    private static void AddColumn(Element column, IGeometrySource source, PlanGeometryBuilder builder, PlanTransform2D transform)
    {
        var sourceRef = source.SourceRefFor(column, GeometrySourceKind.ColumnOutline);

        try
        {
            var outlines = RevitPlanShapeReader.ReadPlanOutlines(column, source.LocalPlaneElevationFeet);
            if (outlines.Count > 0)
            {
                foreach (var outline in outlines)
                    builder.AddPolyline(outline.Select(transform.Apply), sourceRef, closed: true);
                return;
            }
        }
        catch (Exception exception) when (exception is Autodesk.Revit.Exceptions.ApplicationException ||
                                          exception is InvalidOperationException)
        {
            // Fall through to the bounding rectangle and say so, rather than losing the column.
        }

        var rectangle = RevitPlanShapeReader.ReadBoundingRectangle(column);
        if (rectangle is null)
        {
            builder.AddWarning($"柱（Id {column.Id}）沒有可用的平面幾何，已略過。");
            return;
        }

        builder.AddPolyline(rectangle.Select(transform.Apply), sourceRef, closed: true);
        builder.AddWarning($"柱（Id {column.Id}）的輪廓無法分析，已改用外接矩形，請確認該處邊界。");
    }

    // Auxiliary lines are drawn by the reviewer in this Area Plan, so they are always read through
    // the view: a helper line that is not visible there was not meant for this review.
    private void AddAuxiliaryLines(ViewPlan view, PlanGeometryExtractionOptions options, PlanGeometryBuilder builder)
    {
        if (!options.IncludeAuxiliaryLines) return;

        var hostDocumentUniqueId = RevitDocumentIdentity.Of(_document);
        var skippedByStyle = 0;

        foreach (var element in new FilteredElementCollector(_document, view.Id)
                     .OfCategory(BuiltInCategory.OST_Lines)
                     .WhereElementIsNotElementType())
        {
            if (!(element is CurveElement curveElement) || curveElement.GeometryCurve is null) continue;

            if (!MatchesAuxiliaryStyle(curveElement))
            {
                skippedByStyle++;
                continue;
            }

            builder.AddPolyline(
                RevitPlanShapeReader.Flatten(curveElement.GeometryCurve),
                new SourceRef(hostDocumentUniqueId, curveElement.UniqueId, GeometrySourceKind.AuxiliaryLine));
        }

        if (skippedByStyle > 0)
            builder.AddWarning($"已略過 {skippedByStyle} 條線型不在輔助線清單中的線段。");
    }

    private bool MatchesAuxiliaryStyle(CurveElement curveElement)
    {
        if (_auxiliaryLineStyleNames.Count == 0) return true;
        var style = curveElement.LineStyle;
        return style is not null && _auxiliaryLineStyleNames.Contains(style.Name);
    }

    private void AddLinkedGeometry(
        ViewPlan view,
        PlanGeometryExtractionOptions options,
        PlanGeometryBuilder builder,
        double cutElevationFeet)
    {
        foreach (var link in new FilteredElementCollector(_document, view.Id)
                     .OfClass(typeof(RevitLinkInstance))
                     .WhereElementIsNotElementType()
                     .OfType<RevitLinkInstance>())
        {
            var linkDocument = link.GetLinkDocument();
            if (linkDocument is null)
            {
                builder.AddWarning($"連結模型「{link.Name}」未載入，已略過。");
                continue;
            }

            var revitTransform = link.GetTotalTransform();
            var transformResult = LinkGeometryPolicy.TryCreateTransform(
                ToBasis(revitTransform.BasisX),
                ToBasis(revitTransform.BasisY),
                ToBasis(revitTransform.BasisZ),
                ToBasis(revitTransform.Origin));

            if (transformResult.IsFailure)
            {
                builder.AddWarning($"連結模型「{link.Name}」已略過：{transformResult.Error.Message}");
                continue;
            }

            AddDocumentGeometry(
                new LinkSource(linkDocument, link, cutElevationFeet - revitTransform.Origin.Z),
                options,
                builder,
                transformResult.Value);
        }
    }

    private static BasisVector ToBasis(XYZ vector) => new BasisVector(vector.X, vector.Y, vector.Z);

    // --- element sources -------------------------------------------------------------------

    // Host and link documents differ in how elements are found and identified, but not in what is
    // done with them, so the difference is isolated here instead of threaded through every method.
    private interface IGeometrySource
    {
        IEnumerable<Element> Collect(BuiltInCategory category);

        SourceRef SourceRefFor(Element element, GeometrySourceKind kind);

        /// <summary>The extraction plane in this document's own coordinates.</summary>
        double LocalPlaneElevationFeet { get; }
    }

    private sealed class HostSource : IGeometrySource
    {
        private readonly RevitPlanGeometryExtractor _owner;
        private readonly ViewPlan _view;
        private readonly Level _level;
        private readonly bool _restrictToView;
        private readonly string _documentUniqueId;

        public HostSource(
            RevitPlanGeometryExtractor owner,
            ViewPlan view,
            Level level,
            bool restrictToView,
            double cutElevationFeet)
        {
            _owner = owner;
            _view = view;
            _level = level;
            _restrictToView = restrictToView;
            _documentUniqueId = RevitDocumentIdentity.Of(owner._document);
            LocalPlaneElevationFeet = cutElevationFeet;
        }

        public double LocalPlaneElevationFeet { get; }

        // Honouring the view scope also means honouring what the view hides (spec 10.1: visible
        // model); with the scope off, the level association is the only filter left.
        public IEnumerable<Element> Collect(BuiltInCategory category)
        {
            var collector = _restrictToView
                ? new FilteredElementCollector(_owner._document, _view.Id)
                : new FilteredElementCollector(_owner._document).WherePasses(new ElementLevelFilter(_level.Id));

            return collector.OfCategory(category).WhereElementIsNotElementType();
        }

        public SourceRef SourceRefFor(Element element, GeometrySourceKind kind) =>
            new SourceRef(_documentUniqueId, element.UniqueId, kind);
    }

    private sealed class LinkSource : IGeometrySource
    {
        private readonly Document _linkDocument;
        private readonly string _documentUniqueId;
        private readonly string _linkInstanceUniqueId;

        public LinkSource(Document linkDocument, RevitLinkInstance link, double localPlaneElevationFeet)
        {
            _linkDocument = linkDocument;
            _documentUniqueId = RevitDocumentIdentity.Of(linkDocument);
            _linkInstanceUniqueId = link.UniqueId;
            LocalPlaneElevationFeet = localPlaneElevationFeet;
        }

        public double LocalPlaneElevationFeet { get; }

        // Link levels rarely line up with host levels by name or id, so membership is decided
        // geometrically: keep what the extraction plane actually cuts through.
        public IEnumerable<Element> Collect(BuiltInCategory category)
        {
            foreach (var element in new FilteredElementCollector(_linkDocument)
                         .OfCategory(category)
                         .WhereElementIsNotElementType())
            {
                var box = element.get_BoundingBox(null);
                if (box is null) continue;
                if (box.Min.Z - LinkPlaneSlackFeet <= LocalPlaneElevationFeet &&
                    LocalPlaneElevationFeet <= box.Max.Z + LinkPlaneSlackFeet)
                {
                    yield return element;
                }
            }
        }

        public SourceRef SourceRefFor(Element element, GeometrySourceKind kind) =>
            new SourceRef(_documentUniqueId, element.UniqueId, kind, _linkInstanceUniqueId);
    }
}
