using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;

namespace BuildingRegulationReview.Application.Parameters;

/// <summary>One of the model's levels, as the floor numbering reads it.</summary>
public sealed class BuildingLevel
{
    public BuildingLevel(string levelId, string name, double elevationMeters)
    {
        if (string.IsNullOrWhiteSpace(levelId)) throw new ArgumentException("A level needs its id.", nameof(levelId));
        if (double.IsNaN(elevationMeters) || double.IsInfinity(elevationMeters))
            throw new ArgumentOutOfRangeException(nameof(elevationMeters));

        LevelId = levelId.Trim();
        Name = string.IsNullOrWhiteSpace(name) ? levelId.Trim() : name.Trim();
        ElevationMeters = elevationMeters;
    }

    public string LevelId { get; }
    public string Name { get; }
    public double ElevationMeters { get; }

    public override string ToString() =>
        $"{Name} @ {ElevationMeters.ToString("0.###", CultureInfo.InvariantCulture)} m";
}

/// <summary>
/// The storey number of every level, and how many of them are above ground — the two facts 第70條
/// counts a storey's position from the top with.
/// </summary>
public sealed class FloorNumbering
{
    internal FloorNumbering(
        IDictionary<string, int> byLevelId,
        int floorsAboveGround,
        IEnumerable<string> warnings)
    {
        ByLevelId = new ReadOnlyDictionary<string, int>(byLevelId);
        FloorsAboveGround = floorsAboveGround;
        Warnings = new ReadOnlyCollection<string>(warnings.ToList());
    }

    public static readonly FloorNumbering Empty =
        new(new Dictionary<string, int>(StringComparer.Ordinal), 0, Array.Empty<string>());

    /// <summary>Storey number per level: 1 is the lowest above ground, −1 the first basement.</summary>
    public IReadOnlyDictionary<string, int> ByLevelId { get; }

    /// <summary>地上層數 — how many distinct elevations sit at or above ground.</summary>
    public int FloorsAboveGround { get; }

    /// <summary>What the numbering had to assume, which the user has to be able to see before it is written.</summary>
    public IReadOnlyList<string> Warnings { get; }

    public int? For(string? levelId) =>
        levelId is not null && ByLevelId.TryGetValue(levelId, out var number) ? number : (int?)null;

    public bool IsEmpty => ByLevelId.Count == 0;
}

/// <summary>
/// Numbers the model's levels into storeys: the lowest level at or above ground is 1F, the one above
/// it 2F, and the levels below ground count back from the ground as −1, −2 and so on. This is the
/// convention 防火檢討_所在樓層序 records (地上為正、地下為負).
/// </summary>
/// <remarks>
/// <para>
/// Ground is elevation zero, which is the project's own datum rather than 基地地面 — Revit has no
/// 基地地面 to ask. A project whose base point is not at ground level will number every storey
/// wrongly, so the assumption is always reported as a warning rather than left implicit; the
/// numbering is a proposal the user reviews, never something written behind their back.
/// </para>
/// <para>
/// Every level counts, including ones that are not storeys at all — a parapet, a top-of-steel, a
/// foundation datum. There is nothing in a Level that says which it is, and guessing from the name
/// would be worse than saying so: the count is reported alongside the numbering so an inflated
/// 地上層數 is visible before it reaches the model.
/// </para>
/// <para>
/// Levels sharing an elevation share a storey number, which is what a model with a structural and an
/// architectural level per floor wants.
/// </para>
/// </remarks>
public static class LevelFloorNumbering
{
    /// <summary>Levels closer than this are the same storey; 1 mm, well below any real floor-to-floor.</summary>
    public const double DefaultToleranceMeters = 0.001;

    public static FloorNumbering From(IEnumerable<BuildingLevel>? levels, double toleranceMeters = DefaultToleranceMeters)
    {
        if (toleranceMeters < 0) throw new ArgumentOutOfRangeException(nameof(toleranceMeters));

        var all = (levels ?? Array.Empty<BuildingLevel>()).Where(l => l is not null).ToList();
        if (all.Count == 0)
            return new FloorNumbering(new Dictionary<string, int>(StringComparer.Ordinal), 0,
                new[] { "模型中沒有樓層，無法判斷樓層序。" });

        // Distinct elevations, lowest first: one storey per elevation, not one per level.
        var storeys = new List<double>();
        foreach (var elevation in all.Select(l => l.ElevationMeters).OrderBy(x => x))
        {
            if (storeys.Count == 0 || Math.Abs(elevation - storeys[storeys.Count - 1]) > toleranceMeters)
                storeys.Add(elevation);
        }

        var above = storeys.Where(e => e > -toleranceMeters).ToList();
        var below = storeys.Where(e => e <= -toleranceMeters).OrderByDescending(x => x).ToList();

        var numberOf = new Dictionary<double, int>();
        for (var i = 0; i < above.Count; i++) numberOf[above[i]] = i + 1;
        for (var i = 0; i < below.Count; i++) numberOf[below[i]] = -(i + 1);

        var byLevel = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var level in all)
        {
            var storey = storeys.FirstOrDefault(e => Math.Abs(e - level.ElevationMeters) <= toleranceMeters);
            if (numberOf.TryGetValue(storey, out var number)) byLevel[level.LevelId] = number;
        }

        return new FloorNumbering(byLevel, above.Count, Warnings(all, above, below, toleranceMeters));
    }

    private static IEnumerable<string> Warnings(
        IReadOnlyList<BuildingLevel> all,
        IReadOnlyList<double> above,
        IReadOnlyList<double> below,
        double tolerance)
    {
        yield return "樓層序以高程 0 為地面：高程 0 以上由下往上編 1、2、3…，以下編 −1、−2…。" +
                     "若本案的基地地面不在高程 0，請自行調整。";

        if (above.Count == 0)
            yield return "沒有任何高程 0 以上的樓層，地上層數會是 0，第70條的層位無從計算。";

        yield return string.Format(
            CultureInfo.InvariantCulture,
            "依高程判定：地上 {0} 層、地下 {1} 層。屋突、女兒牆、結構基準面等非樓層的 Level 也會被算進去，請核對地上層數是否正確。",
            above.Count,
            below.Count);

        var shared = all.GroupBy(l => Math.Round(l.ElevationMeters / Math.Max(tolerance, 1e-9)))
            .Where(g => g.Count() > 1)
            .Select(g => string.Join("／", g.Select(l => l.Name)))
            .ToList();

        if (shared.Count > 0)
            yield return "高程相同的樓層視為同一層：" + string.Join("；", shared) + "。";
    }
}
