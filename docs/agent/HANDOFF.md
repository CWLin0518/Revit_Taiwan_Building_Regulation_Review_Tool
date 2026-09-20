# Agent Handoff
- Phase: P2
- Completed Task: P2-T04
- Next Task: P2-T05
- Status: READY_FOR_NEW_SESSION
- Commit: 7c9bae2
- Spec Version: Draft v1.1 (`docs/fire-review-spec.md`)

## Completed
- `RingGeometry` (Domain): ring-level plane geometry — signed area (shoelace), point-in-ring by crossing number with the half-open rule on Y, and an interior point (centroid first, horizontal band scan when the centroid falls outside). It sits in the Domain so a solved face can hit-test itself.
- `PlanRegionMap` (Domain): every enclosed area one package defines. `PlanFace` carries its outer boundary, holes, `Region2D` draft area and a representative point; `FaceBoundary` keeps the node IDs and edge indices the loop ran through so a boundary stays traceable to the model; `FaceAdjacency` records the shared length between two faces; `MultiFaceRegion` is spec 10.3's MultiPolygon and reports how many contiguous parts a chosen set of faces falls into; `RegionIssue` is what the solve resolved but a reviewer should still see.
- `RegionSolver` (Application): `Result<PlanRegionMap> Solve(PlanLineNetwork, DateTime?)`. Checks planarity, prunes dangling edges, traces faces with half-edges, assigns holes by smallest containing face, builds adjacency from the two sides of each edge, and reports draft areas.

## Changed Files
- `src/BuildingRegulationReview.Domain/Geometry/RingGeometry.cs`
- `src/BuildingRegulationReview.Domain/Geometry/PlanRegionMap.cs`
- `src/BuildingRegulationReview.Application/Geometry/RegionSolver.cs`
- `tests/BuildingRegulationReview.Core.Tests/Geometry/RegionSolverTests.cs`
- `tests/BuildingRegulationReview.Core.Tests/Geometry/RingGeometryTests.cs`
- `docs/agent/p2-t04-region-solving.md`
- `docs/agent/phase-state.yaml`
- `docs/agent/HANDOFF.md`

## Decisions and Assumptions
- Inside and outside come from the traversal, never from a bounding box. At each node the walk turns onto the clockwise-next neighbour, so loops that enclose area come out counter-clockwise and each connected part's surrounding loop comes out clockwise.
- A clockwise loop becomes a hole of the smallest face that contains it, chosen only among faces of other connected parts. If two candidates are within a sliver's area of each other, that is ambiguity and the solve fails.
- Failure is reserved for cases that would need an interpretation: `geometry.regions.non-planar`, `geometry.regions.ambiguous-nesting`, `geometry.regions.degenerate-face`, `geometry.regions.no-closed-loop`. Everything resolved without a choice travels with the map as a `RegionIssue`, matching how P2-T03 split failures from issues.
- Sliver faces (under a snap-tolerance square, 10 mm x 10 mm) are reported, not dropped: removing one would leave a gap in the adjacency graph.
- Dangling edges are pruned before tracing, each with a warning naming its source. Left in, the walk would run out and back along them and leave meaningless spikes on the boundary.
- Faces are ordered by their node sequence rotated to start at the lowest node ID, so one network always yields the same face IDs.
- Contiguity is reported, not enforced. `MultiFaceRegion.ContiguousPartCount` tells the Editor what it needs; spec 10.3's "no non-contiguous merge unless confirmed" is a P2-T06 policy.

## Verification Results
- Solution build: 0 warnings, 0 errors, including the net48 Revit project.
- Core tests: 160/160 passed (129 before this task; 31 added — `RegionSolverTests` 21, `RingGeometryTests` 10).
- Golden cases: clean rectangle (1 face, 100 sq ft, 40 ft perimeter, no adjacency, no issues); partitioned rectangle (2 faces of 50 sq ft, one adjacency of 10 ft over one edge); island (surrounding face 375 sq ft with one clockwise 25 sq ft hole, island 25 sq ft, adjacency 20 ft over 4 edges, and the surrounding face does not claim a point inside the island); three concentric rings (500 / 300 / 100 sq ft, innermost ring becomes the middle face's hole); L-shaped room (centroid outside, 64 sq ft, representative point genuinely inside).
- Pruning: one free end, a chain of two free ends, and a whole part that closes nothing each produce warnings while the room still solves.
- Determinism: solving one network twice produces identical face IDs, areas, representative points, hole counts, adjacencies and issue messages.
- Ambiguity: unsplit crossing, two parts 5 mm apart (message quotes the distance), two edges joining one pair of nodes, and nothing enclosing an area each return an error instead of a guess.

## Known Issues / Risks
- Not yet run against a real model. Extractor to repairer to solver has never been exercised end to end; the read-only run against `建築防火檢討1.rvt` is still outstanding from P2-T02.
- `PlanRegionMap` has no storage record. Solved faces live in memory only; persisting them is a P2-T06 / P2-T07 decision and must not change the existing `PlanGeometrySnapshot` field set.
- The planarity check is O(n^2) with a bounding-box prefilter, the same order as the repair. Performance on a large plan is a P2-T09 measurement.
- Endpoints created by view-extent clipping are dangling ends after repair and get pruned here, so a zone does not close along the clip boundary. The user has to add an auxiliary line where that matters.
- The sliver threshold and the ambiguous-nesting distance are not wired to any settings UI.

## Exact Next Steps
- Begin P2-T05: Region Editor basic interaction — display, zoom, selection, left click to add and right click to remove, create / delete a zone draft, name and colour (spec 10.3).
- Exit criteria: UI tests or a reproducible manual test checklist pass.
- Build on what is already there: `PlanRegionMap.FaceAt` turns a click into a face, `PlanFace.RepresentativePoint` gives each face a stable label anchor, `NeighboursOf` / `AreAdjacent` answer which faces touch, and `Combine` reports a zone's draft area and whether it is contiguous. `RegionIssue` and `PlanLineNetwork.Issues` are what the editor has to surface as unclosed-boundary errors.

## Do Not Do
- Do not change `PlanGeometrySnapshot.CurrentSchemaVersion` or the storage record field set without a migration path.
- Do not introduce Revit types into the Domain or Application layers.
- Do not resolve an ambiguous region by picking one interpretation, and do not add automatic filleting or unlimited gap closing (spec 10.2 non-goal).
- Do not define new epsilons; read tolerances from `GeometryTolerance`.
- Do not let the editor merge non-contiguous faces silently; spec 10.3 requires explicit confirmation.
- Do not stage unrelated user or generated files: `.gitignore`, `src/BuildingRegulationReview/bin`, `obj` output and `.gtoffice/` must stay uncommitted.
