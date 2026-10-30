using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using BuildingRegulationReview.Application.Reviews;
using BuildingRegulationReview.Domain.ProjectSetup;

namespace BuildingRegulationReview.Application.ProjectSetup;

/// <summary>專案現在就有的一個同名專案參數，以及它綁成什麼樣子。</summary>
/// <remarks>
/// <see cref="Guid"/> 為 null 表示專案有這個名字的參數，但它不是共用參數（或不是由共用參數檔加進來
/// 的），所以沒有 GUID 可以比對；<see cref="ValueType"/> 為 null 表示它的資料型別不是外掛認得的那幾種。
/// 兩種都算衝突，不是「可以直接沿用」。
/// </remarks>
public sealed class FireReviewParameterBindingObservation
{
    public FireReviewParameterBindingObservation(
        string name,
        Guid? guid,
        SharedParameterValueType? valueType,
        ReviewParameterLevel level,
        IEnumerable<ReviewParameterHost> hosts)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("參數名稱不可為空。", nameof(name));
        if (hosts is null) throw new ArgumentNullException(nameof(hosts));

        Name = name;
        Guid = guid;
        ValueType = valueType;
        Level = level;
        Hosts = new ReadOnlyCollection<ReviewParameterHost>(hosts.Distinct().OrderBy(x => (int)x).ToList());
    }

    public string Name { get; }
    public Guid? Guid { get; }
    public SharedParameterValueType? ValueType { get; }
    public ReviewParameterLevel Level { get; }

    /// <summary>已綁到的類別（只含外掛認得的那些；多綁的其他類別無害，不在這裡）。</summary>
    public IReadOnlyList<ReviewParameterHost> Hosts { get; }
}

/// <summary>專案現況的來源。Revit 端讀 <c>Document.ParameterBindings</c>，不開交易。</summary>
public interface IFireReviewParameterBindingReader
{
    IReadOnlyList<FireReviewParameterBindingObservation> Read();
}

public enum FireReviewParameterActionKind
{
    /// <summary>定義相符、類別齊全，不必動。</summary>
    AlreadyBound,

    /// <summary>專案完全沒有這個參數，要從共用參數檔加入並綁定。</summary>
    Create,

    /// <summary>參數在、定義也對，但少綁了某些類別，補綁即可，既有值不受影響。</summary>
    AddCategories,

    /// <summary>同名但 GUID、資料型別或實體／類型不符。不覆蓋，只顯示修正指引（spec 8.2）。</summary>
    Conflict
}

/// <summary>一個參數在這個專案裡要做的事。</summary>
public sealed class FireReviewParameterAction
{
    internal FireReviewParameterAction(
        FireReviewParameterDefinition definition,
        FireReviewParameterActionKind kind,
        IEnumerable<ReviewParameterHost> boundHosts,
        IEnumerable<ReviewParameterHost> missingHosts,
        string? problem = null,
        string? fix = null)
    {
        Definition = definition;
        Kind = kind;
        BoundHosts = new ReadOnlyCollection<ReviewParameterHost>(boundHosts.Distinct().OrderBy(x => (int)x).ToList());
        MissingHosts = new ReadOnlyCollection<ReviewParameterHost>(missingHosts.Distinct().OrderBy(x => (int)x).ToList());
        Problem = problem;
        Fix = fix;
    }

    public FireReviewParameterDefinition Definition { get; }
    public FireReviewParameterActionKind Kind { get; }

    /// <summary>外掛需要的類別中已經綁好的那些。</summary>
    public IReadOnlyList<ReviewParameterHost> BoundHosts { get; }

    /// <summary>還要補綁的類別。</summary>
    public IReadOnlyList<ReviewParameterHost> MissingHosts { get; }

    /// <summary>衝突的具體內容，其餘情況為 null。</summary>
    public string? Problem { get; }

    /// <summary>衝突要怎麼處理，其餘情況為 null。</summary>
    public string? Fix { get; }

    /// <summary>會被這次動作改動到的專案，就是要建立或要補綁的那些。</summary>
    public bool IsWork => Kind == FireReviewParameterActionKind.Create || Kind == FireReviewParameterActionKind.AddCategories;

    public string StatusText => Kind switch
    {
        FireReviewParameterActionKind.AlreadyBound => "已綁定",
        FireReviewParameterActionKind.Create => "將建立",
        FireReviewParameterActionKind.AddCategories => "將補綁類別",
        _ => "衝突，需人工處理"
    };

    /// <summary>差異預覽那一欄的說明：衝突講問題與修正方式，其餘講這次會做什麼與參數的用途。</summary>
    public string DetailText => Kind switch
    {
        FireReviewParameterActionKind.Conflict => Problem + "　修正方式：" + Fix,
        FireReviewParameterActionKind.AddCategories =>
            $"已綁 {Text(BoundHosts)}，要補綁 {Text(MissingHosts)}。既有的值不會被改動。",
        FireReviewParameterActionKind.Create => Definition.Purpose,
        _ => Definition.Purpose
    };

    private static string Text(IReadOnlyList<ReviewParameterHost> hosts) =>
        hosts.Count == 0 ? "（無）" : string.Join("、", hosts.Select(ReviewInputSources.Label));
}

/// <summary>差異預覽的結果：每個參數一列，加上要不要按下建立的判斷（spec 8.2）。</summary>
public sealed class FireReviewParameterSetupPlan
{
    internal FireReviewParameterSetupPlan(IReadOnlyList<FireReviewParameterAction> actions) => Actions = actions;

    public IReadOnlyList<FireReviewParameterAction> Actions { get; }

    public IReadOnlyList<FireReviewParameterAction> ToCreate =>
        Actions.Where(x => x.Kind == FireReviewParameterActionKind.Create).ToList();

    public IReadOnlyList<FireReviewParameterAction> ToExtend =>
        Actions.Where(x => x.Kind == FireReviewParameterActionKind.AddCategories).ToList();

    public IReadOnlyList<FireReviewParameterAction> Conflicts =>
        Actions.Where(x => x.Kind == FireReviewParameterActionKind.Conflict).ToList();

    public IReadOnlyList<FireReviewParameterAction> AlreadyBound =>
        Actions.Where(x => x.Kind == FireReviewParameterActionKind.AlreadyBound).ToList();

    /// <summary>有東西可以建或可以補綁。衝突不算——那要使用者自己先處理掉。</summary>
    public bool HasWork => Actions.Any(x => x.IsWork);

    public string Summary
    {
        get
        {
            var parts = new List<string>();
            if (ToCreate.Count > 0) parts.Add($"將建立 {ToCreate.Count} 個參數");
            if (ToExtend.Count > 0) parts.Add($"將補綁 {ToExtend.Count} 個參數的類別");
            if (AlreadyBound.Count > 0) parts.Add($"{AlreadyBound.Count} 個已就緒");
            if (Conflicts.Count > 0) parts.Add($"{Conflicts.Count} 個衝突需人工處理");
            return parts.Count == 0 ? "沒有要處理的參數。" : string.Join("；", parts) + "。";
        }
    }
}

/// <summary>
/// 把 <see cref="FireReviewParameterCatalog"/> 與專案現況比出差異。spec 8.2：先產生差異預覽，經確認
/// 後才建立或補綁；既有同名但 GUID 或型別不符時禁止直接覆蓋，顯示修正指引。
/// </summary>
/// <remarks>
/// 「禁止直接覆蓋」不是保守而已：Revit 不允許兩個同名專案參數並存，換掉一個等於先移除舊的，舊的
/// 值會跟著消失（主檔頭註記的 …0009 → …000d 那次就是這樣）。哪些值該搬、要不要搬，只有使用者知道。
/// </remarks>
public static class FireReviewParameterSetupPlanner
{
    public static FireReviewParameterSetupPlan Inspect(IFireReviewParameterBindingReader reader)
    {
        if (reader is null) throw new ArgumentNullException(nameof(reader));
        return Inspect(reader.Read());
    }

    public static FireReviewParameterSetupPlan Inspect(IEnumerable<FireReviewParameterBindingObservation> observed)
    {
        if (observed is null) throw new ArgumentNullException(nameof(observed));

        var byName = observed
            .GroupBy(x => x.Name, StringComparer.Ordinal)
            .ToDictionary(x => x.Key, x => x.ToList(), StringComparer.Ordinal);

        var actions = FireReviewParameterCatalog.All
            .Select(definition => Action(definition, byName.TryGetValue(definition.Name, out var found) ? found : null))
            .ToList();

        return new FireReviewParameterSetupPlan(new ReadOnlyCollection<FireReviewParameterAction>(actions));
    }

    private static FireReviewParameterAction Action(
        FireReviewParameterDefinition definition,
        IReadOnlyList<FireReviewParameterBindingObservation>? found)
    {
        if (found is null || found.Count == 0)
        {
            return new FireReviewParameterAction(definition, FireReviewParameterActionKind.Create,
                Array.Empty<ReviewParameterHost>(), definition.Hosts);
        }

        // 同名多筆：挑一筆來判定就等於把另一筆藏起來，而挑到哪一筆取決於 Revit 的列舉順序。哪一筆該留
        // 只有使用者知道，所以整筆交給他，連相符的那一筆也不動。
        if (found.Count > 1)
        {
            return new FireReviewParameterAction(definition, FireReviewParameterActionKind.Conflict,
                Array.Empty<ReviewParameterHost>(), definition.Hosts,
                $"專案裡有 {found.Count} 筆叫 {definition.Name} 的專案參數，無法判斷該沿用哪一筆。",
                $"請在「管理 > 專案參數」把多餘的 {definition.Name} 移除，只留一筆，再按一次一鍵建立。" +
                "移除會讓原本填在那一筆上的值消失，需要的話請先記下來或匯出明細表。");
        }

        var actual = found[0];
        var conflict = Conflict(definition, actual);
        if (conflict is not null) return conflict;

        var missing = definition.Hosts.Where(x => !actual.Hosts.Contains(x)).ToList();
        var bound = definition.Hosts.Where(actual.Hosts.Contains).ToList();

        return missing.Count == 0
            ? new FireReviewParameterAction(definition, FireReviewParameterActionKind.AlreadyBound, bound, missing)
            : new FireReviewParameterAction(definition, FireReviewParameterActionKind.AddCategories, bound, missing);
    }

    private static FireReviewParameterAction? Conflict(
        FireReviewParameterDefinition definition,
        FireReviewParameterBindingObservation actual)
    {
        if (actual.Guid is null)
        {
            return Conflict(definition, actual,
                $"專案已經有一個叫 {definition.Name} 的專案參數，但它不是共用參數，沒有 GUID 可以與外掛的定義對應。",
                Remove(definition));
        }

        if (actual.Guid.Value != definition.Guid)
        {
            return Conflict(definition, actual,
                $"專案的 {definition.Name} 是另一個 GUID（{actual.Guid.Value:D}），外掛的定義是 {definition.Guid:D}。" +
                "GUID 是參數與定義的繫結鍵，換模型或換電腦時會對不起來。",
                Remove(definition));
        }

        if (actual.ValueType != definition.ValueType)
        {
            var actualText = actual.ValueType is null
                ? "外掛不認得的資料型別"
                : FireReviewParameterCatalog.Label(actual.ValueType.Value);
            return Conflict(definition, actual,
                $"專案的 {definition.Name} 是{actualText}，外掛的定義是{definition.ValueTypeText}。",
                Remove(definition));
        }

        if (actual.Level != definition.Level)
        {
            return Conflict(definition, actual,
                $"專案的 {definition.Name} 綁在{ReviewInputSources.Label(actual.Level)}，外掛需要{definition.LevelText}。",
                Remove(definition));
        }

        return null;
    }

    private static FireReviewParameterAction Conflict(
        FireReviewParameterDefinition definition,
        FireReviewParameterBindingObservation actual,
        string problem,
        string fix) =>
        new FireReviewParameterAction(definition, FireReviewParameterActionKind.Conflict,
            actual.Hosts.Where(definition.Hosts.Contains), definition.Hosts.Where(x => !actual.Hosts.Contains(x)),
            problem, fix);

    /// <summary>
    /// 唯一安全的處理方式。Revit 不允許同名的兩個專案參數並存，所以必須先移除，而移除會讓已經填在
    /// 那個參數上的值消失，該不該搬只有使用者知道——外掛不替他決定。
    /// </summary>
    private static string Remove(FireReviewParameterDefinition definition) =>
        $"請先在「管理 > 專案參數」選 {definition.Name} 按「移除」，再回來按一次一鍵建立。" +
        "移除會讓原本填在這個參數上的值消失，需要的話請先記下來或匯出明細表。";
}
