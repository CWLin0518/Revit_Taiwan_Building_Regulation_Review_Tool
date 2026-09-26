using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using BuildingRegulationReview.Application.Checks;
using BuildingRegulationReview.Application.WriteBack;
using BuildingRegulationReview.Domain.Reviews;

namespace BuildingRegulationReview.Application.Reviews;

/// <summary>
/// A text note the review view should carry at a 帷幕牆 junction (帷幕牆規格 §7.1: CW-H 於交點標註實測
/// <c>continuousFireRatedLength</c> 與 <c>projectionDepth</c>; CW-V 標註實測
/// <c>continuousFireRatedHeight</c>). The text is what the run measured, written out in millimetres —
/// the unit a person reads off a drawing — and the placement is where the junction is.
/// </summary>
public sealed class PlannedReviewNote
{
    internal PlannedReviewNote(
        ReviewMarkKey key,
        Guid resultId,
        string junctionId,
        CurtainWallJunctionKind junctionKind,
        CurtainWallJunctionPlacement placement,
        string number,
        string text,
        string zoneName)
    {
        Key = key;
        ResultId = resultId;
        JunctionId = junctionId;
        JunctionKind = junctionKind;
        Placement = placement;
        Number = number;
        Text = text;
        ZoneName = zoneName;
        Signature = CurtainWallReviewMarks.NoteSignature(text, placement);
    }

    public ReviewMarkKey Key { get; }
    public Guid ResultId { get; }

    /// <summary>
    /// The drawing number this 未符合 is referred to by (<c>CW-H-01</c>, <c>CW-V-01</c>). It opens
    /// <see cref="Text"/>, so the note on the plan and the row of the 檢討表 read the same.
    /// </summary>
    public string Number { get; }

    /// <summary>The junction this note is about; the same ID a later run keys its mark on.</summary>
    public string JunctionId { get; }

    public CurtainWallJunctionKind JunctionKind { get; }

    /// <summary>Where the note goes: the intersection for CW-H, the 層間帶 for CW-V.</summary>
    public CurtainWallJunctionPlacement Placement { get; }

    /// <summary>What the note says.</summary>
    public string Text { get; }

    public string ZoneName { get; }

    /// <summary>What reaches the model apart from the mark. Same signature and same run: nothing to do.</summary>
    public string Signature { get; }

    public string Description =>
        $"{Number} 未符合{CurtainWallJunctionKinds.Label(JunctionKind)}標註（區劃「{ZoneName}」，{JunctionId}）";

    public override string ToString() => Description;
}

/// <summary>
/// A red Filled Region the review view's elevation or section should show over a 層間帶 that failed
/// (帷幕牆規格 §7.1, CW-V). Unlike a 區劃 region this is not a plan outline: it is a stretch of one
/// curtain wall's plane, so the adapter has to project it into the view it draws in.
/// </summary>
public sealed class PlannedSpandrelBand
{
    internal PlannedSpandrelBand(
        ReviewMarkKey key,
        Guid resultId,
        string junctionId,
        string curtainWallUniqueId,
        CurtainWallJunctionPlacement placement,
        string number,
        string zoneName)
    {
        Key = key;
        ResultId = resultId;
        JunctionId = junctionId;
        CurtainWallUniqueId = curtainWallUniqueId;
        Placement = placement;
        Number = number;
        ZoneName = zoneName;
        Signature = CurtainWallReviewMarks.BandSignature(curtainWallUniqueId, placement, number);
    }

    public ReviewMarkKey Key { get; }
    public Guid ResultId { get; }
    public string JunctionId { get; }

    /// <summary>
    /// The drawing number of this 層間帶 (<c>CW-V-01</c>). The section the band is drawn in is named
    /// after it, and the note the plan carries for the same junction says the same thing — that pair
    /// is what lets a reviewer get from a 未符合 row to the section it was judged on (帷幕牆規格 §7.1).
    /// </summary>
    public string Number { get; }

    /// <summary>The curtain wall whose plane the band lies on — which view the band belongs in.</summary>
    public string CurtainWallUniqueId { get; }

    /// <summary>The floor edge and the 900 mm above and below the slab (docs §4.5).</summary>
    public CurtainWallJunctionPlacement Placement { get; }

    public string ZoneName { get; }
    public string Signature { get; }

    public string Description => $"{Number} 未符合層間帶紅色填滿區域（區劃「{ZoneName}」，{JunctionId}）";

    public override string ToString() => Description;
}

/// <summary>
/// What the markup plan reads back out of a 帷幕牆區劃交接 result. The plan is rebuilt from the stored
/// run alone (spec 13.1), so everything §7.1 needs — which junction, which panels, where it is and
/// what was measured — comes from the evidence the check wrote, never from re-reading the model:
/// re-reading would place a mark where the model is now while the result says what it was.
/// </summary>
internal static class CurtainWallReviewMarks
{
    private const double MillimetersPerMeter = 1000.0;

    /// <summary>Two placements this close are the same placement, in millimetres.</summary>
    private const double SignatureQuantumMm = 0.1;

    public const string JunctionIdField = "junction.id";
    public const string PanelsField = "junction.panels";
    public const string PlacementField = "junction.placement";
    public const string LengthField = "junction.continuousFireRatedLength";
    public const string HeightField = "junction.continuousFireRatedHeight";
    public const string ProjectionField = "junction.projectionDepth";

    public static string? JunctionId(ReviewEvidence evidence) => Text(evidence, JunctionIdField);

    /// <summary>The panels §7.1 paints red — the junction's own, not every element the result is about.</summary>
    public static IReadOnlyList<string> Panels(ReviewEvidence evidence)
    {
        var text = Text(evidence, PanelsField);
        if (text is null) return Array.Empty<string>();

        return new ReadOnlyCollection<string>(text.Split(',')
            .Select(x => x.Trim()).Where(x => x.Length > 0)
            .Distinct(StringComparer.Ordinal).ToList());
    }

    public static CurtainWallJunctionPlacement? Placement(ReviewEvidence evidence) =>
        CurtainWallJunctionPlacement.TryParseEvidence(Text(evidence, PlacementField), out var placement) ? placement : null;

    /// <summary>
    /// CW-H：實測連續具時效長度與突出深度, opened by the junction's drawing number. A measurement the run
    /// never took is said to be missing rather than shown as zero — the same distinction the check keeps
    /// between 未符合 and 資料不足.
    /// </summary>
    public static string HorizontalNoteText(ReviewEvidence evidence, string number) =>
        $"{number}　交接帶連續具時效長度 {Millimeters(evidence, LengthField)}／突出 {Millimeters(evidence, ProjectionField)}";

    /// <summary>
    /// CW-V：實測連續具時效高度與突出深度. The note stands in the plan and the 層間帶 itself is drawn in a
    /// section, so the note says which section: the number is the section's name (帷幕牆規格 §7.1).
    /// </summary>
    public static string SpandrelNoteText(ReviewEvidence evidence, string number) =>
        $"{number}　層間帶連續具時效高度 {Millimeters(evidence, HeightField)}／突出 {Millimeters(evidence, ProjectionField)}" +
        $"（詳見立面 {number}）";

    public static string NoteSignature(string text, CurtainWallJunctionPlacement placement) =>
        "note|" + text + "|" + Extent(placement);

    /// <summary>
    /// The band carries no text of its own, but its number is written to its Comments and names the
    /// section it lives in, so a renumbered band is a changed band: the signature says so.
    /// </summary>
    public static string BandSignature(string curtainWallUniqueId, CurtainWallJunctionPlacement placement, string number) =>
        "band|" + curtainWallUniqueId + "|" + number + "|" + Extent(placement);

    private static string Extent(CurtainWallJunctionPlacement placement) => string.Format(
        CultureInfo.InvariantCulture,
        "{0}|{1}|{2}",
        PlannedElementSignature.ForSegment(placement.StartMm, placement.EndMm, SignatureQuantumMm),
        Quantize(placement.BottomElevationMm),
        Quantize(placement.TopElevationMm));

    private static string Quantize(double millimeters) =>
        (Math.Round(millimeters / SignatureQuantumMm) * SignatureQuantumMm).ToString("0.###", CultureInfo.InvariantCulture);

    /// <summary>A length the evidence holds in metres, as the millimetres a drawing is annotated in.</summary>
    private static string Millimeters(ReviewEvidence evidence, string field)
    {
        var value = evidence.Find(field);
        return value is null || value.Kind != ReviewValueKind.Quantity
            ? "未量得"
            : (value.Number * MillimetersPerMeter).ToString("0.#", CultureInfo.InvariantCulture) + " mm";
    }

    private static string? Text(ReviewEvidence evidence, string field)
    {
        var value = evidence.Find(field);
        return value is not null && value.Kind == ReviewValueKind.Text && value.Text.Length > 0 ? value.Text : null;
    }
}
