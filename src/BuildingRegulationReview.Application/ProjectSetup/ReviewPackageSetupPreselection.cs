using System;
using System.Collections.Generic;
using System.Linq;

namespace BuildingRegulationReview.Application.ProjectSetup;

/// <summary>One of the floor plans the setup window is offering, as the window knows it.</summary>
public sealed class ReviewPackageSetupFloorPlan
{
    public ReviewPackageSetupFloorPlan(string uniqueId, string name)
    {
        UniqueId = uniqueId;
        Name = name;
    }

    public string UniqueId { get; }
    public string Name { get; }
}

/// <summary>What one of the selected floor plans saved last time, with the name to say it by.</summary>
public sealed class ReviewPackageSetupSavedOptions
{
    public ReviewPackageSetupSavedOptions(string floorPlanName, ReviewPackageSetupSelection saved)
    {
        FloorPlanName = floorPlanName;
        Saved = saved ?? throw new ArgumentNullException(nameof(saved));
    }

    public string FloorPlanName { get; }
    public ReviewPackageSetupSelection Saved { get; }
}

public enum ReviewPackageSetupPreselectionKind
{
    /// <summary>None of the selected floor plans has anything saved.</summary>
    Nothing,

    /// <summary>One saved record, or several that say the same thing: safe to preselect.</summary>
    Agreed,

    /// <summary>Two or more saved records that disagree: preselecting one would overwrite the others.</summary>
    Conflict
}

/// <summary>
/// Whether the setup window may preselect saved options for the floor plans currently selected, and
/// which record it would use (驗證清單 C-13).
/// </summary>
/// <remarks>
/// <para>
/// The window applies one set of options to every selected floor plan, so preselecting the first
/// saved record it finds would write that record over all the others — the same loss C-13 is about,
/// only with another package's values instead of the defaults. Hence <see cref="Kind"/>: disagreeing
/// records are reported, never silently resolved.
/// </para>
/// <para>
/// <see cref="Key"/> exists because the window is asked to preselect far more often than the
/// selection actually changes: every time it regains focus it refreshes the area schemes, which
/// re-raises selection changes. Applying on every one of those would undo whatever the user had
/// picked by hand in between, so the window applies only when this key differs from the one its
/// controls already hold.
/// </para>
/// </remarks>
public sealed class ReviewPackageSetupPreselection
{
    private ReviewPackageSetupPreselection(
        ReviewPackageSetupPreselectionKind kind,
        IReadOnlyList<ReviewPackageSetupSavedOptions> saved,
        string key)
    {
        Kind = kind;
        Saved = saved;
        Key = key;
    }

    public ReviewPackageSetupPreselectionKind Kind { get; }

    /// <summary>The saved records among the selected floor plans, in the order they were selected.</summary>
    public IReadOnlyList<ReviewPackageSetupSavedOptions> Saved { get; }

    /// <summary>The record to preselect, or null unless <see cref="Kind"/> is Agreed.</summary>
    public ReviewPackageSetupSelection? Agreed =>
        Kind == ReviewPackageSetupPreselectionKind.Agreed ? Saved[0].Saved : null;

    /// <summary>
    /// Identifies what this answer was computed from: the area scheme plus the set of selected floor
    /// plans. Two calls with the same selection give the same key, whatever order the floor plans
    /// were clicked in, so a window that already applied this key can tell there is nothing new to
    /// apply.
    /// </summary>
    public string Key { get; }

    public static ReviewPackageSetupPreselection For(
        IEnumerable<ReviewPackageSetupFloorPlan> selectedFloorPlans,
        string? areaSchemeUniqueId,
        ReviewPackageSetupHistory history)
    {
        if (selectedFloorPlans is null) throw new ArgumentNullException(nameof(selectedFloorPlans));
        if (history is null) throw new ArgumentNullException(nameof(history));

        var floorPlans = selectedFloorPlans.Where(floorPlan => floorPlan is not null).ToList();
        var key = BuildKey(floorPlans, areaSchemeUniqueId);
        var saved = floorPlans
            .Select(floorPlan => new { floorPlan.Name, Saved = history.Find(floorPlan.UniqueId, areaSchemeUniqueId) })
            .Where(found => found.Saved is not null)
            .Select(found => new ReviewPackageSetupSavedOptions(found.Name, found.Saved!))
            .ToList();

        var kind = saved.Count == 0 ? ReviewPackageSetupPreselectionKind.Nothing
            : saved.All(other => SaysTheSame(saved[0].Saved, other.Saved)) ? ReviewPackageSetupPreselectionKind.Agreed
            : ReviewPackageSetupPreselectionKind.Conflict;
        return new ReviewPackageSetupPreselection(kind, saved, key);
    }

    /// <summary>
    /// Whether two saved records would put the same thing on the three controls. The package key
    /// itself is deliberately not compared: records for different floor plans always differ there,
    /// and that is not a disagreement about what to preselect.
    /// </summary>
    private static bool SaysTheSame(ReviewPackageSetupSelection left, ReviewPackageSetupSelection right) =>
        Same(left.AreaPlanTemplateUniqueId, right.AreaPlanTemplateUniqueId) &&
        Same(left.ScopeBoxUniqueId, right.ScopeBoxUniqueId) &&
        left.CopyCropSettings == right.CopyCropSettings;

    private static bool Same(string? left, string? right) =>
        string.Equals(left ?? string.Empty, right ?? string.Empty, StringComparison.Ordinal);

    private static string BuildKey(
        IReadOnlyList<ReviewPackageSetupFloorPlan> floorPlans,
        string? areaSchemeUniqueId)
    {
        // Sorted, so clicking the same two floor plans in the other order is still the same key.
        var ids = floorPlans.Select(floorPlan => floorPlan.UniqueId ?? string.Empty)
            .OrderBy(id => id, StringComparer.Ordinal);
        return (areaSchemeUniqueId ?? string.Empty) + "\u001f" + string.Join("\u001f", ids);
    }
}
