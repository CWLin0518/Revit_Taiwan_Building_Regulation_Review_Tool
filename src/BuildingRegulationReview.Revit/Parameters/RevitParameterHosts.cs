using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Autodesk.Revit.DB;
using BuildingRegulationReview.Application.Reviews;

namespace BuildingRegulationReview.Revit.Parameters;

/// <summary>
/// <see cref="ReviewParameterHost"/> 與 Revit 內建類別的對應。讀參數綁定與建立參數綁定用的是同一份
/// 對應，分成兩份遲早會漂掉：柱一個 host 對應兩個內建類別（<c>OST_Columns</c> 建築柱與
/// <c>OST_StructuralColumns</c> 結構柱），漏掉其中一個就是「明明綁了卻讀不到」。
/// </summary>
public static class RevitParameterHosts
{
    public static IReadOnlyDictionary<BuiltInCategory, ReviewParameterHost> ByCategory { get; } =
        new ReadOnlyDictionary<BuiltInCategory, ReviewParameterHost>(
            new Dictionary<BuiltInCategory, ReviewParameterHost>
            {
                { BuiltInCategory.OST_ProjectInformation, ReviewParameterHost.ProjectInformation },
                { BuiltInCategory.OST_Areas, ReviewParameterHost.Areas },
                { BuiltInCategory.OST_Walls, ReviewParameterHost.Walls },
                { BuiltInCategory.OST_Columns, ReviewParameterHost.Columns },
                { BuiltInCategory.OST_StructuralColumns, ReviewParameterHost.Columns },
                { BuiltInCategory.OST_StructuralFraming, ReviewParameterHost.StructuralFraming },
                { BuiltInCategory.OST_Floors, ReviewParameterHost.Floors },
                { BuiltInCategory.OST_Ceilings, ReviewParameterHost.Ceilings },
                { BuiltInCategory.OST_Doors, ReviewParameterHost.Doors },
                { BuiltInCategory.OST_Windows, ReviewParameterHost.Windows },
                { BuiltInCategory.OST_CurtainWallPanels, ReviewParameterHost.CurtainPanels }
            });

    private static readonly IReadOnlyDictionary<ReviewParameterHost, IReadOnlyList<BuiltInCategory>> Reverse =
        new ReadOnlyDictionary<ReviewParameterHost, IReadOnlyList<BuiltInCategory>>(
            ByCategory.GroupBy(pair => pair.Value).ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<BuiltInCategory>)new ReadOnlyCollection<BuiltInCategory>(
                    group.Select(pair => pair.Key).ToList())));

    /// <summary>綁一個 host 要綁到的內建類別，一個都不能少（柱是兩個）。</summary>
    public static IReadOnlyList<BuiltInCategory> CategoriesOf(ReviewParameterHost host) =>
        Reverse.TryGetValue(host, out var categories)
            ? categories
            : throw new ArgumentOutOfRangeException(nameof(host), host, "沒有對應的 Revit 內建類別。");
}
