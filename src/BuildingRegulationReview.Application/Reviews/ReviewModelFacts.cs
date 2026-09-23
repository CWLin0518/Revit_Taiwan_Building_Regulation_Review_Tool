using System;

namespace BuildingRegulationReview.Application.Reviews;

/// <summary>
/// The building facts the model measures rather than the user typing them. Like <c>zone.area</c>
/// (spec 11.4 step 2), these can never be supplied as a <see cref="ReviewInput"/> overriding the
/// model: 建築物高度 is the model's own extent, so there is no Project Information parameter for it
/// and nothing to keep in sync.
/// </summary>
/// <remarks>
/// 建築技術規則建築設計施工編第1條第9款 measures the height from 基地地面 to the building's highest
/// point. The adapter reports the extent it can see — the top of the model above its lowest storey
/// level — and says so in the evidence, so a project whose 基地地面 is not that datum reads the
/// source and corrects it rather than silently trusting a number.
/// </remarks>
public sealed class ReviewModelFacts
{
    /// <summary>Nothing measured: <c>building.height</c> is then simply missing, never zero.</summary>
    public static readonly ReviewModelFacts None = new(null, null);

    public ReviewModelFacts(double? buildingHeightMeters, string? buildingHeightSource = null)
    {
        if (buildingHeightMeters is double h && (double.IsNaN(h) || double.IsInfinity(h) || h < 0))
            throw new ArgumentOutOfRangeException(nameof(buildingHeightMeters), "建築物高度不能是負數或非有限值。");

        BuildingHeightMeters = buildingHeightMeters;
        BuildingHeightSource = string.IsNullOrWhiteSpace(buildingHeightSource)
            ? "模型：最高構件頂端減最低樓層高程"
            : buildingHeightSource!.Trim();
    }

    /// <summary>The measured height in metres, or null when the model could not be measured.</summary>
    public double? BuildingHeightMeters { get; }

    /// <summary>What the height was measured from, kept as the input's evidence source.</summary>
    public string BuildingHeightSource { get; }
}
