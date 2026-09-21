using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using BuildingRegulationReview.Application.Reviews;
using BuildingRegulationReview.Application.WriteBack;
using BuildingRegulationReview.Domain.Geometry;
using BuildingRegulationReview.Domain.ReviewPackages;
using BuildingRegulationReview.Revit.WriteBack;
using RevitApplicationException = Autodesk.Revit.Exceptions.ApplicationException;

namespace BuildingRegulationReview.Revit.Reviews;

/// <summary>
/// Marks one review run in the package's dedicated review view (spec 11.4 item 4, 11.5 item 6, 11.6
/// item 4): red Filled Regions over the failing 區劃, a red By Element Override on the failing members
/// and openings, and the record of what each overridden element looked like before.
/// </summary>
/// <remarks>
/// <para>
/// The view is a duplicate of the source floor plan made once per package and found again by its
/// ownership mark (<see cref="ManagedOutputKind.ReviewView"/>), so a user who renames it keeps it.
/// Marking never touches the source plan, the Area Plan or any other view.
/// </para>
/// <para>
/// Only the current package's regions and the elements this tool recorded are changed; everything
/// else in the view — hand-drawn regions, other packages' marks, overrides the user set on elements the
/// tool never painted — is left alone (spec 13.2). The whole pass runs in one <c>TransactionGroup</c>:
/// a failure of the view itself rolls everything back, a single element Revit refuses is logged and the
/// rest carries on.
/// </para>
/// </remarks>
public sealed class RevitReviewViewMarker
{
    /// <summary>The Filled Region Type the red regions use. Created on first use; an existing one is used as the user left it.</summary>
    public const string RegionTypeName = "BCR_防火檢討_未符合";

    private const string HatchPatternName = "BCR_防火檢討_斜線";

    private static readonly Color Red = new Color(255, 0, 0);

    private readonly Document _document;

    public RevitReviewViewMarker(Document document) =>
        _document = document ?? throw new ArgumentNullException(nameof(document));

    /// <summary>The package's review view, or null when none has been made yet.</summary>
    public ViewPlan? FindView(Guid packageId)
    {
        if (packageId == Guid.Empty) throw new ArgumentException("Package ID cannot be empty.", nameof(packageId));
        var key = new ManagedOutputKey(packageId, ManagedOutputKind.ReviewView).ToToken();

        return new FilteredElementCollector(_document)
            .OfClass(typeof(ViewPlan))
            .Cast<ViewPlan>()
            .Where(v => !v.IsTemplate)
            .FirstOrDefault(v => ManagedElementMark.TryRead(v, out var token, out _) && string.Equals(token, key, StringComparison.Ordinal));
    }

    /// <summary>The red regions in the view that carry a review mark of any package, for the diff.</summary>
    public IReadOnlyList<ExistingReviewMark> ReadMarks(View view)
    {
        if (view is null) throw new ArgumentNullException(nameof(view));

        var found = new List<ExistingReviewMark>();
        foreach (var region in new FilteredElementCollector(_document, view.Id).OfClass(typeof(FilledRegion)))
        {
            if (!ManagedElementMark.TryRead(region, out var token, out var signature)) continue;
            if (!ReviewMarkKey.TryParse(token, out _)) continue;
            found.Add(new ExistingReviewMark(region.UniqueId, token, signature));
        }
        return found;
    }

    /// <summary>The elements this tool has overridden in the view, with their original look.</summary>
    public IReadOnlyList<RecordedElementOverride> ReadOverrides(View view) =>
        ReviewViewOverrideStorage.Read(view ?? throw new ArgumentNullException(nameof(view)));

    /// <summary>
    /// Finds or makes the view, compares it with the plan and applies the difference. Opens its own
    /// transactions: call it outside any transaction.
    /// </summary>
    /// <param name="defaultName">Used only when the view has to be created, e.g. <see cref="ReviewOutputNaming.ReviewView"/>.</param>
    public ReviewMarkupResult Mark(ReviewPackage package, ReviewMarkupPlan plan, string defaultName)
    {
        if (package is null) throw new ArgumentNullException(nameof(package));
        if (plan is null) throw new ArgumentNullException(nameof(plan));
        if (plan.PackageId != package.PackageId)
            throw new ArgumentException("The plan was built for another package.", nameof(plan));

        var items = new List<ReviewMarkupItem>();
        using (var group = new TransactionGroup(_document, "標示防火檢討結果"))
        {
            group.Start();
            try
            {
                View view;
                using (var transaction = new Transaction(_document, "建立防火檢討視圖"))
                {
                    transaction.Start();
                    var ensured = EnsureView(package, defaultName, items);
                    if (ensured is null)
                    {
                        transaction.RollBack();
                        group.RollBack();
                        return new ReviewMarkupResult(plan.PackageId, plan.RunId, null, items,
                            items.LastOrDefault()?.Message ?? "無法建立防火檢討視圖");
                    }
                    view = ensured;
                    transaction.Commit();
                }

                var diff = ReviewMarkupDiff.Compute(plan, ReadMarks(view), ReadOverrides(view));
                using (var transaction = new Transaction(_document, "更新防火檢討標示"))
                {
                    transaction.Start();
                    ApplyRegions(view, diff, items);
                    ApplyOverrides(view, diff, items);
                    transaction.Commit();
                }

                foreach (var skipped in plan.Skipped)
                    items.Add(new ReviewMarkupItem(ApplyOutcome.Skipped, skipped.Subject, null, skipped.Reason));

                if (group.Assimilate() != TransactionStatus.Committed)
                    return new ReviewMarkupResult(plan.PackageId, plan.RunId, null, items, "Revit 未能提交檢討視圖的變更");

                return new ReviewMarkupResult(plan.PackageId, plan.RunId, view.UniqueId, items);
            }
            catch (Exception exception)
            {
                if (group.GetStatus() == TransactionStatus.Started) group.RollBack();
                return new ReviewMarkupResult(plan.PackageId, plan.RunId, null, items, "標示過程發生錯誤，已整批復原：" + exception.Message);
            }
        }
    }

    /// <summary>
    /// Resolves what 定位 can go to: the UniqueIds that are elements of this document. Linked subjects
    /// and deleted elements are left out; the caller shows the rest.
    /// </summary>
    public IReadOnlyList<ElementId> Locate(ReviewTableEntry entry)
    {
        if (entry is null) throw new ArgumentNullException(nameof(entry));
        if (entry.IsLinked) return Array.Empty<ElementId>();

        return entry.LocateUniqueIds
            .Select(uid => _document.GetElement(uid))
            .Where(e => e is not null)
            .Select(e => e!.Id)
            .ToList();
    }

    private ViewPlan? EnsureView(ReviewPackage package, string defaultName, List<ReviewMarkupItem> items)
    {
        var existing = FindView(package.PackageId);
        if (existing is not null) return existing;

        const string subject = "防火檢討視圖";
        if (!(_document.GetElement(package.SourceFloorPlanUniqueId) is ViewPlan source) || source.IsTemplate)
        {
            items.Add(new ReviewMarkupItem(ApplyOutcome.Failed, subject, null,
                "找不到工作包的來源平面圖，無法複製出檢討視圖。請重新執行「建立視圖」"));
            return null;
        }

        if (!source.CanViewBeDuplicated(ViewDuplicateOption.Duplicate))
        {
            items.Add(new ReviewMarkupItem(ApplyOutcome.Failed, subject, source.UniqueId,
                $"Revit 不允許複製來源平面圖「{source.Name}」"));
            return null;
        }

        try
        {
            var view = (ViewPlan)_document.GetElement(source.Duplicate(ViewDuplicateOption.Duplicate));
            view.Name = ReviewOutputNaming.MakeUnique(
                string.IsNullOrWhiteSpace(defaultName) ? ReviewOutputNaming.ReviewView(null, source.Name) : defaultName,
                IsViewNameTaken);
            ManagedElementMark.Write(view, new ManagedOutputKey(package.PackageId, ManagedOutputKind.ReviewView), view.Name);
            items.Add(new ReviewMarkupItem(ApplyOutcome.Created, subject + "「" + view.Name + "」", view.UniqueId));
            return view;
        }
        catch (RevitApplicationException exception)
        {
            items.Add(new ReviewMarkupItem(ApplyOutcome.Failed, subject, source.UniqueId, "Revit 拒絕建立檢討視圖：" + exception.Message));
            return null;
        }
    }

    private void ApplyRegions(View view, ReviewMarkupDiff diff, List<ReviewMarkupItem> items)
    {
        ElementId? typeId = null;
        var elevation = (view as ViewPlan)?.GenLevel?.Elevation ?? 0.0;

        foreach (var change in diff.Regions)
        {
            switch (change.Action)
            {
                case ReviewMarkAction.Unchanged:
                    continue;

                case ReviewMarkAction.Remove:
                    Remove(change.Existing!, diff.Plan.PackageId, items);
                    continue;

                case ReviewMarkAction.Create:
                case ReviewMarkAction.Update:
                    typeId ??= RegionType();
                    Draw(view, change, diff.Plan.PackageId, typeId, elevation, items);
                    continue;
            }
        }
    }

    /// <summary>
    /// Draws a planned region. An update deletes the old region and draws it again — a Filled Region's
    /// boundary cannot be edited from the API — but only after the new one exists, so a refusal leaves
    /// the old mark in place.
    /// </summary>
    private void Draw(View view, ReviewRegionChange change, Guid packageId, ElementId typeId, double elevation, List<ReviewMarkupItem> items)
    {
        var planned = change.Planned!;
        var old = change.Existing is null ? null : _document.GetElement(change.Existing.ElementUniqueId);
        if (old is not null && !ManagedElementMark.IsOwnedBy(old, packageId))
        {
            items.Add(new ReviewMarkupItem(ApplyOutcome.Skipped, planned.Description, old.UniqueId,
                "既有元素的擁有權標記已不屬於本工作包，保留不動"));
            return;
        }

        try
        {
            var loops = planned.Loops.Select(l => ToCurveLoop(l, elevation)).Where(l => l is not null).Select(l => l!).ToList();
            if (loops.Count == 0)
            {
                items.Add(new ReviewMarkupItem(ApplyOutcome.Skipped, planned.Description, null, "區劃邊界過短或退化，無法建立填滿區域"));
                return;
            }

            var region = FilledRegion.Create(_document, typeId, view.Id, loops);
            ManagedElementMark.Write(region, planned.Key, planned.Signature);
            WriteComment(region, planned.Key.ToLabel());

            if (old is not null) _document.Delete(old.Id);
            items.Add(new ReviewMarkupItem(old is null ? ApplyOutcome.Created : ApplyOutcome.Updated, planned.Description, region.UniqueId,
                old is null ? null : change.Reason));
        }
        catch (RevitApplicationException exception)
        {
            items.Add(new ReviewMarkupItem(ApplyOutcome.Failed, planned.Description, old?.UniqueId, "Revit 拒絕建立填滿區域：" + exception.Message));
        }
    }

    private void Remove(ExistingReviewMark mark, Guid packageId, List<ReviewMarkupItem> items)
    {
        const string description = "已不需要的未符合區劃標示";
        var element = _document.GetElement(mark.ElementUniqueId);
        if (element is null) return;

        // Asked again of the element itself: the view is live, and ownership is what makes deletion safe.
        if (!ManagedElementMark.IsOwnedBy(element, packageId))
        {
            items.Add(new ReviewMarkupItem(ApplyOutcome.Skipped, description, element.UniqueId, "擁有權標記已不屬於本工作包，保留不動"));
            return;
        }

        try
        {
            _document.Delete(element.Id);
            items.Add(new ReviewMarkupItem(ApplyOutcome.Deleted, description, mark.ElementUniqueId));
        }
        catch (RevitApplicationException exception)
        {
            items.Add(new ReviewMarkupItem(ApplyOutcome.Failed, description, mark.ElementUniqueId, "Revit 拒絕刪除：" + exception.Message));
        }
    }

    private void ApplyOverrides(View view, ReviewMarkupDiff diff, List<ReviewMarkupItem> items)
    {
        var records = ReadOverrides(view).ToDictionary(r => r.ElementUniqueId, StringComparer.Ordinal);
        OverrideGraphicSettings? red = null;

        foreach (var change in diff.Overrides)
        {
            var element = _document.GetElement(change.ElementUniqueId);
            var description = change.Planned?.Description ?? "先前標示的元素 " + change.ElementUniqueId;

            if (element is null)
            {
                // The element is gone, so is anything to restore; the record goes with it.
                records.Remove(change.ElementUniqueId);
                items.Add(new ReviewMarkupItem(ApplyOutcome.Skipped, description, change.ElementUniqueId, "元素已不在模型中"));
                continue;
            }

            try
            {
                var current = RevitOverrideStateCodec.Write(_document, view.GetElementOverrides(element.Id));
                switch (change.Action)
                {
                    case ReviewMarkAction.Create:
                    case ReviewMarkAction.Update:
                    case ReviewMarkAction.Unchanged:
                    {
                        red ??= RedOverride();
                        var applied = RevitOverrideStateCodec.Write(_document, red);
                        var record = change.Recorded ?? new RecordedElementOverride(change.ElementUniqueId, diff.Plan.RunId, current, applied);
                        if (!string.Equals(current, applied, StringComparison.Ordinal)) view.SetElementOverrides(element.Id, red);
                        else if (change.Action == ReviewMarkAction.Unchanged) continue;

                        records[change.ElementUniqueId] = record.ForRun(diff.Plan.RunId, applied);
                        items.Add(new ReviewMarkupItem(change.Action == ReviewMarkAction.Create ? ApplyOutcome.Created : ApplyOutcome.Updated,
                            description, element.UniqueId, change.Action == ReviewMarkAction.Create ? null : change.Reason));
                        break;
                    }

                    case ReviewMarkAction.Remove:
                    {
                        var record = change.Recorded!;
                        records.Remove(change.ElementUniqueId);
                        if (ReviewOverrideRestore.Decide(record, current) == OverrideRestoreDecision.RestoreOriginal)
                        {
                            view.SetElementOverrides(element.Id, RevitOverrideStateCodec.Read(_document, record.OriginalState));
                            items.Add(new ReviewMarkupItem(ApplyOutcome.Deleted, "元素紅色標示 " + element.UniqueId, element.UniqueId, change.Reason));
                        }
                        else
                        {
                            items.Add(new ReviewMarkupItem(ApplyOutcome.Skipped, "元素紅色標示 " + element.UniqueId, element.UniqueId,
                                "使用者已修改這個元素在檢討視圖的顯示，保留現況"));
                        }
                        break;
                    }
                }
            }
            catch (RevitApplicationException exception)
            {
                items.Add(new ReviewMarkupItem(ApplyOutcome.Failed, description, element.UniqueId, "Revit 拒絕變更元素顯示：" + exception.Message));
            }
        }

        ReviewViewOverrideStorage.Write(view, records.Values.OrderBy(r => r.ElementUniqueId, StringComparer.Ordinal));
    }

    private OverrideGraphicSettings RedOverride()
    {
        var solid = SolidFillPatternId();
        var settings = new OverrideGraphicSettings();
        settings.SetProjectionLineColor(Red);
        settings.SetCutLineColor(Red);
        if (solid != ElementId.InvalidElementId)
        {
            settings.SetSurfaceForegroundPatternId(solid);
            settings.SetCutForegroundPatternId(solid);
        }
        settings.SetSurfaceForegroundPatternColor(Red);
        settings.SetCutForegroundPatternColor(Red);
        return settings;
    }

    private ElementId RegionType()
    {
        var types = new FilteredElementCollector(_document).OfClass(typeof(FilledRegionType)).Cast<FilledRegionType>().ToList();
        var existing = types.FirstOrDefault(t => string.Equals(t.Name, RegionTypeName, StringComparison.Ordinal));
        if (existing is not null) return existing.Id;

        var baseType = types.FirstOrDefault()
            ?? throw new InvalidOperationException("這個專案沒有任何填滿區域類型，無法建立未符合區劃的紅色標示。");
        var created = (FilledRegionType)baseType.Duplicate(RegionTypeName);
        created.ForegroundPatternId = HatchPatternId();
        created.ForegroundPatternColor = Red;
        created.BackgroundPatternId = ElementId.InvalidElementId;
        created.IsMasking = false;
        return created.Id;
    }

    private ElementId HatchPatternId()
    {
        var existing = new FilteredElementCollector(_document).OfClass(typeof(FillPatternElement)).Cast<FillPatternElement>()
            .FirstOrDefault(p => string.Equals(p.Name, HatchPatternName, StringComparison.Ordinal));
        if (existing is not null) return existing.Id;

        var spacing = UnitUtils.ConvertToInternalUnits(2.0, UnitTypeId.Millimeters);
        var pattern = new FillPattern(HatchPatternName, FillPatternTarget.Drafting, FillPatternHostOrientation.ToView, Math.PI / 4, spacing);
        return FillPatternElement.Create(_document, pattern).Id;
    }

    private ElementId SolidFillPatternId() =>
        new FilteredElementCollector(_document).OfClass(typeof(FillPatternElement)).Cast<FillPatternElement>()
            .FirstOrDefault(p => p.GetFillPattern().IsSolidFill)?.Id ?? ElementId.InvalidElementId;

    private CurveLoop? ToCurveLoop(IReadOnlyList<Point2D> points, double elevation)
    {
        var tolerance = _document.Application.ShortCurveTolerance;
        var kept = new List<XYZ>();
        foreach (var point in points)
        {
            var xyz = new XYZ(point.X, point.Y, elevation);
            if (kept.Count == 0 || kept[kept.Count - 1].DistanceTo(xyz) > tolerance) kept.Add(xyz);
        }
        if (kept.Count > 1 && kept[0].DistanceTo(kept[kept.Count - 1]) <= tolerance) kept.RemoveAt(kept.Count - 1);
        if (kept.Count < 3) return null;

        var loop = new CurveLoop();
        for (var i = 0; i < kept.Count; i++)
            loop.Append(Line.CreateBound(kept[i], kept[(i + 1) % kept.Count]));
        return loop;
    }

    private static void WriteComment(Element element, string text)
    {
        var parameter = element.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
        if (parameter is null || parameter.IsReadOnly || parameter.StorageType != StorageType.String) return;
        parameter.Set(text);
    }

    private bool IsViewNameTaken(string name) => new FilteredElementCollector(_document)
        .OfClass(typeof(View))
        .Cast<View>()
        .Any(view => string.Equals(view.Name, name, StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// The record of the elements this tool overrode in a review view, kept on the view itself: each entry
/// holds the element, the run that painted it, its original look and what the tool set.
/// </summary>
internal static class ReviewViewOverrideStorage
{
    private static readonly Guid ViewId = new Guid("3b8e0f6a-94d2-4c1e-8a57-2f60c9d4b713");
    private static readonly Guid EntryId = new Guid("a7c41d2e-5b93-4f08-b6e1-8d2f35c0a9e4");

    public static IReadOnlyList<RecordedElementOverride> Read(View view)
    {
        var schema = Schema.Lookup(ViewId);
        var entrySchema = Schema.Lookup(EntryId);
        if (schema is null || entrySchema is null) return Array.Empty<RecordedElementOverride>();

        var entity = view.GetEntity(schema);
        if (entity is null || !entity.IsValid()) return Array.Empty<RecordedElementOverride>();

        var found = new List<RecordedElementOverride>();
        foreach (var entry in entity.Get<IList<Entity>>(schema.GetField("Entries")) ?? new List<Entity>())
        {
            var uid = entry.Get<string>(entrySchema.GetField("ElementUniqueId"));
            var run = entry.Get<string>(entrySchema.GetField("RunId"));
            if (string.IsNullOrWhiteSpace(uid) || !Guid.TryParse(run, out var runId) || runId == Guid.Empty) continue;
            found.Add(new RecordedElementOverride(uid, runId,
                entry.Get<string>(entrySchema.GetField("OriginalState")) ?? string.Empty,
                entry.Get<string>(entrySchema.GetField("AppliedState")) ?? string.Empty));
        }
        return found;
    }

    /// <summary>Replaces the record. Requires an open transaction.</summary>
    public static void Write(View view, IEnumerable<RecordedElementOverride> records)
    {
        var (schema, entrySchema) = GetOrCreate();
        var entries = records.Select(r =>
        {
            var entry = new Entity(entrySchema);
            entry.Set(entrySchema.GetField("ElementUniqueId"), r.ElementUniqueId);
            entry.Set(entrySchema.GetField("RunId"), r.RunId.ToString("D"));
            entry.Set(entrySchema.GetField("OriginalState"), r.OriginalState);
            entry.Set(entrySchema.GetField("AppliedState"), r.AppliedState);
            return entry;
        }).ToList();

        var entity = new Entity(schema);
        entity.Set<IList<Entity>>(schema.GetField("Entries"), entries);
        view.SetEntity(entity);
    }

    private static (Schema View, Schema Entry) GetOrCreate()
    {
        var entry = Schema.Lookup(EntryId) ?? Build(EntryId, "BCR_ReviewViewOverride_v1",
            "One element overridden by the fire compartment review tool in its review view.", b =>
            {
                b.AddSimpleField("ElementUniqueId", typeof(string));
                b.AddSimpleField("RunId", typeof(string));
                b.AddSimpleField("OriginalState", typeof(string));
                b.AddSimpleField("AppliedState", typeof(string));
            });
        var view = Schema.Lookup(ViewId) ?? Build(ViewId, "BCR_ReviewViewOverrides_v1",
            "Elements overridden by the fire compartment review tool in its review view, schema 1.0.", b =>
                b.AddArrayField("Entries", typeof(Entity)).SetSubSchemaGUID(EntryId));
        return (view, entry);
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
}
