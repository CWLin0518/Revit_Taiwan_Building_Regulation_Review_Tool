using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.Revit.DB;
using BuildingRegulationReview.Application.Reviews;
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

        if (!(_document.GetElement(request.AreaPlanUniqueId) is ViewPlan view) || view.ViewType != ViewType.AreaPlan)
        {
            return plan.IsEmpty
                ? log.Complete()
                : log.RolledBack("找不到這個檢討套件的 Area Plan，沒有寫入任何東西。");
        }

        // A plan with no steps still has something to report: the Areas the model already holds are
        // measured against the draft, which is the evidence spec 10.6 asks for and the only thing
        // that can let an already-correct package advance. Measuring opens no transaction.
        if (plan.IsEmpty)
        {
            _placedAreas.Clear();
            VerifyAreas(plan, log);
            return log.Complete();
        }

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
                ZoneUses(plan, log);
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

                VerifyAreas(plan, log);

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

            // One sketch plane for the whole run: SketchPlane.Create makes a new element every call,
            // and a plan with fifty walls has no reason to leave fifty identical planes behind.
            _sketchPlane ??= SketchPlane.Create(
                _document,
                Plane.CreateByNormalAndOrigin(XYZ.BasisZ, new XYZ(0, 0, level.Elevation)));
            var created = _document.Create.NewAreaBoundaryLine(_sketchPlane, curve, view);
            ManagedElementMark.Write(created, step.Key, planned.Signature);
            Remember(step, created);

            if (existing is null)
            {
                log.Created(step, created.UniqueId);
                return;
            }

            ReplaceWith(existing);
            log.Updated(step, created.UniqueId);
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

    /// <summary>
    /// Turns the leader off, which is what a tag sitting on its own Area wants: a leader would only
    /// point at itself, and the Area tag type may default to having one.
    /// </summary>
    /// <remarks>
    /// Revit refuses when it judges the head to fall outside the host — 「Head position of the tag
    /// with no leader could not be located outside of the host element」 — which a concave or narrow
    /// 區劃 can provoke even at the Area's own placement point. A tag with a leader is still a
    /// correct tag, so the refusal keeps the leader and says so, rather than losing the tag: the
    /// step would otherwise fail, and a failed step is what keeps the whole package out of
    /// 可開始檢討 over an annotation.
    /// </remarks>
    private static void DropLeader(AreaTag tag, string? zoneName, ApplyResult.Builder log)
    {
        if (!tag.HasLeader) return;

        try
        {
            tag.HasLeader = false;
        }
        catch (RevitApplicationException)
        {
            log.Note(string.Format(
                CultureInfo.InvariantCulture,
                "「{0}」的面積標註保留了引線：Revit 不接受在這個位置放置無引線的標註。",
                string.IsNullOrWhiteSpace(zoneName) ? "區劃" : zoneName));
        }
    }

    /// <summary>
    /// Moves an existing tag's head to <paramref name="head"/>, then takes the leader off again.
    /// </summary>
    /// <remarks>
    /// Revit only constrains the head of a <em>leaderless</em> tag — 「Head position of the tag with
    /// no leader could not be located outside of the host element」 — so moving one directly is
    /// refused whenever Revit judges the destination to be outside the host, which it did even for
    /// the Area's own location point. Lending the tag a leader for the move lifts that constraint;
    /// <see cref="DropLeader"/> then takes it away again, and keeps it when Revit still objects.
    /// </remarks>
    private static void MoveHead(AreaTag tag, UV head, string? zoneName, ApplyResult.Builder log)
    {
        var target = new XYZ(head.U, head.V, tag.TagHeadPosition?.Z ?? 0);
        var hadLeader = tag.HasLeader;

        if (!hadLeader) tag.HasLeader = true;

        try
        {
            tag.TagHeadPosition = target;
        }
        catch (RevitApplicationException)
        {
            // Put it back the way it was found rather than leaving a leader nobody asked for.
            if (!hadLeader) tag.HasLeader = false;
            throw;
        }

        DropLeader(tag, zoneName, log);
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

        // Revit judges the head against the Area's own extent, so the Area is asked where it is
        // rather than being told: the draft's representative point is where the Area was asked to
        // go, and Revit refuses a leaderless tag whose head it does not consider inside the host.
        var anchor = (area.Location as LocationPoint)?.Point;
        var head = anchor is null ? new UV(placement.X, placement.Y) : new UV(anchor.X, anchor.Y);

        try
        {
            // A tag whose host Area was rebuilt is deleted with it, so an update whose element has
            // gone falls through to creation rather than failing and waiting for the next run.
            var tag = Owned(plan, step) as AreaTag;
            if (tag is null)
            {
                AreaTag created;
                try
                {
                    created = _document.Create.NewAreaTag(view, area, head);
                }
                catch (RevitApplicationException exception)
                {
                    // 「Head position of the tag with no leader could not be located outside of the
                    // host element」: Revit will not place this annotation here, and no point this
                    // side of the API changes its mind. An annotation is not what the checks read —
                    // the boundaries and the Area are — so it is handed back as something to do by
                    // hand rather than failing the step, which would hold the whole 防火區劃檢討 out
                    // of 可開始檢討 over a label.
                    Handback(step, planned, log, exception);
                    return;
                }

                try
                {
                    DropLeader(created, planned.ZoneName, log);
                    ManagedElementMark.Write(created, step.Key, planned.Signature);
                }
                catch (RevitApplicationException)
                {
                    // The tag exists but could not be finished. Deleting it keeps the run from
                    // leaving an unmarked tag behind: the next run would neither own it nor replace
                    // it, so every attempt would add one more.
                    _document.Delete(created.Id);
                    throw;
                }

                Remember(step, created);
                log.Created(step, created.UniqueId);
                return;
            }

            MoveHead(tag, head, planned.ZoneName, log);
            ManagedElementMark.Write(tag, step.Key, planned.Signature);
            Remember(step, tag);
            log.Updated(step, tag.UniqueId);
        }
        catch (RevitApplicationException exception)
        {
            Handback(step, planned, log, exception);
        }
    }

    /// <summary>
    /// Gives an annotation Revit refused back to the user instead of failing over it. Recorded as
    /// skipped, not failed, because <c>ReviewPackageProgress</c> blocks Ready on failures and a
    /// missing label is not a boundary problem — the 區劃 itself is written, measured and reviewable.
    /// </summary>
    private static void Handback(ApplyStep step, PlannedElement planned, ApplyResult.Builder log, Exception exception)
    {
        // An update failed on a tag that is still in the model; only an add leaves nothing behind.
        // Telling the user to place one they already have is how duplicates get made.
        var exists = step.Change != ApplyChangeKind.Add;

        log.Skipped(step, (exists ? "Revit 不接受把面積標註移到這個位置：" : "Revit 不接受在這個位置建立面積標註：")
            + exception.Message);

        log.Manual(
            planned.Description,
            exists
                ? "Revit 拒絕把這個面積標註移到計畫的位置，標註仍在原處，該區劃的邊界與面積不受影響。"
                : "Revit 拒絕在這個位置建立面積標註，該區劃的邊界與面積本身不受影響。",
            exists
                ? "標註已經存在，不需要另外放置；若位置不理想，請在 Area Plan 中直接拖曳它，或改用帶引線的標註類型。"
                : "請在 Area Plan 中手動放置這個區劃的面積標註；改用帶引線的標註類型通常就能放。");
    }

    /// <summary>
    /// Copies the 區劃 outline into the package's Drafting View (spec 10.5 item 4). The view is
    /// found or created inside this run's group, so a rollback takes it with everything else rather
    /// than leaving an empty view behind.
    /// </summary>
    /// <summary>
    /// Writes 防火檢討_區劃用途 onto the Areas the drafts changed it on (<see cref="ZoneUseOperations"/>),
    /// after the Areas stage so an Area this run created can be written too.
    /// </summary>
    /// <remarks>
    /// Inside the write-back's own transaction group, not through
    /// <c>RevitFireReviewParameterWriter</c>, which opens a transaction of its own: geometry and 用途
    /// have to share one Undo and one rollback, or a run that rolls back its elements would leave
    /// their uses behind. Every refusal is recorded rather than swallowed — an unbound parameter is
    /// the common one, and a run that quietly dropped it would report 套用完成 over a model where the
    /// 第79條之2 review still sees nothing.
    /// </remarks>
    private void ZoneUses(ApplyPlan plan, ApplyResult.Builder log)
    {
        if (plan.ZoneUses.IsEmpty) return;

        using (var transaction = new Transaction(_document, "設定區劃用途"))
        {
            transaction.Start();
            foreach (var operation in plan.ZoneUses) ZoneUse(plan, operation, log);

            if (transaction.Commit() != TransactionStatus.Committed)
                throw new InvalidOperationException("Revit 無法提交「設定區劃用途」。");
        }
    }

    private void ZoneUse(ApplyPlan plan, ZoneUseOperation operation, ApplyResult.Builder log)
    {
        var element = Find(plan, operation.Key);
        if (element is null)
        {
            log.ZoneUse(ApplyOutcome.Skipped, operation, "找不到要設定的面積。", null);
            return;
        }

        // The same last word the deletion stage gives the element itself: the Editor is modeless and
        // the model is live, so ownership is re-checked here rather than trusted from the preview.
        if (!ManagedElementMark.IsOwnedBy(element, plan.PackageId))
        {
            log.ZoneUse(ApplyOutcome.Skipped, operation, "這個面積沒有本套件的擁有權標記，不會設定用途。", element.UniqueId);
            return;
        }

        var parameter = element.LookupParameter(ReviewInputSources.ZoneUse);
        if (parameter is null)
        {
            log.ZoneUse(
                ApplyOutcome.Failed,
                operation,
                $"這個面積沒有 {ReviewInputSources.ZoneUse} 參數，用途未寫入；請先執行「防火檢討參數設定」。",
                element.UniqueId);
            return;
        }

        if (parameter.StorageType != StorageType.String)
        {
            log.ZoneUse(
                ApplyOutcome.Failed,
                operation,
                $"{ReviewInputSources.ZoneUse} 不是文字參數，用途未寫入。",
                element.UniqueId);
            return;
        }

        if (parameter.IsReadOnly)
        {
            log.ZoneUse(ApplyOutcome.Failed, operation, $"{ReviewInputSources.ZoneUse} 是唯讀的，用途未寫入。", element.UniqueId);
            return;
        }

        // The preview was taken before the group opened, and the 批次設定面板 may have written since.
        // Re-reading costs nothing and keeps 同值不重寫 true at the moment it matters.
        if (string.Equals(parameter.AsString() ?? string.Empty, operation.TargetUse, StringComparison.Ordinal))
        {
            log.ZoneUse(ApplyOutcome.Skipped, operation, "這個面積的區劃用途已經是要設定的值，未重複寫入。", element.UniqueId);
            return;
        }

        try
        {
            if (!parameter.Set(operation.TargetUse))
            {
                log.ZoneUse(ApplyOutcome.Failed, operation, "Revit 拒絕寫入這個區劃用途。", element.UniqueId);
                return;
            }
        }
        catch (RevitApplicationException exception)
        {
            log.ZoneUse(ApplyOutcome.Failed, operation, exception.Message, element.UniqueId);
            return;
        }

        log.ZoneUse(ApplyOutcome.Updated, operation, null, element.UniqueId);
    }

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

            foreach (var step in steps)
            {
                if (step.Kind == ManagedElementKind.DetailLabel && step.Change != ApplyChangeKind.Delete)
                    Label(plan, step, log, lookup.View);
                else
                    Copy(plan, step, log, lookup.View);
            }

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
            var created = _document.Create.NewDetailCurve(view, curve);
            ManagedElementMark.Write(created, step.Key, planned.Signature);
            Remember(step, created);

            if (existing is not null && existing.OwnerViewId == view.Id)
            {
                ReplaceWith(existing);
                log.Updated(step, created.UniqueId);
                return;
            }

            log.Created(step, created.UniqueId);
        }
        catch (RevitApplicationException exception)
        {
            log.Failed(step, exception.Message);
        }
    }

    /// <summary>
    /// Writes one 單線圖 area label: a Text Note centred on the part, in the project's default text
    /// type, so it reads at whatever scale the user draws the view at.
    /// </summary>
    private void Label(ApplyPlan plan, ApplyStep step, ApplyResult.Builder log, ViewDrafting view)
    {
        var planned = step.Planned!;
        if (planned.Placement is null || string.IsNullOrWhiteSpace(planned.Text))
        {
            log.Failed(step, "這個單線圖面積標註沒有放置點或文字。");
            return;
        }

        var placement = planned.Placement.Value;
        var position = new XYZ(placement.X, placement.Y, 0.0);
        // Revit breaks Text Note lines on a carriage return, not a line feed.
        var text = planned.Text!.Replace("\n", "\r");

        try
        {
            var existing = Owned(plan, step) as TextNote;
            if (existing is not null && existing.OwnerViewId == view.Id)
            {
                existing.Coord = position;
                existing.Text = text;
                ManagedElementMark.Write(existing, step.Key, planned.Signature);
                Remember(step, existing);
                log.Updated(step, existing.UniqueId);
                return;
            }

            var typeId = _document.GetDefaultElementTypeId(ElementTypeGroup.TextNoteType);
            if (typeId == ElementId.InvalidElementId)
            {
                typeId = new FilteredElementCollector(_document).OfClass(typeof(TextNoteType)).FirstElementId();
            }

            if (typeId == ElementId.InvalidElementId)
            {
                log.Failed(step, "這個專案沒有任何文字類型，無法建立單線圖面積標註。");
                return;
            }

            var options = new TextNoteOptions(typeId)
            {
                HorizontalAlignment = HorizontalTextAlignment.Center,
                VerticalAlignment = VerticalTextAlignment.Middle
            };
            var created = TextNote.Create(_document, view.Id, position, text, options);
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
    /// Compares every one of this package's Areas with what the drafts computed (spec 10.6).
    /// Read-only and after the stages have committed, so Revit has regenerated and the numbers are
    /// the real ones.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The verdict goes on the result rather than straight to the log, because it is the one thing
    /// that decides whether the package may reach Ready. <c>ReviewPackageProgress</c> reads it back;
    /// nothing here judges, so a disagreement never throws away boundaries that were written.
    /// </para>
    /// <para>
    /// Both the Areas this run placed and the ones it left alone are measured. Measuring only what
    /// was written meant a re-apply on an already-correct model reported no evidence at all, and
    /// <c>ReviewPackageProgress.After</c> then left the package in 區劃草稿 — a package whose
    /// boundaries were right could never reach Ready, because being right is exactly what left
    /// nothing to write.
    /// </para>
    /// </remarks>
    private void VerifyAreas(ApplyPlan plan, ApplyResult.Builder log)
    {
        var measured = new HashSet<ElementId>();

        foreach (var placed in _placedAreas)
        {
            if (!(_document.GetElement(placed.ElementId) is Autodesk.Revit.DB.Area area)) continue;
            if (!measured.Add(area.Id)) continue;

            log.Area(Compare(placed.Planned, area));
        }

        foreach (var item in plan.UnchangedAreas)
        {
            // Only this package's own Areas: one that lost its mark, or that now belongs to somebody
            // else, is not ours to vouch for. The editor is modeless, so an Area can be deleted
            // between the preview and the run — reported rather than skipped, because a verdict is
            // only as good as the measurements it was given.
            var area = _document.GetElement(item.ElementUniqueId) as Autodesk.Revit.DB.Area;
            if (area is null || !ManagedElementMark.IsOwnedBy(area, plan.PackageId))
            {
                log.Area(AreaAgreement.Missing(
                    item.Planned!.Key, item.Planned.ZoneName, item.Planned.NetAreaSquareMeters, item.ElementUniqueId));
                continue;
            }

            if (!measured.Add(area.Id)) continue;

            log.Area(Compare(item.Planned!, area));
        }
    }

    private static AreaAgreementFinding Compare(PlannedElement planned, Autodesk.Revit.DB.Area area) =>
        AreaAgreement.Compare(
            planned.Key,
            planned.ZoneName,
            planned.NetAreaSquareMeters,
            PlanUnits.SquareFeetToSquareMeters(area.Area),
            elementUniqueId: area.UniqueId);

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

    /// <summary>
    /// Retires a line whose replacement has just been drawn. Lines are never moved in place:
    /// <c>SetGeometryCurve</c> honours the line's end joins, so moving one drags every line joined to
    /// it, and a ring updated segment by segment is pulled out of shape before the next segment is
    /// set — the 區劃 ends up nowhere near its draft. A fresh line joins nothing that will move.
    /// </summary>
    private void ReplaceWith(Element existing) => _document.Delete(existing.Id);

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
