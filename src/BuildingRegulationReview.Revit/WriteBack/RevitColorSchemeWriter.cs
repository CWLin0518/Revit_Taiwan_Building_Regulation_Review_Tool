using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.Revit.DB;
using BuildingRegulationReview.Application.WriteBack;
using BuildingRegulationReview.Domain.Regions;
using RevitApplicationException = Autodesk.Revit.Exceptions.ApplicationException;

namespace BuildingRegulationReview.Revit.WriteBack;

/// <summary>
/// Keeps the Area Color Scheme saying what the 區劃 drafts say (spec 10.5 item 3): one entry per
/// 區劃 name, coloured the way the Editor draws it, and the scheme applied to the package's Area
/// Plan.
/// </summary>
/// <remarks>
/// Two things decide almost everything here. The scheme is a project-wide element, so the tool works
/// only on one carrying its own ownership mark; a scheme somebody set up is read, never edited, and
/// the run says so instead. And Revit refuses several of these edits outright — an entry in use will
/// not be removed, a duplicated value will not be added, a parameter the scheme cannot key on will
/// not be set. Spec 10.5 item 3 anticipates exactly this: 若 API 版本不支援完整編輯，套用預先配置方案
/// 並列出需人工處理項. Every refusal becomes a <see cref="ManualAction"/> naming what to click, and
/// the run carries on.
/// </remarks>
internal sealed class RevitColorSchemeWriter
{
    private static readonly ElementId AreasCategoryId = new ElementId(BuiltInCategory.OST_Areas);

    /// <summary>
    /// The parameter the entries key on. Write-back already sets the Area's name from the 區劃 name
    /// (see <see cref="RevitZoneWriteBack"/>), so keying on it keeps one value behind both the plan
    /// and the colours instead of two that have to be kept in step.
    /// </summary>
    private static readonly ElementId NameParameterId = new ElementId(BuiltInParameter.ROOM_NAME);

    private readonly Document _document;

    public RevitColorSchemeWriter(Document document) =>
        _document = document ?? throw new ArgumentNullException(nameof(document));

    /// <summary>
    /// Syncs the scheme and applies it to the view. Requires an open transaction; everything it
    /// cannot do lands on <paramref name="log"/> as a manual item rather than as an exception.
    /// </summary>
    public void Sync(ViewPlan areaPlan, Guid packageId, ColorSchemeEntries entries, ApplyResult.Builder log)
    {
        if (areaPlan is null) throw new ArgumentNullException(nameof(areaPlan));
        if (entries is null) throw new ArgumentNullException(nameof(entries));
        if (log is null) throw new ArgumentNullException(nameof(log));

        var scheme = Owned(packageId);
        if (scheme is null)
        {
            // No zones and no scheme of ours: there is nothing to colour and nothing of ours left
            // over, so making a scheme now would leave the project an element nobody asked for.
            if (entries.IsEmpty) return;
            scheme = Adopt(areaPlan, packageId, log);
            if (scheme is null) return;
        }

        if (!Key(scheme, log)) return;

        var plan = ColorSchemePlan.Build(entries, Read(scheme));
        foreach (var action in plan.ManualActions) log.Manual(action);

        Execute(scheme, plan, log);
        log.Note(plan.Summary);
        Apply(areaPlan, scheme, log);
    }

    // ---- finding the scheme ------------------------------------------------------------------

    /// <summary>The scheme this package created, if it still exists.</summary>
    private ColorFillScheme? Owned(Guid packageId) => Schemes()
        .FirstOrDefault(scheme => ManagedElementMark.IsOwnedBy(scheme, packageId));

    /// <summary>
    /// Makes one by duplicating a scheme of the same Area Scheme, which is the only way the API
    /// offers to create a <c>ColorFillScheme</c>. The duplicate is marked as ours immediately, so a
    /// second run finds it instead of making another.
    /// </summary>
    private ColorFillScheme? Adopt(ViewPlan areaPlan, Guid packageId, ApplyResult.Builder log)
    {
        var areaSchemeId = areaPlan.AreaScheme?.Id;
        var source = Schemes()
            .Where(scheme => scheme.CategoryId == AreasCategoryId)
            .FirstOrDefault(scheme => areaSchemeId is null || scheme.AreaSchemeId == areaSchemeId);

        if (source is null)
        {
            log.Manual(
                ManagedOutputKey.Describe(ManagedOutputKind.ColorFillScheme),
                "這個專案的面積配置底下沒有任何色彩配置可以複製，Revit 的 API 只能複製既有的色彩配置，不能從無到有建立",
                "請在 Revit 的「色彩配置」對話框中為這個面積配置新增一個配置，再重新套用");
            return null;
        }

        var name = ReviewOutputNaming.MakeUnique(
            ReviewOutputNaming.Default(areaPlan.AreaScheme?.Name, areaPlan.Name),
            candidate => !source.IsValidSchemeName(candidate));

        try
        {
            var created = _document.GetElement(source.Duplicate(name)) as ColorFillScheme;
            if (created is null)
            {
                log.Manual(
                    ManagedOutputKey.Describe(ManagedOutputKind.ColorFillScheme),
                    "Revit 複製色彩配置後沒有回傳可用的元素",
                    "請手動複製一份色彩配置並套用到這個 Area Plan");
                return null;
            }

            ManagedElementMark.Write(created, new ManagedOutputKey(packageId, ManagedOutputKind.ColorFillScheme), name);
            log.Note("已建立本檢討套件專用的面積色彩配置「" + name + "」。");
            return created;
        }
        catch (RevitApplicationException exception)
        {
            log.Manual(
                ManagedOutputKey.Describe(ManagedOutputKind.ColorFillScheme),
                "Revit 拒絕複製色彩配置：" + exception.Message,
                "請手動複製一份色彩配置並套用到這個 Area Plan");
            return null;
        }
    }

    /// <summary>
    /// Points the scheme at the Area name. A scheme keyed on something else would colour the plan by
    /// a value the tool never writes, so the run stops here rather than colouring by accident.
    /// </summary>
    private bool Key(ColorFillScheme scheme, ApplyResult.Builder log)
    {
        try
        {
            if (scheme.IsByRange) scheme.IsByRange = false;

            if (scheme.ParameterDefinition == NameParameterId) return true;

            if (!scheme.GetSupportedParameterIds().Contains(NameParameterId) ||
                !scheme.IsValidParameterDefinitionId(NameParameterId))
            {
                log.Manual(
                    ManagedOutputKey.Describe(ManagedOutputKind.ColorFillScheme),
                    "這個 Revit 版本不接受用「名稱」當色彩配置的依據參數",
                    "請在「色彩配置」對話框中自行選一個依據參數並手動設定各區劃顏色");
                return false;
            }

            scheme.ParameterDefinition = NameParameterId;
            return true;
        }
        catch (RevitApplicationException exception)
        {
            log.Manual(
                ManagedOutputKey.Describe(ManagedOutputKind.ColorFillScheme),
                "Revit 拒絕設定色彩配置的依據參數：" + exception.Message,
                "請在「色彩配置」對話框中自行選定依據參數再重新套用");
            return false;
        }
    }

    // ---- entries -----------------------------------------------------------------------------

    private static IReadOnlyList<ExistingColorSchemeEntry> Read(ColorFillScheme scheme) => scheme.GetEntries()
        .Where(entry => entry.StorageType == StorageType.String)
        .Select(entry =>
        {
            var color = entry.Color;
            var known = color is not null && color.IsValid;
            return new ExistingColorSchemeEntry(
                entry.GetStringValue() ?? string.Empty,
                known ? new ZoneColor(color!.Red, color.Green, color.Blue) : default,
                entry.IsInUse,
                known);
        })
        .ToList();

    private void Execute(ColorFillScheme scheme, ColorSchemePlan plan, ApplyResult.Builder log)
    {
        var fillPatternId = SolidFillPatternId(scheme);

        foreach (var step in plan.Steps)
        {
            switch (step.Change)
            {
                case ColorSchemeChange.Add:
                    Add(scheme, step, fillPatternId, log);
                    break;
                case ColorSchemeChange.Update:
                    Recolor(scheme, step, log);
                    break;
                case ColorSchemeChange.Remove:
                    Remove(scheme, step, log);
                    break;
            }
        }
    }

    private static void Add(ColorFillScheme scheme, ColorSchemeStep step, ElementId fillPatternId, ApplyResult.Builder log)
    {
        try
        {
            var entry = new ColorFillSchemeEntry(StorageType.String);
            entry.SetStringValue(step.Value);
            entry.Color = ToRevitColor(step.Color);
            if (fillPatternId != ElementId.InvalidElementId) entry.FillPatternId = fillPatternId;

            var consistency = scheme.IsEntryConsistentWithScheme(entry);
            if (consistency == EntryAndSchemeConsistency.ValueDuplicated)
            {
                // Revit generates an entry of its own as soon as an Area carries the value, so an
                // addition can arrive to find the row already there. Recolouring it is the same
                // outcome, and it keeps the run idempotent instead of reporting a false problem.
                Recolor(scheme, step, log);
                return;
            }

            if (consistency != EntryAndSchemeConsistency.Consistent)
            {
                log.Manual(
                    step.Description,
                    "Revit 不接受這個色彩項目（" + consistency + "）",
                    "請在「色彩配置」對話框中手動加入這個名稱並指定顏色");
                return;
            }

            scheme.AddEntry(entry);
        }
        catch (RevitApplicationException exception)
        {
            log.Manual(
                step.Description,
                "Revit 拒絕新增這個色彩項目：" + exception.Message,
                "請在「色彩配置」對話框中手動加入這個名稱並指定顏色");
        }
    }

    private static void Recolor(ColorFillScheme scheme, ColorSchemeStep step, ApplyResult.Builder log)
    {
        var entry = Find(scheme, step.Value);
        if (entry is null)
        {
            log.Manual(
                step.Description,
                "要更新的色彩項目已經不在配置裡",
                "請重新套用一次，或在「色彩配置」對話框中手動加入這個名稱");
            return;
        }

        try
        {
            entry.Color = ToRevitColor(step.Color);
            if (!scheme.CanUpdateEntry(entry))
            {
                log.Manual(
                    step.Description,
                    "Revit 不允許更新這個色彩項目",
                    "請在「色彩配置」對話框中手動改成這個顏色");
                return;
            }

            scheme.UpdateEntry(entry);
        }
        catch (RevitApplicationException exception)
        {
            log.Manual(
                step.Description,
                "Revit 拒絕更新這個色彩項目：" + exception.Message,
                "請在「色彩配置」對話框中手動改成這個顏色");
        }
    }

    private static void Remove(ColorFillScheme scheme, ColorSchemeStep step, ApplyResult.Builder log)
    {
        var entry = Find(scheme, step.Value);
        if (entry is null) return;

        try
        {
            // Asked of Revit at the moment of removal, not of the list read earlier: an Area placed
            // during this very run can have taken the value since.
            if (entry.IsInUse || !scheme.CanRemoveEntry(entry))
            {
                log.Manual(
                    step.Description,
                    "已經沒有對應的區劃，但 Revit 不允許刪除這個色彩項目",
                    "請確認沒有面積還在使用這個名稱後，於「色彩配置」對話框中手動移除");
                return;
            }

            scheme.RemoveEntry(entry);
        }
        catch (RevitApplicationException exception)
        {
            log.Manual(
                step.Description,
                "Revit 拒絕刪除這個色彩項目：" + exception.Message,
                "請於「色彩配置」對話框中手動移除");
        }
    }

    private static ColorFillSchemeEntry? Find(ColorFillScheme scheme, string value) => scheme.GetEntries()
        .FirstOrDefault(entry => entry.StorageType == StorageType.String
            && string.Equals(entry.GetStringValue(), value, StringComparison.Ordinal));

    // ---- applying it to the view ---------------------------------------------------------------

    /// <summary>
    /// Puts the scheme on the Area Plan. A view already showing a scheme this tool did not create is
    /// left as it is: that is somebody's decision about their own view, and overruling it silently
    /// would be exactly the overwrite spec 18 item 6 forbids.
    /// </summary>
    private static void Apply(ViewPlan areaPlan, ColorFillScheme scheme, ApplyResult.Builder log)
    {
        try
        {
            var current = areaPlan.GetColorFillSchemeId(AreasCategoryId);
            if (current == scheme.Id) return;

            if (current != ElementId.InvalidElementId)
            {
                log.Manual(
                    ManagedOutputKey.Describe(ManagedOutputKind.ColorFillScheme),
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "這個 Area Plan 目前套用的是另一個色彩配置，工具不會覆寫既有設定（本套件的配置是「{0}」）",
                        scheme.Name),
                    "若要看到區劃顏色，請在視圖的「色彩配置」中改選這個配置");
                return;
            }

            if (!areaPlan.CanApplyColorFillScheme(AreasCategoryId, scheme.Id))
            {
                log.Manual(
                    ManagedOutputKey.Describe(ManagedOutputKind.ColorFillScheme),
                    "Revit 不允許把這個色彩配置套用到這個 Area Plan",
                    "請在視圖的「色彩配置」中手動選取這個配置");
                return;
            }

            areaPlan.SetColorFillSchemeId(AreasCategoryId, scheme.Id);
        }
        catch (RevitApplicationException exception)
        {
            log.Manual(
                ManagedOutputKey.Describe(ManagedOutputKind.ColorFillScheme),
                "Revit 拒絕套用色彩配置：" + exception.Message,
                "請在視圖的「色彩配置」中手動選取這個配置");
        }
    }

    // ---- helpers -----------------------------------------------------------------------------

    private IEnumerable<ColorFillScheme> Schemes() => new FilteredElementCollector(_document)
        .OfClass(typeof(ColorFillScheme))
        .Cast<ColorFillScheme>();

    /// <summary>
    /// A solid drafting pattern, so a new entry actually fills the 區劃. An entry Revit built itself
    /// already has one; a copied one is used when the project has no solid pattern of its own.
    /// </summary>
    private ElementId SolidFillPatternId(ColorFillScheme scheme)
    {
        var solid = new FilteredElementCollector(_document)
            .OfClass(typeof(FillPatternElement))
            .Cast<FillPatternElement>()
            .FirstOrDefault(pattern =>
            {
                var fill = pattern.GetFillPattern();
                return fill is not null && fill.IsSolidFill && fill.Target == FillPatternTarget.Drafting;
            });

        if (solid is not null) return solid.Id;

        return scheme.GetEntries()
            .Select(entry => entry.FillPatternId)
            .FirstOrDefault(id => id is not null && id != ElementId.InvalidElementId)
            ?? ElementId.InvalidElementId;
    }

    private static Color ToRevitColor(ZoneColor color) => new Color(color.Red, color.Green, color.Blue);
}
