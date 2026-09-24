using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using BuildingRegulationReview.Application.Candidates;
using BuildingRegulationReview.Application.Checks;
using BuildingRegulationReview.Application.Diagnostics;
using BuildingRegulationReview.Application.ReviewPackages;
using BuildingRegulationReview.Domain.Common;
using BuildingRegulationReview.Domain.ReviewPackages;
using BuildingRegulationReview.Domain.Rules;

namespace BuildingRegulationReview.Application.Reviews;

public enum ReadinessSeverity
{
    Info,
    Warning,

    /// <summary>A 關鍵條件 of spec 11.1: the review does not start while one of these stands.</summary>
    Blocking
}

/// <summary>Which spec 11.1 condition an item is about.</summary>
public enum ReadinessCondition
{
    Package,
    RuleSet,
    ProjectSettings,
    Parameters,
    SpatialRelations
}

/// <summary>One finding of the pre-review check, with how to fix it (spec 11.1「列出修正方式」).</summary>
public sealed class ReadinessItem
{
    internal ReadinessItem(ReadinessCondition condition, ReadinessSeverity severity, string code, string message, string? fix, string? detail)
    {
        Condition = condition;
        Severity = severity;
        Code = code;
        Message = message;
        Fix = fix;
        Detail = detail;
    }

    public ReadinessCondition Condition { get; }
    public ReadinessSeverity Severity { get; }
    public string Code { get; }
    public string Message { get; }
    public string? Fix { get; }
    public string? Detail { get; }

    public string ConditionText => ReviewReadiness.Label(Condition);

    public override string ToString() => Fix is null ? $"[{ConditionText}] {Message}" : $"[{ConditionText}] {Message} → {Fix}";
}

/// <summary>What the adapter observed about the project settings spec 11.1 names.</summary>
public sealed class ReviewModelConditions
{
    public static readonly ReviewModelConditions Unknown = new();

    public ReviewModelConditions(
        string? phaseName = null,
        bool hasDesignOptions = false,
        int linkedModelCount = 0,
        string? lengthUnit = null,
        string? areaUnit = null)
    {
        if (linkedModelCount < 0) throw new ArgumentOutOfRangeException(nameof(linkedModelCount));
        PhaseName = Clean(phaseName);
        HasDesignOptions = hasDesignOptions;
        LinkedModelCount = linkedModelCount;
        LengthUnit = Clean(lengthUnit);
        AreaUnit = Clean(areaUnit);
    }

    /// <summary>The phase of the source floor plan.</summary>
    public string? PhaseName { get; }

    public bool HasDesignOptions { get; }
    public int LinkedModelCount { get; }
    public string? LengthUnit { get; }
    public string? AreaUnit { get; }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value!.Trim();
}

/// <summary>Everything the pre-review check looks at, read once by the adapter.</summary>
public sealed class ReviewReadinessInput
{
    public ReviewReadinessInput(
        ReviewPackage package,
        IEnumerable<string>? boundaryStaleReasons,
        Result<CompiledRuleSet> ruleSet,
        ReviewModelConditions? conditions,
        ReviewParameterSnapshot? parameters,
        Result<CandidateSet>? candidates,
        bool acceptRuleSetUpdate = false)
    {
        Package = package ?? throw new ArgumentNullException(nameof(package));
        BoundaryStaleReasons = new ReadOnlyCollection<string>((boundaryStaleReasons ?? Array.Empty<string>())
            .Where(x => !string.IsNullOrWhiteSpace(x)).ToList());
        RuleSet = ruleSet ?? throw new ArgumentNullException(nameof(ruleSet));
        Conditions = conditions ?? ReviewModelConditions.Unknown;
        Parameters = parameters ?? ReviewParameterSnapshot.Empty;
        Candidates = candidates;
        AcceptRuleSetUpdate = acceptRuleSetUpdate;
    }

    public ReviewPackage Package { get; }

    /// <summary>The reasons <see cref="ReviewStaleness"/> found against the package's boundaries and Areas.</summary>
    public IReadOnlyList<string> BoundaryStaleReasons { get; }

    /// <summary>The rule set the add-in offers now, or why it could not be loaded.</summary>
    public Result<CompiledRuleSet> RuleSet { get; }

    public ReviewModelConditions Conditions { get; }
    public ReviewParameterSnapshot Parameters { get; }

    /// <summary>The resolved candidates, or why they could not be read; null when not read yet.</summary>
    public Result<CandidateSet>? Candidates { get; }

    /// <summary>The user confirmed moving the package to the rule set version the add-in offers now.</summary>
    public bool AcceptRuleSetUpdate { get; }
}

/// <summary>The outcome of the pre-review check.</summary>
public sealed class ReviewReadinessReport
{
    internal ReviewReadinessReport(Guid packageId, IEnumerable<ReadinessItem> items, CompiledRuleSet? ruleSet, bool ruleSetChanges)
    {
        PackageId = packageId;
        Items = new ReadOnlyCollection<ReadinessItem>(items
            .OrderByDescending(x => x.Severity).ThenBy(x => x.Condition).ToList());
        RuleSet = ruleSet;
        RuleSetChanges = ruleSetChanges;
    }

    public Guid PackageId { get; }
    public IReadOnlyList<ReadinessItem> Items { get; }

    /// <summary>The rule set the review will lock and run on; null when it could not be loaded.</summary>
    public CompiledRuleSet? RuleSet { get; }

    /// <summary>The run will lock a rule set or version other than the one the package has.</summary>
    public bool RuleSetChanges { get; }

    public IEnumerable<ReadinessItem> Blocking => Items.Where(x => x.Severity == ReadinessSeverity.Blocking);
    public IEnumerable<ReadinessItem> Warnings => Items.Where(x => x.Severity == ReadinessSeverity.Warning);

    public bool CanRun => RuleSet is not null && !Blocking.Any();

    /// <summary>A version mismatch is the one blocker the user can lift from the review window.</summary>
    public bool NeedsRuleSetConfirmation => Items.Any(x =>
        x.Severity == ReadinessSeverity.Blocking && x.Code == ReviewErrorCode.RuleVersionMismatch);

    public string Message => CanRun
        ? Warnings.Any()
            ? $"前置檢查通過，有 {Warnings.Count()} 項提醒。"
            : "前置檢查通過，可以開始檢討。"
        : $"前置檢查有 {Blocking.Count()} 項關鍵條件不成立，已停止檢討；請依修正方式處理後重新檢查。";

    /// <summary>The report as log entries (spec 14).</summary>
    public ReviewLog ToLog(DateTime? timestampUtc = null)
    {
        var log = new ReviewLog.Builder(PackageId, timestampUtc);
        foreach (var item in Items)
        {
            log.Add(item.Code, ReviewStage.Review,
                item.Severity switch
                {
                    ReadinessSeverity.Blocking => ReviewSeverity.Error,
                    ReadinessSeverity.Warning => ReviewSeverity.Warning,
                    _ => ReviewSeverity.Info
                },
                $"前置檢查（{item.ConditionText}）：{item.Message}",
                technicalDetail: item.Detail,
                suggestion: item.Fix);
        }

        log.Add(CanRun ? ReviewErrorCode.ReviewReady : ReviewErrorCode.ReviewNotReady, ReviewStage.Review,
            CanRun ? ReviewSeverity.Info : ReviewSeverity.Error, Message);
        return log.Build();
    }
}

/// <summary>
/// Spec 11.1 前置檢查: 工作包為 Ready 且 Area／Boundary 未過期；規則集存在且版本已鎖定；Area Scheme、
/// 專案單位、Phase、Design Option、Link 狀態符合設定；必要參數存在且可讀；來源元素與區劃空間關係可
/// 解析. Any key condition that fails stops the review, and every item says how to fix it.
/// </summary>
public static class ReviewReadiness
{
    public static ReviewReadinessReport Evaluate(ReviewReadinessInput input)
    {
        if (input is null) throw new ArgumentNullException(nameof(input));

        var items = new List<ReadinessItem>();
        Package(input, items);
        var changes = RuleSet(input, items);
        var ruleSet = input.RuleSet.IsSuccess ? input.RuleSet.Value : null;
        Settings(input.Conditions, items);
        if (ruleSet is not null) Parameters(input, ruleSet, items);
        Spatial(input, items);

        return new ReviewReadinessReport(input.Package.PackageId, items, ruleSet, changes);
    }

    public static string Label(ReadinessCondition condition) => condition switch
    {
        ReadinessCondition.Package => "工作包",
        ReadinessCondition.RuleSet => "規則集",
        ReadinessCondition.ProjectSettings => "專案設定",
        ReadinessCondition.Parameters => "必要參數",
        ReadinessCondition.SpatialRelations => "空間關係",
        _ => condition.ToString()
    };

    private static void Package(ReviewReadinessInput input, List<ReadinessItem> items)
    {
        var package = input.Package;
        var status = ReviewPackageProgress.Describe(package.Status);

        if (package.AreaPlanUniqueId is null)
        {
            items.Add(Block(ReadinessCondition.Package, ReviewErrorCode.ReviewNotReady,
                "這個工作包還沒有 Area Plan。", "請先執行「防火區劃設定」建立 Area Plan。"));
            return;
        }

        switch (package.Status)
        {
            case ReviewPackageStatus.Setup:
            case ReviewPackageStatus.BoundaryDraft:
                items.Add(Block(ReadinessCondition.Package, ReviewErrorCode.ReviewNotReady,
                    $"工作包狀態為「{status}」，區劃範圍尚未完成寫入。",
                    "請在「防火區劃編輯器」套用區劃，直到工作包進入「可開始檢討」。"));
                break;
            case ReviewPackageStatus.Error:
                items.Add(Block(ReadinessCondition.Package, ReviewErrorCode.ReviewNotReady,
                    $"工作包狀態為「{status}」。", "請重新執行「防火區劃設定」後再套用區劃。"));
                break;
            case ReviewPackageStatus.Stale when input.BoundaryStaleReasons.Count == 0:
                items.Add(Info(ReadinessCondition.Package, ReviewErrorCode.StatusStale,
                    "上一次的檢討結果已失效，這次檢討會以模型現況重新判定。"));
                break;
        }

        foreach (var reason in input.BoundaryStaleReasons)
        {
            items.Add(Block(ReadinessCondition.Package, ReviewErrorCode.StatusStale,
                "區劃邊界或面積已過期：" + reason.TrimEnd('。') + "。",
                "請重新開啟「防火區劃編輯器」確認區劃並重新套用。"));
        }
    }

    private static bool RuleSet(ReviewReadinessInput input, List<ReadinessItem> items)
    {
        if (input.RuleSet.IsFailure)
        {
            items.Add(Block(ReadinessCondition.RuleSet, Code(input.RuleSet.Error, ReviewErrorCode.RuleMissing),
                "規則集無法載入：" + input.RuleSet.Error.Message,
                "請確認外掛的規則檔存在且格式正確，或洽規則維護者。",
                input.RuleSet.Error.TechnicalDetail));
            return false;
        }

        var offered = input.RuleSet.Value.RuleSet;
        var package = input.Package;
        if (package.RuleSetId is null || package.RuleSetVersion is null)
        {
            items.Add(Info(ReadinessCondition.RuleSet, ReviewErrorCode.ReviewReady,
                $"首次檢討，將鎖定規則集「{offered.RuleSetId}」版本 {offered.Version}。"));
        }
        else if (!string.Equals(package.RuleSetId, offered.RuleSetId, StringComparison.Ordinal) ||
                 !string.Equals(package.RuleSetVersion, offered.Version, StringComparison.Ordinal))
        {
            var message = $"工作包鎖定的是規則集「{package.RuleSetId}」版本 {package.RuleSetVersion}，" +
                          $"外掛目前提供「{offered.RuleSetId}」版本 {offered.Version}。";
            items.Add(input.AcceptRuleSetUpdate
                ? new ReadinessItem(ReadinessCondition.RuleSet, ReadinessSeverity.Warning, ReviewErrorCode.RuleVersionMismatch,
                    message + "已確認改用新版本，舊的檢討結果將標示為需更新。", null, null)
                : Block(ReadinessCondition.RuleSet, ReviewErrorCode.RuleVersionMismatch, message,
                    "確認要改用新版本後勾選「改用目前規則版本」再執行；舊的檢討結果與人工覆寫會標示為需更新。"));
        }

        foreach (var category in new[]
                 {
                     RuleCategory.CompartmentArea,
                     RuleCategory.FireResistance,
                     RuleCategory.OpeningProtection,
                     RuleCategory.CompartmentContinuity
                 })
        {
            if (input.RuleSet.Value.OfCategory(category).Any()) continue;
            items.Add(new ReadinessItem(ReadinessCondition.RuleSet, ReadinessSeverity.Warning, ReviewErrorCode.RuleMissing,
                $"規則集沒有「{CategoryLabel(category)}」規則，該項結果都會是人工覆核。", "請洽規則維護者補上規則。", null));
        }

        return package.RuleSetId is null ||
               !string.Equals(package.RuleSetId, offered.RuleSetId, StringComparison.Ordinal) ||
               !string.Equals(package.RuleSetVersion, offered.Version, StringComparison.Ordinal);
    }

    private static void Settings(ReviewModelConditions conditions, List<ReadinessItem> items)
    {
        if (conditions.HasDesignOptions)
        {
            items.Add(new ReadinessItem(ReadinessCondition.ProjectSettings, ReadinessSeverity.Warning, ReviewErrorCode.EnvironmentLimited,
                "專案有 Design Option，只會檢討主要設計選項中的元素。",
                "若要檢討其他選項，請先把該選項設為主要選項（多選項檢討屬 V1.1）。", null));
        }

        if (conditions.LinkedModelCount > 0)
        {
            items.Add(new ReadinessItem(ReadinessCondition.ProjectSettings, ReadinessSeverity.Warning, ReviewErrorCode.EnvironmentLimited,
                $"專案有 {conditions.LinkedModelCount} 個連結模型，連結模型中的元素不會被讀取與檢討。",
                "需要檢討的構件與門窗請放在主模型中（連結模型檢討屬 V1.1）。", null));
        }

        var facts = new List<string>();
        if (conditions.PhaseName is not null) facts.Add($"Phase「{conditions.PhaseName}」");
        if (conditions.LengthUnit is not null) facts.Add($"長度單位 {conditions.LengthUnit}");
        if (conditions.AreaUnit is not null) facts.Add($"面積單位 {conditions.AreaUnit}");
        if (facts.Count > 0)
        {
            items.Add(Info(ReadinessCondition.ProjectSettings, ReviewErrorCode.ReviewReady,
                $"依來源平面圖的 {string.Join("、", facts)} 檢討；數值一律換算為公尺、平方公尺與分鐘。"));
        }
    }

    private static void Parameters(ReviewReadinessInput input, CompiledRuleSet ruleSet, List<ReadinessItem> items)
    {
        var candidates = input.Candidates is { IsSuccess: true } ? input.Candidates.Value : null;
        foreach (var source in ReviewInputSources.NeededBy(ruleSet))
        {
            var bound = input.Parameters.HostsOf(source.ParameterName).Where(source.Hosts.Contains).ToList();
            var where = string.Join("、", source.Hosts.Select(ReviewInputSources.Label));
            if (bound.Count == 0)
            {
                items.Add(Block(ReadinessCondition.Parameters, ReviewErrorCode.ParameterMissing,
                    $"規則需要「{source.Label}」（{source.Field}），但專案沒有參數 {source.ParameterName}。",
                    $"請在「管理 > 專案參數」加入 {source.ParameterName}（{ReviewInputSources.Label(source.Level)}），類別：{where}。"));
                continue;
            }

            if (candidates is null) continue;
            var used = UsedHosts(source, candidates).Where(h => !bound.Contains(h)).ToList();
            if (used.Count == 0) continue;
            items.Add(new ReadinessItem(ReadinessCondition.Parameters, ReadinessSeverity.Warning, ReviewErrorCode.ParameterMissing,
                $"參數 {source.ParameterName} 沒有綁定到 {string.Join("、", used.Select(ReviewInputSources.Label))}，這些元素的「{source.Label}」會是資料不足。",
                $"請把 {source.ParameterName} 的類別加上 {string.Join("、", used.Select(ReviewInputSources.Label))}。", null));
        }
    }

    private static IEnumerable<ReviewParameterHost> UsedHosts(ReviewInputSource source, CandidateSet candidates)
    {
        // Only the hosts the parameter is actually meant for: 梁 are candidates but carry no
        // 設計防火時效 parameter, so an unbound 結構構架 is not a gap to report.
        if (source.Hosts.SequenceEqual(ReviewInputSources.MemberHosts))
            return candidates.Members.Select(m => ReviewInputSources.HostOf(m.Observation.Category))
                .Where(source.Hosts.Contains).Distinct().OrderBy(x => x);
        if (source.Hosts.SequenceEqual(ReviewInputSources.OpeningHosts))
            return candidates.Openings.Select(o => ReviewInputSources.HostOf(o.Observation.Category))
                .Where(source.Hosts.Contains).Distinct().OrderBy(x => x);
        return Array.Empty<ReviewParameterHost>();
    }

    private static void Spatial(ReviewReadinessInput input, List<ReadinessItem> items)
    {
        if (input.Candidates is null) return;
        if (input.Candidates.IsFailure)
        {
            items.Add(Block(ReadinessCondition.SpatialRelations, Code(input.Candidates.Error, ReviewErrorCode.CandidateZoneUnusable),
                input.Candidates.Error.Message, "請確認 Area Plan 與區劃仍在模型中，必要時重新執行「防火區劃設定」。",
                input.Candidates.Error.TechnicalDetail));
            return;
        }

        var set = input.Candidates.Value;
        var zones = ReviewPreconditions.Zones(set, "防火區劃");
        if (zones.IsFailure)
        {
            items.Add(Block(ReadinessCondition.SpatialRelations, zones.Error.Code, zones.Error.Message,
                "請回到「防火區劃編輯器」修正邊界後重新套用。", zones.Error.TechnicalDetail));
            return;
        }

        var troubled = set.Zones.Where(z => !z.IsClear).ToList();
        if (troubled.Count > 0)
        {
            items.Add(new ReadinessItem(ReadinessCondition.SpatialRelations, ReadinessSeverity.Warning, ReviewErrorCode.CandidateZoneUnusable,
                $"區劃 {string.Join("、", troubled.Select(z => $"「{z.Name}」"))} 有部分未封閉或與其他區劃重疊，這些區劃的結果會是人工覆核。",
                "請回到「防火區劃編輯器」修正後重新套用。", null));
        }

        if (set.Ambiguities.Count > 0)
        {
            items.Add(Info(ReadinessCondition.SpatialRelations, ReviewErrorCode.CandidateAmbiguous,
                $"有 {set.Ambiguities.Count} 個元素與區劃的空間關係無法自動判定（幕牆、非 Hosted、偏離中心線等），會列為人工覆核。"));
        }

        foreach (var warning in set.Warnings)
            items.Add(Info(ReadinessCondition.SpatialRelations, ReviewErrorCode.ReviewReady, warning));
    }

    private static string CategoryLabel(RuleCategory category) => category switch
    {
        RuleCategory.CompartmentArea => "防火區劃面積",
        RuleCategory.FireResistance => "構件防火時效",
        RuleCategory.OpeningProtection => "防火門窗",
        RuleCategory.CompartmentContinuity => "帷幕牆區劃交接",
        _ => category.ToString()
    };

    private static string Code(Error error, string fallback) =>
        ReviewErrorCode.IsKnown(error.Code) ? error.Code : fallback;

    private static ReadinessItem Block(ReadinessCondition condition, string code, string message, string fix, string? detail = null) =>
        new(condition, ReadinessSeverity.Blocking, code, message, fix, detail);

    private static ReadinessItem Info(ReadinessCondition condition, string code, string message) =>
        new(condition, ReadinessSeverity.Info, code, message, null, null);
}
