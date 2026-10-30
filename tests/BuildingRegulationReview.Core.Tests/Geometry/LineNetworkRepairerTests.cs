using System;
using System.Linq;
using BuildingRegulationReview.Application.Geometry;
using BuildingRegulationReview.Domain.Geometry;
using Xunit;

namespace BuildingRegulationReview.Core.Tests.Geometry;

public class LineNetworkRepairerTests
{
    private static readonly Guid PackageId = Guid.Parse("11111111-2222-3333-4444-555555555555");
    private static readonly GeometryTolerance Tolerance = GeometryTolerance.Default;

    // The four rectangle corners every closure case is built from, in feet.
    private const double Right = 10.0;
    private const double Top = 10.0;

    [Fact]
    public void LeavesACleanRectangleUntouched()
    {
        var result = Repair(Rectangle());

        Assert.True(result.IsSuccess);
        var network = result.Value;
        Assert.Equal(4, network.Edges.Count);
        Assert.Equal(4, network.Nodes.Count);
        Assert.Empty(network.Repairs);
        Assert.Empty(network.Issues);
        Assert.Equal(1, network.LoopCount);
        Assert.True(network.HasClosedLoop);
        Assert.All(network.Nodes, n => Assert.Equal(2, network.DegreeOf(n.Id)));
    }

    [Fact]
    public void RemovesAnIdenticalDuplicateAndKeepsBothSources()
    {
        var segments = Rectangle().Concat(new[] { Seg(0, 0, Right, 0, "DUP") }).ToArray();

        var network = Succeeds(Repair(segments));

        Assert.Equal(4, network.Edges.Count);
        var duplicates = network.RepairsOfKind(NetworkRepairKind.DuplicateRemoved).ToList();
        Assert.Single(duplicates);
        Assert.Contains(duplicates[0].Sources, s => s.ElementUniqueId == "DUP");

        var bottom = network.Edges.Single(e => Math.Abs(e.Start.Y) < 1e-9 && Math.Abs(e.End.Y) < 1e-9);
        Assert.Equal(2, bottom.Sources.Count);
    }

    [Fact]
    public void MergesOverlappingCollinearLinesIntoOneSpan()
    {
        var network = Succeeds(Repair(new[]
        {
            Seg(0, 0, 10, 0, "W1"),
            Seg(5, 0, 15, 0, "W2")
        }));

        var edge = Assert.Single(network.Edges);
        Assert.Equal(0, Math.Min(edge.Start.X, edge.End.X), 9);
        Assert.Equal(15, Math.Max(edge.Start.X, edge.End.X), 9);
        Assert.Equal(2, edge.Sources.Count);
        Assert.Single(network.RepairsOfKind(NetworkRepairKind.DuplicateRemoved));
    }

    [Fact]
    public void SplitsCrossingLinesAtTheirIntersection()
    {
        var network = Succeeds(Repair(new[]
        {
            Seg(-5, 0, 5, 0, "H"),
            Seg(0, -5, 0, 5, "V")
        }));

        Assert.Equal(4, network.Edges.Count);
        Assert.Equal(5, network.Nodes.Count);

        var centre = network.Nodes.Single(n => Math.Abs(n.Position.X) < 1e-9 && Math.Abs(n.Position.Y) < 1e-9);
        Assert.Equal(4, network.DegreeOf(centre.Id));
        Assert.Equal(2, network.RepairsOfKind(NetworkRepairKind.IntersectionSplit).Count());
    }

    [Fact]
    public void SnapsAnEndThatFallsShortOfAWallInteriorWithoutExtending()
    {
        // The stub stops 5 mm below the wall: inside the snap tolerance, so splitting the wall and
        // pulling the endpoint onto the new node is enough. No extension is needed or logged.
        var network = Succeeds(Repair(new[]
        {
            Seg(0, 0, 10, 0, "WALL"),
            Seg(5, Mm(5), 5, 10, "STUB")
        }));

        Assert.Equal(3, network.Edges.Count);
        var junction = network.Nodes.Single(n => Math.Abs(n.Position.X - 5) < 1e-9 && Math.Abs(n.Position.Y) < Mm(1));
        Assert.Equal(3, network.DegreeOf(junction.Id));

        var snapped = Assert.Single(network.RepairsOfKind(NetworkRepairKind.EndpointSnapped));
        Assert.Equal(5.0, snapped.DistanceMillimeters, 3);
        Assert.Empty(network.RepairsOfKind(NetworkRepairKind.GapExtended));
    }

    [Fact]
    public void DoesNotMergeCollinearPiecesAcrossAJunction()
    {
        // The wall is split by the stub, and its two halves are collinear. Merging them would
        // dissolve the junction the stub needs, so degree three blocks the merge.
        var network = Succeeds(Repair(new[]
        {
            Seg(0, 0, 10, 0, "WALL"),
            Seg(5, Mm(5), 5, 10, "STUB")
        }));

        Assert.Empty(network.RepairsOfKind(NetworkRepairKind.CollinearMerged));
        Assert.Equal(2, network.Edges.Count(e => Math.Abs(e.Start.Y) < 1e-9 && Math.Abs(e.End.Y) < 1e-9));
    }

    [Fact]
    public void ExtendsAShortGapAlongTheSegmentDirection()
    {
        var network = Succeeds(Repair(RectangleWithBottomGap(Mm(30))));

        Assert.Equal(4, network.Edges.Count);
        Assert.Equal(4, network.Nodes.Count);
        Assert.Equal(1, network.LoopCount);
        Assert.False(network.HasErrors);

        var extension = Assert.Single(network.RepairsOfKind(NetworkRepairKind.GapExtended));
        Assert.Equal(30.0, extension.DistanceMillimeters, 3);
        Assert.Equal(new Point2D(Right, 0), extension.ResultStart);
        Assert.Contains(extension.Sources, s => s.ElementUniqueId == "BOTTOM");
    }

    [Fact]
    public void ReportsAGapBeyondToleranceInsteadOfGuessing()
    {
        var network = Succeeds(Repair(RectangleWithBottomGap(Mm(150))));

        Assert.Empty(network.RepairsOfKind(NetworkRepairKind.GapExtended));
        Assert.True(network.HasErrors);

        var gap = network.Issues.First(i => i.Kind == NetworkIssueKind.GapBeyondTolerance);
        Assert.Equal(NetworkIssueSeverity.Error, gap.Severity);
        Assert.Equal(150.0, gap.DistanceMillimeters, 3);
        Assert.Contains("150", gap.Message);

        // Nothing enclosed an area, so the network says so rather than inventing a corner.
        Assert.Contains(network.Issues, i => i.Kind == NetworkIssueKind.NoClosedLoop);
        Assert.False(network.HasClosedLoop);
    }

    [Fact]
    public void ReportsGeometryOfOneElementThatCrossesItself()
    {
        var source = new SourceRef("doc", "AUX", GeometrySourceKind.AuxiliaryLine);
        var network = Succeeds(Repair(new[]
        {
            new Segment2D(new Point2D(0, 0), new Point2D(10, 10), source),
            new Segment2D(new Point2D(0, 10), new Point2D(10, 0), source)
        }));

        var issue = Assert.Single(network.Issues, i => i.Kind == NetworkIssueKind.SelfIntersection);
        Assert.Equal(NetworkIssueSeverity.Warning, issue.Severity);
        Assert.Equal(new Point2D(5, 5), issue.Location);

        // The crossing is still split, so the network stays planar for region solving.
        Assert.Equal(4, network.Edges.Count);
    }

    [Fact]
    public void MergesCollinearPiecesOfOneWallBackIntoASingleEdge()
    {
        var segments = new[]
        {
            Seg(0, 0, 4, 0, "BOTTOM-A"),
            Seg(4, 0, Right, 0, "BOTTOM-B"),
            Seg(Right, 0, Right, Top, "RIGHT"),
            Seg(Right, Top, 0, Top, "TOP"),
            Seg(0, Top, 0, 0, "LEFT")
        };

        var network = Succeeds(Repair(segments));

        Assert.Equal(4, network.Edges.Count);
        var merged = Assert.Single(network.RepairsOfKind(NetworkRepairKind.CollinearMerged));
        Assert.Equal(new Point2D(4, 0), merged.OriginalStart);

        var bottom = network.Edges.Single(e => Math.Abs(e.Start.Y) < 1e-9 && Math.Abs(e.End.Y) < 1e-9);
        Assert.Equal(2, bottom.Sources.Count);
        Assert.Equal(Right, bottom.LengthFeet, 9);
    }

    [Fact]
    public void KeepsBothCompartmentsOfADividedRectangle()
    {
        // The shape a fire compartment review actually starts from: one room split in two by an
        // interior wall. Both halves have to survive as separate loops for P2-T04 to solve them.
        var segments = Rectangle().Concat(new[] { Seg(5, 0, 5, Top, "DIVIDER") }).ToArray();

        var network = Succeeds(Repair(segments));

        Assert.Equal(7, network.Edges.Count);
        Assert.Equal(6, network.Nodes.Count);
        Assert.Equal(1, network.ComponentCount);
        Assert.Equal(2, network.LoopCount);
        Assert.False(network.HasErrors);
        Assert.Empty(network.DanglingNodes);
    }

    [Fact]
    public void KeepsAnIslandAsItsOwnComponent()
    {
        var island = new[]
        {
            Seg(3, 3, 7, 3, "ISLAND-S"),
            Seg(7, 3, 7, 7, "ISLAND-E"),
            Seg(7, 7, 3, 7, "ISLAND-N"),
            Seg(3, 7, 3, 3, "ISLAND-W")
        };

        var network = Succeeds(Repair(Rectangle().Concat(island).ToArray()));

        Assert.Equal(8, network.Edges.Count);
        Assert.Equal(8, network.Nodes.Count);
        Assert.Equal(2, network.ComponentCount);
        Assert.Equal(2, network.LoopCount);
        Assert.False(network.HasErrors);
    }

    [Fact]
    public void DropsAPieceShorterThanTheSnapTolerance()
    {
        var segments = Rectangle().Concat(new[] { Seg(20, 20, 20, Mm(5) + 20, "SLIVER") }).ToArray();

        var network = Succeeds(Repair(segments));

        Assert.Equal(4, network.Edges.Count);
        var issue = Assert.Single(network.Issues, i => i.Kind == NetworkIssueKind.CollapsedSegment);
        Assert.Equal(NetworkIssueSeverity.Warning, issue.Severity);
        Assert.Contains(issue.Sources, s => s.ElementUniqueId == "SLIVER");
    }

    [Fact]
    public void ReportsAnOpenNetworkAsHavingNoClosedLoop()
    {
        var network = Succeeds(Repair(new[]
        {
            Seg(0, 0, 10, 0, "A"),
            Seg(10, 0, 10, 10, "B")
        }));

        Assert.Equal(0, network.LoopCount);
        Assert.Contains(network.Issues, i => i.Kind == NetworkIssueKind.NoClosedLoop && i.IsError);
        Assert.Equal(2, network.DanglingNodes.Count());
        Assert.Equal(2, network.Issues.Count(i => i.Kind == NetworkIssueKind.DanglingEnd));
    }

    [Fact]
    public void FailsWhenNothingSurvivesTheRepair()
    {
        var result = Repair(new[] { Seg(0, 0, 0, Mm(5), "SLIVER") });

        Assert.True(result.IsFailure);
        Assert.Equal("geometry.repair.empty", result.Error.Code);
        Assert.Contains("inputSegments=1", result.Error.TechnicalDetail);
    }

    [Fact]
    public void EveryRepairNamesItsSourceAndDistance()
    {
        var network = Succeeds(Repair(RectangleWithBottomGap(Mm(30))));

        Assert.NotEmpty(network.Repairs);
        Assert.All(network.Repairs, repair =>
        {
            Assert.NotEmpty(repair.Sources);
            Assert.True(repair.DistanceFeet >= 0);
            Assert.False(string.IsNullOrWhiteSpace(repair.Message));
        });
    }

    [Fact]
    public void LeavesTheInputSnapshotUnchanged()
    {
        var snapshot = SnapshotOf(RectangleWithBottomGap(Mm(30)));
        var before = snapshot.Segments.Select(s => (s.Start, s.End, s.Source)).ToList();

        new LineNetworkRepairer().Repair(snapshot);

        var after = snapshot.Segments.Select(s => (s.Start, s.End, s.Source)).ToList();
        Assert.Equal(before, after);
    }

    [Fact]
    public void ProducesTheSameNetworkOnASecondRun()
    {
        var segments = RectangleWithBottomGap(Mm(30));

        var first = Succeeds(Repair(segments));
        var second = Succeeds(Repair(segments));

        Assert.Equal(
            first.Edges.Select(e => (e.StartNodeId, e.EndNodeId, e.Start, e.End)),
            second.Edges.Select(e => (e.StartNodeId, e.EndNodeId, e.Start, e.End)));
        Assert.Equal(
            first.Nodes.Select(n => n.Position),
            second.Nodes.Select(n => n.Position));
        Assert.Equal(
            first.Repairs.Select(r => (r.Kind, r.DistanceFeet)),
            second.Repairs.Select(r => (r.Kind, r.DistanceFeet)));
    }

    [Fact]
    public void CarriesWallsStoppedAtACornerColumnToTheirIntersection()
    {
        var network = Succeeds(Repair(new[]
        {
            Seg(0, 0, Right - 0.3, 0, "BOTTOM"),
            Seg(Right, 0.3, Right, Top, "RIGHT"),
            Seg(Right, Top, 0, Top, "TOP"),
            Seg(0, Top, 0, 0, "LEFT")
        }.Concat(Column(Right, 0, 0.3, "C1")).ToArray()));

        Assert.Equal(4, network.Edges.Count);
        Assert.Equal(1, network.LoopCount);
        Assert.Contains(network.Nodes, n => n.Position.DistanceTo(new Point2D(Right, 0)) < 1e-9);
        Assert.All(network.Nodes, n => Assert.Equal(2, network.DegreeOf(n.Id)));
        Assert.DoesNotContain(network.Edges.SelectMany(e => e.Sources), s => s.Kind == GeometrySourceKind.ColumnOutline);
        Assert.Equal(2, network.RepairsOfKind(NetworkRepairKind.JoinedThroughColumn).Count());
    }

    [Fact]
    public void JoinsAWallRunInterruptedByAColumnIntoOneEdge()
    {
        var network = Succeeds(Repair(new[]
        {
            Seg(0, 0, 4.7, 0, "BOTTOM-A"),
            Seg(5.3, 0, Right, 0, "BOTTOM-B"),
            Seg(Right, 0, Right, Top, "RIGHT"),
            Seg(Right, Top, 0, Top, "TOP"),
            Seg(0, Top, 0, 0, "LEFT")
        }.Concat(Column(5, 0, 0.3, "C1")).ToArray()));

        Assert.Equal(4, network.Edges.Count);
        var bottom = network.Edges.Single(e => Math.Abs(e.Start.Y) < 1e-9 && Math.Abs(e.End.Y) < 1e-9);
        Assert.Equal(Right, Math.Abs(bottom.End.X - bottom.Start.X), 9);
    }

    [Fact]
    public void MeetsAPartitionAndAnInterruptedWallAtOneJunctionInsideTheColumn()
    {
        var network = Succeeds(Repair(new[]
        {
            Seg(0, 0, 4.7, 0, "BOTTOM-A"),
            Seg(5.3, 0, Right, 0, "BOTTOM-B"),
            Seg(Right, 0, Right, Top, "RIGHT"),
            Seg(Right, Top, 0, Top, "TOP"),
            Seg(0, Top, 0, 0, "LEFT"),
            Seg(5, 0.3, 5, Top, "PARTITION")
        }.Concat(Column(5, 0, 0.3, "C1")).ToArray()));

        var junction = network.Nodes.Single(n => n.Position.DistanceTo(new Point2D(5, 0)) < 1e-9);
        Assert.Equal(3, network.DegreeOf(junction.Id));
        Assert.Equal(7, network.Edges.Count);
    }

    [Fact]
    public void CarriesALoneStubThroughTheColumnToTheWallPassingIt()
    {
        var network = Succeeds(Repair(Rectangle()
            .Concat(new[] { Seg(5, 0.3, 5, Top, "PARTITION") })
            .Concat(Column(5, 0, 0.3, "C1"))
            .ToArray()));

        var junction = network.Nodes.Single(n => n.Position.DistanceTo(new Point2D(5, 0)) < 1e-9);
        Assert.Equal(3, network.DegreeOf(junction.Id));
    }

    [Fact]
    public void NeverTurnsAColumnOutlineIntoAnEdge()
    {
        var network = Succeeds(Repair(Rectangle().Concat(Column(5, 5, 0.3, "FREE")).ToArray()));

        Assert.Equal(4, network.Edges.Count);
        Assert.Empty(network.RepairsOfKind(NetworkRepairKind.JoinedThroughColumn));
    }

    [Fact]
    public void FailsWhenOnlyColumnsWereExtracted()
    {
        var result = Repair(Column(5, 5, 0.3, "ONLY").ToArray());

        Assert.True(result.IsFailure);
    }

    private static Segment2D[] Rectangle() => new[]
    {
        Seg(0, 0, Right, 0, "BOTTOM"),
        Seg(Right, 0, Right, Top, "RIGHT"),
        Seg(Right, Top, 0, Top, "TOP"),
        Seg(0, Top, 0, 0, "LEFT")
    };

    private static Segment2D[] RectangleWithBottomGap(double gapFeet) => new[]
    {
        Seg(0, 0, Right - gapFeet, 0, "BOTTOM"),
        Seg(Right, 0, Right, Top, "RIGHT"),
        Seg(Right, Top, 0, Top, "TOP"),
        Seg(0, Top, 0, 0, "LEFT")
    };

    private static Segment2D Seg(double x1, double y1, double x2, double y2, string element) =>
        new Segment2D(
            new Point2D(x1, y1),
            new Point2D(x2, y2),
            new SourceRef("doc", element, GeometrySourceKind.WallCenterline));

    // A square column outline centred on (x, y), closed the way the extractor adds it.
    private static Segment2D[] Column(double x, double y, double half, string element)
    {
        var source = new SourceRef("doc", element, GeometrySourceKind.ColumnOutline);
        var corners = new[]
        {
            new Point2D(x - half, y - half),
            new Point2D(x + half, y - half),
            new Point2D(x + half, y + half),
            new Point2D(x - half, y + half)
        };
        return corners.Select((c, i) => new Segment2D(c, corners[(i + 1) % corners.Length], source)).ToArray();
    }

    private static double Mm(double millimeters) => PlanUnits.MillimetersToFeet(millimeters);

    private static PlanGeometrySnapshot SnapshotOf(Segment2D[] segments) =>
        new PlanGeometrySnapshot(PackageId, "host-doc", "level-1", segments, Tolerance);

    private static Domain.Common.Result<PlanLineNetwork> Repair(Segment2D[] segments) =>
        new LineNetworkRepairer().Repair(SnapshotOf(segments), new DateTime(2026, 9, 21, 0, 0, 0, DateTimeKind.Utc));

    private static PlanLineNetwork Succeeds(Domain.Common.Result<PlanLineNetwork> result)
    {
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.ToString() : string.Empty);
        return result.Value;
    }
}
