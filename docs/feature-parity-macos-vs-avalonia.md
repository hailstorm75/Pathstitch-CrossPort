# macOS to Avalonia Feature-Parity Audit

**Snapshot:** 2026-07-18  
**Purpose:** implementation handoff for agents closing remaining gaps  
**Benchmark:** behavior present in original Swift/macOS app  
**Method:** static comparison of Swift, Python geometry backend, Avalonia/C# source, packaging scripts, and existing tests, followed by focused implementation verification and the warnings-as-errors .NET suite.

## Executive result

Avalonia has broad functional parity. All three workspaces exist. All 23 named 2D tool concepts exist. Core project lifecycle, drawing creation/editing, parametric dimensions, layers, reference images, 3D import/projection/unfold, batch processing, import/export, command search, shortcuts, tutorials, recent projects, and macOS Quick Look packaging are implemented.

Remaining work:

1. **P0 edited DXF structural fidelity:** unchanged direct/generated and Batch DXFs preserve opaque source structure. Regular full export and project save now merge uniquely handled planar LINE/LWPOLYLINE/CIRCLE/ARC edits, basic TEXT edits, gated solid non-associative planar polyline/native-edge HATCH similarity transforms, and safe new entities into the immutable source while retaining untouched entities, BLOCKS, custom tables/styles, and third-party XDATA. New entities receive globally unique handles, model-space ownership, and an advanced `$HANDSEED`; missing layers are inserted into one validated LAYER table with owned handles, true color, CONTINUOUS linetype, and an updated table count. Unreferenced supported-entity deletion also merges safely. Missing APPID/PATHSTITCH, LTYPE/CONTINUOUS/DASHED, and STYLE/STANDARD dependencies are inserted with owned handles and capacity-safe symbol-table updates. New construction entities use canonical CONSTRUCTION/#808080 metadata, ACI 8/DASHED layer and entity styling, and validated simple DASHED patterns. Externally referenced deletion, existing layer/color changes, associative/non-similarity/tilted HATCH, unsupported native geometry, duplicate/missing handles, selected-only, measurements, and version changes still use model serialization.
2. **P1 tilted OCS text fidelity:** planar curves and HATCH loops project correctly. TEXT insertion, baseline, height, width, negative extrusion, and one-axis mirrored glyph runs now retain handedness through editor transforms and DXF/SVG/PDF/PNG output. Arbitrary non-orthogonal shear remains a best-fit approximation in the scalar rotation/width text model.
3. **P1 release evidence:** native macOS packaging/CI source exists; exact-commit Apple-silicon artifact evidence remains.

Do **not** open task for compound-path Explode. Avalonia now exposes it in canvas context menu (`DxfPreviewCanvas.cs:587-588, 696-708, 6126-6155`). Earlier notes calling it unreachable are stale.

## Status vocabulary

| Status | Meaning |
|---|---|
| **Full** | User outcome and important options found in source; supporting tests/evidence found where noted. |
| **Full+** | Port matches outcome and fixes known original persistence defect. |
| **Partial** | Main workflow exists, but material options, UX, or fidelity differ. |
| **Platform-equivalent** | Same outcome through platform-appropriate implementation. |
| **Unverified** | Source exists; native/runtime acceptance not established here. |

## Feature matrix

| Area | Avalonia parity | Agent note |
|---|---|---|
| Workspaces | **Full** | 2D, 3D, Batch; stable mode-switch tests. |
| 2D tool catalog | **Full** | Same 23 concepts. Compare `AppState.swift:35-60` and `Editor2DPreviewDocument.cs:5-30`. |
| Selection/navigation | **Full** | Click/Shift/marquee/chain/snaps/grid; temporary Space-pan at `EditorShellView.axaml.cs:21, 292-353`. |
| Move/transform | **Full** | Drag, numeric move/rotate, point-to-point, pivot scale, copy, flips, duplicate. |
| Geometry creation | **Full** | Line, circle, rectangle, polygon, text, editable pen/Bezier. |
| Offset/thickness/cleanup/trim | **Full** | Same feature families; circular and whole-curve trim suites exist. |
| Boolean/stroke/fill/explode | **Full** | Same, including context-menu Explode, compound HATCH hole loops, even-odd rendering, and filled DXF/SVG/PDF/PNG output. PAR-014 complete. |
| Sewing holes | **Full** | Advanced controls, true offset-curve joins, filters, persistence, Batch plumbing, and deterministic Python golden parity are covered. PAR-001 complete. |
| Fillet/chamfer | **Full** | Parametric corner editing, G1/G2, preview/edit/persistence. |
| Convert lines | **Full** | Dashed/dotted/zigzag/wave/striped/square/triangle families. |
| Mirror/pattern/folding | **Full** | Live link; rectangular/circular/path patterns; creases/glue tabs. |
| Dimensions/formulas | **Full** | Driving/driven, variables, UnitsNET-backed mm/cm/m/in conversion, dependency errors; parity tests exist. PAR-012 complete. |
| Text | **Full** | Editing and rich Unicode DXF/SVG/PNG/PDF output are implemented. PDF uses deterministic bundled OFL CJK/emoji fonts, exact ToUnicode maps, grapheme-safe fallback, transformed multiline layout, and atomic writes. PAR-017/PAR-018 complete. |
| Layers/folders | **Full** | Hierarchy, ordering, merge, visibility, lock/color, reference layers. |
| Reference images | **Full** | Transform, depth, opacity, lock, calibration, restore. |
| Trace/background removal | **Full for declared scope** | Metric-gated technical tracing and flat/gradient removal; UI explicitly excludes AI photo cutout. |
| PSD workflows | **Full** | All four modes preserve PSD registration/visibility at full opacity. Vectorize modes stage shared all-layer previews, support cancel, commit vectors while hiding sources, and keep import/commit as separate undo steps. Current-viewport fit/centering and portable parser fixtures are covered. PAR-008 complete. |
| .stch lifecycle | **Full+** | ZIP/legacy, windows/sessions/recent thumbnails; port persists more 3D state. |
| Import routing | **Partial** | STCH, ASCII/binary DXF, SVG, PDF, PSD/raster, STEP/STP, OBJ, and STL route correctly. DXF supports BOM-safe UTF-8, legacy declared codepages, Unicode escapes, SPLINE/HATCH, bounded curves, planar OCS projection, and exact one-axis mirrored TEXT; packaged ezdxf converts binary transport into a temporary ASCII import intermediary without changing the source, while arbitrary non-orthogonal text shear remains approximate. PAR-012 through PAR-016/PAR-020. |
| 2D export | **Partial** | Generated DXF/SVG/PDF/PNG, selected-only, measurements, canonical millimetres, fills/holes, and rich layout work. Unchanged direct/generated DXFs preserve opaque structure through full export and project save/reopen; uniquely handled planar LINE/LWPOLYLINE/CIRCLE/ARC/basic-TEXT edits, gated solid polyline/native-edge HATCH similarity transforms, and new entities on existing or safely inserted layers merge without discarding opaque source records. Broader edited-source merging remains; full-Unicode PDF embedding is complete. PAR-012 through PAR-020. |
| 3D import/viewport | **Full / Unverified on macOS** | Packaged OCCT normalizes STEP/STP/OBJ/STL, including mixed multi-import, into stable topology; legacy JSON mesh workspaces retain an explicit reduced fallback. PAR-009 complete. |
| Projection/unfold | **Full / Unverified on macOS** | Same modes, faces/body, nets, seams, anchors, global/per-edge decorations, and dimensions route through OCCT for STEP/OBJ/STL; generated DXFs declare millimetres and reject ambiguous append targets. PAR-009/PAR-012 complete. |
| Batch processing/export | **Full** | Queue, select/remove, offset/holes, DXF/SVG/PDF/PNG export. Unmodified source DXF is copied byte-for-byte except canonical millimetre HEADER fields; modified items use geometry serialization. PAR-012/PAR-019. |
| Batch item UX | **Full** | Virtualized read-only previews, stable item identity, isolated Edit → Save & Return/Cancel round trip. |
| Recent projects | **Full / Unverified on macOS** | Persisted cards load immediately, then a cancellable live stream merges Spotlight snapshots while Home remains open. Updates normalize/dedupe, rank newest, cap 20, preserve missing persisted entries and selection, suppress identical snapshots, and honor removal tombstones. PAR-010 complete. |
| Toolbar customization | **Full** | Main, Shapes, More containers; constrained cross-container button/drag moves, per-container order, reset, legacy migration, reconciliation, persistence, and live flyout activation. PAR-005 complete. |
| Shortcuts/command search | **Full** | Custom app/tool shortcuts, conflicts, reset, palette. |
| Core preferences/help | **Full** | Theme, pan, SVG, tutorial/intros, resets. |
| Dock icon/Quick Look prefs | **Full / Unverified on macOS** | Automatic/Light/Dark bridge and independent DXF/STEP/STCH app-group toggles implemented. PAR-006 complete; native package execution remains PAR-007. |
| Quick Look/file registration | **Platform-equivalent / Unverified** | STCH/DXF use crisp static previews; STEP uses a packaged interactive SceneKit mesh with orbit/zoom controls, crease-aware normals, studio lighting, and static fallback. Source/package checks present; native interaction remains PAR-007 evidence. PAR-011 source complete. |
| Updates | **Intentional divergence** | Explicit manual GitHub release download; no unsigned in-app installer or automatic network checks. |

## Action backlog

### PAR-001 — Complete advanced sewing-hole behavior

**Priority:** P0  
**Status (completed 2026-07-18):** Avalonia persists and exposes all six inputs, samples real mitre/round offset curves, flexes pitch between offset corners, applies proximity/crossing/radius/closed-obstacle/existing-circle/keep-out filters, and forwards the same settings through Batch. Twelve deterministic Python-generated cases cover Count, sharp/round joins, acute and self-near paths, crossing/nearly-parallel lines, existing circles, and keep-outs. Avalonia intentionally enforces the Python function's documented closed-obstacle containment contract where the current Python implementation fails to suppress the contained point.  

**Risk:** manufacturing geometry correctness  
**Parallel-safe:** mostly isolated to sewing model, geometry, inspector, persistence, tests

Confirmed original-only inputs:

- `offset_corner_fillet`
- `enable_proximity_filter`
- `enable_corner_interpolation`
- `enable_line_proximity_filter`
- `line_proximity_threshold`
- `proximity_filter_distance`

Evidence:

- Original: `Pathstitch/Pathstitch/App/AppState.swift:1081-1087, 4837-4843`; `ContentView.swift:1536, 1622-1667`; `pathstitch_core/dxf_ops.py:2303-2751`.
- Port: `src/Domain/Domain.App/Models/Editor2DSewingHoleModels.cs:31-48`; `src/Pathstitch.App/Pages/Editor2DInspector.axaml:202-305`.

Implementation:

1. Extend `Editor2DSewingHoleParameters` with stable JSON names and backward-compatible defaults.
2. Add ViewModel properties and grouped/collapsible inspector controls.
3. Implement rounded offset joins, same-path hole merging, corner interpolation, crossing-line/end rejection.
4. Pass same settings through Batch bulk-hole execution.
5. Persist committed operation parameters; old projects must deserialize unchanged.

Acceptance:

- Golden fixtures against `dxf_ops.py`: sharp/rounded joins, acute corners, nearly parallel lines, self-near paths, existing circles, keep-outs.
- Count mode returns exact requested count and skips corner subdivision/proximity merging, matching original.
- Closed Fill closes evenly; saddle second row retains half-pitch phase.
- Preview, commit, undo/redo, reopen, Batch produce identical parameters/results.
- Compare hole centers/radii within tolerance, not serialized DXF text.

### PAR-002 — Add Batch thumbnails and edit round trip

**Priority:** P1  
**Status:** Completed 2026-07-18  
**Risk:** workflow completeness; avoid coupling Batch state to active 2D document

Evidence:

- Original thumbnail/Edit: `Pathstitch/Pathstitch/Modes/BatchMode/BatchModeView.swift:360-400`.
- Original Save & Return: `Pathstitch/Pathstitch/ContentView.swift:3903-3971`.
- Port text-only template: `src/Pathstitch.App/Pages/EditorBatchView.axaml:25-61`.
- Port already stores loaded `Document`: `src/Domain/Domain.App/Models/EditorBatchItem.cs:71-74`.

Implementation:

1. Add read-only miniature canvas/thumbnail per item. Reuse `Editor2DPreviewDocument`; disable hit testing and adorners.
2. Add Edit command carrying stable item identity, not list index.
3. Open working copy in 2D. Preserve queued original until Save & Return.
4. Save & Return replaces item document/source bytes, refreshes preview/status, returns to Batch, marks project dirty.
5. Cancel returns without mutation. Removal/reordering must not redirect edit commit.

Acceptance:

- Headless UI finds preview and Edit button for each loaded item.
- Edit → mutate → Save & Return changes only selected item and its exported geometry.
- Cancel leaves item logically/byte-for-byte unchanged.
- Project save/reopen retains edited data, selection, order, naming/export settings.
- Large queues use bounded rendering; no full interactive canvas for off-screen items.


Completion evidence:

- EditorBatchItem.Id persists with backward-compatible generation for legacy state.
- Virtualized ListBox cards lazy-load documents and host hit-test-disabled miniature DxfPreviewCanvas previews.
- Batch editing snapshots normal 2D state plus undo/redo, edits a defensive document copy, and restores normal state on Save or Cancel.
- Save resolves by stable ID, clears stale export/status state, and fails safely when the edited item was removed.
- Project capture retains the normal 2D workspace during an active Batch edit; Batch item documents continue through existing project persistence.
- Focused Batch/UI tests passed 27/27; final combined warnings-as-errors suite passed 949/949.

### PAR-003 — Establish trace/background-removal fidelity target

**Priority:** P1  
**Risk:** output quality; dependency size/security  
**Status:** Completed 2026-07-18 — technical-drawing scope selected; AI photo cutout is an explicit non-goal  
**First step:** tests and product decision, not immediate dependency replacement

Difference:

- Original uses Potrace and tries rembg: `pathstitch_core/dxf_ops.py:3143-3293`.
- Port traces threshold-mask pixel boundaries and simplifies/smooths: `AvaloniaReferenceImageTraceService.cs:41-178`.
- Port background removal flood-fills against a four-corner bilinear background model; it remains deterministic non-AI removal.

Acceptance study:

1. Add fixed PNG fixtures: logo, antialiased drawing, internal holes, noisy scan, border-touching subject, gradient background, photograph/hair, transparency.
2. Compare contour count, winding/holes, area error, Hausdorff distance, node count, alpha-mask IoU.
3. Define thresholds separately for technical drawings and photographic cutouts.
4. If current service passes technical-drawing targets, retain it and rename UI/help so it does not imply AI removal.
5. If photo cutout parity required, package cross-platform segmentation with offline behavior, deterministic versioning, licensing review, model-size budget.

Do not claim rembg parity for corner-color flood fill.

Implemented evidence:

- Trace tolerance now maps to Python's Potrace turd-size formula and removes small specks before contour refinement.
- Silhouette alpha classification now matches Python's alpha greater-than-10 cutoff.
- Background removal interpolates all four corner colors, clearing smooth gradients without erasing dissimilar foreground.
- Eighteen generated PNG inputs/masks cover clean, antialiased, holes/islands, noisy, border-touching, transparency, flat/gradient backgrounds, and a synthetic hair decision gate.
- Metric gates cover mask IoU, boundary Hausdorff distance, components, holes, node count, and alpha false-positive/false-negative rates.
- Fifteen focused tests pass; fixture regeneration is byte-identical.
- UI says “Remove Flat Background” and explains that behavior is deterministic, not AI subject cutout.

### PAR-004 — Decide and implement update parity

**Priority:** P1  
**Status:** Completed 2026-07-18 — secure manual-download divergence selected  
**Risk:** supply-chain security, signing, rollback

Difference:

- Original Sparkle controller: `Pathstitch/Pathstitch/App/UpdaterManager.swift:15-51`.
- Port browser redirect: `src/Pathstitch.App/Services/AppUpdateService.cs:5-14`.

Decision: explicitly accept manual release download as product divergence.

Accepted outcome:

- Help/About label the action “Download Updates Manually” instead of implying an in-app version check.
- Service policy exposes manual-browser strategy, no automatic checks, and no installation capability.
- Browser handoff uses the fixed HTTPS official release URL.
- No asset is downloaded or executed inside the app without a signed-manifest trust root and signed/notarized platform installers.

If implementing:

- Version/feed check, automatic-check opt-in/persistence, manual result UI, signed verification, download/install/relaunch, offline/error state.
- Integrate signing/notarization and feed generation with macOS release workflow; define Windows equivalent.
- Never install unsigned assets or trust only transport URL.

Acceptance:

- Older/current/newer version fixtures.
- Opt-in survives restart; automatic check respects setting.
- Invalid signature, malformed feed, offline state, cancel, downgrade fail safely.
- Manual check reports current, available, or actionable failure inside app.

### PAR-005 — Resolve toolbar-container parity

**Priority:** P2  
**Status (completed 2026-07-18):** Avalonia now uses Main, Shapes, and More containers with Swift-equivalent defaults. Context-menu actions and pointer drag/drop move tools by stable identifier, Shapes accepts only shape-origin tools, and earlier/later controls reorder within one container. Project state persists container/order. Legacy one-rail layouts migrate to Main without hiding tools; unknown IDs, duplicates, missing tools, and invalid Shapes placements reconcile safely. Per-mode/all-mode reset, shortcut/search reachability, archive round-trip, and live Shapes-flyout activation are covered.

**Risk:** low; personalization

Implemented outcome:

- Original has three persistent containers with constrained move semantics.
- Port now exposes equivalent container semantics in EditorToolRail.axaml and EditorPageViewModel.Tools.cs.

Chosen implementation:

1. Added Main/Shapes/More containers, cross-container move/drop, persisted container/order, reset, and migration.
2. One-rail divergence is no longer applicable.

Acceptance evidence: keyboard-accessible context/button movement and drag movement, archive/reopen persistence, missing/new tool migration, duplicate-ID repair, reset per mode/all modes, and headless flyout activation tests.

### PAR-006 — Complete macOS preferences and recent discovery

**Priority:** P2  
**Status (completed 2026-07-18):** Preferences now expose Automatic/Light/Dark Dock icons and independent DXF/STEP/STCH Finder preview toggles. A packaged Swift C-ABI bridge writes exact app-group UserDefaults keys and updates the Dock icon from bundled light/dark assets. Preview and thumbnail extensions default enabled, read the shared suite, and declare STCH support. Spotlight discovery streams cancellable live snapshots after persisted cards render; merge behavior normalizes/dedupes paths, ranks newest, caps 20, preserves persisted missing entries and selection, suppresses unchanged snapshots, and uses tombstones until a project is reopened. App/extension entitlements and inside-out signing source are present. Native execution remains PAR-007 evidence, not a PAR-006 source claim. Focused checks passed 29/29; warnings-as-errors build passed; full suite passed 974/974.  
**Risk:** native integration/shared preference plumbing

Closed outcomes:

- App/Dock icon Automatic/Light/Dark: `PreferencesWindow.swift:113-118, 337-370`.
- Per-format Finder preview toggles for DXF/STEP/STCH in shared app-group defaults: `PreferencesWindow.swift:94-99, 135-139`.
- Spotlight-discovered projects merge asynchronously with persisted recents without delaying initial Home rendering.

Implementation:

- Quick Look providers read shared sandbox-compatible preferences; default all formats on.
- Settings affect preview and thumbnail extensions.
- Package light/dark icon assets; update macOS app icon without changing Windows.
- Add macOS-only Spotlight provider behind interface; merge/dedupe normalized paths, newest first, cap 20, preserve missing state.

Acceptance: toggle each format independently, restart app/extension, verify disabled provider yields to Finder; test icon resolution in light/dark/automatic; test Spotlight merge/dedupe without delaying launch.

### PAR-007 — Produce native macOS evidence and repair tracker

**Priority:** P1 release gate  
**Status (in progress 2026-07-18):** Native source gate expanded and stale tracker reconciled. R&D audit found and source-fixed CI disk duplication, missing packaged STCH ZIP decoding, shallow round-trip checks, partial nested signing, unpinned Swift deployment target, generic app icon, and evidence paths that trusted producer booleans/hashes without independently parsing DXF semantics. Packaging uses pinned ZIPFoundation source, registered ICNS, macOS 13 target, and inside-out all-Mach-O signing. Packaged acceptance schema 4 exercises STEP plus OBJ/STL/mixed OCCT routing, connected/separate nets, manual seams, tabs, holes, exact STCH source/output hashes, and preserved input/output artifacts. It requires unique $INSUNITS=4 and $MEASUREMENT=1 declarations inside each DXF HEADER and records those values per descriptor. The independent collector uses protocol literals, parses every retained or newly discovered DXF, records dxfUnitEvidence, extracts and parses STCH project.json.dxfDataBase64, and binds its SHA-256 to the external unfold and round-trip hashes. Twenty-five corrupted synthetic bundles cannot produce passed status; validator tests pass 26/26. AUD-021, AUD-022, and AUD-032 remain honestly In progress. Warnings-as-errors build passes; focused collector/delivery contracts pass 28/28; full .NET suite passes 1049/1049; packaged OCCT mesh/import/unfold tests pass 23/23. Exact-commit Apple-silicon execution, preview-extension runtime observation, and provisioned cross-process app-group proof are still required; no native result is claimed from this Windows host. Sign-off template: docs/architecture/PAR-007-macos-native-evidence.md.  
**Risk:** packaged failures hidden by source-level parity

Existing implementation:

- `scripts/package-avalonia-macos.ps1:59-126` builds Quick Look/thumbnail extensions and fixtures.
- `.github/workflows/macos-release.yml:51-146` validates metadata, architectures, project round trip, DXF/STEP previews.
- `docs/architecture/AUD-017-step-parity-evidence.md:33-41` documents accepted canonical-curve differences and requests native execution.

Prepared source gates:

- Packaged acceptance emits STEP/OBJ/STL/mixed import, projection, connected/separate unfold, seam/decorations, semantic STCH round-trip JSON, retained inputs/outputs, and hashes.
- Finder smoke includes STCH, DXF, and STEP; package verification checks Swift bridge, icons, app-group, sandbox, and arm64.
- Schema-4 native evidence retains one self-contained, hash-inventoried root with package ZIP, input/output geometry, STCH archive, acceptance JSON, independently parsed DXF-unit facts, strict code-sign transcript, entitlements, plugin inventory, exact qlmanage logs/maps, and three Quick Look PNGs.
- Tracker now distinguishes implemented source from missing exact-commit native proof.
Acceptance:

- Green clean-run `osx-arm64` artifact from committed source.
- Launch package; open/save/reopen project; import STEP/OBJ/STL; combine mixed bodies; project/unfold connected and separate nets; force a seam; export tabs/holes/DXF.
- Verify Quick Look extensions activate for STCH/DXF/STEP and produce non-placeholder output.
- Verify codesign/notarization/entitlements when distribution requires them.
- Attach hashes/logs/screenshots to evidence doc, then reconcile stale `AUDIT_TASKS.md`. Source presence alone is not packaged evidence.

### PAR-008 — Close PSD import and vectorization fidelity

**Priority:** P1  
**Status (completed 2026-07-18):** Avalonia now matches native PSD staging semantics. Raster and vector content share one canvas transform, preserve inherited group visibility, import at full opacity, fit to 80% of current viewport, and center at view center unless dropped at a model point. Auto/Merge vectorization opens shared settings and asynchronously previews every queued raster. Cancel retains imported sources. Commit uses cached previews, adds editable vectors, hides source images, and records a second undo boundary separate from import.  
**Risk:** layered artwork registration, destructive conversion UX, parser portability

Implemented evidence:

- Editor2DWorkspaceViewModel.Psd.cs imports first, then starts a batch trace session; no hard-coded immediate conversion remains.
- Shared trace options propagate to every queued PSD raster. Preview generation runs off UI thread for all layers; commit performs no tracing work.
- First undo reverses vectorization and restores sources; second undo reverses import.
- Viewport placement uses live canvas pixel size, zoom, and offsets; drop points remain exact.
- dxf_ops.py propagates parent-group visibility to descendants and preserves duplicate-name/order/placement behavior.
- Deterministic hierarchy matrix covers nested hidden groups, duplicate names, raster/vector smart-object routing, empty layers, and flattened fallback.
- Portable binary fixtures exercise real layered PSD hierarchy/visibility, flattened PSD fallback, and clean malformed-input failure through locked psd-tools 1.17.4; regeneration is byte-identical.
- Focused PSD tests passed 12/12; real Python fixture suite passed 4/4; warnings-as-errors build passed; full .NET suite passed 976/976.

Acceptance closed:

- Load as-is, flattened image, auto-vectorize, and merge/vectorize modes retain shared registration.
- Shared tune/preview/cancel/commit workflow applies to all raster layers.
- Converted sources hide only on commit.
- Import and vectorization remain independently undoable.
- Full opacity, inherited visibility, viewport placement, layered hierarchy, flattened files, and malformed files have deterministic coverage.
### PAR-009 — Unify OBJ/STL and mixed 3D workflows on packaged OCCT

**Priority:** P0  
**Status (completed 2026-07-18):** R&D found that the Full 3D claim overstated OBJ/STL behavior. Avalonia sent only STEP/STP through the packaged worker, rejected mixed STEP+mesh imports, and flattened OBJ/STL as isolated triangles while ignoring connected-net controls. All supported 3D source formats now use the packaged OCCT topology path in production. Pairwise mixed imports normalize to a retained final STEP document; null-kernel tests and restored legacy JSON workspaces keep the reduced C# mesh fallback.  
**Risk:** manufacturing net topology, mixed-model data loss, misleading option parity

Implemented evidence:

- OpenGeometryEditor3DOperationService routes STEP/STP/OBJ/STL import, projection, distortion, and unfold through the packaged worker whenever production DI supplies it.
- Mixed STEP/OBJ/STL import reuses pairwise CombineAsync and imports the final normalized STEP so stable IDs match persisted bytes.
- OBJ faces are sewn before topology extraction; two adjacent triangles expose five unique edges and one shared adjacency instead of six isolated edges.
- STEP reload sewing preserves free mesh shells alongside solids. A STEP + OBJ + STL chain reimports as four bodies; neither mesh body is silently dropped or split.
- Connected OBJ nets produce one patch and a crease. Manual cuts produce two patches. Glue-tab and per-edge sew-hole decorations reach GLUE_TABS and SEW_HOLES output layers.
- Worker handshake/runtime specification declares obj-import, stl-import, and mixed-combine; packaged runtime smoke runs the portable mesh parity matrix.
- Focused C# routing tests passed 19/19, packaged OCCT tests passed 12/12, and Python mesh parity passed 3/3.

Acceptance closed:

- Single OBJ/STL imports carry stable topology and use the same projection/unfold request surface as STEP.
- Mixed STEP/OBJ/STL import chains preserve all bodies and retain the final normalized source for save/reopen.
- Connected/separate layout, forced seams, anchor, global/per-edge decoration, and dimension payloads are not discarded by a mesh-only fallback in packaged releases.
- Legacy JSON mesh workspaces remain readable and fail safely if asked to combine with STEP; existing bytes are not mutated.
### PAR-010 — Make Recent Projects discovery live

**Priority:** P1  
**Status (completed 2026-07-18):** R&D found that the Full recent-projects claim overstated lifecycle parity. The Swift benchmark retains NSMetadataQuery and processes both initial gathering and later updates, while Avalonia ran mdfind once during Home load. Avalonia now exposes a cancellable asynchronous snapshot stream. Home renders persisted cards before discovery releases, consumes updates for the full page lifetime, preserves path-based selection across reorder, clears selection when its item disappears, and avoids collection churn for identical snapshots. macOS polls Spotlight sequentially with timeout/failure isolation and suppresses unchanged snapshots. The recent-project service retains the latest snapshot so local select/remove/reopen operations do not temporarily discard discovery-only cards. Page disposal and load-token cancellation stop enumeration and prevent late mutation.  
**Risk:** stale Home state, background task lifetime, selection loss, transient Spotlight failure

Implemented evidence:

- IProjectDiscoveryProvider has a backward-compatible WatchAsync stream; one-shot providers still produce one snapshot.
- MacOSSpotlightProjectDiscoveryProvider performs non-overlapping five-second mdfind snapshots, dedupes paths, orders deterministically, skips failed polls, and emits only semantic changes.
- RecentProjectsService separates merge logic from provider invocation and caches the latest immutable discovery snapshot behind a lock.
- HomePageViewModel applies semantic list equality before replacement and keeps selected state by normalized path.
- BasePageViewModel retains its linked load cancellation source until disposal, so background page work remains cancellable after LoadPageAsync returns.
- Controlled-channel tests prove persisted-first rendering, live add/reorder/delete, persisted missing cards, selection survival/clear, identical-snapshot suppression, provider failure isolation, tombstone/reopen behavior, cancellation, and rejection of late updates.
- Focused recent-project lifecycle tests pass 9/9; macOS document/source contracts pass in the combined 15/15 group; warnings-as-errors build passes; full .NET suite passes 997/997.
- SVG parser-setting tests now share a nonparallel collection after full-suite verification exposed cross-class static-setting contamination.

Acceptance closed:

- Externally created, modified, and deleted project snapshots update Home without page reload.
- Persisted cards appear without waiting for Spotlight.
- Discovery-only deletions disappear; persisted deletions remain as missing cards.
- Tombstoned items remain hidden until RecordProject explicitly reopens them.
- Selection survives reorder and clears only when its path leaves the merged result.
- Identical snapshots do not replace the collection or raise redundant RecentProjects changes.
- Cancellation and provider failure leave the last valid/persisted UI state intact.
### PAR-011 — Restore interactive STEP Quick Look preview

**Priority:** P2  
**Status (source-complete 2026-07-18; native interaction unverified):** R&D found that the packaged Avalonia preview extension reduced every format to NSImageView, while the Swift benchmark gives STEP files an interactive SceneKit mesh with drag-to-orbit and scroll-to-zoom. The packaged extension now retains static high-fidelity STCH/DXF rendering but installs a SceneKit SCNView for successfully tessellated STEP models. It recenters and unit-scales geometry, validates triangle indices, recomputes crease-aware welded normals, applies ambient/key/fill lighting, enables camera controls, and retains the existing bitmap/point render as a fail-safe. Security-scoped file access is balanced. Packaging compiles the SceneKit helper only into the preview extension, not the thumbnail extension.  
**Risk:** Quick Look extension compilation/runtime, mesh seam quality, camera framing

Implemented source evidence:

- InteractiveStepPreview.swift builds a normalized SCNGeometry from packaged foxtrot StepMeshData.
- allowsCameraControl restores orbit/zoom interaction; a bounded camera and four-sample antialiasing provide stable initial presentation.
- Crease-aware smoothing removes duplicated B-rep seam stripes while retaining hard edges.
- PreviewProvider keeps static DXF/STCH behavior and STEP bitmap fallback when interactive initialization is unavailable.
- package-avalonia-macos.ps1 accepts preview-only extra Swift sources and remains PowerShell-parser clean.
- Focused macOS delivery/document contracts pass 8/8; warnings-as-errors build passes; full .NET suite remains 997/997.

Native acceptance still required:

- Compile/package on the exact Apple-silicon commit.
- Open a STEP Finder preview and prove SCNView orbit/zoom interaction.
- Confirm DXF/STCH remain static, correctly scaled, and non-placeholder.
- Confirm failed STEP tessellation falls back without a blank preview.

### PAR-012 — Make DXF physical units explicit and spec-correct

**Priority:** P0 manufacturing integrity  
**Status (completed 2026-07-18):** R&D found that every Avalonia-generated DXF omitted `$INSUNITS` and `$MEASUREMENT`, even though editor coordinates are millimetres. A two-inch input corrected to 50.8 internal units could therefore export as 50.8 unitless units. The C# reader also stopped at code 20, while the Python table incorrectly treated code 21 as a survey mil. All C# length conversion now uses UnitsNET 5.75.0 behind one domain catalog. New DXFs from full/selected/quick/Batch export, SVG/PDF/raster conversion, projection, unfold, and blank-document creation declare `$INSUNITS=4` and `$MEASUREMENT=1`. Appends convert declared units when safe or reject ambiguous inputs before coordinate systems mix.  
**Risk:** physical manufacturing scale, cross-CAD interchange, mixed-unit append corruption

Implemented evidence:

- `EditorLengthUnits.cs` maps Autodesk INSUNITS codes 1-24 through UnitsNET, including exact U.S. survey-foot derivatives for codes 21-24; code 0 and unknown codes remain unscaled.
- UnitsNET also replaces dimension-expression conversion constants and standard import-scale factors.
- Corrected prior code 17/18/20 orders-of-magnitude errors for gigametres, astronomical units, and parsecs; corrected Python code 21 and added codes 22-24.
- `EditorDxfDocument` writes the canonical millimetre header in both document and generated-polyline writers. Reimport sees code 4, factor 1, and no physical-size drift.
- C# append converts generated millimetre polylines and gap into the existing document's declared units. The HEADER scanner rejects duplicate, conflicting, truncated, or multiply-declared unit metadata; missing, unitless, malformed, and unsupported declarations fail before output is written.
- Python `op_append_dxf` converts secondary geometry into primary declared units; projection/unfold append accepts only millimetre working DXFs. Every Python-created production DXF uses the shared millimetre constructor.
- Explicit non-mm declarations prompt with their exact factor. Metres remain deliberately weak because common exporters stamp code 6 on millimetre drawings; millimetres need no correction prompt.
- Spec references: [Autodesk INSUNITS 0-24](https://help.autodesk.com/cloudhelp/2025/ENU/AutoCAD-Core/files/GUID-A58A87BB-482B-4042-A00A-EEF55A2B4FD8.htm), [NIST U.S. survey foot definition](https://www.nist.gov/pml/us-surveyfoot), and [UnitsNET 5.75.0](https://www.nuget.org/packages/UnitsNet/5.75.0).
- Focused .NET unit/import/export tests pass 80/80; Python unit/export/append tests pass 7/7; export-option script passes; packaged OCCT mesh/import/unfold passes 23/23; native evidence validator passes 26/26; warnings-as-errors build and full .NET 1049/1049 pass.

Acceptance closed:

- Full, selected-only, quick, and Batch DXFs declare millimetres and metric measurement mode.
- A two-inch line corrected to 50.8 mm exports/reimports as 50.8 mm without another prompt.
- Every official INSUNITS code has table-driven conversion/name/prompt coverage; unitless and unknown remain fail-safe.
- Inch-target C# append converts a 25.4 mm generated segment to one drawing unit; unitless append writes no mixed output.
- Python merge converts mm secondary geometry into inch primary geometry; Python projection/unfold outputs retain metric headers.

### PAR-013 — Correct SVG coordinate handedness and preserve layers

**Priority:** P0 interchange integrity  
**Status (completed 2026-07-18):** R&D found that Avalonia imported and exported raw SVG Y coordinates, mirroring asymmetric CAD geometry relative to the Python/macOS path. The parser also flattened the XML tree without carrying layer identity while the writer emitted layer groups that could not round-trip. SVG export now maps world Y to `-Y` and derives the matching viewBox; import applies physical root scaling, flips back to world coordinates, and normalizes to the Python contract's 10-unit minimum. Recursive drawable traversal ignores definitions and carries `data-layer-name`, Inkscape labels, or group IDs. Filled compound paths preserve even-odd loops, and plain SVG output now declares a visible stroke instead of relying on SVG's `stroke:none` default.  
**Risk:** mirrored manufacturing geometry, lost cut-layer assignment, invisible exports

Evidence:

- An asymmetric two-layer fixture produces the exact Python golden coordinates and layer names.
- Avalonia layered SVG export reimports with unchanged world orientation and source layers.
- Compound filled SVG round-trip retains exterior/hole loops and `fill-rule="evenodd"`.

### PAR-014 — Preserve DXF SPLINE and HATCH geometry

**Priority:** P0 geometry loss  
**Status (completed 2026-07-18):** R&D found that Avalonia counted `SPLINE` and `HATCH` entities but discarded their geometry. SPLINE import now evaluates polynomial and rational B-splines with De Boor interpolation and adaptive 0.1-unit flattening, with fit-point fallback. HATCH import retains polyline boundaries with bulges plus line, arc, ellipse, and spline edge paths. The largest loop remains the selection outline while all loops persist as one compound fill. Canvas, DXF, SVG, PDF, and PNG output use even-odd hole rendering; affine transforms and import scaling carry every loop. Filled DXF paths export as real solid HATCH entities instead of unfilled LWPOLYLINEs.  
**Risk:** silent curved-part loss, lost fill regions/holes, manufacturing output divergence

Evidence:

- Cubic and rational quarter-circle fixtures pass and agree with the packaged `ezdxf.path.make_path(...).flattening(0.1)` reference geometry.
- Polyline HATCH-with-hole round-trips as one filled entity with two loops, metadata, area, and transformed coordinates preserved.
- Mixed line/arc edge boundaries flatten without becoming unsupported.
- Fill-to-stroke expands compound HATCH loops into separate closed strokes, retaining the outer ID and owning-layer membership while exposing hole strokes.
- Focused unit/import/SVG/export/curve/fill tests pass 78/78; warnings-as-errors solution build passes.
### PAR-015 — Bound DXF curve flattening error

**Priority:** P0 manufacturing integrity  
**Status (completed 2026-07-18):** R&D found fixed 10°/15° sampling and hard segment caps on LWPOLYLINE bulges, ARC, CIRCLE, ELLIPSE, and HATCH edges. A radius-1000 semicircle could deviate 8.56 units from its source. Circular sampling now derives segment count from chord sagitta and a 0.1-unit tolerance. Ellipses use adaptive quarter/mid/three-quarter deviation checks. SPLINE retains the same tolerance contract.  
**Risk:** irreversible large-radius curve distortion

Evidence:

- Radius-1000 bulge, ARC, and CIRCLE fixtures verify every chord at or below 0.1-unit sagitta.
- Large closed ellipse fixture verifies every adaptive chord against the exact parametric midpoint.
- HATCH arc-edge coverage remains valid under tolerance-driven point counts.

### PAR-016 — Preserve imported source layers in the production workspace

**Priority:** P1  
**Status (completed 2026-07-18):** R&D found PAR-013 stopped at parser metadata: `AddImportedDrawings` still collapsed every DXF/SVG path into one filename layer. Imports now group translated paths by `SourceLayerName`, persist all generated layer IDs, and retain them through normalization, undo/redo, reload, and STCH save/reopen. Reload reuses matching import layers, creates new source layers, removes obsolete empty generated layers, and leaves unrelated/user-split layers intact. Legacy single-layer import groups remain readable.  
**Risk:** cut/score/print role loss after import or reload

Evidence:

- CUT/SCORE/CUT input creates two ordered workspace layers with exact membership.
- Reload to SCORE/PRINT preserves group identity and centers while updating layer ownership; undo/redo restores both layer sets.
- `generatedLayerIds` round-trips through STCH and defaults safely for legacy state.

### PAR-017 — Preserve rich text through DXF and PDF

**Priority:** P1  
**Status (completed 2026-07-18):** R&D found the C# DXF path dropped Python-compatible PATHSTITCH XDATA and PDF wrote all text as one unrotated Helvetica line. DXF export now registers the PATHSTITCH APPID, writes Python-compatible font/B/I/U/spacing and chunked newline-safe text XDATA, flattens only the native TEXT fallback, and restores all fields on import. PDF output now emits rotation and width matrices, absolute character spacing, true multiline baselines, underlines, and Helvetica/Times/Courier bold/italic family mapping.  
**Risk:** design-label corruption and misleading print output

Evidence:

- A styled 320-character multiline fixture crosses the XDATA chunk boundary and round-trips through internal and public DXF loaders.
- Native DXF TEXT remains single-line and valid while XDATA restores exact normalized multiline content.
- PDF fixture verifies rotated/warped matrices, two baselines, character spacing, Courier bold-oblique selection, underline strokes, and valid xref/object counts.
- Rich-text completion build passed with 0 warnings/errors; current repository-wide verification is recorded below.

### PAR-018 — Preserve text fidelity and document state across every export

**Priority:** P1  
**Status (completed 2026-07-18):** SVG emits font family, B/I/U, character spacing, width factor, rotation, multiline baselines, and escaped Unicode with CAD-to-SVG Y semantics. PNG uses CAD-up mapping and renders Unicode graphemes as glyphs instead of text bounds. PDF preserves multiline order and rich transforms while routing every non-ASCII run through deterministic embedded Noto CJK/emoji assets rather than installed fonts. Unsupported graphemes and invalid UTF-16 fail actionably before an atomic target replacement. DXF/SVG/PNG/PDF export activity no longer changes clean/dirty revision state.  
**Risk:** visual corruption and false unsaved-change prompts

Evidence:

- Rich SVG, PNG orientation/text, PDF baseline, quick export, and clean/dirty lifecycle fixtures are included in the focused export suite.
- Clean documents stay clean through every export; already-dirty documents remain dirty.
- Official OFL Noto Sans CJK SC and monochrome Noto Emoji assets are embedded as assembly resources. Grapheme-level fallback never consults host fonts; bold/italic synthesis, width, rotation, spacing, multiline baselines, underline, colors, fills, holes, and 612×792 page geometry remain intact.
- The CJK runtime font removes only compatibility-radical cmap aliases so ToUnicode maps retain canonical source characters. Structural tests inflate the generated maps and verify exact `Größe`, `中文`, `第二行`, and surrogate-pair emoji destinations.
- Latin-1 text now enters the Unicode path instead of ASCII replacement. Invalid UTF-16 leaves a pre-existing target and temporary-file set unchanged.
- Poppler rendered the QA PDF at 612×792 with the expected 36-point frame margins and complete rotated CJK/emoji glyph region; pdfplumber extracted every expected source character. Focused Unicode PDF tests pass 3/3.

### PAR-019 — Preserve opaque DXF structure during export

**Priority:** P0  
**Status (completed 2026-07-18):** Unmodified Batch items and unchanged direct/generated regular DXFs use source-preserving paths. Regular export holds an immutable session byte snapshot and a versioned semantic fingerprint of only DXF-writer inputs, eliminating mutable-cache races. Full export and project save/reopen retain unsupported entities, BLOCKS, custom tables/styles, XDATA, legacy/UTF-8 bytes, and source `$ACADVER`, while atomically canonicalizing only `$INSUNITS=4` and `$MEASUREMENT=1`. Edited full export/save now performs an atomic, handle-keyed merge for uniquely handled planar LINE, LWPOLYLINE, CIRCLE, ARC, basic TEXT, and strictly gated solid non-associative planar polyline or native line/arc/ellipse/rational-spline edge-path HATCH entities on their source layers. TEXT replacements preserve unrelated vendor XDATA, replace only the owned PATHSTITCH XDATA application block, and preserve registered custom native group-7 styles. Rich TEXT edits/new entities insert a missing APPID table or PATHSTITCH record and a missing STYLE table or STANDARD record with deterministic owned handles. HATCH replacements require zero elevation, world-Z extrusion, non-associative boundaries, and zero boundary-object references. Polyline and native edge loops infer one exact translation/rotation/reflection/uniform-scale similarity from flattened editor geometry. Raw rewrites transform vertices, line endpoints, arc centers/radii/angles, ellipse centers/major vectors/parameters, and spline control/fit/tangent data; knot and weight data remain untouched, while reflection negates bulges and toggles native curve direction. Shear and non-uniform scale reject instead of flattening curves. Candidate validation uses the source flattening-error budget scaled by similarity magnitude; successful saves promote reopened canonical HATCH geometry into both archive state and live editor state, making same-session and reopened second merges tessellation-stable. New supported entities are inserted into model space with deterministic globally unique hexadecimal handles, validated `330` ownership, and an atomically advanced `$HANDSEED`; allocation scans definitions across all non-HEADER sections, including DIMSTYLE `105`, and never reuses a deleted source handle. When a requested layer is missing, one validated LAYER symbol table receives a separately allocated/owned layer record with true color and CONTINUOUS linetype. Missing LTYPE tables or CONTINUOUS/DASHED records are inserted before LAYER; mixed requests share one table and one capacity update. Symbol-table `70` values are treated as capacities: spare capacity remains byte-stable and only insufficient capacity grows before entity insertion. Save, Save As, and reopen persist the assigned handle/layer provenance and exact merged bytes; live state promotes only after archive success and only when no newer edit revision exists. Unchanged supported entities and all unsupported/opaque records remain raw; changed records retain handles, owner/extension envelope data, visual overrides, and third-party XDATA. Default imported `Layer 1` metadata resolves back to each entity source layer, while explicit layer name/color/reassignment changes reject merge. Unreferenced supported-entity deletion removes only the matched raw record. A reverse handle-reference scan gates codes 330–369, 390–399, 480–481, and XDATA 1005; referenced deletion rejects merge. Ambiguous/missing owners, duplicate/malformed global handle definitions, missing handles on existing paths, malformed or duplicate touched symbol tables/record owners, missing custom TEXT styles, malformed/solid/complex DASHED records, construction HATCH, associative/non-similarity/tilted HATCH edits, unsupported native edits, selected export, real measurement augmentation, and requested-version changes reject merge before target write and use the serializer. Every candidate merge is reopened and checked for expected handles, layers, geometry, and touched dependencies before atomic target replacement. Pattern, mirror-copy, transform-copy, and offset creation now clear inherited source handles; in-place transforms retain them. Undo to the exact baseline still restores byte-preserving export.  
**Risk:** silent third-party CAD data loss

Evidence:

- Structure fixtures retain INSERT/MTEXT/BLOCKS/custom XDATA/linetype data and raw UTF-8 suffix bytes.
- Batch tests prove lazy-loaded unmodified items preserve structure and modified items use serialization.
- Regular lifecycle tests cover immutable-cache tamper resistance, full export, canonical project save/reopen, merged edits across reopen, geometry/text/layer mutations, new-entity and new-LAYER allocation, APPID/LTYPE/STYLE table and record insertion, coalesced CONTINUOUS/DASHED creation, construction layer/entity styling, capacity preservation/growth, candidate reopen validation, Save As provenance, stable second-save identity, unreferenced deletion merge, referenced-deletion rejection, ambiguous-owner, duplicate-global-handle, malformed symbol-owner/count atomic rejection, selected/measurement/version exclusions, and Undo restoration.
- Merge fixtures cover LINE/LWPOLYLINE/CIRCLE/ARC/basic-TEXT replacement, rich-TEXT edit/new insertion with missing dependency creation, owned PATHSTITCH XDATA replacement, vendor TEXT XDATA and registered native STYLE retention, safe straight/bulged-polyline and native line/arc/ellipse/rational-spline edge-path HATCH similarity transforms, canonical uniform-scale second merges, non-uniform curved-HATCH rejection without target mutation, canonical construction-line insertion, invalid DASHED and associative-HATCH rejection, raw untouched entities, BLOCKS/INSERT retention, UTF-8 BOM transport, and source-handle provenance for patterns/transforms/offsets.
- Completion evidence covers native line/arc/ellipse/rational-spline edge paths, reflection direction semantics, radius/vector scaling, malformed edge-count atomic rejection, canonical project-save promotion, and byte-stable second merges after uniform scaling. Selected export remains intentionally model-derived.

### PAR-020 — Complete DXF transport and OCS interoperability

**Priority:** P1  
**Status (in progress 2026-07-18):** R2007+ DXF writes and strictly reads UTF-8, including BOM-prefixed files. Transport declarations are discovered only inside one complete HEADER section; duplicate/malformed `$ACADVER` or `$DWGCODEPAGE` declarations fail explicitly, while entity payload lookalikes cannot override decoding. R2000 output stays ASCII using standard `\U+XXXX` escapes, including surrogate pairs and length-safe XDATA chunks. Legacy imports honor declared `$DWGCODEPAGE` values such as ANSI_1252; malformed or unsupported declarations fail with actionable errors. Binary DXF routes through the pinned packaged ezdxf runtime into a validated temporary ASCII intermediary; source bytes remain unchanged, temporary output is deleted on success/failure, and the strict low-level parser still rejects raw binary input. The intermediary is model-import transport, not an opaque structure-preserving rewrite. LWPOLYLINE, legacy POLYLINE, ARC, CIRCLE, and HATCH use the arbitrary-axis OCS transform; projected ARC/CIRCLE geometry becomes polylines when circular metadata would be invalid. ELLIPSE projects WCS center/major vectors with extrusion-derived minor axes. TEXT projects insertion, baseline rotation, height, and width. Generation flags 2/4, legacy negative group 41, negative extrusion, and editor reflections decompose into signed text bases; standard output writes positive group 41 plus group 71 bit 2 for backward text. Signed bounds and DXF/SVG/PDF/PNG transforms retain one-axis mirrored glyph runs. Arbitrary non-orthogonal shear remains a best-fit approximation because the editor model stores scalar rotation and width rather than a full 2x2 text matrix.  
**Risk:** locale-dependent text loss and misplaced planar geometry

Evidence:

- Unicode fixtures cover `Größe`, CJK, emoji, Unicode layer/font names, long XDATA, R2000 ASCII escapes, R2018 raw/BOM-prefixed UTF-8, CP1252 transport, HEADER scoping, and duplicate-declaration rejection.
- Golden OCS fixtures were generated with `ezdxf.path.make_path(...).flattening(0.1)` and cover tilted elevation, negative extrusion, projected ARC/CIRCLE/TEXT, transformed HATCH loops, generation flags 2/4/6, reflected editor text, and standards-compliant mirrored DXF round trips.
- Invalid codepages and unavailable binary-conversion runtimes produce actionable failures. A committed binary R2010 fixture covers units, Unicode layer/TEXT, negative-extrusion OCS, and vendor XDATA; Python conversion tests pass 2/2, .NET routing/cleanup/runtime tests pass 18/18, and combined DXF/text regression passes 121/121. Warnings-as-errors build passes with 0 warnings/errors; full .NET suite passes 1140/1140.

## Suggested parallel work lanes

| Lane | Tickets | Main overlap |
|---|---|---|
| Geometry | PAR-001, PAR-009, PAR-012, PAR-014, PAR-015, PAR-020 | Sewing holes, packaged 3D topology/routing, physical DXF units, splines, compound fills, bounded curves, and OCS projection |
| Batch/DXF | PAR-002, PAR-019 | Batch view/model, 2D transition lifecycle, and source-preserving DXF export |
| Imaging/export | PAR-003, PAR-008, PAR-013, PAR-016, PAR-018 | Reference images, PSD, SVG/PNG/PDF output fidelity, and production import-layer ownership |
| Native/release | PAR-004, PAR-006, PAR-007, PAR-010, PAR-011 | macOS bundle, preferences, live discovery, interactive previews, signing/workflow |
| UX cleanup | PAR-005 | Tool catalog/rail/preferences |

Open work remains in PAR-020 and PAR-007. Re-run native evidence after any native, packaging, update, geometry-worker, unit-contract, or recent-discovery change.

## Port improvements: preserve, do not regress

Original has known persistence defects. Match intended outcome, not bugs.

1. Original saves 3D viewport state but not source STEP/OBJ/STL needed to unfold after reopen. Port preserves/extracts source entries: `Project3DStateService.cs:116-224, 316-422`.
2. Original `Body3D.visible` is excluded from coding keys and reloads visible. Port serializes visibility: `Project3DStateService.cs:66`.
3. Original omits newer edit/link metadata from `ProjectSaveContainer`. Port persistence is broader. Keep round-trip coverage for mirror links, patterns, sewing operations, reference images, Batch state, seam configuration.

## Verification notes

- Source-based audit. Full means implementation surface and supporting tests/evidence found, not every path executed here.
- Final warnings-as-errors verification passed with 0 warnings/errors; full .NET suite passed 1140/1140. PAR-018 Unicode PDF tests passed 3/3, PAR-019 structure-preservation tests passed 46/46, Python unit/export/append contracts passed 7/7, recent-project lifecycle tests passed 9/9, focused collector/delivery contracts passed 28/28, executable native-evidence validation passed 26/26, and combined packaged OCCT mesh/import/unfold Python suites passed 23/23. Native macOS packaging cannot execute on this Windows host and remains explicitly gated by PAR-007.
- Source changed during audit. Explode became reachable. Re-check ticket evidence before starting; close/report work already completed concurrently.
- `AUDIT_TASKS.md` is not authoritative alone: several Todo labels conflict with current macOS package/Quick Look source.
- Explicit original Batch picker is drawing/project-oriented; port's DXF/SVG/PDF/STCH input set is not treated as a parity gap. Main confirmed Batch gap is preview/edit round trip.

## Primary evidence index

- Original feature state: `Pathstitch/Pathstitch/App/AppState.swift`
- Original main 2D UI: `Pathstitch/Pathstitch/ContentView.swift`
- Original canvas: `Pathstitch/Pathstitch/Modes/TwoDMode/DxfCanvasView.swift`
- Original Batch: `Pathstitch/Pathstitch/Modes/BatchMode/BatchModeView.swift`
- Original 3D: `Pathstitch/Pathstitch/Modes/ThreeDMode/`
- Original geometry: `pathstitch_core/dxf_ops.py`, `step_ops.py`, `surface_unfold.py`, `net_unfold.py`
- Port 2D models/tools: `Editor2DPreviewDocument.cs`, `EditorSidebarToolDefinition.cs`
- Port 2D canvas/UI: `DxfPreviewCanvas.cs`, `Editor2DInspector.axaml`
- Port Batch: `EditorBatchWorkspaceViewModel.cs`, `EditorBatchView.axaml`
- Port 3D: `Editor3DWorkspaceViewModel.cs`, `EditorPageViewModel.UnfoldConfiguration.cs`
- Port persistence: `src/Domain/Domain.App/Services/Project3DStateService.cs`
- Port preferences: `PreferencesDialog.axaml(.cs)`, `UserPreferencesStore.cs`
- macOS delivery: `scripts/package-avalonia-macos.ps1`, `.github/workflows/macos-release.yml`, `docs/macos-delivery.md`

