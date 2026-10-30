using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Autodesk.Revit.DB;
using BuildingRegulationReview.Domain.Geometry;

namespace BuildingRegulationReview.Revit.Geometry;

/// <summary>
/// 帷幕牆上門窗位置的診斷輸出（驗證清單 3.1「帷幕牆門的 LocationPoint」）。只記錄，不影響判定。
/// </summary>
/// <remarks>
/// <para>
/// <see cref="RevitCurtainWallGeometryReader"/> 量一片嵌板的範圍時，有 <c>LocationPoint</c> 就把它當成
/// 中心，再上下各加半個高度。門的插入點通常在門檻，不在中心；若帷幕牆門真的有 <c>LocationPoint</c>，
/// 量出來的範圍會整片往下偏半個門高，影響它落在層間帶還是 CW-O。這裡把每扇帷幕牆門窗的各個位置來源
/// 並列出來，在 Revit 上跑一次就能看出實際走的是哪一條、差多少。
/// </para>
/// <para>
/// 開關：<see cref="Directory"/> 底下有 <see cref="SwitchFileName"/> 這個檔案才輸出，平常不寫任何東西。
/// 每次讀取（一個工作包一次）附加一段到 <see cref="LogFileName"/>。
/// </para>
/// </remarks>
internal sealed class CurtainOpeningDiagnostics
{
    public const string SwitchFileName = "curtain-wall-openings.on";
    public const string LogFileName = "curtain-wall-openings.log";

    public static string Directory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BuildingRegulationReview", "Diagnostics");

    private readonly List<string> _lines = new List<string>();

    private CurtainOpeningDiagnostics(string header) => _lines.Add(header);

    /// <summary>開關檔存在時回傳一個收集器，否則回 null（呼叫端一律以 <c>?.</c> 呼叫）。</summary>
    public static CurtainOpeningDiagnostics? StartIfEnabled(Level level)
    {
        try
        {
            if (!File.Exists(Path.Combine(Directory, SwitchFileName))) return null;
        }
        catch (Exception exception) when (IsIoFailure(exception))
        {
            return null;
        }

        return new CurtainOpeningDiagnostics(string.Format(CultureInfo.InvariantCulture,
            "=== {0:yyyy-MM-dd HH:mm:ss} 樓層 {1}（Id {2}，高程 {3} mm）",
            DateTime.Now, level.Name, level.Id, Mm(level.Elevation)));
    }

    /// <summary>
    /// 一扇帷幕牆門窗：位置的三個來源（LocationPoint、transform 原點、外接矩形）、讀到的寬高，
    /// 以及讀取器實際量出的範圍。
    /// </summary>
    public void Record(
        Element element,
        string? typeName,
        double? widthFeet,
        double? heightFeet,
        (double StartMm, double EndMm, double BottomMm, double TopMm)? extent)
    {
        var text = new StringBuilder();
        text.Append(string.Format(CultureInfo.InvariantCulture,
            "門窗 Id {0}（{1}，型別 {2}），宿主 Id {3}",
            element.Id, element.Category?.Name ?? "?", typeName ?? "?",
            (element as FamilyInstance)?.Host?.Id.ToString() ?? "?"));

        text.Append("\n  Location：");
        switch (element.Location)
        {
            case LocationPoint point:
                text.Append("LocationPoint ").Append(Xyz(point.Point));
                break;
            case LocationCurve curve:
                text.Append("LocationCurve ").Append(curve.Curve is null ? "（無曲線）" : Xyz(curve.Curve.GetEndPoint(0)) + " → " + Xyz(curve.Curve.GetEndPoint(1)));
                break;
            case null:
                text.Append("null");
                break;
            default:
                text.Append(element.Location.GetType().Name);
                break;
        }

        text.Append("\n  Transform 原點：").Append(TransformOrigin(element) is XYZ origin ? Xyz(origin) : "讀不到");

        var box = element.get_BoundingBox(null);
        text.Append("\n  外接矩形：").Append(box is null
            ? "null"
            : Xyz(box.Min) + " ～ " + Xyz(box.Max) + "，中心 " + Xyz((box.Min + box.Max) / 2.0));

        text.Append("\n  寬 ").Append(widthFeet is double w ? Mm(w) + " mm" : "讀不到")
            .Append("，高 ").Append(heightFeet is double h ? Mm(h) + " mm" : "讀不到");

        text.Append("\n  讀取器量出的範圍：").Append(extent is { } e
            ? string.Format(CultureInfo.InvariantCulture, "沿牆 {0:0.#}～{1:0.#} mm，高程 {2:0.#}～{3:0.#} mm",
                e.StartMm, e.EndMm, e.BottomMm, e.TopMm)
            : "null（這片被略過）");

        if (box is not null && extent is { } measured)
        {
            text.Append(string.Format(CultureInfo.InvariantCulture,
                "\n  與外接矩形高程的差：底 {0:+0.#;-0.#;0} mm，頂 {1:+0.#;-0.#;0} mm",
                measured.BottomMm - PlanUnits.FeetToMillimeters(box.Min.Z),
                measured.TopMm - PlanUnits.FeetToMillimeters(box.Max.Z)));
        }

        _lines.Add(text.ToString());
    }

    /// <summary>附加到記錄檔。寫不進去就算了：診斷不能讓檢討失敗。</summary>
    public void Flush()
    {
        if (_lines.Count == 1) _lines.Add("（這一層沒有帷幕牆門窗）");
        try
        {
            File.AppendAllText(Path.Combine(Directory, LogFileName),
                string.Join("\n", _lines) + "\n\n", new UTF8Encoding(false));
        }
        catch (Exception exception) when (IsIoFailure(exception))
        {
        }
    }

    private static XYZ? TransformOrigin(Element element)
    {
        try
        {
            return element is FamilyInstance instance ? instance.GetTotalTransform()?.Origin : null;
        }
        catch (Autodesk.Revit.Exceptions.ApplicationException)
        {
            return null;
        }
    }

    private static bool IsIoFailure(Exception exception) =>
        exception is IOException || exception is UnauthorizedAccessException || exception is System.Security.SecurityException;

    private static string Mm(double feet) =>
        PlanUnits.FeetToMillimeters(feet).ToString("0.#", CultureInfo.InvariantCulture);

    private static string Xyz(XYZ point) =>
        "(" + Mm(point.X) + ", " + Mm(point.Y) + ", " + Mm(point.Z) + ")";
}
