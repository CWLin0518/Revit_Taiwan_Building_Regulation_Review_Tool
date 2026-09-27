using System;
using BuildingRegulationReview.Domain.Geometry;
using BuildingRegulationReview.Domain.Reviews;
using BuildingRegulationReview.Domain.Rules;

namespace BuildingRegulationReview.Application.Candidates;

/// <summary>
/// Turns resolved candidates into the <see cref="RuleFacts"/> the rule engine reads. Only what the
/// spatial resolution knows is set here: the relation, the category and the identity. Provided fire
/// ratings, provided protection and the zone's reviewed area are read and validated by the checks
/// that own them (P3-T04 to P3-T06), which add them to the same facts.
/// </summary>
/// <remarks>
/// An ambiguous relation never reaches the engine: it is already a ManualReview result, and turning
/// it into a <c>false</c> would let a rule decide something the geometry could not.
/// </remarks>
public static class CandidateFacts
{
    public static RuleFacts ForZone(CandidateSet set, CandidateZone zone, RuleFieldCatalog? catalog = null)
    {
        if (set is null) throw new ArgumentNullException(nameof(set));
        if (zone is null) throw new ArgumentNullException(nameof(zone));

        var facts = new RuleFacts(catalog ?? RuleFieldCatalog.Default);
        facts.Set("zone.id", zone.ZoneIdText);
        if (set.LevelName is not null) facts.Set("zone.levelName", set.LevelName);
        return facts;
    }

    public static RuleFacts ForMember(CandidateSet set, MemberCandidate member, Guid zoneId, RuleFieldCatalog? catalog = null)
    {
        if (member is null) throw new ArgumentNullException(nameof(member));
        var relation = Decided(member.RelationTo(zoneId), zoneId);
        var facts = ForZone(set, ZoneOf(set, zoneId), catalog);

        var observation = member.Observation;
        facts.Set("element.category", CandidateCategories.RuleText(observation.Category));
        facts.Set("element.isCompartmentBoundary", relation.IsBoundary);

        // Unconditional, unlike the two nullable facts below: 「不是帷幕牆」 is an answer and not a
        // gap, so a rule may compare it without waiting on anything. Only a Wall can be one, and a
        // MemberObservation of any other category reports false.
        facts.Set("element.isCurtainWall", observation.IsCurtainWall);
        if (observation.TypeName is not null) facts.Set("element.typeName", observation.TypeName);
        if (observation.IsStructural.HasValue) facts.Set("element.isStructural", observation.IsStructural.Value);
        return facts;
    }

    public static RuleFacts ForOpening(CandidateSet set, OpeningCandidate opening, Guid zoneId, RuleFieldCatalog? catalog = null)
    {
        if (opening is null) throw new ArgumentNullException(nameof(opening));
        var relation = Decided(opening.RelationTo(zoneId), zoneId);
        var facts = ForZone(set, ZoneOf(set, zoneId), catalog);

        var observation = opening.Observation;
        facts.Set("opening.kind", CandidateCategories.RuleText(observation.Category));
        facts.Set("opening.isHosted", opening.HasResolvedHost);
        facts.Set("opening.hostIsCompartmentBoundary", relation.IsBoundary);
        if (observation.HostUniqueId is not null) facts.Set("opening.hostUniqueId", observation.HostUniqueId);
        if (observation.WidthFeet is double width && observation.HeightFeet is double height)
            facts.Set("opening.area", PlanUnits.SquareFeetToSquareMeters(width * height), ReviewUnit.SquareMeter);
        return facts;
    }

    private static CandidateZone ZoneOf(CandidateSet set, Guid zoneId)
    {
        if (set is null) throw new ArgumentNullException(nameof(set));
        return set.Zone(zoneId) ?? throw new ArgumentException("The zone is not part of this candidate set.", nameof(zoneId));
    }

    private static ZoneRelation Decided(ZoneRelation? relation, Guid zoneId)
    {
        if (relation is null) throw new ArgumentException($"The element has no relation to zone {zoneId:D}.", nameof(zoneId));
        if (relation.IsAmbiguous)
            throw new ArgumentException("An ambiguous relation is a ManualReview result, not facts for the rule engine.", nameof(zoneId));
        return relation;
    }
}
