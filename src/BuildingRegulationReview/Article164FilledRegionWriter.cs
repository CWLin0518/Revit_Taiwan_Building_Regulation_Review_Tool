using System;
using System.Linq;
using Autodesk.Revit.DB;

namespace BuildingRegulationReview
{
    internal static class Article164FilledRegionWriter
    {
        private const string DiagonalPatternName = "164條－陰影斜線";
        private static readonly double DiagonalPatternSpacing = UnitUtils.ConvertToInternalUnits(1.3, UnitTypeId.Millimeters);

        public static ElementId GetOrCreateFilledRegionType(Document document, string name, Color color, bool diagonalHatch = false)
        {
            var types = new FilteredElementCollector(document).OfClass(typeof(FilledRegionType)).Cast<FilledRegionType>().ToList();
            var type = types.FirstOrDefault(x => x.Name == name) ?? (FilledRegionType)types.First().Duplicate(name);
            type.ForegroundPatternColor = color;
            type.ForegroundPatternId = diagonalHatch ? GetOrCreateDiagonalPatternId(document) : GetSolidFillPatternId(document);
            return type.Id;
        }

        private static ElementId GetSolidFillPatternId(Document document) =>
            new FilteredElementCollector(document).OfClass(typeof(FillPatternElement)).Cast<FillPatternElement>()
                .First(x => x.GetFillPattern().IsSolidFill).Id;

        private static ElementId GetOrCreateDiagonalPatternId(Document document)
        {
            var existing = new FilteredElementCollector(document).OfClass(typeof(FillPatternElement))
                .Cast<FillPatternElement>().FirstOrDefault(p => p.Name == DiagonalPatternName);
            if (existing != null) return existing.Id;

            var pattern = new FillPattern(DiagonalPatternName, FillPatternTarget.Drafting,
                FillPatternHostOrientation.ToView, Math.PI / 4, DiagonalPatternSpacing);
            return FillPatternElement.Create(document, pattern).Id;
        }
    }
}
