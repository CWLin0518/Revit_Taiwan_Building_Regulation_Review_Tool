using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using BuildingRegulationReview.Application.Checks;
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
/// and openings, the 帷幕牆 annotations and 層間帶 of 帷幕牆規格 §7.1, and the record of what each
/// overridden element looked like before.
/// </summary>
/// <remarks>
/// <para>
/// The view is a duplicate of the source floor plan made once per package and found again by its
/// ownership mark (<see cref="ManagedOutputKind.ReviewView"/>), so a user who renames it keeps it.
/// Marking never touches the source plan, the Area Plan or any other view.
/// </para>
/// <para>
/// A 層間帶 is a stretch of one façade rather than a plan outline, so it cannot be drawn in that plan:
/// each curtain wall that has one gets an elevation of its own, owned the same way
/// (<see cref="ManagedOutputKind.CurtainWallElevation"/>) and made by the tool so the user does not
/// have to prepare anything. Where every mark goes comes from the placement the check wrote into the
/// result's evidence, never from reading the model again: the plan is rebuilt from the stored run
/// (spec 13.1), and re-reading would put the mark where the model is now while the result says what it
/// was.
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

    /// <summary>How far from square two directions may be and still count as square, as a dot product.</summary>
    private const double SquareTolerance = 1e-3;

    /// <summary>Revit's own short curve tolerance in feet, for the places a document is not to hand.</summary>
    private const double XyzTolerance = 1.0 / 256.0;

    private static readonly Color Red = new Color(255, 0, 0);

    /// <summary>How much of the façade a 帷幕牆 elevation shows around the bands it was made for.</summary>
    private static readonly double ElevationMarginFeet = UnitUtils.ConvertToInternalUnits(1000.0, UnitTypeId.Millimeters);

    /// <summary>How deep into the model a 帷幕牆 elevation looks, so the wall itself is inside the cut.</summary>
    private static readonly double ElevationDepthFeet = UnitUtils.ConvertToInternalUnits(3000.0, UnitTypeId.Millimeters);

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

    /// <summary>
    /// The marks of any package the view already carries, for the diff: the red Filled Regions of a
    /// 區劃 or a 層間帶, and the 帷幕牆 annotations. A text note left out here would be taken for missing
    /// and drawn a second time on every re-run (帷幕牆規格 §10 案例 19).
    /// </summary>
    public IReadOnlyList<ExistingReviewMark> ReadMarks(View view)
    {
        if (view is null) throw new ArgumentNullException(nameof(view));

        var found = new List<ExistingReviewMark>();
        var marked = new FilteredElementCollector(_document, view.Id)
            .WherePasses(new ElementMulticlassFilter(new List<Type> { typeof(FilledRegion), typeof(TextNote) }));

        foreach (var element in marked)
        {
            if (!ManagedElementMark.TryRead(element, out var token, out var signature)) continue;
            if (!ReviewMarkKey.TryParse(token, out _)) continue;
            found.Add(new ExistingReviewMark(element.UniqueId, token, signature));
        }
        return found;
    }

    /// <summary>
    /// The package's 帷幕牆 elevations, by the UniqueId of the curtain wall each one looks at. Found by
    /// the ownership mark, like every other output, so a user who renames one keeps it.
    /// </summary>
    public IReadOnlyDictionary<string, ViewSection> FindCurtainWallElevations(Guid packageId)
    {
        if (packageId == Guid.Empty) throw new ArgumentException("Package ID cannot be empty.", nameof(packageId));

        var found = new Dictionary<string, ViewSection>(StringComparer.Ordinal);
        foreach (var view in new FilteredElementCollector(_document).OfClass(typeof(ViewSection)).Cast<ViewSection>().Where(v => !v.IsTemplate))
        {
            if (!ManagedElementMark.TryRead(view, out var token, out _)) continue;
            if (!ManagedOutputKey.TryParse(token, out var key)) continue;
            if (key.PackageId != packageId || key.Kind != ManagedOutputKind.CurtainWallElevation) continue;

            // Two views for one wall can only come from a copy; the first by Id is the one taken over.
            if (!found.ContainsKey(key.Subject)) found.Add(key.Subject, view);
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

                // Every view this package marks in, so a 層間帶 already drawn in an elevation is taken
                // over instead of drawn again: marks the diff never sees look like marks that are gone.
                var elevations = FindCurtainWallElevations(package.PackageId)
                    .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
                var existing = ReadMarks(view).Concat(elevations.Values.SelectMany(ReadMarks)).ToList();
                var diff = ReviewMarkupDiff.Compute(plan, existing, ReadOverrides(view));

                // The elevations come first and in a transaction of their own: a view has to exist and
                // be regenerated before anything can be drawn in it.
                if (diff.Bands.Any(b => b.Action == ReviewMarkAction.Create || b.Action == ReviewMarkAction.Update))
                {
                    using (var transaction = new Transaction(_document, "建立帷幕牆檢討立面"))
                    {
                        transaction.Start();
                        EnsureCurtainWallElevations(view, diff, elevations, items);
                        transaction.Commit();
                    }
                }

                using (var transaction = new Transaction(_document, "更新防火檢討標示"))
                {
                    transaction.Start();
                    ApplyRegions(view, diff, items);
                    ApplyOverrides(view, diff, items);
                    ApplyNotes(view, diff, items);
                    ApplyBands(diff, elevations, items);
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

    /// <summary>
    /// One elevation per curtain wall that has a 層間帶 to draw (帷幕牆規格 §7.1). The tool makes it
    /// itself, marked and named like every other output, so the user does not have to prepare a view
    /// before running a review; an elevation an earlier run made is taken over and, when this run's
    /// bands fall outside what it shows, widened rather than replaced.
    /// </summary>
    private void EnsureCurtainWallElevations(
        View reviewView,
        ReviewMarkupDiff diff,
        Dictionary<string, ViewSection> elevations,
        List<ReviewMarkupItem> items)
    {
        var groups = diff.Bands
            .Where(b => b.Action == ReviewMarkAction.Create || b.Action == ReviewMarkAction.Update)
            .Select(b => b.Planned!)
            .GroupBy(b => b.CurtainWallUniqueId, StringComparer.Ordinal)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .ToList();

        ElementId? typeId = null;
        foreach (var group in groups)
        {
            var bands = group.ToList();
            if (group.Key.Length == 0)
            {
                Skip(bands, "結果沒有記錄層間帶所屬的帷幕牆，無法決定要畫在哪一面立面上", items);
                continue;
            }

            var frame = SpandrelFrame.For(bands);
            if (frame is null)
            {
                Skip(bands, "層間帶沒有長度，無法建立填滿區域", items);
                continue;
            }

            if (elevations.TryGetValue(group.Key, out var existing))
            {
                EnsureVisible(existing, bands, items);
                continue;
            }

            typeId ??= SectionTypeId();
            if (typeId == ElementId.InvalidElementId)
            {
                foreach (var band in bands)
                    items.Add(new ReviewMarkupItem(ApplyOutcome.Failed, band.Description, null,
                        "這個專案沒有任何剖面視圖類型，無法建立帷幕牆檢討立面"));
                continue;
            }

            var created = CreateCurtainWallElevation(reviewView, diff.Plan.PackageId, group.Key, frame, typeId, items);
            if (created is not null) elevations[group.Key] = created;
        }
    }

    private ViewSection? CreateCurtainWallElevation(
        View reviewView,
        Guid packageId,
        string curtainWallUniqueId,
        SpandrelFrame frame,
        ElementId sectionTypeId,
        List<ReviewMarkupItem> items)
    {
        var subject = ManagedOutputKey.Describe(ManagedOutputKind.CurtainWallElevation);
        try
        {
            var section = ViewSection.CreateSection(_document, sectionTypeId, frame.SectionBox());
            section.Name = ReviewOutputNaming.MakeUnique(
                ReviewOutputNaming.CurtainWallElevation(reviewView.Name, CurtainWallLabel(curtainWallUniqueId)),
                IsViewNameTaken);
            ManagedElementMark.Write(
                section,
                new ManagedOutputKey(packageId, ManagedOutputKind.CurtainWallElevation, curtainWallUniqueId),
                section.Name);

            // The bands are drawn in the next transaction, but the view has to be complete first.
            _document.Regenerate();
            items.Add(new ReviewMarkupItem(ApplyOutcome.Created, subject + "「" + section.Name + "」", section.UniqueId));
            return section;
        }
        catch (RevitApplicationException exception)
        {
            items.Add(new ReviewMarkupItem(ApplyOutcome.Failed, subject, null, "Revit 拒絕建立帷幕牆檢討立面：" + exception.Message));
            return null;
        }
    }

    /// <summary>
    /// Widens an existing elevation's crop until this run's bands are inside it. Only ever widened: a
    /// mark nobody can see is no mark, and a crop the user narrowed is still theirs everywhere else.
    /// </summary>
    private void EnsureVisible(ViewSection elevation, IReadOnlyList<PlannedSpandrelBand> bands, List<ReviewMarkupItem> items)
    {
        var subject = $"檢討立面「{elevation.Name}」的裁剪範圍";
        if (!elevation.CropBoxActive) return;

        try
        {
            var box = elevation.CropBox;
            var inverse = box.Transform.Inverse;
            double minX = box.Min.X, minY = box.Min.Y, maxX = box.Max.X, maxY = box.Max.Y;

            foreach (var corner in bands.SelectMany(b => b.Placement.Corners()))
            {
                var local = inverse.OfPoint(ToFeet(corner));
                minX = Math.Min(minX, local.X - ElevationMarginFeet);
                minY = Math.Min(minY, local.Y - ElevationMarginFeet);
                maxX = Math.Max(maxX, local.X + ElevationMarginFeet);
                maxY = Math.Max(maxY, local.Y + ElevationMarginFeet);
            }

            if (minX >= box.Min.X && minY >= box.Min.Y && maxX <= box.Max.X && maxY <= box.Max.Y) return;

            var widened = new BoundingBoxXYZ { Transform = box.Transform };
            widened.Min = new XYZ(minX, minY, box.Min.Z);
            widened.Max = new XYZ(maxX, maxY, box.Max.Z);
            elevation.CropBox = widened;
            items.Add(new ReviewMarkupItem(ApplyOutcome.Updated, subject, elevation.UniqueId, "放大以容納本次的層間帶"));
        }
        catch (RevitApplicationException exception)
        {
            items.Add(new ReviewMarkupItem(ApplyOutcome.Skipped, subject, elevation.UniqueId,
                "Revit 拒絕調整裁剪範圍，層間帶仍會建立，但可能落在可見範圍外：" + exception.Message));
        }
    }

    /// <summary>
    /// The band as a closed rectangle on the elevation's own sketch plane. Null when the view no longer
    /// looks along this façade, which is the one case where drawing would silently distort the band.
    /// </summary>
    private CurveLoop? BandLoop(ViewSection elevation, PlannedSpandrelBand band)
    {
        var tolerance = _document.Application.ShortCurveTolerance;
        var along = ToFeet(band.Placement.EndMm, 0.0) - ToFeet(band.Placement.StartMm, 0.0);
        if (along.GetLength() <= tolerance) return null;

        var normal = elevation.ViewDirection.Normalize();
        if (Math.Abs(along.Normalize().DotProduct(normal)) > SquareTolerance) return null;
        if (Math.Abs(normal.DotProduct(XYZ.BasisZ)) > SquareTolerance) return null;

        // Onto the plane the view sketches in. The band is already parallel to it — the test above says
        // so — so the projection only slides the rectangle across, it does not change its shape.
        var origin = elevation.Origin;
        var corners = band.Placement.Corners()
            .Select(ToFeet)
            .Select(p => p - normal.Multiply(p.Subtract(origin).DotProduct(normal)))
            .ToList();

        var loop = new CurveLoop();
        for (var i = 0; i < corners.Count; i++)
        {
            var from = corners[i];
            var to = corners[(i + 1) % corners.Count];
            if (from.DistanceTo(to) <= tolerance) return null;
            loop.Append(Line.CreateBound(from, to));
        }
        return loop;
    }

    /// <summary>
    /// What the elevation is called after. This reads the model, but only for a name: where the band
    /// goes still comes from the result's evidence alone (帷幕牆規格 §7.1).
    /// </summary>
    private string CurtainWallLabel(string curtainWallUniqueId)
    {
        var element = _document.GetElement(curtainWallUniqueId);
        if (element is null) return "帷幕牆";

        var mark = element.get_Parameter(BuiltInParameter.ALL_MODEL_MARK)?.AsString();
        return string.IsNullOrWhiteSpace(mark)
            ? "帷幕牆 " + element.Id.Value.ToString(CultureInfo.InvariantCulture)
            : mark!.Trim();
    }

    private ElementId TextNoteTypeId()
    {
        var preferred = _document.GetDefaultElementTypeId(ElementTypeGroup.TextNoteType);
        if (preferred != ElementId.InvalidElementId && _document.GetElement(preferred) is TextNoteType) return preferred;

        return new FilteredElementCollector(_document).OfClass(typeof(TextNoteType)).Cast<TextNoteType>()
            .OrderBy(t => t.Id.Value).FirstOrDefault()?.Id ?? ElementId.InvalidElementId;
    }

    private ElementId SectionTypeId() =>
        new FilteredElementCollector(_document).OfClass(typeof(ViewFamilyType)).Cast<ViewFamilyType>()
            .Where(t => t.ViewFamily == ViewFamily.Section)
            .OrderBy(t => t.Id.Value).FirstOrDefault()?.Id ?? ElementId.InvalidElementId;

    private static void Skip(IEnumerable<PlannedSpandrelBand> bands, string reason, List<ReviewMarkupItem> items)
    {
        foreach (var band in bands)
            items.Add(new ReviewMarkupItem(ApplyOutcome.Skipped, band.Description, null, reason));
    }

    private static XYZ ToFeet(PlacementCorner corner) => ToFeet(corner.PlanMm, corner.ElevationMm);

    private static XYZ ToFeet(Point2D planMm, double elevationMm) => new XYZ(
        UnitUtils.ConvertToInternalUnits(planMm.X, UnitTypeId.Millimeters),
        UnitUtils.ConvertToInternalUnits(planMm.Y, UnitTypeId.Millimeters),
        UnitUtils.ConvertToInternalUnits(elevationMm, UnitTypeId.Millimeters));

    /// <summary>A plan point in millimetres, at an elevation Revit already gave us in feet.</summary>
    private static XYZ AtElevation(Point2D planMm, double elevationFeet) => new XYZ(
        UnitUtils.ConvertToInternalUnits(planMm.X, UnitTypeId.Millimeters),
        UnitUtils.ConvertToInternalUnits(planMm.Y, UnitTypeId.Millimeters),
        elevationFeet);

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
                    Remove(change.Existing!, diff.Plan.PackageId, items, "已不需要的未符合區劃標示");
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

    private void Remove(ExistingReviewMark mark, Guid packageId, List<ReviewMarkupItem> items, string description)
    {
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

    /// <summary>
    /// 帷幕牆規格 §7.1: the measurements a failing junction was judged on, written at the junction in the
    /// review plan. A note is updated where it stands rather than redrawn, because its text and its
    /// position are both writable — unlike a Filled Region's boundary — so a leader somebody added to it
    /// survives the next run.
    /// </summary>
    private void ApplyNotes(View view, ReviewMarkupDiff diff, List<ReviewMarkupItem> items)
    {
        ElementId? typeId = null;
        var elevation = (view as ViewPlan)?.GenLevel?.Elevation ?? 0.0;

        foreach (var change in diff.Notes)
        {
            switch (change.Action)
            {
                case ReviewMarkAction.Unchanged:
                    continue;

                case ReviewMarkAction.Remove:
                    Remove(change.Existing!, diff.Plan.PackageId, items, "已不需要的帷幕牆交接處標註");
                    continue;

                case ReviewMarkAction.Create:
                case ReviewMarkAction.Update:
                    typeId ??= TextNoteTypeId();
                    if (typeId == ElementId.InvalidElementId)
                    {
                        items.Add(new ReviewMarkupItem(ApplyOutcome.Failed, change.Planned!.Description, null,
                            "這個專案沒有任何文字類型，無法建立交接處標註"));
                        continue;
                    }
                    WriteNote(view, change, diff.Plan.PackageId, typeId, elevation, items);
                    continue;
            }
        }
    }

    private void WriteNote(
        View view,
        ReviewMarkChange<PlannedReviewNote> change,
        Guid packageId,
        ElementId typeId,
        double elevation,
        List<ReviewMarkupItem> items)
    {
        var planned = change.Planned!;
        var old = change.Existing is null ? null : _document.GetElement(change.Existing.ElementUniqueId);
        if (old is not null && !ManagedElementMark.IsOwnedBy(old, packageId))
        {
            items.Add(new ReviewMarkupItem(ApplyOutcome.Skipped, planned.Description, old.UniqueId,
                "既有元素的擁有權標記已不屬於本工作包，保留不動"));
            return;
        }

        var origin = AtElevation(planned.Placement.MidpointMm, elevation);
        try
        {
            if (old is TextNote existing)
            {
                existing.Text = planned.Text;
                existing.Coord = origin;
                ManagedElementMark.Write(existing, planned.Key, planned.Signature);
                WriteComment(existing, planned.Key.ToLabel());
                items.Add(new ReviewMarkupItem(ApplyOutcome.Updated, planned.Description, existing.UniqueId, change.Reason));
                return;
            }

            var note = TextNote.Create(_document, view.Id, origin, planned.Text, typeId);
            ManagedElementMark.Write(note, planned.Key, planned.Signature);
            WriteComment(note, planned.Key.ToLabel());

            if (old is not null) _document.Delete(old.Id);
            items.Add(new ReviewMarkupItem(old is null ? ApplyOutcome.Created : ApplyOutcome.Updated, planned.Description,
                note.UniqueId, old is null ? null : change.Reason));
        }
        catch (RevitApplicationException exception)
        {
            items.Add(new ReviewMarkupItem(ApplyOutcome.Failed, planned.Description, old?.UniqueId, "Revit 拒絕建立文字標註：" + exception.Message));
        }
    }

    /// <summary>
    /// 帷幕牆規格 §7.1 (CW-V): the 層間帶 in red, in the elevation of the curtain wall it lies on. The
    /// elevation was found or made beforehand; a band with none left is reported rather than dropped.
    /// </summary>
    private void ApplyBands(ReviewMarkupDiff diff, IReadOnlyDictionary<string, ViewSection> elevations, List<ReviewMarkupItem> items)
    {
        ElementId? typeId = null;

        foreach (var change in diff.Bands)
        {
            switch (change.Action)
            {
                case ReviewMarkAction.Unchanged:
                    continue;

                case ReviewMarkAction.Remove:
                    Remove(change.Existing!, diff.Plan.PackageId, items, "已不需要的層間帶標示");
                    continue;

                case ReviewMarkAction.Create:
                case ReviewMarkAction.Update:
                {
                    var planned = change.Planned!;
                    if (!elevations.TryGetValue(planned.CurtainWallUniqueId, out var elevation))
                    {
                        // EnsureCurtainWallElevations already said why, on this same band.
                        continue;
                    }

                    typeId ??= RegionType();
                    DrawBand(elevation, change, diff.Plan.PackageId, typeId, items);
                    continue;
                }
            }
        }
    }

    private void DrawBand(
        ViewSection elevation,
        ReviewMarkChange<PlannedSpandrelBand> change,
        Guid packageId,
        ElementId typeId,
        List<ReviewMarkupItem> items)
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
            var loop = BandLoop(elevation, planned);
            if (loop is null)
            {
                items.Add(new ReviewMarkupItem(ApplyOutcome.Skipped, planned.Description, elevation.UniqueId,
                    $"檢討立面「{elevation.Name}」的視圖方向已不沿著這面帷幕牆，無法在其中畫出層間帶。" +
                    "請刪除該立面後重新標示"));
                return;
            }

            var region = FilledRegion.Create(_document, typeId, elevation.Id, new List<CurveLoop> { loop });
            ManagedElementMark.Write(region, planned.Key, planned.Signature);
            WriteComment(region, planned.Key.ToLabel());

            if (old is not null) _document.Delete(old.Id);
            items.Add(new ReviewMarkupItem(old is null ? ApplyOutcome.Created : ApplyOutcome.Updated, planned.Description,
                region.UniqueId, old is null ? null : change.Reason));
        }
        catch (RevitApplicationException exception)
        {
            items.Add(new ReviewMarkupItem(ApplyOutcome.Failed, planned.Description, old?.UniqueId, "Revit 拒絕建立填滿區域：" + exception.Message));
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

    /// <summary>
    /// Which way a 帷幕牆 elevation looks and how much of the façade it has to show: the frame the section
    /// is cut on, worked out from the bands one curtain wall needs drawn.
    /// </summary>
    /// <remarks>
    /// Every band of one curtain wall sits on that wall's plan location line — the geometry layer puts
    /// them there with <c>PointAt</c> — so one plane holds all of them, whichever storey they are on, and
    /// one elevation per wall is enough. The frame comes from the placements alone, which is what lets a
    /// band be marked from a stored result without reading the model again (spec 13.1).
    /// </remarks>
    private sealed class SpandrelFrame
    {
        private SpandrelFrame(Transform transform, double halfLengthFeet, double halfHeightFeet)
        {
            Transform = transform;
            HalfLengthFeet = halfLengthFeet;
            HalfHeightFeet = halfHeightFeet;
        }

        public Transform Transform { get; }
        public double HalfLengthFeet { get; }
        public double HalfHeightFeet { get; }

        /// <summary>Null when no band has a direction to look at, which leaves nothing to cut a section on.</summary>
        public static SpandrelFrame? For(IReadOnlyList<PlannedSpandrelBand> bands)
        {
            var directed = bands.FirstOrDefault(b => !b.Placement.IsPoint);
            if (directed is null) return null;

            var along = ToFeet(directed.Placement.EndMm, 0.0) - ToFeet(directed.Placement.StartMm, 0.0);
            if (along.GetLength() <= XyzTolerance) return null;

            var basisX = along.Normalize();
            var basisY = XYZ.BasisZ;
            var anchor = ToFeet(directed.Placement.StartMm, 0.0);

            double alongMin = double.MaxValue, alongMax = double.MinValue, upMin = double.MaxValue, upMax = double.MinValue;
            foreach (var corner in bands.SelectMany(b => b.Placement.Corners()))
            {
                var point = ToFeet(corner);
                var offset = point.Subtract(anchor).DotProduct(basisX);
                alongMin = Math.Min(alongMin, offset);
                alongMax = Math.Max(alongMax, offset);
                upMin = Math.Min(upMin, point.Z);
                upMax = Math.Max(upMax, point.Z);
            }

            var transform = Transform.Identity;
            transform.BasisX = basisX;
            transform.BasisY = basisY;
            transform.BasisZ = basisX.CrossProduct(basisY);
            transform.Origin = anchor
                .Add(basisX.Multiply((alongMin + alongMax) / 2.0))
                .Add(basisY.Multiply((upMin + upMax) / 2.0));

            return new SpandrelFrame(
                transform,
                ((alongMax - alongMin) / 2.0) + ElevationMarginFeet,
                ((upMax - upMin) / 2.0) + ElevationMarginFeet);
        }

        /// <summary>The section box <see cref="ViewSection.CreateSection"/> cuts the elevation on.</summary>
        public BoundingBoxXYZ SectionBox()
        {
            var box = new BoundingBoxXYZ { Transform = Transform };
            box.Min = new XYZ(-HalfLengthFeet, -HalfHeightFeet, -ElevationDepthFeet);
            box.Max = new XYZ(HalfLengthFeet, HalfHeightFeet, ElevationDepthFeet);
            return box;
        }
    }
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
