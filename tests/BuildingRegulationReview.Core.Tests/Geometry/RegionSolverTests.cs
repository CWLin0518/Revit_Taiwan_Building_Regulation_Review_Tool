using System;
using System.Collections.Generic;
using System.Linq;
using BuildingRegulationReview.Application.Geometry;
using BuildingRegulationReview.Domain.Common;
using BuildingRegulationReview.Domain.Geometry;
using Xunit;

namespace BuildingRegulationReview.Core.Tests.Geometry;

public class RegionSolverTests
{
    private static readonly Guid PackageId = Guid.Parse("11111111-2222-3333-4444-555555555555");
    private static readonly GeometryTolerance Tolerance = GeometryTolerance.Default;
    private static readonly DateTime SolvedAt = new DateTime(2026, 9, 21, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void SolvesOneRoomFromACleanRectangle()
    {
        var map = Succeeds(Solve(Rectangle(0, 0, 10, 10, "R")));

        var face = Assert.Single(map.Faces);
        Assert.Equal(0, face.Id);
        Assert.Equal(100.0, face.NetAreaSquareFeet, 9);
        Assert.Equal(40.0, face.PerimeterFeet, 9);
        Assert.False(face.HasHoles);
        Assert.True(face.Outer.IsCounterClockwise);
        Assert.Empty(map.Adjacencies);
        Assert.Empty(map.Issues);
        Assert.True(face.Contains(face.RepresentativePoint));
    }

    [Fact]
    public void KeepsTheDraftAreaInBothUnits()
    {
        var face = Assert.Single(Succeeds(Solve(Rectangle(0, 0, 10, 10, "R"))).Faces);

        Assert.Equal(PlanUnits.SquareFeetToSquareMeters(100.0), face.NetAreaSquareMeters, 9);
    }

    [Fact]
    public void SolvesTwoRoomsSplitByAPartitionAndRecordsTheSharedWall()
    {
        var map = Succeeds(Solve(Rectangle(0, 0, 10, 10, "R").Concat(new[] { Seg(5, 0, 5, 10, "PART") })));

        Assert.Equal(2, map.Faces.Count);
        Assert.All(map.Faces, f => Assert.Equal(50.0, f.NetAreaSquareFeet, 9));
        Assert.Equal(100.0, map.TotalNetAreaSquareFeet, 9);

        var left = Assert.IsType<PlanFace>(map.FaceAt(new Point2D(2.5, 5)));
        var right = Assert.IsType<PlanFace>(map.FaceAt(new Point2D(7.5, 5)));
        Assert.NotEqual(left.Id, right.Id);

        var adjacency = Assert.Single(map.Adjacencies);
        Assert.True(adjacency.Touches(left.Id));
        Assert.True(adjacency.Touches(right.Id));
        Assert.Equal(10.0, adjacency.SharedLengthFeet, 9);
        Assert.Single(adjacency.SharedEdgeIndices);
        Assert.True(map.AreAdjacent(left.Id, right.Id));
        Assert.Equal(right.Id, Assert.Single(map.NeighboursOf(left.Id)).Id);
    }

    [Fact]
    public void TurnsAnIslandIntoAHoleOfTheFaceAroundIt()
    {
        var map = Succeeds(Solve(Rectangle(0, 0, 20, 20, "OUT").Concat(Rectangle(5, 5, 5, 5, "IN"))));

        Assert.Equal(2, map.Faces.Count);

        var surrounding = Assert.IsType<PlanFace>(map.FaceAt(new Point2D(1, 1)));
        var island = Assert.IsType<PlanFace>(map.FaceAt(new Point2D(7.5, 7.5)));

        Assert.Equal(375.0, surrounding.NetAreaSquareFeet, 9);
        Assert.Single(surrounding.Holes);
        Assert.False(surrounding.Holes[0].IsCounterClockwise);
        Assert.Equal(25.0, surrounding.Holes[0].AreaSquareFeet, 9);

        Assert.Equal(25.0, island.NetAreaSquareFeet, 9);
        Assert.False(island.HasHoles);

        // The island is inside the hole, so the surrounding face must not claim that point.
        Assert.False(surrounding.Contains(new Point2D(7.5, 7.5)));

        var adjacency = Assert.Single(map.Adjacencies);
        Assert.Equal(20.0, adjacency.SharedLengthFeet, 9);
        Assert.Equal(4, adjacency.SharedEdgeIndices.Count);
    }

    [Fact]
    public void NestsThreeRingsFromTheInsideOut()
    {
        var map = Succeeds(Solve(Rectangle(0, 0, 30, 30, "A")
            .Concat(Rectangle(5, 5, 20, 20, "B"))
            .Concat(Rectangle(10, 10, 10, 10, "C"))));

        Assert.Equal(3, map.Faces.Count);

        var outer = Assert.IsType<PlanFace>(map.FaceAt(new Point2D(1, 1)));
        var middle = Assert.IsType<PlanFace>(map.FaceAt(new Point2D(6, 6)));
        var inner = Assert.IsType<PlanFace>(map.FaceAt(new Point2D(15, 15)));

        Assert.Equal(900.0 - 400.0, outer.NetAreaSquareFeet, 9);
        Assert.Equal(400.0 - 100.0, middle.NetAreaSquareFeet, 9);
        Assert.Equal(100.0, inner.NetAreaSquareFeet, 9);

        // The innermost ring must become the middle face's hole, never the outer face's.
        Assert.Equal(100.0, Assert.Single(middle.Holes).AreaSquareFeet, 9);
        Assert.Equal(400.0, Assert.Single(outer.Holes).AreaSquareFeet, 9);
    }

    [Fact]
    public void SolvesAnLShapedRoomWhoseCentroidIsOutsideIt()
    {
        var map = Succeeds(Solve(new[]
        {
            Seg(0, 0, 10, 0, "S"),
            Seg(10, 0, 10, 4, "E"),
            Seg(10, 4, 4, 4, "N1"),
            Seg(4, 4, 4, 10, "E2"),
            Seg(4, 10, 0, 10, "N2"),
            Seg(0, 10, 0, 0, "W")
        }));

        var face = Assert.Single(map.Faces);
        Assert.Equal(64.0, face.NetAreaSquareFeet, 9);
        Assert.True(face.Contains(face.RepresentativePoint));
        Assert.True(face.Contains(new Point2D(1, 1)));
        Assert.False(face.Contains(new Point2D(8, 8)));
    }

    [Fact]
    public void PrunesAFreeEndAndStillSolvesTheRoom()
    {
        var map = Succeeds(Solve(Rectangle(0, 0, 10, 10, "R").Concat(new[] { Seg(5, 10, 5, 7, "STUB") })));

        var face = Assert.Single(map.Faces);
        Assert.Equal(100.0, face.NetAreaSquareFeet, 9);

        var pruned = Assert.Single(map.Issues, i => i.Kind == RegionIssueKind.DanglingEdgePruned);
        Assert.Equal(NetworkIssueSeverity.Warning, pruned.Severity);
        Assert.Contains(pruned.Sources, s => s.ElementUniqueId == "STUB");
        Assert.False(map.HasErrors);
    }

    [Fact]
    public void PrunesAWholeChainOfFreeEnds()
    {
        // Removing the outer stub frees the one behind it, so pruning has to repeat.
        var map = Succeeds(Solve(Rectangle(0, 0, 10, 10, "R").Concat(new[]
        {
            Seg(5, 10, 5, 13, "STUB1"),
            Seg(5, 13, 8, 13, "STUB2")
        })));

        Assert.Single(map.Faces);
        Assert.Equal(2, map.Issues.Count(i => i.Kind == RegionIssueKind.DanglingEdgePruned));
    }

    [Fact]
    public void ReportsAPartOfThePlanThatClosesNothing()
    {
        var map = Succeeds(Solve(Rectangle(0, 0, 10, 10, "R").Concat(new[]
        {
            Seg(30, 0, 40, 0, "LOOSE1"),
            Seg(40, 0, 40, 5, "LOOSE2")
        })));

        Assert.Single(map.Faces);
        var open = Assert.Single(map.Issues, i => i.Kind == RegionIssueKind.OpenComponent);
        Assert.Equal(NetworkIssueSeverity.Warning, open.Severity);
    }

    [Fact]
    public void FlagsASliverAsAnIssueWithoutDroppingIt()
    {
        // 1 ft base, 0.002 ft high: under a snap-tolerance square, so it is a near-overlap artefact.
        var map = Succeeds(SolveNetwork(NetworkOf(
            (new Point2D(0, 0), new Point2D(1, 0)),
            (new Point2D(1, 0), new Point2D(0.5, 0.002)),
            (new Point2D(0.5, 0.002), new Point2D(0, 0)))));

        var face = Assert.Single(map.Faces);
        var sliver = Assert.Single(map.Issues, i => i.Kind == RegionIssueKind.SliverFace);
        Assert.Equal(face.Id, sliver.FaceId);
        Assert.True(face.NetAreaSquareFeet <= RegionSolver.SliverAreaSquareFeet(Tolerance));
    }

    [Fact]
    public void FailsWhenNothingEnclosesAnArea()
    {
        var result = Solve(new[]
        {
            Seg(0, 0, 10, 0, "A"),
            Seg(10, 0, 10, 10, "B")
        });

        Assert.True(result.IsFailure);
        Assert.Equal("geometry.regions.no-closed-loop", result.Error.Code);
    }

    [Fact]
    public void FailsWhenTwoEdgesStillCrossAwayFromANode()
    {
        // Built by hand: a repaired network never looks like this, and if one ever does the solver
        // must not pick a side of the crossing for the user.
        var result = SolveNetwork(NetworkOf(
            (new Point2D(-5, 0), new Point2D(5, 0)),
            (new Point2D(0, -5), new Point2D(0, 5))));

        Assert.True(result.IsFailure);
        Assert.Equal("geometry.regions.non-planar", result.Error.Code);
    }

    [Fact]
    public void FailsWhenTwoPartsAlmostTouchSoNestingIsAmbiguous()
    {
        // The island's left wall sits 5 mm from the outer wall: too close to say whether the strip
        // between them is part of the surrounding room or a separate one.
        var gap = PlanUnits.MillimetersToFeet(5.0);
        var result = SolveNetwork(NetworkOf(
            RectangleEdges(0, 0, 20, 20)
                .Concat(RectangleEdges(gap, 5, 5, 5))
                .ToArray()));

        Assert.True(result.IsFailure);
        Assert.Equal("geometry.regions.ambiguous-nesting", result.Error.Code);
        Assert.Contains("mm", result.Error.Message);
    }

    [Fact]
    public void FailsWhenTwoEdgesJoinTheSamePairOfNodes()
    {
        var network = NetworkOf(
            (new Point2D(0, 0), new Point2D(10, 0)),
            (new Point2D(10, 0), new Point2D(0, 0)));

        var result = SolveNetwork(network);

        Assert.True(result.IsFailure);
        Assert.Equal("geometry.regions.degenerate-face", result.Error.Code);
    }

    [Fact]
    public void CarriesThePackageIdentityThrough()
    {
        var map = Succeeds(Solve(Rectangle(0, 0, 10, 10, "R")));

        Assert.Equal(PackageId, map.PackageId);
        Assert.Equal("host-doc", map.HostDocumentUniqueId);
        Assert.Equal("level-1", map.LevelUniqueId);
        Assert.Equal(SolvedAt, map.SolvedAtUtc);
        Assert.Same(Tolerance, map.Tolerance);
    }

    [Fact]
    public void KeepsEveryFaceTraceableToItsSourceElements()
    {
        var map = Succeeds(Solve(Rectangle(0, 0, 10, 10, "R").Concat(new[] { Seg(5, 0, 5, 10, "PART") })));

        var left = Assert.IsType<PlanFace>(map.FaceAt(new Point2D(2.5, 5)));
        Assert.Contains(left.DistinctSources, s => s.ElementUniqueId == "PART");
        Assert.Contains(left.DistinctSources, s => s.ElementUniqueId == "R-W");
        Assert.All(left.Outer.EdgeIndices, i => Assert.InRange(i, 0, int.MaxValue));
    }

    [Fact]
    public void ReportsContiguityWhenFacesAreCombinedIntoOneZone()
    {
        var map = Succeeds(Solve(Rectangle(0, 0, 15, 10, "R").Concat(new[]
        {
            Seg(5, 0, 5, 10, "P1"),
            Seg(10, 0, 10, 10, "P2")
        })));

        Assert.Equal(3, map.Faces.Count);
        var left = Assert.IsType<PlanFace>(map.FaceAt(new Point2D(2.5, 5)));
        var middle = Assert.IsType<PlanFace>(map.FaceAt(new Point2D(7.5, 5)));
        var right = Assert.IsType<PlanFace>(map.FaceAt(new Point2D(12.5, 5)));

        var joined = map.Combine(new[] { left.Id, middle.Id });
        Assert.True(joined.IsContiguous);
        Assert.Equal(1, joined.ContiguousPartCount);
        Assert.Equal(100.0, joined.NetAreaSquareFeet, 9);
        Assert.Equal(2, joined.Polygons.Count());

        var split = map.Combine(new[] { left.Id, right.Id });
        Assert.False(split.IsContiguous);
        Assert.Equal(2, split.ContiguousPartCount);
        Assert.Equal(100.0, split.NetAreaSquareFeet, 9);
    }

    [Fact]
    public void RefusesToCombineAFaceThatIsNotInTheMap()
    {
        var map = Succeeds(Solve(Rectangle(0, 0, 10, 10, "R")));

        Assert.Throws<ArgumentOutOfRangeException>(() => map.Combine(new[] { 7 }));
    }

    [Fact]
    public void ReportsNoFaceOutsideEveryBoundary()
    {
        var map = Succeeds(Solve(Rectangle(0, 0, 10, 10, "R")));

        Assert.Null(map.FaceAt(new Point2D(50, 50)));
    }

    [Fact]
    public void ProducesTheSameMapEveryRun()
    {
        var segments = Rectangle(0, 0, 20, 20, "OUT")
            .Concat(Rectangle(5, 5, 10, 10, "IN"))
            .Concat(new[] { Seg(0, 10, 5, 10, "SPUR") })
            .ToArray();

        var first = Succeeds(Solve(segments));
        var second = Succeeds(Solve(segments));

        Assert.Equal(
            first.Faces.Select(f => (f.Id, f.NetAreaSquareFeet, f.RepresentativePoint, f.Holes.Count)),
            second.Faces.Select(f => (f.Id, f.NetAreaSquareFeet, f.RepresentativePoint, f.Holes.Count)));
        Assert.Equal(
            first.Adjacencies.Select(a => (a.FirstFaceId, a.SecondFaceId, a.SharedLengthFeet)),
            second.Adjacencies.Select(a => (a.FirstFaceId, a.SecondFaceId, a.SharedLengthFeet)));
        Assert.Equal(
            first.Issues.Select(i => (i.Kind, i.Message)),
            second.Issues.Select(i => (i.Kind, i.Message)));
    }

    [Fact]
    public void RejectsANullNetwork()
    {
        Assert.Throws<ArgumentNullException>(() => new RegionSolver().Solve(null!));
    }

    private static Segment2D[] Rectangle(double x, double y, double width, double height, string prefix) => new[]
    {
        Seg(x, y, x + width, y, prefix + "-S"),
        Seg(x + width, y, x + width, y + height, prefix + "-E"),
        Seg(x + width, y + height, x, y + height, prefix + "-N"),
        Seg(x, y + height, x, y, prefix + "-W")
    };

    private static (Point2D Start, Point2D End)[] RectangleEdges(double x, double y, double width, double height) => new[]
    {
        (new Point2D(x, y), new Point2D(x + width, y)),
        (new Point2D(x + width, y), new Point2D(x + width, y + height)),
        (new Point2D(x + width, y + height), new Point2D(x, y + height)),
        (new Point2D(x, y + height), new Point2D(x, y))
    };

    private static Segment2D Seg(double x1, double y1, double x2, double y2, string element) =>
        new Segment2D(
            new Point2D(x1, y1),
            new Point2D(x2, y2),
            new SourceRef("doc", element, GeometrySourceKind.WallCenterline));

    private static Result<PlanRegionMap> Solve(IEnumerable<Segment2D> segments)
    {
        var snapshot = new PlanGeometrySnapshot(PackageId, "host-doc", "level-1", segments, Tolerance);
        var repaired = new LineNetworkRepairer().Repair(snapshot, SolvedAt);
        Assert.True(repaired.IsSuccess, repaired.IsFailure ? repaired.Error.ToString() : string.Empty);
        return SolveNetwork(repaired.Value);
    }

    private static Result<PlanRegionMap> SolveNetwork(PlanLineNetwork network) =>
        new RegionSolver().Solve(network, SolvedAt);

    // Builds a network straight from coordinates, bypassing the repair, so the guards that only fire
    // on geometry the repair would never emit can still be exercised.
    private static PlanLineNetwork NetworkOf(params (Point2D Start, Point2D End)[] edges)
    {
        var nodeIds = new Dictionary<Point2D, int>();
        var nodes = new List<NetworkNode>();
        var built = new List<NetworkEdge>();

        foreach (var (start, end) in edges)
        {
            built.Add(new NetworkEdge(
                NodeId(start),
                NodeId(end),
                start,
                end,
                new[] { new SourceRef("doc", $"E{built.Count}", GeometrySourceKind.WallCenterline) }));
        }

        return new PlanLineNetwork(PackageId, "host-doc", "level-1", nodes, built, Tolerance, null, null, null, SolvedAt);

        int NodeId(Point2D point)
        {
            if (nodeIds.TryGetValue(point, out var existing)) return existing;
            var id = nodes.Count;
            nodeIds[point] = id;
            nodes.Add(new NetworkNode(id, point));
            return id;
        }
    }

    private static PlanRegionMap Succeeds(Result<PlanRegionMap> result)
    {
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.ToString() : string.Empty);
        return result.Value;
    }
}
