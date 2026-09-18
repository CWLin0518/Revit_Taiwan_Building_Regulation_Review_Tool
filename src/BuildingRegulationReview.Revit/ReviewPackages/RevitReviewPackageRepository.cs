using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using BuildingRegulationReview.Application.ReviewPackages;
using BuildingRegulationReview.Domain.ReviewPackages;

namespace BuildingRegulationReview.Revit.ReviewPackages;

/// <summary>Stores one ReviewPackage per DataStorage. Write operations require an open Revit transaction.</summary>
public sealed class RevitReviewPackageRepository : IReviewPackageRepository
{
    private const string StorageNamePrefix = "BCR.ReviewPackage.";
    private readonly Document _document;

    public RevitReviewPackageRepository(Document document) =>
        _document = document ?? throw new ArgumentNullException(nameof(document));

    public ReviewPackage? Get(Guid packageId)
    {
        if (packageId == Guid.Empty) throw new ArgumentException("Package ID cannot be empty.", nameof(packageId));
        var storage = FindStorage(packageId);
        return storage is null ? null : Read(storage);
    }

    public IReadOnlyList<ReviewPackage> GetAll() => GetStorages()
        .Select(Read)
        .OrderBy(x => x.PackageId)
        .ToList();

    public void Save(ReviewPackage package)
    {
        if (package is null) throw new ArgumentNullException(nameof(package));
        RequireWritableDocument();
        var schema = ReviewPackageSchema.GetOrCreate();
        var storage = FindStorage(package.PackageId) ?? DataStorage.Create(_document);
        storage.Name = StorageNamePrefix + package.PackageId.ToString("D");
        storage.SetEntity(ReviewPackageSchema.ToEntity(schema, ReviewPackageStorageMapper.ToRecord(package)));
    }

    public bool Delete(Guid packageId)
    {
        if (packageId == Guid.Empty) throw new ArgumentException("Package ID cannot be empty.", nameof(packageId));
        RequireWritableDocument();
        var storage = FindStorage(packageId);
        if (storage is null) return false;
        _document.Delete(storage.Id);
        return true;
    }

    private ReviewPackage Read(DataStorage storage)
    {
        var schema = ReviewPackageSchema.Lookup();
        if (schema is null) throw new InvalidOperationException("ReviewPackage schema is not registered.");
        return ReviewPackageStorageMapper.FromRecord(ReviewPackageSchema.FromEntity(storage.GetEntity(schema)));
    }

    private DataStorage? FindStorage(Guid packageId)
    {
        var schema = ReviewPackageSchema.Lookup();
        if (schema is null) return null;
        var expected = packageId.ToString("D");
        return GetStorages().FirstOrDefault(x => string.Equals(
            x.GetEntity(schema).Get<string>(schema.GetField("PackageId")), expected,
            StringComparison.OrdinalIgnoreCase));
    }

    private IEnumerable<DataStorage> GetStorages()
    {
        var schema = ReviewPackageSchema.Lookup();
        if (schema is null) return Enumerable.Empty<DataStorage>();
        return new FilteredElementCollector(_document).OfClass(typeof(DataStorage)).Cast<DataStorage>()
            .Where(x => x.GetEntity(schema).IsValid());
    }

    private void RequireWritableDocument()
    {
        if (!_document.IsModifiable)
            throw new InvalidOperationException("Save and Delete must be called inside an open Revit transaction.");
    }
}

internal static class ReviewPackageSchema
{
    internal static readonly Guid Id = new Guid("d3272af5-4945-4be6-bf9f-a5a6202bd306");
    private const string Name = "BCR_ReviewPackage_v1";

    internal static Schema? Lookup() => Schema.Lookup(Id);

    internal static Schema GetOrCreate()
    {
        var existing = Lookup();
        if (existing is not null) return existing;
        var builder = new SchemaBuilder(Id);
        builder.SetSchemaName(Name);
        builder.SetDocumentation("Taiwan Building Regulation Review package metadata, schema 1.0.");
        builder.SetVendorId("BCRV");
        builder.SetReadAccessLevel(AccessLevel.Public);
        builder.SetWriteAccessLevel(AccessLevel.Public);
        AddSimple<string>(builder, "SchemaVersion");
        AddSimple<string>(builder, "PackageId");
        AddSimple<string>(builder, "SourceFloorPlanUniqueId");
        AddSimple<string>(builder, "LevelUniqueId");
        AddSimple<string>(builder, "AreaSchemeUniqueId");
        AddSimple<string>(builder, "AreaPlanUniqueId");
        AddSimple<string>(builder, "DraftingViewUniqueId");
        builder.AddArrayField("LegendViewUniqueIds", typeof(string));
        AddSimple<string>(builder, "SheetUniqueId");
        builder.AddArrayField("GeneratedElementUniqueIds", typeof(string));
        AddSimple<int>(builder, "BoundaryRevision");
        AddSimple<string>(builder, "RuleSetId");
        AddSimple<string>(builder, "RuleSetVersion");
        AddSimple<string>(builder, "LastReviewRunId");
        AddSimple<int>(builder, "Status");
        AddSimple<long>(builder, "UpdatedAtUtcTicks");
        return builder.Finish();
    }

    internal static Entity ToEntity(Schema schema, ReviewPackageStorageRecord record)
    {
        var entity = new Entity(schema);
        Set(entity, schema, "SchemaVersion", record.SchemaVersion);
        Set(entity, schema, "PackageId", record.PackageId);
        Set(entity, schema, "SourceFloorPlanUniqueId", record.SourceFloorPlanUniqueId);
        Set(entity, schema, "LevelUniqueId", record.LevelUniqueId);
        Set(entity, schema, "AreaSchemeUniqueId", record.AreaSchemeUniqueId);
        Set(entity, schema, "AreaPlanUniqueId", record.AreaPlanUniqueId);
        Set(entity, schema, "DraftingViewUniqueId", record.DraftingViewUniqueId);
        Set(entity, schema, "LegendViewUniqueIds", record.LegendViewUniqueIds);
        Set(entity, schema, "SheetUniqueId", record.SheetUniqueId);
        Set(entity, schema, "GeneratedElementUniqueIds", record.GeneratedElementUniqueIds);
        Set(entity, schema, "BoundaryRevision", record.BoundaryRevision);
        Set(entity, schema, "RuleSetId", record.RuleSetId);
        Set(entity, schema, "RuleSetVersion", record.RuleSetVersion);
        Set(entity, schema, "LastReviewRunId", record.LastReviewRunId);
        Set(entity, schema, "Status", record.Status);
        Set(entity, schema, "UpdatedAtUtcTicks", record.UpdatedAtUtcTicks);
        return entity;
    }

    internal static ReviewPackageStorageRecord FromEntity(Entity entity)
    {
        if (!entity.IsValid()) throw new InvalidOperationException("DataStorage does not contain a valid ReviewPackage entity.");
        var schema = entity.Schema;
        return new ReviewPackageStorageRecord
        {
            SchemaVersion = Get<string>(entity, schema, "SchemaVersion"),
            PackageId = Get<string>(entity, schema, "PackageId"),
            SourceFloorPlanUniqueId = Get<string>(entity, schema, "SourceFloorPlanUniqueId"),
            LevelUniqueId = Get<string>(entity, schema, "LevelUniqueId"),
            AreaSchemeUniqueId = Get<string>(entity, schema, "AreaSchemeUniqueId"),
            AreaPlanUniqueId = Get<string>(entity, schema, "AreaPlanUniqueId"),
            DraftingViewUniqueId = Get<string>(entity, schema, "DraftingViewUniqueId"),
            LegendViewUniqueIds = Get<IList<string>>(entity, schema, "LegendViewUniqueIds"),
            SheetUniqueId = Get<string>(entity, schema, "SheetUniqueId"),
            GeneratedElementUniqueIds = Get<IList<string>>(entity, schema, "GeneratedElementUniqueIds"),
            BoundaryRevision = Get<int>(entity, schema, "BoundaryRevision"),
            RuleSetId = Get<string>(entity, schema, "RuleSetId"),
            RuleSetVersion = Get<string>(entity, schema, "RuleSetVersion"),
            LastReviewRunId = Get<string>(entity, schema, "LastReviewRunId"),
            Status = Get<int>(entity, schema, "Status"),
            UpdatedAtUtcTicks = Get<long>(entity, schema, "UpdatedAtUtcTicks")
        };
    }

    private static void AddSimple<T>(SchemaBuilder builder, string name) => builder.AddSimpleField(name, typeof(T));
    private static void Set<T>(Entity entity, Schema schema, string name, T value) => entity.Set(schema.GetField(name), value);
    private static T Get<T>(Entity entity, Schema schema, string name) => entity.Get<T>(schema.GetField(name));
}
