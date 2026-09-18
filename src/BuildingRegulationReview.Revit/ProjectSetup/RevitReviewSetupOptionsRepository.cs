using System;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using BuildingRegulationReview.Application.ProjectSetup;

namespace BuildingRegulationReview.Revit.ProjectSetup;

public sealed class RevitReviewSetupOptionsRepository
{
    private static readonly Guid SchemaId = new Guid("763174a4-c95d-4fa0-9b82-a463f599c28b");
    private readonly Document _document;
    public RevitReviewSetupOptionsRepository(Document document) => _document = document ?? throw new ArgumentNullException(nameof(document));

    public void Save(Guid packageId, ReviewPackageSetupSelection selection)
    {
        if (!_document.IsModifiable) throw new InvalidOperationException("Save must be called inside an open Revit transaction.");
        var schema = GetOrCreateSchema();
        var storage = Find(packageId) ?? DataStorage.Create(_document);
        storage.Name = "BCR.ReviewSetupOptions." + packageId.ToString("D");
        var entity = new Entity(schema);
        entity.Set(schema.GetField("PackageId"), packageId.ToString("D"));
        entity.Set(schema.GetField("AreaPlanTemplateUniqueId"), selection.AreaPlanTemplateUniqueId ?? string.Empty);
        entity.Set(schema.GetField("CopyCropSettings"), selection.CopyCropSettings ? 1 : 0);
        entity.Set(schema.GetField("ScopeBoxUniqueId"), selection.ScopeBoxUniqueId ?? string.Empty);
        storage.SetEntity(entity);
    }

    private DataStorage? Find(Guid packageId)
    {
        var schema = Schema.Lookup(SchemaId); if (schema == null) return null;
        var expected = packageId.ToString("D");
        return new FilteredElementCollector(_document).OfClass(typeof(DataStorage)).Cast<DataStorage>()
            .FirstOrDefault(x => x.GetEntity(schema).IsValid() && string.Equals(x.GetEntity(schema).Get<string>(schema.GetField("PackageId")), expected, StringComparison.OrdinalIgnoreCase));
    }

    private static Schema GetOrCreateSchema()
    {
        var schema = Schema.Lookup(SchemaId); if (schema != null) return schema;
        var builder = new SchemaBuilder(SchemaId);
        builder.SetSchemaName("BCR_ReviewSetupOptions_v1"); builder.SetVendorId("BCRV");
        builder.SetReadAccessLevel(AccessLevel.Public); builder.SetWriteAccessLevel(AccessLevel.Public);
        builder.AddSimpleField("PackageId", typeof(string));
        builder.AddSimpleField("AreaPlanTemplateUniqueId", typeof(string));
        builder.AddSimpleField("CopyCropSettings", typeof(int));
        builder.AddSimpleField("ScopeBoxUniqueId", typeof(string));
        return builder.Finish();
    }
}
