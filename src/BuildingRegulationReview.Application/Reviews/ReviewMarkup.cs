using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using BuildingRegulationReview.Application.Candidates;
using BuildingRegulationReview.Application.Checks;
using BuildingRegulationReview.Application.Diagnostics;
using BuildingRegulationReview.Application.WriteBack;
using BuildingRegulationReview.Domain.Common;
using BuildingRegulationReview.Domain.Geometry;
using BuildingRegulationReview.Domain.Reviews;

namespace BuildingRegulationReview.Application.Reviews;

/// <summary>
/// What a review mark is, which is also what family of marks a later run matches it against. A run
/// that plans no spandrel band must not take a missing band as licence to delete the notes — so each
/// kind is diffed among its own (see <see cref="ReviewMarkupDiff"/>).
/// </summary>
public enum ReviewMarkKind
{
    /// <summary>A red Filled Region over one enclosed Area of a failing 區劃 (spec 11.4 item 4).</summary>
    Zone,

    /// <summary>A text note at a 帷幕牆 junction, carrying what was measured there (帷幕牆規格 §7.1).</summary>
    JunctionNote,

    /// <summary>A red Filled Region over a 層間帶 in the elevation view (帷幕牆規格 §7.1).</summary>
    SpandrelBand
}

/// <summary>
/// The ownership mark of a review mark in the review view (spec 11.4 item 4: 標註元素須含 Package
/// ID、Run ID、Zone ID). The token carries all three; what a later run matches on is the
/// <see cref="Slot"/> — package, zone, part, kind and subject — so a new run takes over the mark the
/// last run drew for the same thing instead of drawing a second one (spec 13.2).
/// </summary>
/// <remarks>
/// A <see cref="ReviewMarkKind.Zone"/> mark keeps the five-token form the tool has always written, so
/// regions already in a model still parse and are taken over rather than duplicated; the 帷幕牆 marks
/// of 帷幕牆規格 §7.1 add the kind and the junction they belong to.
/// </remarks>
public readonly struct ReviewMarkKey : IEquatable<ReviewMarkKey>
{
    /// <summary>Distinct from the P2 prefixes, so a review mark is never read as a boundary element.</summary>
    public const string Prefix = "BCRRV";

    private const char Separator = '/';

    public ReviewMarkKey(Guid packageId, Guid runId, Guid zoneId, int partIndex)
        : this(packageId, runId, zoneId, partIndex, ReviewMarkKind.Zone, null)
    {
    }

    public ReviewMarkKey(Guid packageId, Guid runId, Guid zoneId, int partIndex, ReviewMarkKind kind, string? subject)
    {
        if (packageId == Guid.Empty) throw new ArgumentException("Package ID cannot be empty.", nameof(packageId));
        if (runId == Guid.Empty) throw new ArgumentException("Run ID cannot be empty.", nameof(runId));
        if (zoneId == Guid.Empty) throw new ArgumentException("Zone ID cannot be empty.", nameof(zoneId));
        if (partIndex < 0) throw new ArgumentOutOfRangeException(nameof(partIndex));
        if (!Enum.IsDefined(typeof(ReviewMarkKind), kind)) throw new ArgumentOutOfRangeException(nameof(kind));

        var trimmed = (subject ?? string.Empty).Trim();
        if (kind == ReviewMarkKind.Zone)
        {
            if (trimmed.Length > 0) throw new ArgumentException("A 區劃 mark is identified by its part alone.", nameof(subject));
        }
        else
        {
            if (trimmed.Length == 0) throw new ArgumentException("A 帷幕牆 mark has to name what it belongs to.", nameof(subject));
            if (trimmed.IndexOf(Separator) >= 0) throw new ArgumentException($"A subject cannot contain '{Separator}'.", nameof(subject));
        }

        PackageId = packageId;
        RunId = runId;
        ZoneId = zoneId;
        PartIndex = partIndex;
        Kind = kind;
        Subject = trimmed;
    }

    public Guid PackageId { get; }
    public Guid RunId { get; }
    public Guid ZoneId { get; }

    /// <summary>Which enclosed Area of the 區劃, in the order the candidate zone lists them.</summary>
    public int PartIndex { get; }

    public ReviewMarkKind Kind { get; }

    /// <summary>What the mark belongs to within the 區劃 — the 交接處代號 for a 帷幕牆 mark, empty for a 區劃 one.</summary>
    public string Subject { get; }

    /// <summary>What identifies the mark across runs: everything but the run.</summary>
    public string Slot => string.Join(Separator.ToString(),
        PackageId.ToString("N", CultureInfo.InvariantCulture),
        ZoneId.ToString("N", CultureInfo.InvariantCulture),
        PartIndex.ToString(CultureInfo.InvariantCulture),
        Kind.ToString(),
        Subject);

    public string ToToken()
    {
        var common = string.Join(Separator.ToString(),
            Prefix,
            PackageId.ToString("N", CultureInfo.InvariantCulture),
            RunId.ToString("N", CultureInfo.InvariantCulture),
            ZoneId.ToString("N", CultureInfo.InvariantCulture),
            PartIndex.ToString(CultureInfo.InvariantCulture));

        return Kind == ReviewMarkKind.Zone ? common : common + Separator + Kind + Separator + Subject;
    }

    /// <summary>The readable form the adapter also writes to the region's Comments, for a user looking at it in Revit.</summary>
    public string ToLabel()
    {
        var text = $"Package {PackageId:D} / Run {RunId:D} / Zone {ZoneId:D} / Part {PartIndex.ToString(CultureInfo.InvariantCulture)}";
        return Kind == ReviewMarkKind.Zone ? text : text + $" / {Kind} {Subject}";
    }

    public static bool TryParse(string? token, out ReviewMarkKey key)
    {
        key = default;
        if (string.IsNullOrWhiteSpace(token)) return false;

        var parts = token!.Trim().Split(Separator);
        if (parts.Length != 5 && parts.Length != 7) return false;
        if (!string.Equals(parts[0], Prefix, StringComparison.Ordinal)) return false;
        if (!Guid.TryParseExact(parts[1], "N", out var packageId) || packageId == Guid.Empty) return false;
        if (!Guid.TryParseExact(parts[2], "N", out var runId) || runId == Guid.Empty) return false;
        if (!Guid.TryParseExact(parts[3], "N", out var zoneId) || zoneId == Guid.Empty) return false;
        if (!int.TryParse(parts[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out var partIndex) || partIndex < 0) return false;

        var kind = ReviewMarkKind.Zone;
        var subject = (string?)null;
        if (parts.Length == 7)
        {
            if (!Enum.TryParse(parts[5], out kind) || !Enum.IsDefined(typeof(ReviewMarkKind), kind)) return false;
            if (kind == ReviewMarkKind.Zone || parts[6].Length == 0) return false;
            subject = parts[6];
        }

        key = new ReviewMarkKey(packageId, runId, zoneId, partIndex, kind, subject);
        return true;
    }

    public bool Equals(ReviewMarkKey other) =>
        PackageId == other.PackageId && RunId == other.RunId && ZoneId == other.ZoneId && PartIndex == other.PartIndex &&
        Kind == other.Kind && string.Equals(Subject, other.Subject, StringComparison.Ordinal);

    public override bool Equals(object? obj) => obj is ReviewMarkKey other && Equals(other);

    public override int GetHashCode()
    {
        unchecked
        {
            var hash = 17;
            hash = (hash * 31) + PackageId.GetHashCode();
            hash = (hash * 31) + RunId.GetHashCode();
            hash = (hash * 31) + ZoneId.GetHashCode();
            hash = (hash * 31) + PartIndex;
            hash = (hash * 31) + (int)Kind;
            hash = (hash * 31) + (Subject ?? string.Empty).GetHashCode();
            return hash;
        }
    }

    public static bool operator ==(ReviewMarkKey left, ReviewMarkKey right) => left.Equals(right);
    public static bool operator !=(ReviewMarkKey left, ReviewMarkKey right) => !left.Equals(right);

    public override string ToString() => ToToken();
}

/// <summary>A red Filled Region the current run says should be in the review view: one per enclosed Area of a failing 區劃.</summary>
public sealed class PlannedReviewRegion
{
    internal PlannedReviewRegion(ReviewMarkKey key, Guid resultId, string zoneName, IEnumerable<IReadOnlyList<Point2D>> loops)
    {
        Key = key;
        ResultId = resultId;
        ZoneName = zoneName;
        Loops = new ReadOnlyCollection<IReadOnlyList<Point2D>>(loops
            .Select(l => (IReadOnlyList<Point2D>)new ReadOnlyCollection<Point2D>(l.ToList())).ToList());
        Signature = ReviewMarkupPlan.RegionSignature(zoneName, Loops);
    }

    public ReviewMarkKey Key { get; }
    public Guid ResultId { get; }
    public string ZoneName { get; }

    /// <summary>The outer loop first, then its holes, in model feet — one Filled Region holds them all.</summary>
    public IReadOnlyList<IReadOnlyList<Point2D>> Loops { get; }

    /// <summary>What reaches the model apart from the mark. Same signature and same run: nothing to do.</summary>
    public string Signature { get; }

    public string Description => $"未符合區劃「{ZoneName}」紅色填滿區域（第 {Key.PartIndex + 1} 部分）";

    public override string ToString() => Description;
}

/// <summary>A model element the review view should show red: it failed at least one element check.</summary>
/// <remarks>
/// One element, one mark, however many results it failed — the element is either red or it is not, and
/// painting it twice would say nothing the first mark did not. What the results were is carried in
/// <see cref="ResultIds"/>, <see cref="CheckTypes"/> and, for 第79條之2, <see cref="ShaftRequirements"/>.
/// </remarks>
public sealed class PlannedElementOverride
{
    internal PlannedElementOverride(string elementUniqueId, IEnumerable<ReviewTableEntry> entries)
    {
        var list = entries.ToList();
        ElementUniqueId = elementUniqueId;
        ResultIds = new ReadOnlyCollection<Guid>(list.Select(e => e.ResultId).Distinct().ToList());
        CheckTypes = new ReadOnlyCollection<string>(list.Select(e => e.CheckType).Distinct(StringComparer.Ordinal).ToList());
        ShaftRequirements = new ReadOnlyCollection<string>(ShaftRequirementsOf(list));

        var first = list[0];
        var element = first.TypeName is null
            ? $"未符合{first.CategoryLabel} {elementUniqueId}"
            : $"未符合{first.CategoryLabel}「{first.TypeName}」 {elementUniqueId}";
        Description = ShaftRequirements.Count == 0
            ? element
            : element + $"（{string.Join("、", ShaftRequirements)}）";
    }

    public string ElementUniqueId { get; }
    public IReadOnlyList<Guid> ResultIds { get; }
    public IReadOnlyList<string> CheckTypes { get; }

    /// <summary>
    /// The 第79條之2第1項 requirements this element failed, in 條文 order (垂直區劃規格 §7.2). A 管道間
    /// 維修門 owes 防火時效 and 遮煙性能 at once, so it carries two results and is still painted once;
    /// without the requirements in <see cref="Description"/> the two 檢討表 rows and this one mark read
    /// as a duplicate rather than as two findings (§9 第 8 項). Empty for every other check — none of
    /// them splits one element into several subjects.
    /// </summary>
    public IReadOnlyList<string> ShaftRequirements { get; }

    public string Description { get; }

    public override string ToString() => Description;

    /// <summary>
    /// The requirement labels as the results stored them, deduplicated and in 條文 order. The label is
    /// read from the result, not from the current <see cref="VerticalCompartmentRequirements.Label"/>,
    /// for the same reason the 檢討表 row is (§7.1): an older run is shown in the wording it was
    /// written with.
    /// </summary>
    private static List<string> ShaftRequirementsOf(IReadOnlyList<ReviewTableEntry> entries) =>
        entries.Where(e => e.ShaftRequirementLabel is not null)
            .OrderBy(e => e.ShaftRequirement is VerticalCompartmentRequirement requirement
                ? VerticalCompartmentRequirements.Order(requirement)
                : VerticalCompartmentRequirements.All.Count)
            .ThenBy(e => e.ShaftRequirementLabel, StringComparer.Ordinal)
            .Select(e => e.ShaftRequirementLabel!)
            .Distinct(StringComparer.Ordinal)
            .ToList();
}

/// <summary>A failing result the run could not mark in the view, and why — never dropped without a word.</summary>
public sealed class SkippedReviewMark
{
    public SkippedReviewMark(Guid? resultId, string subject, string reason)
    {
        if (string.IsNullOrWhiteSpace(subject)) throw new ArgumentException("A skipped mark needs a subject.", nameof(subject));
        if (string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("A skipped mark needs a reason.", nameof(reason));

        ResultId = resultId;
        Subject = subject.Trim();
        Reason = reason.Trim();
    }

    public Guid? ResultId { get; }
    public string Subject { get; }
    public string Reason { get; }
    public string Text => $"未標示 {Subject}：{Reason}";

    public override string ToString() => Text;
}

/// <summary>
/// What the review view should show for one run (spec 11.4 item 4, 11.5 item 6, 11.6 item 4): a red
/// Filled Region over every 區劃 whose area fails, and a red By Element Override on every member and
/// opening that fails. Pure data: the Revit adapter draws it, the diff decides what that means for
/// what is already there.
/// </summary>
/// <remarks>
/// The plan reads the <see cref="ReviewRun.EffectiveStatus"/>, so a failure a reviewer has overridden
/// is not painted red, and a pass a reviewer has overridden to Fail is. A stale result is never
/// painted: showing what an out-of-date run said as if it were the model's current state is exactly
/// what spec 13.1 forbids; it is listed as skipped instead.
/// </remarks>
public sealed class ReviewMarkupPlan
{
    private const double SignatureQuantumFeet = 1e-4;

    private ReviewMarkupPlan(
        Guid packageId,
        Guid runId,
        IEnumerable<PlannedReviewRegion> regions,
        IEnumerable<PlannedElementOverride> overrides,
        IEnumerable<PlannedReviewNote> notes,
        IEnumerable<PlannedSpandrelBand> bands,
        IEnumerable<SkippedReviewMark> skipped,
        IReadOnlyDictionary<Guid, string> numbers)
    {
        PackageId = packageId;
        RunId = runId;
        Regions = new ReadOnlyCollection<PlannedReviewRegion>(regions.ToList());
        Overrides = new ReadOnlyCollection<PlannedElementOverride>(overrides.ToList());
        Notes = new ReadOnlyCollection<PlannedReviewNote>(notes.ToList());
        Bands = new ReadOnlyCollection<PlannedSpandrelBand>(bands.ToList());
        Skipped = new ReadOnlyCollection<SkippedReviewMark>(skipped.ToList());
        Numbers = numbers;
    }

    public Guid PackageId { get; }
    public Guid RunId { get; }
    public IReadOnlyList<PlannedReviewRegion> Regions { get; }
    public IReadOnlyList<PlannedElementOverride> Overrides { get; }

    /// <summary>The 帷幕牆 annotations of 帷幕牆規格 §7.1, in junction order.</summary>
    public IReadOnlyList<PlannedReviewNote> Notes { get; }

    /// <summary>The 層間帶 regions of 帷幕牆規格 §7.1 (CW-V), in junction order.</summary>
    public IReadOnlyList<PlannedSpandrelBand> Bands { get; }

    public IReadOnlyList<SkippedReviewMark> Skipped { get; }

    /// <summary>
    /// The drawing number of every 未符合 帷幕牆交接 in the run, by result ID
    /// (<see cref="CurtainWallMarkNumbers"/>). It covers the junctions this plan could not mark as well
    /// as the ones it could, so a skipped one can still be named by the number the 檢討表 shows.
    /// </summary>
    public IReadOnlyDictionary<Guid, string> Numbers { get; }

    /// <summary>
    /// Builds the plan from the run's table and the zones as the candidate set resolved them — the same
    /// zones the run was computed from, which is what the geometry of the red regions has to follow.
    /// </summary>
    public static Result<ReviewMarkupPlan> Build(ReviewTable table, IEnumerable<CandidateZone> zones)
    {
        if (table is null) throw new ArgumentNullException(nameof(table));
        if (zones is null) throw new ArgumentNullException(nameof(zones));

        if (table.Run.State != ReviewRunState.Completed)
            return Result.Failure<ReviewMarkupPlan>(new Error(ReviewErrorCode.ReviewMarkRefused,
                $"檢討紀錄 {table.RunId:D} 的狀態是 {table.Run.State}，只有完成的檢討可以標示在檢討視圖。",
                "Only a completed review run can be marked in the review view."));

        var numbers = CurtainWallMarkNumbers.Assign(table);
        var byZone = zones.GroupBy(z => z.ZoneIdText, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var regions = new List<PlannedReviewRegion>();
        var notes = new List<PlannedReviewNote>();
        var bands = new List<PlannedSpandrelBand>();
        var skipped = new List<SkippedReviewMark>();
        var failing = new List<(string ElementUniqueId, ReviewTableEntry Entry)>();

        foreach (var entry in table.Entries.Where(e => e.EffectiveStatus == ReviewStatus.Fail))
        {
            var isArea = string.Equals(entry.CheckType, ReviewCheckTypes.CompartmentArea, StringComparison.Ordinal);
            if (entry.IsStale)
            {
                skipped.Add(new SkippedReviewMark(entry.ResultId, Subject(entry, isArea, numbers),
                    "結果已過期（模型或規則已變更），請重新檢討後再標示"));
                continue;
            }

            if (isArea)
            {
                PlanRegions(table, entry, byZone, regions, skipped, numbers);
                continue;
            }

            if (entry.IsLinked)
            {
                skipped.Add(new SkippedReviewMark(entry.ResultId, Subject(entry, false, numbers),
                    "元素位於連結模型，主模型的檢討視圖無法逐元素覆寫，請在連結模型中確認"));
                continue;
            }

            if (entry.JunctionKind is CurtainWallJunctionKind junctionKind)
            {
                PlanJunction(table, entry, junctionKind, byZone, notes, bands, failing, skipped, numbers);
                continue;
            }

            // Everything else — 構件, 門窗 and the 防火設備 of 第79條之2 — is marked by painting the
            // element itself red. A 垂直區劃 failure is a device whose performance falls short, not a
            // place in the plan, so it gets no annotation and no drawing number: there is nothing to
            // stand a note at and nothing for a number to appear on (垂直區劃規格 §7.2, the same reason
            // CW-O carries neither). What it does need is the requirement in the line, which
            // PlannedElementOverride writes.
            foreach (var subject in entry.LocateUniqueIds)
                failing.Add((subject, entry));
        }

        var overrides = failing
            .GroupBy(x => x.ElementUniqueId, StringComparer.Ordinal)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => new PlannedElementOverride(g.Key, g.Select(x => x.Entry)));

        return Result.Success(new ReviewMarkupPlan(table.PackageId, table.RunId,
            regions.OrderBy(r => r.Key.Slot, StringComparer.Ordinal), overrides,
            notes.OrderBy(n => n.Key.Slot, StringComparer.Ordinal),
            bands.OrderBy(b => b.Key.Slot, StringComparer.Ordinal), skipped, numbers));
    }

    internal static string RegionSignature(string zoneName, IReadOnlyList<IReadOnlyList<Point2D>> loops) =>
        "region|" + zoneName + "|" + string.Join("|", loops.Select(loop =>
            string.Join(";", loop.Select(p => PlannedElementSignature.ForPoint(p, SignatureQuantumFeet)))));

    private static void PlanRegions(
        ReviewTable table,
        ReviewTableEntry entry,
        IReadOnlyDictionary<string, CandidateZone> zones,
        List<PlannedReviewRegion> regions,
        List<SkippedReviewMark> skipped,
        IReadOnlyDictionary<Guid, string> numbers)
    {
        if (entry.ZoneId is null || !zones.TryGetValue(entry.ZoneId, out var zone))
        {
            skipped.Add(new SkippedReviewMark(entry.ResultId, Subject(entry, true, numbers),
                "找不到這個區劃目前的範圍，請重新讀取區劃後再標示"));
            return;
        }

        var drawn = 0;
        for (var index = 0; index < zone.Parts.Count; index++)
        {
            var part = zone.Parts[index];
            if (!part.IsEnclosed) continue;
            regions.Add(new PlannedReviewRegion(
                new ReviewMarkKey(table.PackageId, table.RunId, zone.ZoneId, index), entry.ResultId, zone.Name,
                part.Observation.BoundaryLoops));
            drawn++;
        }

        if (drawn == 0)
            skipped.Add(new SkippedReviewMark(entry.ResultId, Subject(entry, true, numbers),
                "區劃沒有封閉的面積，無法建立填滿區域"));
    }

    /// <summary>
    /// 帷幕牆規格 §7.1: CW-H paints the panels at the junction red and annotates the intersection,
    /// CW-V draws the 層間帶 red in the elevation and annotates the height, CW-O paints its panels red.
    /// <para>
    /// A 帷幕牆 result is about the curtain wall, its host and its panels, so it cannot be painted the
    /// way a member result is — painting <see cref="ReviewTableEntry.LocateUniqueIds"/> would turn the
    /// whole curtain wall and the 區劃牆 red as well. The panels the junction actually covers are what
    /// the evidence recorded, and that is what is painted.
    /// </para>
    /// </summary>
    private static void PlanJunction(
        ReviewTable table,
        ReviewTableEntry entry,
        CurtainWallJunctionKind kind,
        IReadOnlyDictionary<string, CandidateZone> zones,
        List<PlannedReviewNote> notes,
        List<PlannedSpandrelBand> bands,
        List<(string ElementUniqueId, ReviewTableEntry Entry)> failing,
        List<SkippedReviewMark> skipped,
        IReadOnlyDictionary<Guid, string> numbers)
    {
        var evidence = entry.Result.Evidence;
        var subject = Subject(entry, false, numbers);

        // CW-V is marked by its 層間帶, not by its panels: a spandrel's panels are shown in the
        // elevation the band is drawn in, and painting them in the plan would say nothing (§7.1).
        if (kind != CurtainWallJunctionKind.FloorToCurtainWall)
        {
            var panels = CurtainWallReviewMarks.Panels(evidence);
            if (panels.Count == 0)
                skipped.Add(new SkippedReviewMark(entry.ResultId, subject, "結果沒有記錄交接處的帷幕嵌板，無法標示紅色覆寫"));
            foreach (var panel in panels) failing.Add((panel, entry));
        }

        if (kind == CurtainWallJunctionKind.CurtainPanelOther) return;

        var junctionId = CurtainWallReviewMarks.JunctionId(evidence);
        if (junctionId is null)
        {
            skipped.Add(new SkippedReviewMark(entry.ResultId, subject, "結果沒有記錄交接處代號，無法標示"));
            return;
        }

        if (entry.ZoneId is null || !zones.TryGetValue(entry.ZoneId, out var zone))
        {
            skipped.Add(new SkippedReviewMark(entry.ResultId, subject, "找不到這個區劃目前的範圍，請重新讀取區劃後再標示"));
            return;
        }

        var placement = CurtainWallReviewMarks.Placement(evidence);
        if (placement is null)
        {
            skipped.Add(new SkippedReviewMark(entry.ResultId, subject, "結果沒有記錄交接處的位置，請重新檢討後再標示"));
            return;
        }

        // Every junction that gets this far was numbered by CurtainWallMarkNumbers, which reads the same
        // table and the same 未符合 rows; a junction with no number would be a mark nothing can be
        // matched against, so it is said out loud rather than drawn anonymously.
        var number = CurtainWallMarkNumbers.Of(numbers, entry.ResultId);
        if (number is null)
        {
            skipped.Add(new SkippedReviewMark(entry.ResultId, subject, "無法指派檢討圖號，無法標示"));
            return;
        }

        if (kind == CurtainWallJunctionKind.FloorToCurtainWall)
        {
            if (placement.IsPoint)
            {
                skipped.Add(new SkippedReviewMark(entry.ResultId, subject, "層間帶沒有長度，無法建立填滿區域"));
                return;
            }

            bands.Add(new PlannedSpandrelBand(
                new ReviewMarkKey(table.PackageId, table.RunId, zone.ZoneId, 0, ReviewMarkKind.SpandrelBand, junctionId),
                entry.ResultId, junctionId, JunctionCurtainWall(entry, evidence), placement, number, zone.Name));
        }

        notes.Add(new PlannedReviewNote(
            new ReviewMarkKey(table.PackageId, table.RunId, zone.ZoneId, 0, ReviewMarkKind.JunctionNote, junctionId),
            entry.ResultId, junctionId, kind, placement, number,
            kind == CurtainWallJunctionKind.WallToCurtainWall
                ? CurtainWallReviewMarks.HorizontalNoteText(evidence, number)
                : CurtainWallReviewMarks.SpandrelNoteText(evidence, number),
            zone.Name));
    }

    /// <summary>
    /// The curtain wall a band lies on: the junction's own field, and failing that the first subject,
    /// which is where <see cref="CurtainWallJunction.SubjectUniqueIds"/> puts it.
    /// </summary>
    private static string JunctionCurtainWall(ReviewTableEntry entry, ReviewEvidence evidence)
    {
        var value = evidence.Find("junction.curtainWallUniqueId");
        return value is not null && value.Kind == ReviewValueKind.Text && value.Text.Length > 0
            ? value.Text
            : entry.LocateUniqueIds.FirstOrDefault() ?? string.Empty;
    }

    /// <summary>
    /// What a skipped mark is about. A 帷幕牆交接 is named by its drawing number first: the 檢討表 shows
    /// the same number, so "未標示 CW-V-02 …" names a row the reader can go and look at. A 第79條之2
    /// subject is named by its requirement for the same reason the mark is (§7.2): one 維修門 is skipped
    /// once per requirement, and two lines naming only the door would read as the tool saying it twice.
    /// </summary>
    private static string Subject(ReviewTableEntry entry, bool isArea, IReadOnlyDictionary<Guid, string> numbers)
    {
        if (isArea) return $"區劃「{entry.ZoneName ?? entry.ZoneId ?? "?"}」";

        var number = CurtainWallMarkNumbers.Of(numbers, entry.ResultId);
        var subject = $"{entry.CategoryLabel} {string.Join(",", entry.LocateUniqueIds)}";
        if (entry.ShaftRequirementLabel is string requirement) subject += $"（{requirement}）";
        return number is null ? subject : number + " " + subject;
    }
}

/// <summary>A red region already in the review view, as the adapter read its mark.</summary>
public sealed class ExistingReviewMark
{
    public ExistingReviewMark(string elementUniqueId, string keyToken, string signature)
    {
        if (string.IsNullOrWhiteSpace(elementUniqueId)) throw new ArgumentException("An element UniqueId is required.", nameof(elementUniqueId));

        ElementUniqueId = elementUniqueId.Trim();
        KeyToken = (keyToken ?? string.Empty).Trim();
        Signature = (signature ?? string.Empty).Trim();
        HasKey = ReviewMarkKey.TryParse(KeyToken, out var key);
        Key = key;
    }

    public string ElementUniqueId { get; }
    public string KeyToken { get; }
    public string Signature { get; }
    public bool HasKey { get; }
    public ReviewMarkKey Key { get; }

    public bool BelongsTo(Guid packageId) => HasKey && Key.PackageId == packageId;
}

/// <summary>
/// The view state the tool changed on one element, and what it changed it to (spec 11.5 item 6: 保存原
/// 視圖狀態與本工具覆寫的元素集合). The states are opaque snapshots the adapter writes and reads; the
/// Application only needs to compare them.
/// </summary>
public sealed class RecordedElementOverride
{
    public RecordedElementOverride(string elementUniqueId, Guid runId, string originalState, string appliedState)
    {
        if (string.IsNullOrWhiteSpace(elementUniqueId)) throw new ArgumentException("An element UniqueId is required.", nameof(elementUniqueId));
        if (runId == Guid.Empty) throw new ArgumentException("Run ID cannot be empty.", nameof(runId));

        ElementUniqueId = elementUniqueId.Trim();
        RunId = runId;
        OriginalState = originalState ?? string.Empty;
        AppliedState = appliedState ?? string.Empty;
    }

    public string ElementUniqueId { get; }

    /// <summary>The run that last painted the element.</summary>
    public Guid RunId { get; }

    /// <summary>The element's override in the view before the tool first touched it.</summary>
    public string OriginalState { get; }

    /// <summary>What the tool set; if the element no longer shows this, somebody changed it since.</summary>
    public string AppliedState { get; }

    public RecordedElementOverride ForRun(Guid runId, string appliedState) =>
        new RecordedElementOverride(ElementUniqueId, runId, OriginalState, appliedState);
}

public enum ReviewMarkAction
{
    /// <summary>A region to draw, or an element to paint for the first time.</summary>
    Create,

    /// <summary>A region whose outline or run changed, or an element already painted by an earlier run.</summary>
    Update,

    /// <summary>Already exactly as planned, by this run.</summary>
    Unchanged,

    /// <summary>A region of this package no longer needed, or an element to give its original look back.</summary>
    Remove
}

/// <summary>What one run does to one mark already in the view: the planned mark, what was there, and why.</summary>
public class ReviewMarkChange<TPlanned>
    where TPlanned : class
{
    internal ReviewMarkChange(ReviewMarkAction action, TPlanned? planned, ExistingReviewMark? existing, string reason)
    {
        Action = action;
        Planned = planned;
        Existing = existing;
        Reason = reason;
    }

    public ReviewMarkAction Action { get; }
    public TPlanned? Planned { get; }
    public ExistingReviewMark? Existing { get; }
    public string Reason { get; }
}

public sealed class ReviewRegionChange : ReviewMarkChange<PlannedReviewRegion>
{
    internal ReviewRegionChange(ReviewMarkAction action, PlannedReviewRegion? planned, ExistingReviewMark? existing, string reason)
        : base(action, planned, existing, reason)
    {
    }
}

public sealed class ReviewOverrideChange
{
    internal ReviewOverrideChange(ReviewMarkAction action, string elementUniqueId, PlannedElementOverride? planned, RecordedElementOverride? recorded, string reason)
    {
        Action = action;
        ElementUniqueId = elementUniqueId;
        Planned = planned;
        Recorded = recorded;
        Reason = reason;
    }

    public ReviewMarkAction Action { get; }
    public string ElementUniqueId { get; }
    public PlannedElementOverride? Planned { get; }
    public RecordedElementOverride? Recorded { get; }
    public string Reason { get; }
}

/// <summary>
/// What marking one run in the review view will do to what is already there (spec 13.2): update the
/// package's own regions, draw the missing ones, delete those no longer needed; paint the failing
/// elements, and give every element the tool painted before but that no longer fails its original look.
/// </summary>
/// <remarks>
/// Only the current package's marks and the elements this tool recorded are ever touched. A region of
/// another package, a region whose mark does not parse, and an override the user set on an element the
/// tool never recorded are all outside the diff — that is what 只更新目前 Run 管理的元素 means in a view
/// the user can still draw in.
/// </remarks>
public sealed class ReviewMarkupDiff
{
    private ReviewMarkupDiff(
        ReviewMarkupPlan plan,
        IEnumerable<ReviewRegionChange> regions,
        IEnumerable<ReviewOverrideChange> overrides,
        IEnumerable<ReviewMarkChange<PlannedReviewNote>> notes,
        IEnumerable<ReviewMarkChange<PlannedSpandrelBand>> bands,
        int foreignMarks)
    {
        Plan = plan;
        Regions = new ReadOnlyCollection<ReviewRegionChange>(regions.ToList());
        Overrides = new ReadOnlyCollection<ReviewOverrideChange>(overrides.ToList());
        Notes = new ReadOnlyCollection<ReviewMarkChange<PlannedReviewNote>>(notes.ToList());
        Bands = new ReadOnlyCollection<ReviewMarkChange<PlannedSpandrelBand>>(bands.ToList());
        ForeignMarks = foreignMarks;
    }

    public ReviewMarkupPlan Plan { get; }
    public IReadOnlyList<ReviewRegionChange> Regions { get; }
    public IReadOnlyList<ReviewOverrideChange> Overrides { get; }

    /// <summary>The 帷幕牆 annotations of 帷幕牆規格 §7.1.</summary>
    public IReadOnlyList<ReviewMarkChange<PlannedReviewNote>> Notes { get; }

    /// <summary>The 層間帶 regions of 帷幕牆規格 §7.1 (CW-V).</summary>
    public IReadOnlyList<ReviewMarkChange<PlannedSpandrelBand>> Bands { get; }

    /// <summary>Marks in the view that are not this package's and were left alone.</summary>
    public int ForeignMarks { get; }

    public int Count(ReviewMarkAction action) =>
        Regions.Count(r => r.Action == action) + Overrides.Count(o => o.Action == action) +
        Notes.Count(n => n.Action == action) + Bands.Count(b => b.Action == action);

    public bool HasChanges =>
        Regions.Any(r => r.Action != ReviewMarkAction.Unchanged) || Overrides.Any(o => o.Action != ReviewMarkAction.Unchanged) ||
        Notes.Any(n => n.Action != ReviewMarkAction.Unchanged) || Bands.Any(b => b.Action != ReviewMarkAction.Unchanged);

    /// <summary>The scope summary spec 15 asks for before any automatic change.</summary>
    public string Summary =>
        $"檢討視圖標示：新增 {Count(ReviewMarkAction.Create)}、更新 {Count(ReviewMarkAction.Update)}、" +
        $"移除 {Count(ReviewMarkAction.Remove)}、不變 {Count(ReviewMarkAction.Unchanged)}、略過 {Plan.Skipped.Count}";

    public static ReviewMarkupDiff Compute(
        ReviewMarkupPlan plan,
        IEnumerable<ExistingReviewMark> existingMarks,
        IEnumerable<RecordedElementOverride> recordedOverrides)
    {
        if (plan is null) throw new ArgumentNullException(nameof(plan));
        if (existingMarks is null) throw new ArgumentNullException(nameof(existingMarks));
        if (recordedOverrides is null) throw new ArgumentNullException(nameof(recordedOverrides));

        var marks = existingMarks.ToList();
        var ours = marks.Where(m => m.BelongsTo(plan.PackageId)).ToList();
        var foreign = marks.Count - ours.Count;

        var regionChanges = Match(plan.Regions, ours, ReviewMarkKind.Zone, plan.RunId, r => r.Key, r => r.Signature,
                "未符合區劃尚無標示", "區劃範圍已變更", "區劃已不再未符合")
            .Select(c => new ReviewRegionChange(c.Action, c.Planned, c.Existing, c.Reason))
            .ToList();

        var noteChanges = Match(plan.Notes, ours, ReviewMarkKind.JunctionNote, plan.RunId, n => n.Key, n => n.Signature,
            "未符合交接處尚無標註", "實測值或交接位置已變更", "交接處已不再未符合");

        var bandChanges = Match(plan.Bands, ours, ReviewMarkKind.SpandrelBand, plan.RunId, b => b.Key, b => b.Signature,
            "未符合層間帶尚無標示", "層間帶範圍已變更", "層間帶已不再未符合");

        var overrideChanges = new List<ReviewOverrideChange>();
        var recorded = recordedOverrides
            .GroupBy(r => r.ElementUniqueId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        foreach (var paint in plan.Overrides)
        {
            if (!recorded.TryGetValue(paint.ElementUniqueId, out var record))
                overrideChanges.Add(new ReviewOverrideChange(ReviewMarkAction.Create, paint.ElementUniqueId, paint, null, "未符合元素尚未標示"));
            else if (record.RunId == plan.RunId)
                overrideChanges.Add(new ReviewOverrideChange(ReviewMarkAction.Unchanged, paint.ElementUniqueId, paint, record, "已由本次檢討標示"));
            else
                overrideChanges.Add(new ReviewOverrideChange(ReviewMarkAction.Update, paint.ElementUniqueId, paint, record, "沿用前次檢討的標示，改為本次檢討"));
        }

        var painted = new HashSet<string>(plan.Overrides.Select(o => o.ElementUniqueId), StringComparer.Ordinal);
        foreach (var record in recorded.Values.Where(r => !painted.Contains(r.ElementUniqueId)).OrderBy(r => r.ElementUniqueId, StringComparer.Ordinal))
            overrideChanges.Add(new ReviewOverrideChange(ReviewMarkAction.Remove, record.ElementUniqueId, null, record, "元素已不再未符合，恢復原顯示"));

        return new ReviewMarkupDiff(plan, regionChanges, overrideChanges, noteChanges, bandChanges, foreign);
    }

    /// <summary>
    /// One family of marks against what the view already holds of that family. Matching is per kind:
    /// a run that plans no 層間帶 has said nothing about the 區劃 regions, so it must not read their
    /// slots as marks it no longer needs.
    /// </summary>
    private static List<ReviewMarkChange<TPlanned>> Match<TPlanned>(
        IReadOnlyList<TPlanned> planned,
        IReadOnlyList<ExistingReviewMark> ours,
        ReviewMarkKind kind,
        Guid runId,
        Func<TPlanned, ReviewMarkKey> key,
        Func<TPlanned, string> signature,
        string createReason,
        string changedReason,
        string staleReason)
        where TPlanned : class
    {
        var changes = new List<ReviewMarkChange<TPlanned>>();
        var bySlot = ours.Where(m => m.Key.Kind == kind)
            .GroupBy(m => m.Key.Slot, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.OrderBy(m => m.ElementUniqueId, StringComparer.Ordinal).ToList(), StringComparer.Ordinal);
        var wanted = new HashSet<string>(StringComparer.Ordinal);

        foreach (var mark in planned)
        {
            var slot = key(mark).Slot;
            wanted.Add(slot);
            if (!bySlot.TryGetValue(slot, out var existing))
            {
                changes.Add(new ReviewMarkChange<TPlanned>(ReviewMarkAction.Create, mark, null, createReason));
                continue;
            }

            var keep = existing[0];
            if (keep.Key.RunId == runId && string.Equals(keep.Signature, signature(mark), StringComparison.Ordinal))
                changes.Add(new ReviewMarkChange<TPlanned>(ReviewMarkAction.Unchanged, mark, keep, "與本次檢討相同"));
            else
                changes.Add(new ReviewMarkChange<TPlanned>(ReviewMarkAction.Update, mark, keep,
                    keep.Key.RunId != runId ? "沿用前次檢討的標示，改為本次檢討" : changedReason));

            // Two marks in one slot can only come from a copy or an interrupted run; one is enough.
            foreach (var duplicate in existing.Skip(1))
                changes.Add(new ReviewMarkChange<TPlanned>(ReviewMarkAction.Remove, null, duplicate, "重複的標示"));
        }

        foreach (var pair in bySlot.Where(p => !wanted.Contains(p.Key)).OrderBy(p => p.Key, StringComparer.Ordinal))
            foreach (var stale in pair.Value)
                changes.Add(new ReviewMarkChange<TPlanned>(ReviewMarkAction.Remove, null, stale, staleReason));

        return changes;
    }
}

/// <summary>What to do with an element the tool painted once, when it is time to give it back.</summary>
public enum OverrideRestoreDecision
{
    /// <summary>The element still shows what the tool set: put the original back.</summary>
    RestoreOriginal,

    /// <summary>Somebody changed it since; their change is kept and the record is dropped.</summary>
    KeepUserChange,

    /// <summary>The element is not in the model any more; only the record goes.</summary>
    ForgetMissing
}

public static class ReviewOverrideRestore
{
    /// <summary>
    /// Decided on the element as it is at the moment of restoring, not when the diff was built: the
    /// view is live and the user may have edited it in between.
    /// </summary>
    public static OverrideRestoreDecision Decide(RecordedElementOverride record, string? currentState)
    {
        if (record is null) throw new ArgumentNullException(nameof(record));
        if (currentState is null) return OverrideRestoreDecision.ForgetMissing;
        return string.Equals(currentState, record.AppliedState, StringComparison.Ordinal)
            ? OverrideRestoreDecision.RestoreOriginal
            : OverrideRestoreDecision.KeepUserChange;
    }
}

/// <summary>One line of what marking the view did.</summary>
public sealed class ReviewMarkupItem
{
    public ReviewMarkupItem(ApplyOutcome outcome, string description, string? elementUniqueId = null, string? message = null)
    {
        if (!Enum.IsDefined(typeof(ApplyOutcome), outcome)) throw new ArgumentOutOfRangeException(nameof(outcome));
        if (string.IsNullOrWhiteSpace(description)) throw new ArgumentException("A result line needs a description.", nameof(description));

        Outcome = outcome;
        Description = description.Trim();
        ElementUniqueId = string.IsNullOrWhiteSpace(elementUniqueId) ? null : elementUniqueId!.Trim();
        Message = string.IsNullOrWhiteSpace(message) ? null : message!.Trim();
    }

    public ApplyOutcome Outcome { get; }
    public string Description { get; }
    public string? ElementUniqueId { get; }
    public string? Message { get; }

    public string Text => Message is null
        ? ApplyResultItem.OutcomeText(Outcome) + " " + Description
        : ApplyResultItem.OutcomeText(Outcome) + " " + Description + "：" + Message;

    public override string ToString() => Text;
}

/// <summary>
/// What marking the view did (spec 15: 完成後提供新增／更新／刪除／略過數量). Unchanged lines are not
/// listed; the plan's skipped results are counted as skipped.
/// </summary>
public sealed class ReviewMarkupResult
{
    public ReviewMarkupResult(Guid packageId, Guid runId, string? viewUniqueId, IEnumerable<ReviewMarkupItem> items, string? fatalError = null)
    {
        PackageId = packageId;
        RunId = runId;
        ViewUniqueId = string.IsNullOrWhiteSpace(viewUniqueId) ? null : viewUniqueId!.Trim();
        Items = new ReadOnlyCollection<ReviewMarkupItem>((items ?? throw new ArgumentNullException(nameof(items))).ToList());
        FatalError = string.IsNullOrWhiteSpace(fatalError) ? null : fatalError!.Trim();
    }

    public Guid PackageId { get; }
    public Guid RunId { get; }
    public string? ViewUniqueId { get; }
    public IReadOnlyList<ReviewMarkupItem> Items { get; }

    /// <summary>Set when the whole marking was rolled back; the view is then exactly as it was.</summary>
    public string? FatalError { get; }

    public bool IsRolledBack => FatalError is not null;

    public int Count(ApplyOutcome outcome) => Items.Count(i => i.Outcome == outcome);

    public string Summary => IsRolledBack
        ? "檢討視圖標示已整批復原：" + FatalError
        : $"檢討視圖標示完成：新增 {Count(ApplyOutcome.Created)}、更新 {Count(ApplyOutcome.Updated)}、" +
          $"刪除 {Count(ApplyOutcome.Deleted)}、略過 {Count(ApplyOutcome.Skipped)}、失敗 {Count(ApplyOutcome.Failed)}";
}
