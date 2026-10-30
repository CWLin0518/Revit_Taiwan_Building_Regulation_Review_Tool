using System;
using Autodesk.Revit.DB;

namespace BuildingRegulationReview.Revit.Geometry;

// A Revit Document has no UniqueId of its own, but its ProjectInformation element does and it
// survives renaming, moving and detaching. Snapshots outlive file paths, so provenance is keyed on
// that instead of PathName; the path is only a fallback for documents without project information.
public static class RevitDocumentIdentity
{
    public static string Of(Document document)
    {
        if (document is null) throw new ArgumentNullException(nameof(document));

        var projectInformation = document.ProjectInformation;
        if (projectInformation != null && !string.IsNullOrWhiteSpace(projectInformation.UniqueId))
            return projectInformation.UniqueId;

        if (!string.IsNullOrWhiteSpace(document.PathName)) return document.PathName;
        if (!string.IsNullOrWhiteSpace(document.Title)) return document.Title;

        throw new InvalidOperationException("無法識別 Revit 文件，缺少專案資訊與檔案路徑。");
    }
}
