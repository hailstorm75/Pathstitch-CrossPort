# Project Audit Task Backlog

Local implementation backlog derived from the Avalonia project audit. These are not GitHub issues.

Status values: `Todo`, `In progress`, `Blocked`, `Done`.

## Milestone 1: Mode-aware editor shell

### AUD-001 — Introduce an explicit editor mode

- **Priority:** P0
- **Status:** Done
- **Depends on:** None
- Add a root `EditorMode` model with `TwoD`, `ThreeD`, and a reserved `Batch` value.
- Make mode transitions explicit instead of deriving the active workspace from generated-output state.
- Preserve each workspace's active tool when switching modes.
- **Acceptance:** Mode changes are observable from the shell view model, and switching 2D → 3D → 2D restores the previously active 2D tool.

### AUD-002 — Split the monolithic editor view into workspace components

- **Priority:** P0
- **Status:** Done
- **Depends on:** AUD-001
- Extract `EditorShellView`, `EditorToolRail`, `Editor2DView`, `Editor3DView`, and `EditorInspectorHost` from `EditorPageView.axaml`.
- Keep viewport-only interaction and transient canvas controls inside workspace views.
- Move persistent tool and inspector panels out of viewport overlays.
- **Acceptance:** The shell owns layout and mode switching; neither workspace view contains the other workspace's controls.

### AUD-003 — Make the left context mode-aware

- **Priority:** P0
- **Status:** Done
- **Depends on:** AUD-001, AUD-002
- Show 2D tools and layers in 2D mode.
- Show Select, Move, and Plane/Projection tools plus bodies in 3D mode.
- Reserve a batch-action context for Batch mode.
- **Acceptance:** The 3D tool rail and Solid Bodies panel are absent in 2D mode, and the 2D tool rail is absent in 3D mode.

### AUD-004 — Move 2D tools and options out of viewport overlays

- **Priority:** P0
- **Status:** Done
- **Depends on:** AUD-002, AUD-003
- Remove the horizontal 2D tool `StackPanel` from the viewport overlay.
- Move persistent 2D selection and active-tool options into the shared rail/inspector regions.
- Retain only transient handles, previews, and inline numeric controls over the canvas.
- **Acceptance:** All 2D tools remain reachable at narrow window widths without clipping or horizontal overflow.

### AUD-005 — Scope inspectors to the active workspace

- **Priority:** P0
- **Status:** Done
- **Depends on:** AUD-001, AUD-002
- Gate every 3D inspector panel on 3D mode.
- Gate every 2D inspector panel on 2D mode.
- Remove visibility rules that depend only on a tool enum without checking the active mode.
- **Acceptance:** A 2D canvas can never be displayed with Selected Faces, Solid Bodies, Projection, or Unfold panels.

### AUD-006 — Add editor-shell regression tests

- **Priority:** P0
- **Status:** Done
- **Depends on:** AUD-003, AUD-004, AUD-005
- Add headless Avalonia tests for mode-specific rail, center view, and inspector visibility.
- Test active-tool preservation across mode changes.
- Add a narrow-window layout test for the 2D tool rail.
- **Acceptance:** Tests fail if 3D context leaks into 2D mode or if 2D tools return to a non-wrapping viewport overlay.

## Milestone 2: Unified tool infrastructure

### AUD-007 — Define one mode-aware tool descriptor

- **Priority:** P0
- **Status:** Done
- **Depends on:** AUD-001
- Define a descriptor containing mode, identifier, icon, label, shortcut, command, inspector key, group, order, enabled state, and selected state.
- Replace unrelated 2D and 3D descriptor shapes with the shared model.
- **Acceptance:** Every visible editor tool is represented by exactly one descriptor.

### AUD-008 — Generate the shared tool rail from the catalog

- **Priority:** P1
- **Status:** Done
- **Depends on:** AUD-003, AUD-007
- Render tools by active mode, group, and order.
- Remove manually declared 2D tool buttons and per-tool activation handlers.
- **Acceptance:** Adding a tool descriptor makes the tool appear without adding a new XAML button or click handler.

### AUD-009 — Generate shortcut routing from the tool catalog

- **Priority:** P1
- **Status:** Done
- **Depends on:** AUD-007
- Resolve shortcuts within the active mode.
- Remove semantic remapping such as `project` → Pan and `unfold` → Line.
- Detect duplicate shortcuts in the same mode.
- **Acceptance:** Automated tests cover every descriptor and prove that identical keys may resolve differently only when modes differ.

### AUD-010 — Use the tool catalog for command search and persistence

- **Priority:** P1
- **Status:** Done
- **Depends on:** AUD-007, AUD-009
- Feed the command palette/search surface from the same descriptors.
- Persist customizable order and shortcuts by stable tool identifier.
- **Acceptance:** Rail, shortcuts, command search, and persisted customization expose the same tool set.

## Milestone 3: Separate workspace state

### AUD-011 — Extract `Editor2DWorkspaceViewModel`

- **Priority:** P0
- **Status:** Done
- **Depends on:** AUD-001
- Move 2D document, selection, active tool, measurements, editing operations, and undo/redo state out of `EditorPageViewModel.Output.cs`.
- Make the 2D document valid independently of generated exports or a loaded 3D model.
- **Acceptance:** A blank 2D project can be created, edited, saved, closed, and reopened without creating generated-output state first.

### AUD-012 — Replace `GeneratedOutput*` editor terminology

- **Priority:** P1
- **Status:** Done
- **Depends on:** AUD-011
- Rename editor state to `TwoD*`, sketch/document terminology, or domain-specific names.
- Keep “output” terminology only for exported artifacts and export previews.
- Add persistence compatibility for existing `.stch` fields where required.
- **Acceptance:** No editable 2D document or tool property is named as generated output.

### AUD-013 — Extract `Editor3DWorkspaceViewModel`

- **Priority:** P1
- **Status:** Done
- **Depends on:** AUD-001
- Move bodies, face selection, movement, projection, unfolding, and viewport lifecycle out of the shell view model.
- Expose narrow coordination events to the editor shell.
- **Acceptance:** The shell coordinates workspaces without directly mutating their internal selection or tool state.

### AUD-014 — Split oversized editor controls

- **Priority:** P1
- **Status:** Done
- **Depends on:** AUD-002, AUD-011, AUD-013
- Break `DxfPreviewCanvas` into rendering, hit-testing, interaction/session, and geometry-editing collaborators.
- Replace direct code-behind event wiring with commands or behavior objects where practical.
- **Acceptance:** The canvas control delegates major responsibilities to independently testable components, and editor XAML/code-behind no longer centralizes all tool workflows.

## Milestone 4: STEP and geometry-kernel strategy

### AUD-015 — Record the geometry-kernel decision

- **Priority:** P0
- **Status:** Done
- **Depends on:** None
- Write an ADR comparing a cross-platform OCCT binding, a packaged geometry worker/backend, and retaining the Python/OpenCASCADE worker behind .NET interfaces.
- Evaluate B-rep topology, analytic surfaces, exact edges, projection/unfold quality, licensing, packaging, and macOS support.
- Do not treat OBJ/STL conversion as STEP parity.
- **Acceptance:** One strategy is selected with explicit tradeoffs, migration steps, and rollback criteria.

### AUD-016 — Restore STEP/B-rep import in the Avalonia workflow

- **Priority:** P0
- **Status:** Done
- **Depends on:** AUD-015
- Implement STEP/STP loading through the selected kernel strategy.
- Preserve face identities, topology, analytic surface types, and exact edges needed for projection and unfolding.
- Replace tests that expect STEP rejection with geometry and topology assertions.
- **Acceptance:** A representative STEP fixture imports, displays, projects, unfolds, saves, and reopens without an OBJ/STL conversion step.

### AUD-017 — Add kernel contract and parity tests

- **Priority:** P0
- **Status:** Done
- **Depends on:** AUD-015
- Define shared service-contract tests for import, projection, flattening, distortion, and failure reporting.
- Run the same relevant fixtures against the chosen implementation and the legacy reference path where possible.
- **Acceptance:** Kernel changes cannot silently reduce supported topology or geometric accuracy.

## Milestone 5: Cross-platform and macOS delivery

### AUD-018 — Make application and tests portable or multi-targeted

- **Priority:** P0
- **Status:** Done
- **Depends on:** AUD-015
- Remove unconditional `net10.0-windows7.0`, `WinExe`, COM, and Windows-manifest assumptions.
- Introduce portable targets or explicit Windows/macOS target configurations.
- **Acceptance:** The application and test projects restore and compile for Windows and `osx-arm64` configurations.

### AUD-019 — Introduce platform-specific file integration services

- **Priority:** P1
- **Status:** Done
- **Depends on:** AUD-018
- Abstract reveal/open operations from `explorer.exe`.
- Implement Windows Explorer and macOS Finder behavior behind the same interface.
- **Acceptance:** Reveal-file behavior is unit-tested by platform selection and does not hardcode a Windows executable in shared code.

### AUD-020 — Package the OpenGeometry runtime deterministically

- **Priority:** P0
- **Status:** Done
- **Depends on:** AUD-015, AUD-018
- Eliminate the requirement for a developer-installed `node` executable at runtime, or package the selected runtime explicitly.
- Make restore/build inputs reproducible and version-pinned.
- **Acceptance:** A clean release artifact runs geometry operations on a machine without Node/npm installed globally.

### AUD-021 — Add macOS publish, packaging, and CI

- **Priority:** P0
- **Status:** Todo
- **Depends on:** AUD-016, AUD-018, AUD-020
- Add an `osx-arm64` publish profile and repeatable application-bundle packaging.
- Add signing/notarization configuration or clearly documented unsigned-development behavior.
- Add macOS CI for publish, launch smoke testing, WebView initialization, file dialogs, and save/reopen.
- **Acceptance:** CI produces a launchable macOS artifact that has no external developer-runtime dependencies.

### AUD-022 — Add macOS document integration and previews

- **Priority:** P2
- **Status:** Todo
- **Depends on:** AUD-021
- Define `.stch` document association and Finder integration.
- Restore or port Quick Look/thumbnail support for DXF and STEP where applicable.
- **Acceptance:** Packaged builds register supported document types and show verified previews on macOS.

## Milestone 6: Feature-parity backlog

### AUD-023 — Implement first-class 2D layers

- **Priority:** P1
- **Status:** Done
- **Depends on:** AUD-011
- Add layer creation, selection, visibility, locking, ordering, and geometry membership.
- Preserve the behavior where selecting a layer selects its geometry.
- **Acceptance:** Layer state persists through `.stch` save/reopen and is isolated to the 2D workspace.

### AUD-024 — Port reference-image and tracing workflow

- **Priority:** P2
- **Status:** Done
- **Depends on:** AUD-011, AUD-023
- Add reference-image-only layers, import sizing, transform/calibration, opacity, locking, and tracing controls.
- **Acceptance:** Imported images remain non-geometry references until explicitly traced and survive save/reopen.

### AUD-025 — Add Batch mode

- **Priority:** P2
- **Status:** Done
- **Depends on:** AUD-001, AUD-002
- Add batch actions, batch cards, and batch settings as a separate editor mode.
- **Acceptance:** Batch workflows do not open or mutate a normal 2D/3D workspace implicitly.

### AUD-026 — Restore missing direct tools and toolbar customization

- **Priority:** P1
- **Status:** Done
- **Depends on:** AUD-007, AUD-008, AUD-010
- Add direct descriptors for Add Holes/Sewing, Flip Horizontal, Flip Vertical, and Duplicate.
- Add grouped, reorderable, resizable/reflowing toolbar behavior.
- **Acceptance:** Tool availability matches the supported macOS catalog, and customization persists by stable identifier.

### AUD-027 — Restore mature sewing-hole workflows

- **Priority:** P1
- **Status:** Done
- **Depends on:** AUD-011, AUD-023, AUD-026
- Port pitch, margin, corner, avoidance, symmetry, and preview behavior required by existing project specifications.
- **Acceptance:** Sewing operations are non-destructive where specified, preview before commit, and persist editable parameters.

### AUD-028 — Make corner operations parametrically re-editable

- **Priority:** P1
- **Status:** Done
- **Depends on:** AUD-011
- Replace destructive local Fillet/Chamfer passes with persistent per-corner parameters and re-entry behavior.
- Preserve shared initial radius for corners selected in one tool session without permanently linking them.
- **Acceptance:** Saved projects can reopen and edit existing Fillet/Chamfer parameters without reconstructing source geometry manually.

### AUD-029 — Complete text-tool properties

- **Priority:** P2
- **Status:** Done
- **Depends on:** AUD-011
- Add installed-font selection, font size, character spacing, bold, italic, underline, and multi-line editing.
- **Acceptance:** Text properties render consistently, remain editable, and persist through `.stch` save/reopen.

## Cross-cutting verification

### AUD-030 — Add workspace persistence round-trip tests

- **Priority:** P0
- **Status:** Done
- **Depends on:** AUD-011, AUD-013
- Test `.stch` save/reopen with simultaneous 2D and 3D workspace state.
- Cover active mode, active tools, selections, layers, generated exports, and body transforms.
- **Acceptance:** Round-trip tests compare semantic state and detect missing or cross-contaminated workspace data.

### AUD-031 — Establish UI test fixtures and accessibility identifiers

- **Priority:** P1
- **Status:** Done
- **Depends on:** AUD-002
- Add stable automation identifiers to shell regions, tools, inspectors, dialogs, and canvases.
- Create reusable headless and platform smoke-test fixtures.
- **Acceptance:** UI tests select controls by stable identifiers rather than text position or fragile visual-tree indexes.

### AUD-032 — Add release-readiness checks

- **Priority:** P1
- **Status:** Todo
- **Depends on:** AUD-017, AUD-021, AUD-030, AUD-031
- Verify zero-warning builds, unit tests, UI coordination tests, package contents, clean-machine launch, and representative document workflows.
- Keep README platform and kernel claims synchronized with verified capabilities.
- **Acceptance:** A single documented verification procedure produces auditable Windows and macOS readiness results.

## Suggested execution order

1. AUD-001, AUD-015
2. AUD-002, AUD-007, AUD-011, AUD-017
3. AUD-003 through AUD-006, AUD-008 through AUD-010, AUD-012 through AUD-014
4. AUD-016, AUD-018 through AUD-021, AUD-030, AUD-031
5. AUD-023 through AUD-029
6. AUD-022, AUD-032
