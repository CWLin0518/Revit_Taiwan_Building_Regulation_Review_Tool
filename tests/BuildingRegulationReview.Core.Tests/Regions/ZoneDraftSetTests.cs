using System;
using System.Linq;
using BuildingRegulationReview.Domain.Common;
using BuildingRegulationReview.Domain.Regions;
using Xunit;

namespace BuildingRegulationReview.Core.Tests.Regions;

public class ZoneDraftSetTests
{
    private static readonly Guid FirstZoneId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid SecondZoneId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000002");

    [Fact]
    public void StartsEmpty()
    {
        Assert.True(ZoneDraftSet.Empty.IsEmpty);
        Assert.Equal(0, ZoneDraftSet.Empty.Count);
        Assert.Equal(0, ZoneDraftSet.Empty.AssignedFaceCount);
    }

    [Fact]
    public void KeepsZonesInCreationOrder()
    {
        var set = Succeeds(Succeeds(ZoneDraftSet.Empty.Add(Zone(SecondZoneId, "B"))).Add(Zone(FirstZoneId, "A")));

        Assert.Equal(new[] { "B", "A" }, set.Zones.Select(z => z.Name));
    }

    [Fact]
    public void RefusesASecondZoneWithTheSameNameWhateverTheCase()
    {
        var set = Succeeds(ZoneDraftSet.Empty.Add(Zone(FirstZoneId, "防火區劃 A")));

        var result = set.Add(Zone(SecondZoneId, "防火區劃 a"));

        Assert.True(result.IsFailure);
        Assert.Equal("regions.zone.duplicateName", result.Error.Code);
    }

    [Fact]
    public void RefusesTheSameZoneTwice()
    {
        var set = Succeeds(ZoneDraftSet.Empty.Add(Zone(FirstZoneId, "A")));

        Assert.Equal("regions.zone.duplicateId", set.Add(Zone(FirstZoneId, "B")).Error.Code);
    }

    [Fact]
    public void AssignsAFaceToAZone()
    {
        var set = Succeeds(ZoneDraftSet.Empty.Add(Zone(FirstZoneId, "A")));

        var assignment = Succeeds(set.Assign(3, FirstZoneId));

        Assert.Equal(3, assignment.FaceId);
        Assert.Equal(FirstZoneId, assignment.ZoneId);
        Assert.Null(assignment.PreviousZoneId);
        Assert.False(assignment.MovedFromAnotherZone);
        Assert.Equal(FirstZoneId, assignment.Zones.ZoneOf(3)!.Id);
    }

    [Fact]
    public void LeavesTheOriginalSetUntouchedWhenAFaceIsAssigned()
    {
        var set = Succeeds(ZoneDraftSet.Empty.Add(Zone(FirstZoneId, "A")));

        Succeeds(set.Assign(3, FirstZoneId));

        Assert.False(set.IsAssigned(3));
    }

    [Fact]
    public void MovesAFaceOutOfTheZoneThatHeldItSoItBelongsToOnlyOne()
    {
        var set = TwoZones();
        set = Succeeds(set.Assign(7, FirstZoneId)).Zones;

        var assignment = Succeeds(set.Assign(7, SecondZoneId));

        Assert.Equal(SecondZoneId, assignment.ZoneId);
        Assert.Equal(FirstZoneId, assignment.PreviousZoneId);
        Assert.True(assignment.MovedFromAnotherZone);
        Assert.Empty(assignment.Zones.Zone(FirstZoneId)!.FaceIds);
        Assert.Equal(new[] { 7 }, assignment.Zones.Zone(SecondZoneId)!.FaceIds);
        Assert.Equal(1, assignment.Zones.AssignedFaceCount);
    }

    [Fact]
    public void RefusesToAssignAFaceToTheZoneThatAlreadyHasIt()
    {
        var set = Succeeds(Succeeds(ZoneDraftSet.Empty.Add(Zone(FirstZoneId, "A"))).Assign(7, FirstZoneId)).Zones;

        Assert.Equal("regions.zone.faceAlreadyInZone", set.Assign(7, FirstZoneId).Error.Code);
    }

    [Fact]
    public void RefusesAZoneCreatedWithAFaceAnotherZoneOwns()
    {
        var set = Succeeds(Succeeds(ZoneDraftSet.Empty.Add(Zone(FirstZoneId, "A"))).Assign(7, FirstZoneId)).Zones;

        var result = set.Add(new ZoneDraft(SecondZoneId, "B", ZoneColorPalette.At(1), new[] { 7 }));

        Assert.True(result.IsFailure);
        Assert.Equal("regions.zone.faceAlreadyAssigned", result.Error.Code);
    }

    [Fact]
    public void UnassignsAFace()
    {
        var set = Succeeds(Succeeds(ZoneDraftSet.Empty.Add(Zone(FirstZoneId, "A"))).Assign(7, FirstZoneId)).Zones;

        var assignment = Succeeds(set.Unassign(7));

        Assert.Null(assignment.ZoneId);
        Assert.Equal(FirstZoneId, assignment.PreviousZoneId);
        Assert.False(assignment.Zones.IsAssigned(7));
    }

    [Fact]
    public void SaysSoWhenThereIsNothingToUnassign()
    {
        Assert.Equal("regions.zone.faceNotAssigned", ZoneDraftSet.Empty.Unassign(7).Error.Code);
    }

    [Fact]
    public void RenamesAZone()
    {
        var set = Succeeds(ZoneDraftSet.Empty.Add(Zone(FirstZoneId, "A")));

        Assert.Equal("防火區劃 1", Succeeds(set.Rename(FirstZoneId, " 防火區劃 1 ")).Zone(FirstZoneId)!.Name);
    }

    [Fact]
    public void RefusesARenameOntoAnotherZonesName()
    {
        var set = TwoZones();

        Assert.Equal("regions.zone.duplicateName", set.Rename(SecondZoneId, "A").Error.Code);
    }

    [Fact]
    public void AllowsAZoneToKeepItsOwnNameWhileBeingRenamed()
    {
        var set = TwoZones();

        Assert.True(set.Rename(FirstZoneId, "a").IsSuccess);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void RefusesABlankName(string name)
    {
        var set = Succeeds(ZoneDraftSet.Empty.Add(Zone(FirstZoneId, "A")));

        Assert.Equal("regions.zone.invalidName", set.Rename(FirstZoneId, name).Error.Code);
    }

    [Fact]
    public void RefusesAnOverlongName()
    {
        var set = Succeeds(ZoneDraftSet.Empty.Add(Zone(FirstZoneId, "A")));

        Assert.Equal(
            "regions.zone.invalidName",
            set.Rename(FirstZoneId, new string('x', ZoneDraft.MaximumNameLength + 1)).Error.Code);
    }

    [Fact]
    public void RecolorsAZone()
    {
        var set = Succeeds(ZoneDraftSet.Empty.Add(Zone(FirstZoneId, "A")));

        var recolored = Succeeds(set.Recolor(FirstZoneId, ZoneColor.FromHex("#123456")));

        Assert.Equal(ZoneColor.FromHex("#123456"), recolored.Zone(FirstZoneId)!.Color);
        Assert.NotEqual(ZoneColor.FromHex("#123456"), set.Zone(FirstZoneId)!.Color);
    }

    [Fact]
    public void RemovingAZoneReleasesItsFaces()
    {
        var set = Succeeds(Succeeds(ZoneDraftSet.Empty.Add(Zone(FirstZoneId, "A"))).Assign(7, FirstZoneId)).Zones;

        var removed = Succeeds(set.Remove(FirstZoneId));

        Assert.True(removed.IsEmpty);
        Assert.False(removed.IsAssigned(7));
        Assert.Null(removed.ZoneOf(7));
    }

    [Fact]
    public void ReportsAnUnknownZone()
    {
        Assert.Equal("regions.zone.unknownZone", ZoneDraftSet.Empty.Remove(FirstZoneId).Error.Code);
        Assert.Equal("regions.zone.unknownZone", ZoneDraftSet.Empty.Rename(FirstZoneId, "A").Error.Code);
        Assert.Equal("regions.zone.unknownZone", ZoneDraftSet.Empty.Recolor(FirstZoneId, ZoneColorPalette.At(0)).Error.Code);
        Assert.Equal("regions.zone.unknownZone", ZoneDraftSet.Empty.Assign(0, FirstZoneId).Error.Code);
    }

    [Fact]
    public void DropsFacesASecondSolveNoLongerProduces()
    {
        var set = Succeeds(Succeeds(ZoneDraftSet.Empty.Add(Zone(FirstZoneId, "A"))).Assign(7, FirstZoneId)).Zones;
        set = Succeeds(set.Assign(2, FirstZoneId)).Zones;

        var retained = set.RetainFaces(id => id != 7);

        Assert.Equal(new[] { 2 }, retained.Zone(FirstZoneId)!.FaceIds);
    }

    [Fact]
    public void KeepsTheSameSetWhenEveryFaceSurvives()
    {
        var set = Succeeds(Succeeds(ZoneDraftSet.Empty.Add(Zone(FirstZoneId, "A"))).Assign(7, FirstZoneId)).Zones;

        Assert.Same(set, set.RetainFaces(_ => true));
    }

    [Fact]
    public void KeepsFaceIdsSortedAndDistinct()
    {
        var zone = new ZoneDraft(FirstZoneId, "A", ZoneColorPalette.At(0), new[] { 5, 1, 5, 3 });

        Assert.Equal(new[] { 1, 3, 5 }, zone.FaceIds);
    }

    [Fact]
    public void HandsOutADifferentPaletteColourForEachNewZone()
    {
        var first = ZoneColorPalette.NextUnused(Array.Empty<ZoneColor>());
        var second = ZoneColorPalette.NextUnused(new[] { first });

        Assert.NotEqual(first, second);
        Assert.Equal(ZoneColorPalette.At(0), first);
        Assert.Equal(ZoneColorPalette.At(1), second);
    }

    [Fact]
    public void RoundTripsAColourThroughHex()
    {
        var color = ZoneColor.FromHex("#4E79A7");

        Assert.Equal("#4E79A7", color.ToHex());
        Assert.Equal(color, ZoneColor.FromHex("4e79a7"));
        Assert.True(color.IsDark);
        Assert.False(ZoneColor.FromHex("#EDC948").IsDark);
    }

    [Theory]
    [InlineData("#12345")]
    [InlineData("nothex")]
    public void RefusesAMalformedColour(string hex)
    {
        Assert.Throws<FormatException>(() => ZoneColor.FromHex(hex));
    }

    // ---- the confirmation a disjoint zone carries, and content fingerprints (P2-T06) ------------

    [Fact]
    public void RemembersThatTheUserAllowedAZoneToBeInPieces()
    {
        var zones = TwoZones();
        Assert.False(zones.Zone(FirstZoneId)!.AllowsDisjointParts);

        var allowed = Succeeds(zones.SetDisjointAllowed(FirstZoneId, true));

        Assert.True(allowed.Zone(FirstZoneId)!.AllowsDisjointParts);
        Assert.False(allowed.Zone(SecondZoneId)!.AllowsDisjointParts);
        Assert.False(zones.Zone(FirstZoneId)!.AllowsDisjointParts);
    }

    [Fact]
    public void KeepsTheConfirmationWhileFacesComeAndGo()
    {
        var zones = Succeeds(TwoZones().SetDisjointAllowed(FirstZoneId, true));

        var assigned = Succeeds(zones.Assign(3, FirstZoneId)).Zones;

        Assert.True(assigned.Zone(FirstZoneId)!.AllowsDisjointParts);
        Assert.True(Succeeds(assigned.Unassign(3)).Zones.Zone(FirstZoneId)!.AllowsDisjointParts);
    }

    [Fact]
    public void RefusesToConfirmAZoneThatIsNotThere()
    {
        Assert.Equal(
            "regions.zone.unknownZone",
            ZoneDraftSet.Empty.SetDisjointAllowed(FirstZoneId, true).Error.Code);
    }

    [Fact]
    public void GivesEqualDraftsTheSameSignature()
    {
        Assert.Equal(TwoZones().Signature(), TwoZones().Signature());
        Assert.Equal(string.Empty, ZoneDraftSet.Empty.Signature());
    }

    [Theory]
    [InlineData("rename")]
    [InlineData("recolor")]
    [InlineData("assign")]
    [InlineData("confirm")]
    public void ChangesTheSignatureWheneverSomethingReachesTheModel(string edit)
    {
        var zones = TwoZones();
        var changed = edit switch
        {
            "rename" => Succeeds(zones.Rename(FirstZoneId, "C")),
            "recolor" => Succeeds(zones.Recolor(FirstZoneId, ZoneColor.FromHex("#123456"))),
            "assign" => Succeeds(zones.Assign(7, FirstZoneId)).Zones,
            _ => Succeeds(zones.SetDisjointAllowed(FirstZoneId, true))
        };

        Assert.NotEqual(zones.Signature(), changed.Signature());
    }

    private static ZoneDraftSet TwoZones() =>
        Succeeds(Succeeds(ZoneDraftSet.Empty.Add(Zone(FirstZoneId, "A"))).Add(Zone(SecondZoneId, "B")));

    private static ZoneDraft Zone(Guid id, string name) =>
        new ZoneDraft(id, name, ZoneColorPalette.At(id == FirstZoneId ? 0 : 1));

    private static T Succeeds<T>(Result<T> result)
    {
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.ToString() : string.Empty);
        return result.Value;
    }
}
