using System;
using BuildingRegulationReview.Domain.Common;
using BuildingRegulationReview.Domain.Geometry;

namespace BuildingRegulationReview.Application.Geometry;

// What the extraction reads (spec 10.1). Defaults match the first-version scope: host walls and
// columns inside the view's crop or scope box, links off until P2-T02 proves a read-only strategy.
public sealed class PlanGeometryExtractionOptions
{
    public static readonly PlanGeometryExtractionOptions Default = new PlanGeometryExtractionOptions();

    public PlanGeometryExtractionOptions(
        bool includeWallCenterlines = true,
        bool includeColumnOutlines = true,
        bool includeAuxiliaryLines = true,
        bool includeRoomSeparationLines = true,
        bool includeLinkedModels = false,
        bool restrictToViewExtent = true,
        GeometryTolerance? tolerance = null)
    {
        if (!includeWallCenterlines && !includeColumnOutlines && !includeAuxiliaryLines && !includeRoomSeparationLines)
            throw new ArgumentException("At least one geometry source must be enabled.", nameof(includeWallCenterlines));

        IncludeWallCenterlines = includeWallCenterlines;
        IncludeColumnOutlines = includeColumnOutlines;
        IncludeAuxiliaryLines = includeAuxiliaryLines;
        IncludeRoomSeparationLines = includeRoomSeparationLines;
        IncludeLinkedModels = includeLinkedModels;
        RestrictToViewExtent = restrictToViewExtent;
        Tolerance = tolerance ?? GeometryTolerance.Default;
    }

    public bool IncludeWallCenterlines { get; }
    public bool IncludeColumnOutlines { get; }
    public bool IncludeAuxiliaryLines { get; }

    /// <summary>Room Separation lines on this level — the only edge a 挑空 or other wall-less 區劃 has.</summary>
    public bool IncludeRoomSeparationLines { get; }

    /// <summary>Links stay read-only references; write-back always happens in the host document.</summary>
    public bool IncludeLinkedModels { get; }

    /// <summary>Clips to the Area Plan's crop region or scope box when the view defines one.</summary>
    public bool RestrictToViewExtent { get; }

    public GeometryTolerance Tolerance { get; }

    public bool Includes(GeometrySourceKind kind)
    {
        switch (kind)
        {
            case GeometrySourceKind.WallCenterline: return IncludeWallCenterlines;
            case GeometrySourceKind.ColumnOutline: return IncludeColumnOutlines;
            case GeometrySourceKind.AuxiliaryLine: return IncludeAuxiliaryLines;
            case GeometrySourceKind.RoomSeparationLine: return IncludeRoomSeparationLines;
            default: throw new ArgumentOutOfRangeException(nameof(kind));
        }
    }
}

public sealed class PlanGeometryExtractionRequest
{
    public PlanGeometryExtractionRequest(Guid packageId, string areaPlanUniqueId, PlanGeometryExtractionOptions? options = null)
    {
        if (packageId == Guid.Empty) throw new ArgumentException("Package ID cannot be empty.", nameof(packageId));
        if (string.IsNullOrWhiteSpace(areaPlanUniqueId)) throw new ArgumentException("Area Plan UniqueId is required.", nameof(areaPlanUniqueId));

        PackageId = packageId;
        AreaPlanUniqueId = areaPlanUniqueId.Trim();
        Options = options ?? PlanGeometryExtractionOptions.Default;
    }

    public Guid PackageId { get; }
    public string AreaPlanUniqueId { get; }
    public PlanGeometryExtractionOptions Options { get; }
}

// Implemented by the Revit adapter in P2-T02. Read-only by contract: extraction opens no
// transaction and must never modify the model.
public interface IPlanGeometryExtractor
{
    Result<PlanGeometrySnapshot> Extract(PlanGeometryExtractionRequest request);
}
