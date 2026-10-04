using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Text;
using BuildingRegulationReview.Application.Diagnostics;
using BuildingRegulationReview.Domain.Geometry;
using BuildingRegulationReview.Domain.Reviews;
using BuildingRegulationReview.Domain.Rules;

namespace BuildingRegulationReview.Application.Candidates;

/// <summary>How one element sits relative to one 區劃 in plan.</summary>
public enum ZoneRelationKind
{
    /// <summary>The element forms part of the zone's boundary: a wall or beam along it, a column it runs through.</summary>
    Boundary,

    /// <summary>The element runs across the boundary, partly inside the zone and partly outside.</summary>
    Crossing,

    /// <summary>The element lies inside the zone without forming its boundary.</summary>
    Inside,

    /// <summary>
    /// An opening in a curtain wall that bounds the zone where no zone lies beyond it: the wall is the
    /// building's outer facade, not a 區劃分隔 between two parts of the building. 第79條 does not ask
    /// such an opening to be a 防火設備; the facade answers to 第79條之3、第79條之4 instead
    /// (帷幕牆區劃交接). Openings only.
    /// </summary>
    Facade,

    /// <summary>The geometry does not decide it; the result is ManualReview (spec 11.0 P3-T03).</summary>
    Ambiguous
}

/// <summary>Why a relation, or a zone, could not be decided from the model.</summary>
public enum CandidateAmbiguityKind
{
    /// <summary>The Area boundary runs inside the member's thickness but not along its centreline.</summary>
    BoundaryOffCenterline,

    /// <summary>The Area boundary runs along a column face instead of through the column.</summary>
    BoundaryAlongOutline,

    /// <summary>The element has no plan geometry to measure.</summary>
    NoPlanGeometry,

    /// <summary>The opening's host wall is itself ambiguous at this boundary.</summary>
    HostRelationAmbiguous,

    /// <summary>The opening names a host the review did not read, or a host that is not a wall.</summary>
    HostNotResolved,

    /// <summary>An opening in a curtain wall. MVP policy: manual review (spec 17.1, 11.6).</summary>
    CurtainWallOpening,

    /// <summary>An opening without a host near the boundary. MVP policy: manual review (spec 11.6).</summary>
    NonHostedOpening,

    /// <summary>The opening has no plan location to test.</summary>
    OpeningLocationUnknown,

    /// <summary>An element from a linked model. MVP policy: manual review (spec 17.1).</summary>
    LinkedElement,

    /// <summary>One of the zone's Areas is not enclosed, so the zone's extent is unknown.</summary>
    ZoneNotEnclosed,

    /// <summary>The zone's Areas overlap another zone's, so an element could belong to either.</summary>
    ZonesOverlap
}

/// <summary>
/// The lengths a relation was decided on, in feet. Each one is only present when the kind of element
/// measures it, so an absent value means "not measured", never zero.
/// </summary>
public sealed class RelationMeasurements
{
    public static readonly RelationMeasurements None = new RelationMeasurements();

    public RelationMeasurements(
        double? boundaryLengthFeet = null,
        double? bandLengthFeet = null,
        double? insideLengthFeet = null,
        double? outsideLengthFeet = null,
        double? throughLengthFeet = null,
        double? distanceToBoundaryFeet = null)
    {
        BoundaryLengthFeet = Check(boundaryLengthFeet, nameof(boundaryLengthFeet));
        BandLengthFeet = Check(bandLengthFeet, nameof(bandLengthFeet));
        InsideLengthFeet = Check(insideLengthFeet, nameof(insideLengthFeet));
        OutsideLengthFeet = Check(outsideLengthFeet, nameof(outsideLengthFeet));
        ThroughLengthFeet = Check(throughLengthFeet, nameof(throughLengthFeet));
        DistanceToBoundaryFeet = Check(distanceToBoundaryFeet, nameof(distanceToBoundaryFeet));
    }

    /// <summary>Length of the centreline lying on the boundary.</summary>
    public double? BoundaryLengthFeet { get; }

    /// <summary>Length lying along the boundary within the member's own thickness.</summary>
    public double? BandLengthFeet { get; }

    public double? InsideLengthFeet { get; }
    public double? OutsideLengthFeet { get; }

    /// <summary>Length of the zone boundary running through the element's outline.</summary>
    public double? ThroughLengthFeet { get; }

    /// <summary>Distance from an opening's location to the zone boundary.</summary>
    public double? DistanceToBoundaryFeet { get; }

    internal IEnumerable<(string Field, double Feet)> Present()
    {
        if (BoundaryLengthFeet.HasValue) yield return ("candidate.boundaryLength", BoundaryLengthFeet.Value);
        if (BandLengthFeet.HasValue) yield return ("candidate.bandLength", BandLengthFeet.Value);
        if (InsideLengthFeet.HasValue) yield return ("candidate.insideLength", InsideLengthFeet.Value);
        if (OutsideLengthFeet.HasValue) yield return ("candidate.outsideLength", OutsideLengthFeet.Value);
        if (ThroughLengthFeet.HasValue) yield return ("candidate.throughLength", ThroughLengthFeet.Value);
        if (DistanceToBoundaryFeet.HasValue) yield return ("candidate.distanceToBoundary", DistanceToBoundaryFeet.Value);
    }

    private static double? Check(double? value, string name)
    {
        if (value.HasValue && (double.IsNaN(value.Value) || double.IsInfinity(value.Value) || value.Value < 0))
            throw new ArgumentOutOfRangeException(name, "A measurement must be a finite, non-negative length.");
        return value;
    }
}

/// <summary>One element's relation to one zone, with the measurements and the sentence that explain it.</summary>
public sealed class ZoneRelation
{
    public ZoneRelation(
        Guid zoneId,
        ZoneRelationKind kind,
        string message,
        RelationMeasurements? measurements = null,
        CandidateAmbiguityKind? ambiguity = null)
    {
        if (zoneId == Guid.Empty) throw new ArgumentException("Zone ID cannot be empty.", nameof(zoneId));
        if (!Enum.IsDefined(typeof(ZoneRelationKind), kind)) throw new ArgumentOutOfRangeException(nameof(kind));
        if (string.IsNullOrWhiteSpace(message)) throw new ArgumentException("A relation must say why.", nameof(message));
        if ((kind == ZoneRelationKind.Ambiguous) != ambiguity.HasValue)
            throw new ArgumentException("Exactly the ambiguous relations name what made them ambiguous.", nameof(ambiguity));

        ZoneId = zoneId;
        Kind = kind;
        Message = message.Trim();
        Measurements = measurements ?? RelationMeasurements.None;
        Ambiguity = ambiguity;
    }

    public Guid ZoneId { get; }
    public ZoneRelationKind Kind { get; }
    public string Message { get; }
    public RelationMeasurements Measurements { get; }
    public CandidateAmbiguityKind? Ambiguity { get; }
    public bool IsAmbiguous => Kind == ZoneRelationKind.Ambiguous;
    public bool IsBoundary => Kind == ZoneRelationKind.Boundary;
    public bool IsFacade => Kind == ZoneRelationKind.Facade;
}

/// <summary>A wall, column, beam or floor with the zones it relates to.</summary>
public sealed class MemberCandidate
{
    public MemberCandidate(MemberObservation observation, IEnumerable<ZoneRelation> relations)
    {
        Observation = observation ?? throw new ArgumentNullException(nameof(observation));
        Relations = CandidateOrdering.Relations(relations);
    }

    public MemberObservation Observation { get; }
    public IReadOnlyList<ZoneRelation> Relations { get; }
    public CandidateSource Source => Observation.Source;
    public CandidateCategory Category => Observation.Category;

    public ZoneRelation? RelationTo(Guid zoneId) => Relations.FirstOrDefault(r => r.ZoneId == zoneId);

    /// <summary>The source evidence a result about this member and zone carries (spec 11.3 evidence).</summary>
    public ReviewEvidence EvidenceFor(Guid zoneId)
    {
        var relation = RelationTo(zoneId) ?? throw new ArgumentException("The member has no relation to this zone.", nameof(zoneId));
        var items = CandidateEvidence.Relation(relation).ToList();
        items.AddRange(CandidateEvidence.Source(Source, Category, Observation.TypeName));
        if (Observation.WidthFeet.HasValue) items.Add(CandidateEvidence.Length("source.width", Observation.WidthFeet.Value));
        return new ReviewEvidence(items);
    }
}

/// <summary>A door, window or curtain panel with the zones it relates to.</summary>
public sealed class OpeningCandidate
{
    public OpeningCandidate(OpeningObservation observation, IEnumerable<ZoneRelation> relations, MemberObservation? host)
    {
        Observation = observation ?? throw new ArgumentNullException(nameof(observation));
        Relations = CandidateOrdering.Relations(relations);
        Host = host;
    }

    public OpeningObservation Observation { get; }
    public IReadOnlyList<ZoneRelation> Relations { get; }

    /// <summary>The host wall the review resolved, or null when there was none to resolve.</summary>
    public MemberObservation? Host { get; }

    public CandidateSource Source => Observation.Source;
    public CandidateCategory Category => Observation.Category;

    /// <summary>Hosted by a wall the review read; curtain walls count, their policy is decided per relation.</summary>
    public bool HasResolvedHost => Host is not null;

    public ZoneRelation? RelationTo(Guid zoneId) => Relations.FirstOrDefault(r => r.ZoneId == zoneId);

    public ReviewEvidence EvidenceFor(Guid zoneId)
    {
        var relation = RelationTo(zoneId) ?? throw new ArgumentException("The opening has no relation to this zone.", nameof(zoneId));
        var items = CandidateEvidence.Relation(relation).ToList();
        items.AddRange(CandidateEvidence.Source(Source, Category, Observation.TypeName));
        if (Observation.HostUniqueId is not null) items.Add(new ReviewEvidenceItem("source.hostUniqueId", ReviewValue.OfText(Observation.HostUniqueId)));
        if (Host is not null) items.Add(new ReviewEvidenceItem("source.hostIsCurtainWall", ReviewValue.OfBoolean(Host.IsCurtainWall)));
        return new ReviewEvidence(items);
    }
}

/// <summary>One Area of a zone as the review measured it.</summary>
public sealed class CandidateZonePart
{
    public CandidateZonePart(ZonePartObservation observation, double? geometricAreaSquareFeet)
    {
        Observation = observation ?? throw new ArgumentNullException(nameof(observation));
        GeometricAreaSquareFeet = geometricAreaSquareFeet;
    }

    public ZonePartObservation Observation { get; }
    public string AreaUniqueId => Observation.AreaUniqueId;
    public bool IsEnclosed => Observation.IsEnclosed;
    public double? RevitAreaSquareMeters => Observation.RevitAreaSquareFeet is double feet ? PlanUnits.SquareFeetToSquareMeters(feet) : (double?)null;

    /// <summary>Outer loop minus holes of the boundary Revit reported; the cross-check of spec 11.4 step 2.</summary>
    public double? GeometricAreaSquareFeet { get; }

    public double? GeometricAreaSquareMeters => GeometricAreaSquareFeet is double feet ? PlanUnits.SquareFeetToSquareMeters(feet) : (double?)null;
}

/// <summary>A 區劃 as the review sees it: its Areas and whether its extent can be relied on.</summary>
public sealed class CandidateZone
{
    public CandidateZone(Guid zoneId, string name, IEnumerable<CandidateZonePart> parts, IEnumerable<CandidateAmbiguityKind>? problems = null)
    {
        if (zoneId == Guid.Empty) throw new ArgumentException("Zone ID cannot be empty.", nameof(zoneId));
        if (parts is null) throw new ArgumentNullException(nameof(parts));

        ZoneId = zoneId;
        Name = string.IsNullOrWhiteSpace(name) ? zoneId.ToString("D") : name.Trim();
        Parts = new ReadOnlyCollection<CandidateZonePart>(parts.OrderBy(p => p.AreaUniqueId, StringComparer.Ordinal).ToList());
        Problems = new ReadOnlyCollection<CandidateAmbiguityKind>((problems ?? Array.Empty<CandidateAmbiguityKind>()).Distinct().OrderBy(p => p).ToList());
    }

    public Guid ZoneId { get; }

    /// <summary>The zone ID as rules and results carry it (<c>zone.id</c>, <see cref="ReviewResult.ZoneId"/>).</summary>
    public string ZoneIdText => ZoneId.ToString("D");

    public string Name { get; }
    public IReadOnlyList<CandidateZonePart> Parts { get; }
    public IReadOnlyList<CandidateAmbiguityKind> Problems { get; }

    /// <summary>No zone-level problem: every Area is enclosed and none overlaps another zone.</summary>
    public bool IsClear => Problems.Count == 0;

    /// <summary>At least one enclosed Area, so elements can be measured against it at all.</summary>
    public bool IsMeasurable => Parts.Any(p => p.IsEnclosed);

    public IEnumerable<string> AreaUniqueIds => Parts.Select(p => p.AreaUniqueId);

    /// <summary>Sum of Revit's own Area values, or null when any Area did not report one.</summary>
    public double? RevitAreaSquareMeters =>
        Parts.All(p => p.RevitAreaSquareMeters.HasValue) ? Parts.Sum(p => p.RevitAreaSquareMeters!.Value) : (double?)null;

    /// <summary>Sum of the measured boundaries, or null when any Area is not enclosed.</summary>
    public double? GeometricAreaSquareMeters =>
        Parts.All(p => p.GeometricAreaSquareMeters.HasValue) ? Parts.Sum(p => p.GeometricAreaSquareMeters!.Value) : (double?)null;
}

/// <summary>
/// Something candidate resolution could not decide. It becomes a ManualReview result rather than a
/// guess (spec 11.0 P3-T03: 歧義回傳 ManualReview).
/// </summary>
public sealed class CandidateAmbiguity
{
    public CandidateAmbiguity(
        CandidateAmbiguityKind kind,
        IEnumerable<string> subjectUniqueIds,
        Guid? zoneId,
        string message,
        ReviewEvidence? evidence = null)
    {
        if (!Enum.IsDefined(typeof(CandidateAmbiguityKind), kind)) throw new ArgumentOutOfRangeException(nameof(kind));
        if (subjectUniqueIds is null) throw new ArgumentNullException(nameof(subjectUniqueIds));
        if (zoneId == Guid.Empty) throw new ArgumentException("Zone ID cannot be empty.", nameof(zoneId));
        if (string.IsNullOrWhiteSpace(message)) throw new ArgumentException("An ambiguity must say why.", nameof(message));

        var subjects = subjectUniqueIds.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim())
            .Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToList();
        if (subjects.Count == 0) throw new ArgumentException("An ambiguity must name the elements it is about.", nameof(subjectUniqueIds));

        Kind = kind;
        SubjectUniqueIds = new ReadOnlyCollection<string>(subjects);
        ZoneId = zoneId;
        Message = message.Trim();
        Evidence = evidence ?? ReviewEvidence.Empty;
    }

    public CandidateAmbiguityKind Kind { get; }
    public IReadOnlyList<string> SubjectUniqueIds { get; }
    public Guid? ZoneId { get; }
    public string Message { get; }
    public ReviewEvidence Evidence { get; }

    /// <summary>The spec 14 error code a log entry about this ambiguity carries.</summary>
    public string ErrorCode => Kind == CandidateAmbiguityKind.ZoneNotEnclosed || Kind == CandidateAmbiguityKind.ZonesOverlap
        ? ReviewErrorCode.CandidateZoneUnusable
        : ReviewErrorCode.CandidateAmbiguous;

    /// <summary>
    /// The ManualReview result of a check that met this ambiguity. No rule decided it, so — like the
    /// engine's NoRule outcome — it is attributed to the rule set itself.
    /// </summary>
    public ReviewResult ToReviewResult(Guid resultId, Guid runId, Guid packageId, string checkType, CompiledRuleSet ruleSet)
    {
        if (ruleSet is null) throw new ArgumentNullException(nameof(ruleSet));
        var set = ruleSet.RuleSet;
        return new ReviewResult(resultId, runId, packageId, checkType, SubjectUniqueIds, ZoneId?.ToString("D"),
            ReviewStatus.ManualReview, null, null, set.RuleSetId, set.Version, $"規則集「{set.Title}」",
            Message, Evidence);
    }

    public override string ToString() => $"{Kind} [{string.Join(",", SubjectUniqueIds)}]: {Message}";
}

/// <summary>
/// The outcome of candidate resolution for one package: every zone, every member and opening that
/// relates to one, and everything that could not be decided. Ordered canonically, so the same model
/// always yields the same set (spec 11.0 P3-T03: 固定模型的候選集合可重現).
/// </summary>
public sealed class CandidateSet
{
    public CandidateSet(
        Guid packageId,
        string levelUniqueId,
        string? levelName,
        IEnumerable<CandidateZone> zones,
        IEnumerable<MemberCandidate> members,
        IEnumerable<OpeningCandidate> openings,
        IEnumerable<CandidateAmbiguity> ambiguities,
        int unrelatedMemberCount,
        int unrelatedOpeningCount,
        IEnumerable<string>? warnings = null)
    {
        if (packageId == Guid.Empty) throw new ArgumentException("Package ID cannot be empty.", nameof(packageId));
        if (string.IsNullOrWhiteSpace(levelUniqueId)) throw new ArgumentException("Level UniqueId is required.", nameof(levelUniqueId));
        if (unrelatedMemberCount < 0) throw new ArgumentOutOfRangeException(nameof(unrelatedMemberCount));
        if (unrelatedOpeningCount < 0) throw new ArgumentOutOfRangeException(nameof(unrelatedOpeningCount));

        PackageId = packageId;
        LevelUniqueId = levelUniqueId.Trim();
        LevelName = string.IsNullOrWhiteSpace(levelName) ? null : levelName!.Trim();
        Zones = new ReadOnlyCollection<CandidateZone>((zones ?? throw new ArgumentNullException(nameof(zones))).OrderBy(z => z.ZoneId).ToList());
        Members = new ReadOnlyCollection<MemberCandidate>((members ?? throw new ArgumentNullException(nameof(members)))
            .OrderBy(m => m.Category).ThenBy(m => m.Source, CandidateOrdering.Sources).ToList());
        Openings = new ReadOnlyCollection<OpeningCandidate>((openings ?? throw new ArgumentNullException(nameof(openings)))
            .OrderBy(o => o.Category).ThenBy(o => o.Source, CandidateOrdering.Sources).ToList());
        Ambiguities = new ReadOnlyCollection<CandidateAmbiguity>((ambiguities ?? throw new ArgumentNullException(nameof(ambiguities)))
            .OrderBy(a => a.ZoneId?.ToString("D") ?? string.Empty, StringComparer.Ordinal)
            .ThenBy(a => a.SubjectUniqueIds[0], StringComparer.Ordinal)
            .ThenBy(a => a.Kind)
            .ToList());
        UnrelatedMemberCount = unrelatedMemberCount;
        UnrelatedOpeningCount = unrelatedOpeningCount;
        Warnings = new ReadOnlyCollection<string>((warnings ?? Array.Empty<string>()).ToList());
    }

    public Guid PackageId { get; }
    public string LevelUniqueId { get; }
    public string? LevelName { get; }
    public IReadOnlyList<CandidateZone> Zones { get; }
    public IReadOnlyList<MemberCandidate> Members { get; }
    public IReadOnlyList<OpeningCandidate> Openings { get; }
    public IReadOnlyList<CandidateAmbiguity> Ambiguities { get; }

    /// <summary>Members read from the storey that relate to no zone at all.</summary>
    public int UnrelatedMemberCount { get; }

    /// <summary>Openings read from the storey that are candidates for no zone.</summary>
    public int UnrelatedOpeningCount { get; }

    public IReadOnlyList<string> Warnings { get; }

    /// <summary>
    /// Spec 11.1 前置檢查「來源元素與區劃空間關係可解析」: at least one zone, and every zone
    /// measurable. Individual ambiguities do not block; they become ManualReview results.
    /// </summary>
    public bool CanReview => Zones.Count > 0 && Zones.All(z => z.IsMeasurable);

    public CandidateZone? Zone(Guid zoneId) => Zones.FirstOrDefault(z => z.ZoneId == zoneId);

    public IEnumerable<MemberCandidate> MembersOf(Guid zoneId) => Members.Where(m => m.RelationTo(zoneId) is not null);

    public IEnumerable<OpeningCandidate> OpeningsOf(Guid zoneId) => Openings.Where(o => o.RelationTo(zoneId) is not null);

    /// <summary>
    /// A canonical text of the whole set, one line per fact, lengths rounded to a micrometre. Two
    /// resolutions of the same model produce the same text whatever order the elements were read in.
    /// </summary>
    public string Signature()
    {
        var text = new StringBuilder();
        text.Append("package|").Append(PackageId.ToString("D")).Append('|').Append(LevelUniqueId).Append('\n');

        foreach (var zone in Zones)
        {
            text.Append("zone|").Append(zone.ZoneIdText).Append('|').Append(zone.Name)
                .Append("|parts=").Append(zone.Parts.Count.ToString(CultureInfo.InvariantCulture))
                .Append("|enclosed=").Append(zone.Parts.Count(p => p.IsEnclosed).ToString(CultureInfo.InvariantCulture))
                .Append("|area=").Append(Number(zone.GeometricAreaSquareMeters))
                .Append("|problems=").Append(string.Join(",", zone.Problems)).Append('\n');
        }

        foreach (var member in Members)
            AppendRelations(text, "member", member.Category, member.Source, member.Relations);

        foreach (var opening in Openings)
            AppendRelations(text, "opening", opening.Category, opening.Source, opening.Relations);

        foreach (var ambiguity in Ambiguities)
        {
            text.Append("ambiguity|").Append(ambiguity.Kind).Append('|')
                .Append(ambiguity.ZoneId?.ToString("D") ?? "-").Append('|')
                .Append(string.Join(",", ambiguity.SubjectUniqueIds)).Append('\n');
        }

        text.Append("unrelated|members=").Append(UnrelatedMemberCount.ToString(CultureInfo.InvariantCulture))
            .Append("|openings=").Append(UnrelatedOpeningCount.ToString(CultureInfo.InvariantCulture)).Append('\n');
        return text.ToString();
    }

    private static void AppendRelations(StringBuilder text, string prefix, CandidateCategory category, CandidateSource source, IEnumerable<ZoneRelation> relations)
    {
        foreach (var relation in relations)
        {
            var m = relation.Measurements;
            text.Append(prefix).Append('|').Append(category).Append('|').Append(source).Append('|')
                .Append(relation.ZoneId.ToString("D")).Append('|').Append(relation.Kind)
                .Append('|').Append(relation.Ambiguity?.ToString() ?? "-")
                .Append("|b=").Append(Length(m.BoundaryLengthFeet))
                .Append("|w=").Append(Length(m.BandLengthFeet))
                .Append("|i=").Append(Length(m.InsideLengthFeet))
                .Append("|o=").Append(Length(m.OutsideLengthFeet))
                .Append("|t=").Append(Length(m.ThroughLengthFeet))
                .Append("|d=").Append(Length(m.DistanceToBoundaryFeet)).Append('\n');
        }
    }

    private static string Length(double? feet) => Number(feet is double f ? PlanUnits.FeetToMeters(f) : (double?)null);

    private static string Number(double? value) =>
        value is double v ? Math.Round(v, 6, MidpointRounding.AwayFromZero).ToString("0.######", CultureInfo.InvariantCulture) : "-";
}

internal static class CandidateOrdering
{
    public static readonly IComparer<CandidateSource> Sources = Comparer<CandidateSource>.Create(CandidateSource.Compare);

    public static IReadOnlyList<ZoneRelation> Relations(IEnumerable<ZoneRelation> relations)
    {
        if (relations is null) throw new ArgumentNullException(nameof(relations));
        var list = relations.ToList();
        if (list.Any(r => r is null)) throw new ArgumentException("Relations cannot contain null.", nameof(relations));
        if (list.GroupBy(r => r.ZoneId).Any(g => g.Count() > 1))
            throw new ArgumentException("An element relates to one zone at most once.", nameof(relations));
        return new ReadOnlyCollection<ZoneRelation>(list.OrderBy(r => r.ZoneId).ToList());
    }
}

internal static class CandidateEvidence
{
    public static IEnumerable<ReviewEvidenceItem> Relation(ZoneRelation relation)
    {
        yield return new ReviewEvidenceItem("candidate.zoneId", ReviewValue.OfText(relation.ZoneId.ToString("D")));
        yield return new ReviewEvidenceItem("candidate.relation", ReviewValue.OfText(relation.Kind.ToString()));
        if (relation.Ambiguity.HasValue)
            yield return new ReviewEvidenceItem("candidate.ambiguity", ReviewValue.OfText(relation.Ambiguity.Value.ToString()));
        foreach (var (field, feet) in relation.Measurements.Present())
            yield return Length(field, feet);
    }

    public static IEnumerable<ReviewEvidenceItem> Source(CandidateSource source, CandidateCategory category, string? typeName)
    {
        yield return new ReviewEvidenceItem("source.elementUniqueId", ReviewValue.OfText(source.ElementUniqueId));
        yield return new ReviewEvidenceItem("source.documentUniqueId", ReviewValue.OfText(source.DocumentUniqueId));
        if (source.LinkInstanceUniqueId is not null)
            yield return new ReviewEvidenceItem("source.linkInstanceUniqueId", ReviewValue.OfText(source.LinkInstanceUniqueId));
        yield return new ReviewEvidenceItem("source.category", ReviewValue.OfText(CandidateCategories.RuleText(category)));
        if (typeName is not null) yield return new ReviewEvidenceItem("source.typeName", ReviewValue.OfText(typeName));
    }

    public static ReviewEvidenceItem Length(string field, double feet) =>
        new ReviewEvidenceItem(field, ReviewValue.Quantity(PlanUnits.FeetToMeters(feet), ReviewUnit.Meter));
}
