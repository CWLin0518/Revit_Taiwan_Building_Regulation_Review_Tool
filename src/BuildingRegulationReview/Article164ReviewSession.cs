using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace BuildingRegulationReview
{
    internal static class Article164ReviewSession
    {
        internal sealed class Result
        {
            public bool Compliant;
            public IReadOnlyList<Rhino.Geometry.Curve> FootprintSilhouette;
            public IReadOnlyList<Rhino.Geometry.Curve> ShadowSilhouette;
            public XYZ LineStart;
            public XYZ LineEnd;
            public double RoadWidthInternal;
            public bool HasPermanentOpenSpace;
            public double ShadowAreaInternal;
            public double AllowedAreaInternal;
            public ElementId LevelId;
            public double BaseZ;
            public ElementId PlanViewId;
            public ElementId FootprintTypeId;
            public ElementId ShadowTypeId;
        }

        private static readonly Dictionary<Document, Result> ResultsByDocument = new Dictionary<Document, Result>();

        public static void SetResult(Document document, Result result) => ResultsByDocument[document] = result;

        public static Result GetResult(Document document) =>
            ResultsByDocument.TryGetValue(document, out var result) ? result : null;
    }
}
