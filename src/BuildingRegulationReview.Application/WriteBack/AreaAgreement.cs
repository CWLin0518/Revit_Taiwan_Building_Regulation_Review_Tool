using System;
using System.Globalization;

namespace BuildingRegulationReview.Application.WriteBack;

/// <summary>How the draft's own arithmetic and Revit's measurement of the written Area compare.</summary>
public enum AreaAgreementKind
{
    /// <summary>The same room measured twice. Nothing to report.</summary>
    Agrees,

    /// <summary>Revit measured nothing: the Area never landed inside a closed ring.</summary>
    NotEnclosed,

    /// <summary>Both measured something, and the two numbers are too far apart.</summary>
    Differs,

    /// <summary>The draft computed nothing to compare against, so the question does not arise.</summary>
    NotComparable
}

/// <summary>
/// One Area's verdict, carrying the identity the log needs (spec 10.6: 所有區劃均可由 Package ID 與
/// Zone ID 追溯) alongside the two numbers that disagreed.
/// </summary>
public sealed class AreaAgreementFinding
{
    public AreaAgreementFinding(
        ManagedElementKey key,
        string? zoneName,
        double draftSquareMeters,
        double revitSquareMeters,
        AreaAgreementKind kind,
        string? message,
        string? elementUniqueId = null)
    {
        if (!Enum.IsDefined(typeof(AreaAgreementKind), kind)) throw new ArgumentOutOfRangeException(nameof(kind));

        Key = key;
        ZoneName = string.IsNullOrWhiteSpace(zoneName) ? null : zoneName!.Trim();
        DraftSquareMeters = draftSquareMeters;
        RevitSquareMeters = revitSquareMeters;
        Kind = kind;
        Message = string.IsNullOrWhiteSpace(message) ? null : message!.Trim();
        ElementUniqueId = string.IsNullOrWhiteSpace(elementUniqueId) ? null : elementUniqueId!.Trim();
    }

    public ManagedElementKey Key { get; }
    public Guid PackageId => Key.PackageId;
    public Guid ZoneId => Key.ZoneId;
    public string? ZoneName { get; }
    public double DraftSquareMeters { get; }
    public double RevitSquareMeters { get; }
    public AreaAgreementKind Kind { get; }

    /// <summary>What to put on the log; null exactly when the two agree or cannot be compared.</summary>
    public string? Message { get; }

    public string? ElementUniqueId { get; }

    /// <summary>
    /// True when this Area is a reason to refuse Ready (spec 10.6). A draft with nothing to compare
    /// is not: it says nothing about the boundary either way.
    /// </summary>
    public bool BlocksReady => Kind == AreaAgreementKind.NotEnclosed || Kind == AreaAgreementKind.Differs;

    public override string ToString() => Message ?? "面積相符。";
}

/// <summary>
/// Compares the area the drafts computed with the area Revit reports for the Area that was written
/// (spec 10.6: 面積與 Revit Area 的差異超過容許值時，禁止進入 Ready，並指出可能的邊界問題).
/// </summary>
/// <remarks>
/// The two numbers are computed from different things — the draft sums the solved faces, Revit
/// measures the enclosure the boundary lines actually formed — so a disagreement is nearly always a
/// boundary that did not close where the solver thought it did. Write-back records the verdict;
/// <see cref="ReviewPackages.ReviewPackageProgress"/> is what turns it into a refusal to advance.
/// </remarks>
public static class AreaAgreement
{
    /// <summary>One percent. Below this the two numbers are the same room measured twice.</summary>
    public const double DefaultRelativeTolerance = 0.01;

    /// <summary>
    /// Judges one Area. An Area that Revit reports as zero is called out separately: that is not a
    /// rounding difference, it is an Area that never landed inside a closed ring.
    /// </summary>
    public static AreaAgreementFinding Compare(
        ManagedElementKey key,
        string? zoneName,
        double draftSquareMeters,
        double revitSquareMeters,
        double relativeTolerance = DefaultRelativeTolerance,
        string? elementUniqueId = null)
    {
        if (relativeTolerance < 0) throw new ArgumentOutOfRangeException(nameof(relativeTolerance));

        var label = string.IsNullOrWhiteSpace(zoneName) ? "區劃" : "區劃「" + zoneName!.Trim() + "」";

        if (revitSquareMeters <= 0)
        {
            return new AreaAgreementFinding(
                key, zoneName, draftSquareMeters, revitSquareMeters, AreaAgreementKind.NotEnclosed,
                label + "的面積沒有落在封閉的邊界內，Revit 量到 0 m²，請檢查邊界線是否閉合。",
                elementUniqueId);
        }

        if (draftSquareMeters <= 0)
        {
            return new AreaAgreementFinding(
                key, zoneName, draftSquareMeters, revitSquareMeters, AreaAgreementKind.NotComparable, null, elementUniqueId);
        }

        var difference = Math.Abs(draftSquareMeters - revitSquareMeters);
        if (difference <= draftSquareMeters * relativeTolerance)
        {
            return new AreaAgreementFinding(
                key, zoneName, draftSquareMeters, revitSquareMeters, AreaAgreementKind.Agrees, null, elementUniqueId);
        }

        return new AreaAgreementFinding(
            key, zoneName, draftSquareMeters, revitSquareMeters, AreaAgreementKind.Differs,
            string.Format(
                CultureInfo.InvariantCulture,
                "{0}的草算面積 {1:0.##} m² 與 Revit 量到的 {2:0.##} m² 相差 {3:0.#}%，可能有邊界沒有閉合。",
                label,
                draftSquareMeters,
                revitSquareMeters,
                difference / draftSquareMeters * 100.0),
            elementUniqueId);
    }

    /// <summary>What to put on the log, or null when the two agree.</summary>
    public static string? Describe(
        string zoneName,
        double draftSquareMeters,
        double revitSquareMeters,
        double relativeTolerance = DefaultRelativeTolerance) =>
        Compare(
            new ManagedElementKey(PlaceholderPackageId, PlaceholderZoneId, ManagedElementKind.Area, 0, 0),
            zoneName,
            draftSquareMeters,
            revitSquareMeters,
            relativeTolerance).Message;

    // Describe() answers about the numbers alone, for a caller that has no key to give. The two
    // placeholders never leave this method: only the message escapes it.
    private static readonly Guid PlaceholderPackageId = new Guid("00000000-0000-0000-0000-0000000000a1");
    private static readonly Guid PlaceholderZoneId = new Guid("00000000-0000-0000-0000-0000000000a2");
}
