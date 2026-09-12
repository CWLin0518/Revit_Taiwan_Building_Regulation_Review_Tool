using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;

namespace BuildingRegulationReview
{
    /// <summary>Owns every Revit-side FilledRegion operation for Article 164.</summary>
    internal sealed class Article164FilledRegionWriter
    {
        private static readonly Guid SchemaGuid = new Guid("E2CF8429-1BE1-49A2-82D4-164164164164");
        private readonly Document _document;
        private readonly View _view;

        public Article164FilledRegionWriter(Document document, View view)
        {
            _document = document ?? throw new ArgumentNullException(nameof(document));
            _view = view ?? throw new ArgumentNullException(nameof(view));
        }

        public IReadOnlyList<ElementId> ReplaceResult(IEnumerable<CurveLoop> boundaries, bool compliant, string runId)
        {
            if (!_document.IsModifiable)
                throw new InvalidOperationException("FilledRegion 必須在 Revit Transaction 內寫入。");

            DeletePreviousToolRegions();
            var typeId = GetOrCreateType(compliant);
            var schema = GetOrCreateSchema();
            var createdIds = new List<ElementId>();

            foreach (var boundary in boundaries.Where(x => x != null))
            {
                var region = FilledRegion.Create(_document, typeId, _view.Id, new List<CurveLoop> { boundary });
                var entity = new Entity(schema);
                entity.Set("RuleId", "Article164");
                entity.Set("RunId", runId ?? string.Empty);
                entity.Set("Status", compliant ? "Pass" : "Fail");
                region.SetEntity(entity);
                createdIds.Add(region.Id);
            }
            return createdIds;
        }

        private void DeletePreviousToolRegions()
        {
            var schema = Schema.Lookup(SchemaGuid);
            if (schema == null) return;
            var ids = new FilteredElementCollector(_document, _view.Id)
                .OfClass(typeof(FilledRegion))
                .WhereElementIsNotElementType()
                .Where(x =>
                {
                    var entity = x.GetEntity(schema);
                    return entity.IsValid() && entity.Get<string>("RuleId") == "Article164";
                })
                .Select(x => x.Id)
                .ToList();
            if (ids.Count > 0) _document.Delete(ids);
        }

        private ElementId GetOrCreateType(bool compliant)
        {
            var name = compliant ? "164條－符合" : "164條－不符合";
            var types = new FilteredElementCollector(_document).OfClass(typeof(FilledRegionType)).Cast<FilledRegionType>().ToList();
            var existing = types.FirstOrDefault(x => x.Name == name);
            if (existing != null) return existing.Id;

            var created = (FilledRegionType)types.First().Duplicate(name);
            created.ForegroundPatternColor = compliant ? new Color(70, 180, 90) : new Color(225, 65, 65);
            created.ForegroundPatternId = new FilteredElementCollector(_document)
                .OfClass(typeof(FillPatternElement)).Cast<FillPatternElement>()
                .First(x => x.GetFillPattern().IsSolidFill).Id;
            return created.Id;
        }

        private static Schema GetOrCreateSchema()
        {
            var existing = Schema.Lookup(SchemaGuid);
            if (existing != null) return existing;
            var builder = new SchemaBuilder(SchemaGuid);
            builder.SetSchemaName("BuildingRegulationReviewArticle164");
            builder.SetReadAccessLevel(AccessLevel.Public);
            builder.SetWriteAccessLevel(AccessLevel.Public);
            builder.AddSimpleField("RuleId", typeof(string));
            builder.AddSimpleField("RunId", typeof(string));
            builder.AddSimpleField("Status", typeof(string));
            return builder.Finish();
        }
    }
}
