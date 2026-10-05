using Autodesk.Revit.DB;
using BuildingRegulationReview.Application.Candidates;

namespace BuildingRegulationReview.Revit.Geometry;

/// <summary>
/// Reads a Wall Type's <c>Function</c> as the declaration
/// <see cref="CurtainWallFunctionDeclaration"/> names (docs/regulations/curtain-wall-fire-compartment.md §4.8).
/// </summary>
/// <remarks>
/// One reader for both pipelines — <c>RevitCandidateObservationReader</c> and
/// <c>RevitCurtainWallGeometryReader</c> — so the two cannot disagree about what the model declared.
/// It converts and nothing else; whether the declaration beats the geometry is
/// <see cref="CurtainWallExposureClassifier"/>'s question. A <c>CurtainSystem</c> has no such
/// parameter and reads as <see cref="CurtainWallFunctionDeclaration.NotRead"/>.
/// </remarks>
public static class RevitWallFunctionReader
{
    public static CurtainWallFunctionDeclaration Of(WallType? type)
    {
        if (type is null) return CurtainWallFunctionDeclaration.NotRead;

        var parameter = type.get_Parameter(BuiltInParameter.FUNCTION_PARAM);
        if (parameter is null || parameter.StorageType != StorageType.Integer || !parameter.HasValue)
            return CurtainWallFunctionDeclaration.NotRead;

        return parameter.AsInteger() switch
        {
            (int)WallFunction.Exterior => CurtainWallFunctionDeclaration.Exterior,
            (int)WallFunction.Interior => CurtainWallFunctionDeclaration.Interior,
            _ => CurtainWallFunctionDeclaration.Other
        };
    }

    public static CurtainWallFunctionDeclaration Of(Element? element) =>
        Of((element as Wall)?.WallType ?? element as WallType);
}
