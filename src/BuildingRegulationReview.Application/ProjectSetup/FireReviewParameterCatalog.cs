using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using BuildingRegulationReview.Application.Checks;
using BuildingRegulationReview.Application.Reviews;
using BuildingRegulationReview.Domain.ProjectSetup;

namespace BuildingRegulationReview.Application.ProjectSetup;

/// <summary>
/// 參數在 Revit 屬性面板上的群組。與共用參數主檔的 <c>GROUP</c> 欄一致：防火檢討的參數歸「防火」，
/// 推定用的 結構材料／防火被覆厚度 歸「結構」。
/// </summary>
public enum FireReviewParameterGroup
{
    FireProtection,
    Structural
}

/// <summary>
/// 一個防火檢討專案參數的完整定義：名稱、GUID、資料型別、綁實體還是類型、綁哪些類別，以及要給
/// 使用者看的用途說明。
/// </summary>
/// <remarks>
/// GUID 是專案參數與共用參數定義的繫結鍵，換一個就等於讓已經綁過舊定義的模型變成孤兒，所以它寫在
/// 這裡也寫在 <c>assets/SharedParameters/fire-review-shared-params.txt</c>，由
/// <c>FireReviewParameterCatalogTests</c> 逐項比對兩邊。名稱是中文，主檔是 Big5／cp950，安裝時
/// 以 GUID 查定義、再核對名稱，名稱對不上就是編碼出問題而不是定義少了。
/// </remarks>
public sealed class FireReviewParameterDefinition
{
    internal FireReviewParameterDefinition(
        string name,
        Guid guid,
        SharedParameterValueType valueType,
        ReviewParameterLevel level,
        IEnumerable<ReviewParameterHost> hosts,
        FireReviewParameterGroup group,
        string purpose)
    {
        Name = name;
        Guid = guid;
        ValueType = valueType;
        Level = level;
        Hosts = new ReadOnlyCollection<ReviewParameterHost>(
            hosts.Distinct().OrderBy(x => (int)x).ToList());
        Group = group;
        Purpose = purpose;
    }

    public string Name { get; }
    public Guid Guid { get; }
    public SharedParameterValueType ValueType { get; }
    public ReviewParameterLevel Level { get; }

    /// <summary>綁定的類別，依 <see cref="ReviewParameterHost"/> 的宣告順序，讓訊息文字不會隨機變動。</summary>
    public IReadOnlyList<ReviewParameterHost> Hosts { get; }

    public FireReviewParameterGroup Group { get; }

    /// <summary>這個參數是做什麼的、誰要填，給差異預覽那一欄用。</summary>
    public string Purpose { get; }

    public string HostsText => string.Join("、", Hosts.Select(ReviewInputSources.Label));
    public string LevelText => ReviewInputSources.Label(Level);
    public string ValueTypeText => FireReviewParameterCatalog.Label(ValueType);

    public override string ToString() => $"{Name}（{LevelText}，{HostsText}）";
}

/// <summary>
/// 防火檢討要在專案裡建立的所有共用參數。「一鍵建立」就是把這份清單與專案現況比對後補齊
/// （spec 8.2）。
/// </summary>
/// <remarks>
/// 清單不是另外手寫一份，而是由 <see cref="ReviewInputSources.All"/> 推出來的：檢討會讀的每一個
/// 參數都要建得出來，同一個名稱在不同欄位下的類別取聯集（設計防火時效 既答主要構造的第70條、也答
/// 管道間維修門的第79條之2第1項，兩邊的類別都要綁到）。這樣未來規則新增一個輸入欄位時，這個功能
/// 自動跟著建立它，只差在 <see cref="Declarations"/> 補上它的 GUID 與說明——沒補會讓測試直接失敗，
/// 不會靜靜漏掉。
///
/// 另外兩個不是檢討讀的，但「防火參數批次設定」面板要靠它們依第71～73條推定時效，模型沒綁就只能
/// 逐一手填：結構材料 與 防火被覆厚度。
///
/// 不在清單裡的是 防火檢討_法規要求防火時效（主檔 …000a）：那是保留給回寫的欄位，沒有任何規則或
/// 面板讀它，建了只是多一個沒人填的格子。
/// </remarks>
public static class FireReviewParameterCatalog
{
    private const string GuidPrefix = "bcf10001-0000-4a00-9b00-0000000000";

    /// <summary>
    /// 每個參數名稱對應的 GUID、資料型別、群組與用途說明。GUID 與型別必須與共用參數主檔一致。
    /// </summary>
    private static readonly IReadOnlyDictionary<string, Declaration> Declarations =
        new Dictionary<string, Declaration>(StringComparer.Ordinal)
        {
            [ReviewInputSources.FireResistiveConstruction] = new Declaration("01", SharedParameterValueType.YesNo,
                FireReviewParameterGroup.FireProtection,
                "本案是否為防火構造建築物。沒勾（或沒填）整套規則都不啟動，檢討結果會全是資料不足。"),
            [ReviewInputSources.BuildingUse] = new Declaration("02", SharedParameterValueType.Text,
                FireReviewParameterGroup.FireProtection,
                "建築技術規則的建築物用途類組，例如 A-1、B-2。第79條之1的用途豁免要靠它。"),
            [ReviewInputSources.FloorsAboveGround] = new Declaration("03", SharedParameterValueType.Integer,
                FireReviewParameterGroup.FireProtection,
                "地上層數。可由「防火參數批次設定」面板依各樓層高程提案。"),
            [ReviewInputSources.ZoneUse] = new Declaration("05", SharedParameterValueType.Text,
                FireReviewParameterGroup.FireProtection,
                "每個區劃的用途，填在 Area 上。面積上限的用途豁免與樓梯間都看這一欄。"),
            [ReviewInputSources.Sprinklered] = new Declaration("06", SharedParameterValueType.YesNo,
                FireReviewParameterGroup.FireProtection,
                "該區劃是否設有自動滅火設備。影響面積上限（未設 1500 m²／有設 3000 m²）。"),
            [ReviewInputSources.FloorNumber] = new Declaration("07", SharedParameterValueType.Integer,
                FireReviewParameterGroup.FireProtection,
                "該區劃所在的樓層序（地上為正、地下為負）。"),
            [ReviewInputSources.InteriorFinish] = new Declaration("0e", SharedParameterValueType.Text,
                FireReviewParameterGroup.FireProtection,
                "牆與天花板類型的室內裝修耐燃等級：無／耐燃一級／耐燃一級含底材。檢討時由區劃內實際構件彙總。"),
            [FireRatingParameters.Provided] = new Declaration("08", SharedParameterValueType.Text,
                FireReviewParameterGroup.FireProtection,
                "設計或認證防火時效，例如 60 min、1hr、一小時。主要構造依第70條，管道間維修門依第79條之2第1項，帷幕嵌板依第79條第4項。可由批次設定面板推定。"),
            [FireProtectionParameters.Provided] = new Declaration("0d", SharedParameterValueType.YesNo,
                FireReviewParameterGroup.FireProtection,
                "勾選＝這個型號是防火門窗等防火設備；未勾＝不是。是型號的性質，逐一實體勾沒有意義。"),
            [SmokeProtectionParameters.Provided] = new Declaration("0f", SharedParameterValueType.YesNo,
                FireReviewParameterGroup.FireProtection,
                "勾選＝這個型號通過遮煙性能試驗（第1條第45款）。第79條之2第1項對昇降機道與管道間維修門同時要求防火與遮煙。"),
            [CurtainPanelKindParameters.Provided] = new Declaration("13", SharedParameterValueType.Text,
                FireReviewParameterGroup.FireProtection,
                "帷幕嵌板種類：實心或玻璃。實心以設計防火時效作答、玻璃以設計防火保護作答，未宣告則判資料不足（決議 16）。"),
            [ReviewInputSources.LinksRefugeFloor] = new Declaration("11", SharedParameterValueType.YesNo,
                FireReviewParameterGroup.FireProtection,
                "挑空是否為避難層通達其直上層或直下層（第79條之2第3項第一款）。只有挑空的區劃需要填。"),
            [ReviewInputSources.CannotBeSubdivided] = new Declaration("12", SharedParameterValueType.YesNo,
                FireReviewParameterGroup.FireProtection,
                "本區劃是否為構造或設備上無法區劃分隔之部分（第79條之1）。只有觀眾席、生產線、教室、體育館、零售市場、停車空間需要填。"),
            [StructuralMaterialParameters.Material] = new Declaration("0b", SharedParameterValueType.Text,
                FireReviewParameterGroup.Structural,
                "結構材料：RC／SRC／SC。批次設定面板用它加上斷面尺寸依第71～73條推定防火時效；檢討本身不讀。"),
            [StructuralMaterialParameters.Cover] = new Declaration("0c", SharedParameterValueType.Length,
                FireReviewParameterGroup.Structural,
                "防火被覆的單面厚度，只有 SC 鋼骨造需要；RC／SRC 留白。推定時效用，檢討本身不讀。")
        };

    /// <summary>要建立的參數，依綁定的類別與名稱排序。</summary>
    public static IReadOnlyList<FireReviewParameterDefinition> All { get; } = Build();

    public static FireReviewParameterDefinition? For(string name) =>
        All.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.Ordinal));

    public static string Label(SharedParameterValueType valueType) => valueType switch
    {
        SharedParameterValueType.Text => "文字",
        SharedParameterValueType.YesNo => "是非",
        SharedParameterValueType.Integer => "整數",
        SharedParameterValueType.Number => "數值",
        SharedParameterValueType.Length => "長度",
        _ => valueType.ToString()
    };

    public static string Label(FireReviewParameterGroup group) => group switch
    {
        FireReviewParameterGroup.Structural => "結構",
        _ => "防火"
    };

    private static IReadOnlyList<FireReviewParameterDefinition> Build()
    {
        var defined = new List<FireReviewParameterDefinition>();

        foreach (var byName in ReviewInputSources.All.GroupBy(x => x.ParameterName, StringComparer.Ordinal))
        {
            var levels = byName.Select(x => x.Level).Distinct().ToList();
            if (levels.Count != 1)
            {
                throw new InvalidOperationException(
                    $"參數 {byName.Key} 在 ReviewInputSources 裡同時被當成 " +
                    string.Join("、", levels.Select(ReviewInputSources.Label)) +
                    "，無法決定要綁實體還是類型。");
            }

            defined.Add(Define(byName.Key, levels[0], byName.SelectMany(x => x.Hosts)));
        }

        // 推定用的兩個。綁在會由自己的時效作答的構造上（牆、柱、梁、樓板、帷幕嵌板），與
        // 設計防火時效 同一組——但不含門：管道間維修門答的是時效，不是由斷面推定的構造。
        defined.Add(Define(StructuralMaterialParameters.Material, ReviewParameterLevel.Type, ReviewInputSources.FireRatingHosts));
        defined.Add(Define(StructuralMaterialParameters.Cover, ReviewParameterLevel.Type, ReviewInputSources.FireRatingHosts));

        return new ReadOnlyCollection<FireReviewParameterDefinition>(defined
            .OrderBy(x => x.Hosts.Min(h => (int)h))
            .ThenBy(x => x.Name, StringComparer.Ordinal)
            .ToList());
    }

    private static FireReviewParameterDefinition Define(
        string name,
        ReviewParameterLevel level,
        IEnumerable<ReviewParameterHost> hosts)
    {
        if (!Declarations.TryGetValue(name, out var declaration))
        {
            throw new InvalidOperationException(
                $"檢討會讀參數 {name}，但 FireReviewParameterCatalog 沒有登錄它的 GUID、資料型別、群組與用途說明，" +
                "「一鍵建立」建不出來。請同時在 assets/SharedParameters/fire-review-shared-params.txt 與這裡加上定義。");
        }

        if (level == ReviewParameterLevel.InstanceOrType)
        {
            throw new InvalidOperationException(
                $"參數 {name} 登錄為「實體或類型」，但專案參數只能綁其中一邊；請在 ReviewInputSources 決定它是實體還是類型參數。");
        }

        return new FireReviewParameterDefinition(
            name,
            new Guid(GuidPrefix + declaration.GuidSuffix),
            declaration.ValueType,
            level,
            hosts,
            declaration.Group,
            declaration.Purpose);
    }

    private sealed class Declaration
    {
        public Declaration(string guidSuffix, SharedParameterValueType valueType, FireReviewParameterGroup group, string purpose)
        {
            GuidSuffix = guidSuffix;
            ValueType = valueType;
            Group = group;
            Purpose = purpose;
        }

        /// <summary>GUID 的最後兩碼；前面 34 碼是共用的 <see cref="GuidPrefix"/>。</summary>
        public string GuidSuffix { get; }

        public SharedParameterValueType ValueType { get; }
        public FireReviewParameterGroup Group { get; }
        public string Purpose { get; }
    }
}
