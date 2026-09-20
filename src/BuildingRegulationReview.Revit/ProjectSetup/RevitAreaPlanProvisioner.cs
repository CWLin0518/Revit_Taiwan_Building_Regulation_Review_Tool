using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using BuildingRegulationReview.Application.ProjectSetup;
using BuildingRegulationReview.Domain.ReviewPackages;
using BuildingRegulationReview.Revit.ReviewPackages;

namespace BuildingRegulationReview.Revit.ProjectSetup;

public sealed class RevitAreaPlanProvisioner
{
    private readonly Document _document;

    public RevitAreaPlanProvisioner(Document document) =>
        _document = document ?? throw new ArgumentNullException(nameof(document));

    public AreaPlanProvisioningResult Provision(ReviewPackage package, ReviewPackageSetupSelection options)
    {
        if (package is null) throw new ArgumentNullException(nameof(package));
        if (options is null) throw new ArgumentNullException(nameof(options));
        if (_document.IsModifiable)
            throw new InvalidOperationException("Area Plan provisioning owns its transaction and requires an unmodified document.");

        var existing = Resolve<ViewPlan>(package.AreaPlanUniqueId);
        if (IsMatchingAreaPlan(existing, package))
            return new AreaPlanProvisioningResult(package, false);

        var source = Require<ViewPlan>(package.SourceFloorPlanUniqueId, "來源樓層平面");
        var level = Require<Level>(package.LevelUniqueId, "樓層");
        var scheme = Require<AreaScheme>(package.AreaSchemeUniqueId, "面積配置");
        var warnings = new List<string>();

        using (var transaction = new Transaction(_document, "建立防火區劃 Area Plan"))
        {
            transaction.Start();
            try
            {
                var areaPlan = ViewPlan.CreateAreaPlan(_document, scheme.Id, level.Id);
                areaPlan.Name = MakeUniqueName(source.Name + " - 防火區劃");
                ApplyTemplate(areaPlan, options.AreaPlanTemplateUniqueId, warnings);
                if (options.CopyCropSettings) ApplyCrop(source, areaPlan, warnings);
                ApplyScopeBox(areaPlan, options.ScopeBoxUniqueId, warnings);

                var updated = AreaPlanProvisioning.Complete(package, areaPlan.UniqueId);
                new RevitReviewPackageRepository(_document).Save(updated);
                if (transaction.Commit() != TransactionStatus.Committed)
                    throw new InvalidOperationException("Revit 無法提交 Area Plan 建立交易。");
                return new AreaPlanProvisioningResult(updated, true, warnings);
            }
            catch
            {
                if (transaction.GetStatus() == TransactionStatus.Started) transaction.RollBack();
                throw;
            }
        }
    }

    private bool IsMatchingAreaPlan(ViewPlan? view, ReviewPackage package) =>
        view != null && !view.IsTemplate && view.ViewType == ViewType.AreaPlan &&
        view.GenLevel?.UniqueId == package.LevelUniqueId && view.AreaScheme?.UniqueId == package.AreaSchemeUniqueId;

    private void ApplyTemplate(ViewPlan target, string? uniqueId, ICollection<string> warnings)
    {
        if (string.IsNullOrWhiteSpace(uniqueId)) return;
        var template = Resolve<ViewPlan>(uniqueId);
        if (template == null || !template.IsTemplate || template.ViewType != ViewType.AreaPlan)
        { warnings.Add("指定的 Area Plan 樣板不存在或類型不符，已略過。"); return; }
        TryOptional(() => target.ViewTemplateId = template.Id, "Area Plan 樣板無法套用，已保留新視圖。", warnings);
    }

    private static void ApplyCrop(ViewPlan source, ViewPlan target, ICollection<string> warnings)
    {
        TryOptional(() =>
        {
            target.CropBox = source.CropBox;
            target.CropBoxActive = source.CropBoxActive;
            target.CropBoxVisible = source.CropBoxVisible;
        }, "裁切範圍受樣板控制或不可寫，已略過複製。", warnings);
    }

    private void ApplyScopeBox(ViewPlan target, string? uniqueId, ICollection<string> warnings)
    {
        if (string.IsNullOrWhiteSpace(uniqueId)) return;
        var scopeBox = _document.GetElement(uniqueId);
        if (scopeBox == null || scopeBox.Category?.Id.Value != (long)BuiltInCategory.OST_VolumeOfInterest)
        { warnings.Add("指定的 Scope Box 不存在，已略過。"); return; }
        TryOptional(() =>
        {
            var parameter = target.get_Parameter(BuiltInParameter.VIEWER_VOLUME_OF_INTEREST_CROP);
            if (parameter == null || parameter.IsReadOnly || !parameter.Set(scopeBox.Id))
                throw new InvalidOperationException();
        }, "Scope Box 受樣板控制或不可寫，已略過。", warnings);
    }

    private static void TryOptional(Action action, string warning, ICollection<string> warnings)
    { try { action(); } catch (Exception) { warnings.Add(warning); } }

    private T Require<T>(string uniqueId, string label) where T : Element =>
        Resolve<T>(uniqueId) ?? throw new InvalidOperationException(label + "不存在或類型不符，未建立 Area Plan。");

    private T? Resolve<T>(string? uniqueId) where T : Element =>
        string.IsNullOrWhiteSpace(uniqueId) ? null : _document.GetElement(uniqueId) as T;

    private string MakeUniqueName(string baseName)
    {
        var candidate = baseName;
        var suffix = 2;
        while (HasViewName(candidate)) candidate = baseName + " (" + suffix++ + ")";
        return candidate;
    }

    private bool HasViewName(string name)
    {
        foreach (View view in new FilteredElementCollector(_document).OfClass(typeof(View)))
            if (string.Equals(view.Name, name, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }
}
