using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace BuildingRegulationReview.Application.Diagnostics;

/// <summary>Which part of the review a log entry came from (spec 14: 階段).</summary>
public enum ReviewStage
{
    /// <summary>Reading walls, columns and auxiliary lines out of the model (spec 10.1).</summary>
    Extraction,

    /// <summary>De-duplication, splitting, snapping, extension and merging (spec 10.2).</summary>
    Repair,

    /// <summary>Closing loops into faces and zones (spec 10.4 of the geometry pipeline).</summary>
    Solving,

    /// <summary>What the user did in the Region Editor (spec 10.3).</summary>
    Editing,

    /// <summary>Building the difference list the user approves (spec 10.4).</summary>
    Preview,

    /// <summary>Writing boundaries, Areas, tags, colours and the 單線圖 (spec 10.5).</summary>
    WriteBack,

    /// <summary>Advancing or invalidating the package's own state (spec 13).</summary>
    Status,

    /// <summary>Running, storing, invalidating and overriding the review itself (spec 11).</summary>
    Review
}

/// <summary>How much the entry matters. Only <see cref="Error"/> stops a package advancing.</summary>
public enum ReviewSeverity
{
    Info,
    Warning,
    Error
}

/// <summary>
/// The error codes spec 14 requires every entry to carry, grouped by the important error types that
/// clause lists. A code is a stable identifier: it survives a reworded message, so a user can quote
/// it and a future rule can match on it.
/// </summary>
public static class ReviewErrorCode
{
    // 幾何未閉合、容差修復失敗、重疊或自交
    public const string GeometryNotClosed = "BCR-GEO-001";
    public const string GeometryRepairFailed = "BCR-GEO-002";
    public const string GeometryOverlapOrSelfIntersection = "BCR-GEO-003";
    public const string GeometryNothingToRead = "BCR-GEO-004";

    // 元素位於 Group、Design Option、Link 或不可編輯 Workset
    public const string ElementNotEditable = "BCR-ELEM-001";
    public const string ElementMissing = "BCR-ELEM-002";
    public const string ElementNotOwned = "BCR-ELEM-003";

    // 參數缺失、GUID 衝突、資料型態錯誤或唯讀
    public const string ParameterMissing = "BCR-PARAM-001";
    public const string ParameterGuidConflict = "BCR-PARAM-002";
    public const string ParameterTypeMismatch = "BCR-PARAM-003";
    public const string ParameterReadOnly = "BCR-PARAM-004";

    // 視圖／樣板不相容、名稱衝突或無法放置
    public const string ViewIncompatible = "BCR-VIEW-001";
    public const string ViewNameConflict = "BCR-VIEW-002";
    public const string ViewCannotPlace = "BCR-VIEW-003";

    // 規則缺失、條件衝突、運算錯誤或版本不一致
    public const string RuleMissing = "BCR-RULE-001";
    public const string RuleConflict = "BCR-RULE-002";
    public const string RuleComputationFailed = "BCR-RULE-003";
    public const string RuleVersionMismatch = "BCR-RULE-004";
    public const string RuleSchemaInvalid = "BCR-RULE-005";

    // 來源元素與區劃空間關係可解析（spec 11.1）
    public const string CandidateAmbiguous = "BCR-CAND-001";
    public const string CandidateZoneUnusable = "BCR-CAND-002";
    public const string CandidateFacadeInferred = "BCR-CAND-003";

    // 寫回本身
    public const string WriteBackRolledBack = "BCR-WB-001";
    public const string WriteBackElementFailed = "BCR-WB-002";
    public const string WriteBackElementSkipped = "BCR-WB-003";
    public const string WriteBackManualAction = "BCR-WB-004";
    public const string WriteBackCompleted = "BCR-WB-005";

    // 面積比對與狀態
    public const string AreaDisagrees = "BCR-AREA-001";
    public const string AreaNotEnclosed = "BCR-AREA-002";
    public const string StatusStale = "BCR-STALE-001";
    public const string StatusBlocked = "BCR-STALE-002";
    public const string StatusAdvanced = "BCR-STALE-003";

    // 構件防火時效（spec 11.5 第 5 點：複合構造無法判定）
    public const string FireRatingUndetermined = "BCR-RATE-001";

    // 帷幕牆區劃交接（docs/regulations/curtain-wall-fire-compartment.md §3.4）
    public const string CurtainWallNotPlanar = "BCR-CW-001";
    public const string CurtainWallJunctionUnresolved = "BCR-CW-002";
    public const string CurtainWallJunctionSplitByGridLine = "BCR-CW-003";
    public const string CurtainWallVerticalSpace = "BCR-CW-004";
    public const string CurtainWallFacadeOverlapsPanel = "BCR-CW-005";
    public const string CurtainWallFloorNotMeeting = "BCR-CW-006";
    public const string CurtainWallExposureUndecided = "BCR-CW-007";

    /// <summary>
    /// A warning the Revit 帷幕牆 geometry reader hands back about what it read (驗證清單 B-03). It is
    /// the reader speaking about the model, not a judgement of a 交接處, so it carries its own code
    /// rather than riding on <see cref="ReviewCompleted"/>.
    /// </summary>
    public const string CurtainWallReaderWarning = "BCR-CW-008";

    // 人工覆寫（spec 11.8）
    public const string OverrideRejected = "BCR-OVR-001";
    public const string OverrideNeedsReconfirmation = "BCR-OVR-002";
    public const string ReviewRunUnreadable = "BCR-RUN-001";

    // 檢討視圖標示（spec 11.4 第 4 點、11.5 第 6 點、11.6 第 4 點）
    public const string ReviewMarkRefused = "BCR-MARK-001";
    public const string ReviewMarkSkipped = "BCR-MARK-002";
    public const string ReviewMarkUserChangeKept = "BCR-MARK-003";

    // 前置檢查、執行、取消與效能（spec 11.1、13.2、15）
    public const string ReviewNotReady = "BCR-PRE-001";
    public const string ReviewReady = "BCR-PRE-002";
    public const string EnvironmentLimited = "BCR-ENV-001";
    public const string ReviewCancelled = "BCR-RUN-002";
    public const string ReviewCompleted = "BCR-RUN-003";
    public const string ReviewSaveRolledBack = "BCR-RUN-004";
    public const string PerformanceExceeded = "BCR-PERF-001";

    private static readonly IReadOnlyDictionary<string, string> Descriptions =
        new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { GeometryNotClosed, "幾何未閉合" },
            { GeometryRepairFailed, "容差修復失敗" },
            { GeometryOverlapOrSelfIntersection, "重疊或自交" },
            { GeometryNothingToRead, "範圍內沒有可用的幾何" },
            { ElementNotEditable, "元素位於 Group、Design Option、連結模型或不可編輯的 Workset" },
            { ElementMissing, "元素已不在模型中" },
            { ElementNotOwned, "元素沒有本套件的擁有權標記" },
            { ParameterMissing, "參數缺失" },
            { ParameterGuidConflict, "參數 GUID 衝突" },
            { ParameterTypeMismatch, "參數資料型態錯誤" },
            { ParameterReadOnly, "參數為唯讀" },
            { ViewIncompatible, "視圖或樣板不相容" },
            { ViewNameConflict, "視圖名稱衝突" },
            { ViewCannotPlace, "無法在視圖中放置" },
            { RuleMissing, "規則缺失" },
            { RuleConflict, "規則條件衝突" },
            { RuleComputationFailed, "規則運算錯誤" },
            { RuleVersionMismatch, "規則版本不一致" },
            { RuleSchemaInvalid, "規則格式不符合 schema" },
            { CandidateAmbiguous, "元素與區劃的空間關係無法判定" },
            { CandidateZoneUnusable, "區劃範圍無法用於檢討" },
            { CandidateFacadeInferred, "帷幕牆外側無區劃，推定為外牆" },
            { WriteBackRolledBack, "寫回整批復原" },
            { WriteBackElementFailed, "元素寫入失敗" },
            { WriteBackElementSkipped, "元素略過未寫入" },
            { WriteBackManualAction, "需人工處理" },
            { WriteBackCompleted, "寫回完成" },
            { AreaDisagrees, "草算面積與 Revit 面積不符" },
            { AreaNotEnclosed, "面積未落在封閉邊界內" },
            { StatusStale, "結果已失效" },
            { StatusBlocked, "尚不得進入 Ready" },
            { StatusAdvanced, "套件狀態已更新" },
            { FireRatingUndetermined, "複合構造無法判定防火時效" },
            { CurtainWallNotPlanar, "帷幕牆非平面，超出 MVP 範圍" },
            { CurtainWallJunctionUnresolved, "帷幕牆與區劃的交接處無法唯一解析" },
            { CurtainWallJunctionSplitByGridLine, "交接帶被 grid line 分割" },
            { CurtainWallVerticalSpace, "連跨複數樓層之帷幕牆，改依第79條之2檢討" },
            { CurtainWallFacadeOverlapsPanel, "外牆與帷幕嵌板重疊" },
            { CurtainWallFloorNotMeeting, "帷幕牆穿過本層標高而無區劃樓地板與其交接" },
            { CurtainWallExposureUndecided, "無法判定帷幕牆為建築物外牆或室內帷幕牆" },
            { CurtainWallReaderWarning, "帷幕牆幾何讀取警告" },
            { OverrideRejected, "人工覆寫不成立" },
            { OverrideNeedsReconfirmation, "人工覆寫需重新確認" },
            { ReviewRunUnreadable, "檢討紀錄無法讀取" },
            { ReviewMarkRefused, "檢討視圖無法標示" },
            { ReviewMarkSkipped, "未符合項目未標示" },
            { ReviewMarkUserChangeKept, "保留使用者修改的元素顯示" },
            { ReviewNotReady, "前置檢查未通過" },
            { ReviewReady, "前置檢查" },
            { EnvironmentLimited, "模型條件超出 MVP 範圍" },
            { ReviewCancelled, "檢討已取消" },
            { ReviewCompleted, "檢討完成" },
            { ReviewSaveRolledBack, "檢討結果寫入已整批復原" },
            { PerformanceExceeded, "超出效能目標" }
        });

    /// <summary>Every code this tool can emit, for the documentation and for the tests that pin it.</summary>
    public static IReadOnlyCollection<string> All =>
        new ReadOnlyCollection<string>(Descriptions.Keys.ToList());

    /// <summary>The short Chinese name of a code, or the code itself when it is not one of ours.</summary>
    public static string Describe(string? code) =>
        code is not null && Descriptions.TryGetValue(code, out var text) ? text : (code ?? string.Empty);

    public static bool IsKnown(string? code) => code is not null && Descriptions.ContainsKey(code);
}
