using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using BuildingRegulationReview.Application.Reviews;
using BuildingRegulationReview.Domain.Reviews;

namespace BuildingRegulationReview.Revit.Reviews;

/// <summary>
/// Stores one <see cref="ReviewRun"/> per DataStorage, following the package repository (P1-T03):
/// every property is its own Extensible Storage field and results, values and overrides are
/// sub-entities, so nothing is an opaque blob. Write operations require an open Revit transaction.
/// </summary>
public sealed class RevitReviewRunRepository : IReviewRunRepository
{
    private const string StorageNamePrefix = "BCR.ReviewRun.";
    private readonly Document _document;

    public RevitReviewRunRepository(Document document) =>
        _document = document ?? throw new ArgumentNullException(nameof(document));

    public ReviewRun? Get(Guid runId)
    {
        if (runId == Guid.Empty) throw new ArgumentException("Run ID cannot be empty.", nameof(runId));
        var storage = FindStorage("RunId", runId).FirstOrDefault();
        return storage is null ? null : Read(storage);
    }

    public IReadOnlyList<ReviewRun> GetForPackage(Guid packageId)
    {
        if (packageId == Guid.Empty) throw new ArgumentException("Package ID cannot be empty.", nameof(packageId));
        return FindStorage("PackageId", packageId)
            .Select(Read)
            .OrderBy(x => x.StartedAtUtc).ThenBy(x => x.RunId)
            .ToList();
    }

    public void Save(ReviewRun run)
    {
        if (run is null) throw new ArgumentNullException(nameof(run));
        RequireWritableDocument();
        var schemas = ReviewRunSchemas.GetOrCreate();
        var storage = FindStorage("RunId", run.RunId).FirstOrDefault() ?? DataStorage.Create(_document);
        storage.Name = StorageNamePrefix + run.RunId.ToString("D");
        storage.SetEntity(ReviewRunSchemas.ToEntity(schemas, ReviewRunStorageMapper.ToRecord(run)));
    }

    public bool Delete(Guid runId)
    {
        if (runId == Guid.Empty) throw new ArgumentException("Run ID cannot be empty.", nameof(runId));
        RequireWritableDocument();
        var storage = FindStorage("RunId", runId).FirstOrDefault();
        if (storage is null) return false;
        _document.Delete(storage.Id);
        return true;
    }

    private static ReviewRun Read(DataStorage storage)
    {
        var schema = ReviewRunSchemas.LookupRun()
                     ?? throw new InvalidOperationException("ReviewRun schema is not registered.");
        return ReviewRunStorageMapper.FromRecord(ReviewRunSchemas.FromEntity(storage.GetEntity(schema)));
    }

    private IEnumerable<DataStorage> FindStorage(string field, Guid id)
    {
        var schema = ReviewRunSchemas.LookupRun();
        if (schema is null) return Enumerable.Empty<DataStorage>();
        var expected = id.ToString("D");
        return GetStorages(schema).Where(x => string.Equals(
            x.GetEntity(schema).Get<string>(schema.GetField(field)), expected, StringComparison.OrdinalIgnoreCase));
    }

    private IEnumerable<DataStorage> GetStorages(Schema schema) =>
        new FilteredElementCollector(_document).OfClass(typeof(DataStorage)).Cast<DataStorage>()
            .Where(x => x.GetEntity(schema).IsValid());

    private void RequireWritableDocument()
    {
        if (!_document.IsModifiable)
            throw new InvalidOperationException("Save and Delete must be called inside an open Revit transaction.");
    }
}

internal sealed class ReviewRunSchemaSet
{
    public ReviewRunSchemaSet(Schema run, Schema result, Schema value, Schema overrideEntry)
    {
        Run = run;
        Result = result;
        Value = value;
        Override = overrideEntry;
    }

    public Schema Run { get; }
    public Schema Result { get; }
    public Schema Value { get; }
    public Schema Override { get; }
}

/// <summary>
/// The four released schemas of a stored run. Like the package schema, a released GUID is never
/// redefined: a physical field change needs new GUIDs and an adapter migration. Optional values are
/// stored as arrays of zero or one sub-entity, because an unset sub-entity field cannot be told apart
/// from a broken one. Numbers are stored as round-trip text so no Revit unit is involved.
/// </summary>
internal static class ReviewRunSchemas
{
    internal static readonly Guid RunId = new Guid("966fd4d4-3fba-42b3-912e-9cd3d0819302");
    internal static readonly Guid ResultId = new Guid("f45e3cb0-e14b-4958-bbcf-d15cb26b84be");
    internal static readonly Guid ValueId = new Guid("f00e87ac-3314-4fc7-a449-4fb6821a155e");
    internal static readonly Guid OverrideId = new Guid("61936165-bf36-482a-8379-20605b78d02d");

    internal static Schema? LookupRun() => Schema.Lookup(RunId);

    internal static ReviewRunSchemaSet GetOrCreate()
    {
        var value = Schema.Lookup(ValueId) ?? Build(ValueId, "BCR_ReviewValue_v1", "Review value of a stored review run.", b =>
        {
            AddSimple<string>(b, "Kind");
            AddSimple<string>(b, "Number");
            AddSimple<string>(b, "Unit");
            AddSimple<string>(b, "Text");
            AddSimple<bool>(b, "Flag");
        });

        var result = Schema.Lookup(ResultId) ?? Build(ResultId, "BCR_ReviewResult_v1", "Review result of a stored review run.", b =>
        {
            AddSimple<string>(b, "ResultId");
            AddSimple<string>(b, "CheckType");
            b.AddArrayField("SubjectUniqueIds", typeof(string));
            AddSimple<string>(b, "ZoneId");
            AddSimple<string>(b, "Status");
            b.AddArrayField("ActualValue", typeof(Entity)).SetSubSchemaGUID(ValueId);
            b.AddArrayField("RequiredValue", typeof(Entity)).SetSubSchemaGUID(ValueId);
            AddSimple<string>(b, "RuleId");
            AddSimple<string>(b, "RuleVersion");
            AddSimple<string>(b, "LegalReference");
            AddSimple<string>(b, "Message");
            b.AddArrayField("EvidenceFields", typeof(string));
            b.AddArrayField("EvidenceValues", typeof(Entity)).SetSubSchemaGUID(ValueId);
            AddSimple<string>(b, "ReviewedBy");
            AddSimple<long>(b, "ReviewedAtUtcTicks");
        });

        var overrideEntry = Schema.Lookup(OverrideId) ?? Build(OverrideId, "BCR_ReviewOverride_v1", "Manual override audit entry of a stored review run.", b =>
        {
            AddSimple<string>(b, "OverrideId");
            AddSimple<string>(b, "ResultId");
            AddSimple<string>(b, "OriginalStatus");
            AddSimple<string>(b, "OverriddenStatus");
            AddSimple<string>(b, "Reason");
            AddSimple<string>(b, "Comment");
            AddSimple<string>(b, "OverriddenBy");
            AddSimple<long>(b, "OverriddenAtUtcTicks");
            AddSimple<string>(b, "RuleVersion");
            AddSimple<string>(b, "DependencyFingerprint");
            AddSimple<string>(b, "Standing");
            AddSimple<string>(b, "StandingReason");
            AddSimple<string>(b, "PreviousOverrideId");
        });

        var run = LookupRun() ?? Build(RunId, "BCR_ReviewRun_v1", "Taiwan Building Regulation Review run, results, evidence and overrides.", b =>
        {
            AddSimple<string>(b, "SchemaVersion");
            AddSimple<string>(b, "RunId");
            AddSimple<string>(b, "PackageId");
            AddSimple<string>(b, "RuleSetId");
            AddSimple<string>(b, "RuleSetVersion");
            AddSimple<int>(b, "BoundaryRevision");
            AddSimple<long>(b, "StartedAtUtcTicks");
            AddSimple<string>(b, "State");
            AddSimple<long>(b, "CompletedAtUtcTicks");
            b.AddArrayField("Results", typeof(Entity)).SetSubSchemaGUID(ResultId);
            AddSimple<string>(b, "ContextFingerprint");
            b.AddArrayField("SubjectIds", typeof(string));
            b.AddArrayField("SubjectFingerprints", typeof(string));
            b.AddArrayField("Overrides", typeof(Entity)).SetSubSchemaGUID(OverrideId);
        });

        return new ReviewRunSchemaSet(run, result, value, overrideEntry);
    }

    internal static Entity ToEntity(ReviewRunSchemaSet schemas, ReviewRunStorageRecord record)
    {
        var s = schemas.Run;
        var entity = new Entity(s);
        Set(entity, s, "SchemaVersion", record.SchemaVersion);
        Set(entity, s, "RunId", record.RunId);
        Set(entity, s, "PackageId", record.PackageId);
        Set(entity, s, "RuleSetId", record.RuleSetId);
        Set(entity, s, "RuleSetVersion", record.RuleSetVersion);
        Set(entity, s, "BoundaryRevision", record.BoundaryRevision);
        Set(entity, s, "StartedAtUtcTicks", record.StartedAtUtcTicks);
        Set(entity, s, "State", record.State);
        Set(entity, s, "CompletedAtUtcTicks", record.CompletedAtUtcTicks);
        Set<IList<Entity>>(entity, s, "Results", record.Results.Select(x => ToEntity(schemas, x)).ToList());
        Set(entity, s, "ContextFingerprint", record.ContextFingerprint);
        Set<IList<string>>(entity, s, "SubjectIds", record.SubjectFingerprints.Select(x => x.SubjectId).ToList());
        Set<IList<string>>(entity, s, "SubjectFingerprints", record.SubjectFingerprints.Select(x => x.Fingerprint).ToList());
        Set<IList<Entity>>(entity, s, "Overrides", record.Overrides.Select(x => ToEntity(schemas.Override, x)).ToList());
        return entity;
    }

    internal static ReviewRunStorageRecord FromEntity(Entity entity)
    {
        if (!entity.IsValid()) throw new InvalidOperationException("DataStorage does not contain a valid ReviewRun entity.");
        var s = entity.Schema;
        var ids = Get<IList<string>>(entity, s, "SubjectIds");
        var fingerprints = Get<IList<string>>(entity, s, "SubjectFingerprints");
        if (ids.Count != fingerprints.Count)
            throw new InvalidOperationException("Stored review run has subject fingerprints that do not line up.");

        return new ReviewRunStorageRecord
        {
            SchemaVersion = Get<string>(entity, s, "SchemaVersion"),
            RunId = Get<string>(entity, s, "RunId"),
            PackageId = Get<string>(entity, s, "PackageId"),
            RuleSetId = Get<string>(entity, s, "RuleSetId"),
            RuleSetVersion = Get<string>(entity, s, "RuleSetVersion"),
            BoundaryRevision = Get<int>(entity, s, "BoundaryRevision"),
            StartedAtUtcTicks = Get<long>(entity, s, "StartedAtUtcTicks"),
            State = Get<string>(entity, s, "State"),
            CompletedAtUtcTicks = Get<long>(entity, s, "CompletedAtUtcTicks"),
            Results = Get<IList<Entity>>(entity, s, "Results").Select(ResultFrom).ToList(),
            ContextFingerprint = Get<string>(entity, s, "ContextFingerprint"),
            SubjectFingerprints = ids.Select((id, i) => new ReviewFingerprintRecord { SubjectId = id, Fingerprint = fingerprints[i] }).ToList(),
            Overrides = Get<IList<Entity>>(entity, s, "Overrides").Select(OverrideFrom).ToList()
        };
    }

    private static Entity ToEntity(ReviewRunSchemaSet schemas, ReviewResultRecord record)
    {
        var s = schemas.Result;
        var entity = new Entity(s);
        Set(entity, s, "ResultId", record.ResultId);
        Set(entity, s, "CheckType", record.CheckType);
        Set<IList<string>>(entity, s, "SubjectUniqueIds", record.SubjectUniqueIds.ToList());
        Set(entity, s, "ZoneId", record.ZoneId);
        Set(entity, s, "Status", record.Status);
        Set<IList<Entity>>(entity, s, "ActualValue", Optional(schemas.Value, record.ActualValue));
        Set<IList<Entity>>(entity, s, "RequiredValue", Optional(schemas.Value, record.RequiredValue));
        Set(entity, s, "RuleId", record.RuleId);
        Set(entity, s, "RuleVersion", record.RuleVersion);
        Set(entity, s, "LegalReference", record.LegalReference);
        Set(entity, s, "Message", record.Message);
        Set<IList<string>>(entity, s, "EvidenceFields", record.Evidence.Select(x => x.Field).ToList());
        Set<IList<Entity>>(entity, s, "EvidenceValues", record.Evidence.Select(x => ToEntity(schemas.Value, x.Value)).ToList());
        Set(entity, s, "ReviewedBy", record.ReviewedBy);
        Set(entity, s, "ReviewedAtUtcTicks", record.ReviewedAtUtcTicks);
        return entity;
    }

    private static ReviewResultRecord ResultFrom(Entity entity)
    {
        var s = entity.Schema;
        var fields = Get<IList<string>>(entity, s, "EvidenceFields");
        var values = Get<IList<Entity>>(entity, s, "EvidenceValues");
        if (fields.Count != values.Count)
            throw new InvalidOperationException("Stored review result has evidence that does not line up.");

        return new ReviewResultRecord
        {
            ResultId = Get<string>(entity, s, "ResultId"),
            CheckType = Get<string>(entity, s, "CheckType"),
            SubjectUniqueIds = Get<IList<string>>(entity, s, "SubjectUniqueIds").ToList(),
            ZoneId = Get<string>(entity, s, "ZoneId"),
            Status = Get<string>(entity, s, "Status"),
            ActualValue = OptionalFrom(Get<IList<Entity>>(entity, s, "ActualValue")),
            RequiredValue = OptionalFrom(Get<IList<Entity>>(entity, s, "RequiredValue")),
            RuleId = Get<string>(entity, s, "RuleId"),
            RuleVersion = Get<string>(entity, s, "RuleVersion"),
            LegalReference = Get<string>(entity, s, "LegalReference"),
            Message = Get<string>(entity, s, "Message"),
            Evidence = fields.Select((field, i) => new ReviewEvidenceRecord { Field = field, Value = ValueFrom(values[i]) }).ToList(),
            ReviewedBy = Get<string>(entity, s, "ReviewedBy"),
            ReviewedAtUtcTicks = Get<long>(entity, s, "ReviewedAtUtcTicks")
        };
    }

    private static IList<Entity> Optional(Schema schema, ReviewValueRecord? value) =>
        value is null ? new List<Entity>() : new List<Entity> { ToEntity(schema, value) };

    private static ReviewValueRecord? OptionalFrom(IList<Entity> entities) => entities.Count switch
    {
        0 => null,
        1 => ValueFrom(entities[0]),
        _ => throw new InvalidOperationException("Stored review result has more than one value in a single-value field.")
    };

    private static Entity ToEntity(Schema schema, ReviewValueRecord record)
    {
        var entity = new Entity(schema);
        Set(entity, schema, "Kind", record.Kind);
        Set(entity, schema, "Number", record.Number.ToString("R", CultureInfo.InvariantCulture));
        Set(entity, schema, "Unit", record.Unit);
        Set(entity, schema, "Text", record.Text);
        Set(entity, schema, "Flag", record.Flag);
        return entity;
    }

    private static ReviewValueRecord ValueFrom(Entity entity)
    {
        var s = entity.Schema;
        var number = Get<string>(entity, s, "Number");
        if (!double.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
            throw new InvalidOperationException($"Stored review value has an invalid number '{number}'.");

        return new ReviewValueRecord
        {
            Kind = Get<string>(entity, s, "Kind"),
            Number = parsed,
            Unit = Get<string>(entity, s, "Unit"),
            Text = Get<string>(entity, s, "Text"),
            Flag = Get<bool>(entity, s, "Flag")
        };
    }

    private static Entity ToEntity(Schema schema, ReviewOverrideRecord record)
    {
        var entity = new Entity(schema);
        Set(entity, schema, "OverrideId", record.OverrideId);
        Set(entity, schema, "ResultId", record.ResultId);
        Set(entity, schema, "OriginalStatus", record.OriginalStatus);
        Set(entity, schema, "OverriddenStatus", record.OverriddenStatus);
        Set(entity, schema, "Reason", record.Reason);
        Set(entity, schema, "Comment", record.Comment);
        Set(entity, schema, "OverriddenBy", record.OverriddenBy);
        Set(entity, schema, "OverriddenAtUtcTicks", record.OverriddenAtUtcTicks);
        Set(entity, schema, "RuleVersion", record.RuleVersion);
        Set(entity, schema, "DependencyFingerprint", record.DependencyFingerprint);
        Set(entity, schema, "Standing", record.Standing);
        Set(entity, schema, "StandingReason", record.StandingReason);
        Set(entity, schema, "PreviousOverrideId", record.PreviousOverrideId);
        return entity;
    }

    private static ReviewOverrideRecord OverrideFrom(Entity entity)
    {
        var s = entity.Schema;
        return new ReviewOverrideRecord
        {
            OverrideId = Get<string>(entity, s, "OverrideId"),
            ResultId = Get<string>(entity, s, "ResultId"),
            OriginalStatus = Get<string>(entity, s, "OriginalStatus"),
            OverriddenStatus = Get<string>(entity, s, "OverriddenStatus"),
            Reason = Get<string>(entity, s, "Reason"),
            Comment = Get<string>(entity, s, "Comment"),
            OverriddenBy = Get<string>(entity, s, "OverriddenBy"),
            OverriddenAtUtcTicks = Get<long>(entity, s, "OverriddenAtUtcTicks"),
            RuleVersion = Get<string>(entity, s, "RuleVersion"),
            DependencyFingerprint = Get<string>(entity, s, "DependencyFingerprint"),
            Standing = Get<string>(entity, s, "Standing"),
            StandingReason = Get<string>(entity, s, "StandingReason"),
            PreviousOverrideId = Get<string>(entity, s, "PreviousOverrideId")
        };
    }

    private static Schema Build(Guid id, string name, string documentation, Action<SchemaBuilder> fields)
    {
        var builder = new SchemaBuilder(id);
        builder.SetSchemaName(name);
        builder.SetDocumentation(documentation);
        builder.SetVendorId("BCRV");
        builder.SetReadAccessLevel(AccessLevel.Public);
        builder.SetWriteAccessLevel(AccessLevel.Public);
        fields(builder);
        return builder.Finish();
    }

    private static void AddSimple<T>(SchemaBuilder builder, string name) => builder.AddSimpleField(name, typeof(T));

    // Revit rejects null strings in Extensible Storage, so empty text stands for "none" as in the record.
    private static void Set<T>(Entity entity, Schema schema, string name, T value) =>
        entity.Set(schema.GetField(name), value is null && typeof(T) == typeof(string) ? (T)(object)string.Empty : value);

    private static T Get<T>(Entity entity, Schema schema, string name) => entity.Get<T>(schema.GetField(name));
}
