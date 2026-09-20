# Agent Handoff
- Phase: P2
- Completed Task: P2-T03
- Next Task: P2-T04
- Status: READY_FOR_NEW_SESSION
- Commit: 04799be
- Spec Version: Draft v1.1 (`docs/fire-review-spec.md`)

## Completed
- `PlanLineNetwork` (Domain): the repaired plan as a graph — nodes, edges, the repair log and the issue list, plus the tolerance and scope it came from. Exposes `DegreeOf`, `EdgesAt`, `DanglingNodes`, `ComponentCount`, `LoopCount` (E - V + C) and `HasClosedLoop`.
- `NetworkRepair` (Domain): one repair log entry, holding the original geometry, the repair kind, the distance moved and the source elements, as spec 10.2 requires.
- `NetworkIssue` (Domain): what the pipeline refused to guess at, with a severity and the plan location the reviewer has to look at.
- `SegmentGeometry` (Application): the plane geometry every repair step measures with — segment intersection with tolerance slack, point-to-segment and point-to-line distance, axis projection, ray-to-segment hit, collinear overlap.
- `LineNetworkRepairer` (Application): spec 10.2 in order over a `PlanGeometrySnapshot` — de-duplicate, split at intersections, snap endpoints, extend short gaps, merge collinear runs, detect closure. Returns `Result<PlanLineNetwork>`; only an empty result fails, with `geometry.repair.empty`.

## Changed Files
- `src/BuildingRegulationReview.Domain/Geometry/PlanLineNetwork.cs`
- `src/BuildingRegulationReview.Application/Geometry/SegmentGeometry.cs`
- `src/BuildingRegulationReview.Application/Geometry/LineNetworkRepairer.cs`
- `tests/BuildingRegulationReview.Core.Tests/Geometry/LineNetworkRepairerTests.cs`
- `tests/BuildingRegulationReview.Core.Tests/Geometry/SegmentGeometryTests.cs`
- `docs/agent/p2-t03-line-network-repair.md`
- `docs/agent/phase-state.yaml`
- `docs/agent/HANDOFF.md`

## Verification Results
- Solution build: 0 warnings, 0 errors, including the net48 Revit project.
- Core tests: 129/129 passed (97 before this task; 32 added).
- Normal: a clean rectangle needs no repair at all; a rectangle divided by an interior wall keeps both compartments (7 edges, 6 nodes, 2 loops); an island inside a room stays its own component (2 components, 2 loops).
- Short gap: a 5 mm shortfall onto a wall interior is resolved by splitting and snapping with no extension logged; a 30 mm gap is closed by extending along the segment direction and the rectangle closes.
- Over tolerance: a 150 mm gap is not extended; it becomes a `GapBeyondTolerance` error quoting the measured distance and the tolerance, and the network reports that nothing encloses an area.
- Self-intersection: two pieces of one element crossing are split so the network stays planar, and a `SelfIntersection` warning names the crossing point.
- Other: duplicate merging keeps both sources; collinear merging never crosses a degree-3 junction; pieces shorter than the snap tolerance are dropped with a warning; the input snapshot is not mutated; two runs over one snapshot produce identical nodes, edges and repairs.

## Known Issues / Risks
- Not yet run against a real model. The extractor output has never been fed into the repairer end to end; the read-only run against `建築防火檢討1.rvt` is still outstanding from P2-T02.
- `PlanLineNetwork` has no storage record. The repair log lives in memory only; persisting it for the review report or the audit trail is a P2-T06 / P2-T07 decision and must not change the existing `PlanGeometrySnapshot` field set.
- Gap extension is a single pass: after extending one dangling end it does not revisit ends already processed. Chained gaps (B only becomes reachable once A is closed) are left to the user.
- Endpoints created by view-extent clipping look like ordinary dangling ends and usually surface as `DanglingEnd` warnings. Whether a zone should close along the clip boundary is still a P2-T04 decision.
- `GapReportingFactor` (10x the extension tolerance = 500 mm) is provisional, like the tolerances themselves, and is not wired to any settings UI.

## Exact Next Steps
- Begin P2-T04: solve closed loops and regions from `PlanLineNetwork` — loops, holes, MultiPolygon, adjacency and draft areas (spec 10.2 closing step and 10.3).
- Exit criteria: geometry golden tests pass; ambiguous cases produce an error rather than a guess.
- Build on what is already there: `LoopCount` / `ComponentCount` state how many independent closed areas and connected components exist, `EdgesAt` / `DegreeOf` give the adjacency queries face tracing needs, and `Loop2D` / `Region2D` (P2-T01) already provide signed area and hole subtraction.

## Do Not Do
- Do not change `PlanGeometrySnapshot.CurrentSchemaVersion` or the storage record field set without a migration path.
- Do not introduce Revit types into the Domain or Application layers.
- Do not add automatic filleting or unlimited gap closing (spec 10.2 non-goal), and do not resolve an ambiguous region by picking one interpretation.
- Do not define new epsilons; read tolerances from `GeometryTolerance`.
- Do not stage unrelated user or generated files: `.gitignore`, `src/BuildingRegulationReview/bin`, `obj` output and `.gtoffice/` must stay uncommitted.
