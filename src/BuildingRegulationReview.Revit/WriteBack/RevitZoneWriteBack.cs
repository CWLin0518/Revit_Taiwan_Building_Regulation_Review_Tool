using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.Revit.DB;
using BuildingRegulationReview.Application.WriteBack;
using BuildingRegulationReview.Domain.Geometry;
using BuildingRegulationReview.Revit.ReviewPackages;
using RevitApplicationException = Autodesk.Revit.Exceptions.ApplicationException;

namespace BuildingRegulationReview.Revit.WriteBack;

/// <summary>
/// Writes the approved 區劃 drafts into the model (spec 10.5): the Area Boundary Lines that fence
/// each contiguous part, the Area inside it and its tag; the Area Color Scheme that colours them;
/// and the Drafting View holding the 單線圖 copies. Everything it creates carries this package's
/// ownership mark, which is what makes a second run a no-op and what confines deletion to the tool's
/// own work.
/// </summary>
/// <remarks>
/// The whole run lives in one <c>TransactionGroup</c>. Each stage is its own transaction inside it,
/// because Revit has to regenerate between placing boundaries and placing the Areas they enclose,
/// and a rollback of the group undoes every stage together — the Drafting View and the colour
/// scheme included, so a run either leaves all of its outputs or none of them.
/// </remarks>
public sealed class RevitZoneWriteBack
{
    private readonly Document _document;
    private readonly Dictionary<ManagedElementKey, ElementId> _written = new Dictionary<ManagedElementKey, ElementId>();
    private readonly List<PlacedArea> _placedAreas = new List<PlacedArea>();
    private SketchPlane? _sketchPlane;

    public RevitZoneWriteBack(Document document) =>
        _document = document ?? throw new ArgumentNullException(nameof(document));

    /// <summary>
    /// Runs the plan against the package's views. Returns what happened; it does not throw for
    /// anything the user can act on, because a write-back that fails still has to report a log.
    /// </summary>
    public ApplyResult Apply(ApplyPlan plan, ZoneWriteBackRequest request)
    {
        if (plan is null) throw new ArgumentNullException(nameof(plan));
        if (request is null) throw new ArgumentNullException(nameof(request));
        if (_document.IsModifiable)
            throw new InvalidOperationException("Write-back owns its transaction group and requires an unmodified document.");

        var log = new ApplyResult.Builder(plan);
        if (plan.IsEmpty) return log.Complete();

        if (!(_document.GetElement(request.AreaPlanUniqueId) is ViewPlan view) || view.ViewType != ViewType.AreaPlan)
            return log.RolledBack("找不到這個檢討套件的 Area Plan，沒有寫入任何東西。");

        var level = view.GenLevel;
        if (level is null) return log.RolledBack("這個 Area Plan 沒有對應的樓層，無法建立邊界線。");

        _written.Clear();
        _placedAreas.Clear();
        _sketchPlane = null;

        using (var group = new TransactionGroup(_document, "寫回防火區劃"))
        {
            group.Start();
            try
            {
                Run(plan, ApplyStage.Remove, "移除不再需要的面積與標註", step => Remove(plan, step, log));
                Run(plan, ApplyStage.Boundaries, "建立與更新面積邊界線", step => Boundary(plan, step, log, view, level));
                Run(plan, ApplyStage.Areas, "建立與更新面積", step => Area(plan, step, log, view));
                Run(plan, ApplyStage.Tags, "建立與更新面積標註", step => Tag(plan, step, log, view));
                DetailCurves(plan, request, log);
                ColorScheme(plan, request, view, log);

                if (request.FailurePolicy == ApplyFailurePolicy.RollBackEverything && log.FailureCount > 0)
                {
                    group.RollBack();
                    return log.RolledBack(string.Format(
                        CultureInfo.InvariantCulture,
                        "有 {0} 個元素無法寫入，依設定整批復原。",
                        log.FailureCount));
                }

                VerifyPlacedAreas(log);

                if (group.Assimilate() != TransactionStatus.Committed)
                    return log.RolledBack("Revit 無法提交寫回交易。");
            }
            catch (Exception exception)
            {
                if (group.GetStatus() == TransactionStatus.Started) group.RollBack();
                return log.RolledBack(exception.Message);
            }
        }

        return log.Complete();
    }

    // ---- stages ------------------------------------------------------------------------------

    /// <summary>
    /// Runs one stage in its own transaction. A stage that cannot commit is fatal and is left to the
    /// caller's catch, which rolls the group back: a half-written ring of boundaries is worse than
    /// no boundaries at all.
    /// </summary>
    private void Run(ApplyPlan plan, ApplyStage stage, string name, Action<ApplyStep> execute)
    {
        var steps = plan.StepsOf(stage);
        if (steps.Count == 0) return;

        using (var transaction = new Transaction(_document, name))
        {
            transaction.Start();
            foreach (var step in steps) execute(step);

            if (transaction.Commit() != TransactionStatus.Committed)
                throw new InvalidOperationException("Revit 無法提交「" + name + "」。");
        }
    }

    private void Remove(ApplyPlan plan, ApplyStep step, ApplyResult.Builder log)
    {
        var element = Resolve(step.ElementUniqueId);
        if (element is null)
        {
            log.Skipped(step, "元素已經不在模型中。");
            return;
        }

        // Spec 10.4: the preview said this is ours, but the model is live and the Editor is modeless,
        // so the element itself has the last word before anything is deleted.
        if (!ManagedElementMark.IsOwnedBy(element, plan.PackageId))
        {
            log.Skipped(step, "這個元素沒有本套件的擁有權標記，不會刪除。");
            return;
        }

        try
        {
            _document.Delete(element.Id);
            log.Deleted(step);
        }
        catch (RevitApplicationException exception)
        {
            log.Failed(step, exception.Message);
        }
    }

    private void Boundary(ApplyPlan plan, ApplyStep step, ApplyResult.Builder log, ViewPlan view, Level level)
    {
        if (step.Change == ApplyChangeKind.Delete)
        {
            Remove(plan, step, log);
            return;
        }

        var planned = step.Planned!;
        var curve = ToLine(planned, level.Elevation);
        if (curve is null)
        {
            log.Failed(step, "這一段邊界太短，Revit 無法建立線段。");
            return;
        }

        try
        {
            var existing = Owned(plan, step) as ModelCurve;
            if (existing is not null)
            {
                existing.SetGeometryCurve(curve, false);
                ManagedElementMark.Write(existing, step.Key, planned.Signature);
                log.Updated(step, existing.UniqueId);
                return;
            }

            // One sketch plane for the whole run: SketchPlane.Create makes a new element every call,
            // and a plan with fifty walls has no reason to leave fifty identical planes behind.
            _sketchPlane ??= SketchPlane.Create(
                _document,
                Plane.CreateByNormalAndOrigin(XYZ.BasisZ, new XYZ(0, 0, level.Elevation)));
            var created = _document.Create.NewAreaBoundaryLine(_sketchPlane, curve, view);
            ManagedElementMark.Write(created, step.Key, planned.Signature);
            Remember(step, created);
            log.Created(step, created.UniqueId);
        }
        catch (RevitApplicationException exception)
        {
            log.Failed(step, exception.Message);
        }
    }

    private void Area(ApplyPlan plan, ApplyStep step, ApplyResult.Builder log, ViewPlan view)
    {
        var planned = step.Planned!;
        if (planned.Placement is null)
        {
            log.Failed(step, "這個面積沒有放置點。");
            return;
        }

        var placement = planned.Placement.Value;

        try
        {
            var area = Owned(plan, step) as Autodesk.Revit.DB.Area;
            if (area is null)
            {
                area = _document.Create.NewArea(view, new UV(placement.X, placement.Y));
                SetName(area, planned.ZoneName);
                ManagedElementMark.Write(area, step.Key, planned.Signature);
                Remember(step, area);
                _placedAreas.Add(new PlacedArea(area.Id, planned));
                log.Created(step, area.UniqueId);
                return;
            }

            MoveTo(area, placement);
            SetName(area, planned.ZoneName);
            ManagedElementMark.Write(area, step.Key, planned.Signature);
            Remember(step, area);
            _placedAreas.Add(new PlacedArea(area.Id, planned));
            log.Updated(step, area.UniqueId);
        }
        catch (RevitApplicationException exception)
        {
            log.Failed(step, exception.Message);
        }
    }

    private void Tag(ApplyPlan plan, ApplyStep step, ApplyResult.Builder log, ViewPlan view)
    {
        var planned = step.Planned!;
        if (planned.Placement is null)
        {
            log.Failed(step, "這個標註沒有放置點。");
            return;
        }

        var placement = planned.Placement.Value;
        var areaKey = new ManagedElementKey(
            step.Key.PackageId,
            step.Key.ZoneId,
            ManagedElementKind.Area,
            step.Key.PartIndex,
            0);

        if (!(Find(plan, areaKey) is Autodesk.Revit.DB.Area area))
        {
            log.Skipped(step, "找不到要標註的面積，這次不建立標註。");
            return;
        }

        try
        {
            // A tag whose host Area was rebuilt is deleted with it, so an update whose element has
            // gone falls through to creation rather than failing and waiting for the next run.
            var tag = Owned(plan, step) as AreaTag;
            if (tag is null)
            {
                var created = _document.Create.NewAreaTag(view, area, new UV(placement.X, placement.Y));
                ManagedElementMark.Write(created, step.Key, planned.Signature);
                Remember(step, created);
                log.Created(step, created.UniqueId);
                return;
            }

            tag.TagHeadPosition = new XYZ(placement.X, placement.Y, tag.TagHeadPosition?.Z ?? 0);
            ManagedElementMark.Write(tag, step.Key, planned.Signature);
            Remember(step, tag);
            log.Updated(step, tag.UniqueId);
        }
        catch (RevitApplicationException exception)
        {
            log.Failed(step, exception.Message);
        }
    }

    /// <summary>
    /// Copies the 區劃 outline into the package's Drafting View (spec 10.5 item 4). The view is
    /// found or created inside this run's group, so a rollback takes it with everything else rather
    /// than leaving an empty view behind.
    /// </summary>
    private void DetailCurves(ApplyPlan plan, ZoneWriteBackRequest request, ApplyResult.Builder log)
    {
        var steps = plan.StepsOf(ApplyStage.DetailCurves);
        if (steps.Count == 0) return;

        using (var transaction = new Transaction(_document, "建立與更新單線圖"))
        {
            transaction.Start();

            var lookup = new RevitDraftingViewWriter(_document).Ensure(request.Package, request.DefaultOutputName);
            if (lookup.View is null)
            {
                // A failure rather than a skip: the drafts are not in the model, so the Editor has
                // to go on showing unapplied changes, and a run set to roll back on any failure has
                // to roll this one back too.
                log.Manual(lookup.ManualAction!);
                foreach (var step in steps) log.Failed(step, "沒有可用的單線圖視圖。");
                transaction.RollBack();
                return;
            }

            foreach (var step in steps) Copy(plan, step, log, lookup.View);

            if (lookup.Created)
            {
                // Identity before name (spec 10.5 item 5): the package records the UniqueId in the
                // same group as the view itself, so the two can never end up existing apart.
                new RevitReviewPackageRepository(_document)
                    .Save(request.Package.WithDraftingView(lookup.View.UniqueId));
                log.Note("已建立單線圖視圖「" + lookup.View.Name + "」。");
            }

            if (transaction.Commit() != TransactionStatus.Committed)
                throw new InvalidOperationException("Revit 無法提交「建立與更新單線圖」。");
        }
    }

    private void Copy(ApplyPlan plan, ApplyStep step, ApplyResult.Builder log, ViewDrafting view)
    {
        if (step.Change == ApplyChangeKind.Delete)
        {
            Remove(plan, step, log);
            return;
        }

        var planned = step.Planned!;
        // A drafting view has no level, so the copy sits on the view's own plane; the XY is the same
        // as the boundary line's, which is what makes it a copy rather than a second drawing.
        var curve = ToLine(planned, 0.0);
        if (curve is null)
        {
            log.Failed(step, "這一段線太短，Revit 無法建立細部線。");
            return;
        }

        try
        {
            var existing = Owned(plan, step) as Autodesk.Revit.DB.DetailCurve;
            if (existing is not null && existing.OwnerViewId == view.Id)
            {
                existing.SetGeometryCurve(curve, false);
                ManagedElementMark.Write(existing, step.Key, planned.Signature);
                Remember(step, existing);
                log.Updated(step, existing.UniqueId);
                return;
            }

            var created = _document.Create.NewDetailCurve(view, curve);
            ManagedElementMark.Write(created, step.Key, planned.Signature);
            Remember(step, created);
            log.Created(step, created.UniqueId);
        }
        catch (RevitApplicationException exception)
        {
            log.Failed(step, exception.Message);
        }
    }

    /// <summary>
    /// Brings the Area Color Scheme in line with the 區劃 colours (spec 10.5 item 3). Whatever Revit
    /// refuses becomes a manual item on the log; nothing here is fatal, because a colour the user
    /// has to set by hand is not a reason to throw away the boundaries that did get written.
    /// </summary>
    private void ColorScheme(ApplyPlan plan, ZoneWriteBackRequest request, ViewPlan view, ApplyResult.Builder log)
    {
        using (var transaction = new Transaction(_document, "更新面積色彩配置"))
        {
            transaction.Start();
            new RevitColorSchemeWriter(_document).Sync(view, request.PackageId, plan.ColorEntries, log);

            if (transaction.Commit() != TransactionStatus.Committed)
                throw new InvalidOperationException("Revit 無法提交「更新面積色彩配置」。");
        }
    }

    /// <summary>
    /// Compares every Area written with what the drafts computed (spec 10.6). Read-only and after
    /// the stages have committed, so Revit has regenerated and the numbers are the real ones.
    /// </summary>
    private void VerifyPlacedAreas(ApplyResult.Builder log)
    {
        foreach (var placed in _placedAreas)
        {
            if (!(_document.GetElement(placed.ElementId) is Autodesk.Revit.DB.Area area)) continue;

            var note = AreaAgreement.Describe(
                placed.Planned.ZoneName ?? string.Empty,
                placed.Planned.NetAreaSquareMeters,
                PlanUnits.SquareFeetToSquareMeters(area.Area));
            if (note is not null) log.Note(note);
        }
    }

    // ---- helpers -----------------------------------------------------------------------------

    /// <summary>
    /// The element a step is about, but only when it is still this package's. An element that lost
    /// its mark, or was replaced by somebody else's, is treated as absent, so the step creates a new
    /// one instead of overwriting work that is not ours.
    /// </summary>
    private Element? Owned(ApplyPlan plan, ApplyStep step)
    {
        var element = Resolve(step.ElementUniqueId);
        return element is not null && ManagedElementMark.IsOwnedBy(element, plan.PackageId) ? element : null;
    }

    /// <summary>An element this run wrote, or one already in the model under the same key.</summary>
    private Element? Find(ApplyPlan plan, ManagedElementKey key)
    {
        if (_written.TryGetValue(key, out var id)) return _document.GetElement(id);
        return plan.ExistingElementIds.TryGetValue(key, out var uniqueId) ? Resolve(uniqueId) : null;
    }

    private void Remember(ApplyStep step, Element element) => _written[step.Key] = element.Id;

    private Element? Resolve(string? uniqueId) =>
        string.IsNullOrWhiteSpace(uniqueId) ? null : _document.GetElement(uniqueId);

    private Line? ToLine(PlannedElement planned, double elevation)
    {
        if (planned.Points.Count < 2) return null;

        var start = new XYZ(planned.Points[0].X, planned.Points[0].Y, elevation);
        var end = new XYZ(planned.Points[1].X, planned.Points[1].Y, elevation);
        return start.DistanceTo(end) < _document.Application.ShortCurveTolerance
            ? null
            : Line.CreateBound(start, end);
    }

    private static void SetName(Element area, string? zoneName)
    {
        if (string.IsNullOrWhiteSpace(zoneName)) return;
        var parameter = area.get_Parameter(BuiltInParameter.ROOM_NAME);
        if (parameter is not null && !parameter.IsReadOnly) parameter.Set(zoneName);
    }

    private static void MoveTo(Element area, Point2D placement)
    {
        if (!(area.Location is LocationPoint location)) return;

        var current = location.Point;
        if (Math.Abs(current.X - placement.X) < 1e-9 && Math.Abs(current.Y - placement.Y) < 1e-9) return;
        location.Point = new XYZ(placement.X, placement.Y, current.Z);
    }

    private sealed class PlacedArea
    {
        public PlacedArea(ElementId elementId, PlannedElement planned)
        {
            ElementId = elementId;
            Planned = planned;
        }

        public ElementId ElementId { get; }
        public PlannedElement Planned { get; }
    }
}
