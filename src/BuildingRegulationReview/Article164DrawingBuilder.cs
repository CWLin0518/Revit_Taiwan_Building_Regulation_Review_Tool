using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using DBLine = Autodesk.Revit.DB.Line;

namespace BuildingRegulationReview
{
    internal sealed class Article164DrawingBuilder
    {
        public const string LegendViewName = "164條圖說－圖例";
        public const string SheetNumber = "164-1";
        private const string SheetName = "第164條道路陰影檢討";

        private const double SwatchSizeFactor = 2.2;
        private const double RowPaddingFactor = 0.6;
        private const double LabelColumnChars = 15;
        private const double BodyTextChars = 32;
        private const double CharWidthFactor = 0.95;
        private const double LineHeightFactor = 1.6;

        private const string ArticleText =
            "建築技術規則建築設計施工編 第164條\n" +
            "建築物高度依下列規定：一、建築物以三‧六比一之斜率，依垂直建築線方向投影於面前道路之陰影面積，不得超過基地臨接面前道路之長度與該道路寬度乘積之半，" +
            "且其陰影最大不得超過面前道路對側境界線；建築基地臨接面前道路之對側有永久性空地，其陰影面積得加倍計算。陰影及高度之計算如下：\n" +
            "As ≦ (L × Sw) / 2，且 H ≦ 3.6 (Sw + D)\n" +
            "其中　As：建築物以三‧六比一之斜率，依垂直建築線方向，投影於面前道路之陰影面積。L：基地臨接面前道路之長度。" +
            "Sw：面前道路寬度（依本編第十四條第一項各款之規定）。H：建築物各部分高度。D：建築物各部分至建築線之水平距離。\n" +
            "二、前款所稱之斜率，為高度與水平距離之比值。";

        private static readonly int[] StandardScales = { 1, 2, 5, 10, 20, 25, 50, 100, 150, 200, 250, 300, 400, 500, 1000 };

        private readonly Document _document;
        private readonly Article164ReviewSession.Result _run;

        public Article164DrawingBuilder(Document document, Article164ReviewSession.Result run)
        {
            _document = document;
            _run = run;
        }

        public static (double Width, double Height) ComputePlanBoxSize(double paperWidth, double paperHeight) =>
            (paperWidth * 0.60, paperHeight * 0.85);

        public (double Width, double Height) EstimateLegendBoxSize(double textSizeFeet)
        {
            var swatchSize = textSizeFeet * SwatchSizeFactor;
            var padding = textSizeFeet * RowPaddingFactor;
            var swatchColumnWidth = swatchSize + padding * 2;
            var labelColumnWidth = textSizeFeet * CharWidthFactor * LabelColumnChars;
            var tableWidth = swatchColumnWidth + labelColumnWidth;
            var rowHeight = swatchSize + padding;
            var tableHeight = rowHeight * 2;

            var wrapWidth = textSizeFeet * CharWidthFactor * BodyTextChars;
            var charsPerLine = Math.Max(10, (int)BodyTextChars);
            var lineHeight = textSizeFeet * LineHeightFactor;
            var lines = EstimateLineCount(ArticleText, charsPerLine)
                + EstimateLineCount(BuildFormulaText(), charsPerLine)
                + EstimateLineCount(BuildCalculationText(), charsPerLine);
            var textHeight = lines * lineHeight;

            var margin = textSizeFeet * 1.2;
            var width = Math.Max(tableWidth, wrapWidth) + margin * 2;
            var height = margin + tableHeight + margin + textHeight + margin;
            return (width, height);
        }

        private static int EstimateLineCount(string text, int charsPerLine)
        {
            var lines = 0;
            foreach (var segment in text.Split('\n'))
                lines += Math.Max(1, (int)Math.Ceiling(segment.Length / (double)charsPerLine));
            return lines;
        }

        public ViewSheet CreateSheetShell(ElementId titleBlockTypeId)
        {
            DeleteExistingSheetByNumber(SheetNumber);
            var sheet = ViewSheet.Create(_document, titleBlockTypeId);
            sheet.SheetNumber = SheetNumber;
            sheet.Name = SheetName;
            return sheet;
        }

        public BoundingBoxXYZ GetTitleBlockBounds(ViewSheet sheet)
        {
            var titleBlockInstance = new FilteredElementCollector(_document, sheet.Id)
                .OfCategory(BuiltInCategory.OST_TitleBlocks).WhereElementIsNotElementType().FirstOrDefault();
            var bounds = titleBlockInstance?.get_BoundingBox(sheet);
            if (bounds == null)
                throw new InvalidOperationException("找不到圖框的邊界範圍，無法排版圖說內容，請確認選擇的圖框類型正常。");
            return bounds;
        }

        public ViewPlan GetPlanView()
        {
            var view = _document.GetElement(_run.PlanViewId) as ViewPlan;
            if (view == null)
                throw new InvalidOperationException("找不到「開始檢討」建立的平面視圖，請重新執行開始檢討。");
            return view;
        }

        public int ComputePlanViewScale(double targetWidth, double targetHeight)
        {
            var box = GetPlanView().CropBox;
            var modelWidth = box.Max.X - box.Min.X;
            var modelHeight = box.Max.Y - box.Min.Y;
            var rawScale = Math.Max(modelWidth / targetWidth, modelHeight / targetHeight);
            var candidate = StandardScales.FirstOrDefault(s => s >= rawScale);
            return candidate != 0 ? candidate : (int)Math.Ceiling(rawScale / 100.0) * 100;
        }

        public void ApplyPlanViewScale(int scale) => GetPlanView().Scale = scale;

        public (ViewDrafting View, double ActualWidth, double ActualHeight) CreateLegendView(double textSizeFeet)
        {
            Article164PlanViewBuilder.DeleteExistingViewByName<ViewDrafting>(_document, LegendViewName);
            var viewFamilyTypeId = new FilteredElementCollector(_document).OfClass(typeof(ViewFamilyType))
                .Cast<ViewFamilyType>().First(v => v.ViewFamily == ViewFamily.Drafting).Id;
            var view = ViewDrafting.Create(_document, viewFamilyTypeId);
            view.Name = LegendViewName;
            view.Scale = 1;

            var bodyTypeId = GetOrCreateTextNoteType("164條圖說－內文文字", textSizeFeet);

            var swatchSize = textSizeFeet * SwatchSizeFactor;
            var padding = textSizeFeet * RowPaddingFactor;
            var swatchColumnWidth = swatchSize + padding * 2;
            var labelColumnWidth = textSizeFeet * CharWidthFactor * LabelColumnChars;
            var tableWidth = swatchColumnWidth + labelColumnWidth;
            var rowHeight = swatchSize + padding;
            var margin = textSizeFeet * 1.2;

            var minWidth = TextNote.GetMinimumAllowedWidth(_document, bodyTypeId);
            var maxWidth = TextNote.GetMaximumAllowedWidth(_document, bodyTypeId);
            var wrapWidthRaw = Math.Max(tableWidth, textSizeFeet * CharWidthFactor * BodyTextChars);
            var textWidth = Math.Max(minWidth, Math.Min(wrapWidthRaw, maxWidth));

            var y = 0.0;
            y = DrawLegendTable(view, tableWidth, swatchColumnWidth, rowHeight, swatchSize, padding, y, bodyTypeId);
            y -= margin;
            y = CreateWrappedText(view, 0, y, textWidth, ArticleText, bodyTypeId, margin);
            y = CreateWrappedText(view, 0, y, textWidth, BuildFormulaText(), bodyTypeId, margin);
            y = CreateWrappedText(view, 0, y, textWidth, BuildCalculationText(), bodyTypeId, margin);

            var contentWidth = Math.Max(tableWidth, textWidth);
            var top = margin;
            var bottom = y;
            var left = -margin;
            var right = contentWidth + margin;
            view.CropBoxActive = true;
            view.CropBoxVisible = false;
            view.CropBox = new BoundingBoxXYZ { Min = new XYZ(left, bottom, -1), Max = new XYZ(right, top, 1) };

            return (view, right - left, top - bottom);
        }

        private double DrawLegendTable(View view, double tableWidth, double swatchColumnWidth, double rowHeight,
            double swatchSize, double padding, double topY, ElementId textTypeId)
        {
            var rows = new[]
            {
                (TypeId: _run.FootprintTypeId, Label: "本案新建建物"),
                (TypeId: _run.ShadowTypeId, Label: _run.Compliant ? "道路陰影（符合）" : "道路陰影（不符合）"),
            };
            var tableBottom = topY - rowHeight * rows.Length;

            DrawLine(view, new XYZ(0, topY, 0), new XYZ(tableWidth, topY, 0));
            DrawLine(view, new XYZ(0, tableBottom, 0), new XYZ(tableWidth, tableBottom, 0));
            DrawLine(view, new XYZ(0, topY, 0), new XYZ(0, tableBottom, 0));
            DrawLine(view, new XYZ(tableWidth, topY, 0), new XYZ(tableWidth, tableBottom, 0));
            DrawLine(view, new XYZ(swatchColumnWidth, topY, 0), new XYZ(swatchColumnWidth, tableBottom, 0));

            for (var i = 0; i < rows.Length; i++)
            {
                var rowTop = topY - rowHeight * i;
                var rowBottom = rowTop - rowHeight;
                if (i > 0) DrawLine(view, new XYZ(0, rowTop, 0), new XYZ(tableWidth, rowTop, 0));

                var swatchX = (swatchColumnWidth - swatchSize) / 2;
                var swatchY = rowBottom + (rowHeight - swatchSize) / 2;
                DrawSwatch(view.Id, rows[i].TypeId, swatchX, swatchY, swatchSize);
                TextNote.Create(_document, view.Id, new XYZ(swatchColumnWidth + padding, rowTop - padding, 0), rows[i].Label, textTypeId);
            }
            return tableBottom;
        }

        private void DrawSwatch(ElementId viewId, ElementId typeId, double x, double y, double size)
        {
            var loop = new CurveLoop();
            var a = new XYZ(x, y, 0); var b = new XYZ(x + size, y, 0);
            var c = new XYZ(x + size, y + size, 0); var d = new XYZ(x, y + size, 0);
            loop.Append(DBLine.CreateBound(a, b)); loop.Append(DBLine.CreateBound(b, c));
            loop.Append(DBLine.CreateBound(c, d)); loop.Append(DBLine.CreateBound(d, a));
            FilledRegion.Create(_document, typeId, viewId, new List<CurveLoop> { loop });
        }

        private void DrawLine(View view, XYZ from, XYZ to)
        {
            if (from.DistanceTo(to) <= _document.Application.ShortCurveTolerance) return;
            _document.Create.NewDetailCurve(view, DBLine.CreateBound(from, to));
        }

        private double CreateWrappedText(ViewDrafting view, double x, double topY, double width, string text, ElementId typeId, double gap)
        {
            var note = TextNote.Create(_document, view.Id, new XYZ(x, topY, 0), width, text, typeId);
            _document.Regenerate();
            var box = note.get_BoundingBox(view);
            var height = box != null ? box.Max.Y - box.Min.Y : width * 0.3;
            return topY - height - gap;
        }

        private static string BuildFormulaText() =>
            "計算公式：\n" +
            "As ≦ (L × Sw) / 2（道路對側無永久性空地）；有永久性空地時 As ≦ L × Sw\n" +
            "投影公式：P' = (X, Y) + N × max(0, Z－Z0) / 3.6";

        private string BuildCalculationText()
        {
            var length = ToMeters(_run.LineStart.DistanceTo(_run.LineEnd));
            var roadWidth = ToMeters(_run.RoadWidthInternal);
            var shadowArea = ToSquareMeters(_run.ShadowAreaInternal);
            var allowedArea = ToSquareMeters(_run.AllowedAreaInternal);
            var openSpaceNote = _run.HasPermanentOpenSpace ? "道路對側為永久性空地，容許面積 = L × Sw" : "道路對側非永久性空地，容許面積 = L × Sw ÷ 2";
            return "檢討過程：\n" +
                   $"L（建築線長度）= {length:0.##} m\n" +
                   $"Sw（面前道路寬度）= {roadWidth:0.##} m\n" +
                   $"{openSpaceNote} = {allowedArea:0.##} m²\n" +
                   $"陰影面積 As = {shadowArea:0.##} m²\n" +
                   $"As ≦ 容許面積 → 符合";
        }

        private ElementId GetOrCreateTextNoteType(string name, double textSizeFeet)
        {
            var types = new FilteredElementCollector(_document).OfClass(typeof(TextNoteType)).Cast<TextNoteType>().ToList();
            var type = types.FirstOrDefault(t => t.Name == name) ?? (TextNoteType)types.First().Duplicate(name);
            type.get_Parameter(BuiltInParameter.TEXT_SIZE).Set(textSizeFeet);
            return type.Id;
        }

        private void DeleteExistingSheetByNumber(string sheetNumber)
        {
            var existing = new FilteredElementCollector(_document).OfClass(typeof(ViewSheet)).Cast<ViewSheet>()
                .Where(s => s.SheetNumber == sheetNumber).Select(s => s.Id).ToList();
            if (existing.Count > 0) _document.Delete(existing);
        }

        private static double ToMeters(double internalFeet) => UnitUtils.ConvertFromInternalUnits(internalFeet, UnitTypeId.Meters);
        private static double ToSquareMeters(double squareFeet) => squareFeet * 0.09290304;
    }
}
