# Agent Handoff
- Phase: P2
- Completed Task: P2-T05
- Next Task: P2-T06
- Status: READY_FOR_NEW_SESSION
- Commit: eef9f9b
- Spec Version: Draft v1.1 (`docs/fire-review-spec.md`)

## Completed
- `ZoneColor` / `ZoneColorPalette` (Domain): a plain RGB value plus ten categorical colours handed out in order, so a zone colour travels from the Editor to the Area Color Scheme in P2-T08 without WPF or Revit owning the type.
- `ZoneDraft` / `ZoneDraftSet` (Domain): the 區劃 drafts of one package, immutable, holding spec 10.3's rule that a solved face belongs to one zone at a time. `Assign` moves a face out of the zone that held it and reports the move; names are unique ignoring case.
- `EditorViewport` (Application): model feet (Y up) to screen pixels (Y down), immutable. `ZoomAt` pins the model point under the cursor, `FitTo` frames the plan with padding, zoom limits clamp instead of failing.
- `RegionEditorView` (Application): one redraw's worth of screen-space data — faces with rings and fill, zone labels with draft area, holes and contiguous part count, repair and solve issues merged into one list, and the summary line.
- `RegionEditorSession` (Application): the Editor itself. Left click adds the face under the cursor to the active zone, right click takes it out, rubber band and Ctrl-click select, zones are created, renamed, recoloured and deleted, and every face traces back to its source elements. Every operation returns a `Result` whose message is what the status bar shows.
- WPF shell (`src/BuildingRegulationReview/RegionEditor/`): canvas, window, name prompt, colour picker and package picker, all code-behind in the project's existing style.
- `RegionEditorCommand` + ribbon button: extraction, repair and solving, read-only, then the Editor. Source elements are selected back in Revit through an `ExternalEvent`.

## Changed Files
- `src/BuildingRegulationReview.Domain/Regions/ZoneColor.cs`
- `src/BuildingRegulationReview.Domain/Regions/ZoneDraft.cs`
- `src/BuildingRegulationReview.Domain/Regions/ZoneDraftSet.cs`
- `src/BuildingRegulationReview.Application/RegionEditing/EditorViewport.cs`
- `src/BuildingRegulationReview.Application/RegionEditing/RegionEditorView.cs`
- `src/BuildingRegulationReview.Application/RegionEditing/RegionEditorSession.cs`
- `src/BuildingRegulationReview/RegionEditor/RegionEditorCanvas.cs`
- `src/BuildingRegulationReview/RegionEditor/RegionEditorWindow.cs`
- `src/BuildingRegulationReview/RegionEditor/RegionEditorDialogs.cs`
- `src/BuildingRegulationReview/RegionEditorCommand.cs`
- `src/BuildingRegulationReview/App.cs`
- `tests/BuildingRegulationReview.Core.Tests/Regions/ZoneDraftSetTests.cs`
- `tests/BuildingRegulationReview.Core.Tests/RegionEditing/EditorViewportTests.cs`
- `tests/BuildingRegulationReview.Core.Tests/RegionEditing/RegionEditorSessionTests.cs`
- `docs/agent/p2-t05-region-editor.md`
- `docs/agent/phase-state.yaml`
- `docs/agent/HANDOFF.md`

## Decisions and Assumptions
- Every interaction rule lives in the Application layer. The session decides what a gesture means and the view decides what appears; WPF only draws and forwards events. That is what lets the whole exit criteria be asserted by tests with no window open.
- Unique ownership is enforced by moving and reporting, not by refusing. Refusing would make the user walk back to the old zone first; moving silently would make a zone lose a face for no visible reason.
- Drafts are immutable values. P2-T06 can keep past `ZoneDraftSet` values for Undo/Redo without inverse operations. Zoom, pan and selection are deliberately outside that history because they do not edit the draft.
- Rubber band selection is crossing-style with three cheap rules: the band holds the representative point, holds a boundary vertex, or lies inside the face. The third makes a small band inside a large room still pick that room.
- Left click adds (spec 10.3), Ctrl+left click toggles selection, dragging is always a band, right click removes, and panning is middle-drag or Alt+drag — right click already has a meaning.
- Repair issues and solve issues are shown as one list, errors first: to the reviewer both answer the same question, and both carry source elements.
- The launch command runs the full pipeline read-only. Without it the manual checklist could not be run at all; write-back stays in P2-T07.
- Zone names are unique ignoring case, because a name reaches the Area parameters where duplicates cannot be told apart. Traceability still rides on Package ID and Zone ID.

## Verification Results
- Solution build: 0 warnings, 0 errors. The Revit add-in project (`src/BuildingRegulationReview/`) is not in the .sln and was rebuilt separately: 0 warnings, 0 errors.
- Core tests: 247/247 passed (160 before this task; 87 added — `ZoneDraftSetTests` 27, `EditorViewportTests` 18, `RegionEditorSessionTests` 42).
- Covered by tests: face and hole drawing, fills and selection state, zone labels with draft area, hole count and disjoint parts, the summary line, zoom around the cursor, fit and zoom-to-zone, resize, click and band selection in replace / add / toggle modes, all left- and right-click outcomes including the no-active-zone, nothing-here, already-in-zone and not-assigned messages, automatic zone naming and colouring, duplicate names, rename, recolour, delete with the list position kept, and source element lookup.
- Headless run against the built DLLs (`scratchpad/headless-editor.ps1`, four rooms with an island and a stub wall): 5 faces, 5 adjacencies, 1 issue; the full click sequence produced the expected Chinese messages and error codes, and the summary reported 222.97 m² assigned — exactly the whole 60x40 ft plan, so hole subtraction and the island face do not double count.
- The WPF window could not be rendered locally: `MS.Internal.FontCache.Util` fails to initialize in this environment (`UriFormatException`), so no `System.Windows.Window` can be constructed at all. The window layer is covered by the manual checklist in `docs/agent/p2-t05-region-editor.md` instead.

## Known Issues / Risks
- Drafts live in memory only. Persistence, Undo/Redo and the prompt for unapplied changes are P2-T06; write-back is P2-T07.
- A non-contiguous zone is only reported ("n 塊不相連"). Spec 10.3's explicit confirmation is still missing (P2-T06).
- `BuildView()` recomputes every face's screen coordinates on each redraw and does no viewport culling. Fine for a floor; large plans are a P2-T09 measurement.
- The WPF window has never run for real. Its rendering and event wiring passed compilation and review only — the manual checklist has to be executed in Revit.
- The end-to-end run against `建築防火檢討1.rvt` is still outstanding from P2-T02. `RegionEditorCommand` now provides the entry point for it.
- The Editor cannot draw auxiliary lines: a gap in the network still has to be fixed in Revit and the editor reopened.
- Face IDs come from one solve. After a re-solve an old draft's IDs can be stale; `ZoneDraftSet.RetainFaces` is ready for that cleanup but nothing calls it yet.

## Exact Next Steps
- Begin P2-T06: Editor state and difference preview — Undo/Redo, unique ownership, non-contiguous warning, unapplied-changes prompt, and the Add/Update/Delete preview (spec 10.3, 10.4).
- Exit criteria: state replay is stable and the difference covers only the current package.
- Build on what is there: `ZoneDraftSet` is an immutable value, so an Undo stack is a list of past sets plus the active zone ID; `MultiFaceRegion.ContiguousPartCount` (via `ZoneVisual.ContiguousPartCount`) is the non-contiguous signal that now needs a confirmation step; `ZoneDraftSet.RetainFaces` is the hook for a re-solve; `RegionEditorSession.Zones` is the single place state changes.

## Do Not Do
- Do not change `PlanGeometrySnapshot.CurrentSchemaVersion` or the storage record field set without a migration path.
- Do not introduce Revit or WPF types into the Domain or Application layers.
- Do not put interaction rules in the WPF layer; the session decides, the canvas draws.
- Do not resolve an ambiguous region by picking one interpretation, and do not define new epsilons; tolerances come from `GeometryTolerance`.
- Do not let the editor merge non-contiguous faces silently; spec 10.3 requires explicit confirmation.
- Do not write to the model before P2-T07, and keep extraction, repair and solving transaction-free.
- Do not stage unrelated user or generated files: `.gitignore`, `src/BuildingRegulationReview/bin`, `obj` output and `.gtoffice/` must stay uncommitted.
