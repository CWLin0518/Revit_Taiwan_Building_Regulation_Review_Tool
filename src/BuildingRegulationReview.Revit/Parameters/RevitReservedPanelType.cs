using Autodesk.Revit.DB;
using BuildingRegulationReview.Application.Checks;

namespace BuildingRegulationReview.Revit.Parameters;

/// <summary>
/// Revit 的保留嵌板型別（<c>System Panel : Wall</c>）與它真正的構造來源（決議 16、步驟 16h）。
/// </summary>
/// <remarks>
/// 帷幕牆型別把 <c>Curtain Panel</c> 指到一個 <c>Basic Wall</c> 型別時，格子裡放的是這個保留型別的
/// 嵌板：實測它沒有任何量體（<c>SolidCount</c> 0、讀不到 BoundingBox、連 <c>Volume</c> 參數都不存在），
/// 型別參數全部唯讀。讀取層把兩個事實交給 <see cref="CurtainPanelTypeSubstitution"/> 去判——
/// 型別參數是不是唯讀的，以及帷幕牆型別指到的是不是一個牆型別。
/// </remarks>
internal static class RevitReservedPanelType
{
    /// <summary>
    /// <c>WallType</c> 上顯示為「Curtain Panel」的那個型別參數。
    /// </summary>
    /// <remarks>
    /// Revit 2024 的 <c>BuiltInParameter</c> 同時有 <c>AUTO_PANEL</c> 與 <c>AUTO_PANEL_WALL</c>，
    /// 反射看不出哪一個才是它（enum 成員沒有中繼資料可查）。所以兩個都問，取第一個真的指到一個
    /// <c>ElementType</c> 的——這比挑一個寫死可靠：挑錯會讓整條路無聲地失效，而兩個都問在任何一種
    /// 對應下都答得出來，也與介面語言無關。
    /// </remarks>
    private static readonly BuiltInParameter[] CurtainPanelOfWallType =
    {
        BuiltInParameter.AUTO_PANEL,
        BuiltInParameter.AUTO_PANEL_WALL
    };

    /// <summary>
    /// 訂製嵌板族沒有 Thickness 時改問的參數，依序。第一個「型別身上找得到」的就定生死。
    /// </summary>
    /// <remarks>
    /// 這些是**退路**，不是主要探測：保留型別身上根本沒有這三個參數（實測 <c>System Panel : Wall</c>
    /// 的參數清單裡一個都沒有，而同為帷幕嵌板的 <c>系統面板1 : 玻璃 1.0cm</c> 三個都在），所以拿它們
    /// 當主探測會一路回 null 而永遠判不出保留型別。
    /// </remarks>
    private static readonly string[] FallbackProbes =
    {
        CurtainPanelKindParameters.Provided,
        FireRatingParameters.Provided,
        StructuralMaterialParameters.Material
    };

    /// <summary>
    /// 這個嵌板型別是不是 Revit 的保留型別——以 <c>Parameter.IsReadOnly</c> 實際判斷。
    /// </summary>
    /// <remarks>
    /// <para>
    /// **不以型別名稱比對**（「牆」／「Wall」）：名稱是可以改的，而反射實測 <c>PanelType</c> 自己
    /// 宣告零個屬性、零個方法，沒有任何現成 API 可以問「這是不是保留型別」。一個參數都問不到時
    /// 回 false——那是「問不出來」，照現行路徑走會答資料不足，比猜好。
    /// </para>
    /// <para>
    /// 主探測是內建的 Thickness（<c>CURTAIN_WALL_SYSPANEL_THICKNESS</c>）：每一個系統嵌板型別身上
    /// 都有它，與介面語言無關，而且兩邊實測分得開——保留型別 <c>System Panel : Wall</c> 寫入時
    /// Revit 答「參數 Thickness 是唯讀的」，一般型別 <c>系統面板1 : 玻璃 1.0cm</c> 寫得進去。
    /// 共享參數當不了主探測：保留型別身上根本沒有它們（見 <see cref="FallbackProbes"/>）。
    /// </para>
    /// </remarks>
    public static bool IsReserved(ElementType? type)
    {
        if (type is null) return false;

        var thickness = type.get_Parameter(BuiltInParameter.CURTAIN_WALL_SYSPANEL_THICKNESS);
        if (thickness is not null) return thickness.IsReadOnly;

        foreach (var name in FallbackProbes)
        {
            var parameter = type.LookupParameter(name);
            if (parameter is null) continue;

            return parameter.IsReadOnly;
        }

        return false;
    }

    /// <summary>
    /// 供給這片嵌板的牆型別，或 null 表示這片嵌板自己作答（不是保留型別，或解析不出牆型別）。
    /// </summary>
    /// <remarks>
    /// 逐格判斷：某一格被個別改成別的型別時，那一格走的是它自己型別的路，不套用 host 型別的
    /// <c>Curtain Panel</c>（Revit 允許逐格不同，<c>CurtainGrid.ChangePanelType</c> 收的是 <c>Element</c>）。
    /// </remarks>
    public static WallType? SourceWallTypeOf(Document document, Element panel, ElementType? panelType)
    {
        if (document is null || panel is null) return null;
        if (!IsReserved(panelType)) return null;

        var host = HostWallOf(document, panel);
        return host?.WallType is null ? null : CurtainPanelTypeOf(document, host.WallType);
    }

    /// <summary>The wall the panel sits in, or null for a curtain system's panel, which has no wall type.</summary>
    private static Wall? HostWallOf(Document document, Element panel)
    {
        if (panel is Panel typed && typed.Host is Wall hosted) return hosted;
        if (panel is FamilyInstance instance && instance.Host is Wall inserted) return inserted;

        var id = panel.get_Parameter(BuiltInParameter.CURTAIN_WALL_PANEL_HOST_ID)?.AsElementId();
        return id is null || id == ElementId.InvalidElementId ? null : document.GetElement(id) as Wall;
    }

    /// <summary>帷幕牆型別的「Curtain Panel」指到的牆型別，或 null 表示它指的不是一道牆。</summary>
    private static WallType? CurtainPanelTypeOf(Document document, WallType curtainWallType)
    {
        foreach (var builtIn in CurtainPanelOfWallType)
        {
            var id = curtainWallType.get_Parameter(builtIn)?.AsElementId();
            if (id is null || id == ElementId.InvalidElementId) continue;

            // 指到系統嵌板族（例如「系統面板1 : 玻璃 1.0cm」）時這裡是 null，代表沒有牆型別可借，
            // 照現行路徑走。
            if (document.GetElement(id) is WallType wallType) return wallType;
        }

        return null;
    }

    /// <summary>面板那一列要顯示的來源：名稱、厚度、結構材料、設計防火時效。</summary>
    public static CurtainPanelSourceType Describe(WallType wallType)
    {
        var familyName = string.IsNullOrWhiteSpace(wallType.FamilyName) ? null : wallType.FamilyName.Trim();
        var name = string.IsNullOrWhiteSpace(wallType.Name) ? "（未命名類型）" : wallType.Name.Trim();

        return new CurtainPanelSourceType(
            wallType.UniqueId,
            familyName is null ? name : $"{familyName}：{name}",
            thicknessMeters: Thickness(wallType),
            material: Text(wallType, StructuralMaterialParameters.Material),
            providedRating: Text(wallType, FireRatingParameters.Provided));
    }

    private static double? Thickness(WallType wallType)
    {
        if (wallType.Width > 0) return UnitUtils.ConvertFromInternalUnits(wallType.Width, UnitTypeId.Meters);

        CompoundStructure? structure;
        try
        {
            structure = wallType.GetCompoundStructure();
        }
        catch (Autodesk.Revit.Exceptions.ApplicationException)
        {
            return null;
        }

        var width = structure?.GetWidth() ?? 0;
        return width > 0 ? UnitUtils.ConvertFromInternalUnits(width, UnitTypeId.Meters) : (double?)null;
    }

    private static string? Text(Element element, string name)
    {
        var parameter = element.LookupParameter(name);
        if (parameter is null || !parameter.HasValue) return null;

        return parameter.StorageType switch
        {
            StorageType.String => parameter.AsString(),
            StorageType.Integer => parameter.AsValueString() ?? parameter.AsInteger().ToString(),
            StorageType.Double => parameter.AsValueString(),
            StorageType.ElementId => parameter.AsValueString(),
            _ => null
        };
    }
}
