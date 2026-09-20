# Agent Handoff
- Phase: P2
- Completed Task: P2-T02
- Next Task: P2-T03
- Status: READY_FOR_NEW_SESSION
- Commit: c03d4ed
- Spec Version: Draft v1.1 (`docs/fire-review-spec.md`)

## Completed
- `RevitPlanGeometryExtractor`: the read-only Revit implementation of `IPlanGeometryExtractor`. Resolves the Area Plan and its level, picks the extraction scope (scope box over crop region), derives the cut elevation from the view range, and reads wall location curves, column plan outlines and auxiliary lines. Opens no transaction.
- `RevitPlanShapeReader`: the only class that touches `XYZ`. Curve flattening (lines keep two points, arcs tessellate), column silhouettes via `ExtrusionAnalyzer`, plan extents from the eight transformed corners of a `BoundingBoxXYZ`.
- `RevitDocumentIdentity`: document provenance keyed on `ProjectInformation.UniqueId`, with path and title as fallbacks.
- `PlanGeometryBuilder` (Application): degenerate-geometry removal, view-extent clipping, provenance, warning de-duplication, discard accounting, and deterministic segment ordering. Empty extraction fails with `geometry.extraction.empty` instead of returning an empty snapshot.
- `PlanExtentClipper` (Application): Liang-Barsky clip that cuts boundary-crossing segments at the boundary rather than discarding them.
- `LinkGeometryPolicy` + `BasisVector` (Application): accepts a link transform only as a planar rigid transform; rejects mirrored, scaled, tilted and skewed links with distinct error codes.

## Changed Files
- `src/BuildingRegulationReview.Application/Geometry/PlanGeometryBuilder.cs`
- `src/BuildingRegulationReview.Application/Geometry/PlanExtentClipper.cs`
- `src/BuildingRegulationReview.Application/Geometry/LinkGeometryPolicy.cs`
- `src/BuildingRegulationReview.Revit/Geometry/RevitPlanGeometryExtractor.cs`
- `src/BuildingRegulationReview.Revit/Geometry/RevitPlanShapeReader.cs`
- `src/BuildingRegulationReview.Revit/Geometry/RevitDocumentIdentity.cs`
- `tests/BuildingRegulationReview.Core.Tests/Geometry/PlanGeometryBuilderTests.cs`
- `tests/BuildingRegulationReview.Core.Tests/Geometry/PlanExtentClipperTests.cs`
- `tests/BuildingRegulationReview.Core.Tests/Geometry/LinkGeometryPolicyTests.cs`
- `docs/agent/p2-t02-revit-geometry-extraction.md`
- `docs/agent/phase-state.yaml`
- `docs/agent/HANDOFF.md`

## Verification Results
- Solution build: 0 warnings, 0 errors, including the net48 Revit project against the Revit 2024 API.
- Core tests: 97/97 passed (66 before this task; 31 added).
- Clipping: inside, outside, bounding-box-overlap-only, one-end cut, both-ends cut, along-boundary and tolerance-edge cases all pass.
- Link transforms: unrotated, rotated + translated, point mapping, mirrored, scaled, tilted, upside-down and non-orthogonal cases all pass.
- Determinism: two builders fed the same elements in different orders produce identical segment sequences; segment order within one element's polyline is preserved.
- Provenance: every segment carries document / element / link UniqueId and `GeometrySourceKind`, and survives the storage round trip.

## Known Issues / Risks
- Not yet run against a real model. The "stable output for a fixed test model" exit criterion is currently covered only at unit-test level by the ordering invariant; it still needs a read-only run against `建築防火檢討1.rvt` to confirm element counts and traceability.
- Extraction has no command or UI entry point yet; wiring happens in P2-T05 or P2-T09.
- Clipping creates endpoints on the view boundary that match no model element. Whether a zone should close along that boundary is a P2-T03 / P2-T04 decision.
- Provisional values not yet wired to the setup UI: auxiliary line-style whitelist (empty = accept all), default tolerances (1 / 10 / 50 mm, 0.5 degrees), and the 50 mm link level-plane slack.

## Exact Next Steps
- Begin P2-T03: network normalisation and repair (spec 10.2), in this order — de-duplicate, split at intersections, snap endpoints, extend across short gaps, merge collinear runs, detect closed loops.
- Every repair must record the original geometry, the repair kind and the distance. Anything beyond tolerance is flagged as an error for the user, never guessed.
- Exit criteria: normal, short-gap, over-tolerance and self-intersecting cases all covered by tests.
- Repair consumes `PlanGeometrySnapshot` and must read its tolerances from `GeometryTolerance` rather than defining new epsilons.

## Do Not Do
- Do not change `PlanGeometrySnapshot.CurrentSchemaVersion` or the storage record field set without a migration path.
- Do not introduce Revit types into the Domain or Application layers.
- Do not add automatic filleting or unlimited gap closing (spec 10.2 non-goal).
- Do not stage unrelated user or generated files: `.gitignore`, `src/BuildingRegulationReview/bin`, `obj` output and `.gtoffice/` must stay uncommitted.
