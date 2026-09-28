using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reflection;
using Autodesk.Revit.DB;
using BuildingRegulationReview.Application.ProjectSetup;
using BuildingRegulationReview.Application.Reviews;
using BuildingRegulationReview.Revit.Parameters;

namespace BuildingRegulationReview.Revit.ProjectSetup;

/// <summary>建立與補綁的結果。部分失敗仍然提交成功的那些，並逐項說明沒成功的是哪一個、為什麼。</summary>
public sealed class FireReviewParameterInstallResult
{
    internal FireReviewParameterInstallResult(
        bool committed,
        IEnumerable<string> created,
        IEnumerable<string> extended,
        IEnumerable<string> failures,
        IEnumerable<string> warnings)
    {
        Committed = committed;
        Created = new ReadOnlyCollection<string>(created.ToList());
        Extended = new ReadOnlyCollection<string>(extended.ToList());
        Failures = new ReadOnlyCollection<string>(failures.ToList());
        Warnings = new ReadOnlyCollection<string>(warnings.Distinct(StringComparer.Ordinal).ToList());
    }

    /// <summary>交易是否提交。false 代表模型完全沒有被改動。</summary>
    public bool Committed { get; }

    public IReadOnlyList<string> Created { get; }
    public IReadOnlyList<string> Extended { get; }
    public IReadOnlyList<string> Failures { get; }
    public IReadOnlyList<string> Warnings { get; }

    public string Summary
    {
        get
        {
            var parts = new List<string>();
            if (Created.Count > 0) parts.Add($"已建立 {Created.Count} 個參數");
            if (Extended.Count > 0) parts.Add($"已補綁 {Extended.Count} 個參數的類別");
            if (parts.Count == 0) parts.Add("沒有任何參數被建立或補綁，模型未被改動");
            if (Failures.Count > 0) parts.Add($"{Failures.Count} 項未能處理");
            return string.Join("；", parts) + "。";
        }
    }
}

/// <summary>
/// 把防火檢討要用的共用參數一次建立並綁定到目前的 Revit 專案（spec 8.2「檢查／建立參數」）。
/// </summary>
/// <remarks>
/// 參數的定義（名稱、GUID、資料型別、說明）來自外掛自己部署的共用參數檔
/// <c>Data\fire-review-shared-params.txt</c>，不是在程式裡另外造一份：GUID 是專案參數與定義的繫結鍵，
/// 兩邊若各寫一份就會漂掉。查定義一律以 GUID（純 ASCII，不受編碼影響），查到之後才核對中文名稱——
/// 名稱對不上就是 Windows 的 ANSI 代碼頁不是 Big5，寧可說清楚也不要綁出一堆亂碼參數。
///
/// 綁定的層級與類別由 <see cref="FireReviewParameterCatalog"/> 決定，而它是從
/// <see cref="ReviewInputSources"/>（檢討真正會讀的來源）推出來的，所以不會出現「建好了但檢討讀不到」。
///
/// 補綁只加類別、不改型別也不改 GUID，既有值不受影響；同名但定義不符的一律不動，交由
/// <see cref="FireReviewParameterSetupPlanner"/> 產生修正指引。
/// </remarks>
public sealed class RevitFireReviewParameterInstaller : IFireReviewParameterBindingReader
{
    public const string SharedParameterFileName = "fire-review-shared-params.txt";

    private readonly Document _document;
    private readonly string _sharedParameterFilePath;

    public RevitFireReviewParameterInstaller(Document document, string? sharedParameterFilePath = null)
    {
        _document = document ?? throw new ArgumentNullException(nameof(document));
        _sharedParameterFilePath = string.IsNullOrWhiteSpace(sharedParameterFilePath)
            ? DefaultSharedParameterFilePath
            : sharedParameterFilePath!;
    }

    /// <summary>外掛部署目錄裡的那一份共用參數檔，與規則檔同一個 <c>Data</c> 資料夾。</summary>
    public static string DefaultSharedParameterFilePath =>
        Path.Combine(
            Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? string.Empty,
            "Data",
            SharedParameterFileName);

    public string SharedParameterFilePath => _sharedParameterFilePath;

    /// <summary>專案現在綁了哪些防火檢討參數，以及綁成什麼樣子。唯讀，不開交易。</summary>
    public IReadOnlyList<FireReviewParameterBindingObservation> Read()
    {
        var wanted = new HashSet<string>(
            FireReviewParameterCatalog.All.Select(x => x.Name), StringComparer.Ordinal);

        var observations = new List<FireReviewParameterBindingObservation>();
        using (var iterator = _document.ParameterBindings.ForwardIterator())
        {
            iterator.Reset();

            while (iterator.MoveNext())
            {
                var definition = iterator.Key;
                if (definition is null || !wanted.Contains(definition.Name)) continue;
                if (!(iterator.Current is ElementBinding binding)) continue;

                Guid? guid = null;
                if (definition is InternalDefinition internalDefinition &&
                    _document.GetElement(internalDefinition.Id) is SharedParameterElement shared)
                {
                    guid = shared.GuidValue;
                }

                observations.Add(new FireReviewParameterBindingObservation(
                    definition.Name,
                    guid,
                    RevitSharedParameterInventoryReader.ToValueType(definition.GetDataType()),
                    binding is TypeBinding ? ReviewParameterLevel.Type : ReviewParameterLevel.Instance,
                    HostsFullyBoundBy(binding)));
            }
        }

        return observations;
    }

    /// <summary>
    /// 一個 host 只有在它**每一個**內建類別都綁到時才算綁好。
    /// </summary>
    /// <remarks>
    /// 柱是兩個內建類別（建築柱 <c>OST_Columns</c> 與結構柱 <c>OST_StructuralColumns</c>）。只綁到其中
    /// 一個就回報「柱已綁定」的話，差異預覽會說「已綁定」、按鈕停用，使用者**再按幾次都補不上另一個**，
    /// 而檢討在那一半的型別上一律答資料不足——一條沒有出路的死巷。少一個就回報這個 host 沒綁，讓它走
    /// 補綁那條路（補綁會把兩個類別都插進去，已經在的那個插第二次無害）。
    /// </remarks>
    private static IEnumerable<ReviewParameterHost> HostsFullyBoundBy(ElementBinding binding)
    {
        var bound = new HashSet<BuiltInCategory>();
        if (binding.Categories is not null)
        {
            foreach (Category category in binding.Categories)
                if (category is not null) bound.Add(category.BuiltInCategory);
        }

        return RevitParameterHosts.ByCategory.Values
            .Distinct()
            .Where(host => RevitParameterHosts.CategoriesOf(host).All(bound.Contains))
            .ToList();
    }

    /// <summary>差異預覽。按下建立之前顯示給使用者看的就是這個。</summary>
    public FireReviewParameterSetupPlan Inspect() => FireReviewParameterSetupPlanner.Inspect(this);

    /// <summary>
    /// 建立缺少的參數、補綁缺少的類別。衝突項一律不動。要自己的交易，所以不可在別的交易裡呼叫。
    /// </summary>
    public FireReviewParameterInstallResult Apply(FireReviewParameterSetupPlan plan)
    {
        if (plan is null) throw new ArgumentNullException(nameof(plan));
        if (_document.IsModifiable)
            throw new InvalidOperationException("建立專案參數需要自己的交易，不能在其他交易進行中呼叫。");

        if (!plan.HasWork)
        {
            return new FireReviewParameterInstallResult(false, Array.Empty<string>(), Array.Empty<string>(),
                Array.Empty<string>(), Array.Empty<string>());
        }

        if (!File.Exists(_sharedParameterFilePath))
        {
            return Blocked(
                $"找不到共用參數檔 {_sharedParameterFilePath}。請重新部署外掛" +
                "（scripts\\install-revit-2024.ps1）後重開 Revit。");
        }

        var application = _document.Application;
        var originalFilename = application.SharedParametersFilename;
        try
        {
            application.SharedParametersFilename = _sharedParameterFilePath;

            DefinitionFile file;
            try
            {
                file = application.OpenSharedParameterFile();
            }
            catch (Autodesk.Revit.Exceptions.ApplicationException exception)
            {
                return Blocked($"Revit 無法讀取共用參數檔 {_sharedParameterFilePath}：{exception.Message}");
            }

            if (file is null) return Blocked($"Revit 無法讀取共用參數檔 {_sharedParameterFilePath}。");

            return Apply(plan, Index(file));
        }
        finally
        {
            // 使用者原本指定的共用參數檔是全域設定，借用完要還回去。原本沒有指定時 Revit 會拒絕空字串，
            // 那就維持指向外掛的檔案——比讓例外蓋掉真正的結果好。
            try
            {
                application.SharedParametersFilename = originalFilename;
            }
            catch (Exception exception) when (exception is Autodesk.Revit.Exceptions.ApplicationException ||
                                             exception is ArgumentException)
            {
            }
        }
    }

    private FireReviewParameterInstallResult Apply(
        FireReviewParameterSetupPlan plan,
        IReadOnlyDictionary<Guid, ExternalDefinition> definitions)
    {
        var created = new List<string>();
        var extended = new List<string>();
        var failures = new List<string>();
        var warnings = new List<string>();

        using (var transaction = new Transaction(_document, "建立防火檢討專案參數"))
        {
            transaction.Start();
            try
            {
                foreach (var action in plan.ToCreate)
                    if (Create(action, definitions, failures, warnings)) created.Add(action.Definition.Name);

                foreach (var action in plan.ToExtend)
                    if (Extend(action, failures, warnings)) extended.Add(action.Definition.Name);

                if (created.Count == 0 && extended.Count == 0)
                {
                    transaction.RollBack();
                    return new FireReviewParameterInstallResult(false, created, extended, failures, warnings);
                }

                if (transaction.Commit() != TransactionStatus.Committed)
                    throw new InvalidOperationException("Revit 沒有提交建立專案參數的交易。");

                return new FireReviewParameterInstallResult(true, created, extended, failures, warnings);
            }
            catch
            {
                if (transaction.GetStatus() == TransactionStatus.Started) transaction.RollBack();
                throw;
            }
        }
    }

    private bool Create(
        FireReviewParameterAction action,
        IReadOnlyDictionary<Guid, ExternalDefinition> definitions,
        List<string> failures,
        List<string> warnings)
    {
        var definition = action.Definition;

        if (!definitions.TryGetValue(definition.Guid, out var shared))
        {
            failures.Add($"{definition.Name}：共用參數檔裡沒有 GUID {definition.Guid:D} 的定義，" +
                         "部署的檔案可能不是外掛的那一份。");
            return false;
        }

        if (!string.Equals(shared.Name, definition.Name, StringComparison.Ordinal))
        {
            failures.Add($"{definition.Name}：共用參數檔讀出來的名稱是「{shared.Name}」。" +
                         "這個檔是 Big5／cp950，Revit 依 Windows「非 Unicode 程式語言」的設定讀它；" +
                         "那個設定不是繁體中文時中文參數名就會變成亂碼。請改設定後重開 Revit，" +
                         "或改用「管理 > 共用參數」手動加入。");
            return false;
        }

        var categories = CategoriesOf(definition, null, warnings);
        if (Missing(definition, categories) is string unreachable)
        {
            failures.Add($"{definition.Name}：{unreachable}");
            return false;
        }

        if (_document.ParameterBindings.Insert(shared, BindingOf(definition, categories), GroupOf(definition.Group)))
            return true;

        failures.Add($"{definition.Name}：Revit 不接受這個綁定。專案裡可能已經有同名的參數，" +
                     $"或 {definition.Name} 與某個類別的內建參數同名（內建的那個佔住了名字）。" +
                     "請到「管理 > 專案參數」確認。");
        return false;
    }

    private bool Extend(FireReviewParameterAction action, List<string> failures, List<string> warnings)
    {
        var definition = action.Definition;

        // 先把要動的那一筆找出來再動它：BindingMap 正在被列舉時 ReInsert 會讓列舉器失效。
        Definition? key = null;
        ElementBinding? existing = null;
        using (var iterator = _document.ParameterBindings.ForwardIterator())
        {
            iterator.Reset();
            while (iterator.MoveNext())
            {
                if (iterator.Key is null || !string.Equals(iterator.Key.Name, definition.Name, StringComparison.Ordinal))
                    continue;
                if (!(iterator.Current is ElementBinding binding)) continue;

                key = iterator.Key;
                existing = binding;
                break;
            }
        }

        if (key is null || existing is null)
        {
            failures.Add($"{definition.Name}：專案裡找不到這個專案參數，可能在預覽之後被移除了。請按「重新檢查」。");
            return false;
        }

        // 預覽是先算好的，ReInsert 卻是現在才動手，所以動手前自己再核對一次定義。這是整個功能唯一
        // 會置換既有綁定的地方——「絕不覆蓋定義不符的參數」必須是這裡的結構保證，不能只靠呼叫端剛好
        // 先跑過 planner。
        if (Mismatch(definition, key, existing) is string mismatch)
        {
            failures.Add($"{definition.Name}：{mismatch}這一筆沒有被改動，請按「重新檢查」重新判定。");
            return false;
        }

        var categories = CategoriesOf(definition, existing.Categories, warnings);
        if (Missing(definition, categories) is string unreachable)
        {
            failures.Add($"{definition.Name}：{unreachable}");
            return false;
        }

        // 沿用參數現在所在的屬性群組：使用者若把它搬過位置，補綁類別不該順手把它搬回來。讀不到群組
        // （空的 ForgeTypeId）時才落回外掛的群組，否則 ReInsert 會拒收。
        var group = key.GetGroupTypeId();
        if (group is null || group.Empty()) group = GroupOf(definition.Group);

        if (_document.ParameterBindings.ReInsert(key, BindingOf(definition, categories), group))
            return true;

        failures.Add($"{definition.Name}：Revit 不接受補綁後的類別組合。");
        return false;
    }

    /// <summary>
    /// 現況與定義不符的地方，相符則為 null。與 <see cref="FireReviewParameterSetupPlanner"/> 判衝突的
    /// 依據相同（GUID、資料型別、實體／類型），只是在真正動手的那一刻重讀一次。
    /// </summary>
    private string? Mismatch(FireReviewParameterDefinition definition, Definition key, ElementBinding binding)
    {
        var guid = key is InternalDefinition internalDefinition &&
                   _document.GetElement(internalDefinition.Id) is SharedParameterElement shared
            ? shared.GuidValue
            : (Guid?)null;

        if (guid is null) return "專案裡的這個參數不是共用參數。";
        if (guid.Value != definition.Guid) return $"專案裡的這個參數是另一個 GUID（{guid.Value:D}）。";

        var valueType = RevitSharedParameterInventoryReader.ToValueType(key.GetDataType());
        if (valueType != definition.ValueType) return "專案裡的這個參數是另一種資料型別。";

        var level = binding is TypeBinding ? ReviewParameterLevel.Type : ReviewParameterLevel.Instance;
        return level != definition.Level ? "專案裡的這個參數綁在另一個層級（實體／類型）。" : null;
    }

    /// <summary>
    /// 定義要求的類別裡，實際上一個都沒進到 <paramref name="categories"/> 的那些，相符則為 null。
    /// 全部到位才回 null——少綁任何一個都要說出來，否則 ReInsert 會「成功」卻什麼也沒補上。
    /// </summary>
    private string? Missing(FireReviewParameterDefinition definition, CategorySet categories)
    {
        var unreachable = definition.Hosts
            .Where(host => RevitParameterHosts.CategoriesOf(host)
                .Any(builtIn => CategoryOf(builtIn) is not Category category || !categories.Contains(category)))
            .Select(ReviewInputSources.Label)
            .ToList();

        if (unreachable.Count == 0) return null;

        return categories.Size == 0
            ? $"{definition.HostsText} 在這個專案裡都不能接受專案參數，沒有可綁定的類別。"
            : $"{string.Join("、", unreachable)} 無法綁定（類別不存在或不接受專案參數），已整筆略過。";
    }

    /// <summary>
    /// 要綁的類別集合：<paramref name="keep"/> 裡原有的一律留著（外掛沒在管的類別也不動），再加上
    /// 定義要求的那些。不接受專案參數的類別會被跳過並記一條提醒，而不是讓整個綁定失敗。
    /// </summary>
    private CategorySet CategoriesOf(
        FireReviewParameterDefinition definition,
        CategorySet? keep,
        List<string> warnings)
    {
        var categories = _document.Application.Create.NewCategorySet();

        if (keep is not null)
            foreach (Category category in keep)
                if (category is not null) categories.Insert(category);

        foreach (var host in definition.Hosts)
        {
            foreach (var builtIn in RevitParameterHosts.CategoriesOf(host))
            {
                var category = CategoryOf(builtIn);
                if (category is null)
                {
                    warnings.Add($"專案裡沒有 {ReviewInputSources.Label(host)}（{builtIn}）這個類別，{definition.Name} 沒有綁到它。");
                    continue;
                }

                if (!category.AllowsBoundParameters)
                {
                    warnings.Add($"{category.Name} 不接受專案參數，{definition.Name} 沒有綁到它。");
                    continue;
                }

                categories.Insert(category);
            }
        }

        return categories;
    }

    private Category? CategoryOf(BuiltInCategory builtIn)
    {
        try
        {
            return _document.Settings.Categories.get_Item(builtIn);
        }
        catch (Exception exception) when (exception is Autodesk.Revit.Exceptions.ApplicationException ||
                                          exception is Autodesk.Revit.Exceptions.ArgumentException)
        {
            return null;
        }
    }

    private Binding BindingOf(FireReviewParameterDefinition definition, CategorySet categories) =>
        definition.Level == ReviewParameterLevel.Type
            ? _document.Application.Create.NewTypeBinding(categories)
            : (Binding)_document.Application.Create.NewInstanceBinding(categories);

    private static ForgeTypeId GroupOf(FireReviewParameterGroup group) =>
        group == FireReviewParameterGroup.Structural ? GroupTypeId.Structural : GroupTypeId.FireProtection;

    /// <summary>共用參數檔的定義，以 GUID 為鍵——GUID 是純 ASCII，不會因為代碼頁而讀錯。</summary>
    private static IReadOnlyDictionary<Guid, ExternalDefinition> Index(DefinitionFile file)
    {
        var definitions = new Dictionary<Guid, ExternalDefinition>();
        foreach (DefinitionGroup group in file.Groups)
        {
            foreach (Definition definition in group.Definitions)
            {
                if (definition is ExternalDefinition external && !definitions.ContainsKey(external.GUID))
                    definitions[external.GUID] = external;
            }
        }

        return definitions;
    }

    private static FireReviewParameterInstallResult Blocked(string reason) =>
        new FireReviewParameterInstallResult(false, Array.Empty<string>(), Array.Empty<string>(),
            new[] { reason }, Array.Empty<string>());
}
