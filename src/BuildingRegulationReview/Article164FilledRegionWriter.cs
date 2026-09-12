using System.Linq;
using Autodesk.Revit.DB;

namespace BuildingRegulationReview
{
    internal static class Article164FilledRegionWriter
    {
        public static ElementId GetOrCreateFilledRegionType(Document document, string name, Color color)
        {
            var types = new FilteredElementCollector(document).OfClass(typeof(FilledRegionType)).Cast<FilledRegionType>().ToList();
            var existing = types.FirstOrDefault(x => x.Name == name);
            if (existing != null) return existing.Id;

            var created = (FilledRegionType)types.First().Duplicate(name);
            created.ForegroundPatternColor = color;
            created.ForegroundPatternId = new FilteredElementCollector(document)
                .OfClass(typeof(FillPatternElement)).Cast<FillPatternElement>()
                .First(x => x.GetFillPattern().IsSolidFill).Id;
            return created.Id;
        }
    }
}
